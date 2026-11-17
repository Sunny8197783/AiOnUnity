using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>One thing heard about a claim: the claim itself, or what can confirm or correct it.</summary>
    public sealed class RumorItem
    {
        /// <summary>'claim' for the claim the player judges; else who or what it comes from (manager, seoyeon, official).</summary>
        public readonly string Key;
        /// <summary>How it reaches the player: sns | neighbor | radio | notice.</summary>
        public readonly string Channel;
        /// <summary>Who says it, and the details that help weigh it (time, whether someone saw it), as shown to the player.</summary>
        public readonly string From, Meta;
        /// <summary>The notebook label it really deserves: official | site | neighbor | rumor.</summary>
        public readonly string Label;
        public readonly bool Truth;
        readonly Func<GameState, string> _text;

        public RumorItem(string key, string channel, string from, string meta, string label, bool truth, Func<GameState, string> text)
        { Key = key; Channel = channel; From = from; Meta = meta; Label = label; Truth = truth; _text = text; }

        /// <summary>The words for this run (the evacuation set names the road this seed closed).</summary>
        public string Text(GameState g) => _text(g);
    }

    /// <summary>A moment where a claim reaches the player together with other things heard about it.</summary>
    public sealed class RumorSet
    {
        public readonly string Id, Chapter;
        /// <summary>FACTS ids the set cites (cards after a judgement).</summary>
        public readonly IReadOnlyList<string> Facts;
        /// <summary>Items[0] (key 'claim') is the claim; the others can confirm or correct it.</summary>
        public readonly IReadOnlyList<RumorItem> Items;
        readonly Func<GameState, string> _road;

        public RumorSet(string id, string chapter, string[] facts, RumorItem[] items, Func<GameState, string> road = null)
        { Id = id; Chapter = chapter; Facts = facts; Items = items; _road = road; }

        /// <summary>The closure id the claim is about, for the set that names a road (evac_shortcut); null for the others.</summary>
        public string Road(GameState g) => _road?.Invoke(g);
    }

    /// <summary>A <see cref="RumorItem"/> with its text evaluated (talk lines, the phone and notes).</summary>
    public sealed class RumorPost
    {
        public string Key, Channel, From, Meta, Label, Text;
        public bool Truth;

        /// <summary>The original's object, key for key.</summary>
        public JObject ToJObject() => new JObject
        {
            ["key"] = Key, ["channel"] = Channel, ["from"] = From, ["meta"] = Meta, ["label"] = Label, ["truth"] = Truth, ["text"] = Text,
        };
    }

    /// <summary>What <see cref="Rumors.Judge"/> says about a pick.</summary>
    public sealed class RumorJudgement
    {
        /// <summary>The pick matches the item's real label, or the player chose 'rumor' (unconfirmed) for a false claim.</summary>
        public bool Ok;
        /// <summary>The label the item really deserves.</summary>
        public string Label;
        public bool Truth;
        /// <summary>sorted | doubted (a true item held back as rumor) | misled: goes to BagDrill.RecordEncounter(g, "info", result).</summary>
        public string Result;
        /// <summary>FACTS ids of the set.</summary>
        public List<string> Facts = new();

        public JObject ToJObject() => new JObject
        {
            ["ok"] = Ok, ["label"] = Label, ["truth"] = Truth, ["result"] = Result, ["facts"] = new JArray(Facts),
        };
    }

    /// <summary>A rumor the player has judged or believed, with its claim (the phone's SNS list).</summary>
    public sealed class SeenRumor
    {
        public string Id;
        public RumorPost Claim;
    }

    /// <summary>
    /// 소식 가려듣기 (original game/rumors.js): three moments where a claim reaches 정민 together with other things heard
    /// about it. Pure logic. Safety lines come from FACTS (info_rumor, info_scope, ...); the posts themselves are fiction.
    /// Every set holds at least one TRUE item that is not official, so the lesson is "check the source", not
    /// "only trust officials".
    /// </summary>
    public static class Rumors
    {
        // evac_shortcut names closures.evac[0], which conditions always close (GameConditions.EvacClosureSets).
        static string Shortcut(GameState g)
        {
            var evac = g.Cond?.Closures?.Evac;
            return evac != null && evac.Count > 0 ? evac[0] : null;
        }

        static string RoadLabel(GameState g) => TownMap.Closures[Shortcut(g)]?.Label ?? "통제 구간";

        /// <summary>The road's label without its trailing ' 통제' (the original's replace(/ 통제$/, '')).</summary>
        static string RoadName(GameState g)
        {
            const string suffix = " 통제";
            var label = RoadLabel(g);
            return label.EndsWith(suffix, StringComparison.Ordinal) ? label.Substring(0, label.Length - suffix.Length) : label;
        }

        /// <summary>Every rumor set (the original RUMORS), in story order.</summary>
        public static readonly IReadOnlyList<RumorSet> All = new[]
        {
            new RumorSet("shelter_closed", "endure", new[] { "shelter_rumor", "info_rumor", "info_harm" }, new[]
            {
                new RumorItem("claim", "sns", "SNS (102동 주민이 전해 줌)", "작성자·시각 없음", "rumor", false,
                    _ => "생활문화센터 대피소가 폐쇄됐대요. 다들 다른 데로 가래요."),
                new RumorItem("manager", "neighbor", "박영수 소장", "오늘 오후 · 직접 다녀옴", "site", true,
                    _ => "아까 생활문화센터 앞까지 다녀왔는데, 출입구 A는 열려 있고 안내 요원도 있었어요."),
                new RumorItem("official", "radio", "재난방송·행정복지센터 게시판", "공식 안내 (가상)", "official", true,
                    _ => "생활문화센터 대피소는 정상 운영 중입니다. 확인되지 않은 정보에 주의해 주십시오."),
            }),
            new RumorSet("camp_food", "crossroads", new[] { "info_scope", "info_rumor" }, new[]
            {
                new RumorItem("claim", "sns", "SNS 퍼온 글", "사진 없음 · 시각 없음", "rumor", false,
                    _ => "[퍼옴] 생활문화센터 임시 거처 급식 끊김. 가면 굶는다고 함."),
                new RumorItem("seoyeon", "neighbor", "오서연의 메모", "문 앞 쪽지 · 오늘 아침", "neighbor", true,
                    _ => "주민센터에 직접 물어봤어요. 임시 거처 배식은 하루 두 번(12시·18시)이래요. — 303호"),
                new RumorItem("official", "notice", "연무구 안내 (가상)", "행정복지센터 게시판·라디오", "official", true,
                    _ => "임시 거처: 급수·급식 지원, 공용 공간, 개인 공간 협소"),
            }),
            new RumorSet("evac_shortcut", "evac", new[] { "evac_note", "info_rumor", "info_area" }, new[]
            {
                new RumorItem("claim", "sns", "SNS 단체방", "누가 확인했는지 없음", "rumor", false,
                    g => $"{RoadLabel(g)} 풀림! 그쪽으로 가면 집결지까지 지름길이래요."),
                new RumorItem("manager", "neighbor", "박영수 소장", "1층 관리사무소 · 방금 보고 옴", "site", true,
                    g => $"{RoadName(g)} 쪽은 아직 바리케이드가 있어요. 안내 요원 있는 큰길로 가세요."),
                new RumorItem("official", "notice", "연무구 대피 안내 (가상)", "D+12 · 재난문자·게시판", "official", true,
                    _ => "임시 집결지까지는 안내 요원이 있는 권고 경로로 이동하십시오. 통제 구간은 지나갈 수 없습니다."),
            }, road: Shortcut),
        };

        static readonly Dictionary<string, RumorSet> ById = All.ToDictionary(r => r.Id);

        /// <summary>The set with this id. Throws for an unknown id.</summary>
        public static RumorSet Rumor(string id) =>
            id != null && ById.TryGetValue(id, out var r) ? r : throw new ArgumentException($"unknown rumor: {id}");

        static RumorItem ItemOf(RumorSet r, string key) =>
            r.Items.FirstOrDefault(i => i.Key == key) ?? throw new ArgumentException($"rumor {r.Id} has no item {key}");

        /// <summary>The set's items with their text evaluated for this run.</summary>
        public static List<RumorPost> Posts(GameState g, string id) => Rumor(id).Items.Select(it => new RumorPost
        {
            Key = it.Key, Channel = it.Channel, From = it.From, Meta = it.Meta, Label = it.Label, Truth = it.Truth, Text = it.Text(g),
        }).ToList();

        /// <summary>
        /// Judges the notebook label the player gave an item (<paramref name="pick"/>). Ok = the pick matches its real
        /// label, or the player chose 'rumor' (unconfirmed) for a false claim. Records 'rumor_judged' { id, item, pick, ok }.
        /// </summary>
        public static RumorJudgement Judge(GameState g, string id, string pick, string item = "claim")
        {
            item ??= "claim";
            var r = Rumor(id);
            var it = ItemOf(r, item);
            if (Array.IndexOf(GameRules.Labels, pick) < 0) throw new ArgumentException($"unknown info label: {pick}");
            bool ok = pick == it.Label || (!it.Truth && pick == "rumor");
            GameRules.RecordEvent(g, "rumor_judged", new JObject { ["id"] = id, ["item"] = item, ["pick"] = pick, ["ok"] = ok });
            return new RumorJudgement
            {
                Ok = ok, Label = it.Label, Truth = it.Truth, Result = ok ? "sorted" : it.Truth ? "doubted" : "misled", Facts = r.Facts.ToList(),
            };
        }

        /// <summary>
        /// The player acts on the claim. True when the claim was false (know.misled = id).
        /// Records 'rumor_believed' { id, truth }.
        /// </summary>
        public static bool Believe(GameState g, string id)
        {
            var it = ItemOf(Rumor(id), "claim");
            GameRules.RecordEvent(g, "rumor_believed", new JObject { ["id"] = id, ["truth"] = it.Truth });
            if (!it.Truth) g.SetKnow("misled", id);
            return !it.Truth;
        }

        /// <summary>
        /// The player checks the claim: the true items to show. Clears know.misled if it was this id.
        /// Records 'rumor_checked' { id }.
        /// </summary>
        public static List<RumorPost> Verify(GameState g, string id)
        {
            Rumor(id);
            GameRules.RecordEvent(g, "rumor_checked", new JObject { ["id"] = id });
            if (KnownMisled(g) == id) g.SetKnow("misled", null);
            return Posts(g, id).Where(p => p.Truth).ToList();
        }

        /// <summary>know.misled when it is a string, else null.</summary>
        static string KnownMisled(GameState g) =>
            g.Know != null && g.Know.TryGetValue("misled", out var v) ? StringOf(v) : null;

        /// <summary>A JSON string's value; null for anything else (the original compares with ===).</summary>
        static string StringOf(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;

        /// <summary>The id of the false claim the player is acting on, or null.</summary>
        public static string Misled(GameState g)
        {
            var id = KnownMisled(g);
            return id != null && ById.ContainsKey(id) ? id : null;
        }

        /// <summary>
        /// <see cref="Misled"/> once a 'rumor_followed' { id } event exists for it (the story records it at the closed
        /// road), else null.
        /// </summary>
        public static string LostByRumor(GameState g)
        {
            var id = Misled(g);
            return id != null && g.Events.Any(e => e.Type == "rumor_followed" && DetailId(e) == id) ? id : null;
        }

        static string DetailId(GameEvent e) => e.Details != null ? StringOf(e.Details["id"]) : null;

        /// <summary>Rumors judged or believed so far, oldest first, each with its claim (phone SNS list).</summary>
        public static List<SeenRumor> SeenRumors(GameState g)
        {
            var ids = new List<string>();
            foreach (var e in g.Events)
            {
                if (e.Type != "rumor_judged" && e.Type != "rumor_believed") continue;
                var id = DetailId(e);
                if (id != null && ById.ContainsKey(id) && !ids.Contains(id)) ids.Add(id);
            }
            return ids.Select(id => new SeenRumor { Id = id, Claim = Posts(g, id)[0] }).ToList();
        }

        /// <summary>
        /// Every fact a set cites, resolved to its verified text (cards after a judgement).
        /// <paramref name="fact"/> is id -> text or null; facts it does not know are left out.
        /// </summary>
        public static List<string> RumorFacts(string id, Func<string, string> fact) =>
            Rumor(id).Facts.Select(fact).Where(t => !string.IsNullOrEmpty(t)).ToList();
    }
}
