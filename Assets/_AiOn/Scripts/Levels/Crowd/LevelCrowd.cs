using System.Linq;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AiOn.Levels.Crowd
{
    /// <summary>
    /// A level's crowd in the scene: the pure logic (TownCrowd, ShelterPeople or MartShoppers) fed with the player,
    /// camera and power each frame, drawn by a CrowdView. Created by UnityStoryHost when a level is entered and gone
    /// with the level scene. Also keeps the level's own NPCs that follow the crowd in step: in town the outside clerk
    /// and the two shelter guides (town.js refreshNpcs; the guides also point / wave / idle on a 10 s cycle, or face and
    /// talk to a player within 7 m, as town.js update does), in the shelter the manager's talk / idle.
    /// </summary>
    public sealed class LevelCrowd : MonoBehaviour
    {
        ICrowdLogic _logic;
        CrowdView _view;
        UnityStoryHost _host;
        float _t0;
        IStoryPerson _clerkOutside, _guideC, _guideEast, _manager;
        string _managerAnim;
        readonly string[] _guideAnim = new string[2];
        double _nt;

        public ICrowdLogic Logic => _logic;
        public CrowdView View => _view;

        /// <summary>The crowd of a level, or null for levels without one (apartment, office, store).</summary>
        public static LevelCrowd Create(string levelId, LevelLayout layout, UnityStoryHost host)
        {
            ICrowdLogic logic;
            switch (levelId)
            {
                case "town":
                    logic = new TownCrowd(WalkHeight.FromLayout(layout, WalkHeight.Pick.Highest), SolidGrid.FromLayout(layout));
                    break;
                case "shelter":
                    logic = new ShelterPeople(WalkHeight.FromLayout(layout, WalkHeight.Pick.Lowest));
                    break;
                case "mart":
                    logic = new MartShoppers(SolidGrid.FromLayout(layout));
                    break;
                default:
                    return null;
            }
            if (layout == null) Debug.LogWarning($"[crowd] {levelId}: no level layout on the story host (AiOn/Wire Level Layouts); people walk without walls or heights");
            var go = new GameObject("Crowd");
            SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            var c = go.AddComponent<LevelCrowd>();
            c._logic = logic;
            c._host = host;
            c._t0 = Time.time;
            c._view = new CrowdView(go.transform, name => host ? host.peoplePrefabs.FirstOrDefault(p => p && p.name == name) : null);
            if (levelId == "town")
            {
                c._clerkOutside = host.Npc("clerkOutside"); c._guideC = host.Npc("guideC"); c._guideEast = host.Npc("guideEast");
            }
            else if (levelId == "shelter") c._manager = host.Npc("shelterManager");
            c.SyncNpcs();
            return c;
        }

        /// <summary>
        /// Makes the instances of every body the crowd has now. The host calls it after the level's remembered states
        /// (prepare) are applied and while the fade is still opaque, so the bodies the story asked for are the ones made.
        /// </summary>
        public void Prewarm()
        {
            if (_logic == null) return;
            _view.Prewarm(_logic.Bodies);
            var cam = _host && _host.cameraRig ? _host.cameraRig.cam : null;
            var viewer = cam ? cam.transform.position : _host && _host.player ? _host.player.transform.position : Vector3.zero;
            _view.Sync(_logic.Bodies, viewer, (float)CrowdTier.Current.ShadowM);
        }

        /// <summary>Takes a level state; true when the crowd listens to that key.</summary>
        public bool Apply(string key, JToken value)
        {
            if (_logic == null || !_logic.Apply(key, value)) return false;
            SyncNpcs();
            return true;
        }

        void SyncNpcs()
        {
            if (_logic is TownCrowd town)
            {
                if (_clerkOutside != null) _clerkOutside.Visible = town.ClerkOutsideVisible;
                if (_guideC != null) _guideC.Visible = town.GuideCVisible;
                if (_guideEast != null) _guideEast.Visible = town.GuideEastVisible;
            }
            else if (_logic is ShelterPeople shelter && _manager != null && shelter.ManagerAnim != _managerAnim)
            {
                _managerAnim = shelter.ManagerAnim;
                _manager.SetAnim(_managerAnim);
            }
        }

        void Update()
        {
            if (_logic == null) return;
            var player = _host ? _host.player : null;
            var cam = _host && _host.cameraRig ? _host.cameraRig.cam : null;
            Vector3 cp = cam ? cam.transform.position : player ? player.transform.position : Vector3.zero;
            Vector3 cf = cam ? cam.transform.forward : Vector3.forward;
            var env = EnvironmentController.Instance;
            var f = new CrowdFrame(Time.time - _t0, player, player ? player.transform.position.x : 0, player ? -player.transform.position.z : 0,
                cam, cp.x, -cp.z, cf.x, -cf.z, env == null || env.power, _host ? _host.Env : "day");
            _logic.Update(Time.deltaTime, f);
            _view.Sync(_logic.Bodies, cp, (float)CrowdTier.Current.ShadowM);
            if (_logic is TownCrowd) DriveGuides(Time.deltaTime, player);
        }

        // town.js update: localNpcs = [guideEast, guideC, clerkOutside]; the guides (k 0, 1) talk to a player within 7 m,
        // else point (3 s), wave (2 s) and idle on a 10 s cycle offset 3.1 s each. The clerk is left to the story.
        void DriveGuides(double dt, Component player)
        {
            _nt += dt;
            for (int k = 0; k < 2; k++)
            {
                var p = k == 0 ? _guideEast : _guideC;
                if (p == null || !p.Visible) { _guideAnim[k] = null; continue; }
                bool near = false;
                double px = 0, pz = 0;
                if (player)
                {
                    px = player.transform.position.x; pz = -player.transform.position.z;
                    double dx = px - p.X, dz = pz - p.Z;
                    near = dx * dx + dz * dz < 49;
                }
                if (near) p.LookAt(px, pz); else p.LookAt(null, null);
                double ph = (_nt + k * 3.1) % 10;
                string a = near ? "talk" : ph < 3 ? "point" : ph < 5 ? "wave" : "idle";
                if (a != _guideAnim[k]) { _guideAnim[k] = a; p.SetAnim(a); }
            }
        }
    }
}
