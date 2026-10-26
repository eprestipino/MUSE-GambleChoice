using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace GambleChoice
{
    public static class GambleProtocol
    {
        public static string Hash(object value)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value)))).Replace("-", "").ToLowerInvariant();
        }

        public static GambleConfig Read(string json) => GambleConfigJson.Read(json);
        public static void Validate(GambleConfig c)
        {
            c.Ledger?.RejectUnknown();
            c.Design?.RejectUnknown();
            c.Adaptive?.RejectUnknown();
            if (c.SwapRoles)
                throw new ArgumentException("SwapRoles is a schema 1 style flag; schema 2 uses explicit outcome colors and palette assignments.");
            if (c.PhysicalReward)
                throw new ArgumentException("Schema 2 settlement is virtual only; physical cash-out is not implemented.");
            if (c.Purpose != "Demonstration" && c.Purpose != "Participant")
                throw new ArgumentException("Purpose must be Demonstration or Participant.");
            if (c.Purpose == "Participant" && (string.IsNullOrWhiteSpace(c.ProtocolId) || string.IsNullOrWhiteSpace(c.QualificationReference)))
                throw new ArgumentException("Participant profiles require protocol and apparatus qualification references.");
            if (c.AmountRenderer != "TokenSlots" && c.AmountRenderer != "Numeric")
                throw new ArgumentException("Unknown reward representation.");
            if (c.GridCells < 1 || c.GridCells > 100)
                throw new ArgumentException("GridCells must be 1-100.");
            if (c.Ledger == null || c.Ledger.InitialBalance < 0 || c.Ledger.InitialBalance > 1000000 || !new[]
            {
                "Block",
                "Task"
            }.Contains(c.Ledger.Scope) || !new[]
            {
                "Stop",
                "AllowDebt"
            }.Contains(c.Ledger.InsufficientFunds) || c.Ledger.Settlement != "VirtualEndowment")
                throw new ArgumentException("Invalid explicit virtual ledger policy.");
            if (c.Design == null || !new[]
            {
                "Sequential",
                "Randomized"
            }.Contains(c.Design.Order) || !new[]
            {
                "Independent",
                "Balanced",
                "Fixed"
            }.Contains(c.Design.PositionPolicy) || !new[]
            {
                "Configured",
                "Balanced"
            }.Contains(c.Design.PalettePolicy) || c.Design.PracticeTrials < 0 || c.Design.PracticeTrials >= c.Trials)
                throw new ArgumentException("Invalid design/practice settings.");
            if (c.Design.RepresentationCycle != null && (c.Design.RepresentationCycle.Length == 0 || c.Design.RepresentationCycle.Any(r => !GambleRepresentations.Valid(r))))
                throw new ArgumentException("Invalid representation cycle.");
            if (c.Adaptive == null || c.Adaptive.Lower < -6 || c.Adaptive.Upper > 6 || c.Adaptive.Lower > c.Adaptive.Upper)
                throw new ArgumentException("Invalid certainty-equivalent bounds.");
            if ((c.Options == null ? 0 : 1) + (c.Schedule == null ? 0 : 1) + (c.TrialTable == null ? 0 : 1) != 1)
                throw new ArgumentException("Schema 2 requires exactly one of Options, Schedule or TrialTable.");
            if (c.TrialTable != null)
            {
                if (c.TrialTable.Length == 0)
                    throw new ArgumentException("TrialTable is empty.");
                foreach (var row in c.TrialTable)
                {
                    if (row == null || string.IsNullOrWhiteSpace(row.ConditionId) || row.Options == null || row.Options.Length < 2 || row.Options.Length > 6 || row.Renderer != null && !GambleRepresentations.Valid(row.Renderer))
                        throw new ArgumentException("Invalid trial-table row.");
                    row.RejectUnknown();
                    ValidateChoices(row.Options, c, row.Renderer);
                }
            }

            if (c.Options != null)
                ValidateChoices(c.Options, c);
            if (c.Schedule != null)
            {
                if (c.Schedule.Length == 0)
                    throw new ArgumentException("Schedule must not be empty.");
                foreach (var row in c.Schedule)
                    ValidateChoices(row, c);
            }
        }

        static void ValidateChoices(Lottery[] choices, GambleConfig c, string rowRenderer = null)
        {
            if (choices == null || choices.Length < 2 || choices.Length > 6)
                throw new ArgumentException("A trial needs 2-6 choices.");
            var renderers = rowRenderer != null ? new[] { rowRenderer }
                : c.Design.RepresentationCycle ?? new[] { c.Renderer };
            foreach (string renderer in renderers)
                foreach (var o in choices)
                    ValidateOption(o, c, renderer);
            if (choices.Select(o => o.Id).Distinct().Count() != choices.Length)
                throw new ArgumentException("Duplicate choice IDs within a trial.");
            if (c.Adaptive.Enabled && (choices.Length != 2 || choices.Count(o => o.Id == c.Adaptive.SureChoiceId) != 1 || choices.Count(o => o.Id == c.Adaptive.RiskyChoiceId) != 1 || c.Adaptive.SureChoiceId == c.Adaptive.RiskyChoiceId))
                throw new ArgumentException("Bisection requires exactly the named sure and risky choices.");
            if (c.Adaptive.Enabled)
            {
                var sure = choices.Single(o => o.Id == c.Adaptive.SureChoiceId);
                if (sure.Outcomes.Length != 1 || sure.Outcomes[0].Probability != 1)
                    throw new ArgumentException("The adaptive sure choice must contain exactly one certain outcome.");
            }
        }

        public static void ValidateOption(Lottery o, GambleConfig c, string renderer = null)
        {
            o?.RejectUnknown();
            if (o != null && (o.Low != 0 || o.High != 0 || o.PHigh != 0))
                throw new ArgumentException("Schema 2 uses Outcomes; remove legacy Low, High and PHigh values.");
            if (o == null || string.IsNullOrWhiteSpace(o.Id) || o.Palette < 0 || o.Palette > 1 || o.Outcomes == null || o.Outcomes.Length < 1 || o.Outcomes.Length > 6)
                throw new ArgumentException("Choice needs an ID and 1-6 explicit outcomes.");
            if (o.ProbabilityRepresentation != null && !GambleRepresentations.Valid(o.ProbabilityRepresentation) || o.RewardRepresentation != null && o.RewardRepresentation != "Numeric" && o.RewardRepresentation != "TokenSlots")
                throw new ArgumentException("Unknown per-choice representation.");
            double sum = 0;
            var ids = new HashSet<string>();
            foreach (var x in o.Outcomes)
            {
                if (x == null || string.IsNullOrWhiteSpace(x.Id) || !ids.Add(x.Id) || x.TokenDelta < -6 || x.TokenDelta > 6 || double.IsNaN(x.Probability) || double.IsInfinity(x.Probability) || x.Probability < 0 || x.Probability > 1)
                    throw new ArgumentException("Outcomes require unique IDs, integer deltas -6..6, and probabilities [0,1].");
                x.RejectUnknown();
                if (x.Color != null && !System.Text.RegularExpressions.Regex.IsMatch(x.Color, "^#[0-9a-fA-F]{6}$"))
                    throw new ArgumentException("Outcome colors must be #RRGGBB.");
                if ((o.ProbabilityRepresentation ?? renderer ?? c.Renderer) == "Grid" && Math.Abs(x.Probability * c.GridCells - Math.Round(x.Probability * c.GridCells)) > 1e-8)
                    throw new ArgumentException("Grid probability is not exactly representable; change GridCells or renderer.");
                sum += x.Probability;
            }

            if (Math.Abs(sum - 1) > 1e-9)
                throw new ArgumentException("Outcome probabilities must sum to one; they are never silently normalized.");
        }

        public static GamblePlannedTrial Resolve(GambleConfig raw, int trial)
        {
            if (raw == null || raw.SchemaVersion != 2)
                throw new ArgumentException("Protocol resolution requires schema 2.");
            return new GamblePreparedProtocol(raw).Resolve(trial, trial);
        }

        internal static GamblePlannedTrial ResolveValidated(GambleConfig raw, int trial, int[] order)
        {
            var c = raw.CopySettings();
            var d = c.Design;
            int rows = raw.TrialTable?.Length ?? raw.Schedule?.Length ?? 1;
            bool practice = trial < d.PracticeTrials;
            int index = practice ? 0 : trial - d.PracticeTrials;
            int cycle = practice ? trial : index / rows, row = practice ? 0 : index % rows;
            if (d.Order == "Randomized" && !practice)
            {
                row = order[row];
            }

            var plan = raw.TrialTable == null ? new GamblePlannedTrial
            {
                ConditionId = "condition-" + row,
                Options = raw.Schedule == null ? raw.Options : raw.Schedule[row]
            }

            : raw.TrialTable[row];
            c.Options = GamblePreparedProtocol.CloneOptions(plan.Options);
            c.ChoiceCount = c.Options.Length;
            c.Schedule = null;
            c.TrialTable = null;
            if (plan.Renderer != null)
                c.Renderer = plan.Renderer;
            else if (d.RepresentationCycle != null)
                c.Renderer = d.RepresentationCycle[(cycle / c.ChoiceCount) % d.RepresentationCycle.Length];
            if (d.PositionPolicy == "Balanced")
            {
                var original = c.Options.ToArray();
                var rng = new GambleRandom(c.Seed, row, 0xBB881133);
                for (int i = original.Length - 1; i > 0; i--)
                {
                    int j = (int)(rng.Next() * (i + 1));
                    var t = original[i];
                    original[i] = original[j];
                    original[j] = t;
                }

                c.Options = Enumerable.Range(0, original.Length).Select(i => original[(i + cycle) % original.Length]).ToArray();
                c.ShufflePositions = false;
            }
            else if (d.PositionPolicy == "Fixed")
                c.ShufflePositions = false;
            if (d.PalettePolicy == "Balanced")
            {
                for (int i = 0; i < c.Options.Length; i++)
                    c.Options[i].Palette = (cycle + Array.FindIndex(plan.Options, x => x.Id == c.Options[i].Id)) % 2;
                c.PaletteMode = "Fixed";
            }

            foreach (var o in c.Options)
            {
                o.ProbabilityRepresentation = o.ProbabilityRepresentation ?? c.Renderer;
                o.RewardRepresentation = o.RewardRepresentation ?? c.AmountRenderer;
                ValidateOption(o, c);
            }

            return new GamblePlannedTrial
            {
                ConditionId = plan.ConditionId,
                Practice = practice || plan.Practice,
                Options = c.Options,
                Config = c
            };
        }

        public static GamblePlannedTrial[] Compile(GambleConfig config)
        {
            if (config == null || config.SchemaVersion != 2)
                throw new ArgumentException("Compilation requires schema 2.");
            var prepared = new GamblePreparedProtocol(config);
            return Enumerable.Range(0, config.Trials).Select(i =>
            {
                var p = prepared.Resolve(i, i);
                p.Options = GambleSchedule.Resolve(p.Config, i);
                var adaptive = new GambleAdaptiveController();
                adaptive.Prepare(config.Adaptive, 0);
                adaptive.Apply(p.Options);
                return p;
            }).ToArray();
        }
    }
}
