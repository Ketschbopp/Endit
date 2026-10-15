using System.Collections.Generic;
using RimWorld;
using Verse;

namespace EndIt
{
    /// <summary>
    /// Bombs (grenades, launchers and other explosive weapons) and ammunition (mortar shells, modded ammo)
    /// no longer rot away when left outside. Applied once at startup to every loaded def, modded ones included.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class NoAmmoDeterioration
    {
        static NoAmmoDeterioration()
        {
            if (!EndItMod.Settings.noAmmoDeterioration) return;

            int count = 0;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.category != ThingCategory.Item || !IsBombOrAmmo(def)) continue;
                def.SetStatBaseValue(StatDefOf.DeteriorationRate, 0f);
                def.deteriorateFromEnvironmentalEffects = false;
                count++;
            }
            Log.Message($"[End It] Disabled deterioration for {count} bombs and ammunition types.");
        }

        private static bool IsBombOrAmmo(ThingDef def)
        {
            // Mortar shells and anything else loaded into a turret.
            if (def.IsShell) return true;

            // Grenades, molotovs, launchers: weapons that fire something explosive.
            if (def.IsRangedWeapon)
            {
                ProjectileProperties proj = WeaponUtil.PrimaryVerb(def)?.defaultProjectile?.projectile;
                if (proj != null && (proj.explosionRadius > 0f || (proj.damageDef != null && proj.damageDef.isExplosive)))
                    return true;
            }

            // Ammo from mods (Combat Extended and the like) lives under an "Ammo" category.
            return InAmmoCategory(def.thingCategories);
        }

        private static bool InAmmoCategory(List<ThingCategoryDef> categories)
        {
            if (categories == null) return false;
            foreach (ThingCategoryDef cat in categories)
            {
                for (ThingCategoryDef c = cat; c != null; c = c.parent)
                {
                    if (c.defName.IndexOf("Ammo", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || c.defName.IndexOf("Shell", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }
    }
}
