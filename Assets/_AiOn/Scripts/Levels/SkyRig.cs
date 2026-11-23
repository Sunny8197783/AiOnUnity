using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AiOn.Levels
{
    /// <summary>
    /// The original's sky rig (engine/sky.js) for URP: an "AiOn/Sky" skybox material (Preetham sky, night dome, stars,
    /// moon, drifting clouds, fade to the fog colour at the horizon) plus the fixed distant world around the level origin:
    /// a ring of 160 city blocks with a tiled window texture and lit windows (light group 'windows'), blinking red
    /// aviation beacons on towers over 90 m, and two unlit hill rings (R = 760 m and 1100 m).
    /// Created at runtime by EnvironmentController; Apply(preset) sets everything from an EnvPreset.
    ///
    /// Brightness: the original's sky shader output went through the same tone mapping as its scene (ACES with
    /// toneMappingExposure = preset exposure). Here the sky goes through the same URP post exposure (log2(exposure) +
    /// exposureOffset) and ACES as the scene, so the sky keeps the original relative balance at _SkyExposure = 1.
    /// skyExposure is a pure look knob on top (e.g. 0.8 if the day sky reads too bright against the fogged town).
    ///
    /// Coordinates: the original is three.js (x east, y up, z south); Unity is (x, y, -z). Directions come from
    /// EnvPreset.DirectionTo (azimuth from north clockwise, north = Unity +z); procedural layouts are generated in the
    /// original's frame and their z is negated on output.
    /// </summary>
    public class SkyRig : MonoBehaviour
    {
        public const float MinFarClip = 1400f;
        /// <summary>presets.js MOON: [azimuth, elevation] of the moon disc.</summary>
        public static readonly Vector2 MoonAzEl = new Vector2(140f, 34f);

        [Tooltip("Multiplier on the whole sky before the horizon fade (1 = the original's balance against the scene).")]
        public float skyExposure = 1f;
        [Tooltip("How much the fog colour hazes the sky just above the horizon (0..1). 0 = the original (no haze layer); " +
                 "the below-horizon fade to the fog colour is what hides the seam.")]
        [Range(0, 1)] public float horizonHaze = 0f;
        [Tooltip("Strength of the distant windows' emission at lit level 1 (the original's kit.lit('windows', mat, 0.9)).")]
        public float windowEmission = 0.9f;

        Material _sky, _city, _hillsNearMat, _hillsFarMat, _beaconMat;
        Texture2D _cityAlbedo, _cityEmissive;
        readonly List<Mesh> _meshes = new List<Mesh>();
        GameObject _world;
        Renderer _beacons;
        bool _built, _beaconsOn;
        float _time;

        public Material SkyMaterial => _sky;
        /// <summary>True when the AiOn/Sky shader loaded (otherwise EnvironmentController keeps its fallback skybox).</summary>
        public bool Ready { get { Build(); return _sky != null; } }

        static readonly int IdSunDir = Shader.PropertyToID("_SunDir"), IdTurbidity = Shader.PropertyToID("_Turbidity"),
            IdRayleigh = Shader.PropertyToID("_Rayleigh"), IdMie = Shader.PropertyToID("_MieCoefficient"),
            IdMieG = Shader.PropertyToID("_MieDirectionalG"), IdSkyGain = Shader.PropertyToID("_SkyGain"),
            IdSkyExposure = Shader.PropertyToID("_SkyExposure"), IdNight = Shader.PropertyToID("_Night"),
            IdZenith = Shader.PropertyToID("_NightZenith"), IdHorizon = Shader.PropertyToID("_NightHorizon"),
            IdGlowColor = Shader.PropertyToID("_GlowColor"), IdGlow = Shader.PropertyToID("_Glow"),
            IdStars = Shader.PropertyToID("_Stars"), IdMoonDir = Shader.PropertyToID("_MoonDir"), IdMoon = Shader.PropertyToID("_Moon"),
            IdCover = Shader.PropertyToID("_CloudCover"), IdCloudOpacity = Shader.PropertyToID("_CloudOpacity"),
            IdCloudLit = Shader.PropertyToID("_CloudLit"), IdCloudShade = Shader.PropertyToID("_CloudShade"),
            IdCloudSun = Shader.PropertyToID("_CloudSunDir"), IdCloudTime = Shader.PropertyToID("_CloudTime"),
            IdFogColor = Shader.PropertyToID("_FogColor"), IdHaze = Shader.PropertyToID("_HorizonHaze"),
            IdSkyVis = Shader.PropertyToID("_SkyVis"), IdColor = Shader.PropertyToID("_Color"),
            IdFogAmount = Shader.PropertyToID("_FogAmount"), IdCull = Shader.PropertyToID("_Cull"),
            IdEmissionColor = Shader.PropertyToID("_EmissionColor"), IdShadeLift = Shader.PropertyToID("_ShadeLift");

        /// <summary>Loads a shader from Resources/Shaders (so it ships in builds), falling back to Shader.Find.</summary>
        public static Shader LoadShader(string resource, string shaderName)
        {
            var s = Resources.Load<Shader>("Shaders/" + resource);
            if (s == null) s = Shader.Find(shaderName);
            return s;
        }

        // ------------------------------------------------------------------------------------------------ build

        void Build()
        {
            if (_built) return;
            _built = true;

            var skyShader = LoadShader("AiOnSky", "AiOn/Sky");
            if (skyShader == null || !skyShader.isSupported) { Debug.LogWarning("[SkyRig] AiOn/Sky shader missing or unsupported; using the fallback skybox"); return; }
            _sky = new Material(skyShader) { name = "AiOnSky (runtime)" };

            _world = new GameObject("SkyWorld");
            _world.transform.SetParent(transform, false);
            _world.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // world-fixed around the level origin

            var unlit = LoadShader("AiOnUnlitVertexColorNoFog", "AiOn/UnlitVertexColorNoFog");
            BuildSkyline(unlit);
            if (unlit != null)
            {
                _hillsNearMat = UnlitMaterial(unlit, "HillsNear", 0f, 0f);
                _hillsFarMat = UnlitMaterial(unlit, "HillsFar", 0f, 0f);
                // 광교산 rises to the north (three.js angle 1.5 pi = -z = north)
                AddRenderer("HillsNear", HillRing(HillsNearR, 25f, 110f, 5, new[] { new Vector3(Mathf.PI * 1.5f, 0.35f, 0.9f), new Vector3(0.4f, 0.25f, 0.4f) }), _hillsNearMat);
                AddRenderer("HillsFar", HillRing(HillsFarR, 50f, 150f, 11, new[] { new Vector3(Mathf.PI * 1.35f, 0.5f, 0.6f), new Vector3(2.6f, 0.4f, 0.5f) }), _hillsFarMat);
            }
            else Debug.LogWarning("[SkyRig] AiOn/UnlitVertexColorNoFog shader missing; no hills or beacons");
        }

        /// <summary>Radii of the two hill rings (m).</summary>
        public const float HillsNearR = 760f, HillsFarR = 1100f;

        /// <summary>
        /// How far a hill ring at <paramref name="distance"/> is blended toward the fog colour: the exp² fog's own amount
        /// there, at least <paramref name="floor"/> (the original's fixed haze) and at most <paramref name="cap"/>, so the
        /// ridge line still shows as a faint, pale silhouette over the hazed horizon in every preset (pure; tested).
        /// </summary>
        public static float HillHaze(float floor, float cap, float distance, float fogDensity)
        {
            float d = Mathf.Max(0f, fogDensity) * distance;
            return Mathf.Clamp(1f - Mathf.Exp(-d * d), floor, Mathf.Max(floor, cap));
        }

        /// <summary>Most haze per ring: the far ring always paler than the near one (aerial perspective).</summary>
        public const float HillsNearCap = 0.8f, HillsFarCap = 0.9f;

        /// <summary>Distance (m) whose exp² fog amount sets the sky's horizon haze: the middle of the distant city ring.</summary>
        public const float SkylineHazeR = 450f;

        /// <summary>
        /// How strongly the sky just above the horizon fades to the fog colour (0..0.95): the fog's own amount at the
        /// distant skyline. The fogged city ring and hills then stand against a sky of nearly their own colour, as in real
        /// haze, instead of fog-coloured slabs (or sky-coloured gaps between them) hanging along the top of aerial shots.
        /// Never below <paramref name="floor"/> (the inspector's horizonHaze) (pure; tested).
        /// </summary>
        public static float SkyHorizonHaze(float fogDensity, float floor = 0f)
        {
            float d = Mathf.Max(0f, fogDensity) * SkylineHazeR;
            return Mathf.Clamp(Mathf.Max(floor, 1f - Mathf.Exp(-d * d)), 0f, 0.95f);
        }

        static Material UnlitMaterial(Shader s, string name, float fog, float cull)
        {
            var m = new Material(s) { name = name + " (runtime)" };
            m.SetFloat(IdFogAmount, fog);
            m.SetFloat(IdCull, cull);
            return m;
        }

        Renderer AddRenderer(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_world.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            r.allowOcclusionWhenDynamic = false;
            _meshes.Add(mesh);
            return r;
        }

        // ---- Distant city (sky.js makeSkyline): one mesh of boxes, a tiled 512x512 window texture (8 floors x 8 bays per 32 x 24 m tile).

        void BuildSkyline(Shader unlit)
        {
            var r = new Mulberry32(4242);
            var cells = new List<Vector2Int>();

            var albedo = new Canvas(512);
            albedo.Fill(0, 0, 512, 512, new Color32(0xb9, 0xb4, 0xaa, 255), 1f);
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    albedo.Fill(x * 64, y * 64 + 58, 64, 3, new Color32(0, 0, 0, 255), 0.07f);
                    albedo.Fill(x * 64 + 8, y * 64 + 14, 48, 34, new Color32(0x2b, 0x34, 0x40, 255), 1f);
                    albedo.Fill(x * 64 + 8, y * 64 + 14, 20, 34, new Color32(170, 195, 215, 255), 0.18f);
                    cells.Add(new Vector2Int(x * 64 + 8, y * 64 + 14));
                }
            var emissive = new Canvas(512);
            emissive.Fill(0, 0, 512, 512, new Color32(0, 0, 0, 255), 1f);
            foreach (var c in cells)
                if (r.Next() < 0.42f)
                {
                    var col = r.Next() < 0.75f ? new Color32(0xff, 0xcf, 0x8a, 255) : new Color32(0xd8, 0xec, 0xff, 255);
                    float a = 0.5f + r.Next() * 0.5f;
                    emissive.Fill(c.x, c.y, 48, 34, col, a);
                }
            _cityAlbedo = albedo.ToTexture("CityWindows");
            _cityEmissive = emissive.ToTexture("CityWindowsLit");

            var verts = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            var placed = new List<Vector3>(); // x, z (three.js frame), radius
            var tops = new List<Vector3>();
            int count = 0;
            for (int tries = 0; count < 160 && tries < 1500; tries++)
            {
                float ang = r.Next() * Mathf.PI * 2f, dist = 330f + Mathf.Pow(r.Next(), 0.7f) * 300f;
                float x = Mathf.Cos(ang) * dist, z = Mathf.Sin(ang) * dist, k = r.Next();
                float w, d, h, ry;
                if (k < 0.05f) { w = d = 24f + r.Next() * 8f; h = 95f + r.Next() * 45f; ry = r.Next() * Mathf.PI; }                         // landmark tower
                else if (k < 0.6f) { w = 48f + r.Next() * 30f; d = 13f + r.Next() * 4f; h = 39f + Mathf.Floor(r.Next() * 10f) * 3f; ry = (r.Next() - 0.5f) * 0.3f; } // apartment slab
                else { w = 18f + r.Next() * 26f; d = 14f + r.Next() * 18f; h = 9f + Mathf.Floor(r.Next() * 8f) * 3f; ry = r.Next() * Mathf.PI; } // low-rise
                float rad = Mathf.Max(w, d) * 0.6f;
                bool overlap = false;
                foreach (var p in placed) if (Mathf.Sqrt((p.x - x) * (p.x - x) + (p.y - z) * (p.y - z)) < p.z + rad) { overlap = true; break; }
                if (overlap) continue;
                placed.Add(new Vector3(x, z, rad));
                float off = Mathf.Floor(r.Next() * 8f) / 8f;
                // three.js -> Unity: z negated, rotation about y negated
                AddBox(verts, normals, uvs, tris, new Vector3(x, 0f, -z), Quaternion.Euler(0f, -ry * Mathf.Rad2Deg, 0f), w, h, d, off);
                if (h > 90f) tops.Add(new Vector3(x, h - 1f, -z));
                count++;
            }

            var mesh = new Mesh { name = "DistantCity" };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                _city = new Material(lit) { name = "DistantCity (runtime)" };
                _city.SetTexture("_BaseMap", _cityAlbedo);
                _city.SetColor("_BaseColor", Color.white);
                _city.SetFloat("_Smoothness", 0.1f); // roughness 0.9
                _city.SetFloat("_Metallic", 0f);
                _city.SetTexture("_EmissionMap", _cityEmissive);
                _city.SetVector(IdEmissionColor, Vector4.zero);
                _city.EnableKeyword("_EMISSION");
                _city.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                AddRenderer("DistantCity", mesh, _city);
            }
            else { Debug.LogWarning("[SkyRig] URP Lit shader not found; no distant city"); Object.Destroy(mesh); }

            // Aviation obstruction lights: small red HDR spheres (r = 1.2 m), fogged like their towers, one mesh.
            if (unlit != null && tops.Count > 0)
            {
                _beaconMat = UnlitMaterial(unlit, "Beacons", 1f, 2f);
                _beacons = AddRenderer("Beacons", Spheres(tops, 1.2f, 8, 6), _beaconMat);
            }
        }

        /// <summary>A box from y = -2 to h - 2 with the original's window UVs (sides: width / 32 + off, height / 24; roof: one wall texel).</summary>
        static void AddBox(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 c, Quaternion rot, float w, float h, float d, float off)
        {
            float y0 = -2f, y1 = h - 2f;
            var locals = new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            foreach (var ln in locals)
            {
                bool alongX = Mathf.Abs(ln.x) > 0.5f;
                float halfN = alongX ? w * 0.5f : d * 0.5f, faceW = alongX ? d : w;
                Vector3 nn = rot * ln, right = Vector3.Cross(Vector3.up, -nn).normalized; // right as seen from outside
                Vector3 mid = c + nn * halfN;
                Vector3 l = mid - right * (faceW * 0.5f), rr = mid + right * (faceW * 0.5f);
                float u1 = faceW / 32f + off, v1 = h / 24f;
                int b = v.Count;
                v.Add(new Vector3(l.x, y0, l.z)); uv.Add(new Vector2(off, 0));
                v.Add(new Vector3(l.x, y1, l.z)); uv.Add(new Vector2(off, v1));
                v.Add(new Vector3(rr.x, y1, rr.z)); uv.Add(new Vector2(u1, v1));
                v.Add(new Vector3(rr.x, y0, rr.z)); uv.Add(new Vector2(u1, 0));
                for (int i = 0; i < 4; i++) n.Add(nn);
                Quad(v, t, b, nn);
            }
            {
                Vector3 ax = rot * Vector3.right * (w * 0.5f), az = rot * Vector3.forward * (d * 0.5f), top = new Vector3(c.x, y1, c.z);
                int b = v.Count;
                v.Add(top - ax - az); v.Add(top - ax + az); v.Add(top + ax + az); v.Add(top + ax - az);
                for (int i = 0; i < 4; i++) { n.Add(Vector3.up); uv.Add(new Vector2(0.004f, 0.004f)); }
                Quad(v, t, b, Vector3.up);
            }
        }

        /// <summary>Two triangles over verts b..b+3 (a fan in order), wound so they face along 'outward' (Unity: clockwise = front).</summary>
        static void Quad(List<Vector3> v, List<int> t, int b, Vector3 outward)
        {
            Tri(v, t, b, b + 1, b + 2, outward);
            Tri(v, t, b, b + 2, b + 3, outward);
        }

        static void Tri(List<Vector3> v, List<int> t, int a, int b, int c, Vector3 outward)
        {
            // Unity's front face is clockwise seen from the front; Cross(b - a, c - a) then points towards the viewer.
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), outward) >= 0f) { t.Add(a); t.Add(b); t.Add(c); }
            else { t.Add(a); t.Add(c); t.Add(b); }
        }

        static Mesh Spheres(List<Vector3> centres, float radius, int seg, int rings)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var n = new List<Vector3>(); var col = new List<Color>();
            foreach (var c in centres)
            {
                int b = v.Count;
                for (int j = 0; j <= rings; j++)
                {
                    float th = Mathf.PI * j / rings;
                    for (int i = 0; i <= seg; i++)
                    {
                        float ph = Mathf.PI * 2f * i / seg;
                        var dir = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                        v.Add(c + dir * radius); n.Add(dir); col.Add(Color.white);
                    }
                }
                for (int j = 0; j < rings; j++)
                    for (int i = 0; i < seg; i++)
                    {
                        int a = b + j * (seg + 1) + i, a2 = a + seg + 1;
                        Vector3 outward = (v[a] + v[a2 + 1]) * 0.5f - c;
                        Tri(v, t, a, a2, a2 + 1, outward);
                        Tri(v, t, a, a2 + 1, a + 1, outward);
                    }
            }
            var m = new Mesh { name = "Beacons" };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetColors(col); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            m.UploadMeshData(true);
            return m;
        }

        // ---- Hill ring (sky.js hillRing): unlit silhouette, vertex colour 0.6 at the foot to 1 at the crest.
        // peaks: (angle rad in the three.js frame, width rad, extra height 0..1).
        static Mesh HillRing(float R, float baseH, float amp, uint seed, Vector3[] peaks)
        {
            var r = new Mulberry32(seed);
            const int seg = 220;
            float p0 = r.Next() * 6.28f, p1 = r.Next() * 6.28f, p2 = r.Next() * 6.28f, p3 = r.Next() * 6.28f;
            var v = new List<Vector3>(); var col = new List<Color>(); var idx = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = (float)i / seg * Mathf.PI * 2f;
                float h = 0.5f + 0.25f * Mathf.Sin(a * 3 + p0) + 0.15f * Mathf.Sin(a * 7 + p1) + 0.08f * Mathf.Sin(a * 13 + p2) + 0.05f * Mathf.Sin(a * 29 + p3);
                foreach (var pk in peaks)
                {
                    float da = Mathf.Atan2(Mathf.Sin(a - pk.x), Mathf.Cos(a - pk.x));
                    h += pk.z * Mathf.Exp(-(da * da) / (pk.y * pk.y));
                }
                float x = Mathf.Cos(a) * R, z = -Mathf.Sin(a) * R; // three.js z = sin(a) R, Unity z = -that
                v.Add(new Vector3(x, -30f, z)); v.Add(new Vector3(x, baseH + amp * Mathf.Max(0f, h), z));
                col.Add(new Color(0.6f, 0.6f, 0.6f, 1f)); col.Add(Color.white);
                if (i < seg) { int k = i * 2; idx.Add(k); idx.Add(k + 1); idx.Add(k + 2); idx.Add(k + 1); idx.Add(k + 3); idx.Add(k + 2); }
            }
            var m = new Mesh { name = "HillRing" + R };
            m.SetVertices(v); m.SetColors(col); m.SetTriangles(idx, 0);
            m.RecalculateBounds();
            m.UploadMeshData(true);
            return m; // double-sided via the material's _Cull = Off
        }

        // ------------------------------------------------------------------------------------------------ apply / update

        /// <summary>Sets every sky uniform, the hill colours and the distant city's lit level from a preset.
        /// Returns false when the sky shader is unavailable (the caller keeps its fallback skybox).</summary>
        /// <param name="power">Grid power: off forces the 'windows' group to 0 like core.js litTarget (city dark, beacons off).</param>
        public bool Apply(EnvPreset e, bool power = true)
        {
            Build();
            if (_sky == null || e == null) return false;

            Vector3 sunDir = EnvPreset.DirectionTo(e.Sun), keyDir = EnvPreset.DirectionTo(e.Key);
            Color fogLin = e.FogColor.linear;
            // presets.js envState: skyVis = noSky ? 0 : 1 (interiors keep the sky for their windows; the car park has none)
            float skyVis = e.NoSky ? 0f : 1f;

            _sky.SetVector(IdSunDir, sunDir);
            _sky.SetFloat(IdTurbidity, e.Turbidity);
            _sky.SetFloat(IdRayleigh, e.Rayleigh);
            _sky.SetFloat(IdMie, e.Mie);
            _sky.SetFloat(IdMieG, e.MieG);
            _sky.SetFloat(IdSkyGain, e.SkyGain);
            _sky.SetFloat(IdSkyExposure, skyExposure);

            _sky.SetFloat(IdNight, e.NightAmount);
            _sky.SetVector(IdZenith, Lin(e.Zenith));
            _sky.SetVector(IdHorizon, Lin(e.Horizon));
            _sky.SetVector(IdGlowColor, Lin(e.GlowColor));
            _sky.SetFloat(IdGlow, e.Glow);
            _sky.SetFloat(IdStars, e.Stars);
            _sky.SetVector(IdMoonDir, EnvPreset.DirectionTo(MoonAzEl));
            _sky.SetFloat(IdMoon, e.Moon);

            _sky.SetFloat(IdCover, e.CloudCover);
            _sky.SetFloat(IdCloudOpacity, e.CloudOpacity);
            _sky.SetVector(IdCloudLit, Lin(e.CloudLit) * e.CloudGain);
            _sky.SetVector(IdCloudShade, Lin(e.CloudShade) * e.CloudGain);
            _sky.SetVector(IdCloudSun, e.Sun.y > -4f ? sunDir : keyDir);
            _sky.SetFloat(IdCloudTime, _time);

            // the photographed sky (day, afternoon, dusk): which one, turned so its bright side faces this preset's sun
            _photoSunAz = e.Sun.x;
            _photo.Want(PhotoFor(e.Name, e.NightAmount));
            var kc = e.KeyColor.linear;
            _sky.SetVector(IdPhotoSun, new Vector4(kc.r, kc.g, kc.b, 0) * (0.6f + 0.4f * Mathf.Clamp01(e.KeyIntensity / 4f)));
            WritePhoto();

            _sky.SetVector(IdFogColor, (Vector4)fogLin);
            _sky.SetFloat(IdSkyVis, skyVis);
            RenderSettings.skybox = _sky;

            Color hills = e.Hills.linear;
            // Aerial perspective for the unlit, unfogged hill rings: hazed toward the fog colour by the exp² fog at their
            // distance (never less than the original's 0.3 / 0.62, never more than HillsNearCap / HillsFarCap so the 광교산
            // ridge stays), with their foot shading flattened by the same amount (at 0.6 it kept them darker than the fogged
            // city in front). The sky behind them is hazed to the fog colour too (below), so they read as pale far ridges.
            float density = RenderSettings.fog ? RenderSettings.fogDensity : 0f;
            float nearHaze = HillHaze(0.3f, HillsNearCap, HillsNearR, density), farHaze = HillHaze(0.62f, HillsFarCap, HillsFarR, density);
            if (_hillsNearMat) { _hillsNearMat.SetVector(IdColor, (Vector4)Color.Lerp(hills, fogLin, nearHaze)); _hillsNearMat.SetFloat(IdShadeLift, nearHaze); }
            if (_hillsFarMat) { _hillsFarMat.SetVector(IdColor, (Vector4)Color.Lerp(hills, fogLin, farHaze)); _hillsFarMat.SetFloat(IdShadeLift, farHaze); }
            _sky.SetFloat(IdHaze, SkyHorizonHaze(density, horizonHaze));

            float windows = !power ? 0f : (e.Lit.TryGetValue("windows", out var wl) ? wl : e.NightAmount);
            float glow = windowEmission * Mathf.Max(0f, windows);
            if (_city) _city.SetVector(IdEmissionColor, new Vector4(glow, glow, glow, 1f)); // linear, like emissiveIntensity
            if (_beaconMat) _beaconMat.SetVector(IdColor, new Vector4(3.2f, 0.26f, 0.15f, 1f));
            _beaconsOn = windows > 0.001f;

            if (_world) _world.SetActive(skyVis > 0.5f);
            UpdateBeacons();
            EnsureFarClip();
            return true;
        }

        static Vector4 Lin(Color srgb) { var c = srgb.linear; return new Vector4(c.r, c.g, c.b, 1f); }

        void Update()
        {
            if (_sky == null) return;
            _time += Time.deltaTime;
            _sky.SetFloat(IdCloudTime, _time);
            StepPhoto(Time.unscaledDeltaTime);
            UpdateBeacons();
            EnsureFarClip();
        }

        // ------------------------------------------------------------------------------------------------ sky photos

        /// <summary>
        /// The photographed skies (Poly Haven "puresky" HDRIs, CC0; Resources/Sky): the sun painted out and the rest stored
        /// as an LDR equirect at a fixed scale. scale undoes that scale (linear); sunU is where the photo's sun was (0..1 of
        /// the width), so the photo is turned to put it at the preset's sun azimuth.
        /// </summary>
        public static readonly (string resource, float scale, float sunU)[] Photos =
        {
            ("Sky/sky_day", 2.41f, 0.595f), ("Sky/sky_afternoon", 3.28f, 0.600f), ("Sky/sky_dusk", 5.26f, 0.606f),
        };

        /// <summary>Linear gain of the photos against the analytic sky (look knob).</summary>
        public const float PhotoGain = 0.4f;
        public const float PhotoFade = 1.5f;

        /// <summary>Which photo a preset shows (index into Photos), -1 for none: the night (and the shelter) keep the analytic dome.</summary>
        public static int PhotoFor(string preset, float nightAmount)
        {
            if (nightAmount >= 0.9f) return -1;
            return preset switch { "afternoon" => 1, "dusk" => 2, _ => 0 };
        }

        /// <summary>The photo's yaw (turns) that puts its sun (at sunU) at the preset's sun azimuth (degrees from north).</summary>
        public static float PhotoYaw(float sunU, float sunAzimuth) => Mathf.Repeat(sunU - sunAzimuth / 360f, 1f);

        static readonly int IdHdriTex = Shader.PropertyToID("_HdriTex"), IdHdriTex2 = Shader.PropertyToID("_HdriTex2"),
            IdHdri = Shader.PropertyToID("_Hdri"), IdHdriMix = Shader.PropertyToID("_HdriMix"),
            IdHdriGain = Shader.PropertyToID("_HdriGain"), IdHdriGain2 = Shader.PropertyToID("_HdriGain2"),
            IdHdriYaw = Shader.PropertyToID("_HdriYaw"), IdHdriYaw2 = Shader.PropertyToID("_HdriYaw2"),
            IdPhotoSun = Shader.PropertyToID("_PhotoSun");
        static readonly Texture2D[] s_photos = new Texture2D[3];
        PhotoBlend _photo = PhotoBlend.None;
        float _photoSunAz;

        static Texture2D PhotoTex(int i)
        {
            if (i < 0 || i >= Photos.Length) return null;
            if (!s_photos[i]) s_photos[i] = Resources.Load<Texture2D>(Photos[i].resource);
            return s_photos[i];
        }

        /// <summary>Shows the wanted photo at once (a cut: a level load, a preset snapped without a blend).</summary>
        public void SnapPhoto()
        {
            _photo.Snap();
            WritePhoto();
        }

        void StepPhoto(float dt)
        {
            if (_photo.Step(dt / PhotoFade)) WritePhoto();
        }

        void WritePhoto()
        {
            if (_sky == null) return;
            var a = PhotoTex(_photo.A);
            bool on = a && _photo.Weight > 0.002f;
            _sky.SetFloat(IdHdri, on ? _photo.Weight : 0f);
            if (!on) return;
            _sky.SetTexture(IdHdriTex, a);
            _sky.SetFloat(IdHdriGain, Photos[_photo.A].scale * PhotoGain);
            _sky.SetFloat(IdHdriYaw, PhotoYaw(Photos[_photo.A].sunU, _photoSunAz));
            var b = PhotoTex(_photo.B);
            _sky.SetFloat(IdHdriMix, b ? _photo.Mix : 0f);
            if (b)
            {
                _sky.SetTexture(IdHdriTex2, b);
                _sky.SetFloat(IdHdriGain2, Photos[_photo.B].scale * PhotoGain);
                _sky.SetFloat(IdHdriYaw2, PhotoYaw(Photos[_photo.B].sunU, _photoSunAz));
            }
        }

        void UpdateBeacons()
        {
            if (_beacons) _beacons.enabled = _beaconsOn && (_time % 1.6f) < 0.8f;
        }

        /// <summary>The skyline ring sits at 330-630 m and the hills at 760 / 1100 m: the main camera must see ~1300 m.</summary>
        static void EnsureFarClip()
        {
            var cam = Camera.main;
            if (cam && cam.farClipPlane < MinFarClip) cam.farClipPlane = MinFarClip;
        }

        void OnDestroy()
        {
            if (_sky && RenderSettings.skybox == _sky) RenderSettings.skybox = null;
            foreach (var m in _meshes) if (m) Destroy(m);
            _meshes.Clear();
            if (_sky) Destroy(_sky);
            if (_city) Destroy(_city);
            if (_hillsNearMat) Destroy(_hillsNearMat);
            if (_hillsFarMat) Destroy(_hillsFarMat);
            if (_beaconMat) Destroy(_beaconMat);
            if (_cityAlbedo) Destroy(_cityAlbedo);
            if (_cityEmissive) Destroy(_cityEmissive);
        }

        // ------------------------------------------------------------------------------------------------ helpers

        /// <summary>The original's PRNG (engine/kit.js rng, mulberry32).</summary>
        sealed class Mulberry32
        {
            uint _a;
            public Mulberry32(uint seed) { _a = seed; }
            public float Next()
            {
                unchecked
                {
                    _a += 0x6D2B79F5u;
                    uint t = _a;
                    t = (t ^ (t >> 15)) * (t | 1u);
                    t ^= t + (t ^ (t >> 7)) * (t | 61u);
                    double v = (t ^ (t >> 14)) / 4294967296.0;
                    return Mathf.Min((float)v, 0.99999994f);
                }
            }
        }

        /// <summary>A tiny 2D canvas (top-left origin like HTML canvas) that blends sRGB bytes like Canvas2D fillRect.</summary>
        sealed class Canvas
        {
            readonly int _size;
            readonly Color32[] _px;
            public Canvas(int size) { _size = size; _px = new Color32[size * size]; }

            public void Fill(int x, int y, int w, int h, Color32 c, float alpha)
            {
                int x0 = Mathf.Max(0, x), x1 = Mathf.Min(_size, x + w), y0 = Mathf.Max(0, y), y1 = Mathf.Min(_size, y + h);
                for (int cy = y0; cy < y1; cy++)
                {
                    int row = (_size - 1 - cy) * _size; // canvas rows run down, texture rows up (three.js flipY)
                    for (int cx = x0; cx < x1; cx++)
                    {
                        var d = _px[row + cx];
                        _px[row + cx] = new Color32(
                            (byte)Mathf.RoundToInt(d.r + (c.r - d.r) * alpha),
                            (byte)Mathf.RoundToInt(d.g + (c.g - d.g) * alpha),
                            (byte)Mathf.RoundToInt(d.b + (c.b - d.b) * alpha), 255);
                    }
                }
            }

            public Texture2D ToTexture(string name)
            {
                var t = new Texture2D(_size, _size, TextureFormat.RGBA32, true, false) // sRGB
                {
                    name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4,
                };
                t.SetPixels32(_px);
                t.Apply(true, true);
                return t;
            }
        }
    }

    /// <summary>
    /// Which sky photos show and how far the cross-fade has gone (pure; tested). A shows at weight Weight; while B is set
    /// the sky fades from A to B by Mix. Going back to A mid-fade turns the fade round (no jump, and the sky does not end
    /// on B); a third photo mid-fade starts from whichever of the two shows more.
    /// </summary>
    public struct PhotoBlend
    {
        public int A, B, Target;
        public float Mix, Weight;

        public static PhotoBlend None => new PhotoBlend { A = -1, B = -1, Target = -2 };

        /// <summary>The preset wants photo want (-1: none, the night's analytic dome).</summary>
        public void Want(int want)
        {
            if (want == Target) return;
            Target = want;
            if (want < 0) return; // the weight fades out; A (and a fade under way) stay for a quick return
            if (A < 0 || Weight <= 0.002f) { A = want; B = -1; Mix = 0f; return; } // nothing on screen: no fade needed
            if (B < 0) { if (want != A) { B = want; Mix = 0f; } return; }
            if (want == B) return;
            if (want == A) { A = B; B = want; Mix = 1f - Mix; return; } // back the way it came
            if (Mix >= 0.5f) A = B; // a third photo: from the one that shows more
            B = want; Mix = 0f;
        }

        /// <summary>Advances the fades by k (seconds / fade time); true when anything moved.</summary>
        public bool Step(float k)
        {
            bool moved = false;
            float wantW = Target >= 0 ? 1f : 0f;
            if (!Mathf.Approximately(Weight, wantW)) { Weight = Mathf.MoveTowards(Weight, wantW, k); moved = true; }
            if (B >= 0)
            {
                Mix = Mathf.MoveTowards(Mix, 1f, k); moved = true;
                if (Mix >= 1f) { A = B; B = -1; Mix = 0f; }
            }
            return moved;
        }

        /// <summary>A cut: the wanted photo at once.</summary>
        public void Snap()
        {
            if (Target >= 0) { A = Target; B = -1; Mix = 0f; Weight = 1f; }
            else Weight = 0f;
        }

        /// <summary>The photo that shows most right now (-1: none).</summary>
        public int Showing => Weight <= 0.002f ? -1 : B >= 0 && Mix >= 0.5f ? B : A;
    }
}
