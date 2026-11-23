// A small offscreen 3D set for a minigame screen (competition upgrade, minigame art): its own camera renders a lit set
// into a RenderTexture that the screen shows in a RawImage, so the CPR patient, the front door, the partition and the
// rest are real people and lit objects instead of flat boxes. Each studio sits below the world (its own slot, near
// enough to the origin that millimetre parts keep their shape in float precision), on its own layer (Layer, which no
// other camera draws) and rendering layer (LightBit): the level's lights never touch it and its lights never touch
// the level; its ambient is its own too (custom SH per renderer), so a set looks the same by day or in a blackout. A
// local post volume (only a camera inside it sees it) gives it a neutral tonemap, so the global looks (emergency,
// radio, mask) stay on the game view. A still picture is drawn only when it changes (Invalidate); a moving one sets
// Live. It removes itself, its texture and the materials it made when its owner (the screen) goes away.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace AiOn.UI.Studio
{
    public sealed class MiniStudio : MonoBehaviour
    {
        /// <summary>The GameObject layer every studio object is on (unnamed in the project; only studio cameras draw it).</summary>
        public const int Layer = 30;
        /// <summary>The URP rendering (light) layer of studio renderers and lights; scene lights use bit 0, people fill bit 3.</summary>
        public const int LightBit = 6;
        public const uint LightMask = 1u << LightBit;

        /// <summary>Studio cameras alive now (ScreenFxFeature leaves them alone).</summary>
        public static readonly HashSet<Camera> Cameras = new();

        static int _slot;
        static bool _hooked;

        /// <summary>The slot origins (set space): x and z inside ±450 m and 500 m down, where a float still resolves 0.03-0.06 mm.</summary>
        public static Vector3 SlotOrigin(int slot) => new(-450f + (slot % 16) * 60f, -500f, 300f);

        public Camera Cam { get; private set; }
        public RenderTexture Texture { get; private set; }
        /// <summary>The set's origin: build the set in its local space (y up, the floor at 0).</summary>
        public Transform Set { get; private set; }

        Object _owner;
        readonly List<Object> _made = new();
        Volume _volume;
        MaterialPropertyBlock _ambientBlock;

        /// <summary>
        /// A studio for <paramref name="owner"/> (it goes when the owner does) rendering <paramref name="pixels"/> into
        /// its texture with <paramref name="background"/> behind the set.
        /// </summary>
        public static MiniStudio Create(string name, Component owner, Vector2Int pixels, Color background)
        {
            var go = new GameObject("Studio_" + name);
            go.layer = Layer;
            go.transform.position = SlotOrigin(_slot++ % 16);
            if (!_hooked) { _hooked = true; RenderPipelineManager.beginCameraRendering += HideFromOtherCameras; }
            var s = go.AddComponent<MiniStudio>();
            s._owner = owner;
            s.Set = go.transform;

            var tex = new RenderTexture(Mathf.Max(16, pixels.x), Mathf.Max(16, pixels.y), 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Studio_" + name, antiAliasing = 1, useMipMap = false,
            };
            tex.Create();
            s.Texture = tex;
            s._made.Add(tex);

            var camGo = new GameObject("Camera");
            camGo.layer = Layer;
            camGo.transform.SetParent(go.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.cullingMask = 1 << Layer;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 40f;
            cam.fieldOfView = 40f;
            cam.depth = -50; // before the game camera
            cam.allowHDR = true;
            cam.allowMSAA = false;
            cam.targetTexture = tex;
            cam.useOcclusionCulling = false;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.volumeLayerMask = 1 << Layer;
            data.volumeTrigger = camGo.transform;
            data.renderShadows = true;
            data.requiresDepthTexture = false;
            data.requiresColorTexture = false;
            s.Cam = cam;
            Cameras.Add(cam);

            s.BuildVolume();
            s.Ambient(new Color(0.006f, 0.007f, 0.009f), Color.black);
            s.Invalidate(); // the first picture (the set is built in this frame)
            return s;
        }

        /// <summary>
        /// The set's own ambient: <paramref name="all"/> from every side plus <paramref name="fromAbove"/> more from above
        /// (spherical harmonics given to each renderer, not the scene's RenderSettings). Applies to what is already built.
        /// </summary>
        public void Ambient(Color all, Color fromAbove)
        {
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(all);
            if (fromAbove.maxColorComponent > 0) sh.AddDirectionalLight(Vector3.up, fromAbove, 1f);
            _ambientBlock ??= new MaterialPropertyBlock();
            _ambientBlock.Clear();
            _ambientBlock.CopySHCoefficientArraysFrom(new[] { sh });
            if (Set) foreach (var r in Set.GetComponentsInChildren<Renderer>(true)) Light(r);
        }

        void Light(Renderer r)
        {
            if (r is not MeshRenderer && r is not SkinnedMeshRenderer) return; // world-space UI keeps its own
            r.lightProbeUsage = LightProbeUsage.CustomProvided;
            r.SetPropertyBlock(_ambientBlock);
        }

        /// <summary>Adopt (layers, no probes) and the set's own ambient.</summary>
        public void Take(GameObject go, bool shadows = true)
        {
            Adopt(go, shadows);
            if (_ambientBlock != null) foreach (var r in go.GetComponentsInChildren<Renderer>(true)) Light(r);
        }

        void BuildVolume()
        {
            var vg = new GameObject("Look");
            vg.layer = Layer;
            vg.transform.SetParent(Set, false);
            var box = vg.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(56, 30, 56); // every view a set aims (the balcony is 20 m along x) well inside
            _volume = vg.AddComponent<Volume>();
            _volume.isGlobal = false; // only a camera inside the box: the game camera never is
            _volume.blendDistance = 0;
            _volume.priority = 50;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _made.Add(profile);
            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.Neutral);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.15f);
            ca.contrast.Override(8f);
            ca.saturation.Override(-6f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.28f);
            vig.smoothness.Override(0.5f);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.35f);
            bloom.threshold.Override(1.1f);
            _volume.sharedProfile = profile;
        }

        /// <summary>The look's colour adjustments (tweak exposure or saturation per set).</summary>
        public ColorAdjustments Grade => _volume && _volume.sharedProfile.TryGet<ColorAdjustments>(out var c) ? c : null;
        public Vignette Vignette => _volume && _volume.sharedProfile.TryGet<Vignette>(out var v) ? v : null;

        /// <summary>
        /// No tonemapping and no grade: printed colours (a dial's bands) come out as printed, so they match the UI's own
        /// colours beside the picture. For a set lit softly enough that nothing needs the highlights rolled off.
        /// </summary>
        public void Untoned()
        {
            if (!_volume) return;
            if (_volume.sharedProfile.TryGet<Tonemapping>(out var tm)) tm.mode.Override(TonemappingMode.None);
            var g = Grade;
            if (g != null) { g.postExposure.Override(0f); g.contrast.Override(0f); g.saturation.Override(0f); }
        }

        /// <summary>A macro lens's shallow focus: sharp at <paramref name="focus"/> m from the camera, soft before and behind.</summary>
        public void Focus(float focus, float aperture, float focalLength)
        {
            if (!_volume) return;
            var dof = _volume.sharedProfile.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(focus);
            dof.aperture.Override(aperture);
            dof.focalLength.Override(focalLength);
        }

        /// <summary>The studio's picture filling <paramref name="parent"/> (under whatever the screen draws on top).</summary>
        public RawImage Show(RectTransform parent)
        {
            var rt = UiKit.Rect("StudioView", parent);
            rt.SetAsFirstSibling();
            UiKit.Fill(rt);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = Texture;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Pixels for a stage of <paramref name="size"/> canvas units on <paramref name="canvas"/> (sharp at the screen's resolution, capped).</summary>
        public static Vector2Int PixelsFor(Canvas canvas, Vector2 size, float cap = 1.5f)
        {
            float k = canvas && canvas.scaleFactor > 0 ? canvas.scaleFactor : Screen.height / 1080f;
            k = Mathf.Clamp(k, 0.5f, cap);
            return new Vector2Int(Mathf.RoundToInt(size.x * k), Mathf.RoundToInt(size.y * k));
        }

        /// <summary>Places the camera at <paramref name="eye"/> (set space) looking at <paramref name="at"/>.</summary>
        public void Aim(Vector3 eye, Vector3 at, float fov)
        {
            Cam.transform.localPosition = eye;
            Cam.transform.rotation = Quaternion.LookRotation(Set.TransformPoint(at) - Set.TransformPoint(eye), Vector3.up);
            Cam.fieldOfView = fov;
        }

        /// <summary>Where a set-space point lands on the picture, 0..1 from the lower left (for overlays).</summary>
        public Vector2 ViewportOf(Vector3 setPoint)
        {
            var v = Cam.WorldToViewportPoint(Set.TransformPoint(setPoint));
            return new Vector2(v.x, v.y);
        }

        // ------------------------------------------------------------------ building

        /// <summary>Puts <paramref name="go"/> and its children on the studio layers (camera layer, light layer, no probes).</summary>
        public static void Adopt(GameObject go, bool shadows = true)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.renderingLayerMask = LightMask;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveShadows = true;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true; // tipped and leaning bodies outgrow their bounds
            }
        }

        public GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale, Material mat, bool shadows = true)
        {
            var p = GameObject.CreatePrimitive(type);
            p.name = name;
            var col = p.GetComponent<Collider>();
            if (col) DestroyImmediate(col);
            p.transform.SetParent(parent ? parent : Set, false);
            p.transform.localPosition = pos;
            p.transform.localRotation = Quaternion.Euler(euler);
            p.transform.localScale = scale;
            p.GetComponent<MeshRenderer>().sharedMaterial = mat;
            Take(p, shadows);
            return p;
        }

        /// <summary>
        /// A box with rounded edges and corners (<paramref name="radius"/>, m) of <paramref name="size"/>: soft goods
        /// (a backpack, a pocket, a strap), a moulded case. Its mesh is made here (a subdivided cube pushed out to the
        /// rounded shape) and owned by the studio.
        /// </summary>
        public GameObject SoftBox(string name, Transform parent, Vector3 pos, Vector3 euler, Vector3 size, float radius, Material mat, bool shadows = true)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent ? parent : Set, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(euler);
            g.AddComponent<MeshFilter>().sharedMesh = Own(StudioMath.RoundedBox(size, radius, 6));
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            Take(g, shadows);
            return g;
        }

        public GameObject Empty(string name, Transform parent, Vector3 pos, Vector3 euler)
        {
            var g = new GameObject(name);
            g.layer = Layer;
            g.transform.SetParent(parent ? parent : Set, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(euler);
            return g;
        }

        /// <summary>A spot light that lights only the studio.</summary>
        public Light Spot(string name, Vector3 pos, Vector3 at, Color color, float intensity, float range, float angle, bool shadows)
        {
            var l = MakeLight(name, LightType.Spot, pos, at, color, intensity, range, shadows);
            l.spotAngle = angle;
            l.innerSpotAngle = angle * 0.45f;
            return l;
        }

        /// <summary>A point light that lights only the studio.</summary>
        public Light Point(string name, Vector3 pos, Color color, float intensity, float range) =>
            MakeLight(name, LightType.Point, pos, pos + Vector3.down, color, intensity, range, false);

        Light MakeLight(string name, LightType type, Vector3 pos, Vector3 at, Color color, float intensity, float range, bool shadows)
        {
            var g = new GameObject(name);
            g.layer = Layer;
            g.transform.SetParent(Set, false);
            g.transform.localPosition = pos;
            var dir = at - pos;
            if (dir.sqrMagnitude > 1e-6f) g.transform.localRotation = Quaternion.LookRotation(dir, Mathf.Abs(Vector3.Dot(dir.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up);
            var l = g.AddComponent<Light>();
            l.type = type;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.cullingMask = 1 << Layer;
            l.renderingLayerMask = (int)LightMask;
            var ad = l.GetUniversalAdditionalLightData();
            ad.renderingLayers = LightMask;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            if (shadows) { l.shadowStrength = 0.85f; l.shadowBias = 0.02f; l.shadowNormalBias = 0.25f; l.shadowNearPlane = 0.05f; }
            return l;
        }

        static Shader _lit;

        /// <summary>A URP Lit material owned by this studio.</summary>
        public Material Lit(Color color, float smooth = 0.3f, float metal = 0f, Texture baseMap = null, Vector2? tiling = null, Texture normal = null, float normalScale = 1f)
        {
            if (!_lit) _lit = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(_lit) { name = "StudioLit" };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            if (baseMap) { m.SetTexture("_BaseMap", baseMap); m.mainTexture = baseMap; }
            if (tiling.HasValue) { m.SetTextureScale("_BaseMap", tiling.Value); m.SetTextureScale("_BumpMap", tiling.Value); }
            if (normal)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", normalScale);
                m.EnableKeyword("_NORMALMAP");
            }
            _made.Add(m);
            return m;
        }

        /// <summary>Makes <paramref name="m"/> alpha-clipped (a round face on a square card).</summary>
        public static Material Cutout(Material m, float cutoff = 0.5f)
        {
            m.SetFloat("_AlphaClip", 1);
            m.SetFloat("_Cutoff", cutoff);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            return m;
        }

        /// <summary>An emissive (unlit-looking) URP Lit material: glow strips, the gap's light.</summary>
        public Material Glow(Color color, float intensity)
        {
            var m = Lit(Color.black, 0f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return m;
        }

        /// <summary>A transparent URP Lit material (smoke, glass) owned by this studio.</summary>
        public Material Clear(Color color, float smooth = 0.5f, Texture baseMap = null)
        {
            var m = Lit(color, smooth, 0, baseMap);
            m.SetFloat("_Surface", 1);
            m.SetFloat("_Blend", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
            m.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        /// <summary>Keeps <paramref name="o"/> until the studio goes (textures and materials made for this set).</summary>
        public T Own<T>(T o) where T : Object { if (o) _made.Add(o); return o; }

        // ------------------------------------------------------------------ people

        /// <summary>
        /// A still copy of a people prefab (UnityStoryHost.peoplePrefabs) for the set: built asleep and stripped of its
        /// scripts and colliders, so nothing that looks for people (talks, crowds, PlayerHands) finds it; its Animator holds
        /// the idle's first frame (written every frame, so bone tweaks in LateUpdate start from the same pose). Null
        /// without a host or the prefab (EditMode, a scene without the Boot host).
        /// </summary>
        public Animator Puppet(string preset, Vector3 pos, float yaw)
        {
            var host = AiOn.Story.UnityStoryHost.Instance;
            GameObject prefab = null;
            if (host != null && host.peoplePrefabs != null)
                foreach (var p in host.peoplePrefabs) if (p && p.name == preset) { prefab = p; break; }
            if (!prefab) return null;
            var holder = new GameObject("Puppet_" + preset);
            holder.layer = Layer;
            holder.transform.SetParent(Set, false);
            holder.SetActive(false);
            var body = Instantiate(prefab, holder.transform, false);
            body.name = preset;
            foreach (var mb in body.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
            foreach (var c in body.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            // carried props (jug, phone, bag...) stay hidden: they are children switched on by PersonProps
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            HairBothSides(body);
            holder.transform.localPosition = pos;
            holder.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            Take(body);
            holder.SetActive(true);
            var an = body.GetComponentInChildren<Animator>(true);
            if (an)
            {
                an.applyRootMotion = false;
                an.updateMode = AnimatorUpdateMode.UnscaledTime;
                an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                an.Play("idle", 0, 0f);
                an.Update(0f);
                an.speed = 0f;
            }
            return an;
        }

        /// <summary>
        /// The hair cards (the avatars' "_opacity" material) drawn from both sides on a studio copy: the game sees heads at
        /// eye level, where the back-face culling of the cards does not show, but these pictures look down on crowns, where
        /// it leaves gaps that read as holes and a painted cap.
        /// </summary>
        void HairBothSides(GameObject body)
        {
            foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = smr.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!m || !m.name.EndsWith("_opacity") || !m.HasProperty("_Cull")) continue;
                    var copy = Own(new Material(m) { name = m.name + "_studio" });
                    copy.SetFloat("_Cull", (float)CullMode.Off);
                    copy.doubleSidedGI = true;
                    mats[i] = copy;
                    changed = true;
                }
                if (changed) smr.sharedMaterials = mats;
            }
        }

        /// <summary>
        /// For a body seen from above and close (the CPR rescuer's crown fills part of the picture): the hair cards go
        /// (flat strips that from up there read as grey sheets over the painted hair) and the head's gloss comes down, so
        /// the painted hair reads as hair rather than a shiny cap. The face keeps its colour and relief.
        /// </summary>
        public void HairFromAbove(GameObject body)
        {
            foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = smr.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!m || !m.HasProperty("_BaseColor")) continue;
                    if (m.name.EndsWith("_opacity_studio"))
                    {
                        var c = m.GetColor("_BaseColor"); c.a = 0f; // every texel under the clip threshold
                        m.SetColor("_BaseColor", c);
                    }
                    else if (m.name.EndsWith("_head") && m.HasProperty("_Smoothness"))
                    {
                        var copy = Own(new Material(m) { name = m.name + "_studio" });
                        copy.SetFloat("_Smoothness", Mathf.Min(m.GetFloat("_Smoothness"), 0.5f));
                        mats[i] = copy;
                        changed = true;
                    }
                }
                if (changed) smr.sharedMaterials = mats;
            }
        }

        /// <summary>Sets a face blendshape (by name or suffix, as PersonFace finds them) on every mesh that has it.</summary>
        public static void Shape(GameObject body, string name, float weight)
        {
            foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = smr.sharedMesh;
                if (!m) continue;
                for (int k = 0; k < m.blendShapeCount; k++)
                    if (m.GetBlendShapeName(k).EndsWith(name, System.StringComparison.Ordinal)) smr.SetBlendShapeWeight(k, weight);
            }
        }

        // ------------------------------------------------------------------ drawing on demand

        /// <summary>True while something in the set moves (CPR presses, smoke): the camera draws every frame.</summary>
        public bool Live
        {
            get => _live;
            set { _live = value; if (value && !_paused) Cam.enabled = true; else Invalidate(); }
        }
        bool _live, _paused;
        int _frames;

        /// <summary>The still set changed: draw it again (two frames, so skinned bodies and bloom settle), then hold the picture.</summary>
        public void Invalidate()
        {
            if (_paused || !Cam) return;
            _frames = 2;
            Cam.enabled = true;
        }

        /// <summary>Stops drawing (the screen shows something else); Resume or Invalidate draws again.</summary>
        public void Pause() { _paused = true; if (Cam) Cam.enabled = false; }
        public void Resume() { _paused = false; if (_live) Cam.enabled = true; else Invalidate(); }

        /// <summary>The studio layer is only ever drawn by studio cameras (whatever another camera's mask or far plane).</summary>
        static void HideFromOtherCameras(ScriptableRenderContext ctx, Camera cam)
        {
            // play mode only: the hook outlives play mode (no domain reload), and an edit-time render of the open Boot
            // scene's camera then dirtied its saved culling mask
            if (!Application.isPlaying) return;
            if (cam && (cam.cullingMask & (1 << Layer)) != 0 && !Cameras.Contains(cam)) cam.cullingMask &= ~(1 << Layer);
        }

        // ------------------------------------------------------------------ lifetime

        void LateUpdate()
        {
            if (!_owner) { Destroy(gameObject); return; }
            // a still picture: the frames asked for are drawn (this LateUpdate precedes the frame's render), then it holds
            if (!_live && Cam && Cam.enabled)
            {
                if (_frames > 0) _frames--;
                else Cam.enabled = false;
            }
        }

        void OnDestroy()
        {
            if (Cam) { Cameras.Remove(Cam); Cam.targetTexture = null; }
            foreach (var o in _made)
            {
                if (!o) continue;
                if (o is RenderTexture rt) rt.Release();
                Destroy(o);
            }
            _made.Clear();
        }
    }
}
