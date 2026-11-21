// 3장 「산에서 내려온 사람들」 (competition upgrade): a sub-scene of 3장 버티기, not a chapter. Nobody comes on screen and
// 정민 never meets anyone armed: on D+1, on the way to the water point, 정민 passes traces by the path up the hill behind
// the flats (Traces). On D+2 morning an official text declares 통합방위 병종사태 (외출 자제, 뒷산 출입 금지) and the
// group chat claims "북한군 수십 명" (RumorScene with Rumors "mountain_raid"). At home 정민 turns yesterday's memory into
// a report card (screen "sighting", rules in Game/MountainLogic.cs), answers a stranger at the door through the
// intercom (a lost courier), and sees an unknown object on the lawn by the car park (don't touch, 1338 or the police
// station). The report's accuracy decides when the 통제 is lifted (a game rule). After the chapter card, the record card
// "그날, 시민들은" gives three sourced lines on 1996 (no names, no victims) and today's line.
//
// Events: mountain_traces { noted, reached, minute }, sighting (SightingReport.Result), intercom { first, opened, asked,
// office, police }, unknown_object { first, reported, extKnown }, defense_lift { lift }, record_card { id }. Know:
// mountainTraces (noted | seen), defenseLift (early | evening | late).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class MountainScene
    {
        public const string Screen = "sighting";
        /// <summary>The story mark at the start of the D+2 day (the scene-select state is taken here).</summary>
        public const string StartMark = "mountain";
        public const string TracesEvent = "mountain_traces";
        public const string TracesKnow = "mountainTraces";
        public const string LiftKnow = "defenseLift";

        /// <summary>Every fact the report screen may show (each with its source).</summary>
        public static readonly string[] FactIds =
        {
            "defense_report", "defense_zone", "suspect_report", "report_5w", "report_112_text", "info_suspect", "info_rumor", "info_appearance",
        };

        /// <summary>The D+1 memo (after its time); the report screen shows the same words.</summary>
        public const string TracesMemo = "뒷산 산책로 입구(약수터 갈림길 앞): 젖은 발자국 두 줄(크기 다름), 산에서 내려와 큰길 버스정류장 쪽으로. 새 운동화 택 2개, 검은 가방 1개 (만지지 않음)";

        static JToken Undefined => null;
        static bool Deaf(StoryContext ctx) => ctx.Settings.Profile == "deaf";
        static bool PhoneOn(StoryContext ctx) => ctx.G.Phone.Battery > 0;

        // ---------------------------------------------------------------- D+1: the traces

        /// <summary>D+1, out of the flats on the way to the water point: traces by the path up the hill (traces only).</summary>
        public static async Task Traces(StoryContext ctx)
        {
            var g = ctx.G;
            if (g.Events.Any(e => e.Type == TracesEvent)) return;
            int minute = g.Minute;
            // where and which way are seen here (the report card's exact 어디서 and 이동 방향 come from these lines)
            var lines = new List<string>
            {
                "단지를 나서는데, 뒷산 산책로 입구 약수터 갈림길 앞에서 눈길이 멈춘다.",
                "마른 보도 위에 젖은 발자국 두 줄. 크기가 다르고, 바닥 무늬가 새것처럼 또렷하다. 발자국은 산에서 내려와 큰길 버스정류장 쪽으로 이어진다.",
                "옆에는 떼어 낸 운동화 택 두 개, 산책로 옆에는 검은 가방 하나.",
                "밤새 비는 오지 않았다. 왠지 찜찜하다.",
            };
            string memo = g.Phone.Battery > 0 ? "휴대폰 메모에" : "종이에";
            var choices = new List<TalkChoice>
            {
                new("note", $"가까이 가지 않고, 본 것을 {memo} 적어 둔다"), new("bag", "가방을 열어 본다"), new("pass", "급수소가 먼저다. 그냥 지나간다"),
            };
            bool reached = false;
            var pick = await ctx.Say("뒷산 산책로 입구", lines, choices, "급수소 가는 길");
            if (pick == "bag")
            {
                reached = true;
                await ctx.Say("정민", "손을 뻗다가 멈췄다. 누구 것인지도, 무엇인지도 모르는 물건이다.", null, "산책로 입구");
                await StoryCommon.FactCard(ctx, "모르는 물건", new[] { "info_suspect" });
                pick = await ctx.Say("정민", "그래도 마음에 걸린다.", new List<TalkChoice> { new("note", $"본 것을 {memo} 적어 둔다"), new("pass", "그냥 지나간다") }, "산책로 입구");
            }
            bool noted = pick == "note";
            if (noted) ctx.Note($"{GameRules.Hhmm(minute)} {TracesMemo}", "site");
            GameRules.RecordEvent(g, TracesEvent, new JObject { ["noted"] = noted, ["reached"] = reached, ["minute"] = minute });
            g.SetKnow(TracesKnow, noted ? "noted" : "seen");
        }

        // ---------------------------------------------------------------- D+2: the day indoors

        /// <summary>The D+2 day, from the morning text to the evening (then the skip to the fire's night).</summary>
        public static async Task Run(StoryContext ctx)
        {
            ctx.Reached(StartMark);
            bool deaf = Deaf(ctx), phone = PhoneOn(ctx);
            await ctx.Skip("D+1 토 밤", "D+2 일 08:10", new[]
            {
                deaf ? "밤사이 뒷산 쪽 하늘로 헬기 불빛이 몇 번 지나갔다." : "밤사이 뒷산 쪽에서 헬기 소리가 몇 번 들렸다.",
                !phone ? "아침, 이웃들이 복도를 바쁘게 오갔다." : deaf ? "아침, 휴대폰 화면이 번쩍이며 진동했다." : "아침, 휴대폰이 요란하게 울렸다.",
            });
            var g = ctx.G;
            g.Day = 2; g.Minute = 8 * 60 + 10;
            ctx.OutdoorEnv = "day";
            ctx.SetLevelState("apartment", "windowView", "day");
            await ctx.Goto("apartment", "home", "interiorDark");
            ctx.Refresh();
            await StoryCommon.EmergencyText(ctx, MountainLogic.DeclareSms, MountainLogic.DeclareTime);
            // one line of law after the text (defense_zone comes with the report card's "back" feedback), then a beat to read it
            await StoryCommon.FactCard(ctx, "통합방위사태", new[] { "defense_stage_c" });
            if (PhoneOn(ctx)) await ctx.Caption(Deaf(ctx) ? "단톡방 알림이 연달아 뜬다." : "단톡방 알림이 연달아 울린다.", null, 2600);
            await Rumour(ctx);
            await Day(ctx);
            await Close(ctx);
        }

        /// <summary>The group chat's "북한군 수십 명": named with the notebook labels, then checked or believed and corrected.</summary>
        static async Task Rumour(StoryContext ctx)
        {
            var g = ctx.G;
            var heard = await ctx.Modal(() => RumorScene.HearClaim(ctx, MountainLogic.RumorId, new[] { "103동 단톡방에 메시지가 연달아 올라온다. 사진은 없다." }));
            var posts = Rumors.Posts(g, MountainLogic.RumorId);
            RumorPost manager = posts.First(p => p.Key == "manager"), official = posts.First(p => p.Key == "official");
            if (heard.Act == "check")
            {
                Rumors.Verify(g, MountainLogic.RumorId);
                bool radio = ctx.Rules.Usable(g, "radio");
                await ctx.Say(radio ? "재난방송" : "관리사무소 안내방송", new[] { official.Text }, null, "공식 안내 (가상)");
                await ctx.Say(manager.From, new[] { manager.Text }, null, manager.Meta);
                ctx.Note("뒷산: 병종사태 · 인원 확인 중 · 외출 자제 (공식 안내)", "official");
                if (PhoneOn(ctx)) // the group chat needs the phone
                {
                    var tell = await ctx.Say("103동 단톡방", new[] { "\"진짜예요? 다들 나가야 하는 거 아니에요?\"" }, new List<TalkChoice>
                    {
                        new("tell", "확인한 내용을 올린다: \"방송은 외출 자제래요. 인원은 확인 중이고요.\""), new("quiet", "그냥 둔다"),
                    }, "답장");
                    if (tell == "tell")
                    {
                        GameRules.RecordEvent(g, "mountain_chat", new JObject { ["posted"] = "correction" });
                        ctx.Toast("\"아 그래요? 짐 싸다가 멈췄어요. 고마워요.\"", "neighbor", 4200);
                    }
                }
            }
            else if (heard.Taken)
            {
                // believed and acted on: 서연 brings the official word. It is a correction, not a check of 정민's own
                // (Rumors.Verify would record rumor_checked and the "checked first" deed), as in Neighbors' spread branch.
                await ctx.Say("정민", "짐을 싸야 하나. 현관의 배낭을 끌어당겼다.", null, "302호");
                bool deaf = Deaf(ctx);
                string ask = PhoneOn(ctx) ? "문자 보셨어요?" : "휴대폰 꺼졌죠?";
                await ctx.Say("오서연", new[]
                {
                    deaf ? $"현관문 틈으로 쪽지가 들어왔다. \"{ask} 방송에선 외출하지 말래요. 산 쪽 길도 막혔대요. — 303호\""
                         : $"정민 씨, {ask} 방송에선 외출하지 말래요. 산 쪽 길도 막혔대요.",
                    "단톡방 글은 누가 쓴 건지도 모르잖아요. 저는 라디오를 계속 들을게요.",
                }, null, "303호 · 문 너머");
                g.SetKnow("misled", null);
                GameRules.RecordEvent(g, "rumor_corrected", new JObject { ["id"] = MountainLogic.RumorId, ["after"] = "believed", ["by"] = "seoyeon" });
                ctx.Note("뒷산: 병종사태 · 외출 자제 (서연이 공식 안내로 확인)", "neighbor");
            }
            else ctx.Toast("확인되지 않은 말이다. 문자대로 집에 있자.", "self", 4200);
            await StoryCommon.FactCard(ctx, "소문을 만났을 때", new[] { "info_rumor", "radio_listen" });
        }

        /// <summary>The day at home: the report (when the traces were seen), the intercom, the window, then the calendar.</summary>
        static async Task Day(StoryContext ctx)
        {
            var g = ctx.G;
            bool hasTraces = g.Events.Any(e => e.Type == TracesEvent);
            int stage = hasTraces ? 0 : 1; // 0 report, 1 intercom, 2 window, 3 end the day
            void Show()
            {
                var steps = new List<ObjectiveStep>();
                if (hasTraces) steps.Add(new ObjectiveStep("어제 본 것 신고하기 (책상)", stage > 0));
                if (stage >= 1) steps.Add(new ObjectiveStep("현관 인터폰 받기 (현관문)", stage > 1));
                if (stage >= 2) steps.Add(new ObjectiveStep("거실 창밖 살피기", stage > 2));
                if (stage >= 3) steps.Add(new ObjectiveStep("달력에서 하루 마치기", false));
                ctx.Objective("외출 자제 · 집에서", stage == 0
                    ? SightingReport.HowBody + " 책상에 앉아 본 것을 정리합니다."
                    : "문자대로 집 안에 머뭅니다. 밖에는 나가지 않습니다.", steps);
            }
            async Task Ring()
            {
                await ctx.Caption(ctx.Settings.Profile == "deaf" ? "현관 인터폰 화면이 깜빡인다." : "띵동— 현관 인터폰이 울린다.", null, 2600);
            }
            if (!hasTraces) await Ring();
            Show();
            const string StayLine = "외출 자제 안내가 나와 있다. 오늘은 집에 있자.";
            await ctx.Roam(new RoamOptions
            {
                Rate = 0,
                On =
                {
                    ["homeDesk"] = async _ =>
                    {
                        if (stage != 0) { ctx.Toast(hasTraces ? "신고는 마쳤다. 경찰에서 다시 연락이 올 수도 있다." : "책상 위 노트북은 꺼져 있다."); return Undefined; }
                        await Report(ctx);
                        stage = 1; Show();
                        await Ring();
                        return Undefined;
                    },
                    ["homeExitDoor"] = async _ =>
                    {
                        if (stage != 1) { ctx.Toast(StayLine); return Undefined; }
                        await Intercom(ctx);
                        stage = 2; Show();
                        await ctx.Caption(Deaf(ctx) ? "창밖 잔디밭 쪽에서 아이들이 모여 무언가를 가리킨다." : "창밖 잔디밭 쪽에서 아이들이 웅성거리는 소리가 들린다.", null, 2800);
                        return Undefined;
                    },
                    ["homeWindow"] = async _ =>
                    {
                        if (stage != 2) { ctx.Toast("산 쪽은 조용하다. 순찰차 한 대가 큰길을 천천히 지나간다."); return Undefined; }
                        await UnknownObject(ctx);
                        stage = 3; Show();
                        return Undefined;
                    },
                    ["homeRadio"] = async _ =>
                    {
                        var official = Rumors.Posts(g, MountainLogic.RumorId).First(p => p.Key == "official");
                        await ctx.Say(ctx.Rules.Usable(g, "radio") ? "재난방송" : "거실 탁자", new[] { ctx.Rules.Usable(g, "radio") ? official.Text : "라디오를 쓸 수 없다. 아침 문자를 다시 읽었다: 외출 자제, 뒷산 출입 금지." }, null, "공식 안내 (가상)");
                        return Undefined;
                    },
                    ["homePlanner"] = async _ =>
                    {
                        await Task.CompletedTask;
                        if (stage >= 3) return (JToken)"done";
                        ctx.Toast("아직 할 일이 남았다.");
                        return Undefined;
                    },
                },
            });
            ctx.Objective(null);
        }

        /// <summary>The report card from home (screen "sighting"), then the reply. Sets know defenseLift.</summary>
        public static async Task Report(StoryContext ctx)
        {
            var g = ctx.G;
            var tr = g.Events.LastOrDefault(e => e.Type == TracesEvent);
            var setup = new SightSetup
            {
                Noted = tr?.Details?["noted"]?.Type == JTokenType.Boolean && (bool)tr.Details["noted"],
                SeenMinute = tr?.Details?["minute"]?.Type == JTokenType.Integer ? (int)tr.Details["minute"] : 9 * 60 + 25,
                Profile = ctx.Settings.Profile ?? "",
                Phone = PhoneOn(ctx),
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
            var res = await ctx.Modal(() => ctx.Ui.Open(Screen, o));
            var details = res as JObject ?? new JObject();
            if (details["played"] == null) details["played"] = false;
            GameRules.RecordEvent(g, "sighting", (JObject)details.DeepClone());
            var r = SightResult.Read(details);
            if (!r.Played) { g.SetKnow(LiftKnow, "late"); return; }
            g.SetKnow(LiftKnow, r.Lift ?? "late");
            ctx.Pass(10);
            string reply = r.Accurate
                ? "알려 주신 산책로 입구와 방향으로 바로 확인하겠습니다. 가방은 만지지 않으셨죠? 잘하셨습니다. 문자 안내대로 집 안에 계세요."
                : r.Guesses > 0
                    ? "확인하겠습니다. 직접 보신 것과 들으신 것을 나눠 말씀해 주시면 더 도움이 됩니다. 집 안에 계세요."
                    : "확인하겠습니다. 생각나는 게 더 있으면 다시 연락 주세요. 집 안에 계세요.";
            if (r.Channel == "office112")
                await ctx.Say("박영수 소장", new[] { "112에 카드 그대로 불러 줬어요. 경찰이 이렇게 전해 달래요.", $"\"{reply}\"" }, null, "관리사무소 · 인터폰");
            else if (r.Channel == "borrow112")
                await ctx.Say(Deaf(ctx) ? "112 문자 회신" : "112 상황실", new[] { reply }, null, "경찰 · 서연의 휴대폰");
            else
                await ctx.Say(r.Channel == "text112" ? "112 문자 회신" : "112 상황실", new[] { reply }, null, "경찰");
            if (r.Noted) ctx.Toast("어제 적어 둔 메모 덕에 시각을 정확히 말할 수 있었다.", "self", 4200);
        }

        /// <summary>A stranger at the door: not opening, talking through the intercom, asking the office (a lost courier).</summary>
        public static async Task Intercom(StoryContext ctx)
        {
            var g = ctx.G;
            bool deaf = Deaf(ctx), phone = PhoneOn(ctx);
            var lines = new[]
            {
                // 3장 runs in the blackout: the intercom works on the building's emergency power (as in 살펴서 대피)
                "정전이지만 현관 인터폰은 건물 비상전원으로 켜져 있다. 화면에 모자를 눌러쓴 남자가 보인다. 큰 가방을 멨다." + (deaf ? " 화면 아래로 말이 글자로 뜬다." : ""),
                "\"저기요, 죄송한데 문 좀 잠깐 열어 주시겠어요? 물 한 잔만요. 길이 다 막혀서요.\"",
            };
            var pick = await ctx.Say("현관 인터폰", lines, new List<TalkChoice>
            {
                new("open", "문을 열어 준다"), new("ask", "문은 닫아 둔 채 인터폰으로 누구신지 묻는다"), new("silent", "대답하지 않고 가만히 있는다"),
            }, "302호 현관");
            string first = pick;
            if (pick == "silent")
                pick = await ctx.Say("현관 인터폰", deaf ? "\"…계세요? 302호 맞죠?\" 인터폰 화면이 한 번 더 깜빡인다." : "\"…계세요? 302호 맞죠?\" 인터폰이 한 번 더 울린다.", new List<TalkChoice>
                {
                    new("ask", "문은 닫아 둔 채 인터폰으로 누구신지 묻는다"), new("open", "문을 열어 준다"),
                }, "302호 현관");
            bool opened = false, asked = false, office = false, police = false;
            if (pick == "ask")
            {
                asked = true;
                var checks = new List<TalkChoice> { new("office", "문은 닫아 둔 채, 관리사무소에 인터폰으로 확인을 부탁한다") };
                if (phone) checks.Add(new TalkChoice("police", deaf ? "112에 문자로 알려 확인을 부탁한다" : "112에 알려 확인을 부탁한다"));
                checks.Add(new TalkChoice("open", "택배 기사라니 문을 연다"));
                var next = await ctx.Say("현관 인터폰", new[]
                {
                    "\"아, 놀라셨죠. 한빛택배 기사예요. 이 동네는 이번 주에 처음 맡았는데, 산 쪽 길이 막혀 돌아왔더니 104동을 못 찾겠고 물도 다 떨어졌어요.\"",
                }, checks, "302호 현관");
                if (next == "office")
                {
                    office = true;
                    await ctx.Say("박영수 소장", new[] { "아, 이번 주에 담당이 바뀐 기사님이에요. 아침에 인사하고 가셨어요. 내가 1층에서 물 한 병 드리고 104동 길 알려 드릴게요.", "잘하셨어요. 요즘 같은 땐 문 열기 전에 꼭 물어봐야 해요." }, null, "관리사무소 · 인터폰");
                }
                else if (next == "police")
                {
                    police = true;
                    await ctx.Say(deaf ? "112 문자 회신" : "112 상황실", new[] { "근처 순찰 중인 경찰관이 가서 확인하겠습니다. 문은 열지 말고 기다려 주세요." }, null, "경찰");
                    ctx.Pass(15);
                    ctx.Toast("경찰관이 확인했다. 길을 잃은 택배 기사였다. 1층에서 물을 받아 갔다.", "site", 4600);
                }
                else opened = true;
            }
            else opened = true;
            if (opened)
                await ctx.Say("택배 기사", new[] { "\"고맙습니다. 산 쪽이 다 막혀서 한참 돌았어요.\" 그는 물을 받아 들고 104동 쪽으로 걸어갔다." }, null, "302호 현관 앞");
            await ctx.Say("정민", opened && asked
                ? "물어본 건 잘했다. 하지만 택배 기사라는 말만 듣고 열었다. 관리사무소에 한 번 확인할 수 있었다."
                : opened
                    ? "괜찮은 사람이었다. 그래도 누구인지 모를 때는 문을 닫은 채 먼저 물어볼 수 있었다."
                    : "길 잃은 택배 기사였다. 수상한 건 옷차림이 아니라 행동이다. 문은 닫아 둔 채 물어보면 된다.", null, "302호");
            await StoryCommon.FactCard(ctx, "현관 앞의 낯선 사람", new[] { "door_talk", "info_appearance" });
            GameRules.RecordEvent(g, "intercom", new JObject { ["first"] = first, ["opened"] = opened, ["asked"] = asked, ["office"] = office, ["police"] = police });
        }

        /// <summary>An unknown object on the lawn by the car park: keep the children away without touching it, then 1338 or the police station.</summary>
        public static async Task UnknownObject(StoryContext ctx)
        {
            var g = ctx.G;
            bool deaf = Deaf(ctx), phone = PhoneOn(ctx);
            // the place is what the living-room window shows (the car park and its lawn); nothing is spawned there
            var lines = new[]
            {
                "주차장 옆 잔디밭 한가운데 은색 비닐 뭉치가 떨어져 있다. 끈이 달렸고, 무엇인지 알 수 없다.",
                "아이 둘이 다가가 막대기로 건드려 보려 한다.",
            };
            var options = new List<(string id, string label)>
            {
                ("shout", deaf ? "창문을 열고 크게 손짓해 아이들을 물러서게 한다" : "창문을 열고 \"만지지 마! 멀리 떨어져!\" 외친다"),
                ("go", "내려가서 직접 치운다"), ("photo", "가까이 가서 사진을 찍어 단톡방에 올린다"),
            };
            string first = null, pick = null;
            var tried = new HashSet<string>();
            while (pick != "shout")
            {
                pick = await ctx.Say("거실 창밖", tried.Count == 0 ? lines : new[] { "아이들은 아직 잔디밭에 있다." },
                    options.Where(c => !tried.Contains(c.id)).Select(c => new TalkChoice(c.id, c.label)).ToList(), "단지 잔디밭 · 미상 낙하물") ?? "shout";
                first ??= pick;
                tried.Add(pick);
                if (pick == "go") await ctx.Say("정민", "신발을 신다가 멈췄다. 무엇인지 모르는 물건을 내가 치울 일은 아니다.", null, "302호");
                else if (pick == "photo") await ctx.Say("정민", "휴대폰을 들다가 내려놓았다. 가까이 가는 것부터가 위험하다.", null, "302호");
                if (pick != "shout") await StoryCommon.FactCard(ctx, "만지지 않기", new[] { "object_no_touch" });
            }
            await ctx.Caption("아이들이 놀라 뒤로 물러선다. 경비원이 달려와 잔디밭 둘레를 막는다.", null, 3000);
            var where = phone
                ? new List<TalkChoice>
                {
                    new("1338", "군부대 주민신고망 1338에 위치를 알린다"),
                    new("police", deaf ? "112 문자로 경찰에 위치를 알린다" : "가까운 파출소(경찰서)에 위치를 알린다"),
                    new("office", "관리사무소에만 말한다"),
                }
                : new List<TalkChoice>
                {
                    new("office1338", "휴대폰이 꺼져 있다. 관리사무소 인터폰으로 1338 신고를 부탁한다"), new("office", "관리사무소에만 말한다"),
                };
            var report = await ctx.Say("정민", "어디에 알릴까?", where, "신고") ?? (phone ? "1338" : "office1338");
            if (report == "office1338")
                await ctx.Say("박영수 소장", new[] { "알겠어요, 1338에 지금 바로 위치를 불러 줄게요. 주차장 옆 잔디밭 한가운데 맞죠?" }, null, "관리사무소 · 인터폰");
            else if (report == "office")
            {
                await ctx.Say("박영수 소장", new[] { "제가 1338에 바로 알릴게요. 위치가 주차장 옆 잔디밭 한가운데 맞죠?" }, null, "관리사무소 · 인터폰");
                await ctx.Say("정민", "다음엔 내가 직접 1338이나 경찰서에 위치를 알려도 된다.", null, "302호");
            }
            else
                await ctx.Say(report == "1338" ? "1338" : deaf ? "112 문자 회신" : "연무파출소", new[] { "위치 확인했습니다. 만지지 말고 사람들이 다가가지 않게만 해 주세요. 곧 처리반이 갑니다." }, null, "신고 접수");
            bool extKnown = g.Knows("extinguisherLocated");
            await ctx.Say("박영수 소장", new[] { "혹시 몰라 1층 현관 소화기를 잔디밭 쪽에 꺼내 두었어요." + (extKnown ? " 3층은 계단 옆에 있는 거 아시죠?" : "") }, null, "관리사무소 · 인터폰");
            ctx.Pass(60);
            await ctx.Caption("한 시간쯤 뒤 군 처리반이 와서 물체를 수거해 갔다. 안에는 젖은 종이 뭉치뿐이었다고 했다.", null, 3600);
            await StoryCommon.FactCard(ctx, "미상 낙하물", new[] { "object_no_touch", "report_object" });
            GameRules.RecordEvent(g, "unknown_object", new JObject { ["first"] = first, ["reported"] = report, ["extKnown"] = extKnown });
        }

        /// <summary>The evening: the lift (or the 통제 kept overnight), then the skip to the fire's night (21:40).</summary>
        static async Task Close(StoryContext ctx)
        {
            var g = ctx.G;
            var lift = g.Know != null && g.Know.TryGetValue(LiftKnow, out var v) && v?.Type == JTokenType.String ? (string)v : "late";
            int at = MountainLogic.LiftMinute(lift) ?? 19 * 60;
            g.Minute = Math.Max(g.Minute, at);
            ctx.OutdoorEnv = "dusk"; // the evening, before the skip to the fire's blackout night
            ctx.SetLevelState("apartment", "windowView", "dusk");
            ctx.Refresh();
            var hhmm = GameRules.Hhmm(g.Minute);
            await StoryCommon.EmergencyText(ctx, MountainLogic.LiftSms(lift, hhmm), "D+2 " + hhmm, "안전안내문자");
            GameRules.RecordEvent(g, "defense_lift", new JObject { ["lift"] = lift });
            await ctx.Skip("D+2 일 저녁", "D+2 일 21:40", new[]
            {
                lift == "late" ? "산 쪽 통제는 밤새 이어졌다. 창밖으로 순찰차 불빛이 오갔다." : "통제가 풀리자 단지에 조금씩 사람 소리가 돌아왔다.",
            });
            g = ctx.G;
            g.Minute = Math.Max(g.Minute, 21 * 60 + 40); // the card's time (the fire checkpoint follows)
        }

        /// <summary>
        /// D+3 morning (Endure.DayThree): when the 통제 was kept overnight ("late"), the promised next-morning text lifts it.
        /// Know defenseLift becomes "morning".
        /// </summary>
        public static async Task MorningAfter(StoryContext ctx)
        {
            var g = ctx.G;
            var last = g.Events.LastOrDefault(e => e.Type == "defense_lift");
            if (last?.Details?["lift"]?.Type != JTokenType.String || (string)last.Details["lift"] != "late") return;
            if (g.Know != null && g.Know.TryGetValue(LiftKnow, out var v) && v?.Type == JTokenType.String && (string)v == "morning") return;
            await StoryCommon.EmergencyText(ctx, MountainLogic.MorningSms, MountainLogic.MorningTime, "안전안내문자");
            g.SetKnow(LiftKnow, "morning");
        }

        // ---------------------------------------------------------------- after the chapter: the record card

        /// <summary>Played the sub-scene this run (the record card follows the chapter card only then).</summary>
        public static bool Played(GameState g) => g.Events.Any(e => e.Type == TracesEvent || e.Type == "sighting");

        /// <summary>"그날, 시민들은": three sourced lines on 1996 and today's line (a fact), between 3장 and 4장.</summary>
        public static async Task RecordCard(StoryContext ctx)
        {
            var g = ctx.G;
            if (!Played(g) || g.Events.Any(e => e.Type == "record_card")) return;
            var body = MountainLogic.RecordFacts.Select(id => ctx.Facts.Text(id)).Where(t => !string.IsNullOrEmpty(t)).ToList();
            if (body.Count == 0) return;
            var src = ctx.Facts.Cite(MountainLogic.RecordFacts[0])?.Source;
            if (!string.IsNullOrEmpty(src)) body.Add($"<size=18><color=#94a3b8>출처: {src}</color></size>");
            var today = ctx.Facts.Cite(MountainLogic.TodayFact);
            if (today != null) body.Add($"<color=#8fd9cf>오늘의 행동요령 ▸</color> {today.Text}");
            await ctx.Card(new CardOptions
            {
                Kind = "record", Title = MountainLogic.RecordTitle, Meta = MountainLogic.RecordMeta, Body = body,
                Actions = new List<CardAction> { new("ok", "다음 장으로", true) },
            });
            GameRules.RecordEvent(g, "record_card", new JObject { ["id"] = "1996" });
        }
    }
}
