// 루머 해부 card (competition upgrade, prebunking): after a rumour has been checked or corrected, the post is taken apart
// once per run (RumorAnatomy has the posts, the techniques and the lines). Talks through ctx.Ui directly: call it from a
// roam handler (already modal) or wrap it in ctx.Modal.
using System.Collections.Generic;
using System.Threading.Tasks;
using AiOn.Game;

namespace AiOn.Story
{
    public static class PrebunkScene
    {
        /// <summary>
        /// A test seam like DDay.CprPart: the tests that compare a chapter with the original set it to false (no card, no
        /// 'prebunk' event).
        /// </summary>
        public static bool Enabled = true;

        /// <summary>The toast when the card named a technique this run had not met yet (where the archive keeps them).</summary>
        public static readonly string Notice = $"짚어 본 수법은 기록관에 모입니다 · {Archive.Path} › 기록관 › 루머 수법";

        /// <summary>The card for this post, once per run. Returns true when it was shown.</summary>
        public static async Task<bool> Show(StoryContext ctx, string id)
        {
            var g = ctx.G;
            if (!Enabled || RumorAnatomy.Of(id) == null || RumorAnatomy.Shown(g, id)) return false;
            var meta = RumorAnatomy.Meta(g, id);
            var body = RumorAnatomy.CardLines(g, id, ctx.Facts, AiOn.UI.TextScaler.Scale); // size tags follow 글자 크기
            var fresh = RumorAnatomy.Record(g, id);
            if (ctx.Settings.Profile == "lowvision" && !ctx.UiReadsAloud)
                try { ctx.Speak?.Invoke(RumorAnatomy.Spoken(g, id)); } catch { /* no voice */ }
            await ctx.Ui.Card(new CardOptions
            {
                Kind = RumorAnatomy.Kind, Title = RumorAnatomy.Of(id).Where, Meta = meta, Body = body,
                Actions = new List<CardAction> { new("ok", "다음 글에서도 짚어 볼게요", true) },
            });
            if (fresh.Count > 0)
                ctx.Toast(Notice, "self", 4200);
            return true;
        }
    }
}
