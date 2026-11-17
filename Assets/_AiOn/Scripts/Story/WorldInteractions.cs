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
            var model = new BagPackingModel(items, db);
            var tips = BagDrill.AlertTips.Select(session.Fact).Where(t => t != null).ToList();
            return BagScreen.Open(model, "alert", BagDrill.LadderStage(g, "bag"), db.bagLimitKg, db.bagLimitL, BagDrill.Goals["alert"], tips, result =>
            {
                var (_, toast) = BagDrill.FinishAlertBag(session.Rules, g, result);
                Last = new WorldResponse { Toast = toast, ToastSeconds = 5f };
                SyncBagLabel();
                hud.Toast(toast, 5f);
            });
        }

        void SyncBagLabel()
        {
            if (!session || session.State == null) return;
            foreach (var p in InteractionPoint.All.Where(x => x.kind == "bag"))
                p.label = session.State.Knows("bagPacked") ? "비상가방 (꾸림)" : "비상가방 (비어 있음)";
        }
    }
}
