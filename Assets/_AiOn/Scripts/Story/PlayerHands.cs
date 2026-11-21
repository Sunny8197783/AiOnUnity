// Port of engine/actions.js and its app.js wiring: the hands. G picks up the loose thing in reach and puts down what is
// in hand (so does Q). Each level is strewn with loose things the first time it is entered (LooseThings.Scatter: bricks,
// rocks, bottles and cans in the town, bottles, cans and empty boxes elsewhere); they stay where they were left when you
// come back. Loose things fly under gravity and bounce off walls and floors (LooseThings.Hit).
// Competition upgrade (non-violent): the original punched (Q / a click) and threw things that knocked people over, and
// people hit back. Here nothing ever targets a person: there is no punch, what is in hand is set down a short way in
// front, and a flying thing (blast debris included) stops at a person instead of striking them. Leaving a level with
// something in hand drops it where you stand (Drop, the director's DropHeld).
// Positions are Unity's here; the pure rules (original frame) are in LooseThings.
using System;
using System.Collections.Generic;
using AiOn.Levels;
using AiOn.Levels.Crowd;
using AiOn.People;
using AiOn.Player;
using AiOn.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace AiOn.Story
{
    [DefaultExecutionOrder(1000)] // LateUpdate (the held thing) after PlayerCameraRig placed the camera and PlayerBody turned the body
    public class PlayerHands : MonoBehaviour
    {
        public UnityStoryHost host;
        /// <summary>The danger layer's effects (blast debris arrives through it as loose things).</summary>
        public DangerFx dangerFx;
        public Func<double> Random = () => UnityEngine.Random.value;

        /// <summary>How fast a thing set down leaves the hand (m/s, forward) and its small lift: a gentle place, never a throw.</summary>
        public const float PlaceSpeed = 1.6f, PlaceLift = 0.6f;

        sealed class Item
        {
            public string Kind;
            public Vector3 Pos, V, From;
            public Quaternion Rot;
            public bool Flying, ByPlayer;
            public Transform T;
        }

        readonly Dictionary<string, List<Item>> _byLevel = new();
        readonly List<Transform> _people = new();
        readonly List<(double x, double y, double z)> _peoplePos = new();
        List<Item> _items;
        string _builtFor;
        Transform _root;
        Item _held, _near, _picking, _throwing;
        bool _attached;
        float _busyT, _pickIn = -1, _throwIn = -1, _peopleAge = 99;
        string _prompt;
        GameHud _promptHud;

        static readonly RaycastHit[] Hits = new RaycastHit[12];
        static readonly Dictionary<string, Mesh> Meshes = new();
        static readonly Dictionary<string, Material> Mats = new();

        PlayerController Player => host ? host.player : null;
        PlayerBody Body => Player ? Player.GetComponent<PlayerBody>() : null;
        Camera Cam => host && host.cameraRig ? host.cameraRig.cam : null;

        /// <summary>What is in hand (its Korean name), or null.</summary>
        public string Held => _held != null ? LooseThings.Kinds[_held.Kind].Name : null;
        /// <summary>The loose things of the current level (for checks).</summary>
        public int ItemCount => _items?.Count ?? 0;

        // ---------------------------------------------------------------- level

        void EnsureLevel()
        {
            var id = host ? host.LevelId : null;
            if (id == null) return;
            if (_builtFor == id && _root) return;
            var active = SceneManager.GetActiveScene();
            if (active.name != UnityStoryHost.SceneOf(id)) return; // still loading
            if (_held != null) { _held.Flying = true; _held.V = Vector3.zero; _held = null; } // never dropped: it stays behind
            _picking = _throwing = null; _attached = false; _pickIn = _throwIn = -1;
            _builtFor = id;
            if (!_byLevel.TryGetValue(id, out _items)) _byLevel[id] = _items = Scatter(id);
            var go = new GameObject("LooseThings");
            if (go.scene != active) SceneManager.MoveGameObjectToScene(go, active);
            _root = go.transform;
            foreach (var it in _items) it.T = MakeMesh(it.Kind, _root, it.Pos, it.Rot);
        }

        List<Item> Scatter(string id)
        {
            var list = new List<Item>();
            var layout = host.LayoutOf(id);
            if (!layout) return list;
            var walk = new List<VaultPlanner.Walk>();
            foreach (var w in layout.walk) if (w != null) walk.Add(VaultPlanner.WalkOf(w));
            var solids = new List<VaultPlanner.Box>();
            foreach (var s in layout.solids) if (s != null && !s.off && !s.IsEmpty) solids.Add(VaultPlanner.BoxOf(s));
            foreach (var p in LooseThings.Scatter(id, walk, solids))
                list.Add(new Item { Kind = p.Kind, Pos = LevelLayout.ToUnity((float)p.X, (float)p.Y, (float)p.Z), Rot = Quaternion.Euler(0, LevelLayout.ToUnityYaw((float)p.Yaw), 0) });
            return list;
        }

        static Transform MakeMesh(string kind, Transform parent, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject("Loose_" + kind);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = ScaleOf(kind);
            go.AddComponent<MeshFilter>().sharedMesh = MeshOf(kind);
            go.AddComponent<MeshRenderer>().sharedMaterial = MatOf(kind);
            return go.transform;
        }

        /// <summary>actions.js KINDS sizes on Unity's unit primitives (cube 1 m, cylinder 1 m wide and 2 m tall, sphere 1 m).</summary>
        static Vector3 ScaleOf(string kind) => kind switch
        {
            "brick" => new Vector3(0.21f, 0.065f, 0.1f),
            "rock" => new Vector3(0.15f, 0.12f, 0.14f),
            "bottle" => new Vector3(0.078f, 0.13f, 0.078f),
            "can" => new Vector3(0.066f, 0.06f, 0.066f),
            _ => new Vector3(0.3f, 0.22f, 0.24f),
        };

        static Mesh MeshOf(string kind)
        {
            var prim = kind == "rock" ? PrimitiveType.Sphere : kind == "bottle" || kind == "can" ? PrimitiveType.Cylinder : PrimitiveType.Cube;
            var key = prim.ToString();
            if (Meshes.TryGetValue(key, out var m) && m) return m;
            var tmp = GameObject.CreatePrimitive(prim);
            tmp.SetActive(false); // its collider never takes part
            m = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            Meshes[key] = m;
            return m;
        }

        static Material MatOf(string kind)
        {
            if (Mats.TryGetValue(kind, out var m) && m) return m;
            var k = LooseThings.Kinds[kind];
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Loose_" + kind };
            m.SetColor("_BaseColor", new Color(((k.Color >> 16) & 255) / 255f, ((k.Color >> 8) & 255) / 255f, (k.Color & 255) / 255f));
            m.SetFloat("_Smoothness", 1f - (float)k.Rough);
            if (kind == "can") m.SetFloat("_Metallic", 0.6f);
            Mats[kind] = m;
            return m;
        }

        // ---------------------------------------------------------------- input

        void Update()
        {
            float dt = Time.deltaTime;
            EnsureLevel();
            if (_items == null || !_root) return;
            _busyT = Mathf.Max(0, _busyT - dt); _peopleAge += dt;
            Timers(dt);

            var P = Player;
            bool free = P && !InputGate.Blocked && InputGate.ReleasedFrame != Time.frameCount && !SuppressKeys && !OtherBody;
            if (free)
            {
                var kb = Keyboard.current;
                bool g = kb != null && kb.gKey.wasPressedThisFrame;
                bool q = kb != null && kb.qKey.wasPressedThisFrame;
                if (g) { if (_held != null) Toss(); else if (_near != null) Pick(_near); }
                else if (q && _held != null) Toss(); // Q only sets down what is in hand: there is no punch
            }

            for (int i = _items.Count - 1; i >= 0; i--) { var it = _items[i]; if (it.Flying && it != _held) Fly(it, dt); } // backwards: Fly may remove it
            Nearest(free);
        }

        /// <summary>Both hands are on something else (the 3D extinguisher): G and Q and the pickup prompt wait; dropped things still fall.</summary>
        public bool SuppressKeys;
        /// <summary>Someone else is the player (서연, 304호 어르신; HostSettings from ExperienceProfile.Hands): no pickups, no prompt.</summary>
        public bool OtherBody;

        void LateUpdate()
        {
            if (_held == null || !_attached || _held.T == null) return;
            var body = Body;
            var hand = body && body.person && body.person.isActiveAndEnabled ? body.RightHand : null;
            Vector3 pos; Quaternion rot;
            if (hand) { pos = hand.position - Vector3.up * 0.06f; rot = hand.rotation; }
            else if (Cam) { var c = Cam.transform; pos = c.TransformPoint(new Vector3(0.34f, -0.34f, 0.62f)); rot = c.rotation * Quaternion.Euler(12f, 0, -18f); } // first person: low in the bottom right
            else return;
            _held.T.SetPositionAndRotation(pos, rot);
            _held.Pos = pos; _held.Rot = rot;
        }

        void Timers(float dt)
        {
            if (_pickIn >= 0 && (_pickIn -= dt) < 0 && _picking != null && _held == _picking) { _attached = true; Sound("pick"); _picking = null; }
            if (_throwIn >= 0 && (_throwIn -= dt) < 0 && _throwing != null) Release(_throwing);
        }

        // bend down for it; the hand closes on it ~0.4 s in and it comes up held
        void Pick(Item it)
        {
            if (_busyT > 0 || Player.Busy) return;
            _held = it; it.Flying = false; _busyT = (float)LooseThings.PickBusy;
            _picking = it; _attached = false; _pickIn = (float)LooseThings.PickClose;
            Body?.Play("pickup");
            SetPrompt(null);
        }

        // bend down and set it on the ground a little in front (the pickup motion, ~0.4 s in)
        void Toss()
        {
            if (_busyT > 0 || _held == null) return;
            _busyT = (float)LooseThings.PickBusy;
            Body?.Play("pickup");
            _throwing = _held; _throwIn = (float)LooseThings.PickClose;
        }

        void Release(Item it)
        {
            _throwing = null;
            if (_held != it) return;
            _held = null; _attached = false;
            var dir = Player.transform.forward; dir.y = 0;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            if (it.T) it.Pos = it.T.position;
            it.V = dir * PlaceSpeed + Vector3.up * PlaceLift; // a gentle place in front, never a throw
            it.Flying = true; it.ByPlayer = true; it.From = Player.transform.position;
        }

        /// <summary>actions.js drop(): what is in hand falls where you stand (before a level change, on a retry).</summary>
        public void Drop()
        {
            if (_held == null) return;
            var it = _held; _held = null; _attached = false; _picking = _throwing = null;
            if (it.T) it.Pos = it.T.position;
            it.V = Vector3.zero; it.Flying = true; it.ByPlayer = false; it.From = it.Pos;
        }

        /// <summary>actions.js spawn(): a loose thing flung from pos with velocity v (blast debris).
        /// It flies, stops at anyone in its path (never striking them) and can be picked up once it lands. False when the current
        /// level's things are not built yet (the caller then shows its visual-only debris instead).</summary>
        public bool Spawn(string kind, Vector3 pos, Vector3 v)
        {
            if (kind == null || !LooseThings.Kinds.ContainsKey(kind)) return false;
            EnsureLevel();
            if (_items == null || !_root || _builtFor != (host ? host.LevelId : null)) return false;
            var rot = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);
            _items.Add(new Item { Kind = kind, Pos = pos, Rot = rot, V = v, Flying = true, ByPlayer = false, From = pos, T = MakeMesh(kind, _root, pos, rot) });
            return true;
        }

        // ---------------------------------------------------------------- flight

        void Fly(Item it, float dt)
        {
            var k = LooseThings.Kinds[it.Kind];
            float r = (float)k.R;
            it.V.y -= (float)LooseThings.Gravity * dt;
            var from = it.Pos;
            var step = it.V * dt;
            float len = step.magnitude;
            bool hit = false; RaycastHit h = default;
            if (len > 1e-5f)
            {
                int n = Physics.SphereCastNonAlloc(from, r, step / len, Hits, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float bd = float.MaxValue;
                var P = Player;
                for (int i = 0; i < n; i++)
                {
                    var c = Hits[i].collider;
                    if (!c || Hits[i].distance <= 0f) continue;
                    if (P && c.transform.IsChildOf(P.transform)) continue;
                    if (Hits[i].distance < bd) { bd = Hits[i].distance; h = Hits[i]; hit = true; }
                }
            }
            if (hit)
            {
                it.Pos = from + step / len * Mathf.Max(0, h.distance - 0.002f);
                double vx = it.V.x, vy = it.V.y, vz = it.V.z;
                var b = LooseThings.Hit(ref vx, ref vy, ref vz, h.normal.x, h.normal.y, h.normal.z, it.Kind, out bool rest);
                it.V = new Vector3((float)vx, (float)vy, (float)vz);
                if (b != LooseThings.Bounce.None) Sound(b == LooseThings.Bounce.Glass ? "glass" : "thud");
                if (rest)
                {
                    it.Flying = false;
                    if (h.normal.y > 0.6f) it.Pos = new Vector3(it.Pos.x, h.point.y + r, it.Pos.z);
                    float yaw = it.Rot.eulerAngles.y;
                    it.Rot = Quaternion.Euler(it.Kind == "bottle" || it.Kind == "can" ? 90 : 0, yaw, 0); // lying on its side
                }
            }
            else it.Pos = from + step;
            if (it.Flying) it.Rot *= Quaternion.Euler(dt * 9f * Mathf.Rad2Deg, 0, dt * 5f * Mathf.Rad2Deg);
            if (it.T) it.T.SetPositionAndRotation(it.Pos, it.Rot);

            // fell out of the world: gone
            var sp = LevelSpawnPoints.Current;
            if (sp != null && it.Pos.y < sp.killY) { it.Flying = false; it.V = Vector3.zero; if (it.T) it.T.gameObject.SetActive(false); _items.Remove(it); return; }

            // a person in the path flown this frame: it stops short and drops (nobody is ever struck)
            if (it.V.magnitude <= 3f) return;
            RefreshPeople();
            for (int i = 0; i < _people.Count; i++)
            {
                var pp = _people[i].position;
                if (!LooseThings.Strikes(from.x, from.y, from.z, it.Pos.x, it.Pos.y, it.Pos.z, pp.x, pp.y, pp.z)) continue;
                it.Pos = from; it.V = new Vector3(0, Mathf.Min(0, it.V.y), 0);
                if (it.T) it.T.SetPositionAndRotation(it.Pos, it.Rot);
                break;
            }
        }

        // ---------------------------------------------------------------- people

        /// <summary>The people in the scene the hands can reach: shown, not the player's own body (refreshed 4x a second).</summary>
        void RefreshPeople()
        {
            if (_peopleAge < 0.25f) { UpdatePeoplePos(); return; }
            _peopleAge = 0;
            _people.Clear();
            var P = Player;
            foreach (var a in FindObjectsByType<PersonAnimator>(FindObjectsSortMode.None))
            {
                if (!a || !a.isActiveAndEnabled) continue;
                if (P && a.transform.IsChildOf(P.transform)) continue;
                var root = a.transform.parent ? a.transform.parent : a.transform;
                if (!_people.Contains(root)) _people.Add(root);
            }
            UpdatePeoplePos();
        }

        void UpdatePeoplePos()
        {
            _peoplePos.Clear();
            for (int i = _people.Count - 1; i >= 0; i--) if (!_people[i] || !_people[i].gameObject.activeInHierarchy) _people.RemoveAt(i);
            foreach (var t in _people) { var p = t.position; _peoplePos.Add((p.x, p.y, p.z)); }
        }

        // ---------------------------------------------------------------- the G prompt

        void Nearest(bool free)
        {
            Item best = null;
            var P = Player;
            if (_held == null && free && P && !P.Busy)
            {
                float bd = (float)LooseThings.PickRadius;
                var at = P.transform.position;
                foreach (var it in _items)
                {
                    if (it.Flying || it.T == null) continue;
                    float dx = it.Pos.x - at.x, dz = it.Pos.z - at.z, d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < bd && Mathf.Abs(it.Pos.y - at.y) < LooseThings.PickDy) { bd = d; best = it; }
                }
            }
            if (best != _near) _near = best;
            SetPrompt(_near != null ? LooseThings.PickLabel(_near.Kind) : null); // every frame: cheap unless the label or the HUD changed
            var hudNow = GameHud.Current;
            if (hudNow) hudNow.ItemTarget = _near != null ? _near.T : null; // outlined and bracketed like a point
        }

        void SetPrompt(string label)
        {
            var hud = GameHud.Current;
            if (_prompt == label && hud == _promptHud) return; // a level load swaps the HUD: write again to the one that stays
            _prompt = label; _promptHud = hud;
            if (hud) hud.ItemPrompt = label;
        }

        void Sound(string name)
        {
            if (host) host.Cue("hit", new Newtonsoft.Json.Linq.JObject { ["name"] = name });
        }

        void OnDisable() => SetPrompt(null);
    }
}
