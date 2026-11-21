using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AiOn.Game;
using AiOn.Story.Testing;
using NUnit.Framework;

namespace AiOn.Tests
{
    /// <summary>우리 집 안전 준비: the family card's fields and checks, the A6 layouts, the checklist and the training guide.</summary>
    public class HomePrepTests
    {
        static Facts Facts => FakeStoryHost.LoadFacts(); // facts.json + facts-ext.json, as the game reads them

        static Dictionary<string, string> V(params (string id, string v)[] pairs) => pairs.ToDictionary(p => p.id, p => p.v);

        // ---------------------------------------------------------------- A6 layouts

        [Test]
        public void A6_at_300_dpi_is_1240_by_1748_pixels()
        {
            var (w, h) = PrintLayout.A6Pixels();
            Assert.AreEqual(1240, w);
            Assert.AreEqual(1748, h);
            Assert.AreEqual(148.0 / 105.0, h / (double)w, 0.002, "portrait A6 proportions");
            Assert.AreEqual((620, 874), PrintLayout.A6Pixels(150));
        }

        static void AssertInsideAndApart(List<PrintLayout.Cell> cells, int w, int h)
        {
            foreach (var c in cells)
            {
                Assert.That(c.X, Is.GreaterThanOrEqualTo(0), c.Id);
                Assert.That(c.Y, Is.GreaterThanOrEqualTo(0), c.Id);
                Assert.That(c.Right, Is.LessThanOrEqualTo(w + 0.01f), c.Id);
                Assert.That(c.Bottom, Is.LessThanOrEqualTo(h + 0.01f), c.Id);
                Assert.That(c.W, Is.GreaterThan(0), c.Id);
                Assert.That(c.H, Is.GreaterThan(0), c.Id);
            }
            for (int i = 0; i < cells.Count; i++)
                for (int j = i + 1; j < cells.Count; j++)
                    Assert.IsFalse(PrintLayout.Overlaps(cells[i], cells[j]), $"{cells[i].Kind}:{cells[i].Id} overlaps {cells[j].Kind}:{cells[j].Id}");
        }

        [Test]
        public void The_card_layout_fits_A6_with_every_field_once_and_nothing_overlapping()
        {
            foreach (var dpi in new[] { 300, 150 })
            {
                var (w, h) = PrintLayout.A6Pixels(dpi);
                var cells = PrintLayout.Card(w, h);
                AssertInsideAndApart(cells, w, h);
                var fields = cells.Where(c => c.Kind == "field").Select(c => c.Id).ToList();
                CollectionAssert.AreEqual(FamilyCard.Fields.Select(f => f.Id), fields, "every field once, in tab order");
                CollectionAssert.AreEqual(FamilyCard.Sections.Select(s => s.id), cells.Where(c => c.Kind == "section").Select(c => c.Id));
                CollectionAssert.AreEqual(FamilyCard.Hotlines.Select(x => x.number), cells.Where(c => c.Kind == "hotline").Select(c => c.Id));
                Assert.AreEqual(1, cells.Count(c => c.Kind == "band"));
                var note = cells.Single(c => c.Kind == "note");
                Assert.That(note.Y, Is.GreaterThan(cells.Where(c => c.Kind != "note").Max(c => c.Bottom)), "the foot note under everything");
                var hot = cells.Single(c => c.Kind == "hotnote");
                Assert.AreEqual(FamilyCard.HotlineNoteFact, hot.Id);
                Assert.That(hot.Y, Is.GreaterThanOrEqualTo(cells.Where(c => c.Kind == "hotline").Max(c => c.Bottom)), "the 119·112 fact sits under the numbers");
            }
        }

        [Test]
        public void Each_field_s_longest_value_fits_its_box_at_print_size()
        {
            var (w, h) = PrintLayout.A6Pixels();
            const float valuePx = 36; // HomePrepArt.Card's value size at 300 dpi; best-fit only shrinks below it
            foreach (var c in PrintLayout.Card(w, h).Where(c => c.Kind == "field"))
            {
                var f = FamilyCard.Get(c.Id);
                Assert.IsNotNull(f, c.Id);
                float glyph = f.Kind == FamilyCard.Kind.Phone ? valuePx * 0.6f : valuePx; // digits are about half a Hangul syllable wide
                Assert.That(f.Max * glyph, Is.LessThanOrEqualTo(c.W - 8), $"{c.Id}: {f.Max} characters in {c.W}px");
                Assert.AreEqual(f.Label, c.Text);
            }
        }

        [Test]
        public void The_checklist_layout_fits_A6_with_one_row_per_item()
        {
            var (w, h) = PrintLayout.A6Pixels();
            var cells = PrintLayout.Checklist(w, h);
            AssertInsideAndApart(cells, w, h);
            CollectionAssert.AreEqual(HomeChecklist.Items.Select(i => i.Id), cells.Where(c => c.Kind == "item").Select(c => c.Id));
            Assert.AreEqual(1, cells.Count(c => c.Kind == "date"));
        }

        // ---------------------------------------------------------------- the card's fields and checks

        [Test]
        public void Fields_have_unique_ids_known_sections_limits_and_generic_hints()
        {
            Assert.AreEqual(FamilyCard.Fields.Length, FamilyCard.Fields.Select(f => f.Id).Distinct().Count());
            var sections = FamilyCard.Sections.Select(s => s.id).ToList();
            foreach (var f in FamilyCard.Fields)
            {
                Assert.Contains(f.Section, sections, f.Id);
                Assert.That(f.Max, Is.InRange(8, 40), f.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(f.Label), f.Id);
                // examples never borrow the fictional town or a real-looking number
                Assert.That(f.Hint ?? "", Does.Not.Contain("연무").And.Not.Contain("한빛"), f.Id);
                Assert.IsFalse(Regex.IsMatch(f.Hint ?? "", @"\d{3,4}-[1-9]\d{3}"), f.Id + ": a hint shows 0000, not a dialable number");
            }
            // what 국민안전24 says to prepare: 개인사항, 가족사항, 혈액형, 복용약, 집·직장·학교 연락처 (one number each), 만남의 장소
            // 1·2·3차, the nearest shelter, 신고·구호기관
            foreach (var id in new[] { "name", "blood", "meds", "fam1", "fam1Phone", "placeHome", "placeWork", "placeSchool", "meet1", "meet2", "meet3", "shelter" })
                Assert.IsNotNull(FamilyCard.Get(id), id);
            foreach (var id in new[] { "placeHome", "placeWork", "placeSchool" })
                Assert.AreEqual(FamilyCard.Kind.Phone, FamilyCard.Get(id).Kind, id + ": one number per place, not three squeezed in one box");
            foreach (var f in FamilyCard.Fields.Where(x => x.Kind == FamilyCard.Kind.Phone))
                Assert.IsFalse(string.IsNullOrEmpty(f.Owner), f.Id + " names whose number it is in messages");
            CollectionAssert.AreEqual(new[] { "119", "112", "1338" }, FamilyCard.Hotlines.Select(x => x.number));
        }

        [Test]
        public void Clean_keeps_one_trimmed_line_within_the_limit()
        {
            Assert.AreEqual("", FamilyCard.Clean(null, 10));
            Assert.AreEqual("홍 길동", FamilyCard.Clean("  홍\n\t길동  ", 10));
            Assert.AreEqual("a b", FamilyCard.Clean("a     b", 0));
            Assert.AreEqual("가나다", FamilyCard.Clean("가나다라마", 3));
            Assert.AreEqual("ab", FamilyCard.Clean("ab \u0001", 10));
            string emoji = "ab" + char.ConvertFromUtf32(0x1F600);
            Assert.AreEqual("ab", FamilyCard.Clean(emoji, 3), "never half a surrogate pair");
        }

        [Test]
        public void Phone_numbers_are_digits_and_dashes_with_7_to_15_digits()
        {
            Assert.IsTrue(FamilyCard.IsPhone(""));
            Assert.IsTrue(FamilyCard.IsPhone("010-1234-5678"));
            Assert.IsTrue(FamilyCard.IsPhone("02 123 4567"));
            Assert.IsTrue(FamilyCard.IsPhone("+82 10 1234 5678"));
            Assert.IsTrue(FamilyCard.IsPhone("(031)123-4567"));
            Assert.IsFalse(FamilyCard.IsPhone("119"), "too short for a family phone");
            Assert.IsFalse(FamilyCard.IsPhone("010-12ab-5678"));
            Assert.IsFalse(FamilyCard.IsPhone("0101234567890123"), "16 digits");
            Assert.IsTrue(FamilyCard.PhoneChar('7') && FamilyCard.PhoneChar('-') && !FamilyCard.PhoneChar('a') && !FamilyCard.PhoneChar('\n'));
            Assert.IsNull(FamilyCard.Problem(FamilyCard.Get("fam1Phone"), "010-1234-5678"));
            Assert.That(FamilyCard.Problem(FamilyCard.Get("fam1Phone"), "12"), Does.Contain("7~15"));
            Assert.That(FamilyCard.Problem(FamilyCard.Get("name"), new string('가', 13)), Does.Contain("12자"));
        }

        [Test]
        public void Status_names_the_problem_then_what_is_blank_then_ready()
        {
            var blank = V();
            Assert.IsTrue(FamilyCard.CanSave(blank), "a blank card prints to be filled in by hand");
            Assert.That(FamilyCard.Status(blank), Does.Contain("손으로"));
            CollectionAssert.AreEqual(new[] { "가족 전화", "1차 만남의 장소", "가까운 대피소" }, FamilyCard.Missing(blank));

            var bad = V(("fam2Phone", "01-2"), ("fam2", "엄마"));
            Assert.IsFalse(FamilyCard.CanSave(bad));
            Assert.AreEqual("가족 2 전화번호를 7~15자리 숫자로 적어 주세요", FamilyCard.Status(bad));
            // while that number is still being typed it is not an error yet (saving still checks it)
            Assert.That(FamilyCard.Status(bad, skip: "fam2Phone"), Does.Not.Contain("전화번호를"));
            Assert.IsEmpty(FamilyCard.Problems(bad, "fam2Phone"));
            Assert.AreEqual("집 전화번호를 7~15자리 숫자로 적어 주세요", FamilyCard.Status(V(("placeHome", "12"))));
            Assert.That(FamilyCard.Status(V(("name", new string('가', 13)))), Does.StartWith("이름: 12자"));

            var nameless = V(("fam1Phone", "010-1234-5678"), ("meet1", "정문"), ("shelter", "주민센터 지하"));
            Assert.That(FamilyCard.Status(nameless), Does.Contain("가족 1의 이름"));
            CollectionAssert.AreEqual(new[] { 1 }, FamilyCard.PhonesWithoutName(nameless));

            var some = V(("fam1", "아빠"), ("fam1Phone", "010-1234-5678"));
            Assert.That(FamilyCard.Status(some), Does.StartWith("아직 빈 칸").And.Contain("1차 만남의 장소").And.Not.Contain("가족 전화"));

            var full = V(("fam1", "아빠"), ("fam1Phone", "010-1234-5678"), ("meet1", "정문"), ("shelter", "주민센터 지하"));
            Assert.IsEmpty(FamilyCard.Missing(full));
            Assert.That(FamilyCard.Status(full), Does.StartWith("다 적었습니다"));
        }

        [Test]
        public void The_png_goes_to_a_dated_file_named_for_what_it_is_never_for_what_is_written()
        {
            var when = new DateTime(2026, 11, 21, 9, 5, 7);
            Assert.AreEqual("family-card-20261121-090507.png", FamilyCard.FileName(FamilyCard.CardStem, when));
            Assert.AreEqual("home-checklist-20261121-090507.png", FamilyCard.FileName(FamilyCard.ChecklistStem, when));
            string dir = Path.Combine("x", "y");
            var taken = new HashSet<string> { Path.Combine(dir, "a.png"), Path.Combine(dir, "a-2.png") };
            Assert.AreEqual(Path.Combine(dir, "a-3.png"), FamilyCard.UniquePath(dir, "a.png", taken.Contains));
            Assert.AreEqual(Path.Combine(dir, "b.png"), FamilyCard.UniquePath(dir, "b.png", taken.Contains));

            var (pics, inPics, synced0) = FamilyCard.SaveFolder(Path.Combine("C:", "Users", "someone", "Pictures"), "data");
            Assert.IsTrue(inPics);
            Assert.IsFalse(synced0);
            Assert.That(pics, Does.EndWith(FamilyCard.PicturesFolder));
            var (fallback, inPics2, synced2) = FamilyCard.SaveFolder("", "data");
            Assert.IsFalse(inPics2);
            Assert.IsFalse(synced2);
            Assert.AreEqual(Path.Combine("data", "cards"), fallback);

            string line = FamilyCard.SavedLine(Path.Combine(pics, "family-card-20261121-090507.png"), true);
            Assert.That(line, Does.Contain("사진 폴더").And.Contain("family-card-20261121-090507.png").And.Not.Contain("someone"), "no account name on screen");
            Assert.AreEqual("2026.11.21", FamilyCard.DateLine(when));
        }

        [Test]
        public void A_Pictures_folder_a_cloud_drive_syncs_is_passed_over_for_the_game_s_own_folder()
        {
            var roots = new[] { @"C:\Users\someone\OneDrive", null, "", @"D:\Sync" };
            Assert.IsTrue(FamilyCard.IsSynced(@"C:\Users\someone\OneDrive\사진", roots), "OneDrive folder backup moves Pictures here");
            Assert.IsTrue(FamilyCard.IsSynced(@"c:/users/someone/onedrive/Pictures/", roots), "case and slashes do not matter");
            Assert.IsTrue(FamilyCard.IsSynced(@"D:\OneDrive - 회사\Pictures", null), "a work OneDrive with no variable set");
            Assert.IsTrue(FamilyCard.IsSynced(@"D:\Sync\Pictures", roots));
            Assert.IsFalse(FamilyCard.IsSynced(@"D:\SyncPictures", roots), "a root matches whole folder names only");
            Assert.IsFalse(FamilyCard.IsSynced(@"C:\Users\someone\Pictures", roots));
            Assert.IsFalse(FamilyCard.IsSynced("", roots));

            var (dir, pictures, synced) = FamilyCard.SaveFolder(@"C:\Users\someone\OneDrive\사진", "data", roots);
            Assert.IsFalse(pictures);
            Assert.IsTrue(synced);
            Assert.AreEqual(Path.Combine("data", "cards"), dir);
            string line = FamilyCard.SavedLine(Path.Combine(dir, "family-card-20261121-090507.png"), pictures, synced);
            Assert.That(line, Does.Contain("게임 기록 폴더").And.Contain(FamilyCard.SyncedNote).And.Not.Contain("someone"));
            Assert.That(FamilyCard.SavedSpoken("카드", false), Does.Contain("게임 기록 폴더").And.Not.Contain("사진"));
            Assert.That(FamilyCard.SavedSpoken("카드", true), Does.Contain("사진 폴더").And.Contain(FamilyCard.PicturesFolder));
        }

        [Test]
        public void The_card_says_what_paper_can_do_and_where_its_items_come_from()
        {
            Assert.That(FamilyCard.CardLead, Does.Not.Contain("연락합니다"), "a paper card does not make a call when the network is down");
            Assert.That(FamilyCard.CardLead, Does.Contain("생존배낭"));
            Assert.That(Facts.Text("contact_card"), Does.Contain("생존배낭"), "the lead line follows contact_card");
            Assert.That(FamilyCard.NotOfficial, Does.Contain("준비 항목").And.Contain("만남의 장소").And.Contain("대피소"));
            Assert.That(FamilyCard.NotOfficial, Does.Not.Contain("적으라고"), "the page lists the card's items and the other steps separately");
        }

        [Test]
        public void The_screen_says_plainly_that_nothing_is_kept()
        {
            Assert.AreEqual("이 카드는 저장되지 않습니다. 이미지로만 내려받습니다.", FamilyCard.NotSaved);
            Assert.That(FamilyCard.NotSavedMore, Does.Contain("인터넷"));
            Assert.That(FamilyCard.NotOfficial, Does.Contain("공식 서식은 아닙니다"));
            Assert.That(FamilyCard.CardFootnote, Does.Contain("공식 서식 아님"));
            Assert.That(HomeChecklist.Note, Does.Contain("저장되지 않고"));
            Assert.That(HomeChecklist.PrintNote, Does.Contain("공식 서식 아님").And.Not.Contain("저장"), "the paper does not talk about saving");
        }

        // ---------------------------------------------------------------- sources

        [Test]
        public void Every_fact_the_three_screens_cite_exists_with_a_source()
        {
            var f = Facts;
            var ids = FamilyCard.Facts.Concat(FamilyCard.HotlineFacts).Concat(HomeChecklist.AllFacts())
                .Concat(TrainingGuide.Entries.SelectMany(e => e.Facts)).Distinct().ToList();
            foreach (var id in ids)
            {
                var fact = f.Fact(id);
                Assert.IsNotNull(fact, id);
                var src = f.Source(fact.SourceId);
                Assert.IsNotNull(src, id + " -> " + fact.SourceId);
                Assert.That(src.Url, Does.StartWith("https://"), id);
            }
            Assert.That(f.Text("contact_fields"), Does.Contain("혈액형").And.Contain("복용약"));
            Assert.That(f.Text("contact_meet"), Does.Contain("1차, 2차, 3차"));
            Assert.That(f.Text("report_object"), Does.Contain("1338"));
            Assert.That(f.Text("comms_112"), Does.Contain("119, 112"));
        }

        [Test]
        public void Checklist_items_are_each_sourced_and_cover_what_was_asked()
        {
            CollectionAssert.AreEqual(new[] { "exits", "partition", "hatch", "gauge", "stairs", "corridor", "shelter", "supplies", "card" }, HomeChecklist.Items.Select(i => i.Id));
            foreach (var i in HomeChecklist.Items)
            {
                Assert.IsNotEmpty(i.Facts, i.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(i.Title) || string.IsNullOrWhiteSpace(i.How), i.Id);
                // no sizes for 경량칸막이 · 대피공간 · 하향식 피난구, and no 욕실 (not in the 소방청 source)
                Assert.IsFalse(Regex.IsMatch(i.How + i.Where, @"\d+\s*(cm|m|㎝|㎡)"), i.Id);
                Assert.That(i.How, Does.Not.Contain("욕실"), i.Id);
            }
            Assert.That(HomeChecklist.Get("shelter").Where, Does.Contain("안전디딤돌").And.Contain("safekorea.go.kr").And.Not.Contain("http"));
            foreach (var id in new[] { "home_exits_know", "home_hatch", "home_exit_clear", "home_unaware", "shelter_apps" })
                Assert.That(Facts.Text(id), Does.Not.Contain("욕실").And.Not.Match(@"\d+\s*(cm|m|㎝|㎡)"), id);
        }

        [Test]
        public void Checklist_marks_cycle_and_count()
        {
            var partition = HomeChecklist.Get("partition");
            var stairs = HomeChecklist.Get("stairs");
            var gauge = HomeChecklist.Get("gauge");
            Assert.IsTrue(partition.CanBeAbsent && partition.CanFix);
            Assert.IsFalse(stairs.CanBeAbsent, "every home has a way down");
            Assert.IsTrue(gauge.CanFix && !gauge.CanBeAbsent, "a needle out of the green is something to fix");
            Assert.AreEqual(HomeChecklist.Mark.Done, HomeChecklist.Next(partition, HomeChecklist.Mark.Todo));
            Assert.AreEqual(HomeChecklist.Mark.Fix, HomeChecklist.Next(partition, HomeChecklist.Mark.Done));
            Assert.AreEqual(HomeChecklist.Mark.Absent, HomeChecklist.Next(partition, HomeChecklist.Mark.Fix));
            Assert.AreEqual(HomeChecklist.Mark.Todo, HomeChecklist.Next(partition, HomeChecklist.Mark.Absent));
            Assert.AreEqual(HomeChecklist.Mark.Todo, HomeChecklist.Next(stairs, HomeChecklist.Mark.Done));
            Assert.AreEqual(HomeChecklist.Mark.Todo, HomeChecklist.Next(gauge, HomeChecklist.Mark.Fix));
            Assert.AreEqual("아직 → 확인함 → 고칠 것 → 우리 집엔 없음", HomeChecklist.CycleText(partition));
            Assert.AreEqual("아직 → 확인함", HomeChecklist.CycleText(stairs));

            var marks = new Dictionary<string, HomeChecklist.Mark> { ["exits"] = HomeChecklist.Mark.Done, ["hatch"] = HomeChecklist.Mark.Absent, ["gauge"] = HomeChecklist.Mark.Fix };
            Assert.AreEqual((1, 1, 1, 6), HomeChecklist.Count(marks));
            Assert.AreEqual("확인 2 / 9", HomeChecklist.SummaryHead(marks));
            Assert.AreEqual("고칠 것 1 · 없음 1 · 남음 6", HomeChecklist.SummaryRest(marks));
            Assert.AreEqual("확인 2 / 9 · 고칠 것 1 · 없음 1 · 남음 6", HomeChecklist.Summary(marks));
            Assert.AreEqual("확인 0 / 9 · 남음 9", HomeChecklist.Summary(new Dictionary<string, HomeChecklist.Mark>()));
            Assert.AreEqual(HomeChecklist.Mark.Todo, HomeChecklist.MarkOf(null, "exits"));
            Assert.AreEqual("우리 집엔 없음", HomeChecklist.MarkText(HomeChecklist.Mark.Absent));
            Assert.AreEqual("고칠 것", HomeChecklist.MarkText(HomeChecklist.Mark.Fix));
            // one name on screen and on paper; not only 피난시설
            Assert.AreEqual(HomeChecklist.Title, PrintLayout.Checklist(1240, 1748).Single(c => c.Kind == "band").Text);
            Assert.That(HomeChecklist.Subtitle, Does.Contain("소화기").And.Contain("비상용품"));
        }

        [Test]
        public void Training_guide_names_agencies_and_sites_only()
        {
            Assert.That(TrainingGuide.Entries.Select(e => e.Agency), Is.SupersetOf(new[] { "국민안전교육플랫폼", "대한심폐소생협회", "소방청" }));
            foreach (var e in TrainingGuide.Entries)
            {
                Assert.IsNotEmpty(e.Where, e.Id);
                Assert.IsNotEmpty(e.Facts, e.Id);
                foreach (var w in e.Where)
                {
                    Assert.IsTrue(Regex.IsMatch(w, @"\b[\w.]+\.(go\.kr|or\.kr|org)\b"), e.Id + ": a site address as text: " + w);
                    Assert.That(w, Does.Not.Contain("http"), "shown as text, not a link the game opens");
                }
                string all = e.Title + e.What + string.Join(" ", e.Where);
                Assert.IsFalse(Regex.IsMatch(all, @"\d{2,4}-\d{3,4}-?\d{0,4}"), e.Id + ": no phone numbers");
                Assert.IsFalse(Regex.IsMatch(all, @"(로|길)\s?\d+|번지|\d+시\s?\d*분?|\d+월|\d+원"), e.Id + ": no address, schedule or fee");
            }
            Assert.That(TrainingGuide.Note, Does.Contain("주소·일정·요금"));
            // the checked date matches the sources added for it
            var f = Facts;
            foreach (var id in new[] { "train_platform", "train_list", "train_metaverse", "train_cpr" })
                Assert.AreEqual(TrainingGuide.CheckedAt, f.Source(f.Fact(id).SourceId).CheckedAt, id);
            Assert.That(f.Text("cpr_practice"), Does.Contain("실습"));
            // the CPR entry rests on the association's own page, not on a remark in an interview
            Assert.That(f.Source(f.Fact("train_cpr").SourceId).Url, Does.Contain("kacpr.org"));
            Assert.That(f.Text("train_cpr"), Does.Contain("교육기관 찾기"));
        }
    }
}
