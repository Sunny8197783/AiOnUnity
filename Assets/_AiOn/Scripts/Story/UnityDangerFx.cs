// The scene side of the danger layer (story/danger.js's three.js meshes, engine/fx.js fire + smoke, actions.spawn) in
// Unity: a flash light, a fireball and sparks (URP particles), a shock ring of dust along the ground, a soft dark crater
// decal, smoke puffs that billow up and fade, thrown debris (Rigidbody primitives), fires with their smoke columns, and
// rubble heaps that are solid. Calls come in the original frame (x east, y up, z south); LevelLayout converts.
//
// Everything lives under one "DangerFx" object in the active scene (the level), so a level change takes it all away;
// the next call builds a fresh rig. Counts are capped (craters, rubble, debris, fires, puffs) so long fights do not leak.
// Pure helpers are in DangerFxMath (tested in DangerFxTests).
using System;
using System.Collections.Generic;
using AiOn.Levels;
using AiOn.People;
using AiOn.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AiOn.Story
{
    /// <summary>The danger layer's DangerFx for the Unity host.</summary>
    public class UnityDangerFx : DangerFx
    {
        readonly UnityStoryHost _host;
        DangerFxRig _rig;

        public UnityDangerFx(UnityStoryHost host) { _host = host; }

        /// <summary>The rig of the current level (built on first use, rebuilt after a level change).</summary>
        DangerFxRig Rig()
        {
            var active = SceneManager.GetActiveScene();
            if (_rig && _rig.gameObject.scene == active) return _rig;
            var go = new GameObject("DangerFx");
            if (go.scene != active) SceneManager.MoveGameObjectToScene(go, active);
            _rig = go.AddComponent<DangerFxRig>();
            _rig.Build(_host);
            return _rig;
        }

        Transform PlayerT => _host && _host.player ? _host.player.transform : null;

        // ---------- level geometry ----------

        static readonly RaycastHit[] Hits = new RaycastHit[24];
        static readonly List<(float y, bool walk)> GroundHits = new(24); // reused: GroundAt runs every frame in a chase

        public override double? GroundAt(double x, double z)
        {
            var origin = LevelLayout.ToUnity((float)x, 400f, (float)z);
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, 900f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            GroundHits.Clear();
            for (int i = 0; i < n; i++) GroundHits.Add((Hits[i].point.y, DangerFxMath.IsWalkable(Hits[i].collider.transform)));
            var y = DangerFxMath.PickGround(GroundHits);
            return y.HasValue ? (double?)y.Value : null;
        }

        public override bool Covered(double x, double z)
        {
            var p = PlayerT;
            if (!p) return false;
            // the original samples level solids along a level line at the body's 1 m height (P.y + 1)
            var end = p.position + Vector3.up * 1f;
            var start = LevelLayout.ToUnity((float)x, end.y, (float)z);
            var delta = end - start;
            float len = delta.magnitude;
            if (len < 0.05f) return false;
            int n = Physics.RaycastNonAlloc(start, delta / len, Hits, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = Hits[i].collider;
                if (!c || c.isTrigger) continue;
                if (!DangerFxMath.IsCover(c.transform)) continue;           // walk zones and ceilings are not solids
                if (c.transform.IsChildOf(p)) continue;                     // the player's own body
                if (c.GetComponentInParent<Animator>()) continue;          // people are not cover
                var rb = c.attachedRigidbody;
                if (rb && !rb.isKinematic) continue;                       // loose debris is not cover
                return true;
            }
            return false;
        }

        // ---------- effects ----------

        public override object Blast(double x, double y, double z, double size, double crater)
        {
            var rig = Rig();
            var at = LevelLayout.ToUnity((float)x, (float)y, (float)z);
            rig.Blast(at, (float)size);
            var p = PlayerT;
            if (p && _host)
            {
                var d = new Vector2(p.position.x - at.x, p.position.z - at.z).magnitude;
                _host.Shake(DangerFxMath.BlastShake(d), 1.2); // the host skips it under reduce-motion
            }
            return rig.Crater(at, (float)crater);
        }

        /// <summary>actions.spawn: the hands take blast debris and the looter's can as loose things (they fly, strike
        /// people, can be picked up). Returns false when they cannot; the visual-only rigidbody debris stands in then.</summary>
        public Func<string, Vector3, Vector3, bool> SpawnLoose;

        public override void Debris(string kind, double x, double y, double z, double vx, double vy, double vz)
        {
            var at = LevelLayout.ToUnity((float)x, (float)y, (float)z);
            var v = DangerFxMath.ToUnityVelocity(vx, vy, vz);
            bool taken = false;
            try { taken = SpawnLoose != null && SpawnLoose(kind, at, v); }
            catch (Exception e) { Debug.LogException(e); }
            if (!taken) Rig().Debris(kind, at, v, PlayerT);
        }

        public override object Ignite(double x, double y, double z, double seconds) => Rig().Ignite(LevelLayout.ToUnity((float)x, (float)y, (float)z));

        public override void FireLevel(object fire, double flame, double smoke)
        {
            if (fire is DangerFxRig.FireHandle h) h.SetLevel((float)flame, (float)smoke);
        }

        public override void Extinguish(object fire)
        {
            if (fire is DangerFxRig.FireHandle h) h.Out();
        }

        public override object Rubble(double cx, double cz, double w, double d, double h)
        {
            var gy = GroundAt(cx, cz) ?? 0;
            return Rig().Rubble(LevelLayout.ToUnity((float)cx, (float)gy, (float)cz), (float)w, (float)d, (float)h);
        }

        public override void Clear(object mark)
        {
            if (mark is GameObject go && go) UnityEngine.Object.Destroy(go);
        }

        public override void ResetPuffs()
        {
            if (_rig) _rig.ResetPuffs();
        }

        public override void PlayerAct(string act, double seconds)
        {
            if (act != "hurt" || !_host || !_host.player) return;
            var body = _host.player.GetComponent<PlayerBody>();
            if (body && body.person && body.person.isActiveAndEnabled) body.person.Play("hurt"); // the one-shot clip_hurt
            else _host.Shake(0.15, Math.Max(0.2, seconds));                                      // first person: a jolt
        }
    }

    /// <summary>Pure parts of UnityDangerFx (original frame in, Unity out where noted).</summary>
    public static class DangerFxMath
    {
        /// <summary>Parents named like LevelBuilder's walk-zone colliders.</summary>
        public static readonly string[] WalkParents = { "Floors", "Stairs" };

        public static bool IsWalkable(Transform t)
        {
            for (var p = t; p; p = p.parent)
                if (Array.IndexOf(WalkParents, p.name) >= 0) return true;
            return false;
        }

        /// <summary>Parents of LevelBuilder colliders that are not level solids (walk zones and ceilings).</summary>
        public static readonly string[] NotCoverParents = { "Floors", "Stairs", "Ceilings" };

        /// <summary>covered() only counts level solids: true unless the collider is under Floors, Stairs or Ceilings.</summary>
        public static bool IsCover(Transform t)
        {
            for (var p = t; p; p = p.parent)
                if (Array.IndexOf(NotCoverParents, p.name) >= 0) return false;
            return true;
        }

        /// <summary>groundAt(walk, x, z, _, any=true): the highest walk-zone hit, or null when there is none.</summary>
        public static float? PickGround(IReadOnlyList<(float y, bool walk)> hits)
        {
            float? best = null;
            foreach (var (y, walk) in hits)
                if (walk && (best == null || y > best)) best = y;
            return best;
        }

        /// <summary>The blast's camera shake at distance d (engine.shake(max(0.04, 0.6 - d * 0.02))).</summary>
        public static float BlastShake(float d) => Mathf.Max(0.04f, 0.6f - d * 0.02f);

        public static Vector3 ToUnityVelocity(double vx, double vy, double vz) => new((float)vx, (float)vy, (float)-vz);

        /// <summary>flashK runs 1 -> 0 at 2 per second.</summary>
        public static float FlashDecay(float k, float dt) => Mathf.Max(0, k - dt * 2);

        /// <summary>The original's flash light, fireball and shock ring at flashK k (1 = the blast, 0 = gone).</summary>
        public static (float light, float ballScale, float ballAlpha, float ringScale, float ringAlpha) BlastCurve(float k, float size) =>
            (k * k * size, (2 + (1 - k) * 7) * size, Mathf.Min(1, k * k * 1.6f), (1 + (1 - k) * 16) * size, 0.55f * k);

        /// <summary>How many of the oldest to drop so that one more fits under max.</summary>
        public static int Evict(int count, int max) => Mathf.Max(0, count + 1 - max);

        /// <summary>
        /// The rubble heap's chunks, relative to its centre on the ground, in the original frame: bigger, higher chunks
        /// toward the middle (position, euler degrees, scale).
        /// </summary>
        public static List<(Vector3 pos, Vector3 euler, Vector3 scale)> RubbleChunks(float w, float d, float h, Func<double> random, int n = 60)
        {
            float R(float a, float b) => a + (float)random() * (b - a);
            var list = new List<(Vector3, Vector3, Vector3)>(n);
            for (int i = 0; i < n; i++)
            {
                float u = R(-1, 1), v = R(-1, 1), k = 1 - Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)), s = R(0.4f, 1.2f);
                var pos = new Vector3(u * w / 2, k * h * R(0.5f, 1), v * d / 2);
                var euler = new Vector3(R(0, 3), R(0, 3), R(0, 3)) * Mathf.Rad2Deg;
                list.Add((pos, euler, new Vector3(s * R(0.8f, 1.6f), s * 0.55f, s)));
            }
            return list;
        }

        /// <summary>Steepest side of the rubble hull (the player's CharacterController walks up to slopeLimit 50).</summary>
        public const float RubbleMaxSlopeDeg = 45f;

        /// <summary>
        /// The climbable rubble solid: a frustum over the W x D footprint from y -0.5 (sunk into the ground) up to h, its
        /// sides no steeper than RubbleMaxSlopeDeg (the top shrinks to a ridge, and is lowered if the footprint is too
        /// narrow for h). Vertices 0..3 are the base (-x-z, +x-z, +x+z, -x+z), 4..7 the top in the same order; local frame.
        /// </summary>
        public static Vector3[] RubbleHull(float w, float d, float h)
        {
            const float baseY = -0.5f;
            float tan = Mathf.Tan(RubbleMaxSlopeDeg * Mathf.Deg2Rad);
            float rise = Mathf.Max(0.01f, Mathf.Min(h - baseY, Mathf.Min(w, d) / 2 * tan));
            float run = rise / tan, topY = baseY + rise;
            float bx = w / 2, bz = d / 2, tx = Mathf.Max(0, bx - run), tz = Mathf.Max(0, bz - run);
            return new[]
            {
                new Vector3(-bx, baseY, -bz), new Vector3(bx, baseY, -bz), new Vector3(bx, baseY, bz), new Vector3(-bx, baseY, bz),
                new Vector3(-tx, topY, -tz), new Vector3(tx, topY, -tz), new Vector3(tx, topY, tz), new Vector3(-tx, topY, tz),
            };
        }

        /// <summary>The hull's 12 triangles (outward, clockwise seen from outside) over RubbleHull's vertex order.</summary>
        public static readonly int[] RubbleHullTris =
        {
            0, 1, 2, 0, 2, 3,       // bottom (faces down)
            4, 7, 6, 4, 6, 5,       // top (faces up)
            0, 4, 5, 0, 5, 1,       // -z side
            1, 5, 6, 1, 6, 2,       // +x side
            2, 6, 7, 2, 7, 3,       // +z side
            3, 7, 4, 3, 4, 0,       // -x side
        };
    }

    /// <summary>Destroys the procedural meshes of its GameObject with it (rubble heaps cleared or trimmed mid-level).</summary>
    public class DangerFxMeshOwner : MonoBehaviour
    {
        public readonly List<Mesh> meshes = new();
        void OnDestroy()
        {
            foreach (var m in meshes) if (m) Destroy(m);
            meshes.Clear();
        }
    }

    /// <summary>The level-scoped effects of the danger layer (one per level, under the active scene).</summary>
    public class DangerFxRig : MonoBehaviour
    {
        public const int MaxCraters = 20, MaxRubble = 8, MaxDebris = 48, MaxFires = 8, MaxPuffs = 160;
        public const float DebrisLife = 8f;

        public sealed class FireHandle
        {
            public GameObject Root; public FireFx Fire; public SmokeFx Smoke; public bool IsOut;
            public void SetLevel(float flame, float smoke)
            {
                if (IsOut || !Root) return;
                if (Fire) Fire.SetLevel(flame);
                if (Smoke) Smoke.SetDensity(smoke);
            }
            public void Out()
            {
                if (IsOut) return;
                IsOut = true;
                if (!Root) return;
                if (Fire) Fire.SetLevel(0);
                if (Smoke) Smoke.SetDensity(0);
                Destroy(Root, 9f); // the last smoke drifts off first
            }
        }

        sealed class Flying { public GameObject Go; public float Born; public Vector3 Scale; }

        UnityStoryHost _host;
        Light _flash;
        Transform _ring;
        Material _ringMat;
        ParticleSystem _ball, _sparks, _puffs;
        float _flashK, _size = 1;
        readonly List<GameObject> _craters = new(), _rubble = new();
        readonly List<Flying> _debris = new();
        readonly List<FireHandle> _fires = new();
        readonly List<UnityEngine.Object> _owned = new(); // meshes / materials made for this rig

        static Material _craterMat, _rubbleMat;
        static readonly Dictionary<string, Material> DebrisMats = new();
        static Mesh _ringMesh, _craterMesh;
        static readonly System.Random Rng = new();
        static float Rnd(float a, float b) => a + (float)Rng.NextDouble() * (b - a);

        /// <summary>The current level's rig (the minimap reads its rubble), or null before the first blast.</summary>
        public static DangerFxRig Current { get; private set; }

        public void Build(UnityStoryHost host)
        {
            _host = host;
            Current = this;
            // One flash light, kept in the scene (off) so it is never created mid-fight.
            _flash = new GameObject("Flash").AddComponent<Light>();
            _flash.transform.SetParent(transform, false);
            _flash.type = LightType.Point; _flash.color = new Color(1f, 0.69f, 0.44f); _flash.range = 40; _flash.shadows = LightShadows.None;
            _flash.intensity = 0; _flash.enabled = false;

            // The shock ring: dust thrown out along the ground.
            var ring = new GameObject("ShockRing");
            ring.transform.SetParent(transform, false);
            ring.AddComponent<MeshFilter>().sharedMesh = RingMesh();
            _ringMat = new Material(StoryFx.SmokeMat); _owned.Add(_ringMat);
            _ringMat.SetColor("_BaseColor", new Color(0.8f, 0.74f, 0.64f, 0));
            var rr = ring.AddComponent<MeshRenderer>(); rr.sharedMaterial = _ringMat; rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ring = ring.transform; ring.SetActive(false);

            _ball = Particles("Fireball", StoryFx.FlameMat, 64);
            {
                var col = _ball.colorOverLifetime; col.enabled = true;
                col.color = Grad(new[] { (new Color(1f, 0.9f, 0.6f), 0f), (new Color(1f, 0.55f, 0.15f), 0.35f), (new Color(0.4f, 0.12f, 0.03f), 1f) },
                                 new[] { (1f, 0f), (0.8f, 0.4f), (0f, 1f) });
                var sz = _ball.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 0.5f, 1, 1.6f));
            }
            _sparks = Particles("Sparks", StoryFx.FlameMat, 160);
            {
                var main = _sparks.main; main.gravityModifier = 1f;
                var col = _sparks.colorOverLifetime; col.enabled = true;
                col.color = Grad(new[] { (new Color(1f, 0.85f, 0.5f), 0f), (new Color(1f, 0.4f, 0.1f), 1f) }, new[] { (1f, 0f), (1f, 0.6f), (0f, 1f) });
                var r = _sparks.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.04f; r.lengthScale = 1f;
            }
            _puffs = Particles("Puffs", StoryFx.SmokeMat, MaxPuffs);
            {
                var col = _puffs.colorOverLifetime; col.enabled = true;
                col.color = Grad(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.04f), (0.55f, 0.5f), (0f, 1f) });
                // scale s0 -> s1 by sqrt(k): s0 is about a quarter of s1
                var sz = _puffs.sizeOverLifetime; sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 0.28f), new Keyframe(0.1f, 0.5f), new Keyframe(0.25f, 0.64f), new Keyframe(0.5f, 0.78f), new Keyframe(1, 1)));
                // rising slows with age (vy * (1 - 0.7k))
                var vel = _puffs.velocityOverLifetime; vel.enabled = true;
                vel.speedModifier = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0.3f));
                var rot = _puffs.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            }
        }

        ParticleSystem Particles(string name, Material mat, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            // looping with emission off: it never emits by itself and stays playing, so Emit() always simulates
            var main = ps.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = max;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.maxParticleSize = 10f; // no screen-size clamp: close smoke and fireballs keep their real size
            ps.Play();
            return ps;
        }

        static Gradient Grad((Color c, float t)[] colors, (float a, float t)[] alphas)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[colors.Length]; for (int i = 0; i < colors.Length; i++) ck[i] = new GradientColorKey(colors[i].c, colors[i].t);
            var ak = new GradientAlphaKey[alphas.Length]; for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i].a, alphas[i].t);
            g.SetKeys(ck, ak);
            return g;
        }

        // ---------- blast ----------

        public void Blast(Vector3 at, float size)
        {
            _size = Mathf.Max(0.2f, size);
            _flashK = 1;
            _flash.transform.position = at + Vector3.up * 2 * _size;
            _ring.position = at + Vector3.up * 0.15f;
            _ring.gameObject.SetActive(true);
            Animate(0);

            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < 14; i++)
            {
                ep.position = at + new Vector3(Rnd(-0.6f, 0.6f), Rnd(0.6f, 1.8f), Rnd(-0.6f, 0.6f)) * _size;
                ep.velocity = new Vector3(Rnd(-2, 2), Rnd(1, 4), Rnd(-2, 2)) * _size;
                ep.startSize = Rnd(2.5f, 4.5f) * _size; ep.startLifetime = Rnd(0.45f, 0.9f); ep.startColor = Color.white;
                ep.rotation = Rnd(0, 360);
                _ball.Emit(ep, 1);
            }
            for (int i = 0; i < 36; i++)
            {
                float a = Rnd(0, Mathf.PI * 2), v = Rnd(7, 16);
                ep.position = at + Vector3.up * 0.6f;
                ep.velocity = new Vector3(Mathf.Cos(a) * v, Rnd(3, 11), Mathf.Sin(a) * v);
                ep.startSize = Rnd(0.05f, 0.11f); ep.startLifetime = Rnd(0.5f, 1.3f); ep.startColor = Color.white; ep.rotation = 0;
                _sparks.Emit(ep, 1);
            }
            Billow(at, Mathf.RoundToInt(14 * Mathf.Min(2, _size)), new Color(0.18f, 0.165f, 0.157f), 1.5f, 8 * _size, 20);
        }

        /// <summary>billow(): n soft puffs that rise, grow and fade (original pooled sprites).</summary>
        public void Billow(Vector3 at, int n, Color color, float up, float size, float life)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                ep.position = at + new Vector3(Rnd(-1, 1), Rnd(0.3f, 2), Rnd(-1, 1));
                ep.velocity = new Vector3(Rnd(-0.8f, 0.8f), up * Rnd(0.5f, 1.2f), Rnd(-0.8f, 0.8f));
                ep.startLifetime = life * Rnd(0.6f, 1);
                ep.startSize = size * Rnd(0.7f, 1.3f);
                ep.startColor = new Color(color.r, color.g, color.b, Rnd(0.55f, 0.85f));
                ep.rotation = Rnd(0, 360);
                _puffs.Emit(ep, 1);
            }
        }

        public void ResetPuffs()
        {
            if (_puffs) _puffs.Clear(true);
            _flashK = 0; Animate(0);
        }

        void Animate(float dt)
        {
            if (dt > 0) _flashK = DangerFxMath.FlashDecay(_flashK, dt);
            var (light, _, _, ringScale, ringAlpha) = DangerFxMath.BlastCurve(_flashK, _size);
            _flash.enabled = _flashK > 0;
            _flash.intensity = 30f * light;
            _ring.localScale = new Vector3(ringScale, 1, ringScale);
            _ringMat.SetColor("_BaseColor", new Color(0.8f, 0.74f, 0.64f, ringAlpha));
            if (_flashK <= 0 && _ring.gameObject.activeSelf) _ring.gameObject.SetActive(false);
        }

        // ---------- crater ----------

        public GameObject Crater(Vector3 at, float scale)
        {
            Trim(_craters, MaxCraters);
            var go = new GameObject("Crater");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(at + Vector3.up * 0.03f, Quaternion.Euler(0, Rnd(0, 360), 0));
            go.transform.localScale = new Vector3(scale, 1, scale);
            go.AddComponent<MeshFilter>().sharedMesh = CraterMesh();
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = CraterMat(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _craters.Add(go);
            return go;
        }

        // ---------- rubble ----------

        readonly List<LevelLayout.SolidBox> _rubbleBoxes = new();

        /// <summary>The live rubble heaps' solid boxes in the original frame (for the minimap); the boxes are reused.</summary>
        public void RubbleBoxes(List<LevelLayout.SolidBox> into)
        {
            int n = 0;
            foreach (var go in _rubble)
            {
                if (!go) continue;
                var mc = go.GetComponent<MeshCollider>();
                if (!mc) continue;
                var b = mc.bounds;
                if (n == _rubbleBoxes.Count) _rubbleBoxes.Add(new LevelLayout.SolidBox { id = "rubble" });
                var s = _rubbleBoxes[n++];
                s.x0 = b.min.x; s.x1 = b.max.x; s.y0 = b.min.y; s.y1 = b.max.y; s.z0 = -b.max.z; s.z1 = -b.min.z; s.off = false;
                into.Add(s);
            }
        }

        public GameObject Rubble(Vector3 center, float w, float d, float h)
        {
            Trim(_rubble, MaxRubble);
            var go = new GameObject("Rubble");
            go.transform.SetParent(transform, false);
            go.transform.position = center;
            var owner = go.AddComponent<DangerFxMeshOwner>(); // the meshes die with the heap (Clear, Trim or level unload)
            var mesh = HeapMesh(w, d, h); owner.meshes.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = RubbleMat();
            // the solid: a frustum with walkable sides, so the heap can be climbed over (Danger.cs) rather than a wall
            var hull = new Mesh { name = "RubbleHull" };
            hull.SetVertices(DangerFxMath.RubbleHull(w, d, h)); hull.SetTriangles(DangerFxMath.RubbleHullTris, 0);
            hull.RecalculateNormals(); hull.RecalculateBounds();
            owner.meshes.Add(hull);
            var mc = go.AddComponent<MeshCollider>(); mc.convex = true; mc.sharedMesh = hull;
            _rubble.Add(go);
            Billow(center, 12, new Color(0.70f, 0.66f, 0.60f), 1.2f, 9, 12); // the dust cloud
            return go;
        }

        static Mesh HeapMesh(float w, float d, float h)
        {
            var chunks = DangerFxMath.RubbleChunks(w, d, h, Rng.NextDouble);
            var verts = new List<Vector3>(chunks.Count * 24); var normals = new List<Vector3>(chunks.Count * 24); var tris = new List<int>(chunks.Count * 36);
            foreach (var (pos, euler, scale) in chunks)
            {
                var m = Matrix4x4.TRS(LevelLayout.ToUnity(pos.x, pos.y, pos.z), Quaternion.Euler(euler), scale);
                AddBox(m, verts, normals, tris);
            }
            var mesh = new Mesh { name = "RubbleHeap" };
            mesh.SetVertices(verts); mesh.SetNormals(normals); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static readonly Vector3[] Faces = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        static void AddBox(Matrix4x4 m, List<Vector3> verts, List<Vector3> normals, List<int> tris)
        {
            foreach (var n in Faces)
            {
                // two axes spanning the face, ordered so the winding faces out (Unity: clockwise front faces)
                var a = n.x != 0 ? Vector3.up : Vector3.right;
                var b = Vector3.Cross(n, a);
                int i0 = verts.Count;
                var c = n * 0.5f;
                foreach (var (sa, sb) in new[] { (-1f, -1f), (-1f, 1f), (1f, 1f), (1f, -1f) })
                    verts.Add(m.MultiplyPoint3x4(c + (a * sa + b * sb) * 0.5f));
                var wn = m.MultiplyVector(n).normalized;
                for (int k = 0; k < 4; k++) normals.Add(wn);
                // orient by the real normal so non-uniform scale or a flipped basis cannot turn a face inside out
                var e1 = verts[i0 + 1] - verts[i0]; var e2 = verts[i0 + 2] - verts[i0];
                if (Vector3.Dot(Vector3.Cross(e1, e2), wn) >= 0) tris.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
                else tris.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
            }
        }

        // ---------- debris ----------

        public void Debris(string kind, Vector3 at, Vector3 velocity, Transform player)
        {
            while (_debris.Count >= MaxDebris) { var old = _debris[0]; _debris.RemoveAt(0); if (old.Go) Destroy(old.Go); }
            GameObject go;
            Vector3 scale;
            switch (kind)
            {
                case "brick": go = GameObject.CreatePrimitive(PrimitiveType.Cube); scale = new Vector3(0.21f, 0.065f, 0.1f); break;
                case "can": go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); scale = new Vector3(0.066f, 0.06f, 0.066f); break;
                default: go = GameObject.CreatePrimitive(PrimitiveType.Sphere); scale = new Vector3(0.14f, 0.12f, 0.13f); kind = "rock"; break;
            }
            go.name = "Debris_" + kind;
            go.layer = 2; // Ignore Raycast: never ground, never cover
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(at, UnityEngine.Random.rotation);
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = DebrisMat(kind);
            var col = go.GetComponent<Collider>();
            if (player) foreach (var pc in player.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(col, pc); // no stepping on pebbles
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = kind == "brick" ? 2f : kind == "can" ? 0.1f : 0.6f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = velocity;
            rb.angularVelocity = new Vector3(Rnd(-9, 9), Rnd(-6, 6), Rnd(-9, 9));
            _debris.Add(new Flying { Go = go, Born = Time.time, Scale = scale });
        }

        // ---------- fire ----------

        public FireHandle Ignite(Vector3 at)
        {
            _fires.RemoveAll(f => f.IsOut || !f.Root);
            while (_fires.Count >= MaxFires) { _fires[0].Out(); _fires.RemoveAt(0); }
            var root = new GameObject("BlastFire");
            root.transform.SetParent(transform, false);
            root.transform.position = at;
            var fire = StoryFx.CreateFire(root.transform, "Fire", at, 1.6f);
            // the original's street fires have no light of their own (light: false): only the blast flash lights up
            var fl = fire.transform.Find("FireLight"); if (fl) Destroy(fl.gameObject);
            // fx.smoke({ceiling: 22, spread: 7, radius: 0.9}): a plume rising out of the fire, widening as it goes
            var smoke = StoryFx.CreateSmoke(root.transform, "SmokeColumn", at + Vector3.up * 1.2f, new Vector3(1.8f, 0.5f, 1.8f), new Color(0.165f, 0.153f, 0.145f));
            var sps = smoke.GetComponent<ParticleSystem>();
            if (sps)
            {
                var sm = sps.main; sm.startSpeed = 0f;
                var vel = sps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                vel.y = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
                vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                var sz = sps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 4));
                var sr = sps.GetComponent<ParticleSystemRenderer>(); if (sr) sr.maxParticleSize = 10f;
            }
            var h = new FireHandle { Root = root, Fire = fire, Smoke = smoke };
            h.SetLevel(1, 1);
            _fires.Add(h);
            return h;
        }

        // ---------- frame ----------

        void Update()
        {
            float dt = Time.deltaTime;
            if (_flashK > 0) Animate(dt);
            float now = Time.time;
            for (int i = _debris.Count - 1; i >= 0; i--)
            {
                var f = _debris[i];
                if (!f.Go) { _debris.RemoveAt(i); continue; }
                float age = now - f.Born;
                if (age > DebrisLife) { Destroy(f.Go); _debris.RemoveAt(i); continue; }
                if (age > DebrisLife - 0.5f) f.Go.transform.localScale = f.Scale * Mathf.Clamp01((DebrisLife - age) / 0.5f);
            }
        }

        static void Trim(List<GameObject> list, int max)
        {
            list.RemoveAll(g => !g);
            for (int n = DangerFxMath.Evict(list.Count, max); n > 0; n--) { Destroy(list[0]); list.RemoveAt(0); }
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            foreach (var o in _owned) if (o) Destroy(o);
            _owned.Clear();
        }

        // ---------- shared meshes and materials ----------

        /// <summary>A flat annulus 0.7..1 on the ground, soft at both edges (vertex alpha).</summary>
        static Mesh RingMesh()
        {
            if (_ringMesh) return _ringMesh;
            const int seg = 48;
            float[] radii = { 0.7f, 0.85f, 1f }; float[] alpha = { 0, 1, 0 };
            var v = new List<Vector3>(); var c = new List<Color>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg;
                for (int r = 0; r < 3; r++) { v.Add(new Vector3(Mathf.Cos(a) * radii[r], 0, Mathf.Sin(a) * radii[r])); c.Add(new Color(1, 1, 1, alpha[r])); uv.Add(new Vector2(0.5f, 0.5f)); }
            }
            for (int i = 0; i < seg; i++)
                for (int r = 0; r < 2; r++)
                {
                    int a0 = i * 3 + r, a1 = a0 + 1, b0 = a0 + 3, b1 = b0 + 1;
                    t.AddRange(new[] { a0, a1, b1, a0, b1, b0, a0, b1, a1, a0, b0, b1 }); // both sides
                }
            _ringMesh = new Mesh { name = "ShockRing" };
            _ringMesh.SetVertices(v); _ringMesh.SetColors(c); _ringMesh.SetUVs(0, uv); _ringMesh.SetTriangles(t, 0); _ringMesh.RecalculateBounds();
            return _ringMesh;
        }

        /// <summary>A ragged dark disc of radius ~1: solid in the middle, fading out at the edge.</summary>
        static Mesh CraterMesh()
        {
            if (_craterMesh) return _craterMesh;
            const int seg = 28;
            var rng = new System.Random(7);
            var v = new List<Vector3> { Vector3.zero }; var c = new List<Color> { Color.white }; var uv = new List<Vector2> { new(0.5f, 0.5f) }; var t = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg, rr = 0.85f + (float)rng.NextDouble() * 0.3f;
                v.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * rr * 0.85f); c.Add(Color.white); uv.Add(new Vector2(0.5f, 0.5f)); // solid out to 85%
                v.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * rr); c.Add(new Color(1, 1, 1, 0)); uv.Add(new Vector2(0.5f, 0.5f));
            }
            for (int i = 0; i < seg; i++)
            {
                int inner = 1 + i * 2, outer = inner + 1, nInner = 1 + ((i + 1) % seg) * 2, nOuter = nInner + 1;
                t.AddRange(new[] { 0, nInner, inner });                         // up-facing (clockwise seen from above)
                t.AddRange(new[] { inner, nInner, nOuter, inner, nOuter, outer });
            }
            _craterMesh = new Mesh { name = "Crater" };
            _craterMesh.SetVertices(v); _craterMesh.SetColors(c); _craterMesh.SetUVs(0, uv); _craterMesh.SetTriangles(t, 0);
            _craterMesh.RecalculateNormals(); _craterMesh.RecalculateBounds();
            return _craterMesh;
        }

        static Material CraterMat()
        {
            if (_craterMat) return _craterMat;
            _craterMat = new Material(StoryFx.SmokeMat) { name = "Crater", renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 10 };
            _craterMat.SetColor("_BaseColor", new Color(0.043f, 0.039f, 0.035f, 0.75f));
            return _craterMat;
        }

        static Material Lit(string name, Color color, float smoothness)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        static Material RubbleMat() => _rubbleMat ? _rubbleMat : _rubbleMat = Lit("Rubble", new Color(0.553f, 0.529f, 0.502f), 0.05f);

        static Material DebrisMat(string kind)
        {
            if (DebrisMats.TryGetValue(kind, out var m) && m) return m;
            m = kind switch
            {
                "brick" => Lit("Brick", new Color(0.604f, 0.290f, 0.196f), 0.05f),
                "can" => Lit("Can", new Color(0.722f, 0.753f, 0.784f), 0.65f),
                _ => Lit("Rock", new Color(0.467f, 0.451f, 0.424f), 0f),
            };
            DebrisMats[kind] = m;
            return m;
        }
    }
}
