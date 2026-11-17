using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace AiOn.UI
{
    /// <summary>
    /// lounge({ name, best, topic }) -> null (original ui/lounge.js). 대피소 라운지: the last 12 hours of fixed-phrase
    /// messages from the public relay, phrase buttons and posting your best run; offline it says so and the game goes
    /// on. The original's live EventSource stream is replaced by polling the relay every few seconds. With no valid
    /// name in the options the name is made once and kept in PlayerPrefs (the original loungeName(store)).
    /// Keys: Tab / ← → choose a button, Enter or Space sends / closes, Esc closes.
    /// </summary>
    public class LoungeScreen : DebriefScreenBase
    {
        LoungeArgs _a;
        LoungeFeed _feed;
        RectTransform _feedList;
        ScrollRect _feedScroll;
        Text _status;
        readonly List<Button> _sendButtons = new();
        bool _connected;
        protected override bool EscCloses => true;

        public static async Task<JToken> Open(JObject options)
        {
            var a = LoungeLogic.Parse(options);
            if (!Lounge.IsName(a.Name))
            {
                var stored = PlayerPrefs.GetString(LoungeLogic.NameKey, "");
                a.Name = LoungeLogic.EnsureName(stored, null, out var created);
                if (created) { PlayerPrefs.SetString(LoungeLogic.NameKey, a.Name); PlayerPrefs.Save(); }
            }
            await Show<LoungeScreen>(s => s._a = a, 1240, 1000);
            return null;
        }

        protected override void Build()
        {
            _feed = new LoungeFeed(_a.Name);
            Eyebrow(Window, "대피소 라운지", 48, 28, 900);
            Heading(Window, "연무동 이웃들과 이야기하기", 48, 58, WindowW - 96);
            UiKit.At(UiKit.Label("Name", Window, LoungeLogic.NameLine(_a.Name), 20, UiKit.Dim).rectTransform, 48, 116, WindowW - 96, 30);

            var feedArea = UiKit.Box("Feed", Window, new Color(0, 0, 0, 0.22f)).rectTransform;
            UiKit.At(feedArea, 48, 156, WindowW - 96, 430);
            _feedList = UiKit.ScrollList("FeedScroll", feedArea, 6);
            _feedScroll = _feedList.parent.GetComponent<ScrollRect>();
            Keys.Scroll = _feedScroll;

            _status = UiKit.Label("Status", Window, LoungeLogic.Connecting, 20, UiKit.Dim);
            UiKit.At(_status.rectTransform, 48, 596, WindowW - 96, 30);

            // Phrase buttons: two rows of five.
            var phrases = Lounge.Phrases.ToList();
            float bw = (WindowW - 96 - 4 * 12) / 5f;
            for (int i = 0; i < phrases.Count; i++)
            {
                var key = phrases[i].Key;
                var b = UiKit.Button("Phrase", Window, phrases[i].Value, 19, DebriefUi.Ghost, UiKit.Ink);
                UiKit.At((RectTransform)b.transform, 48 + (i % 5) * (bw + 12), 636 + (i / 5) * 76, bw, 66);
                b.onClick.AddListener(() => Send(Lounge.PhraseBody(_a.Name, key)));
                _sendButtons.Add(b);
                Keys.Add(b);
            }
            UiKit.At(UiKit.Label("Note", Window, LoungeLogic.Note, 18, DebriefUi.Muted).rectTransform, 48, 794, WindowW - 96, 60);

            var footer = new List<(string, bool, bool, Action)>();
            if (_a.Best != null) footer.Add(("내 최고 기록 알리기", false, false, () => Send(Lounge.RunBody(_a.Name, _a.Best))));
            footer.Add(("닫기", true, true, () => Finish(null)));
            var made = Footer(footer.ToArray());
            if (_a.Best != null)
            {
                made[0].GetComponent<Image>().color = new Color(1, 1, 1, 0.14f);
                _sendButtons.Add(made[0]);
            }
            _ = PollLoop();
        }

        void AddRow(LoungeMessage m)
        {
            if (!_feed.Add(m)) return;
            var r = _feed.Rows[_feed.Rows.Count - 1];
            bool mine = r.Class.Contains("mine"), run = r.Class.Contains("run");
            var row = DebriefUi.Card(_feedList, mine ? new Color(0.24f, 0.78f, 0.71f, 0.12f) : run ? new Color(0.95f, 0.70f, 0.24f, 0.10f) : UiKit.Row, 16, 8, 2);
            UiKit.Label("Li", row, $"<b><color={DebriefUi.Rgb(mine ? UiKit.Teal : run ? DebriefUi.Accent : UiKit.Ink)}>{r.Who}</color></b>   {r.Text}   <size=16><color={DebriefUi.Rgb(UiKit.Dim)}>{r.Time}</color></size>", 21, UiKit.Ink);
            while (_feedList.childCount > LoungeFeed.Max)
            {
                var first = _feedList.GetChild(0);
                first.SetParent(null, false);
                Destroy(first.gameObject);
            }
            Canvas.ForceUpdateCanvases();
            _feedScroll.verticalNormalizedPosition = 0;
        }

        /// <summary>History of the last 12 hours, then the messages after the newest one every few seconds.</summary>
        async Task PollLoop()
        {
            while (this)
            {
                var (ok, text) = await Request(LoungeLogic.PollUrl(_a.Topic, _feed.LastId), null);
                if (!this) return;
                if (ok)
                {
                    foreach (var m in Lounge.ReadHistory(text)) AddRow(m);
                    if (!_connected) { _connected = true; _status.text = LoungeLogic.Connected; }
                }
                else { _connected = false; _status.text = LoungeLogic.Offline; }
                float until = Time.unscaledTime + (float)LoungeLogic.PollEvery;
                while (this && Time.unscaledTime < until) await Task.Yield();
            }
        }

        /// <summary>One message per 5 s from this panel (the relay limits bursts too).</summary>
        async void Send(string body)
        {
            foreach (var b in _sendButtons) b.interactable = false;
            var (ok, _) = await Request(LoungeLogic.TopicUrl(_a.Topic), body);
            if (!this) return;
            if (!ok) _status.text = LoungeLogic.SendFailed;
            else _ = PollOnce();
            float until = Time.unscaledTime + (float)LoungeLogic.SendCooldown;
            while (this && Time.unscaledTime < until) await Task.Yield();
            if (!this) return;
            foreach (var b in _sendButtons) if (b) b.interactable = true;
        }

        /// <summary>Shows a sent message without waiting for the next poll.</summary>
        async Task PollOnce()
        {
            var (ok, text) = await Request(LoungeLogic.PollUrl(_a.Topic, _feed.LastId), null);
            if (this && ok) foreach (var m in Lounge.ReadHistory(text)) AddRow(m);
        }

        static async Task<(bool ok, string text)> Request(string url, string postBody)
        {
            using var req = postBody == null ? UnityWebRequest.Get(url) : new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(postBody)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            req.timeout = 10;
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            bool ok = req.result == UnityWebRequest.Result.Success && req.responseCode >= 200 && req.responseCode < 300;
            return (ok, ok ? req.downloadHandler.text : null);
        }
    }
}
