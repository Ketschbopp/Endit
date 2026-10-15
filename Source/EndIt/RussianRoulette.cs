using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace EndIt
{
    /// <summary>Only pawns with a revolver on them (held or in inventory) can have this break.</summary>
    public class MentalBreakWorker_RussianRoulette : MentalBreakWorker
    {
        public override bool BreakCanOccur(Pawn pawn)
        {
            return base.BreakCanOccur(pawn) && pawn.Spawned && !pawn.Downed && pawn.RaceProps.Humanlike
                   && pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                   && WeaponUtil.FindRevolver(pawn) != null;
        }
    }

    /// <summary>Keeps the pawn on the roulette job until it ends the state (six pulls, or a live round).</summary>
    public class MentalState_RussianRoulette : MentalState
    {
        private int jobStarts;

        public override void MentalStateTick(int delta)
        {
            base.MentalStateTick(delta);
            if (pawn.MentalState != this || !pawn.Spawned || pawn.Downed) return;
            if (pawn.CurJobDef == EndItDefOf.EndIt_RussianRoulette) return;

            ThingWithComps revolver = WeaponUtil.FindRevolver(pawn);
            // No revolver any more, or the job keeps failing: snap out of it.
            if (revolver == null || jobStarts >= 3)
            {
                RecoverFromState();
                return;
            }

            jobStarts++;
            pawn.jobs.StartJob(JobMaker.MakeJob(EndItDefOf.EndIt_RussianRoulette, revolver), JobCondition.InterruptForced);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref jobStarts, "jobStarts");
        }
    }

    /// <summary>
    /// Six rounds: spin the cylinder, revolver to the temple, pull. Each pull is live with the chance set in
    /// the mod settings (one in six by default). A live round is a pistol End it; surviving all six ends the break.
    /// </summary>
    public class JobDriver_RussianRoulette : JobDriver, IEndItAnimated
    {
        public const int Rounds = 6;

        private int round;
        private int roundStartTick = -1;
        private bool fired;
        private bool dryFire;

        private ThingWithComps Revolver => TargetThingA as ThingWithComps;

        public bool Aiming => roundStartTick >= 0 && !ended;
        public WeaponStyle Style => WeaponStyle.Pistol;
        public int AimTicks => Find.TickManager.TicksGame - roundStartTick;
        public bool Roulette => true;
        public bool DryFire => dryFire;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref round, "round");
            Scribe_Values.Look(ref roundStartTick, "roundStartTick", -1);
            Scribe_Values.Look(ref fired, "fired");
            Scribe_Values.Look(ref dryFire, "dryFire");
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override string GetReport()
        {
            return "EndIt_RouletteReport".Translate(Mathf.Min(round + 1, Rounds), Rounds);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Revolver == null || Revolver.Destroyed);

            Toil equip = ToilMaker.MakeToil("EndIt_RouletteEquip");
            equip.initAction = () =>
            {
                if (!EquipRevolver()) EndJobWith(JobCondition.Incompletable);
            };
            equip.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return equip;

            Toil play = ToilMaker.MakeToil("EndIt_RoulettePlay");
            play.initAction = () =>
            {
                pawn.pather?.StopDead();
                if (roundStartTick < 0) StartRound();
            };
            play.tickAction = RouletteTick;
            play.handlingFacing = true;
            play.defaultCompleteMode = ToilCompleteMode.Never;
            play.AddFailCondition(() => pawn.equipment?.Primary != Revolver);
            yield return play;
        }

        private void StartRound()
        {
            roundStartTick = Find.TickManager.TicksGame;
            fired = false;
            dryFire = false;
        }

        private void RouletteTick()
        {
            if (!pawn.Downed) pawn.Rotation = Rot4.South;
            int ticks = AimTicks;

            if (!fired && ticks >= EndItAnimation.RouletteFireTick)
            {
                fired = true;
                if (Rand.Chance(EndItMod.Settings.rouletteLiveChance))
                {
                    Vector3 muzzle = EndItAnimation.TryGetWorldPose(pawn, this, pawn.DrawPos, out _, out _, out Vector3 tip)
                        ? tip
                        : pawn.DrawPos;
                    EndItWounds.SelfInflict(pawn, Revolver, WeaponStyle.Pistol, muzzle);
                    if (pawn.Dead) return;
                    // Shot but alive: the game is over.
                    Finish();
                    return;
                }

                dryFire = true;
                SoundDefOf.Click.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
                MoteMaker.ThrowText(pawn.DrawPos + new Vector3(0f, 0f, 0.6f), pawn.Map, "EndIt_RouletteClick".Translate(), 3f);
            }

            if (ticks < EndItAnimation.RouletteRoundTicks) return;

            round++;
            if (round >= Rounds)
            {
                Messages.Message("EndIt_RouletteSurvived".Translate(pawn.LabelShort, Rounds), pawn,
                    MessageTypeDefOf.NeutralEvent);
                Finish();
                return;
            }
            StartRound();
        }

        private void Finish()
        {
            if (pawn.MentalStateDef == EndItDefOf.EndIt_RussianRouletteState) pawn.MentalState.RecoverFromState();
            if (!ended) EndJobWith(JobCondition.Succeeded);
        }

        private bool EquipRevolver()
        {
            ThingWithComps revolver = Revolver;
            if (revolver == null || revolver.Destroyed) return false;
            if (pawn.equipment.Primary == revolver) return true;
            if (pawn.inventory == null || !pawn.inventory.innerContainer.Contains(revolver)) return false;

            ThingWithComps toEquip = revolver.stackCount > 1 ? (ThingWithComps)revolver.SplitOff(1) : revolver;
            toEquip.holdingOwner?.Remove(toEquip);
            // Put whatever they were holding into their inventory rather than on the floor.
            ThingWithComps old = pawn.equipment.Primary;
            if (old != null)
            {
                pawn.equipment.Remove(old);
                if (!pawn.inventory.innerContainer.TryAdd(old)) GenPlace.TryPlaceThing(old, pawn.Position, pawn.Map, ThingPlaceMode.Near);
            }
            pawn.equipment.AddEquipment(toEquip);
            job.SetTarget(TargetIndex.A, toEquip);
            return true;
        }
    }
}
