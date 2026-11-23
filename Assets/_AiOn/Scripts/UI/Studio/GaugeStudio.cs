// The 1장 소화기 점검 screen's picture (competition upgrade, minigame art): the corridor extinguisher itself (the CC0 Poly
// Haven korean_fire_extinguisher_01, Resources/Props/KoreanExtinguisher, the one the 3D extinguisher scene holds) seen up
// close at its pressure gauge, framed tight so the dial fills the picture and the model's valve and lever (whose 1k
// texture will not stand more zoom) stay at the edges: a sharp face of our own over the model's (the yellow / green / red
// bands from ExtinguisherCheck in the legend's own colours, mostly self-lit so the light does not wash them out, ticks),
// a real needle at the reading, a hub and a glass cover that catches the corridor light. Closer still in low vision.
// Against the corridor wall by the hydrant cabinet. Nothing moves: it is drawn once (and again if the needle is set).
using UnityEngine;

namespace AiOn.UI.Studio
{
    public sealed class GaugeStudio : MonoBehaviour
    {
        /// <summary>The model's gauge: its face centre and radius (m, model space; measured by ray against the mesh).</summary>
        static readonly Vector3 FaceCentre = new(0f, 0.5755f, 0.0728f);
        const float FaceRadius = 0.0212f;

        MiniStudio _s;
        Transform _needle;

        public MiniStudio Studio => _s;

        /// <summary>The camera's field of view: the dial about three quarters of the picture's height (low vision: nearly all of it).</summary>
        public const float Fov = 17f, FovLowVision = 15.5f;

        /// <summary>The legend's colours (ExtGaugeScreen: 낮음 #F2B33D, 정상 #4CC38A, 높음 #E64849), so the dial and its key match.</summary>
        public static readonly Color Low = new Color32(242, 179, 61, 255), Normal = new Color32(76, 195, 138, 255), High = new Color32(230, 72, 73, 255);

        /// <summary>The set with the needle at <paramref name="needle"/> (0..1 over the dial); null without the model.</summary>
        public static GaugeStudio Create(Component owner, Vector2Int pixels, double needle, bool lowVision = false)
        {
            var prefab = Resources.Load<GameObject>("Props/KoreanExtinguisher");
            if (!prefab) return null;
            var s = MiniStudio.Create("Gauge", owner, pixels, new Color(0.05f, 0.055f, 0.06f));
            var me = s.gameObject.AddComponent<GaugeStudio>();
            me._s = s;
            me.Build(prefab, lowVision);
            me.Set(needle);
            return me;
        }

        void Build(GameObject prefab, bool lowVision)
        {
            // the corridor: the wall behind, the floor, the hydrant cabinet's red edge to one side
            var plaster = _s.Lit(new Color(0.6f, 0.61f, 0.6f), 0.2f, 0, StudioTextures.Plaster, new Vector2(3, 3), StudioTextures.PlasterNormal, 0.25f);
            _s.Part(PrimitiveType.Cube, "Wall", null, new Vector3(0, 1.2f, -0.28f), Vector3.zero, new Vector3(3f, 2.6f, 0.05f), plaster);
            var floor = _s.Lit(new Color(0.55f, 0.55f, 0.55f), 0.45f, 0, StudioTextures.Tile, new Vector2(3, 3), StudioTextures.TileNormal, 0.6f);
            _s.Part(PrimitiveType.Cube, "Floor", null, new Vector3(0, -0.05f, 0.6f), Vector3.zero, new Vector3(3f, 0.1f, 2f), floor);
            var cabinet = _s.Lit(new Color(0.62f, 0.07f, 0.06f), 0.55f, 0.3f);
            _s.Part(PrimitiveType.Cube, "HydrantCabinet", null, new Vector3(-0.62f, 1.0f, -0.2f), Vector3.zero, new Vector3(0.75f, 1.5f, 0.14f), cabinet);
            var ext = Instantiate(prefab, _s.Set, false);
            ext.name = "Extinguisher";
            ext.transform.localPosition = new Vector3(0, 0, 0);
            _s.Take(ext);

            // our dial face over the model's (sharp at any size), the needle, the hub, the glass
            // self-lit and drawn without tonemapping (Untoned below): the printed colours come out as printed, the
            // legend's own, instead of washed pale by the light and the tonemap
            var faceMat = MiniStudio.Cutout(_s.Lit(Color.black, 0f, 0, DialTexture));
            faceMat.EnableKeyword("_EMISSION");
            faceMat.SetTexture("_EmissionMap", DialTexture);
            faceMat.SetColor("_EmissionColor", Color.white);
            faceMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            var face = _s.Part(PrimitiveType.Quad, "DialFace", null, FaceCentre + new Vector3(0, 0, 0.0009f), new Vector3(0, 180, 0), Vector3.one * (FaceRadius * 2f), faceMat, false);
            face.GetComponent<MeshRenderer>().receiveShadows = true;
            var pivot = _s.Empty("Needle", null, FaceCentre + new Vector3(0, 0, 0.0022f), Vector3.zero);
            _needle = pivot.transform;
            var black = _s.Lit(new Color(0.03f, 0.03f, 0.035f), 0.5f, 0.2f);
            _s.Part(PrimitiveType.Cube, "Pointer", _needle, new Vector3(0, FaceRadius * 0.42f, 0), Vector3.zero, new Vector3(0.0011f, FaceRadius * 1.04f, 0.0006f), black, false);
            _s.Part(PrimitiveType.Cube, "Tail", _needle, new Vector3(0, -FaceRadius * 0.12f, 0), Vector3.zero, new Vector3(0.0016f, FaceRadius * 0.3f, 0.0006f), black, false);
            _s.Part(PrimitiveType.Cylinder, "Hub", null, FaceCentre + new Vector3(0, 0, 0.0027f), new Vector3(90, 0, 0), new Vector3(0.0055f, 0.0008f, 0.0055f), _s.Lit(new Color(0.12f, 0.12f, 0.13f), 0.7f, 0.8f), false);
            var glass = _s.Clear(new Color(0.85f, 0.9f, 0.95f, 0.05f), 0.96f);
            glass.SetFloat("_EnvironmentReflections", 0); glass.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); // a highlight, not a milky sheet
            _s.Part(PrimitiveType.Cylinder, "Glass", null, FaceCentre + new Vector3(0, 0, 0.0042f), new Vector3(90, 0, 0), new Vector3(FaceRadius * 2.08f, 0.0004f, FaceRadius * 2.08f), glass, false);

            // light: the corridor's ceiling light ahead and above, a soft fill from the open end, a glint on the glass
            _s.Spot("Ceiling", new Vector3(0.25f, 1.9f, 0.9f), FaceCentre, new Color(0.96f, 0.98f, 1f), 2.8f, 4f, 40f, true);
            _s.Spot("Fill", new Vector3(-0.7f, 0.8f, 0.8f), FaceCentre, new Color(0.9f, 0.88f, 0.84f), 0.7f, 4f, 50f, false);
            _s.Point("Glint", FaceCentre + new Vector3(0.05f, 0.06f, 0.12f), new Color(1f, 1f, 1f), 0.05f, 0.25f);
            // from the side, so the black lever and the valve keep their shape instead of a flat silhouette
            _s.Spot("Side", FaceCentre + new Vector3(0.35f, 0.25f, 0.12f), FaceCentre + new Vector3(0, 0.07f, 0), new Color(1f, 0.97f, 0.92f), 1.6f, 3f, 35f, false);

            // a close look at the gauge: the dial fills most of the picture, the valve at the edges
            var eye = FaceCentre + new Vector3(0.045f, 0.03f, 0.2f);
            _s.Aim(eye, FaceCentre + new Vector3(0.0f, -0.001f, 0f), lowVision ? FovLowVision : Fov);
            _s.Untoned();
            // a macro lens: the dial sharp, the valve and lever behind it soft (their 1k texture never shows its pixels)
            _s.Focus(Vector3.Distance(eye, FaceCentre), 4f, 70f);
        }

        /// <summary>Points the needle at <paramref name="v"/> (0 the left stop, 1 the right stop, over 240°).</summary>
        public void Set(double v)
        {
            float a = 120f - 240f * Mathf.Clamp01((float)v); // as ExtGaugeScreen.NeedleAngle: + turns left, as seen
            _needle.localRotation = Quaternion.Euler(0, 0, -a);   // seen from +z (the camera), a turn about +z reads clockwise
            _s.Invalidate();
        }

        static Texture2D _dial;

        /// <summary>The dial face, 512 px: cream paper, the three bands (ExtinguisherCheck), black ticks, a fine print ring.</summary>
        public static Texture2D DialTexture
        {
            get
            {
                if (_dial) return _dial;
                const int n = 512;
                var t = StudioShared.Keep(new Texture2D(n, n, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 }, "GaugeDial");
                var px = new Color32[n * n];
                var paper = new Color(0.94f, 0.93f, 0.88f);
                var yellow = Low;
                var green = Normal;
                var red = High;
                var ink = new Color(0.08f, 0.08f, 0.09f);
                var rim = new Color(0.55f, 0.56f, 0.57f);
                float px1 = 2f / n; // one pixel in radius units (for anti-aliased edges)
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float ang = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg; // 0 up, + to the right
                        float v = (ang + 120f) / 240f;
                        var c = paper * (0.97f + 0.03f * (1 - r)); // a faint vignette on the paper
                        // the bands
                        if (r > 0.6f && r < 0.84f && v >= 0 && v <= 1)
                        {
                            var band = v < AiOn.Game.ExtinguisherCheck.GreenFrom ? yellow : v <= AiOn.Game.ExtinguisherCheck.GreenTo ? green : red;
                            float e = Mathf.Min(Mathf.Clamp01((r - 0.6f) / px1), Mathf.Clamp01((0.84f - r) / px1));
                            c = Color.Lerp(c, band, e);
                        }
                        // ticks: 21 over the arc, every fifth longer
                        if (v >= -0.01f && v <= 1.01f)
                        {
                            float step = v * 20f, k = Mathf.Round(step);
                            float off = Mathf.Abs(step - k) * (240f / 20f) * Mathf.Deg2Rad * r; // distance along the arc
                            bool major = ((int)k % 5) == 0;
                            float inner = major ? 0.84f : 0.86f, w = major ? 0.018f : 0.01f;
                            if (r > inner && r < 0.94f && k >= 0 && k <= 20)
                                c = Color.Lerp(c, ink, Mathf.Clamp01((w - off) / px1));
                        }
                        // a fine print ring inside the bands
                        float ring = Mathf.Abs(r - 0.52f);
                        c = Color.Lerp(c, ink * 1.5f, 0.6f * Mathf.Clamp01((0.006f - ring) / px1));
                        // the metal rim at the edge, transparent outside
                        if (r > 0.96f) c = Color.Lerp(c, rim, Mathf.Clamp01((r - 0.96f) / px1));
                        float a = Mathf.Clamp01((1f - r) / px1);
                        c.a = a;
                        px[y * n + x] = c;
                    }
                t.SetPixels32(px);
                t.Apply(true, true);
                return _dial = t;
            }
        }
    }
}
