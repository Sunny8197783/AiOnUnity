// UnityCineFx's pure parts: the typed title timing (story/cine.js title(): 400 + i*700 + j*45 ms per character),
// text-element typing, fill light mapping and colour parsing, and the missile's arc.
using AiOn.Story;
using NUnit.Framework;
using UnityEngine;

namespace AiOn.Tests
{
    public class UnityCineFxTests
    {
        [Test]
        public void Characters_appear_on_the_original_schedule()
        {
            Assert.AreEqual(0.4, UnityCineFx.CharTime(0, 0), 1e-9);
            Assert.AreEqual(1.1, UnityCineFx.CharTime(1, 0), 1e-9);
            Assert.AreEqual(0.4 + 0.7 + 3 * 0.045, UnityCineFx.CharTime(1, 3), 1e-9);

            Assert.AreEqual(0, UnityCineFx.TypedCount(0, 5, 0.39));
            Assert.AreEqual(1, UnityCineFx.TypedCount(0, 5, 0.4));
            Assert.AreEqual(2, UnityCineFx.TypedCount(0, 5, 0.4 + 0.045));
            Assert.AreEqual(5, UnityCineFx.TypedCount(0, 5, 10));
            Assert.AreEqual(0, UnityCineFx.TypedCount(1, 5, 1.0), "the second line starts 0.7 s after the first");
            Assert.AreEqual(1, UnityCineFx.TypedCount(1, 5, 1.1));
            Assert.AreEqual(0, UnityCineFx.TypedCount(0, 0, 10));
        }

        [Test]
        public void Typing_counts_text_elements()
        {
            Assert.AreEqual("", UnityCineFx.TypedText("연무동", 0));
            Assert.AreEqual("연무", UnityCineFx.TypedText("연무동", 2));
            Assert.AreEqual("연무동", UnityCineFx.TypedText("연무동", 9));
            Assert.AreEqual(3, UnityCineFx.TextLength("연무동"));
            Assert.AreEqual(2, UnityCineFx.TextLength("a\U0001F525"), "a surrogate pair is one character");
            Assert.AreEqual("a\U0001F525", UnityCineFx.TypedText("a\U0001F525b", 2));
            Assert.AreEqual(0, UnityCineFx.TextLength(null));
        }

        [Test]
        public void Fill_maps_intensity_and_colour()
        {
            Assert.AreEqual(0f, UnityCineFx.FillIntensity(0));
            Assert.AreEqual(0f, UnityCineFx.FillIntensity(-1));
            Assert.Greater(UnityCineFx.FillIntensity(9), UnityCineFx.FillIntensity(1.6));
            var c = UnityCineFx.ParseColor("#e8ecff", Color.black);
            Assert.AreEqual(0xe8 / 255f, c.r, 1e-3);
            Assert.AreEqual(0xec / 255f, c.g, 1e-3);
            Assert.AreEqual(1f, c.b, 1e-3);
            Assert.AreEqual(Color.red, UnityCineFx.ParseColor(null, Color.red));
            Assert.AreEqual(Color.red, UnityCineFx.ParseColor("not a colour", Color.red));
        }

        [Test]
        public void Missile_runs_from_to_along_a_slight_arc()
        {
            double[] a = { 120, 240, -220 }, b = { 0, 0, 0 };
            var p0 = UnityCineFx.MissilePoint(a, b, 0);
            var p1 = UnityCineFx.MissilePoint(a, b, 1);
            for (int i = 0; i < 3; i++) { Assert.AreEqual(a[i], p0[i], 1e-9); Assert.AreEqual(b[i], p1[i], 1e-9); }

            // Accelerating like the original's k*k, and lifted above the straight line in between, by at most 15 m.
            double k = Mathf.Sqrt(0.5f), e = k * k;
            var mid = UnityCineFx.MissilePoint(a, b, k);
            Assert.AreEqual(a[0] + (b[0] - a[0]) * e, mid[0], 1e-6);
            Assert.AreEqual(a[2] + (b[2] - a[2]) * e, mid[2], 1e-6);
            double lift = mid[1] - (a[1] + (b[1] - a[1]) * e);
            Assert.Greater(lift, 0);
            Assert.LessOrEqual(lift, 15 + 1e-9);

            // Out-of-range k clamps.
            Assert.AreEqual(b[1], UnityCineFx.MissilePoint(a, b, 2)[1], 1e-9);
            Assert.AreEqual(a[1], UnityCineFx.MissilePoint(a, b, -1)[1], 1e-9);
        }
    }
}
