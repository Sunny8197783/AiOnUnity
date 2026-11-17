using UnityEngine;

namespace AiOn.Player
{
    /// <summary>
    /// First- and third-person camera like the original (engine/controls.js): FOV 70 (+4 while running), a gentle
    /// head bob in first person, and a third-person shoulder camera 3.4 m back and 1.7 m up that is pulled in when
    /// a wall or ceiling blocks its line of sight. V toggles the view.
    /// </summary>
    public class PlayerCameraRig : MonoBehaviour
    {
        public PlayerController player;
        public Camera cam;
        [Tooltip("Shown only in third person.")]
        public GameObject body;

        public bool thirdPerson;

        [Header("Story control")]
        [Tooltip("Cutscene camera: when set, the camera sits here looking at overrideTarget (Unity space).")]
        public bool overrideActive;
        public Vector3 overridePosition, overrideTarget;
        public float overrideFov = 50f;
        [Tooltip("Strength of the current shake (0 = none); it eases out by shakeUntil.")]
        public float shakeStrength;
        public float shakeUntil;

        /// <summary>Shakes the camera for a while (screen shake; reduced or skipped by the caller under reduce-motion).</summary>
        public void Shake(float strength, float seconds) { shakeStrength = Mathf.Max(shakeStrength, strength); shakeUntil = Time.time + seconds; }
        public float fov = 70f, runFovBoost = 4f;

        [Header("Third person")]
        public float distance = 3.4f, height = 1.7f, shoulder = 0.45f, probeRadius = 0.2f;
        public LayerMask obstacles = Physics.DefaultRaycastLayers; // not Ignore Raycast (thrown debris)

        float _phase, _bob, _fov, _camDist;
        PlayerInputReader _in;

        void Start()
        {
            if (!player) player = GetComponentInParent<PlayerController>();
            if (!cam) cam = GetComponentInChildren<Camera>();
            _in = player.GetComponent<PlayerInputReader>();
            _fov = fov; _camDist = distance;
            ApplyView();
        }

        /// <summary>Shows the body in third person only.</summary>
        public void ApplyView()
        {
            if (body) body.SetActive(thirdPerson);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (!InputGate.Blocked && _in.ViewPressed) { thirdPerson = !thirdPerson; ApplyView(); }

            if (overrideActive)
            {
                cam.fieldOfView = overrideFov;
                cam.transform.SetPositionAndRotation(overridePosition, Quaternion.LookRotation((overrideTarget - overridePosition).normalized, Vector3.up));
                ApplyShake();
                return;
            }

            var p = player.transform.position;
            var rot = Quaternion.Euler(player.Pitch, player.Yaw, 0);

            float hs = player.HorizontalSpeed;
            _phase += hs * dt * 2.2f;
            float wantBob = player.Grounded ? Mathf.Min(1f, hs / player.walkSpeed) * (player.Running ? 1.3f : 1f) : 0f;
            _bob += (wantBob - _bob) * (1f - Mathf.Exp(-6f * dt));
            _fov = Mathf.Lerp(_fov, fov + (player.Running ? runFovBoost : 0f), 1f - Mathf.Exp(-5f * dt));
            cam.fieldOfView = _fov;

            if (!thirdPerson)
            {
                var eye = p + Vector3.up * player.EyeHeight;
                var right = rot * Vector3.right;
                eye += Vector3.up * (Mathf.Sin(_phase * 2f) * 0.028f * _bob) + right * (Mathf.Sin(_phase) * 0.018f * _bob);
                cam.transform.SetPositionAndRotation(eye, rot * Quaternion.Euler(0, 0, Mathf.Sin(_phase) * 0.23f * _bob));
                _camDist = distance;
                ApplyShake();
                return;
            }

            float h = height * (player.EyeHeight / PlayerController.Eye);
            // Keep the pivot under a low ceiling (standing on a step, a landing under a beam): a pivot that starts
            // inside the ceiling's collider would let the camera slip through it.
            const float chest = 0.9f;
            if (Physics.Raycast(p + Vector3.up * chest, Vector3.up, out var above, h - chest + 0.35f, obstacles, QueryTriggerInteraction.Ignore)
                && !above.collider.transform.IsChildOf(player.transform))
                h = Mathf.Max(1.0f, chest + above.distance - 0.3f);
            var pivot = p + Vector3.up * h;
            var back = rot * new Vector3(shoulder, 0, -distance);
            float want = back.magnitude;
            var dir = back / want;
            float allowed = want;
            if (Physics.SphereCast(pivot, probeRadius, dir, out var hit, want, obstacles, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(player.transform))
                allowed = Mathf.Max(0.3f, hit.distance - 0.05f);
            // Pull in at once, ease back out.
            _camDist = allowed < _camDist ? allowed : Mathf.Lerp(_camDist, allowed, 1f - Mathf.Exp(-4f * dt));
            cam.transform.SetPositionAndRotation(pivot + dir * _camDist, rot);
            HideBodyWhenClose(_camDist < 0.95f);
            ApplyShake();
        }

        // When a wall pushes the camera into the player's back, the body would fill the screen: it keeps casting its
        // shadow but is no longer drawn until the camera has room again.
        GameObject _bodyOf;
        Renderer[] _bodyRenderers;
        bool _bodyHidden;

        void HideBodyWhenClose(bool hide)
        {
            if (!body) return;
            if (_bodyOf != body) { _bodyOf = body; _bodyRenderers = body.GetComponentsInChildren<Renderer>(true); _bodyHidden = false; }
            if (hide == _bodyHidden) return;
            _bodyHidden = hide;
            foreach (var r in _bodyRenderers)
                if (r) r.shadowCastingMode = hide ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
        }

        void ApplyShake()
        {
            if (shakeStrength <= 0) return;
            float left = shakeUntil - Time.time;
            if (left <= 0) { shakeStrength = 0; return; }
            float k = shakeStrength * Mathf.Clamp01(left / 0.5f), t = Time.time * 31f;
            cam.transform.position += cam.transform.rotation * new Vector3(Mathf.PerlinNoise(t, 1.3f) - 0.5f, Mathf.PerlinNoise(2.1f, t) - 0.5f, 0) * (0.12f * k);
            cam.transform.rotation *= Quaternion.Euler((Mathf.PerlinNoise(t, 5.7f) - 0.5f) * 2f * k, (Mathf.PerlinNoise(7.3f, t) - 0.5f) * 2f * k, 0);
        }
    }
}
