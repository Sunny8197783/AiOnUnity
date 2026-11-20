using System.Collections.Generic;
using AiOn.Interaction;
using UnityEngine;

namespace AiOn.Rendering
{
    /// <summary>
    /// The outline around what the player can use. Nothing is added to the scene: each frame the target's meshes are
    /// drawn once more with the AiOn/Outline material (Resources/Look/Outline).
    /// What can be outlined: a point's door leaf (the solids it is hosted on), small separate props at the point, and a
    /// loose thing in reach. Most fixtures are part of the level's merged mesh and have no mesh of their own; those get
    /// the HUD's brackets only.
    /// </summary>
    public static class OutlineHighlight
    {
        public const float MaxPropSize = 2.6f, NearPad = 0.25f;

        static Material s_mat;
        static bool s_missing;
        static MaterialPropertyBlock s_mpb;
        static readonly int ColorId = Shader.PropertyToID("_Color"), CenterId = Shader.PropertyToID("_Center");
        /// <summary>How long a point with nothing to outline waits before it is searched again (its prop may be dressed in later).</summary>
        public const float EmptyRetrySeconds = 3f;

        static readonly Dictionary<InteractionPoint, MeshFilter[]> s_cache = new();
        static readonly Dictionary<InteractionPoint, float> s_emptyUntil = new();
        static readonly List<MeshFilter> s_tmp = new();
        // The loaded scenes' small, movable renderers: the only ones IsPropAt can accept. Gathered once per level (and again
        // when a search comes up empty on an old list), so finding a point's prop scans dozens of props, not the whole town.
        static readonly List<MeshRenderer> s_props = new();
        static float s_propsAt = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_mat = null; s_missing = false; Forget();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m) => Forget();

        /// <summary>Drops everything found so far (a new level).</summary>
        public static void Forget() { s_cache.Clear(); s_emptyUntil.Clear(); s_props.Clear(); s_propsAt = -1f; }

        static void GatherProps()
        {
            s_props.Clear();
            s_propsAt = Time.unscaledTime;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r.isPartOfStaticBatch) continue;
                var size = r.bounds.size;
                if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) <= MaxPropSize) s_props.Add(r);
            }
        }

        static void SearchProps(Vector3 at)
        {
            for (int i = 0; i < s_props.Count && s_tmp.Count < 6; i++)
            {
                var r = s_props[i];
                if (!r || !r.enabled || !IsPropAt(r.bounds, at)) continue;
                if (r.TryGetComponent(out MeshFilter mf) && Usable(mf)) s_tmp.Add(mf);
            }
        }

        /// <summary>True when a prop this big sits close enough to the point to be "the thing" (pure; tested).</summary>
        public static bool IsPropAt(Bounds bounds, Vector3 point)
        {
            var s = bounds.size;
            if (Mathf.Max(s.x, Mathf.Max(s.y, s.z)) > MaxPropSize) return false;
            bounds.Expand(NearPad * 2f);
            return bounds.Contains(point);
        }

        /// <summary>
        /// The meshes to outline for a point (found once per point, then remembered; a point with none is looked at again
        /// after EmptyRetrySeconds rather than written off).
        /// </summary>
        public static MeshFilter[] MeshesOf(InteractionPoint p)
        {
            if (!p) return System.Array.Empty<MeshFilter>();
            if (s_cache.TryGetValue(p, out var known)) return known;
            float now = Time.unscaledTime;
            if (s_emptyUntil.TryGetValue(p, out var until) && now < until) return System.Array.Empty<MeshFilter>();
            if (s_cache.Count > 64) s_cache.Clear(); // a long level's many points
            s_tmp.Clear();
            if (!string.IsNullOrEmpty(p.host))
                foreach (var door in Object.FindObjectsByType<Door>(FindObjectsSortMode.None))
                    if (p.IsHost(door.id))
                        foreach (var mf in (door.pivot ? door.pivot : door.transform).GetComponentsInChildren<MeshFilter>())
                            if (Usable(mf)) s_tmp.Add(mf);
            if (s_tmp.Count == 0)
            {
                if (s_propsAt < 0f) GatherProps();
                SearchProps(p.Position);
                if (s_tmp.Count == 0 && now - s_propsAt > EmptyRetrySeconds) { GatherProps(); SearchProps(p.Position); } // dressed in since
            }
            if (s_tmp.Count == 0)
            {
                s_emptyUntil[p] = now + EmptyRetrySeconds;
                return System.Array.Empty<MeshFilter>();
            }
            s_emptyUntil.Remove(p);
            var found = s_tmp.ToArray();
            s_cache[p] = found;
            return found;
        }

        static bool Usable(MeshFilter mf) =>
            mf && mf.sharedMesh && mf.TryGetComponent(out MeshRenderer r) && r.enabled && !r.isPartOfStaticBatch;

        /// <summary>Draws the outline of these meshes this frame, at alpha 0..1.</summary>
        public static void Draw(IReadOnlyList<MeshFilter> meshes, Color color, float alpha)
        {
            if (meshes == null || meshes.Count == 0 || alpha <= 0.01f || s_missing) return;
            if (!s_mat)
            {
                var template = Resources.Load<Material>("Look/Outline");
                if (!template) { s_missing = true; Debug.LogWarning("[Look] missing Resources/Look/Outline (run AiOn → Graphics → Build Look Assets)"); return; }
                s_mat = new Material(template) { name = "Outline (runtime)" };
            }
            s_mpb ??= new MaterialPropertyBlock();
            color.a *= alpha;
            var rp = new RenderParams(s_mat) { matProps = s_mpb, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
            for (int i = 0; i < meshes.Count; i++)
            {
                var mf = meshes[i];
                if (!mf || !mf.sharedMesh || !mf.gameObject.activeInHierarchy) continue;
                var mesh = mf.sharedMesh;
                s_mpb.SetColor(ColorId, color);
                s_mpb.SetVector(CenterId, mesh.bounds.center);
                rp.layer = mf.gameObject.layer;
                var m = mf.transform.localToWorldMatrix;
                for (int sub = 0; sub < mesh.subMeshCount; sub++) Graphics.RenderMesh(rp, mesh, sub, m);
            }
        }

        /// <summary>The meshes of a loose thing (a brick, a bottle).</summary>
        public static void MeshesOf(Transform t, List<MeshFilter> into)
        {
            into.Clear();
            if (!t) return;
            foreach (var mf in t.GetComponentsInChildren<MeshFilter>()) if (mf && mf.sharedMesh) into.Add(mf);
        }
    }
}
