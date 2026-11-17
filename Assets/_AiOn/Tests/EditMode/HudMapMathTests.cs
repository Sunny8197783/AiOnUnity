// The HUD's compass strip and minimap arithmetic (original ui/hud.js setCompass / setMap) and the cutscene skip keys
// the Unity host raises while a cutscene holds the controls (story/cine.js SKIP).
using System.Collections.Generic;
using System.Linq;
using AiOn.Levels;
using AiOn.Story;
using AiOn.UI;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class HudMapMathTests
    {
        [Test]
        public void CompassNamesEvery45DegreesWithCardinalsMarked()
        {
            Assert.AreEqual("북 N", HudMapMath.CompassName(0));
            Assert.AreEqual("북 N", HudMapMath.CompassName(360));
            Assert.AreEqual("동 E", HudMapMath.CompassName(90));
            Assert.AreEqual("남서", HudMapMath.CompassName(225));
            Assert.AreEqual("북서", HudMapMath.CompassName(-45));
            Assert.IsNull(HudMapMath.CompassName(30));
            Assert.IsTrue(HudMapMath.IsCardinal(-90));
            Assert.IsFalse(HudMapMath.IsCardinal(135));
        }

        [Test]
        public void HeadingWordRoundsToTheNearestEighthLikeJs()
        {
            Assert.AreEqual("북", HudMapMath.HeadingWord(0));
            Assert.AreEqual("북", HudMapMath.HeadingWord(-22));
            Assert.AreEqual("북동", HudMapMath.HeadingWord(22.5)); // Math.round(0.5) = 1
            Assert.AreEqual("서", HudMapMath.HeadingWord(-90));
            Assert.AreEqual("북", HudMapMath.HeadingWord(350));
        }

        [Test]
        public void TheStripShowsEightyDegreesEitherSideAt2Point2PxPerDegree()
        {
            var names = new List<(int deg, double x)>();
            int ticks = 0;
            for (int d = -HudMapMath.CompassSpan; d <= HudMapMath.CompassSpan; d++)
            {
                var kind = HudMapMath.CompassMark(10.4, d, out int deg, out double x);
                if (kind == HudMapMath.MarkKind.Name) names.Add((deg, x));
                if (kind == HudMapMath.MarkKind.Tick) ticks++;
            }
            // heading 10.4: rounded 10, shows -70..90 -> names at -45, 0, 45, 90
            CollectionAssert.AreEqual(new[] { -45, 0, 45, 90 }, names.Select(n => n.deg).ToArray());
            Assert.AreEqual(-10.4 * 2.2, names[1].x, 1e-9, "north sits left of centre when facing a little east");
            Assert.AreEqual(7, ticks, "15° ticks between the names: -60 -30 -15 15 30 60 75");
        }

        [Test]
        public void MapIsNorthUpWithThePlayerAtTheCentre()
        {
            HudMapMath.ToMap(13, 5, 10, 5, 9, out float ux, out float uy);
            Assert.AreEqual(27f, ux, 1e-4); Assert.AreEqual(0f, uy, 1e-4);
            HudMapMath.ToMap(10, 2, 10, 5, 9, out ux, out uy); // 3 m north (-z) is up
            Assert.AreEqual(0f, ux, 1e-4); Assert.AreEqual(27f, uy, 1e-4);
            Assert.AreEqual(2, HudMapMath.MapScale("town"));
            Assert.AreEqual(9, HudMapMath.MapScale("apartment"));
        }

        [Test]
        public void OnlyThisFloorsWalkZonesSolidsAndPointsAreDrawn()
        {
            var floor = new LevelLayout.WalkZone { y = 3 };
            var stairs = new LevelLayout.WalkZone { isRamp = true, rampY0 = 0, rampY1 = 3 };
            Assert.IsTrue(HudMapMath.WalkOnFloor(floor, 3.1));
            Assert.IsFalse(HudMapMath.WalkOnFloor(floor, 0));
            Assert.IsTrue(HudMapMath.WalkOnFloor(stairs, 0));
            Assert.IsTrue(HudMapMath.WalkOnFloor(stairs, 4.5));
            Assert.IsFalse(HudMapMath.WalkOnFloor(stairs, 4.7));

            var wall = new LevelLayout.SolidBox { x0 = 0, x1 = 1, y0 = 0, y1 = 2.5f, z0 = 0, z1 = 1 };
            Assert.IsTrue(HudMapMath.SolidShown(wall, 0));
            Assert.IsFalse(HudMapMath.SolidShown(new LevelLayout.SolidBox { x0 = 0, x1 = 1, y0 = 0, y1 = 0.3f, z0 = 0, z1 = 1 }, 0), "a kerb under the knee");
            Assert.IsFalse(HudMapMath.SolidShown(new LevelLayout.SolidBox { x0 = 0, x1 = 1, y0 = 0, y1 = 2.5f, z0 = 0, z1 = 1, off = true }, 0));
            Assert.IsFalse(HudMapMath.SolidShown(wall, 6), "the floor below");

            Assert.IsTrue(HudMapMath.PointShown(true, null, 3));
            Assert.IsTrue(HudMapMath.PointShown(true, 4, 3));
            Assert.IsFalse(HudMapMath.PointShown(true, 6, 3));
            Assert.IsFalse(HudMapMath.PointShown(false, 3, 3));

            Assert.IsTrue(HudMapMath.Near(5, 6, 5, 6, 0, 0, 10.3));
            Assert.IsFalse(HudMapMath.Near(11, 12, 0, 1, 0, 0, 10.3));
            Assert.AreEqual(2, HudMapMath.PointStyle("shelter"));
            Assert.AreEqual(1, HudMapMath.PointStyle("door"));
            Assert.AreEqual(0, HudMapMath.PointStyle("item"));
        }

        [Test]
        public void TheMapDrawsSolidsByTheirLiveState()
        {
            // a closure authored off, switched on by the story; a wall authored on, its slab opened
            var closure = new LevelLayout.SolidBox { x0 = 0, x1 = 3, y0 = 0, y1 = 1.2f, z0 = 0, z1 = 0.4f, id = "closure:a", off = true };
            Assert.IsFalse(HudMapMath.SolidShown(closure, 0));
            Assert.IsTrue(HudMapMath.SolidShown(closure, false, 0));
            var slab = new LevelLayout.SolidBox { x0 = 0, x1 = 1, y0 = 0, y1 = 2.1f, z0 = -0.03f, z1 = 0.03f };
            Assert.IsFalse(HudMapMath.SolidShown(slab, true, 0));

            var grid = AiOn.Levels.Crowd.SolidGrid.FromLayout(null);
            Assert.IsFalse(grid.TryGetSwitched("closure:a", out _), "never switched: the authored state stands");
            grid.SetOff("closure:a", false);
            Assert.IsTrue(grid.TryGetSwitched("closure:a", out bool off));
            Assert.IsFalse(off);
        }

        [Test]
        public void ADoorsClosedSlabIsFoundByItsSwingArea()
        {
            var door = new LevelLayout.DoorDef { id = "door302", axis = "z", c = 0, a = -1.6f, b = -0.7f, y = 5.6f };
            Assert.IsTrue(LevelLayout.IsDoorSolid(door, new LevelLayout.SolidBox { x0 = -1.6f, x1 = -0.7f, y0 = 5.6f, y1 = 7.7f, z0 = -0.03f, z1 = 0.03f }));
            Assert.IsFalse(LevelLayout.IsDoorSolid(door, new LevelLayout.SolidBox { x0 = -1.6f, x1 = -0.7f, y0 = 0f, y1 = 2.1f, z0 = -0.03f, z1 = 0.03f }), "another floor");
            Assert.IsFalse(LevelLayout.IsDoorSolid(door, new LevelLayout.SolidBox { x0 = -5f, x1 = 5f, y0 = 5.6f, y1 = 7.7f, z0 = -0.03f, z1 = 0.03f }), "the wall around it");
            Assert.IsTrue(LevelLayout.IsDoorSolid(door, new LevelLayout.SolidBox { id = "door302" }), "its leaf boxes by id");
        }

        [Test]
        public void TheHostRaisesExactlyTheCutsceneSkipKeysWhileControlsAreHeld()
        {
            CollectionAssert.AreEquivalent(StoryCine.SkipKeys.ToArray(), UnityStoryHost.SkipKeyCodes);
        }
    }
}
