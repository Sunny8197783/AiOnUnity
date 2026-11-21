// 사전/사후 퀴즈 and 플레이테스트 기록 내보내기 (competition upgrade): the items against the checked facts, the shuffles,
// the comparison, the learning report's quiz part, the export (JSON, CSV, files) and the story hooks (a new run, the
// ending, the teacher mode).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class QuizTests
    {
        static Facts F => FakeStoryHost.LoadFacts();

        /// <summary>The answers json a screen hands back: pick(item) per concept (null = not answered).</summary>
        internal static JObject Answers(string phase, Func<QuizItem, string> pick, uint seed = 5, IEnumerable<string> only = null)
        {
            var arr = new JArray();
            foreach (var i in QuizLogic.Items.Where(x => only == null || only.Contains(x.Id)))
            {
                var p = pick(i);
                if (p != null) arr.Add(new JObject { ["concept"] = i.Id, ["option"] = p });
            }
            return new JObject { ["phase"] = phase, ["seed"] = seed, ["answers"] = arr };
        }

        static string Right(QuizItem i, string phase) => i.Ask(phase).Correct;
        static string Wrong(QuizItem i, string phase) => i.Ask(phase).Options.First(o => o.Id != i.Ask(phase).Correct).Id;

        static GameState Run(params (string type, JObject details)[] events)
        {
            var g = new GameState { Seed = 1234 };
            int seq = 0;
            foreach (var (type, details) in events) g.Events.Add(new GameEvent { Seq = seq++, Type = type, Details = details ?? new JObject() });
            return g;
        }

        // ---------------------------------------------------------------- the items

        [Test]
        public void Items_AreSevenConcepts_EachAskedTwiceInOtherWords_WithOneRightAnswer()
        {
            Assert.That(QuizLogic.Items.Select(i => i.Id), Is.EqualTo(new[] { "alert", "lift", "look", "cpr", "ext", "report", "info" }));
            foreach (var i in QuizLogic.Items)
            {
                Assert.That(i.Label, Is.Not.Empty);
                foreach (var phase in new[] { QuizLogic.Pre, QuizLogic.Post })
                {
                    var a = i.Ask(phase);
                    Assert.That(a.Options.Count, Is.InRange(2, 3), $"{i.Id} {phase}: short (plus 잘 모르겠어요)");
                    Assert.That(a.Options.Select(o => o.Id), Is.Unique, i.Id);
                    Assert.That(a.Options.Select(o => o.Text), Is.Unique, i.Id);
                    Assert.That(a.Options.Count(o => o.Id == a.Correct), Is.EqualTo(1), $"{i.Id} {phase}: one right answer");
                    Assert.That(a.Options.Select(o => o.Id), Has.None.EqualTo(QuizLogic.Unsure));
                }
                Assert.That(i.Post.Prompt, Is.Not.EqualTo(i.Pre.Prompt), $"{i.Id}: the post quiz asks in other words");
                // the pre quiz's intro lists the topics: a neutral name, never the answer ("엘리베이터 대신 계단")
                Assert.That(i.Topic, Is.Not.Empty.And.Not.EqualTo(i.Label), i.Id);
                foreach (var giveaway in new[] { "계단", "엘리베이터", "살펴", "1338", "112", "119", "지목", "5cm", "사이렌", "공식", "안전핀" })
                    Assert.That(i.Topic, Does.Not.Contain(giveaway), $"{i.Id}: topic gives away {giveaway}");
            }
        }

        [Test]
        public void WrongAnswers_ArePlausible_NotStrawmen()
        {
            foreach (var i in QuizLogic.Items)
                foreach (var phase in new[] { QuizLogic.Pre, QuizLogic.Post })
                {
                    var a = i.Ask(phase);
                    int right = a.Right.Text.Length;
                    foreach (var o in a.Options.Where(o => o.Id != a.Correct))
                    {
                        foreach (var bad in new[] { "무조건", "아무것도", "직접 열어" }) Assert.That(o.Text, Does.Not.Contain(bad), $"{i.Id} {phase} {o.Id}");
                        // about as long as the right answer: the longest, most specific option is not a giveaway
                        Assert.That(o.Text.Length, Is.GreaterThanOrEqualTo((int)(right * 0.6)), $"{i.Id} {phase} {o.Id}: much shorter than the right answer");
                    }
                }
            // CPR: each wrong option keeps one of the two right halves, so the question checks 지목 and the depth/rate apart
            var cpr = QuizLogic.Item("cpr");
            foreach (var a in new[] { cpr.Pre, cpr.Post })
            {
                var wrong = a.Options.Where(o => o.Id != a.Correct).ToList();
                Assert.That(wrong.Count(o => o.Text.Contains("약 5cm")), Is.EqualTo(1), "one wrong option with the right depth (no 지목)");
                Assert.That(wrong.Count(o => !o.Text.Contains("약 5cm")), Is.EqualTo(1), "one with the 지목 but a wrong depth");
            }
        }

        [Test]
        public void AClass_IsAskedOnlyTheConceptsItPlays()
        {
            Assert.That(QuizLogic.ClassConcepts, Is.SubsetOf(QuizLogic.Items.Select(i => i.Id)));
            Assert.That(QuizLogic.ClassConcepts, Has.None.EqualTo("cpr").And.None.EqualTo("report"), "CPR and the 1338·112 reports are after the class's stops");
            // every lesson of the route that a quiz concept teaches is one the class is asked
            foreach (var step in SceneSelect.TeacherRoute)
            {
                var item = QuizLogic.Items.FirstOrDefault(i => i.Facts.Contains(step.Fact));
                if (item != null) Assert.That(QuizLogic.ClassConcepts, Does.Contain(item.Id), step.Moment);
            }
            var qs = QuizLogic.Questions(QuizLogic.Post, 3, QuizLogic.ClassConcepts);
            Assert.That(qs.Select(q => q.Item.Id), Is.EquivalentTo(QuizLogic.ClassConcepts));
            var pre = QuizAnswers.Read(Answers(QuizLogic.Pre, i => QuizLogic.Unsure, only: QuizLogic.ClassConcepts));
            var post = QuizAnswers.Read(Answers(QuizLogic.Post, i => Right(i, QuizLogic.Post), only: QuizLogic.ClassConcepts));
            Assert.That((int)pre.ToJson()["total"], Is.EqualTo(5));
            var c = QuizLogic.Compare(pre, post);
            Assert.That(c.Total, Is.EqualTo(5));
            Assert.That(c.Rows.Select(r => r.Concept), Is.EqualTo(QuizLogic.ClassConcepts), "no row for what was not asked");
            Assert.That(QuizLogic.Headline(c), Is.EqualTo("사전 0/5 → 사후 5/5 · 새로 안 것 5개"));
            Assert.That(QuizLogic.IntroBody(QuizLogic.Pre, false, 5, teacher: true), Does.Contain("5문항").And.Contain("수업이 끝난 뒤"));
            Assert.That(QuizLogic.PreDoneBody(true), Does.Contain("수업이 끝나면"));
            Assert.That(QuizLogic.IntroBody(QuizLogic.Pre, false, 7), Does.Contain("이야기가 끝난 뒤"));
        }

        [Test]
        public void Items_RestateOnlyCheckedFacts_AndNeverAnUncheckedNumber()
        {
            var facts = F;
            foreach (var i in QuizLogic.Items)
            {
                Assert.That(i.Facts, Is.Not.Empty, i.Id);
                foreach (var id in i.Facts)
                {
                    var f = facts.Fact(id);
                    Assert.That(f, Is.Not.Null, $"{i.Id}: fact {id}");
                    Assert.That(facts.Source(f.SourceId), Is.Not.Null, $"{id} has a source");
                }
                var all = string.Join(" ", new[] { i.Pre, i.Post }.SelectMany(a => a.Options.Select(o => o.Text).Append(a.Prompt)));
                foreach (var bad in new[] { "1337", "111", "113", "2분", "골든타임" }) Assert.That(all, Does.Not.Contain(bad), $"{i.Id}: {bad}");
            }
            // the verified wording, word for word where the right answer states a number
            var cpr = QuizLogic.Item("cpr");
            Assert.That(cpr.Pre.Right.Text, Does.Contain("약 5cm").And.Contain("분당 100~120회").And.Contain("지목"));
            Assert.That(cpr.Post.Right.Text, Does.Contain("약 5cm").And.Contain("분당 100~120회").And.Contain("한 사람을 정해"));
            Assert.That(facts.Text("cpr_depth_rate"), Does.Contain("약 5cm").And.Contain("분당 100~120회"));
            Assert.That(QuizLogic.Item("report").Pre.Right.Text, Does.Contain("1338").And.Contain("112").And.Contain("119").And.Contain("가까운 경찰서"));
            Assert.That(facts.Text("report_object"), Does.Contain("1338").And.Contain("가까운 경찰서"));
            Assert.That(QuizLogic.Item("alert").Post.Right.Text, Does.Contain("1분").And.Contain("사이렌 없이"));
            Assert.That(facts.Text("alert_levels"), Does.Contain("사이렌 없이").And.Contain("1분"));
            // 소화기: 안전핀 → 바람을 등지고 호스를 불 쪽으로 → 움켜쥐고 쓸듯이 (ext_1~3)
            foreach (var a in new[] { QuizLogic.Item("ext").Pre.Right.Text, QuizLogic.Item("ext").Post.Right.Text })
            {
                Assert.That(a.IndexOf("안전핀", StringComparison.Ordinal), Is.LessThan(a.IndexOf("바람을 등지고", StringComparison.Ordinal)));
                Assert.That(a.IndexOf("바람을 등지고", StringComparison.Ordinal), Is.LessThan(a.IndexOf("움켜쥐고", StringComparison.Ordinal)));
            }
        }

        // ---------------------------------------------------------------- the questions as asked

        [Test]
        public void Questions_PreKeepsTheConceptOrder_PostShufflesIt_BothShuffleOptions_UnsureLast()
        {
            var ids = QuizLogic.Items.Select(i => i.Id).ToList();
            var pre = QuizLogic.Questions(QuizLogic.Pre, 42);
            Assert.That(pre.Select(q => q.Item.Id), Is.EqualTo(ids));
            Assert.That(QuizLogic.Questions(QuizLogic.Pre, 42).SelectMany(q => q.Options.Select(o => o.Id)), Is.EqualTo(pre.SelectMany(q => q.Options.Select(o => o.Id))), "deterministic for a seed");
            bool reordered = false, optionsMoved = false;
            for (uint s = 1; s < 30; s++)
            {
                var post = QuizLogic.Questions(QuizLogic.Post, s);
                Assert.That(post.Select(q => q.Item.Id), Is.EquivalentTo(ids), "the same concepts");
                reordered |= !post.Select(q => q.Item.Id).SequenceEqual(ids);
                foreach (var q in post.Concat(QuizLogic.Questions(QuizLogic.Pre, s)))
                {
                    Assert.That(q.Options.Last().Id, Is.EqualTo(QuizLogic.Unsure));
                    Assert.That(q.Options.Last().Text, Is.EqualTo("잘 모르겠어요"));
                    var phase = post.Contains(q) ? QuizLogic.Post : QuizLogic.Pre;
                    Assert.That(q.Options.Take(q.Options.Count - 1).Select(o => o.Id), Is.EquivalentTo(q.Item.Ask(phase).Options.Select(o => o.Id)));
                    optionsMoved |= q.Options[0].Id != q.Item.Ask(phase).Options[0].Id;
                }
            }
            Assert.That(reordered, Is.True, "the post quiz's order differs");
            Assert.That(optionsMoved, Is.True, "the right answer is not always first");
            Assert.That(QuizLogic.SeedFor(7, QuizLogic.Pre), Is.Not.EqualTo(QuizLogic.SeedFor(7, QuizLogic.Post)));
        }

        // ---------------------------------------------------------------- scoring and the comparison

        [Test]
        public void States_Score_AndChange()
        {
            var alert = QuizLogic.Item("alert");
            Assert.That(QuizLogic.State(alert, QuizLogic.Pre, "airraid"), Is.EqualTo("right"));
            Assert.That(QuizLogic.State(alert, QuizLogic.Pre, "caution"), Is.EqualTo("wrong"));
            Assert.That(QuizLogic.State(alert, QuizLogic.Post, "right"), Is.EqualTo("right"));
            Assert.That(QuizLogic.State(alert, QuizLogic.Post, QuizLogic.Unsure), Is.EqualTo("unsure"));
            Assert.That(QuizLogic.State(alert, QuizLogic.Post, null), Is.Null);

            Assert.That(QuizLogic.Change("right", "right"), Is.EqualTo("kept"));
            Assert.That(QuizLogic.Change("wrong", "right"), Is.EqualTo("learned"));
            Assert.That(QuizLogic.Change("unsure", "right"), Is.EqualTo("learned"));
            Assert.That(QuizLogic.Change("right", "unsure"), Is.EqualTo("slipped"));
            Assert.That(QuizLogic.Change("unsure", "wrong"), Is.EqualTo("missed"));
            Assert.That(QuizLogic.Change(null, "right"), Is.EqualTo("postRight"));
            Assert.That(QuizLogic.Change(null, "wrong"), Is.EqualTo("postWrong"));
            Assert.That(QuizLogic.Change("wrong", null), Is.EqualTo("preOnly"));
            Assert.That(QuizLogic.ChangeText("learned"), Is.EqualTo("몰랐음 → 앎"));
            Assert.That(QuizLogic.ChangeText("kept"), Is.EqualTo("앎 → 앎"));
        }

        [Test]
        public void Compare_ReadsEachConceptBeforeAndAfter()
        {
            // pre: alert right, the rest unsure; post: all right but info wrong
            var pre = QuizAnswers.Read(Answers(QuizLogic.Pre, i => i.Id == "alert" ? Right(i, QuizLogic.Pre) : QuizLogic.Unsure));
            var post = QuizAnswers.Read(Answers(QuizLogic.Post, i => i.Id == "info" ? Wrong(i, QuizLogic.Post) : Right(i, QuizLogic.Post)));
            var c = QuizLogic.Compare(pre, post);
            Assert.That(c.PreScore, Is.EqualTo(1));
            Assert.That(c.PostScore, Is.EqualTo(6));
            Assert.That(c.Total, Is.EqualTo(7));
            Assert.That(c.Rows.Select(r => r.Change), Is.EqualTo(new[] { "kept", "learned", "learned", "learned", "learned", "learned", "missed" }));
            Assert.That(c.Learned, Is.EqualTo(5));
            Assert.That(QuizLogic.Headline(c), Is.EqualTo("사전 1/7 → 사후 6/7 · 새로 안 것 5개"));

            var only = QuizLogic.Compare(null, post);
            Assert.That(only.HasPre, Is.False);
            Assert.That(only.Rows.First().Change, Is.EqualTo("postRight"));
            Assert.That(QuizLogic.Headline(only), Is.EqualTo("사후 6/7 (사전 퀴즈는 건너뜀)"));
            Assert.That(QuizLogic.Headline(QuizLogic.Compare(pre, null)), Is.EqualTo("사전 퀴즈만 풀었습니다 (사후 퀴즈는 건너뜀)"), "after the ending: skipped, and no score");
            Assert.That(QuizLogic.Headline(QuizLogic.Compare(pre, null), postLater: true), Is.EqualTo("사전 퀴즈를 풀었습니다 · 사후 퀴즈는 수업 끝에"));
            Assert.That(QuizLogic.Compare(null, null), Is.Null);
        }

        [Test]
        public void Answers_RoundTrip_AndASkippedSittingReadsAsNone()
        {
            var a = QuizAnswers.Read(Answers(QuizLogic.Post, i => Right(i, QuizLogic.Post), seed: 99));
            var json = a.ToJson();
            Assert.That((string)json["phase"], Is.EqualTo("post"));
            Assert.That((int)json["score"], Is.EqualTo(7));
            Assert.That((int)json["total"], Is.EqualTo(7));
            Assert.That(json["answers"].Select(x => (string)x["state"]), Has.All.EqualTo("right"));
            var back = QuizAnswers.Read(json);
            Assert.That(back.Seed, Is.EqualTo(99u));
            Assert.That(back.Picks, Is.EqualTo(a.Picks));
            Assert.That(back.Order, Is.EqualTo(QuizLogic.Items.Select(i => i.Id)));
            Assert.That(QuizAnswers.Read(new JObject()), Is.Null, "the fake host's {}");
            Assert.That(QuizAnswers.Read(JObject.Parse("{ phase: 'pre', skipped: true }")), Is.Null);
            Assert.That(QuizAnswers.Read(JObject.Parse("{ answers: [ { concept: 'nope', option: 'x' } ] }")), Is.Null, "unknown concepts are dropped");
            Assert.That(QuizAnswers.Read(new JValue("skip")), Is.Null);
        }

        [Test]
        public void TheRunsQuizEvents_FeedTheLearningReport()
        {
            Assert.That(LearningReport.Build(Run()).Quiz, Is.Null, "no quiz, no part");
            var g = Run(("quiz", QuizAnswers.Read(Answers(QuizLogic.Pre, i => Wrong(i, QuizLogic.Pre))).ToJson()),
                ("blackout", null),
                ("quiz", QuizAnswers.Read(Answers(QuizLogic.Post, i => Right(i, QuizLogic.Post))).ToJson()));
            Assert.That(QuizLogic.Find(g, QuizLogic.Pre).Phase, Is.EqualTo("pre"));
            var r = LearningReport.Build(g);
            Assert.That(r.Quiz, Is.Not.Null);
            Assert.That(r.Quiz.PreScore, Is.EqualTo(0));
            Assert.That(r.Quiz.PostScore, Is.EqualTo(7));
            Assert.That(r.Quiz.Rows.Select(x => x.Change), Has.All.EqualTo("learned"));
            Assert.That(r.Rows.Select(x => x.Id), Has.None.EqualTo("quiz"), "the quiz is not a judged action");
        }

        // ---------------------------------------------------------------- the export

        [Test]
        public void Export_HoldsTheQuizTheReportAndTheMinigames_NothingTyped()
        {
            var g = Run(("quiz", QuizAnswers.Read(Answers(QuizLogic.Pre, i => QuizLogic.Unsure)).ToJson()),
                ("alert", new JObject { ["minute"] = 100 }), ("arrived", new JObject { ["minute"] = 110, ["late"] = false }),
                ("cpr", new JObject { ["played"] = true, ["quality"] = 0.8 }),
                ("bag_score", new JObject { ["score"] = 6 }),
                ("note_left", new JObject { ["text"] = "엄마, 학교 체육관으로 가요" }), // typed by the player: never exported
                ("quiz", QuizAnswers.Read(Answers(QuizLogic.Post, i => Right(i, QuizLogic.Post))).ToJson()));
            var now = new DateTime(2026, 11, 21, 9, 30, 0, DateTimeKind.Utc);
            var o = PlaytestExport.Build(g, "story", now);
            Assert.That((string)o["format"], Is.EqualTo(PlaytestExport.Format));
            Assert.That((string)o["runId"], Does.Match("^run-[0-9a-f]{8}$"));
            Assert.That((string)o["runId"], Is.EqualTo(PlaytestExport.RunId(1234)), "stable for the run");
            Assert.That(PlaytestExport.RunId(1235), Is.Not.EqualTo(PlaytestExport.RunId(1234)));
            Assert.That((string)o["exportedAt"], Is.EqualTo("2026-11-21T09:30Z"));
            Assert.That((int)o["quiz"]["pre"]["score"], Is.EqualTo(0));
            Assert.That((int)o["quiz"]["post"]["score"], Is.EqualTo(7));
            Assert.That(o["quiz"]["compare"].Select(x => (string)x["change"]), Has.All.EqualTo("learned"));
            Assert.That((int)o["report"]["total"], Is.GreaterThan(0));
            Assert.That(o["report"]["rows"].Select(x => (string)x["id"]), Does.Contain("golden"));
            Assert.That((int)o["report"]["goldenMinutes"], Is.EqualTo(10));
            Assert.That(o["ending"].Type, Is.EqualTo(JTokenType.Null), "the run has not ended");
            Assert.That((double)o["minigames"]["cpr"]["quality"], Is.EqualTo(0.8));
            Assert.That((string)PlaytestExport.Build(g, "demo", now, seed: 99)["runId"], Is.EqualTo(PlaytestExport.RunId(99)), "a given seed (a moment's, a class's) over the state's");
            Assert.That((int)o["minigames"]["bag_score"]["score"], Is.EqualTo(6));
            var text = o.ToString();
            Assert.That(text, Does.Not.Contain("체육관"), "free text the player typed stays out");
            foreach (var key in new[] { "name", "user", "device", "email", "machine" }) Assert.That(o.SelectTokens("$.." + key), Is.Empty, key);

            // the ending reached (any picker; the default is Endings.PickEnding)
            g.Chapter = "ending";
            var ended = PlaytestExport.Build(g, "demo", now, ending: x => new Fate { Id = "bus", No = 1, Title = "버스", Kind = "path", Path = "bus" });
            Assert.That((string)ended["ending"]["id"], Is.EqualTo("bus"));
            Assert.That((string)ended["mode"], Is.EqualTo("demo"));
        }

        [Test]
        public void Export_TheMinigameList_NamesEventsTheScenesReallyRecord()
        {
            Assert.That(PlaytestExport.MinigameEvents, Does.Contain(ExtinguisherCheck.Event).And.Contain(ExtinguisherScene.UseEvent)
                .And.Contain(TwentyTwoLogic.Event).And.Contain(ElderPovLogic.Event).And.Contain(ShelterLifeLogic.Event));
            Assert.That(PlaytestExport.MinigameEvents, Has.None.EqualTo("extgauge"), "the gauge check's screen name, not its event");
            // the gauge check recorded the way ExtinguisherScene records it is exported
            var g = Run();
            GameRules.RecordEvent(g, ExtinguisherCheck.Event, new JObject { ["read"] = true, ["low"] = false });
            Assert.That((bool)PlaytestExport.Build(g, "story", DateTime.UtcNow)["minigames"][ExtinguisherCheck.Event]["read"], Is.True);
            // every name is recorded somewhere: a RecordEvent literal or a constant in the scripts (not just this list)
            var scripts = Path.Combine(UnityEngine.Application.dataPath, "_AiOn", "Scripts");
            var code = string.Join("\n", Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f) != "PlaytestExport.cs").Select(File.ReadAllText));
            foreach (var type in PlaytestExport.MinigameEvents)
                Assert.That(code.Contains($"RecordEvent(g, \"{type}\"") || code.Contains($"RecordEvent(G, \"{type}\"") || code.Contains($"RecordEvent(ctx.G, \"{type}\"")
                    || code.Contains($"Event = \"{type}\""), Is.True, type);
        }

        [Test]
        public void AClassExport_HoldsEveryStepsRows_AndMarksTheReportAsTheLastStep()
        {
            var a = Run(("alert", new JObject { ["minute"] = 100 }), ("arrived", new JObject { ["minute"] = 110, ["late"] = false }));
            var b = Run(("smoke_choice", new JObject { ["pick"] = "towel" }));
            var o = PlaytestExport.WithSteps(PlaytestExport.Build(b, "teacher", DateTime.UtcNow, seed: 7), new[] { ("alert", a), ("fire", b) });
            Assert.That((string)o["report"]["scope"], Is.EqualTo("lastStep"));
            Assert.That(o["steps"].Select(x => (string)x["moment"]), Is.EqualTo(new[] { "alert", "fire" }));
            Assert.That(o["steps"][0]["rows"].Select(x => (string)x["id"]), Does.Contain("golden"));
            var csv = PlaytestExport.ToCsv(o).Split('\n');
            Assert.That(csv, Does.Contain("report,scope,lastStep"));
            Assert.That(csv.Any(l => l.StartsWith("step_row,alert.golden,")), Is.True);
        }

        [Test]
        public void TheSavedLine_ShowsTheFileNotThePath()
        {
            var line = PlaytestExport.SavedLine(Path.Combine("C:", "Users", "someone", "AppData", "exports", "playtest-run-1-x.json"));
            Assert.That(line, Does.Contain("exports/playtest-run-1-x.json"));
            Assert.That(line, Does.Not.Contain("someone").And.Not.Contain("Users"));
        }

        [Test]
        public void Export_WithoutAGame_IsTheQuizOnly_AndWithQuizReplacesThatPart()
        {
            var pre = QuizAnswers.Read(Answers(QuizLogic.Pre, i => Right(i, QuizLogic.Pre), seed: 77));
            var o = PlaytestExport.Build(null, "teacher", DateTime.UtcNow, pre: pre);
            Assert.That((string)o["runId"], Is.EqualTo(PlaytestExport.RunId(77)));
            Assert.That(o["report"], Is.Null);
            Assert.That(o["minigames"], Is.Null);
            Assert.That(o["quiz"]["post"].Type, Is.EqualTo(JTokenType.Null));
            var post = QuizAnswers.Read(Answers(QuizLogic.Post, i => QuizLogic.Unsure));
            PlaytestExport.WithQuiz(o, pre, post);
            Assert.That((int)o["quiz"]["post"]["score"], Is.EqualTo(0));
            Assert.That(o["quiz"]["compare"].Select(x => (string)x["change"]), Has.All.EqualTo("slipped"));
        }

        [Test]
        public void Csv_IsSectionKeyValue_Escaped()
        {
            Assert.That(PlaytestExport.Csv("a"), Is.EqualTo("a"));
            Assert.That(PlaytestExport.Csv("a,b"), Is.EqualTo("\"a,b\""));
            Assert.That(PlaytestExport.Csv("say \"hi\""), Is.EqualTo("\"say \"\"hi\"\"\""));
            Assert.That(PlaytestExport.Csv("two\nlines"), Is.EqualTo("\"two\nlines\""));
            Assert.That(PlaytestExport.Csv(null), Is.EqualTo(""));

            var g = Run(("quiz", QuizAnswers.Read(Answers(QuizLogic.Pre, i => Right(i, QuizLogic.Pre))).ToJson()),
                ("bag_score", new JObject { ["score"] = 6, ["load"] = new JObject { ["kg"] = 7.5 } }));
            var csv = PlaytestExport.ToCsv(PlaytestExport.Build(g, "story", new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc)));
            var lines = csv.TrimEnd('\n').Split('\n');
            Assert.That(lines[0], Is.EqualTo("section,key,value"));
            Assert.That(lines, Does.Contain("run,mode,story"));
            Assert.That(lines, Does.Contain("quiz_pre,score,7"));
            Assert.That(lines, Does.Contain("quiz_pre,total,7"));
            Assert.That(lines.Any(l => System.Text.RegularExpressions.Regex.IsMatch(l, @",\d+/\d+$")), Is.False, "no bare 3/7: a spreadsheet reads it as a date");
            Assert.That(lines, Does.Contain("quiz_pre,alert,airraid (right)"));
            Assert.That(lines, Does.Contain("quiz_post,skipped,true"));
            Assert.That(lines, Does.Contain("quiz_compare,alert,preOnly"));
            Assert.That(lines, Does.Contain("minigame,bag_score.score,6"));
            Assert.That(lines, Does.Contain("minigame,bag_score.load.kg,7.5"));
            Assert.That(lines, Does.Contain("ending,id,"));
        }

        [Test]
        public void Write_PutsAJsonAndACsvInTheFolder_AndReturnsTheJsonPath()
        {
            var dir = Path.Combine(Path.GetTempPath(), "aion-export-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var o = PlaytestExport.Build(Run(), "story", DateTime.UtcNow);
                var path = PlaytestExport.Write(o, Path.Combine(dir, PlaytestExport.Folder), new DateTime(2026, 11, 21, 9, 5, 7));
                Assert.That(Path.GetFileName(path), Is.EqualTo($"playtest-{o["runId"]}-20261121-090507.json"));
                Assert.That(File.Exists(path), Is.True);
                Assert.That(File.Exists(Path.ChangeExtension(path, ".csv")), Is.True);
                Assert.That((string)JObject.Parse(File.ReadAllText(path))["runId"], Is.EqualTo((string)o["runId"]));
                var bytes = File.ReadAllBytes(Path.ChangeExtension(path, ".csv"));
                Assert.That(bytes.Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }), "the CSV opens in a spreadsheet with its Korean labels");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        // ---------------------------------------------------------------- the story hooks

        [Test]
        public void ANewRun_OffersThePreQuizFirst_AndTheEndingDocumentLandsBeforeThePostQuiz_ThenTheReportCompares()
        {
            var r = StoryPlaythroughTests.DefaultRun("quiz");
            var seen = new List<JObject>();
            var endings = new List<JObject>();
            r.Screens[QuizLogic.Screen] = o =>
            {
                seen.Add(o);
                return (string)o["phase"] == QuizLogic.Pre
                    ? Answers(QuizLogic.Pre, i => i.Id == "cpr" ? QuizLogic.Unsure : Right(i, QuizLogic.Pre))
                    : Answers(QuizLogic.Post, i => Right(i, QuizLogic.Post));
            };
            // the player presses the document's '사후 퀴즈' button, then 처음부터 다시 on the document shown again
            r.Screens["ending"] = o => { endings.Add(o); return endings.Count == 1 ? "quiz" : "restart"; };
            var result = r.Play();
            Assert.That(result, Is.EqualTo("restart"));
            Assert.That(seen.Select(o => (string)o["phase"]), Is.EqualTo(new[] { "pre", "post" }));
            Assert.That((uint)seen[0]["seed"], Is.EqualTo(r.LastG.Seed));
            Assert.That(seen[0]["concepts"], Is.Null, "a story run is asked all seven");
            Assert.That((string)seen[1]["pre"]["answers"][0]["concept"], Is.EqualTo("alert"), "the post quiz gets the pre answers");
            Assert.That((string)seen[1]["mode"], Is.EqualTo("story"));
            var export = (JObject)seen[1]["export"];
            Assert.That((string)export["ending"]["id"], Is.Not.Empty, "the export basis knows the ending");
            Assert.That((string)export["runId"], Is.EqualTo(PlaytestExport.RunId(r.LastG.Seed)), "a story run's id is its own seed's");
            Assert.That(export["minigames"]["cpr"], Is.Not.Null);
            // the ending document first (it offers the quiz), the quiz, then the document again (a short hold), no second offer
            Assert.That(endings, Has.Count.EqualTo(2));
            Assert.That((bool?)endings[0]["postQuiz"], Is.True);
            Assert.That(endings[0]["again"], Is.Null);
            Assert.That(endings[1]["postQuiz"], Is.Null, "offered once");
            Assert.That((bool?)endings[1]["again"], Is.True);
            Assert.That(endings[1]["openReport"], Is.Null);
            var log = r.Host.Log;
            int preAt = log.FindIndex(c => c.Kind == "open" && c.Text == QuizLogic.Screen);
            int postAt = log.FindLastIndex(c => c.Kind == "open" && c.Text == QuizLogic.Screen);
            Assert.That(preAt, Is.LessThan(log.FindIndex(c => c.Kind == "card" && c.Text == "체험 모드")));
            Assert.That(postAt, Is.GreaterThan(log.FindIndex(c => c.Kind == "open" && c.Text == "ending")), "the ending lands first");
            Assert.That(postAt, Is.LessThan(log.FindLastIndex(c => c.Kind == "open" && c.Text == "ending")));
            var quiz = r.LastG.Events.Where(e => e.Type == QuizLogic.Event).ToList();
            Assert.That(quiz.Select(e => (string)e.Details["phase"]), Is.EqualTo(new[] { "pre", "post" }));
            Assert.That(quiz[1].Seq, Is.GreaterThan(r.LastG.Events.First(e => e.Type == "run_recorded").Seq));
            var rep = LearningReport.Build(r.LastG).Quiz;
            Assert.That(rep.PreScore, Is.EqualTo(6));
            Assert.That(rep.PostScore, Is.EqualTo(7));
            Assert.That(rep.Rows.Single(x => x.Concept == "cpr").Change, Is.EqualTo("learned"));
            Assert.That(rep.Rows.Where(x => x.Concept != "cpr").Select(x => x.Change), Has.All.EqualTo("kept"));
        }

        [Test]
        public void TheReportButton_WhileThePostQuizIsPending_OffersItFirst_ThenOpensTheReport()
        {
            var r = StoryPlaythroughTests.DefaultRun("quiz-report");
            var endings = new List<JObject>();
            var seen = new List<string>();
            r.Screens[QuizLogic.Screen] = o => { seen.Add((string)o["phase"]); return (string)o["phase"] == QuizLogic.Pre ? Answers(QuizLogic.Pre, i => QuizLogic.Unsure) : Answers(QuizLogic.Post, i => Right(i, QuizLogic.Post)); };
            r.Screens["ending"] = o => { endings.Add(o); return endings.Count == 1 ? "report" : "menu"; };
            Assert.That(r.Play(), Is.EqualTo("menu"));
            Assert.That(seen, Is.EqualTo(new[] { "pre", "post" }));
            Assert.That(endings, Has.Count.EqualTo(2));
            Assert.That((bool?)endings[1]["openReport"], Is.True, "back on the document, the report it was asked for opens");
            Assert.That(LearningReport.Build(r.LastG).Quiz.HasPost, Is.True, "the report it opens has the comparison");
        }

        [Test]
        public void ASkippedPreQuiz_LeavesNoEvent_AndThePostQuizIsStillOffered_OnTheFirstPressOfTheDocument()
        {
            var r = StoryPlaythroughTests.DefaultRun("quiz-skip");
            var seen = new List<string>();
            r.Screens[QuizLogic.Screen] = o => { seen.Add((string)o["phase"]); return JObject.Parse("{ skipped: true }"); };
            Assert.That(r.Play(), Is.EqualTo("restart"), "처음부터 다시: the quiz is offered (and skipped), then the restart goes on");
            Assert.That(seen, Is.EqualTo(new[] { "pre", "post" }));
            var log = r.Host.Log;
            Assert.That(log.Count(c => c.Kind == "open" && c.Text == "ending"), Is.EqualTo(1));
            Assert.That(log.FindLastIndex(c => c.Kind == "open" && c.Text == QuizLogic.Screen), Is.GreaterThan(log.FindIndex(c => c.Kind == "open" && c.Text == "ending")));
            Assert.That(r.LastG.Events.Any(e => e.Type == QuizLogic.Event), Is.False);
            Assert.That(LearningReport.Build(r.LastG).Quiz, Is.Null);
        }

        [Test]
        public void TheExportSeed_IsNewForEachMomentOrClass()
        {
            var d = new StoryPlaythroughTests.Rig { Name = "export-seed" }.D; // Random is a constant 0.25 in the rig
            uint a = d.NextExportSeed(), b = d.NextExportSeed();
            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(PlaytestExport.RunId(a), Is.Not.EqualTo(PlaytestExport.RunId(b)));
            Assert.That(d.ExportSeed, Is.Null, "none outside a moment or a class");
        }

        [Test]
        public void AMoment_ExportsUnderItsOwnId_NotTheSharedPreparedSeed()
        {
            var r = new StoryPlaythroughTests.Rig { Name = "moment-export" };
            r.D.PreparedSave = id => File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath, "_AiOn", "Resources", SceneSelect.ResourceFolder, id + ".json"));
            StoryPlaythroughTests.DefaultFrom(r, "evac", null);
            uint? during = null; uint prepared = 0;
            r.Screens["ending"] = o => { during = r.D.ExportSeed; prepared = r.D.G.Seed; return "menu"; };
            r.Host.Run(() => r.D.PlayPrepared(SceneSelect.Get("evac")));
            Assert.That(r.Fails, Is.Empty);
            Assert.That(during, Is.Not.Null);
            Assert.That(PlaytestExport.RunId(during.Value), Is.Not.EqualTo(PlaytestExport.RunId(prepared)), "every prepared save ships with one seed");
            Assert.That(r.D.ExportSeed, Is.Null, "cleared after the moment");
        }

        [Test]
        public void TheTeacherMode_AsksTheClassConcepts_BeforeTheClassAndAfterItsSummary_AndEachStepCarriesThePreQuiz()
        {
            var r = new StoryPlaythroughTests.Rig { Name = "teacher-quiz" };
            r.D.PreparedSave = id => File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath, "_AiOn", "Resources", SceneSelect.ResourceFolder, id + ".json"));
            StoryPlaythroughTests.DefaultFrom(r, "dday", "alert", teacherStop: true);
            StoryPlaythroughTests.DefaultFrom(r, "endure", "fire", teacherStop: true);
            StoryPlaythroughTests.DefaultFrom(r, "crossroads", null);
            StoryPlaythroughTests.DefaultFrom(r, "evac", null);
            var seen = new List<JObject>();
            var endings = new List<JObject>();
            uint? seedAtEnding = null;
            r.Screens["ending"] = o => { endings.Add(o); seedAtEnding = r.D.ExportSeed; return "restart"; };
            r.Screens[QuizLogic.Screen] = o =>
            {
                seen.Add(o);
                var only = o["concepts"].Select(x => (string)x).ToList();
                return (string)o["phase"] == QuizLogic.Pre ? Answers(QuizLogic.Pre, i => QuizLogic.Unsure, only: only) : Answers(QuizLogic.Post, i => Right(i, QuizLogic.Post), only: only);
            };
            r.Host.AnswerOpen("landing", "teacher");
            r.Host.Run(() => r.D.TitleOnce());
            Assert.That(r.Fails, Is.Empty);
            Assert.That(seen.Select(o => (string)o["phase"]), Is.EqualTo(new[] { "pre", "post" }), "not again at the last moment's ending");
            Assert.That(seen.Select(o => o["concepts"].Select(x => (string)x)), Has.All.EqualTo(QuizLogic.ClassConcepts), "a class is asked what it plays");
            Assert.That((string)seen[1]["mode"], Is.EqualTo("teacher"));
            Assert.That(QuizAnswers.Read(seen[1]["pre"]).Picks.Values, Has.All.EqualTo(QuizLogic.Unsure));
            Assert.That(endings, Has.Count.EqualTo(1));
            Assert.That(endings[0]["postQuiz"], Is.Null, "the class's post quiz is after its summary, not at the document");
            // each step's run holds the class's pre quiz: the report at the last step's ending shows it (pre only, unmarked)
            Assert.That(QuizLogic.Find(r.D.LastPrepared, QuizLogic.Pre), Is.Not.Null);
            Assert.That(LearningReport.Build(r.D.LastPrepared).Quiz.HasPost, Is.False);
            // one anonymous id for the class, its own (the prepared saves all share one seed)
            uint classSeed = (uint)seen[0]["seed"];
            Assert.That(seedAtEnding, Is.EqualTo(classSeed));
            var export = (JObject)seen[1]["export"];
            Assert.That((string)export["runId"], Is.EqualTo(PlaytestExport.RunId(classSeed)));
            Assert.That((string)export["runId"], Is.Not.EqualTo(PlaytestExport.RunId(r.D.LastPrepared.Seed)));
            Assert.That((string)export["report"]["scope"], Is.EqualTo("lastStep"));
            Assert.That(export["steps"].Select(x => (string)x["moment"]), Is.EqualTo(SceneSelect.TeacherRoute.Select(t => t.Moment)), "every step's rows");
            Assert.That(r.D.ExportSeed, Is.Null, "cleared after the class");
            var log = r.Host.Log;
            int summary = log.FindIndex(c => c.Kind == "card" && c.Text == SceneSelect.TeacherSummaryTitle);
            int profile = log.FindIndex(c => c.Kind == "card" && c.Text == "체험 모드");
            Assert.That(log.FindIndex(c => c.Kind == "open" && c.Text == QuizLogic.Screen), Is.GreaterThan(profile).And.LessThan(log.FindIndex(c => c.Kind == "chapter")));
            Assert.That(log.FindLastIndex(c => c.Kind == "open" && c.Text == QuizLogic.Screen), Is.GreaterThan(summary));
        }
    }
}
