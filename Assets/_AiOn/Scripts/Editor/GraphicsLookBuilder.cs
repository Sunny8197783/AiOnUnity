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
            AddDecalFeature();
            BuildDecalMaterials();
            BuildFxMaterials();
            TuneAmbientOcclusion();
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
        /// URP decals on every renderer (once): DBuffer (albedo + normal) on the PC renderer, which already has the depth-normals
        /// prepass for SSAO; screen-space on the mobile one (no extra prepass). LevelDecals places the projectors.
        /// </summary>
        static void AddDecalFeature()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (!data) continue;
                var feature = data.rendererFeatures.OfType<DecalRendererFeature>().FirstOrDefault();
                if (!feature)
                {
                    feature = ScriptableObject.CreateInstance<DecalRendererFeature>();
                    feature.name = "Decal";
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
                bool mobile = data.name.Contains("Mobile");
                var fso = new SerializedObject(feature);
                fso.FindProperty("m_Settings.technique").intValue = mobile ? 2 : 1; // ScreenSpace : DBuffer
                fso.FindProperty("m_Settings.maxDrawDistance").floatValue = 80f;
                fso.FindProperty("m_Settings.dBufferSettings.surfaceData").intValue = 1; // albedo + normal
                fso.FindProperty("m_Settings.screenSpaceSettings.normalBlend").intValue = 0;
                fso.ApplyModifiedProperties();
                feature.SetActive(true);
                EditorUtility.SetDirty(data);
                Debug.Log($"[Look] decal feature on {data.name} ({(mobile ? "screen space" : "DBuffer")})");
            }
        }

        /// <summary>
        /// Contact shadows that read (the renderer's SSAO was a faint 0.4 at a 0.3 m radius): stronger and wider on the PC
        /// renderer, still from the depth-normals prepass, after-opaque off (it darkens the ambient only, not the lamps).
        /// </summary>
        static void TuneAmbientOcclusion()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (!data || data.name.Contains("Mobile")) continue;
                foreach (var f in data.rendererFeatures)
                {
                    if (!f || f.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                    var so = new SerializedObject(f);
                    so.FindProperty("m_Settings.Intensity").floatValue = 0.85f;
                    so.FindProperty("m_Settings.Radius").floatValue = 0.45f;
                    so.FindProperty("m_Settings.DirectLightingStrength").floatValue = 0.2f;
                    so.FindProperty("m_Settings.Falloff").floatValue = 60f;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(data);
                    Debug.Log($"[Look] SSAO tuned on {data.name}");
                }
            }
        }

        /// <summary>
        /// The decal materials LevelDecals loads (Resources/Decals/&lt;picture&gt;.mat on URP's decal shader graph), and the
        /// puddle: a thin transparent URP Lit sheet, dark and nearly mirror-smooth, its shape in the picture's alpha.
        /// </summary>
        static void BuildDecalMaterials()
        {
            const string dir = "Assets/_AiOn/Resources/Decals";
            var decal = Shader.Find("Shader Graphs/Decal");
            if (!decal) { Debug.LogError("[Look] URP decal shader not found"); return; }
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = Path.GetFileNameWithoutExtension(path);
                if (name == "puddle_mask") continue;
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var mpath = $"{dir}/{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(mpath);
                if (!m) { m = new Material(decal); AssetDatabase.CreateAsset(m, mpath); }
                m.shader = decal;
                m.SetTexture("Base_Map", tex);
                m.SetFloat("Normal_Blend", 0f);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
            }
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var ppath = $"{dir}/Puddle.mat";
            var pm = AssetDatabase.LoadAssetAtPath<Material>(ppath);
            if (!pm) { pm = new Material(lit); AssetDatabase.CreateAsset(pm, ppath); }
            pm.shader = lit;
            pm.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/puddle_mask.png"));
            pm.SetColor("_BaseColor", new Color(0.05f, 0.055f, 0.06f, 0.8f));
            pm.SetFloat("_Smoothness", 0.97f);
            // the mask's alpha fades the reflection too: with "preserve specular" the whole quad kept its sheen and read
            // as a pale rectangle on the floor round a dark blob
            pm.SetFloat("_BlendModePreserveSpecular", 0f);
            pm.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            pm.SetFloat("_Metallic", 0f);
            pm.SetFloat("_Surface", 1f);
            pm.SetFloat("_Blend", 0f);
            pm.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            pm.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            pm.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            pm.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            pm.SetFloat("_ZWrite", 0f);
            pm.SetFloat("_ReceiveShadows", 1f);
            pm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            pm.SetOverrideTag("RenderType", "Transparent");
            pm.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 10;
            EditorUtility.SetDirty(pm);
        }

        /// <summary>
        /// The fire's and the room smoke's particle materials (Resources/Fx, see StoryFx.FlameSheetMat): URP Particles/Unlit
        /// with the flipbook picture, soft particles (fade over 0.35 m against the depth buffer) and camera fading.
        /// </summary>
        static void BuildFxMaterials()
        {
            const string dir = "Assets/_AiOn/Resources/Fx";
            var unlit = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            // smoke takes the scene's light (the fire's orange glow from below, the moon, a torch): an unlit grey puff
            // read as a glowing orb in a dark corridor
            var lit = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
            foreach (var (name, tex, additive) in new[] { ("FxFlame", "fx_flame", true), ("FxSmoke", "fx_smoke", false) })
            {
                var path = $"{dir}/{name}.mat";
                var shader = additive || !lit ? unlit : lit;
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
                m.shader = shader;
                if (!additive) { m.SetFloat("_ReceiveShadows", 0f); m.EnableKeyword("_RECEIVE_SHADOWS_OFF"); m.SetFloat("_Smoothness", 0f); m.SetColor("_SpecColor", Color.black); }
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{tex}.png"));
                // the flames are additive and their tongue picture covers less than a soft dot: an HDR gain lets them read
                // (and reach the bloom) without piling into a white blur
                m.SetColor("_BaseColor", additive ? new Color(2.4f, 2.4f, 2.4f, 1f) : Color.white);
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", additive ? 2f : 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
                m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_BLENDMODE_ADD"); m.DisableKeyword("_BLENDMODE_ALPHA");
                m.EnableKeyword(additive ? "_BLENDMODE_ADD" : "_BLENDMODE_ALPHA");
                m.SetFloat("_SoftParticlesEnabled", 1f);
                m.SetFloat("_SoftParticlesNearFadeDistance", 0f);
                // flames rise out of whatever burns: a short fade, or the tongues inside the boxes vanish
                float soft = additive ? 0.08f : 0.35f;
                m.SetFloat("_SoftParticlesFarFadeDistance", soft);
                m.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / soft, 0f, 0f));
                m.EnableKeyword("_SOFTPARTICLES_ON");
                m.SetFloat("_CameraFadingEnabled", 1f);
                m.SetFloat("_CameraNearFadeDistance", 0.3f);
                m.SetFloat("_CameraFarFadeDistance", 0.9f);
                m.SetVector("_CameraFadeParams", new Vector4(0.3f, 1f / 0.6f, 0f, 0f));
                m.EnableKeyword("_FADING_ON");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                EditorUtility.SetDirty(m);
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
