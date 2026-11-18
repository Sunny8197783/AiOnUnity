using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.UI
{
    /// <summary>The radio screen's option object (original ui/radio.js arguments, minus the onSignal callback).</summary>
    public sealed class RadioOptions
    {
        /// <summary>guided | hint | free</summary>
        public string Mode = "guided";
        public readonly List<RadioStation> Stations = new();
        /// <summary>The JSON each station came from (same order as Stations), handed back as the result's station.</summary>
        public readonly List<JObject> Sources = new();
        public double Start = 89.5;
        public bool Battery = true;
        public double ExtraSeconds;
        /// <summary>radio | phone</summary>
        public string Device = "radio";

        public static RadioOptions Parse(JObject o)
        {
            o ??= new JObject();
            var r = new RadioOptions();
            // JS default applies only to undefined: an explicit null/other mode is neither guided, hint nor free ("").
            if (o["mode"] is JToken m && m.Type != JTokenType.Undefined) r.Mode = m.Type == JTokenType.String ? (string)m : "";
            foreach (var t in o["stations"] as JArray ?? new JArray())
            {
                if (!(t is JObject s)) continue;
                r.Stations.Add(new RadioStation
                {
                    Freq = RadioLogic.Number(s["freq"]) ?? double.NaN,
                    Name = (string)s["name"] ?? "",
                    Kind = (string)s["kind"] ?? "",
                    Lines = (s["lines"] as JArray ?? new JArray()).Select(l => (string)l ?? "").ToArray(),
                });
                r.Sources.Add(s);
            }
            // JS: r1(Number(start) || 89.5)
            var start = RadioLogic.Number(o["start"]);
            r.Start = start is double st && st != 0 && !double.IsNaN(st) ? st : 89.5;
            r.Battery = o["battery"] == null || o["battery"].Type == JTokenType.Undefined || RadioLogic.Truthy(o["battery"]);
            r.ExtraSeconds = RadioLogic.Number(o["extraSeconds"]) ?? 0;
            if (double.IsNaN(r.ExtraSeconds)) r.ExtraSeconds = 0;
            if (o["device"] is JToken d && d.Type == JTokenType.String) r.Device = (string)d;
            return r;
        }
    }

    /// <summary>Constants, words and formatting of the radio screen (original ui/radio.js top level).</summary>
    public static class RadioLogic
    {
        public const double Min = 87.5, Max = 108, Lock = 0.75;

        /// <summary>Clamp to the band and round to 0.1 MHz.</summary>
        public static double R1(double f) => Num.JsRound(Math.Min(Max, Math.Max(Min, f)) * 10) / 10;

        public static string Word(double c) => c <= 0 ? "신호 없음" : c < 0.4 ? "약함" : c < Lock ? "보통" : "잘 들림";

        /// <summary>How long a caption line stays before the next one (seconds).</summary>
        public static double LineSeconds(string s) => Math.Min(5.5, Math.Max(2.4, 1.2 + (s ?? "").Length * 0.055));

        /// <summary>Freq as JS prints a number (95.1 -> "95.1", 90 -> "90").</summary>
        public static string FreqText(double f) => f.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>freq.toFixed(1)</summary>
        public static string Fixed1(double f) => (Num.JsRound(f * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture);

        public static readonly Dictionary<string, string> ModeText = new()
        {
            ["guided"] = "안내 있음", ["hint"] = "힌트 가능", ["free"] = "혼자 판단", ["view"] = "보기",
        };

        /// <summary>Info pill (icon, text) for a kind (original mini-dom LABELS).</summary>
        public static (string icon, string text) Pill(string kind) => kind switch
        {
            "official" => ("◆", "공식 안내"),
            "site" => ("◎", "현장 확인"),
            "neighbor" => ("●", "이웃에게 들음"),
            "rumor" => ("?", "소문·미확인"),
            _ => ("✎", "내 기록"),
        };

        public const string KeysHint = "손잡이를 끌어 돌리기 · 휠 0.1MHz · 다이얼에서 ←/→ 0.3MHz · Shift+←/→ 0.1MHz · PgUp/PgDn 1MHz · Esc 끄기";

        /// <summary>X of frequency f on a dial of width w (original drawDial x()).</summary>
        public static double DialX(double f, double w) => 30 + ((f - Min) / (Max - Min)) * (w - 60);

        /// <summary>One waveform sample's offset from the middle (original drawWave), noise in -1..1.</summary>
        public static double WaveOffset(double x, double t, double clarity, bool voice, double h, double noise)
        {
            double v = clarity * (voice ? 1 : 0.25) * Math.Sin(x * 0.09 + t * 9) * Math.Sin(x * 0.017 + t * 2.1) * h * 0.34;
            double n = noise * (1 - clarity) * h * 0.4;
            return v + n;
        }

        public static double? Number(JToken t)
        {
            if (t == null) return null;
            switch (t.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float: return (double)t;
                case JTokenType.String:
                    var s = ((string)t).Trim();
                    if (s == "") return 0;
                    return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
                case JTokenType.Boolean: return (bool)t ? 1 : 0;
                case JTokenType.Null: return 0;
                default: return double.NaN;
            }
        }

        public static bool Truthy(JToken t)
        {
            if (t == null) return false;
            switch (t.Type)
            {
                case JTokenType.Null:
                case JTokenType.Undefined: return false;
                case JTokenType.Boolean: return (bool)t;
                case JTokenType.Integer:
                case JTokenType.Float: var d = (double)t; return d != 0 && !double.IsNaN(d);
                case JTokenType.String: return ((string)t).Length > 0;
                default: return true;
            }
        }
    }

    /// <summary>A line that came over the air.</summary>
    public sealed class RadioHeard
    {
        public RadioStation Station;
        public string Text;
        public int Index;
    }

    /// <summary>
    /// The radio's state machine (original ui/radio.js without the DOM): power, antenna/earphones, the dial, the locked
    /// station and its captions, what was heard and what was noted, and the result. The screen draws it.
    /// </summary>
    public sealed class RadioTuner
    {
        public readonly RadioOptions Options;
        public bool Phone => Options.Device == "phone";
        public string Aerial => Phone ? "이어폰" : "안테나";
        public string Mode => Options.Mode;

        public double Freq { get; private set; }
        public bool Power { get; private set; }
        /// <summary>The power was switched on at least once (the phone app costs battery only then).</summary>
        public bool WasPowered { get; private set; }
        public bool Antenna { get; private set; }
        public bool CanPower { get; private set; }
        public bool Closed { get; private set; }
        public bool Dragging { get; private set; }

        /// <summary>Station the dial is locked on (null: none).</summary>
        public RadioStation Current { get; private set; }
        /// <summary>Clarity after the antenna factor (0..1).</summary>
        public double Clarity { get; private set; }

        public string Status { get; private set; } = "";
        /// <summary>The battery-loading bar is showing (seconds it takes).</summary>
        public double LoadingSeconds { get; private set; }
        public string CaptionName { get; private set; } = "";
        /// <summary>Kind for the caption pill (guided mode, official/rumor only), else null.</summary>
        public string CaptionPill { get; private set; }
        public string CaptionText { get; private set; } = "";

        public readonly List<RadioHeard> Heard = new();
        readonly List<(string text, RadioStation station)> _noted = new();
        readonly HashSet<string> _seen = new();
        readonly Dictionary<RadioStation, int> _pos = new();
        double _lockT, _nextAt = 0.8, _readyIn = -1;
        string _shownState = "";

        public RadioTuner(RadioOptions options)
        {
            Options = options ?? new RadioOptions();
            Freq = RadioLogic.R1(Options.Start);
        }

        /// <summary>The station the help points at: the first official one, else the first.</summary>
        public RadioStation Target => Options.Stations.FirstOrDefault(s => s.Kind == "official") ?? Options.Stations.FirstOrDefault();

        // ---------- texts ----------
        public string Eyebrow => Phone ? "FM 라디오" : "라디오";
        public string Title => Phone ? "휴대폰 FM 라디오 앱 · 배터리 사용" : "휴대용 라디오";
        public string OffLabel => Phone ? "앱 닫기" : "라디오 끄기";
        public string PowerLabel => Power ? (Phone ? "앱 끄기" : "전원 끄기") : (Phone ? "FM 라디오 앱 켜기" : "전원 켜기");
        public string AntennaLabel => Antenna ? (Phone ? "이어폰 빼기" : "안테나 접기") : (Phone ? "이어폰 꽂기" : "안테나 펴기");
        public string Readout => Power ? RadioLogic.Fixed1(Freq) : "--.-";
        // The original read "신호 신호 없음" with no signal (word() already says 신호); here it is just "신호 없음".
        public string SignalText => Power ? $"{(Clarity <= 0 ? "" : "신호 ")}{RadioLogic.Word(Clarity)}{(Antenna ? "" : $" · {Aerial} 없음")}" : "꺼짐";
        public bool BarOn(int i) => Power && Clarity * 5 > i + 0.01;
        /// <summary>Knob rotation, clockwise degrees (2 MHz per turn).</summary>
        public double KnobDegrees => (Freq - RadioLogic.Min) * 180;
        /// <summary>What onSignal gets: the clarity while on, null while off.</summary>
        public double? SignalValue => Power && !Closed ? Clarity : (double?)null;

        public string[] HelpLines()
        {
            var t = Target;
            return new[]
            {
                $"전원을 켜고 {(Aerial == "안테나" ? "안테나를 편 뒤" : "이어폰을 꽂은 뒤")}, 손잡이를 천천히 돌려 보세요 — 목표 {(t != null ? $"{RadioLogic.FreqText(t.Freq)}MHz" : "방송이 들리는 곳")}",
                $"잡음이 줄고 신호 막대가 차오르면 방송이 들립니다. {(Phone ? "휴대폰 FM 라디오는 이어폰 줄이 안테나 역할을 합니다." : "안테나를 접으면 신호가 약해집니다.")}",
            };
        }

        // ---------- start ----------

        /// <summary>Initial state; the battery decides when the switch works. Returns seconds until ready (0 now, -1 never).</summary>
        public double Begin()
        {
            Refresh();
            if (!Options.Battery)
            {
                Status = Phone ? "배터리가 없어 켜지지 않는다." : "건전지가 없어 켜지지 않는다.";
                CaptionText = "";
                _readyIn = -1;
            }
            else if (Options.ExtraSeconds > 0)
            {
                LoadingSeconds = Math.Min(4, Options.ExtraSeconds / 10);
                Status = "어둠 속에서 건전지를 찾아 넣는 중… ";
                _readyIn = LoadingSeconds;
            }
            else { Ready(); _readyIn = 0; }
            return _readyIn;
        }

        public void Ready()
        {
            if (Closed) return;
            CanPower = true;
            LoadingSeconds = 0;
            Status = Phone ? "FM 라디오 앱을 켜세요." : "전원 스위치를 켜세요.";
        }

        // ---------- controls ----------

        public void TogglePower()
        {
            if (!CanPower || Closed) return;
            if (Power) { Power = false; Refresh(); }
            else { Power = true; WasPowered = true; Status = ""; Refresh(); }
        }

        public void ToggleAntenna()
        {
            if (Closed) return;
            Antenna = !Antenna;
            Refresh();
        }

        public void SetFreq(double f)
        {
            if (!Power || Closed) return;
            Freq = RadioLogic.R1(f);
            Refresh();
        }

        public void Nudge(double step) => SetFreq(Freq + step);
        public void Wheel(bool up) => SetFreq(Freq + (up ? 0.1 : -0.1));

        /// <summary>Arrow steps: 0.3 MHz, Shift 0.1; PgUp/PgDn 1 MHz.</summary>
        public static double ArrowStep(bool shift) => shift ? 0.1 : 0.3;

        public bool KnobDown()
        {
            if (!Power || Closed) return false;
            Dragging = true;
            return true;
        }

        /// <summary>The knob turned by d radians (clockwise positive); one full turn is 2 MHz, not rounded until let go.</summary>
        public void KnobTurn(double d)
        {
            if (!Dragging || Closed) return;
            if (d > Math.PI) d -= 2 * Math.PI;
            if (d < -Math.PI) d += 2 * Math.PI;
            Freq = Math.Min(RadioLogic.Max, Math.Max(RadioLogic.Min, Freq + (d / (2 * Math.PI)) * 2));
            Refresh();
        }

        public void KnobUp()
        {
            if (!Dragging) return;
            Dragging = false;
            Freq = RadioLogic.R1(Freq);
            Refresh();
        }

        // ---------- signal ----------

        /// <summary>Recomputes signal, lock and caption (original update()).</summary>
        public void Refresh()
        {
            RadioStation station = null;
            double raw = 0;
            if (Power)
            {
                var sig = Drills.RadioSignal(RadioLogic.R1(Freq), Options.Stations);
                station = sig.Station; raw = sig.Clarity;
            }
            // Antenna folded / no earphones: only a fraction of the signal gets through, never enough to lock.
            Clarity = raw * (Antenna ? 1 : 0.6);
            var locked = Power && station != null && Clarity >= RadioLogic.Lock ? station : null;
            if (locked != Current) { Current = locked; _lockT = 0; _nextAt = 0.8; }
            string state = !Power ? "off" : Current != null ? $"on:{RadioLogic.FreqText(Current.Freq)}" : Clarity > 0 ? "near" : "static";
            if (state != _shownState)
            {
                _shownState = state;
                if (Current != null)
                {
                    CaptionName = Current.Name;
                    CaptionPill = Mode == "guided" && (Current.Kind == "official" || Current.Kind == "rumor") ? Current.Kind : null;
                    CaptionText = "…";
                }
                else if (state == "near")
                {
                    CaptionName = ""; CaptionPill = null;
                    CaptionText = Mode == "free" ? "…지직… 말소리가 섞여 들린다…" : "…지직… 말소리가 섞여 들린다. 조금만 더 맞춰 보자.";
                }
                else if (state == "static")
                {
                    CaptionName = ""; CaptionPill = null;
                    CaptionText = "치이이익— (잡음뿐이다)";
                }
            }
        }

        /// <summary>
        /// Time passes (seconds, clamped to 0.1): the battery finishes loading, and a locked station speaks its next line.
        /// Returns the newly heard line (null when nothing new reached the transcript).
        /// </summary>
        public RadioHeard Tick(double dt)
        {
            dt = Math.Max(0, Math.Min(0.1, dt));
            if (Closed) return null;
            if (_readyIn > 0) { _readyIn -= dt; if (_readyIn <= 0) { _readyIn = 0; Ready(); } }
            RadioHeard added = null;
            var cur = Current;
            if (cur != null && cur.Lines != null && cur.Lines.Length > 0)
            {
                _lockT += dt;
                if (_lockT >= _nextAt)
                {
                    int i = _pos.TryGetValue(cur, out var p) ? p : 0;
                    CaptionText = cur.Lines[i];
                    added = AddHeard(cur, i);
                    _pos[cur] = (i + 1) % cur.Lines.Length;
                    _nextAt = _lockT + RadioLogic.LineSeconds(cur.Lines[i]) + (i + 1 == cur.Lines.Length ? 2.5 : 0);
                }
            }
            return added;
        }

        RadioHeard AddHeard(RadioStation station, int i)
        {
            var key = $"{RadioLogic.FreqText(station.Freq)}:{i}";
            if (!_seen.Add(key)) return null;
            var h = new RadioHeard { Station = station, Text = station.Lines[i], Index = i };
            Heard.Add(h);
            return h;
        }

        // ---------- notes ----------

        public bool IsNoted(string text) => _noted.Any(n => n.text == text);

        /// <summary>'메모' on a heard line: keeps it, or lets it go again. Returns whether it is kept now.</summary>
        public bool ToggleNote(RadioHeard h)
        {
            int i = _noted.FindIndex(n => n.text == h.Text);
            if (i < 0) { _noted.Add((h.Text, h.Station)); return true; }
            _noted.RemoveAt(i);
            return false;
        }

        // ---------- result ----------

        /// <summary>The station heard most (ties: the one heard later).</summary>
        public RadioStation Best()
        {
            var n = new Dictionary<RadioStation, int>();
            foreach (var h in Heard) n[h.Station] = (n.TryGetValue(h.Station, out var c) ? c : 0) + 1;
            RadioStation top = null;
            foreach (var h in Heard) if (top == null || n[h.Station] >= n[top]) top = h.Station;
            return top;
        }

        public void Close() { Closed = true; Dragging = false; }

        /// <summary>{ station, official, heard, noted: [{ text, kind }] }</summary>
        public JObject Result()
        {
            var best = Best();
            int bi = best == null ? -1 : Options.Stations.IndexOf(best);
            return new JObject
            {
                ["station"] = bi >= 0 ? Options.Sources[bi].DeepClone() : JValue.CreateNull(),
                ["official"] = Heard.Any(h => h.Station.Kind == "official"),
                ["heard"] = new JArray(Heard.Select(h => h.Text)),
                ["noted"] = new JArray(_noted.Select(x => NotedJson(x.text, x.station))),
                ["powered"] = WasPowered,
            };
        }

        /// <summary>{ text, kind: station.kind } — kind is the station's own JSON value (left out when the station had none, as JSON.stringify drops undefined).</summary>
        JObject NotedJson(string text, RadioStation station)
        {
            var o = new JObject { ["text"] = text };
            int i = Options.Stations.IndexOf(station);
            var kind = i >= 0 && i < Options.Sources.Count ? Options.Sources[i]["kind"] : new JValue(station.Kind);
            if (kind != null) o["kind"] = kind.DeepClone();
            return o;
        }
    }
}
