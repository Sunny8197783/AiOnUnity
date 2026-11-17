using System.Collections.Generic;
using AiOn.Levels.Crowd;
using UnityEngine;

namespace AiOn.People
{
    /// <summary>
    /// Hand-held and worn props on a baked person (original engine/people.js PROPS): a 10 L water jug, a mart basket,
    /// a shopping cart, a backpack, a phone, a flashlight. The baked prefabs carry none, so each is built on first use
    /// from Unity's built-in cube / cylinder meshes (no colliders) as a child named Prop_&lt;name&gt; and toggled after.
    /// Jugs and baskets hang plumb from the hand (a <see cref="HangingProp"/> on the prop levels itself after the animation).
    /// </summary>
    public static class PersonProps
    {
        static readonly Dictionary<Color, Material> Mats = new();
        static Mesh _cube, _cyl;

        /// <summary>Animations that need a prop shown (people.js AUTO).</summary>
        public static PropMask Auto(string anim) => anim switch
        {
            "phone" => PropMask.Phone, "carry" => PropMask.WaterJug, "walker" => PropMask.Walker, _ => PropMask.None,
        };

        /// <summary>
        /// Shows or hides a prop by name. Children whose names contain the prop name (props baked into a model) are
        /// toggled as before; otherwise the prop is built once and toggled.
        /// </summary>
        public static void Set(GameObject root, string prop, bool on)
        {
            if (!root || string.IsNullOrEmpty(prop)) return;
            var built = Find(root.transform, "Prop_" + prop);
            if (built) { built.gameObject.SetActive(on); return; }
            bool any = false;
            string low = prop.ToLowerInvariant();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != root.transform && t.name.ToLowerInvariant().Contains(low)) { t.gameObject.SetActive(on); any = true; }
            if (any || !on) return;
            var mask = Props.Of(prop);
            if (mask == PropMask.None) return;
            var made = Build(root.transform, mask);
            if (made) made.gameObject.SetActive(true);
        }

        /// <summary>Brings the shown props of a person to <paramref name="want"/>; <paramref name="have"/> tracks what is shown.</summary>
        /// <returns>True when a prop was built (new renderers under the person).</returns>
        public static bool SetMask(GameObject root, PropMask want, ref PropMask have)
        {
            if (want == have || !root) return false;
            bool built = false;
            foreach (var (m, name) in Props.Names)
            {
                bool w = (want & m) != 0, h = (have & m) != 0;
                if (w == h) continue;
                var t = Find(root.transform, "Prop_" + name);
                if (!t && w) { t = Build(root.transform, m); built |= t != null; }
                if (t) t.gameObject.SetActive(w);
            }
            have = want;
            return built;
        }

        static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = Find(root.GetChild(i), name);
                if (r) return r;
            }
            return null;
        }

        static Material Mat(Color c)
        {
            if (Mats.TryGetValue(c, out var m) && m) return m;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh) { name = "Prop_" + ColorUtility.ToHtmlStringRGB(c) };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.35f);
            Mats[c] = m;
            return m;
        }

        static Mesh Cube => _cube ? _cube : (_cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx"));
        static Mesh Cyl => _cyl ? _cyl : (_cyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx"));

        static Color Hex(string h) => ColorUtility.TryParseHtmlString(h, out var c) ? c : Color.gray;

        static void Part(Transform parent, Mesh mesh, Vector3 pos, Vector3 size, string color, bool shadow = true)
        {
            var go = new GameObject("part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Mat(Hex(color));
            r.shadowCastingMode = shadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Transform Bone(Transform root, string name) => Find(root, name) ?? root;

        /// <summary>Builds a prop (hidden parts are built in the person's rest frame: +z forward, y up).</summary>
        static Transform Build(Transform root, PropMask p)
        {
            string name = null;
            foreach (var (m, n) in Props.Names) if (m == p) name = n;
            if (name == null || p == PropMask.Walker || p == PropMask.Baton) return null; // not built in the port
            var go = new GameObject("Prop_" + name);
            var t = go.transform;
            var yawRot = Quaternion.Euler(0, root.eulerAngles.y, 0);
            switch (p)
            {
                case PropMask.WaterJug: // 10 L container about 70 % full, hanging from the right hand
                    {
                        var hand = Bone(root, "rHand");
                        t.SetParent(hand, false);
                        t.SetPositionAndRotation(hand.position + Vector3.down * HangingProp.Drop, yawRot);
                        go.AddComponent<HangingProp>().body = root;
                        Part(t, Cube, new Vector3(0, -0.36f + 0.05f, 0), new Vector3(0.14f, 0.20f, 0.24f), "#dfe8ec");
                        Part(t, Cube, new Vector3(0, -0.36f - 0.10f, 0), new Vector3(0.141f, 0.10f, 0.241f), "#9fbccb");
                        Part(t, Cube, new Vector3(0, -0.03f, 0), new Vector3(0.025f, 0.05f, 0.17f), "#dfe8ec", false); // handle
                        Part(t, Cyl, new Vector3(0, -0.2f, -0.085f), new Vector3(0.044f, 0.015f, 0.044f), "#2f6db0", false); // cap
                        break;
                    }
                case PropMask.Basket: // red mart hand basket with a few groceries, left hand
                    {
                        var hand = Bone(root, "lHand");
                        t.SetParent(hand, false);
                        t.SetPositionAndRotation(hand.position + Vector3.down * HangingProp.Drop, yawRot);
                        go.AddComponent<HangingProp>().body = root;
                        Part(t, Cube, new Vector3(0, -0.275f, 0), new Vector3(0.26f, 0.21f, 0.40f), "#c22f38");
                        Part(t, Cube, new Vector3(0, -0.085f, 0), new Vector3(0.02f, 0.17f, 0.12f), "#a8262e", false); // handle
                        Part(t, Cube, new Vector3(-0.06f, -0.14f, -0.07f), new Vector3(0.09f, 0.08f, 0.09f), "#3b8fd6", false); // a bottle top
                        break;
                    }
                case PropMask.Cart: // shopping cart at the feet, handle 1.0 m high 0.4 m ahead, basket to 1.2 m
                    {
                        t.SetParent(root, false);
                        Part(t, Cube, new Vector3(0, 1.0f, 0.38f), new Vector3(0.6f, 0.035f, 0.035f), "#c9343c");
                        Part(t, Cube, new Vector3(0, 0.78f, 0.80f), new Vector3(0.5f, 0.42f, 0.78f), "#b9bdc3");
                        Part(t, Cube, new Vector3(0, 0.62f, 0.75f), new Vector3(0.3f, 0.18f, 0.3f), "#d9e6ee", false);
                        foreach (var s in new[] { -1f, 1f })
                        {
                            Part(t, Cube, new Vector3(s * 0.25f, 0.57f, 0.42f), new Vector3(0.025f, 0.86f, 0.025f), "#b9bdc3", false);
                            Part(t, Cube, new Vector3(s * 0.19f, 0.15f, 0.8f), new Vector3(0.025f, 0.025f, 0.75f), "#b9bdc3", false);
                            Part(t, Cyl, new Vector3(s * 0.2f, 0.055f, 0.47f), new Vector3(0.11f, 0.013f, 0.11f), "#9b2b30", false);
                            Part(t, Cyl, new Vector3(s * 0.15f, 0.055f, 1.15f), new Vector3(0.11f, 0.013f, 0.11f), "#9b2b30", false);
                        }
                        break;
                    }
                case PropMask.Backpack:
                    {
                        var chest = Bone(root, "chest");
                        t.SetParent(chest, false);
                        t.SetPositionAndRotation(chest.position - yawRot * Vector3.forward * 0.17f - Vector3.up * 0.08f, yawRot);
                        Part(t, Cube, Vector3.zero, new Vector3(0.29f, 0.42f, 0.14f), "#2b3242");
                        Part(t, Cube, new Vector3(0, -0.05f, -0.09f), new Vector3(0.23f, 0.19f, 0.05f), "#232937");
                        break;
                    }
                case PropMask.Phone:
                    {
                        var hand = Bone(root, "rHand");
                        t.SetParent(hand, false);
                        t.SetPositionAndRotation(hand.position + yawRot * new Vector3(-0.02f, 0.02f, 0.04f), yawRot * Quaternion.Euler(-60, 0, 0));
                        Part(t, Cube, Vector3.zero, new Vector3(0.07f, 0.145f, 0.009f), "#16181c", false);
                        break;
                    }
                case PropMask.Flashlight:
                    {
                        var hand = Bone(root, "lHand");
                        t.SetParent(hand, false);
                        t.SetPositionAndRotation(hand.position, yawRot * Quaternion.Euler(90, 0, 0));
                        Part(t, Cyl, new Vector3(0, 0.04f, 0), new Vector3(0.035f, 0.1f, 0.035f), "#2a2d33", false);
                        break;
                    }
            }
            return t;
        }
    }
}
