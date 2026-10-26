using System.Collections.Generic;

namespace GambleChoice
{
    public static class GambleSchedule
    {
        public static Lottery[] Resolve(GambleConfig config, int trial)
        {
            var c = config.Copy();
            c.Validate();
            var options = c.Options;
            if (options == null)
            {
                var pairs = new List<int[]>();
                for (int lo = 0; lo <= 6; lo++)
                    for (int hi = lo; hi <= 6; hi++)
                        pairs.Add(new[] { lo, hi });
                options = new Lottery[c.ChoiceCount];
                double[] probs =
                {
                    .2,
                    .5,
                    .8,
                    1
                };
                for (int i = 0; i < options.Length; i++)
                {
                    int index = (trial * c.ChoiceCount + i) % pairs.Count;
                    options[i] = new Lottery
                    {
                        Id = "offer-" + i,
                        Low = pairs[index][0],
                        High = pairs[index][1],
                        PHigh = probs[(trial + i) % 4]
                    };
                }
            }

            var palette = new GambleRandom(c.Seed, trial, 0xCAFEBABEu);
            for (int i = 0; i < options.Length; i++)
                options[i].Palette = c.PaletteMode == "Fixed" ? options[i].Palette : c.PaletteMode == "Random" ? (int)(palette.Next() * 2) : (i + trial) % 2;
            if (c.ShufflePositions)
            {
                var rng = new GambleRandom(c.Seed, trial, 0x56781234u);
                for (int i = options.Length - 1; i > 0; i--)
                {
                    int j = (int)(rng.Next() * (i + 1));
                    var tmp = options[i];
                    options[i] = options[j];
                    options[j] = tmp;
                }
            }

            return options;
        }
    }
}
