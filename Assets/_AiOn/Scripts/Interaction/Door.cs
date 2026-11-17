using AiOn.Levels;
using UnityEngine;

namespace AiOn.Interaction
{
    /// <summary>
    /// A hinged door like the original B.door: the leaf turns up to 90° about its hinge; open (k) runs 0..1.
    /// While it swings the leaf does not shove the player (its collider is off); at rest it blocks, except a door
    /// built non-solid, which only blocks while more than half open. Auto doors open while the player is close on
    /// the same floor (the stairwell fire doors, the 302 front door).
    /// </summary>
    public class Door : MonoBehaviour
    {
        public string id;
        public LevelLayout.DoorDef def;
        public Transform pivot;
        public BoxCollider leafCollider;

        [Range(0, 1)] public float k;
        [Range(0, 1)] public float target;
        [Tooltip("Open per second when moved by script or auto (the original: auto 1.8, scripted 1.2).")]
        public float speed = 1.8f;

        Transform _player;

        public const float MaxAngleDeg = 90f;

        /// <summary>Unity rotation of the leaf about y, in degrees, at opening k (the original's phi, mirrored).</summary>
        public static float AngleDeg(LevelLayout.DoorDef d, float k)
        {
            int ang = d.axis == "z" ? (d.hinge == "a" ? -d.dir : d.dir) : (d.hinge == "a" ? d.dir : -d.dir);
            float phi = ang * k * MaxAngleDeg; // three.js: counter-clockwise from above
            return -phi; // Unity's z is mirrored, so the turn flips
        }

        /// <summary>Leaf width (the doorway less 3 cm), hinge side sign and hinge position in Unity space.</summary>
        public static (float lw, int sgn, Vector3 hinge) Geometry(LevelLayout.DoorDef d)
        {
            float w = d.b - d.a, lw = w - 0.03f;
            int sgn = d.hinge == "a" ? 1 : -1;
            float hu = d.hinge == "a" ? d.a + 0.015f : d.b - 0.015f;
            var hinge = d.axis == "z" ? LevelLayout.ToUnity(hu, d.y, d.c) : LevelLayout.ToUnity(d.c, d.y, hu);
            return (lw, sgn, hinge);
        }

        /// <summary>Direction from the hinge to the free edge while closed, in Unity space.</summary>
        public static Vector3 ClosedLeafDir(LevelLayout.DoorDef d, int sgn) =>
            d.axis == "z" ? new Vector3(sgn, 0, 0) : new Vector3(0, 0, -sgn);

        /// <summary>Where the leaf's free edge is at opening k (Unity space, floor height).</summary>
        public static Vector3 FreeEdge(LevelLayout.DoorDef d, float k)
        {
            var (lw, sgn, hinge) = Geometry(d);
            return hinge + Quaternion.Euler(0, AngleDeg(d, k), 0) * ClosedLeafDir(d, sgn) * lw;
        }

        public Vector3 Center => def.axis == "z"
            ? LevelLayout.ToUnity((def.a + def.b) / 2, def.y, def.c)
            : LevelLayout.ToUnity(def.c, def.y, (def.a + def.b) / 2);

        void Start()
        {
            var pc = FindFirstObjectByType<Player.PlayerController>();
            if (pc) _player = pc.transform;
            Apply(k, still: true);
        }

        /// <summary>Opens or closes it by script (e.g. 303 when Seoyeon opens her door: 0.72).</summary>
        public void SetTarget(float open, float perSecond = 1.2f) { target = Mathf.Clamp01(open); speed = perSecond; }

        void Update()
        {
            if (def.auto > 0 && _player)
            {
                var p = _player.position;
                var c = Center;
                bool near = new Vector2(p.x - c.x, p.z - c.z).magnitude < def.auto && Mathf.Abs(p.y - def.y) < 1.2f;
                target = near ? 1f : 0f;
                speed = 1.8f;
            }
            if (!Mathf.Approximately(k, target))
            {
                k = Mathf.MoveTowards(k, target, speed * Time.deltaTime);
                Apply(k, still: Mathf.Approximately(k, target));
            }
        }

        public void Apply(float open, bool still)
        {
            k = open;
            if (pivot) pivot.localRotation = Quaternion.Euler(0, AngleDeg(def, k), 0);
            if (leafCollider) leafCollider.enabled = still && (def.solid || k >= 0.5f);
        }
    }
}
