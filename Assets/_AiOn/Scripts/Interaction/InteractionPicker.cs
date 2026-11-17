using System;
using System.Collections.Generic;
using UnityEngine;

namespace AiOn.Interaction
{
    /// <summary>What the picker needs to know about a point.</summary>
    public interface IReachable
    {
        Vector3 Position { get; }
        /// <summary>Own radius (0 = default).</summary>
        float Radius { get; }
        /// <summary>Explicit reach overriding radius and minimum (0 = none).</summary>
        float ReachOverride { get; }
        bool Enabled { get; }
    }

    /// <summary>
    /// Which interaction point (E prompt) the player is at: the original engine/reach.js rules. A point is in reach
    /// within its own radius (never under 1.6 m), measured flat; while it lies within 100° of where the camera or the
    /// body faces (anywhere once closer than 1.2 m); from 1.8 m below to 2.2 m above the feet; and not behind a wall.
    /// Clustered points go to the best score (distance plus how far off the view it is, in radians); the current one
    /// is kept unless another is better by more than 0.3, so the prompt never flickers.
    /// </summary>
    public sealed class InteractionPicker
    {
        public const float Min = 1.6f, ConeDeg = 100f, Free = 1.2f, Below = 1.8f, Above = 2.2f, Turn = 1.0f, Keep = 0.3f;

        public static float ReachOf(IReachable p) => p.ReachOverride > 0 ? p.ReachOverride : Mathf.Max(p.Radius > 0 ? p.Radius : Min, Min);

        /// <summary>
        /// Score of a point in reach and in view, or +infinity. <paramref name="yawsDeg"/> are the facings to test
        /// (camera and body), in Unity degrees. Visibility (walls) is tested separately: it is the costly part.
        /// </summary>
        public static float RawScore(IReachable p, Vector3 feet, IReadOnlyList<float> yawsDeg)
        {
            if (p == null || !p.Enabled) return float.PositiveInfinity;
            var pos = p.Position;
            float dx = pos.x - feet.x, dz = pos.z - feet.z, dist = Mathf.Sqrt(dx * dx + dz * dz), dy = pos.y - feet.y;
            if (dist > ReachOf(p) || dy > Above || dy < -Below) return float.PositiveInfinity;
            if (dist < 0.05f) return dist;
            float toPoint = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg; // Unity yaw that faces the point
            float off = float.PositiveInfinity;
            foreach (var y in yawsDeg) off = Mathf.Min(off, Mathf.Abs(Mathf.DeltaAngle(y, toPoint)));
            if (off > ConeDeg && dist > Free) return float.PositiveInfinity;
            return dist + off * Mathf.Deg2Rad * Turn;
        }

        readonly List<(IReachable p, float s)> _cand = new();

        public IReachable Current { get; private set; }

        public void Reset() => Current = null;

        /// <summary>Picks the point to prompt for. <paramref name="hidden"/> says whether a wall hides a point.</summary>
        public IReachable Pick(IEnumerable<IReachable> points, Vector3 feet, IReadOnlyList<float> yawsDeg, Func<IReachable, bool> hidden = null)
        {
            _cand.Clear();
            foreach (var p in points)
            {
                float s = RawScore(p, feet, yawsDeg);
                if (!float.IsPositiveInfinity(s)) _cand.Add((p, s));
            }
            _cand.Sort((a, b) => a.s.CompareTo(b.s));

            IReachable best = null;
            float bs = float.PositiveInfinity;
            foreach (var c in _cand)
                if (hidden == null || !hidden(c.p)) { best = c.p; bs = c.s; break; }

            if (Current != null && best != null && !ReferenceEquals(Current, best))
            {
                int i = _cand.FindIndex(k => ReferenceEquals(k.p, Current));
                if (i >= 0 && _cand[i].s <= bs + Keep && (hidden == null || !hidden(Current))) best = Current;
            }
            return Current = best;
        }
    }
}
