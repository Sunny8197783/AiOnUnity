using UnityEngine;

namespace AiOn.Levels
{
    /// <summary>
    /// Fire and smoke (original engine/fx.js fire() / smoke(), which were shaders and could not be exported) as URP
    /// particle systems: a fire with flames, embers and a flickering light whose level runs 0..1, and a smoke volume
    /// that fills a box under the ceiling with density 0..1. Positions are Unity space.
    /// </summary>
    public static class StoryFx
    {
        static Material _flameMat, _smokeMat;
        static Texture2D _soft;

        static Texture2D Soft()
        {
            if (_soft) return _soft;
            const int n = 64;
            _soft = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1, d = Mathf.Sqrt(dx * dx + dy * dy);
                    _soft.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1 - d) * Mathf.Clamp01(1 - d)));
                }
            _soft.Apply();
            return _soft;
        }

        static Material ParticleMat(bool additive)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = new Material(shader);
            m.SetTexture("_BaseMap", Soft());
            m.SetFloat("_Surface", 1); // transparent
            m.SetFloat("_Blend", additive ? 2 : 0); // 2 additive, 0 alpha
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) m.EnableKeyword("_BLENDMODE_ADD"); else m.EnableKeyword("_BLENDMODE_ALPHA");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        public static Material FlameMat => _flameMat ? _flameMat : _flameMat = ParticleMat(true);
        public static Material SmokeMat => _smokeMat ? _smokeMat : _smokeMat = ParticleMat(false);

        static Material _flameSheet, _smokeSheet;

        /// <summary>
        /// The fire's and the room smoke's own materials ("그래픽 사실화"): a 4x4 flipbook of flame tongues / smoke puffs
        /// (Resources/Fx, our own procedural pictures) with soft particles (no hard line where a puff meets a wall or the
        /// floor) and near-camera fading. Built as assets by AiOn → Graphics → Build Look Assets (so builds keep the
        /// shader variants); the plain soft-dot materials above stay for the sparks, puffs and rings that use them.
        /// </summary>
        public static Material FlameSheetMat => _flameSheet ? _flameSheet : _flameSheet = SheetMat("Fx/FxFlame", true);
        public static Material SmokeSheetMat => _smokeSheet ? _smokeSheet : _smokeSheet = SheetMat("Fx/FxSmoke", false);

        /// <summary>Flipbook layout of the Fx pictures.</summary>
        public const int SheetTiles = 4;

        static Material SheetMat(string resource, bool additive)
        {
            var asset = Resources.Load<Material>(resource);
            if (asset) return asset;
            var m = ParticleMat(additive); // no asset (an old checkout): the plain look
            return m;
        }

        /// <summary>Turns on the flipbook for a particle system: a random frame, or the frames over each particle's life.</summary>
        public static void Flipbook(ParticleSystem ps, bool animate)
        {
            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.mode = ParticleSystemAnimationMode.Grid;
            tsa.numTilesX = SheetTiles; tsa.numTilesY = SheetTiles;
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            if (animate) { tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1)); tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, SheetTiles * SheetTiles - 1f); tsa.cycleCount = 1; }
            else { tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f); tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, SheetTiles * SheetTiles - 1f); }
        }

        /// <summary>A fire's light flicker 0..1-ish (pure; tested): two octaves of noise and a rare dip, never below 0.6.</summary>
        public static float Flicker(float t, float seed = 0f)
        {
            float a = Mathf.PerlinNoise(t * 7.3f, seed + 0.3f), b = Mathf.PerlinNoise(t * 19f, seed + 4.1f), c = Mathf.PerlinNoise(t * 1.7f, seed + 9.7f);
            return Mathf.Clamp(0.72f + 0.2f * a + 0.1f * b - 0.12f * Mathf.Max(0f, c - 0.6f) * 2.5f, 0.6f, 1.05f);
        }

        /// <summary>
        /// A fire at position (the floor under it). fuelHeight is how tall the burning thing is (the hall fire's cardboard
        /// boxes): the flames and the light start at its top, where they show, instead of inside it, where the box hid
        /// them and the light burnt its top face white. lightOffset moves the light off a wall the fire stands against.
        /// </summary>
        public static FireFx CreateFire(Transform parent, string name, Vector3 position, float scale, float fuelHeight = 0f, Vector3 lightOffset = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var fire = go.AddComponent<FireFx>();
            fire.Build(scale, fuelHeight, lightOffset);
            return fire;
        }

        /// <summary>The fire light's height above the floor (pure; tested): inside the flames, a little over half the fire's
        /// size above the top of what burns, so the nearest face is not at point blank.</summary>
        public static float LightHeight(float scale, float fuelHeight) => Mathf.Max(0f, fuelHeight) + 0.6f * scale;

        /// <summary>Whether a fire light casts shadows (pure; tested): a fire that has taken hold, at full light, on a tier
        /// that allows extra-light shadows.</summary>
        public static bool FireShadows(float level, float lightScale, bool allowed) => allowed && level >= 0.35f && lightScale > 0.5f;

        /// <summary>The strike site's smoke column (original town-strike.js: 22 big soft puffs rising to 70 m over
        /// 55 s, widening from 6 to 40 m and drifting east with the wind).</summary>
        public static SmokeColumnFx CreateSmokeColumn(Transform parent, string name, Vector3 basePosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = basePosition;
            var c = go.AddComponent<SmokeColumnFx>();
            c.Build();
            return c;
        }

        public static SmokeFx CreateSmoke(Transform parent, string name, Vector3 center, Vector3 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var smoke = go.AddComponent<SmokeFx>();
            smoke.Build(size, color);
            return smoke;
        }
    }

    public class FireFx : MonoBehaviour
    {
        ParticleSystem _flames, _embers, _smoke;
        float _flameRate, _smokeRate;
        float _seed;
        Light _light;
        float _level, _scale = 1, _lightY;

        public float Level => _level;

        /// <summary>Flame tongues per second at full size (per unit of scale).</summary>
        public const float FlameRate = 20f;

        /// <summary>The fire light's intensity at full level (per unit of scale).</summary>
        public const float LightIntensity = 6f;
        /// <summary>Scales the fire's light (the 3D extinguisher brings the view close: less glare on the boxes).</summary>
        public float LightScale = 1f;

        public void Build(float scale, float fuelHeight = 0f, Vector3 lightOffset = default)
        {
            _scale = scale;
            float top = Mathf.Max(0f, fuelHeight);
            // a few big, upright flame tongues (the flipbook carries the fire's own colours: a yellow-white core, orange body,
            // red licking tips), each playing its frames over its life; many small additive dots piled into a pink blur
            _flameRate = FlameRate * scale;
            _flames = Make("Flames", StoryFx.FlameSheetMat, _flameRate);
            StoryFx.Flipbook(_flames, true);
            var main = _flames.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f * scale, 0.55f * scale);
            main.startSize3D = true; // tall, not round: width x height
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.42f * scale, 0.7f * scale);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.75f * scale, 1.15f * scale);
            main.startSizeZ = 1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.82f, 0.62f, 0.6f), new Color(1f, 0.72f, 0.5f, 0.5f)); // overlapping tongues add up: each is half strength
            // from the top of what burns (a flame inside a box is a box), over its width
            var shape = _flames.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 6f; shape.radius = 0.26f * scale; shape.rotation = new Vector3(-90, 0, 0);
            shape.position = new Vector3(0, top - 0.04f * scale, 0);
            // upright billboards standing on their base (the flipbook's flame base sits at the bottom of its frame)
            var fr = _flames.GetComponent<ParticleSystemRenderer>(); fr.renderMode = ParticleSystemRenderMode.VerticalBillboard;
            fr.pivot = new Vector3(0f, 0.42f, 0f);
            fr.sortMode = ParticleSystemSortMode.YoungestInFront;
            var fn = _flames.noise; fn.enabled = true; fn.strength = 0.18f * scale; fn.frequency = 1.4f; fn.scrollSpeed = 1.0f; // they sway
            var col = _flames.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1f, 0.85f, 0.72f), 0.6f), new GradientColorKey(new Color(0.75f, 0.45f, 0.3f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var size = _flames.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1, 0.75f)));

            // a few tiny sparks, drawn as short streaks and fading out: big soft dots bloomed into a string of glowing orbs
            _embers = Make("Embers", StoryFx.FlameMat, 8 * scale);
            var em = _embers.main;
            em.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            em.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            em.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
            em.startColor = new Color(1f, 0.55f, 0.18f, 0.7f);
            em.gravityModifier = -0.12f;
            var es = _embers.shape; es.shapeType = ParticleSystemShapeType.Cone; es.angle = 25f; es.radius = 0.3f * scale; es.rotation = new Vector3(-90, 0, 0);
            es.position = new Vector3(0, top, 0);
            var er = _embers.GetComponent<ParticleSystemRenderer>(); er.renderMode = ParticleSystemRenderMode.Stretch; er.velocityScale = 0.04f; er.lengthScale = 1f;
            var ec = _embers.colorOverLifetime; ec.enabled = true;
            var eg = new Gradient();
            eg.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1f, 0.4f, 0.15f), 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0, 1) });
            ec.color = eg;

            // dark smoke off the top of the flames: rises, spreads and thins (alpha-blended, soft against the ceiling)
            _smoke = Make("FireSmoke", StoryFx.SmokeSheetMat, 0f);
            StoryFx.Flipbook(_smoke, false);
            _smokeRate = 22f * scale;
            var sm = _smoke.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(2.6f, 4.2f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.35f * scale, 0.7f * scale);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.45f * scale, 0.8f * scale);
            sm.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            sm.startColor = new Color(0.07f, 0.065f, 0.06f, 0.5f);
            sm.maxParticles = 160;
            var ss = _smoke.shape; ss.shapeType = ParticleSystemShapeType.Cone; ss.angle = 12f; ss.radius = 0.25f * scale; ss.rotation = new Vector3(-90, 0, 0);
            _smoke.transform.localPosition = new Vector3(0, top + 0.7f * scale, 0);
            var sz = _smoke.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 0.6f, 1, 2.4f));
            var sc = _smoke.colorOverLifetime; sc.enabled = true;
            var sg = new Gradient();
            // a multiplier on the dark start colour: grey-black all its life, see-through while it is still in the fire's
            // light (lit up close it glowed like a lantern), thickest once it has risen
            sg.SetKeys(new[] { new GradientColorKey(new Color(0.8f, 0.7f, 0.6f), 0), new GradientColorKey(Color.white, 0.4f), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.3f, 0.15f), new GradientAlphaKey(0.9f, 0.4f), new GradientAlphaKey(0.6f, 0.75f), new GradientAlphaKey(0, 1) });
            sc.color = sg;
            var sr = _smoke.rotationOverLifetime; sr.enabled = true; sr.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var sn = _smoke.noise; sn.enabled = true; sn.strength = 0.25f; sn.frequency = 0.5f;
            var srr = _smoke.GetComponent<ParticleSystemRenderer>(); srr.sortMode = ParticleSystemSortMode.Distance; srr.sortingFudge = 2f;

            _light = new GameObject("FireLight").AddComponent<Light>();
            _light.transform.SetParent(transform, false);
            _lightY = StoryFx.LightHeight(scale, top);
            _light.transform.localPosition = new Vector3(lightOffset.x, _lightY + lightOffset.y, lightOffset.z); // still: a wandering light made its shadows crawl
            _light.type = LightType.Point; _light.color = new Color(1f, 0.55f, 0.2f); _light.range = 7 * scale; _light.shadows = LightShadows.None;
            AiOn.Rendering.LightShadowBias.Set(_light); // pipeline bias (tuned for the sun) drew contour bands on the wall behind
            SetLevel(0);
        }

        ParticleSystem Make(string name, Material mat, float rate)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 600;
            var emission = ps.emission; emission.rateOverTime = rate;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            return ps;
        }

        /// <summary>Recolours the flames (the 3D extinguisher's practice target is a translucent teal stand-in, not a fire).</summary>
        public void Tint(Color a, Color b)
        {
            foreach (var ps in new[] { _flames, _embers })
            {
                if (!ps) continue;
                if (ps == _flames)
                {
                    // the flipbook carries fire colours: a recoloured stand-in uses the plain soft glow instead
                    var tsa = ps.textureSheetAnimation; tsa.enabled = false;
                    ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = StoryFx.FlameMat;
                }
                var m = ps.main; m.startColor = new ParticleSystem.MinMaxGradient(a, b);
                var col = ps.colorOverLifetime;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0, 1) });
                col.color = g;
            }
            if (_light) _light.color = new Color(a.r, a.g, a.b);
            SmokeOn = false; SetLevel(_level);
        }

        /// <summary>0 = out, 0.4 = small, 1 = large (original fire.setLevel).</summary>
        public void SetLevel(float level)
        {
            _level = Mathf.Clamp01(level);
            Apply(_flames);
            Apply(_embers);
            if (_smoke)
            {
                var e = _smoke.emission;
                e.enabled = _level > 0.15f && SmokeOn;
                e.rateOverTime = _smokeRate * _level;
                if (e.enabled && !_smoke.isPlaying) _smoke.Play();
            }
        }

        /// <summary>The fire's own smoke (off for the extinguisher's teal practice stand-in).</summary>
        public bool SmokeOn = true;

        void Apply(ParticleSystem ps)
        {
            if (!ps) return;
            var e = ps.emission;
            e.enabled = _level > 0.01f;
            if (ps == _flames && _flameRate > 0) e.rateOverTime = _flameRate * Mathf.Lerp(0.35f, 1f, _level); // a smaller fire is thinner too
            var m = ps.main;
            float k = Mathf.Lerp(0.4f, 1.15f, _level); // a bigger fire is taller and wider, not just denser (a dense pile clipped white)
            if (m.startSize3D) { m.startSizeXMultiplier = k; m.startSizeYMultiplier = k; } else m.startSizeMultiplier = k;
            if (_level > 0.01f && !ps.isPlaying) ps.Play();
        }

        void Update()
        {
            if (!_light) return;
            if (_seed == 0f) _seed = 1f + (GetInstanceID() & 63);
            float t = Time.time, flicker = StoryFx.Flicker(t, _seed);
            _light.intensity = LightIntensity * _scale * _level * flicker * LightScale; // was 12, then 8.5: the wall beside the boxes clipped white
            bool on = _level > 0.01f;
            _light.enabled = on;
            if (!on) return;
            // a fire that has taken hold throws shadows (the boxes, the doorframes) on the tiers that afford them; the light
            // only flickers in strength (moving it made the shadow edges crawl)
            var want = StoryFx.FireShadows(_level, LightScale, AiOn.Rendering.WindowLights.ShadowsAllowed) ? LightShadows.Soft : LightShadows.None;
            if (_light.shadows != want) _light.shadows = want;
        }
    }

    public class SmokeFx : MonoBehaviour
    {
        ParticleSystem _ps;
        float _density;
        Color _color;

        public float Density => _density;

        public void Build(Vector3 size, Color color)
        {
            _color = color;
            _ps = gameObject.AddComponent<ParticleSystem>();
            var main = _ps.main;
            main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 900;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = new Color(color.r, color.g, color.b, 0.35f);
            var shape = _ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = size;
            var col = _ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(color, 0), new GradientColorKey(color, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.25f), new GradientAlphaKey(1, 0.75f), new GradientAlphaKey(0, 1) });
            col.color = g;
            GetComponent<ParticleSystemRenderer>().sharedMaterial = StoryFx.SmokeSheetMat;
            StoryFx.Flipbook(_ps, false);
            var rot = _ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            var nz = _ps.noise; nz.enabled = true; nz.strength = 0.08f; nz.frequency = 0.25f; // a slow churn under the ceiling
            SetDensity(0);
        }

        /// <summary>0..1: how thick the smoke is (original smoke.setDensity).</summary>
        public void SetDensity(float density)
        {
            _density = Mathf.Clamp01(density);
            var e = _ps.emission;
            e.rateOverTime = 90f * _density;
            var main = _ps.main;
            main.startColor = new Color(_color.r, _color.g, _color.b, Mathf.Lerp(0.15f, 0.55f, _density));
            if (_density > 0.01f && !_ps.isPlaying) _ps.Play();
        }
    }
}

namespace AiOn.Levels
{
    public class SmokeColumnFx : MonoBehaviour
    {
        ParticleSystem _ps;
        bool _night;

        public void Build()
        {
            _ps = gameObject.AddComponent<ParticleSystem>();
            var main = _ps.main;
            main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 120;
            main.startLifetime = 55f; main.startSpeed = 0f; main.prewarm = true;
            main.startSize = 6f; main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = new Color(0.2f, 0.19f, 0.18f, 0.85f);
            var em = _ps.emission; em.rateOverTime = 22f / 55f * 2f;
            var shape = _ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 2.5f;
            // rise 3 -> 73 m (linear in age), drift east k^2 * 38 m
            var vel = _ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            // all three axes in curve mode (Unity requires one mode for x, y and z)
            vel.x = new ParticleSystem.MinMaxCurve(2f * 38f / 55f, AnimationCurve.Linear(0, 0, 1, 1));
            vel.y = new ParticleSystem.MinMaxCurve(70f / 55f, AnimationCurve.Linear(0, 1, 1, 1));
            vel.z = new ParticleSystem.MinMaxCurve(0f, AnimationCurve.Linear(0, 0, 1, 0));
            var noise = _ps.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 0.05f;
            var size = _ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 40f / 6f));
            var rot = _ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            SetNight(false);
            var r = GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = StoryFx.SmokeMat; r.sortMode = ParticleSystemSortMode.Distance; r.maxParticleSize = 4f;
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        /// <summary>At night the column's base is lit orange by the fires below it (the original's glow term).</summary>
        public void SetNight(bool night)
        {
            _night = night;
            var col = _ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            var baseC = new Color(0.2f, 0.19f, 0.18f);
            var lit = night ? new Color(0.75f, 0.41f, 0.23f) : baseC;
            g.SetKeys(new[] { new GradientColorKey(lit, 0), new GradientColorKey(baseC, 0.25f), new GradientColorKey(baseC, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.85f, 0.125f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0, 1) });
            col.color = g;
        }

        public void SetOn(bool on)
        {
            if (on && !_ps.isPlaying) _ps.Play(true);
            else if (!on) _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
