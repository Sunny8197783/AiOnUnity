// StoryContext (story/runtime.js) on FakeStoryHost: ports of the runtime parts of tests/story.test.mjs and
// tests/r6-fix1-doors.test.mjs, plus roam / goto / payoff / cue / modal tests of our own.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class StoryContextTests
    {
        static (FakeStoryHost host, StoryContext ctx) Rig(long seed = 7, StorySettings settings = null)
        {
            var host = new FakeStoryHost();
            var rules = TestSupport.Rules;
            var ctx = host.CreateContext(rules, rules.CreateGame(seed), settings: settings);
            return (host, ctx);
        }

        static StoryPoint P(string id, string kind, string label) => new StoryPoint { Id = id, Kind = kind, Label = label };
        static Func<StoryPoint, Task<JToken>> H(Func<StoryPoint, JToken> f) => p => Task.FromResult(f(p));

        // ------------------------------------------------------------------ story.test.mjs

        [Test]
        public void StateSetInOneRunGoesBackToTheLevelDefaultAfterLevelStatesIsReset_F1()
        {
            var (host, ctx) = Rig();
            host.Run(async () =>
            {
                ctx.SetLevelState("town", "strike", "hit");
                ctx.SetLevelState("town", "closures", new JArray("sijang"));
                await ctx.Goto("town", "home");
                Assert.That((string)host.State("strike"), Is.EqualTo("hit"));
                ctx.LevelStates = new Dictionary<string, Dictionary<string, JToken>>(); // what resetRun / a checkpoint retry leave behind
                await ctx.Goto("apartment", "home");
                await ctx.Goto("town", "home");
                Assert.That((string)host.State("strike"), Is.EqualTo("none"));
                TestSupport.AssertSameJson(host.State("closures"), new JArray());
                // a key the run still holds is applied as it is; keys never touched are not re-sent
                ctx.SetLevelState("town", "buses", "on");
                host.Level("town").State.Remove("traffic");
                await ctx.Goto("apartment", "home"); await ctx.Goto("town", "home");
                Assert.That((string)host.State("buses"), Is.EqualTo("on"));
                Assert.That(host.State("traffic"), Is.Null);
            });
            Assert.That(StoryContext.StaleKeys(new[] { "a", "b", "c" }, new[] { "b" }, new[] { "a", "b" }), Is.EqualTo(new[] { "a" }));
            foreach (var kv in StoryContext.LevelDefaults) Assert.That(kv.Value.Count, Is.GreaterThan(0), kv.Key);
        }

        [Test]
        public void ShelterSeoyeonIsARuntimeStateTheNextRunHidesAgain()
        {
            var (host, ctx) = Rig();
            host.Run(async () =>
            {
                await ctx.Goto("shelter", "spot");
                host.Person("seoyeon").Visible = false;
                ctx.SetLevelState("shelter", "seoyeon", "shown");
                Assert.That(host.Person("seoyeon").Visible, Is.True);
                Assert.That(host.State("seoyeon"), Is.Null, "not sent to the level");
                ctx.LevelStates = new Dictionary<string, Dictionary<string, JToken>>();
                await ctx.Goto("shelter", "spot");
                Assert.That(host.Person("seoyeon").Visible, Is.False);
            });
        }

        [Test]
        public void Roam_StarRunsBeforeTheEverydayText_FallsThroughOnNull_AndEAlwaysAnswers_F8_F12()
        {
            var (host, ctx) = Rig();
            var result = host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                int star = 0;
                var done = ctx.Roam(new RoamOptions
                {
                    On = { ["*"] = H(p => { star++; if (p.Kind == "notice") return StoryContext.Handled; if (p.Id == "stop") return "stopped"; return null; }) },
                });
                await host.PressAsync(P("aptNotice", "notice", "단지 게시판"));
                Assert.That(star, Is.EqualTo(1)); Assert.That(host.Talks.Count, Is.EqualTo(0), "the peacetime board text did not run");
                await host.PressAsync(P("center", "place", "행정복지센터"));
                Assert.That(star, Is.EqualTo(2)); Assert.That(host.Talks.Last().Speaker, Is.EqualTo("행정복지센터"), "falls through to the description");
                await host.PressAsync(P("shelterA", "shelter", "출입구 A"));
                Assert.That(host.Toasts.Last(), Is.EqualTo(StoryContext.IdleLine(new StoryPoint { Kind = "shelter" })));
                await host.PressAsync(P("stop", "npc", "x"));
                return await done;
            });
            Assert.That((string)result, Is.EqualTo("stopped"));
            StringAssert.Contains("304호 어르신: 지금은 할 일이 없다", StoryContext.IdleLine(P("x", "npc", "304호 어르신")));
        }

        [Test]
        public void RoamDeadlineTypeMayBeDecidedAtTheMomentItPasses()
        {
            var (host, ctx) = Rig();
            var fail = host.RunExpectFail(async () =>
            {
                await ctx.Goto("town", "home");
                host.G.Day = 0; host.G.Minute = 20 * 60 + 31;
                var p = ctx.Roam(new RoamOptions { Deadline = new RoamDeadline { Day = 0, Minute = 20 * 60 + 30, TypeOf = () => "lostWay" } });
                host.RunFrame();
                await p;
            });
            Assert.That(fail.Type, Is.EqualTo("lostWay"));
        }

        [Test]
        public void PayoffToastCarriesTheCauseStampFromRipples_M1()
        {
            var (host, ctx) = Rig();
            var g = host.G;
            g.Day = -1; g.Minute = 10 * 60; GameRules.RecordEvent(g, "bag_packed", new JObject());
            g.Day = 0; g.Minute = 20 * 60 + 6; ctx.Payoff("bag", "바로 나섰다");
            Assert.That(host.Toasts.Last(), Is.EqualTo("준비가 도움이 됐습니다 · 바로 나섰다 (D-1 오전 · 비상가방을 꾸려 현관에 두었다)"));
            Assert.That(((ValueTuple<string, int>)host.Calls("toast").Last().Data).Item2, Is.EqualTo(6000));
            ctx.Payoff("judgement", "판단했다");
            Assert.That(host.Toasts.Last(), Is.EqualTo("준비가 도움이 됐습니다 · 판단했다"));
            Assert.That(((ValueTuple<string, int>)host.Calls("toast").Last().Data).Item2, Is.EqualTo(4600));
            Assert.That(g.Events.Count(e => e.Type == "payoff"), Is.EqualTo(2));
        }

        // ------------------------------------------------------------------ r6-fix1-doors.test.mjs

        [Test]
        public void EOutThroughTheLobbyAndEBackIn_TheLobbySpawnDoesNotBounceYouOut_R6F1()
        {
            var (host, ctx) = Rig();
            host.Run(async () =>
            {
                await ctx.Goto("apartment", "lobby");
                _ = ctx.Roam(new RoamOptions());
                host.PlayerX = 4; host.PlayerZ = -2.5; host.RunFrame(); // > 1.6 m from lobbyExit: it arms
                await host.PressAsync("lobbyExit");
                Assert.That(host.LevelId, Is.EqualTo("town"));
                host.RunFrames(5);
                await host.PressAsync("home");
                Assert.That(host.LevelId, Is.EqualTo("apartment"));
                Assert.That(host.PlayerZ, Is.EqualTo(-5.4), "on the lobby spawn, 0.6 m from the exit");
                for (int i = 0; i < 10; i++) { host.RunFrame(); await host.Settle(); }
                Assert.That(host.LevelId, Is.EqualTo("apartment"), "still in the lobby, not walked back out");
                host.PlayerZ = -2.5; host.RunFrame(); host.PlayerZ = -5.9; host.RunFrame(); await host.Settle();
                Assert.That(host.LevelId, Is.EqualTo("town"), "walking away and back into the door still goes through it");
            });
        }

        // ------------------------------------------------------------------ roam (ours)

        [Test]
        public void Roam_HandledKeepsRoaming_AValueEndsIt_LevelQualifiedKeysWork()
        {
            var (host, ctx) = Rig();
            int n = 0;
            var r = host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                var done = ctx.Roam(new RoamOptions
                {
                    On =
                    {
                        ["aptNotice"] = H(_ => { n++; return StoryContext.Handled; }),
                        ["town:center"] = H(_ => new JObject { ["went"] = "center" }),
                    },
                });
                Assert.That(ctx.Roaming, Is.True);
                await host.PressAsync("aptNotice");
                await host.PressAsync("aptNotice");
                Assert.That(done.IsCompleted, Is.False, "Handled keeps roaming");
                await host.PressAsync("center");
                return await done;
            });
            Assert.That(n, Is.EqualTo(2));
            Assert.That((string)r["went"], Is.EqualTo("center"));
            Assert.That(ctx.Roaming, Is.False);
        }

        [Test]
        public void Roam_ANullStringKeepsRoaming_JsonNullEndsIt()
        {
            var (host, ctx) = Rig();
            bool ready = false;
            var r = host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                var done = ctx.Roam(new RoamOptions
                {
                    On =
                    {
                        ["aptNotice"] = p => Task.FromResult<JToken>(ready ? "never" : null), // string-typed null: undefined
                        ["center"] = H(_ => JValue.CreateNull()),
                    },
                });
                await host.PressAsync("aptNotice");
                Assert.That(done.IsCompleted, Is.False);
                await host.PressAsync("center");
                return await done;
            });
            Assert.That(r.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(StoryContext.IsUndefined(null), Is.True);
            Assert.That(StoryContext.IsUndefined((JToken)(string)null), Is.True);
            Assert.That(StoryContext.IsUndefined(JValue.CreateNull()), Is.False);
            Assert.That(StoryContext.IsUndefined(StoryContext.Handled), Is.False);
        }

        [Test]
        public void Roam_HandlersRunModal_AndAPressDuringAHandlerIsIgnored()
        {
            var (host, ctx) = Rig();
            bool controlsInHandler = true; int calls = 0;
            var gate = new TaskCompletionSource<JToken>();
            host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                var done = ctx.Roam(new RoamOptions { On = { ["aptNotice"] = _ => { calls++; controlsInHandler = host.ControlsEnabled; return gate.Task; } } });
                await host.PressAsync("aptNotice");
                await host.PressAsync("aptNotice"); // busy: ignored
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(host.ControlsEnabled, Is.False, "a dialog is open");
                gate.SetResult("ok");
                Assert.That((string)await done, Is.EqualTo("ok"));
            });
            Assert.That(controlsInHandler, Is.False);
            Assert.That(host.ControlsEnabled, Is.True);
        }

        [Test]
        public void Roam_DoorsFollowTheTable_UnlessOverriddenOrDisabled()
        {
            var (host, ctx) = Rig();
            host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                // doors off: a door with no handler only says it is not the time
                var a = ctx.Roam(new RoamOptions { Doors = false, On = { ["aptNotice"] = H(_ => "x") } });
                await host.PressAsync("mart");
                Assert.That(host.LevelId, Is.EqualTo("town"));
                Assert.That(host.Toasts.Last(), Is.EqualTo("지금은 이 문으로 나갈 때가 아니다."));
                await host.PressAsync("aptNotice"); await a;

                // a chapter handler wins over the door table
                var b = ctx.Roam(new RoamOptions { On = { ["mart"] = H(_ => "mine") } });
                await host.PressAsync("mart");
                Assert.That((string)await b, Is.EqualTo("mine"));
                Assert.That(host.LevelId, Is.EqualTo("town"));

                // E follows the table; the roam goes on in the new level
                var c = ctx.Roam(new RoamOptions { On = { ["martExit"] = H(_ => "out") } });
                await host.PressAsync("mart");
                Assert.That(host.LevelId, Is.EqualTo("mart"));
                Assert.That(host.AmbienceName, Is.EqualTo("mart"));
                await host.PressAsync("martExit");
                Assert.That((string)await c, Is.EqualTo("out"));

                // a disabled walk-through door is not walked through
                await ctx.Goto("town", "store");
                host.Point("store").Enabled = false;
                int frames = 0;
                var d = ctx.Roam(new RoamOptions { Tick = _ => ++frames > 3 ? "time" : null });
                host.MoveTo(36, 14); host.RunFrame(); host.MoveTo(36, 10.6); host.RunFrame();
                Assert.That(host.LevelId, Is.EqualTo("town"));
                Assert.That((string)await d, Is.EqualTo("time"));
                host.Point("store").Enabled = true;
            });
        }

        [Test]
        public void Roam_WalkThroughDoorArmsOnlyAfter1_6m()
        {
            var (host, ctx) = Rig();
            host.Run(async () =>
            {
                await ctx.Goto("town", "store"); // spawn (36, 8.6), store door (36, 10.6): 2 m away
                var done = ctx.Roam(new RoamOptions { Tick = _ => host.LevelId == "store" ? "in" : null });
                host.MoveTo(36, 10.2); host.RunFrame(); // 0.4 m from the door, but no frame saw us more than 1.6 m away yet
                await host.Settle();
                Assert.That(host.LevelId, Is.EqualTo("town"), "not armed: never more than 1.6 m away in a frame");
                host.MoveTo(36, 12.4); host.RunFrame(); // 1.8 m: arms
                host.MoveTo(36, 11.2); host.RunFrame(); // 0.6 m: through
                await host.Settle();
                Assert.That(host.LevelId, Is.EqualTo("store"));
                host.RunFrame();
                Assert.That((string)await done, Is.EqualTo("in"));
            });
        }

        [Test]
        public void Roam_DeadlineFailsWithItsType_AndPausesWhileADialogIsOpen()
        {
            var (host, ctx) = Rig();
            var gate = new TaskCompletionSource<JToken>();
            var fail = host.RunExpectFail(async () =>
            {
                await ctx.Goto("town", "home");
                host.G.Day = 0; host.G.Minute = 20 * 60 + 29;
                var p = ctx.Roam(new RoamOptions { Deadline = new RoamDeadline { Day = 0, Minute = 20 * 60 + 30, Type = "late" }, On = { ["aptNotice"] = _ => gate.Task } });
                await host.PressAsync("aptNotice");
                host.G.Minute = 20 * 60 + 40;
                host.RunFrames(3);
                Assert.That(p.IsCompleted, Is.False, "a menu or dialog pauses deadlines");
                gate.SetResult(null);
                await host.Settle();
                host.RunFrame();
                await p;
            });
            Assert.That(fail.Type, Is.EqualTo("late"));
        }

        [Test]
        public void Roam_HealthZeroFailsDead_WithTheDeathCause()
        {
            var (host, ctx) = Rig();
            var f1 = host.RunExpectFail(async () => { await ctx.Goto("town", "home"); var p = ctx.Roam(); host.G.Stats.Health = 0; host.RunFrame(); await p; });
            Assert.That(f1.Type, Is.EqualTo("dead")); Assert.That((string)f1.Detail["why"], Is.EqualTo("hurt"));
            Assert.That(ctx.Roaming, Is.False);
            host.G.Stats.Health = 0;
            ctx.DeathCause = "smoke";
            var f2 = host.RunExpectFail(async () => await ctx.Roam());
            Assert.That((string)f2.Detail["why"], Is.EqualTo("smoke"));
        }

        [Test]
        public void Roam_AHandlerFailRejectsTheRoam()
        {
            var (host, ctx) = Rig();
            var f = host.RunExpectFail(async () =>
            {
                await ctx.Goto("town", "home");
                var p = ctx.Roam(new RoamOptions { On = { ["aptNotice"] = _ => { ctx.Fail("caught", new JObject { ["by"] = "x" }); return Task.FromResult<JToken>(null); } } });
                host.Press("aptNotice");
                await p;
            });
            Assert.That(f.Type, Is.EqualTo("caught")); Assert.That((string)f.Detail["by"], Is.EqualTo("x"));
        }

        [Test]
        public void Roam_RateRunsTheClockOnlyWhileControlsAreOn_AndIsRestoredAfter()
        {
            var (host, ctx) = Rig();
            host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                host.G.Day = 0; host.G.Minute = 600;
                ctx.Rate = 0;
                var p = ctx.Roam(new RoamOptions { Rate = 60, Tick = _ => host.G.Minute >= 602 ? "two" : null });
                Assert.That(ctx.Rate, Is.EqualTo(60));
                host.SetControls(false); host.RunFrames(90); host.SetControls(true);
                Assert.That(host.G.Minute, Is.EqualTo(600), "paused while a menu is open");
                host.RunFrames(29);
                Assert.That(host.G.Minute, Is.EqualTo(600));
                Assert.That((string)await p, Is.EqualTo("two"));
                Assert.That(host.G.Minute, Is.EqualTo(602));
                Assert.That(host.Clock, Is.EqualTo("D-day 10:02"));
            });
            Assert.That(ctx.Rate, Is.EqualTo(0));
        }

        [Test]
        public void Roam_ShelterHandlerTakesShelterPointsInTown()
        {
            var (host, ctx) = Rig();
            var r = host.Run(async () =>
            {
                await ctx.Goto("town", "shelter");
                var p = ctx.Roam(new RoamOptions { Shelter = pt => Task.FromResult<JToken>(pt.Id) });
                await host.PressAsync("shelterA");
                return await p;
            });
            Assert.That((string)r, Is.EqualTo("shelterA"));
        }

        [Test]
        public void Roam_CommonPointsAnswer_IdleLinesOtherwise()
        {
            var (host, ctx) = Rig();
            host.Answer("2");
            host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                var p = ctx.Roam(new RoamOptions { On = { ["aptNotice"] = H(_ => "end") } });
                long money = host.G.Money;
                await host.PressAsync(P("atm1", "atm", "ATM"));
                Assert.That(host.G.Money, Is.EqualTo(money - 100000));
                Assert.That(host.Toasts.Last(), Is.EqualTo("현금 10만 원을 찾아 지갑에 넣었다."));
                Assert.That(host.Talks.Last().Lines[1], Is.EqualTo($"계좌 잔액 {money.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}원. 얼마를 찾을까?"));
                await host.PressAsync("busStop");
                Assert.That(host.Talks.Last().Speaker, Is.EqualTo("버스 정류장"));
                await host.PressAsync(P("thing", "inspect", "이상한 것"));
                Assert.That(host.Toasts.Last(), Is.EqualTo("이상한 것: 지금은 할 일이 없다."));
                await host.PressAsync(new StoryPoint { Id = "thing2", Kind = "inspect", Label = "설명", Desc = "한 줄\n두 줄" });
                Assert.That(host.Talks.Last().Lines, Is.EqualTo(new[] { "한 줄", "두 줄" }));
                Assert.That(host.Talks.Last().Role, Is.EqualTo("살펴보기"));
                await host.PressAsync("aptNotice");
                await p;
            });
            Assert.That(host.G.Events.Any(e => e.Type == "cash" && (int)e.Details["n"] == 2));
        }

        [Test]
        public void Roam_WalletOnTheDeskTakesTheId()
        {
            var (host, ctx) = Rig();
            host.Run(async () =>
            {
                await ctx.Goto("apartment", "home");
                var p = ctx.Roam(new RoamOptions { Tick = _ => host.G.Knows("idTaken") ? "id" : null });
                await host.PressAsync("homeWallet");
                host.RunFrame();
                await p;
            });
            Assert.That(host.G.Items["id_card"].Bag, Is.EqualTo(1));
            Assert.That((string)host.Level("apartment").State["wallet"], Is.EqualTo("taken"));
            Assert.That((string)ctx.LevelStates["apartment"]["wallet"], Is.EqualTo("taken"));
            StringAssert.StartsWith("지갑과 신분증을 챙겼다.", host.Toasts.Last());
            Assert.That(host.EventTypes(), Does.Contain("id_taken"));
        }

        [Test]
        public void Near_IsTrueOnlyInThatLevelWithinR()
        {
            var (host, ctx) = Rig();
            host.Run(() => ctx.Goto("town", "home"));
            var near = ctx.Near("town", 78, 28, 3);
            Assert.That((bool)near(0), Is.True);
            Assert.That(ctx.Near("town", 78, 40)(0), Is.Null);
            Assert.That(ctx.Near("apartment", 78, 28)(0), Is.Null);
        }

        // ------------------------------------------------------------------ goto (ours)

        [Test]
        public void Goto_PicksTheEnvAndAmbience_AndKeepsShelterPowered()
        {
            var (host, ctx) = Rig();
            host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                Assert.That(host.Env, Is.EqualTo("day")); Assert.That(host.AmbienceName, Is.EqualTo("city"));
                ctx.OutdoorEnv = "dusk"; await ctx.Goto("town", "home");
                Assert.That(host.Env, Is.EqualTo("dusk")); Assert.That(host.AmbienceName, Is.EqualTo("night"));
                ctx.OutdoorEnv = "blackout"; await ctx.Goto("town", "home");
                Assert.That(host.AmbienceName, Is.EqualTo("blackout"));
                await ctx.Goto("apartment", "home");
                Assert.That(host.Env, Is.EqualTo("interior")); Assert.That(host.AmbienceName, Is.EqualTo("indoor"));
                ctx.Power = false;
                Assert.That(host.PowerOn, Is.False);
                await ctx.Goto("apartment", "home");
                Assert.That(host.Env, Is.EqualTo("interiorDark"));
                await ctx.Goto("shelter", "spot");
                Assert.That(host.Env, Is.EqualTo("underground")); Assert.That(host.PowerOn, Is.True, "the shelter's generator");
                ctx.Power = false;
                Assert.That(host.PowerOn, Is.True, "setting power in the shelter keeps it on");
                await ctx.Goto("store", "entry", "night");
                Assert.That(host.Env, Is.EqualTo("night"), "a chapter's env wins"); Assert.That(host.AmbienceName, Is.EqualTo("mart"));
                Assert.That(host.PowerOn, Is.False);
                await ctx.Goto("office", "entry");
                Assert.That(host.AmbienceName, Is.EqualTo("office"));
            });
        }

        [Test]
        public void Goto_EngineDefaultsWinOverTheTable_AndStaleKeysUseThem()
        {
            var (host, ctx) = Rig();
            host.Level("mart").Defaults["crowd"] = "busy";
            host.Run(async () =>
            {
                ctx.SetLevelState("mart", "crowd", "panic");
                await ctx.Goto("mart", "entry");
                Assert.That((string)host.State("crowd"), Is.EqualTo("panic"));
                ctx.LevelStates.Clear();
                await ctx.Goto("town", "home"); await ctx.Goto("mart", "entry");
                Assert.That((string)host.State("crowd"), Is.EqualTo("busy"));
            });
        }

        [Test]
        public void SetLevelState_AppliesNowOnlyInTheCurrentLevel()
        {
            var (host, ctx) = Rig();
            host.Run(async () =>
            {
                await ctx.Goto("town", "home");
                ctx.SetLevelState("apartment", "power", "off");
                Assert.That(host.Level("apartment").State.ContainsKey("power"), Is.False, "remembered, not sent");
                ctx.SetLevelState("town", "buses", "on");
                Assert.That((string)host.State("buses"), Is.EqualTo("on"));
                await ctx.Goto("apartment", "home");
                Assert.That((string)host.State("power"), Is.EqualTo("off"));
            });
            Assert.That(host.Warnings, Is.Empty, string.Join("\n", host.Warnings));
        }

        // ------------------------------------------------------------------ cues, modal, misc (ours)

        [Test]
        public void Refresh_ToastsTheMemoryCueOnce_CueSyncAndTheSettingSilenceIt()
        {
            var deed = Deeds.Table.First(d => d.Src == "new" && !string.IsNullOrEmpty(d.Cue));
            var (host, ctx) = Rig();
            ctx.Remember(deed.Id);
            ctx.Refresh();
            Assert.That(host.Toasts.Last(), Is.EqualTo(Deeds.CueText(deed.Id)));
            Assert.That(host.ToastLabels.Last(), Is.EqualTo("neighbor"));
            int n = host.Toasts.Count;
            ctx.Refresh(); ctx.Stat("tension", 1);
            Assert.That(host.Toasts.Count, Is.EqualTo(n), "once");

            var (h2, c2) = Rig();
            c2.Remember(deed.Id); c2.CueSync(); c2.Refresh();
            Assert.That(h2.Toasts, Is.Empty, "CueSync: already seen");

            var (h3, c3) = Rig(settings: new StorySettings { MemoryCue = false });
            c3.Remember(deed.Id); c3.Refresh();
            Assert.That(h3.Toasts, Is.Empty, "memory cue off");
            Assert.That(c3.Remember(deed.Id), deed.Each ? Is.Not.Null : Is.Null, "a deed is remembered once");
        }

        [Test]
        public void Refresh_SetsHudAndPlayerNumbers()
        {
            var (host, ctx) = Rig();
            host.G.Stats.Tension = 70;
            ctx.SpeedOverride = 0.5; ctx.ProfileSpeed = 0.8;
            ctx.Refresh();
            Assert.That(host.SpeedMul, Is.EqualTo(0.4).Within(1e-9));
            Assert.That(host.Tension, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(host.BagKg, Is.Null, "bag meter hidden until the bag is on");
            Assert.That(host.Clock, Is.EqualTo(GameRules.FormatClock(host.G)));
            host.G.SetKnow("bagOn", true);
            ctx.SpeedOverride = null; ctx.ProfileSpeed = null;
            ctx.Refresh();
            Assert.That(host.BagKg, Is.Not.Null);
            Assert.That(host.SpeedMul, Is.EqualTo(ctx.Rules.SpeedMul(host.G)));
            Assert.That(host.StaminaUse, Is.EqualTo(1 + ctx.Rules.BagLoad(host.G).WeightKg / ctx.Rules.Db.bagLimitKg));
        }

        [Test]
        public void Modal_LocksControlsWhileTheUiIsOpen_EvenWhenItThrows()
        {
            var (host, ctx) = Rig();
            var seen = new List<bool>();
            host.TalkPolicy = o => { seen.Add(host.ControlsEnabled); return o.Choices[0].Id; };
            host.CardPolicy = o => { seen.Add(host.ControlsEnabled); return "ok"; };
            host.Run(async () =>
            {
                Assert.That(await ctx.Say("나", "응?", new List<TalkChoice> { new("a", "A") }), Is.EqualTo("a"));
                Assert.That(await ctx.Card(new CardOptions { Title = "t", Actions = new List<CardAction> { new("ok", "확인", true) } }), Is.EqualTo("ok"));
                bool threw = false;
                try { await ctx.Modal(() => throw new InvalidOperationException()); } catch (InvalidOperationException) { threw = true; }
                Assert.That(threw, Is.True);
                Assert.That(host.ControlsEnabled, Is.True, "controls come back after a throw");
                Assert.That(await ctx.Modal(async () => { seen.Add(host.ControlsEnabled); await Task.Yield(); return 3; }), Is.EqualTo(3));
            });
            Assert.That(seen, Is.EqualTo(new[] { false, false, false }));
            Assert.That(host.ControlsEnabled, Is.True);
            Assert.That(host.Talks[0].Lines, Is.EqualTo(new[] { "응?" }));
            Assert.That(host.Talks[0].Role, Is.EqualTo(""));
        }

        [Test]
        public void Skip_UsesTheOverride_AndFallsBackToTheCardWhenItThrows()
        {
            var (host, ctx) = Rig();
            var lines = new[] { "사흘이 지났다." };
            bool controlsDuringCard = true;
            host.Run(async () =>
            {
                await ctx.Skip("D+1", "D+3", lines);
                Assert.That(host.TimeSkips.Count, Is.EqualTo(1));
                int over = 0;
                ctx.SkipOverride = (a, b, t) => { over++; return Task.CompletedTask; };
                await ctx.Skip("D+3", "D+5", lines);
                Assert.That(over, Is.EqualTo(1)); Assert.That(host.TimeSkips.Count, Is.EqualTo(1));
                ctx.SkipOverride = (a, b, t) => throw new Exception("no cine");
                await ctx.Skip("D+5", "D+7", lines);
                Assert.That(host.TimeSkips.Count, Is.EqualTo(2));
                Assert.That(host.TimeSkips[1].from, Is.EqualTo("D+5"));
                controlsDuringCard = host.ControlsEnabled;
            });
            Assert.That(controlsDuringCard, Is.True);
        }

        [Test]
        public void Caption_OffKeepsTheTimingButDrawsNothing_DeafAlwaysDraws()
        {
            var (host, ctx) = Rig(settings: new StorySettings { Captions = false });
            const string text = "열 글자짜리 자막입니다";
            host.Run(() => ctx.Caption(text, "나"));
            Assert.That(host.Captions, Is.Empty);
            Assert.That(host.Seconds, Is.EqualTo((1500 + text.Length * 60) / 1000.0).Within(0.05), "length-based wait, in frames");
            var (h2, c2) = Rig(settings: new StorySettings { Captions = false, Profile = "deaf" });
            h2.Run(() => c2.Caption("보인다"));
            Assert.That(h2.Captions, Is.EqualTo(new[] { "보인다" }));
            var (h3, c3) = Rig(settings: new StorySettings { Captions = false });
            h3.Run(() => c3.Caption("x", null, 500));
            Assert.That(h3.Seconds, Is.EqualTo(0.5).Within(0.05));
        }

        [Test]
        public void Note_StatPass_FactLine_Event_Fail_Load()
        {
            var (host, ctx) = Rig();
            host.Run(() => ctx.Goto("town", "home"));
            ctx.Note("대피소는 생활문화센터", "official");
            Assert.That(host.G.Info.Last().Place, Is.EqualTo("town"));
            Assert.That(host.Toasts.Last(), Is.EqualTo("메모: 대피소는 생활문화센터"));
            Assert.That(host.ToastLabels.Last(), Is.EqualTo("official"));
            ctx.Note("집 메모", "self", "apartment");
            Assert.That(host.G.Info.Last().Place, Is.EqualTo("apartment"));
            double h = host.G.Stats.Health;
            ctx.Stat("health", -10);
            Assert.That(host.G.Stats.Health, Is.EqualTo(h - 10)); Assert.That(host.Stats.Health, Is.EqualTo(h - 10));
            int m = host.G.Minute; ctx.Pass(15); Assert.That(host.G.Minute, Is.EqualTo(m + 15));
            var cite = ctx.Facts.Cite("radio_batt");
            Assert.That(ctx.FactLine("radio_batt"), Is.EqualTo($"{cite.Text}\n— {cite.Source}"));
            Assert.That(ctx.FactLine("nope"), Is.EqualTo(""));
            var e = ctx.Event("custom", new JObject { ["a"] = 1 });
            Assert.That(host.G.Events.Last(), Is.SameAs(e));
            var f = Assert.Throws<StoryFail>(() => ctx.Fail("late"));
            Assert.That(f.Type, Is.EqualTo("late")); Assert.That(f.Detail, Is.Not.Null);
            var copy = GameRules.FormatClock(host.G);
            var saved = SaveCodec.Save(host.G);
            var before = host.G;
            ctx.Load(saved);
            Assert.That(host.G, Is.Not.SameAs(before));
            Assert.That(ctx.G, Is.SameAs(host.G));
            Assert.That(GameRules.FormatClock(ctx.G), Is.EqualTo(copy));
            Assert.That(ctx.Place("mart").Label, Is.Not.Empty);
            Assert.That(ctx.Spawn("home").X, Is.EqualTo(78));
            ctx.Checkpoint("cp1");
            Assert.That(host.Checkpoints, Is.EqualTo(new[] { "cp1" }));
            ctx.Objective("가방 싸기", "현관으로", new[] { new ObjectiveStep("물", true) });
            Assert.That(host.Objectives.Last().title, Is.EqualTo("가방 싸기"));
            Assert.That(host.Objectives.Last().steps[0].Done, Is.True);
        }

        [Test]
        public void Nearest_SetsThePrompt()
        {
            var (host, ctx) = Rig();
            host.Run(async () => { await ctx.Goto("town", "home"); var p = ctx.Roam(new RoamOptions { Doors = false, Tick = _ => "x" }); host.WalkTo("aptNotice"); await p; });
            Assert.That(host.Prompt, Is.EqualTo(host.Point("aptNotice").Label));
            host.SetNearest(null);
            Assert.That(host.Prompt, Is.Null);
        }

        [Test]
        public void TimersRunOnFrames()
        {
            var (host, ctx) = Rig();
            var fired = new List<int>();
            ctx.After(0.1, () => fired.Add(1));
            ctx.After(0.5, () => fired.Add(2));
            host.RunFrames(4);
            Assert.That(fired, Is.EqualTo(new[] { 1 }));
            host.RunFrames(12);
            Assert.That(fired, Is.EqualTo(new[] { 1, 2 }));
        }
    }
}
