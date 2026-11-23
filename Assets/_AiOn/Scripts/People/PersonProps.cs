using System.Collections.Generic;
using AiOn.Levels.Crowd;
using UnityEngine;

namespace AiOn.People
{
    /// <summary>
    /// Hand-held and worn props on a baked person (original engine/people.js PROPS): a 10 L water jug, a mart basket,
    /// a shopping cart, a backpack, a phone, a flashlight. The prefabs carry none, so each is built on first use from
    /// Unity's built-in cube / cylinder meshes (no colliders) as a child named Prop_&lt;name&gt; and toggled after. A cart
    /// pusher's hands rest on <see cref="CartHandle"/> (PersonAnimator.HoldPose "cart", the arm pose layer).
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

        static void Part(Transform parent, Mesh mesh, Vector3 pos, Vector3 size, string color, bool shadow = true) =>
            Part(parent, mesh, pos, size, Quaternion.identity, color, shadow);

        static void Part(Transform parent, Mesh mesh, Vector3 pos, Vector3 size, Quaternion rot, string color, bool shadow)
        {
            var go = new GameObject("part");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Mat(Hex(color));
            r.shadowCastingMode = shadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>A box open at the top, walls <paramref name="wall"/> thick (a basket, a cart's wire basket), with a rim.</summary>
        static void OpenBox(Transform parent, Vector3 c, Vector3 size, float wall, string color, string rim)
        {
            float hx = size.x * 0.5f, hy = size.y * 0.5f, hz = size.z * 0.5f;
            Part(parent, Cube, c + new Vector3(0, -hy + wall * 0.5f, 0), new Vector3(size.x, wall, size.z), color);
            foreach (var sx in new[] { -1f, 1f })
                Part(parent, Cube, c + new Vector3(sx * (hx - wall * 0.5f), 0, 0), new Vector3(wall, size.y, size.z), color, false);
            foreach (var sz in new[] { -1f, 1f })
                Part(parent, Cube, c + new Vector3(0, 0, sz * (hz - wall * 0.5f)), new Vector3(size.x, size.y, wall), color, false);
            float r = wall * 1.8f;
            foreach (var sx in new[] { -1f, 1f })
                Part(parent, Cube, c + new Vector3(sx * (hx - wall * 0.5f), hy, 0), new Vector3(r, r, size.z + r), rim, false);
            foreach (var sz in new[] { -1f, 1f })
                Part(parent, Cube, c + new Vector3(0, hy, sz * (hz - wall * 0.5f)), new Vector3(size.x + r, r, r), rim, false);
        }

        /// <summary>The cart handle (person's root frame, a 0.95-scale man): where both palms rest, 0.21 m either side.</summary>
        public static readonly Vector3 CartHandle = new(0f, 1.0f, 0.40f);
        public const float CartGripHalf = 0.21f;

        /// <summary>A cart is sized to its pusher (built for the 0.95-scale man): the model's scale over that.</summary>
        static float CartScale(Transform root)
        {
            var a = root.GetComponentInChildren<Animator>(true);
            float k = a ? a.transform.localScale.y / RocketboxCast.MaleScale : 1f;
            return k > 0.5f && k < 1.5f ? k : 1f;
        }

        /// <summary>The anchor or bone a prop goes on (Rocketbox grips and back anchor, else the old rig's bone by name).</summary>
        static Transform Bone(Transform root, string name) => PersonRig.Find(root, name) ?? root;

        static bool IsGrip(Transform t) => t && (t.name == PersonRig.GripR || t.name == PersonRig.GripL);

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
                        var hp = go.AddComponent<HangingProp>();
                        hp.body = root; hp.drop = IsGrip(hand) ? HangingProp.GripDrop : HangingProp.Drop;
                        t.SetPositionAndRotation(hand.position + Vector3.down * hp.drop, yawRot);
                        // a white 10 L container (body 15 x 30 x 24 cm), its carrying bar in the fist and two posts down to the body
                        Part(t, Cube, new Vector3(0, -0.012f, 0), new Vector3(0.028f, 0.028f, 0.11f), "#e8eef1", false); // bar in the fist
                        foreach (var sz in new[] { -1f, 1f })
                            Part(t, Cube, new Vector3(0, -0.073f, sz * 0.045f), new Vector3(0.026f, 0.096f, 0.024f), "#e8eef1", false);
                        Part(t, Cube, new Vector3(0, -0.27f, 0), new Vector3(0.15f, 0.30f, 0.24f), "#d6e4ea");
                        Part(t, Cube, new Vector3(0, -0.205f, 0), new Vector3(0.152f, 0.006f, 0.242f), "#b9cdd6", false); // water line
                        Part(t, Cyl, new Vector3(0, -0.11f, -0.095f), new Vector3(0.05f, 0.012f, 0.05f), "#2f6db0", false); // cap
                        break;
                    }
                case PropMask.Basket: // red mart hand basket with a few groceries, left hand
                    {
                        var hand = Bone(root, "lHand");
                        t.SetParent(hand, false);
                        var hp = go.AddComponent<HangingProp>();
                        hp.body = root; hp.drop = IsGrip(hand) ? HangingProp.GripDrop : HangingProp.Drop;
                        t.SetPositionAndRotation(hand.position + Vector3.down * hp.drop, yawRot);
                        // a red hand basket (open, 26 x 21 x 40 cm), its handle in the fist, groceries inside
                        Part(t, Cube, new Vector3(0, -0.012f, 0), new Vector3(0.026f, 0.026f, 0.12f), "#a8262e", false); // handle in the fist
                        foreach (var sz in new[] { -1f, 1f })
                            Part(t, Cube, new Vector3(0, -0.09f, sz * 0.07f), new Vector3(0.02f, 0.15f, 0.02f), "#a8262e", false);
                        OpenBox(t, new Vector3(0, -0.275f, 0), new Vector3(0.26f, 0.21f, 0.40f), 0.012f, "#c22f38", "#a8262e");
                        Part(t, Cube, new Vector3(-0.05f, -0.25f, -0.08f), new Vector3(0.07f, 0.22f, 0.07f), "#3b8fd6", false); // a water bottle
                        Part(t, Cube, new Vector3(0.05f, -0.30f, 0.07f), new Vector3(0.11f, 0.13f, 0.07f), "#e5b53a", false); // a box of ramyeon
                        break;
                    }
                case PropMask.Cart: // shopping cart ahead of the pusher: handle where both palms rest, wire basket, wheels on the floor
                    {
                        t.SetParent(root, false);
                        t.localScale = Vector3.one * CartScale(root);
                        const string steel = "#a9afb6", dark = "#5d6268", red = "#c9343c", wheel = "#2b2d31";
                        var h = CartHandle;
                        Part(t, Cyl, h, new Vector3(0.032f, 0.29f, 0.032f), Quaternion.Euler(0, 0, 90), red, false); // the handle bar
                        // the basket: 50 cm wide, 40 high, 72 long, from just ahead of the handle
                        var bc = new Vector3(0, 0.78f, h.z + 0.08f + 0.36f);
                        OpenBox(t, bc, new Vector3(0.50f, 0.40f, 0.72f), 0.01f, steel, dark);
                        foreach (var sx in new[] { -1f, 1f })
                        {
                            // the handle's arms down to the basket's back corners
                            Part(t, Cube, new Vector3(sx * 0.27f, h.y - 0.02f, h.z + 0.05f), new Vector3(0.022f, 0.022f, 0.12f), steel, false);
                            // rear and front legs down to the wheel forks, and the low rail under the bottom tray
                            Part(t, Cube, new Vector3(sx * 0.22f, 0.36f, bc.z - 0.30f), new Vector3(0.022f, 0.48f, 0.022f), steel, false);
                            Part(t, Cube, new Vector3(sx * 0.17f, 0.36f, bc.z + 0.32f), new Vector3(0.022f, 0.48f, 0.022f), steel, false);
                            Part(t, Cube, new Vector3(sx * 0.19f, 0.13f, bc.z), new Vector3(0.022f, 0.022f, 0.66f), steel, false);
                            // upright wheels (discs turned on their side), touching the floor, under their forks
                            foreach (var (wx, wz) in new[] { (sx * 0.22f, bc.z - 0.30f), (sx * 0.17f, bc.z + 0.32f) })
                            {
                                Part(t, Cyl, new Vector3(wx, 0.05f, wz), new Vector3(0.10f, 0.012f, 0.10f), Quaternion.Euler(0, 0, 90), wheel, false);
                                Part(t, Cube, new Vector3(wx, 0.11f, wz), new Vector3(0.035f, 0.05f, 0.03f), dark, false);
                            }
                        }
                        Part(t, Cube, new Vector3(0, 0.15f, bc.z), new Vector3(0.40f, 0.012f, 0.60f), steel, false); // bottom tray
                        Part(t, Cube, new Vector3(-0.08f, 0.70f, bc.z + 0.12f), new Vector3(0.24f, 0.22f, 0.30f), "#d9c49a", false); // a box of water
                        Part(t, Cube, new Vector3(0.12f, 0.66f, bc.z - 0.12f), new Vector3(0.14f, 0.14f, 0.10f), "#3b8fd6", false);
                        break;
                    }
                case PropMask.Backpack:
                    {
                        var chest = Bone(root, "chest");
                        t.SetParent(chest, false);
                        if (chest.name == PersonRig.Back) { t.localPosition = new Vector3(0, 0, -0.075f); t.localRotation = Quaternion.identity; } // on the back's surface
                        else t.SetPositionAndRotation(chest.position - yawRot * Vector3.forward * 0.17f - Vector3.up * 0.08f, yawRot);
                        Part(t, Cube, Vector3.zero, new Vector3(0.29f, 0.42f, 0.14f), "#2b3242");
                        Part(t, Cube, new Vector3(0, -0.05f, -0.09f), new Vector3(0.23f, 0.19f, 0.05f), "#232937");
                        break;
                    }
                case PropMask.Phone:
                    {
                        var hand = Bone(root, "rHand");
                        t.SetParent(hand, false);
                        // in the palm: back of the phone on the palm, long side along the fingers, screen facing out of the hand
                        if (IsGrip(hand)) { t.localPosition = PersonRig.PalmNormal(true) * 0.008f + new Vector3(0, -0.035f, 0.005f); t.localRotation = Quaternion.LookRotation(PersonRig.PalmNormal(true), Vector3.up); }
                        else t.SetPositionAndRotation(hand.position + yawRot * new Vector3(-0.02f, 0.02f, 0.04f), yawRot * Quaternion.Euler(-60, 0, 0));
                        Part(t, Cube, Vector3.zero, new Vector3(0.07f, 0.145f, 0.009f), "#16181c", false);
                        break;
                    }
                case PropMask.Flashlight:
                    {
                        var hand = Bone(root, "lHand");
                        t.SetParent(hand, false);
                        // in the fist, pointing out past the thumb (the grip's +z)
                        if (IsGrip(hand)) { t.localPosition = PersonRig.PalmNormal(false) * 0.02f; t.localRotation = Quaternion.Euler(90, 0, 0); }
                        else t.SetPositionAndRotation(hand.position, yawRot * Quaternion.Euler(90, 0, 0));
                        Part(t, Cyl, new Vector3(0, 0.04f, 0), new Vector3(0.035f, 0.1f, 0.035f), "#2a2d33", false);
                        break;
                    }
            }
            return t;
        }
    }
}
