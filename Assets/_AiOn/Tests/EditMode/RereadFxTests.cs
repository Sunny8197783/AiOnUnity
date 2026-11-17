using System.Collections.Generic;
using System;
using System.Linq;
using AiOn.Levels;
using AiOn.Story;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AiOn.Tests
{
    public class RereadFxTests
    {
        [Test]
        public void EyeLookFacesNorthAtYawZeroAndLeftAtPositiveYaw()
        {
            var (x, y, z) = RereadFx.EyeLook(10, 20, 0, 0, 1.5);
            Assert.AreEqual(10, x, 1e-9); Assert.AreEqual(1.5, y, 1e-9); Assert.AreEqual(19, z, 1e-9);
            // Unity: north is +z, matching LevelLayout.ToUnityYaw(0) = 0 (forward).
            var dir = LevelLayout.ToUnity((float)x, 0, (float)z) - LevelLayout.ToUnity(10, 0, 20);
            Assert.AreEqual(Vector3.forward.z, dir.normalized.z, 1e-5);
            // Positive yaw turns left: from north, left is west (-x).
            var left = RereadFx.EyeLook(0, 0, Math.PI / 2, 0, 0);
            Assert.AreEqual(-1, left.x, 1e-9); Assert.AreEqual(0, left.z, 1e-9);
            // The original start (yaw π) faces south (+z).
            Assert.AreEqual(1, RereadFx.EyeLook(0, 0, Math.PI, 0, 0).z, 1e-9);
        }

        [Test]
        public void EyeLookPitchesDown()
        {
            var (_, y, _) = RereadFx.EyeLook(0, 0, 0, -0.5, 1.5);
            Assert.AreEqual(1.5 + Math.Sin(-0.5), y, 1e-9);
            Assert.Less(y, 1.5);
        }

        [Test]
        public void PitchEasesAndReduceMotionLandsAtOnce()
        {
            Assert.AreEqual(-0.5f, RereadFx.EasePitch(-0.35f, -0.5f, 0.016f, true), 1e-6);
            float p = RereadFx.EasePitch(-0.35f, -0.5f, 0.1f, false);
            Assert.Less(p, -0.35f); Assert.Greater(p, -0.5f);
            Assert.AreEqual(-0.35f, RereadFx.EasePitch(-0.35f, -0.5f, 0f, false), 1e-6);
        }

        [Test]
        public void CraneTakesOnePointSixSeconds()
        {
            Assert.AreEqual(1f, RereadFx.CraneStep(0, 0.01f, true));
            Assert.AreEqual(0.5f, RereadFx.CraneStep(0, 0.8f, false), 1e-5);
            Assert.AreEqual(1f, RereadFx.CraneStep(0.9f, 1f, false));
            Assert.AreEqual(0f, RereadFx.Ease(0)); Assert.AreEqual(1f, RereadFx.Ease(1)); Assert.AreEqual(0.5f, RereadFx.Ease(0.5f), 1e-6);
        }

        [Test]
        public void DownsampleKeepsEveryKthPoint()
        {
            var pts = Enumerable.Range(0, 250).ToList();
            var d = RereadFx.Downsample(pts, 120); // every = ceil(250/120) = 3
            Assert.AreEqual(84, d.Count);
            Assert.AreEqual(0, d[0]); Assert.AreEqual(3, d[1]);
            Assert.AreEqual(10, RereadFx.Downsample(Enumerable.Range(0, 10).ToList(), 120).Count);
            Assert.AreEqual(0, RereadFx.Downsample(new int[0], 120).Count);
        }

        [Test]
        public void PathCountGrowsFromOneToAll()
        {
            Assert.AreEqual(1, RereadFx.PathCount(0, 50));
            Assert.AreEqual(50, RereadFx.PathCount(1, 50));
            Assert.AreEqual(25, RereadFx.PathCount(0.5f, 50)); // floor(0.5 * 49) + 1
            Assert.AreEqual(1, RereadFx.PathCount(1, 1));
            Assert.AreEqual(0, RereadFx.PathCount(1, 0));
        }

        [Test]
        public void GroundSkipsRoofsAndFallsBack()
        {
            float m = RereadFx.GroundMaxY;
            Assert.AreEqual(0.2f, RereadFx.PickGround(new[] { 12f, 0.2f, -4f }, m, 0f));
            Assert.AreEqual(0f, RereadFx.PickGround(new[] { 2.4f, 1.1f, 0f }, m, 0f)); // signboard / kiosk tops skipped
            Assert.AreEqual(5f, RereadFx.PickGround(new[] { 9f, 5f }, m, 0f)); // nothing low enough: the lowest
            Assert.AreEqual(0f, RereadFx.PickGround(new float[0], m, 0f));
        }

        [Test]
        public void PathSplitsAtRecordingGaps()
        {
            var pts = new List<(double x, double z)> { (0, 0), (1.5, 0), (3, 0), (40, 0), (41.5, 0) };
            var runs = RereadFx.SplitRuns(pts, 4.5);
            Assert.AreEqual(2, runs.Count);
            Assert.AreEqual((0, 3), runs[0]);
            Assert.AreEqual((3, 2), runs[1]);
            Assert.AreEqual(1, RereadFx.SplitRuns(pts, 100).Count);
            Assert.AreEqual(0, RereadFx.SplitRuns(new List<(double x, double z)>(), 4.5).Count);
        }

        [Test]
        public void LabelsClipSqueezeAndGrowWest()
        {
            Assert.AreEqual("abc", RereadFx.Clip("abc", 26));
            var longText = new string('가', 30);
            Assert.AreEqual(new string('가', 25) + "…", RereadFx.Clip(longText, 26));
            Assert.AreEqual(46, RereadFx.FitFontSize(46, 500, 860, 20));
            Assert.AreEqual(23, RereadFx.FitFontSize(46, 1720, 860, 20));
            Assert.AreEqual(20, RereadFx.FitFontSize(46, 5000, 860, 20));
            Assert.AreEqual(0.85f, RereadFx.PivotX(-118));
            Assert.AreEqual(0.5f, RereadFx.PivotX(-25));
        }

        [Test]
        public void LinkSamplesEveryTwoMetres()
        {
            Assert.AreEqual(2, RereadFx.LinkSamples(0, 0, 0, 0));
            Assert.AreEqual(6, RereadFx.LinkSamples(0, 0, 10, 0));
        }

        [Test]
        public void NumReadsNumbersAndStrings()
        {
            Assert.AreEqual(2.2, RereadFx.Num(new JValue(2.2), 0));
            Assert.AreEqual(3, RereadFx.Num(new JValue(3), 0));
            Assert.AreEqual(1.5, RereadFx.Num(new JValue("1.5"), 0));
            Assert.AreEqual(7, RereadFx.Num(null, 7));
            Assert.AreEqual(7, RereadFx.Num(JValue.CreateNull(), 7));
        }

        [Test]
        public void NoteChangeFollowsTheContract()
        {
            Assert.AreEqual(RereadScreen.NoteAction.None, RereadScreen.NoteChange(new JObject { ["text"] = "x" }));
            Assert.AreEqual(RereadScreen.NoteAction.Hide, RereadScreen.NoteChange(new JObject { ["note"] = JValue.CreateNull() }));
            Assert.AreEqual(RereadScreen.NoteAction.Show, RereadScreen.NoteChange(JObject.Parse("{note:{hand:{text:'hi'}, mode:'phone'}}")));
            Assert.AreEqual(RereadScreen.NoteAction.None, RereadScreen.NoteChange(null));
        }

        [Test]
        public void PromptClampsCountsAndReadsMax()
        {
            Assert.AreEqual(24, PromptScreen.MaxOf(new JValue(24)));
            Assert.AreEqual(0, PromptScreen.MaxOf(null));
            Assert.AreEqual(0, PromptScreen.MaxOf(new JValue("24")));
            Assert.AreEqual(0, PromptScreen.MaxOf(new JValue(0)));
            Assert.AreEqual("가나", PromptScreen.Clamp("가나다", 2));
            Assert.AreEqual("가나다", PromptScreen.Clamp("가나다", 0));
            Assert.AreEqual("", PromptScreen.Clamp(null, 5));
            Assert.AreEqual("3 / 24", PromptScreen.Counter("가나다", 24));
            Assert.AreEqual("", PromptScreen.Counter("가", 0));
        }
    }
}
