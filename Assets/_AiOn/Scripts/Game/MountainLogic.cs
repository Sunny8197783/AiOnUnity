// 3장 「산에서 내려온 사람들」 (competition upgrade): the pure rules of the sub-scene on D+2. Nobody is ever on screen:
// the threat comes as an official text (통합방위 병종사태, a real stage of 통합방위법) and as a group-chat rumour; what
// 정민 has is traces seen the morning before on the way to the water point (wet footprints of two people, the tags of new
// sneakers, a bag left by the path). The "찜찜한 기억" report card (SightingReport) asks how to tell it (from home, not
// by going back), what to say (언제·어디서·몇 명·복장·이동 방향·남은 물건: only what was seen, "모른다" is honest) and
// where (112). Its accuracy is a game rule for when the 통제 is lifted, never a fact. The intercom stranger and the
// object on the playground are judged here too (IntercomOk, ObjectOk).
//
// Safety sentences are facts (facts-ext.json): defense_stage_c / defense_zone / defense_report (통합방위법),
// suspect_report (도봉구), report_112_text (정책브리핑·경찰청), report_5w (미국 국토안보부, 보조 자료), door_talk
// (에드먼턴 경찰, 보조 자료), object_no_touch / report_object (행정안전부), and the original info_suspect,
// info_appearance, info_rumor. Pure: no UnityEngine.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    public enum SightPhase { How, Card, Channel, Done }

    /// <summary>One answer on the report card. Kind: exact (what was seen) | honest ("모른다") | vague | wrong (a guess).</summary>
    public sealed class SightOption
    {
        public string Id, Text, Kind, Why;
        public SightOption(string id, string text, string kind, string why = null) { Id = id; Text = text; Kind = kind; Why = why; }
    }

    /// <summary>One line of the card (언제, 어디서, ...).</summary>
    public sealed class SightField
    {
        public string Id, Label, Ask;
        public List<SightOption> Options;
    }

    /// <summary>A pick's verdict with its reason and the facts behind it.</summary>
    public sealed class SightFeedback
    {
        public bool Ok;
        public string Title, Text;
        public List<string> Facts = new();
    }

    /// <summary>What the story gives the screen: whether the traces were noted, and when they were seen.</summary>
    public sealed class SightSetup
    {
        /// <summary>The traces went into the phone memo (MountainScene.Traces): the card shows the exact minute.</summary>
        public bool Noted;
        /// <summary>The game minute (of D+1) the traces were seen.</summary>
        public int SeenMinute = 9 * 60 + 25;
        public string Profile = "";
        /// <summary>The phone has battery (else the channels are 서연's phone or the office's intercom).</summary>
        public bool Phone = true;

        public static SightSetup FromOptions(JObject o)
        {
            o ??= new JObject();
            return new SightSetup
            {
                Noted = o["noted"]?.Type == JTokenType.Boolean && (bool)o["noted"],
                SeenMinute = o["seenMinute"]?.Type == JTokenType.Integer ? (int)o["seenMinute"] : 9 * 60 + 25,
                Profile = o["profile"]?.Type == JTokenType.String ? (string)o["profile"] : "",
                Phone = o["phone"]?.Type != JTokenType.Boolean || (bool)o["phone"],
            };
        }

        public JObject ToJson() => new JObject { ["noted"] = Noted, ["seenMinute"] = SeenMinute, ["profile"] = Profile, ["phone"] = Phone };
    }

    /// <summary>The "찜찜한 기억" report card: how to tell it, the card, the channel, then the review.</summary>
    public sealed class SightingReport
    {
        public const string Kicker = "3장 · 찜찜한 기억";
        public const string HowTitle = "어제 본 것을 어떻게 알릴까?";
        public const string HowBody = "내가 본 건 수십 명이 아니라 발자국 두 줄이었다. 그래도 어제 아침 뒷산 입구에서 본 것이 마음에 걸린다.";
        public const string CardTitle = "신고 카드 · 본 것만 적기";
        public const string CardBody = "본 것만 고릅니다. 보지 못한 것은 \"모른다\"가 정확한 답입니다.";
        public const string ChannelTitle = "어디에 알릴까?";
        public const string ChannelBody = "카드가 준비됐다. 급한 신고는 경찰에게.";
        public const string RuleLine = "[게임 규칙] 신고가 정확할수록 수색 범위가 빨리 좁혀져 통제가 일찍 풀립니다.";

        public const int PerField = 2;

        public readonly SightSetup Setup;
        public SightPhase Phase { get; private set; } = SightPhase.How;
        /// <summary>The first way chosen (judged), then the one that was kept.</summary>
        public string FirstHow, How, FirstChannel, Channel;
        /// <summary>field id -> option id</summary>
        public readonly Dictionary<string, string> Picks = new();
        /// <summary>The field being answered (Fields.Count = all answered: the card is ready to send).</summary>
        public int FieldIndex { get; private set; }
        public SightFeedback Feedback { get; private set; }
        public readonly List<SightField> Fields;
        public event Action Changed;

        public SightingReport(SightSetup setup)
        {
            Setup = setup ?? new SightSetup();
            Fields = BuildFields(Setup);
        }

        // ---------------------------------------------------------------- words

        public static readonly IReadOnlyList<SightOption> HowOptions = new[]
        {
            new SightOption("back", "뒷산 입구에 다시 가서 가방을 열어 확인한다", "wrong"),
            new SightOption("photo", "가까이 가서 사진을 찍어 단톡방에 올린다", "wrong"),
            new SightOption("report", "집에서, 본 것만 정리해 신고한다", "exact"),
            new SightOption("follow", "발자국이 간 쪽을 따라가 본다", "wrong"),
        };

        static readonly Dictionary<string, SightFeedback> HowWhy = new()
        {
            ["back"] = new SightFeedback { Title = "다가가지 않습니다", Text = "통제된 산 쪽으로 다시 가면 나도 위험하고 수색에도 방해가 된다. 모르는 물건은 만지지 않는다.", Facts = { "defense_zone", "info_suspect" } },
            ["photo"] = new SightFeedback { Title = "찍으러 가지 않습니다", Text = "가까이 가지 않는다. 누가 확인했는지 모르는 사진은 또 하나의 소문이 되기 쉽다.", Facts = { "suspect_report", "info_rumor" } },
            ["follow"] = new SightFeedback { Title = "따라가지 않습니다", Text = "흔적을 좇는 건 내 일이 아니다. 본 것만으로도 알릴 이유는 충분하다.", Facts = { "defense_report", "suspect_report" } },
            ["report"] = new SightFeedback { Ok = true, Title = "본 것만, 안전한 곳에서", Text = "사람을 보지 못했어도 흔적을 봤다면 알린다. 지체 없이, 본 그대로.", Facts = { "defense_report", "report_5w" } },
        };

        /// <summary>
        /// The channels; the deaf profile sees the text report first. With the phone dead (phone false) the report goes
        /// through 서연's phone or the office's intercom, and putting it off is the one to rethink.
        /// </summary>
        public static IReadOnlyList<SightOption> ChannelOptions(string profile, bool phone = true)
        {
            bool deaf = profile == "deaf";
            if (!phone)
                return new List<SightOption>
                {
                    new("borrow112", deaf ? "303호 서연에게 휴대폰을 빌려 112에 문자로 보낸다" : "303호 서연에게 휴대폰을 빌려 112에 신고한다", "exact"),
                    new("office112", "관리사무소 인터폰으로 박 소장에게 112 신고를 부탁한다", "exact"),
                    new("wait", "휴대폰을 충전할 수 있을 때까지 미룬다", "wrong"),
                };
            var call = new SightOption("call112", "112에 전화한다", "exact");
            var text = new SightOption("text112", "112에 문자로 보낸다 (위치와 상황)", "exact");
            var rest = new[]
            {
                new SightOption("chat", "단톡방에 올려 이웃에게 먼저 알린다", "wrong"),
                new SightOption("call119", "119에 전화한다", "wrong"),
            };
            return (deaf ? new[] { text, call } : new[] { call, text }).Concat(rest).ToList();
        }

        /// <summary>A channel that reaches the police (112), directly or through a neighbour's phone or the office.</summary>
        public static bool ReachesPolice(string id) => id == "call112" || id == "text112" || id == "borrow112" || id == "office112";

        /// <summary>The title once the card is sent.</summary>
        public static string SentTitle(string channel) => channel switch
        {
            "text112" => "112에 문자를 보냈다",
            "borrow112" => "서연의 휴대폰으로 112에 알렸다",
            "office112" => "관리사무소를 거쳐 112에 알렸다",
            _ => "112에 신고했다",
        };

        /// <summary>The card's title suffix once sent.</summary>
        public static string SentTag(string channel) => channel switch
        {
            "text112" => " · 112 문자로 보냄",
            "borrow112" => " · 서연의 휴대폰으로 112에",
            "office112" => " · 관리사무소를 거쳐 112에",
            _ => " · 112에 말함",
        };

        static readonly Dictionary<string, SightFeedback> ChannelWhy = new()
        {
            ["chat"] = new SightFeedback { Title = "단톡방은 신고가 아닙니다", Text = "이웃이 먼저 알면 소문만 커진다. 수상한 것은 경찰에 알린다.", Facts = { "suspect_report", "info_rumor" } },
            ["call119"] = new SightFeedback { Title = "불이나 다친 사람의 일이 아닙니다", Text = "수상한 흔적은 경찰(112)에 알린다.", Facts = { "suspect_report" } },
            ["call112"] = new SightFeedback { Ok = true, Title = "112에 알린다", Text = "본 것을 카드대로 말하고, 다시 연락받을 수 있게 전화를 받을 수 있는 곳에 있는다.", Facts = { "suspect_report", "report_5w" } },
            ["text112"] = new SightFeedback { Ok = true, Title = "112에 문자로", Text = "카드를 그대로 문자로 보낸다. 위치와 상황이 들어 있다.", Facts = { "report_112_text", "suspect_report" } },
            ["borrow112"] = new SightFeedback { Ok = true, Title = "휴대폰을 빌려 112에", Text = "내 휴대폰이 꺼져도 신고는 미루지 않는다. 카드대로 알린다.", Facts = { "defense_report", "suspect_report" } },
            ["office112"] = new SightFeedback { Ok = true, Title = "관리사무소를 거쳐 112에", Text = "카드를 그대로 전해 달라고 부탁한다. 경찰이 다시 찾을 수 있게 집에 있는다.", Facts = { "defense_report", "suspect_report" } },
            ["wait"] = new SightFeedback { Title = "미루지 않습니다", Text = "흔적을 본 사람은 지체 없이 알린다. 휴대폰이 꺼졌으면 이웃의 휴대폰이나 관리사무소를 거쳐서라도.", Facts = { "defense_report" } },
        };

        static string Clock(int minute) => $"{minute / 60}시 {minute % 60:00}분";

        static List<SightField> BuildFields(SightSetup s) => new()
        {
            new SightField { Id = "when", Label = "언제", Ask = "언제 봤나?", Options = new List<SightOption>
            {
                new("dawn", "오늘 새벽", "wrong", "본 것은 어제 아침이다. 시각이 틀리면 수색이 엉뚱한 때를 좇는다."),
                new("seen", s.Noted ? $"어제(토) 아침 {Clock(s.SeenMinute)}쯤 · 메모에 적어 둠" : "어제(토) 아침 9시 반쯤 · 급수소 가던 길", "exact"),
                new("now", "방금 전", "wrong", "급하게 들리게 하려고 시각을 당기면 안 된다."),
                new("unsure", "정확한 시각은 모른다", "honest"),
            } },
            new SightField { Id = "where", Label = "어디서", Ask = "어디서 봤나?", Options = new List<SightOption>
            {
                new("path", "연무동 뒷산 산책로 입구, 약수터 갈림길 앞", "exact"),
                new("hill", "아파트 뒷산 어딘가", "vague", "어디인지 좁혀 말할수록 빨리 찾는다."),
                new("parking", "103동 바로 뒤 주차장", "wrong", "본 곳이 아니다."),
            } },
            new SightField { Id = "count", Label = "몇 명", Ask = "몇 명이었나?", Options = new List<SightOption>
            {
                new("dozens", "수십 명", "wrong", "단톡방 소문의 숫자다. 본 것은 발자국 두 줄이다."),
                new("two", "발자국으로 보아 두 사람 (사람은 못 봄)", "exact"),
                new("one", "한 사람", "wrong", "발자국은 크기가 다른 두 줄이었다."),
                new("unsure", "사람 수는 모른다", "honest"),
            } },
            new SightField { Id = "look", Label = "복장·특징", Ask = "무엇이 눈에 띄었나?", Options = new List<SightOption>
            {
                new("uniform", "군복을 입고 총을 든 사람들", "wrong", "보지 않은 것을 덧붙이면 신고 전체를 믿기 어려워진다."),
                new("odd", "수상하게 생긴 사람들", "wrong", "생김새는 판단 근거가 아니다. 그리고 사람은 보지 못했다."),
                new("shoes", "사람은 못 봤다 · 새 운동화 무늬의 젖은 발자국, 떼어 낸 운동화 택 두 개", "exact"),
                new("unsure", "옷차림은 모른다", "honest"),
            } },
            new SightField { Id = "way", Label = "이동 방향", Ask = "어느 쪽으로 갔나?", Options = new List<SightOption>
            {
                new("road", "발자국이 산에서 내려와 큰길(버스정류장) 쪽으로 이어짐", "exact"),
                new("flats", "우리 아파트 단지 쪽으로", "wrong", "발자국은 단지 쪽이 아니었다. 겁이 본 것을 바꾸면 안 된다."),
                new("unsure", "어느 쪽으로 갔는지는 모른다", "honest"),
            } },
            new SightField { Id = "left", Label = "남은 물건", Ask = "남은 물건이 있었나?", Options = new List<SightOption>
            {
                new("bag", "산책로 옆 검은 가방 하나 (만지지 않음)", "exact"),
                new("radio", "가방 안에 무전기가 있었다", "wrong", "열어 보지 않았다. 보지 않은 것은 말하지 않는다."),
                new("unsure", "기억나지 않는다", "honest"),
            } },
        };

        // ---------------------------------------------------------------- steps

        /// <summary>How to tell it. A wrong way is explained and the page stays (only the first pick is judged).</summary>
        public void ChooseHow(string id)
        {
            if (Phase != SightPhase.How || !HowWhy.TryGetValue(id ?? "", out var why)) return;
            FirstHow ??= id;
            Feedback = why;
            if (why.Ok) { How = id; Phase = SightPhase.Card; FieldIndex = 0; }
            Changed?.Invoke();
        }

        public SightField Current => Phase == SightPhase.Card && FieldIndex < Fields.Count ? Fields[FieldIndex] : null;
        public bool CardReady => Phase == SightPhase.Card && FieldIndex >= Fields.Count;

        /// <summary>Answers the current field and moves to the next (no verdict until the review: the card is written as remembered).</summary>
        public void Pick(string optionId)
        {
            var f = Current;
            if (f == null || f.Options.All(o => o.Id != optionId)) return;
            Picks[f.Id] = optionId;
            Feedback = null;
            FieldIndex++;
            Changed?.Invoke();
        }

        /// <summary>Back to the field before (to change an answer).</summary>
        public void Back()
        {
            if (Phase != SightPhase.Card || FieldIndex == 0) return;
            FieldIndex--;
            Changed?.Invoke();
        }

        /// <summary>The card is sent: on to the channel.</summary>
        public void Submit()
        {
            if (!CardReady) return;
            Phase = SightPhase.Channel;
            Feedback = null;
            Changed?.Invoke();
        }

        public void ChooseChannel(string id)
        {
            if (Phase != SightPhase.Channel || !ChannelWhy.TryGetValue(id ?? "", out var why)) return;
            FirstChannel ??= id;
            Feedback = why;
            if (why.Ok) { Channel = id; Phase = SightPhase.Done; }
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- the verdict

        public SightOption Picked(SightField f) => f == null || !Picks.TryGetValue(f.Id, out var id) ? null : f.Options.FirstOrDefault(o => o.Id == id);
        public string KindOf(string fieldId) => Picked(Fields.FirstOrDefault(f => f.Id == fieldId))?.Kind;

        public static int Points(string kind) => kind == "exact" ? PerField : kind == "honest" || kind == "vague" ? 1 : 0;
        public int Full => Fields.Count * PerField;
        public int Score => Fields.Sum(f => Points(Picked(f)?.Kind));
        /// <summary>Lines filled with something not seen (a guess or the rumour).</summary>
        public int Guesses => Fields.Count(f => Picked(f)?.Kind == "wrong");
        /// <summary>Lines answered "모른다" (honest).</summary>
        public int Unknowns => Fields.Count(f => Picked(f)?.Kind == "honest");
        public string Lift => MountainLogic.Lift(Score, Full, Guesses);

        public JObject Result()
        {
            bool played = Phase == SightPhase.Done;
            var fields = new JObject();
            var kinds = new JObject();
            foreach (var f in Fields)
            {
                var o = Picked(f);
                if (o == null) continue;
                fields[f.Id] = o.Id; kinds[f.Id] = o.Kind;
            }
            return new JObject
            {
                ["played"] = played, ["noted"] = Setup.Noted, ["firstHow"] = FirstHow, ["how"] = How, ["howOk"] = FirstHow == "report",
                ["firstChannel"] = FirstChannel, ["channel"] = Channel, ["channelOk"] = ReachesPolice(FirstChannel),
                ["fields"] = fields, ["kinds"] = kinds, ["score"] = Score, ["full"] = Full, ["guesses"] = Guesses, ["unknowns"] = Unknowns,
                ["lift"] = played ? Lift : null,
            };
        }
    }

    /// <summary>A report card result as the story and the learning report read it (the "sighting" event's details).</summary>
    public sealed class SightResult
    {
        public bool Played, HowOk, ChannelOk, Noted;
        public string Channel, Lift;
        public int Score, Full, Guesses, Unknowns;

        public static SightResult Read(JToken t)
        {
            var o = t as JObject;
            if (o == null) return new SightResult();
            bool B(string k) => o[k]?.Type == JTokenType.Boolean && (bool)o[k];
            int I(string k) => o[k]?.Type == JTokenType.Integer ? (int)o[k] : 0;
            string S(string k) => o[k]?.Type == JTokenType.String ? (string)o[k] : null;
            return new SightResult
            {
                Played = B("played"), HowOk = B("howOk"), ChannelOk = B("channelOk"), Noted = B("noted"), Channel = S("channel"), Lift = S("lift"),
                Score = I("score"), Full = I("full"), Guesses = I("guesses"), Unknowns = I("unknowns"),
            };
        }

        /// <summary>"본 것만 빠짐없이": at least 10 of 12 and nothing made up.</summary>
        public bool Accurate => Played && Guesses == 0 && Full > 0 && Score * 6 >= Full * 5;
    }

    public static class MountainLogic
    {
        /// <summary>The rumour set (Rumors.PortAdded).</summary>
        public const string RumorId = "mountain_raid";

        /// <summary>The sender tag of every alert text (StoryCommon.Area; repeated here because Game does not reference Story).</summary>
        public const string Area = "[연무구(가상)]";

        public const string DeclareTime = "D+2 08:12";
        public const string DeclareSms = "[위급재난문자] 08:12 연무동 일대 통합방위 병종사태 선포. 외출 자제, 뒷산 출입 금지. 수상한 사람은 112, 정체 모를 물체는 1338 또는 가까운 경찰서로 신고 " + Area;

        /// <summary>
        /// When the 통제 is lifted, by the report (a game rule, shown as [게임 규칙]): early (no guess, 10+/12), evening
        /// (at most one guess and 7+/12), else late (kept overnight). No report at all: late too.
        /// </summary>
        public static string Lift(int score, int full, int guesses)
        {
            if (full <= 0) return "late";
            if (guesses == 0 && score * 6 >= full * 5) return "early";
            if (guesses <= 1 && score * 12 >= full * 7) return "evening";
            return "late";
        }

        /// <summary>The game minute (of D+2) the lift text arrives, or null when the 통제 stays overnight.</summary>
        public static int? LiftMinute(string lift) => lift == "early" ? 17 * 60 + 30 : lift == "evening" ? 20 * 60 + 10 : (int?)null;

        /// <summary>The evening text (안전안내문자) at hhmm: lifted, or the search goes on (late) with the next morning's word promised.</summary>
        public static string LiftSms(string lift, string hhmm) => lift == "late"
            ? $"[안전안내문자] {hhmm} 연무동 일대 수색 계속. 외출 자제와 뒷산 출입 금지 유지. 내일 아침 다시 안내 {Area}"
            : $"[안전안내문자] {hhmm} 연무동 일대 통합방위 병종사태 해제. 뒷산 출입 통제는 안전 점검 뒤 해제. 협조에 감사드립니다 {Area}";

        /// <summary>The promised next-morning text after a "late" evening (D+3, MountainScene.MorningAfter).</summary>
        public const string MorningTime = "D+3 07:30";
        public const string MorningSms = "[안전안내문자] 07:30 연무동 일대 통합방위 병종사태 해제. 뒷산 출입 통제는 안전 점검 뒤 해제. 협조에 감사드립니다 " + Area;

        /// <summary>
        /// What the lift means for the review page (the time it would be lifted), naming the actual cause: a guess slows
        /// the search; an honest "모른다" only leaves it wide (and is still better than a guess).
        /// </summary>
        public static string LiftLine(string lift, int guesses) => lift switch
        {
            "early" => "정확한 신고 덕에 수색 범위가 빨리 좁혀졌다. 통제는 오늘 17:30쯤 풀린다.",
            "evening" => guesses > 0
                ? "신고가 도움이 됐다. 다만 짐작이 섞여 확인에 시간이 걸렸다. 통제는 오늘 밤 20:10쯤 풀린다."
                : "신고가 도움이 됐다. 다만 모르는 칸이 있어 통제는 오늘 밤 20:10쯤 풀린다.",
            _ => guesses > 0
                ? "짐작이 섞여 수색이 넓게 퍼졌다. 통제는 내일 아침까지 이어진다."
                : "모르는 칸이 많아 수색 범위를 좁히기 어려웠다. 그래도 짐작보다는 낫다. 통제는 내일 아침까지 이어진다.",
        };

        // ---------------------------------------------------------------- the intercom (현관 앞의 낯선 사람)

        /// <summary>Right: talk through the closed door and check who it is (door_talk). Opening first is the one to rethink.</summary>
        public static bool IntercomOk(string first) => first == "ask";

        // ---------------------------------------------------------------- the object on the playground (미상 낙하물)

        /// <summary>Right first move: keep the children away without touching it (object_no_touch).</summary>
        public static bool ObjectFirstOk(string first) => first == "shout";

        /// <summary>
        /// Right report: 1338 or the nearest police station (report_object); with the phone dead, asking the office to
        /// report it to 1338 (office1338). "관리사무소에만 말한다" (office) leaves the report to someone else.
        /// </summary>
        public static bool ObjectReportOk(string reported) => reported == "1338" || reported == "police" || reported == "office1338";

        // ---------------------------------------------------------------- the record card (그날, 시민들은)

        public const string RecordTitle = "그날, 시민들은";
        public const string RecordMeta = "장 사이 기록 · 실제 있었던 일";
        /// <summary>Three sourced lines (서울신문 1996.9.20), no names, no victims; then today's line.</summary>
        public static readonly string[] RecordFacts = { "record_1996_taxi", "record_1996_student", "record_1996_tips" };
        public const string TodayFact = "suspect_report";
    }
}
