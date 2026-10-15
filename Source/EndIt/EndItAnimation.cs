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
    /// <summary>A job driver whose weapon is drawn by <see cref="EndItAnimation"/>.</summary>
    public interface IEndItAnimated
    {
        bool Aiming { get; }
        WeaponStyle Style { get; }
        int AimTicks { get; }

        /// <summary>Russian roulette: spin the cylinder before each raise.</summary>
        bool Roulette { get; }

        /// <summary>Russian roulette: this pull was an empty chamber.</summary>
        bool DryFire { get; }
    }

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

        // Seppuku: thrust into the belly at Fire, drag across it, hold, then the hands let go.
        private const int SeppukuThrustTicks = 6;
        private const int SeppukuDragTicks = 60;
        private const int SeppukuHoldTicks = 25;
        private const int SeppukuDropTicks = 35;

        /// <summary>Ticks after Fire when the drag across the belly is finished.</summary>
        public const int SeppukuCutTicks = SeppukuThrustTicks + SeppukuDragTicks;

        private static readonly Timeline SwordTimeline =
            new Timeline(70, 190, 190 + SeppukuCutTicks + SeppukuHoldTicks + SeppukuDropTicks);

        // The belly, left to right as the pawn sees it (facing south, their left is screen-right).
        private static readonly Vector2 BellyStart = new Vector2(0.13f, -0.44f);
        private static readonly Vector2 BellyEnd = new Vector2(-0.13f, -0.44f);
        // How far in front of the belly the tip hovers before the thrust.
        private const float SeppukuReach = 0.14f;
        // The blade points back into the body, so it extends down-screen from the belly.
        private const float SeppukuStartAngle = 10f;
        private const float SeppukuEndAngle = 350f;

        // Where the weapon hangs before it's raised: low and to the right, pointing down.
        private static readonly Vector2 RestPoint = new Vector2(0.32f, -0.62f);
        private const float RestAngle = 160f;

        // Russian roulette, per round: spin the cylinder, raise, hesitate, pull, lower, pause.
        public const int RouletteSpinTicks = 60;
        private const int RouletteLowerTicks = 45;
        private const int RoulettePauseTicks = 60;
        public static int RouletteFireTick => RouletteSpinTicks + PistolTimeline.Fire;
        public static int RouletteRoundTicks => RouletteFireTick + RouletteLowerTicks + RoulettePauseTicks;

        // Held flat in front of the chest, barrel to the right, while the cylinder is spun.
        private static readonly Pose RouletteChest = new Pose(new Vector2(0.06f, -0.4f), 90f);

        public static Timeline TimelineFor(WeaponStyle style)
        {
            switch (style)
            {
                case WeaponStyle.Pistol: return PistolTimeline;
                case WeaponStyle.Rifle: return RifleTimeline;
                case WeaponStyle.Sword: return SwordTimeline;
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
            if (style == WeaponStyle.Sword) return SeppukuPose(ticks, halfLen, seed, out tip);

            Timeline tl = TimelineFor(style);
            Pose rest = new Pose(RestPoint, RestAngle);
            Pose aim = AimPose(style, halfLen, out tip);

            if (ticks < tl.Raise)
            {
                return Pose.Lerp(rest, aim, Smooth((float)ticks / tl.Raise));
            }

            if (ticks < tl.Fire) return Hesitate(aim, rest, ticks, tl, seed);

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

        /// <summary>Between Raise and Fire: a moment of doubt and trembling hands.</summary>
        private static Pose Hesitate(Pose aim, Pose rest, int ticks, Timeline tl, int seed)
        {
            float hold = (float)(ticks - tl.Raise) / (tl.Fire - tl.Raise);

            // A moment of doubt: the weapon sags partway back down, then comes back up.
            float doubt = hold > 0.25f && hold < 0.6f ? Mathf.Sin(Mathf.PI * (hold - 0.25f) / 0.35f) * 0.3f : 0f;
            Pose p = Pose.Lerp(aim, rest, doubt);

            // Trembling hands, worse the longer it goes on.
            return Tremble(p, ticks, seed, 0.008f + 0.02f * hold, 3f * hold);
        }

        private static Pose Tremble(Pose p, int ticks, int seed, float amp, float angleAmp)
        {
            float t = ticks + seed;
            p.Pos += new Vector2(Mathf.Sin(t * 0.91f) * amp, Mathf.Cos(t * 1.37f) * amp);
            p.Angle += Mathf.Sin(t * 1.13f) * angleAmp;
            return p;
        }

        /// <summary>
        /// Seppuku: the sword is turned around and held in both hands with the tip at the belly, driven in,
        /// dragged slowly across from the pawn's left to right, held there, then the hands let go.
        /// </summary>
        private static Pose SeppukuPose(int ticks, float halfLen, int seed, out Vector2 tip)
        {
            Timeline tl = SwordTimeline;
            Pose rest = new Pose(RestPoint, RestAngle);
            Vector2 readyTip = BellyStart + new Vector2(0f, -SeppukuReach);
            Pose ready = PointedAt(readyTip, SeppukuStartAngle, halfLen);
            tip = readyTip;

            if (ticks < tl.Raise) return Pose.Lerp(rest, ready, Smooth((float)ticks / tl.Raise));
            if (ticks < tl.Fire) return Hesitate(ready, rest, ticks, tl, seed);

            int after = ticks - tl.Fire;
            if (after < SeppukuThrustTicks)
            {
                tip = Vector2.Lerp(readyTip, BellyStart, Smooth((float)after / SeppukuThrustTicks));
                return PointedAt(tip, SeppukuStartAngle, halfLen);
            }

            after -= SeppukuThrustTicks;
            if (after < SeppukuDragTicks)
            {
                // Slow and uneven: the blade catches and has to be forced along.
                float s = Smooth((float)after / SeppukuDragTicks);
                s = Mathf.Clamp01(s + Mathf.Sin(after * 0.6f) * 0.015f);
                tip = Vector2.Lerp(BellyStart, BellyEnd, s);
                Pose drag = PointedAt(tip, Mathf.LerpAngle(SeppukuStartAngle, SeppukuEndAngle, s), halfLen);
                return Tremble(drag, ticks, seed, 0.006f, 2f);
            }

            Pose cut = PointedAt(BellyEnd, SeppukuEndAngle, halfLen);
            after -= SeppukuDragTicks;
            tip = BellyEnd;
            if (after < SeppukuHoldTicks)
                return Tremble(cut, ticks, seed, 0.01f + 0.01f * after / SeppukuHoldTicks, 3f);

            after -= SeppukuHoldTicks;
            return Pose.Lerp(cut, rest, Smooth((float)after / SeppukuDropTicks));
        }

        /// <summary>
        /// One round of Russian roulette: the cylinder is spun at chest height, the revolver goes to the temple,
        /// the pawn hesitates and pulls the trigger. A live round recoils like a pistol End it; an empty chamber
        /// gives a startled twitch, then the revolver is lowered for the next round.
        /// </summary>
        private static Pose RoulettePose(int ticks, float halfLen, int seed, bool dryFire, out Vector2 tip)
        {
            Timeline tl = PistolTimeline;
            Pose rest = new Pose(RestPoint, RestAngle);
            Pose aim = AimPose(WeaponStyle.Pistol, halfLen, out tip);

            if (ticks < RouletteSpinTicks)
            {
                float t = (float)ticks / RouletteSpinTicks;
                Pose p = Pose.Lerp(rest, RouletteChest, Smooth(t * 3f));
                // The flick of the cylinder: the gun jerks as it spins.
                if (t > 0.35f && t < 0.8f) p.Angle += Mathf.Sin(ticks * 1.7f) * 7f;
                return p;
            }

            int rel = ticks - RouletteSpinTicks;
            if (rel < tl.Raise) return Pose.Lerp(RouletteChest, aim, Smooth((float)rel / tl.Raise));
            if (rel < tl.Fire) return Hesitate(aim, RouletteChest, rel, tl, seed);
            if (!dryFire) return LocalPose(WeaponStyle.Pistol, rel, halfLen, seed, out tip);

            // *click*
            int after = rel - tl.Fire;
            const int twitch = 4;
            const int frozen = 20;
            if (after < twitch)
            {
                Pose jerk = aim;
                jerk.Angle += 8f * Smooth((float)after / twitch);
                return jerk;
            }
            if (after < frozen) return Tremble(aim, ticks, seed, 0.02f, 4f);
            return Pose.Lerp(aim, rest, Smooth((float)(after - frozen) / (RouletteLowerTicks - frozen)));
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Resolves the weapon's world-space centre and aim angle for this frame, plus where the muzzle/tip is.
        /// </summary>
        public static bool TryGetWorldPose(Pawn pawn, IEndItAnimated driver, Vector3 drawLoc, out Vector3 center, out float angle,
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

            int seed = pawn.thingIDNumber % 97;
            Pose local = driver.Roulette
                ? RoulettePose(driver.AimTicks, HalfLength(weapon), seed, driver.DryFire, out Vector2 tip)
                : LocalPose(driver.Style, driver.AimTicks, HalfLength(weapon), seed, out tip);
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
        public static void Draw(Pawn pawn, IEndItAnimated driver, Vector3 drawLoc)
        {
            if (!TryGetWorldPose(pawn, driver, drawLoc, out Vector3 center, out float angle, out _)) return;
            PawnRenderUtility.DrawEquipmentAiming(pawn.equipment.Primary, center, GenMath.PositiveMod(angle, 360f));
        }
    }
}
