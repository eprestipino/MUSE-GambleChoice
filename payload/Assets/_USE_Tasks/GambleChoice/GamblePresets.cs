using System;
using System.Linq;

namespace GambleChoice
{
    public static class GamblePresets
    {
        public static GambleConfig Create(string kind, int choices = 3, string renderer = "HorizontalBar")
        {
            if (!new[]
            {
                "Gain",
                "Loss",
                "Mixed",
                "CertaintyEquivalent"
            }.Contains(kind))
                throw new ArgumentException("Unknown preset.");
            var c = new GambleConfig
            {
                SchemaVersion = 2,
                ChoiceCount = choices,
                Renderer = renderer,
                Trials = 24
            };
            c.Design.PositionPolicy = "Balanced";
            c.Design.PalettePolicy = "Balanced";
            c.Options = Enumerable.Range(0, choices).Select(i => new Lottery { Id = "choice-" + (i + 1), Outcomes = new[] { new GambleOutcome { Id = "loss", TokenDelta = kind == "Gain" ? 0 : -1 - i % 3, Probability = .25 }, new GambleOutcome { Id = "zero", TokenDelta = 0, Probability = .25 }, new GambleOutcome { Id = "gain", TokenDelta = kind == "Loss" ? 0 : 2 + i % 5, Probability = .5 } } }).ToArray();
            if (kind == "CertaintyEquivalent")
            {
                c.ChoiceCount = 2;
                c.Options = new[]
                {
                    new Lottery
                    {
                        Id = "sure",
                        Outcomes = new[]
                        {
                            new GambleOutcome
                            {
                                Id = "certain",
                                TokenDelta = 3,
                                Probability = 1
                            }
                        }
                    },
                    new Lottery
                    {
                        Id = "risky",
                        Outcomes = new[]
                        {
                            new GambleOutcome
                            {
                                Id = "zero",
                                TokenDelta = 0,
                                Probability = .5
                            },
                            new GambleOutcome
                            {
                                Id = "gain",
                                TokenDelta = 6,
                                Probability = .5
                            }
                        }
                    }
                };
                c.Adaptive.Enabled = true;
            }

            c.Validate();
            return c;
        }
    }
}
