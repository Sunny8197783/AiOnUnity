// "불나면 살펴서 대피" (competition upgrade, 3장): left a drawing of what 정민 is looking at (the front door from inside
// with its handle and the gap, the balcony partition, or the phone with the 119 call), right the step, the buttons, the
// feedback with its reason and the sourced facts. Pages follow LookFirstLogic.Phase: 살피기 -> 어디? -> 어떻게? ->
// (문틈 막기 | 경량칸막이) -> 119 신고 -> 돌아보기. The judgement pages are DecisionMoments (the world slows behind them).
// Keys: 1-9 or ↑/↓ + Enter/Space; the pad's d-pad + A. Enter / A acts on the highlighted item, or on the page's main
// (teal) button; a judgement page has none, so the first Enter there only highlights item 1. The highlight stays put
// while the same page redraws. The partition can be pressed (5 times) or held (Space, pad A, or the mouse on the
// button), and with "길게 누르기 대신 켜 두기" one press keeps going (on by default in the mobility, wheelchair and
// guardian modes); while it goes only its gauge is redrawn, not the page.
// The rules live in AiOn.Game.LookFirstLogic; this class only draws and forwards input. Returns LookFirstLogic.Result().
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
    public class LookFirstScreen : UiScreen
    {
        public const string Name = LookFirstScene.Screen;

        public static void Register(UnityStoryHost host) => host.Screens[Name] = Open;

        /// <summary>ui.lookfirst(LookSetup json + { facts: { id: { text, source } }, reduceMotion }) -> LookFirstLogic.Result().</summary>
        public static async Task<JToken> Open(JObject o)
        {
            LookFirstScreen me = null;
            var r = await Show<LookFirstScreen>(s => { s._o = o ?? new JObject(); me = s; });
            if (r is JToken t) return t;
            return me != null && me.L != null ? me.L.Result() : new JObject();
        }

        JObject _o;
        LookFirstLogic L;
        bool _reduce, _toggleHold, _autoPush, _lowVision;
        RectTransform _stage, _side, _list;
        Text _title, _kicker, _keys;
        IDisposable _moment;
        LookPhase _shown = (LookPhase)(-1);
        bool _review, _shownReview; // after Done: the 돌아보기 page

        static readonly Color ButtonBg = new(1, 1, 1, 0.07f);
        static readonly Color Dark = new(0.04f, 0.08f, 0.08f);
        static readonly Color Wall = new(0.16f, 0.17f, 0.18f, 1f);
        static readonly Color DoorC = new(0.36f, 0.27f, 0.2f, 1f);
        static readonly Color Smoke = new(0.6f, 0.6f, 0.62f, 1f);
        static readonly Color Fire = new(1f, 0.55f, 0.18f, 1f);
        static readonly Color Box = new(0.62f, 0.46f, 0.28f, 1f);

        protected override object EscapeResult() => null; // a judgement is made; nothing to skip

        // ---------------------------------------------------------------- build

        protected override void Build()
        {
            var setup = LookSetup.FromOptions(_o);
            L = new LookFirstLogic(setup);
            _reduce = _o["reduceMotion"]?.Type == JTokenType.Boolean && (bool)_o["reduceMotion"];
            _lowVision = setup.Profile == "lowvision";
            _toggleHold = setup.Profile == "mobility" || setup.Profile == "wheelchair" || setup.Profile == "guardian";
            _kicker = UiKit.Label("Kicker", Window, LookFirstLogic.Kicker, 22, UiKit.Dim);
            UiKit.At(_kicker.rectTransform, 40, 26, 1200, 30);
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
            bool deciding = !_review && (p == LookPhase.Where || p == LookPhase.Act);
            if (deciding && _moment == null) _moment = DecisionMoment.Begin("fire");
            else if (!deciding && _moment != null) { _moment.Dispose(); _moment = null; }
            int keep = p == _shown && _review == _shownReview ? _cursor : -1; // the same page redrawn keeps its highlight
            Clear(_side);
            _nav.Clear(); _cursor = -1; _primary = -1;
            _pushLabel = null; _pointerHeld = false;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            var spoken = new System.Text.StringBuilder();
            if (_review) RenderReview(spoken);
            else switch (p)
            {
                case LookPhase.Look: RenderLook(spoken); break;
                case LookPhase.Where: RenderWhere(spoken); break;
                case LookPhase.Act: RenderAct(spoken); break;
                case LookPhase.Seal: RenderSeal(spoken); break;
                case LookPhase.Partition: RenderPartition(spoken); break;
                case LookPhase.Report: RenderReport(spoken); break;
                default: RenderDone(spoken); break;
            }
            DrawStage();
            _keys.text = "1~9 또는 ↑↓ + Enter · 클릭" + (Gamepad.current != null ? "   게임패드: 십자키 + A" : "") + (p == LookPhase.Partition ? "   칸막이: Space를 누르거나 길게" : "");
            _shown = p; _shownReview = _review;
            if (keep >= 0 && _nav.Count > 0) SetCursor(Mathf.Min(keep, _nav.Count - 1));
            if (_lowVision) Speak(spoken.ToString());
        }

        Text Para(string rich, int size, Color c, FontStyle st = FontStyle.Normal)
        {
            var t = UiKit.Label("P", _side, rich, size, c, TextAnchor.UpperLeft, st);
            return t;
        }

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

        void FeedbackBox(LookFeedback f, System.Text.StringBuilder spoken)
        {
            if (f == null) return;
            var tone = f.Verdict switch
            {
                LookVerdict.Good => UiKit.Teal, LookVerdict.Caution => UiKit.Amber, LookVerdict.Wrong => UiKit.Red, LookVerdict.Corrected => UiKit.Amber, _ => UiKit.Ink,
            };
            string tag = f.Tag ?? f.Verdict switch
            {
                LookVerdict.Good => "✓ 맞는 판단", LookVerdict.Caution => "! 다시 생각해 볼 점", LookVerdict.Wrong => "✕ 지침과 다름", LookVerdict.Corrected => "! 이웃이 말린다",
                LookVerdict.Step => "진행", _ => "살핀 것",
            };
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

        string FactText(string id) => _o["facts"]?[id]?["text"]?.Type == JTokenType.String ? EasyKorean.Of((string)_o["facts"][id]["text"]) : null; // 쉬운 한국어
        string FactSource(string id) => _o["facts"]?[id]?["source"]?.Type == JTokenType.String ? (string)_o["facts"][id]["source"] : null;

        void RenderLook(System.Text.StringBuilder spoken)
        {
            _title.text = LookFirstLogic.LookTitle;
            Para(LookFirstLogic.LookBody, 21, UiKit.Ink);
            spoken.Append(_title.text).Append(". ").Append(LookFirstLogic.LookBody).Append(' ');
            if (L.Feedback != null && L.Feedback.Verdict == LookVerdict.Info) spoken.Append(L.Feedback.Text).Append(' ');
            if (L.Grew) Para("<b>그사이 불이 상자 더미로 번졌다.</b> 살피는 것도 짧게.", 20, UiKit.Amber);
            int i = 0;
            foreach (var c in L.AllClues)
            {
                bool seen = L.Notes.TryGetValue(c.Id, out var note);
                var id = c.Id;
                string label = seen
                    ? $"{c.Icon}  <b>{c.Short}</b>  <size=19><color=#e8eded>{note}</color></size>"
                    : $"{c.Icon}  {c.Label}  <size=17><color=#94a3b8>약 {c.Seconds:0}초</color></size>";
                if (seen && c.Fact != null && FactText(c.Fact) != null)
                    label += $"\n<size=16><color=#8fd9cf>▸ {FactText(c.Fact)}</color> <color=#94a3b8>— {FactSource(c.Fact)}</color></size>";
                Choice(label, () => L.Examine(id), done: seen);
                spoken.Append(++i).Append("번, ").Append(c.Label).Append(seen ? ", 살펴봄. " : ". ");
            }
            Choice(L.Examined.Count == 0 ? "살피지 않고 바로 판단한다" : "다 살폈다 · 판단한다 ▶", L.ToWhere, primary: L.Examined.Count > 0);
            spoken.Append(++i).Append("번, 판단한다.");
        }

        void RenderWhere(System.Text.StringBuilder spoken)
        {
            _title.text = LookFirstLogic.WhereTitle;
            spoken.Append(_title.text).Append(". ");
            if (L.Notes.Count == 0) { Para("아무것도 살피지 않았다.", 20, UiKit.Dim); spoken.Append("아무것도 살피지 않았다. "); }
            foreach (var id in L.Examined)
            {
                var c = L.Clue(id);
                var line = $"<b>{c?.Short}</b>  {L.Notes[id]}";
                Para(line, 19, UiKit.Dim);
                spoken.Append(c?.Short).Append(", ").Append(L.Notes[id]).Append(' ');
            }
            int i = 0;
            foreach (var (id, label) in LookFirstLogic.WhereChoices)
            {
                var a = id;
                Choice(label, () => L.JudgeWhere(a));
                spoken.Append(++i).Append("번, ").Append(label).Append(". ");
            }
        }

        void RenderAct(System.Text.StringBuilder spoken)
        {
            _title.text = LookFirstLogic.ActTitle;
            spoken.Append(_title.text).Append(". ");
            FeedbackBox(L.Feedback, spoken);
            int i = 0;
            foreach (var id in LookFirstLogic.ActIds)
            {
                var a = id;
                var label = LookFirstLogic.ActLabel(id, L.Setup.Profile);
                if (id == "partition" && !L.Setup.PartitionCleared) label += "  <size=17><color=#f2b33d>(앞에 상자가 쌓여 있다)</color></size>";
                Choice(label, () => L.Choose(a));
                spoken.Append(++i).Append("번, ").Append(LookFirstLogic.ActLabel(id, L.Setup.Profile)).Append(". ");
            }
        }

        void RenderSeal(System.Text.StringBuilder spoken)
        {
            _title.text = LookFirstLogic.SealTitle;
            spoken.Append(_title.text).Append(". ");
            FeedbackBox(L.Feedback, spoken);
            int i = 0;
            foreach (var (id, label) in LookFirstLogic.SealSteps)
            {
                var a = id;
                bool done = L.Sealed.Contains(id);
                Choice(label + (done ? "  <size=17>· 했다</size>" : ""), () => L.DoSeal(a), done: done);
                spoken.Append(++i).Append("번, ").Append(label).Append(done ? ", 했음. " : ". ");
            }
        }

        void RenderPartition(System.Text.StringBuilder spoken)
        {
            _title.text = LookFirstLogic.PartitionTitle;
            spoken.Append(_title.text).Append(". ");
            FeedbackBox(L.Feedback, spoken);
            if (L.BoxesLeft > 0)
            {
                Choice($"상자 옮기기 ({LookFirstLogic.Boxes - L.BoxesLeft + 1}/{LookFirstLogic.Boxes})", () => L.MoveBox(), primary: true);
                spoken.Append("1번, 상자 옮기기.");
                return;
            }
            _pushLabel = Para(PushText(), 22, UiKit.Ink, FontStyle.Bold);
            var kick = LookFirstLogic.KickLabel(L.Setup.Profile);
            var push = Choice(_autoPush ? "부수는 중… (다시 누르면 멈춤)" : kick + (_toggleHold ? " (누르면 계속)" : " (누르기 · 길게 누르기)"), PushPressed, primary: true);
            HoldOn(push.gameObject);
            Choice((_toggleHold ? "■ " : "□ ") + "길게 누르기 대신 켜 두기", () => { _toggleHold = !_toggleHold; _autoPush = false; Render(); });
            spoken.Append("1번, ").Append(kick).Append(". 2번, 길게 누르기 대신 켜 두기.");
        }

        Text _pushLabel;
        bool _pointerHeld;
        float _heldPushAt = -1;

        string PushText() => $"칸막이 부수기 · {Mathf.RoundToInt((float)L.Push * 100)}%";

        /// <summary>Pointer down / up on the push button: the mouse (or a touch) can hold it like Space.</summary>
        void HoldOn(GameObject go)
        {
            var et = go.AddComponent<EventTrigger>();
            void On(EventTriggerType type, bool down)
            {
                var e = new EventTrigger.Entry { eventID = type };
                e.callback.AddListener(_ => _pointerHeld = down);
                et.triggers.Add(e);
            }
            On(EventTriggerType.PointerDown, true);
            On(EventTriggerType.PointerUp, false);
            On(EventTriggerType.PointerExit, false);
        }

        void PushPressed()
        {
            if (_toggleHold) { _autoPush = !_autoPush; Render(); return; }
            if (_heldPushAt >= 0 && Time.unscaledTime - _heldPushAt < 0.15f) return; // the release of a held press is not one more kick
            L.PushPartition();
        }

        /// <summary>While holding (or the toggle runs): only the gauge, the wall's shade and its cracks change.</summary>
        void UpdatePushGauge()
        {
            if (_pushLabel) _pushLabel.text = PushText();
            if (_wallImg) _wallImg.color = WallShade();
            int n = Mathf.RoundToInt((float)L.Push * _cracks.Count);
            for (int i = 0; i < _cracks.Count; i++) if (_cracks[i]) _cracks[i].SetActive(i < n);
        }

        void RenderReport(System.Text.StringBuilder spoken)
        {
            _title.text = LookFirstLogic.ReportTitle;
            if (L.Feedback != null && L.Setup.Mode != "report" && L.Picked.Count == 0)
                Para((L.Feedback.Verdict == LookVerdict.Good ? "<color=#3cc6b5>✓</color>" : "<color=#f2b33d>!</color>") + $" {L.Feedback.Title} — 이제 119에 알린다.", 19, UiKit.Dim);
            Para(LookFirstLogic.ReportBody, 21, UiKit.Ink);
            spoken.Append(_title.text).Append(". ").Append(LookFirstLogic.ReportBody).Append(' ');
            int i = 0;
            foreach (var c in L.Cards)
            {
                var id = c.Id;
                int at = L.Picked.IndexOf(id);
                Choice((at >= 0 ? $"<b>[{at + 1}]</b> " : "") + c.Text, () => L.Toggle(id), done: at >= 0, compact: true);
                spoken.Append(++i).Append("번, ").Append(c.Text).Append(at >= 0 ? " (골랐음) " : " ");
            }
            if (L.Picked.Count > 0) Choice($"말하기 ▶ ({L.Picked.Count}/{LookFirstLogic.MaxCards}) · Enter", () => L.Submit(), primary: true);
        }

        void RenderDone(System.Text.StringBuilder spoken)
        {
            _title.text = L.Act == "leave" ? "현관을 나선다" : L.Report.Played ? "신고를 마쳤다" : "판단을 마쳤다";
            spoken.Append(_title.text).Append(". ");
            FeedbackBox(L.Feedback, spoken);
            if (L.Setup.Mode == "report") { Choice("계속 ▶", () => Finish(L.Result()), primary: true); return; }
            Choice("돌아보기 ▶", () => { _review = true; Render(); }, primary: true);
        }

        void RenderReview(System.Text.StringBuilder spoken)
        {
            _title.text = "돌아보기 · 불나면 살펴서 대피";
            spoken.Append(_title.text).Append(". ");
            var rows = new List<string>();
            rows.Add(L.Examined.Count >= 2 ? $"✓ 문을 열기 전에 {L.Examined.Count}가지를 살폈다 (약 {L.LookSeconds:0}초)." : L.Examined.Count == 1 ? "! 단서 하나만 보고 판단했다." : "✕ 살피지 않고 판단했다.");
            if (L.Grew) rows.Add("! 살피는 사이 불이 커졌다. 살피되 짧게.");
            rows.Add(!L.WhereRight ? "✕ 불이 난 곳을 잘못 짚었다. 실제는 현관 밖 복도였다."
                : L.Located >= 2 ? "✓ 불이 난 곳: 현관 밖 복도 (304호 앞)." : L.Located == 1 ? "! 불이 난 곳은 맞혔지만, 단서 하나로 짐작했다." : "! 불이 난 곳은 맞혔지만, 단서 없이 짐작했다.");
            if (L.Elevator > 0) rows.Add("! 엘리베이터로 가려다 이웃이 말렸다. 불이 나면 계단으로.");
            rows.Add((L.ActVerdict == LookVerdict.Good ? "✓ " : "! ") + "고른 방법: " + LookFirstLogic.ActLabel(L.Act ?? "", L.Setup.Profile)
                + (L.ActVerdict != LookVerdict.Good ? " — " + Judged() : ""));
            if (L.Act == "partition") rows.Add(L.Setup.PartitionCleared ? "✓ 1장에서 칸막이 앞 상자를 치워 두어 바로 넘어갔다." : "! 칸막이 앞 상자를 먼저 옮겨야 했다.");
            if (L.Report.Played) rows.Add((L.Report.Score >= LookReport.Full ? "✓ " : "! ") + $"119 신고: 필요한 말 {L.Report.Score}/{LookReport.Full}.");
            foreach (var r in rows)
            {
                var c = r.StartsWith("✓") ? UiKit.Teal : r.StartsWith("✕") ? UiKit.Red : UiKit.Amber;
                Para(r, 21, c);
                spoken.Append(r).Append(' ');
            }
            var why = FactText("fire_look");
            if (why != null) Para($"<color=#8fd9cf>▸</color> {why}\n<size=15><color=#94a3b8>출처: {FactSource("fire_look")}</color></size>", 19, UiKit.Ink);
            Para("건물마다 피난시설이 다릅니다. 우리 집 베란다에 경량칸막이·대피공간·하향식 피난구가 있는지, 그 앞이 비어 있는지 확인해 보세요.", 18, UiKit.Dim);
            Choice(L.Act == "leave" ? "복도로 나간다 ▶" : "계속 ▶", () => Finish(L.Result()), primary: true);
        }

        /// <summary>The choice's own verdict title (judged on the fire as it was when choosing).</summary>
        string Judged() => LookFirstLogic.Judge(L.Now, L.Act, L.Setup.PartitionCleared).Title;

        // ---------------------------------------------------------------- the drawing

        void DrawStage()
        {
            Clear(_stage);
            float W = 760, H = 740;
            var p = _review ? LookPhase.Done : L.Phase;
            bool large = L.Size == "large";
            if (p == LookPhase.Partition) { DrawPartition(W, H); return; }
            if (p == LookPhase.Report || (p == LookPhase.Done && L.Report.Played)) { DrawPhone(W, H); return; }
            // the front door from inside: wall, door, handle, the gap under it with smoke and the orange light
            UiKit.At(UiKit.Box("Wall", _stage, Wall).rectTransform, 0, 0, W, H);
            UiKit.At(UiKit.Box("Floor", _stage, new Color(0.11f, 0.1f, 0.09f)).rectTransform, 0, 600, W, 140);
            var door = UiKit.Box("Door", _stage, DoorC).rectTransform;
            UiKit.At(door, 230, 90, 300, 510);
            UiKit.At(UiKit.Box("Frame", _stage, new Color(0.22f, 0.2f, 0.19f)).rectTransform, 222, 82, 8, 518);
            UiKit.At(UiKit.Box("Frame", _stage, new Color(0.22f, 0.2f, 0.19f)).rectTransform, 530, 82, 8, 518);
            var handle = UiKit.Box("Handle", _stage, L.Examined.Contains("handle") ? (large ? new Color(0.95f, 0.62f, 0.3f) : new Color(0.85f, 0.75f, 0.55f)) : new Color(0.75f, 0.75f, 0.72f));
            handle.sprite = HudDraw.DiscSprite; handle.preserveAspect = true;
            UiKit.At(handle.rectTransform, 480, 350, 34, 34);
            UiKit.At(UiKit.Box("Lever", _stage, new Color(0.75f, 0.75f, 0.72f)).rectTransform, 452, 360, 46, 12);
            // the gap: light from the right (304 is down the corridor that way), smoke rising from it
            float glow = large ? 0.85f : 0.45f;
            UiKit.At(UiKit.Box("Gap", _stage, new Color(Fire.r, Fire.g, Fire.b, glow)).rectTransform, 230, 596, 300, 6);
            UiKit.At(UiKit.Box("GapRight", _stage, new Color(Fire.r, Fire.g, Fire.b, glow * 0.7f)).rectTransform, 430, 590, 100, 12);
            int puffs = large ? 9 : 4;
            for (int i = 0; i < puffs; i++)
            {
                var s = UiKit.Box("Smoke", _stage, new Color(Smoke.r, Smoke.g, Smoke.b, large ? 0.32f : 0.18f));
                s.sprite = HudDraw.DiscSprite;
                float x = 250 + i * (260f / puffs), y = 560 - (i % 3) * 26 - (large ? 40 : 0);
                UiKit.At(s.rectTransform, x, y, large ? 90 : 60, large ? 70 : 44);
            }
            if (L.Phase == LookPhase.Seal || (p == LookPhase.Done && L.Act == "stay"))
            {
                if (L.Sealed.Contains("towel")) UiKit.At(UiKit.Box("Towel", _stage, L.Wet == "dry" ? new Color(0.85f, 0.85f, 0.8f) : new Color(0.45f, 0.62f, 0.78f)).rectTransform, 226, 588, 308, 22);
                if (L.Sealed.Contains("window")) Caption("창문 닫음", 40, 40, UiKit.Teal);
                if (L.Sealed.Contains("away")) Caption("안방으로", 40, 80, UiKit.Teal);
            }
            Caption("302호 현관", 40, 620, UiKit.Dim);
            // the corridor outside, in order (apartment level: 302 x -1.3, elevator 1.2, stairs 5, 304 18.8): all to the right
            var strip = UiKit.Label("Corridor", _stage, "문 밖 복도 →  엘리베이터 · <b>계단 (가까움)</b> · 303호 · " + (large ? "<color=#f2b33d>304호 (불)</color>" : "304호 (불)"), 19, UiKit.Dim);
            UiKit.At(strip.rectTransform, 40, 680, 700, 40);
        }

        Image _wallImg;
        readonly List<GameObject> _cracks = new();
        Color WallShade() => Color.Lerp(new Color(0.82f, 0.82f, 0.8f), new Color(0.25f, 0.24f, 0.23f), (float)L.Push);

        void Caption(string text, float x, float y, Color c)
        {
            var t = UiKit.Label("Cap", _stage, text, 20, c);
            UiKit.At(t.rectTransform, x, y, 300, 30);
        }

        void DrawPartition(float W, float H)
        {
            UiKit.At(UiKit.Box("Sky", _stage, new Color(0.07f, 0.09f, 0.14f)).rectTransform, 0, 0, W, H);
            UiKit.At(UiKit.Box("Floor", _stage, new Color(0.2f, 0.2f, 0.21f)).rectTransform, 0, 560, W, 180);
            _wallImg = UiKit.Box("Partition", _stage, WallShade());
            var wall = _wallImg.rectTransform;
            UiKit.At(wall, 380, 120, 300, 440);
            var sign = UiKit.Label("Sign", wall, "경량칸막이\n<size=16>비상시 파괴 후 옆 세대로 대피</size>", 22, new Color(0.7f, 0.12f, 0.12f), TextAnchor.UpperCenter, FontStyle.Bold);
            UiKit.At(sign.rectTransform, 0, 30, 300, 80);
            _cracks.Clear();
            for (int i = 0; i < 6; i++)
            {
                var c = UiKit.Box("Crack", wall, new Color(0.15f, 0.14f, 0.14f)).rectTransform;
                UiKit.At(c, 60 + (i * 37) % 180, 160 + (i * 53) % 220, 70, 5);
                c.localRotation = Quaternion.Euler(0, 0, (i * 47) % 90 - 45);
                _cracks.Add(c.gameObject);
            }
            UpdatePushGauge();
            // the boxes: two on the floor, one on top between them, each with the tape down its top flaps (they read as three)
            for (int i = 0; i < L.BoxesLeft; i++)
            {
                float bx = i < 2 ? 386 + i * 128 : 450, by = i < 2 ? 440 : 318;
                UiKit.At(UiKit.Box("Box", _stage, i == 1 ? Color.Lerp(Box, Color.black, 0.12f) : Box).rectTransform, bx, by, 116, 116);
                UiKit.At(UiKit.Box("Tape", _stage, new Color(0.9f, 0.84f, 0.68f)).rectTransform, bx + 50, by, 16, 44);
            }
            Caption("302호 베란다", 40, 700, UiKit.Dim);
            Caption("벽 너머 301호 베란다", 380, 80, UiKit.Dim);
        }

        void DrawPhone(float W, float H)
        {
            UiKit.At(UiKit.Box("Bg", _stage, new Color(0.05f, 0.06f, 0.07f)).rectTransform, 0, 0, W, H);
            var phone = UiKit.Box("Phone", _stage, new Color(0.1f, 0.12f, 0.15f)).rectTransform;
            UiKit.At(phone, 170, 30, 420, 680);
            UiKit.At(UiKit.Label("Call", phone, "119 · 통화 중", 24, UiKit.Teal, TextAnchor.UpperCenter, FontStyle.Bold).rectTransform, 0, 24, 420, 36);
            float y = 80;
            var lines = L.Picked.Select(id => L.Card(id)?.Text).Where(s => s != null).ToList();
            foreach (var s in lines)
            {
                var bub = UiKit.Box("Bubble", phone, new Color(UiKit.Teal.r, UiKit.Teal.g, UiKit.Teal.b, 0.22f)).rectTransform;
                UiKit.At(bub, 40, y, 340, 84);
                UiKit.At(UiKit.Label("T", bub, s, 18, UiKit.Ink).rectTransform, 14, 8, 312, 72);
                y += 94;
            }
            if (lines.Count == 0) UiKit.At(UiKit.Label("Hint", phone, "전할 말을 오른쪽에서 고릅니다.", 19, UiKit.Dim, TextAnchor.UpperCenter).rectTransform, 20, 300, 380, 60);
        }

        // ---------------------------------------------------------------- input

        float _pushHeld;

        protected override void Update()
        {
            base.Update();
            var es = EventSystem.current;
            if (es && es.currentSelectedGameObject) es.SetSelectedGameObject(null);
            if (L == null || Time.unscaledTime - OpenedAt < 0.25f) return;
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            float dt = Time.unscaledDeltaTime;

            // the partition: held Space / A / mouse on the push button, or the toggle's continuous push
            if (!_review && L.Phase == LookPhase.Partition && L.BoxesLeft == 0)
            {
                bool held = (kb != null && kb.spaceKey.isPressed) || (gp != null && gp.buttonSouth.isPressed) || _pointerHeld;
                if (_autoPush || (held && !_toggleHold && (_pushHeld += dt) > 0.3f))
                {
                    if (!_autoPush) _heldPushAt = Time.unscaledTime;
                    L.PushPartition(dt); // no Changed while it holds: the page is not rebuilt
                    if (L.Phase != LookPhase.Partition) { _autoPush = false; return; } // through: Changed rebuilt the page
                    UpdatePushGauge();
                }
                if (!held) _pushHeld = 0;
            }

            if (_nav.Count == 0) return;
            int move = 0; bool press = false;
            if (kb != null)
            {
                move += (kb.downArrowKey.wasPressedThisFrame ? 1 : 0) - (kb.upArrowKey.wasPressedThisFrame ? 1 : 0);
                press |= kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || (kb.spaceKey.wasPressedThisFrame && L.Phase != LookPhase.Partition);
                for (int i = 0; i < 9 && i < _nav.Count; i++)
                    if (kb[Key.Digit1 + i].wasPressedThisFrame || kb[Key.Numpad1 + i].wasPressedThisFrame) { Press(i); return; }
                if (kb.spaceKey.wasPressedThisFrame && L.Phase == LookPhase.Partition && !_review) { Press(0); return; }
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
            else SetCursor(0);                        // a judgement page has none: the first Enter only shows where it is
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
