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
        public bool tortureSlaves = true;
        public bool brainwashSlaves = true;
        public int executionAimTicks = 75;
        public float suicideChanceRjw = 0.10f;
        public float suicideChanceBrainwash = 0.10f;

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
            Scribe_Values.Look(ref tortureSlaves, "tortureSlaves", true);
            Scribe_Values.Look(ref brainwashSlaves, "brainwashSlaves", true);
            Scribe_Values.Look(ref executionAimTicks, "executionAimTicks", 75);
            Scribe_Values.Look(ref suicideChanceRjw, "suicideChanceRjw", 0.10f);
            Scribe_Values.Look(ref suicideChanceBrainwash, "suicideChanceBrainwash", 0.10f);
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
            list.CheckboxLabeled("EndIt_Settings_TortureSlaves".Translate(), ref Settings.tortureSlaves,
                "EndIt_Settings_TortureSlavesDesc".Translate());
            list.CheckboxLabeled("EndIt_Settings_BrainwashSlaves".Translate(), ref Settings.brainwashSlaves,
                "EndIt_Settings_BrainwashSlavesDesc".Translate());
            list.Label("EndIt_Settings_ExecutionAim".Translate((Settings.executionAimTicks / 60f).ToString("0.0")));
            Settings.executionAimTicks = Mathf.RoundToInt(list.Slider(Settings.executionAimTicks, 0f, 240f));

            list.GapLine();
            list.Label("EndIt_Settings_SuicideBrainwash".Translate(Settings.suicideChanceBrainwash.ToStringPercent()));
            Settings.suicideChanceBrainwash = list.Slider(Settings.suicideChanceBrainwash, 0f, 1f);
            list.Label("EndIt_Settings_SuicideRjw".Translate(Settings.suicideChanceRjw.ToStringPercent()));
            Settings.suicideChanceRjw = list.Slider(Settings.suicideChanceRjw, 0f, 1f);

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
        public static DamageDef EndIt_MercyStab;
        public static JobDef EndIt_GunExecution;
        public static JobDef EndIt_RussianRoulette;
        public static MentalStateDef EndIt_RussianRouletteState;
        public static ThingDef EndIt_Gallows;
        public static PrisonerInteractionModeDef EndIt_Hang;
        public static JobDef EndIt_HangPrisoner;
        public static JobDef EndIt_Hanged;
        public static HediffDef EndIt_Hanging;
        public static ThingDef EndIt_BadChair;
        public static JobDef EndIt_Torture;
        public static JobDef EndIt_BeingTortured;
        public static HediffDef EndIt_UnderTorture;
        public static HediffDef EndIt_NearDrowning;
        public static HediffDef EndIt_ElectricShock;
        public static HediffDef EndIt_TortureTrauma;
        public static HediffDef EndIt_Interrogated;
        public static ThoughtDef EndIt_WasTortured;
        public static ThoughtDef EndIt_TorturedSomeone;
        public static ThoughtDef EndIt_TorturedEnjoyed;
        public static ThoughtDef EndIt_TorturedForFunEnjoyed;
        public static ThoughtDef EndIt_PrisonerTortured;
        public static MentalStateDef EndIt_SuicidalState;
        public static JobDef EndIt_HangSelf;
        public static ThingDef EndIt_Brainwasher;
        public static JobDef EndIt_StrapInBrainwash;
        public static JobDef EndIt_BeingBrainwashed;
        public static HediffDef EndIt_UnderBrainwashing;
        public static HediffDef EndIt_BrainwashCatatonia;
        public static ThoughtDef EndIt_WasBrainwashed;

        static EndItDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(EndItDefOf));
        }
    }
}
