// 2장 대피소 · 쓰러진 할아버지 (competition upgrade). After 정민 has signed in and sat down, the grandfather on the next
// mat collapses. The whole bystander CPR (반응 확인 -> 지목해 119·AED -> 호흡 확인 -> 가슴압박 + 교대 + AED) is the "cpr"
// screen (UI/Screens/CprScreen.cs, rules in Game/CprLogic.cs); it always ends with the paramedics taking over. The story
// records what happened: a "cpr" event with the summary (the deeds cpr_helped / cpr_relay, the learning report's 응급
// 처치 rows and the Fates line read it), the Drills ladder encounter (tool "cpr"), and time passing.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class CprScene
    {
        public const string Screen = "cpr";
        /// <summary>The Drills ladder tool (not in GameRules.Tools, which the original save format pins; added on first use).</summary>
        public const string Tool = "cpr";
        /// <summary>The facts the screen shows, each with its source (facts-ext.json).</summary>
        public static readonly string[] FactIds = { "cpr_check", "cpr_point", "cpr_breath", "cpr_gasp", "cpr_depth_rate", "cpr_fatigue", "cpr_fatigue_why", "cpr_aed_steps", "cpr_aed_clear", "cpr_practice" };
        /// <summary>Game minutes from the collapse to the ambulance leaving.</summary>
        public const int Minutes = 12;
        /// <summary>The grandfather: the grey-haired crowd model (as in the screen's drawing).</summary>
        public const string Model = "crowd2";

        /// <summary>Where he lies: his middle this far in front of 정민 (whichever way they face), across the view.</summary>
        public const double ManAhead = 1.4;
        /// <summary>From the feet (the person's origin) to the middle of the body when lying.</summary>
        const double HalfBody = 0.8;

        public static void EnsureTool(GameState g)
        {
            if (!g.Skill.ContainsKey(Tool)) g.Skill[Tool] = new List<JToken>();
        }

        public static JObject Options(StoryContext ctx, string stage)
        {
            var facts = new JObject();
            foreach (var id in FactIds)
            {
                var c = ctx.Facts.Cite(id);
                if (c != null) facts[id] = new JObject { ["text"] = c.Text, ["source"] = c.Source };
            }
            return new JObject
            {
                ["stage"] = stage, ["profile"] = ctx.Settings.Profile ?? "", ["reduceMotion"] = ctx.Settings.ReduceMotion, ["facts"] = facts,
                // started from 장면 바로가기: the screen may skip to the handover after the shock (a judge's short visit)
                ["demo"] = ctx.Resume == DDay.CprResume,
            };
        }

        public static async Task Run(StoryContext ctx)
        {
            var g = ctx.G;
            EnsureTool(g);
            var stage = Drills.LadderStage(g, Tool);
            var e = ctx.Engine;
            double fx = -Math.Sin(e.PlayerYaw), fz = -Math.Cos(e.PlayerYaw);
            double mx = e.PlayerX + fx * ManAhead, mz = e.PlayerZ + fz * ManAhead;
            // lying across the view: his feet one way, his head the other (the body runs back from the origin)
            double yaw = e.PlayerYaw + Math.PI * 0.5, px = -Math.Sin(yaw), pz = -Math.Cos(yaw);
            IStoryPerson man = null;
            try { man = e.SpawnPerson(Model, mx + px * HalfBody, e.PlayerY, mz + pz * HalfBody, yaw); man?.SetAnim("lie"); } catch { man = null; }
            // look at him from where 정민 sits, a little above (put back after the paramedics leave)
            e.SetCameraOverride(new CameraShot { X = e.PlayerX - fx * 0.4, Y = e.PlayerY + 1.6, Z = e.PlayerZ - fz * 0.4, TargetX = mx, TargetY = e.PlayerY + 0.25, TargetZ = mz, Fov = 55 });
            ctx.Objective("대피소 안에서", "쓰러진 할아버지 곁을 지킵니다.", new List<ObjectiveStep>());
            JToken res;
            try
            {
                await ctx.Say("옆자리", new[] { CprText.CollapseLine, "불러도 대답이 없다." }, null, "대피소");
                res = await ctx.Modal(() => ctx.Ui.Open(Screen, Options(ctx, stage)));
            }
            finally { e.SetCameraOverride(null); }
            var r = CprResult.Read(res);
            var ev = CprLogic.Evaluate(r);
            GameRules.RecordEvent(g, "cpr", r.ToJson());
            Drills.RecordEncounter(g, Tool, ev.Result);
            g.SetKnow("cprDone", true);
            ctx.Pass(Minutes);
            if (man != null) ctx.Engine.RemovePerson(man);
            ctx.Toast("구급대가 할아버지를 넘겨받아 구급차로 옮겼다.", "site", 4600);
            if (r.Played && r.Swaps == 0) ctx.Toast(ctx.Facts.Text("cpr_fatigue") ?? "", "official", 5200);
            ctx.Objective("대피소 안에서", "자리를 지키며 경보가 풀리기를 기다립니다.", new List<ObjectiveStep>());
        }
    }
}
