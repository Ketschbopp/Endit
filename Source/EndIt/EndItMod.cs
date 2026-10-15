using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace EndIt
{
    public class EndItSettings : ModSettings
    {
        public float deathChancePistol = 0.80f;
        public float deathChanceRifle = 0.90f;
        public float deathChanceBlade = 0.60f;
        public bool requireInjury = false;
        public bool confirm = true;
        public bool gunExecutions = true;
        public int executionAimTicks = 75;

        public float DeathChanceFor(WeaponStyle style)
        {
            switch (style)
            {
                case WeaponStyle.Pistol: return deathChancePistol;
                case WeaponStyle.Rifle: return deathChanceRifle;
                default: return deathChanceBlade;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref deathChancePistol, "deathChancePistol", 0.80f);
            Scribe_Values.Look(ref deathChanceRifle, "deathChanceRifle", 0.90f);
            Scribe_Values.Look(ref deathChanceBlade, "deathChanceBlade", 0.60f);
            Scribe_Values.Look(ref requireInjury, "requireInjury", false);
            Scribe_Values.Look(ref confirm, "confirm", true);
            Scribe_Values.Look(ref gunExecutions, "gunExecutions", true);
            Scribe_Values.Look(ref executionAimTicks, "executionAimTicks", 75);
        }
    }

    public class EndItMod : Mod
    {
        public static EndItSettings Settings;

        public EndItMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<EndItSettings>();
            new Harmony("Ketschbopp.EndIt").PatchAll(typeof(EndItMod).Assembly);
        }

        public override string SettingsCategory() => "End It";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);

            list.Label("EndIt_Settings_DeathChancePistol".Translate(Settings.deathChancePistol.ToStringPercent()));
            Settings.deathChancePistol = list.Slider(Settings.deathChancePistol, 0f, 1f);
            list.Label("EndIt_Settings_DeathChanceRifle".Translate(Settings.deathChanceRifle.ToStringPercent()));
            Settings.deathChanceRifle = list.Slider(Settings.deathChanceRifle, 0f, 1f);
            list.Label("EndIt_Settings_DeathChanceBlade".Translate(Settings.deathChanceBlade.ToStringPercent()));
            Settings.deathChanceBlade = list.Slider(Settings.deathChanceBlade, 0f, 1f);

            list.GapLine();
            list.CheckboxLabeled("EndIt_Settings_RequireInjury".Translate(), ref Settings.requireInjury);
            list.CheckboxLabeled("EndIt_Settings_Confirm".Translate(), ref Settings.confirm);

            list.GapLine();
            list.CheckboxLabeled("EndIt_Settings_GunExecutions".Translate(), ref Settings.gunExecutions);
            list.Label("EndIt_Settings_ExecutionAim".Translate((Settings.executionAimTicks / 60f).ToString("0.0")));
            Settings.executionAimTicks = Mathf.RoundToInt(list.Slider(Settings.executionAimTicks, 0f, 240f));

            list.End();
        }
    }

    [DefOf]
    public static class EndItDefOf
    {
        public static JobDef EndIt_EndIt;
        public static DamageDef EndIt_SelfShot;
        public static DamageDef EndIt_SelfCut;
        public static DamageDef EndIt_Seppuku;
        public static DamageDef EndIt_ExecutionShot;
        public static DamageDef EndIt_FinishingShot;
        public static DamageDef EndIt_MercyShot;
        public static JobDef EndIt_GunExecution;
        public static JobDef EndIt_RussianRoulette;
        public static MentalStateDef EndIt_RussianRouletteState;
        public static ThingDef EndIt_Gallows;
        public static PrisonerInteractionModeDef EndIt_Hang;
        public static JobDef EndIt_HangPrisoner;
        public static JobDef EndIt_Hanged;
        public static HediffDef EndIt_Hanging;

        static EndItDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(EndItDefOf));
        }
    }
}
