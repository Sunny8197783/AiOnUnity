// Port of story/danger.js — war in the streets: while the player roams the town after the war has started, shells fall
// (a falling whistle first, then the blast, debris, a crater and a fire), damaged buildings shed rubble onto the
// sidewalk, and desperate people ask for food. Crouching (C) and cover between you and a blast cut the damage.
// Competition upgrade: the stranger asking for food is a calm, non-physical encounter (LooterChoices / LooterOutcome):
// no punch, no chase, nobody hurt; the title's promise ("폭력이나 부상 장면은 나오지 않습니다") holds.
// How much of each depends on the chapter (Intensity). Rubble piles are solids you can climb over.
// ctx.Calm (the 서연 interlude) holds the whole layer: nothing new falls and nothing pending lands.
//
// Danger.Create(ctx, hurt, fx) -> DangerLayer { IsHostile, Trigger, Reset, CurrentLooter }; Reset also clears rubble,
// craters and fires. Danger.ShellAim(x, z, yaw, close) -> (X, Z): where a shell lands (pure, tested).
//
// What the original drew with three.js (flash light, fireball, shock ring, smoke puffs, craters, the rubble mesh and its
// collision box, fire/smoke fx, thrown debris) and what it read from the level geometry (ground height, solids for
// cover) is the host's: DangerFx. Its default does nothing and says the ground is flat (y 0) and nothing gives cover.
// The game rules (timers, damage, toasts, events, the looter's choices, the loot) are here.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    /// <summary>The scene side of the danger layer (original three.js meshes, engine.fx, actions.spawn, level solids).</summary>
    public class DangerFx
    {
        /// <summary>Ground height at (x, z) in the current level, or null where there is no walkable ground.</summary>
        public virtual double? GroundAt(double x, double z) => 0;
        /// <summary>Something solid between the blast at (x, z) and the player's body (sampled at 1 m height).</summary>
        public virtual bool Covered(double x, double z) => false;
        /// <summary>The flash, fireball, shock ring, the crater (scale crater) and the smoke billowing up. Returns the crater mark.</summary>
        public virtual object Blast(double x, double y, double z, double size, double crater) => null;
        /// <summary>A thrown piece (actions.spawn): brick | rock | can.</summary>
        public virtual void Debris(string kind, double x, double y, double z, double vx, double vy, double vz) { }
        /// <summary>A fire and its smoke column at (x, y, z) that burns for seconds. Returns a handle.</summary>
        public virtual object Ignite(double x, double y, double z, double seconds) => null;
        /// <summary>A burning fire's flame level and smoke density (0..1) this frame.</summary>
        public virtual void FireLevel(object fire, double flame, double smoke) { }
        public virtual void Extinguish(object fire) { }
        /// <summary>A rubble heap W x D (h high) at (cx, cz) and its solid box (x0..x1, z0..z1, y -0.5..h), with its dust cloud. Returns the mark.</summary>
        public virtual object Rubble(double cx, double cz, double w, double d, double h) => null;
        /// <summary>Removes a crater or rubble mark (and the rubble's solid).</summary>
        public virtual void Clear(object mark) { }
        /// <summary>All smoke puffs out.</summary>
        public virtual void ResetPuffs() { }
        /// <summary>The spawned body still stands in the scene (a level switch destroys it with the level).</summary>
        public virtual bool Alive(IStoryPerson p) => p != null;
        /// <summary>The danger layer these effects serve (set by Danger.Create): the hands ask it about the looter.</summary>
        public DangerLayer Layer { get; internal set; }
    }

    public sealed class DangerIntensity
    {
        public double Shell, Collapse, Looter;
        public DangerIntensity(double shell, double collapse, double looter) { Shell = shell; Collapse = collapse; Looter = looter; }
    }

    public static class Danger
    {
        /// <summary>shell / collapse / looter: chance weights 0..1 per chapter (null = peace).</summary>
        public static readonly IReadOnlyDictionary<string, Func<GameState, DangerIntensity>> Intensity = new Dictionary<string, Func<GameState, DangerIntensity>>
        {
            ["dday"] = g => g.Knows("strikeSeen") ? new DangerIntensity(0.8, 0.5, 0) : null,
            ["endure"] = _ => new DangerIntensity(0.3, 0.3, 0.7),
            ["crossroads"] = _ => new DangerIntensity(0.6, 0.5, 0.5),
            ["evac"] = _ => new DangerIntensity(0.6, 0.5, 0.6),
        };

        public static readonly string[] Food = { "water_2l", "water_500", "canned", "bars", "rice_instant", "ramen" };

        public static readonly string[][] LooterLines =
        {
            new[] { "저기요, 잠깐만요. 가방에 먹을 거 있죠?", "물이랑 먹을 거 좀 나눠 줘요. 우리도 살아야 할 거 아니에요." },
            new[] { "애들이 사흘째 제대로 못 먹었어요…", "먹을 거 좀 나눠 줘요. 부탁이에요." },
        };

        /// <summary>How each variant sounds (the talk's subtitle), by LooterLines index.</summary>
        public static readonly string[] LooterCues = { "목소리가 날카롭다", "지쳐 보인다" };

        /// <summary>The stranger stops this far away to talk (m): a conversation, not a crowding.</summary>
        public const double TalkDistance = 3.2;

        /// <summary>A stranger who has not reached talking distance after this long (s), or is this far behind (m), gives up and walks off.</summary>
        public const double ApproachGiveUpSeconds = 20, ApproachGiveUpDistance = 22;
        public const string GaveUpLine = "그는 멈춰 서더니 다른 쪽으로 걸어갔다.";

        /// <summary>
        /// Port change (competition upgrade, non-violent): the original's refuse led to a fist fight. Now every answer is
        /// words. give: share food (karma +1). back: decline politely and step back. neighbors: ask people nearby to stand
        /// with you. report: call 112 (needs a charged phone). argue: raise your voice (the bad choice: tension, and in the
        /// end one food item handed over anyway). Nobody is hurt, whatever is picked.
        /// </summary>
        public static List<TalkChoice> LooterChoices(bool haveFood, bool phoneCharged)
        {
            var list = new List<TalkChoice>();
            if (haveFood) list.Add(new TalkChoice("give", "가방에서 먹을 것을 나눈다"));
            list.Add(new TalkChoice("back", "정중히 거절하고 물러선다"));
            list.Add(new TalkChoice("neighbors", "근처 주민에게 함께 있어 달라고 부탁한다"));
            list.Add(new TalkChoice("report", "112에 신고한다", !phoneCharged, phoneCharged ? null : "휴대폰 배터리가 없다")); // the overlay shows a Why even on an open choice
            list.Add(new TalkChoice("argue", "언성을 높여 따진다"));
            return list;
        }

        public sealed class LooterResult
        {
            /// <summary>The event's result (the deed table reads it): gave | escaped | argued.</summary>
            public string Result;
            /// <summary>What was picked: give | back | neighbors | report | argue.</summary>
            public string How;
            public string Line;
            public int Tension;
            public double Karma;
        }

        public const string ArguedEmptyLine = "언성이 높아지자 사람들이 쳐다본다. 가진 게 없다는 걸 확인하고서야 그가 돌아섰다. 손이 떨린다.";

        /// <summary>What an answer leads to (pure; a null or unknown pick counts as stepping back).</summary>
        public static LooterResult LooterOutcome(string pick) => pick switch
        {
            "give" => new LooterResult { Result = "gave", How = "give", Line = "먹을 것을 건넸다. 그는 고개를 숙이고 골목으로 사라졌다.", Karma = 1 },
            "neighbors" => new LooterResult { Result = "escaped", How = "neighbors", Line = "근처를 지나던 주민 두 사람이 곁에 섰다. 그는 한숨을 쉬고 돌아섰다.", Tension = -2 },
            "report" => new LooterResult { Result = "escaped", How = "report", Line = "112에 지금 있는 곳과 상황을 차분히 알렸다. 그는 통화하는 소리를 듣고 자리를 떴다.", Tension = -2 },
            "argue" => new LooterResult { Result = "argued", How = "argue", Line = "언성이 높아지자 사람들이 쳐다본다. 결국 먹을 것 하나를 내주고서야 그가 물러났다. 손이 떨린다.", Tension = 12, Karma = -1 },
            _ => new LooterResult { Result = "escaped", How = "back", Line = "“미안해요, 나눌 게 없어요.” 거리를 두고 천천히 물러섰다. 그는 따라오지 않았다.", Tension = 4 },
        };

        /// <summary>Math.random stand-in. Tests may pin it.</summary>
        public static Func<double> Random = new System.Random().NextDouble;

        static double Rnd(Func<double> r, double a, double b) => a + r() * (b - a);

        /// <summary>
        /// Where a shell lands around the player (x, z, yaw). Mostly in front of the camera, so it is seen (yaw 0 looks
        /// toward -z). A close one lands off to the side (1.2-2 rad from the facing, 5-8 m): it hurts, but a player who
        /// keeps walking the 2.3 s until the blast never walks into its 4 m core.
        /// </summary>
        public static (double X, double Z) ShellAim(double x, double z, double yaw, bool close, Func<double> random = null)
        {
            var r0 = random ?? Random;
            double a = close ? yaw + (r0() < 0.5 ? -1 : 1) * Rnd(r0, 1.2, 2.0) : (r0() < 0.75 ? yaw + Rnd(r0, -0.9, 0.9) : r0() * Math.PI * 2);
            double r = close ? Rnd(r0, 5, 8) : Rnd(r0, 9, 32);
            return (x - Math.Sin(a) * r, z - Math.Cos(a) * r);
        }

        /// <summary>The bearing (original-frame yaw) the looter walks in from: ahead or to the side of the facing, never behind (r 0..1).</summary>
        public static double LooterBearing(double yaw, double r) => yaw + (r - 0.5) * 2 * 0.7;

        /// <summary>createDanger({ ctx, actions, hurt }): hurt(n, why) takes health off (the director's hurt).</summary>
        public static DangerLayer Create(StoryContext ctx, Action<int, string> hurt, DangerFx fx = null) => new DangerLayer(ctx, hurt, fx ?? new DangerFx());
    }

    public sealed class DangerLayer : IDisposable
    {
        sealed class Burning { public object Fire; public double X, Z, T, BurnT; public string Level; }
        sealed class Pending { public double T; public Action Run; }
        sealed class Looter
        {
            public IStoryPerson P; public string Level; public string State = "approach"; public int Talk;
            public double X, Y, Z, T; public string Anim;
        }

        readonly StoryContext _ctx;
        readonly Action<int, string> _hurt;
        readonly DangerFx _fx;
        readonly List<Burning> _burning = new();
        readonly List<object> _marks = new();
        readonly List<Pending> _pending = new();
        readonly HashSet<IStoryPerson> _hostile = new();
        double _shellT = 20, _collapseT = 60, _looterT = 45;
        Looter _looter;

        IStoryEngine Engine => _ctx.Engine;
        double R() => Danger.Random();
        double Rnd(double a, double b) => a + R() * (b - a);

        internal DangerLayer(StoryContext ctx, Action<int, string> hurt, DangerFx fx)
        {
            _ctx = ctx; _hurt = hurt ?? ((n, why) => ctx.Stat("health", -n)); _fx = fx; _fx.Layer = this;
            Engine.Frame += OnFrame;
        }

        public void Dispose() => Engine.Frame -= OnFrame;

        DangerIntensity CurrentIntensity()
        {
            var g = _ctx.G;
            if (_ctx.Calm || g == null || g.Chapter == null || !Danger.Intensity.TryGetValue(g.Chapter, out var f)) return null;
            return f(g);
        }
        bool Active() => _ctx.Roaming && Engine.ControlsEnabled && Engine.LevelId == "town" && !_ctx.Calm;
        void Later(double t, Action run) => _pending.Add(new Pending { T = t, Run = run });
        double Dist(double x, double z) => Math.Sqrt((Engine.PlayerX - x) * (Engine.PlayerX - x) + (Engine.PlayerZ - z) * (Engine.PlayerZ - z));

        int Damage(double x, double z, double[] radii, string why)
        {
            double d = Dist(x, z);
            double b = d < radii[0] ? 85 : d < radii[1] ? 40 : d < radii[2] ? 15 : 0;
            if (b == 0) return 0;
            int n = (int)Num.JsRound(b * (Engine.Crouching ? 0.4 : 1) * (_fx.Covered(x, z) ? 0.3 : 1));
            _hurt(n, why);
            return n;
        }

        void Ignite(double x, double y, double z, double seconds)
        {
            try { _burning.Add(new Burning { Fire = _fx.Ignite(x, y, z, seconds), X = x, Z = z, T = seconds, Level = Engine.LevelId }); }
            catch (Exception) { /* '[danger] fire fx failed' */ }
        }

        // ---- shells ----
        void Shell(DangerIntensity I)
        {
            bool close = R() < 0.18 * I.Shell;
            var (x, z) = Danger.ShellAim(Engine.PlayerX, Engine.PlayerZ, Engine.PlayerYaw, close, Danger.Random);
            _ctx.Audio.Cue("hit", new JObject { ["name"] = "whistle", ["seconds"] = 2.3 });
            _ctx.Toast(close ? "휘이이익— 바로 머리 위다! 엎드려! (C)" : "휘이익— 포탄 낙하음!", "site", 2600);
            Later(2.3, () => Blast(x, z));
        }

        /// <summary>harm false: the sight and sound only (cutscenes); size scales the fireball.</summary>
        void Blast(double x, double z, bool harm = true, double size = 1)
        {
            if (Engine.LevelId != "town") return;
            double y = _fx.GroundAt(x, z) ?? 0;
            double d = Dist(x, z);
            _ctx.Audio.Cue("boom", new JObject { ["distance"] = Math.Min(1, Math.Max(0.3, d / 40)) });
            Engine.Shake(Math.Max(0.04, 0.6 - d * 0.02), 1.2);
            var crater = _fx.Blast(x, y, z, size, Rnd(1.4, 2.2));
            if (crater != null) _marks.Add(crater);
            for (int i = 0; i < 8; i++)
            {
                double a = R() * 6.28, v = Rnd(3, 8);
                _fx.Debris(R() < 0.5 ? "brick" : "rock", x, y + 0.4, z, Math.Cos(a) * v, Rnd(4, 9), Math.Sin(a) * v);
            }
            if (R() < 0.6) Ignite(x, y, z, Rnd(25, 50)); // often a fire that keeps burning
            if (!harm) return;
            int n = Damage(x, z, new double[] { 4, 9, 16 }, "blast");
            if (n >= 30) _ctx.Toast("폭풍에 밀려 바닥에 주저앉았다. 귀가 먹먹하다.", "self", 4200);
            else if (n > 0) _ctx.Toast("가까운 폭발에 몸이 휘청였다. 몸을 낮추자.", "self", 3600);
            _ctx.Stat("tension", 8);
            _ctx.Event("blast", new JObject { ["near"] = Num.JsRound(d), ["dmg"] = n });
        }

        // ---- collapse: the nearest damaged facade sheds a rubble pile onto its sidewalk ----
        void Collapse()
        {
            double px = Engine.PlayerX, pz = Engine.PlayerZ;
            var pick = TownMap.Buildings.Where(b => b.Floors >= 2).Select(b =>
            {
                double x = Math.Min(Math.Max(px, b.Rect.X0 + 3), b.Rect.X1 - 3), z = Math.Min(Math.Max(pz, b.Rect.Z0 + 3), b.Rect.Z1 - 3);
                return (b, x, z, d: Math.Sqrt((px - x) * (px - x) + (pz - z) * (pz - z)));
            }).Where(o => o.d > 5 && o.d < 35).OrderBy(o => o.d).Cast<(TownBuilding b, double x, double z, double d)?>().FirstOrDefault();
            if (pick == null) return;
            var B = pick.Value.b.Rect;
            // the face toward the player
            var faces = new List<(string f, double v)> { ("n", B.Z0 - pz), ("s", pz - B.Z1), ("w", B.X0 - px), ("e", px - B.X1) };
            var f = faces.OrderByDescending(o => o.v).First().f; // stable, like Array.sort
            bool along = f == "n" || f == "s";
            double cx = along ? Math.Min(Math.Max(px, B.X0 + 3), B.X1 - 3) : f == "w" ? B.X0 - 2 : B.X1 + 2;
            double cz = along ? (f == "n" ? B.Z0 - 2 : B.Z1 + 2) : Math.Min(Math.Max(pz, B.Z0 + 3), B.Z1 - 3);
            // never on a door or anything else you interact with: slide along the face, or give up
            bool Blocks(double x, double z) => (Engine.Points ?? Array.Empty<StoryPoint>()).Any(q => Math.Sqrt((q.X - x) * (q.X - x) + (q.Z - z) * (q.Z - z)) < 6);
            foreach (var off in new double[] { 0, 8, -8, 14, -14 })
            {
                double tx = along ? cx + off : cx, tz = along ? cz : cz + off;
                if (!Blocks(tx, tz) && (along ? tx > B.X0 + 2 && tx < B.X1 - 2 : tz > B.Z0 + 2 && tz < B.Z1 - 2)) { cx = tx; cz = tz; break; }
                if (off == -14) return;
            }
            _ctx.Audio.Cue("hit", new JObject { ["name"] = "collapse" });
            _ctx.Toast("우르르— 금 간 건물 벽에서 콘크리트가 떨어지기 시작한다! 벽에서 떨어져!", "site", 3400);
            Engine.Shake(0.08, 2.5);
            Later(3, () =>
            {
                if (Engine.LevelId != "town") return;
                double W = along ? 6 : 4, D = along ? 4 : 6, h = 1.4;
                // ponytail: runtime-only rubble (not in the save); put it in LevelStates if it should outlive a reload
                var mark = _fx.Rubble(cx, cz, W, D, h);
                if (mark != null) _marks.Add(mark);
                _ctx.Audio.Cue("boom", new JObject { ["distance"] = 0.5 }); Engine.Shake(0.25, 1);
                if (Math.Abs(Engine.PlayerX - cx) < W / 2 + 0.4 && Math.Abs(Engine.PlayerZ - cz) < D / 2 + 0.4)
                {
                    _hurt(Engine.Crouching ? 30 : 55, "debris");
                    Engine.TeleportTo(Engine.PlayerX, Engine.PlayerY, cz + (Engine.PlayerZ > cz ? 1 : -1) * (D / 2 + 0.6), Engine.PlayerYaw);
                    _ctx.Toast("쏟아진 잔해를 겨우 피했다. 먼지에 숨이 막힌다.", "self", 4200);
                }
                _ctx.Event("collapse", new JObject { ["x"] = Num.JsRound(cx), ["z"] = Num.JsRound(cz) });
            });
        }

        // ---- a stranger who wants food: walks up and asks; the answer is words, never a fight ----
        void SpawnLooter()
        {
            // Port upgrade: the original came from behind (yaw + pi); here it walks in from ahead or the side, in view, so
            // the approach is seen before the talk opens.
            double a = Danger.LooterBearing(Engine.PlayerYaw, R()), x = Engine.PlayerX - Math.Sin(a) * 14, z = Engine.PlayerZ - Math.Cos(a) * 14;
            var gy = _fx.GroundAt(x, z);
            if (gy == null) return;
            var p = Engine.SpawnPerson("crowd", x, gy.Value, z, a + Math.PI); // facing the player
            if (p == null) return;
            _hostile.Add(p);
            _looter = new Looter { P = p, Level = Engine.LevelId, Talk = (int)Math.Floor(R() * Danger.LooterLines.Length), X = x, Y = gy.Value, Z = z };
            SetAnim("walk");
        }

        void SetAnim(string anim) { if (_looter == null) return; _looter.Anim = anim; _looter.P.SetAnim(anim); }

        static double Karma(GameState g) => g.Extra.TryGetValue("karma", out var k) && k != null && k.Type is JTokenType.Integer or JTokenType.Float ? (double)k : 0;

        /// <summary>The food the bag on your back holds (no bag on, nothing to hand over).</summary>
        static List<string> FoodInBag(GameState g) => g.Knows("bagOn") ? Danger.Food.Where(id => g.Items.TryGetValue(id, out var e) && e != null && e.Bag > 0).ToList() : new List<string>();

        async Task Confront()
        {
            var l = _looter;
            l.State = "talking";
            var g = _ctx.G;
            var have = FoodInBag(g);
            SetAnim("talk"); l.P.LookAt(Engine.PlayerX, Engine.PlayerZ);
            var pick = await _ctx.Say("낯선 사람", Danger.LooterLines[l.Talk], Danger.LooterChoices(have.Count > 0, g.Phone.Battery > 0), Danger.LooterCues[l.Talk]);
            if (_looter == null) return;
            var outcome = Danger.LooterOutcome(pick);
            switch (outcome.Result == "argued" ? "argued" : outcome.How)
            {
                case "give":
                    foreach (var id in have.Take(2)) _ctx.Rules.Consume(g, id, 1, "bag"); // keeps pocket <= bag (a save that loads) and counts it used
                    break;
                case "report":
                    StoryCommon.DrainBattery(g, 2);
                    var fact = _ctx.Facts?.Text("comms_112"); // 긴급전화는 통신이 어려워도 연결된다
                    if (!string.IsNullOrEmpty(fact)) _ctx.After(1.2, () => _ctx.Toast(fact, "official", 5200));
                    break;
                case "argued":
                    if (have.Count > 0) _ctx.Rules.Consume(g, have[0], 1, "bag"); // handed over in the end, to end it
                    break;
            }
            if (outcome.Tension != 0) _ctx.Stat("tension", outcome.Tension);
            if (outcome.Karma != 0) g.Extra["karma"] = Karma(g) + outcome.Karma;
            _ctx.Toast(outcome.Result == "argued" && have.Count == 0 ? Danger.ArguedEmptyLine : outcome.Line, "self", 4600);
            _ctx.Event("looter", new JObject { ["result"] = outcome.Result, ["how"] = outcome.How });
            _ctx.Refresh();
            _looter.State = "leave";
        }

        void LooterTick(double dt)
        {
            var l = _looter;
            if (Engine.LevelId != l.Level) { DropLooter(); return; }
            double dx = Engine.PlayerX - l.X, dz = Engine.PlayerZ - l.Z, d = Math.Sqrt(dx * dx + dz * dz);
            void Step(double v, bool away = false)
            {
                double k = (away ? -1 : 1) * v * dt / (d == 0 ? 1 : d);
                l.X += dx * k; l.Z += dz * k;
                var gy = _fx.GroundAt(l.X, l.Z); if (gy != null) l.Y = gy.Value;
                l.P.SetPosition(l.X, l.Y, l.Z, Math.Atan2(away ? dx : -dx, away ? dz : -dz));
            }
            if (l.State == "leave") { if (l.Anim != "walk") SetAnim("walk"); Step(1.3, true); if (d > 30) DropLooter(); return; }
            // stops at a talking distance (never walks into the player's space) and asks
            if (l.State == "approach")
            {
                l.T += dt;
                if (d < Danger.TalkDistance) _ = Confront();
                else if (l.T > Danger.ApproachGiveUpSeconds || d > Danger.ApproachGiveUpDistance)
                {
                    // the player walked on: he does not follow them across town
                    l.State = "leave";
                    _ctx.Toast(Danger.GaveUpLine, "site", 3200);
                    _ctx.Event("looter", new JObject { ["result"] = "left", ["how"] = "walked_on" });
                }
                else { if (l.Anim != "walk") SetAnim("walk"); Step(1.4); }
                return;
            }
        }

        void DropLooter()
        {
            if (_looter == null) return;
            Engine.RemovePerson(_looter.P);
            _looter = null;
        }

        void OnFrame(double dt)
        {
            // effects keep running (fires burn down) even while a menu is open
            for (int i = _burning.Count - 1; i >= 0; i--)
            {
                var b = _burning[i];
                b.T -= dt;
                try { _fx.FireLevel(b.Fire, Math.Min(1, b.T / 10), Math.Min(1, Math.Max(0, b.T + 12) / 15)); } catch (Exception) { /* fx stand-in */ }
                if (b.T <= -12) { _fx.Extinguish(b.Fire); _burning.RemoveAt(i); continue; }
                if (b.T > 3 && b.Level == Engine.LevelId && Active() && Dist(b.X, b.Z) < 1.3)
                {
                    b.BurnT -= dt;
                    if (b.BurnT <= 0) { b.BurnT = 0.5; _hurt(4, "fire"); _ctx.Toast("뜨겁다! 불길에서 떨어져!", "self", 1500); }
                }
            }
            if (_ctx.Calm) { _pending.Clear(); if (_looter != null) DropLooter(); }
            // The looter's body dies with its level: drop it even while the player is indoors (Active() is town-only), or
            // a bodiless '낯선 사람' would walk back in and fight on the player's return.
            if (_looter != null && (Engine.LevelId != _looter.Level || !_fx.Alive(_looter.P))) DropLooter();
            if (!Active()) return;
            for (int i = _pending.Count - 1; i >= 0; i--)
                if (i < _pending.Count && (_pending[i].T -= dt) <= 0) { var p = _pending[i]; _pending.RemoveAt(i); p.Run(); }
            if (_looter != null) LooterTick(dt);
            var I = CurrentIntensity();
            if (I == null) return;
            if (I.Shell > 0 && (_shellT -= dt * I.Shell) <= 0) { _shellT = Rnd(25, 60); Shell(I); }
            if (I.Collapse > 0 && (_collapseT -= dt * I.Collapse) <= 0) { _collapseT = Rnd(70, 140); Collapse(); }
            if (I.Looter > 0 && _looter == null && (_looterT -= dt * I.Looter) <= 0) { _looterT = Rnd(90, 180); SpawnLooter(); }
        }

        /// <summary>The host switched levels (its level-change path): whatever the old level held is gone.</summary>
        public void LevelChanged()
        {
            if (_looter != null && Engine.LevelId != _looter.Level) DropLooter();
            _hostile.RemoveWhere(p => !_fx.Alive(p));
        }

        /// <summary>The stranger the layer spawned (kept for the host's talk facing; nothing hits anyone any more).</summary>
        public bool IsHostile(IStoryPerson p) => p != null && _hostile.Contains(p);

        /// <summary>The current stranger (null when none), for the talk camera.</summary>
        public IStoryPerson CurrentLooter => _looter?.P;

        /// <summary>Force an event now (dev panel / tests): blast | boom | shell | collapse | looter.</summary>
        public void Trigger(string kind, double x = 0, double z = 0, double size = 1)
        {
            var I = CurrentIntensity() ?? new DangerIntensity(1, 0, 0);
            if (kind == "blast") Blast(x, z);
            else if (kind == "boom") Blast(x, z, false, size);
            else if (kind == "shell") Shell(I);
            else if (kind == "collapse") Collapse();
            else if (kind == "looter" && _looter == null) SpawnLooter();
        }

        /// <summary>A retry or a new run: no looter, nothing pending, and the street as built.</summary>
        public void Reset()
        {
            DropLooter(); _pending.Clear(); _shellT = 20; _collapseT = 60; _looterT = 45;
            foreach (var m in _marks) _fx.Clear(m);
            _marks.Clear();
            foreach (var b in _burning) { try { _fx.Extinguish(b.Fire); } catch (Exception) { /* fx stand-in */ } }
            _burning.Clear();
            _fx.ResetPuffs();
        }
    }
}
