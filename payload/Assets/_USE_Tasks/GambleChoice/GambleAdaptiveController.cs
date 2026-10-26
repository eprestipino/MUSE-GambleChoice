using System.Linq;

namespace GambleChoice
{
    public sealed class GambleAdaptiveController
    {
        GambleAdaptiveSettings settings;
        string key;
        int blockKey, lower, upper, offer, count;
        public void Prepare(GambleAdaptiveSettings config, int block)
        {
            string next = GambleProtocol.Hash(config);
            if (key != next || blockKey != block)
            {
                lower = config.Lower;
                upper = config.Upper;
                offer = (lower + upper) / 2;
                count = 0;
            }

            key = next;
            blockKey = block;
            settings = config;
        }

        public void Apply(Lottery[] choices)
        {
            if (!settings.Enabled)
                return;
            var sure = choices.Single(o => o.Id == settings.SureChoiceId);
            var original = sure.Outcomes[0];
            sure.Outcomes = new[]
            {
                new GambleOutcome
                {
                    Id = original.Id,
                    Color = original.Color,
                    TokenDelta = offer,
                    Probability = 1
                }
            };
        }

        public void Observe(string chosen)
        {
            if (!settings.Enabled)
                return;
            if (chosen == settings.SureChoiceId)
                upper = offer;
            else if (chosen == settings.RiskyChoiceId)
                lower = offer;
            else
                return;
            count++;
            offer = (lower + upper) / 2;
        }

        public GambleAdaptiveSnapshot Snapshot() => new GambleAdaptiveSnapshot
        {
            Enabled = settings.Enabled,
            Lower = lower,
            Upper = upper,
            Offer = offer,
            ValidChoices = count
        };
    }
}
