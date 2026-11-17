using System;
using System.Collections.Generic;
using AiOn.Player;
using UnityEngine;

namespace AiOn.Interaction
{
    /// <summary>
    /// Finds the interaction point the player is at each frame (InteractionPicker rules) and raises Interacted on E.
    /// A point is hidden when a collider blocks the line to it both at 0.3 m and at 1.5 m above the feet (low
    /// furniture or a high sign alone does not hide it); the point's own collider (holding it, or within 0.25 m)
    /// and its host door never hide it.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerInteractor : MonoBehaviour
    {
        public PlayerCameraRig cameraRig;
        public LayerMask blockers = ~0;

        public InteractionPoint Current { get; private set; }
        public event Action<InteractionPoint> Interacted;
        public event Action<InteractionPoint> CurrentChanged;

        readonly InteractionPicker _picker = new();
        readonly List<IReachable> _points = new();
        readonly float[] _yaws = new float[2];
        readonly RaycastHit[] _hits = new RaycastHit[16];
        PlayerController _pc;
        PlayerInputReader _in;

        void Awake()
        {
            _pc = GetComponent<PlayerController>();
            _in = GetComponent<PlayerInputReader>();
            if (!cameraRig) cameraRig = FindFirstObjectByType<PlayerCameraRig>();
        }

        void Update()
        {
            _points.Clear();
            foreach (var p in InteractionPoint.All) _points.Add(p);
            _yaws[0] = cameraRig && cameraRig.cam ? cameraRig.cam.transform.eulerAngles.y : _pc.Yaw;
            _yaws[1] = _pc.BodyYaw; // reach.js: the camera's yaw and the body's

            var picked = InputGate.Blocked ? null : _picker.Pick(_points, transform.position, _yaws, Hidden) as InteractionPoint;
            if (picked != Current) { Current = picked; CurrentChanged?.Invoke(Current); }

            if (!InputGate.Blocked && InputGate.ReleasedFrame != Time.frameCount && Current != null && _in.InteractPressed) Interact();
        }

        /// <summary>Uses the current point (E). Also the hook for automated checks.</summary>
        public bool Interact()
        {
            if (Current == null) return false;
            Interacted?.Invoke(Current);
            return true;
        }

        /// <summary>Picks again right now (after a teleport, before the next frame).</summary>
        public InteractionPoint Refresh()
        {
            _picker.Reset();
            Update();
            return Current;
        }

        bool Hidden(IReachable r)
        {
            var p = (InteractionPoint)r;
            var feet = transform.position;
            var to = p.Position - feet;
            float flat = new Vector2(to.x, to.z).magnitude;
            if (flat < 0.4f) return false;
            float k = (flat - 0.25f) / flat; // the last 0.25 m before the point is ignored
            var d = new Vector3(to.x * k, to.y * k, to.z * k);
            return Blocked(p, feet + Vector3.up * 0.3f, d) && Blocked(p, feet + Vector3.up * 1.5f, d);
        }

        bool Blocked(InteractionPoint p, Vector3 from, Vector3 d)
        {
            int n = Physics.RaycastNonAlloc(from, d.normalized, _hits, d.magnitude, blockers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = _hits[i].collider;
                if (col.transform.IsChildOf(transform)) continue;
                if (col.gameObject.layer == 2) continue; // people (crowds, layer Ignore Raycast) never hide a prompt
                if (Own(p, col)) continue;
                return true;
            }
            return false;
        }

        static bool Own(InteractionPoint p, Collider col)
        {
            var door = col.GetComponentInParent<Door>();
            if (door && p.IsHost(door.id)) return true;
            var b = col.bounds;
            var pos = p.Position;
            // Holds it, or abuts it within 0.25 m (a notice board on its wall, an ATM point in front of the ATM).
            return pos.x >= b.min.x - 0.25f && pos.x <= b.max.x + 0.25f && pos.z >= b.min.z - 0.25f && pos.z <= b.max.z + 0.25f;
        }
    }
}
