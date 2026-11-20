using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using AiOn.Player;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>The facts as the terminal screens read them (loaded once from the session's facts JSON).</summary>
    static class TerminalData
    {
        public static Facts Facts
        {
            get
            {
                var s = GameSession.Current;
                return s ? s.FactsData : Facts.Empty; // facts.json + facts-ext.json, cached by the session
            }
        }
    }

    /// <summary>
    /// 생존 가이드: the emergency terminal's briefing. One card per situation (air raid, CBRN, power and water cuts, moving
    /// to a shelter, fire and smoke, checking news), each an icon and at most three FACTS sentences marked ✓ do / ! take
    /// care / ✕ do not, with who published them. Opened with K while roaming, or as the "guide" screen.
    /// </summary>
    public class GuideScreen : UiScreen
    {
        public const float W = 1760, H = 990;
        List<GuideCard> _cards;

        public static void Register(UnityStoryHost host)
        {
            host.Screens["guide"] = Open;
            host.Screens["report"] = ReportScreen.Open;
            if (!host.TryGetComponent(out GuideHotkey _)) host.gameObject.AddComponent<GuideHotkey>();
        }

        public static async Task<JToken> Open(JObject _)
        {
            var cards = GuideLogic.Cards(TerminalData.Facts);
            UiSound.Play("radio"); // the terminal keys up
            await Show<GuideScreen>(s => s._cards = cards, W, H);
            return null;
        }

        protected override object EscapeResult() => "close";

        protected override void Build()
        {
            var body = Terminal.Frame(Window, "생존 가이드 · 상황별 행동 요령", "긴급 재난 단말기", Terminal.Amber);
            float bw = W - Terminal.Inset * 2, bh = H - Terminal.Inset * 2 - Terminal.HeaderH - 2;
            const float pad = 28, gap = 16, top = 58, foot = 76;

            string Mark(string kind, string word) => $"<b><color={Terminal.Rgb(Terminal.KindColor(kind))}>{word}</color></b>";
            var legend = UiKit.Label("Legend", body,
                $"문장은 공식 자료에서 확인한 그대로입니다.   {Mark("do", "✓ 이렇게 한다")}   {Mark("warn", "! 주의한다")}   {Mark("never", "✕ 하지 않는다")}",
                17, Terminal.Muted, TextAnchor.MiddleLeft);
            UiKit.At(legend.rectTransform, pad, 14, bw - pad * 2, 30);

            int cols = 3, rows = Mathf.CeilToInt(_cards.Count / (float)cols);
            float cw = (bw - pad * 2 - gap * (cols - 1)) / cols, ch = (bh - top - foot - gap * (rows - 1)) / rows;
            for (int i = 0; i < _cards.Count; i++)
            {
                var rt = Card(body, _cards[i], cw, ch);
                UiKit.At(rt, pad + (i % cols) * (cw + gap), top + (i / cols) * (ch + gap), cw, ch);
                UiAppear.On(rt.gameObject, 0.08f + i * 0.05f);
            }

            var close = Terminal.Button(body, "닫기  ·  Esc", Terminal.Amber, true);
            UiKit.At((RectTransform)close.transform, bw - pad - 240, bh - foot + 16, 240, 48);
            close.onClick.AddListener(() => Finish("close"));
        }

        static RectTransform Card(Transform parent, GuideCard card, float w, float h)
        {
            Color tone = Terminal.ToneColor(card.Tone);
            var plate = UiKit.Box(card.Id, parent, Terminal.Plate);
            HudTheme.Round(plate);
            var rt = plate.rectTransform;
            UiKit.At(UiKit.Box("Tone", rt, tone).rectTransform, 14, 0, w - 28, 3);

            var badge = UiKit.Box("Badge", rt, Terminal.Tint(tone, 0.16f)); badge.sprite = HudDraw.DiscSprite;
            UiKit.At(badge.rectTransform, 20, 20, 52, 52);
            var icon = Terminal.Icon(badge.transform, card.Icon, tone, 28);
            var irt = icon.rectTransform; irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f); irt.anchoredPosition = Vector2.zero;

            float tagW = card.Tag.Length * 17 + 26;
            UiKit.At(UiKit.Label("Title", rt, card.Title, 25, Terminal.Ink, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, 86, 22, w - 86 - tagW - 30, 48);
            var tag = UiKit.Box("Tag", rt, Terminal.Tint(tone, 0.18f)); HudTheme.Round(tag);
            UiKit.At(tag.rectTransform, w - tagW - 20, 32, tagW, 28);
            UiKit.Fill(UiKit.Label("T", tag.transform, card.Tag, 15, tone, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform);

            float y = 90, textW = w - 40 - 34;
            if (card.Lines.Count == 0)
            {
                var none = UiKit.Label("None", rt, "이 게임이 확인한 공식 자료에는 이 상황의 행동 요령이 아직 없습니다. 지어낸 문장으로 채우지 않고 비워 둡니다.", 17, Terminal.Muted);
                UiKit.At(none.rectTransform, 20, y, w - 40, 80);
                return rt;
            }
            foreach (var line in card.Lines)
            {
                var mark = Terminal.Icon(rt, Terminal.KindIcon(line.Kind), Terminal.KindColor(line.Kind), 18);
                UiKit.At(mark.rectTransform, 20, y + 4, 18, 18);
                var t = UiKit.Label("Line", rt, line.Text, 17, Terminal.Ink);
                t.lineSpacing = 1.08f;
                UiKit.At(t.rectTransform, 54, y, textW, 10);
                float th = Mathf.Ceil(t.preferredHeight);
                t.rectTransform.sizeDelta = new Vector2(textW, th);
                y += th + 12;
            }
            var sources = card.Lines.Select(l => l.Source).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
            if (sources.Count > 0)
            {
                var src = UiKit.Label("Source", rt, "출처 · " + string.Join(", ", sources), 14, Terminal.Muted, TextAnchor.LowerLeft);
                src.horizontalOverflow = HorizontalWrapMode.Overflow;
                UiKit.At(src.rectTransform, 20, h - 34, w - 40, 22);
            }
            return rt;
        }
    }

    /// <summary>K while roaming opens the survival guide (not in a cutscene, a dialog or under another window).</summary>
    public sealed class GuideHotkey : MonoBehaviour
    {
        bool _open;

        void Update()
        {
            var kb = Keyboard.current;
            if (_open || kb == null || !kb.kKey.wasPressedThisFrame) return;
            var host = UnityStoryHost.Instance;
            if (!host || host.LevelId == null || host.Loading) return;
            if (InputGate.Blocked || UiScreen.OpenCount > 0 || GameHud.Cinematic || StoryOverlay.TalkOpen) return;
            Run();
        }

        async void Run()
        {
            _open = true;
            try { await GuideScreen.Open(null); }
            catch (System.Exception e) { Debug.LogException(e); }
            finally { _open = false; }
        }
    }

    /// <summary>
    /// 생존 학습 리포트: the run read back as a dashboard. Left: the share of judged actions that matched the guidance
    /// (ring gauge), the golden time (alert to shelter) and the final condition. Middle: the areas as a radar chart and
    /// bars. Right: every judged action as O / X, and for each X the guidance that applies, word for word from the facts.
    /// </summary>
    public class ReportScreen : UiScreen
    {
        public const float W = 1760, H = 1000;
        LearningReportData _d;
        TerminalGauge _gauge;
        TerminalRadar _radar;
        Text _rateText;
        int _shownPct; // the figure _rateText shows (it starts at "0%")
        readonly List<(RectTransform fill, float value)> _bars = new();

        public static async Task<JToken> Open(JObject _)
        {
            var host = UnityStoryHost.Instance;
            var data = LearningReport.Build(host ? host.getState?.Invoke() : null);
            UiSound.Play("report");
            await Show<ReportScreen>(s => s._d = data, W, H);
            return null;
        }

        protected override object EscapeResult() => "close";

        static Color RateColor(float rate) => rate >= 0.75f ? Terminal.Emerald : rate >= 0.5f ? Terminal.Amber : Terminal.Red;

        RectTransform Plate(Transform parent, string title, float x, float y, float w, float h)
        {
            var p = UiKit.Box("Plate", parent, Terminal.Plate); HudTheme.Round(p);
            UiKit.At(p.rectTransform, x, y, w, h);
            if (title != null) UiKit.At(UiKit.Label("H", p.transform, title, 16, Terminal.Muted, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, 20, 12, w - 40, 24);
            return p.rectTransform;
        }

        void Bar(Transform parent, string label, float value, Color color, string right, float y, float w)
        {
            UiKit.At(UiKit.Label("L", parent, label, 17, Terminal.Ink, TextAnchor.MiddleLeft).rectTransform, 20, y, 110, 26);
            var track = UiKit.Box("Track", parent, Terminal.Rule).rectTransform; UiKit.At(track, 134, y + 9, w - 134 - 96, 8);
            var fill = UiKit.Box("Fill", track, color); fill.sprite = HudTheme.GradientSprite;
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0, 1);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            _bars.Add((fill.rectTransform, Mathf.Clamp01(value)));
            UiKit.At(UiKit.Label("R", parent, right, 17, color, TextAnchor.MiddleRight, FontStyle.Bold).rectTransform, w - 90, y, 70, 26);
        }

        protected override void Build()
        {
            var body = Terminal.Frame(Window, "생존 학습 리포트", "시뮬레이션 종료 · 기록 분석", Terminal.Emerald);
            float bw = W - Terminal.Inset * 2, bh = H - Terminal.Inset * 2 - Terminal.HeaderH - 2;
            const float pad = 28, gap = 16, top = 20, foot = 76;
            float colH = bh - top - foot;
            float lw = 430, mw = 500, rw = bw - pad * 2 - lw - mw - gap * 2;
            float lx = pad, mx = lx + lw + gap, rx = mx + mw + gap;

            // ---- left: the headline, the golden time, the final condition ----
            var rateP = Plate(body, "행동 요령 준수율", lx, top, lw, 330);
            var gaugeGo = new GameObject("Gauge", typeof(RectTransform), typeof(CanvasRenderer));
            gaugeGo.transform.SetParent(rateP, false);
            _gauge = gaugeGo.AddComponent<TerminalGauge>();
            _gauge.raycastTarget = false; _gauge.Thickness = 18; _gauge.color = RateColor(_d.Rate);
            UiKit.At(_gauge.rectTransform, (lw - 210) / 2, 48, 210, 210);
            _rateText = UiKit.Label("Rate", rateP, _d.Total == 0 ? "–" : "0%", 56, Terminal.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.At(_rateText.rectTransform, (lw - 210) / 2, 48, 210, 210);
            string of = _d.Total == 0 ? "판정할 행동 기록이 아직 없습니다" : $"판정한 행동 {_d.Total}개 중 {_d.Ok}개가 행동 요령과 같았습니다";
            UiKit.At(UiKit.Label("Of", rateP, of, 17, Terminal.Muted, TextAnchor.MiddleCenter).rectTransform, 12, 270, lw - 24, 24);
            UiKit.At(UiKit.Label("Note", rateP, "생존 확률이 아니라, 기록된 행동의 비율입니다", 14, Terminal.Tint(Terminal.Muted, 0.75f), TextAnchor.MiddleCenter).rectTransform, 12, 296, lw - 24, 20);

            var goldP = Plate(body, "경보에서 대피소까지", lx, top + 330 + gap, lw, 136);
            if (_d.GoldenMinutes.HasValue)
            {
                bool ok = _d.GoldenOk == true;
                Color c = ok ? Terminal.Emerald : Terminal.Red;
                UiKit.At(Terminal.Icon(goldP, "clock", c, 44).rectTransform, 22, 56, 44, 44);
                UiKit.At(UiKit.Label("Min", goldP, $"{_d.GoldenMinutes}분", 44, Terminal.Ink, TextAnchor.MiddleLeft, FontStyle.Bold).rectTransform, 82, 48, 170, 56);
                UiKit.At(UiKit.Label("Limit", goldP, $"이 이야기의 제한 시간 {LearningReport.GoldenLimit}분", 16, Terminal.Muted, TextAnchor.MiddleLeft).rectTransform, 84, 102, 260, 22);
                var chip = UiKit.Box("Chip", goldP, Terminal.Tint(c, 0.18f)); HudTheme.Round(chip);
                UiKit.At(chip.rectTransform, lw - 150, 60, 128, 40);
                UiKit.Fill(UiKit.Label("T", chip.transform, ok ? "O  제때" : "X  늦음", 19, c, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform);
            }
            else UiKit.At(UiKit.Label("None", goldP, "이번 체험에는 경보 뒤 대피소에 도착한 기록이 없습니다.", 17, Terminal.Muted).rectTransform, 20, 52, lw - 40, 60);

            float vy = top + 330 + gap + 136 + gap;
            var vitP = Plate(body, "종료 시 상태", lx, vy, lw, colH - (vy - top));
            var stats = new (string key, string label, double v, bool worse)[] { ("health", "체력", _d.Health, false), ("thirst", "갈증", _d.Thirst, true), ("fatigue", "피로", _d.Fatigue, true), ("tension", "긴장", _d.Tension, true) };
            for (int i = 0; i < stats.Length; i++)
            {
                var s = stats[i];
                string word = GameRules.StatWord(s.key, s.v);
                Bar(vitP, s.label, (float)((s.worse ? 100 - s.v : s.v) / 100.0), HudTheme.StatColor(word), word, 48 + i * 34, lw);
            }

            // ---- middle: areas ----
            var areaP = Plate(body, "영역별 대처", mx, top, mw, colH);
            float ay = 48;
            if (_d.Axes.Count >= 3)
            {
                const float rs = 250;
                var radarGo = new GameObject("Radar", typeof(RectTransform), typeof(CanvasRenderer));
                radarGo.transform.SetParent(areaP, false);
                _radar = radarGo.AddComponent<TerminalRadar>();
                _radar.raycastTarget = false; _radar.color = Terminal.Emerald; _radar.Grow = 0;
                _radar.SetValues(_d.Axes.Select(a => a.Score));
                UiKit.At(_radar.rectTransform, (mw - rs) / 2, ay + 44, rs, rs);
                for (int i = 0; i < _d.Axes.Count; i++)
                {
                    var dir = TerminalRadar.AxisDir(i, _d.Axes.Count);
                    var l = UiKit.Label("Axis", areaP, _d.Axes[i].Label, 16, Terminal.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
                    float cx = mw / 2 + dir.x * (rs / 2 + 62), cy = ay + 44 + rs / 2 - dir.y * (rs / 2 + 24);
                    UiKit.At(l.rectTransform, cx - 70, cy - 12, 140, 24);
                }
                ay += rs + 100;
            }
            else if (_d.Axes.Count > 0)
            {
                UiKit.At(UiKit.Label("Few", areaP, "판정된 영역이 3개가 되면 레이더 차트로 보여 줍니다.", 16, Terminal.Muted).rectTransform, 20, ay, mw - 40, 44);
                ay += 52;
            }
            else UiKit.At(UiKit.Label("None", areaP, "아직 판정할 기록이 없습니다. 준비 단계와 경보 이후의 행동이 여기에 쌓입니다.", 17, Terminal.Muted).rectTransform, 20, ay, mw - 40, 60);
            foreach (var a in _d.Axes)
            {
                Bar(areaP, a.Label, a.Score, RateColor(a.Score), $"{a.Ok}/{a.Total}", ay, mw);
                ay += 34;
            }

            // ---- right: the judged actions ----
            int bad = _d.Total - _d.Ok;
            var listP = Plate(body, $"대처 항목     O {_d.Ok}  ·  X {bad}", rx, top, rw, colH);
            var col = DebriefUi.Scroll(listP, 12, 44, rw - 24, colH - 56, out _, 8);
            var facts = TerminalData.Facts;
            int n = 0;
            foreach (var row in _d.Rows.OrderBy(r => r.Ok))
            {
                Color c = row.Ok ? Terminal.Emerald : Terminal.Red;
                var card = DebriefUi.Card(col, row.Ok ? Terminal.PlateHi : Terminal.Tint(Terminal.Red, 0.05f), 16, 12, 6);
                HudTheme.Round(card.GetComponent<Image>());
                UiKit.Label("Label", card, $"<b><color={Terminal.Rgb(c)}>{(row.Ok ? "O" : "X")}</color></b>   {row.Label}", 19, Terminal.Ink, style: row.Ok ? FontStyle.Normal : FontStyle.Bold);
                var f = row.Ok ? null : facts.Fact(row.FactId);
                if (f != null)
                {
                    UiKit.Label("Why", card, "실제 상황에서는", 14, Terminal.Amber, style: FontStyle.Bold);
                    UiKit.Label("Fact", card, f.Text, 17, Terminal.Ink);
                    string pub = facts.Source(f.SourceId)?.Publisher;
                    if (!string.IsNullOrEmpty(pub)) UiKit.Label("Src", card, "출처 · " + pub, 14, Terminal.Muted);
                }
                UiAppear.On(card.gameObject, 0.25f + n++ * 0.05f, slide: false);
            }
            if (_d.Rows.Count == 0) UiKit.Label("Empty", col, "기록된 행동이 없습니다.", 18, Terminal.Muted);

            var close = Terminal.Button(body, "닫기  ·  Esc", Terminal.Emerald, true);
            UiKit.At((RectTransform)close.transform, bw - pad - 240, bh - foot + 16, 240, 48);
            close.onClick.AddListener(() => Finish("close"));
            UiKit.At(UiKit.Label("Fiction", body, "가상의 체험 기록입니다. 실제 상황에서는 정부와 지자체의 안내를 따르세요.", 15, Terminal.Muted, TextAnchor.MiddleLeft).rectTransform, pad, bh - foot + 28, 900, 24);
        }

        /// <summary>How far the gauge and bars have filled, 0..1, this long after the screen opened (pure; tested).</summary>
        public static float Fill(float sinceOpen, bool reduceMotion) => reduceMotion ? 1f : HudMotion.OutCubic(Mathf.Clamp01((sinceOpen - 0.3f) / 0.9f));

        protected override void Update()
        {
            base.Update();
            if (_d == null || !_gauge) return;
            float k = Fill(Time.unscaledTime - OpenedAt, AiOn.Rendering.AtmosphereDirector.ReduceMotion);
            _gauge.Value = _d.Rate * k;
            if (_d.Total > 0)
            {
                int pct = Mathf.RoundToInt(_d.Rate * k * 100f);
                if (pct != _shownPct) { _shownPct = pct; _rateText.text = pct + "%"; } // formats only when the figure changes
            }
            if (_radar) _radar.Grow = k;
            foreach (var (fill, value) in _bars) if (fill) fill.anchorMax = new Vector2(value * k, 1);
        }
    }
}
