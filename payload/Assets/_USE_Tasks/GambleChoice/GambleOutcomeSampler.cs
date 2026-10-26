using System;

namespace GambleChoice
{
    public static class GambleOutcomeSampler
    {
        public static int Select(GambleOutcome[] outcomes, double draw)
        {
            if (double.IsNaN(draw) || draw < 0 || draw >= 1)
                throw new ArgumentException("Draw must be in [0,1).");
            double cumulative = 0;
            int last = -1;
            for (int i = 0; i < outcomes.Length; i++)
            {
                if (outcomes[i].Probability > 0)
                    last = i;
                cumulative += outcomes[i].Probability;
                if (draw < cumulative)
                    return i;
            }

            if (last >= 0 && Math.Abs(cumulative - 1) <= 1e-9)
                return last;
            throw new ArgumentException("Probabilities must sum to one.");
        }
    }
}
