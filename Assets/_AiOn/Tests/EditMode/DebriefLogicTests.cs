using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class DebriefLogicTests
    {
        static JObject J(string json) => JObject.Parse(json);

        [Test]
        public void Lines_FiltersNullAndEmptyAndWrapsScalars()
        {
            Assert.That(DebriefLogic.Lines(null), Is.Empty);
            Assert.That(DebriefLogic.Lines(JValue.CreateNull()), Is.Empty);
            Assert.That(DebriefLogic.Lines(new JValue("a")).Select(t => (string)t), Is.EqualTo(new[] { "a" }));
            Assert.That(DebriefLogic.Lines(new JValue("")), Is.Empty);
            Assert.That(DebriefLogic.Lines(JArray.Parse("[\"a\", null, \"\", \"b\", 0]")).Count, Is.EqualTo(3));
        }

        [Test]
        public void PrepName_UsesTableThenIdThenFallback()
        {
            Assert.That(DebriefLogic.PrepName(new JValue("radio")), Is.EqualTo("라디오 주파수·건전지"));
            Assert.That(DebriefLogic.PrepName(new JValue("nope")), Is.EqualTo("nope"));
            Assert.That(DebriefLogic.PrepName(null), Is.EqualTo("준비"));
            Assert.That(DebriefLogic.PrepName(new JValue("")), Is.EqualTo("준비"));
        }

        [Test]
        public void FailCard_DefaultsAndRows()
        {
            var v = DebriefLogic.FailCard(new JObject());
            Assert.That(v.Title, Is.EqualTo("이번에는 여기까지였습니다"));
            Assert.That(v.Label, Is.EqualTo("다시 시도"));
            Assert.That(v.Rows.Select(r => r.label), Is.EqualTo(new[] { "당시 확인한 정보", "판단에서 놓친 부분", "다음에 시도할 준비" }));
            Assert.That(v.Rows.All(r => r.lines.Count == 0));
            Assert.That(v.Sources, Is.Empty);

            v = DebriefLogic.FailCard(J(@"{ ""title"": ""늦었습니다"", ""seen"": ""a"", ""missed"": [""b"", """", ""c""], ""next"": null,
                ""sources"": [ { ""publisher"": ""행정안전부"", ""title"": ""국민행동요령"", ""url"": ""https://x"" }, { ""title"": ""제목만"" }, null ] }"));
            Assert.That(v.Title, Is.EqualTo("늦었습니다"));
            Assert.That(v.Label, Is.EqualTo("늦었습니다"));
            Assert.That(v.Rows[0].lines, Is.EqualTo(new[] { "a" }));
            Assert.That(v.Rows[1].lines, Is.EqualTo(new[] { "b", "c" }));
            Assert.That(v.Sources.Count, Is.EqualTo(2));
            Assert.That(v.Sources[0].Text, Is.EqualTo("행정안전부 · 국민행동요령"));
            Assert.That(v.Sources[0].Url, Is.EqualTo("https://x"));
            Assert.That(v.Sources[1].Text, Is.EqualTo("제목만"));
            Assert.That(v.Sources[1].Url, Is.Null);
        }

        [Test]
        public void ChapterCard_PayoffsWithRippleCause()
        {
            var v = DebriefLogic.ChapterCard(J(@"{ ""chapter"": ""4장"", ""title"": ""남을까, 떠날까"", ""lines"": [""줄1"", """"],
                ""payoffs"": [ { ""prep"": ""tub"", ""moment"": ""물이 끊겼을 때"", ""id"": ""tub"" }, { ""prep"": ""zzz"" } ],
                ""ripples"": [ { ""prep"": ""tub"", ""label"": ""받아 둔 물"", ""cause"": { ""when"": ""D-1 오전"", ""text"": ""욕조에 물을 받았다"" },
                               ""effect"": { ""when"": ""D+1 저녁"", ""text"": ""물이 끊겼을 때"" } } ] }"));
            Assert.That(v.Eyebrow, Is.EqualTo("4장 끝"));
            Assert.That(v.Label, Is.EqualTo("4장 정리 · 남을까, 떠날까"));
            Assert.That(v.Payoffs[0].Bold, Is.EqualTo("받아 둔 물"));
            Assert.That(v.Payoffs[0].Text, Is.EqualTo("물이 끊겼을 때"));
            Assert.That(v.Payoffs[0].Small, Is.EqualTo("D-1 오전 · 욕조에 물을 받았다"));
            Assert.That(v.Payoffs[1].Bold, Is.EqualTo("zzz"));
            Assert.That(v.Payoffs[1].Text, Is.EqualTo(""));
            Assert.That(v.Payoffs[1].Small, Is.Null);
            Assert.That(v.Lines, Is.EqualTo(new[] { "줄1" }));

            var empty = DebriefLogic.ChapterCard(new JObject());
            Assert.That(empty.Eyebrow, Is.EqualTo(" 끝"));
            Assert.That(empty.Lines, Is.Empty);
            Assert.That(empty.Empty, Is.EqualTo("기록할 것이 많지 않았습니다."));
        }

        [Test]
        public void SameMoment_UsesIdBeforePrep()
        {
            var r = J(@"{ ""prep"": ""radio"", ""effect"": { ""text"": ""m"" } }");
            Assert.That(DebriefLogic.SameMoment(r, J(@"{ ""id"": ""radio"", ""prep"": ""x"", ""moment"": ""m"" }")), Is.True);
            Assert.That(DebriefLogic.SameMoment(r, J(@"{ ""prep"": ""radio"", ""moment"": ""m"" }")), Is.True);
            Assert.That(DebriefLogic.SameMoment(r, J(@"{ ""id"": ""x"", ""prep"": ""radio"", ""moment"": ""m"" }")), Is.False);
            Assert.That(DebriefLogic.SameMoment(r, J(@"{ ""prep"": ""radio"", ""moment"": ""other"" }")), Is.False);
        }

        [Test]
        public void EndingArgs_AcceptsWrappedOrFlat()
        {
            var (rec, src) = DebriefLogic.EndingArgs(J(@"{ ""record"": { ""share"": ""s"" }, ""sources"": [ { ""id"": ""a"" } ] }"));
            Assert.That((string)rec["share"], Is.EqualTo("s"));
            Assert.That(src.Count, Is.EqualTo(1));
            (rec, src) = DebriefLogic.EndingArgs(J(@"{ ""share"": ""t"", ""sources"": [ { ""id"": ""a"" }, { ""id"": ""b"" } ] }"));
            Assert.That((string)rec["share"], Is.EqualTo("t"));
            Assert.That(src.Count, Is.EqualTo(2));
        }

        [Test]
        public void Ending_EmptyRecordShowsTheEmptyLines()
        {
            var v = DebriefLogic.Ending(new JObject(), null, null);
            Assert.That(v.Title, Is.EqualTo("생존 기록"));
            Assert.That(v.Place, Is.Null);
            Assert.That(v.Share, Is.EqualTo(""));
            Assert.That(v.Sections.Select(s => s.Title), Is.EqualTo(new[] { "도움이 된 준비", "쓰지 못한 물건과 그 이유", "확인하지 못한 정보", "함께 버틴 사람들" }));
            Assert.That(v.Sections[0].Empty, Is.EqualTo("이번에는 미리 해 둔 준비가 큰 역할을 하지 못했습니다. 다음에는 하나만 먼저 해 보세요."));
            Assert.That(v.Sections[1].Empty, Is.EqualTo("가진 물건을 모두 쓸 수 있었습니다."));
            Assert.That(v.Sections[2].Empty, Is.EqualTo("필요한 정보를 대부분 확인했습니다."));
            Assert.That(v.Sections[3].Empty, Is.EqualTo("이번에는 혼자 버텼습니다. 이웃과의 연락 한 가지가 선택지를 넓힙니다."));
        }

        static JObject FullRecord() => J(@"{
            ""fate"": { ""no"": 8, ""title"": ""다시 문을 연 가게"", ""place"": ""바로편의점 · D+13"", ""lines"": [""줄 A"", ""줄 B""],
              ""trace"": [ { ""id"": ""blackout_radio"", ""text"": ""라디오를 켰다"", ""when"": ""D-day 밤"", ""sourceIds"": [""mois"", ""none"", ""kbs""] },
                           { ""id"": ""hall_checked"", ""text"": ""복도를 살폈다"", ""when"": """" } ],
              ""people"": [ { ""who"": ""clerk"", ""name"": ""수아"", ""line"": ""돌아왔다"", ""present"": true },
                            { ""who"": ""elder"", ""name"": ""어르신"", ""line"": ""떠났다"", ""present"": false },
                            { ""who"": ""stranger"", ""line"": ""누군가"" } ],
              ""town"": [""동네 줄""],
              ""missed"": [ { ""title"": ""다른 결말"", ""hint"": ""힌트"", ""tip"": ""팁"" }, { ""title"": ""또"", ""hint"": ""힌트2"" } ] },
            ""helped"": [ { ""prep"": ""tub"", ""moment"": ""m1"", ""id"": ""tub"" }, { ""prep"": ""radio"", ""moment"": ""m2"" } ],
            ""unused"": [ { ""item"": ""손전등"", ""why"": ""건전지"" } ],
            ""unchecked"": [""소문 하나""],
            ""people"": [ { ""who"": ""서연"", ""what"": ""x"" } ],
            ""ripples"": [ { ""prep"": ""tub"", ""label"": ""받아 둔 물"", ""cause"": { ""when"": ""D-1 오전"", ""text"": ""c"" }, ""effect"": { ""when"": ""D+1 저녁"", ""text"": ""m1"" } } ],
            ""practice"": { ""text"": ""대피소를 확인해 보세요"", ""sourceId"": ""mois"" },
            ""run"": { ""stats"": { ""complete"": true, ""ms"": 3725000, ""retries"": 2 },
                       ""news"": { ""done"": [""precise"", ""info_first""], ""fresh"": [""precise""], ""bests"": [""prep"", ""zzz""], ""rank"": 1 } },
            ""share"": ""공유 글"" }");

        static List<JToken> Sources() => JArray.Parse(@"[
            { ""id"": ""mois"", ""publisher"": ""행정안전부"", ""title"": ""민방위"", ""url"": ""https://m"", ""checkedAt"": ""2026-01-02"" },
            { ""id"": ""kbs"", ""publisher"": ""KBS"", ""title"": ""재난방송"" } ]").ToList();

        [Test]
        public void Ending_FullRecordSectionsInOriginalOrder()
        {
            var v = DebriefLogic.Ending(FullRecord(), Sources(), f => f == "radio_listen" ? "라디오를 들으세요" : null, w => $"[{w}]");
            Assert.That(v.Title, Is.EqualTo("엔딩 8 · 다시 문을 연 가게"));
            Assert.That(v.Place, Is.EqualTo("바로편의점 · D+13"));
            Assert.That(v.Share, Is.EqualTo("공유 글"));
            Assert.That(v.Sections.Select(s => s.Title ?? "(fate)"), Is.EqualTo(new[]
            {
                "(fate)", "이 결말로 이끈 선택", "그 뒤의 사람들", "도전 과제 2/" + Challenges.All.Count, "연무동",
                "나비효과 · 작은 선택이 남긴 것", "그 밖에 도움이 된 준비", "쓰지 못한 물건과 그 이유", "확인하지 못한 정보",
                "다른 길이 있었다면", "실제 생활에서 확인해 볼 한 가지",
            }));
            var fate = v.Sections[0];
            Assert.That(fate.Paras.Select(p => p.Text), Is.EqualTo(new[] { "줄 A", "줄 B" }));

            var trace = v.Sections[1].Items;
            Assert.That(trace[0].Bold, Is.EqualTo("D-day 밤"));
            Assert.That(trace[0].Small, Is.EqualTo("라디오를 들으세요 — 행정안전부 · 민방위 · KBS · 재난방송"));
            Assert.That(trace[0].Url, Is.EqualTo("https://m"));
            Assert.That(trace[1].Small, Is.Null);

            var people = v.Sections[2].Items;
            Assert.That(people.Select(p => p.Class), Is.EqualTo(new[] { "", "away", "stranger" }));
            Assert.That(people[2].Bold, Is.Null);

            var ripple = v.Sections[5].Items.Single();
            Assert.That(ripple.Bold, Is.EqualTo("받아 둔 물"));
            Assert.That(ripple.Text, Is.EqualTo("[D-1 오전] c → [D+1 저녁] m1"));

            var rest = v.Sections[6].Items.Single();
            Assert.That(rest.Bold, Is.EqualTo("라디오 주파수·건전지"));
            Assert.That(rest.Text, Is.EqualTo("m2"));

            var missed = v.Sections[9].Items;
            Assert.That(missed[0].Small, Is.EqualTo(" (팁)"));
            Assert.That(missed[1].Small, Is.Null);

            var practice = v.Sections[10];
            Assert.That(practice.Paras[0].Text, Is.EqualTo("대피소를 확인해 보세요"));
            Assert.That(practice.Paras[1].Text, Is.EqualTo("출처: 행정안전부 · 민방위 (확인 2026-01-02)"));
            Assert.That(practice.Paras[1].Url, Is.EqualTo("https://m"));
        }

        [Test]
        public void Ending_AllHelpedInChainHidesTheRestSection()
        {
            var rec = FullRecord();
            ((JArray)rec["helped"]).RemoveAt(1);
            var v = DebriefLogic.Ending(rec, Sources(), null);
            Assert.That(v.Sections.Any(s => s.Title == "그 밖에 도움이 된 준비" || s.Title == "도움이 된 준비"), Is.False);
            Assert.That(v.Sections.Any(s => s.Title == "함께 버틴 사람들"), Is.False); // fate has its own people
        }

        [Test]
        public void Ending_RipplesCappedAtFive()
        {
            var rec = new JObject { ["ripples"] = new JArray(Enumerable.Range(0, 8).Select(i => J($@"{{ ""prep"": ""p{i}"", ""label"": ""L{i}"", ""cause"": {{ ""when"": ""w"", ""text"": ""c"" }}, ""effect"": {{ ""when"": ""w"", ""text"": ""e"" }} }}"))) };
            var v = DebriefLogic.Ending(rec, null, null);
            Assert.That(v.Sections.First(s => s.Title == "나비효과 · 작은 선택이 남긴 것").Items.Count, Is.EqualTo(DebriefLogic.MaxRipples));
            Assert.That(v.Sections.Any(s => s.Title == "그 밖에 도움이 된 준비"), Is.False); // chain and nothing else helped
        }

        [Test]
        public void RunSection_CompleteAndContinued()
        {
            var s = DebriefLogic.RunSection(FullRecord()["run"]);
            var titles = Challenges.All.ToDictionary(c => c.Id, c => c.Title);
            Assert.That(s.Items[0].Bold, Is.EqualTo(titles["info_first"]));
            Assert.That(s.Items[0].Text, Is.EqualTo("달성"));
            Assert.That(s.Items[1].Bold, Is.EqualTo(titles["precise"]));
            Assert.That(s.Items[1].Text, Is.EqualTo("새로 달성"));
            Assert.That(s.Items[1].Class, Is.EqualTo("fresh"));
            Assert.That(s.Items.Count(i => i.Class == "next"), Is.EqualTo(2));
            Assert.That(s.Items[2].Bold, Is.EqualTo("다음 도전 · " + titles["rumor_stop"]));
            Assert.That(s.Tail.Text, Is.EqualTo("기록 1시간 2분 · 다시 하기 2번 · 내 기록 1위 · 최고 기록 경신: 1장, "));
            Assert.That(s.TailMuted, Is.False);

            var cont = DebriefLogic.RunSection(J(@"{ ""stats"": { ""complete"": false }, ""news"": { ""done"": [], ""fresh"": [], ""bests"": [] } }"));
            Assert.That(cont.Tail.Text, Is.EqualTo("이어하기로 마친 판은 전체 시간과 순위가 남지 않습니다."));
            Assert.That(cont.TailMuted, Is.True);
            Assert.That(DebriefLogic.RunSection(J(@"{ ""stats"": {} }")), Is.Null);
            Assert.That(DebriefLogic.RunSection(null), Is.Null);
        }

        [Test]
        public void PlainText_HasTitleSectionsAndFiction()
        {
            var v = DebriefLogic.Ending(FullRecord(), Sources(), f => "사실");
            var text = DebriefLogic.PlainText(v);
            StringAssert.Contains("엔딩 8 · 다시 문을 연 가게", text);
            StringAssert.Contains("■ 쓰지 못한 물건과 그 이유", text);
            StringAssert.Contains("- 손전등  건전지", text);
            StringAssert.Contains("(https://m)", text);
            StringAssert.EndsWith(v.Fiction + System.Environment.NewLine, text);
        }

        [Test]
        public void Gallery_CountsSeenAndHidesUnseen()
        {
            var view = DebriefLogic.GalleryArg(new JObject());
            Assert.That(view.Count, Is.EqualTo(13), "the original 11 and the port's two (report, care)");
            var v = DebriefLogic.Gallery(view);
            Assert.That(v.Title, Is.EqualTo("연무동의 열세 가지 끝 · 0/13"));
            Assert.That(v.Rows.All(r => r.Title == "???" && !r.Seen));

            var gal = Endings.AddToGallery(new Gallery(), "reopen", new[] { "blackout_radio", "hall_checked" }, "2026-11-16");
            v = DebriefLogic.Gallery(DebriefLogic.GalleryArg(new JObject { ["gallery"] = JToken.FromObject(gal) }));
            Assert.That(v.Title, Is.EqualTo("연무동의 열세 가지 끝 · 1/13"));
            var row = v.Rows.Single(r => r.Seen);
            Assert.That(row.No, Is.EqualTo("8"));
            Assert.That(row.Title, Is.EqualTo("다시 문을 연 가게"));
            Assert.That(row.Small, Is.EqualTo("바로편의점 · D+13 · 처음 본 날 2026-11-16"));
            Assert.That(row.Sub, Is.EqualTo(new[] { Deeds.Get("blackout_radio").Short, Deeds.Get("hall_checked").Short }));
            var unseen = v.Rows.First(r => !r.Seen);
            Assert.That(unseen.Small, Is.EqualTo(Endings.All.First(e => e.No.ToString() == unseen.No).Hint));

            var direct = DebriefLogic.Gallery(JArray.Parse(@"[ { ""no"": 3, ""seen"": true, ""title"": ""t"", ""place"": ""p"", ""shorts"": [] } ]"));
            Assert.That(direct.Rows[0].Small, Is.EqualTo("p"));
        }

        [Test]
        public void Records_ChallengesRanksAndBests()
        {
            var rec = new Records();
            rec.Done["precise"] = "2026-11-01";
            rec.Best["prep"] = 65000;
            rec.Runs.Add(new RunRecord { Ending = "reopen", N = 3, Retries = 1, Ms = 125000, At = "2026-11-02" });
            var v = DebriefLogic.Records(rec);
            int len = Challenges.All.Count;
            Assert.That(v.Title, Is.EqualTo($"도전 과제 1/{len}"));
            var precise = v.Challenges[Challenges.All.ToList().FindIndex(c => c.Id == "precise")];
            Assert.That(precise.No, Is.EqualTo("✓"));
            Assert.That(precise.Small, Does.StartWith("처음 달성 2026-11-01 · "));
            Assert.That(v.Challenges[0].No, Is.EqualTo("1"));
            Assert.That(v.Ranks.Single().Bold, Is.EqualTo("1위"));
            Assert.That(v.Ranks[0].Text, Is.EqualTo("엔딩 8 · 다시 문을 연 가게"));
            Assert.That(v.Ranks[0].Small, Is.EqualTo($"과제 3/{len} · 다시 하기 1번 · 2분 05초 · 2026-11-02"));
            Assert.That(v.Bests.Count, Is.EqualTo(6));
            Assert.That(v.Bests[0].Bold, Is.EqualTo("프롤로그"));
            Assert.That(v.Bests[0].Small, Is.EqualTo("—"));
            Assert.That(v.Bests[1].Text, Is.EqualTo("준비"));
            Assert.That(v.Bests[1].Small, Is.EqualTo("1분 05초"));

            var empty = DebriefLogic.Records(Challenges.ReadRecords((string)null));
            Assert.That(empty.Ranks, Is.Empty);
            Assert.That(empty.RankEmpty, Is.EqualTo("처음부터 엔딩까지 한 번 마치면 순위가 남습니다."));
        }

        [Test]
        public void ElementChildrenPrintNothingForMissingValues_TemplatesPrintUndefined()
        {
            // el('span', {}, x) skips null/undefined; `${x}` prints "undefined" (dom.js append vs template literals).
            Assert.That(DebriefLogic.Kid(null), Is.EqualTo(""));
            Assert.That(DebriefLogic.Kid(JValue.CreateNull()), Is.EqualTo(""));
            Assert.That(DebriefLogic.Kid(new JValue(true)), Is.EqualTo(""));
            Assert.That(DebriefLogic.Kid(new JValue(0)), Is.EqualTo("0"));
            var v = DebriefLogic.Ending(J(@"{ ""unused"": [ { ""why"": ""w"" } ], ""unchecked"": [ ""u"" ],
                ""fate"": { ""title"": ""t"", ""people"": [ { ""present"": true } ], ""missed"": [ { ""title"": ""m"" } ] } }"), null, _ => null);
            Assert.That(v.Title, Is.EqualTo("엔딩  · t"));
            Assert.That(v.Sections.First(s => s.Title == "쓰지 못한 물건과 그 이유").Items[0].Bold, Is.EqualTo(""));
            Assert.That(v.Sections.First(s => s.Title == "그 뒤의 사람들").Items[0].Text, Is.EqualTo(""));
            Assert.That(v.Sections.First(s => s.Title == "다른 길이 있었다면").Items[0].Text, Is.EqualTo(""));
            var c = DebriefLogic.ChapterCard(J(@"{ ""chapter"": null, ""title"": null }"));
            Assert.That(c.Eyebrow, Is.EqualTo("null 끝"));
            Assert.That(c.Title, Is.EqualTo(""));
        }
    }
}
