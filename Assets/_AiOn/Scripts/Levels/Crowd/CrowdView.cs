using System;
using System.Collections.Generic;
using AiOn.People;
using UnityEngine;
using UnityEngine.Rendering;

namespace AiOn.Levels.Crowd
{
    /// <summary>
    /// Draws a crowd's bodies with pooled baked people: one instance per body, made the first time the body shows and
    /// kept (hidden with SetActive) after, under one root in the level scene, so the scene unload frees them all.
    /// Positions, yaw, animation (only when it changes), props and shadow distance are synced each frame from cached
    /// components; nothing goes through UnityPerson. Each instance carries a kinematic capsule on layer 2
    /// (Ignore Raycast) so the player bumps into people while the camera and prompts look through them, as the
    /// original pushes the player out of crowd members (engine/bodies.js: r 0.24, sitting 0.32, none when down).
    /// Instances are made behind the level fade (<see cref="Prewarm"/>); later ones at most
    /// <see cref="NewPerFrame"/> a frame, so a crowd switch mid-level fills in over a few frames instead of one hitch.
    /// Each starts its animation cycle at its own seed phase (people.js), so crowds do not move in lockstep.
    /// The original's every-3rd-frame animation LOD is not ported: Unity's animator culling covers off-screen people.
    /// </summary>
    public sealed class CrowdView
    {
        sealed class Slot
        {
            public GameObject Go;
            public Transform T;
            public PersonAnimator Anim;
            public Renderer[] Renderers;
            public ShadowCastingMode[] BaseShadow;
            public CapsuleCollider Cap;
            public PropMask Have;
            public string AnimName;
            public float Speed = -1;
            public bool Shadow = true, Active;
        }

        readonly Transform _root;
        readonly Func<string, GameObject> _prefabFor;
        readonly Dictionary<CrowdBody, Slot> _slots = new();

        public CrowdView(Transform root, Func<string, GameObject> prefabFor) { _root = root; _prefabFor = prefabFor; }

        public const int NewPerFrame = 4;
        int _made;

        public int Instances => _slots.Count;

        public static string PrefabName(CrowdBody b) => b.Preset == "crowd" ? "crowd" + (1 + (uint)b.Seed % 6) : b.Preset;

        /// <summary>The instance drawing a body (null until it first showed).</summary>
        public GameObject InstanceOf(CrowdBody b) => _slots.TryGetValue(b, out var s) ? s.Go : null;

        /// <summary>Makes instances for these bodies now (behind the level fade) instead of when they first show.</summary>
        public void Prewarm(IReadOnlyList<CrowdBody> bodies)
        {
            foreach (var b in bodies) Get(b, true);
        }

        /// <summary>The seed phase of people.js (frac(seed * 0.618034)).</summary>
        public static float PhaseOf(int seed)
        {
            double o = (seed * 0.618034) % 1.0;
            return (float)(o < 0 ? o + 1 : o);
        }

        Slot Get(CrowdBody b, bool force = false)
        {
            if (_slots.TryGetValue(b, out var s)) return s;
            if (!force && _made >= NewPerFrame) return null;
            _made++;
            var prefab = _prefabFor(PrefabName(b)) ?? _prefabFor("crowd1");
            s = new Slot();
            _slots[b] = s;
            if (!prefab) return s;
            var go = UnityEngine.Object.Instantiate(prefab, _root);
            if (force) go.SetActive(false); // prewarmed: hidden until its body shows
            go.name = $"Crowd_{b.Seed}";
            go.layer = 2; // Ignore Raycast: the camera boom and prompts look through people
            var cap = go.AddComponent<CapsuleCollider>();
            cap.radius = 0.24f; cap.height = 1.7f; cap.center = new Vector3(0, 0.85f, 0);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.None;
            s.Go = go; s.T = go.transform; s.Cap = cap;
            s.Anim = go.GetComponentInChildren<PersonAnimator>(true);
            if (s.Anim) s.Anim.phase = PhaseOf(b.Seed);
            RefreshRenderers(s);
            s.Active = go.activeSelf;
            return s;
        }

        /// <summary>Takes in renderers added since (built props) and puts each at its own shadow mode, or off when culled.</summary>
        static void RefreshRenderers(Slot s)
        {
            var rs = s.Go.GetComponentsInChildren<Renderer>(true);
            var bs = new ShadowCastingMode[rs.Length];
            for (int i = 0; i < rs.Length; i++)
            {
                int k = s.Renderers != null ? Array.IndexOf(s.Renderers, rs[i]) : -1;
                bs[i] = k >= 0 ? s.BaseShadow[k] : rs[i].shadowCastingMode;
                rs[i].shadowCastingMode = s.Shadow ? bs[i] : ShadowCastingMode.Off;
            }
            s.Renderers = rs; s.BaseShadow = bs;
        }

        /// <summary>Brings every instance to its body. viewer: camera position in Unity space, for the shadow distance.</summary>
        public void Sync(IReadOnlyList<CrowdBody> bodies, Vector3 viewer, float shadowM)
        {
            float s2 = shadowM * shadowM;
            _made = 0;
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (!b.Visible)
                {
                    if (_slots.TryGetValue(b, out var hs) && hs.Go && hs.Active) { hs.Go.SetActive(false); hs.Active = false; }
                    continue;
                }
                var s = Get(b);
                if (s == null || !s.Go) continue;
                var pos = new Vector3((float)b.X, (float)b.Y, (float)-b.Z);
                s.T.SetPositionAndRotation(pos, Quaternion.Euler(0, (float)(-b.Yaw * Mathf.Rad2Deg), 0));
                if (!s.Active) { s.Go.SetActive(true); s.Active = true; s.AnimName = null; b.Snap = true; } // OnEnable restarts the animator
                if (s.Anim)
                {
                    bool gait = PersonAnimator.TakesSpeed(b.Anim); // the gaits and carry play at the body's speed
                    float sp = gait ? (float)b.Speed : 0;
                    if (b.Anim != s.AnimName || (gait && Mathf.Abs(sp - s.Speed) > 0.05f) || b.Snap)
                    {
                        s.Anim.SetAnim(b.Anim, gait && sp > 0.01f ? sp : (float?)null, b.Snap);
                        s.AnimName = b.Anim; s.Speed = sp;
                    }
                }
                if (s.Cap)
                {
                    bool down = b.Anim == "down";
                    if (s.Cap.enabled == down) s.Cap.enabled = !down;
                    float r = b.Anim == "sit" ? 0.32f : 0.24f;
                    if (s.Cap.radius != r) s.Cap.radius = r;
                }
                b.Snap = false;
                if (s.Anim)
                {
                    var hold = (b.Props & PropMask.Cart) != 0 ? PersonClips.ArmsCart : null; // hands on the cart handle
                    if (!ReferenceEquals(s.Anim.HoldPose, hold)) s.Anim.HoldPose = hold;
                }
                var want = b.Props | PersonProps.Auto(b.Anim);
                if (want != s.Have && PersonProps.SetMask(s.Go, want, ref s.Have)) RefreshRenderers(s);
                bool sh = (pos - viewer).sqrMagnitude < s2;
                if (sh != s.Shadow)
                {
                    s.Shadow = sh;
                    for (int k = 0; k < s.Renderers.Length; k++)
                        if (s.Renderers[k]) s.Renderers[k].shadowCastingMode = sh ? s.BaseShadow[k] : ShadowCastingMode.Off;
                }
            }
        }
    }
}
