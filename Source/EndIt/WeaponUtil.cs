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

        // Names that always mean a revolver.
        private static readonly string[] RevolverWords =
        {
            "revolver", "six-shooter", "sixshooter", "six shooter", "sixgun", "six-gun", "peacemaker", "single action",
            "singleaction", "python", "anaconda", "nagant", "webley", "mateba", "rhino", "raging bull", "ragingbull",
            "judge", "smith & wesson", "smith and wesson", "smithwesson", "s&w"
        };

        // Magnum handguns and revolver cartridges; only count on a handgun (so magnum rifles are left out).
        private static readonly string[] MagnumWords =
        {
            "magnum", "desert eagle", "deserteagle", "deagle", ".357", "357", ".44", "44mag", ".454", "454", "casull",
            ".500", "500sw", "460sw", "45colt", "45 colt", ".45 colt", "38special", "38 special", ".38 special"
        };

        private static readonly string[] HandgunWords = { "pistol", "handgun", "sidearm", "revolver" };

        private const float HandgunMaxMass = 2.6f;

        private static readonly Dictionary<ThingDef, bool> revolverCache = new Dictionary<ThingDef, bool>();

        /// <summary>
        /// A revolver or magnum pistol, vanilla or modded: named like a revolver; or a handgun named like a magnum,
        /// or (Combat Extended) firing revolver / magnum ammo.
        /// </summary>
        public static bool IsRevolver(Thing thing)
        {
            if (thing == null) return false;
            WeaponStyle style = Classify(thing);
            if (style != WeaponStyle.Pistol && style != WeaponStyle.Rifle) return false;
            if (!revolverCache.TryGetValue(thing.def, out bool result))
            {
                result = IsRevolverDef(thing.def, style);
                revolverCache[thing.def] = result;
            }
            return result;
        }

        private static bool IsRevolverDef(ThingDef def, WeaponStyle style)
        {
            string tags = def.weaponTags != null ? string.Join(" ", def.weaponTags) : "";
            string text = (def.defName + " " + def.label + " " + tags + " " + AmmoSetName(def)).ToLowerInvariant();
            if (RevolverWords.Any(text.Contains)) return true;
            if (!MagnumWords.Any(text.Contains)) return false;
            return style == WeaponStyle.Pistol || HandgunWords.Any(tags.ToLowerInvariant().Contains)
                   || def.GetStatValueAbstract(StatDefOf.Mass) <= HandgunMaxMass;
        }

        /// <summary>
        /// Combat Extended: the gun's ammo set (e.g. AmmoSet_44Magnum), read without referencing CE. CE's generic
        /// ammo option keeps that set's defName and links it to a generic one through "similarTo", so both names are
        /// returned.
        /// </summary>
        private static string AmmoSetName(ThingDef def)
        {
            if (def.comps == null) return "";
            foreach (CompProperties props in def.comps)
            {
                System.Reflection.FieldInfo field = props.GetType().GetField("ammoSet");
                if (field?.GetValue(props) is not Def ammoSet) continue;
                Def similar = ammoSet.GetType().GetField("similarTo")?.GetValue(ammoSet) as Def;
                return ammoSet.defName + " " + similar?.defName;
            }
            return "";
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
