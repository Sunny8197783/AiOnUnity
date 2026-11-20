using System.Linq;
using AiOn.Game;
using AiOn.Interaction;
using AiOn.UI;
using UnityEngine;

namespace AiOn.Story
{
    /// <summary>
    /// Connects E on a point to what it does in this level and shows the result on the HUD: the bag by the door opens
    /// the bag screen, the wallet is picked up (and leaves the table), everything else gets its ApartmentWorld lines.
    /// A point with nothing to say yet shows its label as a toast, so no E press is silent.
    /// </summary>
    public class WorldInteractions : MonoBehaviour
    {
        public PlayerInteractor interactor;
        public GameHud hud;
        public GameSession session;

        public WorldResponse Last { get; private set; }

        void Start()
        {
            if (!interactor) interactor = FindFirstObjectByType<PlayerInteractor>();
            if (!hud) hud = FindFirstObjectByType<GameHud>();
            if (!session) session = FindFirstObjectByType<GameSession>();
            if (interactor) interactor.Interacted += OnInteracted;
            SyncBagLabel();
        }

        void OnDestroy()
        {
            if (interactor) interactor.Interacted -= OnInteracted;
        }

        void OnInteracted(InteractionPoint p)
        {
            if (p.kind == "bag") { OpenBag(p); return; }

            var ctx = new ApartmentWorld.Context
            {
                Rules = session.Rules, G = session.State, Power = session.power, Fact = session.Fact,
            };
            var r = ApartmentWorld.Use(ctx, p.id, p.label);
            if (p.id == "homeWallet" && session.State.Knows("idTaken")) p.interactable = false; // the wallet leaves the table
            if (r.IsEmpty) r.Toast = p.label;
            Last = r;
            if (r.Lines.Count > 0) hud.Talk(r.Speaker, r.Role, r.Lines, () => hud.Toast(r.Toast, r.ToastSeconds));
            else hud.Toast(r.Toast, r.ToastSeconds);
        }

        /// <summary>The emergency bag scene (original doBag): pack, then judge the bag and say what is missing.</summary>
        public BagScreen OpenBag(InteractionPoint p = null)
        {
            var g = session.State;
            var db = session.Rules.Db;
            var items = BagDrill.BagItems(g, db, session.Fact);
            var o = BagHand.AddOptions(new Newtonsoft.Json.Linq.JObject(), g, Profile(), false);
            var profile = (string)o["profile"];
            var model = new ManualBagModel(items, db, BagAids.For(profile), BagHand.AidsFrom(o["aids"]), (string)o["layout"]);
            var tips = BagDrill.AlertTips.Select(session.Fact).Where(t => t != null).ToList();
            var facts = session.FactsData;
            return BagScreen.Open(model, new BagScreenOptions
            {
                Context = "alert", Mode = BagDrill.LadderStage(g, "bag"), LimitKg = db.bagLimitKg, LimitL = db.bagLimitL,
                Goals = BagDrill.Goals["alert"], Tips = tips, Profile = profile, Cite = id => facts?.Cite(id),
            }, result =>
            {
                BagHand.Remember(g, result.ToJson(), "alert");
                var (_, toast) = BagDrill.FinishAlertBag(session.Rules, g, result.Bag);
                Last = new WorldResponse { Toast = toast, ToastSeconds = 5f };
                SyncBagLabel();
                hud.Toast(toast, 5f);
            });
        }

        /// <summary>The experience mode the story runs with ("" when none or no story).</summary>
        static string Profile()
        {
            var r = FindFirstObjectByType<StoryRunner>();
            var p = r != null && r.Director != null ? r.Director.Profile : "";
            return p == "none" ? "" : p ?? "";
        }

        void SyncBagLabel()
        {
            if (!session || session.State == null) return;
            foreach (var p in InteractionPoint.All.Where(x => x.kind == "bag"))
                p.label = session.State.Knows("bagPacked") ? "비상가방 (꾸림)" : "비상가방 (비어 있음)";
        }
    }
}
