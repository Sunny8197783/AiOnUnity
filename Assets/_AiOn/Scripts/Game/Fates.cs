using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>What became of one person by D+14: a neighbour (who = their id) or someone met once (who = 'stranger').</summary>
    public sealed class PersonLine
    {
        [JsonProperty("who")] public string Who;
        /// <summary>The neighbour's name; "" for a stranger.</summary>
        [JsonProperty("name")] public string Name;
        [JsonProperty("line")] public string Line;
        /// <summary>Met again on the D+14 roam.</summary>
        [JsonProperty("present")] public bool Present;
        /// <summary>The bus ending's mailbox: a short letter, or null.</summary>
        [JsonProperty("letter", NullValueHandling = NullValueHandling.Ignore)] public string Letter;
    }

    /// <summary>
    /// What became of the neighbours and the town by D+14 (original game/fates.js). Pure functions of the deed
    /// ledger and the last-day path. No neighbour dies in any line and no line blames anyone.
    /// </summary>
    public static class Fates
    {
        public static readonly string[] Paths = { "bus", "walk", "seoyeon", "missed", "stay", "volunteer" };
        /// <summary>Paths that leave 연무동.</summary>
        public static readonly HashSet<string> Leave = new() { "bus", "walk", "seoyeon" };
        /// <summary>Paths that stay in town: only these can open a world ending.</summary>
        public static readonly HashSet<string> Town = new() { "missed", "stay", "volunteer" };

        /// <summary>The last-day path (G.know.path, set by the evacuation chapter); anything else counts as the bus.</summary>
        public static string PathOf(GameState g)
        {
            var p = g.Know != null && g.Know.TryGetValue("path", out var t) ? JsSem.Str(t) : null;
            return p != null && Array.IndexOf(Paths, p) >= 0 ? p : "bus";
        }

        sealed class Ctx
        {
            public Dictionary<string, Deed> By;
            public Dictionary<string, string> Ps;
            public Dictionary<string, int> A;
            public string Path, Ending;
            public bool Town;

            public bool Has(string id) => By.ContainsKey(id);
            public int N(string id) => By.TryGetValue(id, out var d) ? d.N : 0;
            public JObject Detail(string id) => By.TryGetValue(id, out var d) && d.Details != null ? d.Details : new JObject();
            public string Promise(string id) => Ps.TryGetValue(id, out var s) ? s : "open";
        }

        static Ctx Context(GameState g, string ending)
        {
            var ds = Deeds.Of(g);
            var path = PathOf(g);
            return new Ctx
            {
                By = ds.ToDictionary(d => d.Id),
                Ps = Deeds.Promises(g, final: true, ds).ToDictionary(p => p.Id, p => p.State),
                A = Deeds.Axes(g, final: true),
                Path = path, Ending = ending, Town = Town.Contains(path),
            };
        }

        sealed class Rule
        {
            public Func<Ctx, bool> Pred;
            public string Line;
            public bool Present;
        }

        static Rule R(Func<Ctx, bool> pred, string line, bool present) => new() { Pred = pred, Line = line, Present = present };

        static readonly string[] ElderMet = { "elder_met", "hall_checked", "fire_warned", "elder_alert_knock", "elder_alert_told" };

        // pred, line, present per neighbour; the first match wins. present: met again on the D+14 roam.
        static readonly Dictionary<string, Rule[]> Rules = new()
        {
            ["seoyeon"] = new[]
            {
                R(c => c.Path == "seoyeon", "서연과 C 경사로를 함께 점검했다. 경사로는 이제 안내 요원이 없어도 열려 있다.", false),
                R(c => c.Has("stairs_asked") && c.Has("water_for_seoyeon"), "서연에게서 문자가 왔다. \"그날 계단에서 먼저 물어봐 줘서, 물도 날라 줘서 고마웠어요.\"", false),
                R(c => c.Has("handover_precise"), "서연은 그날 밤의 문자를 아직 지우지 않았다고 했다. 시각과 자리가 정확히 적힌 문자였다.", false),
                R(c => c.Promise("note_for_seoyeon") == "broken" || c.Promise("news_for_seoyeon") == "broken", "서연은 그 뒤로 소식을 받지 못해 걱정했다고 했다. 다음에는 연락 방법을 하나 더 정해 두자고 했다.", false),
                R(c => !c.Has("seoyeon_contact"), "303호 문 앞에는 \"먼저 떠납니다 — 서연\"이라는 메모가 붙어 있었다. 연락처는 적혀 있지 않았다.", false),
                R(c => true, "서연에게서 짧은 문자가 왔다. \"덕분에 무사히 도착했어요.\"", false),
            },
            ["manager"] = new[]
            {
                R(c => c.Ending == "commons", "박 소장은 천막 기둥에 남은 집 명단을 붙여 두고, 하루 두 번 이름을 부른다.", true),
                R(c => c.Has("stock_to_manager"), "박 소장은 맡기고 간 물과 통조림을 어느 집에 몇 개 나눴는지 공책에 적어 두었다.", true),
                R(c => c.Has("candle_left"), "경보가 울린 밤, 박 소장은 빈 302호에 켜진 촛불을 꺼 두었다. 그 얘기를 한 번도 꺼내지 않았다.", true),
                R(c => !c.Town && !c.Has("manager_contact") && !c.Has("door_note"), "박 소장은 302호에 사람이 있는지 몰라 사흘 동안 문을 두드렸다고 했다.", true),
                R(c => c.Has("water_declined"), "그날 서연의 물은 박 소장이 대신 날랐다. \"그럴 때 부르라고 내가 있는 거예요.\"", true),
                R(c => c.Has("fire_handled"), "\"그날 복도 불, 판단한 사람 덕에 다들 무사했어요.\" 박 소장은 지금도 그 얘기를 한다.", true),
                R(c => true, "박 소장은 오늘도 관리사무소 창구를 지킨다. 빈 집 문단속도 여전히 그의 몫이다.", true),
            },
            ["clerk"] = new[]
            {
                R(c => c.Ending == "reopen", "수아는 나눔 선반 장부에 가져간 것과 두고 간 것을 나란히 적는다.", true),
                R(c => c.Has("store_took"), "수아는 재고 장부의 빈 칸을 지우지 않고 남겨 두었다. 누구였는지는 묻지 않았다.", false),
                R(c => c.Has("store_shuttered"), "수아는 셔터에 붙어 있던 쪽지를 사진으로 찍어 두었다고 했다.", false),
                R(c => c.Promise("store_watch") == "broken", "수아가 돌아왔을 때 가게는 반쯤 열린 채 비어 있었다. 수아는 말없이 선반부터 닦았다.", false),
                R(c => c.Has("clerk_contact"), "수아에게서 가끔 소식이 온다. 본가 쪽은 무사하다고 했다.", false),
                R(c => true, "바로편의점 셔터는 아직 내려가 있다.", false),
            },
            ["elder"] = new[]
            {
                R(c => (c.Has("elder_alert_knock") || c.Has("elder_alert_told")) && c.Town, "304호 어르신은 급수차가 오면 302호 몫까지 물을 받아 두신다.", true),
                R(c => c.Has("elder_alert_knock"), "304호 어르신이 박 소장 편에 안부를 전해 왔다. \"그날 계단에서 정말 고마웠어요.\"", false),
                R(c => c.Has("elder_alert_told"), "304호 어르신이 박 소장 편에 안부를 전해 왔다. \"그날 소장님한테 알려 줘서 고마웠어요.\"", false),
                R(c => c.Has("elder_candle_tip") && c.Has("fire_warned"), "\"촛불 말해 준 거 깜빡했어요. 그래도 그 밤 '불이야' 소리에 바로 나왔지.\"", true),
                R(c => c.Has("fire_warned"), "304호 어르신은 그 밤 \"불이야\" 소리를 듣고 바로 나왔다며 고마워하셨다.", true),
                R(c => c.Has("elder_candle_tip"), "304호 어르신은 이제 손전등을 머리맡에 두고 주무신다.", true),
                R(c => !ElderMet.Any(c.Has), "304호 어르신의 이름은 끝내 몰랐다. 아드님이 모시러 왔다는 얘기만 들었다.", false),
                R(c => true, "304호 어르신은 복도에서 마주치면 먼저 고개를 끄덕여 주신다.", true),
            },
        };

        // pred, line: people met once. At most three are shown, in this order.
        static readonly (Func<Ctx, bool> pred, Func<Ctx, string> line)[] Strangers =
        {
            (c => c.Has("driver_text") && c.Path != "walk", c => "김 기사님에게서 문자가 왔다. 화물차로 구호물자를 나르고 있다고 했다."),
            (c => c.Has("looter_gave"), c => "먹을 것을 나눠 준 그 사람을 몇 주 뒤 배급소에서 다시 만났다. 그는 고개를 숙여 인사했다."),
            (c => c.Has("knock_helped"), c => "그날 밤 문 너머로 물을 청하던 사람을, 며칠 뒤 조끼를 입은 자원봉사자로 다시 만났다."),
            (c => c.Has("looter_escaped"), c => "골목에서 물러섰던 날을 가끔 떠올린다. 물러선 것을 후회하지 않는다."),
            // added by the port (competition upgrade): the stranger's other answers
            (c => c.Has("looter_reported"), c => "골목에서 112에 차분히 알렸던 날을 가끔 떠올린다. 말하길 잘했다."),
            (c => c.Has("looter_neighbors"), c => "골목에서 곁에 서 주었던 두 주민과는 지금도 오가며 인사를 나눈다."),
            (c => c.Has("looter_argued"), c => "골목에서 언성을 높였던 날을 가끔 떠올린다. 다르게 말할 수도 있었다."),
            // added by the port: the bystander CPR in the shelter (it always ended with the paramedics taking over)
            (c => c.Has("cpr_helped"), c => "대피소에서 쓰러졌던 할아버지의 가족이 고맙다는 쪽지를 관리사무소에 맡겨 두었다."),
            // added by the port: 4장 「임시 거처의 하루」 (the child next door, the outlet)
            (c => c.Has("shelter_child_care"), c => "임시 거처에서 봐 주었던 지우가 엄마 손을 잡고 와서 고맙다며 손을 흔들었다."),
            (c => c.Has("shelter_child_referred"), c => "임시 거처 아이 돌봄 자리의 자원봉사자가, 그날 자리를 알려 준 덕에 지우가 거기서 친구를 사귀었다고 전해 주었다."),
            (c => c.Has("shelter_outlet_shared"), c => "충전 차례를 양보했던 할머니가 아드님과 연락이 닿았다며 급수 줄에서 손을 꼭 잡아 주셨다."),
            // added by the port: 3장 「산에서 내려온 사람들」 (the courier at the door, the report: only an accurate one helped narrow the search)
            (c => c.Has("intercom_checked") || c.Has("intercom_police"), c => "그날 인터폰으로 길을 물었던 택배 기사는 이제 104동 앞에서 먼저 손을 흔든다."),
            (c => c.Has("sighting_reported"), c => "뒷산 통제가 풀린 뒤, 파출소에서 그날의 신고가 수색 범위를 좁히는 데 도움이 됐다는 연락이 왔다."),
            // old saves only (nobody fights any more); port change: the original said 주먹을 휘둘렀던
            (c => c.Has("looter_fought"), c => "골목에서 시비가 붙었던 날의 기억은 오래 꿈에 나왔다."),
            (c => c.Has("shelter_water_received"), c => "대피소에서 물을 건네받았던 일을 기억한다. 급수 줄에서, 이번에는 내가 물을 건넸다."),
            (c => c.Has("shelter_water_given"), c => "대피소 옆자리 아이의 가족이 급수 줄에서 먼저 알아보고 손을 흔들었다."),
            (c => c.Has("mart_share"), c => "마트에서 생수를 나눠 드린 할머니를 급수 줄에서 다시 만났다."),
            (c => c.Has("rumor_corrected"), c => JsSem.Is(c.Detail("rumor_corrected")["after"], "spread")
                ? "102동 주민은 다시 알려 줘서 고맙다고 했다. 고친 말도 처음 말처럼 빨리 돌았다."
                : "102동 주민은 그 뒤로 들은 말을 옮기기 전에 출처부터 묻는다고 했다."),
            (c => c.Has("lead_honest") && c.Has("yoon_reply") && c.Ending != "relief", c => "윤 팀장에게서 문자가 왔다. \"확인된 것만 말하던 사람, 일손이 필요할 때 생각나더라.\""),
        };

        // The bus ending's mailbox: a short letter from each neighbour with a bond of 3+ who had a way to reach you.
        static readonly Dictionary<string, (string deed, string text)> Letters = new()
        {
            ["seoyeon"] = ("seoyeon_contact", "\"저도 무사히 도착했어요. 연락 방법을 미리 정해 둔 덕분에 서로 소식을 놓치지 않았네요.\""),
            ["manager"] = ("manager_contact", "\"302호, 잘 지내지요? 단지는 내가 지키고 있어요. 빈 집 문단속도 계속하고 있고요.\""),
            ["clerk"] = ("clerk_contact", "\"본가 쪽은 다들 무사해요. 가게 다시 열면 제일 먼저 알려 드릴게요.\""),
            ["elder"] = ("elder_met", "\"302호 청년, 그때 고마웠어요. 끼니 거르지 말고 몸 성히 지내요.\""),
        };

        /// <summary>The neighbour's letter, or null. a: the final axes; has: whether a deed was done.</summary>
        public static string LetterOf(string who, IReadOnlyDictionary<string, int> a, Func<string, bool> has) =>
            who != null && a.TryGetValue(who, out var bond) && bond >= 3 && Letters.TryGetValue(who, out var l) && has(l.deed) ? l.text : null;

        /// <summary>The four neighbours, then up to three people met once.</summary>
        public static List<PersonLine> PeopleLines(GameState g, string endingId)
        {
            var c = Context(g, endingId);
            var list = new List<PersonLine>();
            foreach (var who in Deeds.NeighbourIds)
            {
                var rule = Rules[who].First(r => r.Pred(c));
                list.Add(new PersonLine
                {
                    Who = who, Name = Deeds.Neighbours[who].Name, Line = rule.Line, Present = rule.Present,
                    Letter = endingId == "bus" ? LetterOf(who, c.A, c.Has) : null,
                });
            }
            foreach (var (_, line) in Strangers.Where(s => s.pred(c)).Take(3))
                list.Add(new PersonLine { Who = "stranger", Name = "", Line = line(c), Present = false });
            return list;
        }

        public static List<string> TownLines(GameState g, string endingId)
        {
            var c = Context(g, endingId);
            int t = c.A["town"];
            string band = t >= 10 ? "단지 게시판에는 물을 나눈 사람의 이름과 날짜가 빼곡히 적혀 있다."
                : t >= 4 ? "급수대에서 남의 물통을 들어 주는 일이 흔해졌다." : "단지의 문들은 대체로 닫혀 있다.";
            // the seoyeon path is back in town by D+14
            var list = c.Town || c.Path == "seoyeon" ? new List<string> { band } : new List<string> { "연무동 소식은 박 소장의 문자로 들었다.", band };
            if (c.Has("rumor_corrected") || c.Has("rumor_checked")) list.Add("대피소가 폐쇄됐다는 소문은 하루를 넘기지 못했다.");
            else if (c.Has("rumor_spread")) list.Add("대피소가 폐쇄됐다는 소문은 한참 동안 돌았다.");
            if (list.Count < 2) list.Add("급수차는 매일 단지 입구에 섰다.");
            return list;
        }
    }
}
