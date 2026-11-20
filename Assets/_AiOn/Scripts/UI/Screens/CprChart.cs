// The CPR debrief's chart (competition upgrade): the true compression quality and what the gauge showed, over the
// compression time, with the neighbour's stretches tinted and a mark where the presses got shallow.
using System.Collections.Generic;
using AiOn.Game;
using UnityEngine;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>The debrief chart: the true quality (amber) and what the gauge showed (teal) over the compression time,
    /// with the neighbour's stretches tinted and a mark where it got shallow.</summary>
    public sealed class CprChart : MaskableGraphic
    {
        public static readonly Color TrueColor = new(0.95f, 0.62f, 0.22f, 1f);
        public static readonly Color GaugeColor = UiKit.Teal;
        public static readonly Color SwapColor = new(0.35f, 0.55f, 0.95f, 0.22f);
        static readonly Color AedColor = new(1f, 1f, 1f, 0.07f);
        static readonly Color Grid = new(1f, 1f, 1f, 0.08f);
        static readonly Color Mark = new(0.9f, 0.28f, 0.3f, 0.8f);

        IReadOnlyList<CprSample> _curve = new List<CprSample>();
        double _dur = 1, _shallow = -1;

        public void Set(IReadOnlyList<CprSample> curve, double seconds, double shallowAt)
        {
            _curve = curve ?? new List<CprSample>();
            _dur = System.Math.Max(1, seconds);
            _shallow = shallowAt;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            float X(double t) => r.xMin + (float)(t / _dur) * r.width;
            float Y(double v) => r.yMin + Mathf.Clamp01((float)v) * r.height;
            for (int i = 0; i <= 2; i++) HudDraw.Quad(vh, r.xMin, Y(i * 0.5) - 0.5f, r.xMax, Y(i * 0.5) + 0.5f, Grid);
            float step = r.width / (float)_dur;
            foreach (var s in _curve)
            {
                if (s.Who == "swap" || s.Who == "gap") HudDraw.Quad(vh, X(s.T), r.yMin, X(s.T) + step, r.yMax, SwapColor);
                else if (s.Who == "aed") HudDraw.Quad(vh, X(s.T), r.yMin, X(s.T) + step, r.yMax, AedColor);
            }
            if (_shallow >= 0) HudDraw.Quad(vh, X(_shallow) - 1.5f, r.yMin, X(_shallow) + 1.5f, r.yMax, Mark);
            Line(vh, true, X, Y, GaugeColor);
            Line(vh, false, X, Y, TrueColor);
        }

        void Line(VertexHelper vh, bool gauge, System.Func<double, float> x, System.Func<double, float> y, Color c)
        {
            for (int i = 1; i < _curve.Count; i++)
            {
                var a = _curve[i - 1]; var b = _curve[i];
                var p0 = new Vector2(x(a.T), y(gauge ? a.Gauge : a.Quality));
                var p1 = new Vector2(x(b.T), y(gauge ? b.Gauge : b.Quality));
                var d = (p1 - p0).normalized;
                var nrm = new Vector2(-d.y, d.x) * 1.6f;
                int v = vh.currentVertCount;
                vh.AddVert(p0 - nrm, c, Vector4.zero); vh.AddVert(p0 + nrm, c, Vector4.zero);
                vh.AddVert(p1 + nrm, c, Vector4.zero); vh.AddVert(p1 - nrm, c, Vector4.zero);
                vh.AddTriangle(v, v + 1, v + 2); vh.AddTriangle(v, v + 2, v + 3);
            }
        }
    }
}
