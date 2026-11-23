// The 「산에서 내려온 사람들」 report card's memory picture (competition upgrade, minigame art): the mouth of the hill path
// as 정민 remembers it, a lit 3D strip seen from a little above: the dirt path and the scrub of the hill on the left, the
// pavement toward the main road on the right, two trails of wet shoe prints walking toward the road (left and right
// feet, one pair larger with a longer stride, drying as they go), two torn sneaker tags and a black backpack lying on
// its back by the path. Nobody is there. Nothing moves, so it is drawn once. SightingScreen shows it in the traces strip.
using UnityEngine;

namespace AiOn.UI.Studio
{
    public sealed class SightStudio : MonoBehaviour
    {
        MiniStudio _s;
        public MiniStudio Studio => _s;

        public static SightStudio Create(Component owner, Vector2Int pixels)
        {
            var s = MiniStudio.Create("Sighting", owner, pixels, new Color(0.04f, 0.05f, 0.05f));
            var me = s.gameObject.AddComponent<SightStudio>();
            me._s = s;
            me.Build();
            return me;
        }

        void Build()
        {
            var root = _s.Empty("Path", null, Vector3.zero, Vector3.zero).transform;
            root.localScale = new Vector3(-1, 1, 1); // built with +x on the viewer's right (the camera looks down -z)
            // the pavement (damp concrete) and the dirt of the path's mouth with the hill's scrub behind
            var pave = _s.Lit(new Color(0.46f, 0.47f, 0.47f), 0.42f, 0, StudioTextures.Concrete, new Vector2(4, 2), StudioTextures.ConcreteNormal, 0.9f);
            _s.Part(PrimitiveType.Cube, "Pavement", root, new Vector3(0.6f, -0.05f, 0), Vector3.zero, new Vector3(4.6f, 0.1f, 2.4f), pave);
            var curb = _s.Lit(new Color(0.62f, 0.62f, 0.6f), 0.3f, 0, StudioTextures.Concrete, new Vector2(4, 0.2f), StudioTextures.ConcreteNormal, 0.6f);
            _s.Part(PrimitiveType.Cube, "Curb", root, new Vector3(0.6f, 0.04f, -0.78f), Vector3.zero, new Vector3(4.6f, 0.08f, 0.14f), curb);
            var dirt = _s.Lit(new Color(0.42f, 0.34f, 0.24f), 0.18f, 0, StudioTextures.Concrete, new Vector2(2, 2), StudioTextures.ConcreteNormal, 1.6f);
            _s.Part(PrimitiveType.Cube, "Dirt", root, new Vector3(-2.15f, -0.04f, 0), new Vector3(0, 4, 0), new Vector3(1.7f, 0.1f, 2.6f), dirt);
            // the hill's edge: clumps of scrub (a dark mass inside, crossed cards of small leaves around it), dry grass
            // along the dirt
            var under = _s.Lit(new Color(0.07f, 0.1f, 0.05f), 0.1f);
            var leaves = MiniStudio.Cutout(_s.Lit(Color.white, 0.15f, 0, StudioTextures.Leaves), 0.5f);
            leaves.SetFloat("_Cull", 0f);
            var grass = _s.Lit(new Color(0.42f, 0.38f, 0.2f), 0.1f);
            for (int c = 0; c < 9; c++)
            {
                float cz = -0.9f + c * 0.22f + Mathf.Sin(c * 2.7f) * 0.06f, cx = -2.15f - (c % 3) * 0.14f + Mathf.Sin(c * 1.3f) * 0.07f;
                float r = 0.2f + 0.07f * (c % 3);
                _s.Part(PrimitiveType.Sphere, "ScrubCore", root, new Vector3(cx, r * 0.3f, cz), new Vector3(0, c * 40, 0), new Vector3(r * 1.3f, r * 0.6f, r * 1.1f), under);
                for (int k = 0; k < 4; k++)
                    _s.Part(PrimitiveType.Quad, "Leaves", root, new Vector3(cx + Mathf.Sin(k * 2.1f + c) * 0.05f, r * 0.55f, cz + Mathf.Cos(k * 1.7f + c) * 0.05f),
                        new Vector3(k < 2 ? 0f : 70f, c * 31 + k * 45, 0), new Vector3(r * 2.1f, r * 1.6f, 1f), leaves);
            }
            for (int g = 0; g < 26; g++)
            {
                float z = -1.1f + g * 0.088f, x = -1.32f - (g * 7 % 5) * 0.025f;
                _s.Part(PrimitiveType.Cube, "Grass", root, new Vector3(x, 0.035f, z), new Vector3((g % 3 - 1) * 14f, g * 37 % 90, 8f + g % 4 * 6f), new Vector3(0.006f, 0.08f + (g % 3) * 0.025f, 0.004f), grass, false);
            }
            // two trails of wet prints from the hill toward the road: left and right feet in turn, each on its own side of
            // the walking line, toes a little out; the second person larger with a longer stride; drying as they go
            var sole = StudioTextures.Sole;
            for (int t = 0; t < 2; t++)
            {
                float len = t == 0 ? 0.25f : 0.3f, row = t == 0 ? 0.22f : -0.2f, stride = t == 0 ? 0.33f : 0.4f;
                float heading = t == 0 ? 3f : -5f; // degrees off straight toward the road
                for (int i = 0; i < 9; i++)
                {
                    float wet = Mathf.Lerp(0.78f, 0.12f, i / 8f);
                    var m = _s.Clear(new Color(0.09f, 0.085f, 0.08f, wet), 0.9f, sole);
                    bool left = i % 2 == 0;
                    float along = -1.3f + i * stride + t * 0.15f;
                    float side = (left ? 1f : -1f) * (t == 0 ? 0.065f : 0.08f) + Mathf.Sin(i * 2.3f + t) * 0.012f;
                    var pos = new Vector3(along, 0.002f + i * 0.0001f, row + side + along * Mathf.Tan(heading * Mathf.Deg2Rad));
                    // a quad lies flat facing up; its texture's top (the toe) toward +x, the way they walked; a right print
                    // is the left one mirrored, and each toes out a little
                    float yaw = -90f + heading * -1f + (left ? 6f : -6f) + Mathf.Sin(i * 1.9f + t * 3f) * 2.5f;
                    _s.Part(PrimitiveType.Quad, "Print", root, pos, new Vector3(90, 0, yaw), new Vector3(len * 0.42f * (left ? 1f : -1f), len, 1), m, false);
                }
            }
            // two torn sneaker tags by the path (white card with a printed band, a cut plastic loop), well inside the picture
            var paper = _s.Lit(new Color(0.93f, 0.92f, 0.88f), 0.2f);
            var ink = _s.Lit(new Color(0.12f, 0.13f, 0.16f), 0.3f);
            var red = _s.Lit(new Color(0.7f, 0.14f, 0.12f), 0.3f);
            for (int i = 0; i < 2; i++)
            {
                var tag = _s.Empty("Tag", root, new Vector3(-0.98f + i * 0.2f, 0.004f, 0.12f - i * 0.12f), new Vector3(0, 25 - i * 70, i * 4)).transform;
                _s.Part(PrimitiveType.Cube, "Card", tag, Vector3.zero, Vector3.zero, new Vector3(0.075f, 0.003f, 0.13f), paper);
                _s.Part(PrimitiveType.Cube, "Band", tag, new Vector3(0, 0.0018f, 0.035f), Vector3.zero, new Vector3(0.075f, 0.0006f, 0.03f), red, false);
                _s.Part(PrimitiveType.Cube, "Print", tag, new Vector3(0, 0.0018f, -0.025f), Vector3.zero, new Vector3(0.045f, 0.0006f, 0.012f), ink, false);
                _s.Part(PrimitiveType.Cylinder, "Loop", tag, new Vector3(0.01f, 0.002f, 0.1f), new Vector3(90, 0, 20), new Vector3(0.004f, 0.04f, 0.004f), ink, false);
            }
            // the backpack, lying on its back: a soft padded body, the front pocket with its zip, the two shoulder straps
            // fallen out flat on the ground, the grab loop at the top
            var bagMat = _s.Lit(new Color(0.07f, 0.072f, 0.08f), 0.32f, 0, StudioTextures.Fabric, new Vector2(3, 3), StudioTextures.FabricNormal, 1.6f);
            var webbing = _s.Lit(new Color(0.12f, 0.12f, 0.13f), 0.2f, 0, StudioTextures.Fabric, new Vector2(1, 6), StudioTextures.FabricNormal, 2f);
            var zip = _s.Lit(new Color(0.02f, 0.02f, 0.022f), 0.6f, 0.4f);
            var pull = _s.Lit(new Color(0.55f, 0.56f, 0.58f), 0.7f, 0.9f);
            var bag = _s.Empty("Bag", root, new Vector3(-1.72f, 0, -0.3f), new Vector3(0, 22, 0)).transform;
            _s.SoftBox("Body", bag, new Vector3(0, 0.07f, 0), Vector3.zero, new Vector3(0.3f, 0.14f, 0.42f), 0.055f, bagMat);
            _s.SoftBox("Pocket", bag, new Vector3(0, 0.145f, -0.06f), new Vector3(-3, 0, 0), new Vector3(0.22f, 0.045f, 0.19f), 0.02f, bagMat);
            _s.Part(PrimitiveType.Cube, "Zip", bag, new Vector3(0, 0.166f, 0.03f), Vector3.zero, new Vector3(0.18f, 0.004f, 0.006f), zip, false);
            _s.Part(PrimitiveType.Cube, "ZipPull", bag, new Vector3(0.092f, 0.168f, 0.04f), new Vector3(0, 15, 0), new Vector3(0.012f, 0.003f, 0.032f), pull, false);
            _s.Part(PrimitiveType.Cube, "TopZip", bag, new Vector3(0.152f, 0.11f, 0f), new Vector3(0, 0, 30), new Vector3(0.004f, 0.006f, 0.36f), zip, false);
            // the shoulder straps fallen out to one side, lying flat and a little curved
            for (int k = 0; k < 2; k++)
            {
                float x = -0.17f - k * 0.07f;
                _s.SoftBox("Strap", bag, new Vector3(x, 0.01f, 0.06f), new Vector3(0, 12 + k * 8, 0), new Vector3(0.055f, 0.014f, 0.2f), 0.006f, webbing);
                _s.SoftBox("StrapLow", bag, new Vector3(x - 0.05f, 0.008f, -0.12f), new Vector3(0, -18 - k * 10, 0), new Vector3(0.03f, 0.008f, 0.16f), 0.003f, webbing);
            }
            _s.SoftBox("Handle", bag, new Vector3(0, 0.11f, 0.215f), new Vector3(20, 0, 0), new Vector3(0.07f, 0.01f, 0.035f), 0.004f, webbing);

            // an overcast morning: a broad cool light from above, a little warm bounce
            _s.Spot("Sky", new Vector3(-0.3f, 4.2f, 1.4f), new Vector3(0.2f, 0, 0), new Color(0.88f, 0.92f, 1f), 15f, 9f, 100f, true);
            _s.Spot("Bounce", new Vector3(1.8f, 1.4f, 2.4f), new Vector3(0.2f, 0, 0), new Color(1f, 0.92f, 0.82f), 4f, 7f, 90f, false);
            _s.Aim(new Vector3(0.25f, 2.5f, 1.3f), new Vector3(0.25f, 0, -0.04f), 24f);
            var v = _s.Vignette;
            if (v != null) v.intensity.Override(0.1f); // a wide strip: a strong vignette darkens its ends
        }
    }
}
