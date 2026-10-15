using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace EndIt
{
    public static class EndItWounds
    {
        /// <summary>The pawn turns the weapon on themselves. Always severe, fatal per the settings' death chance.</summary>
        public static void SelfInflict(Pawn pawn, ThingWithComps weapon, WeaponStyle style, Vector3 muzzle)
        {
            Map map = pawn.Map;
            bool lethal = Rand.Chance(EndItMod.Settings.DeathChanceFor(style));

            if (style == WeaponStyle.Blade)
            {
                SoundDefOf.Execute_Cut.PlayOneShot(new TargetInfo(pawn.Position, map));
            }
            else
            {
                PlayShot(weapon, pawn, muzzle);
            }

            for (int i = 0; i < 3; i++) pawn.health.DropBloodFilth();

            if (style == WeaponStyle.Blade)
                BladeWound(pawn, weapon, lethal);
            else
                GunWound(pawn, weapon, lethal);

            if (!pawn.Dead)
                Messages.Message("EndIt_Survived".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.NegativeHealthEvent);
        }

        private static void GunWound(Pawn pawn, ThingWithComps weapon, bool lethal)
        {
            DamageDef def = EndItDefOf.EndIt_SelfShot;
            float baseDamage = ProjectileDamage(weapon);
            BodyPartRecord brain = Brain(pawn);
            BodyPartRecord skull = Part(pawn, "Skull");
            BodyPartRecord head = Part(pawn, "Head");

            if (lethal)
            {
                if (skull != null) Hit(pawn, def, baseDamage, skull, weapon);
                BodyPartRecord target = brain ?? head;
                DamageInfo kill = Hit(pawn, def, MaxHp(pawn, target) + 20f, target, weapon, ignoreKillProtection: true);
                if (!pawn.Dead) pawn.Kill(kill);
                return;
            }

            // Survivable: the shot glances, deflects or misses the brain. Still a terrible wound.
            switch (Rand.RangeInclusive(0, 3))
            {
                case 0:
                    BodyPartRecord jaw = Part(pawn, "Jaw");
                    if (jaw != null)
                    {
                        Hit(pawn, def, MaxHp(pawn, jaw) * 1.5f, jaw, weapon);
                        break;
                    }
                    goto case 2;
                case 1:
                    BodyPartRecord eye = Part(pawn, "Eye");
                    if (eye != null) Hit(pawn, def, MaxHp(pawn, eye) * 1.5f, eye, weapon);
                    if (skull != null) Hit(pawn, def, MaxHp(pawn, skull) * Rand.Range(0.4f, 0.7f), skull, weapon);
                    break;
                case 2:
                    if (skull != null) Hit(pawn, def, MaxHp(pawn, skull) * Rand.Range(0.5f, 0.8f), skull, weapon);
                    if (brain != null)
                    {
                        float hp = MaxHp(pawn, brain);
                        Hit(pawn, def, Mathf.Min(hp * Rand.Range(0.25f, 0.6f), hp - 1f), brain, weapon);
                    }
                    break;
                default:
                    BodyPartRecord ear = Part(pawn, "Ear");
                    if (ear != null) Hit(pawn, def, MaxHp(pawn, ear) * 1.5f, ear, weapon);
                    if (head != null) Hit(pawn, def, baseDamage * Rand.Range(1f, 1.5f), head, weapon);
                    break;
            }
        }

        private static void BladeWound(Pawn pawn, ThingWithComps weapon, bool lethal)
        {
            DamageDef def = EndItDefOf.EndIt_SelfCut;
            BodyPartRecord neck = Part(pawn, "Neck") ?? Part(pawn, "Head");
            if (neck == null)
            {
                // Odd body plan; just make it hurt.
                DamageInfo d = new DamageInfo(def, 30f, 999f, -1f, null, null, weapon.def);
                pawn.TakeDamage(d);
                if (lethal && !pawn.Dead) pawn.Kill(d);
                return;
            }

            float hp = MaxHp(pawn, neck);
            if (lethal)
            {
                DamageInfo kill = Hit(pawn, def, hp - 1f, neck, weapon, ignoreKillProtection: true);
                if (!pawn.Dead) pawn.Kill(kill);
                return;
            }

            // Hesitation: deep, bleeding, but not through.
            Hit(pawn, def, hp * Rand.Range(0.35f, 0.6f), neck, weapon);
        }

        /// <summary>Gunshot replacement for vanilla's throat-cut execution.</summary>
        public static void ExecuteByGunshot(Pawn executioner, Pawn victim, ThingWithComps weapon)
        {
            Vector3 from = executioner.DrawPos;
            Vector3 dir = victim.DrawPos - from;
            dir.y = 0f;
            Vector3 muzzle = from + dir.normalized * 0.6f;
            muzzle.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            PlayShot(weapon, executioner, muzzle);

            int blood = Mathf.Max(GenMath.RoundRandom(victim.BodySize * 4f), 1);
            for (int i = 0; i < blood; i++) victim.health.DropBloodFilth();

            BodyPartRecord part = Brain(victim) ?? Part(victim, "Head");
            DamageInfo dinfo = new DamageInfo(EndItDefOf.EndIt_ExecutionShot, MaxHp(victim, part) + 20f, 999f, dir.AngleFlat(),
                executioner, part, weapon.def, instigatorGuilty: true);
            dinfo.SetIgnoreInstantKillProtection(true);
            dinfo.SetAllowDamagePropagation(false);
            dinfo.SetIgnoreArmor(true);

            victim.forceNoDeathNotification = true;
            try
            {
                victim.TakeDamage(dinfo);
                if (!victim.Dead) victim.Kill(dinfo);
            }
            finally
            {
                victim.forceNoDeathNotification = false;
            }
        }

        public static void PlayShot(ThingWithComps weapon, Pawn shooter, Vector3 muzzle)
        {
            Map map = shooter.Map;
            VerbProperties verb = WeaponUtil.PrimaryVerb(weapon.def);
            TargetInfo at = new TargetInfo(shooter.Position, map);
            if (verb?.soundCast != null) verb.soundCast.PlayOneShot(at);
            if (verb?.soundCastTail != null) verb.soundCastTail.PlayOneShotOnCamera(map);

            float flash = verb != null && verb.muzzleFlashScale > 0f ? verb.muzzleFlashScale : 9f;
            FleckMaker.Static(muzzle, map, FleckDefOf.ShotFlash, flash);
            FleckMaker.ThrowSmoke(muzzle, map, 0.6f);
        }

        private static DamageInfo Hit(Pawn pawn, DamageDef def, float amount, BodyPartRecord part, ThingWithComps weapon,
            bool ignoreKillProtection = false)
        {
            DamageInfo dinfo = new DamageInfo(def, Mathf.Max(1f, amount), 999f, -1f, null, part, weapon?.def);
            dinfo.SetIgnoreArmor(true);
            dinfo.SetAllowDamagePropagation(false);
            if (ignoreKillProtection) dinfo.SetIgnoreInstantKillProtection(true);
            if (!pawn.Dead) pawn.TakeDamage(dinfo);
            return dinfo;
        }

        private static float ProjectileDamage(ThingWithComps weapon)
        {
            ProjectileProperties proj = WeaponUtil.PrimaryVerb(weapon.def)?.defaultProjectile?.projectile;
            return proj != null ? proj.GetDamageAmount(weapon) : 12f;
        }

        private static float MaxHp(Pawn pawn, BodyPartRecord part)
        {
            return part != null ? part.def.GetMaxHealth(pawn) : 30f;
        }

        private static BodyPartRecord Brain(Pawn pawn)
        {
            return pawn.health.hediffSet.GetNotMissingParts(tag: BodyPartTagDefOf.ConsciousnessSource).FirstOrDefault();
        }

        private static BodyPartRecord Part(Pawn pawn, string defName)
        {
            return pawn.health.hediffSet.GetNotMissingParts().Where(p => p.def.defName == defName).RandomElementWithFallback();
        }
    }
}
