using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class PhoneLogicTests
    {
        [Test]
        public void ShareCountMatchesTheOriginal()
        {
            // Values computed by the original shareCount under node.
            Assert.AreEqual(47, PhoneLogic.ShareCount(""));
            Assert.AreEqual(47, PhoneLogic.ShareCount(null));
            Assert.AreEqual(120, PhoneLogic.ShareCount("대피소 문 닫혔대요!!"));
            Assert.AreEqual(211, PhoneLogic.ShareCount("abc"));
            Assert.AreEqual(134, PhoneLogic.ShareCount("😀 공유"));
            Assert.AreEqual(88, PhoneLogic.ShareCount("[안전안내문자] 19:46 연무동 일대 정전 발생. 복구 시간 미정."));
            // Lone surrogates (node: 100, 54) must not throw.
            Assert.AreEqual(100, PhoneLogic.ShareCount("\uD800a"));
            Assert.AreEqual(54, PhoneLogic.ShareCount("a\uDC00"));
        }

        [Test]
        public void ShareKeyIsStringOfText()
        {
            // shareCount(null) hashes "null" (node: 210); an absent text hashes "".
            var v = PhoneLogic.Parse(JObject.Parse("{messages:[{kind:'sns',text:null},{kind:'sns'}]}"));
            Assert.AreEqual(210, PhoneLogic.ShareCount(v.Posts[0].ShareKey));
            Assert.AreEqual(47, PhoneLogic.ShareCount(v.Posts[1].ShareKey));
        }

        [Test]
        public void OnlyActionWithoutIdHasNoEscape()
        {
            Assert.IsFalse(PhoneLogic.EscapeId(PhoneLogic.Actions(JArray.Parse("[{label:'x'}]")), out _));
        }

        [Test]
        public void SignalTableAndUnknownFallsBackToOk()
        {
            Assert.AreEqual(("신호 좋음", 4), PhoneLogic.Signal("ok"));
            Assert.AreEqual(("신호 약함", 1), PhoneLogic.Signal("weak"));
            Assert.AreEqual(("서비스 없음", 0), PhoneLogic.Signal("none"));
            Assert.AreEqual(("신호 좋음", 4), PhoneLogic.Signal("bogus"));
            Assert.AreEqual(("신호 좋음", 4), PhoneLogic.Signal(null));
            Assert.AreEqual("서비스 없음", PhoneLogic.SignalNote(0));
            Assert.AreEqual("약함", PhoneLogic.SignalNote(1));
            Assert.IsNull(PhoneLogic.SignalNote(4));
        }

        [Test]
        public void BatteryTextBannerAndColours()
        {
            Assert.AreEqual("57%", PhoneLogic.BatteryText(56.5));
            Assert.AreEqual("100%", PhoneLogic.BatteryText(140));
            Assert.AreEqual("0%", PhoneLogic.BatteryText(-3));
            Assert.AreEqual("0%", PhoneLogic.BatteryText(double.NaN));
            Assert.IsTrue(PhoneLogic.BatteryLowColour(20));
            Assert.IsFalse(PhoneLogic.BatteryLowColour(21));
            Assert.IsTrue(PhoneLogic.LowBattery(15));
            Assert.IsFalse(PhoneLogic.LowBattery(16));
            Assert.IsFalse(PhoneLogic.LowBattery(double.NaN));
            Assert.AreEqual("배터리 12% · 절전 모드", PhoneLogic.BannerText(12.4));
            Assert.AreEqual("배터리 0% · 절전 모드", PhoneLogic.BannerText(-5));
        }

        [Test]
        public void ParseDefaultsToOneCloseAction()
        {
            var v = PhoneLogic.Parse(new JObject());
            Assert.AreEqual(50, v.Battery);
            Assert.AreEqual("ok", v.Signal);
            Assert.IsNull(v.MapApp);
            Assert.AreEqual(1, v.Actions.Count);
            Assert.AreEqual("close", (string)v.Actions[0].Id);
            Assert.AreEqual("닫기", v.Actions[0].Label);
            Assert.IsTrue(v.HasEscape);
            Assert.AreEqual("close", (string)v.EscapeId);
            Assert.AreEqual(0, v.Messages.Count);
            Assert.AreEqual(0, PhoneLogic.Parse(JObject.Parse("{\"battery\":null}")).Battery);
        }

        [Test]
        public void ParseSplitsSnsPostsAndKeepsActions()
        {
            var v = PhoneLogic.Parse(JObject.Parse(@"{
                battery: 12, signal: 'weak', mapApp: 'fail',
                messages: [ { from: '안전안내문자', time: '19:46', kind: 'safety', text: 'A' }, null,
                            { from: '동네 단체방', kind: 'sns', time: '방금', text: 'B' },
                            { from: '엄마', kind: 'sms', time: '19:50', text: 'C' } ],
                actions: [ { id: 'radio', label: 'FM 라디오 앱' }, 'go', { id: 'close', label: '닫기', primary: true } ] }"));
            Assert.AreEqual(12, v.Battery);
            Assert.AreEqual("weak", v.Signal);
            Assert.AreEqual("fail", v.MapApp);
            Assert.AreEqual(2, v.Messages.Count);
            Assert.AreEqual(1, v.Posts.Count);
            Assert.AreEqual("B", v.Posts[0].Text);
            Assert.AreEqual("19:50", PhoneLogic.StatusTime(v.Messages));
            Assert.AreEqual(3, v.Actions.Count);
            Assert.AreEqual("go", (string)v.Actions[1].Id);
            Assert.AreEqual("go", v.Actions[1].Label);
            Assert.IsTrue(v.Actions[2].Primary);
            Assert.IsFalse(v.Actions[0].Primary);
            Assert.AreEqual("close", (string)v.EscapeId);
        }

        [Test]
        public void EscapePrefersCloseOrBackElseTheOnlyAction()
        {
            Assert.IsTrue(PhoneLogic.EscapeId(PhoneLogic.Actions(JArray.Parse("[{id:'a'},{id:'back'}]")), out var id));
            Assert.AreEqual("back", (string)id);
            Assert.IsTrue(PhoneLogic.EscapeId(PhoneLogic.Actions(JArray.Parse("['only']")), out id));
            Assert.AreEqual("only", (string)id);
            Assert.IsFalse(PhoneLogic.EscapeId(PhoneLogic.Actions(JArray.Parse("['a','b']")), out _));
            // An object action without a label shows its id.
            Assert.AreEqual("radio", PhoneLogic.Actions(JArray.Parse("[{id:'radio'}]"))[0].Label);
            // Empty list -> 닫기.
            Assert.AreEqual("닫기", PhoneLogic.Actions(new JArray())[0].Label);
        }

        [Test]
        public void BubbleHeads()
        {
            var safety = PhoneLogic.Head(new PhoneMessage { Kind = "safety", From = "안전안내문자", Time = "19:46" });
            Assert.IsTrue(safety.Official);
            Assert.AreEqual("안전안내문자", safety.KindLabel);
            Assert.IsNull(safety.From); // same as the label: not repeated
            var alert = PhoneLogic.Head(new PhoneMessage { Kind = "emergency", From = "수원시청" });
            Assert.AreEqual("위급재난문자", alert.KindLabel);
            Assert.AreEqual("수원시청", alert.From);
            Assert.AreEqual("", alert.Time);
            Assert.AreEqual("나", PhoneLogic.Head(new PhoneMessage { Kind = "me", From = "정민" }).From);
            Assert.AreEqual("알 수 없음", PhoneLogic.Head(new PhoneMessage { Kind = "sms" }).From);
            Assert.AreEqual("알 수 없음", PhoneLogic.Head(new PhoneMessage { Kind = "weird" }).From);
            Assert.AreEqual("sms", PhoneLogic.BubbleKind(new PhoneMessage()));
        }

        [Test]
        public void MapStates()
        {
            Assert.AreEqual(("지도", "현재 위치 · 연무동"), PhoneLogic.MapState("ok"));
            Assert.AreEqual(("지도를 불러오는 중…", "데이터 연결이 느립니다"), PhoneLogic.MapState("loading"));
            Assert.AreEqual(("지도를 불러올 수 없습니다", "네트워크 연결을 확인해 주세요"), PhoneLogic.MapState("fail"));
            Assert.AreEqual(((string)null, (string)null), PhoneLogic.MapState("other"));
        }

        [Test]
        public void AlertLevelAndDefaults()
        {
            Assert.AreEqual("critical", PhoneLogic.AlertLevel("위급재난문자"));
            Assert.AreEqual("urgent", PhoneLogic.AlertLevel("긴급재난문자"));
            Assert.AreEqual("safety", PhoneLogic.AlertLevel("안전안내문자"));
            var (kind, text, time, level) = PhoneLogic.ParseAlert(new JObject());
            Assert.AreEqual("위급재난문자", kind);
            Assert.AreEqual("", text);
            Assert.AreEqual("", time);
            Assert.AreEqual("critical", level);
            var a = PhoneLogic.ParseAlert(JObject.Parse("{kind:'안전안내문자', text:'정전', time:'19:46'}"));
            Assert.AreEqual("safety", a.level);
            Assert.AreEqual("19:46", a.time);
        }
    }
}
