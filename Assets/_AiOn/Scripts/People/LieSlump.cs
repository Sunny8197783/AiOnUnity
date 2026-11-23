using UnityEngine;

namespace AiOn.People
{
    /// <summary>
    /// A person lying still (UnityPerson "lie", the shelter CPR): after the held animator writes its pose, the neck and head
    /// roll a little to one side about the spine, so the body lies slack rather than at attention. Added and removed with
    /// the lying pose; nothing allocates.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class LieSlump : MonoBehaviour
    {
        public float roll = -22f;
        Transform _head, _neck, _chest;

        void Awake()
        {
            var a = GetComponent<Animator>();
            if (!a || !a.isHuman) { enabled = false; return; }
            _head = a.GetBoneTransform(HumanBodyBones.Head);
            _neck = a.GetBoneTransform(HumanBodyBones.Neck);
            _chest = a.GetBoneTransform(HumanBodyBones.UpperChest) ?? a.GetBoneTransform(HumanBodyBones.Chest);
            if (!_head || !_neck || !_chest) enabled = false;
        }

        void LateUpdate()
        {
            var axis = (_head.position - _chest.position).normalized; // along the spine
            _neck.rotation = Quaternion.AngleAxis(roll / 3f, axis) * _neck.rotation;
            _head.rotation = Quaternion.AngleAxis(roll * 2f / 3f, axis) * _head.rotation;
        }
    }
}
