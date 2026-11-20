// Port of story/escape.js — 연기 속 대피 (design M7): after choosing to leave the corridor fire, 정민 gets down to 1F
// through the smoke. A wet towel over nose and mouth and a low body (C) cut what you breathe in (fact fire_big); the
// screen blurs with the smoke you have breathed. Health runs out -> StoryFail('dead', { why: 'smoke' }).
// SmokeEscape(ctx, size, grow) resolves at the 1F lobby with { exposure, towel, crouch }.
//
// The original's blur was CSS on #world (class sim-smoke, --smoke 0..1). Here it is the engine grade:
// Engine.SetGrade({ smoke: 0..1 }) while the smoke lasts, SetGrade(null) at the end.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class Escape
    {
        /// <summary>apartment 'smoke' state per fire size.</summary>
        public static readonly IReadOnlyDictionary<string, double> Air = new Dictionary<string, double> { ["small"] = 0.25, ["large"] = 0.7 };

        /// <summary>At the elevator a neighbour stops 정민 (LookFirstScene.ElevatorStop). Tests comparing with the original turn it off.</summary>
        public static bool NeighbourStops = true;

        static int Count(GameState g, string id) => g.Items.TryGetValue(id, out var e) && e != null ? e.Home + e.Bag : 0;

        /// <param name="neighbour">The elevator correction by 서연 (the look step was played); false keeps the original's fact toast.</param>
        public static async Task<JObject> SmokeEscape(StoryContext ctx, string size = "large", Action grow = null, bool neighbour = true)
        {
            var g = ctx.G;
            var engine = ctx.Engine;
            var s = new Strain.SmokeState();
            bool towel = false;
            double low = 0, total = 0, t = 0, hurtShown = 0, shownLevel = -1;
            int warned = 0;
            size ??= "large";
            void Show() => ctx.Objective("연기 속에서 1층까지", "몸을 낮추고(C) 계단으로 내려갑니다. 엘리베이터는 쓰지 않습니다.", new List<ObjectiveStep>
            {
                new("젖은 수건으로 코와 입 막기 (302호 싱크대·베란다, 선택)", towel), new("계단으로 1층까지", false),
            });
            Show();
            if (g.Knows("gasValve")) ctx.Payoff("gas", "가스 중간 밸브 위치를 알고 있어 나서기 전에 바로 잠갔다"); // doEquip homeGas

            Task<JToken> Wet(StoryPoint _)
            {
                if (towel) { ctx.Toast("젖은 수건으로 코와 입을 막고 있다."); return Task.FromResult<JToken>(null); }
                if (Count(g, "towel") == 0) { ctx.Toast("수건이 보이지 않는다. 소매로라도 코와 입을 막자."); return Task.FromResult<JToken>(null); }
                string bottle = Count(g, "water_500") > 0 ? "water_500" : Count(g, "water_2l") > 0 ? "water_2l" : null;
                if (!g.Knows("tubFilled") && bottle == null) { ctx.Toast("수건을 적실 물이 없다. 몸을 낮춰 바로 내려가자."); return Task.FromResult<JToken>(null); }
                if (g.Knows("tubFilled")) ctx.Payoff("tub", "받아 둔 물로 수건을 적셨다");
                else { ctx.Rules.Consume(g, bottle, 1, g.Items[bottle].Home > 0 ? "home" : "bag"); ctx.Toast("생수 한 병으로 수건을 적셨다."); }
                towel = true;
                AiOn.Rendering.AtmosphereDirector.Mask("cloth"); // the wet towel at the bottom of the view, misting with each breath
                GameRules.RecordEvent(g, "towel_wet", new JObject { ["with"] = g.Knows("tubFilled") ? "tub" : bottle });
                Show();
                ctx.Toast(ctx.Facts.Text("fire_big"), "official", 5200);
                return Task.FromResult<JToken>(null);
            }

            try
            {
                var res = await ctx.Roam(new RoamOptions
                {
                    Rate = 0,
                    Tick = dt =>
                    {
                        bool inApt = engine.LevelId == "apartment";
                        double py = engine.PlayerY;
                        if ((t += dt) > 20 && size == "small") { size = "large"; grow?.Invoke(); }
                        // 302호 itself (floor 5.75, the corridor is 5.6) has its door shut: less smoke; the stairwell less.
                        double a = Air.TryGetValue(size, out var v) ? v : Air["large"];
                        double air = !inApt ? 0 : py > 5.68 ? a * 0.3 : py > 4 ? a : py > 1 ? a * 0.4 : 0;
                        bool crouch = engine.Crouching;
                        total += dt; if (crouch) low += dt;
                        var r = Strain.SmokeStep(s, dt, air, crouch, towel);
                        double lv = Math.Round(r.Level, 3);
                        if (lv != shownLevel) { shownLevel = lv; engine.SetGrade(new JObject { ["smoke"] = lv }); }
                        hurtShown -= dt;
                        if (r.Hurt > 0)
                        {
                            ctx.DeathCause = "smoke"; ctx.Stat("health", -r.Hurt);
                            if (hurtShown <= 0) { hurtShown = 1.5; ctx.Hud.Hurt(0.25); }
                        }
                        if (warned == 0 && s.Exposure > 4) { warned = 1; ctx.Toast(crouch ? "연기가 목을 찌른다. 이대로 낮게 계단까지." : "연기가 목을 찌른다. 몸을 낮추자 (C).", "self", 3600); }
                        else if (warned == 1 && s.Exposure > 14) { warned = 2; ctx.Toast("눈앞이 뿌옇다. 계단은 복도 가운데, 비상구 표시등 쪽이다.", "self", 4200); }
                        if (inApt && py < 1 && towel) ctx.Payoff("towel", "젖은 수건으로 코와 입을 막고 연기 속을 내려왔다");
                        if (inApt && py < 1)
                            return new JObject { ["exposure"] = Num.JsRound(s.Exposure * 10) / 10, ["towel"] = towel, ["crouch"] = total > 0 && low / total > 0.5 };
                        return null;
                    },
                    On =
                    {
                        ["homeExitDoor"] = async _ => { await ctx.Goto("apartment", "corridor3F"); return null; },
                        ["homeDoor"] = async _ => { await ctx.Goto("apartment", "homeEntry"); return null; }, // back in for a towel
                        ["homeSink"] = Wet, ["homeStock"] = Wet,
                        ["elevator"] = async _ =>
                        {
                            // competition upgrade: a neighbour stops 정민 (a correction); the original only showed the fact
                            if (NeighbourStops && neighbour && LookFirstScene.OnThird(ctx)) { await LookFirstScene.ElevatorStop(ctx); return null; }
                            ctx.Toast(ctx.Facts.Text("fire_lift"), "official", 5000);
                            GameRules.RecordEvent(g, "elevator_tried", new JObject { ["fire"] = true });
                            return null;
                        },
                        ["hallFire"] = _ => { ctx.Toast("불길에서 떨어져 계단으로.", "self"); return Task.FromResult<JToken>(null); },
                        ["extinguisher"] = _ => { ctx.Toast("불이 이미 번지고 있다. 끄기보다 대피가 먼저다.", "self"); return Task.FromResult<JToken>(null); },
                    },
                });
                var o = res as JObject ?? new JObject();
                GameRules.RecordEvent(g, "smoke_escape", o);
                return o;
            }
            finally
            {
                engine.SetGrade(null);
                AiOn.Rendering.AtmosphereDirector.Mask(null);
            }
        }
    }
}
