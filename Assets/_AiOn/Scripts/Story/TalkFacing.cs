using System.Collections.Generic;
using AiOn.Player;
using AiOn.UI;
using UnityEngine;

namespace AiOn.Story
{
    /// <summary>
    /// When a dialog opens with a person standing nearby, the player turns to look at their face and they turn toward
    /// the player, so a conversation is framed instead of the camera staring at a wall (an upgrade over the original,
    /// where the view stayed wherever it was). Speakers are matched to the level's NPCs (Npc_&lt;id&gt;) and the people
    /// the story spawned (Spawned_&lt;preset&gt;); signs, phones and objects that 'speak' are left alone.
    /// </summary>
    public class TalkFacing : MonoBehaviour
    {
        static readonly Dictionary<string, string> NpcOf = new()
        {
            ["오서연"] = "seoyeon", ["서연"] = "seoyeon", ["303호"] = "seoyeon",
            ["박영수 소장"] = "manager", ["관리사무소장"] = "manager", ["박 소장"] = "manager",
            ["이수아"] = "clerk", ["수아"] = "clerk",
            ["윤 팀장"] = "coworker",
            ["안내 요원"] = "guide", ["대피소 관리자"] = "guide", ["접수대"] = "guide",
            ["304호 어르신"] = "elder", ["할아버지"] = "elder", ["어르신"] = "elder",
            ["102동 주민"] = "crowd",
        };

        const float Reach = 7f, HeadHeight = 1.55f, TurnSeconds = 0.6f;
        // (Talk shot: see StartShot.)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var go = new GameObject("TalkFacing");
            DontDestroyOnLoad(go);
            go.AddComponent<TalkFacing>();
        }

        Transform _npc;
        Quaternion _npcFrom, _npcTo;
        float _npcT = 1;

        void OnEnable() { StoryOverlay.TalkStarted += OnTalk; StoryOverlay.TalkEnded += OnTalkEnd; }
        void OnDisable() { StoryOverlay.TalkStarted -= OnTalk; StoryOverlay.TalkEnded -= OnTalkEnd; }

        // ---------- the conversation shot ----------
        // Over the player's right shoulder onto the speaker, their face above the dialog box (which covers the lower
        // ~40% of the screen). Eases in; hands the camera back when the dialog closes. Never takes the camera from a
        // cutscene that already holds it.
        const float ShotFov = 42f, EaseIn = 0.5f;
        PlayerCameraRig _rig;
        bool _shot;
        float _shotT;
        Vector3 _fromPos, _fromTarget, _toPos, _toTarget;
        float _fromFov;
        Vector3 _applied;

        void StartShot(PlayerController player, Transform npc)
        {
            _rig = FindFirstObjectByType<PlayerCameraRig>();
            if (!_rig || !_rig.cam || _rig.overrideActive) return;
            var p = player.transform.position; var n = npc.position;
            var dir = n - p; dir.y = 0;
            float d = dir.magnitude;
            if (d < 0.3f) return;
            dir /= d;
            var right = Vector3.Cross(Vector3.up, dir);
            var playerHead = p + Vector3.up * 1.62f;
            var npcHead = n + Vector3.up * HeadHeight;
            var want = playerHead - dir * 0.95f + right * 0.55f + Vector3.up * 0.08f;
            // Not through a wall: pull in along the line from the player's head.
            var off = want - playerHead;
            if (Physics.SphereCast(playerHead, 0.15f, off.normalized, out var hit, off.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(player.transform))
                want = playerHead + off.normalized * Mathf.Max(0.2f, hit.distance - 0.05f);
            // Aim a little below the face so it sits in the upper part of the frame, above the dialog.
            var target = npcHead - Vector3.up * (0.12f + 0.06f * Mathf.Min(d, 4f));
            var camT = _rig.cam.transform;
            _fromPos = camT.position; _fromTarget = camT.position + camT.forward * Mathf.Max(1f, Vector3.Distance(camT.position, npcHead));
            _fromFov = _rig.cam.fieldOfView;
            _toPos = want; _toTarget = target;
            _shotT = 0; _shot = true;
            _rig.overrideActive = true;
            ApplyShot(0);
        }

        void ApplyShot(float k)
        {
            float e = k * k * (3 - 2 * k);
            _rig.overridePosition = _applied = Vector3.Lerp(_fromPos, _toPos, e);
            _rig.overrideTarget = Vector3.Lerp(_fromTarget, _toTarget, e);
            _rig.overrideFov = Mathf.Lerp(_fromFov, ShotFov, e);
        }

        void OnTalkEnd()
        {
            if (!_shot) return;
            _shot = false;
            // Only hand back what is still ours (a cutscene that started meanwhile moved the override elsewhere).
            if (_rig && _rig.overrideActive && (_rig.overridePosition - _applied).sqrMagnitude < 0.0001f) _rig.overrideActive = false;
        }

        void OnTalk(string speaker, string role)
        {
            if (string.IsNullOrEmpty(speaker) || !NpcOf.TryGetValue(speaker.Trim(), out var id)) return;
            if (role != null && (role.Contains("문자") || role.Contains("전화") || role.Contains("문 너머"))) return; // not face to face
            var player = FindFirstObjectByType<PlayerController>();
            if (!player) return;
            var npc = Nearest(id, player.transform.position);
            if (!npc) return;
            player.FaceTowards(npc.position + Vector3.up * HeadHeight);
            var d = player.transform.position - npc.position; d.y = 0;
            if (d.sqrMagnitude < 0.04f) return;
            // a seated person keeps facing their desk (turning the root would swing the body off the chair); the shot still frames them
            var pa = npc.GetComponentInChildren<AiOn.People.PersonAnimator>();
            if (pa && IsSeated(pa.Current)) { StartShot(player, npc); return; }
            _npc = npc; _npcFrom = npc.rotation; _npcTo = Quaternion.LookRotation(d.normalized, Vector3.up); _npcT = 0;
            StartShot(player, npc);
        }

        /// <summary>Seated poses (sit, sitChair) that the talk must not turn.</summary>
        public static bool IsSeated(string anim) => anim != null && anim.StartsWith("sit", System.StringComparison.Ordinal);

        static Transform Nearest(string id, Vector3 from)
        {
            Transform best = null; float bestD = Reach;
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                var n = t.name;
                bool match = id == "crowd"
                    ? n.StartsWith("Npc_crowd") || n.StartsWith("Spawned_crowd")
                    : n == "Npc_" + id || n == "Spawned_" + id;
                if (!match || !t.gameObject.activeInHierarchy) continue;
                float dist = Vector3.Distance(t.position, from);
                if (dist < bestD) { bestD = dist; best = t; }
            }
            return best;
        }

        void Update()
        {
            if (_shot && _rig && _shotT < 1 && (_rig.overridePosition - _applied).sqrMagnitude < 0.0001f)
            {
                _shotT = Mathf.Min(1, _shotT + Time.unscaledDeltaTime / EaseIn);
                ApplyShot(_shotT);
            }
            if (_npcT >= 1 || !_npc) return;
            _npcT = Mathf.Min(1, _npcT + Time.unscaledDeltaTime / TurnSeconds);
            float k = _npcT * _npcT * (3 - 2 * _npcT);
            _npc.rotation = Quaternion.Slerp(_npcFrom, _npcTo, k);
        }
    }
}
