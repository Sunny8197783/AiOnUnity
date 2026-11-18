using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using AiOn.Interaction;
using AiOn.Levels;
using AiOn.Levels.Crowd;
using AiOn.People;
using AiOn.Player;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace AiOn.Story
{
    /// <summary>
    /// Runs the story in Unity: the IStoryEngine (level scenes, the persistent player and camera, environment presets,
    /// level states, characters, frame/interact/key events), IStoryAudio, IStoryPersist and the IStoryUi screens.
    /// Lives in the Boot scene with the player, camera, HUD and overlay, which survive level loads; each level scene's own
    /// test player and systems are removed when it loads. All coordinates it exchanges are the original's frame.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class UnityStoryHost : MonoBehaviour, IStoryEngine, IStoryAudio, IStoryPersist, IStoryUi
    {
        public PlayerController player;
        public PlayerCameraRig cameraRig;
        public PlayerInteractor interactor;
        public StoryOverlay overlay;
        [Tooltip("Person prefabs by preset name (jeongmin, seoyeon, manager, clerk, guide, coworker, elder, crowd1..6).")]
        public List<GameObject> peoplePrefabs = new();
        public StorySettings settings = new();
        [Tooltip("Every level's layout (walk zones, solids): the crowds walk on them. Set by AiOn/Wire Level Layouts.")]
        public List<LevelLayout> levelLayouts = new();
        [Tooltip("Returns the live game state for saving (set by the runner).")]
        public Func<GameState> getState;

        public static UnityStoryHost Instance { get; private set; }

        // ---------- level ----------
        string _levelId, _env = "interior";
        readonly List<StoryPoint> _points = new();
        readonly Dictionary<InteractionPoint, StoryPoint> _byIp = new();
        readonly Dictionary<StoryPoint, InteractionPoint> _ipBy = new();
        readonly Dictionary<string, JToken> _levelState = new();
        readonly HashSet<string> _undrawn = new();
        readonly Dictionary<GameObject, UnityPerson> _persons = new();
        LevelSpawnPoints _spawns;
        ShopGoods _goods;
        bool _held;
        LevelCrowd _crowd;
        Light _flashlight;
        float _airTop;
        bool _wasGrounded = true;
        bool _landReset = true; // set by teleports and level changes: a jump in position is not a fall

        public event Action<double> Frame;
        public event Action<StoryPoint> Interact;
        public event Action<StoryPoint> Nearest;
        public event Action<string> Key;
        public event Action<double> Land;
        public event Action<JObject> Blocked;

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            if (overlay)
            {
                // the minimap draws the level's walk zones and solids as they are now (doors, closures, rubble)
                overlay.MapLayout = LayoutOf; overlay.MapSolidOff = MapSolidOff; overlay.MapExtraSolids = MapExtraSolids;
            }
            foreach (var go in new[] { player ? player.gameObject : null, cameraRig ? cameraRig.gameObject : null, overlay ? overlay.gameObject : null })
                if (go && go.transform.parent == null) DontDestroyOnLoad(go);
            var systems = GameObject.Find("GameSystems");
            if (systems) DontDestroyOnLoad(systems);
            if (interactor)
            {
                interactor.Interacted += ip => { if (ip && _byIp.TryGetValue(ip, out var sp)) Interact?.Invoke(sp); };
                interactor.CurrentChanged += ip => Nearest?.Invoke(ip && _byIp.TryGetValue(ip, out var sp) ? sp : null);
            }
            if (player) _reader = player.GetComponent<PlayerInputReader>();
            if (player) { player.vaultPlanner = PlanVault; player.vaultLandingFree = VaultLandingFree; player.stairBlocked = StairBlocked; }
            if (cameraRig) cameraRig.hostOwnsView = true; // V goes through the Director, which saves the view setting
            AiOn.Settings.AudioBus.Changed += ApplyBuses;
        }

        // ---------- wheelchair: a stair flight is a wall (controls.js prof.stairs) ----------

        bool _stairBlocked;
        PlayerInputReader _reader;

        /// <summary>
        /// The player's step from feet to next (Unity) without stairs: true when it goes onto a stair flight of the level's
        /// walk zones from ground that is not one. The first stop of a contact raises Blocked { kind: 'stairs', x, y, z, toX,
        /// toZ, zone } (original frame) for the Director's help offer.
        /// </summary>
        public bool StairBlocked(Vector3 feet, Vector3 next)
        {
            var layout = LayoutOf(_levelId);
            int i = layout ? AiOn.Settings.StairRule.Blocking(layout.walk, feet.x, -feet.z, next.x, -next.z, feet.y) : -1;
            bool stop = i >= 0;
            if (stop && !_stairBlocked)
            {
                var zone = AiOn.Settings.StairRule.ZoneJson(layout.walk[i]);
                Blocked?.Invoke(new JObject { ["kind"] = "stairs", ["x"] = feet.x, ["y"] = feet.y, ["z"] = -feet.z, ["toX"] = next.x, ["toZ"] = -next.z, ["zone"] = zone });
            }
            _stairBlocked = stop;
            return stop;
        }

        // ---------- vault / climb (engine/vault.js on the level's solids as they are now) ----------

        string _vaultLevel;
        VaultPlanner.World _vaultWorld;
        readonly List<(LevelLayout.SolidBox src, VaultPlanner.Box box)> _vaultBoxes = new();
        readonly List<LevelLayout.SolidBox> _vaultExtra = new();

        /// <summary>The vault plan for the player's feet and heading (Unity), in the original frame; null = just jump.</summary>
        public VaultPlanner.Plan? PlanVault(Vector3 feet, Vector3 dir)
        {
            var layout = LayoutOf(_levelId);
            if (!layout) return null;
            if (_vaultLevel != _levelId || _vaultWorld == null)
            {
                _vaultLevel = _levelId; _vaultWorld = VaultPlanner.WorldOf(layout); _vaultBoxes.Clear();
                foreach (var s in layout.solids) if (s != null && !s.IsEmpty) _vaultBoxes.Add((s, VaultPlanner.BoxOf(s)));
            }
            double x = feet.x, z = -feet.z;
            var w = _vaultWorld;
            w.Solids.Clear();
            foreach (var (src, box) in _vaultBoxes) // only those near, and only the ones on now (doors, closures, the fire)
                if (box.X0 <= x + 4 && box.X1 >= x - 4 && box.Z0 <= z + 4 && box.Z1 >= z - 4 && !MapSolidOff(src)) w.Solids.Add(box);
            MapExtraSolids(_vaultExtra); // rubble heaps from the danger layer
            foreach (var s in _vaultExtra) w.Solids.Add(VaultPlanner.BoxOf(s));
            return VaultPlanner.PlanVault(w, x, z, feet.y, dir.x, -dir.z, PlayerController.Radius, PlayerController.Height);
        }

        static readonly Collider[] VaultOverlap = new Collider[16];

        /// <summary>The scene agrees the body fits where a vault lands (a door leaf or a prop the layout does not know).</summary>
        public bool VaultLandingFree(Vector3 feet)
        {
            const float r = 0.26f;
            int n = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * (0.3f + r), feet + Vector3.up * (PlayerController.Height - r), r, VaultOverlap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = VaultOverlap[i];
                if (!c || (player && c.transform.IsChildOf(player.transform))) continue;
                if (c.GetComponentInParent<Animator>() || c.GetComponentInChildren<PersonAnimator>()) continue; // people step aside (bodies.js pushes the player out)
                return false;
            }
            return true;
        }

        /// <summary>The story's handle on a person in the scene by its root (a named Npc_ or one the story spawned); null for crowd members.</summary>
        public IStoryPerson PersonFor(GameObject root)
        {
            if (!root) return null;
            if (_persons.TryGetValue(root, out var p)) return p;
            return root.name.StartsWith("Npc_", StringComparison.Ordinal) ? Wrap(root.name.Substring(4), root) : null;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            AiOn.Settings.AudioBus.Changed -= ApplyBuses;
        }

        public string LevelId => _levelId;
        public IReadOnlyList<StoryPoint> Points => _points;

        public bool TryGetSpawn(string spawn, out double x, out double y, out double z, out double yaw)
        {
            x = y = z = yaw = 0;
            if (_spawns == null || !_spawns.TryGet(spawn, out var p)) return false;
            x = p.position.x; y = p.position.y; z = -p.position.z; yaw = -p.yaw * Mathf.Deg2Rad;
            return true;
        }

        public static string SceneOf(string levelId) => char.ToUpperInvariant(levelId[0]) + levelId.Substring(1);

        /// <summary>True from the start of Enter until the new level stands (the frame governor does not judge load stalls).</summary>
        public bool Loading { get; private set; }

        /// <summary>Raised once a new level stands (after the spawn and the prepare states), before the fade back in.</summary>
        public event Action<string> LevelEntered;

        public async Task Enter(string levelId, string spawn, Action prepare = null)
        {
            Loading = true;
            try { await EnterLevel(levelId, spawn, prepare); }
            finally { Loading = false; }
        }

        async Task EnterLevel(string levelId, string spawn, Action prepare)
        {
            if (overlay) await overlay.FadeTo(1, 0.25);
            var op = SceneManager.LoadSceneAsync(SceneOf(levelId), LoadSceneMode.Single);
            while (!op.isDone) await Task.Yield();
            var scene = SceneManager.GetSceneByName(SceneOf(levelId));
            // The level scene's own test player, camera, HUD and event system make way for the persistent ones.
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == "Player" || go.name == "PlayerCamera" || go.name == "GameSystems" || go.name == "EventSystem") Destroy(go);
            await Task.Yield();

            _levelId = levelId;
            _landReset = true;
            _levelState.Clear(); _undrawn.Clear(); _persons.Clear(); _slabLevel = null; _slabDoor.Clear();
            _lift = null; _liftElder = null; // died with the old level
            _spawns = FindFirstObjectByType<LevelSpawnPoints>();
            _goods = FindFirstObjectByType<ShopGoods>();
            _points.Clear(); _byIp.Clear(); _ipBy.Clear();
            foreach (var ip in FindObjectsByType<InteractionPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var pos = ip.transform.position;
                var sp = new StoryPoint
                {
                    Id = ip.id, Label = ip.label, Kind = ip.kind, X = pos.x, Y = pos.y, Z = -pos.z,
                    R = ip.radius, Reach = ip.reach, Host = ip.host, Enabled = ip.interactable,
                };
                _points.Add(sp); _byIp[ip] = sp; _ipBy[sp] = ip;
            }
            BuildFx();
            // Interiors' live dressing: stock shelf, sensor / battery lamps, candle, clerk, office poses and dashboard, shelter supply.
            _dressing = AiOn.Levels.Interiors.InteriorDressing.Create(levelId, () => player ? player.transform : null,
                () => cameraRig ? cameraRig.cam : null, Npc, (id, en, label) => SetPoint(id, en, label));
            // The level's lit materials start at the current preset's lit levels and power (whichever of
            // EnvironmentController / LevelMaterials woke first).
            EnvironmentController.Instance?.PushLit();
            if (!string.IsNullOrEmpty(spawn)) Teleport(spawn);
            else if (_spawns != null && _spawns.points.Count > 0) Teleport(_spawns.points[0].name);
            // The level's crowd starts in the original's default (town normal, shelter settled, mart normal); the
            // prepare states override it behind the fade.
            _crowd = LevelCrowd.Create(levelId, LayoutOf(levelId), this);
            prepare?.Invoke();
            _crowd?.Prewarm(); // the bodies of the states prepare set, made behind the opaque fade
            if (interactor) interactor.Refresh();
            try { LevelEntered?.Invoke(levelId); } catch (Exception e) { Debug.LogException(e); }
            if (overlay) await overlay.FadeTo(0, 0.35);
        }

        public void ApplyLevelState(string key, JToken value)
        {
            if (EchoMarksView.TryApply(key, value)) { _levelState[key] = value; return; } // marks of earlier runs
            _levelState[key] = value;
            bool handled = false;
            // Crowds listen to their keys (town: crowd, closures, gates, traffic, waterPoint, buses; shelter: crowd, phase,
            // fire, ways; mart: crowd); only 'crowd' is theirs alone, the others are drawn below as well.
            if (_crowd != null && _crowd.Apply(key, value) && key == "crowd") handled = true;
            var variants = FindFirstObjectByType<LevelStateVariants>();
            if (variants != null && variants.Handles(key)) { variants.Apply(key, value); handled = true; }
            var materials = LevelMaterials.Instance != null ? LevelMaterials.Instance : FindFirstObjectByType<LevelMaterials>();
            if (materials != null && materials.Handles(key)) { materials.Apply(key, value); handled = true; }
            var pointStates = FindFirstObjectByType<LevelPointStates>();
            if (pointStates != null && pointStates.Handles(key))
            {
                foreach (var (id, en, label) in pointStates.Apply(key, value)) SetPoint(id, en, string.IsNullOrEmpty(label) ? null : label);
                handled = true;
            }
            if (_dressing && _dressing.Apply(key, value)) handled = true;
            if (_levelId == "store" && (key == "power" || key == "clerk")) StoreStock();
            switch (key)
            {
                case "buses" when _levelId == "town":
                    Buses(value?.Type == JTokenType.String ? (string)value : null, variants);
                    return;
                case "power":
                    EnvironmentController.Instance?.SetPower((string)value != "off");
                    return;
                case "soldOut":
                    _goods?.SetSoldOut(value is JArray a ? a.Select(t => (string)t) : new string[0]);
                    return;
                case "seoyeonDoor":
                    foreach (var d in FindObjectsByType<Door>(FindObjectsSortMode.None))
                        if (d.id == "door303") d.SetTarget((string)value == "open" ? 0.72f : 0f);
                    { var p = Npc("seoyeon"); if (p != null) p.Visible = (string)value == "open"; }
                    return;
                case "wallet":
                    SetPoint("homeWallet", enabled: (string)value != "taken");
                    return;
                case "bag":
                    SetPoint("homeBag", label: (string)value == "none" ? "가방 둘 자리" : (string)value == "packed" ? "비상가방 (꾸림)" : "비상가방 (비어 있음)");
                    return;
                case "elder304":
                    { var p = Npc("elder"); if (p != null) p.Visible = (string)value == "on"; }
                    return;
                case "seoyeon":
                    { var p = Npc("seoyeon"); if (p != null) p.Visible = (string)value == "shown"; }
                    return;
                case "elder":
                    { var p = Npc("elder"); if (p != null) p.Visible = (string)value == "on"; }
                    return;
                case "hallFire":
                    {
                        string v = (string)value;
                        _fire?.SetLevel(v == "small" ? 0.4f : v == "large" ? 1f : 0f);
                        SetPoint("hallFire", label: v == "none" ? "304호 앞 복도" : v == "out" ? "꺼진 불 자리" : "304호 앞 불");
                    }
                    return;
                case "smoke":
                    {
                        float d = Mathf.Clamp01((float?)value ?? 0f);
                        _smoke?.SetDensity(d);
                        if (!_levelState.TryGetValue("haze", out var h) || (string)h == "follow") _haze?.SetDensity(d * 0.5f);
                    }
                    return;
                case "haze":
                    _haze?.SetDensity((string)value == "follow" ? (_smoke ? _smoke.Density * 0.5f : 0f) : Mathf.Clamp01((float?)value ?? 0f));
                    return;
                case "strike" when _levelId == "town":
                    StrikeFx((string)value == "hit");
                    return;
                case "fire" when _levelId == "shelter":
                    { bool on = (string)value == "large"; _fire?.SetLevel(on ? 1f : 0f); _smoke?.SetDensity(on ? 0.9f : 0f); }
                    return;
            }
            if (!handled && _undrawn.Add(key)) Debug.Log($"[Story] {_levelId}: state '{key}' = {value?.ToString(Newtonsoft.Json.Formatting.None)} is kept but not drawn yet");
        }

        // ---------- level dressing the story's states ask for (port additions) ----------

        /// <summary>
        /// The assembly's buses: on = parked with the wheelchair lift down at the second bus's door and an elder waiting at
        /// it; departing = the reception stays drawn (the variant knows only on/off) while the buses drive out onto 문화로
        /// (BusDepartFx, BusRoute); departed = the reception with an empty bay; off = gone.
        /// </summary>
        void Buses(string v, LevelStateVariants variants)
        {
            if ((v == "departing" || v == "departed") && variants != null) variants.Apply("buses", "on");
            var root = variants != null ? variants.variants.Find(x => x.target && x.rules.Exists(r => r.key == "buses"))?.target : null;
            var mover = root ? root.GetComponent<BusDepartFx>() : null;
            if (root && !mover) mover = root.AddComponent<BusDepartFx>();
            if (mover) mover.SetState(v);
            bool parked = v == "on";
            if (parked && !_lift) _lift = BuildLift();
            if (_lift) _lift.SetActive(parked);
            if (parked && _liftElder == null)
                _liftElder = SpawnPerson("elder", -63.05, 0.12, -45.7, Math.PI / 2); // facing west, toward the lift and the door
            else if (!parked && _liftElder != null) { RemovePerson(_liftElder); _liftElder = null; }
        }
        GameObject _lift;
        IStoryPerson _liftElder;

        /// <summary>A wheelchair lift lowered at the second bus's door (bus x -65, door x -63.75, z -44.8 in the original frame).</summary>
        static GameObject BuildLift()
        {
            var root = new GameObject("BusLift");
            root.transform.position = LevelLayout.ToUnity(-63.05f, 0.12f, -44.8f);
            var grey = new Color(0.46f, 0.48f, 0.5f);
            void Part(string n, Vector3 local, Vector3 size, Color c)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = n;
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = local; go.transform.localScale = size;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = AiOn.Levels.Interiors.InteriorMeshes.LitMat(c, 0.35f);
            }
            Part("Platform", new Vector3(0, 0.05f, 0), new Vector3(1.05f, 0.06f, 1.25f), grey);
            Part("Lip", new Vector3(0.52f, 0.12f, 0), new Vector3(0.04f, 0.14f, 1.25f), new Color(0.85f, 0.7f, 0.15f));
            foreach (var z in new[] { -0.6f, 0.6f })
            {
                Part("Post", new Vector3(-0.45f, 0.5f, z), new Vector3(0.05f, 1.0f, 0.05f), grey);
                Part("Rail", new Vector3(0, 0.95f, z), new Vector3(0.95f, 0.04f, 0.04f), grey);
            }
            return root;
        }

        /// <summary>Store, power off and nobody behind the counter: most of the shelves are bare; batteries and water stay.</summary>
        void StoreStock()
        {
            if (!_goods) return;
            bool off = _levelState.TryGetValue("power", out var p) && p?.Type == JTokenType.String && (string)p == "off";
            bool gone = _levelState.TryGetValue("clerk", out var c) && c?.Type == JTokenType.String && (string)c == "out";
            _goods.SetSparse(off && gone, ShopGoods.SparseKeep);
        }

        // The pack on the player's back while it is on (G.know.bagOn), as the original's people.js prop; never on 서연.
        string _packPreset = "jeongmin";
        bool _packOn;
        GameObject _packBody;
        Transform _packProp;
        float _packT;

        /// <summary>Inside 302 (the apartment's 3F unit, original frame x -7.6..0.3, z 0..9.4): the pack comes off at home.</summary>
        public static bool AtHome(string level, double x, double y, double z) =>
            level == "apartment" && x > -7.7 && x < 0.4 && z > 0.05 && z < 9.5 && y > 5.2 && y < 7.5;

        static Transform FindNamed(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = FindNamed(t.GetChild(i), name); if (r) return r; }
            return null;
        }

        void PackFrame()
        {
            if ((_packT -= Time.deltaTime) > 0) return;
            _packT = 0.25f;
            var body = player ? player.transform.Find("Body") : null;
            var g = getState?.Invoke();
            if (!body) return;
            // PlayerBody shows the pack while carrying "bag" and drops it when the carry changes: re-assert it every tick on
            // the cached prop (no lookup, no allocation); off at home in 302.
            bool want = _packPreset == "jeongmin" && (Carrying == "bag" || (g != null && g.Knows("bagOn") && !AtHome(_levelId, PlayerX, PlayerY, PlayerZ)));
            if (body.gameObject != _packBody) { _packBody = body.gameObject; _packProp = null; _packOn = !want; }
            if (!_packProp && want)
            {
                PersonProps.Set(body.gameObject, "backpack", true); // builds Prop_backpack once
                _packProp = FindNamed(body, "Prop_backpack");
            }
            if (_packProp) { if (_packProp.gameObject.activeSelf != want) _packProp.gameObject.SetActive(want); }
            else if (want != _packOn) PersonProps.Set(body.gameObject, "backpack", want); // a model's own baked pack
            _packOn = want;
        }

        AiOn.Levels.Interiors.InteriorDressing _dressing;
        FireFx _fire;
        SmokeFx _smoke, _haze;
        FireFx[] _strikeFires;
        SmokeColumnFx _strikeSmoke;

        /// <summary>
        /// The destroyed villa (original town-strike.js): three fires in the heap and the smoke column. The ruin itself is
        /// a state variant; its bounds give the site centre (cx, cz) the original placed the fires around.
        /// </summary>
        void StrikeFx(bool hit)
        {
            if (_strikeSmoke == null)
            {
                if (!hit) return;
                var variants = FindFirstObjectByType<LevelStateVariants>();
                Bounds? b = null;
                if (variants)
                    foreach (var v in variants.variants)
                        if (v.target && v.rules.Exists(r => r.key == "strike" && r.visible))
                            foreach (var rend in v.target.GetComponentsInChildren<Renderer>(true))
                            { if (b == null) b = rend.bounds; else { var bb = b.Value; bb.Encapsulate(rend.bounds); b = bb; } }
                if (b == null) { Debug.LogWarning("[Story] town: no strike ruin found for its fires"); return; }
                // back to the original frame: cx, cz (the scorch mark sits at cz - 2)
                float cx = b.Value.center.x, cz = -b.Value.center.z;
                var root = new GameObject("StrikeFx").transform;
                _strikeFires = new[] { (-3f, 1.6f, -2f, 3.2f), (4f, 0.8f, 1f, 2.4f), (0f, 3.2f, 3f, 2.8f) }
                    .Select((f, i) => StoryFx.CreateFire(root, "StrikeFire" + i, LevelLayout.ToUnity(cx + f.Item1, f.Item2, cz + f.Item3), f.Item4 * 0.6f)).ToArray();
                _strikeSmoke = StoryFx.CreateSmokeColumn(root, "StrikeSmoke", LevelLayout.ToUnity(cx, 3f, cz - 2f));
            }
            foreach (var f in _strikeFires) f.SetLevel(hit ? 0.9f : 0f);
            _strikeSmoke.SetNight(_env == "night" || _env == "blackout" || _env == "dusk");
            _strikeSmoke.SetOn(hit);
        }

        /// <summary>The level's fire and smoke (original fx.fire / fx.smoke placements), all off until a state turns them on.</summary>
        void BuildFx()
        {
            _fire = null; _smoke = _haze = null; _strikeFires = null; _strikeSmoke = null;
            var root = new GameObject("StoryFx").transform;
            if (_levelId == "apartment")
            {
                // FIRE_SPOT (19.2, F3, -0.42): flames at (19.15, 5.62, -0.37); corridor smoke under the 3F ceiling; haze in 302.
                _fire = StoryFx.CreateFire(root, "HallFire", LevelLayout.ToUnity(19.15f, 5.62f, -0.37f), 1.2f);
                _smoke = StoryFx.CreateSmoke(root, "CorridorSmoke", LevelLayout.ToUnity(3f, 7.0f, -1.0f), new Vector3(37.4f, 1.2f, 1.7f), new Color(0.32f, 0.31f, 0.3f));
                _haze = StoryFx.CreateSmoke(root, "FlatHaze", LevelLayout.ToUnity(-3.65f, 7.2f, 4.0f), new Vector3(7.7f, 1.2f, 7.8f), new Color(0.34f, 0.32f, 0.3f));
            }
            else if (_levelId == "shelter")
            {
                _fire = StoryFx.CreateFire(root, "ShelterFire", LevelLayout.ToUnity(-18.6f, 0.7f, -9.2f), 2.1f);
                _smoke = StoryFx.CreateSmoke(root, "ShelterSmoke", LevelLayout.ToUnity(-18.6f, 1.9f, -9.2f), new Vector3(12f, 1.6f, 12f), new Color(0.17f, 0.15f, 0.14f));
            }
        }

        void SetPoint(string id, bool? enabled = null, string label = null)
        {
            foreach (var sp in _points)
                if (sp.Id == id) { if (enabled.HasValue) sp.Enabled = enabled.Value; if (label != null) sp.Label = label; }
        }

        public JToken LevelDefault(string key) => null;

        /// <summary>The layout of a level (walk zones, solids), or null when the boot scene was not wired with it.</summary>
        public LevelLayout LayoutOf(string levelId)
        {
            foreach (var l in levelLayouts) if (l && l.levelId == levelId) return l;
            return null;
        }

        // ---------- the minimap's live solids (original: hud.setMap draws L.solids, which the story switches) ----------

        readonly Dictionary<LevelLayout.SolidBox, Door> _slabDoor = new();
        string _slabLevel;

        /// <summary>
        /// Whether a solid of the current level's layout is off right now: a door's closed slab is off once the door
        /// opens past 0.12 (apartment-parts.js), the town's closures, water point, buses and gates follow the crowd's
        /// switched solids (town-events.js, town-shelter.js), the shelter's fire block follows the 'fire' state
        /// (shelter.js); anything else keeps its authored state.
        /// </summary>
        public bool MapSolidOff(LevelLayout.SolidBox s)
        {
            if (s == null || s.IsEmpty) return true;
            if (string.IsNullOrEmpty(s.id))
            {
                if (_slabLevel != _levelId) IndexDoorSlabs();
                return _slabDoor.TryGetValue(s, out var door) && door ? door.k > 0.12f : s.off;
            }
            if (_crowd != null && _crowd.Logic is TownCrowd town && town.Solids != null && town.Solids.TryGetSwitched(s.id, out bool off)) return off;
            if (_levelId == "shelter" && s.id == "fire") return !(_levelState.TryGetValue("fire", out var f) && f?.Type == JTokenType.String && (string)f == "large");
            return s.off;
        }

        /// <summary>Solids the story added at runtime (the danger layer's rubble heaps), in the original frame.</summary>
        public void MapExtraSolids(List<LevelLayout.SolidBox> into)
        {
            into.Clear();
            var rig = DangerFxRig.Current;
            if (rig) rig.RubbleBoxes(into);
        }

        void IndexDoorSlabs()
        {
            _slabLevel = _levelId;
            _slabDoor.Clear();
            var layout = LayoutOf(_levelId);
            if (!layout || layout.doors.Count == 0) return;
            foreach (var door in FindObjectsByType<Door>(FindObjectsSortMode.None))
            {
                if (door.def == null || !door.def.solid) continue; // a non-solid door has no closed slab
                foreach (var s in layout.solids)
                    if (string.IsNullOrEmpty(s.id) && !s.IsEmpty && LevelLayout.IsDoorSolid(door.def, s)) _slabDoor[s] = door;
            }
        }

        /// <summary>The current level's crowd (null in levels without one).</summary>
        public LevelCrowd Crowd => _crowd;
        /// <summary>The level states applied so far (for checks and debugging).</summary>
        public IReadOnlyDictionary<string, JToken> LevelStateNow => _levelState;

        // ---------- environment ----------
        public void SetEnv(string preset, double seconds = 0) { _env = preset; EnvironmentController.Instance?.BlendTo(preset, (float)seconds); }
        public string Env => _env;
        public void SetPower(bool on) => EnvironmentController.Instance?.SetPower(on);

        // ---------- player ----------
        public double PlayerX => player ? player.transform.position.x : 0;
        public double PlayerY => player ? player.transform.position.y : 0;
        public double PlayerZ => player ? -player.transform.position.z : 0;
        public double PlayerYaw => player ? -player.Yaw * Mathf.Deg2Rad : 0;

        public void Teleport(string spawn)
        {
            _landReset = true;
            if (_spawns != null && _spawns.TryGet(spawn, out var p)) player.Teleport(p.position, p.yaw);
            else Debug.LogWarning($"[Story] {_levelId}: no spawn '{spawn}'");
        }

        public void TeleportTo(double x, double y, double z, double yaw)
        {
            _landReset = true;
            player.Teleport(LevelLayout.ToUnity((float)x, (float)y, (float)z), LevelLayout.ToUnityYaw((float)yaw));
        }

        public double SpeedMul { set { if (player) player.speedMul = Mathf.Clamp((float)value, 0.2f, 1.5f); } }
        public double StaminaUse { set { if (player) player.staminaUse = Mathf.Clamp((float)value, 0.5f, 3f); } }
        public bool Crouching => player && player.Crouching;

        public string Carrying
        {
            get => player ? player.GetComponent<PlayerBody>()?.carrying : null;
            set { var b = player ? player.GetComponent<PlayerBody>() : null; if (b) b.carrying = value; }
        }

        public void SetFlashlight(bool on)
        {
            // In the hand (third person) or below the eyes (first), aimed along the gaze, dimmed close to walls: PlayerFlashlight.
            if (!_torch && player && cameraRig && cameraRig.cam)
            {
                _torch = PlayerFlashlight.Create(player, cameraRig, _flashShadows);
                _flashlight = _torch.Light;
            }
            if (_torch) _torch.SetOn(on);
        }

        PlayerFlashlight _torch;
        bool _flashShadows = true;

        /// <summary>The graphics tier: whether the flashlight casts shadows (quality.js flash).</summary>
        public bool FlashShadows
        {
            get => _flashShadows;
            set { _flashShadows = value; if (_torch) _torch.Shadows = value; }
        }

        public void SetPlayerPreset(string preset)
        {
            var prefab = peoplePrefabs.FirstOrDefault(p => p && p.name == preset);
            if (!prefab || !player) return;
            _packPreset = preset; _packBody = null; _packT = 0;
            var old = player.transform.Find("Body");
            bool active = old ? old.gameObject.activeSelf : cameraRig && cameraRig.thirdPerson;
            if (old) Destroy(old.gameObject);
            var body = Instantiate(prefab, player.transform);
            body.name = "Body";
            body.SetActive(active);
            var pb = player.GetComponent<PlayerBody>();
            if (pb) pb.person = body.GetComponentInChildren<PersonAnimator>();
            if (cameraRig) cameraRig.body = body;
        }

        public void SetModelHidden(bool hidden)
        {
            var body = player ? player.transform.Find("Body") : null;
            if (body) body.gameObject.SetActive(!hidden && cameraRig && cameraRig.thirdPerson);
        }

        // ---------- controls and camera ----------
        public bool ControlsEnabled => !InputGate.Blocked;

        public void SetControls(bool on)
        {
            if (!on && !_held) { InputGate.Hold(); _held = true; }
            else if (on && _held) { InputGate.Release(); _held = false; }
        }

        public void SetView(string view)
        {
            if (!cameraRig) return;
            cameraRig.thirdPerson = view != "first";
            cameraRig.ApplyView();
        }

        public string View => cameraRig && !cameraRig.thirdPerson ? "first" : "third";

        public void Shake(double strength, double seconds)
        {
            if (ReduceMotion || !cameraRig) return;
            cameraRig.Shake((float)strength, (float)seconds);
        }

        public void SetCameraOverride(CameraShot shot)
        {
            if (!cameraRig) return;
            cameraRig.overrideActive = shot != null;
            if (shot == null) return;
            cameraRig.overridePosition = LevelLayout.ToUnity((float)shot.X, (float)shot.Y, (float)shot.Z);
            cameraRig.overrideTarget = LevelLayout.ToUnity((float)shot.TargetX, (float)shot.TargetY, (float)shot.TargetZ);
            cameraRig.overrideFov = (float)shot.Fov;
        }

        public Task FadeTo(double black, double seconds) => overlay ? overlay.FadeTo(black, seconds) : Task.CompletedTask;
        public void SetGrade(JObject grade) => EnvironmentController.Instance?.SetGradeOverride(grade);
        public double TimeScale { get => Time.timeScale; set => Time.timeScale = Mathf.Clamp((float)value, 0f, 2f); }
        public bool ReduceMotion => settings != null && settings.ReduceMotion;

        // ---------- characters ----------
        public IStoryPerson Npc(string name)
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == "Npc_" + name && t.gameObject.scene.name == SceneOf(_levelId ?? "x")) return Wrap(name, t.gameObject);
            return null;
        }

        UnityPerson Wrap(string name, GameObject go)
        {
            if (!_persons.TryGetValue(go, out var p)) _persons[go] = p = new UnityPerson(name, go);
            return p;
        }

        public IStoryPerson SpawnPerson(string preset, double x, double y, double z, double yaw)
        {
            var prefab = peoplePrefabs.FirstOrDefault(p => p && p.name == preset) ?? peoplePrefabs.FirstOrDefault(p => p && p.name.StartsWith("crowd"));
            if (!prefab) return null;
            var go = Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            go.name = "Spawned_" + preset;
            var person = Wrap(preset, go);
            person.SetPosition(x, y, z, yaw);
            return person;
        }

        public void RemovePerson(IStoryPerson person)
        {
            if (person is UnityPerson up && up.Root) { _persons.Remove(up.Root); Destroy(up.Root); }
        }

        // ---------- frame, keys, landing ----------
        static readonly (Key key, string code)[] UiKeys =
        {
            (UnityEngine.InputSystem.Key.M, "KeyM"), (UnityEngine.InputSystem.Key.Tab, "Tab"), (UnityEngine.InputSystem.Key.I, "KeyI"),
            (UnityEngine.InputSystem.Key.Escape, "Escape"), (UnityEngine.InputSystem.Key.F, "KeyF"), (UnityEngine.InputSystem.Key.P, "KeyP"),
            (UnityEngine.InputSystem.Key.H, "KeyH"), (UnityEngine.InputSystem.Key.G, "KeyG"), (UnityEngine.InputSystem.Key.Q, "KeyQ"),
            (UnityEngine.InputSystem.Key.X, "KeyX"), (UnityEngine.InputSystem.Key.B, "KeyB"), (UnityEngine.InputSystem.Key.V, "KeyV"),
        };

        /// <summary>StoryCine.SkipKeys as keys: raised even while the controls are held.</summary>
        static readonly (Key key, string code)[] SkipKeys =
        {
            (UnityEngine.InputSystem.Key.Space, "Space"), (UnityEngine.InputSystem.Key.Enter, "Enter"),
            (UnityEngine.InputSystem.Key.NumpadEnter, "NumpadEnter"), (UnityEngine.InputSystem.Key.Escape, "Escape"),
        };

        /// <summary>The key codes raised while the controls are held (for checks).</summary>
        public static string[] SkipKeyCodes => System.Array.ConvertAll(SkipKeys, k => k.code);

        void Update()
        {
            // Point changes the story made (enabled, label) reach the scene.
            foreach (var (sp, ip) in _ipBy)
                if (ip) { ip.interactable = sp.Enabled; ip.label = sp.Label; }

            if (_levelId != null) Frame?.Invoke(Time.deltaTime);
            if (_levelId != null) PackFrame();
            // app.js onFrame: hud.setStamina(p.stamina, p.winded) every frame.
            if (overlay && player && _levelId != null) overlay.SetStamina(player.Stamina, player.Winded);

            var kb = Keyboard.current;
            if (kb != null && !InputGate.Blocked)
            {
                foreach (var (key, code) in UiKeys)
                    if (kb[key].wasPressedThisFrame) Key?.Invoke(code);
            }
            // The view toggle from other devices (the gamepad's north button): the rig leaves V to the Director.
            if (!InputGate.Blocked && _reader && _reader.ViewPressed && (kb == null || !kb[UnityEngine.InputSystem.Key.V].wasPressedThisFrame))
                Key?.Invoke("KeyV");
            else if (kb != null)
            {
                // A cutscene holds the controls, but its skip keys still count (cine.js listens on window keydown in the
                // capture phase). The Director ignores keys while the controls are off, so only StoryCine acts on these.
                foreach (var (key, code) in SkipKeys)
                    if (kb[key].wasPressedThisFrame) Key?.Invoke(code);
            }

            if (player)
            {
                bool g = player.Grounded;
                float y = player.transform.position.y;
                // Falls count only while the player is in control and has not just been moved by the story
                // (a cutscene teleport from the 3rd floor to the street is not a 6 m drop).
                if (_landReset || InputGate.Blocked) { _airTop = y; if (g) _landReset = false; }
                else if (!g) _airTop = _wasGrounded ? y : Mathf.Max(_airTop, y);
                else if (!_wasGrounded) { float drop = _airTop - y; if (drop > 0.3f) Land?.Invoke(drop); }
                _wasGrounded = g;
            }
        }

        // ---------- audio ----------
        public AudioLibrary audioLibrary;
        AudioSource _ambience;
        readonly Dictionary<string, AudioSource> _loops = new();

        public void Ambience(string name, double fadeSeconds = 1.2)
        {
            if (!_ambience) { _ambience = gameObject.AddComponent<AudioSource>(); _ambience.loop = true; _ambience.spatialBlend = 0; _ambience.volume = AmbienceLevel * AiOn.Settings.AudioBus.Ambience; }
            var clip = audioLibrary ? audioLibrary.Get("ambience_" + name) : null;
            if (_ambience.clip == clip) return;
            _ambience.clip = clip;
            if (clip) _ambience.Play(); else _ambience.Stop();
        }

        AudioSource _sfx;
        const float AmbienceLevel = 0.5f;
        readonly Dictionary<string, float> _loopLevel = new();

        /// <summary>The bus settings (환경음, 효과음) on the sources that hold a level; the master is AudioListener.volume.</summary>
        void ApplyBuses()
        {
            if (_ambience) _ambience.volume = AmbienceLevel * AiOn.Settings.AudioBus.Ambience;
            foreach (var kv in _loops) if (kv.Value && _loopLevel.TryGetValue(kv.Key, out var lv)) kv.Value.volume = lv * AiOn.Settings.AudioBus.Effects;
        }

        /// <summary>
        /// One-shot sounds. The ones the original synthesized (boom, the shell whistle and body hits, the phone's
        /// emergency and safety alerts) come from ProceduralAudio; the rest from the recorded AudioLibrary.
        /// </summary>
        public void Cue(string name, JObject args = null)
        {
            if (!_sfx) { _sfx = gameObject.AddComponent<AudioSource>(); _sfx.spatialBlend = 0; _sfx.playOnAwake = false; }
            AudioClip clip = null; float vol = 0.8f;
            switch (name)
            {
                case "boom":
                    clip = ProceduralAudio.Boom((double?)args?["distance"] ?? 1); vol = 1f;
                    break;
                case "hit":
                {
                    var which = (string)args?["name"] ?? "thud";
                    clip = which == "glass" && audioLibrary && audioLibrary.Get("glass") ? audioLibrary.Get("glass")
                        : ProceduralAudio.Hit(which, (double?)args?["seconds"] ?? 2.2);
                    break;
                }
                case "phoneAlert":
                {
                    var kind = (string)args?["kind"];
                    if (kind == "emergency" || kind == "safety") clip = ProceduralAudio.PhoneAlert(kind);
                    break;
                }
            }
            if (!clip && audioLibrary) clip = audioLibrary.Get(name);
            // Menu blips go through the UI bus (audio.js uiBus: open/close/confirm/tick/click), the rest through effects.
            if (clip) _sfx.PlayOneShot(clip, vol * (IsUiCue(name) ? AiOn.Settings.AudioBus.Ui : AiOn.Settings.AudioBus.Effects));
        }

        /// <summary>The cues that are interface sounds (audio.js routes them to uiBus, which 'UI 효과음' sets).</summary>
        public static bool IsUiCue(string name) => name == "click" || name == "open" || name == "close" || name == "confirm" || name == "tick";

        /// <summary>Loops: the siren (one minute, then it ends by itself like the original) and the fire (its level is
        /// the volume) are synthesized; the others come from the AudioLibrary.</summary>
        public void Loop(string name, bool on, JObject args = null)
        {
            if (!_loops.TryGetValue(name, out var src))
            {
                AudioClip clip = name == "siren" ? ProceduralAudio.Siren() : name == "fire" ? ProceduralAudio.FireLoop()
                    : audioLibrary ? audioLibrary.Get(name) : null;
                if (!clip) return;
                src = gameObject.AddComponent<AudioSource>(); src.clip = clip; src.loop = name != "siren"; src.spatialBlend = 0;
                _loops[name] = src; _loopLevel[name] = 0.6f;
            }
            if (name == "fire") _loopLevel[name] = Mathf.Clamp01((float?)args?["level"] ?? 1f) * 0.8f;
            if (name == "siren") _loopLevel[name] = 0.9f;
            src.volume = _loopLevel[name] * AiOn.Settings.AudioBus.Effects;
            if (on && !src.isPlaying) src.Play(); else if (!on) src.Stop();
        }

        // ---------- saving ----------
        public static string SavePath => Path.Combine(Application.persistentDataPath, "aion-save.json");

        public void Checkpoint(string key)
        {
            var g = getState?.Invoke();
            if (g == null) return;
            SaveCodec.MakeCheckpoint(g, key);
            Save();
        }

        public void Save()
        {
            var g = getState?.Invoke();
            if (g == null) return;
            var tmp = SavePath + ".tmp";
            File.WriteAllText(tmp, SaveCodec.Save(g)); // write then swap: a crash never leaves half a save
            if (File.Exists(SavePath)) File.Delete(SavePath);
            File.Move(tmp, SavePath);
        }

        // ---------- screens ----------
        public Task<string> Talk(TalkOptions options) => overlay.Talk(options);
        public Task<string> Card(CardOptions options) => overlay.Card(options);
        public Task TimeSkip(string from, string to, IReadOnlyList<string> text)
        {
            if (player) player.StandUp(); // hours or days pass: nobody is still crouching (an upgrade; the original kept it)
            return overlay.TimeSkip(from, to, text);
        }

        /// <summary>Screen handlers by original screen name (bag, shop, radio, planner, phone...). Each takes the original's
        /// option object and returns the original's result.</summary>
        public readonly Dictionary<string, Func<JObject, Task<JToken>>> Screens = new();

        public async Task<JToken> Open(string screen, JObject options)
        {
            if (Screens.TryGetValue(screen, out var open)) return await open(options ?? new JObject());
            Debug.LogWarning($"[Story] screen '{screen}' is not built yet; showing a placeholder");
            await overlay.Card(new CardOptions
            {
                Kind = "notice", Title = $"준비 중인 화면: {screen}",
                Body = new List<string> { "이 장면의 화면은 아직 옮기는 중입니다. 계속을 누르면 기본 선택으로 진행합니다." },
            });
            return null;
        }
    }
}
