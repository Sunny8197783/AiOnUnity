using System.Linq;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class ComposeLogicTests
    {
        static ComposeState Ramp() => ComposeState.FromOptions(JObject.Parse("{route:'ramp', observedVia:'notice_after', gateSeenAt:'20:11', sentAt:'20:14'}"));

        [Test]
        public void RowsCarryTheExactFragments()
        {
            var s = Ramp();
            Assert.AreEqual(new[] { "what", "time", "route", "unknowns", "advice" }, s.Rows.Select(r => r.Name).ToArray());
            Assert.AreEqual(new[] { "무엇을 봤나", "언제", "내가 쓴 길", "모르는 것", "한마디" }, s.Rows.Select(r => r.Legend).ToArray());
            Assert.AreEqual("도착해서 안내문을 보니 동쪽 출입구는 폐쇄래.", s.Rows[0].Options[0].Label);
            Assert.AreEqual("대피소가 닫혀 있었어.", s.Rows[0].Options[1].Label);
            Assert.AreEqual("20:11에", s.Rows[1].Options[0].Label);
            Assert.AreEqual("시각 없이", s.Rows[1].Options[1].Label);
            Assert.AreEqual("나는 지하주차장 진출입로(B)로 들어왔어.", s.Rows[2].Options[0].Label);
            Assert.IsTrue(s.Rows[3].Checkbox);
            Assert.AreEqual("다른 입구 상태는 확인 못 했어.", s.Rows[3].Options[0].Label);
            Assert.AreEqual("연무로 따라 오다가 문화로에서 왼쪽으로 꺾어.", s.Rows[4].Options[1].Label);
            Assert.AreEqual("없음", s.Rows[4].Options[3].Label);
            Assert.AreEqual("", s.Rows[4].Options[3].Value);
            Assert.AreEqual("서연(303호)에게 문자 · 20:14", s.Eyebrow);
            Assert.AreEqual("보내기 · 20:14", s.SendLabel);
            Assert.AreEqual("입구 안내문: “동쪽 출입구 폐쇄 · A 계단 / B 진출입로 이용”", s.Quote);
            Assert.AreEqual("최대 120자 · 직접 고칠 수 있어요", s.Help);
        }

        [Test]
        public void SignNoticeHasNoQuoteAndStairsDefaults()
        {
            var s = ComposeState.FromOptions(new JObject());
            Assert.IsNull(s.Quote);
            Assert.AreEqual("동쪽 출입구가 닫혀 있었어.", s.Rows[0].Options[0].Label);
            Assert.AreEqual("나는 계단(A)으로 들어왔어.", s.Rows[2].Options[0].Label);
            Assert.AreEqual("연무로 따라 서쪽으로 쭉 오면 왼쪽이야.", s.Rows[4].Options[1].Label);
            Assert.AreEqual("20:09에", s.Rows[1].Options[0].Label);
        }

        [Test]
        public void StartsEmptyAndCannotSendWithoutWhat()
        {
            var s = Ramp();
            Assert.AreEqual("", s.Text);
            Assert.IsFalse(s.CanSend);
            Assert.IsFalse(s.ShowReset);
            Assert.IsNull(s.CheckedValue("what"));
            s.Pick("advice", "follow_me");
            Assert.AreEqual("나처럼 오면 돼.", s.Text);
            Assert.IsFalse(s.CanSend);
            s.Pick("what", "gate_closed");
            Assert.IsTrue(s.CanSend);
        }

        [Test]
        public void ChipsBuildTheSentenceLikeTheOriginal()
        {
            var s = Ramp();
            s.Pick("what", "gate_closed");
            s.Pick("time", "true");
            s.Pick("route", "true");
            s.Toggle("unknowns");
            s.Pick("advice", "direction");
            // Same text the original composeNote gave under node.
            Assert.AreEqual("20:11에 도착해서 안내문을 보니 동쪽 출입구는 폐쇄래. 나는 지하주차장 진출입로(B)로 들어왔어. 다른 입구 상태는 확인 못 했어. 연무로 따라 오다가 문화로에서 왼쪽으로 꺾어.", s.Text);
            Assert.IsTrue(s.IsChecked("unknowns", "true"));
            s.Pick("advice", "");
            Assert.IsNull(s.Chips.Advice);
            Assert.IsTrue(s.IsChecked("advice", ""));
            s.Pick("time", "false");
            Assert.IsFalse(s.Chips.Time);
            Assert.IsTrue(s.Text.StartsWith("도착해서"));
            s.Toggle("unknowns");
            Assert.IsFalse(s.Chips.Unknowns);
            Assert.IsNull(s.CheckedValue("unknowns"));
        }

        [Test]
        public void TypingTakesOverUntilReset()
        {
            var s = Ramp();
            s.Pick("what", "all_closed");
            s.Type("   ");
            Assert.IsTrue(s.Edited);
            Assert.IsTrue(s.ShowReset);
            Assert.IsFalse(s.CanSend); // blank text
            s.Type("동쪽 문 닫힘. C로 와.");
            Assert.IsTrue(s.CanSend);
            s.Pick("time", "true"); // chips still change, text stays the player's
            Assert.AreEqual("동쪽 문 닫힘. C로 와.", s.Text);
            Assert.IsTrue(s.Chips.Time);
            s.Reset();
            Assert.IsFalse(s.Edited);
            Assert.AreEqual("20:11에 대피소가 닫혀 있었어.", s.Text);
            s.Type(new string('가', 200));
            Assert.AreEqual(120, s.Text.Length);
        }

        [Test]
        public void ResultHasTheOriginalShape()
        {
            var s = Ramp();
            s.Pick("what", "gate_closed");
            s.Pick("route", "false");
            var r = s.Result();
            Assert.AreEqual(new[] { "chips", "text", "edited" }, r.Properties().Select(p => p.Name).ToArray());
            var chips = (JObject)r["chips"];
            Assert.AreEqual(new[] { "what", "time", "route", "unknowns", "advice" }, chips.Properties().Select(p => p.Name).ToArray());
            Assert.AreEqual("gate_closed", (string)chips["what"]);
            Assert.AreEqual(false, (bool)chips["time"]);
            Assert.AreEqual(false, (bool)chips["route"]);
            Assert.AreEqual(JTokenType.Null, chips["advice"].Type);
            Assert.AreEqual("도착해서 안내문을 보니 동쪽 출입구는 폐쇄래.", (string)r["text"]);
            Assert.AreEqual(false, (bool)r["edited"]);
        }

        [Test]
        public void MarkedSpansFollowTheOriginal()
        {
            var text = "20:09에 동쪽 출입구가 닫혀 있었어. 나처럼 오면 돼";
            var parts = ComposeLogic.MarkedSpans(text, new[] { "나처럼 오면 돼", "20:09에 동쪽 출입구가 닫혀 있었어", "20:09에 동쪽 출입구가 닫혀 있었어", "없는 말" });
            Assert.AreEqual(3, parts.Count);
            Assert.AreEqual(("20:09에 동쪽 출입구가 닫혀 있었어", true), parts[0]);
            Assert.AreEqual((". ", false), parts[1]);
            Assert.AreEqual(("나처럼 오면 돼", true), parts[2]);
            Assert.AreEqual(string.Join("", parts.Select(p => p.text)), text);
            // Overlap: the later-starting quote inside an earlier one is skipped.
            var o = ComposeLogic.MarkedSpans("abcdef", new[] { "abcd", "cd" });
            Assert.AreEqual(new[] { ("abcd", true), ("ef", false) }, o.ToArray());
        }

        [Test]
        public void NoteAndCaptionHelpers()
        {
            var hand = JObject.Parse("{text:'x', sentAt:'20:14', receivedAt:'20:20', claims:{quotes:{what:'a', time:'b'}}}");
            Assert.AreEqual("정민 · 302호 이웃 · 보냄 20:14 · 받음 20:20", ComposeLogic.NoteHeader(hand));
            Assert.AreEqual(new[] { "a", "b" }, ComposeLogic.Quotes(hand).ToArray());
            Assert.AreEqual(0, ComposeLogic.Quotes(new JObject()).Count);
            Assert.AreEqual("서연으로 계속 ▸", ComposeLogic.CaptionButton(true));
            Assert.AreEqual("다음 ▸", ComposeLogic.CaptionButton(false));
            var pins = ComposeLogic.PinLines(JObject.Parse("{was:'A 계단', now:'혼자 못 내려감', also:{now:'B도 마찬가지'}}"));
            Assert.AreEqual(3, pins.Count);
            Assert.IsTrue(pins[0].was);
            Assert.AreEqual("B도 마찬가지", pins[2].text);
            Assert.AreEqual(0, ComposeLogic.PinLines(null).Count);
            Assert.AreEqual("skip", ComposeLogic.CaptionKey("Escape"));
            Assert.AreEqual("next", ComposeLogic.CaptionKey("KeyE"));
            Assert.IsNull(ComposeLogic.CaptionKey("KeyQ"));
        }
    }
}
