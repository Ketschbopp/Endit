using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace EndIt
{
    /// <summary>Opened from the brainwashing station: pick the subject, who straps them in, and what to change.</summary>
    public class Dialog_Brainwash : Window
    {
        private readonly CompBrainwasher station;
        private Pawn subject;
        private Pawn operatorPawn;
        private bool convertIdeo;
        private Ideo targetIdeo;
        private bool removeLoyalty;

        public override Vector2 InitialSize => new Vector2(540f, 460f);

        public Dialog_Brainwash(CompBrainwasher station)
        {
            this.station = station;
            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = true;
            subject = Subjects().FirstOrDefault();
            operatorPawn = Operators().FirstOrDefault();
            targetIdeo = Faction.OfPlayer.ideos?.PrimaryIdeo;
            convertIdeo = IdeologyActive;
            removeLoyalty = CanRemoveLoyalty(subject);
        }

        private Map Map => station.parent.Map;
        private static bool IdeologyActive => ModsConfig.IdeologyActive && Find.IdeoManager != null && !Find.IdeoManager.classicMode;

        private IEnumerable<Pawn> Subjects()
        {
            return Map.mapPawns.AllPawnsSpawned
                .Where(p => !p.Dead && p.RaceProps.Humanlike && (p.IsPrisonerOfColony || p.IsSlaveOfColony && EndItMod.Settings.brainwashSlaves)
                            && !CompBrainwasher.IsInSession(p) && !CompTortureChair.IsBeingTortured(p));
        }

        private IEnumerable<Pawn> Operators()
        {
            return Map.mapPawns.FreeColonistsSpawned.Where(p => !p.Dead && !p.Downed && !p.InMentalState
                                                                && p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation));
        }

        private static bool CanRemoveLoyalty(Pawn p) => p != null && p.IsPrisonerOfColony && p.guest != null && !p.guest.Recruitable;

        private static string Describe(Pawn p)
        {
            string status = p.IsSlaveOfColony ? "EndIt_Brainwash_Slave".Translate().ToString() : "EndIt_Brainwash_Prisoner".Translate().ToString();
            string ideo = p.ideo?.Ideo != null && ModsConfig.IdeologyActive ? ", " + p.ideo.Ideo.name : "";
            string loyal = CanRemoveLoyalty(p) ? ", " + "EndIt_Brainwash_Loyal".Translate() : "";
            return p.LabelShort + " (" + status + ideo + loyal + ")";
        }

        public override void DoWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);

            Text.Font = GameFont.Medium;
            list.Label("EndIt_Brainwash_Title".Translate());
            Text.Font = GameFont.Small;
            list.GapLine();

            Picker(list, "EndIt_Brainwash_Subject".Translate(), subject != null ? Describe(subject) : null,
                Subjects().Select(p => new FloatMenuOption(Describe(p), () =>
                {
                    subject = p;
                    removeLoyalty = CanRemoveLoyalty(p);
                })));
            Picker(list, "EndIt_Brainwash_Operator".Translate(), operatorPawn?.LabelShort,
                Operators().Select(p => new FloatMenuOption(p.LabelShort, () => operatorPawn = p)));

            list.Gap();
            list.Label("EndIt_Brainwash_Goals".Translate());
            if (IdeologyActive)
            {
                list.CheckboxLabeled("EndIt_Brainwash_ConvertIdeo".Translate(), ref convertIdeo);
                if (convertIdeo)
                    Picker(list, "   " + "EndIt_Brainwash_TargetIdeo".Translate(), targetIdeo?.name,
                        Find.IdeoManager.IdeosListForReading.Select(i => new FloatMenuOption(i.name, () => targetIdeo = i)));
            }
            else
            {
                GUI.color = Color.gray;
                list.Label("EndIt_Brainwash_NoIdeology".Translate());
                GUI.color = Color.white;
                convertIdeo = false;
            }

            bool loyalPossible = CanRemoveLoyalty(subject);
            if (loyalPossible)
            {
                list.CheckboxLabeled("EndIt_Brainwash_RemoveLoyalty".Translate(), ref removeLoyalty);
            }
            else
            {
                GUI.color = Color.gray;
                list.Label("EndIt_Brainwash_NotLoyal".Translate());
                GUI.color = Color.white;
                removeLoyalty = false;
            }

            list.Gap();
            GUI.color = new Color(1f, 0.85f, 0.5f);
            list.Label("EndIt_Brainwash_Warning".Translate((EndItMod.Settings.suicideChanceBrainwash).ToStringPercent()));
            GUI.color = Color.white;

            list.GapLine();
            string problem = Problem();
            if (problem != null)
            {
                GUI.color = ColorLibrary.RedReadable;
                list.Label(problem);
                GUI.color = Color.white;
            }
            if (list.ButtonText("EndIt_Brainwash_Begin".Translate()) && problem == null) Begin();
            list.End();
        }

        private static void Picker(Listing_Standard list, string label, string current, IEnumerable<FloatMenuOption> options)
        {
            Rect row = list.GetRect(30f);
            Widgets.Label(row.LeftPart(0.3f), label);
            if (Widgets.ButtonText(row.RightPart(0.68f), current ?? "EndIt_Torture_None".Translate().ToString()))
            {
                List<FloatMenuOption> menu = options.ToList();
                if (menu.Count == 0) menu.Add(new FloatMenuOption("EndIt_Torture_None".Translate(), null));
                Find.WindowStack.Add(new FloatMenu(menu));
            }
            list.Gap(4f);
        }

        private string Problem()
        {
            if (subject == null || !Subjects().Contains(subject)) return "EndIt_Brainwash_PickSubject".Translate();
            if (operatorPawn == null || !Operators().Contains(operatorPawn)) return "EndIt_Brainwash_PickOperator".Translate();
            if (!convertIdeo && !removeLoyalty) return "EndIt_Brainwash_PickGoal".Translate();
            if (convertIdeo && targetIdeo == null) return "EndIt_Brainwash_PickIdeo".Translate();
            if (convertIdeo && !removeLoyalty && subject.ideo?.Ideo == targetIdeo)
                return "EndIt_Brainwash_AlreadyIdeo".Translate(subject.LabelShort, targetIdeo.name);
            if (!operatorPawn.CanReach(subject, PathEndMode.ClosestTouch, Danger.Deadly)
                || !operatorPawn.CanReach(station.ChairCell, PathEndMode.ClosestTouch, Danger.Deadly))
                return "EndIt_Torture_NoPath".Translate(operatorPawn.LabelShort);
            if (!operatorPawn.CanReserve(subject) || !operatorPawn.CanReserve(station.parent))
                return "EndIt_Torture_Reserved".Translate();
            if (station.ChairCell.GetThingList(Map).Any(t => t is Pawn))
                return "EndIt_Brainwash_Occupied".Translate();
            return null;
        }

        private void Begin()
        {
            BrainwashSession s = new BrainwashSession
            {
                subject = subject,
                operatorPawn = operatorPawn,
                convertIdeo = convertIdeo,
                targetIdeo = convertIdeo ? targetIdeo : null,
                removeLoyalty = removeLoyalty
            };
            if (!station.Begin(s))
            {
                Messages.Message("EndIt_Torture_CouldNotStart".Translate(operatorPawn.LabelShort), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Close();
        }
    }
}
