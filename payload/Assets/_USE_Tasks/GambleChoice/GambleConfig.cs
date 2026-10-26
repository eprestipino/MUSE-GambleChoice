using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GambleChoice
{
    [Serializable, JsonConverter(typeof(GambleConfigJson))]
    public class GambleConfig : GambleStrict
    {
        public int SchemaVersion = 1, Seed = 81273, ChoiceCount = 2, Trials = 24;
        public string Renderer = "Bar", PaletteMode = "Alternating", ItiMode = "Static";
        public bool ShufflePositions = true, SwapRoles = false, ShowLabels = false;
        public bool PhysicalReward = false;
        public string CalibrationId = "";
        public int PulsesPerToken = 1, PulseTicks = 500;
        public double HoldMs = 300, DeadlineMs = 3000, RevealMs = 3000, HighlightMs = 400, TransferMs = 800, RewardWindowMs = 1800, ItiMs = 500, ItiReferenceMs = 8000;
        public string Background = "#17212B", ActualizedColor = "#EEC75E", ItiColor = "#49B8B0", IdleColor = "#526171", HoldColor = "#A6C8D4", CommittedColor = "#E8EDEE";
        public string[] LowColors =
        {
            "#C48B55",
            "#77AA77"
        };
        public string[] HighColors =
        {
            "#649ECC",
            "#AD85C4"
        };
        public Lottery[] Options; // Optional explicit ordered lotteries; otherwise the deterministic catalog schedule is used.
        public Lottery[][] Schedule; // Optional explicit trial table, repeated if the block is longer.
        public GambleGeometry Geometry = new GambleGeometry();
        public string AmountRenderer = "TokenSlots", Purpose = "Demonstration", ProtocolId = "", QualificationReference = "";
        public int GridCells = 20;
        public GamblePlannedTrial[] TrialTable;
        public GambleDesignSettings Design = new GambleDesignSettings();
        public GambleLedgerSettings Ledger = new GambleLedgerSettings();
        public GambleAdaptiveSettings Adaptive = new GambleAdaptiveSettings();
        public GambleConfig Copy() => JsonConvert.DeserializeObject<GambleConfig>(JsonConvert.SerializeObject(this));
        internal GambleConfig CopySettings()
        {
            return JsonConvert.DeserializeObject<GambleConfig>(JsonConvert.SerializeObject(this,
                GambleConfigJson.SettingsWithoutTrialSources));
        }
        public void Validate()
        {
            RejectUnknown();
            if (SchemaVersion == 1 && TrialTable != null)
                throw new ArgumentException("TrialTable requires schema 2.");
            if (SchemaVersion == 1 && Options != null && Schedule != null)
                throw new ArgumentException("Use Options or Schedule, not both.");
            if (Geometry == null)
                throw new ArgumentException("Geometry is required.");
            Geometry.Validate();
            if (SchemaVersion == 1)
                ValidateSchedule();
            if ((SchemaVersion != 1 && SchemaVersion != 2) || ChoiceCount < 2 || ChoiceCount > (SchemaVersion == 1 ? 4 : 6) || Trials < 1 || Trials > 100000)
                throw new ArgumentException("Invalid schema, supported choice count, or trial count.");
            if (SchemaVersion == 1 ? Renderer != "Bar" && Renderer != "Pie" : !GambleRepresentations.Valid(Renderer))
                throw new ArgumentException("Unknown probability representation.");
            if (SchemaVersion == 2)
                GambleProtocol.Validate(this);
            if (PaletteMode != "Fixed" && PaletteMode != "Alternating" && PaletteMode != "Random")
                throw new ArgumentException("Unknown palette mode.");
            if (ItiMode != "Static" && ItiMode != "Remaining" && ItiMode != "Elapsed")
                throw new ArgumentException("Unknown ITI mode.");
            foreach (double v in new[]
            {
                HoldMs,
                DeadlineMs,
                RevealMs,
                HighlightMs,
                TransferMs,
                RewardWindowMs,
                ItiMs,
                ItiReferenceMs
            }

            )
                if (double.IsNaN(v) || double.IsInfinity(v) || v < 0 || v > 3600000)
                    throw new ArgumentException("Timings must be finite nonnegative milliseconds.");
            if (HoldMs <= 0 || DeadlineMs < HoldMs || RevealMs < DeadlineMs || TransferMs <= 0 || HighlightMs <= 0 || ItiReferenceMs <= 0 || ItiMs > ItiReferenceMs)
                throw new ArgumentException("Require 0 < hold <= deadline <= reveal, positive animation durations, ITI <= reference.");
            if (PulsesPerToken < 1 || PulsesPerToken > 100 || PulseTicks < 250 || PulseTicks > 10000)
                throw new ArgumentException("Invalid pulse mapping (ticks are 0.1 ms).");
            double maxTrain = 6 * PulsesPerToken * PulseTicks / 10.0 + (6 * PulsesPerToken - 1) * 200;
            if (PhysicalReward && (string.IsNullOrWhiteSpace(CalibrationId) || RewardWindowMs < maxTrain + 100))
                throw new ArgumentException("Physical rewards require calibration ID and a window covering the maximum pulse train plus 100 ms.");
            if (LowColors == null || HighColors == null || LowColors.Length != 2 || HighColors.Length != 2)
                throw new ArgumentException("Exactly two palette pairs required.");
            var colors = new List<string>(LowColors);
            colors.AddRange(HighColors);
            colors.AddRange(new[] { Background, ActualizedColor, ItiColor, IdleColor, HoldColor, CommittedColor });
            foreach (var c in colors)
                if (c == null || !System.Text.RegularExpressions.Regex.IsMatch(c, "^#[0-9A-Fa-f]{6}$"))
                    throw new ArgumentException("Colors must be #RRGGBB.");
            if (Options != null)
            {
                if (Options.Length != ChoiceCount)
                    throw new ArgumentException("Options count differs from ChoiceCount.");
                var ids = new HashSet<string>();
                foreach (var o in Options)
                {
                    if (SchemaVersion == 1)
                        ValidateOption(o);
                    if (!ids.Add(o.Id))
                        throw new ArgumentException("Duplicate option IDs.");
                }
            }
        }

        public void ValidateSchedule()
        {
            if (Schedule == null)
                return;
            if (Schedule.Length == 0)
                throw new ArgumentException("Schedule must not be empty.");
            foreach (var row in Schedule)
            {
                if (row == null || row.Length < 2 || row.Length > (SchemaVersion == 1 ? 4 : 6))
                    throw new ArgumentException("Invalid schedule choice count.");
                var ids = new HashSet<string>();
                foreach (var o in row)
                {
                    ValidateOption(o);
                    if (!ids.Add(o.Id))
                        throw new ArgumentException("Duplicate schedule option ID.");
                }
            }
        }

        public void ValidateOption(Lottery option)
        {
            if (SchemaVersion == 1)
            {
                ValidateLottery(option);
                if (option.Outcomes != null)
                    throw new ArgumentException("Outcome lists require schema 2.");
            }
            else
                GambleProtocol.ValidateOption(option, this);
        }

        public static void ValidateLottery(Lottery o)
        {
            o?.RejectUnknown();
            if (o == null || string.IsNullOrWhiteSpace(o.Id) || o.Low < 0 || o.High > 6 || o.Low > o.High || double.IsNaN(o.PHigh) || double.IsInfinity(o.PHigh) || o.PHigh < 0 || o.PHigh > 1 || o.Palette < 0 || o.Palette > 1)
                throw new ArgumentException("Lottery requires unique ID, 0 <= low <= high <= 6, probability [0,1], palette 0/1.");
        }
    }
}
