// 더 알아보기 (Game/Archive.cs): the 기록관 entries and their facts, the 신고 번호 카드, the resident lines, the stored
// progress (read, merge, viewed, reveal) and what the default playthrough unlocks.
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AiOn.Game;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class ArchiveTests
    {
        static Facts F => FakeStoryHost.LoadFacts();

        static GameState Game(params (string type, string details)[] extra) => GoldenRuns.Run(null, null, extra);

        static IEnumerable<string> AllText() =>
            Archive.Entries.SelectMany(e => new[] { e.Title, e.InGame, e.Hint, e.ChangeTitle })
                .Concat(Archive.Cards.SelectMany(c => new[] { c.Title, c.Earned, c.Hint, c.Number }))
                .Concat(Archive.Residents.SelectMany(r => new[] { r.Name, r.Role, r.Profile }))
                .Concat(Archive.DrillMonths.SelectMany(m => new[] { m.Label, m.Who })).Append(Archive.CalendarNote).Append(Archive.AheadHint)
                .Concat(Archive.AllFactIds.Select(id => F.Text(id)));

        [Test]
        public void Every_quoted_fact_exists_with_a_dated_source()
        {
            var f = F;
            foreach (var id in Archive.AllFactIds)
            {
                Assert.That(f.Text(id), Is.Not.Null.And.Not.Empty, id);
                var src = f.Source(f.SourceId(id));
                Assert.That(src, Is.Not.Null, id);
                Assert.That(src.Url, Does.StartWith("https://"), id);
            }
            // the new history and calendar facts were checked on 11-21 (the page itself, not a summary)
            foreach (var id in new[] { "arch_1968_event", "arch_1968_change", "arch_1975_event", "arch_1975_change", "arch_1975_why", "arch_2010_event", "arch_2010_change",
                "arch_2024_event", "arch_2024_change", "train_ulchi", "train_drill_2026", "train_plan_2026", "train_who_2026" })
            {
                Assert.That(f.IsExtension(id), id + " lives in facts-ext.json");
                Assert.That(f.Source(f.SourceId(id)).CheckedAt, Does.Match(@"^2026-11-2[01]$"), id);
            }
        }

        [Test]
        public void The_archive_reads_calm_names_no_one_and_uses_only_verified_numbers()
        {
            foreach (var t in AllText().Where(t => t != null))
            {
                Assert.That(t, Does.Not.Contain("희생").And.Not.Contain("사망").And.Not.Contain("전사").And.Not.Contain("사살").And.Not.Contain("김신조"), t);
                Assert.IsFalse(Regex.IsMatch(t, @"(?<!\d)(1337|111|113|110)(?!\d)"), t + ": unverified number");
                Assert.That(t, Does.Not.Contain("2분마다"), t);
            }
            Assert.That(Archive.CalendarNote, Does.Contain("매년 행정안전부 공지"), "dates change every year");
        }

        [Test]
        public void Entries_run_in_time_order_each_from_an_event_to_what_followed()
        {
            Assert.That(Archive.Entries.Select(e => e.Year), Is.EqualTo(new[] { "1968", "1975", "1996", "2010", "2023", "2024" }));
            Assert.That(Archive.Entries.Select(e => e.Id).Distinct().Count(), Is.EqualTo(Archive.Entries.Count));
            foreach (var e in Archive.Entries)
            {
                Assert.That(e.ChangeFacts, Is.Not.Empty, e.Id);
                Assert.That(e.EventFacts, Is.Not.Empty, e.Id + " starts from what happened");
                Assert.That(e.Kind, Is.EqualTo("제도").Or.EqualTo("기록").Or.EqualTo("안내"), e.Id);
                Assert.That(e.Hint, Does.EndWith("열립니다."), e.Id);
                Assert.That(Archive.ChapterOrder, Does.Contain(e.Ch), e.Id);
            }
            // 2024: a stated plan and the standing guidance, not an enacted change
            Assert.That(Archive.Entry("y2024").Kind, Is.EqualTo("안내"));
            Assert.That(F.Text("arch_2024_change"), Does.Contain("밝혔습니다"));
            Assert.That(Archive.Entry("y2010").ChangeWhen, Is.Null, "no date the facts do not state");
            Assert.That(Archive.Entry("y2023").ChangeFacts, Does.Contain("alert_text_fix"), "2023 오발령 → 경보 문자에 사유·대피 방법");
            Assert.That(Archive.Entry("y2024").ChangeFacts, Does.Contain("report_object"), "→ 1338·경찰서 신고 안내");
            Assert.That(F.Text("arch_1968_change"), Does.Contain("1968년 4월 1일").And.Contain("향토예비군"));
            Assert.That(F.Text("alert_text_fix"), Does.Contain("2023년 8월 23일"));
        }

        [Test]
        public void Cards_are_the_verified_channels_with_when_to_use_lines()
        {
            Assert.That(Archive.Cards.Select(c => c.Number), Is.EqualTo(new[] { "112", "119", "1338", "경찰서", "관리사무소" }));
            foreach (var c in Archive.Cards) { Assert.That(c.Facts, Is.Not.Empty, c.Id); Assert.That(Archive.ChapterOrder, Does.Contain(c.Ch), c.Id); }
            Assert.That(F.Text("report_object"), Does.Contain("1338").And.Contain("가까운 경찰서"));
        }

        [Test]
        public void An_empty_run_unlocks_nothing()
        {
            var p = Archive.Scan(Game());
            Assert.That(p.Entries, Is.Empty);
            Assert.That(p.Cards, Is.Empty);
            Assert.That(p.Residents, Is.Empty);
            Assert.That(Archive.Scan(null).Entries, Is.Empty);
        }

        [Test]
        public void What_happened_in_the_story_opens_entries_and_earns_cards()
        {
            var p = Archive.Scan(Game(("chapter_time", "{id:'prologue',ms:1000,retries:0}"), ("record_card", "{id:'1996'}"),
                ("unknown_object", "{first:'shout',reported:'1338'}"), ("twentytwo", "{played:true}")));
            Assert.That(p.Entries, Is.EqualTo(new[] { "y1975", "y1996", "y2023", "y2024" }));
            Assert.That(p.Cards, Is.EqualTo(new[] { "c1338" }));
            Assert.That(p.Residents["jeongmin"], Is.EqualTo(new[] { "지나온 장: 프롤로그" }));

            // the nearest police station is its own card; telling only the office reports nothing (no card)
            Assert.That(Archive.Scan(Game(("unknown_object", "{reported:'police'}"))).Cards, Is.EqualTo(new[] { "cPolice" }), "a save from before the channel was kept");
            Assert.That(Archive.Scan(Game(("unknown_object", "{reported:'police',via:'station'}"))).Cards, Is.EqualTo(new[] { "cPolice" }));
            Assert.That(Archive.Scan(Game(("unknown_object", "{reported:'police',via:'text112'}"))).Cards, Is.EqualTo(new[] { "c112" }), "a deaf player's 112 text is the 112 card, not the 파출소");
            Assert.That(Archive.Scan(Game(("unknown_object", "{reported:'office'}"))).Cards, Is.Empty);
            Assert.That(Archive.Scan(Game(("unknown_object", "{reported:'office1338'}"))).Cards, Is.EqualTo(new[] { "c1338" }), "asking the office to call 1338 with the phone dead");

            // 112: the sighting card sent to the police; a chat post is not a report
            Assert.That(Archive.Scan(Game(("sighting", "{played:true,channel:'text112'}"))).Cards, Does.Contain("c112"));
            Assert.That(Archive.Scan(Game(("sighting", "{played:true,channel:'chat'}"))).Cards, Is.Empty);
            // 119: the 119 call card with the place, or a bystander named to call
            Assert.That(Archive.Scan(Game(("fire_report", "{played:true,place:true}"))).Cards, Is.EqualTo(new[] { "c119" }));
            Assert.That(Archive.Scan(Game(("cpr", "{played:true,named:true}"))).Cards, Is.EqualTo(new[] { "c119" }));
            // the same call made from home, inside 살펴서 대피 (no separate fire_report event), with one item missed
            Assert.That(Archive.Scan(Game(("lookfirst", "{played:true,act:'stay',report:{played:true,place:true,what:false,people:true}}"))).Cards, Is.EqualTo(new[] { "c119" }));
            Assert.That(Archive.Scan(Game(("lookfirst", "{played:true,act:'stay',report:{played:true,place:false}}"))).Cards, Is.Empty, "no place, no card");
            Assert.That(Archive.Scan(Game(("lookfirst", "{played:true,act:'stay',report:null}"))).Cards, Is.Empty);
            Assert.That(Archive.Scan(Game(("cpr", "{played:true,named:false}"))).Cards, Is.Empty);
            // 관리사무소: the intercom checked with the office, the extinguisher gauge reported
            Assert.That(Archive.Scan(Game(("intercom", "{asked:true,opened:false,office:true}"))).Cards, Is.EqualTo(new[] { "cOffice" }));
            Assert.That(Archive.Scan(Game((ExtinguisherCheck.Event, "{action:'report'}"))).Cards, Is.EqualTo(new[] { "cOffice" }));
        }

        [Test]
        public void Residents_follow_the_run()
        {
            var g = GoldenRuns.Run(null, new[] { "seoyeon_contact", "manager_contact" });
            g.SetKnow("elderPick", "help");
            var p = Archive.Scan(g);
            Assert.That(p.Residents["elder"][0], Is.EqualTo("경보 때 정민과 함께 계단을 내려왔다"));
            Assert.That(p.Residents["seoyeon"], Is.EqualTo(new[] { "서연과 연락 방법을 정했다" }));
            Assert.That(p.Residents["manager"], Is.EqualTo(new[] { "박 소장과 연락 방법을 정했다" }));
            Assert.That(p.Residents.ContainsKey("jiwoo"), Is.False, "not met in this run");

            var told = Game(); told.SetKnow("elderPick", "manager"); told.SetKnow("elderTold", true);
            Assert.That(Archive.Scan(told).Residents["elder"][0], Is.EqualTo("경보 때 박 소장과 함께 내려왔다"));
            var untold = Game(); untold.SetKnow("elderPick", "manager");
            Assert.That(Archive.Scan(untold).Residents["elder"][0], Is.EqualTo("경보 때 기다리다 스스로 내려왔다"));
            Assert.That(Archive.Scan(Game(("shelter_life", "{played:true,child:'watch'}"))).Residents.ContainsKey("jiwoo"), Is.True);
            foreach (var r in Archive.Residents) Assert.That(Archive.Pin(r.Pin), Is.Not.Null, r.Id + " has a place on the map");
        }

        [Test]
        public void Stored_progress_is_sanitised_merged_and_marked_seen()
        {
            foreach (var raw in new[] { null, "", "{", "[]", "7", "{\"entries\":[\"nope\",3],\"cards\":\"c112\",\"residents\":{\"x\":[\"a\"],\"elder\":5}}" })
            {
                var e = Archive.Read(raw);
                Assert.That(e.Entries.Count + e.Cards.Count + e.Residents.Count + e.Viewed.Count, Is.Zero, raw);
            }
            var stored = Archive.Read("{\"entries\":[\"y2024\",\"y1975\",\"y1975\"],\"cards\":[\"c112\"],\"residents\":{\"elder\":[\"옛 줄\"]},\"viewed\":[\"y1975\",\"zzz\"]}");
            Assert.That(stored.Entries, Is.EqualTo(new[] { "y2024", "y1975" }));
            Assert.That(stored.Viewed, Is.EqualTo(new[] { "y1975" }));
            Assert.That(stored.IsNew("y2024") && !stored.IsNew("y1975") && stored.NewCount == 2);

            var run = new ArchiveProgress { Entries = { "y1968" }, Cards = { "c1338" }, Residents = { ["seoyeon"] = new List<string> { "새 줄" } } };
            var m = Archive.Merge(stored, run);
            Assert.That(m.Entries, Is.EqualTo(new[] { "y1968", "y1975", "y2024" }), "a union, in table order");
            Assert.That(m.Cards, Is.EqualTo(new[] { "c112", "c1338" }));
            Assert.That(m.Residents["seoyeon"], Is.EqualTo(new[] { "새 줄" }), "the run that met her last");
            Assert.That(m.Residents["elder"], Is.EqualTo(new[] { "옛 줄" }), "a neighbour this run has not met keeps the older lines");
            Assert.That(Archive.Merge(m, new ArchiveProgress { Residents = { ["seoyeon"] = new List<string> { "더 새 줄" } } }).Residents["seoyeon"], Is.EqualTo(new[] { "더 새 줄" }));
            Assert.That(Archive.NeighborsMet(m), Is.EqualTo(2));
            Assert.That(Archive.NeighborsMet(new ArchiveProgress { Residents = { ["jeongmin"] = new List<string> { "a" } } }), Is.Zero, "정민 is not his own neighbour");
            Assert.That(Archive.Read(m.ToJson()).ToJson(), Is.EqualTo(m.ToJson()), "round trip");

            Archive.MarkViewed(m);
            Assert.That(m.NewCount, Is.Zero);
            var all = Archive.RevealAll(stored);
            Assert.That(all.Entries.Count, Is.EqualTo(Archive.Entries.Count));
            Assert.That(all.Cards.Count, Is.EqualTo(Archive.Cards.Count));
            Assert.That(stored.Entries.Count, Is.EqualTo(2), "revealing never touches what is stored");
            Assert.That(Archive.CountLine(stored), Is.EqualTo($"기록 2/{Archive.Entries.Count} · 번호 카드 1/{Archive.Cards.Count}"));
        }

        [Test]
        public void Mid_run_hints_do_not_spoil_the_chapters_ahead()
        {
            var y2024 = Archive.Entry("y2024");
            Assert.That(Archive.HintFor(y2024, "prep"), Is.EqualTo(Archive.AheadHint), "1장: 3장's object stays vague");
            Assert.That(Archive.HintFor(y2024, "endure"), Is.EqualTo(y2024.Hint), "in its own chapter the hint helps");
            Assert.That(Archive.HintFor(y2024, null), Is.EqualTo(y2024.Hint), "the title, teacher mode and scene select show every hint");
            Assert.That(Archive.HintFor(Archive.Card("cOffice"), "dday"), Is.EqualTo(Archive.Card("cOffice").Hint), "a chapter behind is no spoiler");
            Assert.That(Archive.Ahead(Archive.ResidentById("jiwoo").Ch, "endure"), Is.True, "지우네 are met in the temporary stay");
        }

        [Test]
        public void New_unlocks_get_one_notice_and_the_calendar_keeps_one_drill_a_quarter()
        {
            Assert.That(Archive.Notice(new[] { "c1338" }), Does.StartWith("새로 열림 · 신고 번호 카드 1338").And.Contain("더 알아보기"));
            Assert.That(Archive.Notice(new[] { "y2024", "c119" }), Does.Contain("기록관 「2024 정체 모를 물체와 1338」, 신고 번호 카드 119"));
            Assert.That(Archive.Notice(new string[0]), Is.Null);
            var a = new ArchiveProgress { Cards = { "c112" } };
            Assert.That(Archive.Gained(a, new ArchiveProgress { Entries = { "y1975" }, Cards = { "c112", "c119" } }), Is.EqualTo(new[] { "y1975", "c119" }));

            // the 2026 plan: once a quarter, 10~11월 one window; each with who it is for
            Assert.That(Archive.DrillMonths.Count, Is.EqualTo(4), "four drills, not five");
            Assert.That(Archive.DrillMonths.Sum(d => d.Span), Is.EqualTo(5), "March, May, August and October~November");
            foreach (var d in Archive.DrillMonths) Assert.That(d.Who, Is.Not.Empty, d.Month + "월");
            Assert.That(Archive.DrillMonths.Single(d => d.Main).Who, Is.EqualTo("전 국민"));
            Assert.That(F.Text("train_who_2026"), Does.Contain("전 국민").And.Contain("지자체·학교 등"));
        }

        [Test]
        public void The_default_playthrough_opens_the_archive_as_it_goes()
        {
            var r = StoryPlaythroughTests.DefaultRun("archive");
            r.Play();
            var kept = Archive.Read(r.Store.Get(Archive.Key));
            // the careful default run: every chapter, 「22분」, the D+2 day with its record card and the object reported to 1338
            Assert.That(kept.Entries, Is.EqualTo(Archive.Entries.Select(e => e.Id)), "all six entries");
            CollectionAssert.IsSupersetOf(kept.Cards, new[] { "c1338", "cOffice" });
            Assert.That(kept.Residents["elder"][0], Is.EqualTo("경보 때 정민과 함께 계단을 내려왔다"));
            Assert.That(kept.Residents["jeongmin"][0], Does.Contain("5장"));
            Assert.That(kept.Viewed, Is.Empty, "not opened yet");
            // each unlock was told in the story as it happened, once
            var notices = r.Host.Toasts.Where(t => t.StartsWith("새로 열림")).ToList();
            Assert.That(notices, Has.Some.Contains("신고 번호 카드 1338"), "the 1338 card shows the moment it is earned");
            foreach (var e in Archive.Entries) Assert.That(notices.Count(t => t.Contains($"「{e.Year} {e.Title}」")), Is.EqualTo(1), e.Id);
        }
    }
}
