// Pure math behind the minigame studios (UI/Studio): the two-bone arm reach, the swing-twist split that moves a
// wrist's turn into the forearm, the kneeling rescuer's layout over a chest, and a small value-noise used by the
// procedural textures. No Unity objects, only Vector3 / Quaternion maths, so EditMode tests pin it (MinigameArtTests).
using UnityEngine;

namespace AiOn.UI.Studio
{
    public static class StudioMath
    {
        /// <summary>
        /// Where the elbow goes for an arm rooted at <paramref name="shoulder"/> (upper arm <paramref name="upper"/> m,
        /// forearm <paramref name="lower"/> m) whose wrist reaches for <paramref name="target"/>, bending toward
        /// <paramref name="pole"/>. A target out of reach is met at full stretch along the line to it; one too close is
        /// held at the shortest reach. The returned reach (shoulder to wrist) is what the wrist really gets.
        /// </summary>
        public static Vector3 Elbow(Vector3 shoulder, Vector3 target, float upper, float lower, Vector3 pole, out float reach)
        {
            var to = target - shoulder;
            float d = to.magnitude;
            var dir = d > 1e-5f ? to / d : Vector3.down;
            float min = Mathf.Abs(upper - lower) + 1e-4f, max = upper + lower - 1e-4f;
            reach = Mathf.Clamp(d, min, max);
            float x = (upper * upper - lower * lower + reach * reach) / (2f * reach);
            float h = Mathf.Sqrt(Mathf.Max(0f, upper * upper - x * x));
            var side = Vector3.ProjectOnPlane(pole - shoulder, dir);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.ProjectOnPlane(Vector3.up, dir);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.ProjectOnPlane(Vector3.right, dir);
            side.Normalize();
            return shoulder + dir * x + side * h;
        }

        /// <summary>The part of <paramref name="q"/> that turns about <paramref name="axis"/> (swing-twist split).</summary>
        public static Quaternion Twist(Quaternion q, Vector3 axis)
        {
            axis.Normalize();
            var v = new Vector3(q.x, q.y, q.z);
            var p = Vector3.Dot(v, axis) * axis;
            var t = new Quaternion(p.x, p.y, p.z, q.w);
            float n = Mathf.Sqrt(t.x * t.x + t.y * t.y + t.z * t.z + t.w * t.w);
            if (n < 1e-6f) return Quaternion.identity;
            return new Quaternion(t.x / n, t.y / n, t.z / n, t.w / n);
        }

        /// <summary>A kneeling rescuer's layout over a chest (all lengths in metres; angles in degrees).</summary>
        public struct KneelPose
        {
            /// <summary>The trunk's lean forward from upright.</summary>
            public float Lean;
            /// <summary>How far the hip joints sit behind the hands (horizontally).</summary>
            public float Back;
            /// <summary>The hip joints' height above the floor.</summary>
            public float HipHeight;
            /// <summary>How far in front of the hip joints the knees rest on the floor.</summary>
            public float KneeForward;
            /// <summary>The shoulders' height above the floor (straight over the hands).</summary>
            public float ShoulderHeight;
        }

        /// <summary>
        /// A rescuer kneeling at a patient's side with the knees on the floor against his flank and the trunk leaning
        /// forward from the hips until the shoulders are straight over the hands with the arms all but locked.
        /// <paramref name="trunk"/> is hip joint to shoulder, <paramref name="arm"/> shoulder to the heel of the hand,
        /// <paramref name="halfShoulders"/> half the shoulder width (each arm also reaches in to the middle),
        /// <paramref name="thigh"/> hip joint to knee joint, <paramref name="chestTop"/> the hands' height,
        /// <paramref name="kneeGap"/> how far from the hands (toward the rescuer) the knees can come before they touch
        /// him, <paramref name="kneeHeight"/> the knee joint's height when it rests on the floor. The lean is the one
        /// where the height the legs hold the hips at meets the height the straight arms need.
        /// </summary>
        public static KneelPose Kneel(float trunk, float arm, float halfShoulders, float thigh, float chestTop, float kneeGap, float kneeHeight)
        {
            float reach = arm * 0.97f; // a hair short of locked: the elbows never snap straight
            float down = Mathf.Sqrt(Mathf.Max(0.01f, reach * reach - halfShoulders * halfShoulders));
            float shoulder = chestTop + down;
            float lo = 5f, hi = 75f;
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Gap(mid) > 0) hi = mid; else lo = mid;
            }
            float lean = (lo + hi) * 0.5f;
            float r = lean * Mathf.Deg2Rad;
            float back = trunk * Mathf.Sin(r);
            return new KneelPose
            {
                Lean = lean, Back = back, HipHeight = shoulder - trunk * Mathf.Cos(r),
                KneeForward = Mathf.Max(0f, back - kneeGap), ShoulderHeight = shoulder,
            };

            // the hips' height the arms need minus the height the legs hold them at (rises with the lean)
            float Gap(float deg)
            {
                float a = deg * Mathf.Deg2Rad;
                float b = trunk * Mathf.Sin(a);
                float f = Mathf.Max(0f, b - kneeGap);
                float legs = kneeHeight + Mathf.Sqrt(Mathf.Max(0f, thigh * thigh - f * f));
                return shoulder - trunk * Mathf.Cos(a) - legs;
            }
        }

        /// <summary>
        /// A mesh of a box <paramref name="size"/> with edges and corners rounded to <paramref name="radius"/>: each face a
        /// <paramref name="steps"/> x <paramref name="steps"/> grid on the cube, every point moved to the rounded surface
        /// (the nearest point of the inner box plus the radius along the way out), its normal that way out.
        /// </summary>
        public static Mesh RoundedBox(Vector3 size, float radius, int steps)
        {
            var half = size * 0.5f;
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(half.x, Mathf.Min(half.y, half.z)));
            var inner = half - Vector3.one * radius;
            int n = Mathf.Max(1, steps), per = (n + 1) * (n + 1);
            var verts = new Vector3[per * 6];
            var norms = new Vector3[per * 6];
            var uvs = new Vector2[per * 6];
            var tris = new int[n * n * 6 * 6];
            Vector3[] fwd = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            Vector3[] ax = { Vector3.forward, Vector3.back, Vector3.right, Vector3.right, Vector3.left, Vector3.right };
            int ti = 0;
            for (int f = 0; f < 6; f++)
            {
                var nrm = fwd[f]; var u = ax[f]; var v = Vector3.Cross(nrm, u);
                for (int j = 0; j <= n; j++)
                    for (int i = 0; i <= n; i++)
                    {
                        float a = i / (float)n * 2f - 1f, b = j / (float)n * 2f - 1f;
                        var p = Vector3.Scale(nrm + u * a + v * b, half); // on the box
                        var q = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                        var o = p - q;
                        var dir = o.sqrMagnitude > 1e-12f ? o.normalized : nrm;
                        int k = f * per + j * (n + 1) + i;
                        verts[k] = q + dir * radius;
                        norms[k] = dir;
                        uvs[k] = new Vector2(i / (float)n, j / (float)n);
                    }
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        int k = f * per + j * (n + 1) + i;
                        tris[ti++] = k; tris[ti++] = k + 1; tris[ti++] = k + n + 1; // front faces wind so cross(b-a, c-a) points out
                        tris[ti++] = k + 1; tris[ti++] = k + n + 2; tris[ti++] = k + n + 1;
                    }
            }
            var m = new Mesh { name = "RoundedBox", vertices = verts, normals = norms, uv = uvs, triangles = tris };
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A smooth value noise 0..1 that tiles every <paramref name="period"/> cells (for seamless textures).</summary>
        public static float Noise(float x, float y, int period, int seed = 0) => Noise(x, y, period, period, seed);

        /// <summary><see cref="Noise"/> tiling every <paramref name="px"/> cells in x and <paramref name="py"/> in y (0: never).</summary>
        public static float Noise(float x, float y, int px, int py, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Hash(x0, y0, px, py, seed), b = Hash(x0 + 1, y0, px, py, seed);
            float c = Hash(x0, y0 + 1, px, py, seed), d = Hash(x0 + 1, y0 + 1, px, py, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Fractal sum of <see cref="Noise"/> over <paramref name="octaves"/>, 0..1, tiling at <paramref name="period"/> cells.</summary>
        public static float Fbm(float x, float y, int period, int octaves, int seed = 0) => Fbm(x, y, period, period, octaves, seed);

        /// <summary><see cref="Fbm"/> with its own period in x and in y.</summary>
        public static float Fbm(float x, float y, int px, int py, int octaves, int seed)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Noise(x, y, px, py, seed + i * 31);
                norm += amp;
                x *= 2; y *= 2; px *= 2; py *= 2; amp *= 0.5f;
            }
            return sum / norm;
        }

        static float Hash(int x, int y, int px, int py, int seed)
        {
            if (px > 0) x = ((x % px) + px) % px;
            if (py > 0) y = ((y % py) + py) % py;
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 362437);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }
    }
}
