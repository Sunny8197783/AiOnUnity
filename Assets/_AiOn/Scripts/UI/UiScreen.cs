using System;
using System.Threading.Tasks;
using AiOn.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// Base for a full-screen modal screen built in code (the ports of the original ui/*.js screens). Open() makes the
    /// canvas, holds the player (InputGate), frees the cursor and builds the window; the screen calls Finish(result) to
    /// close it, which completes the task Open returned. Esc closes with the screen's EscapeResult().
    /// </summary>
    public abstract class UiScreen : MonoBehaviour
    {
        /// <summary>The window panel (1720 x 940, centred) children are built into.</summary>
        protected RectTransform Window { get; private set; }
        protected RectTransform Root { get; private set; }
        protected float OpenedAt { get; private set; }

        TaskCompletionSource<object> _done;
        bool _wasLocked, _closed;

        /// <summary>Creates screen T on its own canvas and returns when it finishes.</summary>
        public static async Task<object> Show<T>(Action<T> setup = null, float width = 1720, float height = 940) where T : UiScreen
        {
            var canvas = UiKit.ScreenCanvas(typeof(T).Name, 220);
            var s = canvas.gameObject.AddComponent<T>();
            s._done = new TaskCompletionSource<object>();
            s.Root = (RectTransform)canvas.transform;
            UiKit.Fill(UiKit.Box("Shade", s.Root, new Color(0, 0, 0, 0.6f), raycast: true).rectTransform);
            var win = UiKit.Box("Window", s.Root, UiKit.Panel).rectTransform;
            win.anchorMin = win.anchorMax = win.pivot = new Vector2(0.5f, 0.5f);
            win.sizeDelta = new Vector2(width, height);
            s.Window = win;
            InputGate.Hold();
            s._wasLocked = Cursor.lockState == CursorLockMode.Locked;
            PlayerInputReader.SetCursorLocked(false);
            setup?.Invoke(s);
            s.Build();
            s.OpenedAt = Time.unscaledTime;
            return await s._done.Task;
        }

        /// <summary>Lays the screen out (Window is ready).</summary>
        protected abstract void Build();

        /// <summary>What Esc returns (null = Esc does nothing).</summary>
        protected virtual object EscapeResult() => null;

        /// <summary>Closes the screen and hands the result to the caller.</summary>
        protected void Finish(object result)
        {
            if (_closed) return;
            _closed = true;
            InputGate.Release();
            if (_wasLocked) PlayerInputReader.SetCursorLocked(true);
            Destroy(gameObject);
            _done.TrySetResult(result);
        }

        void OnDestroy()
        {
            if (_closed) return;
            _closed = true;
            InputGate.Release();
            _done?.TrySetResult(EscapeResult());
        }

        protected virtual void Update()
        {
            if (_closed || Time.unscaledTime - OpenedAt < 0.2f) return;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                var r = EscapeResult();
                if (r != null) Finish(r);
            }
        }

        // ---------- small builders on top of UiKit ----------

        protected Text Title(string kicker, string title)
        {
            UiKit.At(UiKit.Label("Kicker", Window, kicker ?? "", 22, UiKit.Dim).rectTransform, 40, 26, 900, 30);
            var t = UiKit.Label("Title", Window, title ?? "", 40, UiKit.Ink, style: FontStyle.Bold);
            UiKit.At(t.rectTransform, 40, 52, 1300, 56);
            return t;
        }

        protected Button Action(RectTransform parent, string label, bool primary, Action onClick, float x, float y, float w = 240, float h = 60)
        {
            var b = UiKit.Button(label, parent, label, 24, primary ? UiKit.Teal : new Color(1, 1, 1, 0.08f), primary ? new Color(0.04f, 0.08f, 0.08f) : UiKit.Ink);
            UiKit.At((RectTransform)b.transform, x, y, w, h);
            b.onClick.AddListener(() => onClick());
            return b;
        }

        protected static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
            t.DetachChildren();
        }
    }
}
