using AiOn.People;
using UnityEngine;

namespace AiOn.Player
{
    /// <summary>
    /// The player's visible body (third person), as engine/controls.js poses it: it faces the controller's BodyYaw (the
    /// way it walks, not the camera), plays idle / the walk-run blend / carry / crouch / air, stairsUp or stairsDown on a
    /// stair flight, the vault or climb clip of a vault, and a landing clip after a drop of more than 0.35 m. Carrying
    /// water shows the jug in hand (and a bag on the back shows the backpack).
    /// </summary>
    public class PlayerBody : MonoBehaviour
    {
        public PlayerController player;
        public PersonAnimator person;
        [Tooltip("What the player carries in hand ('water' plays the carry walk and shows the jug; 'bag' shows the backpack).")]
        public string carrying;

        /// <summary>A stair flight is steeper than this (rise over run).</summary>
        public const float StairSlope = 0.35f;
        /// <summary>A landing clip after falling at least this far (m).</summary>
        public const float LandDrop = 0.35f;

        float _airT, _peakY;
        bool _wasAir;
        string _props;
        PersonAnimator _propsOn;

        void OnEnable() { if (player) player.Acted += Play; }
        void OnDisable() { if (player) player.Acted -= Play; }

        /// <summary>A one-shot body clip over whatever the legs do (punch, throw, pickup, vault, climb, land, hurt).</summary>
        public void Play(string clip)
        {
            if (person && person.isActiveAndEnabled) person.Play(clip);
        }

        /// <summary>The right hand of the body (null without a rigged body).</summary>
        public Transform RightHand
        {
            get
            {
                if (!person) return null;
                if (_handOf != person) { _handOf = person; _hand = PersonRig.Find(person.transform, "rHand"); if (_hand == person.transform) _hand = null; }
                return _hand;
            }
        }
        Transform _hand;
        PersonAnimator _handOf;

        /// <summary>controls.js: on a flight steeper than 0.35 while walking, stairsUp or stairsDown by the way it goes.</summary>
        public static string StairAnim(Vector3 groundNormal, Vector3 planarVelocity, float speed)
        {
            if (speed <= 0.15f || groundNormal.y <= 0.01f) return null;
            var flat = new Vector3(groundNormal.x, 0, groundNormal.z);
            float slope = flat.magnitude / groundNormal.y;
            if (slope <= StairSlope) return null;
            // the normal leans downhill: walking along it is going down
            return Vector3.Dot(planarVelocity, flat) > 0 ? "stairsDown" : "stairsUp";
        }

        void LateUpdate()
        {
            if (!player) return;
            // a scene saved before its body prefab was rebuilt holds a stale link: take the body's animator again
            if (!person) person = GetComponentInChildren<PersonAnimator>(true);
            float dt = Time.deltaTime, y = player.transform.position.y;
            float hs = player.HorizontalSpeed;
            bool active = person && person.isActiveAndEnabled;

            // Landings and footsteps belong to the player, not to the visible body: they run in first person too
            // (V hides the body), as app.js times steps from the stride and controls.js shakes on a landing.
            bool air = !player.Grounded;
            _airT = air ? _airT + dt : 0;
            if (air) { _peakY = _wasAir ? Mathf.Max(_peakY, y) : y; }
            else if (_wasAir && _peakY - y > LandDrop) { Play("land"); Landed(_peakY - y); }
            _wasAir = air;

            string stair = null;
            bool walking = !player.Busy && _airT <= 0.15f && !player.Crouching;
            if (walking && carrying != "water" && hs > 0.15f && Physics.Raycast(player.transform.position + Vector3.up * 0.4f, Vector3.down, out var hit, 0.9f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                stair = StairAnim(hit.normal, player.PlanarVelocity, hs);
            if (walking) Footsteps(dt, hs, stair != null); else _stepAcc = 0;

            if (!person) return;
            // the body turns on its own (the player object turns with the camera)
            var root = person.transform.parent ? person.transform.parent : person.transform;
            if (root != transform) root.rotation = Quaternion.Euler(0, player.BodyYaw, 0);
            SyncProps();
            LiftLook();
            if (!active) return;

            if (player.Busy) person.SetAnim("idle"); // the vault / climb clip owns the body
            else if (_airT > 0.15f) person.SetAnim("air");
            else if (player.Crouching) person.SetAnim("crouch");
            else if (carrying == "water" && hs > 0.15f) person.SetAnim("carry", hs);
            else if (stair != null) person.SetAnim(stair);
            else person.Move(hs);
        }

        // ---------------------------------------------------------------- footsteps and landings (app.js, audio.js)

        /// <summary>A heel strike every this many metres walking / running.</summary>
        public const float WalkStride = 0.7f, RunStride = 0.9f;
        /// <summary>A landing from higher than this shakes and dips the view (controls.js).</summary>
        public const float HardLanding = 1.2f;

        /// <summary>app.js STEP_FLOOR: stairs on a flight, else by level (town and shelter concrete; the apartment, office, mart and store tile).</summary>
        public static string Surface(string levelId, bool stairs) => stairs ? "stairs" : levelId switch
        {
            "apartment" or "office" or "mart" or "store" => "tile",
            _ => "concrete",
        };

        /// <summary>audio.js STEP: base pitch and volume per surface (tile is brighter and lighter, stairs heavier).</summary>
        public static (float pitch, float volume) StepVoice(string surface) => surface switch
        {
            "stairs" => (0.86f, 0.62f),
            "tile" => (1.14f, 0.42f),
            _ => (1f, 0.5f),
        };

        /// <summary>Advances the stride meter by the distance walked; true when a heel strikes (the meter keeps the rest).</summary>
        public static bool StepDue(ref float meter, float metres, bool running)
        {
            meter += metres;
            float stride = running ? RunStride : WalkStride;
            if (meter < stride) return false;
            meter -= stride;
            if (meter > stride) meter = 0; // a teleport or hitch: one step, not a burst
            return true;
        }

        float _stepAcc, _libT = -10f, _lastStep;
        AudioSource _stepSrc;
        AudioClip _stepClip;
        PlayerCameraRig _rig;
        AiOn.Story.UnityStoryHost _host;

        void Footsteps(float dt, float hs, bool stairs)
        {
            if (hs < 0.25f || InputGate.Blocked || player.Busy) { _stepAcc = Mathf.Min(_stepAcc, 0.35f); return; }
            if (!StepDue(ref _stepAcc, hs * dt, player.Running)) return;
            if (Time.time - _lastStep < 0.16f) return; // audio.js: never closer than 0.16 s
            _lastStep = Time.time;
            if (!_stepClip || !_host)
            {
                if (Time.unscaledTime - _libT < 2f) return;
                _libT = Time.unscaledTime;
                _host = FindFirstObjectByType<AiOn.Story.UnityStoryHost>();
                _stepClip = _host && _host.audioLibrary ? _host.audioLibrary.Get("footstep") : null;
                if (!_stepClip) return;
            }
            if (!_stepSrc)
            {
                _stepSrc = gameObject.AddComponent<AudioSource>();
                _stepSrc.playOnAwake = false; _stepSrc.spatialBlend = 0f; _stepSrc.loop = false;
            }
            var (pitch, vol) = StepVoice(Surface(_host ? _host.LevelId : null, stairs));
            _stepSrc.pitch = pitch * Random.Range(0.92f, 1.08f);
            // deaf mode and the master volume act on the listener; the effects bus here
            _stepSrc.PlayOneShot(_stepClip, vol * Random.Range(0.8f, 1f) * (player.Running ? 1.15f : 1f) * AiOn.Settings.AudioBus.Effects);
        }

        void Landed(float drop)
        {
            if (drop <= HardLanding) return;
            if (!_rig) _rig = FindFirstObjectByType<PlayerCameraRig>();
            if (!_rig || _rig.reduceMotion) return;
            _rig.Shake(Mathf.Min(0.6f, drop * 0.08f), 0.4f);
            _rig.Dip(Mathf.Min(0.12f, 0.03f + drop * 0.02f));
        }

        // The player's dark hair and suit read as a black hole against a dim room: their own copy of the body material gets a
        // little sheen (roughness 0.7) so the key and fill pick out the shoulders and hair. (An emissive lift of the blacks
        // made the body glow pale in the blackout, where the whole frame is that dark.)
        static readonly int RoughnessId = Shader.PropertyToID("roughnessFactor"), UrpSmoothId = Shader.PropertyToID("_Smoothness");
        PersonAnimator _lookOn;
        readonly System.Collections.Generic.List<Material> _lookMats = new();

        void LiftLook()
        {
            if (_lookOn == person) return;
            _lookOn = person;
            // the previous body's copies go with it (a preset swap)
            foreach (var old in _lookMats) if (old) Destroy(old);
            _lookMats.Clear();
            var root = person.transform.parent ? person.transform.parent : person.transform;
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (!src || src.name.EndsWith(" (player)")) continue;
                    var m = new Material(src) { name = src.name + " (player)" };
                    if (m.HasProperty(RoughnessId)) m.SetFloat(RoughnessId, Mathf.Min(m.GetFloat(RoughnessId), 0.7f));
                    if (m.HasProperty(UrpSmoothId)) m.SetFloat(UrpSmoothId, Mathf.Max(m.GetFloat(UrpSmoothId), 0.3f));
                    mats[i] = m; changed = true;
                    _lookMats.Add(m);
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        void OnDestroy() { foreach (var m in _lookMats) if (m) Destroy(m); _lookMats.Clear(); }

        // controls.js carrying: the jug in hand while carrying water, the backpack while carrying the bag.
        void SyncProps()
        {
            var want = carrying == "water" || carrying == "bag" ? carrying : null;
            if (want == _props && _propsOn == person) return;
            var root = person.transform.parent ? person.transform.parent.gameObject : person.gameObject;
            PersonProps.Set(root, "waterJug", want == "water");
            PersonProps.Set(root, "backpack", want == "bag");
            _props = want; _propsOn = person;
        }
    }
}
