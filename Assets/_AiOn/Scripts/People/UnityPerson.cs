using System.Collections.Generic;
using System.Threading.Tasks;
using AiOn.Levels;
using AiOn.People;
using AiOn.Story;
using UnityEngine;

namespace AiOn.People
{
    /// <summary>A scene character driven by the story (IStoryPerson), in the original frame (x east, z south).</summary>
    public sealed class UnityPerson : IStoryPerson
    {
        public readonly GameObject Root;
        readonly PersonAnimator _anim;

        public UnityPerson(string name, GameObject root)
        {
            Name = name;
            Root = root;
            _anim = root.GetComponentInChildren<PersonAnimator>(true);
        }

        public string Name { get; }
        public bool Visible { get => Root && Root.activeSelf; set { if (Root) Root.SetActive(value); } }
        public double X => Root ? Root.transform.position.x : 0;
        public double Z => Root ? -Root.transform.position.z : 0;

        public void SetPosition(double x, double y, double z, double? yaw = null)
        {
            if (!Root) return;
            Root.transform.position = LevelLayout.ToUnity((float)x, (float)y, (float)z);
            if (yaw.HasValue) Root.transform.rotation = Quaternion.Euler(0, LevelLayout.ToUnityYaw((float)yaw.Value), 0);
        }

        public void SetAnim(string anim, double? speed = null)
        {
            if (_anim) _anim.SetAnim(anim, speed.HasValue ? (float)speed.Value : (float?)null);
            // people.js AUTO: carry shows the jug, phone the phone, while the animation lasts (unless set on by the story)
            var auto = AutoProp(anim);
            if (auto == _autoProp) return;
            if (_autoProp != null && !_props.Contains(_autoProp)) PersonProps.Set(Root, _autoProp, false);
            _autoProp = auto;
            if (auto != null && Root) PersonProps.Set(Root, auto, true);
        }

        static string AutoProp(string anim) => anim switch { "carry" => "waterJug", "phone" => "phone", _ => null };

        readonly HashSet<string> _props = new();
        string _autoProp;
        public void Play(string clip) { if (_anim) _anim.Play(clip); }

        public void LookAt(double? x, double? z)
        {
            // Head-only look is not rigged yet: turn the whole body toward a close target, never away from it.
            if (!Root || !x.HasValue || !z.HasValue) return;
            var to = LevelLayout.ToUnity((float)x.Value, 0, (float)z.Value) - Root.transform.position;
            to.y = 0;
            if (to.sqrMagnitude > 0.01f) Root.transform.rotation = Quaternion.Slerp(Root.transform.rotation, Quaternion.LookRotation(to), 0.15f);
        }

        public void SetProp(string prop, bool on)
        {
            if (!Root) return;
            if (on) _props.Add(prop); else _props.Remove(prop);
            if (!on && prop == _autoProp) return; // the animation still shows it
            PersonProps.Set(Root, prop, on); // baked children by name, else a built prop (jug, basket, cart, backpack...)
        }

        public async Task WalkTo(IReadOnlyList<(double x, double z)> path, double speed = 1.3)
        {
            if (!Root || path == null) return;
            SetAnim(speed > 3 ? "run" : speed > 1.8 ? "brisk" : "walk", speed);
            foreach (var (x, z) in path)
            {
                var target = LevelLayout.ToUnity((float)x, Root.transform.position.y, (float)z);
                while (Root && (Root.transform.position - target).sqrMagnitude > 0.0025f)
                {
                    var to = target - Root.transform.position; to.y = 0;
                    if (to.sqrMagnitude > 0.0001f) Root.transform.rotation = Quaternion.Slerp(Root.transform.rotation, Quaternion.LookRotation(to), Time.deltaTime * 8f);
                    Root.transform.position = Vector3.MoveTowards(Root.transform.position, target, (float)speed * Time.deltaTime);
                    await Task.Yield();
                }
            }
            SetAnim("idle");
        }
    }
}
