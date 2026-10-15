using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace EndIt
{
    /// <summary>Adds the "End it" button to hurt or downed colonists. Healthy ones use the right-click menu.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo g in __result) yield return g;
            if (Find.Selector.NumSelected == 1 && EndItUtility.IsCandidate(__instance) && EndItUtility.IsHurt(__instance))
                yield return EndItUtility.MakeGizmo(__instance);
        }
    }

    /// <summary>
    /// Vanilla refuses to open a right-click menu for a downed pawn ("is incapacitated").
    /// Let it through when the downed pawn is being right-clicked on themself, so "End it" can be offered.
    /// </summary>
    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.ShouldGenerateFloatMenuForPawn))]
    public static class Patch_FloatMenuMakerMap_ShouldGenerateFloatMenuForPawn
    {
        public static void Postfix(Pawn pawn, ref AcceptanceReport __result)
        {
            if (__result.Accepted || pawn == null || !pawn.Downed) return;
            if (EndItUtility.SelfClickedPawn(new List<Pawn> { pawn }, UI.MouseMapPosition()) != null)
                __result = AcceptanceReport.WasAccepted;
        }
    }

    /// <summary>
    /// Right-clicking the selected pawn on themself offers "End it", whether they're healthy, hurt or downed.
    /// </summary>
    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
    public static class Patch_FloatMenuMakerMap_GetOptions
    {
        public static bool Prefix(List<Pawn> selectedPawns, Vector3 clickPos, ref FloatMenuContext context,
            ref List<FloatMenuOption> __result, out FloatMenuOption __state)
        {
            __state = null;
            Pawn pawn = EndItUtility.SelfClickedPawn(selectedPawns, clickPos);
            if (pawn == null || !pawn.Downed) return true;

            // A downed pawn can't follow any of the vanilla orders, so the menu holds only "End it".
            __state = EndItUtility.MakeFloatMenuOption(pawn);
            context = new FloatMenuContext(selectedPawns, clickPos, pawn.Map);
            __result = new List<FloatMenuOption> { __state };
            return false;
        }

        public static void Postfix(List<Pawn> selectedPawns, Vector3 clickPos, List<FloatMenuOption> __result,
            FloatMenuOption __state)
        {
            if (__state != null || __result == null) return;
            Pawn pawn = EndItUtility.SelfClickedPawn(selectedPawns, clickPos);
            if (pawn != null)
            {
                __result.Add(EndItUtility.MakeFloatMenuOption(pawn));
                return;
            }
            GunExecutionOrder.AddOptions(selectedPawns, clickPos, __result);
        }
    }

    /// <summary>Draws the weapon in its animated pose while the pawn is ending it.</summary>
    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.DynamicDrawPhaseAt))]
    public static class Patch_PawnRenderer_DynamicDrawPhaseAt
    {
        public static void Postfix(DrawPhase phase, Vector3 drawLoc, Pawn ___pawn)
        {
            if (phase != DrawPhase.Draw) return;
            object curDriver = ___pawn?.jobs?.curDriver;
            if (curDriver is JobDriver_Torture torture)
            {
                torture.DrawTools(drawLoc);
                return;
            }
            if (curDriver is not IEndItAnimated driver || !driver.Aiming) return;
            EndItAnimation.Draw(___pawn, driver, drawLoc);
        }
    }

    /// <summary>Stops vanilla drawing a second copy of the weapon (e.g. if the pawn was drafted).</summary>
    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.CarryWeaponOpenly))]
    public static class Patch_PawnRenderUtility_CarryWeaponOpenly
    {
        public static bool Prefix(Pawn pawn, ref bool __result)
        {
            if (pawn?.jobs?.curDriver is IEndItAnimated driver && driver.Aiming)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    public static class GunExecution
    {
        public static bool IsExecutionJob(JobDef def)
        {
            return def != null && (def == JobDefOf.PrisonerExecution || def == JobDefOf.GuiltyColonistExecution
                                   || def == JobDefOf.SlaveExecution);
        }

        /// <summary>The executioner's gun, if this vanilla execution should be a shooting.</summary>
        public static ThingWithComps GunFor(Pawn executioner)
        {
            return EndItMod.Settings.gunExecutions ? HeldGun(executioner) : null;
        }

        /// <summary>The gun the pawn has equipped, if any and if it can fire.</summary>
        public static ThingWithComps HeldGun(Pawn pawn)
        {
            ThingWithComps primary = pawn?.equipment?.Primary;
            return WeaponUtil.IsGunStyle(WeaponUtil.Classify(primary)) && WeaponUtil.IsReady(primary) ? primary : null;
        }

        /// <summary>Holding a gun that can't fire (Combat Extended: out of ammo).</summary>
        public static bool HoldsUnloadedGun(Pawn pawn)
        {
            ThingWithComps primary = pawn?.equipment?.Primary;
            return WeaponUtil.IsGunStyle(WeaponUtil.Classify(primary)) && !WeaponUtil.IsReady(primary);
        }

        /// <summary>The gun or blade the pawn has equipped, if any: what right-click executions and mercy kills use.</summary>
        public static ThingWithComps HeldExecutionWeapon(Pawn pawn)
        {
            ThingWithComps primary = pawn?.equipment?.Primary;
            return WeaponUtil.IsReady(primary) ? primary : null;
        }

        public static bool IsMelee(ThingWithComps weapon)
        {
            WeaponStyle style = WeaponUtil.Classify(weapon);
            return style == WeaponStyle.Blade || style == WeaponStyle.Sword;
        }

        /// <summary>
        /// Aims the gun at the job's target A for the configured time before the shot.
        /// Skipped (instantly) when <paramref name="gunFor"/> finds no gun.
        /// </summary>
        public static Toil MakeAimToil(JobDriver driver, System.Func<Pawn, ThingWithComps> gunFor)
        {
            Toil aim = ToilMaker.MakeToil("EndIt_ExecutionAim");
            int endTick = -1;
            aim.initAction = () =>
            {
                Pawn executioner = aim.actor;
                Pawn victim = driver.job.targetA.Thing as Pawn;
                int ticks = EndItMod.Settings.executionAimTicks;
                if (victim == null || ticks <= 0 || gunFor(executioner) == null)
                {
                    driver.ReadyForNextToil();
                    return;
                }
                endTick = Find.TickManager.TicksGame + ticks;
                executioner.rotationTracker.FaceTarget(victim);
                // A busy stance with a focus target makes vanilla draw the gun aimed at the victim.
                // Cooldown (not warmup) so the verb never actually fires.
                executioner.stances.SetStance(new Stance_Cooldown(ticks + 2, victim, executioner.equipment.PrimaryEq?.PrimaryVerb));
            };
            aim.tickAction = () =>
            {
                if (driver.job.targetA.Thing is Pawn victim) aim.actor.rotationTracker.FaceTarget(victim);
                if (Find.TickManager.TicksGame >= endTick) driver.ReadyForNextToil();
            };
            aim.handlingFacing = true;
            aim.defaultCompleteMode = ToilCompleteMode.Never;
            return aim;
        }
    }

    /// <summary>
    /// Gives gun executions a moment of aiming before the shot: inserted right before the
    /// toil that performs the execution (the last one).
    /// </summary>
    [HarmonyPatch]
    public static class Patch_ExecutionDrivers_MakeNewToils
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(JobDriver_Execute), "MakeNewToils");
            yield return AccessTools.Method(typeof(JobDriver_ExecuteGuiltyColonist), "MakeNewToils");
            yield return AccessTools.Method(typeof(JobDriver_ExecuteSlave), "MakeNewToils");
        }

        public static IEnumerable<Toil> Postfix(IEnumerable<Toil> __result, JobDriver __instance)
        {
            List<Toil> toils = __result.ToList();
            if (toils.Count > 0) toils.Insert(toils.Count - 1, GunExecution.MakeAimToil(__instance, GunExecution.GunFor));
            return toils;
        }
    }

    /// <summary>Swaps the throat cut for a gunshot when the executioner has a gun.</summary>
    [HarmonyPatch(typeof(ExecutionUtility), nameof(ExecutionUtility.DoExecutionByCut))]
    public static class Patch_ExecutionUtility_DoExecutionByCut
    {
        public static bool Prefix(Pawn executioner, Pawn victim)
        {
            if (executioner == null || victim == null || !victim.Spawned || !GunExecution.IsExecutionJob(executioner.CurJobDef))
                return true;

            ThingWithComps gun = GunExecution.GunFor(executioner);
            if (gun == null) return true;

            EndItWounds.ExecuteByGunshot(executioner, victim, gun);
            return false;
        }
    }
}
