// 「산에서 내려온 사람들」 · 찜찜한 기억 (competition upgrade, 3장): the report card. Left, what 정민 remembers (the phone
// memo when the traces were noted, else a blurry memory), a sketch of the traces (two trails of footprints of two
// sizes, two tags, a bag; nobody), and the card filling up line by line. Right, the step: how to tell it (from home,
// not by going back) -> the card (언제·어디서·몇 명·복장·이동 방향·남은 물건, one line at a time; "모른다" is an
// honest answer) -> where (112 by call or text) -> 돌아보기 (each line ✓/○/!/✕, the accuracy and when the 통제 lifts,
// a [게임 규칙]). The rules live in AiOn.Game.SightingReport; this class only draws and forwards input.
// Keys: 1-9 or ↑/↓ + Enter/Space, click; the pad's d-pad + A. No timer anywhere (no hold, no rhythm). Low vision: the
// page is read aloud (HostSettings.Speak). Reduce motion: nothing moves here.
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
    public class SightingScreen : UiScreen
    {
        public const string Name = MountainScene.Screen;

        public static void Register(UnityStoryHost host) => host.Screens[Name] = Open;

        /// <summary>ui.sighting(SightSetup json + { facts: { id: { text, source } }, reduceMotion }) -> SightingReport.Result().</summary>
        public static async Task<JToken> Open(JObject o)
        {
            SightingScreen me = null;
            var r = await Show<SightingScreen>(s => { s._o = o ?? new JObject(); me = s; });
            if (r is JToken t) return t;
            return me != null && me.L != null ? me.L.Result() : new JObject { ["played"] = false };
        }

        JObject _o;
        SightingReport L;
        bool _lowVision, _review, _shownReview;
        RectTransform _stage, _side;
        Text _title, _keys;
        IDisposable _moment;
        SightPhase _shown = (SightPhase)(-1);
        int _shownField = -1;

        static readonly Color ButtonBg = new(1, 1, 1, 0.07f);
        static readonly Color Dark = new(0.04f, 0.08f, 0.08f);
        static readonly Color Paper = new(0.9f, 0.88f, 0.82f, 1f);
        static readonly Color Ground = new(0.24f, 0.25f, 0.24f, 1f);
        static readonly Color Wet = new(0.12f, 0.15f, 0.17f, 1f);

        protected override object EscapeResult() => null; // nothing is sent

        // ---------------------------------------------------------------- build

        protected override void Build()
        {
            L = new SightingReport(SightSetup.FromOptions(_o));
            _lowVision = L.Setup.Profile == "lowvision";
            UiKit.At(UiKit.Label("Kicker", Window, SightingReport.Kicker, 22, UiKit.Dim).rectTransform, 40, 26, 1200, 30);
            _title = UiKit.Label("Title", Window, "", 40, UiKit.Ink, style: FontStyle.Bold);
            UiKit.At(_title.rectTransform, 40, 52, 1640, 56);
            _stage = UiKit.Box("Stage", Window, new Color(0.06f, 0.07f, 0.08f, 1f)).rectTransform;
            UiKit.At(_stage, 40, 124, 760, 740);
            _stage.gameObject.AddComponent<RectMask2D>();
            _side = UiKit.Rect("Side", Window);
            UiKit.At(_side, 840, 124, 840, 740);
            var col = _side.gameObject.AddComponent<VerticalLayoutGroup>();
            col.spacing = 12; col.childAlignment = TextAnchor.UpperLeft;
            col.childControlWidth = true; col.childForceExpandWidth = true;
            col.childControlHeight = true; col.childForceExpandHeight = false;
            _keys = UiKit.Label("Keys", Window, "", 18, UiKit.Dim);
            UiKit.At(_keys.rectTransform, 40, 884, 1640, 40);
            L.Changed += Render;
            Render();
        }

        void OnDisable() { _moment?.Dispose(); _moment = null; }

        // ---------------------------------------------------------------- the pages

        readonly List<(Button b, Action act, Image img, Color c)> _nav = new();
        int _cursor = -1, _primary = -1;

        void Render()
        {
            if (!this) return;
            var p = L.Phase;
            bool deciding = !_review && (p == SightPhase.How || p == SightPhase.Channel);
            if (deciding && _moment == null) _moment = DecisionMoment.Begin();
            else if (!deciding && _moment != null) { _moment.Dispose(); _moment = null; }
            int keep = p == _shown && _review == _shownReview && L.FieldIndex == _shownField ? _cursor : -1;
            Clear(_side);
            _nav.Clear(); _cursor = -1; _primary = -1;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            var spoken = new System.Text.StringBuilder();
            if (_review) RenderReview(spoken);
            else switch (p)
            {
                case SightPhase.How: RenderHow(spoken); break;
                case SightPhase.Card: RenderCard(spoken); break;
                case SightPhase.Channel: RenderChannel(spoken); break;
                default: RenderDone(spoken); break;
            }
            DrawStage();
            _keys.text = "1~9 또는 ↑↓ + Enter · 클릭" + (Gamepad.current != null ? "   게임패드: 십자키 + A" : "") + "   시간 제한 없음";
            _shown = p; _shownReview = _review; _shownField = L.FieldIndex;
            if (keep >= 0 && _nav.Count > 0) SetCursor(Mathf.Min(keep, _nav.Count - 1));
            if (_lowVision) Speak(spoken.ToString());
        }

        Text Para(string rich, int size, Color c, FontStyle st = FontStyle.Normal) => UiKit.Label("P", _side, rich, size, c, TextAnchor.UpperLeft, st);

        Button Choice(string label, Action act, bool primary = false, bool done = false, bool compact = false)
        {
            int n = _nav.Count + 1;
            var img = UiKit.Box("Choice", _side, primary ? UiKit.Teal : done ? new Color(UiKit.Teal.r, UiKit.Teal.g, UiKit.Teal.b, 0.16f) : ButtonBg, raycast: true);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            var lay = img.gameObject.AddComponent<VerticalLayoutGroup>();
            lay.padding = compact ? new RectOffset(20, 20, 8, 8) : new RectOffset(20, 20, 13, 13);
            lay.childAlignment = TextAnchor.MiddleLeft;
            lay.childControlWidth = true; lay.childForceExpandWidth = true;
            lay.childControlHeight = true; lay.childForceExpandHeight = false;
            img.gameObject.AddComponent<LayoutElement>().minHeight = compact ? 46 : 58;
            string mark = n <= 9 ? $"<color=#{(primary ? "0a1414" : "8fd9cf")}>{n}</color>  " : "";
            UiKit.Label("Text", img.transform, mark + label, compact ? 20 : 22, primary ? Dark : done ? UiKit.Teal : UiKit.Ink, TextAnchor.MiddleLeft, primary ? FontStyle.Bold : FontStyle.Normal);
            b.onClick.AddListener(() => { UiSound.Play("click"); act(); });
            if (primary && _primary < 0) _primary = _nav.Count;
            _nav.Add((b, act, img, img.color));
            return b;
        }

        string FactText(string id) => _o["facts"]?[id]?["text"]?.Type == JTokenType.String ? (string)_o["facts"][id]["text"] : null;
        string FactSource(string id) => _o["facts"]?[id]?["source"]?.Type == JTokenType.String ? (string)_o["facts"][id]["source"] : null;

        void FeedbackBox(SightFeedback f, System.Text.StringBuilder spoken)
        {
            if (f == null) return;
            var tone = f.Ok ? UiKit.Teal : UiKit.Amber;
            string tag = f.Ok ? "✓ 맞는 판단" : "! 다시 생각해 볼 점";
            var box = UiKit.Box("Feedback", _side, new Color(tone.r, tone.g, tone.b, 0.1f));
            var v = box.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(20, 20, 14, 14); v.spacing = 6;
            v.childControlWidth = true; v.childForceExpandWidth = true; v.childControlHeight = true; v.childForceExpandHeight = false;
            UiKit.Label("Tag", box.transform, $"{tag} · <b>{f.Title}</b>", 20, tone);
            UiKit.Label("Text", box.transform, f.Text, 21, UiKit.Ink);
            foreach (var id in f.Facts.Take(2))
            {
                var text = FactText(id);
                if (string.IsNullOrEmpty(text)) continue;
                var src = FactSource(id);
                UiKit.Label("Fact", box.transform, $"<color=#8fd9cf>▸</color> {text}" + (src != null ? $"\n<size=15><color=#94a3b8>출처: {src}</color></size>" : ""), 18, UiKit.Ink);
            }
            spoken.Append(tag).Append(". ").Append(f.Title).Append(". ").Append(f.Text).Append(' ');
        }

        void RenderHow(System.Text.StringBuilder spoken)
        {
            _title.text = SightingReport.HowTitle;
            Para(SightingReport.HowBody, 21, UiKit.Ink);
            spoken.Append(_title.text).Append(". ").Append(SightingReport.HowBody).Append(' ');
            FeedbackBox(L.Feedback, spoken);
            int i = 0;
            foreach (var o in SightingReport.HowOptions)
            {
                var id = o.Id;
                bool tried = L.FirstHow == id || (L.Feedback != null && !L.Feedback.Ok && L.FirstHow != null && id == L.FirstHow);
                Choice(o.Text, () => L.ChooseHow(id), done: tried && id == "report");
                spoken.Append(++i).Append("번, ").Append(o.Text).Append(". ");
            }
        }

        void RenderCard(System.Text.StringBuilder spoken)
        {
            _title.text = SightingReport.CardTitle;
            if (L.FieldIndex == 0 && L.Feedback != null) FeedbackBox(L.Feedback, spoken);
            else { Para(SightingReport.CardBody, 20, UiKit.Dim); spoken.Append(SightingReport.CardBody).Append(' '); }
            var f = L.Current;
            if (f != null)
            {
                Para($"<b>{f.Label}</b>  <size=18><color=#94a3b8>{L.FieldIndex + 1}/{L.Fields.Count}</color></size> · {f.Ask}", 24, UiKit.Ink);
                spoken.Append(f.Label).Append(". ").Append(f.Ask).Append(' ');
                var picked = L.Picked(f);
                int i = 0;
                foreach (var o in f.Options)
                {
                    var id = o.Id;
                    Choice(o.Text, () => L.Pick(id), done: picked?.Id == id, compact: true);
                    spoken.Append(++i).Append("번, ").Append(o.Text).Append(". ");
                }
                if (L.FieldIndex > 0) Choice("◀ 앞 칸 고치기", () => L.Back(), compact: true);
                return;
            }
            Para("카드를 다 썼다. 왼쪽 카드를 한 번 읽어 보고 보낸다.", 21, UiKit.Ink);
            spoken.Append("카드를 다 썼다. ");
            foreach (var fl in L.Fields) spoken.Append(fl.Label).Append(", ").Append(L.Picked(fl)?.Text ?? "빈칸").Append(". ");
            Choice("신고하기 ▶", () => L.Submit(), primary: true);
            Choice("◀ 마지막 칸 고치기", () => L.Back(), compact: true);
        }

        void RenderChannel(System.Text.StringBuilder spoken)
        {
            _title.text = SightingReport.ChannelTitle;
            Para(SightingReport.ChannelBody, 21, UiKit.Ink);
            spoken.Append(_title.text).Append(". ").Append(SightingReport.ChannelBody).Append(' ');
            FeedbackBox(L.Feedback, spoken);
            int i = 0;
            foreach (var o in SightingReport.ChannelOptions(L.Setup.Profile, L.Setup.Phone))
            {
                var id = o.Id;
                Choice(o.Text, () => L.ChooseChannel(id));
                spoken.Append(++i).Append("번, ").Append(o.Text).Append(". ");
            }
        }

        void RenderDone(System.Text.StringBuilder spoken)
        {
            _title.text = SightingReport.SentTitle(L.Channel);
            spoken.Append(_title.text).Append(". ");
            FeedbackBox(L.Feedback, spoken);
            Choice("돌아보기 ▶", () => { _review = true; Render(); }, primary: true);
        }

        void RenderReview(System.Text.StringBuilder spoken)
        {
            _title.text = "돌아보기 · 찜찜한 기억";
            spoken.Append(_title.text).Append(". ");
            var rows = new List<string>
            {
                L.FirstHow == "report" ? "✓ 다가가거나 찍으러 가지 않고, 집에서 신고했다." : "! 처음엔 흔적을 확인하러 가려 했다. 다가가지 않고 신고하면 된다.",
            };
            foreach (var f in L.Fields)
            {
                var o = L.Picked(f);
                if (o == null) continue;
                rows.Add(o.Kind switch
                {
                    "exact" => $"✓ {f.Label}: 본 그대로 말했다.",
                    "honest" => $"○ {f.Label}: \"모른다\"고 솔직하게 말했다.",
                    "vague" => $"! {f.Label}: 흐릿했다. {o.Why}",
                    _ => $"✕ {f.Label}: \"{o.Text}\" — {o.Why}",
                });
            }
            if (!SightingReport.ReachesPolice(L.FirstChannel)) rows.Add(L.FirstChannel == "wait" ? "! 처음엔 신고를 미루려 했다." : "! 처음엔 112가 아닌 곳을 골랐다.");
            foreach (var r in rows)
            {
                var c = r.StartsWith("✓") || r.StartsWith("○") ? UiKit.Teal : r.StartsWith("✕") ? UiKit.Red : UiKit.Amber;
                Para(r, 19, c);
                spoken.Append(r).Append(' ');
            }
            var score = $"신고 정확도 {L.Score}/{L.Full}" + (L.Guesses > 0 ? $" · 짐작 {L.Guesses}칸" : "");
            Para($"<b>{score}</b> — {MountainLogic.LiftLine(L.Lift, L.Guesses)}", 21, UiKit.Ink);
            Para(SightingReport.RuleLine, 17, UiKit.Dim);
            spoken.Append(score).Append(". ").Append(MountainLogic.LiftLine(L.Lift, L.Guesses)).Append(' ');
            var why = FactText("report_5w");
            if (why != null) Para($"<color=#8fd9cf>▸</color> {why}\n<size=15><color=#94a3b8>출처: {FactSource("report_5w")}</color></size>", 18, UiKit.Ink);
            Choice("계속 ▶", () => Finish(L.Result()), primary: true);
        }

        // ---------------------------------------------------------------- the drawing

        void DrawStage()
        {
            Clear(_stage);
            float W = 760;
            // the memory: the memo, or a blurry one
            var memo = UiKit.Box("Memo", _stage, new Color(0.1f, 0.12f, 0.15f)).rectTransform;
            UiKit.At(memo, 24, 16, W - 48, 118);
            string memoText = L.Setup.Noted
                ? $"<color=#8fd9cf>메모 · 어제 {GameRules.Hhmm(L.Setup.SeenMinute)}</color>\n{MountainScene.TracesMemo}"
                : "<color=#94a3b8>기억 · 어제 아침 급수소 가던 길 (적어 두지 않음)</color>\n뒷산 입구 어디쯤… 젖은 발자국 두 줄, 운동화 택, 가방 하나. 어느 쪽으로 났더라? 몇 시였더라?";
            UiKit.At(UiKit.Label("MemoText", memo, memoText, 18, UiKit.Ink).rectTransform, 18, 10, W - 84, 100);
            DrawTraces(24, 146, W - 48, 198);
            DrawCard(24, 356, W - 48, 370);
        }

        /// <summary>The path's mouth seen from above: the pavement, two trails of wet prints (one larger), two tags, the bag. Nobody.</summary>
        void DrawTraces(float x, float y, float w, float h)
        {
            var g = UiKit.Box("Ground", _stage, Ground).rectTransform;
            UiKit.At(g, x, y, w, h);
            UiKit.At(UiKit.Box("Hill", g, new Color(0.16f, 0.22f, 0.16f)).rectTransform, 0, 0, 170, h);
            UiKit.At(UiKit.Label("HillLabel", g, "뒷산 산책로", 16, UiKit.Dim).rectTransform, 12, 8, 150, 24);
            UiKit.At(UiKit.Label("RoadLabel", g, "큰길·버스정류장 →", 16, UiKit.Dim, TextAnchor.UpperRight).rectTransform, w - 220, 8, 208, 24);
            // two trails from the hill toward the road, the second with larger prints
            for (int t = 0; t < 2; t++)
            {
                float size = t == 0 ? 14 : 19, row = t == 0 ? 82 : 128;
                for (int i = 0; i < 9; i++)
                {
                    var p = UiKit.Box("Print", g, new Color(Wet.r, Wet.g, Wet.b, 0.95f - i * 0.08f));
                    p.sprite = HudDraw.DiscSprite;
                    UiKit.At(p.rectTransform, 150 + i * 56, row + (i % 2 == 0 ? -7 : 7), size, size * 1.5f);
                }
            }
            // two torn tags and the bag by the path (never opened)
            for (int i = 0; i < 2; i++)
            {
                var tag = UiKit.Box("Tag", g, Paper).rectTransform;
                UiKit.At(tag, 200 + i * 26, 170, 16, 24);
                tag.localRotation = Quaternion.Euler(0, 0, i == 0 ? 18 : -24);
            }
            UiKit.At(UiKit.Box("Bag", g, new Color(0.05f, 0.05f, 0.06f)).rectTransform, 60, 150, 62, 44);
            UiKit.At(UiKit.Box("Strap", g, new Color(0.18f, 0.18f, 0.2f)).rectTransform, 74, 140, 34, 10);
        }

        /// <summary>The report card: one line per field, filled as answered; in the review each with its mark.</summary>
        void DrawCard(float x, float y, float w, float h)
        {
            var card = UiKit.Box("Card", _stage, new Color(0.11f, 0.14f, 0.15f)).rectTransform;
            UiKit.At(card, x, y, w, h);
            UiKit.At(UiKit.Label("CardTitle", card, "신고 카드" + (L.Phase == SightPhase.Done ? SightingReport.SentTag(L.Channel) : ""), 20, UiKit.Teal, style: FontStyle.Bold).rectTransform, 18, 12, w - 36, 28);
            float rowY = 50;
            for (int i = 0; i < L.Fields.Count; i++)
            {
                var f = L.Fields[i];
                var o = L.Picked(f);
                bool here = !_review && L.Phase == SightPhase.Card && i == L.FieldIndex;
                var line = UiKit.Box("Line", card, here ? new Color(UiKit.Amber.r, UiKit.Amber.g, UiKit.Amber.b, 0.14f) : new Color(1, 1, 1, 0.03f)).rectTransform;
                UiKit.At(line, 12, rowY, w - 24, 50);
                string mark = "";
                if (_review && o != null) mark = o.Kind == "exact" ? "<color=#3cc6b5>✓</color> " : o.Kind == "honest" ? "<color=#3cc6b5>○</color> " : o.Kind == "vague" ? "<color=#f2b33d>!</color> " : "<color=#e5484d>✕</color> ";
                UiKit.At(UiKit.Label("Field", line, $"{mark}<b>{f.Label}</b>", 18, here ? UiKit.Amber : UiKit.Dim, TextAnchor.MiddleLeft).rectTransform, 12, 3, 130, 44);
                UiKit.At(UiKit.Label("Answer", line, o?.Text ?? (here ? "← 고르는 중" : "—"), 17, o != null ? UiKit.Ink : UiKit.Dim, TextAnchor.MiddleLeft).rectTransform, 140, 3, w - 180, 44);
                rowY += 52;
            }
        }

        // ---------------------------------------------------------------- input

        protected override void Update()
        {
            base.Update();
            var es = EventSystem.current;
            if (es && es.currentSelectedGameObject) es.SetSelectedGameObject(null);
            if (L == null || Time.unscaledTime - OpenedAt < 0.25f || _nav.Count == 0) return;
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            int move = 0; bool press = false;
            if (kb != null)
            {
                move += (kb.downArrowKey.wasPressedThisFrame ? 1 : 0) - (kb.upArrowKey.wasPressedThisFrame ? 1 : 0);
                press |= kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
                for (int i = 0; i < 9 && i < _nav.Count; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame) { Press(i); return; }
            }
            if (gp != null)
            {
                move += (gp.dpad.down.wasPressedThisFrame || gp.leftStick.down.wasPressedThisFrame ? 1 : 0) - (gp.dpad.up.wasPressedThisFrame || gp.leftStick.up.wasPressedThisFrame ? 1 : 0);
                press |= gp.buttonSouth.wasPressedThisFrame;
            }
            if (move != 0) { SetCursor(_cursor < 0 ? 0 : Mathf.Clamp(_cursor + move, 0, _nav.Count - 1)); return; }
            if (!press) return;
            if (_cursor >= 0) Press(_cursor);
            else if (_primary >= 0) Press(_primary); // Enter with nothing highlighted: the page's main (teal) button
            else SetCursor(0);                        // a choice page has none: the first Enter only shows where it is
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
            if (_nav[i].img) _nav[i].img.color = Color.Lerp(_nav[i].c, UiKit.Amber, 0.5f);
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
