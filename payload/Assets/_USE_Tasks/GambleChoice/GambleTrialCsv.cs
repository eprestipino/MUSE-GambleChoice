using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GambleChoice
{
    public static class GambleTrialCsv
    {
        static readonly string[] Header =
        {
            "Trial",
            "Condition",
            "Practice",
            "ChoiceId",
            "OutcomeId",
            "TokenDelta",
            "Probability",
            "ProbabilityRepresentation",
            "RewardRepresentation",
            "Palette",
            "Color"
        };
        static string Q(object s) => "\"" + Convert.ToString(s, CultureInfo.InvariantCulture).Replace("\"", "\"\"") + "\"";
        public static string Export(GamblePlannedTrial[] trials)
        {
            var b = new StringBuilder(string.Join(",", Header) + "\n");
            for (int t = 0; t < trials.Length; t++)
                foreach (var o in trials[t].Options)
                    foreach (var x in o.Outcomes)
                        b.AppendLine(string.Join(",", new object[] { t, trials[t].ConditionId, trials[t].Practice, o.Id, x.Id, x.TokenDelta, x.Probability.ToString("R", CultureInfo.InvariantCulture), o.ProbabilityRepresentation, o.RewardRepresentation, o.Palette, x.Color }.Select(Q)));
            return b.ToString();
        }

        public static List<string[]> Parse(string text)
        {
            var result = new List<string[]>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false, closed = false, start = true;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                            closed = true;
                        }
                    }
                    else
                        cell.Append(ch);
                    continue;
                }

                if (ch == '"')
                {
                    if (!start)
                        throw new FormatException("Unexpected quote in CSV.");
                    quoted = true;
                    start = false;
                    continue;
                }

                if (ch == ',' || ch == '\r' || ch == '\n')
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    start = true;
                    closed = false;
                    if (ch != ',')
                    {
                        if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                            i++;
                        result.Add(row.ToArray());
                        row.Clear();
                    }

                    continue;
                }

                if (closed)
                    throw new FormatException("Text after closing CSV quote.");
                cell.Append(ch);
                start = false;
            }

            if (quoted)
                throw new FormatException("Unclosed CSV quote.");
            if (!start || row.Count > 0)
            {
                row.Add(cell.ToString());
                result.Add(row.ToArray());
            }

            return result;
        }

        public static GambleConfig Import(GambleConfig basis, string text)
        {
            var rows = Parse(text.TrimStart('\ufeff'));
            if (rows.Count < 2 || !rows[0].SequenceEqual(Header))
                throw new FormatException("CSV header must be: " + string.Join(",", Header));
            var trials = new List<GamblePlannedTrial>();
            var choices = new List<Lottery>();
            var outcomes = new List<GambleOutcome>();
            Lottery current = null;
            int previousTrial = -1;
            var seen = new HashSet<string>();
            for (int line = 1; line < rows.Count; line++)
            {
                var a = rows[line];
                if (a.Length != Header.Length)
                    throw new FormatException("CSV column count at row " + (line + 1));
                int trial = int.Parse(a[0], CultureInfo.InvariantCulture);
                if (trial < 0)
                    throw new FormatException("Trial numbers must start at zero.");
                bool practice = bool.Parse(a[2]);
                int palette = int.Parse(a[9], CultureInfo.InvariantCulture);
                if (trial != previousTrial)
                {
                    if (trial != previousTrial + 1)
                        throw new FormatException("Trial numbers must be contiguous, starting at zero.");
                    trials.Add(new GamblePlannedTrial { ConditionId = a[1], Practice = practice, Options = new Lottery[0] });
                    choices = new List<Lottery>();
                    seen.Clear();
                    current = null;
                    previousTrial = trial;
                }

                var plan = trials[trial];
                if (plan.ConditionId != a[1] || plan.Practice != practice)
                    throw new FormatException("Conflicting condition/practice metadata.");
                if (current == null || current.Id != a[3])
                {
                    if (!seen.Add(a[3]))
                        throw new FormatException("Choice rows must be contiguous and IDs unique.");
                    current = new Lottery
                    {
                        Id = a[3],
                        ProbabilityRepresentation = string.IsNullOrEmpty(a[7]) ? null : a[7],
                        RewardRepresentation = string.IsNullOrEmpty(a[8]) ? null : a[8],
                        Palette = palette
                    };
                    choices.Add(current);
                    outcomes = new List<GambleOutcome>();
                }

                if ((current.ProbabilityRepresentation ?? "") != a[7] || (current.RewardRepresentation ?? "") != a[8] || current.Palette != palette)
                    throw new FormatException("Conflicting per-choice metadata.");
                outcomes.Add(new GambleOutcome { Id = a[4], TokenDelta = int.Parse(a[5], CultureInfo.InvariantCulture), Probability = double.Parse(a[6], CultureInfo.InvariantCulture), Color = string.IsNullOrEmpty(a[10]) ? null : a[10] });
                current.Outcomes = outcomes.ToArray();
                plan.Options = choices.ToArray();
            }

            var c = basis.Copy();
            c.SchemaVersion = 2;
            c.Options = null;
            c.Schedule = null;
            c.TrialTable = trials.ToArray();
            c.ChoiceCount = c.TrialTable[0].Options.Length;
            c.Trials = trials.Count;
            c.Design.PracticeTrials = 0;
            c.Design.Order = "Sequential";
            c.Design.PositionPolicy = "Fixed";
            c.Design.PalettePolicy = "Configured";
            c.PaletteMode = "Fixed";
            c.ShufflePositions = false;
            c.Design.RepresentationCycle = null;
            c.Validate();
            return c;
        }
    }
}
