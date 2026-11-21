using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>
    /// 쉬운 한국어 모드 (competition upgrade): the plain wordings of the interface and story lines (EasyKorean.Map) and of the
    /// facts (facts-ext.json "plain"), the rules they keep (same numbers, no hard words, short sentences, the sourced
    /// caveats), and the lookup the views use. The originals never change: the mode only swaps text where it is shown.
    /// </summary>
    public class EasyKoreanTests
    {
        const string BaseFile = "../../../Data/Source/facts.json";
        const string ExtFile = "../../../Data/Source/facts-ext.json";
        static Facts Merged => Facts.Load(Fixture(BaseFile), Fixture(ExtFile));

        [TearDown]
        public void Off() { EasyKorean.On = false; EasyKorean.UseFacts(null); }

        static string AllScripts()
        {
            var dir = Path.Combine(UnityEngine.Application.dataPath, "_AiOn", "Scripts");
            return string.Join("\n", Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f) != "EasyKorean.cs").Select(File.ReadAllText));
        }

        [Test]
        public void Every_mapped_line_is_still_a_line_the_game_shows()
        {
            // a key that no longer appears in the code is a plain wording nobody will see (the original was reworded)
            var src = AllScripts();
            var lost = EasyKorean.Map.Keys.Where(k => !src.Contains(k.Replace("\\", "\\\\").Replace("\"", "\\\""))).ToList();
            Assert.IsEmpty(lost, "reworded originals: " + string.Join(" | ", lost));
        }

        [Test]
        public void Every_plain_line_keeps_the_numbers_avoids_hard_words_and_stays_short()
        {
            foreach (var kv in EasyKorean.Map)
                CollectionAssert.IsEmpty(EasyKorean.Problems(kv.Key, kv.Value), kv.Key);
            Assert.That(EasyKorean.Map.Count, Is.GreaterThanOrEqualTo(60), "tutorial, objectives, minigames, lessons, quiz");
        }

        [Test]
        public void The_rules_catch_a_lost_number_a_hard_word_and_a_long_sentence()
        {
            Assert.That(EasyKorean.Problems("119에 신고합니다.", "소방서에 알립니다."), Has.Some.Contains("119"));
            Assert.That(EasyKorean.Problems("바로 대피합니다.", "즉시 대피합니다."), Has.Some.Contains("즉시"));
            Assert.That(EasyKorean.Problems("짧다.", new string('가', EasyKorean.MaxSentence + 1) + "."), Has.Some.Contains("long"));
            Assert.That(EasyKorean.Problems("같다.", "같다."), Has.Some.Contains("same"));
            CollectionAssert.IsEmpty(EasyKorean.Problems("분당 100~120회", "1분에 100~120번"));
            // an official name kept and glossed where it first appears is fine; left bare, it is still a hard word
            CollectionAssert.IsEmpty(EasyKorean.Problems("명부 쓰기", "명부(이름 적는 종이) 쓰기"));
            Assert.That(EasyKorean.Problems("명부 쓰기", "명부를 쓰기"), Has.Some.Contains("명부"));
        }

        /// <summary>Names of places and things the game uses in its world (signs, buttons, other lines).</summary>
        static readonly string[] KeptNames = { "임시 거처", "명부", "행정복지센터", "대피소", "경량칸막이", "하향식 피난구", "대피공간" };

        static string NoSpace(string s) => s.Replace(" ", "");

        [Test]
        public void A_plain_line_keeps_the_names_the_game_uses_so_one_thing_has_one_name()
        {
            var m = Merged;
            var pairs = EasyKorean.Map.Select(kv => (kv.Key, kv.Value)).Concat(m.PlainTexts.Select(kv => (m.Text(kv.Key), kv.Value)));
            foreach (var (original, plain) in pairs)
                foreach (var name in KeptNames)
                    if (NoSpace(original).Contains(NoSpace(name)))
                        Assert.That(NoSpace(plain), Does.Contain(NoSpace(name)), $"'{name}' kept in: {plain}");
            Assert.AreEqual("임시 거처(잠시 지내는 곳) · 첫날 밤", EasyKorean.Map["임시 거처 첫날 밤"]);
            // 하향식 피난구 is written as the UI and home_hatch write it, in the new plain sentences
            Assert.That(m.Plain("fire_own_trapped"), Does.Contain("하향식 피난구").And.Not.Contain("하향식피난구"));
        }

        [Test]
        public void Plain_fact_wordings_keep_the_conditions_of_the_sentence()
        {
            var m = Merged;
            Assert.That(m.Plain("alert_caution"), Does.Contain("미리 대피"), "사전 대피 (in advance), not an order of priority");
            Assert.That(m.Plain("suspect_report"), Does.Contain("관공서 등에"), "the list of places to report stays open");
        }

        [Test]
        public void Fact_plain_wordings_name_real_facts_and_keep_their_meaning_markers()
        {
            var m = Merged;
            Assert.That(m.PlainTexts.Count, Is.GreaterThanOrEqualTo(60));
            foreach (var kv in m.PlainTexts)
            {
                var original = m.Text(kv.Key);
                Assert.IsNotNull(original, kv.Key);
                CollectionAssert.IsEmpty(EasyKorean.Problems(original, kv.Value), kv.Key);
                // the brief's unverified wording stays out of the plain lines too
                Assert.That(kv.Value, Does.Not.Contain("2분").And.Not.Contain("욕실"), kv.Key);
                Assert.IsFalse(Regex.IsMatch(kv.Value, @"(?<!\d)(1337|111|113)(?!\d)"), kv.Key);
                if (kv.Key.StartsWith("fire_")) Assert.That(kv.Value, Does.Not.Match(@"\d+\s*(cm|m|㎝)"), kv.Key);
                // a foreign or supporting source stays marked in the plain line as in the original
                foreach (var tag in new[] { "(미국", "(캐나다", "(식약처)" })
                    if (original.Contains(tag)) Assert.That(kv.Value, Does.Contain(tag), kv.Key);
            }
            Assert.That(m.Plain("cpr_depth_rate"), Does.Contain("약 5cm").And.Contain("100~120"));
            Assert.That(m.Plain("cpr_ratio"), Does.Contain("30:2").And.Contain("가슴 누르기만 계속"));
            Assert.That(m.Plain("cpr_point"), Does.Contain("한 명").And.Contain("119").And.Contain("AED"));
            Assert.That(m.Plain("alert_levels"), Does.Contain("사이렌 없이").And.Contain("1분"));
            Assert.That(m.Plain("report_object"), Does.Contain("1338").And.Contain("경찰서"));
            Assert.That(m.Plain("fire_stay"), Does.Contain("들어오지 않으면").And.Contain("창문을 닫고").And.Contain("119"));
            Assert.That(m.Plain("fire_seal"), Does.Contain("젖은 수건").And.Contain("119"));
            Assert.That(m.Plain("fire_lift"), Does.Contain("엘리베이터").And.Contain("계단"));
            Assert.IsNull(m.Plain("cpr_fatigue"), "already plain: shown as it is");
        }

        [Test]
        public void The_original_facts_are_untouched_and_the_plain_map_lives_in_the_extension()
        {
            var baseOnly = Facts.Load(Fixture(BaseFile));
            Assert.IsEmpty(baseOnly.PlainTexts, "facts.json has no plain wordings (it is never edited)");
            var m = Merged;
            CollectionAssert.AreEqual(baseOnly.All.Select(f => f.Text), m.All.Take(baseOnly.All.Count).Select(f => f.Text));
            Assert.IsNotNull(m.Plain("fire_stay"), "an original fact's plain wording comes from facts-ext.json");
            Assert.AreNotEqual(m.Text("fire_stay"), m.Plain("fire_stay"));
            Assert.IsInstanceOf<JObject>(JObject.Parse(Fixture(ExtFile))["plain"]);
        }

        [Test]
        public void The_lessons_and_the_helpers_facts_all_have_plain_wordings()
        {
            var m = Merged;
            foreach (var step in SceneSelect.TeacherRoute) Assert.IsNotNull(m.Plain(step.Fact), "lesson card " + step.Fact);
            foreach (var id in Companion.TopicFact.Values) Assert.IsNotNull(m.Plain(id), "도우미 힌트 " + id);
            foreach (var id in new[] { "alert_levels", "alert_stairs", "fire_lift", "fire_look", "fire_stay", "cpr_point", "cpr_depth_rate", "report_object", "suspect_report", "fire_report", "radio_listen", "info_rumor" })
                Assert.IsNotNull(m.Plain(id), "quiz fact " + id);
        }

        [Test]
        public void Of_shows_the_original_when_off_and_the_plain_wording_when_on()
        {
            const string goal = "접수대에서 명부를 쓰고 자리를 잡습니다.";
            EasyKorean.On = false;
            Assert.AreEqual(goal, EasyKorean.Of(goal));
            EasyKorean.On = true;
            Assert.AreEqual(EasyKorean.Map[goal], EasyKorean.Of(goal));
            Assert.AreEqual("아무 문장", EasyKorean.Of("아무 문장"), "no plain wording: as it is");
            Assert.IsNull(EasyKorean.Of(null));
            Assert.AreEqual("", EasyKorean.Of(""));
        }

        [Test]
        public void Facts_are_found_by_their_text_in_blocks_and_after_a_lesson_head()
        {
            var m = Merged;
            EasyKorean.UseFacts(m);
            EasyKorean.On = true;
            var stay = m.Text("fire_stay");
            Assert.AreEqual(m.Plain("fire_stay"), EasyKorean.Of(stay));
            Assert.AreEqual("머리\n" + m.Plain("fire_stay"), EasyKorean.Of("머리\n" + stay), "line by line");
            Assert.AreEqual("불이 나면 먼저 살피기 — " + m.Plain("fire_look"), EasyKorean.Of("불이 나면 먼저 살피기 — " + m.Text("fire_look")));
            Assert.AreEqual(m.Plain("evac_order"), EasyKorean.FactText(m, "evac_order"));
            Assert.AreEqual(m.Text("cpr_fatigue"), EasyKorean.FactText(m, "cpr_fatigue"), "no plain wording: the verified text");
            EasyKorean.On = false;
            Assert.AreEqual(m.Text("evac_order"), EasyKorean.FactText(m, "evac_order"));
            Assert.AreEqual(stay, EasyKorean.Of(stay));
        }

        [Test]
        public void Talks_cards_and_steps_are_shown_plain_with_the_same_ids()
        {
            var talk = new TalkOptions { Speaker = "판단", Lines = { "우리 집 안으로 연기나 불길이 들어오지는 않았다. 어떻게 할까?" },
                Choices = new List<TalkChoice> { new("a", "명부 쓰기 (접수대)"), new("b", "그대로", true, "이유") { Taken = true } } };
            var card = new CardOptions { Kind = "decision", Title = "자리 잡기", Body = { "소등했습니다." }, Actions = new List<CardAction> { new("ok", "자리 잡기", true) { Hint = "소등했습니다." } } };
            var steps = new List<ObjectiveStep> { new("명부 쓰기 (접수대)", true), new("자리 잡기") { Choice = true } };

            EasyKorean.On = false;
            Assert.AreSame(talk, EasyKorean.Talk(talk));
            Assert.AreSame(card, EasyKorean.Card(card));
            Assert.AreSame(steps, EasyKorean.Steps(steps));

            EasyKorean.On = true;
            var t = EasyKorean.Talk(talk);
            Assert.AreEqual("판단", t.Speaker);
            Assert.AreEqual(EasyKorean.Map[talk.Lines[0]], t.Lines[0]);
            CollectionAssert.AreEqual(new[] { "a", "b" }, t.Choices.Select(c => c.Id));
            Assert.AreEqual("명부(이름 적는 종이) 쓰기 (접수대)", t.Choices[0].Label);
            Assert.IsTrue(t.Choices[1].Disabled && t.Choices[1].Taken);
            Assert.AreEqual("명부 쓰기 (접수대)", talk.Choices[0].Label, "the story's own options are not changed");

            var c = EasyKorean.Card(card);
            Assert.AreEqual("decision", c.Kind);
            Assert.AreEqual("앉을 자리 정하기", c.Title);
            Assert.AreEqual("불을 껐습니다.", c.Body[0]);
            Assert.AreEqual("ok", c.Actions[0].Id);
            Assert.IsTrue(c.Actions[0].Primary);
            Assert.AreEqual("불을 껐습니다.", c.Actions[0].Hint);

            var s = EasyKorean.Steps(steps);
            Assert.AreEqual("명부(이름 적는 종이) 쓰기 (접수대)", s[0].Text);
            Assert.IsTrue(s[0].Done);
            Assert.IsTrue(s[1].Choice);
        }

        [Test]
        public void The_settings_form_offers_both_modes_off_by_default_and_submits_them()
        {
            var f = new SettingsForm(null);
            CollectionAssert.AreEqual(new[] { EasyKorean.SettingKey, Companion.SettingKey }, f.TogetherToggles.Select(t => t.Name));
            Assert.IsFalse(f.Toggle[EasyKorean.SettingKey]);
            Assert.IsFalse(f.Toggle[Companion.SettingKey]);
            f.Toggle[EasyKorean.SettingKey] = true;
            var r = f.Submit();
            Assert.IsTrue((bool)r[EasyKorean.SettingKey]);
            Assert.IsFalse((bool)r[Companion.SettingKey]);
            Assert.IsTrue(new SettingsForm(JObject.Parse("{easyKorean:true,companion:true}")).Toggle[Companion.SettingKey]);
            foreach (var t in f.TogetherToggles) Assert.That(t.Sub.Length, Is.LessThanOrEqualTo(40), "one line under the switch");
        }

        [Test]
        public void The_director_turns_the_modes_on_and_off_from_the_settings()
        {
            var host = new FakeStoryHost();
            var parts = new DirectorParts { Chapters = new List<DirectorChapter> { new("prologue", c => Task.FromResult<JToken>(null)) } };
            var d = new StoryDirector(Rules, FakeStoryHost.LoadFacts(), host, host, host, host, new MemoryStoryStore(), parts, host);
            try
            {
                d.UpdateSettings(new JObject { [EasyKorean.SettingKey] = true, [Companion.SettingKey] = true });
                Assert.IsTrue(EasyKorean.On);
                Assert.IsTrue(Companion.On);
                d.UpdateSettings(new JObject { [EasyKorean.SettingKey] = false, [Companion.SettingKey] = false });
                Assert.IsFalse(EasyKorean.On);
                Assert.IsFalse(Companion.On);
            }
            finally { EasyKorean.On = false; Companion.On = false; }
        }
    }
}
