using System;
using System.Collections.Generic;

namespace AiOn.UI
{
    /// <summary>Which HUD parts show, from the overlay's flags (pure: the overlay and GameHud ask it each frame).</summary>
    public static class HudVisibility
    {
        /// <summary>A roaming HUD part (chapter and clock, chips, stats, compass, minimap, stamina): wanted, HUD up, no cutscene.</summary>
        public static bool Part(bool wanted, bool hidden, bool cinematic) => wanted && !hidden && !cinematic;

        /// <summary>The centre dot and the interact prompt: only in free play (no cutscene, modal window or dialog).</summary>
        public static bool Pointer(bool cinematic, int modals, bool dialog) => !cinematic && modals <= 0 && !dialog;

        /// <summary>The faded HUD layer's alpha target: gone under any modal window.</summary>
        public static float LayerAlpha(int modals) => modals > 0 ? 0f : 1f;

        /// <summary>The key strip: free roam (compass wanted, nothing holding the player) and its timer still running.</summary>
        public static bool KeyStrip(bool up, bool roaming, bool timerOn) => up && roaming && timerOn;
    }

    /// <summary>
    /// The key strip's timer: it shows for 20 s of roaming at the start, then fades; H and each level entry bring it
    /// back for 8 s. Only roaming time counts (a dialog or menu does not use it up).
    /// </summary>
    public sealed class KeyStripTimer
    {
        public const float First = 20f, Again = 8f;
        float _left = First;

        public bool On => _left > 0;

        public void Show(float seconds) => _left = Math.Max(_left, seconds);

        /// <summary>Advances by dt while roaming; returns whether the strip should show.</summary>
        public bool Tick(bool roaming, float dt)
        {
            if (roaming && _left > 0) _left = Math.Max(0, _left - dt);
            return _left > 0;
        }
    }

    /// <summary>
    /// The toast rules: toasts that arrive while a modal window is open wait in a queue and come out one by one, 0.6 s
    /// apart, once the last modal closes; the "메모: [안전안내문자] …" echo of an alert the popup shows is dropped; at most
    /// two official toasts are on screen. A plain class with one reused list (no garbage per frame).
    /// </summary>
    public sealed class ToastQueue
    {
        public readonly struct Item
        {
            public readonly string Text, Label;
            public readonly int Ms;
            public Item(string text, string label, int ms) { Text = text; Label = label; Ms = ms; }
        }

        public const float Stagger = 0.6f, FirstDelay = 0.3f;
        public const int OfficialCap = 2;
        public const string NotePrefix = "메모: ";

        readonly List<Item> _pending = new(8);
        double _nextAt;
        bool _wasOpen;

        /// <summary>The text of the last alert popup (the phone's 안전안내문자 / 위급재난문자).</summary>
        public string AlertText { get; set; }

        public int Pending => _pending.Count;

        public void Enqueue(string text, string label, int ms) => _pending.Add(new Item(text, label, ms));

        /// <summary>Call every frame: the next toast to show now, if any.</summary>
        public bool TryNext(double now, bool modalOpen, out Item item)
        {
            item = default;
            if (modalOpen) { _wasOpen = true; return false; }
            if (_wasOpen) { _wasOpen = false; _nextAt = now + FirstDelay; }
            if (_pending.Count == 0 || now < _nextAt) return false;
            item = _pending[0];
            _pending.RemoveAt(0);
            _nextAt = now + Stagger;
            return true;
        }

        /// <summary>
        /// A note toast that only repeats the alert popup ("메모: " + an alert text the popup shows). A note whose text is a
        /// bracketed alert ("[안전안내문자] …", "[위급재난문자] …") is always the popup's: the story notes it just before
        /// opening it. Remembers that text as the alert.
        /// </summary>
        public bool IsAlertEcho(string text)
        {
            if (string.IsNullOrEmpty(text) || !text.StartsWith(NotePrefix, StringComparison.Ordinal)) return false;
            var body = text.Substring(NotePrefix.Length);
            if (body.Length == 0) return false;
            if (!string.IsNullOrEmpty(AlertText) && AlertText.Contains(body)) return true;
            if (body[0] == '[' && body.IndexOf("문자]", StringComparison.Ordinal) > 0) { AlertText = body; return true; }
            return false;
        }

        /// <summary>Index of the toast to remove before adding one with this label (the oldest official past the cap), or -1.</summary>
        public static int EvictIndex(IReadOnlyList<string> visibleLabels, string incoming, int cap = OfficialCap)
        {
            if (incoming != "official" || visibleLabels == null) return -1;
            int count = 0, first = -1;
            for (int i = 0; i < visibleLabels.Count; i++)
                if (visibleLabels[i] == "official") { count++; if (first < 0) first = i; }
            return count >= cap ? first : -1;
        }

        /// <summary>
        /// Index of an official toast on screen that says nearly the same as <paramref name="text"/> (the new one takes its
        /// place instead of stacking a second card of the same advice), or -1. Same = at least 40 % of the shorter one's
        /// two-letter pieces (letters only, the "공습경보 —" heading dropped) appear in the other.
        /// </summary>
        public static int RepeatIndex(IReadOnlyList<string> visibleLabels, IReadOnlyList<string> visibleTexts, string incoming, string text)
        {
            if (incoming != "official" || visibleLabels == null || visibleTexts == null || string.IsNullOrEmpty(text)) return -1;
            var a = Pieces(text);
            for (int i = 0; i < visibleLabels.Count && i < visibleTexts.Count; i++)
            {
                if (visibleLabels[i] != "official" || string.IsNullOrEmpty(visibleTexts[i])) continue;
                var b = Pieces(visibleTexts[i]);
                int small = Math.Min(a.Count, b.Count);
                if (small == 0) continue;
                int shared = 0;
                foreach (var p in a) if (b.Contains(p)) shared++;
                if (shared * 5 >= small * 2) return i;
            }
            return -1;
        }

        static HashSet<string> Pieces(string text)
        {
            int dash = text.IndexOf(" — ", StringComparison.Ordinal);
            if (dash > 0 && dash < 16) text = text.Substring(dash + 3);
            var set = new HashSet<string>();
            char prev = '\0';
            foreach (char c in text)
            {
                if (!char.IsLetterOrDigit(c)) { prev = '\0'; continue; }
                if (prev != '\0') set.Add(new string(new[] { prev, c }));
                prev = c;
            }
            return set;
        }
    }

    /// <summary>The chapter banner's opacity over time (fade in 0.4 s, hold 2.2 s, fade out 0.6 s; reduce motion: 1.5 s still).</summary>
    public static class ChapterBanner
    {
        public const float In = 0.4f, Hold = 2.2f, Out = 0.6f, Still = 1.5f;

        public static float Duration(bool reduceMotion) => reduceMotion ? Still : In + Hold + Out;

        public static float Alpha(float t, bool reduceMotion)
        {
            if (t < 0) return 0;
            if (reduceMotion) return t < Still ? 1 : 0;
            if (t < In) return t / In;
            if (t < In + Hold) return 1;
            if (t < In + Hold + Out) return 1 - (t - In - Hold) / Out;
            return 0;
        }

        /// <summary>
        /// A banner (or the objective's flash) waiting to play starts once it can be seen: the HUD shown and no modal
        /// window or card over it (the HUD layer is faded out under one).
        /// </summary>
        public static bool ShouldStart(bool pending, bool hidden, int openModals) => pending && !hidden && openModals <= 0;

        /// <summary>The objective plate's amber border: two pulses over 0.8 s.</summary>
        public static float Flash(float t)
        {
            const float dur = 0.8f;
            if (t < 0 || t >= dur) return 0;
            float k = t / (dur / 2) % 1f; // two pulses
            return (float)Math.Sin(k * Math.PI);
        }
    }
}
