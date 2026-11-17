using AiOn.Levels;
using UnityEngine;

namespace AiOn.Player
{
    /// <summary>
    /// Walking body with the original's numbers (engine/controls.js PLAYER): capsule r 0.32 h 1.72, eyes at 1.62,
    /// walk 3.0 m/s, run 5.2 m/s on stamina, jump 3.6 m/s against gravity 12, crouch at 0.4x speed.
    /// Stamina: running drains it in 7 s (x staminaUse, e.g. a heavy bag); it refills in 11 s on the move or 5 s
    /// standing; emptied, you walk until it is back to 35 %.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(PlayerInputReader))]
    public class PlayerController : MonoBehaviour
    {
        public const float Radius = 0.32f, Height = 1.72f, Eye = 1.62f;

        [Header("Speeds (m/s)")]
        public float walkSpeed = 3.0f;
        public float runSpeed = 5.2f;
        [Range(0.1f, 1f)] public float crouchFactor = 0.4f;
        [Range(0.1f, 1f)] public float airFactor = 0.9f;
        [Tooltip("How quickly the body reaches the wanted speed (1/s).")]
        public float acceleration = 12f;

        [Header("Jump")]
        public float jumpSpeed = 3.6f;
        public float gravity = 12f;

        [Header("Game state hooks")]
        [Tooltip("GameRules.SpeedMul: fatigue, bag weight, injury. 0.2..1.5.")]
        [Range(0.2f, 1.5f)] public float speedMul = 1f;
        [Tooltip("How fast running spends stamina (heavy bag > 1). 0.5..3.")]
        [Range(0.5f, 3f)] public float staminaUse = 1f;

        [Header("Look")]
        [Tooltip("Degrees per mouse pixel (the original's 0.0024 rad).")]
        public float lookSensitivity = 0.1375f;
        public float pitchLimit = 80f;

        [Header("Spawn")]
        public string startSpawn = "home";

        public float Stamina { get; private set; } = 1f;
        public bool Winded { get; private set; }
        public bool Running { get; private set; }
        public bool Crouching { get; private set; }

        /// <summary>Back on your feet (after a time skip: still crouched days after the smoke escape looked wrong).</summary>
        public void StandUp() => Crouching = false;
        public bool Grounded => _cc.isGrounded;
        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public float HorizontalSpeed { get; private set; }
        public float VerticalSpeed => _vy;
        public float EyeHeight => Eye * (Crouching ? 0.6f : 1f);

        CharacterController _cc;
        PlayerInputReader _in;
        Vector3 _planar;
        float _vy;
        Vector3 _lastSafe;
        bool _hasSafe;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _in = GetComponent<PlayerInputReader>();
            _cc.radius = Radius;
            _cc.height = Height;
            _cc.center = new Vector3(0, Height / 2, 0);
            _cc.stepOffset = 0.3f;
            _cc.slopeLimit = 50f;
            _cc.skinWidth = 0.03f;
            _cc.minMoveDistance = 0f;
        }

        void Start()
        {
            if (!string.IsNullOrEmpty(startSpawn)) SpawnAt(startSpawn);
            PlayerInputReader.SetCursorLocked(true);
        }

        /// <summary>Puts the player on a named spawn of the current level. False when the level has no such spawn.</summary>
        public bool SpawnAt(string spawnName)
        {
            var points = LevelSpawnPoints.Current;
            if (points == null || !points.TryGet(spawnName, out var p))
            {
                Debug.LogWarning($"[Player] no spawn '{spawnName}' in this scene");
                return false;
            }
            Teleport(p.position, p.yaw);
            return true;
        }

        float? _faceYaw;
        float _facePitch;

        /// <summary>Turns (eased, over about half a second) to look at a point, e.g. the face of whoever speaks.</summary>
        public void FaceTowards(Vector3 point)
        {
            var d = point - (transform.position + Vector3.up * EyeHeight);
            float flat = new Vector2(d.x, d.z).magnitude;
            if (flat < 0.1f) return;
            _faceYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            _facePitch = Mathf.Clamp(-Mathf.Atan2(d.y, flat) * Mathf.Rad2Deg, -20f, 25f);
        }

        public void Teleport(Vector3 position, float yaw)
        {
            _faceYaw = null;
            _cc.enabled = false;
            transform.position = position + Vector3.up * 0.02f;
            _cc.enabled = true;
            Yaw = yaw; Pitch = 3f; _planar = Vector3.zero; _vy = 0; // the gaze starts slightly down, like the original
            transform.rotation = Quaternion.Euler(0, Yaw, 0);
            _lastSafe = transform.position; _hasSafe = true;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            bool gated = InputGate.Blocked; // a dialog or menu is open

            // Look
            var look = gated ? Vector2.zero : _in.Look * lookSensitivity;
            Yaw = Mathf.Repeat(Yaw + look.x, 360f);
            Pitch = Mathf.Clamp(Pitch - look.y, -pitchLimit, pitchLimit);
            if (_faceYaw.HasValue) // turning toward someone who speaks (FaceTowards); the player's own look cancels it
            {
                if (look.sqrMagnitude > 0.01f) _faceYaw = null;
                else
                {
                    float k = 1f - Mathf.Exp(-9f * dt);
                    Yaw = Mathf.Repeat(Mathf.LerpAngle(Yaw, _faceYaw.Value, k), 360f);
                    Pitch = Mathf.Lerp(Pitch, _facePitch, k);
                    if (Mathf.Abs(Mathf.DeltaAngle(Yaw, _faceYaw.Value)) < 0.3f && Mathf.Abs(Pitch - _facePitch) < 0.3f) _faceYaw = null;
                }
            }
            transform.rotation = Quaternion.Euler(0, Yaw, 0);

            if (!gated && _in.CrouchPressed) Crouching = !Crouching;

            // Run and stamina (the original's numbers)
            var move = gated ? Vector2.zero : Vector2.ClampMagnitude(_in.Move, 1f);
            bool moving = move.sqrMagnitude > 0.0001f;
            Running = moving && !Winded && !Crouching && _in.Run;
            Stamina = Mathf.Clamp01(Stamina + (Running ? -dt / 7f * staminaUse : dt / (moving ? 11f : 5f)));
            if (Stamina <= 0f) Winded = true;
            else if (Winded && Stamina > 0.35f) Winded = false;

            bool grounded = _cc.isGrounded;
            float speed = (Running ? runSpeed : walkSpeed) * speedMul * (grounded ? 1f : airFactor) * (Crouching ? crouchFactor : 1f);
            var wish = transform.rotation * new Vector3(move.x, 0, move.y) * speed;
            _planar = Vector3.Lerp(_planar, wish, 1f - Mathf.Exp(-acceleration * dt));

            // Jump and gravity
            if (grounded)
            {
                _vy = -2f; // keeps the capsule pressed to ramps and steps
                if (!gated && _in.JumpPressed)
                {
                    if (Crouching) Crouching = false;
                    else { _vy = jumpSpeed; Stamina = Mathf.Max(0, Stamina - 0.06f); }
                }
            }
            else _vy -= gravity * dt;

            _cc.Move((_planar + Vector3.up * _vy) * dt);
            var v = _cc.velocity; v.y = 0;
            HorizontalSpeed = v.magnitude;

            // Fell out of the world: back to the last safe spot.
            if (_cc.isGrounded) { _lastSafe = transform.position; _hasSafe = true; }
            var points = LevelSpawnPoints.Current;
            if (points != null && transform.position.y < points.killY && _hasSafe) Teleport(_lastSafe, Yaw);
        }
    }
}
