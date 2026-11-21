// 휴대폰 배터리 자원 (competition upgrade, 4단계): during the blackout (2장 정전 ~ 3장 D+3) what 정민 does with the phone
// costs battery: scrolling the group chat costs the most, a text the least, the phone's light runs it down (the
// director's light, 1 % per 20 s) and the radio runs on its own batteries. Low battery closes the phone's extra info
// choices (the chat, the FM app) but never the essentials (texts, 112·119), and nothing fails at 0: the story already
// has a way round a dead phone everywhere. The habits that keep it alive (절전 모드, the radio, charging at the shelter)
// answer with a line each and a report row (comms_battery). All numbers here are game numbers, not facts.
// Pure logic; the director (P phone), StoryCommon (radio), RumorScene (incoming chat) and DDay (the shelter's
// emergency power) call it.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    public static class PhoneBattery
    {
        public const string Event = "phone_use";
        public const string SaverKnow = "phoneSaver";
        /// <summary>At or below this the phone keeps to texts and calls (game number).</summary>
        public const double Low = 15;
        /// <summary>The shelter's emergency power strip, one turn (game number).</summary>
        public const double ShelterCharge = 25;
        /// <summary>Power-saving mode: app use and the daily standby cost this share (game number).</summary>
        public const double SaverMul = 0.6;

        /// <summary>
        /// A test seam like DDay.CprPart: the tests that compare a chapter with the original set it to false (no cost on
        /// incoming chat, no low-battery note, no habit lines, no shelter charge offer). Costs the original already had
        /// (the FM app, the phone's light) stay.
        /// </summary>
        public static bool Live = true;

        /// <summary>What one use costs before 절전 모드: sns (scrolling the chat), chat (a chat thread that came in),
        /// text (reading or sending a text), fm (the FM app), map (the map app), call.</summary>
        public static double BaseCost(string what) => what switch
        {
            "sns" => 5, "chat" => 3, "text" => 1, "fm" => 3, "map" => 3, "call" => 2, _ => 0,
        };

        public static bool Saver(GameState g) => g != null && g.Knows(SaverKnow);

        /// <summary>The cost now (whole percent; 절전 모드 takes 40 % off, never below 1).</summary>
        public static double Cost(GameState g, string what)
        {
            double b = BaseCost(what);
            if (b <= 0) return 0;
            return Saver(g) ? Math.Max(1, Math.Ceiling(b * SaverMul)) : b;
        }

        /// <summary>The blackout this resource matters in: grid power off in 2장 or 3장.</summary>
        public static bool Blackout(string chapter, bool power) => !power && (chapter == "dday" || chapter == "endure");

        public static bool IsOff(double battery) => battery <= 0;
        public static bool IsLow(double battery) => battery > 0 && battery <= Low;
        /// <summary>The chat and the FM app wait at low battery (texts and calls do not).</summary>
        public static bool Extras(double battery) => battery > Low;

        /// <summary>True when this change crossed down into low battery.</summary>
        public static bool CrossedLow(double before, double after) => !double.IsNaN(before) && before > Low && after <= Low && after > 0;

        /// <summary>
        /// Spends one use: battery down by the cost (not below 0). Records 'phone_use' { what, cost, left } for the uses
        /// the report counts (sns, chat); returns the cost.
        /// </summary>
        public static double Use(GameState g, string what)
        {
            double cost = Cost(g, what);
            g.Phone.Battery = Math.Max(0, g.Phone.Battery - cost);
            if (what == "sns" || what == "chat")
                GameRules.RecordEvent(g, Event, new JObject { ["what"] = what, ["cost"] = cost, ["left"] = Math.Round(g.Phone.Battery) });
            return cost;
        }

        /// <summary>
        /// What a small use the player did not choose (reading the texts as the phone opens) may take: never the last
        /// percent, so opening the phone never switches it off.
        /// </summary>
        public static double Sip(double battery, double cost) => Math.Max(0, Math.Min(cost, Math.Floor(battery) - 1));

        /// <summary>A chat thread that comes in costs battery only while the phone has some to spare (above Low); at low
        /// battery the notification is read as a preview, free.</summary>
        public static bool ChargesIncoming(double battery) => Extras(battery);

        /// <summary>Letting someone else use the shelter's power strip first. Records 'phone_use' { what: yield }.</summary>
        public static void Yield(GameState g) => GameRules.RecordEvent(g, Event, new JObject { ["what"] = "yield" });

        /// <summary>The radio carried the news in the blackout (StoryCommon.RadioScene sets it, with its line).</summary>
        public const string RadioKnow = "radioSavedPhone";

        /// <summary>Charging somewhere (the shelter): up by the amount (not above 100). Records 'phone_use' { what: charge, where, gain, left }.</summary>
        public static double Charge(GameState g, string where, double amount)
        {
            double before = g.Phone.Battery;
            g.Phone.Battery = Math.Min(100, before + Math.Max(0, amount));
            double gain = g.Phone.Battery - before;
            GameRules.RecordEvent(g, Event, new JObject { ["what"] = "charge", ["where"] = where, ["gain"] = gain, ["left"] = Math.Round(g.Phone.Battery) });
            return gain;
        }

        /// <summary>절전 모드 on or off (know.phoneSaver). Records 'phone_use' { what: saver, on }.</summary>
        public static void SetSaver(GameState g, bool on)
        {
            g.SetKnow(SaverKnow, on ? (JToken)true : null);
            GameRules.RecordEvent(g, Event, new JObject { ["what"] = "saver", ["on"] = on });
        }

        /// <summary>The daily standby cost after the power hour (Ledger): 절전 모드 halves it.</summary>
        public static double Standby(GameState g, bool bank) => (bank ? 6 : 12) * (Saver(g) ? 0.5 : 1);

        static string Pct(double v) => $"{Math.Round(v)}%";

        /// <summary>The note after a use: "단톡방을 훑어봤다 · 배터리 −5% (남은 42%)".</summary>
        public static string UseLine(string what, double cost, double left)
        {
            string act = what switch
            {
                "sns" => "단톡방을 훑어봤다", "chat" => "단톡방 알림을 열어 읽었다", "text" => "문자를 확인했다", "fm" => "휴대폰 FM 라디오를 들었다",
                "map" => "지도앱을 열었다", "call" => "전화를 걸었다", _ => "휴대폰을 썼다",
            };
            return $"{act} · 배터리 −{Pct(cost)} (남은 {Pct(left)})";
        }

        /// <summary>The note when the battery crosses into low.</summary>
        public static string LowLine(double battery) =>
            $"휴대폰 배터리 {Pct(battery)}. 단톡방과 FM 앱은 접고, 문자와 긴급전화를 위해 아껴 두자.";

        /// <summary>Why a chat or FM choice is closed at low battery (the choice's note).</summary>
        public const string LowWhy = "배터리가 얼마 남지 않았다 · 문자와 긴급전화용으로 아껴 둔다";

        /// <summary>The first time the radio carries the news in the blackout: it runs on its own batteries.</summary>
        public const string RadioHabit = "라디오는 건전지로 듣는다. 휴대폰 배터리는 그대로다.";
        public const string SaverOnLine = "절전 모드를 켰다. 화면이 어두워지고, 앱과 대기 전력이 덜 든다.";
        public const string SaverOffLine = "절전 모드를 껐다.";
        public const string ChargeLine = "대피소 비상 전원으로 휴대폰을 채웠다.";
        public const string FactId = "comms_battery";

        /// <summary>What the chat says that day while the power is out (fiction: worry repeats, nothing new is confirmed).</summary>
        public static IReadOnlyList<string> ChatLines(int day) => day switch
        {
            <= 0 => new[] { "\"여기도 정전이에요?\" \"엘리베이터 멈췄어요\"", "\"사이렌 들리세요? 어디로 가요?\"", "\"누가 그러는데 정전이 일주일 간대요\"" },
            1 => new[] { "\"물 어디서 받아요?\" \"편의점 문 닫았대요\"", "\"전기 언제 들어와요?\" 같은 질문이 스무 번째다.", "\"아는 사람이 그러는데…\"로 시작하는 글이 또 올라왔다." },
            2 => new[] { "\"뒷산 쪽 무섭네요\" \"애들 밖에 내보내지 마세요\"", "어제 올라온 글이 퍼온 글로 다시 올라온다.", "\"방송 들으신 분?\" 아무도 답하지 않는다." },
            _ => new[] { "\"충전 어디서 해요?\" \"휴대폰 꺼지기 직전이에요\"", "같은 걱정이 서른 개쯤 쌓였다.", "새로 확인된 소식은 없다." },
        };

        /// <summary>
        /// The report row: shown once the player did something with the battery (scrolled, saved, charged, let someone
        /// else charge first, listened on the radio instead). Any of the habits counts; only heavy scrolling fails it.
        /// </summary>
        public sealed class Verdict
        {
            public bool Show, Ok;
            public int Scrolls;
            public bool SaverOn, Charged, Yielded, Radio;
            public string Yes = "정전 동안 휴대폰 배터리를 아껴 썼다 (절전 모드·라디오·대피소 충전)";
            public string No;
        }

        public static Verdict Judge(GameState g)
        {
            var v = new Verdict();
            var uses = g?.Events?.Where(e => e.Type == Event && e.Details != null).Select(e => e.Details).ToList() ?? new List<JObject>();
            v.Scrolls = uses.Count(d => JsSem.Is(d["what"], "sns"));
            v.SaverOn = uses.Any(d => JsSem.Is(d["what"], "saver") && JsSem.IsTrue(d["on"]));
            v.Charged = uses.Any(d => JsSem.Is(d["what"], "charge"));
            v.Yielded = uses.Any(d => JsSem.Is(d["what"], "yield"));
            v.Radio = g != null && g.Knows(RadioKnow);
            v.Show = v.Scrolls > 0 || v.SaverOn || v.Charged || v.Yielded || v.Radio;
            v.Ok = (v.SaverOn || v.Charged || v.Yielded || v.Radio) && v.Scrolls <= 2;
            if (v.Yielded && !v.SaverOn && !v.Charged && !v.Radio) v.Yes = "대피소 충전기는 배터리가 더 급한 사람에게 먼저 양보했다";
            v.No = v.Scrolls > 2 ? "단톡방을 여러 번 훑느라 배터리를 많이 썼다" : "휴대폰 배터리를 아끼는 방법(절전 모드·라디오·대피소 충전)을 쓰지 않았다";
            return v;
        }
    }
}
