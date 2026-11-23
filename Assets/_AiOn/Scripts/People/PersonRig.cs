using UnityEngine;

namespace AiOn.People
{
    /// <summary>
    /// Where props attach on a Rocketbox person. The people builder adds three empty anchors to each prefab, posed from the
    /// bind pose so they do not depend on the animation: a grip in each palm (Grip_R, Grip_L: under the hand bone; +y toward
    /// the wrist, +z across the knuckles from little finger to index, so a hand hanging at the side has it upright and facing
    /// forward; the palm faces -x on the right hand, +x on the left) and Anchor_Back on the upper chest bone at the surface of
    /// the back (+z forward, y up in the bind pose). <see cref="Find"/> resolves the original rig's names (rHand, lHand,
    /// chest, head) to these anchors, else to the Humanoid bones, else to a child of that name (the old baked people).
    /// </summary>
    public static class PersonRig
    {
        public const string GripR = "Grip_R", GripL = "Grip_L", Back = "Anchor_Back";
        /// <summary>The palm surface sits this far out of the hand bone's line toward the palm (m).</summary>
        public const float PalmOffset = 0.012f;

        /// <summary>The palm's normal in a grip's frame: -x on the right hand, +x on the left.</summary>
        public static Vector3 PalmNormal(bool right) => right ? Vector3.left : Vector3.right;

        /// <summary>The anchor or bone for an original rig name; root when nothing matches.</summary>
        public static Transform Find(Transform root, string name)
        {
            if (!root) return null;
            string anchor = name switch { "rHand" => GripR, "lHand" => GripL, "chest" => Back, _ => null };
            if (anchor != null) { var a = Deep(root, anchor); if (a) return a; }
            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator && animator.isHuman)
            {
                HumanBodyBones? hb = name switch
                {
                    "rHand" => HumanBodyBones.RightHand, "lHand" => HumanBodyBones.LeftHand, "chest" => HumanBodyBones.UpperChest,
                    "head" => HumanBodyBones.Head, "pelvis" => HumanBodyBones.Hips, "neck" => HumanBodyBones.Neck, _ => null,
                };
                if (hb.HasValue)
                {
                    var b = animator.GetBoneTransform(hb.Value);
                    if (!b && hb.Value == HumanBodyBones.UpperChest) b = animator.GetBoneTransform(HumanBodyBones.Chest);
                    if (b) return b;
                }
            }
            return Deep(root, name) ?? root;
        }

        public static Transform Deep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var f = Deep(t.GetChild(i), name); if (f) return f; }
            return null;
        }

        /// <summary>
        /// The grip frame from the bind pose: wrist direction, knuckle line and palm side (pure maths; tested).
        /// Returns the grip's world rotation; position is the palm centre.
        /// </summary>
        public static (Vector3 pos, Quaternion rot) GripFrame(Vector3 wrist, Vector3 middleKnuckle, Vector3 indexKnuckle, Vector3 littleKnuckle, bool right)
        {
            var up = wrist - middleKnuckle;
            up = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up;
            var fwd = Vector3.ProjectOnPlane(indexKnuckle - littleKnuckle, up);
            fwd = fwd.sqrMagnitude > 1e-8f ? fwd.normalized : Vector3.forward;
            var rot = Quaternion.LookRotation(fwd, up);
            var pos = Vector3.Lerp(wrist, middleKnuckle, 0.6f) + rot * PalmNormal(right) * PalmOffset;
            return (pos, rot);
        }

        /// <summary>Adds (or re-poses) the grips and the back anchor; call in the bind pose. Returns a note for the build log.</summary>
        public static string BuildAnchors(Animator a)
        {
            if (!a || !a.isHuman) return "no humanoid";
            var note = "";
            foreach (var right in new[] { true, false })
            {
                var hand = a.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
                var mid = a.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
                var idx = a.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
                var lit = a.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
                if (!hand || !mid || !idx || !lit) { note += right ? "no right fingers " : "no left fingers "; continue; }
                var (pos, rot) = GripFrame(hand.position, mid.position, idx.position, lit.position, right);
                var g = Anchor(hand, right ? GripR : GripL);
                g.SetPositionAndRotation(pos, rot);
                g.localScale = Vector3.one;
            }
            var chest = a.GetBoneTransform(HumanBodyBones.UpperChest) ?? a.GetBoneTransform(HumanBodyBones.Chest);
            if (chest)
            {
                var model = a.transform;
                float back = BackDepth(a, chest.position);
                var b = Anchor(chest, Back);
                b.SetPositionAndRotation(chest.position - model.forward * back - model.up * 0.06f * model.lossyScale.y, model.rotation);
                b.localScale = Vector3.one;
                note += $"back {back:0.000} m";
            }
            return note.Trim();
        }

        static Transform Anchor(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t) return t;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>How far behind the chest bone the back's surface is, from the skinned mesh in the bind pose (m, world).</summary>
        static float BackDepth(Animator a, Vector3 chest)
        {
            var model = a.transform;
            float best = 0f;
            foreach (var smr in a.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!smr.sharedMesh) continue;
                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                var vs = baked.vertices;
                var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one); // baked with the scale
                float band = 0.07f * model.lossyScale.y, width = 0.12f * model.lossyScale.x;
                foreach (var v in vs)
                {
                    var w = m.MultiplyPoint3x4(v);
                    var d = w - chest;
                    if (Mathf.Abs(Vector3.Dot(d, model.up)) > band || Mathf.Abs(Vector3.Dot(d, model.right)) > width) continue;
                    best = Mathf.Max(best, -Vector3.Dot(d, model.forward));
                }
                Object.DestroyImmediate(baked);
            }
            return best > 0.03f ? best : 0.13f * model.lossyScale.z;
        }
    }
}
