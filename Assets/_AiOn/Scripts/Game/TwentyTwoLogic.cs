// 「22분」 (competition upgrade): the rules of the D-1 morning interlude, an adaptation of the 2023.5.31 Seoul 경계경보
// 오발령 (facts info_misfire, misfire_times, misfire_app). A 위급재난문자 with no reason and no place arrives at 06:41; the
// correction ("오발령") comes 22 minutes later, at 07:03, whatever the player does. In between 정민 picks what to do, one
// thing at a time; each takes game minutes. The evaluation is NOT whether the alert was real but whether the ORDER of the
// response was sensible: official channels first (radio_listen), get ready (alert_caution), the elderly first
// (alert_caution), then keep listening ready (radio_listen; what a 경계경보 and the broadcast ask) or go to the nearest
// underground space (shelter_near), no flood of calls (comms_calls), no rumour spread (info_rumor), not back to bed.
// The minutes are game numbers, not facts.
//
// Pure: no UnityEngine, so the EditMode tests read it directly.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>One thing 정민 can do in the 22 minutes.</summary>
    public sealed class TwentyTwoAction
    {
        public readonly string Id, Label;
        public readonly int Minutes;
        public TwentyTwoAction(string id, string label, int minutes) { Id = id; Label = label; Minutes = minutes; }
    }

    /// <summary>One line of the review card (and the learning report).</summary>
    public sealed class TwentyTwoRow
    {
        public string Id, Label, Fact;
        public bool Ok;
    }

    /// <summary>The 22 minutes as they are played: the picks in order and the clock (game minutes after 06:41).</summary>
    public sealed class TwentyTwoRun
    {
        public readonly List<string> Picks = new();
        /// <summary>Minute of the day now (starts at the text, 06:41).</summary>
        public int Minute = TwentyTwoLogic.SentAt;
        public bool Deaf;
        /// <summary>Went down to the underground space (the choosing ends there: wait for the next word).</summary>
        public bool Down => Picks.Contains("down");
        /// <summary>Went back to bed (the choosing ends: the correction wakes 정민).</summary>
        public bool Slept => Picks.Contains("sleep");
        public bool WithElder => Picks.Contains("elder");
        /// <summary>Ready and kept listening to the broadcast (the choosing ends: wait for the next word).</summary>
        public bool Listened => Picks.Contains("listen");
        /// <summary>The correction is in (07:03) or nothing is left to choose.</summary>
        public bool Over => Minute >= TwentyTwoLogic.CorrectionAt || Down || Slept || Listened || Available().Count == 0;
        public int Elapsed => Minute - TwentyTwoLogic.SentAt;

        /// <summary>What can be picked now, in the order shown (follow-ups take the place of the call).</summary>
        public List<TwentyTwoAction> Available()
        {
            var list = new List<TwentyTwoAction>();
            foreach (var a in TwentyTwoLogic.Actions(Deaf))
            {
                if (a.Id == "call")
                {
                    if (!Picks.Contains("call")) { list.Add(a); continue; }
                    if (!Picks.Contains("text") && !Picks.Contains("redial"))
                    {
                        list.Add(TwentyTwoLogic.Text);
                        list.Add(TwentyTwoLogic.Redial(Deaf));
                    }
                    continue;
                }
                // keep listening: once the TV is on and 정민 is ready; back to bed: not after knocking at 304 (she waits)
                if (a.Id == "listen" && !(Picks.Contains("official") && Picks.Contains("ready"))) continue;
                if (a.Id == "sleep" && WithElder) continue;
                if (!Picks.Contains(a.Id)) list.Add(a);
            }
            return list;
        }

        /// <summary>Takes an action: the minutes it costs (with 어르신 the walk down is slower). Throws on one not offered.</summary>
        public int Pick(string id)
        {
            var a = Available().FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException($"'{id}' is not offered now");
            int cost = a.Id == "down" && WithElder ? a.Minutes + TwentyTwoLogic.ElderSlower : a.Minutes;
            if (a.Id == "sleep" || a.Id == "listen") cost = Math.Max(0, TwentyTwoLogic.CorrectionAt - Minute);
            Picks.Add(a.Id);
            Minute += cost;
            return cost;
        }

        /// <summary>Did the correction arrive before this action was finished (the walk down, cut short)?</summary>
        public bool CorrectionDuringLast => Minute > TwentyTwoLogic.CorrectionAt && !Slept && !Listened;

        public JObject Result(string next = null)
        {
            var o = new JObject
            {
                ["played"] = true,
                ["picks"] = new JArray(Picks),
                ["elapsed"] = Elapsed,
                ["verdict"] = TwentyTwoLogic.Verdict(Picks),
            };
            if (next != null) o["next"] = next;
            return o;
        }
    }

    public static class TwentyTwoLogic
    {
        public const string Event = "twentytwo";
        /// <summary>Know: what 정민 decided for the next alert ("check" | "doubt"); the 2장 card reads it.</summary>
        public const string NextKnow = "ttNext";
        /// <summary>The 2장 card "그날 22분이 남긴 것" (shown once, after the 2장 chapter card).</summary>
        public const string AfterEvent = "twentytwo_after";

        /// <summary>Issued 06:32, the text 06:41, the correction 07:03 (22 minutes after the text): the 2023 times.</summary>
        public const int IssuedAt = 6 * 60 + 32, SentAt = 6 * 60 + 41, CorrectionAt = 7 * 60 + 3;
        public const int Span = CorrectionAt - SentAt;
        /// <summary>Game minutes added to the walk down with 304호 어르신 (her pace).</summary>
        public const int ElderSlower = 2;

        public const string Title = "22분";
        public const string ReviewTitle = "22분 돌아보기";
        /// <summary>Quotes the two texts; does not rule on the 2023 dispute (info_misfire: "오발령 논란").</summary>
        public const string ReviewMeta = "2023년 5월 31일 서울의 경계경보 문자와 22분 뒤 '오발령' 정정 문자를 바탕으로 한 장면";
        public const string AfterTitle = "그날 22분이 남긴 것";

        /// <summary>The text: no reason, no place (as the 2023 text). In-game area tag as every story text.</summary>
        public static string AlertSms(string area) =>
            $"[위급재난문자] 오늘 6시 32분 연무동지역에 경계경보 발령. 주민 여러분께서는 대피할 준비를 하시고, 어린이와 노약자가 우선 대피할 수 있도록 해 주시기 바랍니다. {area}";
        /// <summary>The correction, 22 minutes later: no reason given either, and nobody named.</summary>
        public static string CorrectionSms(string area) => $"[위급재난문자] 06:41 연무동지역에 발령된 경계경보는 오발령 사항임을 알려드림 {area}";

        public static readonly TwentyTwoAction Text = new("text", "전화는 그만두고, 문자 한 통만 보낸다", 1);
        public static TwentyTwoAction Redial(bool deaf) => new("redial", deaf ? "될 때까지 영상통화를 다시 건다" : "될 때까지 다시 건다", 4);

        /// <summary>The choices, in the order shown (not the order recommended).</summary>
        public static IReadOnlyList<TwentyTwoAction> Actions(bool deaf) => new[]
        {
            new TwentyTwoAction("app", "휴대폰으로 대피소 위치를 찾아본다", 2),
            new TwentyTwoAction("official", deaf ? "TV를 켜고 재난방송 자막을 찾는다" : "TV를 켜고 재난방송을 찾는다", 3),
            new TwentyTwoAction("call", deaf ? "가족에게 영상통화를 건다" : "가족에게 전화를 건다", 2),
            new TwentyTwoAction("ready", "옷을 입고 신발을 신는다. 나갈 짐을 챙긴다", 4),
            new TwentyTwoAction("chat", "단톡방에 \"전쟁 난 거 아니에요?\"라고 올린다", 1),
            new TwentyTwoAction("elder", "304호 어르신 댁 문을 두드린다", 5),
            new TwentyTwoAction("down", "가장 가까운 지하시설(단지 지하주차장)로 내려간다", 6),
            new TwentyTwoAction("listen", deaf ? "준비를 마친 채 재난방송 자막을 계속 지켜본다" : "준비를 마친 채 재난방송을 계속 듣는다", 0),
            new TwentyTwoAction("sleep", "오발령이겠지. 다시 눕는다", 0),
        };

        /// <summary>The short name of a pick for the "내 순서" line.</summary>
        public static string Short(string id) => id switch
        {
            "app" => "대피소 앱(먹통)", "official" => "재난방송 확인", "call" => "전화(불통)", "text" => "문자 한 통", "redial" => "전화 다시 걸기",
            "ready" => "대피 준비", "chat" => "단톡방에 올리기", "elder" => "304호 어르신", "down" => "지하주차장으로", "listen" => "방송 들으며 대기", "sleep" => "다시 눕기", _ => id,
        };

        /// <summary>The order this scene recommends (not an official sequence: each step has its own sourced fact).</summary>
        public const string Recommended = "공식 방송 확인 → 대피 준비 → 노약자 먼저 → 방송을 들으며 대기, 또는 가장 가까운 지하시설로";

        /// <summary>The review rows, from the picks in order. Each row names the sourced fact behind it.</summary>
        public static List<TwentyTwoRow> Rows(IReadOnlyList<string> picks)
        {
            picks ??= Array.Empty<string>();
            int At(string id) { for (int i = 0; i < picks.Count; i++) if (picks[i] == id) return i; return -1; }
            bool Has(string id) => At(id) >= 0;
            var rows = new List<TwentyTwoRow>();
            void Row(string id, bool ok, string yes, string no, string fact) => rows.Add(new TwentyTwoRow { Id = id, Ok = ok, Label = ok ? yes : no, Fact = fact });

            int official = At("official");
            // early: among the first two things done, and before anything that acts on a guess (going down, posting, bed)
            bool early = official >= 0 && official <= 1 && new[] { "down", "chat", "sleep" }.All(x => !Has(x) || At(x) > official);
            Row("official", early, "이유도 장소도 없는 문자를 TV 재난방송으로 먼저 확인했다",
                official >= 0 ? "공식 방송을 확인하기 전에 다른 일부터 했다" : "TV·라디오 재난방송을 확인하지 않았다", "radio_listen");
            Row("ready", Has("ready"), "옷과 신발을 챙겨 대피할 준비를 했다", "대피할 준비를 하지 않았다", "alert_caution");
            int elder = At("elder"), down = At("down");
            Row("elder", elder >= 0 && (down < 0 || elder < down), "304호 어르신을 먼저 챙겼다",
                elder >= 0 ? "304호 어르신보다 내가 먼저 내려갔다" : "304호 어르신을 챙기지 않았다", "alert_caution");
            // under a 경계경보 the broadcast asks to get ready and keep listening: staying ready by the broadcast is as right
            // as going down (listen is offered only after official and ready)
            Row("down", down >= 0 || Has("listen"), down >= 0 ? "가장 가까운 지하시설 쪽으로 움직였다" : "준비를 마치고 방송을 들으며 다음 안내를 기다렸다",
                "준비를 마친 뒤 방송을 계속 듣거나 가까운 지하시설로 가는 데까지 가지 못했다", "shelter_near");
            if (Has("call")) Row("calls", !Has("redial"), "전화가 안 되자 문자 한 통으로 바꿨다", "안 되는 전화를 계속 걸었다", "comms_calls");
            Row("rumor", !Has("chat"), "확인되지 않은 말을 단톡방에 올리지 않았다", "확인되지 않은 말을 단톡방에 올렸다", "info_rumor");
            if (Has("sleep")) Row("bed", false, null, "경보 문자를 받고 다시 누웠다", "alert_caution");
            return rows;
        }

        /// <summary>good: checked, got ready, the elder first, kept listening or moved, no flood and no rumour, not back to bed; poor: back to bed or under half right.</summary>
        public static string Verdict(IReadOnlyList<string> picks)
        {
            var rows = Rows(picks);
            bool R(string id) => rows.Any(r => r.Id == id && r.Ok);
            bool NoBad(string id) => rows.All(r => r.Id != id || r.Ok);
            if (picks != null && picks.Contains("sleep")) return "poor";
            if (R("official") && R("ready") && R("elder") && R("down") && NoBad("calls") && NoBad("rumor")) return "good";
            return rows.Count(r => r.Ok) * 2 >= rows.Count ? "partial" : "poor";
        }

        public static string VerdictLine(string verdict) => verdict switch
        {
            "good" => "경보가 진짜였어도, 이 순서라면 늦지 않았을 것이다.",
            "partial" => "몇 가지는 잘했다. ○ 표시한 것을 다음에는 먼저 떠올려 보자.",
            _ => "경보가 진짜였다면, 22분은 짧은 시간이 아니었다.",
        };

        /// <summary>The 2장 card's line on the first thing done that morning.</summary>
        public static string FirstLine(IReadOnlyList<string> picks) =>
            picks == null || picks.Count == 0 ? "그날 아침에는 아무것도 하지 않았다." : $"그날 아침 처음 한 일: {Short(picks[0])}.";

        /// <summary>Reads the picks of the recorded event (null when the interlude was not played).</summary>
        public static List<string> PicksOf(GameState g)
        {
            var e = g?.Events?.LastOrDefault(x => x.Type == Event);
            if (e?.Details?["picks"] is not JArray a) return null;
            return a.Where(t => t.Type == JTokenType.String).Select(t => (string)t).ToList();
        }
    }
}
