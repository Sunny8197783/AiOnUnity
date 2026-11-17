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
                if (_handOf != person) { _handOf = person; _hand = FindDeep(person.transform, "rHand"); }
                return _hand;
            }
        }
        Transform _hand;
        PersonAnimator _handOf;

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var f = FindDeep(t.GetChild(i), name); if (f) return f; }
            return null;
        }

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
            if (!player || !person) return;
            // the body turns on its own (the player object turns with the camera)
            var root = person.transform.parent ? person.transform.parent : person.transform;
            if (root != transform) root.rotation = Quaternion.Euler(0, player.BodyYaw, 0);
            SyncProps();
            if (!person.isActiveAndEnabled) { _wasAir = false; _airT = 0; return; }

            float dt = Time.deltaTime, y = player.transform.position.y;
            bool air = !player.Grounded;
            _airT = air ? _airT + dt : 0;
            if (air) { _peakY = _wasAir ? Mathf.Max(_peakY, y) : y; }
            else if (_wasAir && _peakY - y > LandDrop) Play("land");
            _wasAir = air;

            float hs = player.HorizontalSpeed;
            if (player.Busy) person.SetAnim("idle"); // the vault / climb clip owns the body
            else if (_airT > 0.15f) person.SetAnim("air");
            else if (player.Crouching) person.SetAnim("crouch");
            else if (carrying == "water" && hs > 0.15f) person.SetAnim("carry", hs);
            else
            {
                string stair = null;
                if (carrying != "water" && hs > 0.15f && Physics.Raycast(player.transform.position + Vector3.up * 0.4f, Vector3.down, out var hit, 0.9f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    stair = StairAnim(hit.normal, player.PlanarVelocity, hs);
                if (stair != null) person.SetAnim(stair);
                else person.Move(hs);
            }
        }

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
