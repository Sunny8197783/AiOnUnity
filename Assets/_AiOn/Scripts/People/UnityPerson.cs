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
            if (_anim) { _modelRot = _anim.transform.localRotation; _modelPos = _anim.transform.localPosition; }
        }

        Quaternion _modelRot = Quaternion.identity;
        Vector3 _modelPos;

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

        /// <summary>
        /// "lie" (competition upgrade, the shelter CPR): lying still on the back, head behind the feet, made from the first
        /// frame of the idle clip, held, with the model tipped over (there is no lying clip; "down" is a take-cover crouch).
        /// Any other animation stands it up and lets the clips run again.
        /// </summary>
        public const string Lie = "lie";
        /// <summary>How far the back reaches behind the feet when standing (heels, buttocks, shoulder blades): lifts the tipped body onto the floor.</summary>
        public const float LieLift = 0.12f;

        public void SetAnim(string anim, double? speed = null)
        {
            bool lie = anim == Lie;
            if (_anim)
            {
                var t = _anim.transform;
                if (t != Root.transform)
                {
                    // tipped back about the feet in the root's frame, on top of the model's own half turn
                    t.localRotation = lie ? Quaternion.Euler(-90f, 0f, 0f) * _modelRot : _modelRot;
                    t.localPosition = lie ? _modelPos + new Vector3(0, LieLift, 0) : _modelPos;
                }
                if (lie) anim = "idle";
            }
            if (_anim)
            {
                if (!lie)
                {
                    _anim.Hold(false);
                    var slump = _anim.GetComponent<LieSlump>();
                    if (slump) Object.Destroy(slump);
                }
                _anim.SetAnim(anim, speed.HasValue ? (float)speed.Value : (float?)null);
                var face = _anim.GetComponent<PersonFace>();
                if (face) face.CloseEyes(lie);
                if (lie && !_anim.Held)
                {
                    _anim.Hold(true); // a still body, not a swaying standing idle on its back
                    Slump(_anim.GetComponent<Animator>());
                }
            }
            // people.js AUTO: carry shows the jug, phone the phone, while the animation lasts (unless set on by the story)
            var auto = AutoProp(anim);
            if (auto == _autoProp) return;
            if (_autoProp != null && !_props.Contains(_autoProp)) PersonProps.Set(Root, _autoProp, false);
            _autoProp = auto;
            if (auto != null && Root) PersonProps.Set(Root, auto, true);
        }

        /// <summary>Degrees the head of a lying person rolls to one side (and the neck a third of it): slack, not lying at attention.</summary>
        public const float LieHeadRoll = -22f;

        /// <summary>The held lying pose made slack: the head rolled to one side about the spine (after the animator, each frame).</summary>
        static void Slump(Animator a)
        {
            if (!a || !a.isHuman || a.GetComponent<LieSlump>()) return;
            a.gameObject.AddComponent<LieSlump>().roll = LieHeadRoll;
        }

        static string AutoProp(string anim) => anim switch { "carry" => "waterJug", "phone" => "phone", _ => null };

        readonly HashSet<string> _props = new();
        string _autoProp;
        public void Play(string clip) { if (_anim) _anim.Play(clip); }

        /// <summary>Eye height of the person looked at (m above their floor).</summary>
        public const float EyeHeight = 1.55f;

        public void LookAt(double? x, double? z)
        {
            // Heroes turn the head and eyes (PersonFace, Humanoid look IK) and keep looking until cleared; the body still
            // turns a little toward a close target, never away from it, as before.
            var face = _anim ? _anim.GetComponent<PersonFace>() : null;
            if (!Root || !x.HasValue || !z.HasValue) { if (face) face.LookAt(null); return; }
            var at = LevelLayout.ToUnity((float)x.Value, 0, (float)z.Value);
            at.y = Root.transform.position.y + EyeHeight;
            if (face) face.LookAt(at);
            var to = at - Root.transform.position;
            to.y = 0;
            if (to.sqrMagnitude > 0.01f) Root.transform.rotation = Quaternion.Slerp(Root.transform.rotation, Quaternion.LookRotation(to), 0.15f);
        }

        public void SetProp(string prop, bool on)
        {
            if (!Root) return;
            if (on) _props.Add(prop); else _props.Remove(prop);
            if (prop == "cart" && _anim) _anim.HoldPose = on ? PersonClips.ArmsCart : null; // both hands on the handle
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
