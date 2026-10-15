using RimWorld;
using UnityEngine;
using Verse;

namespace EndIt
{
    /// <summary>
    /// Procedural weapon animation for <see cref="JobDriver_EndIt"/>.
    ///
    /// Poses are authored in a head-relative frame for a pawn facing south: x is screen-right,
    /// z is screen-up, angles follow RimWorld's aim convention (0 = up, 90 = right, clockwise).
    /// For a downed pawn the whole frame is rotated with the lying body.
    /// </summary>
    public static class EndItAnimation
    {
        public struct Timeline
        {
            public int Raise;  // weapon comes up from the hip
            public int Fire;   // tick the shot/cut happens; between Raise and Fire is the hesitation
            public int End;    // job ends

            public Timeline(int raise, int fire, int end)
            {
                Raise = raise;
                Fire = fire;
                End = end;
            }
        }

        private struct Pose
        {
            public Vector2 Pos;   // weapon centre
            public float Angle;   // aim angle

            public Pose(Vector2 pos, float angle)
            {
                Pos = pos;
                Angle = angle;
            }

            public static Pose Lerp(Pose a, Pose b, float t)
            {
                return new Pose(Vector2.Lerp(a.Pos, b.Pos, t), Mathf.LerpAngle(a.Angle, b.Angle, t));
            }
        }

        private static readonly Timeline PistolTimeline = new Timeline(50, 150, 185);
        private static readonly Timeline RifleTimeline = new Timeline(80, 180, 220);
        private static readonly Timeline BladeTimeline = new Timeline(50, 150, 185);

        private const int SlashTicks = 8;

        // Where the weapon hangs before it's raised: low and to the right, pointing down.
        private static readonly Vector2 RestPoint = new Vector2(0.32f, -0.62f);
        private const float RestAngle = 160f;

        public static Timeline TimelineFor(WeaponStyle style)
        {
            switch (style)
            {
                case WeaponStyle.Pistol: return PistolTimeline;
                case WeaponStyle.Rifle: return RifleTimeline;
                default: return BladeTimeline;
            }
        }

        /// <summary>Half the visible length of the weapon sprite; sprites rarely fill their whole draw rect.</summary>
        private static float HalfLength(Thing weapon)
        {
            Vector2 size = weapon.def.graphicData?.drawSize ?? Vector2.one;
            return Mathf.Max(size.x, size.y) * 0.5f * 0.75f;
        }

        private static Vector2 Dir(float angle)
        {
            float rad = angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        }

        /// <summary>Weapon centre placed so its muzzle/tip sits at <paramref name="tip"/> while pointing along <paramref name="angle"/>.</summary>
        private static Pose PointedAt(Vector2 tip, float angle, float halfLen)
        {
            return new Pose(tip - Dir(angle) * halfLen, angle);
        }

        private static Pose AimPose(WeaponStyle style, float halfLen, out Vector2 tip)
        {
            switch (style)
            {
                case WeaponStyle.Pistol:
                    // Muzzle at the right temple, barrel pointing left into the head.
                    tip = new Vector2(0.16f, 0.03f);
                    return PointedAt(tip, 270f, halfLen);
                case WeaponStyle.Rifle:
                    // Butt down by the hip, muzzle up under the chin.
                    tip = new Vector2(0.02f, -0.16f);
                    return PointedAt(tip, 348f, halfLen);
                default:
                    // Edge laid against the right side of the throat.
                    tip = new Vector2(0.02f, -0.22f);
                    return PointedAt(tip, 285f, halfLen);
            }
        }

        /// <summary>Weapon pose at a given tick of the aim toil, in the head-relative frame.</summary>
        private static Pose LocalPose(WeaponStyle style, int ticks, float halfLen, int seed, out Vector2 tip)
        {
            Timeline tl = TimelineFor(style);
            Pose rest = new Pose(RestPoint, RestAngle);
            Pose aim = AimPose(style, halfLen, out tip);

            if (ticks < tl.Raise)
            {
                return Pose.Lerp(rest, aim, Smooth((float)ticks / tl.Raise));
            }

            if (ticks < tl.Fire)
            {
                float hold = (float)(ticks - tl.Raise) / (tl.Fire - tl.Raise);

                // A moment of doubt: the weapon sags partway back down, then comes back up.
                float doubt = hold > 0.25f && hold < 0.6f ? Mathf.Sin(Mathf.PI * (hold - 0.25f) / 0.35f) * 0.3f : 0f;
                Pose p = Pose.Lerp(aim, rest, doubt);

                // Trembling hands, worse the longer it goes on.
                float amp = 0.008f + 0.02f * hold;
                float t = ticks + seed;
                p.Pos += new Vector2(Mathf.Sin(t * 0.91f) * amp, Mathf.Cos(t * 1.37f) * amp);
                p.Angle += Mathf.Sin(t * 1.13f) * 3f * hold;
                return p;
            }

            int after = ticks - tl.Fire;

            if (style == WeaponStyle.Blade)
            {
                // Quick drag across the throat, then the hand drops away.
                if (after < SlashTicks)
                {
                    float s = Smooth((float)after / SlashTicks);
                    Vector2 slashTip = Vector2.Lerp(tip, new Vector2(-0.16f, -0.26f), s);
                    float angle = Mathf.LerpAngle(285f, 250f, s);
                    tip = slashTip;
                    return PointedAt(slashTip, angle, halfLen);
                }
                Pose end = PointedAt(new Vector2(-0.16f, -0.26f), 250f, halfLen);
                return Pose.Lerp(end, rest, Smooth((float)(after - SlashTicks) / (tl.End - tl.Fire - SlashTicks)));
            }

            // Recoil: muzzle flips up and away from the head, then the arm goes limp.
            const int kick = 6;
            Vector2 away = -Dir(aim.Angle) * 0.18f;
            Pose kicked = new Pose(aim.Pos + away + new Vector2(0f, 0.08f), aim.Angle + (style == WeaponStyle.Pistol ? 35f : 20f));
            if (after < kick) return Pose.Lerp(aim, kicked, Smooth((float)after / kick));
            return Pose.Lerp(kicked, rest, Smooth((float)(after - kick) / Mathf.Max(1, tl.End - tl.Fire - kick)));
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Resolves the weapon's world-space centre and aim angle for this frame, plus where the muzzle/tip is.
        /// </summary>
        public static bool TryGetWorldPose(Pawn pawn, JobDriver_EndIt driver, Vector3 drawLoc, out Vector3 center, out float angle,
            out Vector3 tipWorld)
        {
            center = tipWorld = drawLoc;
            angle = 0f;
            ThingWithComps weapon = pawn.equipment?.Primary;
            if (weapon == null || !driver.Aiming) return false;

            PawnRenderer renderer = pawn.Drawer?.renderer;
            if (renderer == null) return false;

            bool lying = pawn.Downed || pawn.GetPosture() != PawnPosture.Standing;
            Rot4 facing = lying ? renderer.LayingFacing() : pawn.Rotation;
            float bodyAngle = lying ? renderer.BodyAngle(PawnRenderFlags.None) : 0f;
            Quaternion bodyRot = Quaternion.AngleAxis(bodyAngle, Vector3.up);

            Vector3 head = drawLoc + bodyRot * renderer.BaseHeadOffsetAt(facing);

            Pose local = LocalPose(driver.Style, driver.AimTicks, HalfLength(weapon), pawn.thingIDNumber % 97, out Vector2 tip);
            // Facing west the pawn is mirrored, so mirror the pose to keep the weapon in front of the body.
            if (facing == Rot4.West)
            {
                local.Pos.x = -local.Pos.x;
                local.Angle = 360f - local.Angle;
                tip.x = -tip.x;
            }

            float y = drawLoc.y + Mathf.Max(PawnRenderUtility.AltitudeForLayer(90f), 0.035f);
            center = head + bodyRot * new Vector3(local.Pos.x, 0f, local.Pos.y);
            center.y = y;
            tipWorld = head + bodyRot * new Vector3(tip.x, 0f, tip.y);
            tipWorld.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            angle = local.Angle + bodyAngle;
            return true;
        }

        /// <summary>Called from the pawn renderer's draw phase.</summary>
        public static void Draw(Pawn pawn, JobDriver_EndIt driver, Vector3 drawLoc)
        {
            if (!TryGetWorldPose(pawn, driver, drawLoc, out Vector3 center, out float angle, out _)) return;
            PawnRenderUtility.DrawEquipmentAiming(pawn.equipment.Primary, center, GenMath.PositiveMod(angle, 360f));
        }
    }
}
