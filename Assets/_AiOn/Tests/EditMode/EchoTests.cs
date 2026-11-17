// Port of tests/echo.test.mjs, plus the recorder (begin / death / chalk / trail) on FakeStoryHost.
using System.Linq;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class EchoTests
    {
        [Test]
        public void EchoesFromStorageKeepOnlyWellFormedMarksLast3Runs()
        {
            Assert.That(Echo.ReadEcho("not json").Runs, Is.Empty);
            var runs = new JArray(Enumerable.Range(0, 5).Select(i => new JObject
            {
                ["trail"] = new JArray(new JArray("town", i, 2), new JArray("town", "x", 1), "bad"),
                ["deaths"] = new JArray(new JObject { ["lv"] = "town", ["x"] = 1, ["z"] = 2, ["why"] = "blast" }, new JObject { ["x"] = 1 }),
                ["notes"] = new JArray(new JObject { ["lv"] = "mart", ["x"] = 0, ["z"] = 0, ["text"] = string.Concat(Enumerable.Repeat("물 있음", 10)) }),
            }));
            var e = Echo.ReadEcho(new JObject { ["runs"] = runs }.ToString());
            Assert.That(e.Runs.Count, Is.EqualTo(3));
            Assert.That(e.Runs[0].Trail, Is.EqualTo(new[] { ("town", 2.0, 2.0) }));
            Assert.That(Echo.ReadEcho("{\"runs\":[{\"trail\":[[\"town\",1,1]]}]}").Runs.Count, Is.EqualTo(0)); // left no mark
            Assert.That(e.Runs[0].Deaths.Count, Is.EqualTo(1));
            Assert.That(e.Runs[0].Deaths[0].Why, Is.EqualTo("blast"));
            Assert.That(e.Runs[0].Deaths[0].Yaw, Is.EqualTo(0));
            Assert.That(e.Runs[0].Notes[0].Text.Length, Is.EqualTo(24));
        }

        [Test]
        public void SavedJsonRoundTripsInTheOriginalShape()
        {
            var d = new EchoData();
            var r = new EchoRun();
            for (int i = 0; i < 6; i++) r.Trail.Add(("town", i, 0.5));
            r.Deaths.Add(new EchoDeath { Lv = "town", X = 1, Z = 2, Why = "fire", Yaw = 0.3 });
            d.Runs.Add(r);
            var j = JObject.Parse(d.ToJson());
            Assert.That(j["runs"][0]["trail"][5].ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("[\"town\",5.0,0.5]"));
            Assert.That((string)j["runs"][0]["deaths"][0]["why"], Is.EqualTo("fire"));
            var back = Echo.ReadEcho(d.ToJson());
            Assert.That(back.Runs[0].Trail.Count, Is.EqualTo(6));
            var m = Echo.MarksFor(back, "town");
            Assert.That(m.Crosses.Single().Label, Is.EqualTo("1회 전의 내가 쓰러진 곳 · 화재"));
            Assert.That(m.Ghost.Count, Is.EqualTo(6));
            Assert.That(Echo.MarksFor(back, "mart").Any, Is.False);
        }

        [Test]
        public void RecorderKeepsTheWalkDeathsAndChalkForTheNextRun()
        {
            string stored = null;
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules);
            var echo = Echo.Create(ctx, k => stored, (k, v) => stored = v);
            echo.Begin();
            host.Answer("own");
            host.AnswerOpen("prompt", "  여기 물 나옴  ");
            host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                var roam = ctx.Roam(new RoamOptions { Tick = _ => host.FrameCount > 40 ? "done" : null });
                for (int i = 0; i < 10; i++) { host.MoveTo(host.PlayerX + 2, host.PlayerZ); host.RunFrame(); }
                await roam;
                var n = await echo.Chalk();
                Assert.That(n.Text, Is.EqualTo("여기 물 나옴"));
                echo.Death("blast");
            });
            Assert.That(echo.Current.Trail.Count, Is.GreaterThan(4));
            Assert.That(host.Toasts.Last(), Does.Contain("분필"));
            // the next run sees it
            var host2 = new FakeStoryHost();
            var ctx2 = host2.CreateContext(TestSupport.Rules);
            var echo2 = Echo.Create(ctx2, k => stored, (k, v) => stored = v);
            echo2.Begin();
            EchoMarks shown = null;
            echo2.Show = m => shown = m;
            host2.Run(async () => { await ctx2.Goto("town", "home"); host2.RunFrame(); await host2.Settle(); });
            Assert.That(shown.Level, Is.EqualTo("town"));
            Assert.That(shown.Crosses.Single().Label, Does.EndWith("포격"));
            Assert.That(shown.Notes.Single().Text, Is.EqualTo("여기 물 나옴"));
            Assert.That(shown.Ghost.Count, Is.GreaterThan(4));
            Assert.That(host2.Toasts, Has.Some.Contains("이전 회차의 내가 남긴 흔적"));
        }

        // Review fix: without a host hook the marks never reached the world. By default the recorder now publishes them as
        // level data (Engine.ApplyLevelState("echo", ...)), and SaveAll() is the pagehide save for the host's pause / quit.
        [Test]
        public void MarksOfEarlierRunsArePublishedAsLevelData_AndSaveAllKeepsTheWalk()
        {
            string stored = null;
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules);
            var echo = Echo.Create(ctx, k => stored, (k, v) => stored = v);
            echo.Begin();
            host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                var roam = ctx.Roam(new RoamOptions { Tick = _ => host.FrameCount > 20 ? "done" : null });
                for (int i = 0; i < 8; i++) { host.MoveTo(host.PlayerX + 2, host.PlayerZ); host.RunFrame(); }
                await roam;
                echo.Death("fire");
            });
            Assert.That(host.State(Echo.StateKey), Is.Null, "a first run has nothing to draw");
            // more walking after the last save (no 10 s autosave yet), then the app goes away
            int before = JObject.Parse(stored)["runs"].Last["trail"].Count();
            echo.Current.Trail.Add(("town", 99, 99));
            EchoRecorder.SaveAll();
            Assert.That(JObject.Parse(stored)["runs"].Last["trail"].Count(), Is.EqualTo(before + 1));

            var host2 = new FakeStoryHost();
            var ctx2 = host2.CreateContext(TestSupport.Rules);
            var echo2 = Echo.Create(ctx2, k => stored, (k, v) => stored = v);
            echo2.Begin();
            host2.Run(async () => { await ctx2.Goto("town", "home"); host2.RunFrame(); await host2.Settle(); });
            var drawn = host2.State(Echo.StateKey) as JObject;
            Assert.That(drawn, Is.Not.Null);
            Assert.That((string)drawn["level"], Is.EqualTo("town"));
            Assert.That((string)drawn["crosses"][0]["label"], Is.EqualTo("1회 전의 내가 쓰러진 곳 · 화재"));
            Assert.That(drawn["ghost"].Count(), Is.GreaterThan(4));
            Assert.That(host2.Toasts, Has.Some.Contains("이전 회차의 내가 남긴 흔적"));
        }

        // Review fix: JS PRESETS[Number(pick)] - Number(null) and Number('') are 0, so a dismissed pick writes the first preset.
        [Test]
        public void ChalkPickBecomesAPresetLikeJsNumber()
        {
            Assert.That(Echo.PresetFor(null), Is.EqualTo("여기 위험해"));
            Assert.That(Echo.PresetFor(""), Is.EqualTo("여기 위험해"));
            Assert.That(Echo.PresetFor(" "), Is.EqualTo("여기 위험해"));
            Assert.That(Echo.PresetFor("2"), Is.EqualTo("물 있음"));
            Assert.That(Echo.PresetFor("own"), Is.Null);
            Assert.That(Echo.PresetFor("no"), Is.Null);
            Assert.That(Echo.PresetFor("5"), Is.Null);

            string stored = null;
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules);
            var echo = Echo.Create(ctx, k => stored, (k, v) => stored = v);
            echo.Begin();
            host.Answer(new string[] { null });
            EchoNote n = null;
            host.Run(async () => { await ctx.Goto("town", "home"); n = await echo.Chalk(); });
            Assert.That(n?.Text, Is.EqualTo("여기 위험해"));
            Assert.That((string)JObject.Parse(stored)["runs"].Last["notes"][0]["text"], Is.EqualTo("여기 위험해"));
            Assert.That(host.State(Echo.StateKey)?["notes"]?.Count(), Is.EqualTo(1), "the fresh note is drawn too");
        }
    }
}
