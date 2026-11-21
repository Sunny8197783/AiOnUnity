// 더 알아보기 (competition upgrade): the hub ("more", from the title and the survival guide) and its screens:
// 기록관 「사건이 제도를 바꿨다」 with the 신고 번호 카드, 연무동 지도와 사람들 (the town map drawn by RoutemapLogic with pins
// and resident profiles that follow the latest run), 훈련 달력, and 우리 집 안전 준비 (HomePrepScreen).
// Data and rules: Game/Archive.cs. Every history or guidance sentence is a fact, shown with who published it.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>Shared bits of the 더 알아보기 screens.</summary>
    static class ArchiveUi
    {
        public static readonly Color Tone = Terminal.Sky;

        /// <summary>A label sized to its text at width w, placed at (x, y); returns its height.</summary>
        public static float Para(Transform parent, string name, string text, int size, Color color, float x, float y, float w, FontStyle style = FontStyle.Normal, float spacing = 1.08f)
        {
            var t = UiKit.Label(name, parent, text, size, color, TextAnchor.UpperLeft, style);
            t.lineSpacing = spacing;
            UiKit.At(t.rectTransform, x, y, w, 10);
            float h = Mathf.Ceil(t.preferredHeight);
            t.rectTransform.sizeDelta = new Vector2(w, h);
            return h;
        }

        /// <summary>"출처 · 발행처 「제목」": the publishers of these facts, once each.</summary>
        public static string Sources(Facts facts, IEnumerable<string> factIds)
        {
            var pubs = factIds.Select(facts.SourceId).Where(id => id != null).Distinct().Select(facts.Source).Where(s => s != null)
                .Select(s => s.Publisher).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
            return pubs.Count == 0 ? null : "출처 · " + string.Join(", ", pubs);
        }

        public static Image Chip(Transform parent, string text, Color c, float x, float y, int size = 15)
        {
            var t = UiKit.Label("T", parent, text, size, c, TextAnchor.MiddleCenter, FontStyle.Bold);
            float w = Mathf.Ceil(t.preferredWidth) + 22, h = size + 13;
            var chip = UiKit.Box("Chip", parent, Terminal.Tint(c, 0.18f)); HudTheme.Round(chip);
            UiKit.At(chip.rectTransform, x, y, w, h);
            t.transform.SetParent(chip.transform, false);
            UiKit.Fill(t.rectTransform);
            return chip;
        }

        public static TerminalIcon Badge(Transform parent, string icon, Color c, float x, float y, float size)
        {
            var badge = UiKit.Box("Badge", parent, Terminal.Tint(c, 0.16f)); badge.sprite = HudDraw.DiscSprite;
            UiKit.At(badge.rectTransform, x, y, size, size);
            var ic = Terminal.Icon(badge.transform, icon, c, size * 0.54f);
            var irt = ic.rectTransform; irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f); irt.anchoredPosition = Vector2.zero;
            return ic;
        }

        public static ArchiveProgress Parse(JObject o) => Archive.Read(o?["progress"]);

        /// <summary>The story director, when a run or the title loop is up.</summary>
        public static StoryDirector Director()
        {
            var runner = UnityEngine.Object.FindFirstObjectByType<StoryRunner>();
            return runner ? runner.Director : null;
        }

        /// <summary>Opens the hub from inside a run (the survival guide): the stored archive with this run on top.</summary>
        public static async Task OpenFromGame()
        {
            var d = Director();
            var view = d != null ? d.ArchiveView() : new ArchiveProgress();
            var r = await MoreScreen.Open(d != null ? d.MoreOptions(view) : new JObject { ["progress"] = view.ToJObject() });
            if (JsSem.Is(r, StoryDirector.MoreSeen)) d?.ArchiveSeen(view); // only once the 기록관 itself was opened
        }
    }

    // =====================================================================================================
    // hub: more -> null
    // =====================================================================================================
    public class MoreScreen : UiScreen
    {
        public const float W = 1640, H = 700;

        public static void Register(UnityStoryHost host) => host.Screens[Archive.Screen] = Open;

        /// <summary>Answers StoryDirector.MoreSeen when the 기록관 was opened (its "새로" marks are then spent), else null.</summary>
        public static async Task<JToken> Open(JObject o)
        {
            var p = ArchiveUi.Parse(o);
            var chapter = JsSem.Str(o?["chapter"]);
            MoreScreen me = null;
            await Show<MoreScreen>(s => { me = s; s._p = p; s._chapter = chapter; }, W, H);
            return me != null && me._seen ? StoryDirector.MoreSeen : null;
        }

        ArchiveProgress _p;
        /// <summary>The chapter being played (mid-run, from the guide), or null.</summary>
        string _chapter;
        bool _child, _seen;
        readonly List<Text> _status = new();
        int _cursor;
        readonly List<(Image img, Color c)> _tiles = new();
        readonly HomePrepVoice _voice = new();

        (string id, string icon, string title, string line, string status)[] Tiles() => new[]
        {
            ("archive", "book", "기록관", "사건이 제도를 바꾼 순간들, 이야기 속에서 모은 신고 번호 카드와 루머 수법.",
                Archive.CountLine(_p) + $" · 수법 {_p.Techniques.Count}/{RumorAnatomy.Techniques.Count}" + (_p.NewCount > 0 ? $" · 새로 {_p.NewCount}" : "")),
            ("town", "pin", "연무동 지도와 사람들", "103동, 대피소, 급수소, 뒷산 산책로. 이웃들의 이야기는 지난 판에 따라 바뀝니다.",
                $"이야기에서 만난 이웃 {Archive.NeighborsMet(_p)}명"),
            ("calendar", "calendar", "훈련 달력", "민방위 훈련은 언제, 누구를 대상으로 열릴까요? 2026년 계획으로 봅니다.", "실제 일정은 매년 행정안전부 공지"),
            ("home", "card", HomePrepScreen.Heading, "비상연락카드, 우리 집 안전 점검표, 안전체험관 안내.", "게임 밖 · 실제 우리 집"),
        };

        protected override object EscapeResult() => _child ? null : "close";

        protected override void Build()
        {
            var body = Terminal.Frame(Window, Archive.Heading, "연무동 기록 · 실제 제도와 훈련", ArchiveUi.Tone);
            float bw = W - Terminal.Inset * 2, bh = H - Terminal.Inset * 2 - Terminal.HeaderH - 2;
            const float pad = 36, gap = 20;
            var intro = UiKit.Label("Intro", body, "이야기 밖으로 한 걸음. 연무동은 가상의 동네지만, 여기 적힌 사건과 제도, 훈련은 실제 자료에서 확인한 것입니다.", 20, Terminal.Ink, TextAnchor.MiddleLeft);
            UiKit.At(intro.rectTransform, pad, 20, bw - pad * 2, 52);
            var tiles = Tiles();
            float tw = (bw - pad * 2 - gap * (tiles.Length - 1)) / tiles.Length, th = 380, ty = 88;
            for (int i = 0; i < tiles.Length; i++)
            {
                int k = i;
                var rt = Tile(body, tiles[i], tw, th, () => OpenChild(k));
                UiKit.At(rt, pad + i * (tw + gap), ty, tw, th);
                UiAppear.On(rt.gameObject, 0.06f + i * 0.05f);
            }
            var keys = UiKit.Label("Keys", body, "1~4 또는 ←→ + Enter · 클릭 · 패드 A 고르기   Esc·B 닫기", 16, Terminal.Muted, TextAnchor.MiddleLeft);
            UiKit.At(keys.rectTransform, pad, bh - 58, 800, 30);
            var close = Terminal.Button(body, "닫기  ·  Esc", ArchiveUi.Tone, false);
            UiKit.At((RectTransform)close.transform, bw - pad - 240, bh - 72, 240, 50);
            close.onClick.AddListener(() => { if (!_child) Finish("close"); });
            SetCursor(0);
            _voice.Say(Archive.Heading + ". " + string.Join(". ", tiles.Select((t, i) => $"{i + 1}번, {t.title}. {t.line}")) + " 숫자로 고릅니다.");
        }

        RectTransform Tile(Transform parent, (string id, string icon, string title, string line, string status) t, float w, float h, Action open)
        {
            var img = UiKit.Box(t.id, parent, Terminal.Plate, raycast: true);
            HudTheme.Round(img);
            var rt = img.rectTransform;
            UiKit.At(UiKit.Box("Tone", rt, ArchiveUi.Tone).rectTransform, 14, 0, w - 28, 3);
            ArchiveUi.Badge(rt, t.icon, ArchiveUi.Tone, 26, 28, 76);
            UiKit.At(UiKit.Label("No", rt, (_tiles.Count + 1).ToString(), 18, Terminal.Muted, TextAnchor.MiddleRight, FontStyle.Bold).rectTransform, w - 70, 30, 44, 28);
            UiKit.At(UiKit.Label("Title", rt, t.title, 26, Terminal.Ink, TextAnchor.UpperLeft, FontStyle.Bold).rectTransform, 26, 124, w - 52, 40);
            ArchiveUi.Para(rt, "Line", t.line, 18, Terminal.Muted, 26, 172, w - 52, spacing: 1.12f);
            var st = UiKit.Label("Status", rt, t.status, 16, Terminal.Tint(Terminal.Ink, 0.85f), TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.At(st.rectTransform, 26, h - 96, w - 52, 26);
            _status.Add(st);
            UiKit.At(UiKit.Label("Open", rt, "열기 ▶", 19, ArchiveUi.Tone, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, 26, h - 54, 200, 30);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => { UiSound.Play("click"); open(); });
            var trig = img.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            int idx = _tiles.Count;
            enter.callback.AddListener(_ => { UiSound.Play("tick"); SetCursor(idx); });
            trig.triggers.Add(enter);
            _tiles.Add((img, img.color));
            return rt;
        }

        void SetCursor(int i)
        {
            if (_tiles.Count == 0) return;
            i = Mathf.Clamp(i, 0, _tiles.Count - 1);
            for (int k = 0; k < _tiles.Count; k++) _tiles[k].img.color = k == i ? Terminal.PlateHi : _tiles[k].c;
            _cursor = i;
        }

        async void OpenChild(int i)
        {
            if (_child) return;
            _child = true;
            try
            {
                switch (Tiles()[i].id)
                {
                    case "archive":
                        await ArchiveScreen.Open(_p, _chapter);
                        _seen = true;
                        Archive.MarkViewed(_p); // the hub's own count follows; the director stores it when the hub closes
                        break;
                    case "town": await TownScreen.Open(_p, _chapter); break;
                    case "calendar": await DrillCalendarScreen.Open(); break;
                    default: await HomePrepScreen.Open(null); break;
                }
            }
            catch (Exception e) { Debug.LogException(e); }
            finally { _child = false; }
            var tiles = Tiles();
            for (int k = 0; k < _status.Count && k < tiles.Length; k++) if (_status[k]) _status[k].text = tiles[k].status;
        }

        protected override void Update()
        {
            base.Update();
            if (_child || !IsTop || Time.unscaledTime - OpenedAt < 0.25f) return;
            var es = EventSystem.current;
            if (es && es.currentSelectedGameObject) es.SetSelectedGameObject(null);
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            int move = 0; bool press = false;
            if (kb != null)
            {
                for (int k = 0; k < _tiles.Count; k++)
                    if (kb[Key.Digit1 + k].wasPressedThisFrame || kb[Key.Numpad1 + k].wasPressedThisFrame) { SetCursor(k); OpenChild(k); return; }
                move += (kb.rightArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame ? 1 : 0) - (kb.leftArrowKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame ? 1 : 0);
                press |= kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
            }
            if (gp != null)
            {
                move += (gp.dpad.right.wasPressedThisFrame ? 1 : 0) - (gp.dpad.left.wasPressedThisFrame ? 1 : 0);
                press |= gp.buttonSouth.wasPressedThisFrame;
                if (gp.buttonEast.wasPressedThisFrame) { Finish("close"); return; }
            }
            if (move != 0) { SetCursor(_cursor + move); _voice.Say(Tiles()[_cursor].title); }
            else if (press) OpenChild(_cursor);
        }
    }

    // =====================================================================================================
    // 기록관: 사건이 제도를 바꿨다 · 신고 번호 카드
    // =====================================================================================================
    public class ArchiveScreen : UiScreen
    {
        public const float W = 1760, H = 1000;
        const float Pad = 28, Gap = 18, TabY = 14, TopY = 78, Foot = 76;

        ArchiveProgress _mine, _p;
        /// <summary>The chapter being played (opened mid-run from the guide), or null: hints further on stay vague, no reveal.</summary>
        string _chapter;
        bool _reveal;
        /// <summary>0 사건과 제도 · 1 신고 번호 카드 · 2 루머 수법 (루머 해부).</summary>
        int _tab;
        bool MidRun => _chapter != null;
        int _sel;
        RectTransform _body, _page;
        Text _status;
        Button _tabEvents, _tabCards, _tabTricks, _revealBtn;
        readonly HomePrepVoice _voice = new();
        float BW => W - Terminal.Inset * 2;
        float BH => H - Terminal.Inset * 2 - Terminal.HeaderH - 2;

        public static async Task Open(ArchiveProgress p, string chapter = null)
        {
            UiSound.Play("radio");
            await Show<ArchiveScreen>(s => { s._mine = p ?? new ArchiveProgress(); s._p = s._mine; s._chapter = chapter; }, W, H);
        }

        protected override object EscapeResult() => "close";

        protected override void Build()
        {
            _body = Terminal.Frame(Window, "기록관 · 사건이 제도를 바꿨다", Archive.CountLine(_mine), ArchiveUi.Tone);
            _status = _body.parent.Find("Header/Status")?.GetComponent<Text>();
            _tabEvents = Tab("사건과 제도", Pad, () => SetTab(0));
            _tabCards = Tab("신고 번호 카드", Pad + 250, () => SetTab(1));
            _tabTricks = Tab("루머 수법", Pad + 500, () => SetTab(2));
            _revealBtn = Terminal.Button(_body, "", ArchiveUi.Tone, false);
            UiKit.At((RectTransform)_revealBtn.transform, BW - Pad - 360, TabY, 360, 48);
            _revealBtn.onClick.AddListener(ToggleReveal);
            // mid-run (the guide) the toggle would spoil the chapters ahead: it is for the title, teacher mode and scene select
            _revealBtn.gameObject.SetActive(!MidRun);
            var note = UiKit.Label("Note", _body, "사건과 제도만 다루고, 사람의 이름과 희생 장면은 다루지 않습니다.", 15, Terminal.Muted, TextAnchor.MiddleRight);
            UiKit.At(note.rectTransform, Pad + 750, TabY, BW - Pad * 2 - 750 - (MidRun ? 0 : 380), 48);
            var keys = UiKit.Label("Keys", _body, MidRun
                ? "Tab 사건 / 카드 / 수법 · ↑↓ 고르기 · Esc 닫기   (패드: LB/RB · 위아래 · B)"
                : "Tab 사건 / 카드 / 수법 · ↑↓ 고르기 · R 모두 펼쳐 보기 · Esc 닫기   (패드: LB/RB · 위아래 · Y · B)", 16, Terminal.Muted, TextAnchor.MiddleLeft);
            UiKit.At(keys.rectTransform, Pad, BH - Foot + 22, 1000, 30);
            var close = Terminal.Button(_body, "닫기  ·  Esc", ArchiveUi.Tone, true);
            UiKit.At((RectTransform)close.transform, BW - Pad - 240, BH - Foot + 16, 240, 48);
            close.onClick.AddListener(() => Finish("close"));
            _sel = Math.Max(0, Archive.Entries.ToList().FindIndex(e => _p.IsNew(e.Id)));
            Render();
            // low vision: the screen, then the entry it opened on
            _voice.Say("기록관, 사건이 제도를 바꿨다. " + Archive.CountLine(_mine) + ". " + EntrySpeech() + " Tab으로 신고 번호 카드와 루머 수법.");
        }

        Button Tab(string label, float x, Action click)
        {
            var b = Terminal.Button(_body, label, ArchiveUi.Tone, false);
            UiKit.At((RectTransform)b.transform, x, TabY, 236, 48);
            b.onClick.AddListener(() => click());
            return b;
        }

        void Paint(Button b, bool on)
        {
            b.GetComponent<Image>().color = on ? ArchiveUi.Tone : Terminal.PlateHi;
            var t = b.GetComponentInChildren<Text>(); if (t) t.color = on ? Terminal.Bg : Terminal.Ink;
        }

        void SetTab(int tab)
        {
            tab = ((tab % 3) + 3) % 3;
            if (_tab == tab) return;
            _tab = tab; Render();
            _voice.Say(tab == 1 ? "신고 번호 카드. " + CardSpeech() : tab == 2 ? "루머 수법. " + TrickSpeech() : "사건과 제도. " + EntrySpeech());
        }

        void ToggleReveal()
        {
            if (MidRun) return;
            _reveal = !_reveal;
            _p = _reveal ? Archive.RevealAll(_mine) : _mine;
            Render();
            _voice.Say(_reveal ? "모든 기록과 카드를 펼쳤습니다. 내 기록에는 남지 않습니다." : "내가 연 기록만 봅니다.");
        }

        void Render()
        {
            Paint(_tabEvents, _tab == 0); Paint(_tabCards, _tab == 1); Paint(_tabTricks, _tab == 2);
            var rt = _revealBtn.GetComponentInChildren<Text>();
            if (rt) rt.text = _reveal ? "내가 연 것만 보기" : "모두 펼쳐 보기 · 수업·심사용";
            if (_status) _status.text = _reveal ? "모두 펼친 상태 · 내 기록에는 남지 않습니다" : Archive.CountLine(_mine);
            if (_page) Destroy(_page.gameObject);
            _page = UiKit.Rect("Page", _body);
            UiKit.At(_page, 0, TopY, BW, BH - TopY - Foot);
            if (_tab == 1) CardsPage(); else if (_tab == 2) TricksPage(); else EventsPage();
        }

        // ---------- 사건과 제도 ----------

        void EventsPage()
        {
            float ph = BH - TopY - Foot, lw = 420;
            for (int i = 0; i < Archive.Entries.Count; i++)
            {
                var e = Archive.Entries[i];
                bool open = _p.Entries.Contains(e.Id), on = i == _sel;
                var row = UiKit.Box("Entry" + e.Id, _page, on ? Terminal.PlateHi : Terminal.Plate, raycast: true); HudTheme.Round(row);
                float rh = (ph - Gap * (Archive.Entries.Count - 1)) / Archive.Entries.Count;
                UiKit.At(row.rectTransform, Pad, i * (rh + Gap), lw, rh);
                if (on) UiKit.At(UiKit.Box("Edge", row.transform, ArchiveUi.Tone).rectTransform, 0, 10, 4, rh - 20);
                Color c = open ? Terminal.Ink : Terminal.Tint(Terminal.Muted, 0.7f);
                UiKit.At(UiKit.Label("Year", row.transform, e.Year, 30, open ? ArchiveUi.Tone : c, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, 22, 0, 100, rh);
                UiKit.At(UiKit.Label("Title", row.transform, open ? e.Title : "아직 열리지 않은 기록", 19, c, TextAnchor.MiddleLeft, open ? FontStyle.Bold : FontStyle.Normal).rectTransform, 118, 0, lw - 118 - 80, rh);
                if (!open) { var ic = Terminal.Icon(row.transform, "lock", Terminal.Muted, 22); UiKit.At(ic.rectTransform, lw - 46, rh / 2 - 11, 22, 22); }
                else if (_p.IsNew(e.Id)) ArchiveUi.Chip(row.transform, "새로", Terminal.Amber, lw - 70, rh / 2 - 14, 14);
                var b = row.gameObject.AddComponent<Button>(); b.targetGraphic = row; b.navigation = new Navigation { mode = Navigation.Mode.None };
                int k = i;
                b.onClick.AddListener(() => { if (_sel != k) { _sel = k; UiSound.Play("tick"); Render(); SpeakEntry(); } });
            }
            Detail(Archive.Entries[_sel], Pad + lw + Gap, BW - Pad * 2 - lw - Gap, ph);
        }

        void Detail(ArchiveEntry e, float x, float w, float h)
        {
            var plate = UiKit.Box("Detail", _page, Terminal.Plate); HudTheme.Round(plate);
            UiKit.At(plate.rectTransform, x, 0, w, h);
            var p = plate.rectTransform;
            var facts = TerminalData.Facts;
            const float px = 34;
            float tw = w - px * 2;
            if (!_p.Entries.Contains(e.Id))
            {
                var ic = Terminal.Icon(p, "lock", Terminal.Muted, 64);
                UiKit.At(ic.rectTransform, w / 2 - 32, h / 2 - 120, 64, 64);
                UiKit.At(UiKit.Label("Locked", p, $"{e.Year} · 아직 열리지 않은 기록", 28, Terminal.Ink, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform, px, h / 2 - 40, tw, 40);
                UiKit.At(UiKit.Label("Hint", p, Archive.HintFor(e, _chapter), 20, Terminal.Muted, TextAnchor.MiddleCenter).rectTransform, px, h / 2 + 8, tw, 60);
                UiAppear.On(p.gameObject, 0f, slide: false, duration: 0.18f);
                return;
            }
            float y = 28;
            UiKit.At(UiKit.Label("Year", p, e.Year, 46, ArchiveUi.Tone, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, px, y, 150, 56);
            ArchiveUi.Chip(p, KindChip(e.Kind), e.Kind == "기록" ? Terminal.Emerald : e.Kind == "안내" ? ArchiveUi.Tone : Terminal.Amber, px + 150, y + 14);
            y += 62;
            y += ArchiveUi.Para(p, "Title", e.Title, 30, Terminal.Ink, px, y, tw, FontStyle.Bold) + 22;

            void Block(string head, Color c, IEnumerable<string> ids)
            {
                UiKit.At(UiKit.Label("Head", p, head, 17, c, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, px, y, tw, 26);
                y += 32;
                foreach (var id in ids)
                {
                    var t = facts.Text(id);
                    if (string.IsNullOrEmpty(t)) continue;
                    UiKit.At(UiKit.Box("Bar", p, Terminal.Tint(c, 0.6f)).rectTransform, px, y + 3, 3, 24);
                    y += ArchiveUi.Para(p, "Fact", t, 20, Terminal.Ink, px + 18, y, tw - 18) + 12;
                }
                y += 8;
            }
            if (e.EventFacts.Length > 0) Block($"사건  ·  {e.EventWhen}", Terminal.Amber, e.EventFacts);
            Block(ChangeHead(e.Kind) + "  ·  " + e.ChangeTitle + (string.IsNullOrEmpty(e.ChangeWhen) ? "" : $" ({e.ChangeWhen})"), Terminal.Emerald, e.ChangeFacts);
            UiKit.At(UiKit.Label("GameHead", p, "이 게임에서는", 17, ArchiveUi.Tone, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, px, y, tw, 26);
            y += 32;
            ArchiveUi.Para(p, "Game", e.InGame, 18, Terminal.Tint(Terminal.Ink, 0.85f), px, y, tw);
            var src = ArchiveUi.Sources(facts, e.FactIds);
            if (src != null)
            {
                var s = UiKit.Label("Source", p, src, 14, Terminal.Muted, TextAnchor.LowerLeft);
                UiKit.At(s.rectTransform, px, h - 40, tw, 24);
            }
            UiAppear.On(p.gameObject, 0f, slide: false, duration: 0.18f);
        }

        /// <summary>The chip over an open entry: what kind of change followed.</summary>
        public static string KindChip(string kind) => kind == "기록" ? "기록 · 시민의 대응" : kind == "안내" ? "안내가 나옴" : "제도가 바뀜";

        /// <summary>The heading over the change facts.</summary>
        public static string ChangeHead(string kind) => kind == "기록" ? "그때 시민들은" : kind == "안내" ? "그 뒤 나온 안내" : "그래서 바뀐 것";

        void SpeakEntry() => _voice.Say(EntrySpeech());

        string EntrySpeech()
        {
            var e = Archive.Entries[_sel % Archive.Entries.Count];
            if (!_p.Entries.Contains(e.Id)) return $"{e.Year}년. 아직 열리지 않은 기록. {Archive.HintFor(e, _chapter)}";
            var f = TerminalData.Facts;
            return $"{e.Year}년, {e.Title}. " + string.Join(" ", e.FactIds.Select(f.Text).Where(t => t != null)) + " 이 게임에서는, " + e.InGame;
        }

        // ---------- 신고 번호 카드 ----------

        void CardsPage()
        {
            float ph = BH - TopY - Foot;
            int n = Archive.Cards.Count;
            float cw = (BW - Pad * 2 - Gap * (n - 1)) / n;
            var facts = TerminalData.Facts;
            for (int i = 0; i < n; i++)
            {
                var c = Archive.Cards[i];
                bool got = _p.Cards.Contains(c.Id), on = i == _sel % n;
                var plate = UiKit.Box("Card" + c.Id, _page, on ? Terminal.PlateHi : Terminal.Plate, raycast: true); HudTheme.Round(plate);
                var rt = plate.rectTransform;
                UiKit.At(rt, Pad + i * (cw + Gap), 0, cw, ph);
                Color tone = got ? NumberColor(c.Id) : Terminal.Tint(Terminal.Muted, 0.6f);
                UiKit.At(UiKit.Box("Tone", rt, tone).rectTransform, 14, 0, cw - 28, 4);
                ArchiveUi.Badge(rt, got ? "phone" : "lock", tone, 22, 24, 56);
                if (got && _p.IsNew(c.Id)) ArchiveUi.Chip(rt, "새로", Terminal.Amber, cw - 76, 36, 14);
                float y = 96;
                int numSize = c.Number.Length > 4 ? 40 : 58;
                UiKit.At(UiKit.Label("Number", rt, c.Number, numSize, got ? Terminal.Ink : Terminal.Tint(Terminal.Muted, 0.75f), TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, 22, y, cw - 44, 70);
                y += 72;
                y += ArchiveUi.Para(rt, "Title", c.Title, 20, got ? tone : Terminal.Muted, 22, y, cw - 44, FontStyle.Bold) + 18;
                UiKit.At(UiKit.Box("Rule", rt, Terminal.Rule).rectTransform, 22, y, cw - 44, 1);
                y += 16;
                if (!got)
                {
                    UiKit.At(UiKit.Label("Locked", rt, "아직 모으지 않은 카드", 18, Terminal.Ink, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, 22, y, cw - 44, 28);
                    y += 34;
                    ArchiveUi.Para(rt, "Hint", Archive.HintFor(c, _chapter), 17, Terminal.Muted, 22, y, cw - 44);
                }
                else
                {
                    UiKit.At(UiKit.Label("When", rt, "언제 쓰나", 15, Terminal.Amber, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, 22, y, cw - 44, 22);
                    y += 28;
                    foreach (var id in c.Facts)
                    {
                        var t = facts.Text(id);
                        if (string.IsNullOrEmpty(t)) continue;
                        var mark = Terminal.Icon(rt, "check", Terminal.Emerald, 14);
                        UiKit.At(mark.rectTransform, 22, y + 4, 14, 14);
                        y += ArchiveUi.Para(rt, "Fact", t, 16, Terminal.Ink, 44, y, cw - 66) + 10;
                    }
                    y += 6;
                    bool earned = _mine.Cards.Contains(c.Id); // revealed but not earned: how to earn it
                    UiKit.At(UiKit.Label("StoryHead", rt, earned ? "이야기 속에서" : "얻는 법", 15, ArchiveUi.Tone, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, 22, y, cw - 44, 22);
                    y += 28;
                    ArchiveUi.Para(rt, "Story", earned ? c.Earned : c.Hint, 16, Terminal.Tint(Terminal.Ink, 0.8f), 22, y, cw - 44);
                    var src = ArchiveUi.Sources(facts, c.Facts);
                    if (src != null)
                    {
                        var s = UiKit.Label("Source", rt, src, 13, Terminal.Muted, TextAnchor.LowerLeft);
                        UiKit.At(s.rectTransform, 22, ph - 64, cw - 44, 52);
                        s.verticalOverflow = VerticalWrapMode.Overflow;
                    }
                }
                var b = plate.gameObject.AddComponent<Button>(); b.targetGraphic = plate; b.navigation = new Navigation { mode = Navigation.Mode.None };
                int k = i;
                b.onClick.AddListener(() => { if (_sel % n != k) { _sel = k; UiSound.Play("tick"); Render(); SpeakCard(); } });
                UiAppear.On(rt.gameObject, 0.04f + i * 0.04f);
            }
        }

        static Color NumberColor(string id) => id switch { "c112" => Terminal.Sky, "c119" => Terminal.Red, "c1338" => Terminal.Amber, _ => Terminal.Emerald };

        void SpeakCard() => _voice.Say(CardSpeech());

        string CardSpeech()
        {
            var c = Archive.Cards[_sel % Archive.Cards.Count];
            if (!_p.Cards.Contains(c.Id)) return $"{c.Number}. 아직 모으지 않은 카드. {Archive.HintFor(c, _chapter)}";
            var f = TerminalData.Facts;
            return $"{c.Number}, {c.Title}. " + string.Join(" ", c.Facts.Select(f.Text).Where(t => t != null));
        }

        protected override void Update()
        {
            base.Update();
            if (!IsTop || Time.unscaledTime - OpenedAt < 0.2f) return;
            var es = EventSystem.current;
            if (es && es.currentSelectedGameObject) es.SetSelectedGameObject(null);
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            int move = 0;
            if (kb != null)
            {
                if (kb.tabKey.wasPressedThisFrame) { SetTab(_tab + (kb.shiftKey.isPressed ? -1 : 1)); return; }
                if (kb.rKey.wasPressedThisFrame && !MidRun) { ToggleReveal(); return; }
                move += (kb.downArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame ? 1 : 0) - (kb.upArrowKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame ? 1 : 0);
            }
            if (gp != null)
            {
                if (gp.leftShoulder.wasPressedThisFrame) { SetTab(_tab - 1); return; }
                if (gp.rightShoulder.wasPressedThisFrame) { SetTab(_tab + 1); return; }
                if (gp.buttonNorth.wasPressedThisFrame && !MidRun) { ToggleReveal(); return; }
                if (gp.buttonEast.wasPressedThisFrame) { Finish("close"); return; }
                move += (gp.dpad.down.wasPressedThisFrame || gp.dpad.right.wasPressedThisFrame ? 1 : 0) - (gp.dpad.up.wasPressedThisFrame || gp.dpad.left.wasPressedThisFrame ? 1 : 0);
            }
            if (move == 0) return;
            int n = _tab == 1 ? Archive.Cards.Count : _tab == 2 ? RumorAnatomy.Techniques.Count : Archive.Entries.Count;
            _sel = ((_sel % n) + move + n) % n;
            UiSound.Play("tick");
            Render();
            if (_tab == 1) SpeakCard(); else if (_tab == 2) SpeakTrick(); else SpeakEntry();
        }

        // ---------- 루머 수법 (루머 해부) ----------

        void TricksPage()
        {
            float ph = BH - TopY - Foot;
            var list = RumorAnatomy.Techniques;
            int n = list.Count;
            float cw = (BW - Pad * 2 - Gap * (n - 1)) / n;
            var facts = TerminalData.Facts;
            for (int i = 0; i < n; i++)
            {
                var t = list[i];
                bool got = _p.Techniques.Contains(t.Id), on = i == _sel % n;
                var plate = UiKit.Box("Trick" + t.Id, _page, on ? Terminal.PlateHi : Terminal.Plate, raycast: true); HudTheme.Round(plate);
                var rt = plate.rectTransform;
                UiKit.At(rt, Pad + i * (cw + Gap), 0, cw, ph);
                Color tone = got ? Terminal.Amber : Terminal.Tint(Terminal.Muted, 0.6f);
                UiKit.At(UiKit.Box("Tone", rt, tone).rectTransform, 14, 0, cw - 28, 4);
                ArchiveUi.Badge(rt, got ? "phone" : "lock", tone, 22, 24, 56);
                float y = 96;
                UiKit.At(UiKit.Label("Name", rt, t.Name, 34, got ? Terminal.Ink : Terminal.Tint(Terminal.Muted, 0.75f), TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, 22, y, cw - 44, 56);
                y += 62;
                UiKit.At(UiKit.Box("Rule", rt, Terminal.Rule).rectTransform, 22, y, cw - 44, 1);
                y += 16;
                if (!got)
                {
                    UiKit.At(UiKit.Label("Locked", rt, "아직 짚어 보지 않은 수법", 18, Terminal.Ink, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, 22, y, cw - 44, 28);
                    y += 34;
                    ArchiveUi.Para(rt, "Hint", TrickHint, 17, Terminal.Muted, 22, y, cw - 44);
                }
                else
                {
                    y += ArchiveUi.Para(rt, "What", t.What, 18, Terminal.Ink, 22, y, cw - 44) + 16;
                    UiKit.At(UiKit.Label("AskHead", rt, "스스로 묻기", 15, Terminal.Amber, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, 22, y, cw - 44, 22);
                    y += 28;
                    y += ArchiveUi.Para(rt, "Ask", t.Ask, 18, Terminal.Tint(Terminal.Ink, 0.9f), 22, y, cw - 44) + 16;
                    var (post, mark) = RumorAnatomy.Example(t.Id, _p.Posts);
                    if (post != null)
                    {
                        UiKit.At(UiKit.Label("ExHead", rt, "이야기 속 예", 15, ArchiveUi.Tone, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, 22, y, cw - 44, 22);
                        y += 28;
                        y += ArchiveUi.Para(rt, "Example", "<color=#F2B33D><b>“" + mark.Phrase + "”</b></color>  " + post.Where, 16, Terminal.Tint(Terminal.Ink, 0.8f), 22, y, cw - 44) + 16;
                    }
                    UiKit.At(UiKit.Label("HabitHead", rt, "막는 습관", 15, Terminal.Emerald, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, 22, y, cw - 44, 22);
                    y += 28;
                    var habit = facts.Text(RumorAnatomy.HabitFacts[0]);
                    y += ArchiveUi.Para(rt, "Habit", habit ?? RumorAnatomy.Habit, 16, Terminal.Ink, 22, y, cw - 44) + 10;
                    ArchiveUi.Para(rt, "Channels", RumorAnatomy.Channels, 15, Terminal.Tint(Terminal.Emerald, 0.85f), 22, y, cw - 44);
                    var src = ArchiveUi.Sources(facts, RumorAnatomy.HabitFacts.Take(1));
                    if (src != null)
                    {
                        var sl = UiKit.Label("Source", rt, src, 13, Terminal.Muted, TextAnchor.LowerLeft);
                        UiKit.At(sl.rectTransform, 22, ph - 64, cw - 44, 52);
                        sl.verticalOverflow = VerticalWrapMode.Overflow;
                    }
                }
                var b = plate.gameObject.AddComponent<Button>(); b.targetGraphic = plate; b.navigation = new Navigation { mode = Navigation.Mode.None };
                int k = i;
                b.onClick.AddListener(() => { if (_sel % n != k) { _sel = k; UiSound.Play("tick"); Render(); SpeakTrick(); } });
                UiAppear.On(rt.gameObject, 0.04f + i * 0.04f);
            }
        }

        /// <summary>How a technique is collected (shown while locked).</summary>
        public const string TrickHint = "이야기 속 소문을 확인하거나 바로잡으면, 그 글을 해부하는 카드가 나옵니다.";

        void SpeakTrick() => _voice.Say(TrickSpeech());

        string TrickSpeech()
        {
            var t = RumorAnatomy.Techniques[_sel % RumorAnatomy.Techniques.Count];
            if (!_p.Techniques.Contains(t.Id)) return $"{t.Name}. 아직 짚어 보지 않은 수법. {TrickHint}";
            var (post, mark) = RumorAnatomy.Example(t.Id, _p.Posts);
            return $"{t.Name}. {t.What} 스스로 묻기: {t.Ask}" + (post != null ? $" 이야기 속 예: {mark.Phrase}, {post.Where}." : "");
        }
    }

    // =====================================================================================================
    // 연무동 지도와 사람들
    // =====================================================================================================
    public class TownScreen : UiScreen
    {
        public const float W = 1760, H = 1000;
        const float Pad = 28, Gap = 20, MapW = 900, MapH = 760, Px = 1.1f;

        ArchiveProgress _p;
        string _chapter;
        int _sel;
        MapProjector _proj;
        RectTransform _ring, _list;
        ScrollRect _scroll;
        readonly List<(Image img, Color c)> _rows = new();
        readonly HomePrepVoice _voice = new();

        public static async Task Open(ArchiveProgress p, string chapter = null)
        {
            await Show<TownScreen>(s => { s._p = p ?? new ArchiveProgress(); s._chapter = chapter; }, W, H);
        }

        /// <summary>Mid-run, a neighbour the story has not reached yet stays unnamed (no spoiler of the chapters ahead).</summary>
        bool Hidden(Resident r) => !_p.Residents.ContainsKey(r.Id) && Archive.Ahead(r.Ch, _chapter);

        protected override object EscapeResult() => "close";

        static Color KindColor(string kind) => kind switch
        {
            "home" => Terminal.Sky, "shelter" => Terminal.Emerald, "water" => Terminal.Hex("#38BDF8"), "hill" => Terminal.Hex("#84CC16"),
            "road" => Terminal.Red, "work" => Terminal.Hex("#C084FC"), _ => Terminal.Amber,
        };

        protected override void Build()
        {
            var body = Terminal.Frame(Window, "연무동 지도와 사람들", "가상의 동네 · 지난 이야기에 따라 바뀝니다", ArchiveUi.Tone);
            float bw = W - Terminal.Inset * 2, bh = H - Terminal.Inset * 2 - Terminal.HeaderH - 2;
            const float top = 20, foot = 76;

            // ---- the map: the town as the route map draws it (no labels), then the archive's pins ----
            var frame = UiKit.Box("MapFrame", body, Terminal.Tint(ArchiveUi.Tone, 0.35f)).rectTransform;
            UiKit.At(frame, Pad - 1, top - 1, MapW + 2, MapH + 2);
            var map = UiKit.Box("Map", body, Terminal.Plate).rectTransform;
            UiKit.At(map, Pad, top, MapW, MapH);
            map.gameObject.AddComponent<RectMask2D>();
            _proj = MapProjector.Fit(MapW, MapH);
            var baseLayer = UiKit.Rect("Base", map); UiKit.At(baseLayer, 0, 0, MapW, MapH);
            MapPainter.Paint(baseLayer, RoutemapLogic.BaseLayer(_proj, Array.Empty<string>(), Px, labels: false));
            var pins = UiKit.Rect("Pins", map); UiKit.At(pins, 0, 0, MapW, MapH);
            _ring = UiKit.Rect("Ring", pins);
            var ringImg = MapPainter.Disc("RingDisc", _ring, Terminal.Tint(Terminal.Ink, 0.22f));
            UiKit.Fill(ringImg.rectTransform);
            for (int i = 0; i < Archive.Pins.Count; i++) Pin(pins, Archive.Pins[i], i + 1);
            var north = UiKit.Label("North", map, "▲ 북", 16, Terminal.Muted, TextAnchor.MiddleRight, FontStyle.Bold);
            UiKit.At(north.rectTransform, MapW - 90, 10, 76, 24);

            // ---- the residents ----
            float lx = Pad + MapW + Gap, lw = bw - lx - Pad;
            UiKit.At(UiKit.Label("Head", body, "연무동 사람들", 22, Terminal.Ink, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, lx, top, lw, 32);
            UiKit.At(UiKit.Label("Sub", body, "가상의 인물입니다. 아래 줄은 그 이웃을 가장 최근에 만난 내 이야기에서 일어난 일입니다.", 15, Terminal.Muted, TextAnchor.MiddleLeft).rectTransform, lx, top + 34, lw, 24);
            _list = DebriefUi.Scroll(body, lx, top + 66, lw, MapH - 66, out _scroll, 8);
            for (int i = 0; i < Archive.Residents.Count; i++) Row(Archive.Residents[i], i);

            var legend = UiKit.Label("Legend", body, "사람을 누르거나 ↑↓로 고르면 그 사람이 있는 곳이 지도에서 밝아집니다. 뒷산 산책로는 지도 위쪽 바깥입니다.", 15, Terminal.Muted, TextAnchor.MiddleLeft);
            UiKit.At(legend.rectTransform, Pad, bh - foot + 22, bw - Pad * 2 - 280, 30);
            var close = Terminal.Button(body, "닫기  ·  Esc", ArchiveUi.Tone, true);
            UiKit.At((RectTransform)close.transform, bw - Pad - 240, bh - foot + 16, 240, 48);
            close.onClick.AddListener(() => Finish("close"));
            Select(0, speak: false);
            _voice.Say("연무동 지도와 사람들. " + string.Join(". ", Archive.Pins.Select(p => p.Label)));
        }

        (float x, float y) PinXY(MapPin pin)
        {
            var (x, y) = _proj.P(pin.X, pin.Z);
            if (pin.Edge) y = Mathf.Max(y, 34);
            return (Mathf.Clamp(x, 20, MapW - 20), Mathf.Clamp(y, 20, MapH - 20));
        }

        void Pin(RectTransform parent, MapPin pin, int no)
        {
            var (x, y) = PinXY(pin);
            Color c = KindColor(pin.Kind);
            if (pin.Edge)
            {
                var arrow = UiKit.Label("Arrow", parent, "▲", 20, c, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.At(arrow.rectTransform, x - 14, y - 34, 28, 22);
            }
            var dot = MapPainter.Disc("Pin" + pin.Id, parent, c);
            UiKit.At(dot.rectTransform, x - 14, y - 14, 28, 28);
            UiKit.Fill(UiKit.Label("No", dot.transform, no.ToString(), 15, Terminal.Bg, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform);
            var t = UiKit.Label("Label", parent, pin.Label, 15, Terminal.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            float tw = Mathf.Ceil(t.preferredWidth);
            bool below = pin.Side == "below", left = !below && (pin.Side == "left" || x + 22 + tw > MapW - 6);
            float lx = below ? Mathf.Clamp(x - tw / 2 - 4, 6, MapW - tw - 14) : left ? x - 22 - tw - 8 : x + 20;
            float ly = below ? y + 30 : y;
            var pill = ScreenDecor.Rounded("Pill", parent, new Color(0.04f, 0.06f, 0.1f, 0.82f));
            UiKit.At(pill.rectTransform, lx - 4, ly - 13, tw + 16, 26);
            UiKit.At(t.rectTransform, lx + 4, ly - 13, tw + 4, 26);
            t.transform.SetAsLastSibling();
        }

        void Row(Resident r, int i)
        {
            var card = DebriefUi.Card(_list, Terminal.Plate, 18, 12, 4);
            var img = card.GetComponent<Image>(); HudTheme.Round(img); img.raycastTarget = true;
            var pin = Archive.Pin(r.Pin);
            if (Hidden(r))
            {
                UiKit.Label("Name", card, $"<color={Terminal.Rgb(Terminal.Muted)}>●</color> <b>아직 만나지 않은 이웃</b>", 20, Terminal.Tint(Terminal.Ink, 0.7f));
                UiKit.Label("None", card, Archive.AheadHint.Replace("열립니다", "만납니다"), 15, Terminal.Muted);
                AddRow(card, img, i);
                return;
            }
            string no = pin != null ? $"<color={Terminal.Rgb(KindColor(pin.Kind))}>●</color> " : "";
            UiKit.Label("Name", card, $"{no}<b>{r.Name}</b>   <size=15><color={Terminal.Rgb(Terminal.Muted)}>{r.Role}</color></size>", 20, Terminal.Ink);
            UiKit.Label("Profile", card, r.Profile, 16, Terminal.Tint(Terminal.Ink, 0.82f));
            if (_p.Residents.TryGetValue(r.Id, out var lines) && lines.Count > 0)
                foreach (var l in lines) UiKit.Label("Line", card, $"<color={Terminal.Rgb(ArchiveUi.Tone)}>▸</color> {l}", 16, Terminal.Ink);
            else UiKit.Label("None", card, "아직 내 이야기에서 함께한 일이 없습니다.", 15, Terminal.Muted);
            AddRow(card, img, i);
        }

        void AddRow(RectTransform card, Image img, int i)
        {
            var b = card.gameObject.AddComponent<Button>(); b.targetGraphic = img; b.navigation = new Navigation { mode = Navigation.Mode.None };
            int k = i;
            b.onClick.AddListener(() => { UiSound.Play("tick"); Select(k); });
            _rows.Add((img, img.color));
            UiAppear.On(card.gameObject, 0.05f + i * 0.03f, slide: false);
        }

        void Select(int i, bool speak = true)
        {
            _sel = Mathf.Clamp(i, 0, Archive.Residents.Count - 1);
            for (int k = 0; k < _rows.Count; k++) _rows[k].img.color = k == _sel ? Terminal.PlateHi : _rows[k].c;
            var r = Archive.Residents[_sel];
            var pin = Hidden(r) ? null : Archive.Pin(r.Pin);
            // keyboard and pad: keep the chosen row in view (rows 7 and 8 sit below the fold)
            if (_scroll && _rows.Count > 1) _scroll.verticalNormalizedPosition = 1f - _sel / (float)(_rows.Count - 1);
            if (pin != null)
            {
                var (x, y) = PinXY(pin);
                UiKit.At(_ring, x - 26, y - 26, 52, 52);
                _ring.gameObject.SetActive(true);
            }
            else _ring.gameObject.SetActive(false);
            if (speak)
            {
                if (Hidden(r)) { _voice.Say("아직 만나지 않은 이웃. " + Archive.AheadHint.Replace("열립니다", "만납니다")); return; }
                var lines = _p.Residents.TryGetValue(r.Id, out var l) ? string.Join(". ", l) : "아직 함께한 일이 없습니다";
                _voice.Say($"{r.Name}, {r.Role}. {r.Profile} {lines}");
            }
        }

        protected override void Update()
        {
            base.Update();
            if (!IsTop || Time.unscaledTime - OpenedAt < 0.2f) return;
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            int move = 0;
            if (kb != null) move += (kb.downArrowKey.wasPressedThisFrame ? 1 : 0) - (kb.upArrowKey.wasPressedThisFrame ? 1 : 0);
            if (gp != null)
            {
                if (gp.buttonEast.wasPressedThisFrame) { Finish("close"); return; }
                move += (gp.dpad.down.wasPressedThisFrame ? 1 : 0) - (gp.dpad.up.wasPressedThisFrame ? 1 : 0);
            }
            if (move != 0) Select(_sel + move);
        }
    }

    // =====================================================================================================
    // 훈련 달력
    // =====================================================================================================
    public class DrillCalendarScreen : UiScreen
    {
        public const float W = 1320, H = 700;
        readonly HomePrepVoice _voice = new();

        public static async Task Open() { await Show<DrillCalendarScreen>(null, W, H); }

        protected override object EscapeResult() => "close";

        protected override void Build()
        {
            var body = Terminal.Frame(Window, "훈련 달력", "2026년 계획 · 날짜는 해마다 달라요", Terminal.Amber);
            float bw = W - Terminal.Inset * 2, bh = H - Terminal.Inset * 2 - Terminal.HeaderH - 2;
            const float pad = 34, gap = 8;
            float y = 24;
            float cw = (bw - pad * 2 - gap * 11) / 12, ch = 132;
            for (int m = 1; m <= 12; m++)
            {
                var plan = Archive.DrillMonths.FirstOrDefault(d => d.Month == m);
                int span = plan == null ? 1 : Mathf.Clamp(plan.Span, 1, 13 - m); // 10~11월: one drill, one cell over both months
                float w = cw * span + gap * (span - 1);
                Color tone = plan == null ? Terminal.Muted : plan.Main ? Terminal.Amber : ArchiveUi.Tone;
                var cell = UiKit.Box("M" + m, body, plan == null ? Terminal.Plate : Terminal.Tint(tone, plan.Main ? 0.24f : 0.14f)); HudTheme.Round(cell);
                var rt = cell.rectTransform;
                UiKit.At(rt, pad + (m - 1) * (cw + gap), y, w, ch);
                UiKit.At(UiKit.Box("Top", rt, plan == null ? Terminal.Rule : tone).rectTransform, 0, 0, w, 4);
                string month = span > 1 ? $"{m}~{m + span - 1}월" : m + "월";
                UiKit.At(UiKit.Label("Month", rt, month, 24, plan == null ? Terminal.Muted : Terminal.Ink, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform, 0, 12, w, 34);
                if (plan != null)
                {
                    var t = UiKit.Label("Label", rt, plan.Label, 13, Terminal.Ink, TextAnchor.UpperCenter, plan.Main ? FontStyle.Bold : FontStyle.Normal);
                    UiKit.At(t.rectTransform, 4, 48, w - 8, 50);
                    if (!string.IsNullOrEmpty(plan.Who))
                    {
                        var who = UiKit.Label("Who", rt, plan.Who, 12, plan.Main ? Terminal.Amber : Terminal.Tint(Terminal.Ink, 0.75f), TextAnchor.MiddleCenter, FontStyle.Bold);
                        UiKit.At(who.rectTransform, 2, ch - 30, w - 4, 24);
                    }
                }
                UiAppear.On(rt.gameObject, 0.03f * m, slide: false);
                m += span - 1;
            }
            y += ch + 28;

            var facts = TerminalData.Facts;
            foreach (var id in Archive.CalendarFacts)
            {
                var text = facts.Text(id);
                if (string.IsNullOrEmpty(text)) continue;
                var ic = Terminal.Icon(body, id == "alert_levels" ? "siren" : "calendar", id == "alert_levels" ? Terminal.Amber : ArchiveUi.Tone, 20);
                UiKit.At(ic.rectTransform, pad, y + 3, 20, 20);
                y += ArchiveUi.Para(body, "Fact", text, 19, Terminal.Ink, pad + 34, y, bw - pad * 2 - 34) + 14;
            }
            y += 4;
            var note = UiKit.Box("Note", body, Terminal.Tint(Terminal.Amber, 0.14f)); HudTheme.Round(note);
            var nt = UiKit.Label("T", note.transform, "<b>매년 행안부 공지 확인</b>   " + Archive.CalendarNote, 18, Terminal.Ink, TextAnchor.MiddleLeft);
            UiKit.At(note.rectTransform, pad, y, bw - pad * 2, 64);
            UiKit.Fill(nt.rectTransform, 18, 6);
            var src = ArchiveUi.Sources(facts, Archive.CalendarFacts);
            if (src != null) UiKit.At(UiKit.Label("Source", body, src, 14, Terminal.Muted, TextAnchor.MiddleLeft).rectTransform, pad, bh - 64, bw - pad * 2 - 280, 40);
            var close = Terminal.Button(body, "닫기  ·  Esc", Terminal.Amber, true);
            UiKit.At((RectTransform)close.transform, bw - pad - 240, bh - 70, 240, 50);
            close.onClick.AddListener(() => Finish("close"));
            _voice.Say("훈련 달력. " + string.Join(". ", Archive.DrillMonths.Select(d => $"{(d.Span > 1 ? $"{d.Month}~{d.Month + d.Span - 1}" : d.Month.ToString())}월, {d.Label}, 대상 {d.Who}"))
                + ". " + string.Join(" ", Archive.CalendarFacts.Select(facts.Text).Where(t => !string.IsNullOrEmpty(t))) + " " + Archive.CalendarNote);
        }

        protected override void Update()
        {
            base.Update();
            if (!IsTop || Time.unscaledTime - OpenedAt < 0.2f) return;
            var gp = Gamepad.current;
            if (gp != null && gp.buttonEast.wasPressedThisFrame) Finish("close");
        }
    }
}
