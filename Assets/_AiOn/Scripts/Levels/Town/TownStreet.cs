using System.Collections.Generic;
using AiOn.Levels.Crowd;
using AiOn.Story;
using UnityEngine;

namespace AiOn.Levels.Town
{
    /// <summary>
    /// The town's live street (levels/town-traffic.js, town-events.js, town-props.js lights) on top of the exported model:
    /// <list type="bullet">
    /// <item>moving traffic: the export baked the moving pool where it stood at t 0, so each baked car mesh is split into
    /// its cars, one car per type becomes the template of that type, and the pool drives again (<see cref="TrafficSim"/>)
    /// with live colliders, its paint, night lamps and headlight glow on the road; the modes normal / light / none follow
    /// the 'traffic' state, and no grid power stops the traffic for good (town.js update);</item>
    /// <item>signal heads and pedestrian signals on the crowd's clock, dark without power or in the blackout;</item>
    /// <item>closures: a guide with a light baton at each active closure's spot (more guides as closures grow), the
    /// amber lamps blinking at 1.6 Hz (steady with reduced motion), and the light towers' spot lights;</item>
    /// <item>colliders for the state solids that are off by default (closures and towers, the water point, buses and the
    /// assembly reception, the east gate and the ramp boom), switched with their states;</item>
    /// <item>the street's real lights at night: the 103동 canopy downlight and the lamp heads nearest the story's stops.</item>
    /// </list>
    /// Made by <see cref="LevelCrowd"/> for the town and driven with the crowd's frame time.
    /// </summary>
    public sealed class TownStreet : MonoBehaviour
    {
        const float LightScale = 1f / Mathf.PI; // three.js candela over a 1/π Lambert -> Unity's light intensity
        static readonly string[] PartNames = { "body", "glass", "dark", "lamp" };
        static readonly string[] StateIds = { "waterTank", "waterTable", "queueBelt", "bus", "assemblySign", "assemblyTable", "gateEast", "boomB" };
        static readonly (double x, double z)[] KeyStops = { (-95, 10.5), (77, 15), (36, 8.4), (-80, -15), (-63, 31), (-61, -34) };
        const float Curb = 0.12f;

        UnityStoryHost _host;
        TownCrowd _town;
        WalkHeight _height;
        TrafficSim _sim;
        bool _gridLost;
        string _env; // the env name of the last Tick (the crowd frame's)

        sealed class CarView { public TrafficCar Car; public GameObject Go; public Collider Col; public GameObject Beam; public bool On = true, Beamed; }
        readonly List<CarView> _cars = new();
        readonly List<Object> _made = new();
        Material _lampMoving;
        readonly List<Material> _lampParked = new();
        string _lampEnv;

        struct Sig { public Material M; public string Group; public int Kind; public Color Hue; public float Level; }
        readonly List<Sig> _signals = new();

        readonly List<Material> _flash = new();
        float _flashLevel = -1;
        readonly Dictionary<string, Light> _towers = new();
        readonly List<IStoryPerson> _guides = new();
        readonly List<string> _guideAnim = new();
        readonly List<string> _closures = new();
        double _gt;

        readonly List<(string id, GameObject go)> _stateSolids = new();
        readonly List<(Light light, float baseIntensity)> _street = new();
        float _streetLevel = -1;

        Pt[] _peds = new Pt[64];

        public TrafficSim Traffic => _sim;
        public IReadOnlyList<IStoryPerson> ClosureGuides => _guides;
        public int StateSolidCount => _stateSolids.Count;

        public static TownStreet Create(GameObject parent, UnityStoryHost host, LevelLayout layout, TownCrowd town)
        {
            var s = parent.AddComponent<TownStreet>();
            s._host = host; s._town = town;
            s._height = WalkHeight.FromLayout(layout, WalkHeight.Pick.Highest);
            Transform root = null;
            foreach (var go in parent.scene.GetRootGameObjects()) if (go.name == "Level_town") { root = go.transform; break; }
            if (!root) Debug.LogWarning("[town] no Level_town in the scene: the street stays as exported");
            var all = root ? root.GetComponentsInChildren<Transform>(true) : new Transform[0];
            // Each part on its own: a cosmetic failure must not abort the crowd or the level load.
            Safe(() => s.BuildTraffic(all));
            Safe(() => s.BuildSignals(all));
            Safe(() => s.BuildClosures(all));
            Safe(() => s.BuildStateSolids(root, layout));
            Safe(() => s.BuildStreetLights(all));
            Safe(s.OnState);
            return s;
        }

        static void Safe(System.Action build)
        {
            try { build(); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        double Hy(double x, double z) => _height != null ? _height.At(x, z) : 0;

        // ---------------------------------------------------------------- materials

        /// <summary>The imported material behind a renderer's material (LevelMaterials swaps in runtime copies).</summary>
        static Material SourceOf(Material m)
        {
            var lm = LevelMaterials.Instance;
            if (lm != null && m) foreach (var e in lm.entries) if (e.instance == m && e.source) return e.source;
            return m;
        }

        static readonly int EmissiveFactor = Shader.PropertyToID("emissiveFactor"), EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor"), BaseColor = Shader.PropertyToID("_BaseColor");

        /// <summary>The raw glTF emissive factor of a material (its hue tells the lamps apart).</summary>
        static Color EmissiveOf(Material m)
        {
            m = SourceOf(m);
            if (!m) return Color.black;
            if (m.HasProperty(EmissiveFactor)) return m.GetColor(EmissiveFactor);
            if (m.HasProperty(EmissionColor)) return m.GetColor(EmissionColor);
            return Color.black;
        }

        /// <summary>Writes a linear (three.js) emission as LevelMaterials does: linear.gamma, Unity linearises on upload.</summary>
        static void SetEmission(Material m, Color linear)
        {
            linear.a = 1;
            if (m.HasProperty(EmissiveFactor)) m.SetColor(EmissiveFactor, linear.gamma);
            else if (m.HasProperty(EmissionColor)) m.SetColor(EmissionColor, linear.gamma);
            m.EnableKeyword("_EMISSIVE"); m.EnableKeyword("_EMISSION");
        }

        static void SetBaseColor(Material m, Color srgb)
        {
            if (m.HasProperty(BaseColorFactor)) m.SetVector(BaseColorFactor, new Vector4(srgb.r, srgb.g, srgb.b, 1)); // linear.gamma == the hex
            else if (m.HasProperty(BaseColor)) m.SetColor(BaseColor, srgb);
        }

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
        static Color Linear(string hex) => Hex(hex).linear;

        Material Copy(Material m)
        {
            var c = new Material(m) { name = m.name + " (street)" };
            _made.Add(c);
            return c;
        }

        // ---------------------------------------------------------------- traffic

        /// <summary>
        /// How many equal vertex blocks a baked instanced mesh holds: the fewest blocks that are each no wider than
        /// maxExtent (m) across x and z (an instance's vertices are consecutive in the merged mesh). 0 when none fits.
        /// </summary>
        public static int InstanceCount(IReadOnlyList<Vector3> v, float maxExtent = 6f, int maxK = 64)
        {
            int n = v?.Count ?? 0;
            if (n == 0) return 0;
            for (int k = 1; k <= maxK; k++)
            {
                if (n % k != 0) continue;
                int per = n / k; bool ok = true;
                for (int b = 0; b < k && ok; b++)
                {
                    float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                    for (int i = b * per; i < (b + 1) * per; i++) { var p = v[i]; if (p.x < x0) x0 = p.x; if (p.x > x1) x1 = p.x; if (p.z < z0) z0 = p.z; if (p.z > z1) z1 = p.z; }
                    ok = x1 - x0 <= maxExtent && z1 - z0 <= maxExtent;
                }
                if (ok) return k;
            }
            return 0;
        }

        /// <summary>Centre and extent (x, z) of a vertex block.</summary>
        static (Vector3 c, Vector2 ext) BlockBox(Vector3[] w, int start, int count)
        {
            Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
            for (int i = start; i < start + count; i++) { lo = Vector3.Min(lo, w[i]); hi = Vector3.Max(hi, w[i]); }
            return ((lo + hi) * 0.5f, new Vector2(hi.x - lo.x, hi.z - lo.z));
        }

        sealed class Baked { public string Type, Part; public MeshFilter Mf; public MeshRenderer Mr; public Vector3[] World; public int K; }

        void BuildTraffic(Transform[] all)
        {
            var baked = new List<Baked>();
            foreach (var t in all)
            {
                var name = t.name;
                if (!name.StartsWith("car:") || name.Contains(":parked")) continue;
                int tilde = name.IndexOf('~');
                var parts = (tilde > 0 ? name.Substring(0, tilde) : name).Split(':');
                if (parts.Length != 3) continue;
                var mf = t.GetComponent<MeshFilter>(); var mr = t.GetComponent<MeshRenderer>();
                if (!mf || !mr || !mf.sharedMesh) continue;
                if (!mf.sharedMesh.isReadable)
                {   // cannot be split: hide the frozen cars rather than leave them standing without colliders
                    Debug.LogWarning($"[town] {name}: mesh not readable; its baked cars are hidden");
                    mr.enabled = false; continue;
                }
                var local = mf.sharedMesh.vertices; var world = new Vector3[local.Length];
                var M = t.localToWorldMatrix;
                for (int i = 0; i < local.Length; i++) world[i] = M.MultiplyPoint3x4(local[i]);
                baked.Add(new Baked { Type = parts[1], Part = parts[2], Mf = mf, Mr = mr, World = world });
            }
            foreach (var t in all)
                if (t.name.StartsWith("car:") && t.name.Contains(":lamp:parked") && t.TryGetComponent<MeshRenderer>(out var pr) && pr.sharedMaterial)
                {
                    var m = Copy(pr.sharedMaterial); pr.sharedMaterial = m; _lampParked.Add(m);
                }

            var found = new List<(string type, double x, double z, bool alongX)>();
            var templates = new Dictionary<string, Mesh>(); // type:part
            var mats = new Dictionary<string, Material>();
            var bodyColor = new Dictionary<string, Material>();
            var lampSrc = baked.Find(b => b.Part == "lamp");
            if (lampSrc != null) _lampMoving = Copy(lampSrc.Mr.sharedMaterial);
            foreach (var type in new[] { "sedan", "suv", "truck" })
            {
                var body = baked.Find(b => b.Type == type && b.Part == "body");
                if (body == null) continue;
                int k = InstanceCount(body.World);
                if (k == 0)
                {   // hide the frozen cars rather than leave them standing in the lanes without colliders
                    Debug.LogWarning($"[town] car:{type}: the baked pool could not be split; its baked cars are hidden");
                    foreach (var b in baked) if (b.Type == type) b.Mr.enabled = false;
                    continue;
                }
                int per = body.World.Length / k;
                var lanes = new int[k]; var centres = new Vector3[k]; var along = new bool[k];
                int tpl = -1;
                for (int b = 0; b < k; b++)
                {
                    var (c, ext) = BlockBox(body.World, b * per, per);
                    centres[b] = c; along[b] = ext.x > ext.y;
                    lanes[b] = TownTraffic.LaneOf(along[b], c.x, -c.z);
                    if (lanes[b] >= 0) { found.Add((type, c.x, -c.z, along[b])); if (tpl < 0) tpl = b; }
                }
                foreach (var part in PartNames)
                {
                    var bk = baked.Find(b => b.Type == type && b.Part == part);
                    if (bk == null || bk.World.Length % k != 0) continue;
                    int pp = bk.World.Length / k;
                    if (tpl >= 0)
                    {
                        var lane = TownTraffic.Lanes[lanes[tpl]];
                        templates[type + ":" + part] = Template(bk, tpl * pp, pp, centres[tpl], Travel(lane));
                        if (!mats.ContainsKey(type + ":" + part)) mats[type + ":" + part] = bk.Mr.sharedMaterial;
                    }
                    KeepParked(bk, k, pp, lanes); // the cars idling in a lot stay where they are
                    // the idling cars run their engines: their lamps share the moving pool's (town-traffic.js lampMat)
                    if (part == "lamp" && bk.Mr.enabled && _lampMoving) bk.Mr.sharedMaterial = _lampMoving;
                }
            }

            // Static colliders the scene build made from the original simulation's 'movingCar' solids. They stand where
            // the original's active cars were when the layout was dumped, not where the baked meshes are (no box lies
            // within 2.5 m of a baked car), so they are invisible walls in the lanes either way: the live cars, or the
            // hidden frozen ones above, replace them.
            foreach (var t in all) if (t.name.StartsWith("Solid_movingCar")) t.gameObject.SetActive(false);

            _sim = new TrafficSim(TrafficSim.PoolFrom(found));
            var beamMat = BeamMaterial();
            var beamMesh = BeamMesh();
            var root = new GameObject("Traffic").transform;
            root.SetParent(transform, false);
            for (int i = 0; i < _sim.Pool.Count; i++)
            {
                var car = _sim.Pool[i];
                var go = new GameObject($"Car_{i}_{car.Type}");
                go.transform.SetParent(root, false);
                foreach (var part in PartNames)
                {
                    if (!templates.TryGetValue(car.Type + ":" + part, out var mesh)) continue;
                    var p = new GameObject(part); p.transform.SetParent(go.transform, false);
                    p.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = p.AddComponent<MeshRenderer>();
                    var src = mats[car.Type + ":" + part];
                    if (part == "body")
                    {
                        var key = src.GetInstanceID() + car.Color;
                        if (!bodyColor.TryGetValue(key, out var bm)) { bm = Copy(src); SetBaseColor(bm, Hex(car.Color)); bodyColor[key] = bm; }
                        r.sharedMaterial = bm;
                    }
                    else r.sharedMaterial = part == "lamp" && _lampMoving ? _lampMoving : src;
                    r.shadowCastingMode = part == "body" ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = part != "lamp";
                }
                var col = go.AddComponent<BoxCollider>();
                col.center = new Vector3(0, 0.65f, 0);
                col.size = new Vector3((float)car.Wid + 0.1f, 2.3f, (float)car.Len + 0.1f);
                var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
                GameObject beam = null;
                if (beamMat)
                {
                    beam = new GameObject("Beam");
                    beam.transform.SetParent(go.transform, false);
                    beam.transform.localPosition = new Vector3(0, 0.03f, (float)car.Len / 2);
                    beam.AddComponent<MeshFilter>().sharedMesh = beamMesh;
                    var br = beam.AddComponent<MeshRenderer>(); br.sharedMaterial = beamMat;
                    br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; br.receiveShadows = false;
                    beam.SetActive(false);
                }
                _cars.Add(new CarView { Car = car, Go = go, Col = col, Beam = beam });
            }
            PoseCars();
        }

        /// <summary>Unity direction a lane's cars travel in.</summary>
        static Vector3 Travel(TrafficLane lane) => lane.AxisX ? new Vector3(lane.Dir, 0, 0) : new Vector3(0, 0, -lane.Dir);

        /// <summary>One car of a baked mesh as a mesh of its own, about its centre on the road and facing +z.</summary>
        Mesh Template(Baked bk, int start, int count, Vector3 centre, Vector3 travel)
        {
            var src = bk.Mf.sharedMesh;
            var inv = Quaternion.Inverse(Quaternion.LookRotation(travel));
            var origin = new Vector3(centre.x, 0, centre.z);
            var M = bk.Mf.transform.localToWorldMatrix;
            var nrm = src.normals; var uv = src.uv;
            var v = new Vector3[count]; var n = new Vector3[nrm.Length == src.vertexCount ? count : 0]; var u = new Vector2[uv.Length == src.vertexCount ? count : 0];
            for (int i = 0; i < count; i++)
            {
                v[i] = inv * (bk.World[start + i] - origin);
                if (n.Length > 0) n[i] = (inv * M.MultiplyVector(nrm[start + i])).normalized;
                if (u.Length > 0) u[i] = uv[start + i];
            }
            var tris = new List<int>();
            bool flip = M.determinant < 0;
            for (int sm = 0; sm < src.subMeshCount; sm++)
            {
                var t = src.GetTriangles(sm);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    int a = t[i] - start, b = t[i + 1] - start, c = t[i + 2] - start;
                    if (a < 0 || b < 0 || c < 0 || a >= count || b >= count || c >= count) continue;
                    if (flip) { tris.Add(a); tris.Add(c); tris.Add(b); } else { tris.Add(a); tris.Add(b); tris.Add(c); }
                }
            }
            var mesh = new Mesh { name = $"car:{bk.Type}:{bk.Part}:template" };
            mesh.vertices = v;
            if (n.Length > 0) mesh.normals = n;
            if (u.Length > 0) mesh.uv = u;
            mesh.SetTriangles(tris, 0);
            if (n.Length == 0) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _made.Add(mesh);
            return mesh;
        }

        /// <summary>Leaves only the baked instances that are not on a lane (cars idling in a lot); hides the mesh when none is.</summary>
        void KeepParked(Baked bk, int k, int per, int[] lanes)
        {
            var keep = new List<int>();
            for (int b = 0; b < k; b++) if (lanes[b] < 0) keep.Add(b);
            if (keep.Count == 0) { bk.Mr.enabled = false; return; }
            var src = bk.Mf.sharedMesh;
            var map = new int[src.vertexCount];
            for (int i = 0; i < map.Length; i++) map[i] = -1;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>();
            var sv = src.vertices; var sn = src.normals; var su = src.uv;
            foreach (var b in keep)
                for (int i = b * per; i < (b + 1) * per; i++)
                {
                    map[i] = v.Count; v.Add(sv[i]);
                    if (sn.Length == sv.Length) n.Add(sn[i]);
                    if (su.Length == sv.Length) u.Add(su[i]);
                }
            var tris = new List<int>();
            for (int sm = 0; sm < src.subMeshCount; sm++)
            {
                var t = src.GetTriangles(sm);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    int a = map[t[i]], b = map[t[i + 1]], c = map[t[i + 2]];
                    if (a >= 0 && b >= 0 && c >= 0) { tris.Add(a); tris.Add(b); tris.Add(c); }
                }
            }
            var mesh = new Mesh { name = src.name + " (idling)" };
            mesh.SetVertices(v); if (n.Count == v.Count) mesh.SetNormals(n); if (u.Count == v.Count) mesh.SetUVs(0, u);
            mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
            _made.Add(mesh);
            bk.Mf.sharedMesh = mesh;
        }

        /// <summary>The headlight glow: a 3.4 x 11 m quad on the road ahead of the car, bright and narrow at the car.</summary>
        Mesh BeamMesh()
        {
            var m = new Mesh { name = "carBeam" };
            m.vertices = new[] { new Vector3(-1.7f, 0, 0), new Vector3(1.7f, 0, 0), new Vector3(-1.7f, 0, 11), new Vector3(1.7f, 0, 11) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateNormals(); m.RecalculateBounds();
            _made.Add(m);
            return m;
        }

        Material BeamMaterial()
        {
            const int w = 64, h = 256;
            var shader = SkyRig.LoadShader("AiOnAdditiveGlow", "AiOn/AdditiveGlow");
            if (!shader) { Debug.LogWarning("[town] AiOn/AdditiveGlow missing: no headlight glow"); return null; }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "carBeamTex" };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h, half = 0.2f + 0.3f * v; // town-traffic.js beamTex: 0.3..0.7 at the car, full width far out
                for (int x = 0; x < w; x++)
                {
                    float d = Mathf.Abs((x + 0.5f) / w - 0.5f);
                    float edge = Mathf.Clamp01((half - d) / 0.04f);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(255 * 0.9f * (1 - v) * edge));
                }
            }
            tex.SetPixels32(px); tex.Apply(false, true);
            _made.Add(tex);
            var m = new Material(shader) { name = "carBeams" };
            m.SetTexture("_BaseMap", tex);
            var c = Hex("#ffe9c4"); c.a = 0.32f;
            m.SetColor("_BaseColor", c);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _made.Add(m);
            return m;
        }

        void PoseCars()
        {
            bool night = IsNight(_env ?? EnvName);
            bool beams = night && _sim.Mode != "none";
            foreach (var cv in _cars)
            {
                var c = cv.Car;
                if (cv.On != c.Active) { cv.On = c.Active; cv.Go.SetActive(c.Active); }
                if (!c.Active) continue;
                var lane = TownTraffic.Lanes[c.Lane];
                cv.Go.transform.SetPositionAndRotation(new Vector3((float)c.X, 0, -(float)c.Z), Quaternion.LookRotation(Travel(lane)));
                if (cv.Col.enabled != c.SolidOn) cv.Col.enabled = c.SolidOn;
                if (cv.Beamed != beams) { cv.Beamed = beams; if (cv.Beam) cv.Beam.SetActive(beams); }
            }
        }

        /// <summary>The preset the level shows now (the environment's own; the host's name before the first level); only
        /// until the first <see cref="Tick"/>, which takes the crowd's env name so the signals and the crowd agree.</summary>
        string EnvName
        {
            get
            {
                var env = EnvironmentController.Instance;
                return env != null && env.Current != null ? env.Current.Name : _host ? _host.Env : "day";
            }
        }

        static bool IsNight(string env) => env == "night" || env == "blackout" || env == "dusk";

        void Lamps(string env)
        {
            if (env == _lampEnv) return;
            _lampEnv = env;
            bool night = IsNight(env);
            if (_lampMoving) SetEmission(_lampMoving, Color.white * (night ? 2f : 0.25f));
            float parked = env == "blackout" ? 0 : night ? 0.04f : 0.25f;
            foreach (var m in _lampParked) SetEmission(m, Color.white * parked);
        }

        // ---------------------------------------------------------------- signals

        void BuildSignals(Transform[] all)
        {
            foreach (var t in all)
            {
                if (!t.name.StartsWith("signals")) continue;
                foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var e = EmissiveOf(r.sharedMaterial);
                    if (e.maxColorComponent < 0.2f) continue; // housings and poles
                    var b = r.bounds;
                    bool ped = b.center.y < 4f;
                    int kind;
                    if (e.g > e.r) kind = ped ? 4 : 2;
                    else if (ped) kind = 3;
                    else kind = e.g < 0.15f * e.r ? 0 : 1;
                    // pedestrian signals crossing 연무로 (z within ±8) wait for NS; heads only at 한빛로 (x 48..52) are NS
                    string group = ped ? (b.max.z <= 9f && b.min.z >= -9f ? "NS" : "EW") : (b.size.x < 8f ? "NS" : "EW");
                    string hue = kind switch { 0 => "#ff2a1a", 1 => "#ffa424", 2 => "#27e08a", 3 => "#ff3322", _ => "#2ae68e" };
                    var m = Copy(r.sharedMaterial); r.sharedMaterial = m;
                    _signals.Add(new Sig { M = m, Group = group, Kind = kind, Hue = Linear(hue), Level = -1 });
                }
            }
        }

        void SetSignals(double t, bool power)
        {
            var s = TownTraffic.SignalAt(t);
            var ew = TownTraffic.Lamps(s, "EW", t, power); var ns = TownTraffic.Lamps(s, "NS", t, power);
            for (int i = 0; i < _signals.Count; i++)
            {
                var g = _signals[i];
                var L = g.Group == "NS" ? ns : ew;
                float lv = g.Kind switch { 0 => L.Red, 1 => L.Amber, 2 => L.Green, 3 => L.WalkRed, _ => L.WalkGreen };
                if (lv == g.Level) continue;
                g.Level = lv; _signals[i] = g;
                SetEmission(g.M, g.Hue * lv);
            }
        }

        // ---------------------------------------------------------------- closures

        void BuildClosures(Transform[] all)
        {
            var flashSrc = new Dictionary<Material, Material>();
            foreach (var t in all)
            {
                if (!t.name.StartsWith("closure:")) continue;
                foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var src = SourceOf(r.sharedMaterial);
                    if (!src || src.name.StartsWith("lit:")) continue;
                    var e = EmissiveOf(r.sharedMaterial);
                    if (!(e.r > 0.5f && e.g > 0.2f && e.g < 0.6f && e.b < 0.15f)) continue; // the amber flash lamp
                    if (!flashSrc.TryGetValue(r.sharedMaterial, out var m)) { m = Copy(r.sharedMaterial); flashSrc[r.sharedMaterial] = m; _flash.Add(m); }
                    r.sharedMaterial = m;
                }
            }
            var lights = new GameObject("ClosureLights").transform;
            lights.SetParent(transform, false);
            foreach (var id in TownClosures.Guides.Keys)
            {
                var (x, z, tx, tz) = TownClosures.Tower(id);
                float y = (float)Hy(x, z) + 3.2f;
                var go = new GameObject("Tower_" + id);
                go.transform.SetParent(lights, false);
                go.transform.position = LevelLayout.ToUnity((float)x, y, (float)z);
                go.transform.rotation = Quaternion.LookRotation(LevelLayout.ToUnity((float)tx, 0, (float)tz) - go.transform.position);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot; l.color = Hex("#f4f8ff"); l.range = 26;
                l.spotAngle = 2 * 0.7f * Mathf.Rad2Deg; l.innerSpotAngle = l.spotAngle * 0.4f;
                l.intensity = 120 * LightScale; l.shadows = LightShadows.None;
                l.enabled = false;
                _towers[id] = l;
            }
        }

        readonly List<string> _known = new();
        bool _closuresSynced;

        /// <summary>Places the guides and towers when the known closures change (town-events.js setClosures).</summary>
        void SyncClosures()
        {
            _known.Clear();
            _known.AddRange(TownClosures.Known(_town?.Closures));
            if (_closuresSynced && SameList(_known, _closures)) return;
            _closuresSynced = true;
            _closures.Clear();
            _closures.AddRange(_known);
            foreach (var kv in _towers) kv.Value.enabled = _closures.Contains(kv.Key);
            while (_guides.Count < _closures.Count && _host)
            {
                var p = _host.SpawnPerson("guide", 0, 0, 0, 0);
                if (p == null) break;
                if (p is AiOn.People.UnityPerson up && up.Root) { up.Root.name = "ClosureGuide_" + _guides.Count; up.Root.transform.SetParent(transform, true); }
                p.SetProp("baton", true);
                _guides.Add(p); _guideAnim.Add(null);
            }
            for (int k = 0; k < _guides.Count; k++)
            {
                var p = _guides[k];
                bool on = k < _closures.Count;
                p.Visible = on;
                if (!on) { _guideAnim[k] = null; continue; }
                var g = TownClosures.Guides[_closures[k]];
                p.SetPosition(g.x, Hy(g.x, g.z), g.z, System.Math.Atan2(-g.fx, -g.fz));
                p.SetProp("baton", true);
                p.SetAnim("idle"); _guideAnim[k] = "idle";
            }
        }

        static bool SameList(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        void DriveGuides(double dt, bool hasPlayer, double px, double pz)
        {
            _gt += dt;
            for (int k = 0; k < _guides.Count && k < _closures.Count; k++)
            {
                var p = _guides[k];
                double dx = hasPlayer ? px - p.X : 99, dz = hasPlayer ? pz - p.Z : 99;
                bool near = dx * dx + dz * dz < 64;
                if (near) p.LookAt(px, pz); else p.LookAt(null, null);
                var a = TownClosures.GuideAnim(_gt, k, near);
                if (a != _guideAnim[k]) { _guideAnim[k] = a; p.SetAnim(a); }
            }
        }

        // ---------------------------------------------------------------- state solids

        void BuildStateSolids(Transform root, LevelLayout layout)
        {
            if (!layout) return;
            var parent = new GameObject("StateSolids").transform;
            parent.SetParent(transform, false);
            int i = 0;
            foreach (var s in layout.solids)
            {
                if (s == null || !s.off || s.IsEmpty || string.IsNullOrEmpty(s.id)) continue;
                if (!s.id.StartsWith("closure:") && System.Array.IndexOf(StateIds, s.id) < 0) continue;
                var go = new GameObject($"State_{s.id}_{i++}");
                go.transform.SetParent(parent, false);
                var (c, size) = LevelLayout.BoxToUnity(s.x0, s.x1, s.y0, s.y1, s.z0, s.z1);
                go.transform.position = c; go.transform.localScale = size;
                go.AddComponent<BoxCollider>();
                go.SetActive(false);
                _stateSolids.Add((s.id, go));
            }
        }

        /// <summary>Whether a state solid stands now (the crowd's switched solids, as the map reads them).</summary>
        bool SolidOn(string id) => _town != null && _town.Solids != null && _town.Solids.TryGetSwitched(id, out bool off) && !off;

        // ---------------------------------------------------------------- street lights

        void BuildStreetLights(Transform[] all)
        {
            var heads = new List<Vector3>();
            foreach (var t in all)
            {
                if (!t.TryGetComponent<MeshRenderer>(out var r) || !t.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                var src = SourceOf(r.sharedMaterial);
                if (!src || !src.name.Contains("lit:street") || r.bounds.min.y < 8.5f) continue;
                var v = mf.sharedMesh.vertices;
                if (v.Length % 24 != 0) continue;
                var M = t.localToWorldMatrix;
                for (int b = 0; b < v.Length / 24; b++)
                {
                    Vector3 sum = Vector3.zero;
                    for (int i = 0; i < 24; i++) sum += M.MultiplyPoint3x4(v[b * 24 + i]);
                    heads.Add(sum / 24);
                }
            }
            var parent = new GameObject("StreetLights").transform;
            parent.SetParent(transform, false);
            // 103동 canopy downlight (town-props: slab underside at Y + 2.75), wide enough to reach the posts' inner faces
            AddSpot(parent, "Canopy103", new Vector3(78, Curb + 2.68f, -28.7f), new Vector3(78, 0, -27.8f), "#ffe2bc", 14, 11, 1.4f, 0.4f);
            // the shelter entrances' battery emergency lights (town-shelter.js): A's lit stairwell, and C's opening, lit
            // only while the access shutter is up (kit.gateLit)
            _shelterA = AddPoint(parent, "ShelterA", LevelLayout.ToUnity(-86f, Curb + 1.9f, 14.6f), "#eef4ff");
            _shelterC = AddPoint(parent, "ShelterC", LevelLayout.ToUnity(-110.7f, -0.5f + 1.9f, 15.1f), "#f2f6ff");
            var used = new HashSet<int>();
            foreach (var (x, z) in KeyStops)
            {
                int best = -1; float bd = float.MaxValue;
                for (int i = 0; i < heads.Count; i++)
                {
                    float d = (heads[i].x - (float)x) * (heads[i].x - (float)x) + (-heads[i].z - (float)z) * (-heads[i].z - (float)z);
                    if (d < bd) { bd = d; best = i; }
                }
                if (best < 0 || !used.Add(best)) continue;
                var h = heads[best];
                AddSpot(parent, $"Head_{best}", new Vector3(h.x, Curb + 8.7f, h.z), new Vector3(h.x, 0, h.z), "#ffd9a8", 600, 30, 1.15f, 0.7f);
            }
        }

        void AddSpot(Transform parent, string name, Vector3 at, Vector3 target, string hex, float candela, float range, float angle, float penumbra)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            go.transform.rotation = Quaternion.LookRotation(target - at, Vector3.forward);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot; l.color = Hex(hex); l.range = range;
            l.spotAngle = 2 * angle * Mathf.Rad2Deg; l.innerSpotAngle = l.spotAngle * (1 - penumbra);
            l.shadows = LightShadows.None; l.enabled = false;
            _street.Add((l, candela * LightScale));
        }

        Light _shelterA, _shelterC;
        const float ShelterCandela = 6;

        /// <summary>A three.js PointLight(hex, 6, 6, 2) on the 'emergency' level.</summary>
        static Light AddPoint(Transform parent, string name, Vector3 at, string hex)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = Hex(hex); l.range = 6;
            l.intensity = ShelterCandela * LightScale; l.shadows = LightShadows.None;
            return l;
        }

        /// <summary>Whether shelter entrance C stands open (the access gate).</summary>
        bool AccessOpen => _town != null && _town.Gates.TryGetValue("access", out var a) && a == "open";

        void SetEmergency(float level)
        {
            bool on = level > LevelMaterials.LitEpsilon;
            float i = ShelterCandela * LightScale * level;
            if (_shelterA) { _shelterA.intensity = i; if (_shelterA.enabled != on) _shelterA.enabled = on; }
            if (_shelterC) { bool c = on && AccessOpen; _shelterC.intensity = i; if (_shelterC.enabled != c) _shelterC.enabled = c; }
        }

        void SetStreet(float level)
        {
            if (Mathf.Abs(level - _streetLevel) < 1e-4f) return;
            _streetLevel = level;
            foreach (var (l, i) in _street) { l.intensity = i * level; l.enabled = level > LevelMaterials.LitEpsilon; }
        }

        // ---------------------------------------------------------------- per state and per frame

        /// <summary>After any town state: the traffic mode, closures (guides, towers) and the state solids.</summary>
        public void OnState()
        {
            // TownCrowd keeps only known modes (it logs the unknown ones once, on apply)
            if (_sim != null && _town != null && !_gridLost && _sim.Mode != _town.Traffic) _sim.SetMode(_town.Traffic);
            SyncClosures();
            foreach (var (id, go) in _stateSolids) { bool on = SolidOn(id); if (go.activeSelf != on) go.SetActive(on); }
            if (_sim != null) PoseCars();
        }

        /// <summary>One frame on the crowd's clock t (seconds since the level opened).</summary>
        /// <param name="envName">The env name of the crowd's frame, so the signal heads and the crowd's holds agree.</param>
        public void Tick(double dt, double t, Component player, string envName)
        {
            var env = EnvironmentController.Instance;
            bool grid = env == null || env.power;
            _env = envName ??= EnvName;
            // No grid power after the blackout: dead signals, and nobody drives through town any more
            // (town.js update: setState('traffic', 'none'), so the state and the crowd's holds follow too).
            if (!grid && _sim != null && _sim.Mode != "none") LoseGrid();
            bool power = grid && envName != "blackout";
            bool hasPlayer = player;
            double px = 0, py = 0, pz = 0;
            if (hasPlayer) { var p = player.transform.position; px = p.x; py = p.y; pz = -p.z; }

            if (_sim != null)
            {
                int n = 0;
                if (_town != null)
                {
                    var bodies = _town.Bodies;
                    if (_peds.Length < bodies.Count) _peds = new Pt[bodies.Count + 16];
                    for (int i = 0; i < bodies.Count; i++) { var b = bodies[i]; if (b.Visible) _peds[n++] = new Pt(b.X, b.Z); }
                }
                _sim.Step(dt, t, power, hasPlayer, px, py, pz, _peds, n);
                Lamps(envName);
                PoseCars();
            }
            SetSignals(t, power);
            float flash = TownClosures.Flash(t, _closures.Count > 0, _host && _host.ReduceMotion);
            if (flash != _flashLevel) { _flashLevel = flash; foreach (var m in _flash) SetEmission(m, Linear("#ffa21a") * flash); }
            DriveGuides(dt, hasPlayer, px, pz);
            var lm = LevelMaterials.Instance;
            SetStreet(lm != null ? LevelMaterials.EffectiveLevel(lm.LevelOf("street")) : 0);
            float emergency = lm != null ? LevelMaterials.EffectiveLevel(lm.LevelOf("emergency")) : 1;
            foreach (var kv in _towers) { kv.Value.intensity = 120 * LightScale * emergency; }
            SetEmergency(emergency);
        }

        void LoseGrid()
        {
            _gridLost = true;
            if (_host) _host.ApplyLevelState("traffic", "none"); // -> LevelCrowd.Apply -> TownCrowd + OnState
            else _town?.Apply("traffic", "none");
            _sim.SetMode("none");
        }

        void OnDestroy()
        {
            foreach (var o in _made) if (o) Destroy(o);
            _made.Clear();
        }
    }
}
