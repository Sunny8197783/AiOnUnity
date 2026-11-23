using System.Collections.Generic;
using AiOn.Levels;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AiOn.Rendering
{
    /// <summary>One decal of a level: what (a picture in Resources/Decals), and where to find its surface.</summary>
    public readonly struct DecalSpot
    {
        /// <summary>Picture name (decal_leak, decal_crack, decal_grime, decal_tyre, decal_oil, notice_*) or "puddle".</summary>
        public readonly string Kind;
        /// <summary>Unity position the surface is looked for from, and the direction to look (down for floors).</summary>
        public readonly Vector3 From, Dir;
        public readonly float Width, Height, Roll, Opacity;
        public DecalSpot(string kind, Vector3 from, Vector3 dir, float width, float height, float roll = 0f, float opacity = 1f)
        {
            Kind = kind; From = from; Dir = dir.normalized; Width = width; Height = height; Roll = roll; Opacity = opacity;
        }
    }

    /// <summary>Pure helpers behind <see cref="LevelDecals"/> (EditMode-tested).</summary>
    public static class DecalMath
    {
        /// <summary>
        /// The first surface of the level layout along a ray (Unity coordinates): the solid boxes (walls, counters, pillars;
        /// the original's collision, which the levels keep instead of mesh colliders) and the walk zones' floors. False when
        /// nothing is hit within maxDistance.
        /// </summary>
        public static bool Cast(LevelLayout layout, Vector3 origin, Vector3 dir, float maxDistance, out Vector3 point, out Vector3 normal)
        {
            point = normal = default;
            if (layout == null) return false;
            float best = maxDistance;
            bool hit = false;
            dir = dir.normalized;
            foreach (var s in layout.solids)
            {
                if (s == null || s.off || s.IsEmpty) continue;
                var min = new Vector3(s.x0, s.y0, -s.z1);
                var max = new Vector3(s.x1, s.y1, -s.z0);
                if (RayBox(origin, dir, min, max, out float t, out var n) && t < best && t > 0.001f) { best = t; normal = n; hit = true; }
            }
            if (dir.y < -0.2f)
                foreach (var w in layout.walk)
                {
                    if (w == null || w.isRamp) continue;
                    float t = (w.y - origin.y) / dir.y;
                    if (t <= 0.001f || t >= best) continue;
                    var p = origin + dir * t;
                    float oz = -p.z;
                    if (p.x < w.x0 || p.x > w.x1 || oz < w.z0 || oz > w.z1) continue;
                    best = t; normal = Vector3.up; hit = true;
                }
            if (hit) point = origin + dir * best;
            return hit;
        }

        /// <summary>Slab test from outside a box: distance to the entry face and that face's normal.</summary>
        public static bool RayBox(Vector3 o, Vector3 d, Vector3 min, Vector3 max, out float t, out Vector3 normal)
        {
            t = 0; normal = default;
            float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity;
            int axis = -1; float sign = 0;
            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(d[i]) < 1e-6f)
                {
                    if (o[i] < min[i] || o[i] > max[i]) return false;
                    continue;
                }
                float t1 = (min[i] - o[i]) / d[i], t2 = (max[i] - o[i]) / d[i];
                float s = -1f;
                if (t1 > t2) { (t1, t2) = (t2, t1); s = 1f; }
                if (t1 > tmin) { tmin = t1; axis = i; sign = s; }
                if (t2 < tmax) tmax = t2;
                if (tmin > tmax) return false;
            }
            if (axis < 0 || tmin < 0) return false; // inside the box, or no slab bounded the entry
            t = tmin;
            normal = Vector3.zero; normal[axis] = sign;
            return true;
        }

        /// <summary>
        /// The decals of each level: restrained wear (grime, stains, cracks, tyre and oil marks), a few notice sheets with
        /// fictional Korean text (lift service, recycling days, parcels, a lost umbrella, opening hours, the car park), and
        /// a puddle or two. Placed from fixed look-from points onto the layout's surfaces (see Cast).
        /// </summary>
        public static List<DecalSpot> For(string levelId)
        {
            var l = new List<DecalSpot>();
            var down = Vector3.down;
            switch (levelId)
            {
                case "apartment":
                    // 3F corridor: notices by the lift, wear along the floor, a hairline crack, a stain under the window strip
                    l.Add(new DecalSpot("notice_lift", new Vector3(2.4f, 7.05f, 1.3f), Vector3.back, 0.42f, 0.6f, 1.5f));
                    l.Add(new DecalSpot("notice_parcel", new Vector3(-0.2f, 7.0f, 1.3f), Vector3.back, 0.36f, 0.51f, -2f));
                    l.Add(new DecalSpot("decal_grime", new Vector3(-6f, 6.5f, 1.2f), down, 1.6f, 1.2f, 20f, 0.8f));
                    l.Add(new DecalSpot("decal_grime", new Vector3(7.5f, 6.5f, 0.9f), down, 1.4f, 1.0f, -35f, 0.7f));
                    l.Add(new DecalSpot("decal_grime", new Vector3(14.5f, 6.5f, 1.3f), down, 1.8f, 1.2f, 70f, 0.7f));
                    l.Add(new DecalSpot("decal_crack", new Vector3(12.5f, 6.6f, 1.3f), Vector3.back, 0.9f, 0.9f, 15f, 0.8f));
                    l.Add(new DecalSpot("decal_leak", new Vector3(9f, 6.2f, 1.0f), Vector3.forward, 0.8f, 1.0f, 0f, 0.9f));
                    // stairwell: a long stain and a crack on the landing walls
                    l.Add(new DecalSpot("decal_leak", new Vector3(5f, 4.6f, -2.5f), Vector3.right, 0.9f, 1.8f, 0f, 0.9f));
                    l.Add(new DecalSpot("decal_crack", new Vector3(5f, 2.0f, -2.0f), Vector3.left, 1.0f, 1.0f, -20f, 0.7f));
                    // lobby: recycling days and the lost umbrella on the tiled wall, entrance grime
                    l.Add(new DecalSpot("notice_recycle", new Vector3(3.9f, 1.5f, 2.0f), Vector3.back, 0.42f, 0.6f, -1f));
                    l.Add(new DecalSpot("notice_lost", new Vector3(6.0f, 1.45f, 2.0f), Vector3.back, 0.34f, 0.48f, 2.5f));
                    l.Add(new DecalSpot("decal_grime", new Vector3(4f, 1.0f, 4.6f), down, 2.0f, 1.4f, 0f, 0.6f));
                    break;
                case "town":
                    // the crossing and the street outside 103동: tyre marks, oil, cracks, a puddle by the kerb
                    l.Add(new DecalSpot("decal_tyre", new Vector3(-6f, 1f, 2.5f), down, 0.3f, 4.2f, 84f, 0.95f));
                    l.Add(new DecalSpot("decal_tyre", new Vector3(-5.8f, 1f, 4.0f), down, 0.3f, 3.6f, 86f, 0.9f));
                    l.Add(new DecalSpot("decal_oil", new Vector3(9f, 1f, -1f), down, 1.3f, 1.1f, 30f, 0.8f));
                    l.Add(new DecalSpot("decal_crack", new Vector3(3.5f, 1f, 11.6f), down, 1.6f, 1.6f, 40f, 0.6f));
                    l.Add(new DecalSpot("decal_crack", new Vector3(-12f, 1f, -3f), down, 1.4f, 1.4f, -10f, 0.5f));
                    l.Add(new DecalSpot("puddle", new Vector3(6.5f, 1f, 5.2f), down, 1.6f, 0.9f, 10f, 1f));
                    l.Add(new DecalSpot("decal_oil", new Vector3(80f, 1f, -21f), down, 1.2f, 1.0f, -15f, 0.55f));
                    l.Add(new DecalSpot("decal_grime", new Vector3(-86f, 1f, -13f), down, 2.4f, 1.6f, 0f, 0.5f));
                    l.Add(new DecalSpot("puddle", new Vector3(-80f, 1f, 17.5f), down, 1.8f, 1.1f, 25f, 1f));
                    break;
                case "shelter":
                    // the car park: oil stains in the bays, tyre marks on the lane, a stain down a wall, the car park notice
                    l.Add(new DecalSpot("decal_oil", new Vector3(-14f, 1f, -10f), down, 1.4f, 1.2f, 10f, 0.7f));
                    l.Add(new DecalSpot("decal_oil", new Vector3(16f, 1f, 9f), down, 1.2f, 1.0f, -40f, 0.6f));
                    l.Add(new DecalSpot("decal_oil", new Vector3(-6f, 1f, 12f), down, 1.0f, 0.9f, 75f, 0.55f));
                    l.Add(new DecalSpot("decal_tyre", new Vector3(10f, 1f, -2f), down, 0.26f, 5f, 0f, 0.45f));
                    l.Add(new DecalSpot("decal_tyre", new Vector3(11.6f, 1f, -2f), down, 0.26f, 5f, 0f, 0.45f));
                    l.Add(new DecalSpot("puddle", new Vector3(11f, 1f, -12f), down, 2.0f, 1.2f, 0f, 1f));
                    l.Add(new DecalSpot("decal_grime", new Vector3(4f, 1f, 13f), down, 2.4f, 1.8f, 30f, 0.5f));
                    l.Add(new DecalSpot("notice_carpark", new Vector3(0f, 1.6f, -14f), Vector3.forward, 0.42f, 0.6f, 1f));
                    l.Add(new DecalSpot("decal_leak", new Vector3(-20f, 1.9f, 0f), Vector3.right, 1.0f, 2.0f, 0f, 0.85f));
                    break;
                case "store":
                    l.Add(new DecalSpot("decal_grime", new Vector3(0.6f, 1f, 1.2f), down, 1.6f, 1.0f, 0f, 0.45f));
                    break;
                case "mart":
                    l.Add(new DecalSpot("decal_grime", new Vector3(-9f, 1f, 1.6f), down, 2.4f, 1.4f, 0f, 0.45f));
                    break;
            }
            return l;
        }
    }

    /// <summary>
    /// The levels' decals (URP DBuffer decal projectors; puddles are thin glossy quads that take the live reflection):
    /// see DecalMath.For. Materials come from Resources/Decals (AiOn → Graphics → Build Look Assets makes them). Off on
    /// the lowest tier. Added by EnvironmentController to every level.
    /// </summary>
    public sealed class LevelDecals : MonoBehaviour
    {
        public static bool Allowed { get; set; } = true;

        /// <summary>Decals placed in this level (for checks).</summary>
        public int Placed { get; private set; }
        public int Missed { get; private set; }

        static Mesh s_quad;

        /// <summary>
        /// Projection depth (m), centred on the surface: a floor decal reaches only 6 cm up (the layout's floors match the
        /// meshes, and shoes, a seated person or a lying patient must not take the oil stain), a wall decal 12 cm out (the
        /// layout's walls sit within a few centimetres of the drawn ones; a back against the wall stays clean).
        /// </summary>
        public const float FloorDepth = 0.12f, WallDepth = 0.24f;

        /// <summary>The depth for a surface with this normal (pure; tested).</summary>
        public static float DepthFor(Vector3 normal) => Mathf.Abs(normal.y) > 0.7f ? FloorDepth : WallDepth;

        void Start()
        {
            if (!Allowed) return;
            var id = gameObject.scene.name.ToLowerInvariant();
            var host = AiOn.Story.UnityStoryHost.Instance;
            var layout = host ? host.LayoutOf(id) : null;
            if (layout == null) return;
            var root = new GameObject("Decals").transform;
            root.SetParent(transform, false);
            foreach (var s in DecalMath.For(id))
            {
                if (!DecalMath.Cast(layout, s.From, s.Dir, 8f, out var p, out var n)) { Missed++; continue; }
                if (s.Kind == "puddle") { if (Puddle(root, p, n, s)) Placed++; continue; }
                var mat = Resources.Load<Material>("Decals/" + s.Kind);
                if (!mat) { Missed++; continue; }
                var go = new GameObject("Decal_" + s.Kind);
                go.transform.SetParent(root, false);
                // the box straddles the found surface: the layout's boxes sit within a few centimetres of the drawn walls
                go.transform.position = p;
                var up = Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up;
                go.transform.rotation = Quaternion.LookRotation(-n, up) * Quaternion.Euler(0, 0, s.Roll);
                var d = go.AddComponent<DecalProjector>();
                d.material = mat;
                d.size = new Vector3(s.Width, s.Height, DepthFor(n));
                d.pivot = Vector3.zero;
                d.fadeFactor = s.Opacity;
                d.drawDistance = 60f;
                d.startAngleFade = 60f; d.endAngleFade = 85f; // nothing smeared over the edges it wraps round
                Placed++;
            }
        }

        bool Puddle(Transform root, Vector3 p, Vector3 n, DecalSpot s)
        {
            var mat = Resources.Load<Material>("Decals/Puddle");
            if (!mat) return false;
            if (!s_quad)
            {
                s_quad = new Mesh { name = "PuddleQuad", hideFlags = HideFlags.DontSave };
                s_quad.SetVertices(new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f) });
                s_quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
                s_quad.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
                s_quad.SetTangents(new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) });
                s_quad.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
                s_quad.RecalculateBounds();
            }
            var go = new GameObject("Puddle");
            go.transform.SetParent(root, false);
            go.transform.position = p + n * 0.006f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0, s.Roll, 0);
            go.transform.localScale = new Vector3(s.Width, 1f, s.Height);
            go.AddComponent<MeshFilter>().sharedMesh = s_quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return true;
        }
    }
}
