using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace EndIt
{
    /// <summary>
    /// Right-clicking a prisoner or a downed enemy with a gun-carrying colonist selected offers to shoot them.
    /// </summary>
    public static class GunExecutionOrder
    {
        /// <summary>Prisoners of the colony, or hostile pawns that are down.</summary>
        public static bool IsValidVictim(Pawn victim, Pawn executioner)
        {
            return victim != null && victim != executioner && victim.Spawned && !victim.Dead
                   && (victim.IsPrisonerOfColony || (victim.Downed && victim.HostileTo(Faction.OfPlayer)));
        }

        public static void AddOptions(List<Pawn> selectedPawns, Vector3 clickPos, List<FloatMenuOption> options)
        {
            if (selectedPawns == null || selectedPawns.Count != 1) return;
            Pawn executioner = selectedPawns[0];
            if (executioner == null || !executioner.Spawned || executioner.Downed || executioner.Map != Find.CurrentMap
                || executioner.Faction != Faction.OfPlayer || !executioner.RaceProps.Humanlike)
                return;

            IntVec3 cell = IntVec3.FromVector3(clickPos);
            if (!cell.InBounds(executioner.Map)) return;
            foreach (Thing t in cell.GetThingList(executioner.Map))
            {
                if (t is Pawn victim && IsValidVictim(victim, executioner))
                    options.Add(MakeOption(executioner, victim));
            }
        }

        private static FloatMenuOption MakeOption(Pawn executioner, Pawn victim)
        {
            ThingWithComps gun = GunExecution.HeldGun(executioner);
            string label = "EndIt_GunExecute".Translate(victim.LabelShort, gun != null ? gun.LabelNoCount : "gun");

            string reason = null;
            if (gun == null)
                reason = "EndIt_GunExecute_NoGun".Translate(executioner.LabelShort);
            else if (executioner.WorkTagIsDisabled(WorkTags.Violent))
                reason = "EndIt_GunExecute_NonViolent".Translate(executioner.LabelShort);
            else if (!executioner.CanReach(victim, PathEndMode.Touch, Danger.Deadly))
                reason = "EndIt_GunExecute_NoPath".Translate();
            else if (!executioner.CanReserve(victim))
                reason = "EndIt_GunExecute_Reserved".Translate();

            if (reason != null) return new FloatMenuOption(label + " (" + reason + ")", null);

            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, () =>
            {
                Job job = JobMaker.MakeJob(EndItDefOf.EndIt_GunExecution, victim);
                job.playerForced = true;
                executioner.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }, MenuOptionPriority.AttackEnemy, revalidateClickTarget: victim), executioner, victim);
        }
    }

    /// <summary>Walk up to the victim, aim, and shoot them in the head.</summary>
    public class JobDriver_GunExecution : JobDriver
    {
        private Pawn Victim => (Pawn)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Victim, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => GunExecution.HeldGun(pawn) == null || !GunExecutionOrder.IsValidVictim(Victim, pawn));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return GunExecution.MakeAimToil(this, GunExecution.HeldGun);

            Toil shoot = ToilMaker.MakeToil("EndIt_GunExecutionShoot");
            shoot.initAction = () =>
            {
                Pawn victim = Victim;
                bool prisoner = victim.IsPrisonerOfColony;
                EndItWounds.ExecuteByGunshot(pawn, victim, GunExecution.HeldGun(pawn),
                    prisoner ? EndItDefOf.EndIt_ExecutionShot : EndItDefOf.EndIt_FinishingShot);
                if (prisoner)
                {
                    // Same consequences as a vanilla prisoner execution.
                    ThoughtUtility.GiveThoughtsForPawnExecuted(victim, pawn, PawnExecutionKind.GenericBrutal);
                    TaleRecorder.RecordTale(TaleDefOf.ExecutedPrisoner, pawn, victim);
                }
            };
            shoot.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return shoot;
        }
    }
}
