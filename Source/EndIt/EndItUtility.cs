using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace EndIt
{
    public static class EndItUtility
    {
        /// <summary>Is the "End it" option relevant to this pawn at all (shown, possibly disabled)?</summary>
        public static bool IsCandidate(Pawn pawn)
        {
            return pawn != null && pawn.Spawned && !pawn.Dead && pawn.RaceProps.Humanlike
                   && pawn.Faction == Faction.OfPlayer && pawn.HostFaction == null
                   && (!EndItMod.Settings.requireInjury || IsHurt(pawn));
        }

        public static bool IsHurt(Pawn pawn)
        {
            if (pawn.Downed) return true;
            foreach (Hediff h in pawn.health.hediffSet.hediffs)
            {
                if (h is Hediff_Injury || h is Hediff_MissingPart) return true;
            }
            return false;
        }

        public static bool CanEndIt(Pawn pawn, out string reason, out ThingWithComps weapon, out WeaponSource source)
        {
            weapon = null;
            source = WeaponSource.Equipped;
            reason = null;
            string name = pawn.LabelShort;

            if (pawn.CurJobDef == EndItDefOf.EndIt_EndIt)
                reason = "EndIt_Busy".Translate(name);
            else if (pawn.InMentalState)
                reason = "EndIt_MentalState".Translate(name);
            else if (!pawn.Awake())
                reason = "EndIt_NotAwake".Translate(name);
            else if (EndItMod.Settings.requireInjury && !IsHurt(pawn))
                reason = "EndIt_NotInjured".Translate(name);
            else if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
                reason = "EndIt_CantManipulate".Translate(name);
            else if (!WeaponUtil.TryFindWeapon(pawn, out weapon, out source))
                reason = "EndIt_NoWeapon".Translate(name);

            return reason == null;
        }

        /// <summary>Asks for confirmation (if enabled) and starts the job.</summary>
        public static void TryOrder(Pawn pawn)
        {
            if (!CanEndIt(pawn, out string reason, out ThingWithComps weapon, out _))
            {
                Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (!EndItMod.Settings.confirm)
            {
                StartJob(pawn);
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "EndIt_Confirm".Translate(pawn.LabelShort, weapon.LabelNoCount),
                () => StartJob(pawn), destructive: true, title: "EndIt_ConfirmTitle".Translate()));
        }

        private static void StartJob(Pawn pawn)
        {
            // Re-check: the situation may have changed while the dialog was open.
            if (!CanEndIt(pawn, out string reason, out ThingWithComps weapon, out _))
            {
                Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, false);
                return;
            }

            Job job = JobMaker.MakeJob(EndItDefOf.EndIt_EndIt, weapon);
            job.count = 1;
            job.playerForced = true;

            if (pawn.Downed)
            {
                // Downed pawns can't take ordered jobs; start it directly.
                pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            }
            else
            {
                if (pawn.Drafted) pawn.drafter.Drafted = false;
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }
        }

        public static Command MakeGizmo(Pawn pawn)
        {
            bool can = CanEndIt(pawn, out string reason, out ThingWithComps weapon, out _);
            Command_Action cmd = new Command_Action
            {
                defaultLabel = "EndIt_Label".Translate(),
                defaultDesc = "EndIt_Desc".Translate(pawn.LabelShort, weapon != null ? weapon.LabelNoCount : "weapon"),
                icon = weapon != null ? (Texture)weapon.def.uiIcon : TexCommand.Attack,
                action = () => TryOrder(pawn),
                Order = 5000f
            };
            if (!can) cmd.Disable(reason);
            return cmd;
        }
    }

    /// <summary>Right-clicking a selected colonist on themselves offers "End it".</summary>
    public class FloatMenuOptionProvider_EndIt : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => true;
        protected override bool CanSelfTarget => true;

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;
            if (pawn == null || clickedPawn != pawn || !EndItUtility.IsCandidate(pawn)) return null;

            if (!EndItUtility.CanEndIt(pawn, out string reason, out _, out _))
                return new FloatMenuOption("EndIt_Label".Translate() + ": " + reason, null);

            return new FloatMenuOption("EndIt_Label".Translate(), () => EndItUtility.TryOrder(pawn), MenuOptionPriority.Low);
        }
    }
}
