using System.Collections.Generic;
using UnityEngine;

namespace AiOn.Interaction
{
    /// <summary>
    /// A place the player can use or look at with E (the original level "point"). Its transform is the spot;
    /// kind tells the story/UI what it is ('door', 'inspect', 'pickup', 'bag', 'npc', ...).
    /// </summary>
    public class InteractionPoint : MonoBehaviour, IReachable
    {
        public string id;
        public string label;
        public string kind = "inspect";
        [Tooltip("Own reach radius (0 = default 1.6 m; never under 1.6 m).")]
        public float radius;
        [Tooltip("Explicit reach overriding radius and the minimum (0 = none).")]
        public float reach;
        [Tooltip("Comma-separated ids of the solids it sits on (its door), which never hide it.")]
        public string host;
        public bool interactable = true;

        static readonly List<InteractionPoint> _all = new();
        public static IReadOnlyList<InteractionPoint> All => _all;

        public Vector3 Position => transform.position;
        public float Radius => radius;
        public float ReachOverride => reach;
        public bool Enabled => interactable && isActiveAndEnabled;

        public bool IsHost(string solidId)
        {
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(solidId)) return false;
            foreach (var h in host.Split(','))
                if (h.Trim() == solidId) return true;
            return false;
        }

        void OnEnable() => _all.Add(this);
        void OnDisable() => _all.Remove(this);

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, 0.12f);
            var r = InteractionPicker.ReachOf(this);
            UnityEditorGizmoCircle(transform.position, r);
        }

        static void UnityEditorGizmoCircle(Vector3 c, float r)
        {
            var prev = c + new Vector3(r, 0, 0);
            for (int i = 1; i <= 32; i++)
            {
                float a = i / 32f * Mathf.PI * 2;
                var next = c + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
