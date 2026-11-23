// Pure logic of the original ui/menu.js (landing, pause, settings, about): texts, button lists, the settings form and
// its submit/cancel result, the about grouping and the landing skyline's layout and timeline. No UnityEngine here,
// so it runs in the EditMode tests and the .NET harness.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AiOn.UI
{
    /// <summary>JavaScript value semantics for option objects that come over as JSON.</summary>
    public static class UiJs
    {
        /// <summary>JS truthiness (undefined/null/false/0/NaN/"" are falsy).</summary>
        public static bool Truthy(JToken t)
        {
            if (t == null) return false;
            switch (t.Type)
            {
                case JTokenType.Null: case JTokenType.Undefined: return false;
                case JTokenType.Boolean: return (bool)t;
                case JTokenType.Integer: case JTokenType.Float: { var d = (double)t; return d != 0 && !double.IsNaN(d); }
                case JTokenType.String: return ((string)t).Length > 0;
                default: return true;
            }
        }

        /// <summary>JS Number(v): null -> 0, undefined -> NaN, booleans 0/1, strings parsed ("" -> 0).</summary>
        public static double Num(JToken t)
        {
            if (t == null || t.Type == JTokenType.Undefined) return double.NaN;
            switch (t.Type)
            {
                case JTokenType.Null: return 0;
                case JTokenType.Boolean: return (bool)t ? 1 : 0;
                case JTokenType.Integer: case JTokenType.Float: return (double)t;
                case JTokenType.String: return NumOf((string)t);
                default: return double.NaN;
            }
        }

        public static double NumOf(string s)
        {
            if (s == null) return 0;
            s = s.Trim();
            if (s.Length == 0) return 0;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
        }

        /// <summary>JS String(n) for a number.</summary>
        public static string NumStr(double d)
        {
            if (double.IsNaN(d)) return "NaN";
            if (double.IsPositiveInfinity(d)) return "Infinity";
            if (double.IsNegativeInfinity(d)) return "-Infinity";
            if (d == Math.Floor(d) && Math.Abs(d) < 1e21) return d == 0 ? "0" : d.ToString("0", CultureInfo.InvariantCulture);
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>JS String(v) / template interpolation.</summary>
        public static string Str(JToken t)
        {
            if (t == null || t.Type == JTokenType.Undefined) return "undefined";
            switch (t.Type)
            {
                case JTokenType.Null: return "null";
                case JTokenType.Boolean: return (bool)t ? "true" : "false";
                case JTokenType.Integer: case JTokenType.Float: return NumStr((double)t);
                case JTokenType.String: return (string)t;
                case JTokenType.Array: return string.Join(",", ((JArray)t).Select(x => x.Type == JTokenType.Null ? "" : Str(x)));
                default: return "[object Object]";
            }
        }

        /// <summary>n.toFixed(digits).</summary>
        public static string Fixed(double d, int digits) => double.IsNaN(d) ? "NaN" : d.ToString("F" + digits, CultureInfo.InvariantCulture);

        /// <summary>a.localeCompare(b, 'ko').</summary>
        public static int KoCompare(string a, string b)
        {
            try { return string.Compare(a ?? "", b ?? "", CultureInfo.GetCultureInfo("ko-KR"), CompareOptions.None); }
            catch (Exception) { return string.CompareOrdinal(a ?? "", b ?? ""); }
        }
    }

    /// <summary>A button of the landing screen or the pause menu.</summary>
    public sealed class MenuButton
    {
        public string Id, Label;
        public bool Primary, Ghost, Autofocus;
        public MenuButton(string id, string label, bool primary = false, bool ghost = false, bool autofocus = false)
        { Id = id; Label = label; Primary = primary; Ghost = ghost; Autofocus = autofocus; }
    }

    public static class MenuLogic
    {
        // ---------- landing ----------

        /// <summary>[no, title, when, line] per chapter (original CHAPTERS).</summary>
        public static readonly string[][] Chapters =
        {
            new[] { "프롤로그", "평소와 다른 화요일", "D-3 화 17:30 · 한결물류 사무실", "확인된 사실, 추정, 소문을 나눕니다." },
            new[] { "1장", "준비", "D-2 ~ D-1 · 한빛아파트와 동네", "여섯 칸의 시간으로 무엇을 준비할까요." },
            new[] { "2장", "정전과 경보", "D-day 금 밤", "불이 꺼지고, 경보가 울립니다." },
            new[] { "3장", "버티기", "D+1 ~ D+3", "정전과 단수, 그리고 이웃." },
            new[] { "4장", "남을까, 떠날까", "D+6", "조건이 바뀌면 답도 바뀝니다." },
            new[] { "5장", "공식 대피", "D+12", "가방 하나와 새로운 길." },
            new[] { "엔딩", "생존 기록", "", "도움이 된 준비와 놓친 정보." },
        };

        public const string LandingEyebrow = "이야기 중심 생존 시뮬레이션 · 가상 동네 연무동";
        public const string LandingTitleKo = "경보 이전";
        public const string LandingTitleEn = "Before the Siren";
        public const string LandingPitch = "전쟁 직전의 준비가, 전쟁 이후의 선택지를 바꿉니다.";
        public const string LandingSub = "사재기가 아니라 정보를 확인하고, 상황에 맞게 판단하고, 이웃과 함께 버티는 이야기입니다.";
        public static readonly string[] LandingLoop = { "정보", "계획", "행동", "결과" };
        public const string LandingChaptersHead = "이야기 구성";
        public const string FictionNote = "이 게임의 동네·인물·회사·재난문자·방송은 모두 가상입니다. 실제 상황이 아닙니다. 안전 행동요령은 국민안전24 등 공식 자료에서 확인한 문장만 출처와 함께 인용합니다. 폭력이나 부상 장면은 나오지 않습니다.";
        public static readonly string[][] LandingKeys = { new[] { "WASD", "이동" }, new[] { "E", "살피기" }, new[] { "Tab", "수첩" }, new[] { "Esc", "메뉴" } };

        /// <summary>The landing buttons in order; ids are the values landing() resolves with (scenes / teacher: the judge
        /// entry, SceneSelect).</summary>
        public static List<MenuButton> LandingButtons(bool hasSave, int endings, int challenges, int challengeTotal)
        {
            var list = new List<MenuButton> { new("new", "새로 시작", primary: true, autofocus: !hasSave) };
            if (hasSave) list.Add(new MenuButton("continue", "이어하기", autofocus: true));
            // judge / classroom entry (competition upgrade): prepared states, never the main save
            list.Add(new MenuButton("scenes", "장면 바로가기", ghost: true));
            list.Add(new MenuButton("teacher", "교사용 30분 모드", ghost: true));
            // 더 알아보기: one entry for the extras (기록관, 연무동 지도와 사람들, 훈련 달력, 우리 집 안전 준비) so the title stays tidy
            list.Add(new MenuButton(AiOn.Game.Archive.Screen, AiOn.Game.Archive.Heading, ghost: true));
            list.Add(new MenuButton("records", $"도전 과제 {challenges}/{challengeTotal}", ghost: true));
            list.Add(new MenuButton("lounge", "대피소 라운지", ghost: true));
            if (endings > 0) list.Add(new MenuButton("gallery", $"엔딩 기록 {endings}/{AiOn.Game.Endings.All.Count}", ghost: true));
            list.Add(new MenuButton("settings", "설정", ghost: true));
            list.Add(new MenuButton("about", "출처와 만든 사람", ghost: true));
            return list;
        }

        /// <summary>landing()'s option object: { hasSave = false, endings = 0, challenges = 0 }.</summary>
        public static (bool hasSave, int endings, int challenges) LandingOptions(JObject o)
        {
            o ??= new JObject();
            bool hasSave = UiJs.Truthy(o["hasSave"]);
            int endings = ToInt(o["endings"]), challenges = ToInt(o["challenges"]);
            return (hasSave, endings, challenges);
        }

        static int ToInt(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return 0;
            var d = UiJs.Num(t);
            return double.IsNaN(d) ? 0 : (int)d;
        }

        // ---------- pause ----------

        public const string PauseEyebrow = "일시정지";
        public const string PauseTitle = "잠시 멈춤";
        public const string PauseConfirmText = "메인 메뉴로 나가면 마지막 체크포인트 이후의 진행은 사라집니다.";

        public static List<MenuButton> PauseButtons() => new()
        {
            new MenuButton("resume", "계속하기", primary: true, autofocus: true),
            new MenuButton("settings", "설정"),
            new MenuButton("save", "체크포인트 저장"),
            new MenuButton("lounge", "대피소 라운지"),
            new MenuButton("menu?", "메인 메뉴로", ghost: true), // opens the confirm box
        };

        /// <summary>The pause key list, from the one key table (UiKeys) so Q/G/C/X/B and later keys show here too.</summary>
        public static readonly string[][] PauseKeys = UiKeys.PauseRows();

        /// <summary>Esc in the pause menu: closes the confirm box if open (null = stay), else resumes.</summary>
        public static string PauseEscape(ref bool confirmOpen)
        {
            if (confirmOpen) { confirmOpen = false; return null; }
            return "resume";
        }

        // ---------- about ----------

        public const string AboutEyebrow = "출처와 만든 사람";
        public const string AboutTitle = "〈경보 이전〉에 대하여";
        public const string AboutFictionHead = "가상의 이야기입니다";
        public static readonly string[] AboutFiction =
        {
            "연무동, 한빛아파트, 한결물류, 바로편의점과 모든 등장인물·사건은 가상입니다. 실제 지역·기관·인물과 관계가 없습니다.",
            "게임 속 재난문자, 재난방송, 라디오 주파수, 통제 구간, 출국 제한 같은 조건은 이야기를 위해 만든 것입니다. 어떤 신호 하나가 실제 위기를 뜻하지는 않습니다.",
            "게임이 알려 주는 안전 행동요령은 아래 공식 자료에서 직접 확인한 문장만 인용합니다. 실제 상황에서는 재난방송, 재난문자, 국민안전24와 지자체 안내를 따르세요.",
        };
        public const string AboutCreditsHead = "만든 사람과 도구";
        public static readonly string[] AboutCredits =
        {
            "기획·개발: 〈경보 이전〉 제작팀 (Claude Code와 함께 제작)",
            "엔진: Unity 6.3 (URP). Three.js로 만든 원작을 옮기고 확장했습니다",
            "인물: Microsoft Rocketbox Avatar Library (MIT, Copyright (c) 2020 Microsoft) · 텍스처를 바꾸고 눈동자를 다시 칠함",
            "소리: \"Oculus Audio Pack 01\" by Oculus VR, LLC, CC BY 4.0 (파일 이름을 바꾸고 OGG로 변환) · Wyzowl sfx-kit (CC0) · felix.blume 바람 소리 (CC0) · Lisa Redfern 여름밤 (퍼블릭 도메인) · 일부 효과음은 직접 합성",
            "텍스처·하늘·소품: Poly Haven, ambientCG, Kenney (CC0)",
            "아이콘: Noto Emoji (Apache 2.0)",
            "글꼴: Pretendard (SIL Open Font License 1.1)",
            "전체 목록과 원본 주소: 저장소의 Assets/_AiOn/ThirdParty/THIRD_PARTY.md",
        };
        public static string AboutSourcesHead(int n) => $"참고한 공식 자료 {n}건";

        public sealed class Source
        {
            public string Publisher, Title, Url, CheckedAt;
            /// <summary>The " · 확인 …" suffix, or null.</summary>
            public string CheckedText => string.IsNullOrEmpty(CheckedAt) ? null : $" · 확인 {CheckedAt}";
        }

        /// <summary>Sources grouped by publisher in first-seen order (original byPub Map).</summary>
        public static List<(string publisher, List<Source> list)> GroupSources(JToken sources, out int total)
        {
            var groups = new List<(string, List<Source>)>();
            var index = new Dictionary<string, int>();
            total = 0;
            if (sources is not JArray arr) return groups;
            total = arr.Count;
            foreach (var t in arr)
            {
                var s = new Source
                {
                    Publisher = t?["publisher"] == null || t["publisher"].Type == JTokenType.Null ? null : UiJs.Str(t["publisher"]),
                    Title = UiJs.Truthy(t?["title"]) ? UiJs.Str(t["title"]) : "",
                    Url = UiJs.Truthy(t?["url"]) ? UiJs.Str(t["url"]) : null,
                    CheckedAt = UiJs.Truthy(t?["checkedAt"]) ? UiJs.Str(t["checkedAt"]) : null,
                };
                // Map keys: a missing publisher (undefined) and null are two groups; el('h4', {}, pub) prints nothing for either
                var key = s.Publisher ?? (t?["publisher"] == null ? "\u0000undefined" : "\u0000null");
                if (!index.TryGetValue(key, out var i)) { i = groups.Count; index[key] = i; groups.Add((s.Publisher ?? "", new List<Source>())); }
                groups[i].Item2.Add(s);
            }
            return groups;
        }

        /// <summary>about()'s sources: { sources: [...] } (a bare array under "sources"; missing = []).</summary>
        public static JToken AboutSources(JObject o) => o?["sources"] as JArray ?? new JArray();

        // ---------- story settings bridge ----------

        /// <summary>The fields of a settings result the story reads (StorySettings): captions, profile ('' for 'none'),
        /// memoryCue, reduceMotion.</summary>
        public static (bool captions, string profile, bool memoryCue, bool reduceMotion) StoryFields(JObject s)
        {
            s ??= new JObject();
            var p = s["profile"]?.Type == JTokenType.String ? (string)s["profile"] : "none";
            if (!SettingsForm.Profiles.Contains(p)) p = "none";
            bool captions = s["captions"] == null || s["captions"].Type == JTokenType.Null ? true : UiJs.Truthy(s["captions"]);
            return (captions, p == "none" ? "" : p, s["memoryCue"]?.Type != JTokenType.Boolean || (bool)s["memoryCue"], UiJs.Truthy(s["reduceMotion"]));
        }
    }

    // ---------- settings ----------

    public sealed class RadioOption { public string Value, Label, Sub; public RadioOption(string v, string l, string s = null) { Value = v; Label = l; Sub = s; } }
    public sealed class RadioGroup { public string Name, Legend, Note; public int Cols; public RadioOption[] Options; }
    public sealed class RangeDef
    {
        public string Name, Label; public int Min, Max, Step = 5;
        /// <summary>The output text for an input value.</summary>
        public Func<int, string> Format;
    }
    public sealed class ToggleDef { public string Name, Label, Sub; }

    /// <summary>
    /// The settings form (original settings(current)): radio groups, ranges (HTML range inputs: integer percent, step 5,
    /// clamped and snapped like the browser does), toggles. Submit() and Cancel() give the exact object the original resolved.
    /// </summary>
    public sealed class SettingsForm
    {
        public static readonly string[] Tiers = { "ultralow", "low", "medium", "high", "ultra" };
        public static readonly Dictionary<string, string[]> TierLabels = new()
        {
            ["ultralow"] = new[] { "완전 낮음", "아주 오래된 노트북" }, ["low"] = new[] { "낮음", "가벼운 노트북 · 내장 그래픽" }, ["medium"] = new[] { "중간", "대부분의 PC" },
            ["high"] = new[] { "높음", "그림자 · 주변광" }, ["ultra"] = new[] { "완전 높음", "고성능 그래픽카드" },
        };
        public static readonly string[] Profiles = { "none", "mobility", "wheelchair", "lowvision", "deaf", "guardian" };

        /// <summary>The experience modes with their one-line subtitles (the settings radio and the first-run card's chips).</summary>
        public static readonly RadioOption[] ProfileOptions =
        {
            new("none", "기본"), new("mobility", "보행 장애", "달리기 불가 · 느린 걸음"), new("wheelchair", "휠체어", "앉은 눈높이 · 계단 불가"),
            new("lowvision", "저시력", "흐린 시야 · 음성 안내"), new("deaf", "청각 장애", "소리 없음 · 자막과 신호"), new("guardian", "보호자 동반", "지체 장애 · 함께 걷기"),
        };

        /// <summary>A profile's subtitle, or null (기본 has none).</summary>
        public static string ProfileSub(string id) => ProfileOptions.FirstOrDefault(o => o.Value == id)?.Sub;

        public const string Eyebrow = "설정";
        public const string Title = "보기·소리·조작·접근성";
        public const string ControlsLegend = "조작";
        public const string SoundLegend = "소리";
        public const string AccessLegend = "접근성";
        public const string VolumeLabel = "전체 음량";

        /// <summary>The defaults, in the original's key order.</summary>
        public static JObject Defaults() => new()
        {
            ["quality"] = "medium", ["volume"] = 0.7, ["mute"] = false, ["captions"] = true, ["reduceMotion"] = false, ["textScale"] = 1,
            ["view"] = "third", ["sensitivity"] = 1, ["minimap"] = true, ["volAmbience"] = 1, ["volEffects"] = 1, ["volUi"] = 1,
            ["profile"] = "none", ["memoryCue"] = true,
            // competition upgrade (after the original's keys): 쉬운 한국어, 도우미 모드
            [AiOn.Game.EasyKorean.SettingKey] = false, [AiOn.Game.Companion.SettingKey] = false,
        };

        /// <summary>The left column's last group (competition upgrade): reading and playing together.</summary>
        public const string TogetherLegend = "쉽게 읽기 · 함께 하기";

        public readonly JObject Cur;
        public readonly List<RadioGroup> LeftRadios;
        public readonly RangeDef Sensitivity, Volume;
        public readonly RangeDef[] BusRanges;
        public readonly ToggleDef Minimap, Mute;
        public readonly ToggleDef[] AccessToggles;
        /// <summary>쉬운 한국어 and 도우미 모드 (TogetherLegend, under the experience modes).</summary>
        public readonly ToggleDef[] TogetherToggles;

        /// <summary>Selected radio value per group (null = none checked, like a value no option matches).</summary>
        public readonly Dictionary<string, string> Radio = new();
        /// <summary>Range input values (integers, the browser-sanitised value).</summary>
        public readonly Dictionary<string, int> Range = new();
        public readonly Dictionary<string, bool> Toggle = new();

        public SettingsForm(JObject current)
        {
            Cur = Defaults();
            if (current != null) foreach (var p in current.Properties()) Cur[p.Name] = p.Value.DeepClone();

            LeftRadios = new List<RadioGroup>
            {
                new() { Name = "quality", Legend = "그래픽 품질", Cols = 3,
                    Options = Tiers.Select(q => new RadioOption(q, TierLabels[q][0], TierLabels[q][1])).ToArray(),
                    Note = $"{(UiJs.Truthy(Cur["qualityAuto"]) ? "처음 실행할 때 이 컴퓨터에 맞춰 골랐어요. " : "")}화면이 자주 멈추면 한 단계씩 자동으로 낮춥니다. 사람 수·조명 수·텍스처는 다음 장소로 이동할 때부터 바뀝니다." },
                new() { Name = "view", Legend = "시점", Options = new[] { new RadioOption("third", "3인칭", "어깨 너머"), new RadioOption("first", "1인칭", "정민의 눈") } },
                new() { Name = "textScale", Legend = "글자 크기", Options = new[] { new RadioOption("0.9", "작게"), new RadioOption("1", "보통"), new RadioOption("1.15", "크게"), new RadioOption("1.3", "아주 크게") } },
                new() { Name = "profile", Legend = "체험 모드", Note = "몸과 감각의 조건에 따라 같은 길도 달라집니다.", Options = ProfileOptions },
            };
            foreach (var g in LeftRadios)
            {
                var v = UiJs.Str(Cur[g.Name]);
                Radio[g.Name] = g.Options.Any(o => o.Value == v) ? v : null;
            }

            Func<int, string> pct = v => $"{v}%";
            Sensitivity = new RangeDef { Name = "sensitivity", Label = "마우스 감도", Min = 30, Max = 300, Format = v => $"{UiJs.Fixed(v / 100.0, 2)}배" };
            Volume = new RangeDef { Name = "volume", Label = VolumeLabel, Min = 0, Max = 100, Format = pct };
            BusRanges = new[]
            {
                new RangeDef { Name = "volAmbience", Label = "환경음 (거리 · 실내)", Min = 0, Max = 100, Format = pct },
                new RangeDef { Name = "volEffects", Label = "효과음 (사이렌 · 불 · 라디오)", Min = 0, Max = 100, Format = pct },
                new RangeDef { Name = "volUi", Label = "버튼 소리", Min = 0, Max = 100, Format = pct },
            };
            foreach (var r in new[] { Sensitivity, Volume }.Concat(BusRanges)) Range[r.Name] = Sanitize(r, Math.Floor(UiJs.Num(Cur[r.Name]) * 100 + 0.5)); // Math.round

            Minimap = new ToggleDef { Name = "minimap", Label = "미니맵 표시", Sub = "오른쪽 위의 작은 지도 (북쪽이 위)" };
            Mute = new ToggleDef { Name = "mute", Label = "소리 끄기", Sub = "사이렌과 경보음도 꺼집니다. 자막은 계속 나옵니다." };
            AccessToggles = new[]
            {
                new ToggleDef { Name = "captions", Label = "자막 표시", Sub = "사이렌·방송·소리를 글로도 보여 줍니다." },
                new ToggleDef { Name = "reduceMotion", Label = "움직임 줄이기", Sub = "화면 흔들림, 머리 흔들림, 깜빡임과 전환 효과를 줄입니다." },
                new ToggleDef { Name = "memoryCue", Label = "기억 알림", Sub = "누군가 내 행동을 기억할 때 짧게 알려 줍니다." },
            };
            TogetherToggles = new[]
            {
                new ToggleDef { Name = AiOn.Game.EasyKorean.SettingKey, Label = AiOn.Game.EasyKorean.Label, Sub = AiOn.Game.EasyKorean.Sub },
                new ToggleDef { Name = AiOn.Game.Companion.SettingKey, Label = AiOn.Game.Companion.Label, Sub = AiOn.Game.Companion.Sub },
            };
            foreach (var t in new[] { Minimap, Mute }.Concat(AccessToggles).Concat(TogetherToggles)) Toggle[t.Name] = UiJs.Truthy(Cur[t.Name]);
            Toggle["memoryCue"] = !(Cur["memoryCue"]?.Type == JTokenType.Boolean && !(bool)Cur["memoryCue"]); // cur.memoryCue !== false
        }

        /// <summary>The browser's value sanitisation for &lt;input type=range&gt;: NaN -> the midpoint, clamp, snap to step from min.</summary>
        public static int Sanitize(RangeDef r, double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) v = r.Min + (r.Max - r.Min) / 2.0;
            v = Math.Max(r.Min, Math.Min(r.Max, v));
            double n = Math.Floor((v - r.Min) / r.Step + 0.5);
            double s = r.Min + n * r.Step;
            if (s > r.Max) s -= r.Step;
            return (int)s;
        }

        public RangeDef RangeOf(string name) => name == "volume" ? Volume : name == "sensitivity" ? Sensitivity : BusRanges.First(b => b.Name == name);
        public string RangeText(string name) => RangeOf(name).Format(Range[name]);
        public void SetRange(string name, double v) => Range[name] = Sanitize(RangeOf(name), v);
        /// <summary>One step up or down (arrow keys on a range input).</summary>
        public void StepRange(string name, int dir) { var r = RangeOf(name); SetRange(name, Range[name] + dir * r.Step); }

        /// <summary>'적용': the object settings() resolved with.</summary>
        public JObject Submit()
        {
            var o = (JObject)Cur.DeepClone();
            var quality = Radio["quality"] != null ? (JToken)Radio["quality"] : Cur["quality"]?.DeepClone() ?? JValue.CreateNull();
            bool sameQ = Cur["quality"] != null && quality.Type == Cur["quality"].Type && JToken.DeepEquals(quality, Cur["quality"]);
            o["quality"] = quality;
            o["qualityAuto"] = sameQ && UiJs.Truthy(Cur["qualityAuto"]);
            o["view"] = Radio["view"] != null ? (JToken)Radio["view"] : Cur["view"]?.DeepClone();
            var ts = Radio["textScale"] == null ? 0 : UiJs.NumOf(Radio["textScale"]);
            o["textScale"] = Clean(ts == 0 || double.IsNaN(ts) ? 1 : ts);
            o["volume"] = Clean(Range["volume"] / 100.0);
            o["mute"] = Toggle["mute"];
            o["captions"] = Toggle["captions"];
            o["reduceMotion"] = Toggle["reduceMotion"];
            var sens = Range["sensitivity"] / 100.0;
            o["sensitivity"] = Clean(sens == 0 ? 1 : sens);
            o["minimap"] = Toggle["minimap"];
            o["memoryCue"] = Toggle["memoryCue"];
            o["profile"] = Radio["profile"] ?? "none";
            o["volAmbience"] = Clean(Range["volAmbience"] / 100.0);
            o["volEffects"] = Clean(Range["volEffects"] / 100.0);
            o["volUi"] = Clean(Range["volUi"] / 100.0);
            foreach (var t in TogetherToggles) o[t.Name] = Toggle[t.Name];
            return o;
        }

        /// <summary>'취소' / Esc: { ...cur }.</summary>
        public JObject Cancel() => (JObject)Cur.DeepClone();

        /// <summary>Whole numbers as integer tokens (JSON.stringify prints 1, not 1.0).</summary>
        static JToken Clean(double d) => d == Math.Floor(d) && Math.Abs(d) < 1e15 ? new JValue((long)d) : new JValue(d);
    }

    // ---------- landing skyline ----------

    /// <summary>
    /// The landing art (original skyline(canvas)): the same seeded layout of far boxes, numbered 15-floor apartment slabs
    /// and villas, the stars and poles, and the 18 s timeline (lit -> blackout wave -> dark -> lights return).
    /// </summary>
    public sealed class Skyline
    {
        public sealed class Window { public double X, Y, W, H, K, Off; public string Col; }
        public sealed class Slab { public double X, Y, W, H; public string Label; public bool Exit; public List<Window> Win = new(); }
        public sealed class Star { public double X, Y, R, K; }

        public static readonly string[] Warm = { "#ffd58a", "#ffe3ad", "#ffc56b", "#f6e7c8", "#bcd7ff" };
        public static readonly string[] LayerColors = { "#141a28", "#0f1420", "#0a0e16" };
        public const double Period = 18000;

        public double Width, Height, Ground;
        public List<Slab>[] Layers = new List<Slab>[3];
        public List<Star> Stars = new();
        public List<double> Poles = new();

        uint _rng;
        /// <summary>The original xorshift (on int32 bits) giving 0..0.99999.</summary>
        public double Rand()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng % 100000) / 100000.0;
        }

        public Skyline(double w, double h)
        {
            Width = w; Height = h;
            _rng = 0x9e3779b9;
            Ground = h * 0.86;
            var far = new List<Slab>();
            for (double x = -20; x < w + 40;)
            {
                double sw = 30 + Rand() * 70, sh = h * (0.08 + Rand() * 0.2);
                far.Add(MakeSlab(x, Ground - sh, sw, sh, Math.Max(2, JsRound(sw / 9)), JsRound(sh / 9), 0.25, 2));
                x += sw + Rand() * 8;
            }
            var mid = new List<Slab>(); int num = 101;
            for (double x = -40 + Rand() * 40; x < w + 60;)
            {
                double sw = 110 + Rand() * 90, sh = Math.Min(h * 0.62, h * (0.4 + Rand() * 0.16));
                int cols = JsRound(sw / 15);
                var s = MakeSlab(x, Ground - sh, sw, sh, cols, 15, 0.55, 4); s.Label = (num++).ToString(CultureInfo.InvariantCulture); s.Exit = true; mid.Add(s);
                x += sw + 30 + Rand() * 70;
            }
            var near = new List<Slab>();
            for (double x = -10; x < w + 40;)
            {
                double sw = 60 + Rand() * 60, sh = h * (0.1 + Rand() * 0.07);
                near.Add(MakeSlab(x, Ground - sh, sw, sh, JsRound(sw / 16), 4, 0.5, 5));
                x += sw + 6 + Rand() * 20;
            }
            Layers[0] = far; Layers[1] = mid; Layers[2] = near;
            for (int i = 0; i < 140; i++) Stars.Add(new Star { X = Rand() * w, Y = Rand() * h * 0.55, R = Rand() * 1.2 + 0.2, K = Rand() });
            for (double x = 60 + Rand() * 80; x < w; x += 230 + Rand() * 80) Poles.Add(x);
        }

        static int JsRound(double v) => (int)Math.Floor(v + 0.5);

        Slab MakeSlab(double x, double y, double w, double h, int cols, int rows, double lit, double pad)
        {
            var s = new Slab { X = x, Y = y, W = w, H = h };
            double cw = (w - pad * 2) / cols, rh = (h - pad * 2) / rows;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (Rand() > lit) continue;
                    var win = new Window { X = x + pad + c * cw + cw * 0.18, Y = y + pad + r * rh + rh * 0.22, W = cw * 0.64, H = rh * 0.5 };
                    win.Col = Warm[(int)Math.Floor(Rand() * Warm.Length)];
                    win.K = Rand(); win.Off = Rand();
                    s.Win.Add(win);
                }
            return s;
        }

        /// <summary>Loop position 0..1 at time now (ms); reduced motion holds 0.2.</summary>
        public static double Phase(double nowMs, bool still) => still ? 0.2 : (nowMs % Period) / Period;

        /// <summary>wave: -1 lit, 0..1 the blackout front, 2 dark, -2 lights coming back.</summary>
        public static double Wave(double p) => p < 0.42 ? -1 : p < 0.52 ? (p - 0.42) / 0.1 : p < 0.84 ? 2 : -2;
        /// <summary>0 lit .. 1 blackout.</summary>
        public static double Dark(double p) => p < 0.42 ? 0 : p < 0.52 ? (p - 0.42) / 0.1 : p < 0.84 ? 1 : 1 - (p - 0.84) / 0.16;

        public bool WindowOn(Window w, double p, double nowMs)
        {
            var wave = Wave(p);
            if (wave == -1) return w.Off > 0.04 || Math.Sin(nowMs / 3000 + w.K * 50) > 0;
            if (wave == -2) return w.K < (p - 0.84) / 0.16;
            if (wave == 2) return false;
            return (w.X / Width) > wave;
        }

        /// <summary>A dark window with an emergency light (white blink) in the blackout.</summary>
        public static bool EmergencyLight(Window w, double dark) => dark > 0.5 && w.K > 0.985;

        /// <summary>The original mix(a, b, k) of two #rrggbb colours, as 0..255 channels.</summary>
        public static (int r, int g, int b) Mix(string a, string b, double k)
        {
            int pa = Convert.ToInt32(a.Substring(1), 16), pb = Convert.ToInt32(b.Substring(1), 16);
            int C(int s) => (int)Math.Floor(((pa >> s) & 255) * (1 - k) + ((pb >> s) & 255) * k + 0.5);
            return (C(16), C(8), C(0));
        }

        /// <summary>Sky gradient stops (top, 55 %, horizon) at darkness k.</summary>
        public static (int r, int g, int b)[] Sky(double dark) => new[]
        {
            Mix("#0d1530", "#05070f", dark), Mix("#2b2a4a", "#0b1020", dark), Mix("#8a4d3f", "#1a1626", dark),
        };
    }
}
