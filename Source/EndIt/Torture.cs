using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace EndIt
{
    public enum TortureMethod
    {
        Beating,
        Waterboarding,
        Branding,
        ElectricShock,
        Thumbscrews,
        Cutting,
        FootWhipping,
        IceWater,
        RussianRoulette
    }

    public enum TortureReason
    {
        Intel,
        BreakWill,
        Fun
    }

    /// <summary>One torture session at a Bad Chair: who, how, why and for how long.</summary>
    public class TortureSession : IExposable
    {
        public Pawn prisoner;
        public Pawn torturer;
        public TortureMethod method;
        public TortureReason reason;
        public int hours = 3;
        public bool stopBeforeDeath = true;

        /// <summary>Tick the prisoner was strapped in and the torture began; -1 until then.</summary>
        public int startTick = -1;

        /// <summary>Last time a blow, burn, cut or shock landed; drives the prisoner's flinch.</summary>
        public int lastStrikeTick = -99999;

        /// <summary>Waterboarding / ice water: whether this round pours (skipped while the prisoner is near the limit).</summary>
        public bool pourThisRound;

        /// <summary>Russian roulette: the chamber (0-5) the bullet is in, the next pull, and whether it has fired.</summary>
        public int rouletteBullet = -1;
        public int roulettePull;
        public bool rouletteDone;

        public bool Started => startTick >= 0;
        public int DurationTicks => hours * GenDate.TicksPerHour;
        public int TicksDone => Started ? Find.TickManager.TicksGame - startTick : 0;

        public void ExposeData()
        {
            Scribe_References.Look(ref prisoner, "prisoner");
            Scribe_References.Look(ref torturer, "torturer");
            Scribe_Values.Look(ref method, "method");
            Scribe_Values.Look(ref reason, "reason");
            Scribe_Values.Look(ref hours, "hours", 3);
            Scribe_Values.Look(ref stopBeforeDeath, "stopBeforeDeath", true);
            Scribe_Values.Look(ref startTick, "startTick", -1);
            Scribe_Values.Look(ref lastStrikeTick, "lastStrikeTick", -99999);
            Scribe_Values.Look(ref pourThisRound, "pourThisRound");
            Scribe_Values.Look(ref rouletteBullet, "rouletteBullet", -1);
            Scribe_Values.Look(ref roulettePull, "roulettePull");
            Scribe_Values.Look(ref rouletteDone, "rouletteDone");
        }
    }

    public class CompProperties_TortureChair : CompProperties
    {
        public CompProperties_TortureChair()
        {
            compClass = typeof(CompTortureChair);
        }
    }

    /// <summary>The Bad Chair: holds the current session and offers the "Torture..." / "Stop torture" buttons.</summary>
    public class CompTortureChair : ThingComp
    {
        public TortureSession session;

        /// <summary>
        /// The session, if the torturer is on it (or has it queued); a stale one (torturer gone, job lost) is cleared.
        /// </summary>
        public TortureSession ActiveSession
        {
            get
            {
                if (session == null) return null;
                Pawn t = session.torturer;
                bool onIt = t != null && !t.Dead
                            && (t.CurJobDef == EndItDefOf.EndIt_Torture && t.CurJob.targetB.Thing == parent
                                || t.jobs.jobQueue.Any(q => q.job.def == EndItDefOf.EndIt_Torture && q.job.targetB.Thing == parent));
                if (!onIt) session = null;
                return session;
            }
        }

        public static bool IsBeingTortured(Pawn prisoner)
        {
            return prisoner.CurJobDef == EndItDefOf.EndIt_BeingTortured
                   || prisoner.Map?.listerBuildings.AllBuildingsColonistOfDef(EndItDefOf.EndIt_BadChair)
                       .Any(b => b.TryGetComp<CompTortureChair>()?.ActiveSession?.prisoner == prisoner) == true;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (parent.Faction != Faction.OfPlayer) yield break;

            TortureSession active = ActiveSession;
            if (active == null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "EndIt_Torture_Gizmo".Translate(),
                    defaultDesc = "EndIt_Torture_GizmoDesc".Translate(),
                    icon = parent.def.uiIcon,
                    action = () => Find.WindowStack.Add(new Dialog_Torture(this))
                };
            }
            else
            {
                yield return new Command_Action
                {
                    defaultLabel = "EndIt_Torture_Stop".Translate(),
                    defaultDesc = "EndIt_Torture_StopDesc".Translate(active.prisoner.LabelShort),
                    icon = TexCommand.ClearPrioritizedWork,
                    action = () => active.torturer.jobs.EndCurrentJob(JobCondition.InterruptForced)
                };
            }
        }

        public bool Begin(TortureSession s)
        {
            session = s;
            Job job = JobMaker.MakeJob(EndItDefOf.EndIt_Torture, s.prisoner, parent);
            job.count = 1;
            job.playerForced = true;
            if (s.torturer.jobs.TryTakeOrderedJob(job, JobTag.Misc)) return true;
            session = null;
            return false;
        }

        public override string CompInspectStringExtra()
        {
            TortureSession s = ActiveSession;
            if (s == null) return null;
            string method = TortureMethods.Label(s.method);
            if (!s.Started) return "EndIt_Torture_InspectPending".Translate(s.torturer.LabelShort, s.prisoner.LabelShort, method);
            float hoursLeft = Mathf.Max(0f, (s.DurationTicks - s.TicksDone) / (float)GenDate.TicksPerHour);
            return "EndIt_Torture_Inspect".Translate(s.torturer.LabelShort, s.prisoner.LabelShort, method, hoursLeft.ToString("0.0"));
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref session, "session");
        }
    }

    /// <summary>What each method does, round by round, and how it looks.</summary>
    public static class TortureMethods
    {
        /// <summary>One round of torture: wind up, strike / pour at <see cref="StrikePhase"/>, recover.</summary>
        public const int RoundTicks = 250;
        public const int StrikePhase = 150;
        public const int PourStart = 60;
        public const int PourEnd = 200;

        public static readonly TortureMethod[] All =
        {
            TortureMethod.Beating, TortureMethod.Waterboarding, TortureMethod.Branding, TortureMethod.ElectricShock,
            TortureMethod.Thumbscrews, TortureMethod.Cutting, TortureMethod.FootWhipping, TortureMethod.IceWater,
            TortureMethod.RussianRoulette
        };

        /// <summary>Russian roulette: chambers in the cylinder, and when the cylinder is spun in the first round.</summary>
        public const int RouletteChambers = 6;
        public const int RouletteSpinTicks = 60;

        public static string Label(TortureMethod m) => ("EndIt_TortureMethod_" + m).Translate();
        public static string Description(TortureMethod m) => ("EndIt_TortureMethod_" + m + "_Desc").Translate();

        public static bool Available(TortureMethod m, Pawn torturer, out string reason)
        {
            reason = null;
            if (m == TortureMethod.RussianRoulette && (torturer == null || WeaponUtil.FindRevolver(torturer) == null))
                reason = "EndIt_TortureMethod_NeedsRevolver".Translate(torturer?.LabelShort ?? "torturer");
            if (m == TortureMethod.ElectricShock)
            {
                ResearchProjectDef electricity = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Electricity");
                if (electricity != null && !electricity.IsFinished)
                    reason = "EndIt_TortureMethod_NeedsResearch".Translate(electricity.LabelCap);
            }
            return reason == null;
        }

        /// <summary>How much an hour of this method helps get intel out of someone.</summary>
        public static float IntelFactor(TortureMethod m)
        {
            switch (m)
            {
                case TortureMethod.RussianRoulette: return 1.4f;
                case TortureMethod.Waterboarding: return 1.3f;
                case TortureMethod.ElectricShock: return 1.2f;
                case TortureMethod.Thumbscrews: return 1.15f;
                case TortureMethod.Branding: return 1.1f;
                case TortureMethod.IceWater: return 1f;
                case TortureMethod.Cutting: return 1f;
                case TortureMethod.FootWhipping: return 0.9f;
                default: return 0.85f;
            }
        }

        /// <summary>Close enough to death that a torturer told to keep them alive stops.</summary>
        public static bool InDanger(Pawn p)
        {
            HediffSet h = p.health.hediffSet;
            return Severity(p, HediffDefOf.BloodLoss) >= 0.45f
                   || Severity(p, EndItDefOf.EndIt_NearDrowning) >= 0.8f
                   || Severity(p, EndItDefOf.EndIt_ElectricShock) >= 0.9f
                   || Severity(p, HediffDefOf.Hypothermia) >= 0.45f
                   || p.health.summaryHealth.SummaryHealthPercent < 0.35f
                   || h.PainTotal >= 0.98f && p.Downed && h.BleedRateTotal > 0.6f;
        }

        public static float Severity(Pawn p, HediffDef def) => p.health.hediffSet.GetFirstHediffOfDef(def)?.Severity ?? 0f;

        /// <summary>Per-tick effects of the current round. Damage lands at the strike; pours run over several ticks.</summary>
        public static void Tick(TortureSession s, Pawn torturer, Pawn victim, int phase)
        {
            Map map = victim.Map;
            int now = Find.TickManager.TicksGame;
            bool careful = s.stopBeforeDeath;

            if (s.method == TortureMethod.RussianRoulette)
            {
                RouletteTick(s, torturer, victim, phase, now);
                return;
            }

            if (s.method == TortureMethod.Waterboarding || s.method == TortureMethod.IceWater)
            {
                if (phase == PourStart)
                {
                    // Pause the water while they're near the limit; without the safety the limit is death.
                    HediffDef limitDef = s.method == TortureMethod.Waterboarding ? EndItDefOf.EndIt_NearDrowning : HediffDefOf.Hypothermia;
                    float limit = s.method == TortureMethod.Waterboarding ? (careful ? 0.55f : 1.1f) : (careful ? 0.35f : 1.1f);
                    s.pourThisRound = Severity(victim, limitDef) < limit;
                }
                if (s.pourThisRound && phase >= PourStart && phase < PourEnd && (phase - PourStart) % 10 == 0)
                {
                    Vector3 head = TortureDraw.HeadPos(victim);
                    FleckMaker.WaterSplash(head, map, 0.9f, 1.5f);
                    if (s.method == TortureMethod.Waterboarding)
                        HealthUtility.AdjustSeverity(victim, EndItDefOf.EndIt_NearDrowning, 0.008f);
                    else
                        HealthUtility.AdjustSeverity(victim, HediffDefOf.Hypothermia, 0.005f);
                    if ((phase - PourStart) % 40 == 0) SoundDefOf.Interact_CleanFilth.PlayOneShot(new TargetInfo(victim.Position, map));
                }
                return;
            }

            if (phase != StrikePhase) return;
            s.lastStrikeTick = now;
            Vector3 torso = victim.DrawPos;

            switch (s.method)
            {
                case TortureMethod.Beating:
                    SoundDefOf.Pawn_Melee_Punch_HitPawn.PlayOneShot(new TargetInfo(victim.Position, map));
                    if (Rand.Chance(0.45f)) Hurt(victim, DamageDefOf.Blunt, Rand.Range(2f, 5f), careful,
                        "Torso", "Torso", "Torso", "Head", "Shoulder", "Arm", "Leg", "Jaw");
                    if (Rand.Chance(0.04f)) Hurt(victim, DamageDefOf.Blunt, 6f, careful, "Rib");
                    break;

                case TortureMethod.FootWhipping:
                    SoundDefOf.MeleeHit_Unarmed.PlayOneShot(new TargetInfo(victim.Position, map));
                    if (Rand.Chance(0.5f)) Hurt(victim, DamageDefOf.Blunt, Rand.Range(2f, 3f), careful, "Foot");
                    break;

                case TortureMethod.Branding:
                    SoundDefOf.Interact_Ignite.PlayOneShot(new TargetInfo(victim.Position, map));
                    FleckMaker.ThrowSmoke(torso, map, 0.6f);
                    FleckMaker.ThrowFireGlow(torso, map, 0.5f);
                    if (Rand.Chance(0.3f)) Hurt(victim, DamageDefOf.Burn, Rand.Range(3f, 5f), careful,
                        "Arm", "Arm", "Leg", "Torso", "Shoulder");
                    break;

                case TortureMethod.ElectricShock:
                    for (int i = 0; i < 3; i++) FleckMaker.ThrowMicroSparks(torso, map);
                    SoundDefOf.Interact_Ignite.PlayOneShot(new TargetInfo(victim.Position, map));
                    if (!careful || Severity(victim, EndItDefOf.EndIt_ElectricShock) < 0.8f)
                        HealthUtility.AdjustSeverity(victim, EndItDefOf.EndIt_ElectricShock, 0.08f);
                    if (Rand.Chance(0.2f)) Hurt(victim, DamageDefOf.Burn, 1f, careful, "Hand", "Foot", "Torso");
                    break;

                case TortureMethod.Thumbscrews:
                    SoundDefOf.MetalHitImportant.PlayOneShot(new TargetInfo(victim.Position, map));
                    if (Rand.Chance(0.3f)) Hurt(victim, DamageDefOf.Crush, Rand.Range(2f, 4f), careful, "Finger");
                    break;

                case TortureMethod.Cutting:
                    SoundDefOf.Execute_Cut.PlayOneShot(new TargetInfo(victim.Position, map));
                    if (Rand.Chance(0.35f))
                    {
                        Hurt(victim, DamageDefOf.Cut, Rand.Range(2f, 4f), careful, "Arm", "Arm", "Leg", "Torso", "Shoulder");
                        victim.health.DropBloodFilth();
                    }
                    break;
            }
        }

        /// <summary>
        /// One bullet, one spin, then alternate pulls: the prisoner first (the torturer holds it to their head), then
        /// the torturer's own temple, and so on, until the bullet comes up. Whoever it hits takes a pistol shot to the
        /// head (pistol death chance); the session ends there.
        /// </summary>
        private static void RouletteTick(TortureSession s, Pawn torturer, Pawn victim, int phase, int now)
        {
            if (s.rouletteBullet < 0) s.rouletteBullet = Rand.Range(0, RouletteChambers);
            if (s.rouletteDone || phase != StrikePhase) return;

            Map map = victim.Map;
            bool prisonersTurn = s.roulettePull % 2 == 0;
            Pawn target = prisonersTurn ? victim : torturer;
            ThingWithComps gun = torturer.equipment?.Primary;
            if (prisonersTurn) s.lastStrikeTick = now;

            if (s.roulettePull == s.rouletteBullet && gun != null)
            {
                s.rouletteDone = true;
                s.roulettePull++;
                Vector3 muzzle = TortureDraw.HeadPos(target);
                if (prisonersTurn)
                {
                    EndItWounds.ShotAtTemple(victim, gun, torturer, muzzle);
                    Messages.Message((victim.Dead ? "EndIt_TortureRoulette_PrisonerDied" : "EndIt_TortureRoulette_PrisonerShot")
                        .Translate(victim.LabelShort), victim, MessageTypeDefOf.NegativeHealthEvent);
                }
                else
                {
                    EndItWounds.SelfInflict(torturer, gun, WeaponStyle.Pistol, muzzle, "EndIt_TortureRoulette_TorturerSurvived");
                    if (torturer.Dead)
                        Messages.Message("EndIt_TortureRoulette_TorturerDied".Translate(torturer.LabelShort), victim,
                            MessageTypeDefOf.NegativeHealthEvent);
                }
                return;
            }

            s.roulettePull++;
            SoundDefOf.Click.PlayOneShot(new TargetInfo(target.Position, map));
            MoteMaker.ThrowText(TortureDraw.HeadPos(target) + new Vector3(0f, 0f, 0.4f), map, "EndIt_RouletteClick".Translate(), 2.5f);
        }

        /// <summary>
        /// One injury on a random part with one of the given defNames. With the safety on, never takes the head,
        /// neck or torso low enough to kill.
        /// </summary>
        private static void Hurt(Pawn victim, DamageDef def, float amount, bool careful, params string[] partNames)
        {
            string name = partNames.RandomElement();
            BodyPartRecord part = victim.health.hediffSet.GetNotMissingParts()
                .Where(p => p.def.defName == name).RandomElementWithFallback();
            if (part == null) return;
            if (careful && (name == "Head" || name == "Torso" || name == "Neck" || name == "Jaw")
                && victim.health.hediffSet.GetPartHealth(part) - amount < 8f)
                return;

            DamageInfo dinfo = new DamageInfo(def, amount, 0f, -1f, null, part);
            dinfo.SetIgnoreArmor(true);
            dinfo.SetAllowDamagePropagation(false);
            victim.TakeDamage(dinfo);
        }
    }

    /// <summary>The consequences once a session ends: lasting hediffs, thoughts, resistance, goodwill, intel.</summary>
    public static class TortureAftermath
    {
        private static readonly string[] IntelQuests = { "OpportunitySite_BanditCamp", "OpportunitySite_ItemStash" };

        public static void Apply(TortureSession s, Pawn torturer, Pawn victim)
        {
            float hours = s.TicksDone / (float)GenDate.TicksPerHour;
            if (hours < 0.05f) return;

            TorturerThoughts(s, torturer);
            ColonyThoughts(torturer, victim);
            if (victim.Dead)
            {
                Messages.Message("EndIt_Torture_Died".Translate(victim.LabelShort), MessageTypeDefOf.NegativeHealthEvent);
                return;
            }

            // Lasting: trauma that fades over weeks, and a bad memory.
            HealthUtility.AdjustSeverity(victim, EndItDefOf.EndIt_TortureTrauma, Mathf.Clamp(0.2f + 0.15f * hours, 0.25f, 1f));
            victim.needs?.mood?.thoughts?.memories?.TryGainMemory(EndItDefOf.EndIt_WasTortured, torturer);

            // Broken down: prisoners get easier to recruit or enslave; slaves get more suppressed.
            float factor = s.reason == TortureReason.BreakWill ? 2f : 1f;
            if (victim.IsSlaveOfColony)
            {
                Need_Suppression suppression = victim.needs?.TryGetNeed<Need_Suppression>();
                if (suppression != null) suppression.CurLevel = Mathf.Min(1f, suppression.CurLevel + 0.15f * hours * factor);
            }
            else if (victim.guest != null)
            {
                victim.guest.resistance = Mathf.Max(0f, victim.guest.resistance - 2.5f * hours * factor);
                victim.guest.will = Mathf.Max(0f, victim.guest.will - 0.12f * hours * factor);
            }

            Faction faction = victim.Faction;
            if (faction != null && faction != Faction.OfPlayer && !faction.HostileTo(Faction.OfPlayer))
                faction.TryAffectGoodwillWith(Faction.OfPlayer, -15, true, true, null, victim);

            if (s.reason == TortureReason.Intel && !torturer.Dead) TryGetIntel(s, torturer, victim, hours);
            else
                Messages.Message("EndIt_Torture_Done".Translate(torturer.LabelShort, victim.LabelShort), victim,
                    MessageTypeDefOf.NeutralEvent);
        }

        private static void TorturerThoughts(TortureSession s, Pawn torturer)
        {
            if (torturer == null || torturer.Dead || torturer.story?.traits == null) return;
            var memories = torturer.needs?.mood?.thoughts?.memories;
            bool enjoys = torturer.story.traits.HasTrait(TraitDefOf.Bloodlust);
            if (enjoys)
            {
                memories?.TryGainMemory(s.reason == TortureReason.Fun ? EndItDefOf.EndIt_TorturedForFunEnjoyed : EndItDefOf.EndIt_TorturedEnjoyed);
                if (s.reason == TortureReason.Fun) torturer.needs?.joy?.GainJoy(0.5f, JoyKindDefOf.Social);
            }
            else
            {
                memories?.TryGainMemory(EndItDefOf.EndIt_TorturedSomeone);
            }
        }

        private static void ColonyThoughts(Pawn torturer, Pawn victim)
        {
            Map map = torturer?.Map ?? victim.MapHeld;
            if (map == null) return;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p == torturer) continue;
                p.needs?.mood?.thoughts?.memories?.TryGainMemory(EndItDefOf.EndIt_PrisonerTortured);
            }
        }

        private static void TryGetIntel(TortureSession s, Pawn torturer, Pawn victim, float hours)
        {
            if (victim.health.hediffSet.HasHediff(EndItDefOf.EndIt_Interrogated))
            {
                Messages.Message("EndIt_Torture_AlreadyTalked".Translate(victim.LabelShort), victim, MessageTypeDefOf.NeutralEvent);
                return;
            }

            int social = torturer.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            float chance = Mathf.Clamp((0.12f + 0.025f * social + 0.08f * hours) * TortureMethods.IntelFactor(s.method), 0.05f, 0.9f);
            torturer.skills?.Learn(SkillDefOf.Social, 150f * hours);

            if (!Rand.Chance(chance))
            {
                Messages.Message("EndIt_Torture_NoIntel".Translate(victim.LabelShort, chance.ToStringPercent()), victim,
                    MessageTypeDefOf.NeutralEvent);
                return;
            }

            victim.health.AddHediff(EndItDefOf.EndIt_Interrogated);
            Quest quest = GenerateIntelQuest();
            if (quest == null)
            {
                Messages.Message("EndIt_Torture_IntelNothing".Translate(victim.LabelShort), victim, MessageTypeDefOf.NeutralEvent);
                return;
            }
            QuestUtility.SendLetterQuestAvailable(quest, "EndIt_Torture_IntelSource".Translate(victim.LabelShort));
        }

        /// <summary>A real opportunity on the world map: an enemy camp or an item stash, whichever can run now.</summary>
        private static Quest GenerateIntelQuest()
        {
            float points = StorytellerUtility.DefaultThreatPointsNow(Find.World);
            foreach (string name in IntelQuests.InRandomOrder())
            {
                QuestScriptDef def = DefDatabase<QuestScriptDef>.GetNamedSilentFail(name);
                if (def == null) continue;
                try
                {
                    if (!def.CanRun(points, Find.World)) continue;
                    Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(def, points);
                    if (quest != null) return quest;
                }
                catch (System.Exception e)
                {
                    Log.Warning("[End It] Couldn't generate intel quest " + name + ": " + e.Message);
                }
            }
            return null;
        }
    }

    /// <summary>
    /// The torturer's side: carry the prisoner to the Bad Chair, strap them in, step to the front, and work on them
    /// round after round until the time is up (or, with the safety on, until they're close to death).
    /// </summary>
    public class JobDriver_Torture : JobDriver
    {
        private bool placed;
        private bool finished;

        public Pawn Prisoner => (Pawn)job.targetA.Thing;
        public Thing Chair => job.targetB.Thing;
        public TortureSession Session => Chair?.TryGetComp<CompTortureChair>()?.session;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref placed, "placed");
            Scribe_Values.Look(ref finished, "finished");
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Prisoner, job, 1, -1, null, errorOnFailed)
                   && pawn.Reserve(Chair, job, 1, -1, null, errorOnFailed);
        }

        /// <summary>A lunge toward the prisoner as each blow lands.</summary>
        public override Vector3 ForcedBodyOffset
        {
            get
            {
                TortureSession s = Session;
                if (s == null || !s.Started || Prisoner == null) return Vector3.zero;
                int phase = s.TicksDone % TortureMethods.RoundTicks;
                float reach = s.method == TortureMethod.Beating ? 0.2f
                    : s.method == TortureMethod.FootWhipping || s.method == TortureMethod.Cutting ? 0.12f : 0.07f;
                if (s.method == TortureMethod.Waterboarding || s.method == TortureMethod.IceWater) reach = 0.05f;
                if (s.method == TortureMethod.RussianRoulette) reach = 0f;
                float bell = Mathf.Clamp01(1f - Mathf.Abs(phase - TortureMethods.StrikePhase) / 15f);
                Vector3 dir = Prisoner.DrawPos - pawn.Position.ToVector3Shifted();
                dir.y = 0f;
                return dir.normalized * reach * bell;
            }
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOnAggroMentalState(TargetIndex.A);
            this.FailOn(() => Session == null || Session.torturer != pawn || Session.prisoner != Prisoner);
            this.FailOn(() => !placed && !Prisoner.IsPrisonerOfColony
                                         && !(Prisoner.IsSlaveOfColony && EndItMod.Settings.tortureSlaves));
            AddFinishAction(_ => Finish());

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).FailOnDespawnedOrNull(TargetIndex.A);

            Toil stop = ToilMaker.MakeToil("EndIt_TortureStopPrisoner");
            stop.initAction = () => Prisoner.jobs?.StopAll();
            stop.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return stop;

            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch);

            Toil strapIn = ToilMaker.MakeToil("EndIt_TortureStrapIn");
            strapIn.initAction = () =>
            {
                if (!pawn.carryTracker.TryDropCarriedThing(Chair.Position, ThingPlaceMode.Direct, out _))
                {
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                placed = true;
                StartPrisonerJob();
                Prisoner.health.AddHediff(EndItDefOf.EndIt_UnderTorture);
            };
            strapIn.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return strapIn;

            yield return Toils_Goto.GotoCell(Chair.InteractionCell, PathEndMode.OnCell);

            Toil torture = ToilMaker.MakeToil("EndIt_Torture");
            torture.initAction = () =>
            {
                if (Session.method == TortureMethod.RussianRoulette
                    && !WeaponUtil.EquipFromInventory(pawn, WeaponUtil.FindRevolver(pawn)))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                Session.startTick = Find.TickManager.TicksGame;
            };
            torture.tickAction = TortureTick;
            torture.handlingFacing = true;
            torture.defaultCompleteMode = ToilCompleteMode.Never;
            torture.WithProgressBar(TargetIndex.A, () =>
            {
                TortureSession s = Session;
                if (s == null) return 0f;
                if (s.method == TortureMethod.RussianRoulette) return s.roulettePull / (float)TortureMethods.RouletteChambers;
                return Mathf.Clamp01(s.TicksDone / (float)s.DurationTicks);
            });
            yield return torture;
        }

        private void StartPrisonerJob()
        {
            Pawn p = Prisoner;
            p.pather?.StopDead();
            p.Rotation = Chair.Rotation;
            p.jobs.StartJob(JobMaker.MakeJob(EndItDefOf.EndIt_BeingTortured, Chair), JobCondition.InterruptForced);
        }

        private void TortureTick()
        {
            TortureSession s = Session;
            Pawn victim = Prisoner;
            pawn.rotationTracker.FaceTarget(victim);

            if (victim.Dead)
            {
                EndJobWith(JobCondition.Succeeded);
                return;
            }
            if (victim.CurJobDef != EndItDefOf.EndIt_BeingTortured && victim.Position == Chair.Position)
                StartPrisonerJob();

            int ticks = s.TicksDone;
            int phase = ticks % TortureMethods.RoundTicks;
            TortureMethods.Tick(s, pawn, victim, phase);
            if (ended || pawn.Dead || pawn.Downed) return;

            bool rouletteOver = s.method == TortureMethod.RussianRoulette
                                && s.rouletteDone && phase >= TortureMethods.StrikePhase + 60;
            if (victim.Dead || rouletteOver || s.method != TortureMethod.RussianRoulette && ticks >= s.DurationTicks)
            {
                EndJobWith(JobCondition.Succeeded);
                return;
            }
            if (s.stopBeforeDeath && s.method != TortureMethod.RussianRoulette && TortureMethods.InDanger(victim))
            {
                Messages.Message("EndIt_Torture_StoppedDanger".Translate(pawn.LabelShort, victim.LabelShort), victim,
                    MessageTypeDefOf.CautionInput);
                EndJobWith(JobCondition.Succeeded);
            }
        }

        private void Finish()
        {
            if (finished) return;
            finished = true;

            CompTortureChair comp = Chair?.TryGetComp<CompTortureChair>();
            TortureSession s = comp?.session;
            Pawn victim = Prisoner;

            if (s != null && s.Started && victim != null) TortureAftermath.Apply(s, pawn, victim);
            if (comp != null) comp.session = null;
            if (victim == null || victim.Dead) return;

            Hediff during = victim.health.hediffSet.GetFirstHediffOfDef(EndItDefOf.EndIt_UnderTorture);
            if (during != null) victim.health.RemoveHediff(during);
            if (victim.CurJobDef == EndItDefOf.EndIt_BeingTortured) victim.jobs.EndCurrentJob(JobCondition.InterruptForced);

            // Take them back to their cell.
            if (placed && victim.Spawned && victim.IsPrisonerOfColony && !pawn.Downed && !pawn.Dead)
            {
                Building_Bed bed = RestUtility.FindBedFor(victim, pawn, false, false, GuestStatus.Prisoner);
                if (bed != null)
                {
                    Job escort = JobMaker.MakeJob(JobDefOf.EscortPrisonerToBed, victim, bed);
                    escort.count = 1;
                    pawn.jobs.jobQueue.EnqueueFirst(escort);
                }
            }
        }

        /// <summary>Draws the tool of the trade (and the cloth over the face, for waterboarding).</summary>
        public void DrawTools(Vector3 drawLoc)
        {
            TortureSession s = Session;
            if (s == null || !s.Started || Prisoner == null || !Prisoner.Spawned) return;
            TortureDraw.Draw(s, pawn, Prisoner, drawLoc);
        }
    }

    /// <summary>The prisoner's side: strapped into the chair, flinching, thrashing and shaking with each round.</summary>
    public class JobDriver_BeingTortured : JobDriver
    {
        private Thing Chair => job.targetA.Thing;
        private TortureSession Session => Chair?.TryGetComp<CompTortureChair>()?.session;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        public override bool CanBeginNowWhileLyingDown() => true;

        public override Vector3 ForcedBodyOffset
        {
            get
            {
                TortureSession s = Session;
                if (s == null || !s.Started || s.torturer == null) return Vector3.zero;
                int now = Find.TickManager.TicksGame;
                int since = now - s.lastStrikeTick;
                int phase = s.TicksDone % TortureMethods.RoundTicks;
                float n = now + pawn.thingIDNumber % 97;
                Vector3 away = pawn.Position.ToVector3Shifted() - s.torturer.Position.ToVector3Shifted();
                away.y = 0f;
                away = away.normalized;

                switch (s.method)
                {
                    case TortureMethod.ElectricShock:
                        // Convulsions.
                        if (since >= 45) return Vector3.zero;
                        float amp = 0.05f * (1f - since / 45f);
                        return new Vector3(Mathf.Sin(n * 2.7f) * amp, 0f, Mathf.Cos(n * 3.1f) * amp);
                    case TortureMethod.Waterboarding:
                    case TortureMethod.IceWater:
                        float shiver = s.method == TortureMethod.IceWater
                            ? 0.012f * Mathf.Clamp01(TortureMethods.Severity(pawn, HediffDefOf.Hypothermia) * 4f)
                            : 0f;
                        if (s.pourThisRound && phase >= TortureMethods.PourStart && phase < TortureMethods.PourEnd)
                            shiver += 0.035f;
                        return new Vector3(Mathf.Sin(n * 1.9f) * shiver, 0f, Mathf.Cos(n * 2.3f) * shiver * 0.6f);
                    default:
                        // Recoil from the blow, then trembling.
                        if (since >= 40) return Vector3.zero;
                        float recoil = 0.09f * Mathf.Exp(-since / 8f);
                        float tremble = 0.012f * (1f - since / 40f);
                        return away * recoil + new Vector3(Mathf.Sin(n * 2.1f) * tremble, 0f, 0f);
                }
            }
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Chair == null || !Chair.Spawned || pawn.Position != Chair.Position
                              || Session == null || Session.prisoner != pawn);

            Toil sit = ToilMaker.MakeToil("EndIt_BeingTortured");
            sit.initAction = () =>
            {
                pawn.pather?.StopDead();
                pawn.Rotation = Chair.Rotation;
            };
            sit.tickAction = () => pawn.Rotation = Chair.Rotation;
            sit.handlingFacing = true;
            sit.defaultCompleteMode = ToilCompleteMode.Never;
            yield return sit;
        }
    }

    /// <summary>Draws the torture tools between torturer and prisoner.</summary>
    public static class TortureDraw
    {
        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        private static Material Mat(string name)
        {
            if (!Mats.TryGetValue(name, out Material m))
            {
                m = MaterialPool.MatFrom("Things/EndIt/Torture/" + name, ShaderDatabase.Cutout);
                Mats[name] = m;
            }
            return m;
        }

        public static Vector3 HeadPos(Pawn p)
        {
            Vector3 pos = p.DrawPos;
            PawnRenderer r = p.Drawer?.renderer;
            if (r != null) pos += r.BaseHeadOffsetAt(p.Rotation);
            return pos;
        }

        private static float Bell(int phase, int center, int halfWidth) =>
            Mathf.Clamp01(1f - Mathf.Abs(phase - center) / (float)halfWidth);

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>The tool's path: held at the hand, brought in to the contact point for the strike, then back.</summary>
        private static float Approach(int phase)
        {
            if (phase < TortureMethods.StrikePhase - 20) return 0f;
            if (phase < TortureMethods.StrikePhase) return Smooth((phase - (TortureMethods.StrikePhase - 20)) / 20f);
            if (phase < TortureMethods.StrikePhase + 25) return 1f;
            return 1f - Smooth((phase - TortureMethods.StrikePhase - 25) / 30f);
        }

        public static void Draw(TortureSession s, Pawn torturer, Pawn victim, Vector3 drawLoc)
        {
            int phase = s.TicksDone % TortureMethods.RoundTicks;
            Vector3 vic = victim.DrawPos;
            Vector3 dir = vic - drawLoc;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
            float aim = dir.AngleFlat();
            Rot4 rot = victim.Rotation;
            Vector3 fwd = rot.FacingCell.ToVector3();
            Vector3 right = rot.RighthandCell.ToVector3();

            Vector3 head = HeadPos(victim);
            Vector3 hand = vic + right * 0.3f;
            Vector3 feet = vic + fwd * 0.42f;
            Vector3 grip = drawLoc + dir * 0.3f + new Vector3(0f, 0f, -0.05f);
            float y = Mathf.Max(drawLoc.y, vic.y) + 0.045f;

            switch (s.method)
            {
                case TortureMethod.RussianRoulette:
                    DrawRoulette(s, torturer, victim, drawLoc, grip, dir, aim, phase, y);
                    break;
                case TortureMethod.Waterboarding:
                    DrawTool("Cloth", head, 0f, 0.42f, y);
                    DrawBucket(s, phase, grip, head, aim, y);
                    break;
                case TortureMethod.IceWater:
                    DrawBucket(s, phase, grip, head + new Vector3(0f, 0f, 0.1f), aim, y);
                    break;
                case TortureMethod.Branding:
                    DrawTool("Brand", Vector3.Lerp(grip, Vector3.Lerp(vic, hand, 0.5f) - dir * 0.18f, Approach(phase)), aim, 0.62f, y);
                    break;
                case TortureMethod.ElectricShock:
                    DrawTool("Prod", Vector3.Lerp(grip, vic - dir * 0.2f, Approach(phase)), aim, 0.62f, y);
                    break;
                case TortureMethod.Thumbscrews:
                    float twist = 25f * Bell(phase, TortureMethods.StrikePhase, 12);
                    DrawTool("Thumbscrew", hand, rot.AsAngle + twist, 0.32f, y);
                    break;
                case TortureMethod.Cutting:
                    float a = Approach(phase);
                    float drag = Smooth((phase - TortureMethods.StrikePhase + 8) / 16f);
                    Vector3 cut = Vector3.Lerp(hand - right * 0.12f, hand + right * 0.1f, drag);
                    DrawTool("Knife", Vector3.Lerp(grip, cut - dir * 0.12f, a), aim + 20f, 0.45f, y);
                    break;
                case TortureMethod.FootWhipping:
                    // Raised high, then whipped down across the soles.
                    Vector3 toFeet = feet - drawLoc;
                    toFeet.y = 0f;
                    float feetAngle = toFeet.AngleFlat();
                    float swing = phase < TortureMethods.StrikePhase - 8 ? 0f
                        : phase < TortureMethods.StrikePhase ? Smooth((phase - (TortureMethods.StrikePhase - 8)) / 8f)
                        : 1f - Smooth((phase - TortureMethods.StrikePhase - 10) / 40f);
                    float caneAngle = Mathf.LerpAngle(feetAngle - 75f, feetAngle, swing);
                    float len = 0.75f;
                    Vector3 caneDir = Quaternion.AngleAxis(caneAngle, Vector3.up) * Vector3.forward;
                    DrawTool("Cane", grip + caneDir * len * 0.5f, caneAngle, len, y);
                    break;
            }
        }

        /// <summary>
        /// The torturer's revolver: spun at the chest in the first round, then raised to the prisoner's temple (their
        /// turns) or the torturer's own (the torturer's turns), held trembling, the trigger pulled; recoil if it fired,
        /// a flinch if it clicked, then lowered for the next turn.
        /// </summary>
        private static void DrawRoulette(TortureSession s, Pawn torturer, Pawn victim, Vector3 drawLoc, Vector3 grip,
            Vector3 dir, float aim, int phase, float y)
        {
            ThingWithComps gun = torturer.equipment?.Primary;
            if (gun == null) return;
            int round = s.TicksDone / TortureMethods.RoundTicks;
            bool prisonersTurn = round % 2 == 0;
            bool firedThisRound = s.rouletteDone && round == s.rouletteBullet;
            if (s.rouletteDone && round > s.rouletteBullet) return;

            Vector3 aimPos;
            float aimAngle;
            if (prisonersTurn)
            {
                Vector3 head = HeadPos(victim);
                aimPos = head - dir * 0.32f;
                aimAngle = aim;
            }
            else
            {
                Vector3 head = HeadPos(torturer);
                Vector3 right = torturer.Rotation.RighthandCell.ToVector3();
                aimPos = head + right * 0.3f;
                Vector3 toHead = head - aimPos;
                aimAngle = toHead.AngleFlat();
            }

            int raiseStart = round == 0 ? TortureMethods.RouletteSpinTicks : 0;
            const int raiseEnd = 110;
            float n = Find.TickManager.TicksGame + torturer.thingIDNumber % 97;
            Vector3 pos;
            float angle;
            if (phase < raiseStart)
            {
                // Spinning the cylinder, held at the chest.
                pos = grip + new Vector3(0f, 0f, 0.08f);
                angle = aim + 90f + (phase > 15 && phase < 45 ? Mathf.Sin(phase * 1.7f) * 8f : 0f);
            }
            else if (phase < raiseEnd)
            {
                float t = Smooth((phase - raiseStart) / (float)(raiseEnd - raiseStart));
                pos = Vector3.Lerp(grip, aimPos, t);
                angle = Mathf.LerpAngle(aim + 90f, aimAngle, t);
            }
            else if (phase < TortureMethods.StrikePhase)
            {
                float amp = 0.008f + 0.012f * (phase - raiseEnd) / (TortureMethods.StrikePhase - raiseEnd);
                pos = aimPos + new Vector3(Mathf.Sin(n * 0.91f) * amp, 0f, Mathf.Cos(n * 1.37f) * amp);
                angle = aimAngle + Mathf.Sin(n * 1.13f) * 2f;
            }
            else
            {
                int after = phase - TortureMethods.StrikePhase;
                Vector3 kicked = aimPos - (aimPos - grip).normalized * 0.15f + new Vector3(0f, 0f, 0.08f);
                if (firedThisRound && after < 6)
                {
                    pos = Vector3.Lerp(aimPos, kicked, Smooth(after / 6f));
                    angle = aimAngle + 30f * Smooth(after / 6f);
                }
                else if (after < 25)
                {
                    pos = firedThisRound ? kicked : aimPos;
                    angle = aimAngle + (firedThisRound ? 30f : 8f * Bell(after, 3, 4));
                }
                else
                {
                    float t = Smooth((after - 25) / 50f);
                    pos = Vector3.Lerp(firedThisRound ? kicked : aimPos, grip, t);
                    angle = Mathf.LerpAngle(aimAngle, aim + 90f, t);
                }
            }

            pos.y = y;
            PawnRenderUtility.DrawEquipmentAiming(gun, pos, GenMath.PositiveMod(angle, 360f));
        }

        private static void DrawBucket(TortureSession s, int phase, Vector3 grip, Vector3 head, float aim, float y)
        {
            bool pouring = s.pourThisRound && phase >= TortureMethods.PourStart - 15 && phase < TortureMethods.PourEnd + 15;
            float tilt = pouring
                ? Smooth(Mathf.Min((phase - (TortureMethods.PourStart - 15)) / 15f, (TortureMethods.PourEnd + 15 - phase) / 15f))
                : 0f;
            Vector3 pos = Vector3.Lerp(grip, head + new Vector3(0f, 0f, 0.28f), tilt);
            DrawTool("Bucket", pos, aim + 110f * tilt, 0.5f, y);
        }

        private static void DrawTool(string name, Vector3 pos, float angle, float size, float y)
        {
            pos.y = y;
            Matrix4x4 m = Matrix4x4.TRS(pos, Quaternion.AngleAxis(angle, Vector3.up), new Vector3(size, 1f, size));
            Graphics.DrawMesh(MeshPool.plane10, m, Mat(name), 0);
        }
    }
}
