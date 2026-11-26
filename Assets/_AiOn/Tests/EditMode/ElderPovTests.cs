// 「304호의 계단」 (competition upgrade): the air-raid stairs from 304호 어르신's side. The rules (Game/ElderPovLogic.cs:
// what of the run reaches her, who walks with her, her pace and rests, the landings, the minutes, the review card) and
// the scene on FakeStoryHost (Story/ElderPovScene.cs: the offer, the point of view, each helper, the guided wheelchair
// descent, the clock and the body put back, one event).
using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class ElderPovTests
    {
        // ---------------------------------------------------------------- helpers (also used by the playthroughs)

        /// <summary>The descent as a player would walk it: the 3F stair top, the three landings, 1F (each once the rest is over).</summary>
        internal static void Descend(StoryPlaythroughTests.Rig r)
        {
            foreach (var (x, z, y, label) in Path)
                r.When(() => r.D.Pov == "elder" && r.Roam("apartment"), h => { h.MoveTo(x, z, y); h.RunFrame(); }, "elder: " + label);
        }

        static readonly (double x, double z, double y, string label)[] Path =
        {
            (5.0, 0.8, 5.6, "3F stairs"), (4.15, 2.6, 4.92, "first flight"), (4.15, 4.2, 4.2, "half landing"), (5.85, 0.9, 2.8, "2F"), (4.15, 4.2, 1.4, "half landing"), (5.85, 0.9, 0, "1F"),
        };

        static void QueueDescent(FakeStoryHost host, StoryContext ctx)
        {
            foreach (var (x, z, y, label) in Path)
                host.Queue(h => { h.MoveTo(x, z, y); h.RunFrame(); }, () => ctx.Roaming && host.ControlsEnabled && host.LevelId == "apartment" && host.PlayerPreset == "elder", label);
        }

        static (FakeStoryHost host, StoryContext ctx) Rig(string pick, Action<GameState> setup = null, StorySettings settings = null)
        {
            var host = new FakeStoryHost();
            var g = TestSupport.Rules.CreateGame(11);
            g.Chapter = "dday"; g.Day = 0; g.Minute = DDay.Alert + 14;
            if (pick != null) g.SetKnow("elderPick", pick);
            setup?.Invoke(g);
            var ctx = host.CreateContext(TestSupport.Rules, g, null, settings);
            ctx.SetPlayer = p => host.SetPlayerPreset(p);
            ctx.Power = false;
            ctx.SetLevelState("apartment", "power", "off");
            ctx.SetLevelState("apartment", "elder304", "shelter");
            return (host, ctx);
        }

        static void Play(FakeStoryHost host, StoryContext ctx, bool walk = true)
        {
            if (walk) QueueDescent(host, ctx);
            host.Run(async () =>
            {
                await ctx.Goto("shelter", "spot");
                await ElderPovScene.Run(ctx);
            });
        }

        static void Morning(GameState g, params string[] picks) =>
            GameRules.RecordEvent(g, TwentyTwoLogic.Event, new JObject { ["played"] = true, ["picks"] = new JArray(picks) });

        static JObject Result(FakeStoryHost host) => host.G.Events.Single(e => e.Type == ElderPovLogic.Event).Details;

        static List<string> AllLines(FakeStoryHost host) =>
            host.Talks.SelectMany(t => t.Lines).Concat(host.Captions).Concat(host.Toasts).Concat(host.Cards.SelectMany(c => c.Body)).ToList();

        // ---------------------------------------------------------------- the rules

        [Test]
        public void Read_TakesTheRunsThreadsToHer()
        {
            var g = TestSupport.Rules.CreateGame(3);
            var bare = ElderPovLogic.Read(g, null);
            Assert.That(bare.Pick, Is.Null);
            Assert.That(bare.Met || bare.MorningKnock || bare.CandleTip || bare.HeavyBag || bare.JeongminLight || bare.Prepared, Is.False);
            Assert.That(ElderPovLogic.Available(g), Is.False, "정민 never met her at the stairs: nothing to offer");

            g.SetKnow("elderPick", "help");
            Deeds.Remember(g, "elder_met");
            Deeds.Remember(g, "elder_candle_tip");
            Morning(g, "official", "elder", "down");
            GameRules.RecordEvent(g, "bag_heavy", new JObject { ["where"] = "stairs", ["weightKg"] = 9.1 });
            var s = ElderPovLogic.Read(g, "phone");
            Assert.That(s.Pick, Is.EqualTo("help"));
            Assert.That(s.Met && s.CandleTip && s.MorningKnock && s.MorningDown && s.HeavyBag && s.JeongminLight && s.Prepared, Is.True);
            Assert.That(ElderPovLogic.Available(g), Is.True);
            GameRules.RecordEvent(g, ElderPovLogic.Event, new JObject { ["played"] = false });
            Assert.That(ElderPovLogic.Available(g), Is.False, "once: a skip counts");

            var h = TestSupport.Rules.CreateGame(4);
            h.SetKnow("elderPick", "nope");
            Assert.That(ElderPovLogic.Available(h), Is.False, "an unknown answer");
            GameRules.RecordEvent(h, "bag_heavy", new JObject { ["where"] = "lift" });
            Morning(h, "official", "down");
            var t = ElderPovLogic.Read(h, "flashlight");
            Assert.That(t.HeavyBag, Is.False, "the 5장 lift is not her stairs");
            Assert.That(t.MorningKnock || t.Prepared, Is.False, "went down that morning without knocking");
        }

        [Test]
        public void WhoWalksWithHer_AndWhere_FollowsJeongminsAnswer()
        {
            Assert.That(ElderPovLogic.Picks.Select(ElderPovLogic.Helper), Is.EqualTo(new[] { "jeongmin", "manager", "neighbour" }));
            Assert.That(ElderPovLogic.MeetAt("help", false), Is.EqualTo("door"));
            Assert.That(ElderPovLogic.MeetAt("manager", true), Is.EqualTo("door"), "she waited for 박 소장");
            Assert.That(ElderPovLogic.MeetAt("manager", false), Is.EqualTo("2F"));
            Assert.That(ElderPovLogic.MeetAt("go", true), Is.EqualTo("2F"), "nobody was told: the 203호 neighbour on 2F");
            // manager, but 정민 walked out past the 1F window: 박 소장 never heard (know.elderTold)
            Assert.That(new ElderPovSetup { Pick = "manager", Told = true }.Way, Is.EqualTo("manager"));
            Assert.That(new ElderPovSetup { Pick = "manager" }.Way, Is.EqualTo("untold"));
            Assert.That(new ElderPovSetup { Pick = "go" }.Way, Is.EqualTo("go"));
            Assert.That(ElderPovLogic.Helper("untold"), Is.EqualTo("neighbour"));
            Assert.That(ElderPovLogic.MeetAt("untold", true), Is.EqualTo("2F"));
            var told = TestSupport.Rules.CreateGame(5);
            told.SetKnow("elderPick", "manager");
            Assert.That(ElderPovLogic.Read(told, null).Way, Is.EqualTo("untold"));
            told.SetKnow("elderTold", true);
            Assert.That(ElderPovLogic.Read(told, null).Told, Is.True);
            Assert.That(ElderPovLogic.HelperLight("jeongmin", false), Is.False, "정민 without a light");
            Assert.That(ElderPovLogic.HelperLight("jeongmin", true) && ElderPovLogic.HelperLight("manager", false) && ElderPovLogic.HelperLight("neighbour", false), Is.True);
            Assert.That(new[] { "jeongmin", "manager", "neighbour" }.Select(ElderPovLogic.HelperPreset), Is.EqualTo(new[] { "jeongmin", "manager", "crowd3" }));
        }

        [Test]
        public void HerPace_IsSlowerOnStairs_InTheDark_AndUnderABundle_AndAlwaysSlow()
        {
            double flat = ElderPovLogic.Pace(false, false, false, false), stairs = ElderPovLogic.Pace(true, false, false, false);
            Assert.That(stairs, Is.LessThan(flat));
            Assert.That(ElderPovLogic.Pace(true, true, false, false), Is.GreaterThan(stairs), "a light helps");
            Assert.That(ElderPovLogic.Pace(true, true, true, false), Is.GreaterThan(ElderPovLogic.Pace(true, true, false, false)), "an arm helps");
            Assert.That(ElderPovLogic.Pace(true, false, false, true), Is.LessThan(stairs), "the bundle slows her");
            foreach (var a in new[] { false, true }) foreach (var b in new[] { false, true }) foreach (var c in new[] { false, true }) foreach (var d in new[] { false, true })
            {
                var p = ElderPovLogic.Pace(a, b, c, d);
                Assert.That(p, Is.InRange(ElderPovLogic.MinPace, ElderPovLogic.MaxPace));
                Assert.That(p, Is.LessThan(StoryDirector.Profiles["mobility"].Speed), "slower than the 보행 장애 mode's walk");
            }
            Assert.That(ElderPovLogic.RestSeconds(false, false), Is.GreaterThan(ElderPovLogic.RestSeconds(true, false)));
            Assert.That(ElderPovLogic.RestSeconds(true, false), Is.GreaterThan(ElderPovLogic.RestSeconds(false, true)));
        }

        [Test]
        public void Landings_AreFoundInTheStairwell_AndNowhereElse()
        {
            Assert.That(ElderPovLogic.LandingAt(4.15, 4.2, 4.2), Is.EqualTo(1));
            Assert.That(ElderPovLogic.LandingAt(5.85, 2.8, 0.9), Is.EqualTo(2));
            Assert.That(ElderPovLogic.LandingAt(4.15, 1.4, 4.2), Is.EqualTo(3));
            Assert.That(ElderPovLogic.LandingAt(5.85, 0, 0.9), Is.EqualTo(4));
            Assert.That(ElderPovLogic.LandingAt(4, 0, -5.4), Is.EqualTo(4), "the lobby is 1F too");
            Assert.That(ElderPovLogic.LandingAt(15, 5.6, -0.95), Is.EqualTo(0), "her door");
            Assert.That(ElderPovLogic.LandingAt(4.15, 5.6, 0.8), Is.EqualTo(0), "the 3F stair top");
            Assert.That(ElderPovLogic.LandingAt(4.15, 4.9, 2.6), Is.EqualTo(0), "mid-flight");
            // a flight's walk height just above a landing is still the flight
            var flightD = Elder.ApartmentWalk[7];
            double atLandingEdge = Elder.GroundAt(Elder.ApartmentWalk, 4.15, 3.3, 4.6) ?? 0;
            Assert.That(ElderPovLogic.LandingAt(4.15, atLandingEdge, 3.3), Is.EqualTo(0));
            Assert.That(flightD.Axis, Is.EqualTo("z"));
            // the guided descent stands on each landing in turn
            for (int i = 1; i < ElderPovScene.Steps.Length; i++)
            {
                var p = ElderPovScene.Steps[i];
                Assert.That(ElderPovLogic.LandingAt(p.X, p.Y, p.Z), Is.EqualTo(i), $"step {i}");
                Assert.That(Elder.ZoneAt(Elder.ApartmentWalk, p.X, p.Z, p.Y), Is.GreaterThanOrEqualTo(0), $"step {i} on the walk mesh");
            }
            var top = ElderPovScene.Steps[0];
            Assert.That(Elder.ZoneAt(Elder.ApartmentWalk, top.X, top.Z, top.Y), Is.GreaterThanOrEqualTo(0));
            Assert.That(Elder.ZoneAt(Elder.ApartmentWalk, ElderPovScene.Door.X, ElderPovScene.Door.Z, ElderPovScene.Door.Y), Is.GreaterThanOrEqualTo(0), "her door is on the corridor");
        }

        [Test]
        public void Minutes_CountReadying_Waiting_TheFlightsAlone_AndTheRests()
        {
            var helped = new ElderPovSetup { Pick = "help", MorningKnock = true, JeongminLight = true };
            Assert.That(ElderPovLogic.Minutes(helped, false, false, 3), Is.EqualTo(1 + 1 + 2 + 2 + 3));
            var waited = new ElderPovSetup { Pick = "manager", Told = true };
            Assert.That(ElderPovLogic.Minutes(waited, true, false, 3), Is.EqualTo(2 + 3 + 1 + 2 + 2 + 3), "waited at the door, then together");
            Assert.That(ElderPovLogic.Minutes(waited, false, false, 3), Is.EqualTo(2 + 1 + 4 + 2 + 3), "alone in the dark to 2F");
            Assert.That(ElderPovLogic.Minutes(new ElderPovSetup { Pick = "manager" }, true, false, 3), Is.EqualTo(2 + ElderPovLogic.UntoldWait + 1 + 4 + 2 + 3),
                "untold: she waits a while, then goes alone in the dark to 2F (no 'waited' without 박 소장)");
            var alone = new ElderPovSetup { Pick = "go" };
            Assert.That(ElderPovLogic.Minutes(alone, false, true, 3), Is.EqualTo(4 + 1 + 4 + 2 + 3), "the bundle");
            alone.MorningKnock = true;
            Assert.That(ElderPovLogic.Minutes(alone, false, false, 3), Is.EqualTo(1 + 1 + 3 + 2 + 3), "her own flashlight");
            Assert.That(ElderPovLogic.Minutes(helped, false, false, 3), Is.LessThan(ElderPovLogic.Minutes(new ElderPovSetup { Pick = "go" }, false, true, 3)));
        }

        [Test]
        public void TheReviewCard_SaysWhatReachedHer_WithSourcedLines_AndMarksTheGameNumbers()
        {
            var facts = FakeStoryHost.LoadFacts();
            Func<string, string> fact = id => facts.Text(id);
            Func<string, string> source = id => facts.Cite(id)?.Source;
            var s = new ElderPovSetup { Pick = "help", MorningKnock = true, HeavyBag = true, CandleTip = true, JeongminLight = true };
            var r = new ElderPovResult { Pick = "help", Helper = "jeongmin", MeetAt = "door", Prepared = true, Light = true, Candle = "tip", Rests = 3, Minutes = 9 };
            var lines = ElderPovLogic.ReviewLines(s, r, fact, source);
            Assert.That(lines[0], Does.Contain(facts.Text("alert_stairs")), "the same sentence");
            Assert.That(lines.Any(l => l.Contains("약 9분") && l.Contains("[게임 수치]") && l.Contains("3번") && l.Contains("두 층") && l.Contains("함께 걸었")));
            Assert.That(lines.Any(l => l.Contains("세 층") || l.Contains("금방이던")), Is.False, "3F to 1F is two floors; 정민 walked at her pace");
            Assert.That(lines.Any(l => l.Contains("배낭")), "the heavy bag");
            Assert.That(lines.Any(l => l.Contains("그날 아침") && l.Contains("손전등")), "the 「22분」 knock");
            Assert.That(lines.Any(l => l.Contains("촛불 꼭 꺼 주세요")), "the candle tip");
            foreach (var id in new[] { "alert_night", "power_prep", "alert_caution" }) Assert.That(lines.Any(l => l.Contains(facts.Text(id))), id);
            int caution = lines.FindIndex(l => l.Contains(facts.Text("alert_caution")));
            Assert.That(lines.FindIndex(l => l.Contains("경계경보 때부터")), Is.InRange(0, caution - 1), "the 경계경보 sentence is introduced as the earlier alert");
            Assert.That(lines.Any(l => l.Contains(facts.Text("docs_take"))), Is.False, "prepared: no quick pack");
            Assert.That(lines.Last(), Does.StartWith("<size=18><color=#94a3b8>출처: "));
            Assert.That(lines.Any(l => l.Contains(facts.Text("candle_rules"))), Is.False);

            var dark = new ElderPovSetup { Pick = "go" };
            var d = new ElderPovResult { Pick = "go", Helper = "neighbour", MeetAt = "2F", Bundle = true, Candle = "left", Rests = 3, Minutes = 14 };
            var dl = ElderPovLogic.ReviewLines(dark, d, fact, source);
            Assert.That(dl.Any(l => l.Contains("203호")) && dl.Any(l => l.Contains("유도등")) && dl.Any(l => l.Contains("사진첩")));
            Assert.That(dl.Any(l => l.Contains(facts.Text("candle_rules"))), "the candle left on: its fact");
            Assert.That(dl.Any(l => l.Contains("경보가 해제되면")), "nobody goes back up during the alert");
            Assert.That(dl.Any(l => l.Contains("다시 올라가 껐")), Is.False);
            Assert.That(dl.Any(l => l.Contains("세 층")), Is.False);
            Assert.That(dl.Any(l => l.Contains("통장")), Is.False, "the slow pack is keepsakes, never the documents");
            var mg = new ElderPovResult { Pick = "manager", Way = "manager", Helper = "manager", MeetAt = "2F", Candle = "out", Rests = 3, Minutes = 12 };
            Assert.That(ElderPovLogic.ReviewLines(new ElderPovSetup { Pick = "manager", Told = true }, mg, fact, source).Any(l => l.Contains("2층에서 만났습니다")));
            var quick = ElderPovLogic.ReviewLines(new ElderPovSetup { Pick = "manager", Told = true }, mg, fact, source);
            Assert.That(quick.Any(l => l.Contains("신분증")) && quick.Any(l => l.Contains(facts.Text("docs_take"))), "the quick pack takes 신분증: its fact");
            // manager, but never told: no 'told by 정민'
            var un = new ElderPovResult { Pick = "manager", Way = "untold", Helper = "neighbour", MeetAt = "2F", Candle = "out", Rests = 3, Minutes = 13 };
            var ul = ElderPovLogic.ReviewLines(new ElderPovSetup { Pick = "manager" }, un, fact, source);
            Assert.That(ul.Any(l => l.Contains("들르지 못했습니다") && l.Contains("203호")));
            Assert.That(ul.Any(l => l.Contains("정민이 알린")), Is.False);
            // helped down by 정민 without a light: dark all the way, together (no 2F light)
            var hd = new ElderPovResult { Pick = "help", Way = "help", Helper = "jeongmin", MeetAt = "door", Candle = "reminded", Rests = 3, Minutes = 10 };
            var hl = ElderPovLogic.ReviewLines(new ElderPovSetup { Pick = "help" }, hd, fact, source);
            Assert.That(hl.Any(l => l.Contains("둘 다 손전등이 없어") && l.Contains("1층까지")));
            Assert.That(hl.Any(l => l.Contains("2층부터는")), Is.False);
            Assert.That(hl.Any(l => l.Contains("정민이 끄고 가자고")) && hl.Any(l => l.Contains(facts.Text("candle_rules"))));
            // never a word of pity or blame, never a number given as a fact
            foreach (var l in lines.Concat(dl).Concat(ul).Concat(hl))
                foreach (var bad in new[] { "불쌍", "가엾", "탓", "늦었", "실패", "1337", "111", "113" })
                    Assert.That(l, Does.Not.Contain(bad), bad);
        }

        // ---------------------------------------------------------------- the scene

        [Test]
        public void Help_WithTheMorningKnock_WalksDownWithJeongmin_InHerOwnLight_AndPutsEverythingBack()
        {
            var (host, ctx) = Rig("help", g =>
            {
                Morning(g, "official", "ready", "elder", "down");
                Deeds.Remember(g, "elder_candle_tip");
                g.SetKnow("bagOn", true);
            });
            ctx.LightSource = "flashlight";
            int before = host.G.Events.Count, minute = host.G.Minute;
            Play(host, ctx);

            Assert.That(host.Cards.First().Title, Is.EqualTo(ElderPovLogic.OfferTitle));
            Assert.That(host.Cards.First().Kind, Is.EqualTo("decision"), "an optional choice, not a fact card");
            Assert.That(host.Calls("preset").Select(c => c.Text), Is.EqualTo(new[] { "elder", "jeongmin" }), "her body, then 정민's again");
            Assert.That(host.Calls("enter").Select(c => c.Text), Is.EqualTo(new[] { "shelter:spot", "apartment:corridor3F", "shelter:spot" }));
            // the same sentence 정민 heard, from her side
            Assert.That(host.Talks.Any(t => t.Speaker == ElderPovScene.Speaker && t.Lines[0].Contains("302호 정민 씨예요? 사이렌이 울리는데 계단이 캄캄해서")));
            Assert.That(host.Talks.Any(t => t.Lines.Any(l => l.Contains("손잡이 쪽에 서 줘요"))));
            Assert.That(host.Talks.Any(t => t.Choices?.Count > 0 && t.Speaker == ElderPovScene.Inner), Is.False, "prepared, the candle remembered: no choice at her door");
            Assert.That(host.Captions.Count(c => c.Contains("숨") || c.Contains("층")), Is.GreaterThanOrEqualTo(3), "a rest on each landing");
            Assert.That(host.Calls("flashlight").First().Text, Is.EqualTo("on"));
            Assert.That(host.Flashlight, Is.False, "off again");

            var o = Result(host);
            Assert.That((bool)o["played"] && (bool)o["prepared"] && (bool)o["light"], Is.True);
            Assert.That((string)o["helper"], Is.EqualTo("jeongmin"));
            Assert.That((string)o["meetAt"], Is.EqualTo("door"));
            Assert.That((string)o["candle"], Is.EqualTo("tip"));
            Assert.That((int)o["rests"], Is.EqualTo(3));
            Assert.That((int)o["minutes"], Is.EqualTo(ElderPovLogic.Minutes(ElderPovLogic.Read(host.G, "flashlight"), false, false, 3)));
            Assert.That(host.G.Events.Count, Is.EqualTo(before + 1), "one event, nothing else of hers in 정민's record");
            Assert.That(host.G.Minute, Is.EqualTo(minute), "정민's clock is his again");
            Assert.That(host.G.Events.Last().Minute, Is.EqualTo(minute));
            Assert.That(ctx.SpeedOverride, Is.Null);
            Assert.That(ctx.LightSource, Is.EqualTo("flashlight"));
            Assert.That(ctx.Calm, Is.False);
            Assert.That((string)ctx.LevelStates["apartment"]["elder304"], Is.EqualTo("shelter"), "her state as it was");
            Assert.That(host.Level("apartment").Spawned, Is.Empty, "the helpers are gone");
            var review = host.Cards.Single(c => c.Title == ElderPovLogic.ReviewTitle);
            Assert.That(review.Kind, Is.EqualTo("record"));
            Assert.That(review.Body.Any(l => l.Contains("배낭")), Is.False, "정민's bag was not heavy");
            Assert.That(host.Log.Last(c => c.Kind == "chapter").Text, Is.EqualTo("2장"));
            Assert.That(host.Objectives.Last().title, Is.EqualTo("대피소 안에서"));
        }

        [Test]
        public void ThePointOfViewCard_ComesOverBlack_NotOverTheShelter_AndThePictureComesBack()
        {
            var (host, ctx) = Rig("help");
            Play(host, ctx);
            var card = host.Log.Single(c => c.Kind == "card" && c.Text == "시점 전환 · 304호 어르신");
            var before = host.Log.Where(c => c.Seq < card.Seq).ToList();
            var blackAt = before.LastOrDefault(c => c.Kind == "fade");
            Assert.That(blackAt?.Text, Is.EqualTo("1"), "the shelter fades to black before her card");
            Assert.That(before.Last(c => c.Kind == "enter").Text, Is.EqualTo("shelter:spot"), "still in the shelter: the black hides it");
            var aptAt = host.Log.Single(c => c.Kind == "enter" && c.Text == "apartment:corridor3F");
            Assert.That(aptAt.Seq, Is.GreaterThan(card.Seq), "her corridor after the card");
            Assert.That(host.Log.First(c => c.Kind == "fade" && c.Seq > aptAt.Seq).Text, Is.EqualTo("0"), "the picture back in her corridor");
            Assert.That(host.Black, Is.EqualTo(0), "no black left at the end");
            Assert.That(host.Log.Count(c => c.Kind == "fade" && c.Text == "1"), Is.EqualTo(1), "one fade to black, for the card only");
        }

        [Test]
        public void ASkippedOffer_NeverGoesBlack()
        {
            var (host, ctx) = Rig("go");
            host.AnswerCard("skip");
            Play(host, ctx, walk: false);
            Assert.That(host.Calls("fade"), Is.Empty);
        }

        [Test]
        public void Skip_RecordsOnce_AndGoesNowhere()
        {
            var (host, ctx) = Rig("go");
            host.AnswerCard("skip");
            Play(host, ctx, walk: false);
            Assert.That(host.Calls("enter").Select(c => c.Text), Is.EqualTo(new[] { "shelter:spot" }));
            Assert.That((bool)Result(host)["played"], Is.False);
            Assert.That(ElderPovLogic.Available(host.G), Is.False);
            host.Run(() => ElderPovScene.Run(ctx));
            Assert.That(host.Cards.Count(c => c.Title == ElderPovLogic.OfferTitle), Is.EqualTo(1), "not offered again");
        }

        [Test]
        public void NoAnswerAtTheStairs_NoOffer()
        {
            var (host, ctx) = Rig(null);
            Play(host, ctx, walk: false);
            Assert.That(host.Cards, Is.Empty);
            Assert.That(host.G.Events.Any(e => e.Type == ElderPovLogic.Event), Is.False);
        }

        [Test]
        public void Manager_Waited_BringsHimUpToHerDoor()
        {
            var (host, ctx) = Rig("manager", g => g.SetKnow("elderTold", true));
            host.AnswerWhen(o => o.Speaker == ElderPovScene.Inner && o.Choices.Any(c => c.Id == "wait"), "wait");
            Play(host, ctx);
            var o = Result(host);
            Assert.That((bool)o["waited"], Is.True);
            Assert.That((string)o["meetAt"], Is.EqualTo("door"));
            Assert.That((bool)o["light"], Is.True, "박 소장's flashlight");
            Assert.That(host.Talks.Count(t => t.Speaker == "박영수 소장"), Is.GreaterThanOrEqualTo(2));
            Assert.That(host.Talks.Any(t => t.Lines[0].Contains("1층 관리사무소에 알리고 먼저 갈게요")));
        }

        [Test]
        public void Manager_StartedDown_MeetsHimOn2F()
        {
            var (host, ctx) = Rig("manager", g => g.SetKnow("elderTold", true));
            host.AnswerWhen(o => o.Speaker == ElderPovScene.Inner && o.Choices.Any(c => c.Id == "wait"), "go");
            Play(host, ctx);
            var o = Result(host);
            Assert.That((bool)o["waited"], Is.False);
            Assert.That((string)o["meetAt"], Is.EqualTo("2F"));
            Assert.That((bool)o["light"], Is.False, "alone in the dark until 2F");
            Assert.That(host.Toasts.Any(t => t.Contains("발끝으로 계단 끝을 더듬어요")));
            Assert.That(host.Talks.Any(t => t.Speaker == "박영수 소장" && t.Lines[0].Contains("벌써 여기까지")));
        }

        [Test]
        public void Go_Unprepared_InTheDark_TheNeighbourJoinsOn2F_AndTheCandleLeftWaitsForTheAllClear()
        {
            var (host, ctx) = Rig("go");
            host.AnswerWhen(o => o.Choices.Any(c => c.Id == "leave"), "leave");
            host.AnswerWhen(o => o.Choices.Any(c => c.Id == "bundle"), "bundle");
            Play(host, ctx);
            var o = Result(host);
            Assert.That((string)o["helper"], Is.EqualTo("neighbour"));
            Assert.That((string)o["candle"], Is.EqualTo("left"));
            Assert.That((bool)o["bundle"], Is.True);
            Assert.That(host.Talks.Any(t => t.Speaker == "203호 이웃" && t.Lines[0].Contains("휴대폰 불빛")));
            Assert.That(host.Talks.Any(t => t.Speaker == "박영수 소장" && t.Lines[0].Contains("촛불")));
            Assert.That(host.Talks.Any(t => t.Speaker == "박영수 소장" && t.Lines[0].Contains("다시 올라가지 말고")), "nobody goes back up during the alert");
            Assert.That(host.Talks.Any(t => t.Lines.Any(l => l.Contains("끄고 올게요") && t.Speaker == "박영수 소장")), Is.False);
            Assert.That(host.Toasts, Has.Member(ctx.Facts.Text("candle_rules")), "the candle rule, as at 정민's own door");
            Assert.That(host.Talks.Any(t => t.Lines.Any(l => l.Contains("젊은 사람은 먼저 가야지요"))), "no blame for going first");
            Assert.That(host.Talks.Any(t => t.Lines.Any(l => l.Contains("내가 여기 있는 줄 모르실 거예요"))), "why she does not wait for 박 소장");
            Assert.That(host.Talks.Any(t => t.Choices?.Any(c => c.Label.Contains("통장")) == true), Is.False, "documents are never the slow option");
            Assert.That((int)o["minutes"], Is.GreaterThan(10));
        }

        [Test]
        public void Manager_ButNeverTold_SheWaitsAWhile_ThenGoesOn_AndNobodySaysHeHeardFromJeongmin()
        {
            var (host, ctx) = Rig("manager"); // no elderTold: 정민 walked out past the 1F window
            Play(host, ctx);
            var o = Result(host);
            Assert.That((string)o["way"], Is.EqualTo("untold"));
            Assert.That((string)o["helper"], Is.EqualTo("neighbour"));
            Assert.That((string)o["meetAt"], Is.EqualTo("2F"));
            Assert.That((bool)o["waited"], Is.False);
            Assert.That(host.Talks.Any(t => t.Choices?.Any(c => c.Id == "wait") == true), Is.False, "nobody is coming: no waiting choice");
            Assert.That(host.Talks.Any(t => t.Lines[0].Contains("1층 관리사무소에 알리고 먼저 갈게요")), "the same sentence 정민 said");
            Assert.That(AllLines(host).Any(l => l.Contains("정민 씨한테 들었어요") || l.Contains("정민 씨가 소장님께 알려") || l.Contains("정민이 알린")), Is.False);
            Assert.That(host.Talks.Any(t => t.Speaker == "203호 이웃"));
            Assert.That(host.Talks.Any(t => t.Lines.Any(l => l.Contains("내 걸음으로 가 봐요"))));
            Assert.That((int)o["minutes"], Is.EqualTo(ElderPovLogic.Minutes(ElderPovLogic.Read(host.G, null), false, (bool)o["bundle"], 3)));
        }

        [Test]
        public void Help_TheCandleLeftLit_JeongminSaysSo_AtHerDoor()
        {
            var (host, ctx) = Rig("help");
            host.AnswerWhen(o => o.Choices.Any(c => c.Id == "leave"), "leave");
            Play(host, ctx);
            var o = Result(host);
            Assert.That((string)o["candle"], Is.EqualTo("reminded"));
            Assert.That(host.Talks.Any(t => t.Speaker == "정민" && t.Lines[0].Contains("촛불은 끄고 가요")));
            Assert.That(host.Talks.Any(t => t.Speaker == "박영수 소장" && t.Lines[0].Contains("촛불")), Is.False, "put out before she left");
            var review = host.Cards.Single(c => c.Title == ElderPovLogic.ReviewTitle);
            Assert.That(review.Body.Any(l => l.Contains("정민이 끄고 가자고")));
            Assert.That(review.Body.Any(l => l.Contains("둘 다 손전등이 없어")), "정민 had no light either");
        }

        [Test]
        public void Wheelchair_TakesTheFlightsOneChoiceAtATime()
        {
            var (host, ctx) = Rig("help", null, new StorySettings { Profile = "wheelchair" });
            Play(host, ctx, walk: false); // no roam: every step is a choice
            var o = Result(host);
            Assert.That((bool)o["guided"], Is.True);
            Assert.That((int)o["rests"], Is.EqualTo(3));
            Assert.That(host.Talks.Count(t => t.Choices?.Any(c => c.Id == "step") == true), Is.EqualTo(4));
            Assert.That(host.Cards.Any(c => c.Body.Any(l => l.Contains("휠체어 체험 모드") && l.Contains("반 층씩"))));
            Assert.That(AllLines(host).Any(l => l.Contains("한 층의 반")), Is.False);
            Assert.That(host.PlayerPreset, Is.EqualTo("jeongmin"));
        }

        [Test]
        public void LowVision_HearsTheLinesAndChoices_Deaf_ReadsTheSiren()
        {
            var spoken = new List<string>();
            var (host, ctx) = Rig("go", null, new StorySettings { Profile = "lowvision" });
            ctx.Speak = t => spoken.Add(t);
            Play(host, ctx);
            Assert.That(spoken.Any(t => t.Contains("302호 정민 씨예요?")));
            Assert.That(spoken.Any(t => t.Contains("1번, 촛불을 끄고 나선다")));
            Assert.That(spoken.Any(t => t.Contains(ElderPovLogic.ReviewTitle)));
            Assert.That(spoken.Any(t => t.Contains("<color")), Is.False, "tags stripped");
            Assert.That(spoken.Any(t => t.Contains("발끝으로 계단 끝을 더듬어요")), "the dark stairs toast is heard too");
            Assert.That(spoken.Any(t => t.Contains("난간을 잡고 1층까지 천천히 내려갑니다")), "her goal, once");
            Assert.That(spoken.Any(t => t.Contains(ctx.Facts.Text("candle_rules"))) || !host.Toasts.Contains(ctx.Facts.Text("candle_rules")));

            var (h2, c2) = Rig("help", null, new StorySettings { Profile = "deaf" });
            Play(h2, c2);
            Assert.That(h2.Talks.First(t => t.Speaker == ElderPovScene.Inner).Lines[0], Does.Contain("휴대폰에 빨간 글씨"));
        }

        [Test]
        public void HerInnerLines_AreHonorific_AndNeverPitiful()
        {
            var (host, ctx) = Rig("go");
            Play(host, ctx);
            var inner = host.Talks.Where(t => t.Speaker == ElderPovScene.Inner).SelectMany(t => t.Lines)
                .Concat(host.Calls("caption").Select(c => c.Text)).ToList();
            Assert.That(inner, Is.Not.Empty);
            foreach (var l in AllLines(host))
                foreach (var bad in new[] { "불쌍", "가엾", "짐이 되", "민폐", "늙은이", "다쳤", "부상", "피가" })
                    Assert.That(l, Does.Not.Contain(bad), bad);
            foreach (var l in host.Talks.Where(t => t.Speaker == ElderPovScene.Inner).SelectMany(t => t.Lines))
                Assert.That(l.TrimEnd('.', '…', ' ', '"'), Does.Match("(요|[?])$"), "polite: " + l);
        }

        [Test]
        public void TheDday_ElderResume_StartsSeated_PlaysHerStairs_ThenTheShelterGoesOn()
        {
            var (host, ctx) = Rig("help");
            ctx.Resume = DDay.ElderResume;
            var keepS = DDay.SeoyeonPart; var keepC = DDay.CprPart;
            DDay.SeoyeonPart = _ => System.Threading.Tasks.Task.CompletedTask;
            DDay.CprPart = c => { GameRules.RecordEvent(c.G, "stub_cpr", new JObject()); return System.Threading.Tasks.Task.CompletedTask; };
            try
            {
                QueueDescent(host, ctx);
                host.Run(() => DDay.Run(ctx));
            }
            finally { DDay.SeoyeonPart = keepS; DDay.CprPart = keepC; }
            Assert.That(host.Calls("enter").Select(c => c.Text).Take(3), Is.EqualTo(new[] { "shelter:spot", "apartment:corridor3F", "shelter:spot" }));
            Assert.That(host.Cards.Any(c => c.Title == ElderPovLogic.OfferTitle), Is.False, "the moment starts as her");
            var types = host.G.Events.Select(e => e.Type).ToList();
            Assert.That(types.IndexOf(ElderPovLogic.Event), Is.LessThan(types.IndexOf("stub_cpr")), "before the CPR");
            Assert.That(types, Has.No.Member("arrived"), "the arrival is not played again");
            Assert.That(SceneSelect.Get("elderpov").Key, Is.EqualTo(DDay.ElderResume));
        }
    }
}
