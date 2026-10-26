using System.Linq;

namespace GambleChoice
{
    public static class GambleRepresentations
    {
        public static readonly string[] Names =
        {
            "Bar",
            "Pie",
            "HorizontalBar",
            "Numeric",
            "Grid",
            "Donut"
        };
        public static bool Valid(string name) => Names.Contains(name);
    }
}
