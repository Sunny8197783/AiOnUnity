// Port of story/echo.js — 전 회차의 나 (echoes): what you did in earlier runs stays in the world for the next one. The
// path you walked comes back as a pale ghost that walks it again, the spots where you fell are marked with a red X,
// and chalk notes you wrote on the ground (B) are still there. Kept in the player's storage, last 3 runs.
//
// The original built three.js meshes (canvas text planes, a translucent person). Here the recorder keeps the same
// data and the same storage json; what to draw for a level is DATA (EchoMarks) the host renders: the ✕ marks with
// their label, the chalk notes, and the ghost's path (the host walks a translucent person along it at 1.5 m/s and
// starts over, like the original).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public sealed class EchoDeath
    {
        [JsonProperty("lv")] public string Lv;
        [JsonProperty("x")] public double X;
        [JsonProperty("z")] public double Z;
        [JsonProperty("why")] public string Why = "";
        [JsonProperty("yaw")] public double Yaw;
    }

    public sealed class EchoNote
    {
        [JsonProperty("lv")] public string Lv;
        [JsonProperty("x")] public double X;
        [JsonProperty("z")] public double Z;
        [JsonProperty("text")] public string Text;
        [JsonProperty("yaw")] public double Yaw;
    }

    /// <summary>One run's marks. Trail points are (level, x, z), stored as the original's [lv, x, z] arrays.</summary>
    public sealed class EchoRun
    {
        public List<(string Lv, double X, double Z)> Trail = new();
        public List<EchoDeath> Deaths = new();
        public List<EchoNote> Notes = new();

        public JObject ToJson() => new JObject
        {
            ["trail"] = new JArray(Trail.Select(p => new JArray(p.Lv, p.X, p.Z))),
            ["deaths"] = JArray.FromObject(Deaths),
            ["notes"] = JArray.FromObject(Notes),
        };
    }

    public sealed class EchoData
    {
        public List<EchoRun> Runs = new();
        public string ToJson() => new JObject { ["runs"] = new JArray(Runs.Select(r => r.ToJson())) }.ToString(Formatting.None);
    }

    /// <summary>What to draw in one level for the earlier runs (the original's show(L)).</summary>
    public sealed class EchoMarks
    {
        public sealed class Cross
        {
            public double X, Z, Yaw;
            /// <summary>"{age}회 전의 내가 쓰러진 곳 · {why}", drawn 0.9 m to the near side, facing yaw.</summary>
            public string Label;
            /// <summary>1 = the last run.</summary>
            public int Age;
        }
        public string Level;
        public List<Cross> Crosses = new();
        public List<EchoNote> Notes = new();
        /// <summary>The last run's walk through this level; empty when there is no ghost (5 points or fewer).</summary>
        public List<(double X, double Z)> Ghost = new();
        public bool Any => Crosses.Count > 0 || Notes.Count > 0 || Ghost.Count > 0;

        /// <summary>
        /// The level-state value the host renders (key Echo.StateKey): { level, crosses: [{x,z,yaw,age,label}],
        /// notes: [{lv,x,z,text,yaw}], ghost: [[x,z],...] }. Crosses are '✕' in #e0302a (1.6 m) with the label in #ff8a7a
        /// (3 m) 0.9 m to the near side facing yaw; notes in #f4f1e8 (3 m) facing yaw ± 0.15 rad; the ghost is a translucent
        /// person (jeongmin, opacity 0.3, emissive #6fa8ff) walking the path at 1.5 m/s and starting over.
        /// </summary>
        public JObject ToJson() => new JObject
        {
            ["level"] = Level,
            ["crosses"] = new JArray(Crosses.Select(c => new JObject { ["x"] = c.X, ["z"] = c.Z, ["yaw"] = c.Yaw, ["age"] = c.Age, ["label"] = c.Label })),
            ["notes"] = JArray.FromObject(Notes),
            ["ghost"] = new JArray(Ghost.Select(p => new JArray(p.X, p.Z))),
        };
    }

    public static class Echo
    {
        public const string Key = "gyeongbo.echo.v1";
        /// <summary>The level-state key the marks of earlier runs are published under (Engine.ApplyLevelState).</summary>
        public const string StateKey = "echo";
        public const int Runs = 3, MaxTrail = 1500, MaxNotes = 30;
        public static readonly string[] Presets = { "여기 위험해", "이 길 막혔음", "물 있음", "숨을 곳 있음", "여기서 맞았다. 엎드려" };
        public static readonly IReadOnlyDictionary<string, string> Why = new Dictionary<string, string>
        {
            ["fall"] = "추락", ["fight"] = "몸싸움", ["blast"] = "포격", ["debris"] = "붕괴", ["fire"] = "화재", ["smoke"] = "연기", ["hurt"] = "부상",
        };

        /// <summary>A run that left a mark.</summary>
        public static bool Worth(EchoRun r) => r.Trail.Count > 4 || r.Notes.Count > 0 || r.Deaths.Count > 0;

        static bool IsNum(JToken t) => t != null && (t.Type == JTokenType.Integer || (t.Type == JTokenType.Float && !double.IsNaN((double)t) && !double.IsInfinity((double)t)));
        static bool IsStr(JToken t) => t != null && t.Type == JTokenType.String;
        static IEnumerable<T> Last<T>(IEnumerable<T> xs, int n) { var l = xs.ToList(); return l.Skip(Math.Max(0, l.Count - n)); }

        /// <summary>String(v) for the death cause (null/undefined become "").</summary>
        static string JsString(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined) return "";
            if (t.Type == JTokenType.String) return (string)t;
            if (t.Type == JTokenType.Boolean) return (bool)t ? "true" : "false";
            if (t.Type is JTokenType.Integer or JTokenType.Float)
            {
                double x = (double)t;
                return Math.Floor(x) == x && Math.Abs(x) < 1e15 ? ((long)x).ToString(CultureInfo.InvariantCulture) : x.ToString("R", CultureInfo.InvariantCulture);
            }
            return t.ToString(Formatting.None);
        }

        /// <summary>Saved runs come from storage: keep only well-formed entries (last 3 runs, runs that left a mark).</summary>
        public static EchoData ReadEcho(string raw)
        {
            JToken o;
            try { o = raw == null ? null : JToken.Parse(raw); } catch (JsonException) { return new EchoData(); }
            var runs = (o as JObject)?["runs"] is JArray a ? Last(a, Runs).ToList() : new List<JToken>();
            var data = new EchoData();
            foreach (var r in runs)
            {
                var ro = r as JObject;
                var run = new EchoRun();
                if (ro?["trail"] is JArray tr)
                    run.Trail = Last(tr.OfType<JArray>().Where(p => p.Count > 0 && IsStr(p[0]) && p.Count > 2 && IsNum(p[1]) && IsNum(p[2]))
                        .Select(p => ((string)p[0], (double)p[1], (double)p[2])), MaxTrail).ToList();
                if (ro?["deaths"] is JArray de)
                    run.Deaths = Last(de.OfType<JObject>().Where(d => IsStr(d["lv"]) && IsNum(d["x"]) && IsNum(d["z"])), 10)
                        .Select(d => new EchoDeath { Lv = (string)d["lv"], X = (double)d["x"], Z = (double)d["z"], Why = JsString(d["why"]), Yaw = IsNum(d["yaw"]) ? (double)d["yaw"] : 0 }).ToList();
                if (ro?["notes"] is JArray no)
                    run.Notes = Last(no.OfType<JObject>().Where(n => IsStr(n["lv"]) && IsNum(n["x"]) && IsNum(n["z"]) && IsStr(n["text"])), MaxNotes)
                        .Select(n => { var t = (string)n["text"]; return new EchoNote { Lv = (string)n["lv"], X = (double)n["x"], Z = (double)n["z"], Text = t.Length > 24 ? t.Substring(0, 24) : t, Yaw = IsNum(n["yaw"]) ? (double)n["yaw"] : 0 }; }).ToList();
                if (Worth(run)) data.Runs.Add(run);
            }
            return data;
        }

        /// <summary>The marks of the earlier runs for one level.</summary>
        public static EchoMarks MarksFor(EchoData data, string levelId)
        {
            var m = new EchoMarks { Level = levelId };
            var past = data.Runs;
            for (int k = 0; k < past.Count; k++)
            {
                int age = past.Count - k; // 1 = the last run
                foreach (var d in past[k].Deaths.Where(d => d.Lv == levelId))
                    m.Crosses.Add(new EchoMarks.Cross { X = d.X, Z = d.Z, Yaw = d.Yaw, Age = age, Label = $"{age}회 전의 내가 쓰러진 곳 · {(Why.TryGetValue(d.Why ?? "", out var w) ? w : "")}" });
                m.Notes.AddRange(past[k].Notes.Where(n => n.Lv == levelId));
            }
            var last = past.Count > 0 ? past[^1] : null;
            var pts = last == null ? new List<(double, double)>() : last.Trail.Where(p => p.Lv == levelId).Select(p => (p.X, p.Z)).ToList();
            if (pts.Count > 4) m.Ghost = pts;
            return m;
        }

        /// <summary>
        /// PRESETS[Number(pick)]: JS Number() turns null, '' and blank text into 0 (the first preset), trims, and anything
        /// that is not an index gives undefined (null here).
        /// </summary>
        public static string PresetFor(string pick)
        {
            if (pick == null) return Presets[0];
            var t = pick.Trim();
            if (t.Length == 0) return Presets[0];
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var k) || Math.Floor(k) != k || k < 0 || k >= Presets.Length) return null;
            return Presets[(int)k];
        }

        /// <summary>createEcho: the recorder. get/set read and write the player's storage (string by key).</summary>
        public static EchoRecorder Create(StoryContext ctx, Func<string, string> get, Action<string, string> set) => new EchoRecorder(ctx, get, set);
    }

    /// <summary>
    /// createEcho({ engine, ctx, store }) -> { begin(), resume(), death(why), chalk() }. Records the walk while roaming
    /// (a point every 1.5 m, saved every 10 s), and on entering a level publishes that level's marks of earlier runs
    /// through Show (the host draws them) with a toast the first time a level with marks is shown.
    /// The host should also call Save() (or EchoRecorder.SaveAll()) when the app is paused or quits (the original's pagehide).
    /// </summary>
    public sealed class EchoRecorder
    {
        readonly StoryContext _ctx;
        readonly Func<string, string> _get;
        readonly Action<string, string> _set;
        EchoData _data;
        EchoRun _run; // the run being recorded
        string _shownLevel;
        bool _told; // the original's shown.told: the 'traces' toast once per run, not on every level entry
        double _t, _lastSave;

        /// <summary>
        /// The host draws these marks whenever the level changes (the original's show(L)). Default: publish them on the
        /// current level as Engine.ApplyLevelState(Echo.StateKey, marks.ToJson()) - only for a level that has marks, or
        /// that had marks before (so they get cleared) - so the host renders them like any other level data.
        /// </summary>
        public Action<EchoMarks> Show;
        readonly HashSet<string> _published = new();

        // Live recorders, for the host's pause / quit hook (the original's addEventListener('pagehide', save)).
        static readonly List<WeakReference<EchoRecorder>> Live = new();
        /// <summary>Save every live recorder: call from the host's OnApplicationPause(true) / OnApplicationQuit.</summary>
        public static void SaveAll()
        {
            lock (Live)
            {
                Live.RemoveAll(w => !w.TryGetTarget(out _));
                foreach (var w in Live) if (w.TryGetTarget(out var r)) r.Save();
            }
        }
        /// <summary>The 'own text' chalk prompt (the original's window.prompt); null or empty cancels. Default: Ui.Open("prompt", { text, max }).</summary>
        public Func<string, int, Task<string>> AskText;
        /// <summary>Earlier runs only.</summary>
        public IReadOnlyList<EchoRun> Past => _data.Runs;
        public EchoRun Current => _run;
        public EchoMarks Shown { get; private set; }

        public EchoRecorder(StoryContext ctx, Func<string, string> get, Action<string, string> set)
        {
            _ctx = ctx; _get = get; _set = set;
            _data = Echo.ReadEcho(get?.Invoke(Echo.Key));
            AskText = async (text, max) =>
            {
                var r = await _ctx.Ui.Open("prompt", new JObject { ["text"] = text, ["max"] = max });
                return r != null && r.Type == JTokenType.String ? (string)r : null;
            };
            Show = Publish;
            lock (Live) Live.Add(new WeakReference<EchoRecorder>(this));
            ctx.Engine.Frame += OnFrame;
        }

        void Publish(EchoMarks m)
        {
            if (m == null || m.Level == null || _ctx.Engine.LevelId != m.Level) return;
            if (!m.Any && !_published.Contains(m.Level)) return;
            _published.Add(m.Level);
            _ctx.Engine.ApplyLevelState(Echo.StateKey, m.ToJson());
        }

        public void Save()
        {
            var runs = _data.Runs.Concat(_run != null && Echo.Worth(_run) ? new[] { _run } : Array.Empty<EchoRun>()).ToList();
            _set?.Invoke(Echo.Key, new EchoData { Runs = runs.Skip(Math.Max(0, runs.Count - Echo.Runs)).ToList() }.ToJson());
        }

        void ShowLevel(string levelId)
        {
            _shownLevel = levelId;
            Shown = Echo.MarksFor(_data, levelId);
            Show?.Invoke(Shown);
            if (Shown.Any && !_told) { _told = true; _ctx.Toast("이전 회차의 내가 남긴 흔적이 보인다. 그때의 나는 이 길로 걸었다.", "self", 5200); }
        }

        void OnFrame(double dt)
        {
            _t += dt;
            var E = _ctx.Engine;
            var lv = E.LevelId;
            if (lv != null && _shownLevel != lv) ShowLevel(lv);
            if (_run == null || !_ctx.Roaming || lv == null) return;
            var last = _run.Trail.Count > 0 ? _run.Trail[^1] : default;
            if (_run.Trail.Count == 0 || last.Lv != lv || Math.Sqrt((last.X - E.PlayerX) * (last.X - E.PlayerX) + (last.Z - E.PlayerZ) * (last.Z - E.PlayerZ)) > 1.5)
            {
                _run.Trail.Add((lv, Math.Floor(E.PlayerX * 10 + 0.5) / 10, Math.Floor(E.PlayerZ * 10 + 0.5) / 10));
                if (_run.Trail.Count > Echo.MaxTrail) _run.Trail.RemoveAt(0);
            }
            if (_t - _lastSave > 10) { _lastSave = _t; Save(); }
        }

        /// <summary>A new game: the run that was being recorded becomes an echo.</summary>
        public void Begin()
        {
            if (_run != null && Echo.Worth(_run)) { _data.Runs.Add(_run); if (_data.Runs.Count > Echo.Runs) _data.Runs.RemoveRange(0, _data.Runs.Count - Echo.Runs); }
            _run = new EchoRun();
            Save();
            _shownLevel = null; _told = false;
        }

        /// <summary>Continuing a save keeps recording into the same run.</summary>
        public void Resume() { _run ??= new EchoRun(); _told = false; }

        public void Death(string why)
        {
            var E = _ctx.Engine;
            if (_run == null || E.LevelId == null) return;
            _run.Deaths.Add(new EchoDeath { Lv = E.LevelId, X = E.PlayerX, Z = E.PlayerZ, Why = why ?? "", Yaw = E.PlayerYaw });
            Save();
        }

        /// <summary>B: write on the ground for the next run. Returns the note written, or null.</summary>
        public async Task<EchoNote> Chalk()
        {
            var E = _ctx.Engine;
            if (_run == null || E.LevelId == null) return null;
            var choices = Echo.Presets.Select((t, i) => new TalkChoice(i.ToString(CultureInfo.InvariantCulture), t)).ToList();
            choices.Add(new TalkChoice("own", "직접 쓰기"));
            choices.Add(new TalkChoice("no", "그만"));
            var pick = await _ctx.Say("분필", new[] { "바닥에 뭐라고 적을까? 다음 회차의 내가 보게 된다." }, choices);
            string text = Echo.PresetFor(pick);
            if (pick == "own")
            {
                text = ((await _ctx.Modal(() => AskText("바닥에 적을 말 (24자까지)", 24))) ?? "").Trim();
                if (text.Length > 24) text = text.Substring(0, 24);
            }
            if (string.IsNullOrEmpty(text) || pick == "no") return null;
            // just ahead of you
            double yaw = E.PlayerYaw;
            var n = new EchoNote { Lv = E.LevelId, X = E.PlayerX + Math.Sin(yaw) * -1.2, Z = E.PlayerZ + Math.Cos(yaw) * -1.2, Text = text, Yaw = yaw };
            _run.Notes.Add(n);
            if (_run.Notes.Count > Echo.MaxNotes) _run.Notes.RemoveAt(0);
            Save();
            if (Shown?.Level != n.Lv) ShowLevel(n.Lv); // chalk before the level's first frame: build its marks now
            Shown.Notes.Add(n);
            Show?.Invoke(Shown);
            _ctx.Toast("바닥에 분필로 적었다. 다음 회차의 내가 이걸 보게 된다.", "self");
            return n;
        }
    }
}
