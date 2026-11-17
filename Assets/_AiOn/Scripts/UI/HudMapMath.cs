using System;
using AiOn.Levels;

namespace AiOn.UI
{
    /// <summary>
    /// The arithmetic behind the HUD's compass strip and north-up minimap (original ui/hud.js setCompass / setMap),
    /// kept free of uGUI so it can be tested. Headings are degrees clockwise from north (0 = N, 90 = E); map positions
    /// are in the original frame (x east, z south) and come out as UI offsets from the map centre (y up, north up).
    /// </summary>
    public static class HudMapMath
    {
        /// <summary>Compass pixels per degree in the original's 360 px strip.</summary>
        public const double CompassPxPerDeg = 2.2;
        /// <summary>Degrees either side of the heading the strip shows.</summary>
        public const int CompassSpan = 80;

        static readonly string[] Names = { "북 N", "북동", "동 E", "남동", "남 S", "남서", "서 W", "북서" };
        static readonly string[] Words = { "북", "북동", "동", "남동", "남", "남서", "서", "북서" };

        /// <summary>JS Math.round (halves go up, also for negatives).</summary>
        public static double JsRound(double v) => Math.Floor(v + 0.5);

        public static int Wrap360(int a) => ((a % 360) + 360) % 360;
        public static double Wrap360(double a) => ((a % 360) + 360) % 360;

        /// <summary>The strip's label at a whole degree (북 N, 북동 ...), or null for a plain tick or nothing.</summary>
        public static string CompassName(int deg) { int n = Wrap360(deg); return n % 45 == 0 ? Names[n / 45] : null; }

        /// <summary>A cardinal point (N / E / S / W) gets the big bold label.</summary>
        public static bool IsCardinal(int deg) => Wrap360(deg) % 90 == 0;

        /// <summary>The '…쪽을 보고 있음' word for a heading.</summary>
        public static string HeadingWord(double deg) => Words[(int)JsRound(Wrap360(deg) / 45) % 8];

        public enum MarkKind { None, Tick, Name }

        /// <summary>
        /// What the strip draws at offset d (-80..80) from the rounded heading: the whole degree it stands for, its
        /// x offset from the strip centre in original pixels, and whether it is a name, a 15° tick or nothing.
        /// </summary>
        public static MarkKind CompassMark(double heading, int d, out int deg, out double x)
        {
            deg = (int)JsRound(heading) + d;
            x = (deg - heading) * CompassPxPerDeg;
            int n = Wrap360(deg);
            if (n % 45 == 0) return MarkKind.Name;
            return n % 15 == 0 ? MarkKind.Tick : MarkKind.None;
        }

        /// <summary>Minimap pixels per metre (original: town 2, the rest 9, on a 184 px map).</summary>
        public static double MapScale(string levelId) => levelId == "town" ? 2 : 9;

        /// <summary>UI offset from the map centre (y up = north) of a point in the original frame.</summary>
        public static void ToMap(double x, double z, double px, double pz, double k, out float ux, out float uy)
        {
            ux = (float)((x - px) * k);
            uy = (float)(-(z - pz) * k);
        }

        /// <summary>A box within reach of the map circle (radius r metres) around the player.</summary>
        public static bool Near(double x0, double x1, double z0, double z1, double px, double pz, double r) =>
            x1 > px - r && x0 < px + r && z1 > pz - r && z0 < pz + r;

        /// <summary>A walk zone on the player's floor (within 1.6 m; a ramp spans its low to high end).</summary>
        public static bool WalkOnFloor(LevelLayout.WalkZone w, double py)
        {
            double y0 = w.isRamp ? Math.Min(w.rampY0, w.rampY1) : w.y, y1 = w.isRamp ? Math.Max(w.rampY0, w.rampY1) : y0;
            return !(y1 < py - 1.6 || y0 > py + 1.6);
        }

        /// <summary>A solid the map draws: on, and reaching from knee to above head height on the player's floor.</summary>
        public static bool SolidShown(LevelLayout.SolidBox s, double py) => s != null && SolidShown(s, s.off, py);

        /// <summary>The same with the solid's live on/off state (the story opens doors, sets closures).</summary>
        public static bool SolidShown(LevelLayout.SolidBox s, bool off, double py) => s != null && !off && !s.IsEmpty && !(s.y1 < py + 0.4 || s.y0 > py + 2);

        /// <summary>A point the map marks: enabled and on this floor (no height counts as this floor).</summary>
        public static bool PointShown(bool enabled, double? y, double py) => enabled && Math.Abs((y ?? py) - py) <= 2.2;

        /// <summary>Marker colour class: shelter red, door teal, anything else amber; shelters and doors are bigger.</summary>
        public static int PointStyle(string kind) => kind == "shelter" ? 2 : kind == "door" ? 1 : 0;
    }
}
