using System;

namespace AiOn.Levels.Crowd
{
    /// <summary>
    /// The crowd rows of the original's graphics tiers (engine/quality.js PROFILES): how many crowd members a group
    /// builds and shows, where their shadows stop, how many shoppers and shelter residents there are.
    /// Lowering the tier at run time only hides members (crowd.js apply), it never rebuilds them.
    /// </summary>
    public static class CrowdTier
    {
        public readonly struct Profile
        {
            public readonly string Name;
            public readonly int Cap;
            public readonly double Scale, ShadowM, AnimM, Shop;
            public readonly int ShelterSeats, ShelterWalk;

            public Profile(string name, int cap, double scale, double shadowM, double animM, double shop, int shelterSeats, int shelterWalk)
            {
                Name = name; Cap = cap; Scale = scale; ShadowM = shadowM; AnimM = animM; Shop = shop;
                ShelterSeats = shelterSeats; ShelterWalk = shelterWalk;
            }
        }

        public static readonly Profile UltraLow = new("ultralow", 8, 0.5, 0, 15, 0.35, 6, 1);
        public static readonly Profile Low = new("low", 12, 0.7, 12, 25, 0.5, 8, 2);
        public static readonly Profile Medium = new("medium", 20, 1, 18, 35, 0.75, 16, 3);
        public static readonly Profile High = new("high", 40, 1, 25, 45, 1, 26, 4);
        public static readonly Profile Ultra = new("ultra", 40, 1.25, 35, 60, 1, 26, 4);

        public static Profile Current { get; private set; } = High;

        /// <summary>Picks a tier by the original's name (ultralow | low | medium | high | ultra); unknown names keep high.</summary>
        public static void Set(string tier)
        {
            Current = tier switch
            {
                "ultralow" => UltraLow, "low" => Low, "medium" => Medium, "ultra" => Ultra, _ => High,
            };
        }

        /// <summary>JS Math.round (halves round up).</summary>
        public static double Round(double x) => Math.Floor(x + 0.5);

        /// <summary>quality.js share: n &gt; 0 keeps at least <paramref name="min"/>.</summary>
        public static int Share(int n, double f, int min = 1) => n > 0 ? Math.Max(min, (int)Round(n * f)) : 0;
    }
}
