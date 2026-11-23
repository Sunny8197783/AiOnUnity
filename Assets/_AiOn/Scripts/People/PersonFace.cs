using UnityEngine;

namespace AiOn.People
{
    /// <summary>
    /// The face timing of a hero (pure, unit-tested): when the eyes blink and how far, and how open the mouth is while the
    /// person talks. Blinks last about 0.16 s (closing faster than opening) every 2.2 to 5.5 s; talking opens the jaw in
    /// syllable-like pulses of about 4 a second whose height drifts, never fully closed for long and never wide open.
    /// </summary>
    public static class FaceMotion
    {
        public const float BlinkClose = 0.06f, BlinkHold = 0.03f, BlinkOpen = 0.09f;
        public const float BlinkMin = 2.2f, BlinkMax = 5.5f;
        public static float BlinkLength => BlinkClose + BlinkHold + BlinkOpen;

        /// <summary>Eyelid closure 0..1 at <paramref name="t"/> seconds into a blink (0 outside it).</summary>
        public static float Blink(float t)
        {
            if (t <= 0f || t >= BlinkLength) return 0f;
            if (t < BlinkClose) return t / BlinkClose;
            if (t < BlinkClose + BlinkHold) return 1f;
            float o = (t - BlinkClose - BlinkHold) / BlinkOpen;
            return 1f - o * o * (3f - 2f * o);
        }

        /// <summary>Seconds to the next blink for a 0..1 random draw.</summary>
        public static float NextBlink(float r01) => BlinkMin + (BlinkMax - BlinkMin) * Mathf.Clamp01(r01);

        /// <summary>Jaw opening 0..1 while talking, at time t with a per-person seed (smooth; 0.08..0.85).</summary>
        public static float Mouth(float t, float seed)
        {
            float syll = 0.5f + 0.5f * Mathf.Sin((t * 4.1f + seed) * Mathf.PI * 2f);
            float drift = 0.55f + 0.45f * Mathf.Sin((t * 1.3f + seed * 1.7f) * Mathf.PI * 2f);
            float v = syll * syll * drift;
            return Mathf.Lerp(0.08f, 0.85f, Mathf.Clamp01(v));
        }

        /// <summary>Which mouth shape leads at time t (index into the shapes in use), changing about 6 times a second.</summary>
        public static int Shape(float t, float seed, int count) =>
            count <= 0 ? -1 : (int)Mathf.Repeat(Mathf.Floor(t * 6.3f + seed * 13f) * 7f, count);

        /// <summary>Head-look weight toward <paramref name="target"/> from a head facing <paramref name="forward"/>: full ahead, none behind.</summary>
        public static float LookWeight(Vector3 forward, Vector3 toTarget)
        {
            forward.y = 0; toTarget.y = 0;
            if (forward.sqrMagnitude < 1e-6f || toTarget.sqrMagnitude < 1e-6f) return 0f;
            float a = Vector3.Angle(forward, toTarget);
            if (a <= 70f) return 1f;
            if (a >= 115f) return 0f;
            float k = (a - 70f) / 45f;
            return 1f - k * k * (3f - 2f * k);
        }
    }

    /// <summary>
    /// A hero's face and gaze on the Rocketbox avatars: blinks (ARKit AK_09/AK_10 blendshapes), a talking mouth while
    /// <see cref="Speak"/> runs (AK_25 jaw open and a few visemes) and a head-and-eyes look toward a point through Humanoid IK
    /// (Animator.SetLookAtPosition; the controller's base layer has its IK pass on). Crowd members do not carry it (they have
    /// no blendshapes and no look). Weights only change when they move, and nothing allocates per frame.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class PersonFace : MonoBehaviour
    {
        static readonly string[] Visemes = { "AA_VI_10_aa", "AA_VI_11_E", "AA_VI_13_O", "AA_VI_14_U", "AA_VI_06_CH" };

        Animator _animator;
        SkinnedMeshRenderer _face;
        int _blinkL = -1, _blinkR = -1, _jaw = -1;
        int[] _visemes;
        float _nextBlink, _blinkT = -1f, _speakUntil, _seed, _lastBlink, _lastJaw;
        float _lookW;
        Vector3 _look;
        bool _hasLook;
        int _lastShape = -1;

        /// <summary>Whether the mouth is moving.</summary>
        public bool Speaking => Time.time < _speakUntil;
        public bool HasBlendshapes => _blinkL >= 0 || _jaw >= 0;

        void Awake()
        {
            _animator = GetComponent<Animator>();
            _seed = (GetInstanceID() & 1023) / 1023f;
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = smr.sharedMesh;
                if (!m || m.blendShapeCount == 0) continue;
                _face = smr;
                _blinkL = Index(m, "AK_09_EyeBlinkLeft");
                _blinkR = Index(m, "AK_10_EyeBlinkRight");
                _jaw = Index(m, "AK_25_JawOpen");
                var list = new System.Collections.Generic.List<int>();
                foreach (var v in Visemes) { int i = Index(m, v); if (i >= 0) list.Add(i); }
                _visemes = list.ToArray();
                break;
            }
            _nextBlink = Time.time + FaceMotion.NextBlink(Random.value);
        }

        /// <summary>Blendshape index by name or by suffix (the facial FBX prefixes them with the mesh name); -1 if absent.</summary>
        static int Index(Mesh m, string name)
        {
            int i = m.GetBlendShapeIndex(name);
            if (i >= 0) return i;
            for (int k = 0; k < m.blendShapeCount; k++)
                if (m.GetBlendShapeName(k).EndsWith(name, System.StringComparison.Ordinal)) return k;
            return -1;
        }

        bool _closed;

        /// <summary>Keeps the eyes shut and the mouth still (a person lying unconscious), or lets them blink again.</summary>
        public void CloseEyes(bool closed)
        {
            if (_closed == closed) return;
            _closed = closed;
            if (closed) { _blinkT = -1f; _speakUntil = 0f; }
            else _nextBlink = Time.time + FaceMotion.NextBlink(Random.value);
        }

        public bool EyesClosed => _closed;

        /// <summary>Moves the mouth for this many seconds (0 stops it).</summary>
        public void Speak(float seconds) => _speakUntil = seconds > 0 ? Time.time + seconds : 0f;

        /// <summary>Looks with the head and eyes at a world point; null looks ahead again (eased).</summary>
        public void LookAt(Vector3? point)
        {
            _hasLook = point.HasValue;
            if (point.HasValue) _look = point.Value;
        }

        void Update()
        {
            float now = Time.time, dt = Time.deltaTime;
            // gaze weight eases in and out, and fades when the point goes behind
            float want = 0f;
            if (_hasLook && _animator)
            {
                var head = _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.Head) : null;
                var from = head ? head.position : transform.position + Vector3.up * 1.5f;
                want = FaceMotion.LookWeight(transform.forward, _look - from);
            }
            _lookW = Mathf.MoveTowards(_lookW, want, dt * 2.5f);

            if (!_face) return;
            if (!_closed && _blinkT < 0f && now >= _nextBlink) { _blinkT = 0f; _nextBlink = now + FaceMotion.NextBlink(Random.value); }
            float blink = _closed ? 1f : 0f;
            if (!_closed && _blinkT >= 0f)
            {
                _blinkT += dt;
                blink = FaceMotion.Blink(_blinkT);
                if (_blinkT >= FaceMotion.BlinkLength) _blinkT = -1f;
            }
            if (blink != _lastBlink)
            {
                _lastBlink = blink;
                if (_blinkL >= 0) _face.SetBlendShapeWeight(_blinkL, blink * 100f);
                if (_blinkR >= 0) _face.SetBlendShapeWeight(_blinkR, blink * 100f);
            }

            float jaw = Speaking ? FaceMotion.Mouth(now, _seed) : Mathf.MoveTowards(_lastJaw, 0f, dt * 4f);
            if (Mathf.Abs(jaw - _lastJaw) > 0.004f || (jaw == 0f && _lastJaw != 0f))
            {
                _lastJaw = jaw;
                if (_jaw >= 0) _face.SetBlendShapeWeight(_jaw, jaw * 38f);
                if (_visemes != null && _visemes.Length > 0)
                {
                    int shape = Speaking ? FaceMotion.Shape(now, _seed, _visemes.Length) : -1;
                    if (shape != _lastShape && _lastShape >= 0) _face.SetBlendShapeWeight(_visemes[_lastShape], 0f);
                    _lastShape = shape;
                    if (shape >= 0) _face.SetBlendShapeWeight(_visemes[shape], jaw * 55f);
                }
            }
        }

        void OnAnimatorIK(int layer)
        {
            if (layer != 0 || !_animator) return;
            _animator.SetLookAtWeight(_lookW, 0.12f, 0.65f, 1f, 0.55f);
            if (_lookW > 0f) _animator.SetLookAtPosition(_look);
        }
    }
}
