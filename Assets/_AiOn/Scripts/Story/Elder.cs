// Port of story/elder.js — 경보 속 딜레마: 304호 어르신 (design M2). On the way to the stairs during the air-raid alert,
// 어르신 stands at her door holding the rail. Three answers, none of them wrong:
//   help    – go down together (3 minutes to get ready, then the player's pace drops to hers until 1F)
//   manager – go first and tell 박 소장 at the 1F window, who goes up for her
//   go      – go first; 박 소장 brings her down later
// G.know.elderPick keeps the answer (endure's corridor fire and the shelter read it).
// Elder.CreateElderDilemma(ctx) -> ElderDilemma { Tick(dt), ManagerLine() }: call Tick from the alert roam, ManagerLine at the window.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    /// <summary>A breadcrumb of the player's trail (original frame).</summary>
    public readonly struct Crumb
    {
        public readonly double X, Y, Z;
        public Crumb(double x, double y, double z) { X = x; Y = y; Z = z; }
        public Crumb(double x, double z) : this(x, 0, z) { }
        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    public static class Elder
    {
        public const double CallR = 11, Behind = 1.1, Pace = 0.5;
        /// <summary>The follower keeps at least this far from the player (port fix: after a teleport she stood inside them).</summary>
        public const double MinGap = 0.8;

        /// <summary>Pure: a step (0.9 m) behind someone at (x, z) facing yaw (yaw 0 looks toward -z).</summary>
        public static (double x, double z) BehindOf(double x, double z, double yaw) => (x + Math.Sin(yaw) * 0.9, z + Math.Cos(yaw) * 0.9);

        /// <summary>JS semantics for loose know/rel values: a string's value (else null) and truthiness.</summary>
        internal static string Str(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;
        internal static bool Truthy(JToken t) => t != null && t.Type switch
        {
            JTokenType.Null or JTokenType.Undefined => false,
            JTokenType.Boolean => (bool)t,
            JTokenType.Integer or JTokenType.Float => (double)t != 0 && !double.IsNaN((double)t),
            JTokenType.String => !string.IsNullOrEmpty((string)t),
            _ => true,
        };

        /// <summary>Pure: where a follower walking the player's breadcrumb trail should head (the crumb about `behind` m back).</summary>
        public static Crumb? FollowTarget(IReadOnlyList<Crumb> crumbs, double behind = Behind)
        {
            if (crumbs == null || crumbs.Count == 0) return null;
            double d = 0;
            for (int i = crumbs.Count - 1; i > 0; i--)
            {
                double dx = crumbs[i].X - crumbs[i - 1].X, dz = crumbs[i].Z - crumbs[i - 1].Z;
                d += Math.Sqrt(dx * dx + dz * dz);
                if (d >= behind) return crumbs[i - 1];
            }
            return crumbs[0];
        }

        /// <summary>Pure: the elder dilemma counts as "brought down" for the later payoffs when helped or 박 소장 was told.</summary>
        public static bool ElderSafeEarly(IDictionary<string, JToken> know)
        {
            if (know == null) return false;
            return know.TryGetValue("elderPick", out var p) && Elder.Str(p) == "help" || know.TryGetValue("elderTold", out var t) && Elder.Truthy(t);
        }

        public static bool ElderSafeEarly(GameState g) => ElderSafeEarly(g?.Know);

        public static ElderDilemma CreateElderDilemma(StoryContext ctx) => new ElderDilemma(ctx);

        /// <summary>Where levels/apartment-corridor.js puts her: ELDER = { x: 15.45, y: F3, z: -0.4, yaw: Math.PI / 2 }.</summary>
        public const double HomeX = 15.45, HomeY = 5.6, HomeZ = -0.4, HomeYaw = Math.PI / 2;

        /// <summary>The apartment's walk mesh (L.walk of levels/apartment.js, as in apartment-level.json): floors and the stair ramps.</summary>
        public static readonly StoryCine.WalkZone[] ApartmentWalk =
        {
            new(-1, 9, -6.5, 0, 0),
            new(3.4, 6.6, 0.1, 1.6, 0),
            new(5.1, 6.6, 1.263, 3.463, 0, "z", 0, 1.4),
            new(3.4, 4.9, 1.537, 3.738, 0, "z", 2.8, 1.4),
            new(3.4, 6.6, 3.4, 5, 1.4),
            new(3.4, 6.6, 0.1, 1.6, 2.8),
            new(5.1, 6.6, 1.263, 3.463, 0, "z", 2.8, 4.2),
            new(3.4, 4.9, 1.537, 3.738, 0, "z", 5.6, 4.2),
            new(3.4, 6.6, 3.4, 5, 4.2),
            new(3.4, 6.6, 0.1, 1.6, 5.6),
            new(-16, 22, -2, 0, 5.6),
            new(-2.4, 0.3, 0, 1.3, 5.6),
            new(-7.6, -2.4, 0, 8, 5.75),
            new(-2.4, 0.3, 1.3, 8, 5.75),
            new(-7.6, 0.3, 8, 9.4, 5.67),
        };

        /// <summary>The walk zone under (x, z) at about feetY (the highest, as GroundAt picks), or -1.</summary>
        public static int ZoneAt(IReadOnlyList<StoryCine.WalkZone> walk, double x, double z, double feetY, double below = 0.6)
        {
            int best = -1; double by = double.NegativeInfinity;
            for (int i = 0; i < walk.Count; i++)
            {
                var w = walk[i];
                if (x < w.X0 - 0.02 || x > w.X1 + 0.02 || z < w.Z0 - 0.02 || z > w.Z1 + 0.02) continue;
                double y = w.Y;
                if (w.Axis != null)
                {
                    double span = w.Axis == "x" ? w.X1 - w.X0 : w.Z1 - w.Z0;
                    if (span == 0) span = 1;
                    double k = Math.Min(1, Math.Max(0, (w.Axis == "x" ? x - w.X0 : z - w.Z0) / span));
                    y = w.Y0 + (w.Y1 - w.Y0) * k;
                }
                if (y > feetY + 0.45 || y < feetY - below) continue;
                if (y > by) { by = y; best = i; }
            }
            return best;
        }

        static readonly double[] FollowTurns = { 0, 0.6, -0.6, 1.3, -1.3, 2.0, -2.0 };

        /// <summary>
        /// Where the follower comes through after a door or teleport: a step (0.9 m) behind the player, or, when that is
        /// off the walk mesh or on another walk zone (through the door or wall just passed), a step to one side.
        /// The plain spot behind when nothing fits.
        /// </summary>
        public static (double x, double z) FollowSpot(IReadOnlyList<StoryCine.WalkZone> walk, double px, double py, double pz, double yaw)
        {
            int here = ZoneAt(walk, px, pz, py);
            for (int pass = 0; pass < 2; pass++)
                foreach (var a in FollowTurns)
                {
                    double x = px + Math.Sin(yaw + a) * 0.9, z = pz + Math.Cos(yaw + a) * 0.9;
                    int zone = ZoneAt(walk, x, z, py);
                    if (zone >= 0 && (pass == 1 || here < 0 || zone == here)) return (x, z);
                }
            return BehindOf(px, pz, yaw);
        }

        /// <summary>engine/geom.js groundAt(walk, x, z, feetY, any = false, below): the highest surface within [feetY - below, feetY + 0.45], or null.</summary>
        public static double? GroundAt(IReadOnlyList<StoryCine.WalkZone> walk, double x, double z, double feetY, double below = 0.6)
        {
            double? best = null;
            foreach (var w in walk)
            {
                if (x < w.X0 - 0.02 || x > w.X1 + 0.02 || z < w.Z0 - 0.02 || z > w.Z1 + 0.02) continue;
                double y = w.Y;
                if (w.Axis != null)
                {
                    double span = w.Axis == "x" ? w.X1 - w.X0 : w.Z1 - w.Z0;
                    if (span == 0) span = 1;
                    double k = Math.Min(1, Math.Max(0, (w.Axis == "x" ? x - w.X0 : z - w.Z0) / span));
                    y = w.Y0 + (w.Y1 - w.Y0) * k;
                }
                if (y > feetY + 0.45 || y < feetY - below) continue;
                if (best == null || y > best) best = y;
            }
            return best;
        }

        /// <summary>Her model -> where the level put her (the follower moves her; a retry puts her back). The original's SPOT WeakMap
        /// ({ x, y, z, ry }). IStoryPerson has no Y/yaw getter; the first sight is the level's placement, so y/yaw are ELDER's.</summary>
        internal sealed class Spot { public double X, Y, Z, Yaw; }
        internal static readonly ConditionalWeakTable<IStoryPerson, Spot> Spots = new();
    }

    public sealed class ElderDilemma
    {
        readonly StoryContext ctx;
        bool asking, follow;
        List<Crumb> crumbs = new();
        string anim = "";
        double ny = Elder.HomeY; // her height: IStoryPerson has no Y getter, so the follower keeps it

        GameState G => ctx.G;
        IStoryEngine Engine => ctx.Engine;

        StoryPoint Point() => Engine.Points?.FirstOrDefault(p => p.Id == "elder304");
        IStoryPerson Npc() => Engine.LevelId == "apartment" ? Engine.Npc("elder") : null;

        internal ElderDilemma(StoryContext ctx)
        {
            this.ctx = ctx;
            var n = Npc();
            if (n != null)
            {
                if (Elder.Spots.TryGetValue(n, out var o)) { n.SetPosition(o.X, o.Y, o.Z, o.Yaw); ny = o.Y; n.SetAnim("idle"); }
                else Elder.Spots.Add(n, new Elder.Spot { X = n.X, Y = Elder.HomeY, Z = n.Z, Yaw = Elder.HomeYaw });
            }
        }

        public bool Following => follow;

        static async void Forget(Func<Task> run)
        {
            try { await run(); }
            catch (Exception) { /* the original logs '[elder] dilemma failed' and the alert goes on */ }
        }

        async Task Ask()
        {
            asking = true;
            try
            {
                // C1: two shots of her at the rail; the plain caption if the cutscene cannot play.
                string shown;
                try { shown = await Cutscenes.ElderScene(ctx); }
                catch (Exception) { shown = "failed"; } // '[cine] elder scene failed; the story goes on'
                if (shown != "done" && shown != "skipped") await ctx.Caption("계단 옆 304호 앞에서 어르신이 벽 손잡이를 붙들고 서 있다.", null, 2600);
                var pick = await ctx.Say("304호 어르신", new[] { "\"302호 정민 씨예요? 사이렌이 울리는데 계단이 캄캄해서… 혼자 내려가기가 겁나네요.\"" }, new List<TalkChoice>
                {
                    new("help", "부축해서 같이 내려간다 (시간이 더 걸린다)"),
                    new("manager", "1층 관리사무소에 알리고 먼저 간다"),
                    new("go", "먼저 내려간다"),
                }, "경보 중 · 3층 복도");
                G.SetKnow("elderPick", pick);
                GameRules.RecordEvent(G, "elder_choice", new JObject { ["pick"] = pick, ["minute"] = G.Minute });
                if (pick == "help")
                {
                    await ctx.Say("304호 어르신", new[] { "\"고마워요. 신발만 신을게요. 천천히 가요. 손잡이 쪽에 서 줘요.\"" }, null, "304호 앞");
                    ctx.Pass(3);
                    // A bag over the limit (a game number) is felt on the stairs: one hand for the rail, one for her, and the weight
                    // on the back. She is still helped down; it costs more (the 5장 lift is where there is no hand left at all).
                    var load = ctx.Rules.BagLoad(G);
                    if (G.Knows("bagOn") && load.OverWeight)
                    {
                        await ctx.Caption($"배낭 {BagDrill.FormatKg(load.WeightKg)}에 어깨가 짓눌린다. 한 손은 난간, 한 손은 어르신. 한 층마다 숨을 고른다. (가방 무게 — 게임 규칙)", null, 3600);
                        ctx.Stat("fatigue", 4); ctx.Pass(1);
                        GameRules.RecordEvent(G, "bag_heavy", new JObject { ["where"] = "stairs", ["weightKg"] = Math.Round(load.WeightKg, 2) });
                    }
                    ctx.Toast($"어르신 걸음에 맞춰 계단으로. {ctx.Facts.Text("people_ask")}", "official", 6000);
                    follow = true; crumbs = new List<Crumb>();
                    var p = Point(); if (p != null) p.Enabled = false;
                    ctx.SpeedOverride = Elder.Pace; ctx.Refresh();
                }
                else if (pick == "manager")
                {
                    await ctx.Say("304호 어르신", new[] { "\"그래요, 소장님한테 말 좀 해 줘요. 나는 여기 손잡이 잡고 있을게요.\"" }, null, "304호 앞");
                    ctx.Toast("1층 관리사무소 창구에서 박 소장에게 알리자.", "self", 4200);
                }
                else
                {
                    await ctx.Say("304호 어르신", new[] { "\"그래요, 어서 가요. 나는 소장님 올라오면 같이 갈게요.\"" }, null, "304호 앞");
                }
            }
            finally { asking = false; }
        }

        /// <summary>The follower: 어르신 walks the player's trail a step behind, at her own pace, down to 1F.</summary>
        void Walk(double dt)
        {
            var n = Npc();
            if (n == null) return;
            double px = Engine.PlayerX, py = Engine.PlayerY, pz = Engine.PlayerZ;
            Crumb? last = crumbs.Count > 0 ? crumbs[^1] : (Crumb?)null;
            if (last is Crumb l0 && Math.Sqrt(Sq(l0.X - px) + Sq(l0.Y - py) + Sq(l0.Z - pz)) > 3) { crumbs = new List<Crumb>(); last = null; } // a door or teleport: start over
            if (!(last is Crumb l) || Math.Sqrt(Sq(l.X - px) + Sq(l.Z - pz)) > 0.3 || Math.Abs(l.Y - py) > 0.3) crumbs.Add(new Crumb(px, py, pz));
            if (crumbs.Count > 400) crumbs.RemoveRange(0, 100);
            var tt = Elder.FollowTarget(crumbs);
            if (!(tt is Crumb t)) return;
            double gx = n.X, gz = n.Z;
            double dx = t.X - gx, dz = t.Z - gz, d = Math.Sqrt(dx * dx + dz * dz);
            if (d > 8 || Math.Abs(ny - py) > 3)
            {
                // A door or teleport in between: she comes through a step behind the player, never inside them.
                var (bx, bz) = Elder.FollowSpot(Elder.ApartmentWalk, px, py, pz, Engine.PlayerYaw);
                ny = py; n.SetPosition(bx, py, bz, Engine.PlayerYaw);
                return;
            }
            bool moving = false;
            double? yaw = null;
            if (d > 0.2)
            {
                var v = Math.Min(d * 2, 2.2) * dt;
                double nx = gx + dx / d * v, nz = gz + dz / d * v;
                // never closer than MinGap to the player (a short trail's first crumb is where the player stands)
                if (Math.Sqrt(Sq(nx - px) + Sq(nz - pz)) >= Elder.MinGap || Math.Sqrt(Sq(nx - px) + Sq(nz - pz)) > Math.Sqrt(Sq(gx - px) + Sq(gz - pz)))
                { gx = nx; gz = nz; yaw = Math.Atan2(-dx, -dz); moving = true; }
            }
            // She eases to the walk mesh under her (the trail's height where there is none), as the original does.
            var ground = Elder.GroundAt(Elder.ApartmentWalk, gx, gz, ny + 0.4, 1.2);
            ny += ((ground ?? t.Y) - ny) * Math.Min(1, dt * 10);
            n.SetPosition(gx, ny, gz, yaw);
            var want = moving ? "walk" : "idle";
            anim = want;
            // setAnim(want) + setSpeed(...) every frame: SetAnim with the clip already playing only sets its speed (people.js).
            n.SetAnim(want, moving ? Math.Min(d * 2, 1.2) : 0);
        }

        static double Sq(double x) => x * x;

        async Task Arrive(bool talk = true)
        {
            follow = false;
            ctx.SpeedOverride = null; ctx.Refresh();
            anim = "idle"; Npc()?.SetAnim("idle");
            // gone down; she stays in view until the next enter
            if (!ctx.LevelStates.TryGetValue("apartment", out var apt)) ctx.LevelStates["apartment"] = apt = new Dictionary<string, JToken>();
            apt["elder304"] = "shelter";
            ctx.SetLevelState("shelter", "elder", "on");
            GameRules.RecordEvent(G, "help", new JObject { ["who"] = "elder", ["what"] = "경보 속 캄캄한 계단에서 304호 어르신을 부축해 1층까지 함께 내려갔다" });
            if (!talk) { ctx.Toast("어르신은 1층에서 박 소장과 함께 천천히 오기로 했다.", "neighbor", 4200); return; }
            await ctx.Say("304호 어르신", new[] { "\"다 왔네. 여기서부터는 소장님이랑 천천히 갈게요. 고마워요. 어서 가요.\"" }, null, "1층 계단 앞");
        }

        public void Tick(double dt)
        {
            if (follow && Engine.LevelId != "apartment") { Forget(() => Arrive(false)); return; } // out of the building first
            if (asking || Engine.LevelId != "apartment") return;
            double px = Engine.PlayerX, py = Engine.PlayerY, pz = Engine.PlayerZ;
            if (!G.Knows("elderPick"))
            {
                var p = Point();
                if (p != null && p.Enabled && py > 4 && Math.Abs(py - p.Y) < 1.5 && Math.Sqrt(Sq(px - p.X) + Sq(pz - p.Z)) < Elder.CallR)
                    Forget(() => ctx.Modal(Ask));
                return;
            }
            if (!follow) return;
            Walk(dt);
            var n = Npc();
            if (py < 1 && n != null && ny < 1 && Math.Sqrt(Sq(n.X - px) + Sq(n.Z - pz)) < 3)
            {
                asking = true;
                Forget(async () => { try { await ctx.Modal(() => Arrive()); } finally { asking = false; } });
            }
        }

        /// <summary>At the 1F window: tell 박 소장 (once). Returns true when the line was said.</summary>
        public async Task<bool> ManagerLine()
        {
            if (Elder.Str(G.Know.TryGetValue("elderPick", out var pk) ? pk : null) != "manager" || G.Knows("elderTold")) return false;
            G.SetKnow("elderTold", true);
            await ctx.Ui.Talk(new TalkOptions { Speaker = "정민", Role = "1층 관리사무소", Lines = { "소장님, 304호 어르신이 3층 계단 앞에 계세요. 혼자 내려오시기 어려워 보여요." } });
            await ctx.Ui.Talk(new TalkOptions { Speaker = "박영수 소장", Role = "1층 관리사무소", Lines = { "알려 줘서 고마워요. 제가 바로 올라가서 모시고 갈게요. 정민 씨는 먼저 가요!" } });
            GameRules.RecordEvent(G, "help", new JObject { ["who"] = "manager", ["what"] = "정민의 말을 듣고 304호 어르신을 모시고 내려왔다" });
            ctx.SetLevelState("apartment", "elder304", "shelter");
            ctx.SetLevelState("shelter", "elder", "on");
            return true;
        }
    }
}
