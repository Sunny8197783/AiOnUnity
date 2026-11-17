// Pure logic of 정민's phone and the emergency alert sheet (original ui/phone.js): option parsing, the status bar,
// message headers, the map app's state texts, the share count of a passed-on post and the alert level.
// No Unity types here, so it is tested outside Unity.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AiOn.UI
{
    /// <summary>A button under the phone ({ id, label, primary }); a bare string is both id and label.</summary>
    public sealed class PhoneAction
    {
        /// <summary>What the screen resolves with (the original's id, any JSON value).</summary>
        public JToken Id;
        public string Label;
        public bool Primary;
    }

    /// <summary>One message ({ from, kind, time, text }); kind: emergency | safety | sms | me | sns.</summary>
    public sealed class PhoneMessage
    {
        public string From, Kind, Time, Text;
        /// <summary>What shareCount(m.text) hashes: String(text), so a JSON null text is "null" (absent: "").</summary>
        public string ShareKey;
    }

    /// <summary>The header line of a message bubble.</summary>
    public sealed class PhoneMessageHead
    {
        /// <summary>emergency/safety: the kind label (위급재난문자 / 안전안내문자) shown with an icon.</summary>
        public bool Official;
        public string KindLabel, Icon;
        /// <summary>Who sent it (non-official), or the extra sender after the kind label (official; null when none).</summary>
        public string From;
        public string Time;
    }

    /// <summary>Everything the phone shows, parsed from the original's option object.</summary>
    public sealed class PhoneView
    {
        public double Battery;
        public string Signal;
        public List<PhoneMessage> Messages = new();
        public List<PhoneMessage> Posts = new();
        /// <summary>ok | loading | fail, or null for no map app.</summary>
        public string MapApp;
        public List<PhoneAction> Actions = new();
        /// <summary>What Esc resolves with; null token when Esc does nothing.</summary>
        public JToken EscapeId;
        public bool HasEscape;
    }

    public static class PhoneLogic
    {
        static readonly Dictionary<string, (string text, int bars)> SignalTable = new()
        {
            ["ok"] = ("신호 좋음", 4), ["weak"] = ("신호 약함", 1), ["none"] = ("서비스 없음", 0),
        };
        static readonly Dictionary<string, (string label, string icon)> KindTable = new()
        {
            ["emergency"] = ("위급재난문자", "alert"), ["safety"] = ("안전안내문자", "info"), ["sms"] = ("문자", "sms"),
            ["me"] = ("나", "sms"), ["sns"] = ("SNS", "rumor"),
        };

        /// <summary>A share count for a fictional post, steady for the same text (original shareCount).</summary>
        public static int ShareCount(string text)
        {
            uint h = 7;
            text ??= "";
            for (int i = 0; i < text.Length; i++)
            {
                // for..of walks code points; a lone surrogate counts as its own code unit (codePointAt), never throws.
                int cp = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                h = unchecked(h * 31 + (uint)cp);
            }
            return 40 + (int)(h % 260);
        }

        /// <summary>The signal's spoken text and lit bars; an unknown signal reads as ok.</summary>
        public static (string text, int bars) Signal(string signal) =>
            signal != null && SignalTable.TryGetValue(signal, out var s) ? s : SignalTable["ok"];

        /// <summary>The small note after the bars: 서비스 없음 (0), 약함 (1), else null.</summary>
        public static string SignalNote(int bars) => bars == 0 ? "서비스 없음" : bars == 1 ? "약함" : null;

        /// <summary>JS Math.round (halves up).</summary>
        public static double JsRound(double x) => Math.Floor(x + 0.5);

        /// <summary>JS Number(v): null is 0, a bad string NaN.</summary>
        public static double JsNumber(JToken v)
        {
            if (v == null || v.Type == JTokenType.Null) return 0;
            switch (v.Type)
            {
                case JTokenType.Integer: case JTokenType.Float: return v.Value<double>();
                case JTokenType.Boolean: return v.Value<bool>() ? 1 : 0;
                case JTokenType.String:
                    var s = v.Value<string>().Trim();
                    if (s == "") return 0;
                    return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
                case JTokenType.Undefined: return double.NaN;
                default: return double.NaN;
            }
        }

        /// <summary>The battery as the status bar shows it: 0..100 (Number(battery) || 0, clamped).</summary>
        public static double ClampBattery(double battery) => double.IsNaN(battery) ? 0 : Math.Max(0, Math.Min(100, battery));

        /// <summary>"57%" of the status bar.</summary>
        public static string BatteryText(double battery) => $"{JsRound(ClampBattery(battery)).ToString(CultureInfo.InvariantCulture)}%";

        /// <summary>The status bar battery turns red at 20 % or less.</summary>
        public static bool BatteryLowColour(double battery) => ClampBattery(battery) <= 20;

        /// <summary>The power-saving banner shows at 15 % or less.</summary>
        public static bool LowBattery(double battery) => battery <= 15;

        public static string BannerText(double battery) => $"배터리 {Math.Max(0, JsRound(battery)).ToString(CultureInfo.InvariantCulture)}% · 절전 모드";

        /// <summary>Header of a non-SNS bubble (original bubble()).</summary>
        public static PhoneMessageHead Head(PhoneMessage m)
        {
            var kind = m.Kind ?? "sms";
            var (label, icon) = KindTable.TryGetValue(kind, out var k) ? k : KindTable["sms"];
            bool official = m.Kind == "emergency" || m.Kind == "safety";
            var head = new PhoneMessageHead { Official = official, KindLabel = label, Icon = icon, Time = m.Time ?? "" };
            if (official) head.From = !string.IsNullOrEmpty(m.From) && m.From != label ? m.From : null;
            else head.From = m.Kind == "me" ? "나" : (string.IsNullOrEmpty(m.From) ? "알 수 없음" : m.From);
            return head;
        }

        /// <summary>CSS class kind of a bubble (msg-emergency, msg-safety, msg-me, msg-sns, else sms).</summary>
        public static string BubbleKind(PhoneMessage m) => string.IsNullOrEmpty(m.Kind) ? "sms" : m.Kind;

        /// <summary>The map app's (title, subtitle) for ok | loading | fail; an unknown state shows nothing.</summary>
        public static (string title, string sub) MapState(string state) => state switch
        {
            "ok" => ("지도", "현재 위치 · 연무동"),
            "loading" => ("지도를 불러오는 중…", "데이터 연결이 느립니다"),
            "fail" => ("지도를 불러올 수 없습니다", "네트워크 연결을 확인해 주세요"),
            _ => (null, null),
        };

        /// <summary>The status-bar clock: the time of the newest non-SNS message.</summary>
        public static string StatusTime(IReadOnlyList<PhoneMessage> messages) => messages.Count > 0 ? messages[messages.Count - 1].Time ?? "" : "";

        static string Str(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined) return null;
            return t.Type == JTokenType.String ? t.Value<string>() : t.ToString(Newtonsoft.Json.Formatting.None);
        }

        static bool Truthy(JToken t)
        {
            if (t == null) return false;
            switch (t.Type)
            {
                case JTokenType.Null: case JTokenType.Undefined: return false;
                case JTokenType.Boolean: return t.Value<bool>();
                case JTokenType.String: return t.Value<string>() != "";
                case JTokenType.Integer: case JTokenType.Float: var d = t.Value<double>(); return d != 0 && !double.IsNaN(d);
                default: return true;
            }
        }

        /// <summary>phone()'s actions: none (or empty) means a single 닫기; strings become { id, label }.</summary>
        public static List<PhoneAction> Actions(JToken actions)
        {
            var list = new List<PhoneAction>();
            if (actions is JArray arr && arr.Count > 0)
            {
                foreach (var a in arr)
                {
                    if (a == null || a.Type == JTokenType.Null) continue;
                    if (a.Type == JTokenType.String) { list.Add(new PhoneAction { Id = a.DeepClone(), Label = a.Value<string>() }); continue; }
                    if (a is JObject o)
                    {
                        var id = o["id"]?.DeepClone() ?? JValue.CreateNull();
                        var label = Str(o["label"]) ?? Str(id) ?? "";
                        list.Add(new PhoneAction { Id = id, Label = label, Primary = Truthy(o["primary"]) });
                    }
                }
            }
            if (list.Count == 0) list.Add(new PhoneAction { Id = "close", Label = "닫기" });
            return list;
        }

        /// <summary>Esc picks the 'close' or 'back' action, else the only action, else nothing.</summary>
        public static bool EscapeId(IReadOnlyList<PhoneAction> acts, out JToken id)
        {
            var hit = acts.FirstOrDefault(a => a.Id?.Type == JTokenType.String && (a.Id.Value<string>() == "close" || a.Id.Value<string>() == "back"));
            if (hit != null) { id = hit.Id; return true; }
            // `?? (acts.length === 1 ? acts[0].id : null)` then `escId != null`: an only action without an id gives no Esc.
            if (acts.Count == 1 && acts[0].Id != null && acts[0].Id.Type != JTokenType.Null) { id = acts[0].Id; return true; }
            id = null;
            return false;
        }

        static PhoneMessage Message(JObject o) => new PhoneMessage
        {
            From = Str(o["from"]), Kind = Str(o["kind"]), Time = Str(o["time"]), Text = Str(o["text"]),
            ShareKey = o["text"]?.Type == JTokenType.Null ? "null" : Str(o["text"]) ?? "",
        };

        /// <summary>Parses phone({ battery = 50, signal = 'ok', messages = [], mapApp = null, actions = null }).</summary>
        public static PhoneView Parse(JObject options)
        {
            options ??= new JObject();
            var v = new PhoneView();
            var b = options["battery"];
            v.Battery = b == null || b.Type == JTokenType.Undefined ? 50 : JsNumber(b);
            var sig = options["signal"];
            v.Signal = sig == null || sig.Type == JTokenType.Undefined ? "ok" : Str(sig);
            var msgs = options["messages"];
            IEnumerable<JToken> all = msgs is JArray ma ? ma : msgs is JObject mo ? new JToken[] { mo } : Array.Empty<JToken>();
            foreach (var t in all)
            {
                if (t is not JObject o) continue;
                var m = Message(o);
                (m.Kind == "sns" ? v.Posts : v.Messages).Add(m);
            }
            var map = options["mapApp"];
            v.MapApp = Truthy(map) ? Str(map) : null;
            v.Actions = Actions(options["actions"]);
            v.HasEscape = EscapeId(v.Actions, out v.EscapeId);
            return v;
        }

        /// <summary>The alert sheet's level: critical (위급), urgent (긴급) or safety.</summary>
        public static string AlertLevel(string kind)
        {
            kind ??= "위급재난문자";
            return kind.Contains("위급") ? "critical" : kind.Contains("긴급") ? "urgent" : "safety";
        }

        /// <summary>Parses emergencyAlert({ kind = '위급재난문자', text = '', time = '' }).</summary>
        public static (string kind, string text, string time, string level) ParseAlert(JObject options)
        {
            options ??= new JObject();
            string Or(string key, string def)
            {
                var t = options[key];
                return t == null || t.Type == JTokenType.Undefined ? def : Str(t) ?? "";
            }
            var kind = Or("kind", "위급재난문자");
            return (kind, Or("text", ""), Or("time", ""), AlertLevel(kind));
        }
    }
}
