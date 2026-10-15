using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace EndIt
{
    /// <summary>
    /// Right-clicking a prisoner or a downed enemy with a gun-carrying colonist selected offers to shoot them;
    /// right-clicking a downed friendly pawn offers a mercy kill.
    /// </summary>
    public static class GunExecutionOrder
    {
        /// <summary>Prisoners of the colony, hostile pawns that are down, or mercy-kill targets.</summary>
        public static bool IsValidVictim(Pawn victim, Pawn executioner)
        {
            return victim != null && victim != executioner && victim.Spawned && !victim.Dead
                   && (victim.IsPrisonerOfColony || (victim.Downed && victim.HostileTo(Faction.OfPlayer))
                       || IsMercyTarget(victim));
        }

        /// <summary>A friendly (non-hostile, non-prisoner) pawn that is down, conscious or not.</summary>
        public static bool IsMercyTarget(Pawn victim)
        {
            return victim != null && victim.Downed && !victim.IsPrisonerOfColony
                   && !victim.HostileTo(Faction.OfPlayer);
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
            ThingWithComps weapon = GunExecution.HeldExecutionWeapon(executioner);
            bool mercy = IsMercyTarget(victim);
            string label = (mercy ? "EndIt_MercyKill" : "EndIt_GunExecute")
                .Translate(victim.LabelShort, weapon != null ? weapon.LabelNoCount : "weapon");

            string reason = null;
            if (weapon == null)
                reason = "EndIt_GunExecute_NoGun".Translate(executioner.LabelShort);
            else if (executioner.WorkTagIsDisabled(WorkTags.Violent))
                reason = "EndIt_GunExecute_NonViolent".Translate(executioner.LabelShort);
            else if (!executioner.CanReach(victim, PathEndMode.Touch, Danger.Deadly))
                reason = "EndIt_GunExecute_NoPath".Translate();
            else if (!executioner.CanReserve(victim))
                reason = "EndIt_GunExecute_Reserved".Translate();

            if (reason != null) return new FloatMenuOption(label + " (" + reason + ")", null);

            void Order()
            {
                Job job = JobMaker.MakeJob(EndItDefOf.EndIt_GunExecution, victim);
                job.playerForced = true;
                executioner.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }

            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, () =>
            {
                // Mercy-killing one of your own people can't be undone: ask first.
                if (mercy && victim.Faction == Faction.OfPlayer && EndItMod.Settings.confirm)
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "EndIt_MercyKillConfirm".Translate(executioner.LabelShort, victim.LabelShort),
                        Order, destructive: true));
                    return;
                }
                Order();
            }, MenuOptionPriority.AttackEnemy, revalidateClickTarget: victim), executioner, victim);
        }
    }

    /// <summary>
    /// Walk up to the victim and kill them with the equipped weapon. A gun is aimed and fired into the head; a knife
    /// or sword is raised, held, and driven in (throat cut for executions, heart for mercy kills).
    /// </summary>
    public class JobDriver_GunExecution : JobDriver, IEndItAnimated
    {
        private int strikeStartTick = -1;
        private bool struck;

        private Pawn Victim => (Pawn)job.targetA.Thing;

        public bool Aiming => strikeStartTick >= 0 && !ended;
        public WeaponStyle Style => WeaponUtil.Classify(pawn.equipment?.Primary);
        public int AimTicks => Find.TickManager.TicksGame - strikeStartTick;
        public bool Roulette => false;
        public bool DryFire => false;
        public Pawn Target => Victim;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref strikeStartTick, "strikeStartTick", -1);
            Scribe_Values.Look(ref struck, "struck");
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Victim, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Once the blow has landed the victim is a corpse; let the pull-out finish.
            this.FailOn(() => !struck && (Victim == null || !Victim.Spawned
                                          || GunExecution.HeldExecutionWeapon(pawn) == null
                                          || !GunExecutionOrder.IsValidVictim(Victim, pawn)));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil strike = MakeStrikeToil();
            yield return Toils_Jump.JumpIf(strike, () => GunExecution.IsMelee(GunExecution.HeldExecutionWeapon(pawn)));

            yield return GunExecution.MakeAimToil(this, GunExecution.HeldGun);

            Toil shoot = ToilMaker.MakeToil("EndIt_GunExecutionShoot");
            shoot.initAction = () =>
            {
                Kill(GunExecution.HeldGun(pawn), melee: false);
                EndJobWith(JobCondition.Succeeded);
            };
            shoot.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return shoot;

            yield return strike;
        }

        private Toil MakeStrikeToil()
        {
            Toil strike = ToilMaker.MakeToil("EndIt_MeleeExecution");
            strike.initAction = () =>
            {
                strikeStartTick = Find.TickManager.TicksGame;
                struck = false;
                pawn.pather?.StopDead();
                pawn.rotationTracker.FaceTarget(Victim);
            };
            strike.tickAction = () =>
            {
                if (!struck) pawn.rotationTracker.FaceTarget(Victim);
                int t = AimTicks;
                if (!struck && t >= EndItAnimation.StrikeTick)
                {
                    struck = true;
                    Kill(GunExecution.HeldExecutionWeapon(pawn), melee: true);
                }
                if (t >= EndItAnimation.StrikeEndTick) EndJobWith(JobCondition.Succeeded);
            };
            strike.handlingFacing = true;
            strike.defaultCompleteMode = ToilCompleteMode.Never;
            return strike;
        }

        private void Kill(ThingWithComps weapon, bool melee)
        {
            Pawn victim = Victim;
            if (victim == null || victim.Dead || weapon == null) return;
            bool prisoner = victim.IsPrisonerOfColony;

            if (GunExecutionOrder.IsMercyTarget(victim))
            {
                // Not an execution: no execution thoughts, and the death is announced like any other.
                if (melee)
                    EndItWounds.MercyStab(pawn, victim, weapon);
                else
                    EndItWounds.ExecuteByGunshot(pawn, victim, weapon, EndItDefOf.EndIt_MercyShot, notifyDeath: true);
                return;
            }

            if (melee)
                ExecutionUtility.DoExecutionByCut(pawn, victim);
            else
                EndItWounds.ExecuteByGunshot(pawn, victim, weapon,
                    prisoner ? EndItDefOf.EndIt_ExecutionShot : EndItDefOf.EndIt_FinishingShot);

            if (prisoner)
            {
                // Same consequences as a vanilla prisoner execution.
                ThoughtUtility.GiveThoughtsForPawnExecuted(victim, pawn, PawnExecutionKind.GenericBrutal);
                TaleRecorder.RecordTale(TaleDefOf.ExecutedPrisoner, pawn, victim);
            }
        }
    }
}
