using UnityEngine;

namespace AiOn.Player
{
    /// <summary>
    /// The player's torch (engine/controls.js flashlight): held at the right hand in third person (at the chest when the
    /// body has no hand bone) and just below and ahead of the eyes in first person, aimed along the gaze about 10° low with a
    /// little hand-held lag. A wide soft edge (inner 30°, outer 60°) instead of a hard disc; the beam dims when it meets a wall or a person
    /// close up (the original's flashK), so a wall at arm's length is not burnt white. While it is on the player's own body
    /// casts no shadow (its silhouette filled the view).
    /// </summary>
    [DefaultExecutionOrder(100)] // after the camera rig has placed the camera this frame
    public class PlayerFlashlight : MonoBehaviour
    {
        public PlayerController player;
        public PlayerCameraRig rig;
        public PlayerBody body;
        [Tooltip("Spot intensity with nothing close in front.")]
        public float intensity = 6f;
        public float range = 18f;

        /// <summary>The beam drops about 10° below the gaze (controls.js FLASH_DROP).</summary>
        public const float DropDegrees = 10f;
        /// <summary>The level never falls below this share of the full beam (controls.js 0.12).</summary>
        public const float MinLevel = 0.12f;

        /// <summary>Outdoors the beam lands on the road this far ahead (m), a little brighter than indoors.</summary>
        public const float GroundReach = 7f, OutdoorBoost = 1.4f;

        /// <summary>The point on the ground (at the feet' height) reach metres ahead along the flat gaze.</summary>
        public static Vector3 GroundAim(Vector3 feet, Vector3 gaze, float reach)
        {
            gaze.y = 0;
            gaze = gaze.sqrMagnitude > 1e-6f ? gaze.normalized : Vector3.forward;
            return feet + gaze * reach;
        }

        public Light Light { get; private set; }
        public bool On { get; private set; }

        Vector3 _aim;
        bool _snap = true;
        float _k = 1f;

        public static PlayerFlashlight Create(PlayerController player, PlayerCameraRig rig, bool shadows)
        {
            var go = new GameObject("Flashlight");
            go.transform.SetParent(player.transform, false);
            var f = go.AddComponent<PlayerFlashlight>();
            f.player = player; f.rig = rig; f.body = player.GetComponent<PlayerBody>();
            var l = f.Light = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.spotAngle = 60f; l.innerSpotAngle = 30f;
            l.range = f.range; l.intensity = f.intensity; l.color = new Color(1f, 0.945f, 0.863f); // 0xfff1dc
            // (A generated light cookie turned the whole URP frame cyan here; the inner/outer cone gives the soft edge.)
            l.shadowNearPlane = 0.25f;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            l.enabled = false;
            return f;
        }

        public void SetOn(bool on)
        {
            if (on == On) return;
            On = on;
            if (Light) Light.enabled = on;
            if (on) _snap = true;
            if (rig) rig.bodyCastsShadow = !on;
        }

        public bool Shadows { set { if (Light) Light.shadows = value ? LightShadows.Soft : LightShadows.None; } }

        /// <summary>The beam's level for the distance to whatever it hits: smoothstep(0.4, 2.5, d), never under MinLevel.</summary>
        public static float NearDim(float hitDistance)
        {
            float t = Mathf.Clamp01((hitDistance - 0.4f) / (2.5f - 0.4f));
            return Mathf.Max(MinLevel, t * t * (3f - 2f * t));
        }

        void LateUpdate()
        {
            if (!On || !Light || !player || !rig || !rig.cam) return;
            var cam = rig.cam.transform;
            Vector3 fwd = cam.forward, camPos = cam.position;
            Vector3 origin;
            var hand = rig.thirdPerson && body ? body.RightHand : null;
            if (hand && hand.gameObject.activeInHierarchy) origin = hand.position + fwd * 0.08f;
            else if (rig.thirdPerson)
                origin = player.transform.position + Vector3.up * 1.3f + Quaternion.Euler(0, player.BodyYaw, 0) * new Vector3(0.12f, 0, 0.25f);
            else origin = camPos + cam.right * 0.16f - cam.up * 0.2f + fwd * 0.32f; // ahead of the own body, which would shadow it
            var want = camPos + fwd * 16f + Vector3.down * (16f * Mathf.Tan(DropDegrees * Mathf.Deg2Rad));
            // Outdoors a beam along the gaze only meets the road far off with nothing left of it: unless the player looks
            // up or something stands closer, it is aimed at the ground GroundReach ahead, a pool on the street.
            var env = AiOn.Levels.EnvironmentController.Instance;
            bool outdoor = env != null && env.Current != null && env.Current.Outdoor;
            if (outdoor && fwd.y < 0.15f)
            {
                var toWant = want - camPos;
                if (!Physics.Raycast(camPos, toWant.normalized, GroundReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    want = GroundAim(player.transform.position, fwd, GroundReach);
            }
            float dt = Time.deltaTime;
            bool rm = rig.reduceMotion;
            if (_snap) { _aim = want; _snap = false; }
            else _aim = Vector3.Lerp(_aim, want, 1f - Mathf.Exp(-(rm ? 30f : 14f) * dt));
            var dir = _aim - origin;
            if (dir.sqrMagnitude < 1e-4f) dir = fwd;
            dir.Normalize();
            transform.SetPositionAndRotation(origin, Quaternion.LookRotation(dir, Vector3.up));

            // dim with the distance to whatever the beam meets first (not the player's own capsule: rays start inside it)
            float hitD = range;
            if (Physics.Raycast(origin, dir, out var hit, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(player.transform))
                hitD = hit.distance;
            _k += (NearDim(hitD) - _k) * (1f - Mathf.Exp(-8f * dt));
            Light.intensity = intensity * _k * (outdoor ? OutdoorBoost : 1f);
        }

        void OnDisable() { if (rig) rig.bodyCastsShadow = true; }
    }
}
