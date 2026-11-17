// Port of notecheck.js createReread: the 3D pieces of 서연's "다시 읽기" — the scripted camera (eye / crane), 정민's walked
// path, pins with "what the note said" over "what is true for 서연", and ground links between two places.
// The story (SeoyeonRelay.Reread) sends the ops as data in the ORIGINAL frame (x east, y up, z south, yaw 0 = north);
// this file converts with LevelLayout.ToUnity and only draws. Everything lives under one GameObject in the active (level)
// scene, so a level unload also removes it; "dispose" (or that unload) releases the camera override.
using System;
using System.Collections.Generic;
using System.Globalization;
using AiOn.Levels;
using AiOn.Player;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AiOn.Story
{
    [DefaultExecutionOrder(1000)] // LateUpdate (label billboards) after PlayerCameraRig placed the camera
    public class RereadFx : MonoBehaviour
    {
        public const float GroundMaxY = 0.6f;
        public const float EyeHeight = 1.5f, StartPitch = -0.35f, EyeRate = 2.5f;
        public const float CraneSeconds = 1.6f, PathSeconds = 2.5f, DefaultPinScale = 2.6f;
        public const int MaxPathPoints = 120;
        /// <summary>The crane's end pose (original frame): nearly top-down over both starts and every entrance.</summary>
        public static readonly Vector3 CraneTo = new(-25, 190, 95), CraneTarget = new(-25, 0, 12);

        static readonly Color Amber = new(0xf6 / 255f, 0xbb / 255f, 0x6e / 255f);
        static readonly Color Teal = new(0x85 / 255f, 0xc7 / 255f, 0xbb / 255f);
        static readonly Color LabelBg = new(16 / 255f, 28 / 255f, 30 / 255f, 0.9f);
        static readonly Color NumberInk = new(0x16 / 255f, 0x28 / 255f, 0x2a / 255f);

        public static RereadFx Current { get; private set; }

        UnityStoryHost _host;
        PlayerCameraRig _rig;
        double _sx, _sz, _syaw;
        bool _reduce, _ownsCamera, _disposed;
        float _fov = 70f;
        Vector3 _camPos, _camTarget;
        Action<float> _cameraFn;
        readonly List<Func<float, bool>> _tickers = new(); // false = finished
        readonly List<Transform> _labels = new();
        readonly List<Object> _owned = new();
        Material _lineMat, _uiMat;
        Sprite _circle;

        // ---------- data entry ----------

        /// <summary>Applies the ops in order (start, eye, crane, path, pin, link, dispose). Ops before a start create a
        /// controller at the player's position.</summary>
        public static void Apply(UnityStoryHost host, JArray fx)
        {
            if (fx == null) return;
            foreach (var t in fx)
            {
                if (t is not JObject op) continue;
                string name = op["op"]?.ToString();
                try
                {
                    if (name == "start")
                    {
                        Current?.Dispose();
                        var s = op["start"] as JObject;
                        Create(host, Num(s?["x"], host ? host.PlayerX : 0), Num(s?["z"], host ? host.PlayerZ : 0), Num(s?["yaw"], host ? host.PlayerYaw : 0),
                            op["reduceMotion"]?.Type == JTokenType.Boolean ? (bool)op["reduceMotion"] : host && host.ReduceMotion);
                    }
                    else if (name == "dispose") Current?.Dispose();
                    else
                    {
                        if (!Current) Create(host, host ? host.PlayerX : 0, host ? host.PlayerZ : 0, host ? host.PlayerYaw : 0, host && host.ReduceMotion);
                        Current.Run(name, op);
                    }
                }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        static RereadFx Create(UnityStoryHost host, double x, double z, double yaw, bool reduceMotion)
        {
            var go = new GameObject("RereadFx"); // active scene: dies with the level
            var fx = go.AddComponent<RereadFx>();
            fx._host = host;
            fx._rig = host ? host.cameraRig : null;
            fx._sx = x; fx._sz = z; fx._syaw = yaw;
            fx._reduce = reduceMotion;
            if (fx._rig) fx._fov = fx._rig.fov;
            Current = fx;
            return fx;
        }

        void Run(string name, JObject op)
        {
            switch (name)
            {
                case "eye": Eye((float)Num(op["pitch"], StartPitch)); break;
                case "crane": Crane(); break;
                case "path": ShowPath(op["trail"] as JArray); break;
                case "pin":
                {
                    var at = op["at"] as JObject;
                    if (at == null) return;
                    Pin(op["label"]?.ToString() ?? "", Num(at["x"], 0), Num(at["z"], 0), op["was"]?.ToString() ?? "", op["now"]?.ToString() ?? "",
                        (float)Num(op["scale"], DefaultPinScale));
                    break;
                }
                case "link":
                {
                    if (op["from"] is JObject a && op["to"] is JObject b) Link(Num(a["x"], 0), Num(a["z"], 0), Num(b["x"], 0), Num(b["z"], 0));
                    break;
                }
                default: Debug.LogWarning($"[RereadFx] unknown op '{name}'"); break;
            }
        }

        // ---------- camera ----------

        /// <summary>First person at 서연's start, easing the gaze down so the note fills the lower frame.</summary>
        void Eye(float pitch)
        {
            float current = StartPitch;
            // The story has just put 서연 at her start: her feet are the ground (a raycast could land on a nearby prop).
            var player = _host && _host.player ? _host.player.transform : null;
            var start = LevelLayout.ToUnity((float)_sx, 0f, (float)_sz);
            float ground = player && Mathf.Abs(player.position.x - start.x) < 2f && Mathf.Abs(player.position.z - start.z) < 2f
                ? player.position.y : Ground(_sx, _sz);
            _cameraFn = dt =>
            {
                current = EasePitch(current, pitch, dt, _reduce);
                var look = EyeLook(_sx, _sz, _syaw, current, ground + EyeHeight);
                _camPos = LevelLayout.ToUnity((float)_sx, ground + EyeHeight, (float)_sz);
                _camTarget = LevelLayout.ToUnity((float)look.x, (float)look.y, (float)look.z);
            };
            _cameraFn(0);
            PushCamera();
        }

        /// <summary>Rises from wherever the camera is to the overview, easing position and gaze over 1.6 s.</summary>
        void Crane()
        {
            var to = LevelLayout.ToUnity(CraneTo.x, CraneTo.y, CraneTo.z);
            var target = LevelLayout.ToUnity(CraneTarget.x, CraneTarget.y, CraneTarget.z);
            Vector3 from = default, lookFrom = default;
            float t = -1;
            _cameraFn = dt =>
            {
                if (t < 0)
                {
                    var cam = _rig && _rig.cam ? _rig.cam.transform : null;
                    from = cam ? cam.position : to;
                    lookFrom = cam ? cam.position + cam.forward * 8f : target;
                    t = 0;
                }
                t = CraneStep(t, dt, _reduce);
                float e = Ease(t);
                _camPos = Vector3.Lerp(from, to, e);
                _camTarget = Vector3.Lerp(lookFrom, target, e);
            };
            _cameraFn(0);
            PushCamera();
        }

        void PushCamera()
        {
            if (!_rig || _cameraFn == null) return;
            // LookRotation needs a non-zero direction.
            if ((_camTarget - _camPos).sqrMagnitude < 1e-6f) _camTarget = _camPos + Vector3.forward;
            _rig.overrideActive = true;
            _rig.overridePosition = _camPos;
            _rig.overrideTarget = _camTarget;
            _rig.overrideFov = _fov;
            _ownsCamera = true;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_cameraFn != null) { _cameraFn(dt); PushCamera(); }
            for (int i = _tickers.Count - 1; i >= 0; i--)
                if (!_tickers[i](dt)) _tickers.RemoveAt(i);
        }

        void LateUpdate()
        {
            // Labels face the screen like the original's sprites.
            var cam = _rig && _rig.cam ? _rig.cam : Camera.main;
            if (!cam) return;
            var rot = cam.transform.rotation;
            float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect;
            foreach (var l in _labels)
            {
                if (!l) continue;
                l.rotation = rot;
                // Readable from the crane: at least MinScreenShare of the screen width wide (the original's fixed 13 m
                // labels were a few pixels of text from 190 m up).
                if (!_labelBase.TryGetValue(l, out var baseScale)) _labelBase[l] = baseScale = l.localScale.x;
                float d = Vector3.Dot(l.position - cam.transform.position, cam.transform.forward);
                float want = LabelScale(baseScale, LabelPixelWidth, d, tanHalf);
                l.localScale = Vector3.one * want;
            }
        }

        const float LabelPixelWidth = 1024f, MinScreenShare = 0.24f;
        readonly Dictionary<Transform, float> _labelBase = new();

        /// <summary>The label's scale: its own, or larger so it spans MinScreenShare of the view at distance d.</summary>
        public static float LabelScale(float baseScale, float canvasWidth, float d, float tanHalfHorizontal)
        {
            if (d <= 0.01f || tanHalfHorizontal <= 0) return baseScale;
            float viewWidth = 2f * d * tanHalfHorizontal;
            return Mathf.Max(baseScale, MinScreenShare * viewWidth / canvasWidth);
        }

        // ---------- path ----------

        /// <summary>정민's recorded steps as an amber line along the ground, drawn out by a translucent walker.</summary>
        void ShowPath(JArray trail)
        {
            if (trail == null) return;
            var raw = new List<(double x, double z)>();
            foreach (var p in trail)
                if (p is JObject o && o["x"] != null && o["z"] != null) raw.Add((Num(o["x"], 0), Num(o["z"], 0)));
            var pts = Downsample(raw, MaxPathPoints);
            if (pts.Count == 0) return;
            var world = new Vector3[pts.Count];
            for (int i = 0; i < pts.Count; i++) world[i] = LevelLayout.ToUnity((float)pts[i].x, Ground(pts[i].x, pts[i].z) + 0.15f, (float)pts[i].z);

            // A gap in the recording (e.g. 정민 went back indoors) breaks the line instead of cutting across buildings.
            int every = Math.Max(1, (int)Math.Ceiling(raw.Count / (double)MaxPathPoints));
            var runs = SplitRuns(pts, Math.Max(4.5, 3 * every * 1.5));
            var lines = new List<LineRenderer>(runs.Count);
            foreach (var _ in runs)
            {
                var l = Line("Path", new Color(Amber.r, Amber.g, Amber.b, 0.85f), 1.6f);
                l.positionCount = 0;
                lines.Add(l);
            }

            var ghost = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ghost.name = "Walker";
            Destroy(ghost.GetComponent<Collider>());
            ghost.transform.SetParent(transform, false);
            ghost.transform.localScale = new Vector3(1.2f, 1f, 1.2f); // radius 0.6, height 2
            var gr = ghost.GetComponent<MeshRenderer>();
            gr.sharedMaterial = Own(FlatMat(new Color(Amber.r, Amber.g, Amber.b, 0.45f)));
            gr.shadowCastingMode = ShadowCastingMode.Off; gr.receiveShadows = false;

            float t = _reduce ? 1 : 0;
            bool Tick(float dt)
            {
                if (!ghost) return false;
                t = Mathf.Min(1, t + dt / PathSeconds);
                int count = PathCount(t, world.Length);
                for (int r = 0; r < runs.Count; r++)
                {
                    var line = lines[r];
                    if (!line) return false;
                    int shown = Mathf.Clamp(count - runs[r].start, 0, runs[r].count);
                    if (line.positionCount == shown) continue;
                    line.positionCount = shown;
                    for (int i = 0; i < shown; i++) line.SetPosition(i, world[runs[r].start + i]);
                }
                ghost.transform.position = world[count - 1] + Vector3.up * 0.85f;
                return t < 1;
            }
            if (Tick(0)) _tickers.Add(Tick);
        }

        // ---------- pins ----------

        /// <summary>A pole at the place the reason belongs, topped by "what the note said" over "what is true for 서연".</summary>
        void Pin(string n, double x, double z, string was, string now, float scale)
        {
            float g = Ground(x, z);
            var basePos = LevelLayout.ToUnity((float)x, g, (float)z);

            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole_" + n;
            Destroy(pole.GetComponent<Collider>());
            pole.transform.SetParent(transform, false);
            float poleH = 6f * scale;
            pole.transform.localScale = new Vector3(0.16f, poleH / 2f, 0.16f);
            pole.transform.position = basePos + Vector3.up * (poleH / 2f);
            var pr = pole.GetComponent<MeshRenderer>();
            pr.sharedMaterial = Own(FlatMat(Amber));
            pr.shadowCastingMode = ShadowCastingMode.Off; pr.receiveShadows = false;

            _labels.Add(Label(n, was, now, basePos + Vector3.up * (6.5f * scale), 13f * scale, PivotX(x)));
        }

        Transform Label(string n, string was, string now, Vector3 at, float worldWidth, float pivotX)
        {
            const float W = 1024, H = 300;
            var go = new GameObject("PinLabel_" + n, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 2; // crisper glyphs at overview distance
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(W, H);
            rt.pivot = new Vector2(pivotX, 0.5f);
            rt.position = at;
            rt.localScale = Vector3.one * (worldWidth / W);

            var bg = UiKit.Box("Bg", rt, LabelBg);
            UiKit.Fill(bg.rectTransform);
            bg.material = UiMat();

            var dot = UiKit.Box("Dot", rt, Amber);
            dot.sprite = Circle();
            dot.material = UiMat();
            UiKit.At(dot.rectTransform, 70 - 44, 150 - 44, 88, 88);
            var num = UiKit.Label("Number", dot.transform, n, 56, NumberInk, TextAnchor.MiddleCenter, FontStyle.Bold);
            num.supportRichText = false;
            num.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Fill(num.rectTransform);
            num.material = UiMat();

            TextLine(rt, "Was", Clip(was, 26), Amber, 100);
            TextLine(rt, "Now", Clip(now, 26), Teal, 200);
            return rt;

            void TextLine(RectTransform parent, string name, string text, Color color, float centerY)
            {
                var l = UiKit.Label(name, parent, text, 46, color, TextAnchor.MiddleLeft, FontStyle.Bold);
                l.supportRichText = false;
                l.horizontalOverflow = HorizontalWrapMode.Overflow;
                l.material = UiMat();
                UiKit.At(l.rectTransform, 140, centerY - 36, 860, 72);
                // fillText(text, x, y, maxWidth): squeeze to the width instead of overflowing.
                l.fontSize = FitFontSize(46, l.preferredWidth, 860, 20);
            }
        }

        /// <summary>UI material drawn over the world (the original's depthTest: false sprites).</summary>
        Material UiMat()
        {
            if (_uiMat) return _uiMat;
            _uiMat = Own(new Material(Canvas.GetDefaultCanvasMaterial()) { name = "RereadLabelUI" });
            _uiMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            return _uiMat;
        }

        Sprite Circle()
        {
            if (_circle) return _circle;
            const int S = 128;
            var tex = Own(new Texture2D(S, S, TextureFormat.RGBA32, false) { name = "RereadCircle", wrapMode = TextureWrapMode.Clamp });
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(S / 2f, S / 2f));
                    byte a = (byte)(Mathf.Clamp01(S / 2f - d) * 255);
                    px[y * S + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            _circle = Own(Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100));
            return _circle;
        }

        // ---------- links ----------

        /// <summary>A flat amber strip on the ground between two places (e.g. the two starting points).</summary>
        void Link(double ax, double az, double bx, double bz)
        {
            var line = Line("Link", new Color(Amber.r, Amber.g, Amber.b, 0.7f), 0.35f);
            int n = LinkSamples(ax, az, bx, bz);
            line.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                double f = n == 1 ? 0 : (double)i / (n - 1);
                double x = ax + (bx - ax) * f, z = az + (bz - az) * f;
                line.SetPosition(i, LevelLayout.ToUnity((float)x, Ground(x, z) + 0.17f, (float)z));
            }
        }

        // ---------- helpers ----------

        LineRenderer Line(string name, Color color, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(90, 0, 0); // TransformZ alignment: the ribbon lies flat, facing up
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.alignment = LineAlignment.TransformZ;
            lr.widthMultiplier = width;
            lr.numCornerVertices = 3;
            lr.numCapVertices = 3;
            lr.startColor = lr.endColor = color;
            lr.sharedMaterial = LineMat();
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        Material LineMat() => _lineMat ? _lineMat : _lineMat = Own(FlatMat(Color.white));

        /// <summary>Unlit, transparent, vertex-coloured (MeshBasicMaterial with transparent: true, depthWrite: false).</summary>
        static Material FlatMat(Color color)
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(shader) { name = "RereadFlat", color = color };
            m.renderQueue = (int)RenderQueue.Transparent + 10;
            return m;
        }

        T Own<T>(T o) where T : Object { _owned.Add(o); return o; }

        /// <summary>The walkable height under (x, z) in the original frame: the hit closest to street level (the highest
        /// one no more than 0.6 m up, so signboards, kiosks, awnings and roofs are skipped), ignoring the player,
        /// triggers and the Ignore Raycast layer.</summary>
        float Ground(double x, double z)
        {
            var origin = LevelLayout.ToUnity((float)x, 80f, (float)z);
            var hits = Physics.RaycastAll(origin, Vector3.down, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var heights = new List<float>(hits.Length);
            var player = _host && _host.player ? _host.player.transform : null;
            foreach (var h in hits)
            {
                if (player && h.collider.transform.IsChildOf(player)) continue;
                if (h.collider.transform.IsChildOf(transform)) continue;
                heights.Add(h.point.y);
            }
            return PickGround(heights, GroundMaxY, 0f);
        }

        /// <summary>Removes every piece and hands the camera back.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            Cleanup();
            Destroy(gameObject);
        }

        void OnDestroy() => Cleanup();

        void Cleanup()
        {
            if (_disposed) return;
            _disposed = true;
            _cameraFn = null;
            _tickers.Clear();
            if (_ownsCamera && _rig) _rig.overrideActive = false;
            foreach (var o in _owned) if (o) Destroy(o);
            _owned.Clear();
            if (Current == this) Current = null;
        }

        // ---------- pure logic (EditMode tests) ----------

        public static double Num(JToken t, double fallback) =>
            t != null && (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) ? (double)t
            : t != null && t.Type == JTokenType.String && double.TryParse((string)t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v
            : fallback;

        /// <summary>smoothstep (the original ease).</summary>
        public static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }

        /// <summary>One frame of the eye's pitch easing (rate 2.5/s); reduce motion lands at once.</summary>
        public static float EasePitch(float current, float target, float dt, bool reduceMotion) =>
            current + (target - current) * (reduceMotion ? 1f : 1f - Mathf.Exp(-dt * EyeRate));

        /// <summary>One frame of the crane's progress (0..1 over 1.6 s); reduce motion lands at once.</summary>
        public static float CraneStep(float t, float dt, bool reduceMotion) => reduceMotion ? 1f : Mathf.Min(1f, t + dt / CraneSeconds);

        /// <summary>Where the eye looks, original frame: yaw 0 faces north (-z), positive turns left; pitch up is positive.</summary>
        public static (double x, double y, double z) EyeLook(double x, double z, double yaw, double pitch, double eyeY) =>
            (x - Math.Sin(yaw) * Math.Cos(pitch), eyeY + Math.Sin(pitch), z - Math.Cos(yaw) * Math.Cos(pitch));

        /// <summary>Keeps every k-th point (k = ceil(n / max)) so at most about max remain, first point always kept.</summary>
        public static List<T> Downsample<T>(IReadOnlyList<T> points, int max)
        {
            var r = new List<T>();
            if (points == null || points.Count == 0) return r;
            int every = Math.Max(1, (int)Math.Ceiling(points.Count / (double)Math.Max(1, max)));
            for (int i = 0; i < points.Count; i += every) r.Add(points[i]);
            return r;
        }

        /// <summary>How many path points are shown at progress t (at least one, all at t = 1).</summary>
        public static int PathCount(float t, int n)
        {
            if (n <= 0) return 0;
            return Math.Min(n - 1, (int)Math.Floor(Mathf.Clamp01(t) * (n - 1))) + 1;
        }

        /// <summary>The highest height at or below maxY; else the lowest hit; else the fallback.</summary>
        public static float PickGround(IEnumerable<float> heights, float maxY, float fallback)
        {
            float best = float.NegativeInfinity, lowest = float.PositiveInfinity;
            bool any = false;
            foreach (var h in heights)
            {
                any = true;
                lowest = Math.Min(lowest, h);
                if (h <= maxY && h > best) best = h;
            }
            if (!any) return fallback;
            return float.IsNegativeInfinity(best) ? lowest : best;
        }

        /// <summary>Splits a polyline into runs (start index, count) wherever two neighbours are more than maxGap apart.</summary>
        public static List<(int start, int count)> SplitRuns(IReadOnlyList<(double x, double z)> pts, double maxGap)
        {
            var runs = new List<(int start, int count)>();
            if (pts == null || pts.Count == 0) return runs;
            int start = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                double dx = pts[i].x - pts[i - 1].x, dz = pts[i].z - pts[i - 1].z;
                if (dx * dx + dz * dz > maxGap * maxGap) { runs.Add((start, i - start)); start = i; }
            }
            runs.Add((start, pts.Count - start));
            return runs;
        }

        /// <summary>Entrance C's label grows west so it clears A's (original sprite.center.x = 0.85 when x &lt; -95).</summary>
        public static float PivotX(double x) => x < -95 ? 0.85f : 0.5f;

        /// <summary>The original clip: longer than max becomes max-1 characters and an ellipsis.</summary>
        public static string Clip(string text, int max)
        {
            text ??= "";
            return text.Length > max ? text.Substring(0, max - 1) + "…" : text;
        }

        /// <summary>The font size that squeezes a line of preferredWidth (at size) into maxWidth, not below min.</summary>
        public static int FitFontSize(int size, float preferredWidth, float maxWidth, int min)
        {
            if (preferredWidth <= maxWidth || preferredWidth <= 0) return size;
            return Math.Max(min, (int)Math.Floor(size * maxWidth / preferredWidth));
        }

        /// <summary>Ground samples along a link: one every 2 m, at least the two ends.</summary>
        public static int LinkSamples(double ax, double az, double bx, double bz)
        {
            double len = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            return Math.Max(2, (int)Math.Ceiling(len / 2.0) + 1);
        }
    }
}
