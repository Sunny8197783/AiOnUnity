using System.Collections.Generic;
using UnityEngine;

namespace AiOn.Levels
{
    /// <summary>Named spawn points of a level scene, in Unity space. The player asks for one by name.</summary>
    public class LevelSpawnPoints : MonoBehaviour
    {
        [System.Serializable]
        public struct Point
        {
            public string name;
            public Vector3 position;
            [Tooltip("Degrees about +y.")]
            public float yaw;
        }

        public List<Point> points = new();

        [Tooltip("Below this height the player is lost (fell out of the level) and is put back.")]
        public float killY = -20f;

        public bool TryGet(string spawnName, out Point point)
        {
            foreach (var p in points)
                if (p.name == spawnName) { point = p; return true; }
            point = default;
            return false;
        }

        public static LevelSpawnPoints Current => FindFirstObjectByType<LevelSpawnPoints>();

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.6f);
            foreach (var p in points)
            {
                Gizmos.DrawWireSphere(p.position + Vector3.up * 0.1f, 0.25f);
                Gizmos.DrawRay(p.position + Vector3.up * 0.1f, Quaternion.Euler(0, p.yaw, 0) * Vector3.forward * 0.8f);
            }
        }
    }
}
