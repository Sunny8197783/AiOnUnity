using System;
using System.Collections.Generic;

namespace AiOn.Levels.Crowd
{
    /// <summary>
    /// Walk-surface height at (x, z) from a level's walk zones: the town takes the highest zone (town-util.js heightAt),
    /// the shelter the lowest (shelter-people.js heightAt: the stair foot under the top landing). 0 outside every zone.
    /// </summary>
    public sealed class WalkHeight
    {
        public enum Pick { Highest, Lowest }

        public struct Zone
        {
            public double X0, X1, Z0, Z1, Y;
            public bool Ramp;
            public bool AxisX;
            public double Y0, Y1;
        }

        readonly Zone[] _zones;
        readonly Pick _pick;

        public WalkHeight(IEnumerable<Zone> zones, Pick pick) { _zones = new List<Zone>(zones).ToArray(); _pick = pick; }

        public static WalkHeight FromLayout(LevelLayout layout, Pick pick)
        {
            var list = new List<Zone>();
            if (layout != null)
                foreach (var w in layout.walk)
                    list.Add(new Zone { X0 = w.x0, X1 = w.x1, Z0 = w.z0, Z1 = w.z1, Y = w.y, Ramp = w.isRamp, AxisX = w.rampAxis == "x", Y0 = w.rampY0, Y1 = w.rampY1 });
            return new WalkHeight(list, pick);
        }

        public int Count => _zones.Length;

        public double At(double x, double z)
        {
            double best = _pick == Pick.Highest ? double.NegativeInfinity : double.PositiveInfinity;
            for (int i = 0; i < _zones.Length; i++)
            {
                ref readonly var w = ref _zones[i];
                if (x < w.X0 || x > w.X1 || z < w.Z0 || z > w.Z1) continue;
                double h;
                if (!w.Ramp) h = w.Y;
                else
                {
                    double t = w.AxisX ? (x - w.X0) / (w.X1 - w.X0) : (z - w.Z0) / (w.Z1 - w.Z0);
                    if (_pick == Pick.Highest) t = Math.Min(1, Math.Max(0, t)); // town-util clamps; the shelter's does not need to
                    h = w.Y0 + (w.Y1 - w.Y0) * t;
                }
                if (_pick == Pick.Highest ? h > best : h < best) best = h;
            }
            return double.IsInfinity(best) ? 0 : best;
        }
    }
}
