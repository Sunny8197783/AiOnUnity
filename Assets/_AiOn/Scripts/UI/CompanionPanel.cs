using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using AiOn.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// 도우미 모드's view (Game/Companion.cs has the words): a small "도우미 F1 힌트" chip, the hint plate F1 (or the chip)
    /// opens, and the "함께 생각해 보기" card. The card stops the game before a judgement's choices (StoryOverlay awaits
    /// <see cref="Discuss"/>) and sits beside a screen's own judgement (DecisionMoment with a topic) without stopping it.
    /// The helper never picks: the card only says what to talk about. Its own canvas above the story screens.
    /// </summary>
    public sealed class CompanionPanel : MonoBehaviour
    {
        const int Order = 226; // above the story screens (220-224) and the reread caption (225)
        const float HintSeconds = 20f;
        // the chip sits above the HUD's vitals panel (bottom-left) while roaming, and in the corner under a screen's window
        static readonly Vector2 ChipRoam = new(24, 186), ChipModal = new(12, 12);

        static CompanionPanel s_inst;
        RectTransform _root, _chip, _hint, _talk;
        Image _blocker;
        Text _hintText, _talkText;
        Button _continue;
        float _hintUntil;
        TaskCompletionSource<bool> _waiting;
        float _savedScale = 1f;
        bool _pausedTime, _sideTalk;
        AiOn.Settings.HostSettings _voice;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_inst = null; }

        /// <summary>Makes the panel once (the overlay calls it); it lives across level loads and only shows while the mode is on.</summary>
        public static CompanionPanel Ensure()
        {
            if (s_inst) return s_inst;
            var canvas = UiKit.ScreenCanvas("CompanionPanel", Order);
            DontDestroyOnLoad(canvas.gameObject);
            s_inst = canvas.gameObject.AddComponent<CompanionPanel>();
            s_inst.Build((RectTransform)canvas.transform);
            return s_inst;
        }

        void Build(RectTransform root)
        {
            _root = root;
            _blocker = UiKit.Box("Blocker", root, new Color(0, 0, 0, 0.35f), raycast: true);
            UiKit.Fill(_blocker.rectTransform);
            _blocker.gameObject.SetActive(false);

            // the chip: bottom-left, clear of the talk box (centre) and the key strip (right)
            var chipImg = HudTheme.Plate("Chip", root, dense: true);
            chipImg.raycastTarget = true;
            _chip = chipImg.rectTransform;
            _chip.anchorMin = _chip.anchorMax = _chip.pivot = new Vector2(0, 0);
            _chip.anchoredPosition = ChipRoam; _chip.sizeDelta = new Vector2(184, 44);
            var chipBtn = chipImg.gameObject.AddComponent<Button>();
            chipBtn.onClick.AddListener(ToggleHint);
            var edge = UiKit.Box("Edge", _chip, UiKit.Teal).rectTransform;
            edge.anchorMin = new Vector2(0, 0); edge.anchorMax = new Vector2(0, 1); edge.pivot = new Vector2(0, 0.5f);
            edge.offsetMin = Vector2.zero; edge.offsetMax = new Vector2(4, 0);
            var chipText = UiKit.Label("Text", _chip, Companion.ChipText, 19, HudTheme.Text, TextAnchor.MiddleCenter);
            UiKit.Fill(chipText.rectTransform, 10, 2);

            _hint = Plate("Hint", root, new Vector2(0, 0), ChipRoam + new Vector2(0, 54), 600, out _hintText);
            _hint.gameObject.SetActive(false);

            _talk = Plate("Discuss", root, new Vector2(0.5f, 1), new Vector2(0, -40), 980, out _talkText);
            _continue = UiKit.Button("Continue", _talk, Companion.ContinueLabel, 22, UiKit.Teal, new Color(0.04f, 0.08f, 0.08f));
            UiKit.Height(_continue, 54);
            _continue.onClick.AddListener(() => _waiting?.TrySetResult(true));
            _talk.gameObject.SetActive(false);

            DecisionMoment.Began += OnMomentBegan;
            DecisionMoment.Ended += OnMomentEnded;
            Refresh();
        }

        void OnDestroy()
        {
            DecisionMoment.Began -= OnMomentBegan;
            DecisionMoment.Ended -= OnMomentEnded;
            ResumeTime();
            _waiting?.TrySetResult(false);
            if (s_inst == this) s_inst = null;
        }

        static RectTransform Plate(string name, Transform parent, Vector2 anchor, Vector2 pos, float width, out Text text)
        {
            var img = HudTheme.Plate(name, parent, dense: true);
            img.raycastTarget = true; // a click on the plate does not fall through to the game behind it
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(width, 10);
            var col = img.gameObject.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(24, 24, 18, 20); col.spacing = 12;
            col.childControlWidth = true; col.childControlHeight = true; col.childForceExpandHeight = false; col.childForceExpandWidth = true;
            img.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            text = UiKit.Label("Text", rt, "", 21, HudTheme.Text);
            text.lineSpacing = 1.1f;
            return rt;
        }

        // only in a run: the title and its menus show no chip and F1 does nothing there (Companion.Offered)
        void Refresh()
        {
            bool on = Companion.Offered;
            if (_chip && _chip.gameObject.activeSelf != on) _chip.gameObject.SetActive(on);
            if (!on)
            {
                if (_hint && _hint.gameObject.activeSelf) _hint.gameObject.SetActive(false);
                if (_sideTalk) HideTalk();
                _waiting?.TrySetResult(true); // a card left waiting when the run ends (or the mode goes off) lets the story go on
            }
        }

        void Update()
        {
            Refresh();
            if (!Companion.Offered) return;
            var at = UiScreen.OpenCount > 0 ? ChipModal : ChipRoam;
            if (_chip.anchoredPosition != at) { _chip.anchoredPosition = at; PlaceHint(); }
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) ToggleHint();
            if (_hint.gameObject.activeSelf && Time.unscaledTime > _hintUntil) _hint.gameObject.SetActive(false);
            if (_waiting != null && !_waiting.Task.IsCompleted && Time.unscaledTime > _talkArmAt)
            {
                var g = Gamepad.current;
                if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                    || (g != null && g.buttonSouth.wasPressedThisFrame)) _waiting.TrySetResult(true);
            }
        }

        float _talkArmAt;
        readonly Vector3[] _corners = new Vector3[4];

        /// <summary>
        /// Where the hint plate goes. Roaming: above the chip (bottom-left, clear of the vitals panel). Over a screen: in
        /// the wider free margin beside its window, from the top, so the screen's own content (a log, a key strip, its
        /// own 힌트 보기) stays visible; a window with no margin wide enough (a full-width screen, a story card) gets a
        /// narrower plate above the chip in the corner.
        /// </summary>
        void PlaceHint()
        {
            Vector2 anchor = Vector2.zero, pos = ChipRoam + new Vector2(0, 54);
            float width = 600;
            if (UiScreen.OpenCount > 0)
            {
                pos = ChipModal + new Vector2(0, 54); width = 520;
                var win = UiScreen.TopWindow;
                if (win)
                {
                    win.GetWorldCorners(_corners);
                    float l = _root.InverseTransformPoint(_corners[0]).x, r = _root.InverseTransformPoint(_corners[2]).x;
                    var area = _root.rect;
                    float leftRoom = l - area.xMin, rightRoom = area.xMax - r, room = Mathf.Max(leftRoom, rightRoom);
                    if (room >= MarginMin)
                    {
                        bool left = leftRoom >= rightRoom;
                        anchor = left ? new Vector2(0, 1) : new Vector2(1, 1);
                        pos = new Vector2(left ? 12 : -12, -24);
                        width = Mathf.Min(560, room - 24);
                    }
                }
            }
            _hint.anchorMin = _hint.anchorMax = _hint.pivot = anchor;
            _hint.anchoredPosition = pos;
            _hint.sizeDelta = new Vector2(width, _hint.sizeDelta.y);
            _hintText.fontSize = width < 450 ? 18 : 21;
        }

        const float MarginMin = 300; // the narrowest free margin beside a screen's window the plate goes into

        // ------------------------------------------------------------------ hint (F1)

        void ToggleHint()
        {
            if (!Companion.Offered) return;
            if (_hint.gameObject.activeSelf) { _hint.gameObject.SetActive(false); return; }
            var h = Companion.Hint(Companion.Screen, Companion.ObjectiveTitle, Companion.ObjectiveBody, Companion.ObjectiveSteps, EasyKorean.Of);
            var sb = new StringBuilder();
            sb.Append("<b><color=#f2b33d>").Append(h.Title).Append("</color></b>");
            foreach (var l in h.Lines) sb.Append('\n').Append(l);
            var facts = GameSession.Current ? GameSession.Current.FactsData : null;
            var text = h.FactId != null ? EasyKorean.FactText(facts, h.FactId) : null;
            if (!string.IsNullOrEmpty(text))
            {
                var src = facts.Source(facts.SourceId(h.FactId));
                sb.Append("\n<color=#8fd9cf>▸ ").Append(text).Append("</color>");
                if (src != null) sb.Append("\n<size=16><color=#94a3b8>출처: ").Append(src.Publisher).Append("</color></size>");
            }
            sb.Append("\n<size=16><color=#94a3b8>F1 다시 누르면 닫힘</color></size>");
            _hintText.text = sb.ToString();
            PlaceHint();
            _hint.gameObject.SetActive(true);
            _hint.SetAsLastSibling();
            _hintUntil = Time.unscaledTime + HintSeconds;
            UiSound.Play("tick");
            Speak(OverlaySpeech.Plain(sb.ToString().Replace('\n', ' ')));
        }

        // ------------------------------------------------------------------ 함께 생각해 보기

        /// <summary>
        /// Stops the game and shows the discussion card for a judgement; completes when the pair is done talking (the
        /// button, Enter, Space or the pad's A). Does nothing (completes at once) when the mode is off.
        /// </summary>
        public static Task Discuss(string question, IReadOnlyList<string> choices, string topic = null, bool atBottom = false)
        {
            // once per judgement: a loop asking it again (the same choices) goes straight to the choices
            if (!Companion.Offered || !Companion.FirstPass(choices)) return Task.CompletedTask;
            return Ensure().ShowTalk(Companion.Discuss(question, choices, topic), blocking: true, atBottom);
        }

        Task ShowTalk(CompanionTalk t, bool blocking, bool atBottom = false)
        {
            // over a talk (bottom) the card sits at the top; over a centred decision card at the bottom; beside a screen's
            // own judgement (not blocking) in the lower right, narrower, clear of the screen's title and question
            var anchor = !blocking ? new Vector2(1, 0) : new Vector2(0.5f, atBottom ? 0 : 1);
            _talk.anchorMin = _talk.anchorMax = _talk.pivot = anchor;
            _talk.anchoredPosition = !blocking ? new Vector2(-150, 110) : new Vector2(0, atBottom ? 40 : -40);
            _talk.sizeDelta = new Vector2(blocking ? 980 : 700, _talk.sizeDelta.y);
            _waiting?.TrySetResult(false);
            _talkText.text = TalkText(t, blocking);
            _continue.gameObject.SetActive(blocking);
            _blocker.gameObject.SetActive(blocking);
            _blocker.transform.SetAsLastSibling();
            _talk.gameObject.SetActive(true);
            _talk.SetAsLastSibling();
            _sideTalk = !blocking;
            Speak(OverlaySpeech.Plain(_talkText.text.Replace('\n', ' ')));
            if (!blocking) { _waiting = null; return Task.CompletedTask; }
            if (!_pausedTime && Time.timeScale > 0.001f) { _savedScale = Time.timeScale; Time.timeScale = 0f; _pausedTime = true; }
            PlayerInputReaderHold(true);
            _talkArmAt = Time.unscaledTime + 0.3f; // the key that closed the last line does not close this card
            _waiting = new TaskCompletionSource<bool>();
            var done = _waiting;
            return Finish(done);
        }

        async Task Finish(TaskCompletionSource<bool> done)
        {
            try { await done.Task; }
            finally
            {
                if (_waiting == done) { HideTalk(); }
                UiSound.Play("confirm");
            }
        }

        void HideTalk()
        {
            _talk.gameObject.SetActive(false);
            _blocker.gameObject.SetActive(false);
            _sideTalk = false;
            ResumeTime();
            if (_held) PlayerInputReaderHold(false);
            _waiting = null;
        }

        void ResumeTime()
        {
            if (!_pausedTime) return;
            _pausedTime = false;
            if (Time.timeScale <= 0.001f) Time.timeScale = _savedScale; // a pause someone else set meanwhile stays theirs
        }

        bool _held;
        void PlayerInputReaderHold(bool on)
        {
            if (on == _held) return;
            _held = on;
            if (on) AiOn.Player.PlayerInputReader.HoldPointer(); else AiOn.Player.PlayerInputReader.ReleasePointer();
        }

        static string TalkText(CompanionTalk t, bool blocking)
        {
            var sb = new StringBuilder();
            sb.Append("<size=18><b><color=#3cc6b5>").Append(t.Title).Append(blocking ? " · 게임을 잠시 멈췄어요" : "").Append("</color></b></size>");
            sb.Append("\n<size=26><b>").Append(EasyKorean.Of(t.Question)).Append("</b></size>");
            for (int i = 0; i < t.Prompts.Count; i++) sb.Append("\n<color=#f2b33d>").Append(i + 1).Append(".</color> ").Append(t.Prompts[i]);
            if (t.Choices.Count > 0)
            {
                sb.Append("\n<size=17><color=#94a3b8>고를 수 있는 것: ");
                for (int i = 0; i < t.Choices.Count; i++) sb.Append(i == 0 ? "" : " / ").Append(OverlaySpeech.Plain(EasyKorean.Of(t.Choices[i])));
                sb.Append("</color></size>");
            }
            sb.Append("\n<size=17><color=#94a3b8>").Append(t.Note).Append("</color></size>");
            return sb.ToString();
        }

        // a screen's own judgement (the look-first and sighting screens): the talking points beside it, the game not stopped
        void OnMomentBegan(string topic)
        {
            if (!Companion.Offered || topic == null || (_waiting != null && !_waiting.Task.IsCompleted)) return;
            ShowTalk(Companion.Discuss(TopicQuestion(topic), null, topic), blocking: false);
        }

        void OnMomentEnded()
        {
            if (_sideTalk) HideTalk();
        }

        static string TopicQuestion(string topic) => topic switch
        {
            "fire" => "불이 났을 때, 어떻게 할까?",
            "report" => "본 것을 누구에게, 어떻게 알릴까?",
            _ => "어떻게 할까?",
        };

        void Speak(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!_voice) _voice = FindFirstObjectByType<AiOn.Settings.HostSettings>();
            if (_voice) _voice.Speak(text);
        }
    }
}
