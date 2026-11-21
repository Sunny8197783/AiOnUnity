// 우리 집 안전 준비 (competition upgrade, 4단계 "세계와 학습"): three things the player takes out of the game.
//  - FamilyCard: 우리 가족 비상연락카드. The fields gather the preparation steps of 국민안전24 「비상사태 행동요령」 (contact_fields,
//    contact_places, contact_meet, shelter_apps; there is no official template, so the layout is the game's own and says so). What the
//    player types lives only in the open screen: it is never put in the game save or any file but the PNG they ask for,
//    and nothing is uploaded.
//  - HomeChecklist: 우리 집 안전 점검표, every item backed by checked facts (facts.json + facts-ext.json).
//  - TrainingGuide: where to find real CPR / fire training, by agency name and site address only (no addresses,
//    schedules or fees: those change and were not verified).
//  - PrintLayout: the A6 print layout (300 dpi) shared by the on-screen preview and the saved PNG.
// Pure logic, no UnityEngine; the screens are UI/Screens/HomePrepScreens.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AiOn.Game
{
    public static class FamilyCard
    {
        public const string Screen = "homeprep";

        public const string NotSaved = "이 카드는 저장되지 않습니다. 이미지로만 내려받습니다.";
        public const string NotSavedMore = "게임은 적은 내용을 저장하거나 인터넷으로 보내지 않습니다. 창을 닫으면 지워집니다.";
        public const string NotOfficial = "국민안전24 「비상사태 행동요령」의 준비 항목(비상연락카드, 집·직장·학교 연락처, 만남의 장소, 가까운 대피소)을 모아 이 게임이 만든 양식입니다. 정부 공식 서식은 아닙니다.";
        public const string CardFootnote = "국민안전24 「비상사태 행동요령」을 바탕으로 게임 「경보 이전」이 만든 카드 · 공식 서식 아님";
        /// <summary>The line under the card's title: why it is on paper (contact_card), not a promise the paper cannot keep.</summary>
        public const string CardLead = "통신이 끊길 때를 위해 미리 적어 생존배낭에 넣어 둡니다";
        /// <summary>The fact printed under the hotlines (it is about them, so it sits with them, not in the small print).</summary>
        public const string HotlineNoteFact = "comms_112";

        /// <summary>The facts the card screen shows (what to write, why, and who to share it with).</summary>
        public static readonly string[] Facts = { "contact_card", "contact_fields", "contact_places", "contact_meet", "contact_prescription", "contact_share" };

        public enum Kind { Text, Phone }

        public sealed class Field
        {
            public readonly string Id, Section, Label, Hint;
            public readonly int Max;
            public readonly Kind Kind;
            /// <summary>A phone field's owner in messages ("가족 1", "집"): "가족 1 전화번호를 …".</summary>
            public readonly string Owner;
            public Field(string id, string section, string label, string hint, int max, Kind kind = Kind.Text, string owner = null)
            { Id = id; Section = section; Label = label; Hint = hint; Max = max; Kind = kind; Owner = owner; }
        }

        /// <summary>Card sections in print order: id and heading.</summary>
        public static readonly (string id, string title)[] Sections =
        {
            ("me", "나"), ("family", "가족 연락처"), ("places", "집·직장·학교"), ("meet", "만남의 장소"), ("shelter", "가까운 대피소"), ("hotline", "신고·구호"),
        };

        /// <summary>
        /// Every field, in tab order. Max is what fits its box on the A6 card (PrintLayout.Card). Hints are generic examples,
        /// never the game's fictional town.
        /// </summary>
        public static readonly Field[] Fields =
        {
            new("name", "me", "이름", "", 12),
            new("blood", "me", "혈액형", "예: A형 Rh+", 10),
            new("meds", "me", "복용약·지병", "예: 혈압약(아침 1알)", 30),
            new("fam1", "family", "가족 1 · 이름(관계)", "예: 홍길순(엄마)", 12),
            new("fam1Phone", "family", "전화", "010-0000-0000", 16, Kind.Phone, "가족 1"),
            new("fam2", "family", "가족 2 · 이름(관계)", "", 12),
            new("fam2Phone", "family", "전화", "", 16, Kind.Phone, "가족 2"),
            new("fam3", "family", "가족 3 · 이름(관계)", "", 12),
            new("fam3Phone", "family", "전화", "", 16, Kind.Phone, "가족 3"),
            // contact_places: 집, 직장, 학교 등의 연락처, one number each
            new("placeHome", "places", "집 전화", "02-000-0000", 16, Kind.Phone, "집"),
            new("placeWork", "places", "직장 전화", "", 16, Kind.Phone, "직장"),
            new("placeSchool", "places", "학교 전화", "", 16, Kind.Phone, "학교"),
            new("meet1", "meet", "1차", "예: 아파트 정문 앞", 26),
            new("meet2", "meet", "2차", "예: 가까운 초등학교 운동장", 26),
            new("meet3", "meet", "3차", "예: 가까운 민방위 대피소", 26),
            new("shelter", "shelter", "가까운 민방위 대피소", "지도앱에서 '민방위 대피소' 검색", 26),
        };

        public static Field Get(string id) => Fields.FirstOrDefault(f => f.Id == id);

        /// <summary>신고·구호기관 연락처 printed on every card (119·112: comms_112, fire_report; 1338: report_object).</summary>
        public static readonly (string number, string what)[] Hotlines =
        {
            ("119", "화재·구급"), ("112", "경찰"), ("1338", "낯선 물체·전단 (군부대 주민신고망)"),
        };
        public static readonly string[] HotlineFacts = { "comms_112", "fire_report", "report_object" };

        /// <summary>One line, no control characters, runs of spaces made one, trimmed, at most max characters.</summary>
        public static string Clean(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            bool space = false;
            foreach (char c in s)
            {
                bool ws = char.IsWhiteSpace(c) || char.IsControl(c);
                if (ws) { if (!space && sb.Length > 0) sb.Append(' '); space = true; continue; }
                sb.Append(c); space = false;
            }
            var t = sb.ToString().Trim();
            if (max > 0 && t.Length > max)
            {
                t = t.Substring(0, max);
                if (char.IsHighSurrogate(t[t.Length - 1])) t = t.Substring(0, t.Length - 1); // never half an emoji
                t = t.TrimEnd();
            }
            return t;
        }

        /// <summary>The characters a phone field takes as they are typed: digits and - + ( ) space.</summary>
        public static bool PhoneChar(char c) => c >= '0' && c <= '9' || c == '-' || c == '+' || c == '(' || c == ')' || c == ' ';

        public const int PhoneMinDigits = 7, PhoneMaxDigits = 15;

        /// <summary>A phone number as written: empty (fine: the card can be filled in by hand), or only phone characters with 7~15 digits.</summary>
        public static bool IsPhone(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return true;
            if (!s.All(PhoneChar)) return false;
            int digits = s.Count(char.IsDigit);
            return digits >= PhoneMinDigits && digits <= PhoneMaxDigits;
        }

        /// <summary>What is wrong with one field's value, or null.</summary>
        public static string Problem(Field f, string value)
        {
            if (f == null) return null;
            if (f.Kind == Kind.Phone && !IsPhone(value)) return $"전화번호를 {PhoneMinDigits}~{PhoneMaxDigits}자리 숫자로 적어 주세요";
            if (f.Max > 0 && (value ?? "").Length > f.Max) return $"{f.Max}자까지 들어갑니다";
            return null;
        }

        static string Value(IReadOnlyDictionary<string, string> v, string id) => v != null && v.TryGetValue(id, out var s) ? (s ?? "").Trim() : "";

        /// <summary>
        /// Every problem in field order: "가족 1 전화번호를 …" for a phone, "label: message" otherwise. skip: the field being
        /// typed in (a half-typed number is not an error yet; CanSave still checks every field).
        /// </summary>
        public static List<string> Problems(IReadOnlyDictionary<string, string> values, string skip = null)
        {
            var list = new List<string>();
            foreach (var f in Fields)
            {
                if (skip != null && f.Id == skip) continue;
                var p = Problem(f, Value(values, f.Id));
                if (p == null) continue;
                bool phone = f.Kind == Kind.Phone && !string.IsNullOrEmpty(f.Owner) && p.StartsWith("전화번호를", StringComparison.Ordinal);
                list.Add(phone ? $"{f.Owner} {p}" : $"{f.Label}: {p}");
            }
            return list;
        }

        /// <summary>What the official guidance asks for that is still blank: a family phone, the first meeting place, the shelter.</summary>
        public static List<string> Missing(IReadOnlyDictionary<string, string> values)
        {
            var list = new List<string>();
            if (!new[] { "fam1Phone", "fam2Phone", "fam3Phone" }.Any(id => Value(values, id).Length > 0)) list.Add("가족 전화");
            if (Value(values, "meet1").Length == 0) list.Add("1차 만남의 장소");
            if (Value(values, "shelter").Length == 0) list.Add("가까운 대피소");
            return list;
        }

        /// <summary>A phone written without a name beside it (who is this number?).</summary>
        public static List<int> PhonesWithoutName(IReadOnlyDictionary<string, string> values)
        {
            var rows = new List<int>();
            for (int i = 1; i <= 3; i++)
                if (Value(values, $"fam{i}Phone").Length > 0 && Value(values, $"fam{i}").Length == 0) rows.Add(i);
            return rows;
        }

        /// <summary>The status line under the form: a problem first, then what is blank, else ready. skip: as in Problems.</summary>
        public static string Status(IReadOnlyDictionary<string, string> values, string skip = null)
        {
            var problems = Problems(values, skip);
            if (problems.Count > 0) return problems[0];
            var nameless = PhonesWithoutName(values);
            if (nameless.Count > 0) return $"가족 {nameless[0]}의 이름이나 관계도 적어 주세요";
            var missing = Missing(values);
            if (missing.Count == 0) return "다 적었습니다. 이미지로 저장해 인쇄하거나 휴대폰에 넣어 두세요.";
            if (Fields.All(f => Value(values, f.Id).Length == 0)) return "빈 카드로 저장해 인쇄한 뒤 손으로 적어도 됩니다.";
            return "아직 빈 칸 · " + string.Join(", ", missing) + " (빈칸은 인쇄해서 손으로 적어도 됩니다)";
        }

        /// <summary>Saving is refused only for a malformed phone number (a blank card is a fine card to print).</summary>
        public static bool CanSave(IReadOnlyDictionary<string, string> values) => Problems(values).Count == 0;

        // ------------------------------------------------------------ files (local only)

        public const string CardStem = "family-card", ChecklistStem = "home-checklist";
        public const string PicturesFolder = "경보 이전";

        /// <summary>The PNG's file name: what it is and when, never what was written on it (no names in file names).</summary>
        public static string FileName(string stem, DateTime nowLocal) => $"{stem}-{nowLocal.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.png";

        /// <summary>name in dir, or name-2, name-3 … when taken.</summary>
        public static string UniquePath(string dir, string name, Func<string, bool> exists)
        {
            string path = Path.Combine(dir, name);
            if (exists == null || !exists(path)) return path;
            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            for (int i = 2; i < 1000; i++)
            {
                path = Path.Combine(dir, $"{stem}-{i}{ext}");
                if (!exists(path)) return path;
            }
            return path;
        }

        /// <summary>
        /// Where the image goes: the user's Pictures folder, "경보 이전" inside it; else the game's own data folder. A Pictures
        /// folder that a cloud drive syncs (OneDrive folder backup moves it under the OneDrive folder) is passed over: the
        /// card holds phone numbers and medicines, and the game promises it goes nowhere. synced says why it was passed over.
        /// </summary>
        public static (string dir, bool pictures, bool synced) SaveFolder(string picturesDir, string gameDataDir, IEnumerable<string> syncRoots = null)
        {
            bool has = !string.IsNullOrWhiteSpace(picturesDir);
            bool synced = has && IsSynced(picturesDir, syncRoots);
            if (has && !synced) return (Path.Combine(picturesDir, PicturesFolder), true, false);
            return (Path.Combine(gameDataDir ?? "", "cards"), false, synced);
        }

        /// <summary>
        /// The folder is inside a cloud-synced folder: under one of syncRoots (%OneDrive%, %OneDriveConsumer%,
        /// %OneDriveCommercial%), or a folder on its path is named OneDrive… (e.g. "OneDrive - 회사").
        /// </summary>
        public static bool IsSynced(string dir, IEnumerable<string> syncRoots)
        {
            if (string.IsNullOrWhiteSpace(dir)) return false;
            string d = NormDir(dir);
            if (syncRoots != null)
                foreach (var r in syncRoots)
                    if (!string.IsNullOrWhiteSpace(r) && d.StartsWith(NormDir(r), StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var part in d.Split('/'))
                if (part.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string NormDir(string p) => p.Trim().Replace('\\', '/').TrimEnd('/') + "/";

        public const string SyncedNote = "사진 폴더가 클라우드와 동기화되어 있어, 이 컴퓨터 안의 게임 기록 폴더에 두었습니다.";

        /// <summary>The saved line: the folder by name and the file name, not the full path (it holds the account name).</summary>
        public static string SavedLine(string path, bool pictures, bool synced = false) =>
            $"저장했습니다 · {(pictures ? "사진 폴더 › " + PicturesFolder : "게임 기록 폴더 › cards")} › {Path.GetFileName(path ?? "")}"
            + (synced ? "\n" + SyncedNote : "");

        /// <summary>What low-vision speech says after a save: the same place the line names.</summary>
        public static string SavedSpoken(string what, bool pictures) =>
            $"{what} 이미지를 {(pictures ? "사진 폴더의 " + PicturesFolder + " 폴더" : "게임 기록 폴더")}에 저장했습니다.";

        public static string DateLine(DateTime nowLocal) => nowLocal.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
    }

    /// <summary>우리 집 안전 점검표: what to look at in one's real home, each item with the facts behind it.</summary>
    public static class HomeChecklist
    {
        /// <summary>Todo 아직, Done 확인함 (looked, fine), Fix 고칠 것 (looked, needs fixing), Absent 우리 집엔 없음.</summary>
        public enum Mark { Todo, Done, Absent, Fix }

        public const string Title = "우리 집 안전 점검표";
        public const string Subtitle = "피난시설 · 소화기 · 대피소 · 비상용품";

        public sealed class Item
        {
            public readonly string Id, Title, How, Where;
            public readonly string[] Facts;
            /// <summary>The thing may not exist in every home (an item about a 경량칸막이 when there is none).</summary>
            public readonly bool CanBeAbsent;
            /// <summary>Looking can find something to fix (a blocked 경량칸막이, a needle out of the green).</summary>
            public readonly bool CanFix;
            public Item(string id, string title, string how, string where, bool canBeAbsent, bool canFix, params string[] facts)
            { Id = id; Title = title; How = how; Where = where; CanBeAbsent = canBeAbsent; CanFix = canFix; Facts = facts; }
        }

        public const string Note = "공식 자료를 모아 이 게임이 만든 점검표입니다(공식 서식 아님). 체크한 내용도 저장되지 않고, 이미지로만 내려받습니다.";
        public const string PrintNote = "공식 자료를 모아 게임 「경보 이전」이 만든 점검표 · 공식 서식 아님";

        public static readonly Item[] Items =
        {
            new("exits", "우리 집 피난시설 찾기", "대피공간·경량칸막이·하향식 피난구·완강기 가운데 우리 집에 무엇이 있는지, 어디에 있는지 알아 둡니다.", null, false, false,
                "home_exits_know", "fire_own_trapped"),
            new("partition", "경량칸막이 앞 비우기", "있다면, 그 앞을 수납장이나 짐이 막고 있지 않은지 봅니다.", null, true, true,
                "fire_partition", "fire_partition_clear"),
            new("hatch", "하향식 피난구 둘레 비우기", "있다면, 발코니나 대피공간 바닥의 피난구 둘레에 짐을 쌓아 두지 않았는지 봅니다.", null, true, true,
                "home_hatch", "home_unaware"),
            new("gauge", "소화기 압력계 바늘", "집 소화기의 압력계 바늘이 녹색 구간에 있는지 봅니다. 빨간색·노란색이면 바꿉니다.", null, false, true,
                "ext_gauge", "ext_gauge_bad"),
            new("stairs", "가장 가까운 계단 걸어 보기", "엘리베이터 대신 내려갈 계단이 어디 있는지 한 번 걸어서 확인합니다.", null, false, false,
                "fire_lift", "alert_stairs"),
            new("corridor", "복도·비상구 비우기", "비상구와 계단 앞에 자전거, 상자 같은 물건이 없는지 봅니다.", null, false, true,
                "home_exit_clear"),
            new("shelter", "가까운 민방위 대피소 찾기", "집에서 가장 가까운 민방위 대피소를 찾아 가는 길을 알아 둡니다.",
                "안전디딤돌 앱 · 국민안전24(safekorea.go.kr) · 네이버지도·카카오맵·T맵에서 '민방위 대피소' 검색", false, false,
                "shelter_apps", "shelter_find"),
            new("supplies", "비상용품 점검", "식수, 식량, 상비약, 라디오, 손전등을 한 사람 분량씩 챙기고 유통기한을 봅니다.", null, false, true,
                "bag_days", "bag_each", "bag_nocook"),
            new("card", "가족 비상연락카드", "연락처와 만남의 장소를 적어 생존배낭에 넣어 둡니다.", null, false, false,
                "contact_card", "contact_meet"),
        };

        public static Item Get(string id) => Items.FirstOrDefault(i => i.Id == id);

        /// <summary>The next mark on a click: 아직 → 확인함 → (고칠 것, when looking can find a fault) → (우리 집엔 없음, when it can be absent) → 아직.</summary>
        public static Mark Next(Item item, Mark m)
        {
            bool fix = item != null && item.CanFix, absent = item != null && item.CanBeAbsent;
            switch (m)
            {
                case Mark.Todo: return Mark.Done;
                case Mark.Done: return fix ? Mark.Fix : absent ? Mark.Absent : Mark.Todo;
                case Mark.Fix: return absent ? Mark.Absent : Mark.Todo;
                default: return Mark.Todo;
            }
        }

        public static string MarkText(Mark m) => m switch { Mark.Done => "확인함", Mark.Fix => "고칠 것", Mark.Absent => "우리 집엔 없음", _ => "아직" };

        /// <summary>The marks one item can take, in click order (for the screen's lead line and the speech).</summary>
        public static string CycleText(Item item)
        {
            var parts = new List<string> { MarkText(Mark.Todo), MarkText(Mark.Done) };
            if (item != null && item.CanFix) parts.Add(MarkText(Mark.Fix));
            if (item != null && item.CanBeAbsent) parts.Add(MarkText(Mark.Absent));
            return string.Join(" → ", parts);
        }

        public static Mark MarkOf(IReadOnlyDictionary<string, Mark> marks, string id) =>
            marks != null && marks.TryGetValue(id, out var m) ? m : Mark.Todo;

        public static (int done, int fix, int absent, int todo) Count(IReadOnlyDictionary<string, Mark> marks)
        {
            int d = 0, f = 0, a = 0, t = 0;
            foreach (var i in Items)
                switch (MarkOf(marks, i.Id)) { case Mark.Done: d++; break; case Mark.Fix: f++; break; case Mark.Absent: a++; break; default: t++; break; }
            return (d, f, a, t);
        }

        /// <summary>The headline of the tally: "확인 4 / 9" (looked at, fine or to fix).</summary>
        public static string SummaryHead(IReadOnlyDictionary<string, Mark> marks)
        {
            var (d, f, _, _) = Count(marks);
            return $"확인 {d + f} / {Items.Length}";
        }

        /// <summary>The rest of the tally: "고칠 것 1 · 없음 1 · 남음 4" (고칠 것 and 없음 only when there are any).</summary>
        public static string SummaryRest(IReadOnlyDictionary<string, Mark> marks)
        {
            var (_, f, a, t) = Count(marks);
            var parts = new List<string>();
            if (f > 0) parts.Add($"고칠 것 {f}");
            if (a > 0) parts.Add($"없음 {a}");
            parts.Add($"남음 {t}");
            return string.Join(" · ", parts);
        }

        public static string Summary(IReadOnlyDictionary<string, Mark> marks) => SummaryHead(marks) + " · " + SummaryRest(marks);

        /// <summary>Every fact id the list cites, once, in order (the print's source line).</summary>
        public static List<string> AllFacts() => Items.SelectMany(i => i.Facts).Distinct().ToList();
    }

    /// <summary>안전체험관 안내: where real CPR and fire training is found. Agencies and site addresses only (checked on CheckedAt).</summary>
    public static class TrainingGuide
    {
        public const string CheckedAt = "2026-11-21";

        public sealed class Entry
        {
            public readonly string Id, Title, Agency, What;
            public readonly string[] Where, Facts;
            public Entry(string id, string title, string agency, string what, string[] where, params string[] facts)
            { Id = id; Title = title; Agency = agency; What = what; Where = where; Facts = facts; }
        }

        public static readonly Entry[] Entries =
        {
            new("platform", "가까운 안전체험관 찾기", "국민안전교육플랫폼", "사는 곳에서 가까운 안전체험관과 이용 요금이 있는지 확인합니다.",
                new[] { "kasem.safekorea.go.kr › 안전체험관" }, "train_platform"),
            new("cpr", "심폐소생술 실습 교육", "대한심폐소생협회", "마네킹으로 직접 눌러 보는 실습 교육을 찾습니다.",
                new[] { "www.kacpr.org › 교육안내 › 교육기관찾기" }, "cpr_practice", "train_cpr"),
            new("nfa", "소방 안전 체험", "소방청", "소방청 누리집의 체험 안내와, 집에서 먼저 해 보는 가상 체험관입니다.",
                new[] { "www.nfa.go.kr › 안전정보 › 생활안전정보 › 체험안내", "119metaverse.nfa.go.kr (메타버스 119안전체험관)" }, "train_metaverse"),
            new("list", "시도별 안전체험관 목록", "행정안전부 · 공공데이터포털", "운영시간, 예약방법, 체험분야가 시도별로 정리된 목록입니다.",
                new[] { "www.data.go.kr › '행정안전부_안전체험관 현황' 검색" }, "train_list"),
        };

        public const string Note = "주소·일정·요금은 바뀔 수 있어 이 게임에 적지 않았습니다. 각 누리집에서 확인하세요.";
        public static string CheckedLine => $"누리집 주소와 메뉴 이름은 {CheckedAt}에 확인했습니다. 링크는 글자로만 보여 주고, 게임이 인터넷에 접속하지 않습니다.";
    }

    /// <summary>The printed layouts: A6 (105 x 148 mm) portrait at 300 dpi, in pixels from the top-left corner.</summary>
    public static class PrintLayout
    {
        public const float A6WidthMm = 105f, A6HeightMm = 148f;
        public const int Dpi = 300;

        public static (int w, int h) A6Pixels(int dpi = Dpi) =>
            ((int)Math.Round(A6WidthMm / 25.4 * dpi), (int)Math.Round(A6HeightMm / 25.4 * dpi));

        public sealed class Cell
        {
            /// <summary>band (the title strip), section (a heading), field (a FamilyCard field), hotline (a printed number),
            /// hotnote (the fact under the numbers), item (a checklist row), date, note (the small print at the foot).</summary>
            public string Kind, Id, Text;
            public float X, Y, W, H;
            public float Bottom => Y + H;
            public float Right => X + W;
        }

        public static bool Overlaps(Cell a, Cell b) => a.X < b.Right - 0.01f && b.X < a.Right - 0.01f && a.Y < b.Bottom - 0.01f && b.Y < a.Bottom - 0.01f;

        // card metrics at 1240 px wide (scaled for other sizes)
        public const float BandH = 190, SectionH = 46, SectionGap = 14, RowH = 76, RowGap = 6, HotlineH = 50, HotNoteH = 44, NoteH = 104, Margin = 64;

        /// <summary>The family card: band, then each section with its rows, the hotlines, and the foot note.</summary>
        public static List<Cell> Card(int w, int h)
        {
            float s = w / 1240f, m = Margin * s, inner = w - m * 2, gap = 16 * s;
            var cells = new List<Cell> { new() { Kind = "band", Id = "band", Text = "우리 가족 비상연락카드", X = 0, Y = 0, W = w, H = BandH * s } };
            float y = (BandH + 22) * s;
            void Row(params (string id, float share)[] parts)
            {
                float x = m;
                float avail = inner - gap * (parts.Length - 1);
                foreach (var (id, share) in parts)
                {
                    float cw = avail * share;
                    cells.Add(new Cell { Kind = "field", Id = id, Text = FamilyCard.Get(id)?.Label, X = x, Y = y, W = cw, H = RowH * s });
                    x += cw + gap;
                }
                y += (RowH + RowGap) * s;
            }
            foreach (var (id, title) in FamilyCard.Sections)
            {
                cells.Add(new Cell { Kind = "section", Id = id, Text = title, X = m, Y = y, W = inner, H = SectionH * s });
                y += SectionH * s;
                switch (id)
                {
                    case "me": Row(("name", 0.6f), ("blood", 0.4f)); Row(("meds", 1f)); break;
                    case "family":
                        for (int i = 1; i <= 3; i++) Row(($"fam{i}", 0.45f), ($"fam{i}Phone", 0.55f));
                        break;
                    case "places": Row(("placeHome", 1f / 3), ("placeWork", 1f / 3), ("placeSchool", 1f / 3)); break;
                    case "meet": Row(("meet1", 1f)); Row(("meet2", 1f)); Row(("meet3", 1f)); break;
                    case "shelter": Row(("shelter", 1f)); break;
                    case "hotline":
                        foreach (var (number, what) in FamilyCard.Hotlines)
                        {
                            cells.Add(new Cell { Kind = "hotline", Id = number, Text = what, X = m, Y = y, W = inner, H = HotlineH * s });
                            y += HotlineH * s;
                        }
                        cells.Add(new Cell { Kind = "hotnote", Id = FamilyCard.HotlineNoteFact, X = m, Y = y + 4 * s, W = inner, H = HotNoteH * s });
                        y += (HotNoteH + 4) * s;
                        break;
                }
                y += SectionGap * s;
            }
            cells.Add(new Cell { Kind = "note", Id = "note", Text = FamilyCard.CardFootnote, X = m, Y = h - m * 0.6f - NoteH * s, W = inner, H = NoteH * s });
            return cells;
        }

        public const float ItemH = 128, DateH = 56;

        /// <summary>The checklist print: band, the date line, one row per item, and the foot note (sources).</summary>
        public static List<Cell> Checklist(int w, int h)
        {
            float s = w / 1240f, m = Margin * s, inner = w - m * 2;
            var cells = new List<Cell>
            {
                new() { Kind = "band", Id = "band", Text = HomeChecklist.Title, X = 0, Y = 0, W = w, H = BandH * s },
                new() { Kind = "date", Id = "date", Text = "점검한 날", X = m, Y = (BandH + 18) * s, W = inner, H = DateH * s },
            };
            float y = (BandH + 18 + DateH + 12) * s;
            foreach (var item in HomeChecklist.Items)
            {
                cells.Add(new Cell { Kind = "item", Id = item.Id, Text = item.Title, X = m, Y = y, W = inner, H = ItemH * s });
                y += ItemH * s;
            }
            float noteH = 150 * s;
            cells.Add(new Cell { Kind = "note", Id = "note", Text = HomeChecklist.PrintNote, X = m, Y = h - m * 0.6f - noteH, W = inner, H = noteH });
            return cells;
        }
    }
}
