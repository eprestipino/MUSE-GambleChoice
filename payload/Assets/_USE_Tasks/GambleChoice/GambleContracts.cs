using System;
using Newtonsoft.Json;

namespace GambleChoice
{
    [Serializable]
    public abstract class GambleStrict
    {
        [JsonExtensionData]
        System.Collections.Generic.IDictionary<string, Newtonsoft.Json.Linq.JToken> unknown;
        [System.Runtime.Serialization.OnDeserialized]
        void ClearDerived(System.Runtime.Serialization.StreamingContext context)
        {
            if (this is Lottery && unknown != null)
            {
                unknown.Remove("EV");
                unknown.Remove("Variance");
            }
        }

        public void RejectUnknown()
        {
            // Historical JSON contains these read-only derived summaries. Never let them
            // override economics or re-emit duplicate properties after a deep copy.
            if (this is Lottery && unknown != null)
            {
                unknown.Remove("EV");
                unknown.Remove("Variance");
            }

            if (unknown != null && unknown.Count > 0)
                throw new ArgumentException("Unknown " + GetType().Name + " fields: " + string.Join(", ", unknown.Keys));
        }
    }

    [Serializable]
    public class GambleOutcome : GambleStrict
    {
        public string Id;
        public int TokenDelta;
        public double Probability;
        public string Color;
    }

    [Serializable]
    public class GamblePlannedTrial : GambleStrict
    {
        public string ConditionId = "condition", Renderer;
        public bool Practice;
        public Lottery[] Options;
        [JsonIgnore]
        public GambleConfig Config;
    }

    [Serializable]
    public class GambleDesignSettings : GambleStrict
    {
        public string Order = "Sequential", PositionPolicy = "Independent", PalettePolicy = "Configured";
        public int PracticeTrials;
        public string[] RepresentationCycle;
    }

    [Serializable]
    public class GambleLedgerSettings : GambleStrict
    {
        public string Scope = "Block", InsufficientFunds = "Stop", Settlement = "VirtualEndowment";
        public int InitialBalance = 24;
    }

    [Serializable]
    public class GambleAdaptiveSettings : GambleStrict
    {
        public bool Enabled;
        public string SureChoiceId = "sure", RiskyChoiceId = "risky";
        public int Lower = 0, Upper = 6;
    }

    [Serializable]
    public class GambleAdaptiveSnapshot
    {
        public bool Enabled;
        public int Lower, Upper, Offer, ValidChoices;
        public string Procedure = "integer-bisection-v1";
    }
}
