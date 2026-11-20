using AiOn.Levels;
using UnityEngine;
using UnityEngine.Rendering;

namespace AiOn.Rendering
{
    /// <summary>
    /// Reflections from the level as it looks now. The levels change time of day and lose power at run time, so baked
    /// GI and baked probes would show the wrong room (a lit kitchen in a blackout); the default sky reflection put a pale
    /// sheen on everything at night. Instead one realtime probe rides with the camera and is re-rendered, a face per
    /// frame, only when the look changes (a preset blend ends, the power goes) or the player has moved on 6 m.
    /// Added by EnvironmentController to every level.
    /// </summary>
    public sealed class LevelReflections : MonoBehaviour
    {
        public const float MoveRefresh = 6f;
        public const int Resolution = 128;

        /// <summary>Set by the quality tier (medium and up); the low tiers keep the plain sky reflection.</summary>
        public static bool Allowed { get; set; } = true;

        ReflectionProbe _probe;
        Vector3 _at = new(float.NaN, 0, 0);
        float _dueAt = 0.5f;
        int _renderId = -1;
        float _renderAt;

        /// <summary>True when the probe should be re-rendered at the camera's position (pure; tested).</summary>
        public static bool NeedsRefresh(Vector3 last, Vector3 now, bool lookChanged) =>
            lookChanged || float.IsNaN(last.x) || (now - last).sqrMagnitude > MoveRefresh * MoveRefresh;

        void Awake()
        {
            var go = new GameObject("ReflectionProbe (live)");
            go.transform.SetParent(transform, false);
            _probe = go.AddComponent<ReflectionProbe>();
            _probe.mode = ReflectionProbeMode.Realtime;
            _probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            _probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            _probe.resolution = Resolution;
            _probe.hdr = true;
            _probe.boxProjection = false;
            _probe.size = new Vector3(60f, 30f, 60f);
            _probe.nearClipPlane = 0.4f; // outside the player's own head
            _probe.farClipPlane = 300f;
            _probe.shadowDistance = 0f;
            _probe.importance = 1;
            _probe.intensity = 1f;
            _probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            int ui = LayerMask.NameToLayer("UI");
            _probe.cullingMask = ui >= 0 ? ~(1 << ui) : ~0;
        }

        /// <summary>The look changed: render again shortly (debounced, so a run of changes costs one render).</summary>
        public void Request(float delay = 0.25f) => _dueAt = Mathf.Min(_dueAt < 0 ? float.MaxValue : _dueAt, Time.unscaledTime + delay);

        void Update()
        {
            var cam = Camera.main;
            if (!cam || !_probe) return;
            bool on = Allowed && QualitySettings.realtimeReflectionProbes;
            if (_probe.enabled != on) { _probe.enabled = on; if (on) _dueAt = Time.unscaledTime; }
            if (!on) return;
            var eye = cam.transform.position;
            bool due = _dueAt >= 0 && Time.unscaledTime >= _dueAt;
            if (!NeedsRefresh(_at, eye, due)) return;
            // let the last one finish first (six faces, one a frame), but never wait on one that was dropped
            if (_renderId >= 0 && !_probe.IsFinishedRendering(_renderId) && Time.unscaledTime - _renderAt < 2f) return;
            _probe.transform.position = eye;
            _at = eye;
            _dueAt = -1f;
            _renderId = _probe.RenderProbe();
            _renderAt = Time.unscaledTime;
        }

        void OnEnable() => _dueAt = Time.unscaledTime + 0.5f;
    }
}
