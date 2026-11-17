// Port of engine/actions.js and its app.js wiring: the hands. Q or a click (with the pointer locked) punches, G picks up
// the loose thing in reach and throws what is in hand (so does Q / a click while holding one). Each level is strewn with
// loose things the first time it is entered (LooseThings.Scatter: bricks, rocks, bottles and cans in the town, bottles,
// cans and empty boxes elsewhere); they stay where they were left when you come back. Thrown ones fly under gravity,
// bounce off walls and floors (LooseThings.Hit) and knock people over. A town crowd member flinches and runs (or goes
// down: CrowdGroup.Hit), the looter fights it out (DangerLayer.HitLooter), anyone else is pushed back, may go down and
// may hit back (8-14 damage). Every hit but debris and self-defence is counted by the director (OnHit: karma, trust,
// a shout). Leaving a level with something in hand drops it where you stand (Drop, the director's DropHeld).
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
        /// <summary>The danger layer's effects (its Layer knows the looter).</summary>
        public DangerFx dangerFx;
        /// <summary>A person was hit (the director's OnHit): person (null for a crowd member), how (punch | throw | debris), knocked down.</summary>
        public Action<IStoryPerson, string, bool> OnHit;
        /// <summary>The player was hit back (the director's Hurt): damage, why.</summary>
        public Action<double, string> OnHurt;
        public Func<double> Random = () => UnityEngine.Random.value;

        sealed class Item
        {
            public string Kind;
            public Vector3 Pos, V, From;
            public Quaternion Rot;
            public bool Flying, ByPlayer;
            public Transform T;
        }

        sealed class Reaction
        {
            public double Hp = LooseThings.HitPoints;
            public float T, Push, Back;
            public Vector3 V;
            public bool Placed;
            public string Anim;
            public PersonAnimator A;
            public Transform Root;
        }

        readonly Dictionary<string, List<Item>> _byLevel = new();
        readonly Dictionary<Transform, Reaction> _reactions = new();
        readonly List<Transform> _people = new();
        readonly List<(double x, double y, double z)> _peoplePos = new();
        List<Item> _items;
        string _builtFor;
        Transform _root;
        Item _held, _near, _picking, _throwing;
        bool _attached;
        float _punchT, _busyT, _swingIn = -1, _punchIn = -1, _pickIn = -1, _throwIn = -1, _peopleAge = 99;
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
            _picking = _throwing = null; _attached = false; _pickIn = _throwIn = _punchIn = _swingIn = -1;
            _reactions.Clear();
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
            _punchT = Mathf.Max(0, _punchT - dt); _busyT = Mathf.Max(0, _busyT - dt); _peopleAge += dt;
            Timers(dt);

            var P = Player;
            bool free = P && !InputGate.Blocked && InputGate.ReleasedFrame != Time.frameCount;
            if (free)
            {
                var kb = Keyboard.current;
                var input = P.GetComponent<PlayerInputReader>();
                bool g = kb != null && kb.gKey.wasPressedThisFrame;
                bool q = (kb != null && kb.qKey.wasPressedThisFrame) || (input && input.PunchClicked);
                if (g) { if (_held != null) Toss(); else if (_near != null) Pick(_near); }
                else if (q) { if (_held != null) Toss(); else Punch(); }
            }

            for (int i = _items.Count - 1; i >= 0; i--) { var it = _items[i]; if (it.Flying && it != _held) Fly(it, dt); } // backwards: Fly may remove it
            React(dt);
            Nearest(free);
        }

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
            if (_swingIn >= 0 && (_swingIn -= dt) < 0) Sound("swing");
            if (_punchIn >= 0 && (_punchIn -= dt) < 0) Land();
            if (_pickIn >= 0 && (_pickIn -= dt) < 0 && _picking != null && _held == _picking) { _attached = true; Sound("pick"); _picking = null; }
            if (_throwIn >= 0 && (_throwIn -= dt) < 0 && _throwing != null) Release(_throwing);
        }

        void Punch()
        {
            if (_punchT > 0 || Player.Busy) return;
            _punchT = (float)LooseThings.PunchCooldown;
            Body?.Play("punch");
            _swingIn = (float)LooseThings.SwingAt; _punchIn = (float)LooseThings.PunchLands;
        }

        // the fist arrives ~0.19 s in: the closest person in front within reach
        void Land()
        {
            var P = Player; if (!P) return;
            RefreshPeople();
            var at = P.transform.position;
            float yaw = P.BodyYaw * Mathf.Deg2Rad;
            int best = LooseThings.PunchTarget(at.x, at.y, at.z, Mathf.Sin(yaw), Mathf.Cos(yaw), _peoplePos);
            if (best >= 0) Hit(_people[best], at, "punch", 1);
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

        // wind up and let go at the top of the whip (~0.3 s in), along the line of sight
        void Toss()
        {
            if (_busyT > 0 || _held == null) return;
            _busyT = (float)LooseThings.ThrowBusy;
            Body?.Play("throw");
            _throwing = _held; _throwIn = (float)LooseThings.ThrowRelease;
        }

        void Release(Item it)
        {
            _throwing = null;
            if (_held != it) return;
            _held = null; _attached = false;
            var cam = Cam;
            var dir = cam ? cam.transform.forward : Player.transform.forward;
            if (it.T) it.Pos = it.T.position;
            it.V = dir * (float)LooseThings.ThrowSpeed + Vector3.up * (float)LooseThings.ThrowLift;
            it.Flying = true; it.ByPlayer = true; it.From = Player.transform.position;
            Sound("swing");
        }

        /// <summary>actions.js drop(): what is in hand falls where you stand (before a level change, on a retry).</summary>
        public void Drop()
        {
            if (_held == null) return;
            var it = _held; _held = null; _attached = false; _picking = _throwing = null;
            if (it.T) it.Pos = it.T.position;
            it.V = Vector3.zero; it.Flying = true; it.ByPlayer = false; it.From = it.Pos;
        }

        /// <summary>actions.js spawn(): a loose thing flung from pos with velocity v (blast debris, the looter's dropped can).
        /// It flies, strikes people along its path as 'debris' and can be picked up once it lands. False when the current
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

            // people along the path flown this frame (it stopped at a wall, so nobody behind one is hit)
            if (it.V.magnitude <= 3f) return;
            RefreshPeople();
            for (int i = 0; i < _people.Count; i++)
            {
                var pp = _people[i].position;
                if (!LooseThings.Strikes(from.x, from.y, from.z, it.Pos.x, it.Pos.y, it.Pos.z, pp.x, pp.y, pp.z)) continue;
                Hit(_people[i], it.From, it.ByPlayer ? "throw" : "debris", k.Power);
                it.V *= -0.2f;
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

        void Hit(Transform root, Vector3 from, string how, double power)
        {
            if (!_reactions.TryGetValue(root, out var rec)) _reactions[root] = rec = new Reaction { Root = root, A = root.GetComponentInChildren<PersonAnimator>() };
            bool ko = LooseThings.TakeHit(ref rec.Hp, power);
            Sound(how == "punch" ? "punch" : "thud");
            var P = Player;
            var person = host ? host.PersonFor(root.gameObject) : null;
            var looter = dangerFx?.Layer?.CurrentLooter as UnityPerson;
            if (looter != null && looter.Root == root.gameObject)
            {
                person = looter;
                dangerFx.Layer.HitLooter(looter, ko); // the looter's own reaction: hurt, down, the dropped can
            }
            else if (CrowdHit(root, P ? P.transform.position : from, ko)) { } // a town crowd member flinches, runs or goes down
            else
            {
                bool crowd = root.GetComponentInParent<LevelCrowd>() != null; // shelter / mart people walk where their logic says
                var d = root.position - from; d.y = 0;
                var dir = d.sqrMagnitude > 1e-6f ? d.normalized : root.forward;
                rec.Placed = !crowd;
                rec.T = ko ? 12f : 0.45f; rec.Push = crowd ? 0 : 0.45f; rec.V = dir * 1.6f;
                rec.Back = crowd || ko ? 0 : (Random() < 0.55 ? 0.6f : 0); // shelter / mart people only flinch (crowd.js onHit)
                if (rec.A)
                {
                    if (crowd) rec.A.Play("hurt");
                    else
                    {
                        if (rec.A.Current != "hurt" && rec.A.Current != "down") rec.Anim = rec.A.Current;
                        rec.A.SetAnim(ko ? "down" : "hurt");
                    }
                }
            }
            try { OnHit?.Invoke(person, how, ko); }
            catch (Exception e) { Debug.LogException(e); }
        }

        bool CrowdHit(Transform root, Vector3 from, bool ko)
        {
            var crowd = host ? host.Crowd : null;
            if (crowd == null || !(crowd.Logic is TownCrowd town) || crowd.View == null) return false;
            foreach (var (_, group) in town.Groups)
                foreach (var a in group.Agents)
                    if (crowd.View.InstanceOf(a.Body) == root.gameObject) { group.Hit(a.I, from.x, -from.z, ko); return true; }
            return false;
        }

        // actions.js onFrame: placed people pushed back (sliding along what stands behind them), hitting back, getting up
        void React(float dt)
        {
            if (_reactions.Count == 0) return;
            var P = Player;
            foreach (var kv in _reactions)
            {
                var r = kv.Value;
                // (the original stopped at t <= 0, 0.45 s in, before the 0.6 s hit-back could come: it never did; here it does)
                if (!(r.T > 0 || r.Back > 0) || !r.Root) continue;
                bool was = r.T > 0;
                r.T -= dt;
                if (r.Push > 0)
                {
                    float k = r.Push / 0.45f; r.Push -= dt;
                    var step = r.V * (dt * k * 2f);
                    float len = step.magnitude;
                    if (len > 1e-5f)
                    {
                        var chest = r.Root.position + Vector3.up * 0.9f;
                        if (Physics.SphereCast(chest, 0.24f, step / len, out var h, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                            && !(P && h.collider.transform.IsChildOf(P.transform)) && !h.collider.transform.IsChildOf(r.Root))
                            step = Vector3.ProjectOnPlane(step, new Vector3(h.normal.x, 0, h.normal.z).normalized) * 0.8f; // slide along it
                        r.Root.position += new Vector3(step.x, 0, step.z);
                    }
                }
                if (r.Back > 0 && InputGate.Blocked) r.Back = 0; // a dialog or cutscene took over: no punch lands behind it
                if (r.Back > 0 && (r.Back -= dt) <= 0 && P)
                {
                    var to = P.transform.position - r.Root.position; to.y = 0;
                    if (to.magnitude < 1.8f)
                    {
                        if (r.A) r.A.Play("punch");
                        if (to.sqrMagnitude > 1e-4f) r.Root.rotation = Quaternion.LookRotation(to);
                        Sound("punch");
                        Body?.Play("hurt");
                        host?.Shake(0.12, 0.35);
                        try { OnHurt?.Invoke(LooseThings.HitBackDamage(Random()), "fight"); }
                        catch (Exception e) { Debug.LogException(e); }
                        r.T = Mathf.Max(r.T, 0.4f);
                    }
                }
                if (was && r.T <= 0 && r.Placed && r.A)
                    r.A.SetAnim(r.Anim != null && r.Anim != "hurt" && r.Anim != "down" && r.Anim != "punch" ? r.Anim : "idle");
            }
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
