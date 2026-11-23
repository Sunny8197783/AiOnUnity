using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiOn.People;
using UnityEditor;
using UnityEngine;

namespace AiOn.EditorTools
{
    /// <summary>
    /// Contact sheets of the people prefabs playing their game states, sampled in a preview scene (nothing is saved to a
    /// scene): AiOn → People → Animation Check Sheets writes Captures/people/anim_&lt;preset&gt;_{34,side}.png, one figure per
    /// state on a 1 m grid (a white floor line marks where the feet should be), so facing, foot contact, retargeting and
    /// props can be judged from images.
    /// </summary>
    public static class PeopleAnimPreview
    {
        public static readonly string[] States =
        {
            "idle", "walk", "brisk", "run", "carry", "talk", "phone", "wave", "point", "sitChair", "sit", "sitB", "crouch", "down",
            "stairsUp", "knock", "walker", "lie", "cart", "push",
        };

        [MenuItem("AiOn/People/Animation Check Sheets")]
        public static void SheetsMenu()
        {
            foreach (var p in new[] { "jeongmin", "seoyeon", "elder" }) Sheet(p, "Captures/people", 0.3f);
            Debug.Log("[AiOn] people animation sheets in Captures/people");
        }

        /// <summary>Renders one sheet per view for a preset; t is the normalized time sampled.</summary>
        public static string Sheet(string preset, string dir, float t, string[] states = null, float camYaw = 35f, string suffix = null)
        {
            states ??= States;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PeopleBuilder.PrefabPath(preset));
            if (!prefab) return "no prefab " + preset;
            var paths = new List<string>();
            foreach (var view in suffix != null ? new[] { (suffix, camYaw) } : new[] { ("34", camYaw), ("side", 90f) })
            {
                using var s = new RocketboxPreview.Stage();
                float spacing = 1.1f, x0 = -(states.Length - 1) * spacing * 0.5f;
                for (int i = 0; i < states.Length; i++)
                {
                    var go = (GameObject)Object.Instantiate(prefab);
                    s.Add(go);
                    go.transform.SetPositionAndRotation(new Vector3(x0 + i * spacing, 0, 0), Quaternion.Euler(0, view.Item2, 0)); // turned, the camera stays in front
                    Pose(go, states[i], t);
                }
                // floor line at y = 0 along the row
                var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                s.Add(line);
                line.transform.localScale = new Vector3(states.Length * spacing, 0.004f, 0.02f);
                line.transform.position = new Vector3(0, 0.002f, 0.25f);
                var lm = new Material(Shader.Find("Universal Render Pipeline/Unlit")); s.Temp(lm);
                lm.SetColor("_BaseColor", Color.white);
                line.GetComponent<Renderer>().sharedMaterial = lm;
                float width = states.Length * spacing;
                var cam = s.Cam;
                cam.fieldOfView = 24f;
                float dist = width * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / 2.6f;
                cam.transform.position = new Vector3(0, 1.1f, dist);
                cam.transform.LookAt(new Vector3(0, 0.8f, 0));
                var path = $"{dir}/anim_{preset}_{view.Item1}.png";
                s.Render(path, 3000, 760);
                paths.Add(path);
            }
            return string.Join(", ", paths);
        }

        /// <summary>Puts a person in a state at a normalized time (edit mode: rebinds and steps the animator once).</summary>
        public static void Pose(GameObject go, string state, float t)
        {
            var person = new UnityPerson(go.name, go);
            var anim = go.GetComponentInChildren<Animator>();
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.Rebind();
            // sheet-only names: "sitB" the other half of a seated crowd (leaning back), "cart"/"push" a cart standing / walking
            float phase = 0.25f; string hold = null;
            if (state == "sitB") { state = "sit"; phase = 0.75f; }
            if (state == "cart" || state == "push") { hold = PersonClips.ArmsCart; PersonProps.Set(go, "cart", true); state = state == "cart" ? "idle" : "walk"; }
            var pa = go.GetComponentInChildren<PersonAnimator>();
            if (pa) anim.SetFloat(PersonClips.GaitScaleParam, pa.gaitScale);
            bool gait = state == "walk" || state == "brisk" || state == "run";
            if (state == UnityPerson.Lie) { person.SetAnim(UnityPerson.Lie); return; } // held: the animator stops on that pose
            if (gait) anim.SetFloat("Speed", state == "walk" ? PersonAnimator.WalkSpeed : state == "brisk" ? PersonAnimator.BriskSpeed : PersonAnimator.RunSpeed);
            var st = gait ? "Locomotion" : state == UnityPerson.Lie ? "idle" : state;
            anim.Play(st, 0, state == UnityPerson.Lie ? 0f : t);
            if (anim.layerCount > PersonClips.ArmsLayer) anim.Play(PersonClips.ArmsFor(st, phase, hold), PersonClips.ArmsLayer, 0f);
            if (state == "carry") anim.SetFloat("Speed", PersonAnimator.WalkSpeed);
            anim.Update(0f);
            anim.Update(0.001f);
            var auto = state == "carry" ? "waterJug" : state == "phone" ? "phone" : null;
            if (auto != null) PersonProps.Set(go, auto, true);
            if (state == "walk") PersonProps.Set(go, "backpack", true);
            if (state == "crouch") PersonProps.Set(go, "flashlight", true);
            anim.Update(0f);
            foreach (var hp in go.GetComponentsInChildren<HangingProp>()) hp.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
        }
    }
}
