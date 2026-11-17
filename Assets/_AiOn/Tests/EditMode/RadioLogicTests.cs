using System.Linq;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class RadioLogicTests
    {
        static JObject Opts(string extra = "") => JObject.Parse(@"{
            mode: 'guided', start: 89.5, battery: true, extraSeconds: 0,
            stations: [
              { freq: 95.1, name: '음악 방송', kind: 'music', lines: ['(잔잔한 음악이 흐른다)'] },
              { freq: 99.3, name: '알 수 없는 방송', kind: 'rumor', lines: ['소문 한 줄'] },
              { freq: 90.9, name: '재난방송', kind: 'official', lines: ['첫째 줄', '둘째 줄'] }
            ]" + extra + "}");

        static RadioTuner Ready(string extra = "")
        {
            var t = new RadioTuner(RadioOptions.Parse(Opts(extra)));
            Assert.AreEqual(0, t.Begin());
            return t;
        }

        static void Run(RadioTuner t, double seconds) { for (double s = 0; s < seconds; s += 0.05) t.Tick(0.05); }

        [Test]
        public void HelpersMatchOriginal()
        {
            Assert.AreEqual(87.5, RadioLogic.R1(80));
            Assert.AreEqual(108, RadioLogic.R1(200));
            Assert.AreEqual(90.9, RadioLogic.R1(90.94), 1e-9);
            Assert.AreEqual("신호 없음", RadioLogic.Word(0));
            Assert.AreEqual("약함", RadioLogic.Word(0.3));
            Assert.AreEqual("보통", RadioLogic.Word(0.6));
            Assert.AreEqual("잘 들림", RadioLogic.Word(0.75));
            Assert.AreEqual(2.4, RadioLogic.LineSeconds("짧다"), 1e-9);
            Assert.AreEqual(5.5, RadioLogic.LineSeconds(new string('가', 200)), 1e-9);
            Assert.AreEqual("95.1", RadioLogic.FreqText(95.1));
            Assert.AreEqual("90", RadioLogic.FreqText(90));
            Assert.AreEqual("89.5", RadioLogic.Fixed1(89.5));
            Assert.AreEqual(30, RadioLogic.DialX(87.5, 720), 1e-9);
            Assert.AreEqual(690, RadioLogic.DialX(108, 720), 1e-9);
        }

        [Test]
        public void ParseDefaultsLikeJs()
        {
            var o = RadioOptions.Parse(new JObject());
            Assert.AreEqual("guided", o.Mode);
            Assert.AreEqual(89.5, o.Start);
            Assert.IsTrue(o.Battery);
            Assert.AreEqual("radio", o.Device);
            Assert.AreEqual(89.5, RadioOptions.Parse(JObject.Parse("{start: 0}")).Start);
            Assert.AreEqual(89.5, RadioOptions.Parse(JObject.Parse("{start: 'x'}")).Start);
            Assert.AreEqual(95, RadioOptions.Parse(JObject.Parse("{start: '95'}")).Start);
            Assert.IsFalse(RadioOptions.Parse(JObject.Parse("{battery: false}")).Battery);
            Assert.IsFalse(RadioOptions.Parse(JObject.Parse("{battery: null}")).Battery);
            Assert.AreEqual(3, RadioOptions.Parse(Opts()).Stations.Count);
        }

        [Test]
        public void ReviewNullModeAndMissingKind()
        {
            // mode: null is not 'guided' (JS defaults only undefined): no help, no pill, guided static text.
            var m = RadioOptions.Parse(JObject.Parse("{mode: null}"));
            Assert.AreEqual("", m.Mode);
            var t = new RadioTuner(RadioOptions.Parse(JObject.Parse("{battery: true, stations: [{ freq: 90.9, name: 'X', lines: ['a'] }]}")));
            t.Begin(); t.ToggleAntenna(); t.TogglePower(); t.SetFreq(90.9);
            for (int i = 0; i < 20; i++) t.Tick(0.05);
            Assert.AreEqual(1, t.Heard.Count);
            t.ToggleNote(t.Heard[0]);
            var noted = (JObject)t.Result()["noted"][0];
            Assert.AreEqual("a", (string)noted["text"]);
            Assert.IsFalse(noted.ContainsKey("kind")); // JSON.stringify drops undefined kind
            Assert.AreEqual("X", (string)t.Result()["station"]["name"]);
        }

        [Test]
        public void SwitchOnlyWorksOnceReadyAndDialNeedsPower()
        {
            var t = new RadioTuner(RadioOptions.Parse(Opts(", battery: false")));
            Assert.AreEqual(-1, t.Begin());
            Assert.AreEqual("건전지가 없어 켜지지 않는다.", t.Status);
            t.TogglePower();
            Assert.IsFalse(t.Power);
            Assert.AreEqual("--.-", t.Readout);
            Assert.AreEqual("꺼짐", t.SignalText);

            var p = new RadioTuner(RadioOptions.Parse(Opts(", battery: false, device: 'phone'")));
            p.Begin();
            Assert.AreEqual("배터리가 없어 켜지지 않는다.", p.Status);

            var r = Ready();
            Assert.AreEqual("전원 스위치를 켜세요.", r.Status);
            r.Nudge(1);
            Assert.AreEqual(89.5, r.Freq); // off: the dial does not move
            r.TogglePower();
            Assert.IsTrue(r.Power);
            Assert.AreEqual("", r.Status);
            Assert.AreEqual("전원 끄기", r.PowerLabel);
            r.Nudge(0.3);
            Assert.AreEqual(89.8, r.Freq, 1e-9);
        }

        [Test]
        public void BatteryLoadingDelaysTheSwitch()
        {
            var t = new RadioTuner(RadioOptions.Parse(Opts(", extraSeconds: 40, device: 'phone'")));
            Assert.AreEqual(4, t.Begin(), 1e-9);
            Assert.AreEqual("어둠 속에서 건전지를 찾아 넣는 중… ", t.Status);
            Run(t, 3.9);
            Assert.IsFalse(t.CanPower);
            Run(t, 0.3);
            Assert.IsTrue(t.CanPower);
            Assert.AreEqual("FM 라디오 앱을 켜세요.", t.Status);
            Assert.AreEqual(1, new RadioTuner(RadioOptions.Parse(Opts(", extraSeconds: 10"))).Begin(), 1e-9);
        }

        [Test]
        public void NoSignalIsNotDoubled()
        {
            var t = Ready();
            t.TogglePower(); t.ToggleAntenna();
            t.SetFreq(RadioLogic.Min);
            Assert.AreEqual(0, t.Clarity, 1e-9);
            Assert.AreEqual("신호 없음", t.SignalText);
        }

        [Test]
        public void AntennaNeededToLock()
        {
            var t = Ready();
            t.TogglePower();
            t.SetFreq(90.9);
            Assert.IsNull(t.Current, "folded antenna caps clarity at 0.6");
            Assert.AreEqual(0.6, t.Clarity, 1e-9);
            Assert.AreEqual("신호 보통 · 안테나 없음", t.SignalText);
            Assert.AreEqual("…지직… 말소리가 섞여 들린다. 조금만 더 맞춰 보자.", t.CaptionText);
            t.ToggleAntenna();
            Assert.IsNotNull(t.Current);
            Assert.AreEqual("재난방송", t.CaptionName);
            Assert.AreEqual("official", t.CaptionPill);
            Assert.AreEqual("…", t.CaptionText);
            Assert.AreEqual("신호 잘 들림", t.SignalText);
            Assert.IsTrue(Enumerable.Range(0, 5).All(t.BarOn));
            t.SetFreq(93);
            Assert.AreEqual("치이이익— (잡음뿐이다)", t.CaptionText);
            Assert.AreEqual("안테나 접기", t.AntennaLabel);
        }

        [Test]
        public void FreeModeHasNoPillAndShorterStaticText()
        {
            var t = Ready(", mode: 'free'");
            t.TogglePower(); t.ToggleAntenna();
            t.SetFreq(99.3);
            Assert.IsNull(t.CaptionPill);
            t.SetFreq(99.6);
            Assert.AreEqual("…지직… 말소리가 섞여 들린다…", t.CaptionText);
        }

        [Test]
        public void LinesArriveOneByOneAndResultMatches()
        {
            var t = Ready();
            t.TogglePower(); t.ToggleAntenna();
            t.SetFreq(90.9);
            Assert.IsNull(t.Tick(0.05));
            Run(t, 0.8);
            Assert.AreEqual(1, t.Heard.Count);
            Assert.AreEqual("첫째 줄", t.CaptionText);
            Run(t, 2.5);
            Assert.AreEqual(2, t.Heard.Count);
            Assert.AreEqual("둘째 줄", t.CaptionText);
            Run(t, 8); // loops round: the same lines are not added twice
            Assert.AreEqual(2, t.Heard.Count);

            t.SetFreq(95.1);
            Run(t, 1);
            Assert.AreEqual(3, t.Heard.Count);
            Assert.IsTrue(t.ToggleNote(t.Heard[0]));
            Assert.IsTrue(t.ToggleNote(t.Heard[2]));
            Assert.IsFalse(t.ToggleNote(t.Heard[2]));

            var r = t.Result();
            Assert.AreEqual(90.9, (double)r["station"]["freq"], 1e-9);
            Assert.AreEqual("official", (string)r["station"]["kind"]);
            Assert.IsTrue((bool)r["official"]);
            CollectionAssert.AreEqual(new[] { "첫째 줄", "둘째 줄", "(잔잔한 음악이 흐른다)" }, r["heard"].Select(x => (string)x).ToArray());
            Assert.AreEqual(1, ((JArray)r["noted"]).Count);
            Assert.AreEqual("첫째 줄", (string)r["noted"][0]["text"]);
            Assert.AreEqual("official", (string)r["noted"][0]["kind"]);
        }

        [Test]
        public void BestTiesGoToTheLaterStationAndEmptyIsNull()
        {
            var t = Ready();
            var r0 = t.Result();
            Assert.AreEqual(JTokenType.Null, r0["station"].Type);
            Assert.IsFalse((bool)r0["official"]);
            Assert.AreEqual(0, ((JArray)r0["heard"]).Count);

            t.TogglePower(); t.ToggleAntenna();
            t.SetFreq(95.1); Run(t, 1);
            t.SetFreq(99.3); Run(t, 1);
            var r = t.Result();
            Assert.AreEqual("rumor", (string)r["station"]["kind"]);
            Assert.IsFalse((bool)r["official"]);
        }

        [Test]
        public void KnobTurnsTwoMegahertzPerTurnAndRoundsOnRelease()
        {
            var t = Ready();
            Assert.IsFalse(t.KnobDown(), "off: knob does nothing");
            t.TogglePower();
            Assert.IsTrue(t.KnobDown());
            t.KnobTurn(System.Math.PI / 2); // quarter turn = 0.5 MHz
            Assert.AreEqual(90.0, t.Freq, 1e-9);
            t.KnobTurn(0.123);
            Assert.AreNotEqual(RadioLogic.R1(t.Freq), t.Freq);
            t.KnobUp();
            Assert.AreEqual(RadioLogic.R1(t.Freq), t.Freq);
            Assert.AreEqual((t.Freq - 87.5) * 180, t.KnobDegrees, 1e-9);
            double before = t.Freq;
            t.KnobDown();
            t.KnobTurn(3 * System.Math.PI / 2); // a jump across the seam is a quarter turn back
            Assert.AreEqual(before - 0.5, t.Freq, 1e-9);
            t.KnobUp();
            t.SetFreq(107.9); t.Wheel(true); t.Wheel(true);
            Assert.AreEqual(108, t.Freq);
        }

        [Test]
        public void HelpTextsAndSignalValue()
        {
            var t = Ready();
            var help = t.HelpLines();
            Assert.AreEqual("전원을 켜고 안테나를 편 뒤, 손잡이를 천천히 돌려 보세요 — 목표 90.9MHz", help[0]);
            Assert.AreEqual("잡음이 줄고 신호 막대가 차오르면 방송이 들립니다. 안테나를 접으면 신호가 약해집니다.", help[1]);
            var p = new RadioTuner(RadioOptions.Parse(JObject.Parse("{device:'phone'}")));
            Assert.AreEqual("전원을 켜고 이어폰을 꽂은 뒤, 손잡이를 천천히 돌려 보세요 — 목표 방송이 들리는 곳", p.HelpLines()[0]);
            Assert.AreEqual("앱 닫기", p.OffLabel);
            Assert.AreEqual("FM 라디오 앱 켜기", p.PowerLabel);
            Assert.AreEqual("이어폰 꽂기", p.AntennaLabel);
            Assert.IsNull(t.SignalValue);
            t.TogglePower();
            Assert.AreEqual(0, t.SignalValue);
            t.Close();
            Assert.IsNull(t.SignalValue);
        }
    }
}
