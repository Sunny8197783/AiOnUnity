// Draws the marks of earlier runs (story/echo.js show(L)) from the level-state value Echo publishes under Echo.StateKey
// (EchoMarks.ToJson(): { level, crosses: [{x,z,yaw,age,label}], notes: [{lv,x,z,text,yaw}], ghost: [[x,z],...] }).
//
//   - the last run's walk: a faint dashed chalk line on the ground (older runs fainter, if a "trails" list is ever
//     published), and a pale translucent figure walking it again at 1.5 m/s and starting over, like the original ghost;
//   - chalk notes: text lying on the ground (#f4f1e8, 3 m wide), turned to the writer's yaw +/- 0.15 rad;
//   - deaths: a red chalk X (#e0302a) in a faint ring, with "{age}... {why}" (#ff8a7a, 3 m) 0.9 m to the near side.
//     The original's X was a ~0.2 m glyph on a 1.6 m text plane; here it is deliberately larger (~1.1 m arms, 1.64 m
//     ring) so it reads from a third-person camera.
//
// Marks fade in when first drawn and are fully readable only near the player. Everything lives under one root in the
// active (level) scene, so it dies with the level; a null/empty value, or a redraw, removes the previous marks.
// The last applied value is cached, and a small watcher on the persistent host redraws it when the same level is
// entered again (UnityStoryHost.Enter reloads the scene even for the same id, and Echo does not republish then).
// All positions are in the original frame (x east, z south, yaw 0 = north, + turns left) until converted with LevelLayout.
using System;
using System.Collections.Generic;
using System.Globalization;
using AiOn.Levels;
using AiOn.Player;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AiOn.Story
{
    public static class EchoMarksView
    {
        // ---------- model (pure) ----------

        public sealed class CrossMark { public double X, Z, Yaw; public int Age = 1; public string Label = ""; }
        public sealed class NoteMark { public double X, Z, Yaw; public string Text = ""; }
        public sealed class TrailMark { public int Age = 1; public List<(double X, double Z)> Points = new(); }

        public sealed class Model
        {
            public string Level;
            public List<CrossMark> Crosses = new();
            public List<NoteMark> Notes = new();
            /// <summary>Trails to draw as lines; the "ghost" path is the one with Age 1.</summary>
            public List<TrailMark> Trails = new();
            /// <summary>The path the ghost walks (the last run's), or empty.</summary>
            public List<(double X, double Z)> Ghost = new();
            public bool Empty => Crosses.Count == 0 && Notes.Count == 0 && Trails.Count == 0 && Ghost.Count == 0;
        }

        /// <summary>Whether the ghost figure is drawn (the line is drawn either way).</summary>
        public static bool DrawGhost = true;

        static bool Num(JToken t, out double v)
        {
            v = 0;
            if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) return false;
            v = (double)t;
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }

        static List<(double X, double Z)> Points(JToken t)
        {
            var list = new List<(double, double)>();
            if (t is not JArray a) return list;
            foreach (var p in a)
                if (p is JArray q && q.Count >= 2 && Num(q[0], out var x) && Num(q[1], out var z)) list.Add((x, z));
            return list;
        }

        /// <summary>
        /// The published value as a model, keeping only well-formed entries; null for null / non-object values.
        /// Besides EchoMarks.ToJson()'s ghost path, an optional "trails" list ([{age, points:[[x,z]...]}] or
        /// [[[x,z]...], ...] with ages 1, 2, ...) draws more runs' lines.
        /// </summary>
        public static Model Parse(JToken value)
        {
            if (value is not JObject o) return null;
            var m = new Model { Level = o["level"]?.Type == JTokenType.String ? (string)o["level"] : null };
            if (o["crosses"] is JArray cs)
                foreach (var c in cs)
                    if (c is JObject co && Num(co["x"], out var x) && Num(co["z"], out var z))
                        m.Crosses.Add(new CrossMark
                        {
                            X = x, Z = z, Yaw = Num(co["yaw"], out var yaw) ? yaw : 0,
                            Age = Num(co["age"], out var age) ? Math.Max(1, (int)age) : 1,
                            Label = co["label"]?.Type == JTokenType.String ? (string)co["label"] : "",
                        });
            if (o["notes"] is JArray ns)
                foreach (var n in ns)
                    if (n is JObject no && Num(no["x"], out var x) && Num(no["z"], out var z) && no["text"]?.Type == JTokenType.String
                        && ((string)no["text"]).Trim().Length > 0)
                        m.Notes.Add(new NoteMark { X = x, Z = z, Yaw = Num(no["yaw"], out var yaw) ? yaw : 0, Text = (string)no["text"] });
            m.Ghost = Points(o["ghost"]);
            if (m.Ghost.Count < 2) m.Ghost.Clear();
            if (m.Ghost.Count > 0) m.Trails.Add(new TrailMark { Age = 1, Points = m.Ghost });
            if (o["trails"] is JArray ts)
                for (int i = 0; i < ts.Count; i++)
                {
                    var t = ts[i];
                    var tm = t is JObject to
                        ? new TrailMark { Age = Num(to["age"], out var a) ? Math.Max(1, (int)a) : i + 1, Points = Points(to["points"]) }
                        : new TrailMark { Age = i + 1, Points = Points(t) };
                    if (tm.Points.Count >= 2 && !(m.Ghost.Count > 0 && tm.Age == 1 && SamePath(tm.Points, m.Ghost))) m.Trails.Add(tm);
                }
            return m;
        }

        static bool SamePath(List<(double X, double Z)> a, List<(double X, double Z)> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>
        /// A recorded trail as drawable pieces: points closer than <paramref name="minStep"/> to the last kept one are
        /// dropped, and the line breaks where two points are more than <paramref name="maxGap"/> apart (a cut or a
        /// teleport). Pieces with fewer than 2 points are left out.
        /// </summary>
        public static List<List<(double X, double Z)>> SplitTrail(IReadOnlyList<(double X, double Z)> pts, double minStep = 0.25, double maxGap = 6)
        {
            var pieces = new List<List<(double, double)>>();
            if (pts == null) return pieces;
            List<(double X, double Z)> cur = null;
            foreach (var p in pts)
            {
                if (cur != null && cur.Count > 0)
                {
                    var l = cur[^1];
                    double d = Math.Sqrt((p.X - l.X) * (p.X - l.X) + (p.Z - l.Z) * (p.Z - l.Z));
                    if (d > maxGap) { if (cur.Count >= 2) pieces.Add(cur); cur = null; }
                    else if (d < minStep) continue;
                }
                cur ??= new List<(double, double)>();
                cur.Add(p);
            }
            if (cur != null && cur.Count >= 2) pieces.Add(cur);
            return pieces;
        }

        /// <summary>Chaikin corner cutting for an open polyline (end points kept): a softer chalk line.</summary>
        public static List<(double X, double Z)> Smooth(IReadOnlyList<(double X, double Z)> pts, int iterations = 1)
        {
            var cur = new List<(double X, double Z)>(pts);
            for (int it = 0; it < iterations && cur.Count >= 3; it++)
            {
                var next = new List<(double X, double Z)>(cur.Count * 2) { cur[0] };
                for (int i = 0; i < cur.Count - 1; i++)
                {
                    var a = cur[i]; var b = cur[i + 1];
                    next.Add((0.75 * a.X + 0.25 * b.X, 0.75 * a.Z + 0.25 * b.Z));
                    next.Add((0.25 * a.X + 0.75 * b.X, 0.25 * a.Z + 0.75 * b.Z));
                }
                next.Add(cur[^1]);
                cur = next;
            }
            return cur;
        }

        /// <summary>Line opacity of a run's trail: the last run (age 1) faint, older ones fainter still.</summary>
        public static float TrailAlpha(int age) => 0.13f / (1f + 0.75f * (Math.Max(1, age) - 1)); // faint: at 0.28 and 16 cm wide it read as tape on the floor

        /// <summary>The death label's spot: 0.9 m to the near side of the X (behind the way you were going).</summary>
        public static (double X, double Z) DeathLabelPos(double x, double z, double yaw, double off = 0.9) =>
            (x + Math.Sin(yaw) * off, z + Math.Cos(yaw) * off);

        /// <summary>
        /// A note's slight turn (the original's random +/- 0.15 rad), made stable from its text and spot so a redraw
        /// does not shuffle the notes.
        /// </summary>
        public static double NoteJitter(double x, double z, string text)
        {
            uint h = 2166136261;
            void Mix(string s) { foreach (char c in s) { h ^= c; h *= 16777619; } }
            Mix(text ?? "");
            Mix(Math.Round(x, 1).ToString("F1", CultureInfo.InvariantCulture));
            Mix(Math.Round(z, 1).ToString("F1", CultureInfo.InvariantCulture));
            return (h % 10001) / 10000.0 * 0.3 - 0.15;
        }

        /// <summary>Label opacity by distance from the player: full within <paramref name="near"/>, down to <paramref name="min"/> at <paramref name="far"/>.</summary>
        public static float NearAlpha(float dist, float near = 5f, float far = 16f, float min = 0.22f)
        {
            if (dist <= near) return 1f;
            if (dist >= far) return min;
            float t = (dist - near) / (far - near);
            t = t * t * (3 - 2 * t);
            return 1f + (min - 1f) * t;
        }

        /// <summary>The ghost's base opacity: a faint trace of the last run, not a solid figure.</summary>
        public const float GhostAlpha = 0.22f;

        /// <summary>
        /// How visible the ghost is, 0..1, by its distance to the camera and (on the ground) to the player: it melts away
        /// within about 2 m of either, so a run resumed on its path never starts with a blue figure filling the view (pure; tested).
        /// </summary>
        public static float GhostVisibility(float cameraDist, float playerDist)
        {
            static float Ramp(float d, float from, float to) { float t = Mathf.Clamp01((d - from) / (to - from)); return t * t * (3 - 2 * t); }
            return Mathf.Min(Ramp(cameraDist, 2f, 5f), Ramp(playerDist, 0.8f, 2.4f));
        }

        /// <summary>
        /// 1, or less when the ghost stands between the camera and the player close to the line of sight (lateral metres
        /// from it): a figure walking through the shot in a corridor must not cover the player. In first person (the player
        /// at the camera) the line runs 4 m ahead along the view, and a figure more than about 3 m ahead keeps at least
        /// <see cref="GhostLeadFloor"/>, so in a narrow corridor it still leads the way as a faint silhouette (pure; tested).
        /// </summary>
        public static float GhostClearOfView(Vector3 cam, Vector3 camForward, Vector3 player, Vector3 ghost)
        {
            var d = player - cam; d.y = 0;
            bool first = d.sqrMagnitude < 0.25f;
            if (first) { d = camForward; d.y = 0; d = d.sqrMagnitude > 1e-6f ? d.normalized * 4f : Vector3.zero; }
            var g = ghost - cam; g.y = 0;
            float len = d.magnitude;
            if (len < 0.01f) return 1f;
            float along = Vector3.Dot(g, d / len);
            if (along <= 0f || along >= len + 0.5f) return 1f; // behind the camera, or beyond the player
            float lateral = Mathf.Sqrt(Mathf.Max(0f, g.sqrMagnitude - along * along));
            float t = Mathf.Clamp01((lateral - 0.5f) / 1.0f);
            float v = t * t * (3 - 2 * t);
            if (first)
            {
                float k = Mathf.Clamp01((along - 2.5f) / 1.0f);
                v = Mathf.Max(v, GhostLeadFloor * k * k * (3 - 2 * k));
            }
            return v;
        }

        /// <summary>The least a ghost walking ahead in first person keeps (past about 3 m) of its visibility.</summary>
        public const float GhostLeadFloor = 0.35f;

        /// <summary>
        /// The ghost waits for the player: 0 until they have walked about 1 m from where the echo was drawn, 1 from 3 m
        /// on (pure; tested). A new run's first frame used to open on a translucent 정민 walking through the middle of the
        /// office before the player had done anything.
        /// </summary>
        public static float GhostAfterMove(float moved)
        {
            float t = Mathf.Clamp01((moved - 1f) / 2f);
            return t * t * (3 - 2 * t);
        }

        /// <summary>The original ghost's step: walk toward point i at <paramref name="speed"/>, next point within 0.3 m, snap back to the start when far.</summary>
        public static (double X, double Z, int I, double? Yaw) GhostStep(double x, double z, int i, IReadOnlyList<(double X, double Z)> pts, double dt, double speed = 1.5)
        {
            if (pts == null || pts.Count == 0) return (x, z, 0, null);
            i = ((i % pts.Count) + pts.Count) % pts.Count;
            var to = pts[i];
            double d = Math.Sqrt((to.X - x) * (to.X - x) + (to.Z - z) * (to.Z - z));
            double? yaw = null;
            if (d < 0.3) i = (i + 1) % pts.Count;
            else
            {
                double k = Math.Min(1, speed * dt / d);
                x += (to.X - x) * k; z += (to.Z - z) * k;
                yaw = Math.Atan2(-(to.X - x), -(to.Z - z));
            }
            if (i == 0 && d > 6) { x = to.X; z = to.Z; }
            return (x, z, i, yaw);
        }

        /// <summary>
        /// The order in which trail heights are found: first the point nearest (<paramref name="x"/>, <paramref name="z"/>)
        /// (From = -1: use the player's height), then outward along its piece both ways, then the later pieces forward
        /// and the earlier pieces backward, each point from an already placed neighbour (From).
        /// </summary>
        public static List<(int Piece, int Index, int FromPiece, int FromIndex)> ChainOrder(IReadOnlyList<IReadOnlyList<(double X, double Z)>> pieces, double x, double z)
        {
            var order = new List<(int, int, int, int)>();
            if (pieces == null || pieces.Count == 0) return order;
            int bp = -1, bi = -1; double best = double.MaxValue;
            for (int p = 0; p < pieces.Count; p++)
                for (int i = 0; i < pieces[p].Count; i++)
                {
                    double dx = pieces[p][i].X - x, dz = pieces[p][i].Z - z, d = dx * dx + dz * dz;
                    if (d < best) { best = d; bp = p; bi = i; }
                }
            if (bp < 0) return order;
            order.Add((bp, bi, -1, -1));
            for (int i = bi + 1; i < pieces[bp].Count; i++) order.Add((bp, i, bp, i - 1));
            for (int i = bi - 1; i >= 0; i--) order.Add((bp, i, bp, i + 1));
            for (int p = bp + 1; p < pieces.Count; p++)
            {
                int fp = p - 1, fi = pieces[fp].Count - 1;
                for (int i = 0; i < pieces[p].Count; i++) { order.Add((p, i, fp, fi)); fp = p; fi = i; }
            }
            for (int p = bp - 1; p >= 0; p--)
            {
                int fp = p + 1, fi = 0;
                for (int i = pieces[p].Count - 1; i >= 0; i--) { order.Add((p, i, fp, fi)); fp = p; fi = i; }
            }
            return order;
        }

        /// <summary>Identity of a mark across redraws (so a redraw only fades in what is new).</summary>
        public static string MarkKey(string kind, double x, double z, string text) =>
            string.Format(CultureInfo.InvariantCulture, "{0}|{1:F2}|{2:F2}|{3}", kind, x, z, text ?? "");

        // ---------- drawing ----------

        static GameObject _root;
        static string _drawnLevel;
        static Model _last; // the last drawn model, redrawn after a same-level reload
        static int _rootScene = -1; // handle of the scene the root was built in
        static readonly HashSet<string> _seen = new();
        static Material _lineMat, _ghostMat;
        static Texture2D _dashTex, _softTex;

        static readonly Color Chalk = new(0.957f, 0.945f, 0.91f);
        static readonly Color CrossRed = new(0.878f, 0.188f, 0.165f);
        static readonly Color LabelRed = new(1f, 0.541f, 0.478f);
        static readonly Color GhostBlue = new(0.435f, 0.659f, 1f);
        const float FadeIn = 1.2f, Lift = 0.05f, PixelsPerMetre = 200f, PlayerHintRadius = 4f;

        /// <summary>
        /// Host hook (top of UnityStoryHost.ApplyLevelState): false unless <paramref name="key"/> is Echo.StateKey;
        /// otherwise (re)draws that level's marks in the active scene (null/empty clears them) and returns true.
        /// </summary>
        public static bool TryApply(string key, JToken value)
        {
            if (key != Echo.StateKey) return false;
            var m = Parse(value);
            var host = UnityStoryHost.Instance;
            // a value for another level (published late, after a level change) is not this level's
            if (m?.Level != null && host != null && !string.IsNullOrEmpty(host.LevelId) && host.LevelId != m.Level) return true;
            // the previous drawing was destroyed by a level unload (not by Clear): this is a fresh visit, fade in again
            if (RootUnloaded) _seen.Clear();
            EchoMarksRuntime old = _root ? _root.GetComponent<EchoMarksRuntime>() : null;
            var ghostState = old ? old.GhostState() : null;
            Clear(keepSeen: true);
            if (m == null || m.Empty) { _last = null; return true; }
            if (_drawnLevel != m.Level) _seen.Clear();
            _drawnLevel = m.Level;
            _last = m;
            EnsureWatcher(host);
            Build(m, ghostState);
            return true;
        }

        /// <summary>Removes the drawn marks (and forgets them, so a reload does not bring them back).</summary>
        public static void Clear() => Clear(false);

        static void Clear(bool keepSeen)
        {
            if (_root) Kill(_root);
            _root = null;
            _rootScene = -1;
            if (!keepSeen) { _seen.Clear(); _drawnLevel = null; _last = null; }
        }

        /// <summary>A root was built but is gone without Clear: its scene was unloaded.</summary>
        static bool RootUnloaded => (object)_root != null && !_root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _root = null; _rootScene = -1; _drawnLevel = null; _last = null;
            _seen.Clear();
            DrawGhost = true;
        }

        static void EnsureWatcher(UnityStoryHost host)
        {
            if (!Application.isPlaying) return;
            if (host != null)
            {
                if (!host.GetComponent<EchoMarksWatcher>()) host.gameObject.AddComponent<EchoMarksWatcher>();
                return;
            }
            if (Object.FindFirstObjectByType<EchoMarksWatcher>()) return;
            var go = new GameObject("EchoMarksWatcher");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<EchoMarksWatcher>();
        }

        /// <summary>
        /// Whether the cached marks should be redrawn: there are marks for <paramref name="hostLevel"/>, and the drawing is
        /// gone (its scene unloaded) or was built in another scene than the active one.
        /// </summary>
        public static bool NeedsRedraw(string lastLevel, string hostLevel, bool rootAlive, int rootScene, int activeScene) =>
            lastLevel != null && hostLevel == lastLevel && (!rootAlive || rootScene != activeScene);

        /// <summary>Redraws the cached marks after a same-level reload, once the player has been placed (Enter teleports a frame after the load).</summary>
        sealed class EchoMarksWatcher : MonoBehaviour
        {
            float _pending = -1;

            void LateUpdate()
            {
                var host = UnityStoryHost.Instance;
                string lv = host != null ? host.LevelId : _last?.Level;
                bool need = _last != null && NeedsRedraw(_last.Level, lv, _root, _rootScene,
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle);
                if (!need) { _pending = -1; return; }
                if (_pending < 0) { _pending = 0.3f; return; } // let Enter finish (destroy the scene's own player, teleport)
                _pending -= Time.unscaledDeltaTime;
                if (_pending > 0) return;
                _pending = -1;
                if (_root) Kill(_root);
                _root = null;
                _seen.Clear(); // a fresh visit: fade in again
                Build(_last, null);
            }
        }

        static void Kill(Object o) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }

        static Transform PlayerTransform()
        {
            var host = UnityStoryHost.Instance;
            if (host != null && host.player) return host.player.transform;
            var p = Object.FindFirstObjectByType<PlayerController>();
            return p ? p.transform : null;
        }

        static void Build(Model m, (List<(double X, double Z)> Pts, double X, double Z, int I)? ghostState)
        {
            var player = PlayerTransform();
            float hint = player ? player.position.y : 0f;
            _root = new GameObject("EchoMarks"); // lands in the active (level) scene
            _rootScene = _root.scene.handle;
            var rt = _root.AddComponent<EchoMarksRuntime>();
            rt.Player = player;
            // the player's spot in the original frame (where its y is a trustworthy height hint)
            double px = player ? player.position.x : 0, pz = player ? -player.position.z : 0;

            // trails (older first, so the last run's line draws on top)
            var projected = new List<Vector3>();
            m.Trails.Sort((a, b) => b.Age.CompareTo(a.Age));
            foreach (var tr in m.Trails)
            {
                bool isNew = _seen.Add(MarkKey("trail" + tr.Age, tr.Points[0].X, tr.Points[0].Z, tr.Points.Count.ToString(CultureInfo.InvariantCulture)));
                var pieces = new List<List<(double X, double Z)>>();
                foreach (var piece in SplitTrail(tr.Points)) pieces.Add(Smooth(piece, 1));
                if (pieces.Count == 0) continue;
                var world = new Vector3[pieces.Count][];
                for (int p = 0; p < pieces.Count; p++) world[p] = new Vector3[pieces[p].Count];
                // heights chained outward from the point nearest the player, so stairs and ramps are followed from a
                // height that is known to be right
                foreach (var s in ChainOrder(pieces, px, pz))
                {
                    var q = pieces[s.Piece][s.Index];
                    var u = LevelLayout.ToUnity((float)q.X, 0, (float)q.Z);
                    float fromY = s.FromPiece < 0 ? hint : world[s.FromPiece][s.FromIndex].y - Lift;
                    world[s.Piece][s.Index] = new Vector3(u.x, GroundY(u.x, u.z, fromY, player) + Lift, u.z);
                }
                foreach (var w in world)
                {
                    projected.AddRange(w);
                    var lr = Line("Trail" + tr.Age, _root.transform, w, 0.08f, true);
                    rt.Add(new[] { lr }, null, w[0], isNew, TrailAlpha(tr.Age), Chalk);
                }
            }

            float HintNear(Vector3 u)
            {
                // close to the player (a freshly chalked note, a death just now): the player's own floor
                if (player)
                {
                    float px2 = player.position.x - u.x, pz2 = player.position.z - u.z;
                    if (px2 * px2 + pz2 * pz2 < PlayerHintRadius * PlayerHintRadius) return hint;
                }
                float best = 16f, h = hint;
                foreach (var p in projected)
                {
                    float dx = p.x - u.x, dz = p.z - u.z, d = dx * dx + dz * dz;
                    if (d < best) { best = d; h = p.y - Lift; }
                }
                return h;
            }

            // deaths: a red chalk X in a faint ring, the label on the near side
            foreach (var c in m.Crosses)
            {
                bool isNew = _seen.Add(MarkKey("cross", c.X, c.Z, c.Label));
                var u = LevelLayout.ToUnity((float)c.X, 0, (float)c.Z);
                float gy = GroundY(u.x, u.z, HintNear(u), player);
                var center = new Vector3(u.x, gy + Lift + 0.01f, u.z);
                var holder = new GameObject("Death");
                holder.transform.SetParent(_root.transform, false);
                holder.transform.SetPositionAndRotation(center, Quaternion.Euler(0, LevelLayout.ToUnityYaw((float)c.Yaw), 0));
                const float arm = 0.55f;
                var a = Line("X1", holder.transform, new[] { holder.transform.TransformPoint(new Vector3(-arm, 0, -arm)), holder.transform.TransformPoint(new Vector3(arm, 0, arm)) }, 0.14f, false);
                var b = Line("X2", holder.transform, new[] { holder.transform.TransformPoint(new Vector3(-arm, 0, arm)), holder.transform.TransformPoint(new Vector3(arm, 0, -arm)) }, 0.14f, false);
                var ringPts = new Vector3[40];
                for (int i = 0; i < ringPts.Length; i++)
                {
                    float t = i / (float)ringPts.Length * Mathf.PI * 2;
                    ringPts[i] = center + new Vector3(Mathf.Cos(t) * 0.82f, -0.005f, Mathf.Sin(t) * 0.82f);
                }
                var ring = Line("Ring", holder.transform, ringPts, 0.05f, false);
                ring.loop = true;
                rt.Add(new[] { a, b }, null, center, isNew, 0.85f, CrossRed);
                rt.Add(new[] { ring }, null, center, isNew, 0.3f, CrossRed);
                if (!string.IsNullOrEmpty(c.Label))
                {
                    var lp = DeathLabelPos(c.X, c.Z, c.Yaw);
                    var lu = LevelLayout.ToUnity((float)lp.X, 0, (float)lp.Z);
                    var pos = new Vector3(lu.x, GroundY(lu.x, lu.z, gy, player) + Lift + 0.02f, lu.z);
                    var cg = GroundText("DeathLabel", _root.transform, c.Label, LabelRed, 3f, pos, LevelLayout.ToUnityYaw((float)c.Yaw));
                    rt.Add(null, cg, pos, isNew, 0.9f, LabelRed);
                }
            }

            // chalk notes
            foreach (var n in m.Notes)
            {
                bool isNew = _seen.Add(MarkKey("note", n.X, n.Z, n.Text));
                var u = LevelLayout.ToUnity((float)n.X, 0, (float)n.Z);
                var pos = new Vector3(u.x, GroundY(u.x, u.z, HintNear(u), player) + Lift + 0.02f, u.z);
                var cg = GroundText("Note", _root.transform, n.Text, Chalk, 3f, pos, LevelLayout.ToUnityYaw((float)(n.Yaw + NoteJitter(n.X, n.Z, n.Text))));
                rt.Add(null, cg, pos, isNew, 0.9f, Chalk);
            }

            // the ghost walking the last run's path
            if (DrawGhost && m.Ghost.Count > 4)
            {
                bool same = ghostState.HasValue && SamePath(ghostState.Value.Pts, m.Ghost);
                rt.StartGhost(m.Ghost, same ? ghostState.Value.X : m.Ghost[0].X, same ? ghostState.Value.Z : m.Ghost[0].Z, same ? ghostState.Value.I : 0,
                    hint, MakeGhost(_root.transform), _seen.Add(MarkKey("ghost", m.Ghost[0].X, m.Ghost[0].Z, "")));
            }
        }

        // ---------- pieces ----------

        static readonly RaycastHit[] Hits = new RaycastHit[24];

        /// <summary>The walkable surface under (ux, uz) near <paramref name="hint"/> (first surface below hint + 1.2 m, 6 m down), else hint.</summary>
        internal static float GroundY(float ux, float uz, float hint, Transform ignore, float up = 1.2f, float down = 6f, float? miss = null)
        {
            int n = Physics.RaycastNonAlloc(new Vector3(ux, hint + up, uz), Vector3.down, Hits, down, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.normal.y < 0.5f) continue;
                var ht = h.collider ? h.collider.transform : h.transform;
                if (ignore && ht.IsChildOf(ignore)) continue;
                if (_root && ht.IsChildOf(_root.transform)) continue;
                if (h.point.y > best) best = h.point.y;
            }
            return float.IsNegativeInfinity(best) ? (miss ?? hint) : best;
        }

        static Texture2D DashTex()
        {
            if (_dashTex) return _dashTex;
            const int w = 32, h = 16;
            _dashTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, name = "EchoDash" };
            var rng = new System.Random(7);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = (y + 0.5f) / h * 2 - 1, across = Mathf.Clamp01(1 - v * v);
                    float u = (x + 0.5f) / w, dash = Mathf.SmoothStep(0, 1, Mathf.Clamp01((0.62f - Mathf.Abs(u - 0.4f) * 2.2f) * 6f));
                    float grain = rng.NextDouble() < 0.12 ? 0.25f : 1f; // chalky gaps
                    _dashTex.SetPixel(x, y, new Color(1, 1, 1, across * dash * grain));
                }
            _dashTex.Apply();
            return _dashTex;
        }

        static Texture2D SoftTex()
        {
            if (_softTex) return _softTex;
            const int w = 8, h = 16;
            _softTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "EchoSoft" };
            var rng = new System.Random(3);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = (y + 0.5f) / h * 2 - 1;
                    _softTex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1.2f - v * v * 1.2f) * (rng.NextDouble() < 0.1 ? 0.4f : 1f)));
                }
            _softTex.Apply();
            return _softTex;
        }

        static Material TransparentMat(string shaderName, Texture tex)
        {
            var shader = Shader.Find(shaderName) ?? Shader.Find("Sprites/Default");
            var m = new Material(shader);
            if (tex) m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", 0);
            m.SetFloat("_Cull", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_SrcBlendAlpha", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_DstBlendAlpha", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_BLENDMODE_ALPHA");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        // Lines need vertex colours: the URP particle shader. A dashed (trail) and a solid (chalk X) material share it.
        static Material _solidMat;
        static Material LineMat(bool dashed) => dashed
            ? (_lineMat ? _lineMat : _lineMat = TransparentMat("Universal Render Pipeline/Particles/Unlit", DashTex()))
            : (_solidMat ? _solidMat : _solidMat = TransparentMat("Universal Render Pipeline/Particles/Unlit", SoftTex()));

        static Material GhostMat()
        {
            if (_ghostMat) return _ghostMat;
            var echo = Resources.Load<Shader>("Shaders/AiOnEchoGhost");
            _ghostMat = echo ? new Material(echo) { name = "EchoGhost" } : TransparentMat("Universal Render Pipeline/Unlit", null);
            _ghostMat.SetColor("_BaseColor", new Color(GhostBlue.r, GhostBlue.g, GhostBlue.b, GhostAlpha));
            return _ghostMat;
        }

        static readonly Dictionary<Texture, Material> _ghostCutouts = new();

        /// <summary>
        /// The ghost material for one slot of the body: alpha-clipped cards (hair, eyelashes: the person's material clips
        /// its base map) keep that cut-out, so they read as hair and not as solid quads around the head; anything else is
        /// the plain ghost.
        /// </summary>
        static Material GhostMatFor(Material source)
        {
            var mat = GhostMat();
            if (!source || !source.IsKeywordEnabled("_ALPHATEST_ON") || !source.HasProperty("_BaseMap") || !mat.HasProperty("_Cutoff")) return mat;
            var tex = source.GetTexture("_BaseMap");
            if (!tex) return mat;
            if (_ghostCutouts.TryGetValue(tex, out var m) && m) return m;
            m = new Material(mat) { name = "EchoGhost_" + tex.name };
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Cutoff", source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : 0.5f);
            _ghostCutouts[tex] = m;
            return m;
        }

        /// <summary>The ghost's walking pace along the remembered path (m/s; the original's).</summary>
        public const double GhostSpeed = 1.5;

        /// <summary>The person the ghost is drawn from: 정민's own body (the run it remembers is his).</summary>
        public const string GhostPreset = "jeongmin";

        static LineRenderer Line(string name, Transform parent, Vector3[] pts, float width, bool dashed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(90, 0, 0); // ribbon lies flat (faces up / down)
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.alignment = LineAlignment.TransformZ;
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
            lr.widthMultiplier = width;
            lr.numCapVertices = 3;
            lr.numCornerVertices = 2;
            lr.textureMode = dashed ? LineTextureMode.Tile : LineTextureMode.Stretch;
            if (dashed) lr.textureScale = new Vector2(2.4f, 1f); // short dashes, ~2.4 a metre (at 1.4 they were half-metre strips)
            lr.sharedMaterial = LineMat(dashed);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.allowOcclusionWhenDynamic = false;
            return lr;
        }

        /// <summary>Text lying on the ground, top edge toward Unity yaw <paramref name="yawDeg"/> (read walking that way).</summary>
        static CanvasGroup GroundText(string name, Transform parent, string text, Color color, float widthM, Vector3 pos, float yawDeg)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            float w = widthM * PixelsPerMetre, h = w / 4;
            rt.sizeDelta = new Vector2(w, h);
            rt.localScale = Vector3.one / PixelsPerMetre;
            rt.SetPositionAndRotation(pos, Quaternion.Euler(0, yawDeg, 0) * Quaternion.Euler(90, 0, 0));
            var cg = go.AddComponent<CanvasGroup>();
            cg.interactable = false; cg.blocksRaycasts = false;
            var label = UiKit.Label("Text", rt, text, Mathf.RoundToInt(h * 0.5f), color, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Fill(label.rectTransform);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.RoundToInt(h * 0.2f);
            label.resizeTextMaxSize = Mathf.RoundToInt(h * 0.5f);
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.supportRichText = false;
            return cg;
        }

        /// <summary>
        /// The ghost: 정민's own body (the people prefab, animated, its face and props left out) in the see-through echo
        /// material, casting and receiving no shadows; without the prefab, a soft figure made of primitives as before.
        /// </summary>
        static Transform MakeGhost(Transform parent)
        {
            var g = new GameObject("EchoGhost");
            g.transform.SetParent(parent, false);
            var host = UnityStoryHost.Instance;
            GameObject prefab = null;
            if (host != null && host.peoplePrefabs != null)
                foreach (var p in host.peoplePrefabs) if (p && p.name == GhostPreset) { prefab = p; break; }
            if (prefab)
            {
                // built asleep: the body's scripts never wake. It is a memory, not a person: nothing that looks for people
                // (PlayerHands' reach and thrown things, talks, crowds) may find a PersonAnimator on it.
                g.SetActive(false);
                var body = Object.Instantiate(prefab, g.transform, false);
                body.name = "Body";
                var pa = body.GetComponentInChildren<AiOn.People.PersonAnimator>(true);
                float gait = pa ? pa.gaitScale : 1f;
                foreach (var mb in body.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
                foreach (var c in body.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var r in body.GetComponentsInChildren<Renderer>(true))
                {
                    var src = r.sharedMaterials;
                    var mats = new Material[src.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = GhostMatFor(src[i]);
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                }
                g.SetActive(true);
                var an = body.GetComponentInChildren<Animator>(true);
                if (an && an.runtimeAnimatorController)
                {
                    // the walk at the ghost's pace (GhostStep's 1.5 m/s), driven directly
                    an.applyRootMotion = false;
                    an.SetFloat("Speed", (float)GhostSpeed);
                    an.SetFloat(AiOn.People.PersonClips.GaitScaleParam, gait);
                    an.Play("Locomotion", 0, 0.37f);
                }
                return g.transform;
            }
            void Part(PrimitiveType type, Vector3 pos, Vector3 scale)
            {
                var p = GameObject.CreatePrimitive(type);
                var col = p.GetComponent<Collider>();
                if (col) { col.enabled = false; Kill(col); }
                p.transform.SetParent(g.transform, false);
                p.transform.localPosition = pos;
                p.transform.localScale = scale;
                var r = p.GetComponent<MeshRenderer>();
                r.sharedMaterial = GhostMat();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            Part(PrimitiveType.Capsule, new Vector3(0, 0.74f, 0), new Vector3(0.44f, 0.6f, 0.3f)); // body
            Part(PrimitiveType.Sphere, new Vector3(0, 1.5f, 0), new Vector3(0.24f, 0.27f, 0.25f)); // head
            Part(PrimitiveType.Capsule, new Vector3(-0.11f, 0.38f, 0), new Vector3(0.15f, 0.38f, 0.15f)); // legs
            Part(PrimitiveType.Capsule, new Vector3(0.11f, 0.38f, 0), new Vector3(0.15f, 0.38f, 0.15f));
            return g.transform;
        }

        /// <summary>Fade-in, near-player readability and the ghost's walk for one drawing.</summary>
        sealed class EchoMarksRuntime : MonoBehaviour
        {
            public Transform Player;

            sealed class Item
            {
                public LineRenderer[] Lines;
                public CanvasGroup Group;
                public Vector3 Pos;
                public float Born, Alpha;
                public Color Color;
            }

            readonly List<Item> _items = new();
            float _t;
            bool _settled;

            List<(double X, double Z)> _ghostPts;
            Transform _ghost;
            Renderer[] _ghostParts;
            MaterialPropertyBlock _ghostMpb;
            float _ghostShown = -1f;
            static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
            double _gx, _gz;
            int _gi;
            float _gy, _ghostBorn;
            Vector3? _ghostFrom; // the player's position when the ghost was placed; null once they have moved away

            public void Add(LineRenderer[] lines, CanvasGroup group, Vector3 pos, bool fadeIn, float alpha, Color color)
            {
                var it = new Item { Lines = lines, Group = group, Pos = pos, Born = fadeIn ? _t : -FadeIn, Alpha = alpha, Color = color };
                _items.Add(it);
                Paint(it, fadeIn ? 0 : 1, 1);
            }

            public (List<(double X, double Z)>, double, double, int)? GhostState() =>
                _ghost && _ghostPts != null ? (_ghostPts, _gx, _gz, _gi) : null;

            public void StartGhost(List<(double X, double Z)> pts, double x, double z, int i, float hint, Transform ghost, bool fadeIn)
            {
                _ghostPts = pts; _gx = x; _gz = z; _gi = i; _ghost = ghost;
                _ghostParts = ghost.GetComponentsInChildren<Renderer>(true);
                _ghostMpb ??= new MaterialPropertyBlock();
                _ghostShown = -1f;
                var u = LevelLayout.ToUnity((float)x, 0, (float)z);
                _gy = GroundY(u.x, u.z, hint, Player);
                _ghost.position = new Vector3(u.x, _gy, u.z);
                _ghostBorn = fadeIn ? 0 : -FadeIn;
                _ghost.localScale = Vector3.one;
                _ghostFrom = Player ? Player.position : (Vector3?)null;
            }

            static void Paint(Item it, float fade, float near)
            {
                float a = it.Alpha * fade * near;
                if (it.Group) it.Group.alpha = a;
                if (it.Lines == null) return;
                foreach (var lr in it.Lines)
                {
                    if (!lr) continue;
                    var c = it.Color;
                    var g = new Gradient();
                    bool open = !lr.loop && lr.positionCount > 2;
                    g.SetKeys(new[] { new GradientColorKey(c, 0), new GradientColorKey(c, 1) },
                        open ? new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(a, 0.03f), new GradientAlphaKey(a, 0.97f), new GradientAlphaKey(0, 1) }
                             : new[] { new GradientAlphaKey(a, 0), new GradientAlphaKey(a, 1) });
                    lr.colorGradient = g;
                }
            }

            /// <summary>
            /// Melts the ghost away near the camera and the player and while it crosses the line of sight to the player
            /// (alpha through a property block, renderers off at 0); <paramref name="fadeIn"/> scales it on arrival.
            /// </summary>
            void FadeGhostNear(Vector3 player, float fadeIn)
            {
                if (_ghostParts == null) return;
                var cam = Camera.main;
                Vector3 chest = _ghost.position + Vector3.up * 1f;
                float camDist = cam ? Vector3.Distance(cam.transform.position, chest) : 99f;
                float dx = _ghost.position.x - player.x, dz = _ghost.position.z - player.z;
                float v = Player ? GhostVisibility(camDist, Mathf.Sqrt(dx * dx + dz * dz)) : GhostVisibility(camDist, 99f);
                if (Player && cam) v *= GhostClearOfView(cam.transform.position, cam.transform.forward, Player.position, _ghost.position);
                v *= fadeIn;
                v = Mathf.Round(v * 50f) / 50f; // re-tint only when it changes visibly
                if (Mathf.Approximately(v, _ghostShown)) return;
                _ghostShown = v;
                _ghostMpb.SetColor(BaseColorId, new Color(GhostBlue.r, GhostBlue.g, GhostBlue.b, GhostAlpha * v));
                foreach (var r in _ghostParts)
                {
                    if (!r) continue;
                    r.enabled = v > 0.01f;
                    r.SetPropertyBlock(_ghostMpb);
                }
            }

            bool _cineHidden;

            void Update()
            {
                // not in cutscenes: the chalk of an earlier run read as road markings across the night street shots
                bool cine = AiOn.UI.GameHud.Cinematic;
                if (cine != _cineHidden)
                {
                    _cineHidden = cine;
                    for (int c = 0; c < transform.childCount; c++) transform.GetChild(c).gameObject.SetActive(!cine);
                }
                if (cine) return;
                float dt = Time.deltaTime;
                _t += dt;
                var eye = Player ? Player.position : (Camera.main ? Camera.main.transform.position : Vector3.zero);
                bool fading = false;
                foreach (var it in _items)
                {
                    float fade = Mathf.Clamp01((_t - it.Born) / FadeIn);
                    if (fade < 1) fading = true;
                    if (it.Group)
                    {
                        float dx = it.Pos.x - eye.x, dz = it.Pos.z - eye.z;
                        it.Group.alpha = it.Alpha * fade * NearAlpha(Mathf.Sqrt(dx * dx + dz * dz));
                    }
                    else if (fade < 1 || !_settled) Paint(it, fade, 1);
                }
                _settled = !fading;

                if (_ghost && _ghostPts != null && _ghostPts.Count > 4)
                {
                    var s = GhostStep(_gx, _gz, _gi, _ghostPts, dt, GhostSpeed);
                    _gx = s.X; _gz = s.Z; _gi = s.I;
                    var u = LevelLayout.ToUnity((float)_gx, 0, (float)_gz);
                    _gy = GroundY(u.x, u.z, _gy - 0.9f, Player, 1.2f, 2.3f, miss: _gy); // ray from feet + 0.3 m, 2 m below; no ground: keep the height (it used to sink 0.9 m a frame)
                    _ghost.position = new Vector3(u.x, _gy, u.z);
                    if (s.Yaw.HasValue) _ghost.rotation = Quaternion.Euler(0, LevelLayout.ToUnityYaw((float)s.Yaw.Value), 0);
                    float f = Mathf.Clamp01((_t - _ghostBorn) / FadeIn);
                    if (_ghostFrom.HasValue && Player)
                    {
                        var m = Player.position - _ghostFrom.Value; m.y = 0;
                        float wait = GhostAfterMove(m.magnitude);
                        if (wait >= 1f) _ghostFrom = null;
                        f *= wait;
                    }
                    FadeGhostNear(eye, f);
                    // a little walking bob and leg swing, since it is not an animated person
                    float ph = _t * 1.5f * Mathf.PI * 1.25f;
                    if (_ghost.childCount >= 4)
                    {
                        _ghost.GetChild(2).localRotation = Quaternion.Euler(Mathf.Sin(ph) * 22f, 0, 0);
                        _ghost.GetChild(3).localRotation = Quaternion.Euler(-Mathf.Sin(ph) * 22f, 0, 0);
                        _ghost.GetChild(0).localPosition = new Vector3(0, 0.74f + Mathf.Abs(Mathf.Cos(ph)) * 0.03f, 0);
                    }
                }
            }
        }
    }
}
