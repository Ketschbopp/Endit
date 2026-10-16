using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace EndIt
{
    /// <summary>Opened from the Bad Chair: pick the prisoner, the torturer, the method, the reason and how long.</summary>
    public class Dialog_Torture : Window
    {
        private readonly CompTortureChair chair;
        private Pawn prisoner;
        private Pawn torturer;
        private TortureMethod method = TortureMethod.Beating;
        private TortureReason reason = TortureReason.Intel;
        private int hours = 3;
        private bool stopBeforeDeath = true;

        public override Vector2 InitialSize => new Vector2(560f, 720f);

        public Dialog_Torture(CompTortureChair chair)
        {
            this.chair = chair;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = true;
            prisoner = Prisoners().FirstOrDefault();
            torturer = Torturers().FirstOrDefault();
        }

        private Map Map => chair.parent.Map;

        private IEnumerable<Pawn> Prisoners()
        {
            return Map.mapPawns.PrisonersOfColonySpawned.Where(p => !p.Dead && !CompTortureChair.IsBeingTortured(p));
        }

        private IEnumerable<Pawn> Torturers()
        {
            return Map.mapPawns.FreeColonistsSpawned.Where(p => !p.Dead && !p.Downed && !p.InMentalState
                                                                && !p.WorkTagIsDisabled(WorkTags.Violent)
                                                                && p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation));
        }

        public override void DoWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);

            Text.Font = GameFont.Medium;
            list.Label("EndIt_Torture_Title".Translate());
            Text.Font = GameFont.Small;
            list.GapLine();

            PawnPicker(list, "EndIt_Torture_Prisoner".Translate(), prisoner, Prisoners(), p => prisoner = p,
                p => p.LabelShort + " (" + "EndIt_Torture_Resistance".Translate(p.guest?.resistance.ToString("0.0") ?? "-") + ")");
            PawnPicker(list, "EndIt_Torture_Torturer".Translate(), torturer, Torturers(), p => torturer = p,
                p => p.LabelShort + " (" + SkillDefOf.Social.LabelCap + " " + (p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0) + ")"
                     + (p.story?.traits?.HasTrait(TraitDefOf.Bloodlust) == true ? ", " + TraitDefOf.Bloodlust.degreeDatas[0].label : ""));

            list.Gap();
            list.Label("EndIt_Torture_Method".Translate());
            foreach (TortureMethod m in TortureMethods.All)
            {
                bool available = TortureMethods.Available(m, torturer, out string why);
                Rect row = list.GetRect(26f);
                string label = TortureMethods.Label(m) + (available ? "" : " (" + why + ")");
                if (Widgets.RadioButtonLabeled(row, label, method == m, !available) && available) method = m;
                TooltipHandler.TipRegion(row, TortureMethods.Description(m));
            }

            list.Gap();
            list.Label("EndIt_Torture_Reason".Translate());
            foreach (TortureReason r in new[] { TortureReason.Intel, TortureReason.BreakWill, TortureReason.Fun })
            {
                Rect row = list.GetRect(26f);
                if (Widgets.RadioButtonLabeled(row, ("EndIt_TortureReason_" + r).Translate(), reason == r)) reason = r;
                TooltipHandler.TipRegion(row, ("EndIt_TortureReason_" + r + "_Desc").Translate());
            }
            if (reason == TortureReason.Intel && prisoner != null
                && prisoner.health.hediffSet.HasHediff(EndItDefOf.EndIt_Interrogated))
            {
                GUI.color = ColorLibrary.RedReadable;
                list.Label("EndIt_Torture_AlreadyTalked".Translate(prisoner.LabelShort));
                GUI.color = Color.white;
            }

            if (method == TortureMethod.RussianRoulette)
            {
                GUI.color = new Color(1f, 0.85f, 0.5f);
                list.Label("EndIt_TortureRoulette_Note".Translate());
                GUI.color = Color.white;
            }

            list.Gap();
            list.Label("EndIt_Torture_Hours".Translate(hours));
            hours = Mathf.RoundToInt(list.Slider(hours, 1f, 8f));
            list.CheckboxLabeled("EndIt_Torture_StopBeforeDeath".Translate(), ref stopBeforeDeath,
                "EndIt_Torture_StopBeforeDeathDesc".Translate());

            list.GapLine();
            string problem = Problem();
            if (problem != null)
            {
                GUI.color = ColorLibrary.RedReadable;
                list.Label(problem);
                GUI.color = Color.white;
            }
            if (list.ButtonText("EndIt_Torture_Begin".Translate()) && problem == null) Begin();

            list.End();
        }

        private static void PawnPicker(Listing_Standard list, string label, Pawn current, IEnumerable<Pawn> options,
            System.Action<Pawn> set, System.Func<Pawn, string> describe)
        {
            Rect row = list.GetRect(30f);
            Widgets.Label(row.LeftPart(0.3f), label);
            string button = current != null ? describe(current) : "EndIt_Torture_None".Translate().ToString();
            if (Widgets.ButtonText(row.RightPart(0.68f), button))
            {
                List<FloatMenuOption> menu = options.Select(p => new FloatMenuOption(describe(p), () => set(p))).ToList();
                if (menu.Count == 0) menu.Add(new FloatMenuOption("EndIt_Torture_None".Translate(), null));
                Find.WindowStack.Add(new FloatMenu(menu));
            }
            list.Gap(4f);
        }

        private string Problem()
        {
            if (prisoner == null || !Prisoners().Contains(prisoner)) return "EndIt_Torture_PickPrisoner".Translate();
            if (torturer == null || !Torturers().Contains(torturer)) return "EndIt_Torture_PickTorturer".Translate();
            if (!TortureMethods.Available(method, torturer, out string why)) return why;
            if (!torturer.CanReach(prisoner, PathEndMode.ClosestTouch, Danger.Deadly)
                || !torturer.CanReach(chair.parent, PathEndMode.ClosestTouch, Danger.Deadly))
                return "EndIt_Torture_NoPath".Translate(torturer.LabelShort);
            if (!torturer.CanReserve(prisoner) || !torturer.CanReserve(chair.parent))
                return "EndIt_Torture_Reserved".Translate();
            return null;
        }

        private void Begin()
        {
            TortureSession s = new TortureSession
            {
                prisoner = prisoner,
                torturer = torturer,
                method = method,
                reason = reason,
                hours = hours,
                stopBeforeDeath = stopBeforeDeath
            };
            if (!chair.Begin(s))
            {
                Messages.Message("EndIt_Torture_CouldNotStart".Translate(torturer.LabelShort), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Close();
        }
    }
}
