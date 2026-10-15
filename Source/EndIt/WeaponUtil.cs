using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace EndIt
{
    public enum WeaponStyle
    {
        None,
        Pistol,
        Rifle,
        Blade,
        Sword
    }

    public enum WeaponSource
    {
        Equipped,
        Inventory,
        Ground
    }

    public static class WeaponUtil
    {
        // How far a pawn on their feet will walk to grab a weapon off the floor.
        public const float StandingSearchRadius = 12f;

        // Pistols are light; anything heavier counts as a long gun.
        private const float PistolMaxMass = 1.9f;

        private static readonly string[] PistolWords = { "pistol", "revolver", "handgun", "derringer" };

        private static readonly string[] SwordWords =
            { "sword", "katana", "wakizashi", "tanto", "gladius", "zweihander", "saber", "sabre", "rapier", "scimitar", "cutlass" };

        public static WeaponStyle Classify(Thing thing)
        {
            if (thing == null) return WeaponStyle.None;
            return Classify(thing.def);
        }

        public static WeaponStyle Classify(ThingDef def)
        {
            if (def == null || !def.IsWeapon) return WeaponStyle.None;

            if (def.IsRangedWeapon)
                return IsGun(def) ? (IsPistol(def) ? WeaponStyle.Pistol : WeaponStyle.Rifle) : WeaponStyle.None;

            if (def.IsMeleeWeapon && def.tools != null && def.tools.Any(t => t.capacities != null &&
                    t.capacities.Any(c => c.defName == "Cut" || c.defName == "Stab")))
                return IsSword(def) ? WeaponStyle.Sword : WeaponStyle.Blade;

            return WeaponStyle.None;
        }

        public static bool IsGun(ThingDef def)
        {
            if (def == null || !def.IsRangedWeapon || def.techLevel < TechLevel.Industrial) return false;
            VerbProperties verb = PrimaryVerb(def);
            if (verb == null || verb.verbClass == null || !IsShootVerb(verb.verbClass)) return false;
            ProjectileProperties proj = verb.defaultProjectile?.projectile;
            if (proj == null || proj.damageDef == null) return false;
            // Launchers, grenades, EMP/smoke and the like are not something you put to your head.
            return proj.damageDef.harmsHealth && !proj.damageDef.isExplosive && proj.explosionRadius <= 0f;
        }

        /// <summary>Vanilla's Verb_Shoot, or Combat Extended's Verb_ShootCE.</summary>
        private static bool IsShootVerb(System.Type verbClass)
        {
            return typeof(Verb_Shoot).IsAssignableFrom(verbClass) || CombatExtendedCompat.IsShootVerb(verbClass);
        }

        public static bool IsGunStyle(WeaponStyle style) => style == WeaponStyle.Pistol || style == WeaponStyle.Rifle;

        /// <summary>Blades always; guns only if they can fire (with Combat Extended, if they have ammo).</summary>
        public static bool IsReady(Thing weapon)
        {
            WeaponStyle style = Classify(weapon);
            return style != WeaponStyle.None && (!IsGunStyle(style) || CombatExtendedCompat.CanFire(weapon));
        }

        public static bool IsPistol(ThingDef def)
        {
            string name = (def.defName + " " + def.label).ToLowerInvariant();
            if (PistolWords.Any(name.Contains)) return true;
            return def.GetStatValueAbstract(StatDefOf.Mass) <= PistolMaxMass;
        }

        public static bool IsSword(ThingDef def)
        {
            string name = (def.defName + " " + def.label).ToLowerInvariant();
            return SwordWords.Any(name.Contains);
        }

        public static bool IsRevolver(Thing thing)
        {
            if (thing == null) return false;
            WeaponStyle style = Classify(thing);
            return (style == WeaponStyle.Pistol || style == WeaponStyle.Rifle)
                   && (thing.def.defName + " " + thing.def.label).ToLowerInvariant().Contains("revolver");
        }

        /// <summary>The revolver the pawn holds, or else the first one in their inventory.</summary>
        public static ThingWithComps FindRevolver(Pawn pawn)
        {
            ThingWithComps primary = pawn.equipment?.Primary;
            if (IsRevolver(primary) && EquipmentUtility.CanEquip(primary, pawn)) return primary;
            return pawn.inventory?.innerContainer.FirstOrDefault(t => IsRevolver(t) && t is ThingWithComps
                && EquipmentUtility.CanEquip(t, pawn)) as ThingWithComps;
        }

        public static VerbProperties PrimaryVerb(ThingDef def)
        {
            List<VerbProperties> verbs = def?.Verbs;
            if (verbs.NullOrEmpty()) return null;
            return verbs.FirstOrDefault(v => v.isPrimary) ?? verbs[0];
        }

        private static bool Usable(Thing thing, Pawn pawn)
        {
            return thing is ThingWithComps && IsReady(thing) && EquipmentUtility.CanEquip(thing, pawn);
        }

        /// <summary>
        /// Finds the weapon the pawn would use: what they hold, then their inventory, then the floor.
        /// Downed pawns can only reach adjacent cells.
        /// </summary>
        public static bool TryFindWeapon(Pawn pawn, out ThingWithComps weapon, out WeaponSource source)
        {
            weapon = null;
            source = WeaponSource.Equipped;

            ThingWithComps primary = pawn.equipment?.Primary;
            if (primary != null && Usable(primary, pawn))
            {
                weapon = primary;
                return true;
            }

            if (pawn.inventory != null)
            {
                Thing inv = pawn.inventory.innerContainer.FirstOrDefault(t => Usable(t, pawn));
                if (inv != null)
                {
                    weapon = (ThingWithComps)inv;
                    source = WeaponSource.Inventory;
                    return true;
                }
            }

            if (!pawn.Spawned) return false;
            Map map = pawn.Map;
            source = WeaponSource.Ground;

            if (pawn.Downed)
            {
                foreach (IntVec3 cell in GenAdj.CellsOccupiedBy(pawn).Concat(GenAdj.CellsAdjacent8Way(pawn)))
                {
                    if (!cell.InBounds(map)) continue;
                    foreach (Thing t in cell.GetThingList(map))
                    {
                        if (t.def.category == ThingCategory.Item && Usable(t, pawn) && pawn.CanReserve(t))
                        {
                            weapon = (ThingWithComps)t;
                            return true;
                        }
                    }
                }
                return false;
            }

            Thing found = GenClosest.ClosestThingReachable(pawn.Position, map, ThingRequest.ForGroup(ThingRequestGroup.Weapon),
                PathEndMode.ClosestTouch, TraverseParms.For(pawn), StandingSearchRadius,
                t => Usable(t, pawn) && pawn.CanReserve(t));
            weapon = found as ThingWithComps;
            return weapon != null;
        }
    }
}
