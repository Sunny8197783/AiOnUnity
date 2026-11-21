// 사전/사후 퀴즈 (competition upgrade): the terminal that asks QuizLogic's seven questions. Pages: the offer (풀기 /
// 건너뛰기) -> one question at a time (no answer shown) -> the pre quiz's "마쳤습니다" or, after the post quiz, the result:
// per concept 사전 → 사후 (몰랐음 → 앎 …), the answer given, the right one, and the facts that say it with who published
// them. The result page can write the 플레이테스트 기록 (PlaytestExport) to Application.persistentDataPath/exports and
// shows where. Keys: 1-9 or ↑/↓ + Enter/Space, Backspace = 이전 문항, PgUp/PgDn scroll; pad d-pad + A, B = 이전.
// No timer. Low vision: each page is read aloud (HostSettings.Speak). Reduce motion: nothing moves but the window.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    public class QuizScreen : UiScreen
    {
        public const float W = 1320, H = 900;

        public static void Register(AiOn.Story.UnityStoryHost host) => host.Screens[QuizLogic.Screen] = Open;

        /// <summary>
        /// ui.quiz({ phase: "pre"|"post", seed, profile, pre: answers json (post), export: PlaytestExport base (post), mode,
        /// concepts: the concept ids to ask (a class; default all), exportSeed: the export's anonymous id seed })
        /// -> the answers json (QuizAnswers.ToJson) or { phase, skipped: true }.
        /// </summary>
        public static async Task<JToken> Open(JObject o)
        {
            var r = await Show<QuizScreen>(s => s._o = o ?? new JObject(), W, H);
            return r as JToken ?? new JObject { ["phase"] = (string)(o?["phase"]) ?? QuizLogic.Pre, ["skipped"] = true };
        }

        enum Page { Intro, Question, PreDone, Result }

        JObject _o;
        string _phase;
        uint _seed;
        bool _lowVision;
        List<QuizLogic.Question> _qs;
        QuizAnswers _answers, _pre;
        int _index;
        Page _page;
        RectTransform _body, _content;
        Text _status;
        ScrollRect _scroll;
        string _exportedPath;
        bool _teacher;
        float _pageAt;
        /// <summary>The results page ignores Enter / Space / A this long: a press meant for the last question must not skip it.</summary>
        const float ResultHold = 1.2f;

        protected override object EscapeResult() => _page == Page.Intro ? Skipped() : null; // mid-quiz Esc does nothing

        JObject Skipped() => new JObject { ["phase"] = _phase, ["skipped"] = true };

        protected override void Build()
        {
            _phase = (string)_o["phase"] == QuizLogic.Post ? QuizLogic.Post : QuizLogic.Pre;
            _seed = _o["seed"] != null && _o["seed"].Type == JTokenType.Integer ? unchecked((uint)(long)_o["seed"]) : 1u;
            _lowVision = (string)_o["profile"] == "lowvision";
            _pre = QuizAnswers.Read(_o["pre"]);
            _teacher = (string)_o["mode"] == "teacher";
            var only = _o["concepts"] is JArray ca && ca.Count > 0 ? ca.Select(x => (string)x).Where(x => QuizLogic.Item(x) != null).ToList() : null;
            _qs = QuizLogic.Questions(_phase, QuizLogic.SeedFor(_seed, _phase), only != null && only.Count > 0 ? only : null);
            _answers = new QuizAnswers { Phase = _phase, Seed = _seed };
            _body = Terminal.Frame(Window, QuizLogic.IntroTitle(_phase) + $" · {_qs.Count}문항",
                _phase == QuizLogic.Post ? "이야기를 마친 뒤 · 다시 확인" : "시작하기 전 · 지금 아는 만큼", _phase == QuizLogic.Post ? Terminal.Emerald : Terminal.Amber);
            Show(Page.Intro);
        }

        float BW => W - Terminal.Inset * 2;
        float BH => H - Terminal.Inset * 2 - Terminal.HeaderH - 2;
        const float Pad = 40;

        // ---------------------------------------------------------------- pages

        readonly List<(Button b, Action act, Image img, Color c)> _nav = new();
        int _cursor = -1, _primary = -1;

        void Show(Page p)
        {
            _page = p;
            _pageAt = Time.unscaledTime;
            Clear(_body);
            _nav.Clear(); _cursor = -1; _primary = -1; _scroll = null; _status = null;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            var spoken = new System.Text.StringBuilder();
            switch (p)
            {
                case Page.Intro: Intro(spoken); break;
                case Page.Question: Question(spoken); break;
                case Page.PreDone: PreDone(spoken); break;
                default: Result(spoken); break;
            }
            var keys = UiKit.Label("Keys", _body, KeysLine(), 16, Terminal.Muted, TextAnchor.MiddleLeft);
            UiKit.At(keys.rectTransform, Pad, BH - 40, BW - Pad * 2, 28);
            if (_lowVision) Speak(spoken.ToString());
        }

        string KeysLine()
        {
            bool pad = Gamepad.current != null;
            return _page switch
            {
                Page.Question => "1~9 또는 ↑↓ + Enter · 클릭   Backspace 이전 문항" + (pad ? "   게임패드: 십자키 + A, B 이전" : "") + "   시간 제한 없음",
                Page.Result => "↑↓ + Enter · 클릭   PgUp/PgDn 스크롤" + (pad ? "   게임패드: 십자키 + A, 오른쪽 스틱 스크롤" : ""),
                _ => "1~9 또는 ↑↓ + Enter · 클릭" + (pad ? "   게임패드: 십자키 + A" : "") + (_page == Page.Intro ? "   Esc 건너뛰기" : ""),
            };
        }

        RectTransform Column(float x, float y, float w, float h, float spacing = 14)
        {
            var col = UiKit.Rect("Column", _body);
            UiKit.At(col, x, y, w, h);
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing; v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = true; v.childForceExpandWidth = true; v.childControlHeight = true; v.childForceExpandHeight = false;
            return col;
        }

        Button Choice(Transform parent, string label, Action act, bool primary = false, int size = 24)
        {
            int n = _nav.Count + 1;
            var bg = primary ? Terminal.Tint(_phase == QuizLogic.Post ? Terminal.Emerald : Terminal.Amber, 1f) : Terminal.PlateHi;
            var img = UiKit.Box("Choice", parent, bg, raycast: true);
            HudTheme.Round(img);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            var lay = img.gameObject.AddComponent<VerticalLayoutGroup>();
            lay.padding = new RectOffset(24, 24, 16, 16);
            lay.childAlignment = TextAnchor.MiddleLeft;
            lay.childControlWidth = true; lay.childForceExpandWidth = true; lay.childControlHeight = true; lay.childForceExpandHeight = false;
            img.gameObject.AddComponent<LayoutElement>().minHeight = 64;
            string mark = n <= 9 ? $"<color={(primary ? "#0B0F19" : Terminal.Rgb(Terminal.Muted))}>{n}</color>   " : "";
            UiKit.Label("Text", img.transform, mark + label, size, primary ? Terminal.Bg : Terminal.Ink, TextAnchor.MiddleLeft, primary ? FontStyle.Bold : FontStyle.Normal);
            var trig = img.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => UiSound.Play("tick"));
            trig.triggers.Add(enter);
            b.onClick.AddListener(() => { UiSound.Play("click"); act(); });
            if (primary && _primary < 0) _primary = _nav.Count;
            _nav.Add((b, act, img, bg));
            return b;
        }

        void Badge(string icon, Color tone, float x, float y)
        {
            var badge = UiKit.Box("Badge", _body, Terminal.Tint(tone, 0.16f)); badge.sprite = HudDraw.DiscSprite;
            UiKit.At(badge.rectTransform, x, y, 72, 72);
            var ic = Terminal.Icon(badge.transform, icon, tone, 38);
            var irt = ic.rectTransform; irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f); irt.anchoredPosition = Vector2.zero;
        }

        void Intro(System.Text.StringBuilder spoken)
        {
            Color tone = _phase == QuizLogic.Post ? Terminal.Emerald : Terminal.Amber;
            Badge(_phase == QuizLogic.Post ? "check" : "radio", tone, Pad, 56);
            string title = _phase == QuizLogic.Post ? (_pre != null ? "처음에 풀었던 내용을 다시 확인해요" : "이번 체험에서 배운 것을 확인해요") : "시작하기 전에, 지금 아는 만큼";
            UiKit.At(UiKit.Label("Title", _body, title, 36, Terminal.Ink, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, Pad + 96, 60, BW - Pad * 2 - 96, 64);
            var col = Column(Pad, 168, BW - Pad * 2, BH - 168 - 60, 18);
            string body = QuizLogic.IntroBody(_phase, _pre != null, _qs.Count, _teacher);
            UiKit.Label("Body", col, body, 24, Terminal.Ink).lineSpacing = 1.15f;
            // what it asks about: neutral topic names (the labels would give answers away: "엘리베이터 대신 계단")
            UiKit.Label("Topics", col, "다루는 것 · " + string.Join(" · ", _qs.Select(q => q.Item.Topic)), 18, Terminal.Muted);
            UiKit.Label("Note", col, QuizLogic.IntroNote, 18, Terminal.Muted);
            var gap = UiKit.Rect("Gap", col); gap.gameObject.AddComponent<LayoutElement>().minHeight = 16;
            Choice(col, "풀기 ▶", () => { _index = 0; Show(Page.Question); }, primary: true);
            Choice(col, "건너뛰기", () => Finish(Skipped()));
            spoken.Append(title).Append(". ").Append(body).Append(' ').Append(QuizLogic.IntroNote).Append(" 1번, 풀기. 2번, 건너뛰기.");
        }

        void Question(System.Text.StringBuilder spoken)
        {
            var q = _qs[_index];
            int n = _qs.Count;
            UiKit.At(UiKit.Label("Count", _body, $"문항 {_index + 1} / {n}", 20, Terminal.Muted, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, Pad, 30, 300, 28);
            var track = UiKit.Box("Track", _body, Terminal.Rule).rectTransform; UiKit.At(track, Pad, 66, BW - Pad * 2, 6);
            var fill = UiKit.Box("Fill", track, _phase == QuizLogic.Post ? Terminal.Emerald : Terminal.Amber).rectTransform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2((_index + 1f) / n, 1); fill.offsetMin = fill.offsetMax = Vector2.zero;
            var col = Column(Pad, 100, BW - Pad * 2, BH - 100 - 120, 14);
            var prompt = UiKit.Label("Prompt", col, q.Prompt, 32, Terminal.Ink, style: FontStyle.Bold);
            prompt.lineSpacing = 1.12f;
            var gap = UiKit.Rect("Gap", col); gap.gameObject.AddComponent<LayoutElement>().minHeight = 10;
            spoken.Append($"문항 {_index + 1}. ").Append(EasyKorean.Of(q.Prompt)).Append(' ');
            string was = _answers.Pick(q.Item.Id);
            int i = 0;
            foreach (var opt in q.Options)
            {
                var id = opt.Id;
                Choice(col, opt.Text + (was == id ? "   <color=#94A3B8>(고른 답)</color>" : ""), () => Answer(q, id), size: opt.Id == QuizLogic.Unsure ? 21 : 23);
                spoken.Append(++i).Append("번, ").Append(opt.Text).Append(". ");
            }
            if (_index > 0)
            {
                var back = Terminal.Button(_body, "◀ 이전 문항", Terminal.Muted, false);
                UiKit.At((RectTransform)back.transform, Pad, BH - 104, 220, 48);
                back.onClick.AddListener(Back);
            }
        }

        void Answer(QuizLogic.Question q, string id)
        {
            _answers.Picks[q.Item.Id] = id;
            if (!_answers.Order.Contains(q.Item.Id)) _answers.Order.Add(q.Item.Id);
            if (_index + 1 < _qs.Count) { _index++; Show(Page.Question); return; }
            if (_phase == QuizLogic.Pre) Show(Page.PreDone);
            else Show(Page.Result);
        }

        void Back()
        {
            if (_page != Page.Question || _index == 0) return;
            UiSound.Play("click");
            _index--; Show(Page.Question);
        }

        void PreDone(System.Text.StringBuilder spoken)
        {
            Badge("check", Terminal.Amber, Pad, 56);
            UiKit.At(UiKit.Label("Title", _body, QuizLogic.PreDoneTitle, 36, Terminal.Ink, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, Pad + 96, 60, BW - Pad * 2 - 96, 64);
            var col = Column(Pad, 168, BW - Pad * 2, BH - 168 - 60, 18);
            string doneBody = QuizLogic.PreDoneBody(_teacher);
            UiKit.Label("Body", col, doneBody, 24, Terminal.Ink).lineSpacing = 1.15f;
            int unsure = _answers.Picks.Values.Count(v => v == QuizLogic.Unsure);
            if (unsure > 0) UiKit.Label("Unsure", col, $"'잘 모르겠어요'를 고른 문항 {unsure}개 · {(_teacher ? "수업" : "이야기")} 속에서 만나게 됩니다.", 19, Terminal.Muted);
            var gap = UiKit.Rect("Gap", col); gap.gameObject.AddComponent<LayoutElement>().minHeight = 16;
            Choice(col, "계속 ▶", () => Finish(_answers.ToJson()), primary: true);
            spoken.Append(QuizLogic.PreDoneTitle).Append(". ").Append(doneBody);
        }

        static Color ChangeColor(string change) => change switch
        {
            "learned" => Terminal.Emerald,
            "kept" or "postRight" => Terminal.Sky,
            "slipped" => Terminal.Amber,
            _ => Terminal.Red,
        };

        /// <summary>What changed first: newly learned, then what slipped or is still to review, then what was already known.</summary>
        static int ResultOrder(string change) => change switch { "learned" => 0, "slipped" => 1, "missed" => 2, "postWrong" => 3, "kept" => 4, _ => 5 };

        void Result(System.Text.StringBuilder spoken)
        {
            var cmp = QuizLogic.Compare(_pre, _answers);
            string head = QuizLogic.Headline(cmp);
            UiKit.At(UiKit.Label("Head", _body, head, 34, Terminal.Ink, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, Pad, 24, BW - Pad * 2, 50);
            UiKit.At(UiKit.Label("Note", _body, QuizLogic.ResultNote, 17, Terminal.Muted, TextAnchor.MiddleLeft).rectTransform, Pad, 74, BW - Pad * 2, 26);
            spoken.Append(head).Append(". ");
            var facts = TerminalData.Facts;
            _content = DebriefUi.Scroll(_body, Pad - 8, 110, BW - Pad * 2 + 16, BH - 110 - 140, out _scroll, 10);
            foreach (var row in cmp.Rows.OrderBy(r => ResultOrder(r.Change)))
            {
                var item = QuizLogic.Item(row.Concept);
                var card = DebriefUi.Card(_content, Terminal.Plate, 20, 14, 6);
                HudTheme.Round(card.GetComponent<Image>());
                Color c = ChangeColor(row.Change);
                UiKit.Label("Concept", card, $"<b><color={Terminal.Rgb(c)}>[{row.ChangeText}]</color></b>   <b>{row.Label}</b>", 21, Terminal.Ink);
                var ask = item.Post;
                var pick = _answers.Pick(item.Id);
                var picked = pick == QuizLogic.Unsure ? QuizLogic.UnsureText : ask.Options.FirstOrDefault(o => o.Id == pick)?.Text ?? "—";
                bool right = row.Post == "right";
                UiKit.Label("Mine", card, $"<color={Terminal.Rgb(right ? Terminal.Emerald : Terminal.Red)}>{(right ? "✓" : "✕")}</color>  내 답 · {picked}", 18, Terminal.Ink);
                if (!right) UiKit.Label("Right", card, $"<color={Terminal.Rgb(Terminal.Emerald)}>✓</color>  맞는 답 · {ask.Right?.Text}", 18, Terminal.Ink);
                spoken.Append(row.Label).Append(", ").Append(row.ChangeText).Append(". ");
                // low vision hears the payoff too: the right answer and the fact behind it where the answer was not right
                if (!right)
                {
                    spoken.Append("맞는 답, ").Append(ask.Right?.Text).Append(". ");
                    var first = item.Facts.Select(id => facts.Fact(id)).FirstOrDefault(f => f != null);
                    if (first != null) spoken.Append("근거, ").Append(first.Text).Append(' ');
                }
                foreach (var id in item.Facts)
                {
                    var f = facts.Fact(id);
                    if (f == null) continue;
                    string pub = facts.Source(f.SourceId)?.Publisher;
                    UiKit.Label("Fact", card, $"<color={Terminal.Rgb(Terminal.Amber)}>▸</color> {f.Text}" + (string.IsNullOrEmpty(pub) ? "" : $"   <size=14><color={Terminal.Rgb(Terminal.Muted)}>출처 · {pub}</color></size>"), 17, Terminal.Ink);
                }
            }

            _status = UiKit.Label("Status", _body, "기록 내보내기: 이 컴퓨터에 JSON·CSV 파일로만 저장합니다 (네트워크 없음, 이름 없음).", 15, Terminal.Muted, TextAnchor.UpperLeft);
            UiKit.At(_status.rectTransform, Pad, BH - 120, BW - Pad * 2 - 560, 70);
            var export = MakeButton("기록 내보내기", false, Export);
            UiKit.At((RectTransform)export.transform, BW - Pad - 520, BH - 112, 250, 56);
            var done = MakeButton("계속 ▶", true, () => Finish(_answers.ToJson()));
            UiKit.At((RectTransform)done.transform, BW - Pad - 250, BH - 112, 250, 56);
        }

        Button MakeButton(string label, bool primary, Action act)
        {
            var b = Terminal.Button(_body, label, Terminal.Emerald, primary);
            var img = b.GetComponent<Image>();
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => act());
            if (primary && _primary < 0) _primary = _nav.Count;
            _nav.Add((b, act, img, img.color));
            return b;
        }

        void Export()
        {
            try
            {
                uint? exportSeed = _o["exportSeed"] != null && _o["exportSeed"].Type == JTokenType.Integer ? unchecked((uint)(long)_o["exportSeed"]) : (uint?)null;
                var basis = _o["export"] as JObject ?? PlaytestExport.Build(null, (string)_o["mode"] ?? "story", DateTime.UtcNow, seed: exportSeed ?? _seed);
                var data = PlaytestExport.WithQuiz((JObject)basis.DeepClone(), _phase == QuizLogic.Post ? _pre : _answers, _phase == QuizLogic.Post ? _answers : null);
                _exportedPath = PlaytestExport.Write(data, System.IO.Path.Combine(Application.persistentDataPath, PlaytestExport.Folder), DateTime.Now);
                if (_status) { _status.text = PlaytestExport.SavedLine(_exportedPath); _status.color = Terminal.Emerald; }
                if (_lowVision) Speak("기록을 이 컴퓨터에 저장했습니다.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (_status) { _status.text = "저장하지 못했습니다: " + e.GetType().Name; _status.color = Terminal.Red; }
            }
        }

        // ---------------------------------------------------------------- input

        protected override void Update()
        {
            base.Update();
            var es = EventSystem.current;
            if (es && es.currentSelectedGameObject) es.SetSelectedGameObject(null);
            if (Time.unscaledTime - OpenedAt < 0.25f || _nav.Count == 0) return;
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            int move = 0; bool press = false, back = false; float scroll = 0;
            if (kb != null)
            {
                move += (kb.downArrowKey.wasPressedThisFrame ? 1 : 0) - (kb.upArrowKey.wasPressedThisFrame ? 1 : 0);
                press |= kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
                back |= kb.backspaceKey.wasPressedThisFrame;
                scroll += (kb.pageUpKey.wasPressedThisFrame ? 1 : 0) - (kb.pageDownKey.wasPressedThisFrame ? 1 : 0);
                if (_page != Page.Result)
                    for (int i = 0; i < 9 && i < _nav.Count; i++)
                        if (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame) { Press(i); return; }
            }
            if (gp != null)
            {
                move += (gp.dpad.down.wasPressedThisFrame || gp.leftStick.down.wasPressedThisFrame ? 1 : 0) - (gp.dpad.up.wasPressedThisFrame || gp.leftStick.up.wasPressedThisFrame ? 1 : 0);
                press |= gp.buttonSouth.wasPressedThisFrame;
                back |= gp.buttonEast.wasPressedThisFrame;
                if (_scroll) _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition + gp.rightStick.ReadValue().y * Time.unscaledDeltaTime * 0.8f);
            }
            if (scroll != 0 && _scroll) _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition + scroll * 0.25f);
            if (back) { Back(); return; }
            if (move != 0) { SetCursor(_cursor < 0 ? 0 : Mathf.Clamp(_cursor + move, 0, _nav.Count - 1)); return; }
            if (!press) return;
            if (_page == Page.Result && Time.unscaledTime - _pageAt < ResultHold) return; // the facts page is not skipped by a held-over press
            if (_cursor >= 0) Press(_cursor);
            else if (_primary >= 0) Press(_primary);
            else SetCursor(0);
        }

        void Press(int i)
        {
            if (i < 0 || i >= _nav.Count) return;
            UiSound.Play("click");
            _nav[i].act();
        }

        void SetCursor(int i)
        {
            if (_cursor >= 0 && _cursor < _nav.Count && _nav[_cursor].img) _nav[_cursor].img.color = _nav[_cursor].c;
            _cursor = i;
            if (_nav[i].img) _nav[i].img.color = Color.Lerp(_nav[i].c, Terminal.Amber, 0.45f);
        }

        // ---------------------------------------------------------------- speech (low vision)

        AiOn.Settings.HostSettings _host;
        bool _looked;
        string _said;

        void Speak(string text)
        {
            if (string.IsNullOrEmpty(text) || text == _said) return;
            _said = text;
            if (!_looked) { _looked = true; _host = FindFirstObjectByType<AiOn.Settings.HostSettings>(); }
            if (_host) _host.Speak(text);
        }
    }
}
