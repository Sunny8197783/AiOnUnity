using UnityEngine;

namespace AiOn.People
{
    /// <summary>
    /// Keeps a hanging prop (water jug, mart basket) plumb under the hand it is parented to, turned with the person's
    /// body, after the animator posed the arm. Added by <see cref="PersonProps"/> when it builds such a prop, so crowd
    /// members and story characters alike carry it level; no lookups per frame.
    /// </summary>
    public sealed class HangingProp : MonoBehaviour
    {
        /// <summary>Below the old rig's hand bone (the wrist) / below a Rocketbox palm grip (m).</summary>
        public const float Drop = 0.072f, GripDrop = 0.012f;
        public Transform body;
        public float drop = Drop;

        void LateUpdate()
        {
            var hand = transform.parent;
            if (!hand) return;
            var yaw = body ? body.eulerAngles.y : 0f;
            transform.SetPositionAndRotation(hand.position + Vector3.down * drop, Quaternion.Euler(0, yaw, 0));
        }
    }
}
