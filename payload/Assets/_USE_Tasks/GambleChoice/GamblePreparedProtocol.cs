using System;
using System.Linq;
using Newtonsoft.Json;

namespace GambleChoice
{
    // Block-owned immutable source snapshot; only the current row is copied per trial.
    public sealed class GamblePreparedProtocol
    {
        readonly GambleConfig source;
        int orderCycle = -1;
        int[] order;
        public bool PhysicalReward => source.PhysicalReward;

        public GamblePreparedProtocol(GambleConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            config.Validate();
            source = config.Copy();
            source.Validate();
        }

        public GamblePlannedTrial Resolve(int planTrial, int taskTrial)
        {
            if (planTrial < 0 || taskTrial < 0)
                throw new ArgumentException("Trial indices must be nonnegative.");
            if (source.SchemaVersion == 2)
            {
                int rows = source.TrialTable?.Length ?? source.Schedule?.Length ?? 1;
                int index = planTrial - source.Design.PracticeTrials;
                if (source.Design.Order == "Randomized" && index >= 0)
                {
                    int cycle = index / rows;
                    if (order == null || cycle != orderCycle)
                    {
                        orderCycle = cycle;
                        order = Enumerable.Range(0, rows).ToArray();
                        var random = new GambleRandom(source.Seed, cycle, 0xBEDAC0DE);
                        for (int i = rows - 1; i > 0; i--)
                        {
                            int j = (int)(random.Next() * (i + 1));
                            int value = order[i];
                            order[i] = order[j];
                            order[j] = value;
                        }
                    }
                }
                return GambleProtocol.ResolveValidated(source, planTrial, order);
            }

            var config = source.CopySettings();
            var options = source.Schedule == null ? source.Options : source.Schedule[taskTrial % source.Schedule.Length];
            config.Options = CloneOptions(options);
            if (source.Schedule != null)
                config.ChoiceCount = config.Options.Length;
            return new GamblePlannedTrial { Config = config, Options = config.Options };
        }

        internal static Lottery[] CloneOptions(Lottery[] options)
        {
            return options == null ? null : JsonConvert.DeserializeObject<Lottery[]>(JsonConvert.SerializeObject(options));
        }
    }
}
