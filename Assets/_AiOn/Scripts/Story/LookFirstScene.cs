// 3장 · 불나면 살펴서 대피 (competition upgrade): the judgement step before the corridor fire (Endure.CorridorFire).
// The "lookfirst" screen (UI/Screens/LookFirstScreen.cs, rules in Game/LookFirstLogic.cs) asks 정민 to look before
// leaving, to say where the fire is, and to choose: leave (the existing 3D smoke escape and the extinguisher stay that
// branch), stay in and seal, or the balcony 경량칸막이 (usable at once only if the boxes were cleared in 1장: know
// partitionCleared, PrepActions.Partition). Staying and the partition end with the 119 call; leaving makes the call
// from the lobby (Report). The story keeps a "lookfirst" event (the learning report's rows read it).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class LookFirstScene
    {
        public const string Screen = "lookfirst";
        /// <summary>The know flag 1장 sets when the boxes in front of the balcony partition were moved.</summary>
        public const string ClearedKnow = "partitionCleared";
        /// <summary>The apartment level state of the partition: boxes (default), clear, broken.</summary>
        public const string StateKey = "partition";
        /// <summary>The interaction point on the balcony wall (apartment level).</summary>
        public const string Point = "homePartition";
        /// <summary>Game minutes from sealing the door (or crossing to 303) to the firefighters putting it out.</summary>
        public const int WaitMinutes = 25;

        /// <summary>Every fact the screen may show (each with its source).</summary>
        public static readonly string[] FactIds =
        {
            "fire_look", "fire_door", "fire_stay", "fire_other_smoke", "fire_own_home", "fire_own_trapped", "fire_seal", "fire_lift", "fire_big",
            "fire_partition", "fire_partition_clear", "fire_report", "fire_119",
        };

        static int Count(GameState g, string id) => g.Items.TryGetValue(id, out var e) && e != null ? e.Home + e.Bag : 0;
        static string Bottle(GameState g) => Count(g, "water_500") > 0 ? "water_500" : Count(g, "water_2l") > 0 ? "water_2l" : null;

        public static JObject Options(StoryContext ctx, string mode, bool knocked, string caller = "home", string size = "small")
        {
            var g = ctx.G;
            var setup = new LookSetup
            {
                Knocked = knocked, PartitionCleared = g.Knows(ClearedKnow), Towel = Count(g, "towel") > 0, Tub = g.Knows("tubFilled"),
                Bottle = Bottle(g) != null, Profile = ctx.Settings.Profile ?? "", Mode = mode, Caller = caller,
                Size = size == "large" ? "large" : "small",
            };
            var o = setup.ToJson();
            var facts = new JObject();
            foreach (var id in FactIds)
            {
                var c = ctx.Facts.Cite(id);
                if (c != null) facts[id] = new JObject { ["text"] = c.Text, ["source"] = c.Source };
            }
            o["facts"] = facts;
            o["reduceMotion"] = ctx.Settings.ReduceMotion;
            return o;
        }

        /// <summary>
        /// The whole step, at home as the fire starts. Returns what was decided (Outcome.Act null = nothing was played: the
        /// story goes on to the corridor as before). Stay and partition are resolved here (the wait, the firefighters).
        /// </summary>
        public static async Task<LookFirstLogic.Outcome> Run(StoryContext ctx, bool knocked)
        {
            var g = ctx.G;
            var res = await ctx.Modal(() => ctx.Ui.Open(Screen, Options(ctx, "look", knocked)));
            var o = LookFirstLogic.Outcome.Read(res);
            if (!o.Played) return o;
            var details = res as JObject ?? new JObject();
            GameRules.RecordEvent(g, "lookfirst", (JObject)details.DeepClone());
            if (o.Wet == "bottle")
            {
                var b = Bottle(g);
                if (b != null) ctx.Rules.Consume(g, b, 1, g.Items[b].Home > 0 ? "home" : "bag");
            }
            if (o.Wet == "tub") ctx.Payoff("tub", "받아 둔 물로 수건을 적셔 현관 문틈을 막았다");
            if (o.Act == "partition" && g.Knows(ClearedKnow)) ctx.Payoff("partition", "1장에 치워 둔 경량칸막이로 바로 옆집에 피했다");
            if (o.Act == "partition" && o.PartitionOpen) ctx.SetLevelState("apartment", StateKey, "broken");
            ctx.Refresh();
            return o;
        }

        /// <summary>The 119 call alone (after leaving, from the lobby; size = the fire as it was seen). True when it was played.</summary>
        public static async Task<bool> Report(StoryContext ctx, string caller, string size = "small")
        {
            var res = await ctx.Modal(() => ctx.Ui.Open(Screen, Options(ctx, "report", true, caller, size)));
            var o = LookFirstLogic.Outcome.Read(res);
            if (!o.Report.Played) return false;
            GameRules.RecordEvent(ctx.G, "fire_report", o.Report.ToJson());
            return true;
        }

        /// <summary>True on the 3F corridor (303 is next to its elevator); the 1F lobby's elevator has the same point id.</summary>
        public static bool OnThird(StoryContext ctx) => ctx.Engine.LevelId == "apartment" && ctx.Engine.PlayerY > 4;

        /// <summary>
        /// The elevator during a fire. On 3F 서연 calls out from 303's door (deaf mode: she runs out and points at the
        /// stairs) and 정민 turns back (a correction, never a punishment; fact fire_lift); anywhere else the fact alone, as
        /// in the original. Recorded like the smoke escape's elevator try (elevator_tried, fire: true).
        /// </summary>
        public static async Task ElevatorStop(StoryContext ctx)
        {
            GameRules.RecordEvent(ctx.G, "elevator_tried", new JObject { ["fire"] = true });
            if (!OnThird(ctx)) { ctx.Toast(ctx.Facts.Text("fire_lift"), "official", 5000); return; }
            bool deaf = ctx.Settings.Profile == "deaf";
            await ctx.Ui.Talk(new TalkOptions
            {
                Speaker = LookFirstLogic.Neighbour, Role = "303호 문 앞",
                Lines = { deaf ? LookFirstLogic.ElevatorLine("deaf") : "정민 씨, 엘리베이터는 안 돼요! 불이 나면 계단으로 가야 해요.", ctx.Facts.Text("fire_lift") ?? "" },
            });
        }

        /// <summary>The night of the fire 정민 broke the partition and crossed to 301 (the panel stays broken afterwards).</summary>
        public static bool Broken(GameState g) => g.Events.Any(e => e.Type == "lookfirst" && e.Details?["act"]?.Type == JTokenType.String
            && (string)e.Details["act"] == "partition" && (e.Details["partition"] as JObject)?["open"]?.Type == JTokenType.Boolean && (bool)e.Details["partition"]["open"]);

        /// <summary>
        /// Stay or partition: first the neighbours (unless 304 already raised the alarm: "불이야!" from inside, the original's
        /// fire_shout, so the 3장 challenge stays reachable without going out), then waiting it out while the firefighters
        /// come (a fade, the time, then their word).
        /// </summary>
        public static async Task WaitForRescue(StoryContext ctx, LookFirstLogic.Outcome o, bool knocked = false)
        {
            var en = ctx.Engine;
            bool next = o.Act == "partition";
            if (!knocked)
            {
                var pick = await ctx.Say("정민", new[] { next ? "301호 베란다에 섰다. 3층에는 아직 모르는 이웃이 있을지 모른다." : "문틈을 막았다. 3층에는 아직 모르는 이웃이 있을지 모른다." },
                    new List<TalkChoice> { new("shout", next ? "베란다에서 \"불이야!\" 크게 외쳐 알린다" : "현관 안쪽에서 \"불이야!\" 크게 외쳐 알린다"), new("wait", "그대로 기다린다") }, "알리기");
                if (pick == "shout")
                {
                    GameRules.RecordEvent(ctx.G, "fire_shout", new JObject { ["from"] = o.Act });
                    ctx.Toast("복도 쪽 문들이 열리고, 이웃들이 서로 부르며 계단으로 내려간다.", "site", 4200);
                }
            }
            await ctx.Caption(next
                ? "301호 거실에서 이웃과 함께 창문을 닫고 기다렸다. 휴대폰은 119와 연결된 채였다."
                : "안방 문을 닫고 창문을 꼭 닫은 채 기다렸다. 휴대폰은 119와 연결된 채였다.", null, 3600);
            await en.FadeTo(1, 0.6);
            ctx.Pass(WaitMinutes);
            Endure.FireSound(ctx, 0);
            ctx.SetLevelState("apartment", "hallFire", "out");
            ctx.SetLevelState("apartment", "smoke", 0.1);
            await en.FadeTo(0, 0.6);
            var who = next ? "301호로 피하신 분" : "302호 분";
            var line = o.Report.Played && o.Report.Place
                ? who + ", 괜찮으세요? 신고해 주신 대로 3층 복도로 바로 올라왔어요. 불은 다 껐습니다. 304호 어르신도 무사하세요."
                : who + ", 괜찮으세요? 불은 다 껐습니다. 304호 어르신도 무사하세요.";
            await ctx.Say("소방대원", new[] { line, next ? "베란다 칸막이는 관리사무소에서 다시 막아 줄 거예요. 잘 피하셨어요." : "문틈 막아 두신 덕에 집 안은 연기가 적네요." }, null, "3층 복도");
            GameRules.RecordEvent(ctx.G, "fire_waited", new JObject { ["act"] = o.Act, ["minutes"] = WaitMinutes });
        }
    }
}
