// Port of story/neighbors.js — 3장 저녁의 이웃들: the shelter rumour judged by its source, a barter you can test before
// you agree, and a knock at the door at night. Each has more than one fair answer; saying no costs only the help.
// These scenes talk through ctx.Ui directly (like the original): call them from a roam handler (already modal).
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class Neighbors
    {
        static int Count(GameState g, string id) => g.Items.TryGetValue(id, out var e) && e != null ? e.Home + e.Bag : 0;
        static string From(GameState g, string id) => g.Items[id].Home > 0 ? "home" : "bag";
        static string Qty(StoryContext ctx, string id, int n)
        {
            var d = ctx.Rules.Db.Get(id);
            return $"{d?.displayName ?? id} {n.ToString(CultureInfo.InvariantCulture)}{d?.unit}";
        }

        /// <summary>The same four labels the notebook uses (state.js LABELS).</summary>
        static List<TalkChoice> LabelChoices() => new()
        {
            new("official", "공식 안내"), new("site", "현장에서 확인한 것"), new("neighbor", "이웃에게 들은 것"), new("rumor", "소문 · 확인 안 됨"),
        };

        /// <summary>"생활문화센터 대피소 폐쇄" (Rumors shelter_closed): sort the claim first, then decide what to do with it.</summary>
        public static async Task StoreRumor(StoryContext ctx)
        {
            var g = ctx.G;
            var posts = Rumors.Posts(g, "shelter_closed");
            RumorPost claim = posts[0], site = posts[1];
            var stage = Drills.LadderStage(g, "info");
            var label = await ctx.Ui.Talk(new TalkOptions
            {
                Speaker = "102동 주민", Role = "바로편의점 앞",
                Lines =
                {
                    "여기 주인도 떠났나 봐요. 참, 그 얘기 들었어요?", $"\"{claim.Text}\"", $"({claim.From} · {claim.Meta})",
                    stage == "free" ? "이 말은 어떤 정보일까?" : "누가 말했나? 언제 확인했나? 어디를 두고 한 말인가? 이 말은 어떤 정보일까?",
                },
                Choices = LabelChoices(),
            });
            var res = Rumors.Judge(g, "shelter_closed", label);
            Drills.RecordEncounter(g, "info", res.Result);
            ctx.Note("SNS: 생활문화센터 대피소 폐쇄? (출처 불명)", "rumor", "store");
            if (!res.Ok) ctx.Toast("다시 떠올려 보니, 누가 언제 확인한 말인지는 듣지 못했다.", "self", 4200);
            var pick = await ctx.Ui.Talk(new TalkOptions
            {
                Speaker = "102동 주민", Role = "바로편의점 앞", Lines = { "저도 확실한 건 모르겠어요. 어떡하죠?" },
                Choices = new List<TalkChoice> { new("spread", "큰일이네요. 다른 이웃들한테도 알려야겠어요"), new("check", "어디서 나온 말인지 먼저 확인해 볼게요") },
            });
            if (pick == "spread")
            {
                Rumors.Believe(g, "shelter_closed");
                GameRules.RecordEvent(g, "rumor_spread", new JObject { ["id"] = "shelter_closed" });
                // The correction reaches 정민 the same evening; in this chapter believing it changes no road.
                await ctx.Ui.Talk(new TalkOptions { Speaker = site.From, Role = $"{site.Meta}", Lines = { site.Text, "단톡방에 도는 글은 저도 봤어요. 그래서 직접 가 본 거예요." } });
                g.SetKnow("misled", null);
                ctx.Note("박 소장: 생활문화센터 출입구 A 열려 있음 (직접 다녀옴)", "site");
                var fix = await ctx.Ui.Talk(new TalkOptions
                {
                    Speaker = "정민", Role = "바로편의점 앞", Lines = { "아까 102동 주민에게 폐쇄됐다고 맞장구친 게 마음에 걸린다." },
                    Choices = new List<TalkChoice> { new("fix", "102동 주민에게 아니라고 다시 알린다"), new("no", "그냥 둔다") },
                });
                if (fix == "fix") { ctx.Pass(15); ctx.Remember("rumor_corrected", new JObject { ["after"] = "spread" }); ctx.Toast("\"아, 그래요? 나도 옆집에 말해 둘게요.\"", "neighbor"); }
            }
            else
            {
                var shown = Rumors.Verify(g, "shelter_closed");
                var heard = ctx.Rules.Usable(g, "radio") ? await StoryCommon.RadioScene(ctx, "rumor") : null;
                var official = shown.First(p => p.Label == "official");
                await ctx.Ui.Talk(new TalkOptions { Speaker = heard != null ? "재난방송" : "행정복지센터 게시판(주민의 휴대폰 사진)", Role = "공식 안내", Lines = { $"{official.Text} (가상 안내)" } });
                ctx.Note("생활문화센터 대피소 정상 운영 (공식 안내로 확인)", "official");
                var tell = await ctx.Ui.Talk(new TalkOptions
                {
                    Speaker = "102동 주민", Role = "바로편의점 앞", Lines = { "…뭐래요? 확인됐어요?" },
                    Choices = new List<TalkChoice> { new("tell", "102동 주민에게 확인한 내용을 알려 준다"), new("no", "그냥 간다") },
                });
                if (tell == "tell")
                {
                    await ctx.Ui.Talk(new TalkOptions { Speaker = "102동 주민", Role = "바로편의점 앞", Lines = { "아, 정상 운영이에요? 다행이다. 저도 다른 데 퍼뜨릴 뻔했네요." } });
                    ctx.Remember("rumor_corrected", new JObject { ["after"] = "check" });
                }
            }
            await StoryCommon.FactCard(ctx, "소문을 만났을 때", new[] { "info_rumor", "shelter_rumor", "info_harm" });
        }

        /// <summary>102동 주민 offers a battery pack for food. Testing it first shows it is a used pack (batteries_old).</summary>
        static readonly (string id, int n)[] Foods = { ("bars", 3), ("canned", 1), ("ramen", 2), ("rice_instant", 2) };

        /// <summary>offerFor: the first food the player has enough of, as { id: n } (null when none).</summary>
        public static Dictionary<string, int> OfferFor(GameState g)
        {
            foreach (var (id, n) in Foods) if (Count(g, id) >= n) return new Dictionary<string, int> { [id] = n };
            return null;
        }

        static Dictionary<string, int> OneFood(GameState g)
        {
            foreach (var (id, _) in Foods) if (Count(g, id) >= 1) return new Dictionary<string, int> { [id] = 1 };
            return null;
        }

        /// <summary>'3봉으로' / '1개로': the particle follows the last syllable's final consonant (ㄹ takes 로).</summary>
        public static string Ro(string w)
        {
            int c = w.Length > 0 ? w[w.Length - 1] - 0xac00 : -1;
            int f = c >= 0 && c < 11172 ? c % 28 : 0;
            return w + (f == 0 || f == 8 ? "로" : "으로");
        }

        static string CartLine(StoryContext ctx, Dictionary<string, int> cart) => string.Join(", ", cart.Select(kv => Qty(ctx, kv.Key, kv.Value)));

        public static async Task Barter(StoryContext ctx)
        {
            var g = ctx.G;
            var offer = OfferFor(g);
            bool triedOld = false;
            List<TalkChoice> Choices()
            {
                var l = new List<TalkChoice>
                {
                    new("test", "먼저 손전등에 넣어 봐도 될까요?", Count(g, "flashlight") == 0, "손전등이 없다"),
                    new("take", offer != null ? $"{Ro(CartLine(ctx, offer))} 바꾼다" : "바꾼다", offer == null, "바꿀 만한 먹을거리가 없다"),
                };
                if (Count(g, "old_bars") > 0 && !triedOld) l.Add(new TalkChoice("old", "작년 비상가방에서 나온 건빵을 내민다"));
                l.Add(new TalkChoice("no", "괜찮아요. 지금은 안 바꿀게요"));
                return l;
            }
            var pick = await ctx.Ui.Talk(new TalkOptions
            {
                Speaker = "102동 주민", Role = "물물교환",
                Lines = { "그런데 혹시 먹을 거 남는 거 있어요? 저한테 AA 건전지 한 팩이 있는데, 바꿀래요?", offer != null && offer.ContainsKey("bars") ? "건빵이나 에너지바 세 봉이면 돼요." : "먹을 거 조금이면 돼요." },
                Choices = Choices(),
            });
            while (pick == "old")
            {
                triedOld = true;
                await ctx.Ui.Talk(new TalkOptions { Speaker = "102동 주민", Role = "봉지를 뒤집어 보며", Lines = { "아, 이거 기한이 한참 지났네요. 이건 저도 못 받아요. 드시지도 마세요.", ctx.Facts.Text("bag_nocook") } });
                GameRules.RecordEvent(g, "barter_old_bars", new JObject());
                pick = await ctx.Ui.Talk(new TalkOptions { Speaker = "102동 주민", Role = "물물교환", Lines = { "다른 걸로 바꿀래요?" }, Choices = Choices() });
            }
            if (pick == "test")
            {
                var weak = (ctx.Rules.Db.Get("batteries_old")?.charge ?? 30).ToString(CultureInfo.InvariantCulture);
                GameRules.RecordEvent(g, "barter_tested", new JObject { ["charge"] = ctx.Rules.Db.Get("batteries_old")?.charge ?? 30 });
                var small = OneFood(g);
                var smallId = small?.Keys.First();
                var p2 = await ctx.Ui.Talk(new TalkOptions
                {
                    Speaker = "102동 주민", Role = "손전등을 켜 보니",
                    Lines =
                    {
                        "손전등에 넣고 켜 보니 불빛이 금방 누렇게 흐려진다.", $"\"어? 쓰던 거였나 봐요. 저도 몰랐어요. 남은 게 {weak}% 정도겠네요.\"",
                        small != null ? $"\"그럼 {Qty(ctx, smallId, 1)}만 주세요. 라디오 잠깐 듣기엔 될 거예요.\"" : "\"이건 그냥 두세요. 미안해요.\"",
                    },
                    Choices = small != null
                        ? new List<TalkChoice> { new("small", $"{Ro(Qty(ctx, smallId, 1))} 바꾼다"), new("no", "괜찮아요") }
                        : new List<TalkChoice> { new("no", "알겠어요") },
                });
                if (p2 == "small")
                {
                    ctx.Rules.Trade(g, small, new Dictionary<string, int> { ["batteries_old"] = 1 }, "trader");
                    ctx.Toast($"쓰던 건전지 한 팩을 받았다. 남은 양은 {weak}% 정도.", "self", 4200);
                }
                ctx.Toast(ctx.Facts.Text("radio_batt"), "official", 5200);
                return;
            }
            if (pick == "take")
            {
                ctx.Rules.Trade(g, offer, new Dictionary<string, int> { ["batteries_old"] = 1 }, "trader");
                await ctx.Ui.Talk(new TalkOptions { Speaker = "정민", Role = "집에 와서", Lines = { "받은 건전지를 넣어 보니 불빛이 약하다. 쓰던 건전지였다. 그분도 몰랐던 것 같다.", ctx.Facts.Text("radio_batt") } });
                return;
            }
            await ctx.Ui.Talk(new TalkOptions { Speaker = "102동 주민", Role = "물물교환", Lines = { "네, 그래요. 필요하면 102동 1층으로 와요." } });
        }

        /// <summary>A knock at night: someone asks for water. Giving, pointing to the water point, or not answering are all fair.</summary>
        public static async Task Knock(StoryContext ctx)
        {
            var g = ctx.G;
            var water = new[] { "water_500", "water_2l" }.FirstOrDefault(id => Count(g, id) > 0);
            var pick = await ctx.Ui.Talk(new TalkOptions
            {
                Speaker = "현관 너머", Role = $"D+{g.Day.ToString(CultureInfo.InvariantCulture)} {GameRules.Hhmm(g.Minute)}",
                Lines = { "똑똑. 문 너머에서 낮은 목소리가 들린다.", "\"밤늦게 죄송해요. 101동 사는 사람인데요, 물을 조금만 얻을 수 있을까요? 아이가 열이 나서요.\"" },
                Choices = new List<TalkChoice>
                {
                    new("give", "문을 조금 열고 물 한 병을 건넨다", water == null, "나눌 물이 없다"),
                    new("info", "문 너머로 급수소 위치와 시간을 알려 준다"),
                    new("none", "대답하지 않는다"),
                },
            });
            GameRules.RecordEvent(g, "knock", new JObject { ["pick"] = pick });
            if (pick == "give")
            {
                ctx.Rules.Consume(g, water, 1, From(g, water));
                // G.karma = (G.karma ?? 0) + 1 (a field the original keeps on the game; here in GameState.Extra).
                g.Extra["karma"] = (g.Extra.TryGetValue("karma", out var k) && k != null && (k.Type == JTokenType.Integer || k.Type == JTokenType.Float) ? (long)(double)k : 0) + 1;
                GameRules.RecordEvent(g, "help", new JObject { ["who"] = "stranger", ["what"] = "밤에 문을 두드린 이웃에게 물 한 병을 건넸다" });
                await ctx.Ui.Talk(new TalkOptions { Speaker = "현관 너머", Role = "101동 주민", Lines = { "\"고마워요. 정말로요.\"", "발소리가 계단 쪽으로 멀어진다." } });
            }
            else if (pick == "info")
            {
                GameRules.RecordEvent(g, "help", new JObject { ["who"] = "stranger", ["what"] = "밤에 문을 두드린 이웃에게 급수소 위치와 운영 시간을 알려 주었다" });
                await ctx.Ui.Talk(new TalkOptions { Speaker = "정민", Role = "문 너머로", Lines = { "\"행정복지센터 마당에 비상급수시설이 있어요. 오전 9시 반부터 오후 5시까지예요. 물통을 가져가세요.\"", "\"아, 몰랐어요. 내일 아침에 가 볼게요. 고마워요.\"" } });
                ctx.Toast(ctx.Facts.Text("shelter_share"), "official", 5200);
            }
            else
            {
                await ctx.Caption("잠시 뒤 발소리가 계단 쪽으로 멀어졌다.", null, 3000);
            }
        }
    }
}
