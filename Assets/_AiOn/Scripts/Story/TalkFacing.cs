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

        void OnEnable() => StoryOverlay.TalkStarted += OnTalk;
        void OnDisable() => StoryOverlay.TalkStarted -= OnTalk;

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
            _npc = npc; _npcFrom = npc.rotation; _npcTo = Quaternion.LookRotation(d.normalized, Vector3.up); _npcT = 0;
        }

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
            if (_npcT >= 1 || !_npc) return;
            _npcT = Mathf.Min(1, _npcT + Time.unscaledDeltaTime / TurnSeconds);
            float k = _npcT * _npcT * (3 - 2 * _npcT);
            _npc.rotation = Quaternion.Slerp(_npcFrom, _npcTo, k);
        }
    }
}
