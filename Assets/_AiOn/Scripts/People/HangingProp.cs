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
        public const float Drop = 0.072f;
        public Transform body;

        void LateUpdate()
        {
            var hand = transform.parent;
            if (!hand) return;
            var yaw = body ? body.eulerAngles.y : 0f;
            transform.SetPositionAndRotation(hand.position + Vector3.down * Drop, Quaternion.Euler(0, yaw, 0));
        }
    }
}
