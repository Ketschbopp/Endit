using System;
using System.Reflection;
using Verse;

namespace EndIt
{
    /// <summary>
    /// Optional Combat Extended support, entirely through reflection so End It never needs CE to build or run.
    /// With CE loaded:
    /// <list type="bullet">
    /// <item>CE guns (<c>CombatExtended.Verb_ShootCE</c>, which isn't built on vanilla's <see cref="RimWorld.Verb_Shoot"/>)
    /// count as guns.</item>
    /// <item>A gun that uses CE's ammo system has to be able to fire (a round in the magazine, or ammo on hand for
    /// magazine-less guns), and a shot takes one round out of the magazine.</item>
    /// </list>
    /// Without CE every check here passes and nothing is consumed.
    /// </summary>
    public static class CombatExtendedCompat
    {
        private const string ShootVerbName = "CombatExtended.Verb_ShootCE";

        private static bool initialized;
        private static Type compAmmoUserType;
        private static PropertyInfo useAmmo;
        private static PropertyInfo canBeFiredNow;
        private static PropertyInfo hasMagazine;
        private static PropertyInfo curMagCount;

        public static bool Active
        {
            get
            {
                Init();
                return compAmmoUserType != null;
            }
        }

        private static void Init()
        {
            if (initialized) return;
            initialized = true;
            compAmmoUserType = GenTypes.GetTypeInAnyAssembly("CombatExtended.CompAmmoUser");
            if (compAmmoUserType == null) return;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            useAmmo = compAmmoUserType.GetProperty("UseAmmo", flags);
            canBeFiredNow = compAmmoUserType.GetProperty("CanBeFiredNow", flags);
            hasMagazine = compAmmoUserType.GetProperty("HasMagazine", flags);
            curMagCount = compAmmoUserType.GetProperty("CurMagCount", flags);
            if (useAmmo == null || canBeFiredNow == null || hasMagazine == null || curMagCount == null)
            {
                Log.Warning("[End It] Combat Extended found, but its ammo API has changed; ammo won't be checked or used.");
                compAmmoUserType = null;
            }
        }

        /// <summary>Is this one of CE's gun-firing verbs (Verb_ShootCE or a subclass)?</summary>
        public static bool IsShootVerb(Type verbClass)
        {
            for (Type t = verbClass; t != null; t = t.BaseType)
            {
                if (t.FullName == ShootVerbName) return true;
            }
            return false;
        }

        private static ThingComp AmmoComp(Thing gun)
        {
            if (!Active || gun is not ThingWithComps twc) return null;
            foreach (ThingComp comp in twc.AllComps)
            {
                if (compAmmoUserType.IsInstanceOfType(comp)) return comp;
            }
            return null;
        }

        private static bool UsesAmmo(ThingComp comp) => comp != null && (bool)useAmmo.GetValue(comp);

        /// <summary>False only for a CE gun on the ammo system that has nothing to fire.</summary>
        public static bool CanFire(Thing gun)
        {
            ThingComp comp = AmmoComp(gun);
            return !UsesAmmo(comp) || (bool)canBeFiredNow.GetValue(comp);
        }

        /// <summary>Takes one round out of a CE gun's magazine, if it has one loaded.</summary>
        public static void ConsumeRound(Thing gun)
        {
            ThingComp comp = AmmoComp(gun);
            if (!UsesAmmo(comp) || !(bool)hasMagazine.GetValue(comp)) return;
            int count = (int)curMagCount.GetValue(comp);
            if (count > 0) curMagCount.SetValue(comp, count - 1);
        }
    }
}
