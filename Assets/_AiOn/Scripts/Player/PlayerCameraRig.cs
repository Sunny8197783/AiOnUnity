using UnityEngine;

namespace AiOn.Player
{
    /// <summary>
    /// First- and third-person camera like the original (engine/controls.js): FOV 70 (+4 while running), a gentle
    /// head bob in first person, and a third-person shoulder camera that is pulled in when a wall or ceiling blocks its
    /// line of sight. V toggles the view.
    /// Indoors (an interior env, or a wall close behind) the shoulder camera tightens (1.8 m back, 1.7 m up, 0.45 m to the
    /// side) and its pivot sinks to eye height as the boom shortens, so a small room never turns it into a high
    /// security camera; with less than 0.9 m of room it eases into first person instead. When a cutscene or a talk shot
    /// hands the camera back it eases from where that shot left it (controls.js setOverride blend).
    /// </summary>
    public class PlayerCameraRig : MonoBehaviour
    {
        public PlayerController player;
        public Camera cam;
        [Tooltip("Shown only in third person.")]
        public GameObject body;

        public bool thirdPerson;
        [Tooltip("움직임 줄이기: no head bob, no run FOV kick, no shake (controls.js RM).")]
        public bool reduceMotion;
        [Tooltip("The story host owns V (it saves the view setting); off, V toggles here (a level scene's test player).")]
        public bool hostOwnsView;

        [Header("Story control")]
        [Tooltip("Cutscene camera: when set, the camera sits here looking at overrideTarget (Unity space).")]
        public bool overrideActive;
        public Vector3 overridePosition, overrideTarget;
        public float overrideFov = 50f;
        [Tooltip("Strength of the current shake (0 = none); it eases out by shakeUntil.")]
        public float shakeStrength;
        public float shakeUntil;
        [Tooltip("A talk shot hides the body (it keeps its shadow) when it would fill the frame.")]
        public bool forceBodyHidden;
        [Tooltip("Off while the flashlight is on: the body would throw its own shadow across the view.")]
        public bool bodyCastsShadow = true;

        /// <summary>Shakes the camera for a while (screen shake; reduced or skipped by the caller under reduce-motion).</summary>
        public void Shake(float strength, float seconds) { shakeStrength = Mathf.Max(shakeStrength, strength); shakeUntil = Time.time + seconds; }

        /// <summary>A short downward dip of the view (a hard landing); nothing under reduce-motion.</summary>
        public void Dip(float metres) { if (!reduceMotion) { _dip = Mathf.Max(_dip, metres); _dipT = 0; } }

        /// <summary>Eases from this pose into the rig's own camera over seconds (a cutscene's or talk's last frame).</summary>
        public void BlendFrom(Vector3 pos, Quaternion rot, float fieldOfView, float seconds = 0.6f)
        {
            if (reduceMotion || seconds <= 0) { _blendK = 1; return; }
            _blendPos = pos; _blendRot = rot; _blendFov = fieldOfView; _blendDur = seconds; _blendK = 0;
        }

        public float fov = 70f, runFovBoost = 4f;

        [Header("Third person")]
        // Over the shoulder, a little above the head (the original 3.4 / 1.7 / 0.45 put the head in the middle of the view).
        public float distance = 3.2f, height = 1.95f, shoulder = 0.7f, probeRadius = 0.2f;
        [Header("Third person indoors")]
        public float indoorDistance = 1.8f, indoorHeight = 1.7f, indoorShoulder = 0.45f;
        [Tooltip("A wall this close behind counts as indoors.")]
        public float indoorProbe = 1.2f;
        [Tooltip("Below this much room the view eases into first person.")]
        public float firstPersonBelow = 0.9f;
        public LayerMask obstacles = Physics.DefaultRaycastLayers; // not Ignore Raycast (thrown debris)

        float _phase, _bob, _fov, _camDist = -1, _indoorK, _fpK, _closeT;
        float _dip, _dipT = 1;
        Vector3 _blendPos; Quaternion _blendRot; float _blendFov, _blendDur, _blendK = 1;
        bool _wasOverride;
        PlayerInputReader _in;

        void Start()
        {
            if (!player) player = GetComponentInParent<PlayerController>();
            if (!cam) cam = GetComponentInChildren<Camera>();
            if (cam) cam.nearClipPlane = 0.05f; // the own shoulder and a close wall come near the lens
            _in = player.GetComponent<PlayerInputReader>();
            _fov = fov; _camDist = distance;
            ApplyView();
        }

        /// <summary>Shows the body in third person only.</summary>
        public void ApplyView()
        {
            if (body) body.SetActive(thirdPerson);
        }

        // ---------------------------------------------------------------- pure framing (unit-tested)

        /// <summary>Boom length, pivot height and shoulder offset for an indoor share k (0 outdoors, 1 indoors).</summary>
        public static (float dist, float height, float shoulder) Framing(float k, float outDist, float outHeight, float outShoulder, float inDist, float inHeight, float inShoulder)
        {
            k = Mathf.Clamp01(k);
            return (Mathf.Lerp(outDist, inDist, k), Mathf.Lerp(outHeight, inHeight, k), Mathf.Lerp(outShoulder, inShoulder, k));
        }

        /// <summary>The pivot height for a boom pulled in to camDist of want: from the framing height down to the eyes.</summary>
        public static float PivotHeight(float framingHeight, float eyeHeight, float camDist, float want, float minDist)
        {
            if (want <= minDist) return Mathf.Min(framingHeight, eyeHeight);
            float t = Mathf.Clamp01((camDist - minDist) / (want - minDist));
            return Mathf.Lerp(Mathf.Min(framingHeight, eyeHeight), framingHeight, t);
        }

        /// <summary>The env presets that get the tight indoor framing (interior, interiorNight, interiorDark).</summary>
        public static bool IsInteriorPreset(string name) => name != null && name.StartsWith("interior", System.StringComparison.Ordinal);

        /// <summary>How far into first person (0..1) for the room behind the player: 0 at firstPersonBelow + 0.25 m or more, 1 at firstPersonBelow - 0.2 m.</summary>
        public static float FirstPersonShare(float room, float below) => 1f - Mathf.Clamp01((room - (below - 0.2f)) / 0.45f);

        // ---------------------------------------------------------------- frame

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (!hostOwnsView && !InputGate.Blocked && _in.ViewPressed) { thirdPerson = !thirdPerson; ApplyView(); }
            player.ThirdPerson = thirdPerson; // the body turns on its own only in third person

            if (overrideActive)
            {
                cam.fieldOfView = overrideFov;
                cam.transform.SetPositionAndRotation(overridePosition, Quaternion.LookRotation((overrideTarget - overridePosition).normalized, Vector3.up));
                _wasOverride = true;
                ApplyBody(forceBodyHidden);
                ApplyShake();
                return;
            }
            if (_wasOverride)
            {
                // a cutscene or talk shot just handed the camera back: ease out of its last frame
                _wasOverride = false;
                BlendFrom(cam.transform.position, cam.transform.rotation, cam.fieldOfView, 0.6f);
                _camDist = -1; // start the boom fresh behind the player
            }

            var p = player.transform.position;
            var rot = Quaternion.Euler(player.Pitch, player.Yaw, 0);

            float hs = player.HorizontalSpeed;
            _phase += hs * dt * 2.2f;
            float wantBob = reduceMotion ? 0f : player.Grounded ? Mathf.Min(1f, hs / player.walkSpeed) * (player.Running ? 1.3f : 1f) : 0f;
            _bob += (wantBob - _bob) * (1f - Mathf.Exp(-6f * dt));
            _fov = Mathf.Lerp(_fov, fov + (player.Running && !reduceMotion ? runFovBoost : 0f), 1f - Mathf.Exp(-5f * dt));
            float fovNow = _fov;

            var eye = p + Vector3.up * player.EyeHeight;
            var right = rot * Vector3.right;
            var fpPos = eye + Vector3.up * (Mathf.Sin(_phase * 2f) * 0.028f * _bob) + right * (Mathf.Sin(_phase) * 0.018f * _bob);
            var fpRot = rot * Quaternion.Euler(0, 0, Mathf.Sin(_phase) * 0.23f * _bob);

            Vector3 pos; Quaternion look;
            bool hideBody = forceBodyHidden;
            if (!thirdPerson)
            {
                pos = fpPos; look = fpRot;
                _camDist = -1; _fpK = 0;
            }
            else
            {
                // indoors: the env says so, or a wall stands close behind the shoulder
                var env = AiOn.Levels.EnvironmentController.Instance;
                // (the interior* presets only: the shelter's 'underground' car park is big enough for the full boom; a wall
                // close behind still tightens it)
                bool interior = env != null && env.Current != null && IsInteriorPreset(env.Current.Name);
                var flatBack = Quaternion.Euler(0, player.Yaw, 0) * new Vector3(shoulder * 0.5f, 0, -1f);
                bool close = Physics.SphereCast(eye, probeRadius, flatBack.normalized, out var near, indoorProbe, obstacles, QueryTriggerInteraction.Ignore)
                             && !near.collider.transform.IsChildOf(player.transform);
                _closeT = close ? 0.5f : Mathf.Max(0, _closeT - dt); // a short hold: no flicker along a row of pillars
                float wantIn = interior || _closeT > 0 ? 1f : 0f;
                _indoorK = Mathf.MoveTowards(_indoorK, wantIn, dt / 0.3f);
                float e = _indoorK * _indoorK * (3 - 2 * _indoorK);
                var (dist, h, sh) = Framing(e, distance, height, shoulder, indoorDistance, indoorHeight, indoorShoulder);
                h *= player.EyeHeight / PlayerController.Eye;

                // Keep the pivot under a low ceiling (standing on a step, a landing under a beam): a pivot that starts
                // inside the ceiling's collider would let the camera slip through it.
                const float chest = 0.9f;
                if (Physics.Raycast(p + Vector3.up * chest, Vector3.up, out var above, h - chest + 0.35f, obstacles, QueryTriggerInteraction.Ignore)
                    && !above.collider.transform.IsChildOf(player.transform))
                    h = Mathf.Max(1.0f, chest + above.distance - 0.3f);

                var back = rot * new Vector3(sh, 0, -dist);
                float want = back.magnitude;
                var dir = back / want;
                // probe from the pivot at full framing height first, then settle on the height the boom allows
                float pivotTop = h;
                float allowed = want;
                if (Physics.SphereCast(p + Vector3.up * pivotTop, probeRadius, dir, out var hit, want, obstacles, QueryTriggerInteraction.Ignore)
                    && !hit.collider.transform.IsChildOf(player.transform))
                    allowed = Mathf.Max(0.3f, hit.distance - 0.05f);
                // Pull in at once, ease back out.
                if (_camDist < 0) _camDist = allowed;
                _camDist = allowed < _camDist ? allowed : Mathf.Lerp(_camDist, allowed, 1f - Mathf.Exp(-4f * dt));
                float ph = PivotHeight(h, player.EyeHeight, _camDist, want, firstPersonBelow);
                // the boom from the lower pivot runs under the one probed above: a counter or a shelf top between the
                // eyes and the framing height would otherwise swallow the lens
                if (ph < pivotTop - 0.02f
                    && Physics.SphereCast(p + Vector3.up * ph, probeRadius, dir, out var low, _camDist, obstacles, QueryTriggerInteraction.Ignore)
                    && !low.collider.transform.IsChildOf(player.transform))
                {
                    float lowAllowed = Mathf.Max(0.3f, low.distance - 0.05f);
                    if (lowAllowed < _camDist) { _camDist = lowAllowed; allowed = Mathf.Min(allowed, lowAllowed); }
                }
                var tpPos = p + Vector3.up * ph + dir * _camDist;
                // too little room: first person at the eyes rather than a camera jammed into the back of the head
                float fpWant = FirstPersonShare(allowed, firstPersonBelow);
                _fpK = Mathf.MoveTowards(_fpK, fpWant, dt / 0.25f);
                float f = _fpK * _fpK * (3 - 2 * _fpK);
                pos = Vector3.Lerp(tpPos, fpPos, f);
                look = Quaternion.Slerp(rot, fpRot, f);
                hideBody |= _fpK > 0.35f || _camDist < 0.6f;
            }

            // a hard landing dips the view a little and springs back
            if (_dipT < 1f)
            {
                _dipT = Mathf.Min(1f, _dipT + dt / 0.35f);
                pos += Vector3.down * (_dip * Mathf.Sin(_dipT * Mathf.PI) * (1f - _dipT * 0.5f));
            }

            if (_blendK < 1f)
            {
                _blendK = Mathf.Min(1f, _blendK + Time.unscaledDeltaTime / Mathf.Max(0.05f, _blendDur));
                float e = _blendK * _blendK * (3 - 2 * _blendK);
                pos = Vector3.Lerp(_blendPos, pos, e);
                look = Quaternion.Slerp(_blendRot, look, e);
                fovNow = Mathf.Lerp(_blendFov, fovNow, e);
            }

            cam.fieldOfView = fovNow;
            cam.transform.SetPositionAndRotation(pos, look);
            ApplyBody(hideBody);
            ApplyShake();
        }

        // When a wall pushes the camera into the player's back, the body would fill the screen: it keeps casting its
        // shadow but is no longer drawn until the camera has room again. With the flashlight on it casts none at all.
        GameObject _bodyOf;
        Renderer[] _bodyRenderers;
        int _bodyMode = -1;

        void ApplyBody(bool hide)
        {
            if (!body) return;
            if (_bodyOf != body) { _bodyOf = body; _bodyRenderers = body.GetComponentsInChildren<Renderer>(true); _bodyMode = -1; }
            int mode = (hide ? 1 : 0) | (bodyCastsShadow ? 0 : 2);
            if (mode == _bodyMode) return;
            _bodyMode = mode;
            var cast = hide ? (bodyCastsShadow ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.Off)
                            : (bodyCastsShadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off);
            bool off = hide && !bodyCastsShadow;
            foreach (var r in _bodyRenderers)
            {
                if (!r) continue;
                r.shadowCastingMode = cast;
                r.forceRenderingOff = off;
            }
        }

        void ApplyShake()
        {
            if (shakeStrength <= 0) return;
            if (reduceMotion) { shakeStrength = 0; return; }
            float left = shakeUntil - Time.time;
            if (left <= 0) { shakeStrength = 0; return; }
            float k = shakeStrength * Mathf.Clamp01(left / 0.5f), t = Time.time * 31f;
            cam.transform.position += cam.transform.rotation * new Vector3(Mathf.PerlinNoise(t, 1.3f) - 0.5f, Mathf.PerlinNoise(2.1f, t) - 0.5f, 0) * (0.12f * k);
            cam.transform.rotation *= Quaternion.Euler((Mathf.PerlinNoise(t, 5.7f) - 0.5f) * 2f * k, (Mathf.PerlinNoise(7.3f, t) - 0.5f) * 2f * k, 0);
        }
    }
}
