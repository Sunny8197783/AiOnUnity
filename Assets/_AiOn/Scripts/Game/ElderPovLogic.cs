// 2장 「304호의 계단」 (competition upgrade): the rules of the 304호 어르신 point of view (Story/ElderPovScene.cs).
// The same air-raid night and the same stairs as 정민's, walked at her pace. What the earlier run did decides who is
// with her and what she has in hand:
//   elderPick help    - 정민 is at her side from her door (the same exchange the player had, heard from her side)
//   elderPick manager - 박 소장, told by 정민 at the 1F window (know.elderTold), comes up: she waits at her door, or
//                       starts down and meets him on 2F. Not told (정민 walked out past the window): she waits a while,
//                       then goes on as with "go" ("untold": the 203호 neighbour on 2F, 박 소장 on his round at 1F)
//   elderPick go      - she starts down alone; a neighbour from 203호 joins her on 2F, 박 소장 meets them on 1F
//   「22분」 morning knock (TwentyTwoLogic picks "elder") - since that morning her shoes, a small bag and a flashlight wait
//                       by her door: a light on the dark stairs and no packing
//   elder_candle_tip (2장 blackout) - she puts the candle out before she leaves, remembering who said so
// Every pace, rest and minute here is a game number, never a fact sentence (the review card marks the minutes so).
//
// Pure: no UnityEngine, so the EditMode tests read it directly.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>What this run did before the air-raid night that reaches 304호 (read from the game, see Read).</summary>
    public sealed class ElderPovSetup
    {
        /// <summary>G.know.elderPick: help | manager | go.</summary>
        public string Pick;
        /// <summary>manager: 정민 did stop at the 1F window and tell 박 소장 (know.elderTold, Elder.ManagerLine).</summary>
        public bool Told;
        /// <summary>1장: 정민 greeted her (deed elder_met).</summary>
        public bool Met;
        /// <summary>「22분」 morning: 정민 knocked on 304 (and went down with her: MorningDown).</summary>
        public bool MorningKnock, MorningDown;
        /// <summary>2장 blackout: "자리 비우실 땐 촛불 꼭 꺼 주세요" (deed elder_candle_tip).</summary>
        public bool CandleTip;
        /// <summary>정민 helped her down with a bag over the limit (bag_heavy where stairs).</summary>
        public bool HeavyBag;
        /// <summary>정민 had a light in hand that night (flashlight or phone).</summary>
        public bool JeongminLight;

        /// <summary>Since the 「22분」 morning her shoes, a small bag and a flashlight wait by her door.</summary>
        public bool Prepared => MorningKnock;

        /// <summary>How her night goes: help | manager (told) | untold (manager, but 박 소장 never heard) | go.</summary>
        public string Way => Pick == "manager" && !Told ? "untold" : Pick;
    }

    /// <summary>What happened on her stairs (the event's details and the review card's input).</summary>
    public sealed class ElderPovResult
    {
        public string Pick, Helper, MeetAt;
        /// <summary>ElderPovSetup.Way: help | manager | untold | go.</summary>
        public string Way;
        /// <summary>manager: she waited at her door for 박 소장 (else she started down and met him on 2F).</summary>
        public bool Waited;
        /// <summary>Not prepared: she took the bankbook and the photo album too (heavier, slower).</summary>
        public bool Bundle;
        /// <summary>tip (out, remembering 정민's words) | out | reminded (the helper at her door said so) | left (still lit: 박 소장 looks after the all-clear).</summary>
        public string Candle;
        public bool Prepared, Light;
        public int Rests, Minutes;
        /// <summary>The wheelchair profile: the flights were taken one choice at a time (no free walking on stairs).</summary>
        public bool Guided;

        public JObject ToJson() => new JObject
        {
            ["pick"] = Pick, ["way"] = Way, ["helper"] = Helper, ["meetAt"] = MeetAt, ["waited"] = Waited, ["bundle"] = Bundle, ["candle"] = Candle,
            ["prepared"] = Prepared, ["light"] = Light, ["rests"] = Rests, ["minutes"] = Minutes, ["guided"] = Guided,
        };
    }

    public static class ElderPovLogic
    {
        public const string Event = "elder_pov";
        public const string ReviewTitle = "304호의 계단";
        public const string OfferTitle = "그 시각, 304호";

        public static readonly string[] Picks = { "help", "manager", "go" };

        // ---------------------------------------------------------------- game numbers

        /// <summary>Her walking pace (a speed multiplier on the player's walk), on the flat and alone in the dark.</summary>
        public const double BasePace = 0.34;
        /// <summary>On a stair flight the pace is this much of the flat one.</summary>
        public const double StairFactor = 0.78;
        public const double LightBonus = 0.05, CompanionBonus = 0.05, BundlePenalty = 0.06;
        public const double MinPace = 0.2, MaxPace = 0.5;
        /// <summary>Seconds she stops to catch her breath on a landing: with someone, with a light, alone in the dark.</summary>
        public const double RestCompanion = 2.2, RestLight = 2.8, RestDark = 3.6;
        /// <summary>Untold: the minutes she waits at her door for 박 소장 before she starts down herself.</summary>
        public const int UntoldWait = 2;
        /// <summary>The three landings on the way (half landing below 3F, 2F, half landing below 2F).</summary>
        public const int Landings = 3;

        // ---------------------------------------------------------------- reading the run

        static string Str(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;

        static bool HasDeed(GameState g, string id) =>
            g.Events.Any(e => e.Type == "deed" && Str(e.Details?["id"]) == id);

        /// <summary>The run's 304호 threads. lightSource: StoryContext.LightSource (null = no light in hand).</summary>
        public static ElderPovSetup Read(GameState g, string lightSource)
        {
            if (g == null) throw new ArgumentNullException(nameof(g));
            var picks = TwentyTwoLogic.PicksOf(g) ?? new List<string>();
            return new ElderPovSetup
            {
                Pick = g.Know != null && g.Know.TryGetValue("elderPick", out var p) ? Str(p) : null,
                Told = g.Knows("elderTold"),
                Met = HasDeed(g, "elder_met"),
                MorningKnock = picks.Contains("elder"),
                MorningDown = picks.Contains("elder") && picks.Contains("down"),
                CandleTip = HasDeed(g, "elder_candle_tip"),
                HeavyBag = g.Events.Any(e => e.Type == "bag_heavy" && Str(e.Details?["where"]) == "stairs"),
                JeongminLight = lightSource == "flashlight" || lightSource == "phone",
            };
        }

        /// <summary>The point of view is offered once, after the shelter seat, when 정민 met her at the stairs this run.</summary>
        public static bool Available(GameState g) =>
            g != null && Picks.Contains(g.Know != null && g.Know.TryGetValue("elderPick", out var p) ? Str(p) : null) && !Played(g);

        public static bool Played(GameState g) => g != null && g.Events.Any(e => e.Type == Event);

        // ---------------------------------------------------------------- who is with her

        /// <summary>help -> jeongmin, manager -> manager (박 소장), go and untold -> neighbour (203호). Takes ElderPovSetup.Way.</summary>
        public static string Helper(string pick) => pick == "help" ? "jeongmin" : pick == "manager" ? "manager" : "neighbour";

        /// <summary>The person model the helper walks as.</summary>
        public static string HelperPreset(string helper) => helper == "jeongmin" ? "jeongmin" : helper == "manager" ? "manager" : "crowd3";

        public static string HelperName(string helper) => helper == "jeongmin" ? "정민" : helper == "manager" ? "박 소장" : "203호 이웃";

        /// <summary>Where the helper joins her: "door" (3F, before the first step) or "2F" (the 2F landing). Takes the way.</summary>
        public static string MeetAt(string pick, bool waited) => pick == "help" || (pick == "manager" && waited) ? "door" : "2F";

        /// <summary>Whether the helper carries a light: 정민 only when he had one; 박 소장's flashlight, the neighbour's phone.</summary>
        public static bool HelperLight(string helper, bool jeongminLight) => helper != "jeongmin" || jeongminLight;

        // ---------------------------------------------------------------- the walk

        /// <summary>Her pace as a speed multiplier (the player's own experience mode does not slow it further).</summary>
        public static double Pace(bool stairs, bool light, bool companion, bool bundle)
        {
            double p = BasePace + (light ? LightBonus : 0) + (companion ? CompanionBonus : 0) - (bundle ? BundlePenalty : 0);
            if (stairs) p *= StairFactor;
            return Math.Max(MinPace, Math.Min(MaxPace, p));
        }

        /// <summary>How long she stops on a landing.</summary>
        public static double RestSeconds(bool light, bool companion) => companion ? RestCompanion : light ? RestLight : RestDark;

        /// <summary>
        /// The landing under (x, y, z) in the apartment's original frame (apartment-level.json walk): 1 = the half landing
        /// below 3F (y 4.2), 2 = the 2F landing (y 2.8), 3 = the half landing below 2F (y 1.4), 4 = 1F (the stair foot or
        /// the lobby), 0 = anywhere else (3F, a flight).
        /// </summary>
        public static int LandingAt(double x, double y, double z)
        {
            const double e = 0.02, dy = 0.25;
            bool Box(double x0, double x1, double z0, double z1) => x >= x0 - e && x <= x1 + e && z >= z0 - e && z <= z1 + e;
            if (Math.Abs(y) < 0.3 && Box(-1, 9, -6.5, 1.6)) return 4;
            if (Math.Abs(y - 4.2) < dy && Box(3.4, 6.6, 3.4, 5)) return 1;
            if (Math.Abs(y - 2.8) < dy && Box(3.4, 6.6, 0.1, 1.6)) return 2;
            if (Math.Abs(y - 1.4) < dy && Box(3.4, 6.6, 3.4, 5)) return 3;
            return 0;
        }

        /// <summary>
        /// The story minutes from the siren to the 1F stair foot: getting ready (prepared 1, else 2, the bundle 4), waiting
        /// for 박 소장 (3; untold: 2 before she sets off herself), the corridor (1), four flights (with someone 1 each; alone
        /// with a light 1.5, in the dark 2, rounded up per pair) and a minute per rest.
        /// </summary>
        public static int Minutes(ElderPovSetup s, bool waited, bool bundle, int rests)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            int ready = s.Prepared ? 1 : bundle ? 4 : 2;
            string way = s.Way;
            int wait = way == "manager" && waited ? 3 : way == "untold" ? UntoldWait : 0;
            string helper = Helper(way);
            bool together = MeetAt(way, waited) == "door";
            bool light = s.Prepared || (together && HelperLight(helper, s.JeongminLight));
            // the first two flights (3F to 2F): with the helper from the door, or alone; the last two always together
            int upper = together ? 2 : light ? 3 : 4;
            return ready + wait + 1 + upper + 2 + Math.Max(0, rests);
        }

        // ---------------------------------------------------------------- the review card

        /// <summary>
        /// The review card's lines (pure over the result and the facts). Not a score: what happened on her stairs, what of
        /// this run reached her, and the sourced lines it stands on.
        /// </summary>
        public static List<string> ReviewLines(ElderPovSetup s, ElderPovResult r, Func<string, string> fact, Func<string, string> source)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (r == null) throw new ArgumentNullException(nameof(r));
            var lines = new List<string>();
            var stairs = fact?.Invoke("alert_stairs");
            if (!string.IsNullOrEmpty(stairs)) lines.Add($"같은 문장 — \"{stairs}\"");
            const string Num = " <size=18><color=#94a3b8>[게임 수치]</color></size>";
            lines.Add((r.Helper == "jeongmin"
                          ? $"3층에서 1층까지 두 층. 정민이 어르신 걸음에 맞춰 함께 걸었고, 나설 준비부터 1층까지 약 {r.Minutes}분이 걸렸습니다."
                          : $"3층에서 1층까지 두 층. 정민의 걸음으로는 금방인 계단을, 어르신은 나설 준비부터 약 {r.Minutes}분 걸려 내려오셨습니다.")
                      + $" 층계참에서 {r.Rests}번 숨을 고르셨습니다." + Num);
            switch (r.Way ?? s.Way)
            {
                case "help":
                    lines.Add("정민이 곁에 있었습니다. 손잡이 쪽에 서 달라는 말에 그대로 서 주었습니다." + (s.HeavyBag ? " 무거운 배낭을 멘 채로도 어르신의 팔을 놓지 않았습니다." : ""));
                    break;
                case "manager":
                    lines.Add(r.Waited ? "정민이 알린 박 소장님이 3층까지 올라와, 처음부터 함께 내려갔습니다."
                                       : "정민이 알린 박 소장님을 2층에서 만났습니다. 그 전까지는 혼자 난간을 잡고 내려가셨습니다.");
                    break;
                case "untold":
                    lines.Add("정민은 소장님께 알리겠다고 했지만, 관리사무소 창구에는 들르지 못했습니다. 어르신은 잠시 기다리다 2층까지 혼자 내려가셨고, 2층부터는 203호 이웃과 함께, 1층에서 순찰을 돌던 박 소장님을 만났습니다.");
                    break;
                default:
                    lines.Add("정민은 먼저 내려갔습니다. 2층까지는 혼자, 2층부터는 203호 이웃과 함께 내려가셨고, 1층에서 순찰을 돌던 박 소장님을 만났습니다.");
                    break;
            }
            if (r.Prepared)
                lines.Add((s.MorningDown ? "「22분」 그날 아침 정민과 함께 내려가 본 뒤로" : "「22분」 그날 아침 정민이 문을 두드린 뒤로") +
                          ", 어르신은 현관에 신발과 작은 가방, 손전등을 두셨습니다. 오늘 밤 그 손전등이 캄캄한 계단을 비췄습니다.");
            else if (r.MeetAt == "door" && r.Light)
                lines.Add($"손전등은 없었지만, {HelperName(r.Helper)}의 불빛이 발밑을 비춰 주었습니다.");
            else if (r.MeetAt == "door")
                lines.Add("둘 다 손전등이 없어, 1층까지 비상구 유도등의 희미한 불빛에 기대 발끝으로 계단 끝을 더듬으며 내려가셨습니다.");
            else
                lines.Add($"손전등이 없어, 2층까지는 비상구 유도등의 희미한 불빛에 기대 발끝으로 계단 끝을 더듬으셨습니다. 2층부터는 {HelperName(r.Helper)}의 불빛이 함께였습니다.");
            if (r.Bundle) lines.Add("장롱 속 사진첩과 보따리까지 챙기느라 나서는 데 몇 분이 더 걸렸고, 짐이 무거워 걸음이 더 느렸습니다.");
            else if (!r.Prepared) lines.Add("약봉지와 신분증, 휴대폰만 챙겨 바로 나서셨습니다.");
            lines.Add(r.Candle == "tip" ? "촛불은 나서기 전에 끄셨습니다. 정전된 저녁 정민이 \"자리 비우실 땐 촛불 꼭 꺼 주세요\" 했던 말을 기억하셨습니다."
                    : r.Candle == "out" ? "촛불은 나서기 전에 끄셨습니다."
                    : r.Candle == "reminded" ? $"켜 둔 촛불은 {(r.Helper == "jeongmin" ? "정민" : "박 소장님")}이 끄고 가자고 해서, 나서기 전에 끄셨습니다."
                    : "촛불은 켜 둔 채 나오셨습니다. 박 소장님은 지금은 대피가 먼저라며, 경보가 해제되면 304호부터 올라가 보기로 했습니다.");
            lines.Add("같은 문장도 누가 읽느냐에 따라, 무엇을 준비해 두었느냐에 따라 걸리는 시간이 다릅니다.");
            lines.Add("오늘 밤은 사이렌이 울린 공습경보. 사이렌 없이 문자와 방송으로 알리는 경계경보 때부터 미리 움직이면 어르신의 걸음에도 시간이 남습니다.");
            var ids = FactIds(r);
            foreach (var id in ids)
            {
                var t = fact?.Invoke(id);
                if (!string.IsNullOrEmpty(t)) lines.Add($"<size=18><color=#8fd9cf>▸</color> <color=#94a3b8>{t}</color></size>");
            }
            var src = ids.Prepend("alert_stairs").Select(id => source?.Invoke(id)).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
            if (src.Count > 0) lines.Add($"<size=18><color=#94a3b8>출처: {string.Join(" · ", src)}</color></size>");
            return lines;
        }

        /// <summary>
        /// The sourced lines under the card: lights out before leaving on an air-raid night (alert_night), the flashlight
        /// by the door (power_prep), 신분증 in the quick pack (docs_take), the candle rule when the candle was left lit or
        /// needed a reminder, and moving 노약자 early at the 경계경보 (alert_caution, introduced on the card as the earlier alert).
        /// </summary>
        public static List<string> FactIds(ElderPovResult r)
        {
            var ids = new List<string> { "alert_night", "power_prep" };
            if (r != null && !r.Prepared && !r.Bundle) ids.Add("docs_take");
            if (r != null && (r.Candle == "left" || r.Candle == "reminded")) ids.Add("candle_rules");
            ids.Add("alert_caution");
            return ids;
        }
    }
}
