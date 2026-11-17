namespace AiOn.Game
{
    /// <summary>
    /// mulberry32 PRNG, bit-for-bit the same as the original engine/kit.js and game/state.js rng,
    /// so a seed gives the same situation in Unity as in the web build.
    /// </summary>
    public sealed class Mulberry32
    {
        uint _a;

        public Mulberry32(uint seed) => _a = seed;

        /// <summary>Next value in [0, 1).</summary>
        public double Next()
        {
            unchecked
            {
                _a += 0x6D2B79F5u;
                uint t = _a;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }
}
