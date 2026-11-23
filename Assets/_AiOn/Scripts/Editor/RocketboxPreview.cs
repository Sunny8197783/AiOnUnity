using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiOn.Levels;
using AiOn.People;
using AiOn.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace AiOn.EditorTools
{
    /// <summary>
    /// Check renders of the Rocketbox avatars, made in a preview scene (the open scene is not touched), with the game
    /// camera's anti-aliasing (SMAA High, no MSAA, as PlayerSetup/PC_RPAsset): a labelled lineup of all 16 (front and
    /// three-quarter), close-ups of every hero from the front, 3/4, side and back, one sheet of the crowd and spares,
    /// and three heroes under the game's looks (EnvironmentController presets + the BaseLook volume). Written to
    /// Captures/rocketbox/ (gitignored). Bodies are posed through the Humanoid muscles, which also proves the avatar map.
    /// </summary>
    public static class RocketboxPreview
    {
        public const string OutDir = "Captures/rocketbox";
        static readonly string[] LookPresets = { "day", "interiorNight", "blackout", "underground" };

        [MenuItem("AiOn/People/Rocketbox Check Shots")]
        public static void ShootAll()
        {
            Directory.CreateDirectory(OutDir);
            Lineup(OutDir + "/lineup_front.png", 0f, true);
            Lineup(OutDir + "/lineup_34.png", 35f, false);
            foreach (var e in RocketboxCast.All.Where(c => c.Tier == RocketboxCast.Tier.Hero))
                foreach (var yaw in new[] { 0, 35, 90, 180 })
                    Closeup(e.Avatar, $"{OutDir}/close_{e.Preset}_{YawName(yaw)}.png", yaw);
            CrowdSheet(OutDir + "/crowd_sheet.png");
            foreach (var p in new[] { "seoyeon", "elder", "jeongmin" }) LookSheet(p, $"{OutDir}/looks_{p}.png");
            Debug.Log("[AiOn] Rocketbox check shots in " + Path.GetFullPath(OutDir));
        }

        static string YawName(float yaw) => yaw == 0 ? "front" : yaw == 35 ? "34" : yaw == 90 ? "side" : yaw == 180 ? "back" : yaw.ToString("0");

        public sealed class Stage : System.IDisposable
        {
            public Scene Scene;
            public Camera Cam;
            readonly List<Object> _temp = new List<Object>();
            readonly List<Light> _lights = new List<Light>();

            public Stage(bool neutralLights = true)
            {
                Scene = EditorSceneManager.NewPreviewScene();
                var camGo = Add(new GameObject("cam"));
                Cam = camGo.AddComponent<Camera>();
                Cam.scene = Scene;
                Cam.clearFlags = CameraClearFlags.SolidColor;
                Cam.backgroundColor = new Color(0.42f, 0.43f, 0.45f);
                Cam.nearClipPlane = 0.05f; Cam.farClipPlane = 200f;
                Cam.cameraType = CameraType.Game;
                var data = camGo.AddComponent<UniversalAdditionalCameraData>();
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; // the game camera's AA
                data.antialiasingQuality = AntialiasingQuality.High;
                data.renderPostProcessing = false;
                if (neutralLights)
                {
                    Light(new Vector3(40, -30, 0), 1.6f, new Color(1f, 0.97f, 0.92f), LightShadows.Soft);
                    Light(new Vector3(25, 150, 0), 0.55f, new Color(0.85f, 0.9f, 1f), LightShadows.None);
                    Light(new Vector3(-60, 0, 0), 0.35f, Color.white, LightShadows.None); // from below-front: stands in for ambient
                }
                var ground = Add(GameObject.CreatePrimitive(PrimitiveType.Plane));
                ground.transform.localScale = Vector3.one * 10f;
                var gm = new Material(Shader.Find("Universal Render Pipeline/Lit")); _temp.Add(gm);
                gm.SetColor("_BaseColor", new Color(0.35f, 0.35f, 0.36f)); gm.SetFloat("_Smoothness", 0.2f);
                ground.GetComponent<Renderer>().sharedMaterial = gm;
            }

            public Light Light(Vector3 euler, float intensity, Color c, LightShadows shadows)
            {
                var go = Add(new GameObject("light"));
                go.transform.rotation = Quaternion.Euler(euler);
                var l = go.AddComponent<Light>();
                l.type = LightType.Directional; l.intensity = intensity; l.color = c; l.shadows = shadows;
                l.shadowStrength = 0.8f;
                _lights.Add(l);
                return l;
            }

            public GameObject Add(GameObject go) { SceneManager.MoveGameObjectToScene(go, Scene); return go; }
            public void Temp(Object o) => _temp.Add(o);

            public Texture2D RenderTex(int w, int h)
            {
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                Cam.targetTexture = rt;
                Cam.aspect = (float)w / h;
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(Cam, req)) RenderPipeline.SubmitRenderRequest(Cam, req);
                else Cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                Cam.targetTexture = null;
                rt.Release(); Object.DestroyImmediate(rt);
                return tex;
            }

            public void Render(string path, int w, int h)
            {
                var tex = RenderTex(w, h);
                Save(tex, path);
            }

            public void Dispose()
            {
                foreach (var o in _temp) if (o) Object.DestroyImmediate(o);
                EditorSceneManager.ClosePreviewScene(Scene);
            }
        }

        static void Save(Texture2D tex, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>Places one avatar at x, scaled to the cast's Korean-average height, posed through the Humanoid muscles.</summary>
        public static GameObject Place(Stage s, RocketboxCast.Entry e, float x, bool musclesZero = true)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(RocketboxCast.ModelPath(e.Avatar));
            var go = s.Add((GameObject)Object.Instantiate(model));
            go.name = e.Avatar;
            go.transform.position = Vector3.zero; // posed at the origin, then moved (HumanPose body position ignores a moved root)
            go.transform.localScale = Vector3.one * e.KoreanScale;
            var anim = go.GetComponent<Animator>();
            if (musclesZero && anim && anim.avatar && anim.avatar.isHuman)
            {
                var h = new HumanPoseHandler(anim.avatar, go.transform);
                var pose = new HumanPose();
                h.GetHumanPose(ref pose);
                for (int i = 0; i < pose.muscles.Length; i++) pose.muscles[i] = RelaxedMuscle(HumanTrait.MuscleName[i], pose.muscles[i]);
                h.SetHumanPose(ref pose);
                h.Dispose();
                // stand on the ground again
                var foot = go.GetComponentsInChildren<Transform>().First(t => t.name == "Bip01 L Foot");
                go.transform.position += Vector3.up * (0.1f * e.KoreanScale - foot.position.y);
            }
            go.transform.position += new Vector3(x, 0, 0);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;
            return go;
        }

        /// <summary>
        /// A relaxed standing pose in muscle space, starting from the file's rest pose (A-pose, legs straight): arms
        /// lowered to the sides, elbows slightly bent, fingers loosely curled; everything else as in the rest pose.
        /// </summary>
        static float RelaxedMuscle(string muscle, float rest)
        {
            if (muscle.EndsWith("Arm Down-Up")) return Mathf.Max(-1f, rest - 0.8f);
            if (muscle.EndsWith("Forearm Stretch")) return Mathf.Min(rest, 0.8f);
            if (muscle.Contains("Stretched")) return 0.2f;
            return rest;
        }

        static IEnumerable<RocketboxCast.Entry> LineupOrder() =>
            RocketboxCast.All.Where(e => e.Tier == RocketboxCast.Tier.Hero)
                .Concat(RocketboxCast.All.Where(e => e.Tier != RocketboxCast.Tier.Hero));

        /// <summary>A flat text label standing on the ground in front of a figure (legacy TextMesh, built-in font).</summary>
        static void Label(Stage s, string text, float x, float yaw)
        {
            var go = s.Add(new GameObject("label"));
            var tm = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font; tm.text = text; tm.fontSize = 64; tm.characterSize = 0.012f;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = Color.white;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            go.transform.position = new Vector3(x, 0.06f, 0.45f);
            go.transform.rotation = Quaternion.Euler(0, yaw + 180f, 0);
        }

        public static void Lineup(string path, float yaw, bool labels)
        {
            using (var s = new Stage())
            {
                var order = LineupOrder().ToList();
                const float gap = 0.8f;
                float x0 = -(order.Count - 1) * gap * 0.5f;
                for (int i = 0; i < order.Count; i++)
                {
                    float x = -(x0 + i * gap); // heroes first, left to right seen from the front
                    Place(s, order[i], x);
                    if (labels) Label(s, (order[i].Preset ?? "spare") + "\n" + order[i].Avatar.Replace("_", " "), x, yaw);
                }
                var target = new Vector3(0, 0.9f, 0);
                var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                const float dist = 34f;
                s.Cam.fieldOfView = 2f * Mathf.Atan(1.3f / dist) * Mathf.Rad2Deg; // 2.6 m tall frame, long lens
                s.Cam.transform.position = target + dir * dist + Vector3.up * 0.3f;
                s.Cam.transform.LookAt(target);
                s.Render(path, 3600, 700);
            }
        }

        static void AimHead(Stage s, GameObject go, RocketboxCast.Entry e, float yaw, float dist = 1.45f, float fov = 16f)
        {
            var head = go.GetComponentsInChildren<Transform>().First(t => t.name == "Bip01 Head");
            var target = head.position + Vector3.up * 0.07f * e.KoreanScale;
            var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            s.Cam.fieldOfView = fov;
            s.Cam.transform.position = target + dir * dist + Vector3.up * 0.02f;
            s.Cam.transform.LookAt(target);
        }

        public static void Closeup(string avatar, string path, float yaw, Texture2D headColor = null, Texture2D headNormal = null)
        {
            using (var s = new Stage())
            {
                var e = RocketboxCast.ForAvatar(avatar);
                var go = Place(s, e, 0f);
                if (headColor || headNormal)
                {
                    var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                    var mats = smr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (!mats[i].name.EndsWith("_head")) continue;
                        var m = new Material(mats[i]); s.Temp(m);
                        if (headColor) m.SetTexture("_BaseMap", headColor);
                        if (headNormal) m.SetTexture("_BumpMap", headNormal);
                        mats[i] = m;
                    }
                    smr.sharedMaterials = mats;
                }
                AimHead(s, go, e, yaw);
                s.Render(path, 1000, 1000);
            }
        }

        /// <summary>Head-and-shoulders 3/4 views of the crowd and the spares (1024 maps), one sheet.</summary>
        public static void CrowdSheet(string path)
        {
            var list = RocketboxCast.All.Where(e => e.Tier != RocketboxCast.Tier.Hero).ToList();
            const int cell = 500, cols = 5;
            int rows = (list.Count + cols - 1) / cols;
            var sheet = new Texture2D(cols * cell, rows * cell, TextureFormat.RGB24, false);
            var fill = Enumerable.Repeat(new Color32(40, 40, 44, 255), sheet.width * sheet.height).ToArray();
            sheet.SetPixels32(fill);
            for (int i = 0; i < list.Count; i++)
            {
                using (var s = new Stage())
                {
                    var go = Place(s, list[i], 0f);
                    AimHead(s, go, list[i], 30f, 2.2f, 17f);
                    var t = s.RenderTex(cell, cell);
                    sheet.SetPixels(i % cols * cell, (rows - 1 - i / cols) * cell, cell, cell, t.GetPixels());
                    Object.DestroyImmediate(t);
                }
            }
            sheet.Apply();
            Save(sheet, path);
        }

        /// <summary>
        /// One hero, 3/4 close-up, under each game look: the preset's key light (colour, intensity x keyScale, elevation;
        /// turned to light the face), its trilight ambient as a probe, a warm room lamp for lit interiors, and the post
        /// grade EnvironmentController applies (ACES, exposure, saturation, contrast, tint, bloom, vignette) over the
        /// BaseLook volume at the preset's weight. An approximation of the level lighting, for skin, hair and eyes.
        /// </summary>
        public static void LookSheet(string preset, string path)
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_AiOn/Data/Source/env-presets.json");
            var all = EnvPreset.ParseAll(json.text);
            var e = RocketboxCast.ForPreset(preset);
            const int cell = 700;
            var sheet = new Texture2D(LookPresets.Length * cell, cell, TextureFormat.RGB24, false);
            for (int i = 0; i < LookPresets.Length; i++)
            {
                var env = all[LookPresets[i]];
                using (var s = new Stage(false))
                {
                    var go = Place(s, e, 0f);
                    AimHead(s, go, e, 35f, 1.6f, 18f);
                    var key = s.Light(new Vector3(Mathf.Clamp(env.Key.y, 10f, 70f), 200f, 0), env.KeyIntensity * 0.42f, env.KeyColor, LightShadows.Soft);
                    float fill = env.FillIntensity * 1.6f;
                    var sh = new SphericalHarmonicsL2();
                    var equator = Color.Lerp(env.SkyFill, env.GroundFill, 0.5f) * fill;
                    sh.AddAmbientLight(equator);
                    sh.AddDirectionalLight(Vector3.up, (env.SkyFill * fill - equator) * 0.8f, 1f);
                    sh.AddDirectionalLight(Vector3.down, (env.GroundFill * fill - equator) * 0.8f, 1f);
                    var shs = new[] { sh };
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                    {
                        r.lightProbeUsage = LightProbeUsage.CustomProvided;
                        var mpb = new MaterialPropertyBlock();
                        mpb.CopySHCoefficientArraysFrom(shs);
                        r.SetPropertyBlock(mpb);
                    }
                    if (!env.Outdoor && env.Lit.Count > 0 && env.Lit.Values.Any(v => v > 0.1f))
                    {
                        var lamp = s.Add(new GameObject("lamp")).AddComponent<Light>();
                        lamp.type = LightType.Point; lamp.color = new Color(1f, 0.86f, 0.68f); lamp.intensity = 1.6f; lamp.range = 5f;
                        lamp.shadows = LightShadows.Soft;
                        lamp.transform.position = go.transform.position + new Vector3(-0.9f, 2.3f, 1.1f);
                    }
                    var data = s.Cam.GetComponent<UniversalAdditionalCameraData>();
                    data.renderPostProcessing = true;
                    s.Cam.backgroundColor = env.Outdoor ? new Color(0.30f, 0.33f, 0.37f) * (1f - 0.8f * env.NightAmount) : new Color(0.12f, 0.12f, 0.13f);

                    var baseLook = Resources.Load<VolumeProfile>("Look/AiOn_BaseLook");
                    if (baseLook)
                    {
                        var bv = s.Add(new GameObject("baseLook")).AddComponent<Volume>();
                        bv.isGlobal = true; bv.priority = 5; bv.weight = AtmosphereMath.BaseWeightFor(env.Name); bv.sharedProfile = baseLook;
                    }
                    var prof = ScriptableObject.CreateInstance<VolumeProfile>(); s.Temp(prof);
                    var tone = prof.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
                    var col = prof.Add<ColorAdjustments>(true);
                    col.postExposure.Override(Mathf.Log(Mathf.Max(0.05f, env.Exposure), 2f) + env.GradeExposure);
                    col.saturation.Override((env.Saturation - 1f) * 100f + AtmosphereMath.SaturationBias(AtmosphereMath.BaseWeightFor(env.Name)));
                    col.contrast.Override((env.Contrast - 1f) * 100f);
                    col.colorFilter.Override(Color.Lerp(Color.white, env.Tint, Mathf.Clamp01(env.TintAmount)));
                    var bloom = prof.Add<Bloom>(true);
                    bloom.intensity.Override(env.BloomStrength * 1.6f); bloom.threshold.Override(Mathf.Max(0.6f, env.BloomThreshold));
                    var vig = prof.Add<Vignette>(true); vig.intensity.Override(env.Vignette * 0.8f); vig.smoothness.Override(0.45f);
                    var pv = s.Add(new GameObject("envGrade")).AddComponent<Volume>();
                    pv.isGlobal = true; pv.priority = 10; pv.sharedProfile = prof;

                    var t = s.RenderTex(cell, cell);
                    sheet.SetPixels(i * cell, 0, cell, cell, t.GetPixels());
                    Object.DestroyImmediate(t);
                    key.enabled = false;
                }
            }
            sheet.Apply();
            Save(sheet, path);
        }
    }
}
