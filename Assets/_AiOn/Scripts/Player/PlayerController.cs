using System;
using AiOn.Levels;
using UnityEngine;

namespace AiOn.Player
{
    /// <summary>
    /// Walking body with the original's numbers (engine/controls.js PLAYER): capsule r 0.32 h 1.72, eyes at 1.62,
    /// walk 3.0 m/s, run 5.2 m/s on stamina, jump 3.6 m/s against gravity 12, crouch at 0.4x speed.
    /// Stamina: running drains it in 7 s (x staminaUse, e.g. a heavy bag); it refills in 11 s on the move or 5 s
    /// standing; emptied, you walk until it is back to 35 %.
    /// Space in front of a low wall, car, railing or rubble vaults over it or climbs onto it (VaultPlanner, with the level
    /// data the host gives through <see cref="vaultPlanner"/>); elsewhere it jumps. In third person the body turns toward
    /// where it walks (BodyYaw), not with the camera.
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
        [Tooltip("The 마우스 감도 setting (0.3..3) on top of lookSensitivity.")]
        public float sensitivity = 1f;

        [Header("Body (the experience profile: controls.js setProfile)")]
        public bool canRun = true;
        [Tooltip("Off: Space neither jumps nor vaults (it still stands up from a crouch).")]
        public bool canJump = true;
        [Tooltip("Off (wheelchair): a stair flight is a wall; stairBlocked decides where one starts.")]
        public bool canStairs = true;
        [Tooltip("Eye height standing (1.62; 1.18 seated in a wheelchair).")]
        public float eye = Eye;
        /// <summary>Without stairs: (feet now, feet about to be), Unity, true when that step goes onto a stair flight.</summary>
        public Func<Vector3, Vector3, bool> stairBlocked;

        [Header("Spawn")]
        public string startSpawn = "home";

        public float Stamina { get; private set; } = 1f;
        public bool Winded { get; private set; }
        public bool Running { get; private set; }
        public bool Crouching { get; private set; }

        /// <summary>Back on your feet (after a time skip: still crouched days after the smoke escape looked wrong).</summary>
        public void StandUp() => Crouching = false;
        public bool Grounded => _move.active || _cc.isGrounded;
        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public float HorizontalSpeed { get; private set; }
        public float VerticalSpeed => _vy;
        public float EyeHeight => eye * (Crouching ? 0.6f : 1f);
        /// <summary>Where the body faces (Unity yaw degrees): the move direction in third person, the camera in first.</summary>
        public float BodyYaw { get; private set; }
        /// <summary>Set by the camera rig: the body turns on its own only in third person.</summary>
        public bool ThirdPerson { get; set; } = true;
        /// <summary>The walking velocity this frame (Unity, horizontal).</summary>
        public Vector3 PlanarVelocity { get; private set; }
        /// <summary>A vault or climb is under way (the clip owns the body; no jumping, punching or picking up).</summary>
        public bool Busy => _move.active;

        /// <summary>
        /// The level's vault rules: (feet, unit heading), both Unity, to a plan in the original frame, or null when there
        /// is nothing to get over (then Space jumps). Set by the story host; unset, Space only jumps.
        /// </summary>
        public Func<Vector3, Vector3, VaultPlanner.Plan?> vaultPlanner;
        /// <summary>Optional last check of a planned landing against the scene's colliders (Unity feet position).</summary>
        public Func<Vector3, bool> vaultLandingFree;
        /// <summary>A body clip to play (vault, climb).</summary>
        public event Action<string> Acted;

        struct Move { public bool active; public float t, dur; public Vector3 from; public VaultPlanner.Plan plan; }
        Move _move;

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
            _move.active = false;
            BodyYaw = yaw;
            Yaw = yaw; Pitch = 3f; _planar = Vector3.zero; _vy = 0; // the gaze starts slightly down, like the original
            transform.rotation = Quaternion.Euler(0, Yaw, 0);
            _lastSafe = transform.position; _hasSafe = true;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            bool gated = InputGate.Blocked; // a dialog or menu is open

            // Look
            var look = gated ? Vector2.zero : _in.Look * (lookSensitivity * sensitivity);
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
            Running = moving && canRun && !Winded && !Crouching && _in.Run;
            Stamina = Mathf.Clamp01(Stamina + (Running ? -dt / 7f * staminaUse : dt / (moving ? 11f : 5f)));
            if (Stamina <= 0f) Winded = true;
            else if (Winded && Stamina > 0.35f) Winded = false;

            if (_move.active) { StepMove(dt); return; }

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
                    else if (!canJump) { } // the profile: no jumping, no vaulting
                    else if (TryVault()) { }
                    else { _vy = jumpSpeed; Stamina = Mathf.Max(0, Stamina - 0.06f); }
                }
            }
            else _vy -= gravity * dt;

            if (_move.active) { StepMove(dt); return; } // Space started a vault this frame

            // Without stairs a stair flight is a wall (the body stops at its foot or head; the host may offer help).
            // Runs every grounded frame like controls.js:302-306: standing still asks with ahead == feet, which is never a
            // stop, so the host's contact latch re-arms and the next push into the same flight offers help again.
            if (!canStairs && grounded && stairBlocked != null)
            {
                var pos = transform.position;
                var ahead = _planar.sqrMagnitude > 0.0001f ? pos + _planar * dt + _planar.normalized * Radius : pos;
                if (stairBlocked(pos, ahead)) _planar = Vector3.zero;
            }

            _cc.Move((_planar + Vector3.up * _vy) * dt);
            var v = _cc.velocity; v.y = 0;
            HorizontalSpeed = v.magnitude;
            PlanarVelocity = v;
            TurnBody(dt);

            // Fell out of the world: back to the last safe spot.
            if (_cc.isGrounded) { _lastSafe = transform.position; _hasSafe = true; }
            var points = LevelSpawnPoints.Current;
            if (points != null && transform.position.y < points.killY && _hasSafe) Teleport(_lastSafe, Yaw);
        }
    
        // controls.js: in first person the body is the camera's; in third it turns toward where it walks (damp 10).
        void TurnBody(float dt)
        {
            if (!ThirdPerson) { BodyYaw = Yaw; return; }
            if (HorizontalSpeed <= 0.25f || _planar.sqrMagnitude < 0.0625f) return; // walking, not pushed out of something
            float want = Mathf.Atan2(PlanarVelocity.x, PlanarVelocity.z) * Mathf.Rad2Deg;
            BodyYaw = Mathf.Repeat(BodyYaw + Mathf.DeltaAngle(BodyYaw, want) * (1f - Mathf.Exp(-10f * dt)), 360f);
        }

        /// <summary>
        /// Space in front of something low: plans the vault (or climb) along the walk direction (or, standing, where the
        /// body faces) and starts it. False when there is nothing to get over.
        /// </summary>
        bool TryVault()
        {
            if (vaultPlanner == null) return false;
            var vel = _cc.velocity; vel.y = 0;
            Vector3 dir = vel.magnitude > 0.3f ? vel.normalized : Quaternion.Euler(0, ThirdPerson ? BodyYaw : Yaw, 0) * Vector3.forward;
            VaultPlanner.Plan? plan;
            try { plan = vaultPlanner(transform.position, dir); }
            catch (Exception e) { Debug.LogException(e); return false; }
            if (plan == null) return false;
            var p = plan.Value;
            if (vaultLandingFree != null && !vaultLandingFree(LevelLayout.ToUnity((float)p.X1, (float)p.Y1, (float)p.Z1))) return false;
            _move = new Move { active = true, t = 0, dur = (float)p.Duration, from = transform.position, plan = p };
            Stamina = Mathf.Max(0, Stamina - (float)p.Stamina);
            _planar = Vector3.zero; _vy = 0; HorizontalSpeed = 0; PlanarVelocity = Vector3.zero;
            _cc.enabled = false; // the body goes over the top on the planned arc, not through the capsule's collisions
            BodyYaw = Mathf.Atan2((float)p.X1 - _move.from.x, -(float)p.Z1 - _move.from.z) * Mathf.Rad2Deg;
            Acted?.Invoke(p.Clip);
            return true;
        }

        // The scripted vault / climb: eased along the line with an arc over the top (VaultPlanner.MoveAt).
        void StepMove(float dt)
        {
            _move.t = Mathf.Min(1f, _move.t + dt / Mathf.Max(0.05f, _move.dur));
            var f = _move.from;
            var (x, y, z) = VaultPlanner.MoveAt(f.x, f.y, -f.z, _move.plan, _move.t);
            transform.position = new Vector3((float)x, (float)y, -(float)z);
            if (_move.t < 1f) return;
            _move.active = false;
            _cc.enabled = true;
            _vy = -2f;
            _cc.Move(Vector3.down * 0.05f); // settle on the landing
            _lastSafe = transform.position; _hasSafe = true;
        }
}
}
