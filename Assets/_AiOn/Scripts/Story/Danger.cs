// Port of story/danger.js — war in the streets: while the player roams the town after the war has started, shells fall
// (a falling whistle first, then the blast, debris, a crater and a fire), damaged buildings shed rubble onto the
// sidewalk, and desperate people demand food. Crouching (C) and cover between you and a blast cut the damage.
// How much of each depends on the chapter (Intensity). Rubble piles are solids you can climb over.
// ctx.Calm (the 서연 interlude) holds the whole layer: nothing new falls and nothing pending lands.
//
// Danger.Create(ctx, hurt, fx) -> DangerLayer { IsHostile, Trigger, Reset, HitLooter }; Reset also clears rubble,
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
        /// <summary>A player reaction (engine.act): 'hurt' for seconds.</summary>
        public virtual void PlayerAct(string act, double seconds) { }
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
            new[] { "야, 거기. 가방 내려놔.", "물이랑 먹을 거 있지? 다 알아. 순순히 내놔." },
            new[] { "애들이 사흘째 굶었어요…", "먹을 거 좀 나눠 줘요. 안 주면… 나도 어쩔 수 없어." },
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
            public IStoryPerson P; public string Level; public int Hp = 4; public string State = "approach"; public double HitT; public int Talk; public bool Down;
            public double? Gone; public double X, Y, Z; public string Anim;
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
            if (n >= 30) _ctx.Toast("폭풍에 날아가 바닥을 굴렀다. 귀가 먹먹하다.", "self", 4200);
            else if (n > 0) _ctx.Toast("파편이 스쳤다. 피가 흐른다.", "self", 3600);
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
                    _ctx.Toast("쏟아진 잔해에 깔릴 뻔했다. 온몸이 욱신거린다.", "self", 4200);
                }
                _ctx.Event("collapse", new JObject { ["x"] = Num.JsRound(cx), ["z"] = Num.JsRound(cz) });
            });
        }

        // ---- a looter: walks up, asks; give, refuse (a fight) or run ----
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

        /// <summary>The player hits back (the original's p.onHit, called by actions.js instead of its own reaction).</summary>
        public void HitLooter(IStoryPerson p, bool ko)
        {
            var l = _looter;
            if (l == null || l.P != p || l.Down) return;
            l.Hp -= ko ? 2 : 1; l.State = "fight"; SetAnim("hurt"); l.HitT = 0.9;
            if (l.Hp <= 0) { l.Down = true; l.State = "down"; SetAnim("down"); DropLoot(l); }
        }

        void DropLoot(Looter l)
        {
            _fx.Debris("can", l.X, l.Y + 0.5, l.Z, Rnd(-1, 1), 2, Rnd(-1, 1));
            var g = _ctx.G;
            if (!g.Items.TryGetValue("canned", out var e) || e == null) g.Items["canned"] = e = new ItemEntry();
            if (g.Knows("bagOn")) e.Bag += 1; else e.Home += 1; // the bag may be sitting at home
            _ctx.Toast("상대가 쓰러졌다. 떨어뜨린 통조림 하나를 챙겼다.", "self", 4200);
            _ctx.Event("looter", new JObject { ["result"] = "fought" });
        }

        async Task Confront()
        {
            var l = _looter;
            l.State = "talking";
            var g = _ctx.G;
            var have = g.Knows("bagOn") ? Danger.Food.Where(id => g.Items.TryGetValue(id, out var e) && e != null && e.Bag > 0).ToList() : new List<string>(); // no bag on your back, nothing to hand over
            SetAnim("talk"); l.P.LookAt(Engine.PlayerX, Engine.PlayerZ);
            var choices = new List<TalkChoice>();
            if (have.Count > 0) choices.Add(new TalkChoice("give", "가방에서 먹을 것을 내준다"));
            choices.Add(new TalkChoice("refuse", "거절한다"));
            choices.Add(new TalkChoice("run", "등을 돌려 뛴다"));
            var pick = await _ctx.Say("낯선 사람", Danger.LooterLines[l.Talk], choices, "눈빛이 심상치 않다");
            if (_looter == null) return;
            if (pick == "give")
            {
                foreach (var id in have.Take(2)) _ctx.Rules.Consume(g, id, 1, "bag"); // keeps pocket <= bag (a save that loads) and counts it used
                _ctx.Toast("먹을 것을 건넸다. 그는 말없이 골목으로 사라졌다.", "self", 4200);
                _ctx.Event("looter", new JObject { ["result"] = "gave" });
                g.Extra["karma"] = (g.Extra.TryGetValue("karma", out var k) && k != null && k.Type is JTokenType.Integer or JTokenType.Float ? (double)k : 0) + 1;
                _looter.State = "leave";
            }
            else
            {
                _looter.State = pick == "run" ? "chase" : "fight";
                if (pick == "refuse") _ctx.Toast("“좋게 말할 때 내놨어야지.” 그가 주먹을 쥔다. (클릭/Q로 맞서기, 멀리 뛰면 포기한다)", "site", 5000);
            }
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
            l.HitT = Math.Max(0, l.HitT - dt);
            if (l.State == "down") { if ((l.Gone = (l.Gone ?? 15) - dt) <= 0) DropLooter(); return; }
            if (l.State == "leave") { if (l.Anim != "walk") SetAnim("walk"); Step(1.4, true); if (d > 30) DropLooter(); return; }
            if (l.State == "approach") { if (d < 2.6) _ = Confront(); else { if (l.Anim != "walk") SetAnim("walk"); Step(1.6); } return; }
            if (l.State == "fight" || l.State == "chase")
            {
                if (d > 22) { _ctx.Toast("뒤쫓던 사람이 포기하고 멈춰 섰다.", "self"); l.State = "leave"; _ctx.Event("looter", new JObject { ["result"] = "escaped" }); return; }
                if (l.HitT > 0) return;
                if (d > 1.1) { if (l.Anim != "run") SetAnim("run"); Step(l.State == "chase" ? 4.4 : 3.4); return; }
                l.P.Play("punch"); l.P.LookAt(Engine.PlayerX, Engine.PlayerZ); l.HitT = 1.3;
                _ctx.Audio.Cue("hit", new JObject { ["name"] = "punch" }); _fx.PlayerAct("hurt", 0.35); Engine.Shake(0.1, 0.3);
                _hurt((int)Num.JsRound(Rnd(7, 12)), "fight");
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

        /// <summary>A looter or anyone else the layer made hostile: engine/actions hit them, the app does not count it as violence.</summary>
        public bool IsHostile(IStoryPerson p) => p != null && _hostile.Contains(p);

        /// <summary>The current looter (null when none), for the host's hit test.</summary>
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
