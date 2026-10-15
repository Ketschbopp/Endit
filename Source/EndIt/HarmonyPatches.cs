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
    /// <summary>Adds the "End it" button to hurt colonists (works while downed, unlike the right-click menu).</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo g in __result) yield return g;
            if (EndItUtility.IsCandidate(__instance) && Find.Selector.NumSelected == 1)
                yield return EndItUtility.MakeGizmo(__instance);
        }
    }

    /// <summary>Draws the weapon in its animated pose while the pawn is ending it.</summary>
    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.DynamicDrawPhaseAt))]
    public static class Patch_PawnRenderer_DynamicDrawPhaseAt
    {
        public static void Postfix(DrawPhase phase, Vector3 drawLoc, Pawn ___pawn)
        {
            if (phase != DrawPhase.Draw || ___pawn?.jobs?.curDriver is not JobDriver_EndIt driver || !driver.Aiming) return;
            EndItAnimation.Draw(___pawn, driver, drawLoc);
        }
    }

    /// <summary>Stops vanilla drawing a second copy of the weapon (e.g. if the pawn was drafted).</summary>
    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.CarryWeaponOpenly))]
    public static class Patch_PawnRenderUtility_CarryWeaponOpenly
    {
        public static bool Prefix(Pawn pawn, ref bool __result)
        {
            if (pawn?.jobs?.curDriver is JobDriver_EndIt driver && driver.Aiming)
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

        /// <summary>The executioner's gun, if this execution should be a shooting.</summary>
        public static ThingWithComps GunFor(Pawn executioner)
        {
            if (!EndItMod.Settings.gunExecutions || executioner == null) return null;
            ThingWithComps primary = executioner.equipment?.Primary;
            WeaponStyle style = WeaponUtil.Classify(primary);
            return style == WeaponStyle.Pistol || style == WeaponStyle.Rifle ? primary : null;
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
            if (toils.Count > 0) toils.Insert(toils.Count - 1, MakeAimToil(__instance));
            return toils;
        }

        private static Toil MakeAimToil(JobDriver driver)
        {
            Toil aim = ToilMaker.MakeToil("EndIt_ExecutionAim");
            int endTick = -1;
            aim.initAction = () =>
            {
                Pawn executioner = aim.actor;
                Pawn victim = driver.job.targetA.Thing as Pawn;
                int ticks = EndItMod.Settings.executionAimTicks;
                if (victim == null || ticks <= 0 || GunExecution.GunFor(executioner) == null)
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
