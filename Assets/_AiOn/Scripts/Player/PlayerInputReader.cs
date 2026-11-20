using UnityEngine;
using UnityEngine.InputSystem;

namespace AiOn.Player
{
    /// <summary>
    /// Player input as the original web build maps it: WASD/arrows move, Shift runs, Space jumps, C crouches,
    /// the mouse looks (locked after a click, Esc frees it), E/Enter interacts, V toggles first/third person.
    /// Gamepad: left stick, right stick, L3 run, A jump, B crouch, X interact, Y view.
    /// </summary>
    [DefaultExecutionOrder(-100)] // input is read before the controller and camera use it
    public class PlayerInputReader : MonoBehaviour
    {
        InputAction _move, _look, _lookStick, _run, _jump, _crouch, _interact, _view, _unlock, _lockClick;

        /// <summary>Scripted movement (cutscenes, automated checks) in place of the player's input while set.</summary>
        public Vector2? MoveOverride { get; set; }
        public Vector2 Move => MoveOverride ?? _move.ReadValue<Vector2>();
        /// <summary>Look delta this frame: mouse pixels, or stick scaled to a comparable rate.</summary>
        public Vector2 Look { get; private set; }
        public bool Run => _run.IsPressed();
        public bool JumpPressed => _jump.WasPressedThisFrame();
        public bool CrouchPressed => _crouch.WasPressedThisFrame();
        public bool InteractPressed => _interact.WasPressedThisFrame();
        public bool ViewPressed => _view.WasPressedThisFrame();
        public bool CursorLocked => Cursor.lockState == CursorLockMode.Locked;

        [Tooltip("Stick look speed in 'pixels' per second, to match mouse deltas.")]
        public float stickLookRate = 900f;

        void Awake()
        {
            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick");

            _look = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
            _lookStick = new InputAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick");

            _run = new InputAction("Run", InputActionType.Button);
            _run.AddBinding("<Keyboard>/leftShift"); _run.AddBinding("<Keyboard>/rightShift"); _run.AddBinding("<Gamepad>/leftStickPress");
            _jump = new InputAction("Jump", InputActionType.Button);
            _jump.AddBinding("<Keyboard>/space"); _jump.AddBinding("<Gamepad>/buttonSouth");
            _crouch = new InputAction("Crouch", InputActionType.Button);
            _crouch.AddBinding("<Keyboard>/c"); _crouch.AddBinding("<Gamepad>/buttonEast");
            _interact = new InputAction("Interact", InputActionType.Button);
            _interact.AddBinding("<Keyboard>/e"); _interact.AddBinding("<Keyboard>/enter"); _interact.AddBinding("<Gamepad>/buttonWest");
            _view = new InputAction("View", InputActionType.Button);
            _view.AddBinding("<Keyboard>/v"); _view.AddBinding("<Gamepad>/buttonNorth");
            _unlock = new InputAction("Unlock", InputActionType.Button, "<Keyboard>/escape");
            _lockClick = new InputAction("LockClick", InputActionType.Button, "<Mouse>/leftButton");
        }

        InputAction[] All => new[] { _move, _look, _lookStick, _run, _jump, _crouch, _interact, _view, _unlock, _lockClick };

        void OnEnable() { foreach (var a in All) a.Enable(); }
        void OnDisable() { foreach (var a in All) a.Disable(); }
        void OnDestroy() { foreach (var a in All) a.Dispose(); }

        void Update()
        {
            // A screen that takes the pointer keeps the cursor out and visible, whatever locked it meanwhile (the player's
            // first Start locks it after the title is already up); a click on that screen is a UI click, not a lock.
            if (PointerWanted && (CursorLocked || !Cursor.visible)) SetCursorLocked(false);
            // (the original's click-while-locked fist is gone: the competition upgrade has no hitting)
            if (_unlock.WasPressedThisFrame()) SetCursorLocked(false);
            else if (_lockClick.WasPressedThisFrame() && !CursorLocked && !PointerWanted) SetCursorLocked(true);

            var mouse = CursorLocked ? _look.ReadValue<Vector2>() : Vector2.zero;
            var stick = _lookStick.ReadValue<Vector2>() * (stickLookRate * Time.unscaledDeltaTime);
            Look = mouse + stick;
        }

        public static void SetCursorLocked(bool locked)
        {
            if (locked && PointerWanted) locked = false; // a screen still needs the mouse
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        static int s_pointerHolds;

        /// <summary>True while a screen that is clicked or typed in is open (menus, choices, cards, the bag...).</summary>
        public static bool PointerWanted => s_pointerHolds > 0;

        /// <summary>A screen that needs the mouse opened: the cursor comes out at once (counted, so screens can stack).</summary>
        public static void HoldPointer()
        {
            s_pointerHolds++;
            SetCursorLocked(false);
        }

        /// <summary>That screen closed: when the last one goes, the cursor locks back for looking around.</summary>
        public static void ReleasePointer()
        {
            if (s_pointerHolds <= 0) return;
            if (--s_pointerHolds == 0) SetCursorLocked(true);
        }

        // Play mode may start without a domain reload: never carry a hold over from the last session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPointer() => s_pointerHolds = 0;
    }
}
