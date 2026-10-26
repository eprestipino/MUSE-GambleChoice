using System;
using System.Linq;

namespace GambleChoice
{
    [Serializable]
    public class Lottery : GambleStrict
    {
        public string Id;
        public int Low, High;
        public double PHigh;
        public int Palette;
        public GambleOutcome[] Outcomes;
        public string ProbabilityRepresentation, RewardRepresentation;
        public double EV => Outcomes == null ? Low + PHigh * (High - Low) : Outcomes.Sum(o => o.Probability * o.TokenDelta);
        public double Variance => Outcomes == null ? PHigh * (1 - PHigh) * (High - Low) * (High - Low) : Outcomes.Sum(o => o.Probability * Math.Pow(o.TokenDelta - EV, 2));
    }
}
