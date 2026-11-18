using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using AiOn.UI;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    /// <summary>
    /// The IStoryUi screens built so far, each taking the original ui/*.js option object and returning its result:
    /// bag (ui/bag.js: { mode, context, limit, items, goals, tips } -> { bag: { id: n } }).
    /// </summary>
    public static class StoryScreens
    {
        /// <summary>
        /// How long a time-skip line stays before it continues by itself (original ui/dialog.js timeSkip): reading time,
        /// 1.4 s + 55 ms a character, held between 2.5 s and 7.5 s.
        /// </summary>
        public static int TimeSkipMs(string text) => System.Math.Max(2500, System.Math.Min(7500, 1400 + (text?.Length ?? 0) * 55));

        /// <summary>
        /// The time-skip group, centred on the screen as the original's .skip-inner: the from -> to line, 28 px, the
        /// text, 36 px, the progress track, 22 px, the 계속 control. Returns each part's top edge (y up from the centre).
        /// </summary>
        public static float[] SkipLayout(float whenH, float bodyH, float trackH, float contH)
        {
            float gapBody = bodyH > 0 ? 28 : 0, total = whenH + gapBody + bodyH + 36 + trackH + 22 + contH;
            var y = new float[4];
            y[0] = total / 2;
            y[1] = y[0] - whenH - gapBody;
            y[2] = y[1] - bodyH - 36;
            y[3] = y[2] - trackH - 22;
            return y;
        }

        public static void Register(UnityStoryHost host, GameSession session)
        {
            host.Screens["bag"] = o => Bag(o, session);
            // The ports of the original ui/*.js screens (each registers the names app.js gives it).
            PhoneScreen.Register(host);        // phone, emergencyAlert
            ComposeScreen.Register(host);      // compose, showNote, hideNote, rereadCaption
            RereadScreen.Register(host);       // reread (3D re-read: start/eye/crane/path/pin/link/dispose, note)
            PromptScreen.Register(host);       // prompt ({ text, max } -> string | null)
            RadioScreen.Register(host);        // radio
            ExtinguisherScreen.Register(host); // extinguisher
            PlannerScreen.Register(host);      // planner
            DocsScreen.Register(host);         // docs
            RoutemapScreen.Register(host);     // routemap
            DebriefScreens.Register(host);     // failCard, chapterCard, ending, gallery, records, lounge
            MenuScreens.Register(host);        // landing, pause, settings, about
            NotebookScreens.Register(host);     // notebook, inventory
            ShopScreen.Register(host);         // shop
        }

        static Task<JToken> Bag(JObject o, GameSession session)
        {
            var db = session.Rules.Db;
            var items = new List<BagItemView>();
            foreach (JObject i in o["items"] as JArray ?? new JArray())
            {
                var v = new BagItemView
                {
                    Id = (string)i["id"], Name = (string)i["name"] ?? db.NameOf((string)i["id"]),
                    WeightKg = (double?)i["weightKg"] ?? 0, VolumeL = (double?)i["volumeL"] ?? 0,
                    Home = (int?)i["home"] ?? 0, Bag = (int?)i["bag"] ?? 0, Pocket = (int?)i["pocket"] ?? 0,
                    Tip = (string)i["tip"] ?? "", Ready = (bool?)i["ready"] ?? true,
                    Charge = (double?)i["charge"], Expiry = (string)i["expiry"],
                };
                foreach (var t in i["tags"] as JArray ?? new JArray()) v.Tags.Add((string)t);
                items.Add(v);
            }
            var goals = new List<BagGoal>();
            foreach (var g in o["goals"] as JArray ?? new JArray())
            {
                if (g is JObject go)
                    goals.Add(new BagGoal
                    {
                        Id = (string)go["id"], Label = (string)go["label"],
                        Items = (go["items"] as JArray ?? new JArray()).Select(t => (string)t).ToArray(),
                        Min = (double?)go["min"] ?? 1,
                        Per = go["per"] is JObject per ? per.Properties().ToDictionary(p => p.Name, p => (double)p.Value) : null,
                        Facts = (go["facts"] as JArray ?? new JArray()).Select(t => (string)t).ToArray(),
                    });
                else if (g.Type == JTokenType.String) // goal lines given as text only
                    goals.Add(new BagGoal { Id = (string)g, Label = (string)g, Items = new string[0], Facts = new string[0] });
            }
            var tips = (o["tips"] as JArray ?? new JArray()).Select(t => (string)t).ToList();
            var limit = o["limit"] as JObject;
            var tcs = new TaskCompletionSource<JToken>();
            BagScreen.Open(new BagPackingModel(items, db), (string)o["context"] ?? "alert", (string)o["mode"] ?? "guided",
                (double?)limit?["weightKg"] ?? db.bagLimitKg, (double?)limit?["volumeL"] ?? db.bagLimitL, goals, tips,
                result =>
                {
                    var bag = new JObject();
                    foreach (var (id, n) in result) bag[id] = n;
                    tcs.TrySetResult(new JObject { ["bag"] = bag });
                });
            return tcs.Task;
        }
    }
}
