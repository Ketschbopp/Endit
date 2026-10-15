using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace EndIt
{
    /// <summary>Wardens hang prisoners whose interaction mode is set to "hang", at the nearest free gallows.</summary>
    public class WorkGiver_Warden_Hang : WorkGiver_Warden
    {
        public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!ShouldTakeCareOfPrisoner(pawn, t, forced)) return null;
            Pawn prisoner = (Pawn)t;
            if (prisoner.guest?.ExclusiveInteractionMode != EndItDefOf.EndIt_Hang) return null;
            if (!IsExecutionIdeoAllowed(pawn, prisoner)) return null;
            if (!pawn.CanReserve(prisoner, 1, -1, null, forced)) return null;

            Thing gallows = HangingUtility.FindGallows(pawn, prisoner, forced);
            if (gallows == null)
            {
                JobFailReason.Is("EndIt_NoGallows".Translate());
                return null;
            }

            Job job = JobMaker.MakeJob(EndItDefOf.EndIt_HangPrisoner, prisoner, gallows);
            job.count = 1;
            return job;
        }
    }

    public static class HangingUtility
    {
        /// <summary>Seconds of struggling, at normal speed, before the hanged pawn dies.</summary>
        public const int StrangleTicks = 480;

        /// <summary>How long the warden takes to put the noose on.</summary>
        public const int NooseTicks = 150;

        public static Thing FindGallows(Pawn warden, Pawn prisoner, bool forced)
        {
            return GenClosest.ClosestThingReachable(prisoner.Position, prisoner.Map,
                ThingRequest.ForDef(EndItDefOf.EndIt_Gallows), PathEndMode.ClosestTouch, TraverseParms.For(warden),
                9999f, g => !g.IsForbidden(warden) && warden.CanReserve(g, 1, -1, null, forced) && IsFree(g));
        }

        /// <summary>Nobody already standing (or hanging) on the gallows.</summary>
        private static bool IsFree(Thing gallows)
        {
            foreach (Thing t in gallows.Position.GetThingList(gallows.Map))
            {
                if (t is Pawn) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// The warden's side: carry the prisoner to the gallows, stand them on the trapdoor, put the noose on, drop them,
    /// and wait until it's over.
    /// </summary>
    public class JobDriver_HangPrisoner : JobDriver
    {
        private bool placed;
        private int watchTicks;

        private Pawn Prisoner => (Pawn)job.targetA.Thing;
        private Thing Gallows => job.targetB.Thing;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref placed, "placed");
            Scribe_Values.Look(ref watchTicks, "watchTicks");
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Prisoner, job, 1, -1, null, errorOnFailed)
                   && pawn.Reserve(Gallows, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOnAggroMentalState(TargetIndex.A);
            // Until the prisoner is on the gallows, changing their interaction mode calls it off.
            this.FailOn(() => !placed && (!Prisoner.IsPrisonerOfColony
                                          || Prisoner.guest?.ExclusiveInteractionMode != EndItDefOf.EndIt_Hang));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).FailOnDespawnedOrNull(TargetIndex.A);

            Toil stop = ToilMaker.MakeToil("EndIt_HangStopPrisoner");
            stop.initAction = () => Prisoner.jobs?.StopAll();
            stop.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return stop;

            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch);

            Toil place = ToilMaker.MakeToil("EndIt_HangPlace");
            place.initAction = () =>
            {
                Pawn prisoner = Prisoner;
                if (!pawn.carryTracker.TryDropCarriedThing(Gallows.Position, ThingPlaceMode.Direct, out _))
                {
                    pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                placed = true;
                prisoner.pather?.StopDead();
                prisoner.Rotation = Rot4.South;
                prisoner.jobs.StartJob(JobMaker.MakeJob(EndItDefOf.EndIt_Hanged, Gallows), JobCondition.InterruptForced);
            };
            place.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return place;

            Toil noose = Toils_General.Wait(HangingUtility.NooseTicks, TargetIndex.A);
            noose.WithProgressBarToilDelay(TargetIndex.A);
            noose.AddFailCondition(() => Prisoner.CurJobDef != EndItDefOf.EndIt_Hanged);
            yield return noose;

            Toil drop = ToilMaker.MakeToil("EndIt_HangDrop");
            drop.initAction = () =>
            {
                Pawn prisoner = Prisoner;
                prisoner.health.AddHediff(EndItDefOf.EndIt_Hanging);
                // Same consequences as a vanilla prisoner execution.
                ThoughtUtility.GiveThoughtsForPawnExecuted(prisoner, pawn, PawnExecutionKind.GenericBrutal);
                TaleRecorder.RecordTale(TaleDefOf.ExecutedPrisoner, pawn, prisoner);
            };
            drop.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return drop;

            Toil watch = ToilMaker.MakeToil("EndIt_HangWatch");
            watch.tickAction = () =>
            {
                pawn.rotationTracker.FaceTarget(Prisoner);
                watchTicks++;
                if (Prisoner.Dead || Prisoner.CurJobDef != EndItDefOf.EndIt_Hanged
                    || watchTicks > HangingUtility.StrangleTicks + 120)
                    ReadyForNextToil();
            };
            watch.handlingFacing = true;
            watch.defaultCompleteMode = ToilCompleteMode.Never;
            yield return watch;
        }
    }

    /// <summary>
    /// The prisoner's side: stand on the trapdoor while the noose goes on, then hang until dead.
    /// The pawn is lifted with <see cref="ForcedBodyOffset"/>; they struggle, then go limp and sway.
    /// </summary>
    public class JobDriver_Hanged : JobDriver
    {
        // Standing on the platform, then hanging with the neck in the noose.
        private const float PlatformHeight = 0.22f;
        private const float HangHeight = 0.62f;
        private const int DropTicks = 5;

        private int hangStartTick = -1;
        private int waitTicks;

        private Thing Gallows => job.targetA.Thing;

        private Hediff Hanging => pawn.health.hediffSet.GetFirstHediffOfDef(EndItDefOf.EndIt_Hanging);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref hangStartTick, "hangStartTick", -1);
            Scribe_Values.Look(ref waitTicks, "waitTicks");
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override bool CanBeginNowWhileLyingDown() => true;

        public override Vector3 ForcedBodyOffset
        {
            get
            {
                if (hangStartTick < 0) return new Vector3(0f, 0f, PlatformHeight);

                int t = Find.TickManager.TicksGame - hangStartTick;
                float severity = Hanging?.Severity ?? 1f;

                // The drop, with a jolt at the end of the rope.
                float z = Mathf.Lerp(PlatformHeight, HangHeight, Mathf.Clamp01((float)t / DropTicks));
                if (t >= DropTicks) z += Mathf.Sin((t - DropTicks) * 0.8f) * 0.06f * Mathf.Exp(-(t - DropTicks) / 12f);

                // Kicking and struggling while conscious, fading out into a slow limp sway.
                float struggle = Mathf.Clamp01(1f - severity / 0.65f);
                float x = Mathf.Sin(t * 0.9f + 1.3f) * 0.05f * struggle
                          + Mathf.Sin(t * 0.37f) * 0.03f * struggle
                          + Mathf.Sin(t * 0.045f) * 0.025f;
                z += Mathf.Sin(t * 1.3f) * 0.025f * struggle;
                return new Vector3(x, 0f, z);
            }
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Gallows == null || !Gallows.Spawned || pawn.Position != Gallows.Position);

            Toil hang = ToilMaker.MakeToil("EndIt_Hanged");
            hang.initAction = () =>
            {
                pawn.pather?.StopDead();
                pawn.Rotation = Rot4.South;
            };
            hang.tickAction = HangTick;
            hang.handlingFacing = true;
            hang.defaultCompleteMode = ToilCompleteMode.Never;
            yield return hang;
        }

        private void HangTick()
        {
            pawn.Rotation = Rot4.South;
            Hediff hanging = Hanging;

            if (hanging == null)
            {
                // The warden never came back with the noose: step down.
                if (hangStartTick < 0 && ++waitTicks > 2500) EndJobWith(JobCondition.Incompletable);
                return;
            }

            if (hangStartTick < 0) hangStartTick = Find.TickManager.TicksGame;

            if (hanging.Severity >= 1f)
            {
                // Executions don't get a death letter, same as vanilla.
                pawn.forceNoDeathNotification = true;
                try
                {
                    pawn.Kill(null, hanging);
                }
                finally
                {
                    pawn.forceNoDeathNotification = false;
                }
            }
        }
    }

    /// <summary>
    /// Strangulation from hanging. Climbs to 1 over <see cref="HangingUtility.StrangleTicks"/>, at which point the
    /// <see cref="JobDriver_Hanged"/> kills the pawn. Removed if the pawn stops hanging (cut down).
    /// </summary>
    public class Hediff_Hanging : HediffWithComps
    {
        private bool cutDown;

        public override bool ShouldRemove => cutDown || base.ShouldRemove;

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn.Dead) return;
            if (pawn.jobs?.curDriver is not JobDriver_Hanged)
            {
                cutDown = true;
                return;
            }
            Severity = Mathf.Min(1f, Severity + (float)delta / HangingUtility.StrangleTicks);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref cutDown, "cutDown");
        }
    }
}
