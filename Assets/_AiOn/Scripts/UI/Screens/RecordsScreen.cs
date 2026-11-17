using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AiOn.UI
{
    /// <summary>
    /// records(rec: readRecords(...)) -> null (original ui/records.js). Options: the records object { done, best, runs }
    /// (read through Challenges.ReadRecords). Every challenge with the day it was first done (or how to do it), the
    /// top-5 runs and the best time per chapter. Keys: ↑ ↓ PageUp PageDown scroll, Enter = 닫기, Esc closes.
    /// </summary>
    public class RecordsScreen : DebriefScreenBase
    {
        RecordsViewModel _v;
        protected override bool EscCloses => true;

        public static async Task<JToken> Open(JObject options)
        {
            var view = DebriefLogic.Records(Challenges.ReadRecords((JToken)(options ?? new JObject())));
            await Show<RecordsScreen>(s => s._v = view, 1240, 960);
            return null;
        }

        protected override void Build()
        {
            Eyebrow(Window, _v.Eyebrow, 48, 30, 900);
            Heading(Window, _v.Title, 48, 62, WindowW - 96);
            var col = DebriefUi.Scroll(Window, 48, 132, WindowW - 96, WindowH - 132 - 112, out var scroll, 8);
            Keys.Scroll = scroll;
            foreach (var r in _v.Challenges) DebriefListRows.Add(col, r);

            UiKit.Height(DebriefUi.Para(col, _v.RankTitle, 26, UiKit.Ink, FontStyle.Bold), 56);
            if (_v.Ranks.Count == 0) DebriefUi.Para(col, _v.RankEmpty, 22, DebriefUi.Muted);
            foreach (var r in _v.Ranks) Line(col, r);

            UiKit.Height(DebriefUi.Para(col, _v.BestTitle, 26, UiKit.Ink, FontStyle.Bold), 56);
            foreach (var r in _v.Bests) Line(col, r);

            DebriefUi.Para(col, _v.Note, 19, DebriefUi.Muted);
            Footer(("닫기", true, true, () => Finish(null)));
        }

        static void Line(RectTransform col, DebriefLine l)
        {
            var row = DebriefUi.Card(col, UiKit.Row, 18, 10, 2);
            UiKit.Label("Li", row, $"<b><color={DebriefUi.Rgb(DebriefUi.Accent)}>{l.Bold}</color></b>   {l.Text}", 22, UiKit.Ink);
            if (!string.IsNullOrEmpty(l.Small)) UiKit.Label("Small", row, l.Small, 19, UiKit.Dim);
        }
    }
}
