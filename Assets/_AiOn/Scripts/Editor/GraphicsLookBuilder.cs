using System.IO;
using System.Linq;
using AiOn.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AiOn.EditorTools
{
    /// <summary>
    /// AiOn → Graphics → Build Look Assets: the volume profiles AtmosphereDirector loads (Resources/Look), the guide halo
    /// material, the ScreenFx renderer feature on every URP renderer, and the shadow cascade splits for first person.
    /// Safe to run again: profiles are rebuilt in place (same GUIDs), the feature is added once.
    /// </summary>
    public static class GraphicsLookBuilder
    {
        const string Dir = "Assets/_AiOn/Resources/Look";

        [MenuItem("AiOn/Graphics/Build Look Assets")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(Dir);
            BuildBaseLook();
            BuildEmergency();
            BuildRadioSignal();
            BuildMaskView();
            BuildHaloMaterial();
            BuildMaterial("Outline", "AiOn/Outline");
            AddScreenFxFeature();
            TuneShadows();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Look] look assets, renderer feature and shadows ready");
        }

        static VolumeProfile Profile(string name)
        {
            var path = $"{Dir}/{name}.asset";
            // sub-assets whose script went missing (a moved class) cannot be removed one by one: start the file over
            if (AssetDatabase.LoadAllAssetsAtPath(path).Any(o => o == null)) AssetDatabase.DeleteAsset(path);
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (!p)
            {
                p = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(p, path);
            }
            // start clean: drop the old overrides (sub-assets) so a rebuild never keeps stale values
            foreach (var c in p.components.ToList()) { p.Remove(c.GetType()); Object.DestroyImmediate(c, true); }
            return p;
        }

        static T Add<T>(VolumeProfile p) where T : VolumeComponent
        {
            var c = p.Add<T>(true);
            c.name = typeof(T).Name;
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, p);
            return c;
        }

        /// <summary>The everyday look: cool slate shadows, a faint warmth kept in the highlights, a weak far blur.</summary>
        static void BuildBaseLook()
        {
            var p = Profile("AiOn_BaseLook");
            var smh = Add<ShadowsMidtonesHighlights>(p);
            smh.shadows.Override(new Vector4(0.9f, 0.98f, 1.1f, -0.03f));
            smh.midtones.Override(new Vector4(1f, 0.99f, 1.01f, 0f));
            smh.highlights.Override(new Vector4(1.02f, 1f, 0.98f, -0.1f)); // pulls the blown interior walls back a little
            var split = Add<SplitToning>(p);
            split.shadows.Override(new Color(0.41f, 0.46f, 0.55f)); // slate blue-teal (more green tints lit walls)
            split.highlights.Override(new Color(0.53f, 0.5f, 0.48f));
            split.balance.Override(-45f); // the tone stays in the shadows
            var dof = Add<DepthOfField>(p);
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(28f);
            dof.gaussianEnd.Override(160f);
            dof.gaussianMaxRadius.Override(0.6f);
            dof.highQualitySampling.Override(false);
            EditorUtility.SetDirty(p);
        }

        /// <summary>Air raid, blast, smoke: red grade, hard vignette, coarse grain, lens fringe. Weighted by the director.</summary>
        static void BuildEmergency()
        {
            var p = Profile("AiOn_Emergency");
            var ca = Add<ColorAdjustments>(p);
            ca.colorFilter.Override(new Color(1f, 0.58f, 0.52f));
            ca.saturation.Override(-30f);
            ca.contrast.Override(18f);
            var v = Add<Vignette>(p);
            v.color.Override(new Color(0.22f, 0.01f, 0.01f));
            v.intensity.Override(0.55f);
            v.smoothness.Override(0.45f);
            var g = Add<FilmGrain>(p);
            g.type.Override(FilmGrainLookup.Medium3);
            g.intensity.Override(0.7f);
            g.response.Override(0.6f);
            var chroma = Add<ChromaticAberration>(p);
            chroma.intensity.Override(0.35f);
            EditorUtility.SetDirty(p);
        }

        static void BuildRadioSignal()
        {
            var p = Profile("AiOn_RadioSignal");
            var s = Add<AnalogSignal>(p);
            s.intensity.Override(1f);
            s.scanlines.Override(0.55f);
            s.scanlineCount.Override(300f);
            s.jitter.Override(0.15f);
            s.verticalJump.Override(0f);
            s.horizontalShake.Override(0f);
            s.colorDrift.Override(0.25f);
            s.noise.Override(0.2f);
            var ca = Add<ColorAdjustments>(p);
            ca.saturation.Override(-25f); // a broadcast drains the colour a little
            EditorUtility.SetDirty(p);
        }

        static void BuildMaskView()
        {
            var p = Profile("AiOn_MaskView");
            var m = Add<MaskView>(p);
            m.intensity.Override(1f);
            m.respirator.Override(0f);
            m.mist.Override(0.5f);
            m.breathRate.Override(18f);
            m.rimColor.Override(new Color(0.012f, 0.013f, 0.015f)); // drawn after tone mapping: near black reads as dark rubber
            EditorUtility.SetDirty(p);
        }

        static void BuildHaloMaterial()
        {
            var path = $"{Dir}/GuideHalo.mat";
            var shader = Shader.Find("AiOn/GuideHalo");
            if (!shader) { Debug.LogError("[Look] AiOn/GuideHalo shader not found"); return; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }

        /// <summary>A material in Resources/Look for a shader that is only drawn from code (so builds include it).</summary>
        static void BuildMaterial(string name, string shaderName)
        {
            var path = $"{Dir}/{name}.mat";
            var shader = Shader.Find(shaderName);
            if (!shader) { Debug.LogError($"[Look] {shaderName} shader not found"); return; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            EditorUtility.SetDirty(m);
        }

        /// <summary>ScreenFxFeature on each URP renderer data of the project (once), with its shader assigned.</summary>
        static void AddScreenFxFeature()
        {
            var shader = Shader.Find("Hidden/AiOn/ScreenFx");
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (!data) continue;
                var feature = data.rendererFeatures.OfType<ScreenFxFeature>().FirstOrDefault();
                if (!feature)
                {
                    feature = ScriptableObject.CreateInstance<ScreenFxFeature>();
                    feature.name = "AiOnScreenFx";
                    AssetDatabase.AddObjectToAsset(feature, data);
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                    var so = new SerializedObject(data);
                    var list = so.FindProperty("m_RendererFeatures");
                    var map = so.FindProperty("m_RendererFeatureMap");
                    list.arraySize++;
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                    so.ApplyModifiedProperties();
                }
                var fso = new SerializedObject(feature);
                fso.FindProperty("shader").objectReferenceValue = shader;
                fso.ApplyModifiedProperties();
                feature.SetActive(true);
                EditorUtility.SetDirty(data);
                Debug.Log($"[Look] ScreenFx feature on {data.name}");
            }
        }

        /// <summary>
        /// Shadow cascades for a first-person game: the first cascade ends ~3.5 m out at the high tier's 50 m (hands,
        /// door handles, the shelf in front get the sharpest map), a wider blend border hides the cascade seams.
        /// Distance, cascade count and map size per quality tier stay with QualityTiers.
        /// </summary>
        static void TuneShadows()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (!asset) continue;
                var so = new SerializedObject(asset);
                so.FindProperty("m_Cascade2Split").floatValue = 0.15f;
                so.FindProperty("m_Cascade3Split").vector2Value = new Vector2(0.08f, 0.3f);
                so.FindProperty("m_Cascade4Split").vector3Value = new Vector3(0.07f, 0.2f, 0.45f);
                so.FindProperty("m_CascadeBorder").floatValue = 0.15f;
                so.FindProperty("m_SoftShadowsSupported").boolValue = true;
                so.FindProperty("m_ConservativeEnclosingSphere").boolValue = true;
                so.ApplyModifiedProperties();
                // HDR grading: the cold split toning and the red emergency grade work on the scene's HDR values before
                // ACES, so bright lamps and fire keep their colour instead of clipping to white first
                if (asset.supportsHDR) asset.colorGradingMode = ColorGradingMode.HighDynamicRange;
                EditorUtility.SetDirty(asset);
                Debug.Log($"[Look] shadow cascades tuned on {asset.name}");
            }
        }
    }
}
