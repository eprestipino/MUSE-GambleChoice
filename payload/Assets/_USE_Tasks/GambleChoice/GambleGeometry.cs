using System;

namespace GambleChoice
{
    [Serializable]
    public class GambleGeometry : GambleStrict
    {
        public float TwoChoiceScale = 1, MultiChoiceScale = .7f, BarWidth = 60, BarHeight = 260, TokenRadius = 15, TokenSpacing = 46, TopTokenRadius = 26, ButtonWidth = 180, ButtonHeight = 90;
        public void Validate()
        {
            RejectUnknown();
            foreach (float v in new[]
            {
                TwoChoiceScale,
                MultiChoiceScale,
                BarWidth,
                BarHeight,
                TokenRadius,
                TokenSpacing,
                TopTokenRadius,
                ButtonWidth,
                ButtonHeight
            }

            )
                if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0)
                    throw new ArgumentException("Geometry must be finite and positive.");
            if (TwoChoiceScale > 1.15f || MultiChoiceScale > .8f || BarWidth > 100 || BarHeight > 280 || TokenRadius > 20 || TokenSpacing < 2 * TokenRadius || TokenSpacing > 48 || TopTokenRadius > 40 || ButtonWidth > 250 || ButtonHeight > 110)
                throw new ArgumentException("Geometry exceeds the supported nonoverlapping layout bounds.");
        }
    }
}
