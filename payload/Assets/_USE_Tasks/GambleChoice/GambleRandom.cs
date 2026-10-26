
namespace GambleChoice
{
    // Versioned xorshift32 stream. uint / 2^32 gives [0,1), including neither a hidden third outcome nor an endpoint failure.
    public sealed class GambleRandom
    {
        public const string Algorithm = "xorshift32-v1";
        uint state;
        public GambleRandom(int seed, int trial, uint stream)
        {
            state = unchecked((uint)seed ^ ((uint)trial * 0x9E3779B9u) ^ stream);
            if (state == 0)
                state = 0xA341316Cu;
        }

        public double Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state / 4294967296.0;
        }
    }
}
