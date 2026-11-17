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

        public static FireFx CreateFire(Transform parent, string name, Vector3 position, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var fire = go.AddComponent<FireFx>();
            fire.Build(scale);
            return fire;
        }

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
        ParticleSystem _flames, _embers;
        Light _light;
        float _level, _scale = 1;

        public float Level => _level;

        public void Build(float scale)
        {
            _scale = scale;
            _flames = Make("Flames", StoryFx.FlameMat, 120 * scale);
            var main = _flames.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f * scale, 1.2f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f * scale, 0.7f * scale);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f, 0.9f), new Color(1f, 0.8f, 0.3f, 0.8f));
            var shape = _flames.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 0.3f * scale; shape.rotation = new Vector3(-90, 0, 0);
            var col = _flames.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0), new GradientColorKey(new Color(1f, 0.35f, 0.05f), 0.6f), new GradientColorKey(new Color(0.3f, 0.1f, 0.05f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var size = _flames.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0.3f));

            _embers = Make("Embers", StoryFx.FlameMat, 25 * scale);
            var em = _embers.main;
            em.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            em.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2f);
            em.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            em.startColor = new Color(1f, 0.6f, 0.2f, 1f);
            em.gravityModifier = -0.15f;
            var es = _embers.shape; es.shapeType = ParticleSystemShapeType.Circle; es.radius = 0.4f * scale; es.rotation = new Vector3(-90, 0, 0);

            _light = new GameObject("FireLight").AddComponent<Light>();
            _light.transform.SetParent(transform, false);
            _light.transform.localPosition = new Vector3(0, 0.6f * scale, 0);
            _light.type = LightType.Point; _light.color = new Color(1f, 0.55f, 0.2f); _light.range = 7 * scale; _light.shadows = LightShadows.None;
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

        /// <summary>0 = out, 0.4 = small, 1 = large (original fire.setLevel).</summary>
        public void SetLevel(float level)
        {
            _level = Mathf.Clamp01(level);
            foreach (var ps in new[] { _flames, _embers })
            {
                if (!ps) continue;
                var e = ps.emission;
                e.enabled = _level > 0.01f;
                var m = ps.main; m.startSizeMultiplier = Mathf.Lerp(0.4f, 1f, _level);
                if (_level > 0.01f && !ps.isPlaying) ps.Play();
            }
        }

        void Update()
        {
            if (!_light) return;
            float flicker = 0.75f + 0.25f * Mathf.PerlinNoise(Time.time * 9f, 0.3f);
            _light.intensity = 12f * _scale * _level * flicker;
            _light.enabled = _level > 0.01f;
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
            GetComponent<ParticleSystemRenderer>().sharedMaterial = StoryFx.SmokeMat;
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
