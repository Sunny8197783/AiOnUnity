// 더 알아보기 (competition upgrade, 4단계 세계와 학습): the 기록관 「사건이 제도를 바꿨다」, the 신고 번호 카드 collection,
// the 연무동 map pins and resident profiles, and the 훈련 달력. Pure data and rules; the screens are UI/Screens/ArchiveScreens.cs.
// Every sentence the player reads as history or guidance is a sourced fact (facts.json / facts-ext.json, by id); the
// entry titles, unlock hints and resident profiles are game text about the fictional town.
// What a run unlocked is kept beside the gallery under ArchiveKey: entries and cards only ever add up (any run that is
// the player's own), residents show the latest run, viewed marks what the player has already opened ("새로").
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    /// <summary>What one run shows: its events and deeds, read once.</summary>
    public sealed class ArchiveScan
    {
        public readonly GameState G;
        readonly HashSet<string> _deeds, _events, _chapters;
        /// <summary>The run's deeds in the order they happened.</summary>
        public readonly IReadOnlyList<Deed> DeedList;

        public ArchiveScan(GameState g)
        {
            G = g;
            DeedList = g == null ? new List<Deed>() : Deeds.Of(g);
            _deeds = new HashSet<string>(DeedList.Select(d => d.Id));
            _events = new HashSet<string>(g?.Events.Select(e => e.Type) ?? Enumerable.Empty<string>());
            _chapters = new HashSet<string>(g?.Events.Where(e => e.Type == "chapter_time").Select(e => JsSem.Str(Deeds.Det(e)["id"])).Where(id => id != null)
                ?? Enumerable.Empty<string>());
        }

        public bool Deed(string id) => _deeds.Contains(id);
        public bool Event(string type) => _events.Contains(type);
        /// <summary>The chapter was played to its end (a chapter_time event, partial or not).</summary>
        public bool Cleared(string chapter) => _chapters.Contains(chapter);
        public IEnumerable<string> DeedIds => _deeds;

        /// <summary>The first event of a type whose details pass the test.</summary>
        public bool Any(string type, Func<JObject, bool> test) =>
            G != null && G.Events.Any(e => e.Type == type && test(Deeds.Det(e)));

        public string Know(string key) => G?.Know != null && G.Know.TryGetValue(key, out var v) ? JsSem.Str(v) : null;
        public bool Knows(string key) => G != null && G.Knows(key);
    }

    /// <summary>One 기록관 entry: a real event and the system (or the citizens' record) that followed, all from facts.</summary>
    public sealed class ArchiveEntry
    {
        public string Id, Year, Title;
        /// <summary>"제도" (a system changed), "안내" (guidance and a stated plan followed) or "기록" (what citizens did).</summary>
        public string Kind;
        /// <summary>The chapter it opens in: mid-run, a locked one further on shows Archive.AheadHint instead of its hint.</summary>
        public string Ch;
        /// <summary>ChangeWhen may be null (no date the facts state).</summary>
        public string EventWhen, ChangeWhen, ChangeTitle;
        /// <summary>Fact ids: what happened, then what changed.</summary>
        public string[] EventFacts, ChangeFacts;
        /// <summary>How the story connects (game text, no safety claim).</summary>
        public string InGame;
        /// <summary>How it opens, shown while locked.</summary>
        public string Hint;
        public Func<ArchiveScan, bool> Unlock;
        public IEnumerable<string> FactIds => EventFacts.Concat(ChangeFacts);
    }

    /// <summary>A 신고 번호 카드: earned when the player used that channel rightly in the story.</summary>
    public sealed class NumberCard
    {
        public string Id, Number, Title;
        /// <summary>When to use it: fact ids only.</summary>
        public string[] Facts;
        /// <summary>Where the story taught it (shown once earned) and how to earn it (shown while locked).</summary>
        public string Earned, Hint;
        /// <summary>The first chapter it can be earned in (mid-run spoiler guard, as ArchiveEntry.Ch).</summary>
        public string Ch;
        public Func<ArchiveScan, bool> Unlock;
    }

    /// <summary>A pin on the 연무동 map (TownMap metres: x east, z south).</summary>
    public sealed class MapPin
    {
        public string Id, Label, Kind;
        public double X, Z;
        /// <summary>Drawn at the map's edge with an arrow (the hill path is off the town's map).</summary>
        public bool Edge;
        /// <summary>Where its label goes: "right" (default), "left" or "below" (so neighbouring pins do not cover each other).</summary>
        public string Side = "right";
    }

    /// <summary>A resident of 연무동 (fictional): who, where, and lines that follow the latest run.</summary>
    public sealed class Resident
    {
        public string Id, Name, Role, Profile, Pin;
        /// <summary>The chapter the player first meets them: mid-run, one not met yet and further on stays unnamed.</summary>
        public string Ch;
        /// <summary>Lines from the run, newest knowledge last; null = the resident is not in this run yet.</summary>
        public Func<ArchiveScan, List<string>> Lines;
    }

    /// <summary>The stored progress (and a run's own, before merging).</summary>
    public sealed class ArchiveProgress
    {
        [JsonProperty("v")] public int V = 1;
        [JsonProperty("entries")] public List<string> Entries = new();
        [JsonProperty("cards")] public List<string> Cards = new();
        /// <summary>Resident id -> lines of the latest run that reached them.</summary>
        [JsonProperty("residents")] public Dictionary<string, List<string>> Residents = new();
        /// <summary>Entry and card ids the player has already opened the archive with.</summary>
        [JsonProperty("viewed")] public List<string> Viewed = new();
        /// <summary>루머 해부: the techniques (RumorAnatomy) the player's runs have taken apart, in table order.</summary>
        [JsonProperty("techniques")] public List<string> Techniques = new();
        /// <summary>루머 해부: the posts (RumorAnatomy.All ids) the player's runs took apart, in table order (the examples).</summary>
        [JsonProperty("posts")] public List<string> Posts = new();

        public bool Has(string id) => Entries.Contains(id) || Cards.Contains(id);
        public bool IsNew(string id) => Has(id) && !Viewed.Contains(id);
        public int NewCount => Entries.Concat(Cards).Count(id => !Viewed.Contains(id));

        public string ToJson() => JsonConvert.SerializeObject(this);
        public JObject ToJObject() => JObject.FromObject(this);
    }

    /// <summary>A month on the 훈련 달력 (2026 plan): what is planned then.</summary>
    public sealed class DrillMonth
    {
        public int Month;
        public string Label;
        /// <summary>Who it is for (the plan's 대상 row; train_who_2026 says it in full).</summary>
        public string Who;
        /// <summary>Months the cell covers (10~11월 is one drill in one window).</summary>
        public int Span = 1;
        public bool Main;
    }

    public static class Archive
    {
        public const string Key = "gyeongbo.archive.v1";
        public const string Screen = "more";
        public const string Heading = "더 알아보기";
        /// <summary>Where the hub is in a run, worded the same in every notice (the K terminal is titled 생존 가이드).</summary>
        public const string Path = "생존 가이드 K › " + Heading;

        /// <summary>The chapters in play order (the director's ids).</summary>
        public static readonly string[] ChapterOrder = { "prologue", "prep", "dday", "endure", "crossroads", "evac" };

        /// <summary>The locked hint mid-run for something further on than the chapter being played (no spoilers).</summary>
        public const string AheadHint = "이야기를 이어 가면 열립니다.";

        /// <summary>
        /// Something that opens in chapter ch lies ahead of the chapter being played (current; null = not mid-run, as at
        /// the title, in teacher mode or a scene-select run, where every hint shows).
        /// </summary>
        public static bool Ahead(string ch, string current)
        {
            if (current == null || ch == null) return false;
            int a = Array.IndexOf(ChapterOrder, ch), c = Array.IndexOf(ChapterOrder, current);
            return c >= 0 && a > c;
        }

        public static string HintFor(ArchiveEntry e, string current) => Ahead(e.Ch, current) ? AheadHint : e.Hint;
        public static string HintFor(NumberCard c, string current) => Ahead(c.Ch, current) ? AheadHint : c.Hint;

        static readonly Dictionary<string, string> ChapterNames = new()
        {
            ["prologue"] = "프롤로그", ["prep"] = "1장", ["dday"] = "2장", ["endure"] = "3장", ["crossroads"] = "4장", ["evac"] = "5장",
        };

        // ------------------------------------------------------------------ 기록관: 사건이 제도를 바꿨다

        public static readonly IReadOnlyList<ArchiveEntry> Entries = new[]
        {
            new ArchiveEntry
            {
                Id = "y1968", Year = "1968", Kind = "제도", Ch = "endure", Title = "1·21 사태와 향토예비군",
                EventWhen = "1968. 1. 21", ChangeWhen = "1968. 4. 1", ChangeTitle = "향토예비군 창설",
                EventFacts = new[] { "arch_1968_event" }, ChangeFacts = new[] { "arch_1968_change" },
                InGame = "3장 D+2의 병종사태 문자는 이런 역사 위에 있습니다. 이 게임은 그 장면을 시민의 신고와 실내 대기로만 다룹니다.",
                Hint = "3장 D+2, 뒷산 신고 카드 장면을 지나면 열립니다.",
                Unlock = s => s.Event("sighting") || s.Event("defense_lift"),
            },
            new ArchiveEntry
            {
                Id = "y1975", Year = "1975", Kind = "제도", Ch = "prologue", Title = "민방위의 시작",
                EventWhen = "1975", ChangeWhen = "1975. 7. 25 · 9. 22", ChangeTitle = "민방위기본법 · 민방위대 창설",
                EventFacts = new[] { "arch_1975_event", "arch_1975_why" }, ChangeFacts = new[] { "arch_1975_change" },
                InGame = "정민이 경보를 듣고 대피소로 가는 모든 장면이 이 제도 위에 있습니다.",
                Hint = "프롤로그를 마치면 열립니다.",
                Unlock = s => s.Cleared("prologue"),
            },
            new ArchiveEntry
            {
                Id = "y1996", Year = "1996", Kind = "기록", Ch = "endure", Title = "강릉, 시민의 신고",
                EventWhen = "1996. 9. 18", ChangeWhen = "1996. 9. 18 ~ 19", ChangeTitle = "이어진 주민 신고",
                EventFacts = new[] { "record_1996_taxi", "record_1996_student" }, ChangeFacts = new[] { "record_1996_tips" },
                InGame = "「산에서 내려온 사람들」의 신고 카드는 이 기록에서 왔습니다. 다가가지 않고, 기억하고, 알리는 것.",
                Hint = "3장의 기록 카드 「그날, 시민들은」을 보면 열립니다.",
                Unlock = s => s.Event("record_card"),
            },
            new ArchiveEntry
            {
                Id = "y2010", Year = "2010", Kind = "제도", Ch = "dday", Title = "연평도와 체류형 대피시설",
                EventWhen = "2010. 11", ChangeWhen = null, ChangeTitle = "체류형 주민 대피시설",
                EventFacts = new[] { "arch_2010_event" }, ChangeFacts = new[] { "arch_2010_change" },
                InGame = "대피소는 잠깐 숨는 곳만이 아닙니다. 「임시 거처의 하루」처럼 며칠을 지내는 곳이 되기도 합니다.",
                Hint = "2장을 마치거나 「임시 거처의 하루」를 보내면 열립니다.",
                Unlock = s => s.Cleared("dday") || s.Event(ShelterLifeLogic.Event),
            },
            new ArchiveEntry
            {
                Id = "y2023", Year = "2023", Kind = "제도", Ch = "prep", Title = "22분의 경보와 경보 문자",
                EventWhen = "2023. 5. 31", ChangeWhen = "2023. 8. 23", ChangeTitle = "민방위 경보 발령·전달 규정 개정",
                EventFacts = new[] { "misfire_times", "misfire_app" }, ChangeFacts = new[] { "alert_text_fix", "alert_clarify", "alert_levels" },
                InGame = "「22분」은 그날 아침을 연무동으로 옮긴 장면입니다. 게임 속 경보는 바뀐 규정을 따릅니다.",
                Hint = "1장 「22분」을 겪으면 열립니다.",
                Unlock = s => s.Event(TwentyTwoLogic.Event) || s.Event(TwentyTwoLogic.AfterEvent),
            },
            new ArchiveEntry
            {
                Id = "y2024", Year = "2024", Kind = "안내", Ch = "endure", Title = "정체 모를 물체와 1338",
                EventWhen = "2024. 5", ChangeWhen = "2024. 5. 29", ChangeTitle = "신고 안내 · 경보체계 보완 계획",
                EventFacts = new[] { "arch_2024_event" }, ChangeFacts = new[] { "report_object", "arch_2024_change" },
                InGame = "3장 창밖 잔디밭의 낙하물 장면이 이 안내를 따릅니다. 만지지 않고, 아이들을 멀리 떨어지게 하고, 1338이나 경찰서에.",
                Hint = "3장 D+2, 창밖의 낙하물을 만나면 열립니다.",
                Unlock = s => s.Event("unknown_object"),
            },
        };

        // ------------------------------------------------------------------ 신고 번호 카드

        static bool Object(ArchiveScan s, params string[] reported) => s.Any("unknown_object", d => reported.Contains(JsSem.Str(d["reported"])));

        /// <summary>
        /// The object was told to the police: by the 파출소 (station: true) or, for a deaf player, by a 112 text (false).
        /// The channel is unknown_object's "via"; a save from before it was recorded counts as the station.
        /// </summary>
        static bool ObjectToPolice(ArchiveScan s, bool station) =>
            s.Any("unknown_object", d => JsSem.Is(d["reported"], "police") && (JsSem.Is(d["via"], "text112") != station));

        public static readonly IReadOnlyList<NumberCard> Cards = new[]
        {
            new NumberCard
            {
                Id = "c112", Number = "112", Title = "경찰 긴급신고",
                Facts = new[] { "suspect_report", "report_112_text", "comms_112" },
                Earned = "수상한 사람이나 급한 위험을 경찰에 알렸습니다.",
                Hint = "수상한 일을 112에 알리면 얻습니다.", Ch = "endure",
                Unlock = s => s.Deed("looter_reported") || s.Deed("intercom_police") || ObjectToPolice(s, false)
                    || s.Any("sighting", d => JsSem.IsTrue(d["played"]) && SightingReport.ReachesPolice(JsSem.Str(d["channel"]))),
            },
            new NumberCard
            {
                Id = "c119", Number = "119", Title = "화재 · 구급",
                Facts = new[] { "fire_report", "fire_119", "cpr_point" },
                Earned = "불이 났을 때 119에 알렸거나, 쓰러진 사람 곁에서 주변 사람을 지목해 119 신고를 부탁했습니다.",
                Hint = "불이 났을 때나 사람이 쓰러졌을 때 119를 부르면 얻습니다.", Ch = "dday",
                // the corridor fire's call: from the lobby (fire_report) or from home, inside the 살펴서 대피 screen (lookfirst.report)
                Unlock = s => s.Any("fire_report", d => JsSem.IsTrue(d["played"]) && JsSem.IsTrue(d["place"]))
                    || s.Any("lookfirst", d => d["report"] is JObject r && JsSem.IsTrue(r["played"]) && JsSem.IsTrue(r["place"]))
                    || s.Any("cpr", d => JsSem.IsTrue(d["named"])) || s.Deed("fire_call_clear"),
            },
            new NumberCard
            {
                Id = "c1338", Number = "1338", Title = "군부대 주민신고망",
                Facts = new[] { "report_object", "object_no_touch" },
                Earned = "정체 모를 물체를 만지지 않고 1338에 알렸습니다.",
                Hint = "정체 모를 물체를 만지지 않고 1338에 알리면 얻습니다.", Ch = "endure",
                Unlock = s => Object(s, "1338", "office1338"),
            },
            new NumberCard
            {
                Id = "cPolice", Number = "경찰서", Title = "가까운 경찰서 · 파출소",
                Facts = new[] { "report_object", "info_suspect" },
                Earned = "정체 모를 물체의 위치를 가까운 파출소에 알렸습니다.",
                Hint = "정체 모를 물체를 가까운 경찰서에 알리면 얻습니다.", Ch = "endure",
                Unlock = s => ObjectToPolice(s, true),
            },
            new NumberCard
            {
                Id = "cOffice", Number = "관리사무소", Title = "단지 관리사무소",
                Facts = new[] { "ext_manage" },
                Earned = "단지 안의 일을 관리사무소에 먼저 알리고 확인했습니다.",
                Hint = "소화기, 304호 어르신, 현관 앞 낯선 사람 일을 관리사무소에 알리면 얻습니다.", Ch = "prep",
                Unlock = s => s.Deed("intercom_checked") || s.Deed("elder_alert_told")
                    || s.Any(ExtinguisherCheck.Event, d => JsSem.Is(d["action"], "report")),
            },
        };

        // ------------------------------------------------------------------ 연무동 지도와 사람들

        public static readonly IReadOnlyList<MapPin> Pins = new[]
        {
            new MapPin { Id = "home", Label = "한빛아파트 103동", Kind = "home", X = 88, Z = 36, Side = "left" },
            new MapPin { Id = "office", Label = "관리사무소", Kind = "civic", X = 121, Z = 35, Side = "below" },
            new MapPin { Id = "store", Label = "바로편의점", Kind = "shop", X = 36, Z = 17 },
            new MapPin { Id = "mart", Label = "연무마트", Kind = "shop", X = -16, Z = -37 },
            new MapPin { Id = "work", Label = "한결물류 연무센터", Kind = "work", X = 87, Z = -35 },
            new MapPin { Id = "water", Label = "비상급수시설", Kind = "water", X = -80, Z = -17 },
            new MapPin { Id = "center", Label = "행정복지센터", Kind = "civic", X = -89, Z = -42 },
            new MapPin { Id = "shelter", Label = "대피소 · 임시 거처", Kind = "shelter", X = -91, Z = 31 },
            new MapPin { Id = "truck", Label = "급수차 (출입구 B)", Kind = "water", X = -80, Z = 52 },
            new MapPin { Id = "road", Label = "연무로 통제 구간", Kind = "road", X = -10, Z = 0 },
            new MapPin { Id = "hill", Label = "뒷산 산책로 · 약수터 갈림길", Kind = "hill", X = -50, Z = -108, Edge = true },
        };

        static List<string> DeedLines(ArchiveScan s, string axis, ICollection<string> only = null)
        {
            var lines = new List<string>();
            foreach (var d in s.DeedList)
            {
                if (only != null && !only.Contains(d.Id)) continue;
                var def = Deeds.Get(d.Id);
                if (def?.W != null && def.W.TryGetValue(axis, out var w) && w != 0 && !string.IsNullOrEmpty(def.Short)) lines.Add(def.Short);
            }
            return lines;
        }

        static List<string> Last(List<string> lines, int n = 3) => lines.Count <= n ? lines : lines.Skip(lines.Count - n).ToList();

        public static readonly IReadOnlyList<Resident> Residents = new[]
        {
            new Resident
            {
                Id = "jeongmin", Ch = "prologue", Name = "정민", Role = "103동 302호 · 한결물류 연무센터 운영팀", Pin = "home",
                Profile = "이 이야기의 주인공. 평소에는 회사에서 배송 숫자를 확인하던 사람입니다.",
                Lines = s =>
                {
                    var done = ChapterNames.Where(c => s.Cleared(c.Key)).Select(c => c.Value).ToList();
                    if (done.Count == 0) return null;
                    var l = new List<string> { "지나온 장: " + string.Join(" · ", done) };
                    if (s.Cleared("evac")) l.Add("공식 대피까지 마치고 끝에 닿았다");
                    return l;
                },
            },
            new Resident
            {
                Id = "seoyeon", Ch = "prep", Name = "오서연", Role = "103동 303호 · 옆집 이웃", Pin = "home",
                Profile = "보행 보조기를 쓰는 이웃. 비상시에는 문자가 제일 편하다고 했습니다.",
                Lines = s => { var l = DeedLines(s, "seoyeon"); return l.Count == 0 ? null : Last(l); },
            },
            new Resident
            {
                Id = "elder", Ch = "prep", Name = "304호 어르신", Role = "103동 304호", Pin = "home",
                Profile = "계단을 천천히 오르내리는 이웃. 경보가 울리면 문을 두드려 달라고 했습니다.",
                Lines = s =>
                {
                    var l = new List<string>();
                    switch (s.Know("elderPick"))
                    {
                        case "help": l.Add("경보 때 정민과 함께 계단을 내려왔다"); break;
                        case "manager": l.Add(s.Knows("elderTold") ? "경보 때 박 소장과 함께 내려왔다" : "경보 때 기다리다 스스로 내려왔다"); break;
                        case "go": l.Add("경보 때 자기 걸음으로 내려왔다"); break;
                    }
                    if (s.Any(ElderPovLogic.Event, d => d["played"] == null || JsSem.IsTrue(d["played"])))
                        l.Add("그 계단을 어르신의 걸음으로 다시 걸어 보았다");
                    // the stairs at the alert are said once, by the line above; the deeds add the rest (newest last)
                    var more = s.DeedList.Where(d => l.Count == 0 || Deeds.Get(d.Id)?.Ch != "dday").Select(d => d.Id).ToList();
                    var deeds = DeedLines(s, "elder", more);
                    if (l.Count == 0) return deeds.Count == 0 ? null : Last(deeds);
                    l.AddRange(Last(deeds, 3 - l.Count));
                    return l;
                },
            },
            new Resident
            {
                Id = "manager", Ch = "prep", Name = "박영수 소장", Role = "한빛아파트 관리사무소", Pin = "office",
                Profile = "단지 관리사무소장. 정전, 불, 낯선 물체가 생길 때마다 1층 창구를 지킵니다.",
                Lines = s => { var l = DeedLines(s, "manager"); return l.Count == 0 ? null : Last(l); },
            },
            new Resident
            {
                Id = "clerk", Ch = "prep", Name = "이수아", Role = "바로편의점 점원", Pin = "store",
                Profile = "단지 앞 편의점을 지키는 점원. 정전 뒤에는 가게를 어떻게 할지 고민합니다.",
                Lines = s => { var l = DeedLines(s, "clerk"); return l.Count == 0 ? null : Last(l); },
            },
            new Resident
            {
                Id = "yoon", Ch = "prologue", Name = "윤 팀장과 동료들", Role = "한결물류 연무센터 운영팀", Pin = "work",
                Profile = "D-3 저녁, 같은 대시보드와 같은 소문을 함께 보던 사람들입니다.",
                Lines = s => s.Cleared("prologue") ? new List<string> { "D-3 사무실에서 신호를 사실·추정·소문으로 나눴다" } : null,
            },
            new Resident
            {
                Id = "guide", Ch = "dday", Name = "안내 요원", Role = "연무로 통제 구간", Pin = "road",
                Profile = "통제된 길목에서 돌아갈 길을 알려 주는 현장 요원입니다.",
                Lines = s =>
                {
                    var l = new List<string>();
                    if (s.Knows("closureSeen")) l.Add("2장, 통제 구간에서 돌아갈 길을 들었다");
                    if (s.Any("rumor_checked", d => JsSem.Is(d["id"], "evac_shortcut"))) l.Add("5장, 단체방 글 대신 공식 통제 안내를 확인했다");
                    if (s.Any("rumor_followed", d => JsSem.Is(d["id"], "evac_shortcut"))) l.Add("5장, 소문 속 지름길에서 큰길로 돌아왔다");
                    return l.Count == 0 ? null : l;
                },
            },
            new Resident
            {
                Id = "jiwoo", Ch = "crossroads", Name = "지우네", Role = "임시 거처 옆 돗자리", Pin = "shelter",
                Profile = "임시 거처 첫날 밤 옆자리에 짐을 푼 엄마와 아이 지우입니다.",
                Lines = s =>
                {
                    var l = new List<string>();
                    if (s.Deed("shelter_child_care")) l.Add("엄마가 급수 줄에 간 동안 정민이 지우를 봐 주었다");
                    if (s.Deed("shelter_child_referred")) l.Add("정민이 아이 돌봄 자원봉사 자리를 알려 드렸다");
                    if (l.Count == 0 && s.Any(ShelterLifeLogic.Event, d => JsSem.IsTrue(d["played"]))) l.Add("임시 거처에서 옆 돗자리에 지냈다");
                    return l.Count == 0 ? null : l;
                },
            },
        };

        // ------------------------------------------------------------------ 훈련 달력 (2026 plan; the dates change every year)

        public static readonly IReadOnlyList<DrillMonth> DrillMonths = new[]
        {
            new DrillMonth { Month = 3, Label = "대비태세 점검", Who = "정부·지자체 등" },
            new DrillMonth { Month = 5, Label = "공습대비 대피훈련", Who = "지자체·학교 등" },
            new DrillMonth { Month = 8, Label = "을지연습 · 공습대비 훈련", Who = "전 국민", Main = true },
            new DrillMonth { Month = 10, Span = 2, Label = "재난대비 훈련 (1회)", Who = "정부·지자체 등" },
        };

        public static readonly string[] CalendarFacts = { "train_plan_2026", "train_who_2026", "train_ulchi", "train_drill_2026", "alert_levels" };
        public const string CalendarNote = "훈련 날짜는 해마다 바뀝니다. 올해 일정은 매년 행정안전부 공지와 사는 곳의 시·군·구 안내로 확인하세요.";

        // ------------------------------------------------------------------ rules

        /// <summary>Every fact id the archive, the cards and the calendar quote (all must exist).</summary>
        public static IEnumerable<string> AllFactIds =>
            Entries.SelectMany(e => e.FactIds).Concat(Cards.SelectMany(c => c.Facts)).Concat(CalendarFacts).Distinct();

        /// <summary>What one run unlocked, and its residents' lines.</summary>
        public static ArchiveProgress Scan(GameState g)
        {
            var p = new ArchiveProgress();
            if (g == null) return p;
            var s = new ArchiveScan(g);
            foreach (var e in Entries) if (e.Unlock(s)) p.Entries.Add(e.Id);
            foreach (var c in Cards) if (c.Unlock(s)) p.Cards.Add(c.Id);
            p.Techniques = RumorAnatomy.Seen(g);
            p.Posts = RumorAnatomy.PostsShown(g);
            foreach (var r in Residents)
            {
                var lines = r.Lines(s);
                if (lines != null && lines.Count > 0) p.Residents[r.Id] = lines;
            }
            return p;
        }

        /// <summary>The stored progress, sanitised: unknown ids and anything malformed are dropped.</summary>
        public static ArchiveProgress Read(string raw) => Read(JsSem.Parse(raw));

        public static ArchiveProgress Read(JToken raw)
        {
            var o = raw as JObject;
            var p = new ArchiveProgress();
            if (o == null) return p;
            var known = new HashSet<string>(Entries.Select(e => e.Id).Concat(Cards.Select(c => c.Id)));
            List<string> Ids(string key, Func<string, bool> ok) =>
                (o[key] as JArray)?.Select(JsSem.Str).Where(id => id != null && ok(id)).Distinct().ToList() ?? new List<string>();
            p.Entries = Ids("entries", id => Entries.Any(e => e.Id == id));
            p.Cards = Ids("cards", id => Cards.Any(c => c.Id == id));
            p.Viewed = Ids("viewed", known.Contains);
            p.Techniques = Ids("techniques", id => RumorAnatomy.Technique(id) != null);
            p.Techniques = RumorAnatomy.Techniques.Select(t => t.Id).Where(p.Techniques.Contains).ToList();
            var posts = Ids("posts", id => RumorAnatomy.Of(id) != null);
            p.Posts = RumorAnatomy.All.Select(a => a.Id).Where(posts.Contains).ToList();
            if (o["residents"] is JObject rs)
                foreach (var prop in rs.Properties())
                    if (Residents.Any(r => r.Id == prop.Name) && prop.Value is JArray a)
                    {
                        var lines = a.Select(JsSem.Str).Where(t => !string.IsNullOrEmpty(t)).Take(6).ToList();
                        if (lines.Count > 0) p.Residents[prop.Name] = lines;
                    }
            return p;
        }

        /// <summary>
        /// Stored + a run: entries and cards add up (in table order), each resident the run reached takes the run's lines
        /// (the latest run that met them), the others keep theirs, viewed stays.
        /// </summary>
        public static ArchiveProgress Merge(ArchiveProgress stored, ArchiveProgress run)
        {
            stored ??= new ArchiveProgress();
            run ??= new ArchiveProgress();
            var p = new ArchiveProgress
            {
                Entries = Entries.Select(e => e.Id).Where(id => stored.Entries.Contains(id) || run.Entries.Contains(id)).ToList(),
                Cards = Cards.Select(c => c.Id).Where(id => stored.Cards.Contains(id) || run.Cards.Contains(id)).ToList(),
                Viewed = stored.Viewed.ToList(),
                Techniques = RumorAnatomy.Techniques.Select(t => t.Id).Where(id => stored.Techniques.Contains(id) || run.Techniques.Contains(id)).ToList(),
                Posts = RumorAnatomy.All.Select(a => a.Id).Where(id => stored.Posts.Contains(id) || run.Posts.Contains(id)).ToList(),
            };
            foreach (var r in Residents)
            {
                var from = run.Residents.TryGetValue(r.Id, out var mine) ? mine : stored.Residents.TryGetValue(r.Id, out var old) ? old : null;
                if (from != null) p.Residents[r.Id] = from.ToList();
            }
            return p;
        }

        /// <summary>Everything unlocked so far is now seen (the "새로" marks go).</summary>
        public static ArchiveProgress MarkViewed(ArchiveProgress p)
        {
            p ??= new ArchiveProgress();
            foreach (var id in p.Entries.Concat(p.Cards)) if (!p.Viewed.Contains(id)) p.Viewed.Add(id);
            return p;
        }

        /// <summary>Every entry and card open (the "모두 펼쳐 보기" for a class or a judge; not stored).</summary>
        public static ArchiveProgress RevealAll(ArchiveProgress p)
        {
            var all = Merge(p, null);
            all.Entries = Entries.Select(e => e.Id).ToList();
            all.Cards = Cards.Select(c => c.Id).ToList();
            all.Techniques = RumorAnatomy.Techniques.Select(t => t.Id).ToList();
            all.Viewed = all.Entries.Concat(all.Cards).ToList();
            return all;
        }

        /// <summary>The neighbours (정민 himself is not one) the player's stories have met so far.</summary>
        public static int NeighborsMet(ArchiveProgress p) => Residents.Count(r => r.Id != "jeongmin" && p.Residents.ContainsKey(r.Id));
        public static int NeighborCount => Residents.Count(r => r.Id != "jeongmin");

        /// <summary>The entries and cards b has that a did not (in table order).</summary>
        public static List<string> Gained(ArchiveProgress a, ArchiveProgress b) =>
            b.Entries.Concat(b.Cards).Where(id => !a.Has(id)).ToList();

        /// <summary>The in-story notice for unlocks just earned, or null: "새로 열림 · 신고 번호 카드 1338 · …".</summary>
        public static string Notice(IEnumerable<string> ids)
        {
            var names = (ids ?? Enumerable.Empty<string>())
                .Select(id => Card(id) is NumberCard c ? $"신고 번호 카드 {c.Number}" : Entry(id) is ArchiveEntry e ? $"기록관 「{e.Year} {e.Title}」" : null)
                .Where(n => n != null).ToList();
            return names.Count == 0 ? null : $"새로 열림 · {string.Join(", ", names)}  ({Path})";
        }

        /// <summary>"기록 2/6 · 번호 카드 1/5".</summary>
        public static string CountLine(ArchiveProgress p) =>
            $"기록 {p.Entries.Count}/{Entries.Count} · 번호 카드 {p.Cards.Count}/{Cards.Count}";

        public static ArchiveEntry Entry(string id) => Entries.FirstOrDefault(e => e.Id == id);
        public static NumberCard Card(string id) => Cards.FirstOrDefault(c => c.Id == id);
        public static Resident ResidentById(string id) => Residents.FirstOrDefault(r => r.Id == id);
        public static MapPin Pin(string id) => Pins.FirstOrDefault(p => p.Id == id);
    }
}
