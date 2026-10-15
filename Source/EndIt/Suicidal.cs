using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace EndIt
{
    /// <summary>Starting the suicidal break, and the triggers for it.</summary>
    public static class SuicideUtility
    {
        /// <summary>Hours of brooding before the first attempt.</summary>
        public const int BroodTicks = 5 * GenDate.TicksPerHour;

        public static bool TryTrigger(Pawn pawn, float chance, string reason)
        {
            if (pawn == null || pawn.Dead || !pawn.RaceProps.Humanlike || pawn.InMentalState) return false;
            if (pawn.MentalStateDef == EndItDefOf.EndIt_SuicidalState) return false;
            if (!Rand.Chance(chance)) return false;
            return pawn.mindState?.mentalStateHandler?.TryStartMentalState(EndItDefOf.EndIt_SuicidalState, reason, forced: true) == true;
        }

        public static bool IsAttemptJob(JobDef def)
        {
            return def == EndItDefOf.EndIt_EndIt || def == EndItDefOf.EndIt_Hanged || def == EndItDefOf.EndIt_HangSelf;
        }

        /// <summary>A free gallows the pawn can walk to.</summary>
        public static Thing FindGallows(Pawn pawn)
        {
            if (!pawn.Spawned || pawn.Downed) return null;
            return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(EndItDefOf.EndIt_Gallows),
                PathEndMode.OnCell, TraverseParms.For(pawn), 9999f,
                g => pawn.CanReserve(g) && !g.Position.GetThingList(g.Map).Any(t => t is Pawn && t != pawn));
        }
    }

    /// <summary>
    /// Suicidal: the pawn broods for <see cref="SuicideUtility.BroodTicks"/>, then tries to end it with the
    /// mod's End it (a gun or blade on them or within reach), or failing that by hanging themself from a free
    /// gallows. With neither, they keep looking every hour until the break runs out. Surviving an attempt ends it.
    /// </summary>
    public class MentalState_Suicidal : MentalState
    {
        private bool attempted;
        private int nextTryTick = -1;

        public override void MentalStateTick(int delta)
        {
            base.MentalStateTick(delta);
            if (pawn.Dead || pawn.MentalState != this || !pawn.Spawned) return;

            if (SuicideUtility.IsAttemptJob(pawn.CurJobDef)) return;
            if (attempted)
            {
                // They tried and lived.
                RecoverFromState();
                return;
            }

            if (Age < SuicideUtility.BroodTicks)
            {
                Brood();
                return;
            }

            int now = Find.TickManager.TicksGame;
            if (now < nextTryTick) return;
            if (TryAttempt())
            {
                attempted = true;
                Messages.Message("EndIt_Suicidal_Attempt".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.ThreatBig);
                return;
            }
            nextTryTick = now + GenDate.TicksPerHour;
            Brood();
        }

        /// <summary>Wander about aimlessly; they still eat and sleep, but do nothing else.</summary>
        private void Brood()
        {
            if (pawn.Downed) return;
            JobDef cur = pawn.CurJobDef;
            if (cur == JobDefOf.Wait_Wander || cur == JobDefOf.GotoWander || cur == JobDefOf.Ingest || cur == JobDefOf.LayDown)
                return;
            Job job = JobMaker.MakeJob(JobDefOf.Wait_Wander);
            job.expiryInterval = Rand.Range(600, 1500);
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        private bool TryAttempt()
        {
            if (pawn.Awake() && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                && WeaponUtil.TryFindWeapon(pawn, out ThingWithComps weapon, out _))
            {
                Job job = JobMaker.MakeJob(EndItDefOf.EndIt_EndIt, weapon);
                job.count = 1;
                pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                return pawn.CurJobDef == EndItDefOf.EndIt_EndIt;
            }

            Thing gallows = SuicideUtility.FindGallows(pawn);
            if (gallows != null)
            {
                pawn.jobs.StartJob(JobMaker.MakeJob(EndItDefOf.EndIt_HangSelf, gallows), JobCondition.InterruptForced);
                return pawn.CurJobDef == EndItDefOf.EndIt_HangSelf;
            }
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref attempted, "attempted");
            Scribe_Values.Look(ref nextTryTick, "nextTryTick", -1);
        }
    }

    /// <summary>Walk to a gallows, step onto the platform, put the noose on, and drop.</summary>
    public class JobDriver_HangSelf : JobDriver
    {
        private Thing Gallows => job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Gallows, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoCell(Gallows.Position, PathEndMode.OnCell);

            Toil noose = Toils_General.Wait(HangingUtility.NooseTicks, TargetIndex.None);
            noose.initAction = () => pawn.Rotation = Rot4.South;
            noose.WithProgressBarToilDelay(TargetIndex.A);
            yield return noose;

            Toil drop = ToilMaker.MakeToil("EndIt_HangSelfDrop");
            drop.initAction = () =>
            {
                Thing gallows = Gallows;
                pawn.health.AddHediff(EndItDefOf.EndIt_Hanging);
                pawn.jobs.StartJob(JobMaker.MakeJob(EndItDefOf.EndIt_Hanged, gallows), JobCondition.InterruptForced);
            };
            drop.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return drop;
        }
    }

    /// <summary>
    /// Rim Job World (optional): a forced encounter gives the victim a chance of the suicidal break.
    /// Detected by RJW's victim memories: GotRaped, GotAnalRaped, GotAnalRapedByFemale and GotRapedUnconscious
    /// (any negative "Got...Raped..." memory, so later variants are covered too). The Masochist... versions,
    /// the social opinions (HateMyRapist, AllowedMeToGetRaped) and FeelingBroken are left out.
    /// </summary>
    [HarmonyPatch(typeof(MemoryThoughtHandler), nameof(MemoryThoughtHandler.TryGainMemory),
        new[] { typeof(Thought_Memory), typeof(Pawn) })]
    public static class Patch_MemoryThoughtHandler_TryGainMemory_Rjw
    {
        private static bool? rjwActive;
        // One roll per encounter, even though RJW can hand out several memories for it.
        private static readonly Dictionary<int, int> lastRollTick = new Dictionary<int, int>();

        // RJW is loaded if its basic victim memory exists; no dependency on its package id.
        private static bool RjwActive => rjwActive ??= DefDatabase<ThoughtDef>.GetNamedSilentFail("GotRaped") != null;

        public static bool IsVictimMemory(ThoughtDef def)
        {
            if (def == null) return false;
            string name = def.defName;
            if (!name.StartsWith("Got") || name.IndexOf("Raped", System.StringComparison.Ordinal) < 0) return false;
            return def.stages != null && def.stages.Count > 0 && def.stages[0] != null && def.stages[0].baseMoodEffect < 0f;
        }

        public static void Postfix(MemoryThoughtHandler __instance, Thought_Memory newThought)
        {
            if (!RjwActive || newThought == null || !IsVictimMemory(newThought.def)) return;
            Pawn pawn = __instance.pawn;
            if (pawn == null || pawn.Dead) return;

            int now = Find.TickManager.TicksGame;
            if (lastRollTick.TryGetValue(pawn.thingIDNumber, out int last) && now - last < GenDate.TicksPerHour) return;
            lastRollTick[pawn.thingIDNumber] = now;

            SuicideUtility.TryTrigger(pawn, EndItMod.Settings.suicideChanceRjw, "EndIt_Suicidal_ReasonRjw".Translate());
        }
    }
}
