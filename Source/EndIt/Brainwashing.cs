using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace EndIt
{
    /// <summary>One brainwashing session: who, and what to change.</summary>
    public class BrainwashSession : IExposable
    {
        public Pawn subject;
        public Pawn operatorPawn;
        public Ideo targetIdeo;
        public bool convertIdeo;
        public bool removeLoyalty;

        /// <summary>Powered ticks run so far; -1 until the subject is strapped in.</summary>
        public int progressTicks = -1;

        public bool Started => progressTicks >= 0;

        public void ExposeData()
        {
            Scribe_References.Look(ref subject, "subject");
            Scribe_References.Look(ref operatorPawn, "operatorPawn");
            Scribe_References.Look(ref targetIdeo, "targetIdeo");
            Scribe_Values.Look(ref convertIdeo, "convertIdeo");
            Scribe_Values.Look(ref removeLoyalty, "removeLoyalty");
            Scribe_Values.Look(ref progressTicks, "progressTicks", -1);
        }
    }

    public class CompProperties_Brainwasher : CompProperties
    {
        public CompProperties_Brainwasher()
        {
            compClass = typeof(CompBrainwasher);
        }
    }

    /// <summary>
    /// The brainwashing station: a strap chair facing a television. Holds the session, offers the buttons, and spins
    /// the hypnotic spiral on the screen while it runs.
    /// </summary>
    public class CompBrainwasher : ThingComp
    {
        public const int SessionTicks = 4 * GenDate.TicksPerHour;

        public BrainwashSession session;

        private static Material spiralMat;
        private static Material SpiralMat => spiralMat ??= MaterialPool.MatFrom("Things/EndIt/Brainwash/Spiral", ShaderDatabase.Transparent);

        public bool Powered => parent.TryGetComp<CompPowerTrader>()?.PowerOn ?? true;

        /// <summary>The cell the subject sits in: the one at the back, away from the television.</summary>
        public IntVec3 ChairCell
        {
            get
            {
                IntVec3 facing = parent.Rotation.FacingCell;
                return parent.OccupiedRect().Cells.MinBy(c => c.x * facing.x + c.z * facing.z);
            }
        }

        /// <summary>Clears a session nobody is working on any more.</summary>
        public BrainwashSession ActiveSession
        {
            get
            {
                if (session == null) return null;
                Pawn s = session.subject;
                bool alive = s != null && !s.Dead;
                bool running = session.Started && alive && s.CurJobDef == EndItDefOf.EndIt_BeingBrainwashed;
                Pawn op = session.operatorPawn;
                bool pending = !session.Started && alive && op != null && !op.Dead
                               && (op.CurJobDef == EndItDefOf.EndIt_StrapInBrainwash && op.CurJob.targetB.Thing == parent
                                   || op.jobs.jobQueue.Any(q => q.job.def == EndItDefOf.EndIt_StrapInBrainwash && q.job.targetB.Thing == parent));
                if (!running && !pending) session = null;
                return session;
            }
        }

        public static bool IsInSession(Pawn p)
        {
            return p.Map?.listerBuildings.AllBuildingsColonistOfDef(EndItDefOf.EndIt_Brainwasher)
                .Any(b => b.TryGetComp<CompBrainwasher>()?.ActiveSession?.subject == p) == true;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            if (parent.Faction != Faction.OfPlayer) yield break;

            BrainwashSession active = ActiveSession;
            if (active == null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "EndIt_Brainwash_Gizmo".Translate(),
                    defaultDesc = "EndIt_Brainwash_GizmoDesc".Translate(),
                    icon = parent.def.uiIcon,
                    action = () => Find.WindowStack.Add(new Dialog_Brainwash(this))
                };
            }
            else
            {
                yield return new Command_Action
                {
                    defaultLabel = "EndIt_Brainwash_Stop".Translate(),
                    defaultDesc = "EndIt_Brainwash_StopDesc".Translate(active.subject.LabelShort),
                    icon = TexCommand.ClearPrioritizedWork,
                    action = () => Cancel()
                };
            }
        }

        public bool Begin(BrainwashSession s)
        {
            session = s;
            Job job = JobMaker.MakeJob(EndItDefOf.EndIt_StrapInBrainwash, s.subject, parent);
            job.count = 1;
            job.playerForced = true;
            if (s.operatorPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc)) return true;
            session = null;
            return false;
        }

        public void Cancel()
        {
            BrainwashSession s = session;
            session = null;
            if (s?.subject != null && s.subject.CurJobDef == EndItDefOf.EndIt_BeingBrainwashed)
                s.subject.jobs.EndCurrentJob(JobCondition.InterruptForced);
            if (s?.operatorPawn != null && s.operatorPawn.CurJobDef == EndItDefOf.EndIt_StrapInBrainwash)
                s.operatorPawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }

        public override string CompInspectStringExtra()
        {
            BrainwashSession s = ActiveSession;
            if (s == null) return null;
            if (!s.Started) return "EndIt_Brainwash_InspectPending".Translate(s.operatorPawn.LabelShort, s.subject.LabelShort);
            float hoursLeft = (SessionTicks - s.progressTicks) / (float)GenDate.TicksPerHour;
            string line = "EndIt_Brainwash_Inspect".Translate(s.subject.LabelShort, hoursLeft.ToString("0.0"));
            if (!Powered) line += " " + "EndIt_Brainwash_NoPower".Translate();
            return line;
        }

        /// <summary>The spiral, spinning on the screen. Only visible when the screen faces the camera (north).</summary>
        public override void PostDraw()
        {
            base.PostDraw();
            BrainwashSession s = session;
            if (s == null || !s.Started || !Powered || parent.Rotation != Rot4.North) return;
            Vector3 pos = parent.DrawPos + new Vector3(-0.086f, 0f, 0.578f);
            pos.y = AltitudeLayer.BuildingOnTop.AltitudeFor();
            float angle = Find.TickManager.TicksGame * 6f % 360f;
            Matrix4x4 m = Matrix4x4.TRS(pos, Quaternion.AngleAxis(angle, Vector3.up), new Vector3(0.48f, 1f, 0.48f));
            Graphics.DrawMesh(MeshPool.plane10, m, SpiralMat, 0);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref session, "session");
        }

        /// <summary>The session ran its full course: apply the changes, then catatonia.</summary>
        public void Complete()
        {
            BrainwashSession s = session;
            session = null;
            Pawn p = s?.subject;
            if (p == null || p.Dead) return;

            Hediff during = p.health.hediffSet.GetFirstHediffOfDef(EndItDefOf.EndIt_UnderBrainwashing);
            if (during != null) p.health.RemoveHediff(during);

            List<string> done = new List<string>();
            if (s.convertIdeo && s.targetIdeo != null && ModsConfig.IdeologyActive && p.ideo != null)
            {
                if (p.ideo.Ideo != s.targetIdeo) p.ideo.SetIdeo(s.targetIdeo);
                p.ideo.OffsetCertainty(0.8f - p.ideo.Certainty);
                done.Add("EndIt_Brainwash_DoneIdeo".Translate(s.targetIdeo.name));
            }
            if (s.removeLoyalty && p.guest != null && !p.guest.Recruitable)
            {
                p.guest.Recruitable = true;
                done.Add("EndIt_Brainwash_DoneLoyalty".Translate());
            }

            p.needs?.mood?.thoughts?.memories?.TryGainMemory(EndItDefOf.EndIt_WasBrainwashed);
            p.health.AddHediff(EndItDefOf.EndIt_BrainwashCatatonia);

            Find.LetterStack.ReceiveLetter("EndIt_Brainwash_DoneLabel".Translate(p.LabelShort),
                "EndIt_Brainwash_DoneText".Translate(p.LabelShort, string.Join("\n", done.Select(d => "- " + d))),
                LetterDefOf.NeutralEvent, p);
        }
    }

    /// <summary>The operator carries the subject to the station, straps them in and switches it on.</summary>
    public class JobDriver_StrapInBrainwash : JobDriver
    {
        private Pawn Subject => (Pawn)job.targetA.Thing;
        private Thing Station => job.targetB.Thing;
        private CompBrainwasher Comp => Station?.TryGetComp<CompBrainwasher>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Subject, job, 1, -1, null, errorOnFailed)
                   && pawn.Reserve(Station, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOnAggroMentalState(TargetIndex.A);
            this.FailOn(() => Comp?.session == null || Comp.session.subject != Subject);
            this.FailOn(() => !Subject.IsPrisonerOfColony
                              && !(Subject.IsSlaveOfColony && EndItMod.Settings.brainwashSlaves));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).FailOnDespawnedOrNull(TargetIndex.A);

            Toil stop = ToilMaker.MakeToil("EndIt_BrainwashStopSubject");
            stop.initAction = () => Subject.jobs?.StopAll();
            stop.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return stop;

            yield return Toils_Haul.StartCarryThing(TargetIndex.A);

            Toil walk = ToilMaker.MakeToil("EndIt_BrainwashWalk");
            walk.initAction = () => pawn.pather.StartPath(Comp.ChairCell, PathEndMode.ClosestTouch);
            walk.defaultCompleteMode = ToilCompleteMode.PatherArrival;
            yield return walk;

            Toil strapIn = ToilMaker.MakeToil("EndIt_BrainwashStrapIn");
            strapIn.initAction = () =>
            {
                CompBrainwasher comp = Comp;
                if (!pawn.carryTracker.TryDropCarriedThing(comp.ChairCell, ThingPlaceMode.Direct, out _))
                {
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                Pawn subject = Subject;
                subject.pather?.StopDead();
                subject.Rotation = Station.Rotation;
                subject.health.AddHediff(EndItDefOf.EndIt_UnderBrainwashing);
                comp.session.progressTicks = 0;
                subject.jobs.StartJob(JobMaker.MakeJob(EndItDefOf.EndIt_BeingBrainwashed, Station), JobCondition.InterruptForced);
            };
            strapIn.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return strapIn;

            Toil switchOn = Toils_General.Wait(120, TargetIndex.B);
            switchOn.WithProgressBarToilDelay(TargetIndex.B);
            yield return switchOn;
        }
    }

    /// <summary>
    /// The subject's side: strapped in, staring at the spiral. Runs the session's clock while the station has power;
    /// a cancelled or interrupted session leaves no changes.
    /// </summary>
    public class JobDriver_BeingBrainwashed : JobDriver
    {
        private Thing Station => job.targetA.Thing;
        private CompBrainwasher Comp => Station?.TryGetComp<CompBrainwasher>();

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        public override bool CanBeginNowWhileLyingDown() => true;

        /// <summary>A slow, dazed sway, with the odd twitch.</summary>
        public override Vector3 ForcedBodyOffset
        {
            get
            {
                float t = Find.TickManager.TicksGame + pawn.thingIDNumber % 97;
                float twitch = Mathf.Sin(t * 0.013f) > 0.97f ? Mathf.Sin(t * 2.3f) * 0.02f : 0f;
                return new Vector3(Mathf.Sin(t * 0.025f) * 0.015f + twitch, 0f, 0f);
            }
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Station == null || !Station.Spawned || Comp?.session == null
                              || Comp.session.subject != pawn || pawn.Position != Comp.ChairCell);
            AddFinishAction(_ =>
            {
                Hediff during = pawn.health.hediffSet.GetFirstHediffOfDef(EndItDefOf.EndIt_UnderBrainwashing);
                if (during != null) pawn.health.RemoveHediff(during);
            });

            Toil sit = ToilMaker.MakeToil("EndIt_BeingBrainwashed");
            sit.initAction = () =>
            {
                pawn.pather?.StopDead();
                pawn.Rotation = Station.Rotation;
            };
            sit.tickAction = () =>
            {
                pawn.Rotation = Station.Rotation;
                CompBrainwasher comp = Comp;
                if (!comp.Powered) return;
                comp.session.progressTicks++;
                if (comp.session.progressTicks >= CompBrainwasher.SessionTicks)
                {
                    comp.Complete();
                    EndJobWith(JobCondition.Succeeded);
                }
            };
            sit.handlingFacing = true;
            sit.defaultCompleteMode = ToilCompleteMode.Never;
            sit.WithProgressBar(TargetIndex.A, () =>
            {
                BrainwashSession s = Comp?.session;
                return s == null ? 1f : s.progressTicks / (float)CompBrainwasher.SessionTicks;
            });
            yield return sit;
        }
    }

    /// <summary>
    /// Catatonia after a brainwashing: five hours unresponsive. When it lifts there's a chance (mod setting, 10% by
    /// default) the pawn breaks into the suicidal state.
    /// </summary>
    public class Hediff_BrainwashCatatonia : HediffWithComps
    {
        public override void PostRemoved()
        {
            base.PostRemoved();
            if (pawn == null || pawn.Dead) return;
            SuicideUtility.TryTrigger(pawn, EndItMod.Settings.suicideChanceBrainwash, "EndIt_Suicidal_ReasonBrainwash".Translate());
        }
    }
}
