// 3D 소화기 (competition upgrade): the story side of the first-person extinguisher, and the 1장 pressure-gauge check.
//
// Run3D: a roam with the controls on (the way Endure's retreat runs the smoke escape inside a handler), the extinguisher
// in hand (HeldExtinguisher, built by the Unity host through View), the rules ticked here (HeldExtinguisherLogic), the
// steps on the objective panel (guided), the logic's lines as toasts. It returns the 2D screen's result shape
// ({ sequence, result } plus the 3D details) so the chapters score it with Drills.EvalExtinguisher as before, records an
// "ext_use" event (the learning report reads it) and puts view, controls and grade back as they were.
//
// The 2D "extinguisher" screen stays the alternative: no host view (the tests' FakeStoryHost), the low-vision or the
// mobility profile, or the reduce-motion setting (Use3D). The chapters keep their original 2D call for that path.
//
// CheckGauge (1장, 집 장비 점검): the "extgauge" screen (the dial with the needle a little left of green); telling the
// management office gets the corridor extinguisher replaced (know extGauge = reported); otherwise it stays the low one.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class ExtinguisherScene
    {
        /// <summary>Where a 3D try happens: the fire (its base centre), the exit to keep behind, the fire's size scale (original frame).</summary>
        public sealed class Place
        {
            public string Level;
            public double FireX, FireY, FireZ, ExitX, ExitY, ExitZ, Scale = 1;
            public double RetreatDistance = 7;
            /// <summary>The way out the lines name.</summary>
            public string ExitWord = "계단";
        }

        /// <summary>3층 복도: the parcel boxes outside 304 (FIRE_SPOT), the stairs behind whoever faces them from the extinguisher.</summary>
        public static readonly Place Corridor = new() { Level = "apartment", FireX = 19.15, FireY = 5.62, FireZ = -0.37, ExitX = 5, ExitY = 5.6, ExitZ = -0.7, Scale = 1.2, RetreatDistance = 7.5 };
        /// <summary>
        /// 1장 practice: a stand-in target west of the stairs, between them and 302. From the extinguisher (east of the
        /// stairs) the stairs lie between 정민 and the target: standing right means stepping past them first, so the exit
        /// ends up behind (spraying from where the extinguisher hangs leaves it in front).
        /// </summary>
        public static readonly Place Practice = new() { Level = "apartment", FireX = 1.7, FireY = 5.6, FireZ = -1.0, ExitX = 5, ExitY = 5.6, ExitZ = -0.7, Scale = 0.8, RetreatDistance = 9 };
        /// <summary>대피소 주차장 구석의 상자 불: the B ramp (fromRamp) is behind whoever faces it from 정민's mat.</summary>
        public static readonly Place Shelter = new() { Level = "shelter", FireX = -18.6, FireY = 0.7, FireZ = -9.2, ExitX = 11, ExitY = 0, ExitZ = 15, Scale = 2.1, RetreatDistance = 13, ExitWord = "출입구" };

        /// <summary>The Unity host builds the extinguisher in hand (HeldExtinguisher). Null (tests, no scene): the 2D screen.</summary>
        public static Func<StoryContext, HeldExtinguisherLogic, Place, IDisposable> View;

        /// <summary>
        /// The 1장 gauge check (a port addition); true when it was played (Esc: false, so the point can be visited again).
        /// A test seam: the tests that compare 1장 with the original set it to null.
        /// </summary>
        public static Func<StoryContext, Task<bool>> GaugePart = CheckGauge;

        /// <summary>Seconds the scene holds on a fire put out (or on being driven back) before the story goes on.</summary>
        public const double EndHold = 1.5;

        /// <summary>The practice's line (the toast and the objective): the powder on screen is a stand-in.</summary>
        public const string PracticeLine = "가상 연습입니다. 보이는 분말은 연습용 표시입니다.";

        /// <summary>The point ids whose prompts stay off while the extinguisher is in hand (it is already in hand; the fire is the aim).</summary>
        public static readonly string[] MutedPoints = { "hallFire", "extinguisher" };

        /// <summary>The event a 3D try records (its Result()).</summary>
        public const string UseEvent = "ext_use";

        /// <summary>Profiles that get the 2D screen (the accessible alternative); the reduce-motion setting does too.</summary>
        public static readonly string[] ScreenProfiles = { "lowvision", "mobility" };
        /// <summary>Profiles whose hold inputs are toggles in 3D.</summary>
        public static readonly string[] ToggleProfiles = { "mobility", "wheelchair", "guardian" };

        /// <summary>Whether the 3D try can be shown: the Unity host sets this with View (alive while it lives); tests set it with a stand-in view.</summary>
        public static Func<StoryContext, bool> Available = _ => false;

        public static bool Use3D(StoryContext ctx) =>
            View != null && Available != null && Available(ctx) && ctx?.Settings != null && !ctx.Settings.ReduceMotion && Array.IndexOf(ScreenProfiles, ctx.Settings.Profile ?? "") < 0;

        public static bool Toggle(StoryContext ctx) => Array.IndexOf(ToggleProfiles, ctx?.Settings?.Profile ?? "") >= 0;

        public static ExtinguisherSetup Setup(StoryContext ctx, string mode, string fire, Place place, bool practice = false, bool low = false, bool shouted = false) => new()
        {
            Mode = mode ?? "guided", Fire = fire == "large" ? "large" : "small", Practice = practice, Low = low, Shouted = shouted,
            Toggle = Toggle(ctx), RetreatDistance = place?.RetreatDistance ?? 7, ExitWord = place?.ExitWord ?? "계단",
        };

        /// <summary>
        /// The 3D try. onGrow runs once when a small fire spreads during it (the chapter shows the larger fire). Returns the
        /// result ({ sequence, result, view: "3d", ... }); the chapter scores it as it scored the 2D screen.
        /// </summary>
        public static async Task<JObject> Run3D(StoryContext ctx, ExtinguisherSetup setup, Place place, Action onGrow = null)
        {
            var g = ctx.G;
            var en = ctx.Engine;
            var logic = new HeldExtinguisherLogic(setup);
            bool controls = en.ControlsEnabled;
            string view = en.View;
            IDisposable v = null;
            bool shoutRecorded = setup.Shouted || setup.Practice, grownShown = false;
            double shownSmoke = -1, shownFire = -1, endT = 0;
            int shownSteps = -1;
            // the step list changes a few times a try: it is rebuilt only when the logic's step bits change (no per-frame lists)
            void Steps()
            {
                int key = logic.StepMask;
                if (key == shownSteps) return;
                shownSteps = key;
                string title = setup.Practice ? "소화기 사용 연습 (가상)" : "소화기로 끄기";
                if (logic.Mode == "guided")
                    ctx.Objective(title, setup.Practice ? PracticeLine : logic.Lost ? "끄기 어려운 불입니다. 물러나 알리고 대피합니다." : "작은 불이면 순서대로 끄고, 커지면 물러나 알립니다 (Q 두 번).",
                        logic.Steps().Select(s => new ObjectiveStep(s.Text, s.Done)).ToList());
                else ctx.Objective(title, logic.Mode == "hint" ? "작은 불이면 끄고, 커지면 물러나 알립니다. Y: 다음 순서 힌트" : "작은 불이면 끄고, 커지면 물러나 알립니다.", new List<ObjectiveStep>());
            }
            try
            {
                ctx.HandsFull = true;
                v = View?.Invoke(ctx, logic, place);
                en.SetView("first");
                en.SetControls(true);
                Steps();
                if (setup.Practice) ctx.Toast(PracticeLine, "self", 4200);
                else if (setup.Low) ctx.Toast("손잡이 옆 압력계 바늘이 녹색 구간보다 왼쪽에 있다.", "self", 3600);
                var res = await ctx.Roam(new RoamOptions
                {
                    Rate = 0, Doors = false,
                    Tick = dt =>
                    {
                        logic.Tick(dt);
                        var said = logic.TakeSaid();
                        if (said != null) foreach (var line in said) ctx.Toast(line, "self", 3200);
                        if (!shoutRecorded && logic.Done("shout"))
                        {
                            shoutRecorded = true;
                            GameRules.RecordEvent(g, "fire_shout", new JObject { ["from"] = "extinguisher" });
                        }
                        if (logic.Grown && !grownShown) { grownShown = true; onGrow?.Invoke(); }
                        if (!setup.Practice)
                        {
                            double sm = Math.Round(logic.SmokeExtra, 2);
                            if (Math.Abs(sm - shownSmoke) >= 0.02) { shownSmoke = sm; en.SetGrade(sm > 0.01 ? new JObject { ["smoke"] = sm } : null); }
                            double fl = Math.Round(logic.FireLv, 1);
                            if (fl != shownFire) { shownFire = fl; Endure.FireSound(ctx, Math.Min(1, logic.FireLv * 1.1)); }
                        }
                        Steps();
                        if (!logic.Ended) return null;
                        // a beat on the result (the fire out, or driven back) before the story moves on: the hand on the
                        // handle (Space) lets go first, so it does not skip the next card
                        if (logic.EndKind == "retreat" || logic.EndKind == "stopped" || (endT += dt) >= EndHold) return logic.Result();
                        return null;
                    },
                    On =
                    {
                        ["hallFire"] = _ => { ctx.Toast("소화기를 들고 있다. 불이 난 곳(아래)을 겨누고 손잡이를 쥐자.", "self"); return Task.FromResult(StoryContext.Handled); },
                        ["extinguisher"] = _ => { ctx.Toast("이미 소화기를 들고 있다.", "self"); return Task.FromResult(StoryContext.Handled); },
                        ["stairs3F"] = _ => { logic.Retreat(); return Task.FromResult<JToken>(logic.Result()); },
                        ["elevator"] = async _ => { if (!setup.Practice) await LookFirstScene.ElevatorStop(ctx); else ctx.Toast("연습 중이다.", "self"); return StoryContext.Handled; },
                        ["*"] = _ => Task.FromResult(StoryContext.Handled),
                    },
                });
                var result = res as JObject ?? logic.Result();
                if (!setup.Practice) GameRules.RecordEvent(g, UseEvent, (JObject)result.DeepClone());
                // the recap (the 2D screen's end card): which steps, in the official order, and how the powder went
                v?.Dispose(); v = null;
                ctx.Objective(null);
                var (title, lines) = logic.Recap(id => ctx.Facts.Text(id));
                if (title != null)
                    await ctx.Card(new CardOptions { Kind = "notice", Title = title, Meta = setup.Practice ? "소화기 사용 연습" : "소화기", Body = lines, Actions = new List<CardAction> { new("ok", "확인", true) } });
                return result;
            }
            finally
            {
                ctx.HandsFull = false;
                v?.Dispose();
                en.SetGrade(null);
                en.SetView(view);
                en.SetControls(controls);
                ctx.Objective(null); // the chapter sets its own again (Endure's ShowObjective, the prep list)
            }
        }

        // ------------------------------------------------------------------ 1장 gauge check

        public static JObject GaugeOptions(StoryContext ctx)
        {
            var facts = new JObject();
            foreach (var id in ExtinguisherCheck.FactIds)
            {
                var c = ctx.Facts.Cite(id);
                if (c != null) facts[id] = new JObject { ["text"] = c.Text, ["source"] = c.Source };
            }
            return new JObject
            {
                ["needle"] = ExtinguisherCheck.CorridorNeedle, ["profile"] = ctx.Settings?.Profile ?? "", ["reduceMotion"] = ctx.Settings?.ReduceMotion ?? false, ["facts"] = facts,
            };
        }

        /// <summary>The check at the corridor extinguisher (1장). False when the screen was not played (Esc): nothing recorded.</summary>
        public static async Task<bool> CheckGauge(StoryContext ctx)
        {
            var g = ctx.G;
            var res = await ctx.Ui.Open(ExtinguisherCheck.Screen, GaugeOptions(ctx));
            var o = ExtinguisherCheck.Outcome.Read(res);
            if (!o.Played) return false;
            GameRules.RecordEvent(g, ExtinguisherCheck.Event, o.ToJson());
            g.SetKnow(ExtinguisherCheck.Know, o.Report ? ExtinguisherCheck.Reported : ExtinguisherCheck.Left);
            if (o.Report)
            {
                ctx.Note("관리사무소에 알림: 3층 복도 소화기 압력계 바늘이 녹색 구간보다 왼쪽. 교체 부탁.", "self");
                ctx.Toast("박 소장이 그날 저녁 새 소화기로 바꿔 두었다. 바늘이 녹색 구간 안이다.", "neighbor", 4600);
            }
            else ctx.Toast("소화기는 그대로 두었다.", "self", 2600);
            return true;
        }

        /// <summary>After a corridor try with the low extinguisher: say why it ran out (the gauge fact).</summary>
        public static void LowNote(StoryContext ctx)
        {
            ctx.Toast((ExtinguisherCheck.Checked(ctx.G) ? "1장에 본 대로 압력이 빠진 소화기였다. " : "압력이 빠진 소화기였다. ") + (ctx.Facts.Text("ext_gauge") ?? ""), "official", 6000);
        }
    }
}
