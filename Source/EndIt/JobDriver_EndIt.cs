using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace EndIt
{
    /// <summary>
    /// Grab the weapon (if it isn't already in hand), raise it to the head (or throat), hesitate, and do it.
    /// Works for pawns on their feet and for downed pawns, which skip the walk.
    /// </summary>
    public class JobDriver_EndIt : JobDriver
    {
        private int aimStartTick = -1;
        private bool fired;
        private bool cutDone;
        private WeaponStyle style = WeaponStyle.None;

        private ThingWithComps Weapon => TargetThingA as ThingWithComps;

        public bool Aiming => aimStartTick >= 0 && !ended && style != WeaponStyle.None;
        public WeaponStyle Style => style;
        public int AimTicks => Find.TickManager.TicksGame - aimStartTick;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref aimStartTick, "aimStartTick", -1);
            Scribe_Values.Look(ref fired, "fired");
            Scribe_Values.Look(ref cutDone, "cutDone");
            Scribe_Values.Look(ref style, "style");
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            ThingWithComps weapon = Weapon;
            if (weapon == null) return false;
            if (!weapon.Spawned) return true;
            return pawn.Reserve(weapon, job, 1, 1, null, errorOnFailed);
        }

        public override bool CanBeginNowWhileLyingDown() => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Weapon == null || Weapon.Destroyed);
            // Seppuku: once the blade is in, the cut always happens, even if the pawn passes out first.
            AddFinishAction(_ => FinishSeppukuCut());

            Toil equip = ToilMaker.MakeToil("EndIt_Equip");
            equip.initAction = () =>
            {
                if (!TryEquip()) EndJobWith(JobCondition.Incompletable);
            };
            equip.defaultCompleteMode = ToilCompleteMode.Instant;

            // Already holding it, carrying it, or (downed) it's within arm's reach: skip the walk.
            yield return Toils_Jump.JumpIf(equip, () => !Weapon.Spawned || pawn.Downed
                                                        || pawn.Position.AdjacentTo8WayOrInside(Weapon.Position));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).FailOnDespawnedOrNull(TargetIndex.A);
            yield return equip;

            Toil aim = ToilMaker.MakeToil("EndIt_Aim");
            aim.initAction = () =>
            {
                style = WeaponUtil.Classify(Weapon);
                if (style == WeaponStyle.None)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                aimStartTick = Find.TickManager.TicksGame;
                fired = false;
                cutDone = false;
                pawn.pather?.StopDead();
                if (!pawn.Downed) pawn.Rotation = Rot4.South;
            };
            aim.tickAction = AimTick;
            aim.handlingFacing = true;
            aim.defaultCompleteMode = ToilCompleteMode.Never;
            aim.AddFailCondition(() => pawn.equipment?.Primary != Weapon);
            yield return aim;
        }

        private void AimTick()
        {
            if (!pawn.Downed) pawn.Rotation = Rot4.South;

            EndItAnimation.Timeline t = EndItAnimation.TimelineFor(style);
            int ticks = AimTicks;

            if (style == WeaponStyle.Sword)
            {
                SeppukuTick(t, ticks);
                return;
            }

            if (!fired && ticks >= t.Fire)
            {
                fired = true;
                Vector3 muzzle = EndItAnimation.TryGetWorldPose(pawn, this, pawn.DrawPos, out _, out _, out Vector3 tip)
                    ? tip
                    : pawn.DrawPos;
                EndItWounds.SelfInflict(pawn, Weapon, style, muzzle);
                // Dying (or going down from the pain) ends this job from under us.
                if (ended || pawn.Dead) return;
            }

            if (ticks >= t.End) ReadyForNextToil();
        }

        private void SeppukuTick(EndItAnimation.Timeline t, int ticks)
        {
            if (!fired && ticks >= t.Fire)
            {
                fired = true;
                EndItWounds.SeppukuStab(pawn, Weapon);
                if (ended || pawn.Dead) return;
            }

            if (fired && !cutDone)
            {
                int dragged = ticks - t.Fire;
                if (dragged >= EndItAnimation.SeppukuCutTicks)
                {
                    FinishSeppukuCut();
                    if (ended || pawn.Dead) return;
                }
                else if (dragged > 0 && dragged % 15 == 0 && pawn.Spawned)
                {
                    pawn.health.DropBloodFilth();
                }
            }

            if (ticks >= t.End) ReadyForNextToil();
        }

        private void FinishSeppukuCut()
        {
            if (style != WeaponStyle.Sword || !fired || cutDone || pawn.Dead) return;
            cutDone = true;
            EndItWounds.SeppukuCut(pawn, Weapon);
        }

        private bool TryEquip()
        {
            ThingWithComps weapon = Weapon;
            if (weapon == null || weapon.Destroyed) return false;
            if (pawn.equipment.Primary == weapon) return true;

            ThingWithComps toEquip;
            if (weapon.Spawned)
            {
                if (!pawn.Position.AdjacentTo8WayOrInside(weapon.Position)) return false;
                toEquip = weapon.stackCount > 1 ? (ThingWithComps)weapon.SplitOff(1) : weapon;
                if (toEquip.Spawned) toEquip.DeSpawn();
            }
            else if (pawn.inventory != null && pawn.inventory.innerContainer.Contains(weapon))
            {
                toEquip = weapon.stackCount > 1 ? (ThingWithComps)weapon.SplitOff(1) : weapon;
                if (toEquip.holdingOwner != null) toEquip.holdingOwner.Remove(toEquip);
            }
            else
            {
                return false;
            }

            pawn.equipment.MakeRoomFor(toEquip);
            pawn.equipment.AddEquipment(toEquip);
            job.SetTarget(TargetIndex.A, toEquip);
            return true;
        }
    }
}
