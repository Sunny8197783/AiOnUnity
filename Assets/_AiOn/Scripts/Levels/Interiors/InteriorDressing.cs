using System;
using System.Collections.Generic;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using L = AiOn.Levels.Interiors.InteriorLogic;

namespace AiOn.Levels.Interiors
{
    /// <summary>
    /// The interior levels' live dressing that the exported models could not carry (they were instanced meshes, canvases,
    /// pooled lights and update() code in the original):
    /// apartment: the 베란다 수납장 goods drawn from the 'stock' state with the 2 L overflow row and its body
    /// (apartment-stock.js), the motion-sensor lamps with their 9 s hold and glow domes, the battery 비상조명등 of the lobby
    /// and stair landings in a blackout (apartment.js stepLights), and the lit candle's warm flickering light, flame and
    /// halo (apartment-props.js);
    /// store: 이수아 gone with the 'clerk' state, and the blackout's battery emergency units and the clerk's flashlight
    /// (store.js applyPower);
    /// office: the seated / phoning / pacing staff and the desk dashboard monitor drawn from the 'dashboard' state (office.js);
    /// shelter: the 'emergency' power supply (circuit B off, A at 55 %, the warm battery spot lamps) (shelter.js update).
    /// Made by UnityStoryHost when a level loads (it lives in the level scene and goes with it).
    /// </summary>
    public class InteriorDressing : MonoBehaviour
    {
        string _level;
        Func<Transform> _player;
        Func<Camera> _camera;
        Func<string, IStoryPerson> _npc;
        Action<string, bool?, string> _setPoint;

        public static InteriorDressing Create(string levelId, Func<Transform> player, Func<Camera> camera, Func<string, IStoryPerson> npc, Action<string, bool?, string> setPoint)
        {
            if (levelId != "apartment" && levelId != "store" && levelId != "office" && levelId != "shelter") return null;
            var go = new GameObject("InteriorDressing");
            var d = go.AddComponent<InteriorDressing>();
            d._level = levelId; d._player = player; d._camera = camera; d._npc = npc; d._setPoint = setPoint;
            switch (levelId)
            {
                case "apartment": d.BuildApartment(); break;
                case "store": d.BuildStore(); break;
                case "office": d.BuildOffice(); break;
                case "shelter": d.BuildShelter(); break;
            }
            return d;
        }

        /// <summary>Draws a level state this dressing owns; true when it drew it (the host may draw other parts too).</summary>
        public bool Apply(string key, JToken value)
        {
            string s = value != null && value.Type == JTokenType.String ? (string)value : null;
            switch (_level)
            {
                case "apartment":
                    if (key == "stock") { SetStock(value as JObject); return true; }
                    if (key == "candles") { _candleLit = s == "lit"; ShowCandle(); return true; }
                    return false;
                case "store":
                    if (key == "clerk")
                    {
                        _clerkOut = s == "out";
                        var c = _npc?.Invoke("clerk");
                        if (c != null) c.Visible = !_clerkOut;
                        _setPoint?.Invoke("storeCounter", null, L.CounterLabel(s));
                        return true;
                    }
                    if (key == "power") { SetStorePower(s != "off"); return false; } // the host also turns the lamps and glow
                    return false;
                case "office":
                    if (key == "dashboard") { DrawDashboard(value); return true; }
                    return false;
                case "shelter":
                    if (key == "power") { SetShelterPower(s != "emergency"); return false; }
                    return false;
            }
            return false;
        }

        static Vector3 U(float x, float y, float z) => LevelLayout.ToUnity(x, y, z);
        static Vector3 U(Vector3 o) => LevelLayout.ToUnity(o.x, o.y, o.z);

        static Light PointLight(Transform parent, string name, Vector3 pos, Color color, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = color; l.range = range; l.intensity = 0; l.shadows = LightShadows.None;
            l.enabled = false;
            return l;
        }

        /// <summary>A battery unit's light: a wide spot aimed down from the fixture with a soft edge (no clipped hotspot on the
        /// wall behind it, no silhouette of the player against it).</summary>
        static Light DownLight(Transform parent, string name, Vector3 pos, Color color, float range, float angle = 150f)
        {
            var l = PointLight(parent, name, pos, color, range);
            l.type = LightType.Spot;
            l.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            l.spotAngle = angle; l.innerSpotAngle = angle * 0.3f;
            return l;
        }

        static float IndoorLit => EnvironmentController.Instance ? EnvironmentController.Instance.IndoorLit : 1f;
        static bool GridPower => !EnvironmentController.Instance || EnvironmentController.Instance.power;

        void Update()
        {
            float dt = Time.deltaTime;
            switch (_level)
            {
                case "apartment": StepApartment(dt); break;
                case "office": StepOffice(dt); break;
            }
        }

        // ================================================================== apartment

        /// <summary>At: the fixture point (a sensor's sits 0.25 m under the ceiling); W: its light's fade; G: its glow dome's fade.</summary>
        sealed class Fixture { public Vector3 At; public string Zone; public Light Light; public Renderer Glow; public float Hold, W, G, D; public bool On, Best; }
        readonly List<Fixture> _sensors = new(), _backups = new(), _fixtures = new();
        AiOn.Interaction.Door _door302, _stairDoor3, _stairDoor1;
        bool _doorsFound;
        static MaterialPropertyBlock _mpb;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color DomeGlow = new Color(3.2f, 3.0f, 2.6f) * 0.55f;
        // Unity intensities for the original's fixtures (sensor 1.2, backup 1.6, x0.75 LIGHT_SCALE), matched to the grid lamps
        // The battery backups at about 40 % of the original's 1.6, as wide soft downlights: at full they were a clipped
        // hotspot behind the player in the lobby and stairwell.
        const float SensorIntensity = 2.6f, SensorRange = 6.5f, BackupIntensity = 0.75f, BackupRange = 6f;

        sealed class StockMesh { public L.StockKind Kind; public List<(Vector3, float, Vector3)> Units; public PartMesh Template; public MeshFilter Filter; public MeshRenderer Renderer; public int Shown = -1; }
        readonly List<StockMesh> _stock = new();
        BoxCollider _floorRow;

        Light _candleLight; Transform _flame, _halo; Renderer _haloR; Color _haloColor; bool _candleLit;

        void BuildApartment()
        {
            // ---- the sensor lamps replace the stairwell's and corridor's grid lamps ----
            var env = EnvironmentController.Instance;
            if (env) env.SetLampFactor(p => L.AptSensorArea(p.x, p.y, -p.z) ? 0f : 1f);

            var lights = new GameObject("AptLights").transform;
            lights.SetParent(transform, false);
            var glowMat = InteriorMeshes.Glow("SensorDomeGlow", DomeGlow, null);
            var dome = InteriorMeshes.Dome();
            for (int i = 0; i < L.AptSensors.Length; i++)
            {
                var s = L.AptSensors[i];
                var f = new Fixture { At = new Vector3(s.x, s.y - 0.25f, s.z), Zone = L.AptZone(s.x, s.y - 0.25f, s.z) };
                // the pooled light sat 0.4 m under the fixture (0.25 m under the ceiling): no hot ring on the ceiling
                f.Light = PointLight(lights, "Sensor_" + i, U(s.x, s.y - 0.65f, s.z), InteriorMeshes.Hex("#fff1dc"), SensorRange);
                var g = new GameObject("SensorGlow_" + i);
                g.transform.SetParent(lights, false);
                g.transform.position = U(s.x, s.y - 0.002f, s.z);
                g.transform.localScale = Vector3.one * 1.03f;
                g.AddComponent<MeshFilter>().sharedMesh = dome;
                var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = glowMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                r.enabled = false;
                f.Glow = r;
                _sensors.Add(f);
            }
            for (int i = 0; i < L.AptBackups.Length; i++)
            {
                var b = L.AptBackups[i];
                _backups.Add(new Fixture { At = b, Zone = L.AptZone(b.x, b.y, b.z), Light = DownLight(lights, "Backup_" + i, U(b.x, b.y - 0.1f, b.z), InteriorMeshes.Hex("#e6eeff"), BackupRange) });
            }

            _fixtures.AddRange(_sensors); _fixtures.AddRange(_backups);

            // ---- the stock shelf: one combined mesh per kind, rebuilt when its count changes ----
            var shelf = new GameObject("StockShelf").transform;
            shelf.SetParent(transform, false);
            foreach (var k in L.Kinds)
            {
                var sm = new StockMesh { Kind = k, Template = InteriorMeshes.StockUnit(k), Units = new List<(Vector3, float, Vector3)>() };
                foreach (var slot in L.Slots(k)) sm.Units.Add((U(slot.Pos), -slot.Yaw * Mathf.Rad2Deg, slot.Scale));
                var go = new GameObject("Stock_" + k.Id);
                go.transform.SetParent(shelf, false);
                sm.Filter = go.AddComponent<MeshFilter>();
                sm.Renderer = go.AddComponent<MeshRenderer>();
                var mats = new Material[sm.Template.Colors.Count];
                for (int i = 0; i < mats.Length; i++) mats[i] = InteriorMeshes.LitMat(sm.Template.Colors[i], sm.Template.Smooth[i]);
                sm.Renderer.sharedMaterials = mats;
                sm.Renderer.enabled = false;
                _stock.Add(sm);
            }
            var row = new GameObject("StockFloorRow");
            row.transform.SetParent(shelf, false);
            _floorRow = row.AddComponent<BoxCollider>();
            row.SetActive(false);

            // ---- the candle on the living-room table: light, flame, halo (the saucer and candle are the model's) ----
            var candle = new GameObject("Candle").transform;
            candle.SetParent(transform, false);
            _candleLight = PointLight(candle, "CandleLight", U(-1.62f, 6.37f, 6.12f), InteriorMeshes.Hex("#ff9a40"), 4.5f);
            _flame = GlowSprite(candle, "CandleFlame", U(-1.62f, 6.17f + 0.148f, 6.12f), new Vector3(0.026f, 0.06f, 1), new Color(2.6f, 1.9f, 1.1f), InteriorMeshes.Flame());
            _haloColor = new Color(0.55f, 0.32f, 0.12f);
            _halo = GlowSprite(candle, "CandleHalo", U(-1.62f, 6.17f + 0.14f, 6.12f), new Vector3(0.34f, 0.34f, 1), _haloColor, InteriorMeshes.Soft());
            _haloR = _halo.GetComponent<Renderer>();
            ShowCandle();
            SetStock(null);
        }

        static Transform GlowSprite(Transform parent, string name, Vector3 pos, Vector3 size, Color color, Texture2D mask)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = InteriorMeshes.Quad();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = InteriorMeshes.Glow(name, color, mask);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }

        void ShowCandle()
        {
            if (_flame) _flame.gameObject.SetActive(_candleLit);
            if (_halo) _halo.gameObject.SetActive(_candleLit);
            if (_candleLight) { _candleLight.enabled = _candleLit; if (!_candleLit) _candleLight.intensity = 0; }
        }

        /// <summary>setStock({ itemId: count }): each kind shows count x per units, capped by its slots.</summary>
        void SetStock(JObject counts)
        {
            foreach (var sm in _stock)
            {
                int n = L.Shown(counts?[sm.Kind.Id], sm.Kind.Per, sm.Units.Count);
                if (n == sm.Shown) continue;
                sm.Shown = n;
                if (n == 0) { sm.Renderer.enabled = false; continue; }
                sm.Filter.sharedMesh = sm.Template.Combine(sm.Filter.sharedMesh, sm.Units, n);
                sm.Renderer.enabled = true;
            }
            // the 2 L bottles past the 16 shelf slots stand on the floor; their row gets a body (taller than a step)
            bool row = L.FloorRow(_stock.Count > 0 ? Math.Max(0, _stock[0].Shown) : 0, out var x0, out var x1, out var z0, out var z1);
            if (_floorRow)
            {
                _floorRow.gameObject.SetActive(row);
                if (row)
                {
                    var a = U(x0, L.StockFloor, z0); var b = U(x1, L.StockFloor + 0.5f, z1);
                    _floorRow.transform.position = (a + b) / 2;
                    _floorRow.size = new Vector3(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y), Mathf.Abs(b.z - a.z));
                }
            }
        }

        static float Open(AiOn.Interaction.Door d) => d ? d.k : 0f;

        void FindDoors()
        {
            _doorsFound = true;
            foreach (var d in FindObjectsByType<AiOn.Interaction.Door>(FindObjectsSortMode.None))
            {
                if (d.id == "door302") _door302 = d;
                else if (d.id == "stairDoor3") _stairDoor3 = d;
                else if (d.id == "stairDoor1") _stairDoor1 = d;
            }
        }

        void StepApartment(float dt)
        {
            if (!_doorsFound) FindDoors();
            var pt = _player?.Invoke();
            Vector3 p = pt ? pt.position : Vector3.zero;
            float px = p.x, py = p.y, pz = -p.z, head = py + 1.6f;
            string zone = L.AptZone(px, py + 0.5f, pz);
            bool power = GridPower;
            float lit = IndoorLit, k = Mathf.Min(1, dt * 4);
            float k302 = Open(_door302), kS3 = Open(_stairDoor3), kS1 = Open(_stairDoor1);
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            foreach (var f in _sensors)
            {
                float flat = Mathf.Sqrt((px - f.At.x) * (px - f.At.x) + (pz - f.At.z) * (pz - f.At.z));
                f.On = L.SensorStep(ref f.Hold, dt, power, f.Zone == zone, flat, head - f.At.y);
                // the diffuser dome fades with the lamp (the original swapped it at once)
                float g = f.G + ((f.On ? 1 : 0) - f.G) * k;
                if (g < 0.01f && !f.On) g = 0;
                if (g != f.G)
                {
                    f.G = g;
                    bool show = g > 0;
                    if (f.Glow.enabled != show) f.Glow.enabled = show;
                    if (show) { _mpb.SetColor(BaseColorId, DomeGlow * g); f.Glow.SetPropertyBlock(_mpb); }
                }
            }
            foreach (var f in _backups) f.On = !power;
            // zones keep the light from shining through closed walls; the nearest few lit fixtures get light (stepLights)
            foreach (var f in _fixtures)
            {
                bool same = f.Zone == zone;
                f.D = L.FixtureDistance(f.On, same, !same && L.AptLinked(f.Zone, zone, k302, kS3, kS1), px - f.At.x, head - f.At.y, pz - f.At.z);
                f.Best = false;
            }
            for (int n = 0; n < L.PoolLit; n++)
            {
                Fixture best = null;
                foreach (var f in _fixtures) if (!f.Best && f.D < L.PoolReach && (best == null || f.D < best.D)) best = f;
                if (best == null) break;
                best.Best = true;
            }
            foreach (var f in _sensors) FadeLight(f, f.Best, SensorIntensity * lit, k);
            foreach (var f in _backups) FadeLight(f, f.Best, BackupIntensity, k);

            if (_candleLit && _candleLight)
            {
                float t = Time.time;
                _candleLight.intensity = 2.2f * 0.75f * L.CandleFlicker(t); // the original's 2.2 x LIGHT_SCALE
                var cam = _camera?.Invoke();
                Vector3 flameAt = U(-1.62f, 6.17f + 0.148f, 6.12f);
                if (cam)
                {
                    var toCam = cam.transform.position - flameAt; toCam.y = 0;
                    if (toCam.sqrMagnitude > 1e-6f) _flame.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
                    _halo.rotation = Quaternion.LookRotation(_halo.position - cam.transform.position, Vector3.up);
                }
                // the flame sways a little sideways (its own x, facing the camera) and stretches; the halo breathes
                _flame.position = flameAt + _flame.right * (0.0015f * Mathf.Sin(t * 5.1f));
                _flame.localScale = new Vector3(0.026f, 0.06f * (0.94f + 0.07f * Mathf.Sin(t * 11.3f)), 1);
                if (_haloR) { _mpb.SetColor(BaseColorId, _haloColor * (0.85f + 0.12f * Mathf.Sin(t * 9.7f))); _haloR.SetPropertyBlock(_mpb); }
            }
        }

        static void FadeLight(Fixture f, bool on, float full, float k)
        {
            f.W += ((on ? 1 : 0) - f.W) * k;
            if (f.W < 0.01f && !on) { f.W = 0; if (f.Light.enabled) f.Light.enabled = false; return; }
            if (!f.Light.enabled) f.Light.enabled = true;
            f.Light.intensity = full * f.W;
        }

        // ================================================================== store

        readonly List<Light> _storeEmergency = new();
        bool _clerkOut;

        void BuildStore()
        {
            // battery emergency units (store.js emerg): their heads glow by the material rules, the light pools are these
            var root = new GameObject("StoreEmergency").transform;
            root.SetParent(transform, false);
            var at = new[] { new Vector3(0.6f, 2.5f, -0.67f), new Vector3(-2f, 2.43f, -8.57f), new Vector3(4.57f, 2.43f, -3.4f) };
            // '비상등만 켜져 있다': small dim pools under each unit, the rest of the shop in the dark
            for (int i = 0; i < at.Length; i++) _storeEmergency.Add(DownLight(root, "Emergency_" + i, U(at[i]) + Vector3.down * 0.1f, InteriorMeshes.Hex("#fff2dc"), 4.5f, 130f));
        }

        /// <summary>The store's key and fill with its power cut (a share of the env's).</summary>
        public const float StoreDarkDim = 0.45f;

        void SetStorePower(bool on)
        {
            foreach (var l in _storeEmergency) { l.enabled = !on; l.intensity = on ? 0 : 1.1f; }
            // '비상등만 켜져 있다': the room's general light goes well under the battery units, so their pools show
            AiOn.Levels.EnvironmentController.Instance?.SetRoomDim(on ? 1f : StoreDarkDim);
            var c = _npc?.Invoke("clerk");
            if (c != null) c.SetProp("flashlight", !on); // the clerk keeps a torch at hand in the dark shop
        }

        // ================================================================== office

        sealed class Pose { public string Npc, Anim; }
        static readonly Pose[] OfficePoses =
        {
            new Pose { Npc = "coworker", Anim = "sitChair" }, new Pose { Npc = "staff1", Anim = "sitChair" },
            new Pose { Npc = "staff2", Anim = "phone" }, new Pose { Npc = "driver", Anim = "phone" },
        };
        bool _posed;
        Transform _worker;
        float _pace = 1;

        void BuildOffice() { }

        void StepOffice(float dt)
        {
            if (!_posed)
            {
                _posed = true;
                foreach (var p in OfficePoses) _npc?.Invoke(p.Npc)?.SetAnim(p.Anim);
                var w = _npc?.Invoke("worker");
                if (w is AiOn.People.UnityPerson up && up.Root) { _worker = up.Root.transform; w.SetAnim("walk", 1.1); }
            }
            if (_worker && _worker.gameObject.activeInHierarchy)
            {
                // paces between the pallet stacks below the window (x -8 .. 6)
                var pos = _worker.position; float x = pos.x;
                L.WorkerPace(ref x, ref _pace, dt);
                pos.x = x; _worker.position = pos;
                _worker.rotation = Quaternion.Euler(0, _pace > 0 ? 90f : -90f, 0);
            }
        }

        RectTransform _dash;
        readonly List<L.DashRow> _rows = new();

        /// <summary>The player's desk monitor (office.js dashTex): the dashboard the prologue hands over as data.</summary>
        void DrawDashboard(JToken v)
        {
            if (!L.ParseDashboard(v, out var title, out var updated, _rows)) { Debug.LogWarning("[office] dashboard must be { title, updated, rows }"); return; }
            if (_dash) { _dash.gameObject.SetActive(false); Destroy(_dash.gameObject); }
            var go = new GameObject("DeskDashboard", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3;
            _dash = (RectTransform)go.transform;
            _dash.sizeDelta = new Vector2(1024, 640);
            // ME = (-1.6, -4.6): the screen at (ME.x - 0.26, 1.04, ME.z + 0.184), 0.62 x 0.39 m, facing the chair
            _dash.position = U(-1.6f - 0.26f, 1.04f, -4.6f + 0.184f + 0.003f);
            _dash.rotation = Quaternion.identity;
            _dash.localScale = Vector3.one * (0.62f / 1024f);
            Rect(_dash, 0, 0, 1024, 640, InteriorMeshes.Hex("#0d1418"));
            Rect(_dash, 0, 0, 1024, 70, InteriorMeshes.Hex("#1b2a31"));
            Txt(_dash, title, 28, 12, 720, 30, true, InteriorMeshes.Hex("#e8eef0"));
            Txt(_dash, updated, 760, 16, 250, 22, false, InteriorMeshes.Hex("#7fd1c4"));
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i]; float y = 110 + i * 82;
                Rect(_dash, 28, y, 968, 66, InteriorMeshes.Hex(r.Hot ? "#3a1d20" : "#16232a"));
                Txt(_dash, r.Label, 52, y + 16, 360, 26, false, InteriorMeshes.Hex(r.Hot ? "#ff8a8a" : "#cfd8dc"));
                Txt(_dash, r.Value, 420, y + 15, 390, 28, true, Color.white);
                Txt(_dash, r.Delta, 820, y + 18, 170, 24, false, InteriorMeshes.Hex(r.Hot ? "#ffb4a8" : "#8fa3ab"));
            }
        }

        static void Rect(RectTransform parent, float x, float y, float w, float h, Color c)
        {
            var rt = new GameObject("Box", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
            var img = rt.gameObject.AddComponent<Image>(); img.color = c; img.raycastTarget = false;
        }

        static Font _bold;

        static void Txt(RectTransform parent, string s, float x, float y, float w, int size, bool bold, Color c)
        {
            var rt = new GameObject("Text", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, size * 1.5f);
            var t = rt.gameObject.AddComponent<Text>();
            if (bold && !_bold) _bold = Resources.Load<Font>("Fonts/Pretendard-Bold");
            t.font = bold && _bold ? _bold : AiOn.UI.UiKit.Font;
            t.text = s; t.fontSize = size; t.color = c; t.alignment = TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.supportRichText = false;
        }

        // ================================================================== shelter

        readonly List<Light> _shelterEmergency = new();

        void BuildShelter()
        {
            var root = new GameObject("ShelterEmergency").transform;
            root.SetParent(transform, false);
            // warm battery lamps over the stair A and ramp B ends: the blackout reads at a glance (shelter-services.js emLights)
            var spots = new[] { (new Vector3(-4, 2.4f, -15.6f), new Vector3(-4, 0, -6)), (new Vector3(2, 2.4f, 15.6f), new Vector3(2, 0, 6)) };
            for (int i = 0; i < spots.Length; i++)
            {
                var go = new GameObject("EmergencySpot_" + i);
                go.transform.SetParent(root, false);
                go.transform.position = U(spots[i].Item1);
                go.transform.rotation = Quaternion.LookRotation(U(spots[i].Item2) - U(spots[i].Item1));
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot; l.color = InteriorMeshes.Hex("#ffb070"); l.range = 16;
                l.spotAngle = 2 * 0.95f * Mathf.Rad2Deg; l.innerSpotAngle = l.spotAngle * 0.15f;
                l.intensity = 0; l.shadows = LightShadows.None; l.enabled = false;
                _shelterEmergency.Add(l);
            }
        }

        void SetShelterPower(bool grid)
        {
            var env = EnvironmentController.Instance;
            if (env) env.SetLampFactor(grid ? null : (Func<Vector3, float>)(p => L.ShelterLampFactor(false, L.ShelterCircuit(p.x, p.y, -p.z))));
            foreach (var l in _shelterEmergency) { l.enabled = !grid; l.intensity = grid ? 0 : 70f; } // the original's 14 (three.js candela) reads as a soft warm cast here only near 70
        }
    }
}
