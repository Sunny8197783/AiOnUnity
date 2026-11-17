using System;
using System.Collections.Generic;
using UnityEngine;

namespace AiOn.Levels
{
    /// <summary>
    /// A level's walkable floors, collision boxes, ceilings and spawn points, in the original web build's frame
    /// (three.js: x east, y up, z south; yaw 0 faces north, positive yaw turns left). Use the To* helpers for Unity:
    /// Unity x = east, y = up, z = north, so z flips and yaw turns the other way.
    /// </summary>
    [CreateAssetMenu(menuName = "AiOn/Level Layout", fileName = "LevelLayout")]
    public class LevelLayout : ScriptableObject
    {
        [Serializable]
        public class WalkZone
        {
            public float x0, x1, z0, z1;
            [Tooltip("Floor height of a flat zone.")]
            public float y;
            public bool isRamp;
            [Tooltip("Ramps (stair flights, slopes): height y0 at the low coordinate of the axis to y1 at the high one.")]
            public float rampY0, rampY1;
            [Tooltip("'z': runs z0 -> z1. 'x': runs x0 -> x1.")]
            public string rampAxis = "z";
        }

        [Serializable]
        public class SolidBox
        {
            public float x0, x1, y0, y1, z0, z1;
            [Tooltip("Groups the boxes of one door leaf etc. Empty for plain walls and furniture.")]
            public string id;
            [Tooltip("Inactive in the level's default state (an open door's closed slab, a closed door's swing boxes).")]
            public bool off;
            public bool noClimb, noVault;

            public bool IsEmpty => x1 - x0 <= 0 || y1 - y0 <= 0 || z1 - z0 <= 0;
        }

        [Serializable]
        public class Ceiling
        {
            public float x0, x1, z0, z1, y;
        }

        [Serializable]
        public class Spawn
        {
            public string name;
            public float x, y, z;
            [Tooltip("Original yaw in radians (0 = north, positive turns left).")]
            public float yaw;
        }

        [Serializable]
        public class Point
        {
            public string id, label, kind;
            public float x, y, z;
            [Tooltip("Own reach radius (0 = default). Never under 1.6 m.")]
            public float r;
            [Tooltip("Explicit reach that overrides r and the 1.6 m minimum (0 = none).")]
            public float reach;
            [Tooltip("Solid id(s) the point sits on (its door leaf), which never hide it.")]
            public string host;
            public bool enabled = true;
        }

        /// <summary>A hinged door, as the original B.door(axis, c, a, b, y, h, opts) call that built it.</summary>
        [Serializable]
        public class DoorDef
        {
            public string id;
            [Tooltip("'z': the doorway runs along x at z = c. 'x': it runs along z at x = c.")]
            public string axis = "z";
            public float c, a, b, y, h = 2.1f;
            [Tooltip("'a': hinge at the low end of a..b, 'b': at the high end.")]
            public string hinge = "a";
            [Tooltip("Which side it swings to (+1 / -1).")]
            public int dir = 1;
            [Range(0, 1)] public float open;
            [Tooltip("Blocks the doorway while closed.")]
            public bool solid = true;
            public bool locked;
            [Tooltip("Opens by itself while the player is this close on the same floor (0 = never).")]
            public float auto;
        }

        /// <summary>A solid that is part of a table door: its leaf boxes (by id) or a thin box in its swing area at its floor.</summary>
        public static bool IsDoorSolid(LevelLayout.DoorDef d, LevelLayout.SolidBox s)
        {
            if (!string.IsNullOrEmpty(s.id)) return s.id == d.id;
            if (Mathf.Abs(s.y0 - d.y) > 0.06f) return false;
            float w = d.b - d.a;
            float thin = Mathf.Min(s.x1 - s.x0, s.z1 - s.z0);
            if (thin > 0.22f) return false;
            return d.axis == "z"
                ? s.x0 >= d.a - 0.05f && s.x1 <= d.b + 0.05f && s.z0 >= d.c - w - 0.1f && s.z1 <= d.c + w + 0.1f
                : s.z0 >= d.a - 0.05f && s.z1 <= d.b + 0.05f && s.x0 >= d.c - w - 0.1f && s.x1 <= d.c + w + 0.1f;
        }

        /// <summary>Where a named character stands when the level opens.</summary>
        [Serializable]
        public class Npc
        {
            public string name;
            [Tooltip("Person preset (jeongmin, seoyeon, manager, clerk, guide, coworker, elder, crowd).")]
            public string preset;
            public float x, y, z, yaw;
            public bool visible = true;
        }

        public string levelId;
        [Tooltip("Default environment preset of the level (env-presets.json): interior, underground, day...")]
        public string env = "interior";
        public List<WalkZone> walk = new();
        public List<SolidBox> solids = new();
        public List<Ceiling> ceilings = new();
        public List<Spawn> spawns = new();
        public List<Point> points = new();
        public List<DoorDef> doors = new();
        public List<Npc> npcs = new();

        // ---------- frame conversion ----------

        public static Vector3 ToUnity(float x, float y, float z) => new(x, y, -z);

        /// <summary>Unity yaw in degrees (rotation about +y, clockwise seen from above).</summary>
        public static float ToUnityYaw(float yawRad) => -yawRad * Mathf.Rad2Deg;

        /// <summary>Centre and size of an axis-aligned box given in the original frame.</summary>
        public static (Vector3 center, Vector3 size) BoxToUnity(float x0, float x1, float y0, float y1, float z0, float z1) =>
            (ToUnity((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), new Vector3(x1 - x0, y1 - y0, z1 - z0));

        public Spawn FindSpawn(string spawnName) => spawns.Find(s => s.name == spawnName);
    }
}
