using System.Collections.Generic;
using AiOn.Interaction;
using AiOn.People;
using AiOn.Player;
using AiOn.UI;
using UnityEngine;

namespace AiOn.Story
{
    /// <summary>
    /// The pure maths of the talk shot (unit-tested): the over-the-shoulder candidates in the order they are tried, the aim
    /// that puts the speaker's eyes about 30 % from the top of the frame (above the dialog's bottom stack), and how much of
    /// the frame the player's own body would cover.
    /// </summary>
    public static class TalkFraming
    {
        public const float Back = 1.6f, Side = 0.8f, Up = 0.15f, Fov = 36f;
        /// <summary>Where the speaker's eyes sit, from the top of the frame (0..1).</summary>
        public const float EyesFromTop = 0.30f;
        /// <summary>The player stands at least this far from the speaker for the shot (m).</summary>
        public const float MinGap = 1.1f;
        /// <summary>Above this share of the frame the player's body is hidden for the talk (it keeps its shadow).</summary>
        public const float MaxBodyCover = 0.2f;

        /// <summary>Right shoulder, left shoulder, then shorter booms on either side.</summary>
        public static readonly (float side, float scale)[] Order = { (1f, 1f), (-1f, 1f), (1f, 0.65f), (-1f, 0.65f), (1f, 0.4f), (-1f, 0.4f) };

        /// <summary>A camera position over a shoulder of the player (head at playerHead) looking toward dir (flat, unit).</summary>
        public static Vector3 Candidate(Vector3 playerHead, Vector3 dir, float side, float scale)
        {
            dir.y = 0;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, dir);
            return playerHead - dir * (Back * scale) + right * (Side * side * Mathf.Lerp(0.6f, 1f, scale)) + Vector3.up * Up;
        }

        /// <summary>
        /// A point to look at from cam so that 'point' lands fromTop of the way down the frame (vertical FOV vfov degrees),
        /// centred across.
        /// </summary>
        public static Vector3 AimAt(Vector3 cam, Vector3 point, float vfov, float fromTop)
        {
            var d = point - cam;
            float len = d.magnitude;
            if (len < 1e-4f) return point;
            d /= len;
            float ndc = 1f - 2f * Mathf.Clamp01(fromTop); // +1 top .. -1 bottom
            float a = Mathf.Atan(ndc * Mathf.Tan(vfov * 0.5f * Mathf.Deg2Rad)) * Mathf.Rad2Deg;
            var right = Vector3.Cross(Vector3.up, d);
            if (right.sqrMagnitude < 1e-6f) return point;
            return cam + Quaternion.AngleAxis(a, right.normalized) * d * len;
        }

        /// <summary>Screen height share (0 top .. 1 bottom) at which a point shows for a camera at cam looking at target.</summary>
        public static float ScreenFromTop(Vector3 cam, Vector3 target, Vector3 point, float vfov)
        {
            var rot = Quaternion.LookRotation(target - cam, Vector3.up);
            var local = Quaternion.Inverse(rot) * (point - cam);
            if (local.z <= 1e-4f) return local.y > 0 ? 0f : 1f;
            float ndc = local.y / local.z / Mathf.Tan(vfov * 0.5f * Mathf.Deg2Rad);
            return (1f - ndc) * 0.5f;
        }

        /// <summary>Lines from someone who is not standing there: texts, calls, voices through a door, broadcasts and radios.</summary>
        public static bool NotFaceToFace(string role) => role != null &&
            (role.Contains("문자") || role.Contains("전화") || role.Contains("문 너머") || role.Contains("방송") || role.Contains("무전") || role.Contains("라디오") || role.Contains("스피커"));

        /// <summary>Distances out from a notice board (along its normal) tried for the reading shot, nearest-to-ideal first.</summary>
        public static readonly float[] BoardDistances = { 1.8f, 1.5f, 2.1f, 1.2f };
        public const float BoardFov = 44f, BoardFromTop = 0.42f;

        /// <summary>A camera for reading a board at 'at' (facing out along 'normal'): out along the normal at eye height.</summary>
        public static Vector3 BoardCamera(Vector3 at, Vector3 normal, float eyeY, float dist)
        {
            normal.y = 0;
            normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.forward;
            var c = at + normal * dist;
            c.y = eyeY;
            return c;
        }

        static readonly Vector3[] Corners = new Vector3[8];

        /// <summary>
        /// The share of the frame (0..1) the player's upper body (shoulders and head: a 0.6 wide box from 0.85 to 1.8 m at
        /// feet, facing yaw; the legs sit behind the dialog) covers for a
        /// camera at cam with rotation rot. A body reaching behind the lens counts as filling the frame.
        /// </summary>
        public static float BodyCover(Vector3 cam, Quaternion rot, float vfov, float aspect, Vector3 feet, float yaw)
        {
            var inv = Quaternion.Inverse(rot);
            var body = Quaternion.Euler(0, yaw, 0);
            int i = 0;
            for (int x = -1; x <= 1; x += 2) for (int y = 0; y <= 1; y++) for (int z = -1; z <= 1; z += 2)
                Corners[i++] = feet + body * new Vector3(0.3f * x, 0.85f + 0.95f * y, 0.15f * z);
            float ty = Mathf.Tan(vfov * 0.5f * Mathf.Deg2Rad), tx = ty * aspect;
            float x0 = 1, x1 = -1, y0 = 1, y1 = -1;
            int behind = 0;
            for (int k = 0; k < 8; k++)
            {
                var l = inv * (Corners[k] - cam);
                if (l.z < 0.05f) { behind++; continue; }
                float nx = l.x / l.z / tx, ny = l.y / l.z / ty;
                x0 = Mathf.Min(x0, nx); x1 = Mathf.Max(x1, nx); y0 = Mathf.Min(y0, ny); y1 = Mathf.Max(y1, ny);
            }
            if (behind == 8) return 0f;
            if (behind > 0) return 1f;
            float w = Mathf.Max(0, Mathf.Min(1, x1) - Mathf.Max(-1, x0)), h = Mathf.Max(0, Mathf.Min(1, y1) - Mathf.Max(-1, y0));
            return w * h / 4f;
        }
    }

    /// <summary>
    /// When a dialog opens with a person standing nearby, the player turns to look at their face and they turn toward
    /// the player, so a conversation is framed instead of the camera staring at a wall (an upgrade over the original,
    /// where the view stayed wherever it was). Speakers are matched to the level's NPCs (Npc_&lt;id&gt;), the people the
    /// story spawned (Spawned_&lt;preset&gt;) and the danger layer's looter ('낯선 사람'); scripted speakers with no body
    /// in the level get a stand-in for the talk. A talk about a thing (a fire in the corridor, a notice board, the
    /// player's own '판단') turns the player to that thing instead. Phones, texts and voices through a door are left alone.
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

        /// <summary>Scripted speakers who may have no body in the level: the preset of the stand-in spawned for the talk.</summary>
        static readonly Dictionary<string, string> StandIn = new()
        {
            ["102동 주민"] = "crowd2",
            ["박영수 소장"] = "manager",
        };

        const float Reach = 7f, TurnSeconds = 0.6f;

        static TalkFacing _installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            // '메인 메뉴로' (TitleReturn) destroys every persistent root, this one too: put it back on the next scene load,
            // or every talk after the first return to the title lost its shot, turn and lip movement.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= Reinstall;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += Reinstall;
            if (_installed) return;
            var go = new GameObject("TalkFacing");
            DontDestroyOnLoad(go);
            _installed = go.AddComponent<TalkFacing>();
        }

        static void Reinstall(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (!_installed && Application.isPlaying) Install();
        }

        Transform _npc;
        Quaternion _npcFrom, _npcTo;
        float _npcT = 1;

        void OnEnable() { StoryOverlay.TalkStarted += OnTalk; StoryOverlay.TalkEnded += OnTalkEnd; StoryOverlay.TalkLineShown += OnLine; }
        void OnDisable() { StoryOverlay.TalkStarted -= OnTalk; StoryOverlay.TalkEnded -= OnTalkEnd; StoryOverlay.TalkLineShown -= OnLine; }

        // ---------- the conversation shot ----------
        // Over the player's shoulder onto the speaker, their eyes about 30 % from the top, above the dialog. Eases in;
        // hands the camera back (the rig eases out) shortly after the dialog closes, unless another line follows.
        // Never takes the camera from a cutscene that already holds it.
        const float EaseIn = 0.5f, HandBackDelay = 0.15f;
        PlayerCameraRig _rig;
        bool _shot;
        float _shotT, _endAt = -1;
        Vector3 _fromPos, _fromTarget, _toPos, _toTarget;
        float _fromFov;
        Vector3 _applied;
        string _speaker;
        IStoryPerson _temp;
        string _tempFor;
        float _tempEndAt = -1;
        static readonly RaycastHit[] Hits = new RaycastHit[16];

        bool OursNow => _rig && _rig.overrideActive && (_rig.overridePosition - _applied).sqrMagnitude < 0.0001f;

        void BeginShot(Vector3 camPos, Vector3 target, float fov)
        {
            var camT = _rig.cam.transform;
            if (_shot && OursNow)
            {
                // the next line of the same talk: ease from the current shot
                _fromPos = _rig.overridePosition; _fromTarget = _rig.overrideTarget; _fromFov = _rig.overrideFov;
            }
            else
            {
                _fromPos = camT.position; _fromTarget = camT.position + camT.forward * Mathf.Max(1f, Vector3.Distance(camT.position, target));
                _fromFov = _rig.cam.fieldOfView;
            }
            _toPos = camPos; _toTarget = target; _toFov = fov;
            _shotT = 0; _shot = true; _endAt = -1;
            _rig.overrideActive = true;
            ApplyShot(0);
        }

        float _toFov = TalkFraming.Fov;

        void ApplyShot(float k)
        {
            float e = k * k * (3 - 2 * k);
            _rig.overridePosition = _applied = Vector3.Lerp(_fromPos, _toPos, e);
            _rig.overrideTarget = Vector3.Lerp(_fromTarget, _toTarget, e);
            _rig.overrideFov = Mathf.Lerp(_fromFov, _toFov, e);
        }

        bool Blocked(Vector3 from, Vector3 to, Transform a, Transform b)
        {
            var d = to - from;
            float len = d.magnitude;
            if (len < 1e-3f) return false;
            int n = Physics.SphereCastNonAlloc(from, 0.08f, d / len, Hits, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var t = Hits[i].collider.transform;
                if ((a && t.IsChildOf(a)) || (b && t.IsChildOf(b))) continue;
                if (Hits[i].distance <= 0f) continue; // started inside (the player's capsule at the shoulder)
                return true;
            }
            return false;
        }

        void StartShot(PlayerController player, Transform npc, Vector3 eyes)
        {
            if (!RigFree()) return;
            var p = player.transform.position;
            var dir = eyes - p; dir.y = 0;
            float d = dir.magnitude;
            if (d < 0.3f) return;
            dir /= d;
            // a little room between the two, if there is room behind the player: eased over the shot's ease-in, not a jump
            _pushLeft = 0;
            if (d < TalkFraming.MinGap && player.TryGetComponent<CharacterController>(out var cc) && cc.enabled)
            {
                float push = TalkFraming.MinGap - d;
                if (Physics.SphereCast(p + Vector3.up * (cc.radius + 0.3f), cc.radius * 0.9f, -dir, out var back, push + 0.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && !back.collider.transform.IsChildOf(player.transform))
                    push = Mathf.Max(0, back.distance - 0.05f);
                if (push > 0.01f) { _pushCc = cc; _pushDir = -dir; _pushLeft = push; p += -dir * push; }
            }
            var playerHead = p + Vector3.up * Mathf.Min(1.62f, player.EyeHeight);
            Vector3 cam = Vector3.zero; bool found = false;
            // the first clean candidate with room around the lens; a clean but cramped one only if none has room
            Vector3 cramped = Vector3.zero; bool anyCramped = false;
            foreach (var (side, scale) in TalkFraming.Order)
            {
                var c = TalkFraming.Candidate(playerHead, dir, side, scale);
                if (Blocked(playerHead, c, player.transform, npc) || Blocked(c, eyes, player.transform, npc)) continue;
                if (!Roomy(c, player.transform, npc)) { if (!anyCramped) { cramped = c; anyCramped = true; } continue; }
                cam = c; found = true; break;
            }
            if (!found && anyCramped) { cam = cramped; found = true; }
            if (!found)
            {
                // nowhere clean: the shortest right-shoulder boom, pulled in along its line
                var c = TalkFraming.Candidate(playerHead, dir, 1f, 0.4f);
                var off = c - playerHead;
                if (Physics.SphereCast(playerHead, 0.15f, off.normalized, out var hit, off.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && !hit.collider.transform.IsChildOf(player.transform))
                    c = playerHead + off.normalized * Mathf.Max(0.2f, hit.distance - 0.05f);
                cam = c;
            }
            var target = TalkFraming.AimAt(cam, eyes, TalkFraming.Fov, TalkFraming.EyesFromTop);
            float cover = TalkFraming.BodyCover(cam, Quaternion.LookRotation(target - cam, Vector3.up), TalkFraming.Fov, _rig.cam.aspect, p, player.BodyYaw);
            _rig.forceBodyHidden = cover > TalkFraming.MaxBodyCover;
            // indoors a faint catch light in front of the speaker, so a face under a ceiling lamp is not a dark blank
            var env = AiOn.Levels.EnvironmentController.Instance;
            _lightOn = env != null && env.Current != null && !env.Current.Outdoor;
            if (_lightOn)
            {
                var toCam = cam - eyes; toCam.y = 0;
                _lightAt = eyes + (toCam.sqrMagnitude > 1e-4f ? toCam.normalized : Vector3.forward) * 0.9f + Vector3.up * 0.25f;
            }
            BeginShot(cam, target, TalkFraming.Fov);
        }

        // ---- the push back and the catch light ----
        CharacterController _pushCc;
        Vector3 _pushDir;
        float _pushLeft;
        bool _lightOn;
        Vector3 _lightAt;
        Light _catch;
        const float CatchIntensity = 0.8f;

        void UpdateCatchLight(float dt)
        {
            float want = _shot && _lightOn && _endAt < 0 && OursNow ? CatchIntensity : 0f;
            if (!_catch)
            {
                if (want <= 0) return;
                var go = new GameObject("TalkCatchLight");
                go.transform.SetParent(transform, false);
                _catch = go.AddComponent<Light>();
                _catch.type = LightType.Point; _catch.range = 1.6f; _catch.color = new Color(1f, 0.93f, 0.84f);
                _catch.shadows = LightShadows.None; _catch.intensity = 0;
            }
            if (want > 0) _catch.transform.position = _lightAt;
            _catch.intensity = Mathf.MoveTowards(_catch.intensity, want, dt * CatchIntensity / 0.4f);
            _catch.enabled = _catch.intensity > 0.001f;
        }

        bool RigFree()
        {
            if (!_rig) _rig = FindFirstObjectByType<PlayerCameraRig>();
            if (!_rig || !_rig.cam) return false;
            return !_rig.overrideActive || (_shot && OursNow); // a cutscene's camera stays the cutscene's
        }

        /// <summary>The speaker's face while their line shows: the mouth moves (at most this long) and they look at 정민.</summary>
        public const float SpeakSeconds = 4.5f;
        PersonFace _talkFace;

        /// <summary>
        /// How long the mouth moves for one line (page) of a dialog: about the time it takes to say it (Korean speech runs
        /// near 7 syllables a second; spaces and punctuation are not spoken), 1 to 7 s (pure; tested).
        /// </summary>
        public static float SpeakSecondsFor(string line)
        {
            if (string.IsNullOrEmpty(line)) return 0f;
            int spoken = 0;
            foreach (var ch in line) if (char.IsLetterOrDigit(ch)) spoken++;
            if (spoken == 0) return 0f;
            return Mathf.Clamp(0.5f + spoken / 7f, 1f, 7f);
        }

        /// <summary>Each new page of the same speaker's dialog moves the mouth again, for that page's length.</summary>
        void OnLine(string line)
        {
            if (_talkFace) _talkFace.Speak(SpeakSecondsFor(line));
        }

        void FaceSpeaker(Transform npc, PlayerController player)
        {
            var face = npc ? npc.GetComponentInChildren<PersonFace>() : null;
            if (_talkFace && _talkFace != face) { _talkFace.Speak(0); _talkFace.LookAt(null); }
            _talkFace = face;
            if (!face) return;
            face.Speak(SpeakSeconds);
            if (player) face.LookAt(player.transform.position + Vector3.up * 1.6f);
        }

        void OnTalkEnd()
        {
            if (_talkFace) { _talkFace.Speak(0); _talkFace.LookAt(null); _talkFace = null; }
            if (_shot) _endAt = Time.unscaledTime + HandBackDelay;
            if (_temp != null && _tempState != Leaving) _tempEndAt = Time.unscaledTime + StandInGrace;
        }

        void HandBack()
        {
            _shot = false; _endAt = -1;
            // Only hand back what is still ours (a cutscene that started meanwhile moved the override elsewhere).
            if (OursNow) _rig.overrideActive = false; // the rig eases out of the shot
            if (_rig) _rig.forceBodyHidden = false;
        }

        void RemoveStandIn()
        {
            _tempEndAt = -1; _tempState = Idle;
            if (_npc && _temp is UnityPerson ut && ut.Root && _npc == ut.Root.transform) _npc = null;
            var t = _temp; _temp = null; _tempFor = null;
            if (t == null) return;
            if (t is UnityPerson up && up.Root) { up.Root.SetActive(false); up.Root.transform.SetParent(null, false); }
            var host = FindFirstObjectByType<UnityStoryHost>();
            if (host) host.RemovePerson(t);
        }

        void OnTalk(string speaker, string role)
        {
            speaker = speaker?.Trim();
            if (string.IsNullOrEmpty(speaker)) return;
            // any line keeps a stand-in for now (a broadcast or a third voice between two of their lines must not make them pop out)
            _tempEndAt = -1;
            if (TalkFraming.NotFaceToFace(role)) return; // not face to face
            var player = FindFirstObjectByType<PlayerController>();
            if (!player) return;
            _speaker = speaker;

            // ---- a talk about a thing: the fire, a notice board, the player's own judgement ----
            var point = CurrentPoint(player);
            bool thing = point && (point.kind == "fire" || point.kind == "notice");
            if (thing || speaker == "복도" || speaker == "판단")
            {
                if (point && Vector3.Distance(point.Position, player.transform.position) < 6f)
                {
                    // the player turns (and looks up or down) to the thing; the shoulder camera follows the gaze, which
                    // puts it a little above the middle of the frame, beside the head. (A separate object shot could not
                    // know which way a board faces and often framed the wall beside it.)
                    var at = SubjectOf(point.Position, point.kind, player.transform.position.y);
                    if (point.kind == "notice" && BoardNear(at, player.transform, out var board, out var normal))
                    {
                        player.FaceTowards(board);
                        BoardShot(player, board, normal); // a reading shot square to the board, if there is a clean spot
                    }
                    else player.FaceTowards(at);
                }
                return;
            }

            // ---- a person ----
            Transform npc = null;
            bool standIn = false;
            if (speaker == "낯선 사람")
            {
                var hands = FindFirstObjectByType<PlayerHands>();
                if (hands && hands.dangerFx?.Layer?.CurrentLooter is UnityPerson lp && lp.Root) npc = lp.Root.transform;
            }
            else if (NpcOf.TryGetValue(speaker, out var id))
            {
                npc = Nearest(id, player.transform.position, Reach);
                if (!npc && _temp is UnityPerson tq && tq.Root && _tempFor == speaker
                    && (_tempState != Leaving || (_tempHome - player.transform.position).sqrMagnitude < 9f))
                {
                    // the same stand-in again (one already walking off turns back to their spot)
                    npc = tq.Root.transform; standIn = true;
                    if (_tempState == Leaving) { _tempState = Entering; _tempGoal = _tempHome; _temp.SetAnim("walk", WalkSpeed); }
                }
                if (!npc && StandIn.TryGetValue(speaker, out var preset) && !Nearest(id, player.transform.position, id == "crowd" ? Reach : 500f))
                {
                    if (_temp != null) DismissStandIn(true);
                    npc = SpawnStandIn(player, preset, speaker);
                    standIn = npc;
                }
            }
            if (!npc) return;
            // another person takes over: the stand-in walks off
            if (!standIn && _temp != null) DismissStandIn(false);

            FaceSpeaker(npc, player);
            var eyes = standIn ? _tempHome + _tempEyes : EyesOf(npc);
            if (standIn)
            {
                player.FaceTowards(eyes);
                StartShot(player, npc, eyes);
                return;
            }
            player.FaceTowards(eyes);
            var d = player.transform.position - npc.position; d.y = 0;
            if (d.sqrMagnitude < 0.04f) return;
            // a seated person keeps facing their desk (turning the root would swing the body off the chair); the shot still frames them
            var pa = npc.GetComponentInChildren<PersonAnimator>();
            if (!(pa && IsSeated(pa.Current))) { _npc = npc; _npcFrom = npc.rotation; _npcTo = Quaternion.LookRotation(d.normalized, Vector3.up); _npcT = 0; }
            StartShot(player, npc, eyes);
        }

        /// <summary>
        /// What to frame for a point the talk is about: the points sit on the floor, so a notice board is framed at eye
        /// level (1.45 m) and a fire at its flames (0.7 m) above the floor the player stands on; a point already up high stays.
        /// </summary>
        public static Vector3 SubjectOf(Vector3 point, string kind, float floorY)
        {
            float up = kind == "notice" ? 1.45f : kind == "fire" ? 0.7f : 1f;
            if (point.y - floorY > 0.6f) return point;
            return new Vector3(point.x, floorY + up, point.z);
        }

        /// <summary>A notice point stands in front of its board: the nearest wall within 1.5 m at that height is the board.</summary>
        static bool BoardNear(Vector3 at, Transform player, out Vector3 board, out Vector3 normal)
        {
            float best = 1.5f; board = at; normal = Vector3.zero;
            bool found = false;
            for (int i = 0; i < 8; i++)
            {
                var d = Quaternion.Euler(0, i * 45f, 0) * Vector3.forward;
                if (Physics.Raycast(at, d, out var h, best, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && !h.collider.transform.IsChildOf(player) && Mathf.Abs(h.normal.y) < 0.3f)
                { best = h.distance; board = h.point + h.normal * 0.05f; normal = h.normal; found = true; }
            }
            return found;
        }

        /// <summary>
        /// The reading shot for a notice board: square to it, out along its normal at eye height, the board in the upper
        /// part of the frame (above the dialog). Only from a spot with a clear line and some room around the lens; else
        /// the player just turns to it.
        /// </summary>
        void BoardShot(PlayerController player, Vector3 board, Vector3 normal)
        {
            if (!RigFree()) return;
            var p = player.transform.position;
            float eyeY = p.y + Mathf.Min(1.62f, player.EyeHeight);
            foreach (var dist in TalkFraming.BoardDistances)
            {
                var c = TalkFraming.BoardCamera(board, normal, eyeY, dist);
                if (Blocked(c, board - (board - c).normalized * 0.1f, player.transform, null) || !Roomy(c, player.transform, null)) continue;
                var target = TalkFraming.AimAt(c, board, TalkFraming.BoardFov, TalkFraming.BoardFromTop);
                float cover = TalkFraming.BodyCover(c, Quaternion.LookRotation(target - c, Vector3.up), TalkFraming.BoardFov, _rig.cam.aspect, p, player.BodyYaw);
                _rig.forceBodyHidden = cover > TalkFraming.MaxBodyCover;
                _lightOn = false;
                BeginShot(c, target, TalkFraming.BoardFov);
                return;
            }
        }

        static readonly Collider[] Near = new Collider[8];

        /// <summary>No wall, shelf or frame within 0.35 m of the lens (other than the two people).</summary>
        static bool Roomy(Vector3 c, Transform a, Transform b)
        {
            int n = Physics.OverlapSphereNonAlloc(c, 0.35f, Near, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var t = Near[i].transform;
                if ((a && t.IsChildOf(a)) || (b && t.IsChildOf(b))) continue;
                return false;
            }
            return true;
        }

        static InteractionPoint CurrentPoint(PlayerController player)
        {
            var it = player.GetComponent<PlayerInteractor>();
            return it ? it.Current : null;
        }

        // ---- stand-ins: a scripted speaker with no body in the level walks in for the talk and walks off after ----
        const int Idle = 0, Entering = 1, Leaving = 2;
        /// <summary>A stand-in stays this long after the last line (a broadcast or another voice may come between theirs).</summary>
        const float StandInGrace = 4f, WalkSpeed = 1.4f, EntryDistance = 2.6f, LeaveTimeout = 6f;
        int _tempState;
        Vector3 _tempHome, _tempEyes, _tempGoal;
        float _tempLeaveT;
        static readonly Plane[] Frustum = new Plane[6];
        static readonly float[] StandInSides = { 0f, 0.6f, -0.6f }, StandInDistances = { 2f, 1.6f };

        /// <summary>
        /// A stand-in for a speaker the level has no body for: their spot is 2 m in front of the player, facing them, but
        /// they come in from the side (out of the frame where possible) and turn to the player on arrival.
        /// </summary>
        Transform SpawnStandIn(PlayerController player, string preset, string speaker)
        {
            var host = FindFirstObjectByType<UnityStoryHost>();
            if (!host) return null;
            if (!_rig) _rig = FindFirstObjectByType<PlayerCameraRig>();
            var p = player.transform.position;
            var fwd = Quaternion.Euler(0, player.Yaw, 0) * Vector3.forward;
            Vector3 at = p + fwd * 2f;
            // not inside a shelf or a wall: try a little to either side, then closer
            bool ok = false;
            foreach (var dist in StandInDistances)
            {
                foreach (var sd in StandInSides)
                {
                    var c = p + fwd * dist + Vector3.Cross(Vector3.up, fwd) * sd;
                    if (Physics.CheckCapsule(c + Vector3.up * 0.35f, c + Vector3.up * 1.5f, 0.25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    if (Blocked(p + Vector3.up * 1.2f, c + Vector3.up * 1.2f, player.transform, null)) continue; // not beyond a door or a wall
                    at = c; ok = true; break;
                }
                if (ok) break;
            }
            if (!ok) return null;
            at = Ground(at);
            var start = WayOut(at, p, out bool walk);
            var face = walk ? at - start : p - at; face.y = 0;
            float unityYaw = face.sqrMagnitude > 1e-4f ? Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg : 0f;
            var from = walk ? start : at;
            var person = host.SpawnPerson(preset, from.x, from.y, -from.z, -unityYaw * Mathf.Deg2Rad);
            if (person is not UnityPerson up || !up.Root) return null;
            up.Root.name = "TalkStandIn_" + preset;
            _temp = person; _tempFor = speaker; _tempEndAt = -1;
            _tempHome = at;
            _tempEyes = EyesOf(up.Root.transform) - up.Root.transform.position;
            if (walk) { _tempState = Entering; _tempGoal = at; person.SetAnim("walk", WalkSpeed); }
            else { _tempState = Idle; person.SetAnim("idle"); }
            return up.Root.transform;
        }

        static Vector3 Ground(Vector3 at)
        {
            if (Physics.Raycast(at + Vector3.up * 1.2f, Vector3.down, out var g, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) at.y = g.point.y;
            return at;
        }

        /// <summary>
        /// A spot about 2.6 m off to the side of 'home' (or off and further away from the player) that a person can walk
        /// to in a straight clear line, out of the camera's view where possible. walk is false when there is none.
        /// </summary>
        Vector3 WayOut(Vector3 home, Vector3 player, out bool walk)
        {
            var away = home - player; away.y = 0;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            var side = Vector3.Cross(Vector3.up, away);
            var cam = _rig && _rig.cam ? _rig.cam : Camera.main;
            if (cam) GeometryUtility.CalculateFrustumPlanes(cam, Frustum);
            Vector3 best = home; int bestScore = -1;
            for (int i = 0; i < 4; i++)
            {
                var dirv = i == 0 ? side : i == 1 ? -side : i == 2 ? (side + away).normalized : (away - side).normalized;
                var c = home + dirv * EntryDistance;
                if (Physics.CheckCapsule(c + Vector3.up * 0.35f, c + Vector3.up * 1.5f, 0.25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.CapsuleCast(home + Vector3.up * 0.35f, home + Vector3.up * 1.5f, 0.22f, dirv, EntryDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                c = Ground(c);
                if (Mathf.Abs(c.y - home.y) > 0.3f) continue; // no stairs or drops on the way
                int score = cam && Seen(c) ? 0 : 1;
                if (score > bestScore) { bestScore = score; best = c; }
            }
            walk = bestScore >= 0;
            return best;
        }

        static bool Seen(Vector3 feet) => GeometryUtility.TestPlanesAABB(Frustum, new Bounds(feet + Vector3.up * 0.9f, new Vector3(0.8f, 1.9f, 0.8f)));

        /// <summary>The stand-in walks off (now: removed at once, e.g. another stand-in replaces them).</summary>
        void DismissStandIn(bool now)
        {
            if (_temp == null) return;
            if (now || _temp is not UnityPerson up || !up.Root) { RemoveStandIn(); return; }
            var root = up.Root.transform;
            var player = FindFirstObjectByType<PlayerController>();
            if (!_rig) _rig = FindFirstObjectByType<PlayerCameraRig>();
            var goal = WayOut(root.position, player ? player.transform.position : root.position - root.forward, out bool walk);
            if (!walk) { RemoveStandIn(); return; }
            if (_npc == root) _npc = null;
            _tempGoal = goal; _tempState = Leaving; _tempLeaveT = 0; _tempEndAt = -1;
            _temp.SetAnim("walk", WalkSpeed);
        }

        void UpdateStandIn(float dt)
        {
            if (_temp == null) return;
            if (_temp is not UnityPerson up || !up.Root) { _temp = null; _tempFor = null; _tempState = Idle; _tempEndAt = -1; return; } // the level went
            var root = up.Root.transform;
            if (_tempState == Idle)
            {
                if (_tempEndAt >= 0 && Time.unscaledTime >= _tempEndAt && !_shot) DismissStandIn(false);
                return;
            }
            var to = _tempGoal - root.position; to.y = 0;
            float step = WalkSpeed * dt, left = to.magnitude;
            if (left > 1e-3f) root.rotation = Quaternion.Slerp(root.rotation, Quaternion.LookRotation(to / left, Vector3.up), 1f - Mathf.Exp(-8f * dt));
            if (left > step) root.position += to / left * step;
            else
            {
                root.position = new Vector3(_tempGoal.x, root.position.y, _tempGoal.z);
                if (_tempState == Entering)
                {
                    _tempState = Idle;
                    _temp.SetAnim("idle");
                    var player = FindFirstObjectByType<PlayerController>();
                    if (player)
                    {
                        var d = player.transform.position - root.position; d.y = 0;
                        if (d.sqrMagnitude > 0.04f) { _npc = root; _npcFrom = root.rotation; _npcTo = Quaternion.LookRotation(d.normalized, Vector3.up); _npcT = 0; }
                    }
                    return;
                }
            }
            if (_tempState == Leaving)
            {
                _tempLeaveT += dt;
                var cam = _rig && _rig.cam ? _rig.cam : Camera.main;
                bool seen = cam && SeenBy(cam, root);
                bool arrived = (root.position - _tempGoal).sqrMagnitude < 0.01f;
                if (!seen || _tempLeaveT > LeaveTimeout || (arrived && _tempLeaveT > 2.5f)) RemoveStandIn();
            }
        }

        bool SeenBy(Camera cam, Transform who)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, Frustum);
            var feet = who.position;
            if (!Seen(feet)) return false;
            // behind a wall counts as gone (the player's own capsule does not hide them)
            var head = feet + Vector3.up * 1.5f;
            var from = cam.transform.position;
            var d = head - from;
            float len = d.magnitude;
            if (len < 0.5f) return true;
            var player = _rig ? _rig.player : null;
            return !Blocked(from, head - d / len * 0.4f, player ? player.transform : null, who);
        }

        /// <summary>The speaker's eyes: the head bone, else the top of the body less 0.15 m.</summary>
        static Vector3 EyesOf(Transform npc)
        {
            var anim = npc.GetComponentInChildren<Animator>();
            if (anim && anim.isHuman)
            {
                var h = anim.GetBoneTransform(HumanBodyBones.Head);
                if (h) return h.position + Vector3.up * 0.07f;
            }
            var head = FindDeep(npc, "head");
            if (head) return head.position + Vector3.up * 0.07f;
            bool any = false; Bounds b = default;
            foreach (var r in npc.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (any) return new Vector3(npc.position.x, b.max.y - 0.15f, npc.position.z);
            return npc.position + Vector3.up * 1.55f;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase)) return t;
            for (int i = 0; i < t.childCount; i++) { var f = FindDeep(t.GetChild(i), name); if (f) return f; }
            return null;
        }

        /// <summary>Seated poses (sit, sitChair) that the talk must not turn.</summary>
        public static bool IsSeated(string anim) => anim != null && anim.StartsWith("sit", System.StringComparison.Ordinal);

        static Transform Nearest(string id, Vector3 from, float reach)
        {
            Transform best = null; float bestD = reach;
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
            float now = Time.unscaledTime;
            if (_shot && _endAt >= 0 && now >= _endAt) HandBack();
            float udt = Time.unscaledDeltaTime;
            UpdateStandIn(Time.deltaTime);
            UpdateCatchLight(udt);
            if (_pushLeft > 0 && _pushCc && _pushCc.enabled)
            {
                float m = Mathf.Min(_pushLeft, Mathf.Max(0.3f, _pushLeft / Mathf.Max(0.05f, (1f - _shotT) * EaseIn)) * udt);
                _pushCc.Move(_pushDir * m);
                _pushLeft -= m;
            }
            else _pushLeft = 0;
            if (_shot && _rig && _shotT < 1 && OursNow)
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
