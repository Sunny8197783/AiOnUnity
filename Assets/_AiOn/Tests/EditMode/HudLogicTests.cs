using System.Collections.Generic;
using System.Linq;
using AiOn.Story;
using AiOn.UI;
using NUnit.Framework;

namespace AiOn.Tests
{
    /// <summary>The HUD's pure rules: what shows when, the toast queue, the key strip timer, the banner, objective and choice text.</summary>
    public class HudLogicTests
    {
        [Test]
        public void Parts_hide_when_hidden_or_in_a_cutscene()
        {
            Assert.That(HudVisibility.Part(true, false, false), Is.True);
            Assert.That(HudVisibility.Part(true, true, false), Is.False, "Hide() (gallery, records) hides the chapter and clock too");
            Assert.That(HudVisibility.Part(true, false, true), Is.False);
            Assert.That(HudVisibility.Part(false, false, false), Is.False);
        }

        [Test]
        public void Pointer_only_in_free_play()
        {
            Assert.That(HudVisibility.Pointer(false, 0, false), Is.True);
            Assert.That(HudVisibility.Pointer(true, 0, false), Is.False, "cutscene");
            Assert.That(HudVisibility.Pointer(false, 1, false), Is.False, "modal window");
            Assert.That(HudVisibility.Pointer(false, 0, true), Is.False, "dialog");
            Assert.That(HudVisibility.LayerAlpha(0), Is.EqualTo(1f));
            Assert.That(HudVisibility.LayerAlpha(2), Is.EqualTo(0f));
        }

        [Test]
        public void Key_strip_shows_for_20s_of_roaming_then_8s_again()
        {
            var t = new KeyStripTimer();
            Assert.That(t.Tick(false, 100), Is.True, "time in a dialog or menu does not count");
            for (int i = 0; i < 19; i++) Assert.That(t.Tick(true, 1), Is.True);
            Assert.That(t.Tick(true, 1.01f), Is.False);
            t.Show(KeyStripTimer.Again);
            Assert.That(t.Tick(true, 7.9f), Is.True);
            Assert.That(t.Tick(true, 0.2f), Is.False);
            Assert.That(HudVisibility.KeyStrip(true, true, false), Is.False);
            Assert.That(HudVisibility.KeyStrip(true, false, true), Is.False);
        }

        [Test]
        public void Toasts_wait_for_the_modal_and_come_out_staggered()
        {
            var q = new ToastQueue();
            q.Enqueue("a", "self", 3000);
            q.Enqueue("b", "official", 3000);
            Assert.That(q.TryNext(0, true, out _), Is.False);
            Assert.That(q.TryNext(1, false, out _), Is.False, "a short beat after the window closes");
            Assert.That(q.TryNext(1 + ToastQueue.FirstDelay + 0.001, false, out var first), Is.True);
            Assert.That(first.Text, Is.EqualTo("a"));
            Assert.That(q.TryNext(1.5, false, out _), Is.False);
            Assert.That(q.TryNext(1 + ToastQueue.FirstDelay + ToastQueue.Stagger + 0.001, false, out var second), Is.True); // float steps: a hair after
            Assert.That((second.Text, second.Label, second.Ms), Is.EqualTo(("b", "official", 3000)));
            Assert.That(q.Pending, Is.Zero);
        }

        [Test]
        public void The_memo_echo_of_an_alert_is_dropped()
        {
            var q = new ToastQueue();
            Assert.That(q.IsAlertEcho("메모: [안전안내문자] 연무동 주민 이동 지원."), Is.True);
            Assert.That(q.AlertText, Is.EqualTo("[안전안내문자] 연무동 주민 이동 지원."));
            Assert.That(q.IsAlertEcho("메모: 19:46 연무동 일대 정전, 복구 시간 미정 (안전안내문자)"), Is.False, "a summary note is not the popup's text");
            Assert.That(q.IsAlertEcho("[안전안내문자] 연무동"), Is.False, "only memo toasts");
            q.AlertText = "[위급재난문자] 긴급 대피";
            Assert.That(q.IsAlertEcho("메모: 긴급 대피"), Is.True);
            Assert.That(q.IsAlertEcho("맞아요 · 확인된 사실"), Is.False);
        }

        [Test]
        public void At_most_two_official_toasts()
        {
            Assert.That(ToastQueue.EvictIndex(new[] { "self", "official" }, "official"), Is.EqualTo(-1));
            Assert.That(ToastQueue.EvictIndex(new[] { "self", "official", "site", "official" }, "official"), Is.EqualTo(1));
            Assert.That(ToastQueue.EvictIndex(new[] { "official", "official" }, "self"), Is.EqualTo(-1), "other labels do not push officials out");
        }

        [Test]
        public void Banner_fades_in_holds_and_out()
        {
            Assert.That(ChapterBanner.Alpha(-1, false), Is.Zero);
            Assert.That(ChapterBanner.Alpha(0.2f, false), Is.EqualTo(0.5f).Within(1e-4));
            Assert.That(ChapterBanner.Alpha(1.5f, false), Is.EqualTo(1f));
            Assert.That(ChapterBanner.Alpha(0.4f + 2.2f + 0.3f, false), Is.EqualTo(0.5f).Within(1e-4));
            Assert.That(ChapterBanner.Alpha(3.3f, false), Is.Zero);
            Assert.That(ChapterBanner.Alpha(0.01f, true), Is.EqualTo(1f), "reduce motion: still, no fade");
            Assert.That(ChapterBanner.Alpha(1.6f, true), Is.Zero);
            Assert.That(ChapterBanner.Flash(0.2f), Is.EqualTo(1f).Within(1e-4));
            Assert.That(ChapterBanner.Flash(0.6f), Is.EqualTo(1f).Within(1e-4), "the second pulse");
            Assert.That(ChapterBanner.Flash(0.8f), Is.Zero);
        }

        [Test]
        public void Exclusive_steps_read_as_one_choice()
        {
            var text = StoryOverlay.StepsText(new List<ObjectiveStep>
            {
                new("문에 쪽지 남기기 (선택)"),
                new("버스 타기: 14:00까지 집결지") { Choice = true },
                new("남기: 관리사무소 박 소장에게") { Choice = true },
            });
            var lines = text.Split('\n');
            Assert.That(lines.Length, Is.EqualTo(4));
            Assert.That(lines[0], Is.EqualTo("○  문에 쪽지 남기기 (선택)"));
            Assert.That(lines[1], Does.Contain("다음 중 하나를 고르세요"));
            Assert.That(lines[2], Does.Contain("◇").And.EndsWith("버스 타기: 14:00까지 집결지"));
            Assert.That(text.Split('\n').Count(l => l.Contains("다음 중 하나를 고르세요")), Is.EqualTo(1));
            Assert.That(StoryOverlay.StepsText(null), Is.Empty);
        }

        [Test]
        public void Choice_parts_split_the_reason()
        {
            Assert.That(StoryOverlay.ChoiceParts(new TalkChoice("radio", "라디오를 켠다", true, "라디오가 없다")), Is.EqualTo(("라디오를 켠다", "라디오가 없다")));
            Assert.That(StoryOverlay.ChoiceParts(new TalkChoice("r", "라디오를 켠다 (라디오가 없다)", true)), Is.EqualTo(("라디오를 켠다", "(라디오가 없다)")));
            Assert.That(StoryOverlay.ChoiceParts(new TalkChoice("w", "창밖을 본다", true)), Is.EqualTo(("창밖을 본다", (string)null)), "taken");
            Assert.That(StoryOverlay.ChoiceParts(new TalkChoice("w", "물을 받는다 (욕조)")), Is.EqualTo(("물을 받는다 (욕조)", (string)null)), "an open choice keeps its label whole");
        }

        [Test]
        public void Open_choices_keep_their_note_and_unaffordable_ones_are_not_taken()
        {
            // rumor-scene.js: an open choice's note is its small second line
            Assert.That(StoryOverlay.ChoiceParts(new TalkChoice("follow", "이 말대로 한다", false, "시간이 조금 든다")), Is.EqualTo(("이 말대로 한다", "시간이 조금 든다")));
            // a money pick you cannot afford: disabled, no reason, and not Taken (drawn locked, never "✓")
            var broke = new TalkChoice("1", "5만 원", true);
            Assert.That(broke.Taken, Is.False);
            Assert.That(StoryOverlay.ChoiceParts(broke), Is.EqualTo(("5만 원", (string)null)));
            var done = new TalkChoice("shout", "\"불이야!\" 외친다", true) { Taken = true };
            Assert.That(StoryOverlay.ChoiceParts(done), Is.EqualTo(("\"불이야!\" 외친다", (string)null)));
        }

        [Test]
        public void Banner_waits_for_the_hud_and_for_modals_to_close()
        {
            Assert.That(ChapterBanner.ShouldStart(true, false, 0), Is.True);
            Assert.That(ChapterBanner.ShouldStart(true, false, 1), Is.False, "a card or time skip is up: the HUD layer is faded out");
            Assert.That(ChapterBanner.ShouldStart(true, true, 0), Is.False, "the HUD is hidden");
            Assert.That(ChapterBanner.ShouldStart(false, false, 0), Is.False, "nothing waiting");
        }

        [Test]
        public void One_key_table_feeds_the_strip_the_pause_list_and_the_card()
        {
            var strip = UiKeys.StripText(6);
            Assert.That(strip.Split('\n').Length, Is.EqualTo(3));
            foreach (var k in new[] { "Q", "G", "C", "X", "B" })
            {
                Assert.That(strip, Does.Contain($">{k}<"));
                Assert.That(UiKeys.PauseRows().Any(r => r[0].Contains(k)), Is.True, k);
            }
            Assert.That(UiKeys.CardLines(), Does.Contain("H — 이 조작법 다시 보기"));
            Assert.That(UiKeys.CardLines().Count, Is.EqualTo(UiKeys.All.Length));
        }
    }
}
