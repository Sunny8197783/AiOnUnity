// The re-read's host side (story/seoyeon.js + notecheck.js + ui/compose.js): "reread" carries the 3D ops before the first
// and after the last caption; every "rereadCaption" step carries the ops and the note panel change for that step.
// Data contract: Assets/_AiOn/Scripts/Story/SeoyeonRelay.cs header.
using System.Threading.Tasks;
using AiOn.Story;
using Newtonsoft.Json.Linq;

namespace AiOn.UI
{
    public static class RereadScreen
    {
        /// <summary>Registers "reread" ({ fx: [...], note? } -> null). "rereadCaption" is registered by ComposeScreen
        /// and goes through Caption below.</summary>
        public static void Register(UnityStoryHost host)
        {
            host.Screens["reread"] = o =>
            {
                ApplyNote(o);
                RereadFx.Apply(host, o?["fx"] as JArray);
                return Task.FromResult<JToken>(null);
            };
        }

        /// <summary>One caption step: note panel change, then the 3D ops, then the caption ('next' | 'skip').</summary>
        public static Task<JToken> Caption(UnityStoryHost host, JObject o)
        {
            o ??= new JObject();
            ApplyNote(o);
            RereadFx.Apply(host, o["fx"] as JArray);
            return RereadCaption.Open(o);
        }

        /// <summary>note: { hand, mode } shows the note panel, JSON null hides it, absent leaves it as it is.</summary>
        public static void ApplyNote(JObject o)
        {
            switch (NoteChange(o))
            {
                case NoteAction.Show: RelayNotePanel.Show(o["note"]["hand"] as JObject, o["note"]["mode"]?.ToString()); break;
                case NoteAction.Hide: RelayNotePanel.Hide(); break;
            }
        }

        public enum NoteAction { None, Show, Hide }

        /// <summary>What the step's "note" key asks for (pure, tested).</summary>
        public static NoteAction NoteChange(JObject o)
        {
            if (o == null || !o.TryGetValue("note", out var note)) return NoteAction.None;
            if (note == null || note.Type == JTokenType.Null || note.Type == JTokenType.Undefined) return NoteAction.Hide;
            return note is JObject n && n["hand"] is JObject ? NoteAction.Show : NoteAction.Hide;
        }
    }
}
