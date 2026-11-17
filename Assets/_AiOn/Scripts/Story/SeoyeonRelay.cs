// Port of story/seoyeon.js — "같은 문장, 다른 사람": 정민 texts 서연 from the shelter; 서연 reads it at her own eye height,
// sees why it does not carry over to her, and decides with the notice in front of her.
//
// Screens (IStoryUi.Open):
//   "compose"      { route, observedVia, gateSeenAt, sentAt }  -> { chips, text, edited }          (ui/compose.js compose)
//   "rereadCaption" { text, pin, last, note, fx }               -> "next" | "skip"                  (ui/compose.js rereadCaption)
//     The original drew the re-read in the 3D world (notecheck.js createReread) and the note panel (showNote/hideNote)
//     beside the caption. Here every step carries what to draw as DATA for the host:
//       note: { hand, mode: "phone" | "marked" } shows the note panel, JSON null hides it (absent: unchanged);
//       fx:   [{ op: "eye", pitch }, { op: "crane" }, { op: "path", trail }, { op: "pin", label, at: {x, z}, was, now, scale? },
//              { op: "link", from: {x, z}, to: {x, z} }] in the order the original called them.
//     Before the first step the host gets { op: "start", start: CAFE, reduceMotion } and after the last { op: "dispose" }
//     through Open("reread", { fx }) (result ignored).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class SeoyeonRelay
    {
        /// <summary>Where 서연 starts: in front of 카페 쉼표 (town spawn "cafe"), facing south-west.</summary>
        public static readonly (double X, double Y, double Z, double Yaw) Cafe = (-118, 0, -12.5, Math.PI);

        static JObject At(double x, double z) => new JObject { ["x"] = x, ["z"] = z };
        static JObject Place(string id) { var p = TownMap.Places[id].Pos; return At(p.X, p.Z); }

        /// <summary>The re-read pins' anchors (the original ANCHOR table).</summary>
        public static JObject Anchor(string id) => id switch
        {
            "stairs" => Place("shelterA"),
            "ramp" => Place("shelterB"),
            "notice" => Place("shelterNotice"),
            "gate" => Place("shelterEast"),
            "access" => Place("shelterC"),
            "start0" => At(TownMap.Spawns["home"].Pos.X, TownMap.Spawns["home"].Pos.Z),
            "start1" => At(Cafe.X, Cafe.Z),
            _ => null,
        };

        static int ToMin(string s)
        {
            var p = s.Split(':');
            return int.Parse(p[0], CultureInfo.InvariantCulture) * 60 + int.Parse(p[1], CultureInfo.InvariantCulture);
        }

        static string Str(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;

        public static async Task Run(StoryContext ctx)
        {
            var g = ctx.G;
            var gate = g.Events.FirstOrDefault(e => e.Type == "gate_observed" && Str(e.Details?["who"]) == "jeongmin");
            var gm = gate?.Details?["minute"];
            int gateMinute = gm != null && (gm.Type == JTokenType.Integer || gm.Type == JTokenType.Float) ? (int)(double)gm : g.Minute - 3;
            var relay = Relay.StoreTimes(g, Relay.RelayTimes(g.Minute + 2, gateMinute));
            Relay.ClearHandover(g);
            if (!Elder.Truthy(g.Rel["seoyeon"].Contact))
            {
                await ctx.Card(new CardOptions
                {
                    Kind = "info", Title = "서연에게 연락할 방법이 없다", Meta = "303호 이웃",
                    Body = { "서연의 번호를 모른다. 서연은 오늘 저녁 밖에 있다고 했던 것 같은데….", "준비할 때 이웃과 연락 방법을 정해 두지 않았다." },
                    Actions = new List<CardAction> { new("ok", "계속", true) },
                });
            }
            else
            {
                if (g.Phone.Battery <= 3 && ctx.Rules.Usable(g, "powerbank")) { ctx.Rules.Use(g, "powerbank"); g.Phone.Battery = 45; ctx.Payoff("powerbank", "꺼져 가던 휴대폰을 보조배터리로 살렸다"); }
                if (g.Phone.Battery <= 0)
                {
                    await ctx.Card(new CardOptions
                    {
                        Kind = "info", Title = "휴대폰이 꺼졌다", Meta = "배터리 0%", Body = { "서연에게 문자를 보내려 했지만 휴대폰이 켜지지 않는다." },
                        Actions = new List<CardAction> { new("ok", "계속", true) },
                    });
                }
                else
                {
                    var c = Relay.NoteContext(g);
                    var res = await ctx.Modal(() => ctx.Ui.Open("compose", new JObject
                    {
                        ["route"] = c.Route, ["observedVia"] = c.ObservedVia, ["gateSeenAt"] = relay.GateSeenAt, ["sentAt"] = relay.SentAt,
                    })) as JObject;
                    var chips = res?["chips"] is JObject cj ? cj.ToObject<NoteChips>() : null;
                    Relay.MakeHandover(g, chips, Str(res?["text"]), res?["edited"] is JValue ed && ed.Type == JTokenType.Boolean && (bool)ed);
                    StoryCommon.DrainBattery(g, 2);
                    ctx.Toast($"{ctx.Facts.Text("comms_text")}", "official", 5000);
                }
            }

            // 서연's side. The clock is hers now.
            g.Minute = ToMin(relay.ReceivedAt);
            ctx.Hud.SetChapter("2장", "같은 문장, 다른 사람");
            ctx.Refresh();
            var body = new List<string> { "이제 303호 서연입니다. 서연은 보행 보조기를 쓰고, 병원 진료 뒤 카페에 있다가 경보를 들었습니다." };
            body.AddRange(PrepActions.SeoyeonCard.Select(l => $"· {l}"));
            await ctx.Card(new CardOptions
            {
                Kind = "info", Title = "시점 전환 · 오서연", Meta = $"{relay.ReceivedAt} · 카페 쉼표 앞", Body = body,
                Actions = new List<CardAction> { new("ok", "서연으로", true) },
            });
            ctx.SetLevelState("town", "gates", new JObject { ["east"] = "closed", ["stairs"] = "open", ["ramp"] = "open", ["access"] = "open" });
            ctx.SetLevelState("town", "crowd", "evacuating");
            // This scene is about reading the notice, not dodging shells: the war layer (Danger) holds still.
            ctx.Calm = true;
            try
            {
                await ctx.Goto("town", "cafe");
                ctx.Engine.TeleportTo(Cafe.X, ctx.Engine.PlayerY, Cafe.Z, Cafe.Yaw);
                ctx.SetPlayer?.Invoke("seoyeon");
                ctx.SpeedOverride = 0.55;
                ctx.Refresh();
                if (Relay.StoredHandover(ctx.G) != null) await Reread(ctx);
                else await ctx.Card(new CardOptions
                {
                    Kind = "sms", Title = "받은 문자 없음", Meta = relay.ReceivedAt, Body = { "서연에게 정민의 문자는 오지 않았습니다. 서연은 지금의 현장 안내만으로 판단합니다." },
                    Actions = new List<CardAction> { new("ok", "계속", true) },
                });
                await Walk(ctx);
            }
            finally { ctx.Calm = false; }
            ctx.SpeedOverride = null;
            ctx.SetPlayer?.Invoke("jeongmin");
            g = ctx.G;
            g.Minute = Math.Max(g.Minute, ToMin(relay.ReceivedAt) + 5);
            ctx.Hud.SetChapter("2장", "정전과 경보");
            // Back to 정민: his own goal again, done (the original left 서연's goal on screen).
            ctx.Objective("대피소 안에서", "정민은 대피소에 자리를 잡았습니다.", new[] { new ObjectiveStep("명부 쓰기 (접수대)", true), new ObjectiveStep("자리 잡기", true) });
        }

        static async Task Reread(StoryContext ctx)
        {
            var g = ctx.G;
            var h = Relay.StoredHandover(g);
            var pins = Relay.BuildPins(g);
            var hand = JObject.FromObject(h);
            var engine = ctx.Engine;
            engine.SetControls(false);
            ctx.Hud.SetCinematic(true);
            await ctx.Ui.Open("reread", new JObject
            {
                ["fx"] = new JArray(new JObject { ["op"] = "start", ["start"] = new JObject { ["x"] = Cafe.X, ["z"] = Cafe.Z, ["yaw"] = Cafe.Yaw }, ["reduceMotion"] = ctx.Settings.ReduceMotion }),
            });
            var trail = new JArray((ctx.Trail ?? new List<JObject>()).Select(o => (JToken)o.DeepClone()));
            JArray fx = new JArray();
            JToken note = null; // null = unchanged; JValue null = hide
            void Show(string mode) => note = new JObject { ["hand"] = hand, ["mode"] = mode };
            void Hide() => note = JValue.CreateNull();
            void Crane() { fx.Add(new JObject { ["op"] = "crane" }); fx.Add(new JObject { ["op"] = "path", ["trail"] = trail.DeepClone() }); }
            JObject PlacePin(int i)
            {
                var p = pins[i];
                fx.Add(new JObject { ["op"] = "pin", ["label"] = (i + 1).ToString(CultureInfo.InvariantCulture), ["at"] = Anchor(p.Anchor), ["was"] = p.Was, ["now"] = p.Now });
                if (p.Also != null) fx.Add(new JObject { ["op"] = "pin", ["label"] = "✕", ["at"] = Anchor(p.Also.Anchor), ["was"] = Relay.Entrances[p.Also.Anchor].Label, ["now"] = p.Also.Now, ["scale"] = 2.2 });
                if (p.Line != null) fx.Add(new JObject { ["op"] = "link", ["from"] = Anchor(p.Anchor), ["to"] = Anchor(p.Line) });
                return JObject.FromObject(p);
            }
            async Task<string> Caption(string text, JObject pin = null, bool last = false)
            {
                var o = new JObject { ["text"] = text, ["pin"] = pin ?? (JToken)JValue.CreateNull(), ["last"] = last, ["fx"] = fx };
                if (note != null) o["note"] = note;
                fx = new JArray(); note = null;
                var r = await ctx.Ui.Open("rereadCaption", o);
                return Str(r) == "skip" ? "skip" : "next";
            }
            var steps = new List<Func<Task<string>>>
            {
                () => { engine.SetView("first"); fx.Add(new JObject { ["op"] = "eye", ["pitch"] = -0.5 }); Show("phone"); return Caption("방금 정민으로서 쓴 문장입니다. 서연에게 닿은 것은 이 문장뿐입니다."); },
                () => { Show("marked"); return Caption(h.Kind == "unparsed" ? "이 문장에서 입구·시각·범위를 찾지 못했어요." : "문장을 나눠 봅니다. 표시한 부분이 이 문장이 서연에게 말하는 것입니다."); },
                () => { Hide(); engine.SetView("third"); Crane(); return Caption("정민이 실제로 걸은 길 — 당신은 알고 있지만, 서연은 보지 못했습니다."); },
                () => Caption("정민이 들어갈 수 있던 입구는 둘 다 서연의 기준과 다릅니다.", PlacePin(0)),
                () => Caption($"메시지는 {h.SentAt}에 보낸 것입니다. 지금은 {h.ReceivedAt}, 그 사이 안내판이 바뀌었습니다.", PlacePin(1)),
                () => Caption(string.Join(" ", Relay.ClosingLines(h.Kind)), PlacePin(2), true),
            };
            for (int i = 0; i < steps.Count; i++)
            {
                // Esc: jump to the closing step with every pin placed (step 3+i places pin i).
                if (await steps[i]() == "skip" && i < steps.Count - 1)
                {
                    Hide();
                    if (i < 2) { engine.SetView("third"); Crane(); }
                    for (int j = Math.Max(0, i - 2); j < 2; j++) PlacePin(j);
                    i = steps.Count - 2;
                }
            }
            GameRules.RecordEvent(g, "reread_done", new JObject { ["pins"] = new JArray(pins.Select(p => p.Factor)) });
            await ctx.Ui.Open("reread", new JObject { ["fx"] = new JArray(new JObject { ["op"] = "dispose" }), ["note"] = JValue.CreateNull() });
            ctx.Hud.SetCinematic(false);
            engine.SetView(SettingsView(ctx));
            engine.SetControls(true);
        }

        /// <summary>settings.view: the player's chosen camera ('third' unless the host says otherwise).</summary>
        static string SettingsView(StoryContext ctx) => ViewSetting?.Invoke(ctx) ?? "third";

        /// <summary>StorySettings has no view; the director can supply the player's setting here.</summary>
        public static Func<StoryContext, string> ViewSetting;

        static async Task Walk(StoryContext ctx)
        {
            var g = ctx.G;
            var t = Relay.StoredTimes(g);
            bool read = false;
            void Show() => ctx.Objective("서연 · 지금의 안내로 대피하기", "정민의 문자는 참고만 합니다. 현장 안내판을 보고, 서연에게 맞는 출입구로 갑니다.", new[]
            {
                new ObjectiveStep("생활문화센터 현장 안내판 확인", read), new ObjectiveStep("서연에게 맞는 출입구로", false),
            });
            Show();
            await ctx.Roam(new RoamOptions
            {
                Rate = 2, Doors = false,
                On =
                {
                    ["shelterNotice"] = async _ =>
                    {
                        var pick = await ctx.Ui.Card(new CardOptions
                        {
                            Kind = "notice", Title = "지금 운영 중인 출입구", Meta = $"현장 안내 · {t.AccessOpenAt} 갱신 · 이 시설",
                            Body = { "동쪽 출입구: 폐쇄 유지", "A 계단: 운영", "B 지하주차장 진출입로: 운영 · 차량 통제 · 내리막이 가파름", $"C 경사로: {t.AccessOpenAt} 개방 · 유도블록 · 안내 요원 있음", "어느 입구로 갈지는 서연이 정합니다." },
                            Actions = new List<CardAction> { new("access", "C로 갈게요", true), new("stairs", "A로 갈게요"), new("ramp", "B로 갈게요") },
                        });
                        read = true;
                        GameRules.RecordEvent(g, "notice_read", new JObject { ["who"] = "seoyeon", ["minute"] = g.Minute });
                        GameRules.RecordEvent(g, "notice_choice", new JObject { ["who"] = "seoyeon", ["route"] = pick });
                        ctx.Note($"현장 안내 {t.AccessOpenAt}: C 경사로 개방 (서연이 확인)", "site");
                        if (pick == "access") ctx.Toast($"C 경사로 · {t.AccessOpenAt}에 열림 · 정민이 문자를 보낸 {t.SentAt} 뒤의 일", "site", 5000);
                        Show();
                        return null;
                    },
                },
                Shelter = async point =>
                {
                    if (point.Id == "shelterEast") { ctx.Toast("동쪽 출입구 폐쇄 · 안내판을 확인하세요", "site"); return null; }
                    if (point.Id == "shelterNotice") return null;
                    if (point.Id == "shelterC")
                    {
                        GameRules.RecordEvent(g, "route_attempt", new JObject { ["who"] = "seoyeon", ["route"] = "access", ["minute"] = g.Minute });
                        await ctx.Ui.Card(new CardOptions
                        {
                            Kind = "info", Title = "서연이 출입구 C로 대피했습니다", Meta = GameRules.Hhmm(g.Minute),
                            Body = { "완만한 경사로를 보조기와 함께 내려갔습니다.", ctx.Facts.Text("shelter_rules") },
                            Actions = new List<CardAction> { new("ok", "정민에게로", true) },
                        });
                        return "in";
                    }
                    var route = point.Id == "shelterA" ? "stairs" : "ramp";
                    GameRules.RecordEvent(g, "route_attempt", new JObject { ["who"] = "seoyeon", ["route"] = route, ["minute"] = g.Minute });
                    var pick = await ctx.Ui.Talk(new TalkOptions
                    {
                        Speaker = "오서연", Role = "가까이 와서 보니",
                        Lines =
                        {
                            route == "stairs" ? "계단은 혼자 내려가지 않아요. 보조기와 떨어지지도 않을 거예요." : "이 내리막은 가팔라서 혼자 내려가지 않아요.",
                            $"입구의 안내 요원이 C 경사로가 {t.AccessOpenAt}에 열렸다고 알려 준다.",
                        },
                        Choices = new List<TalkChoice> { new("self", "C 경사로로 계획을 바꾼다"), new("escort", "안내 요원에게 C까지 함께 걸어 달라고 한다") },
                    });
                    GameRules.RecordEvent(g, "help_choice", new JObject { ["who"] = "seoyeon", ["choice"] = pick });
                    if (pick == "escort")
                    {
                        await ctx.Ui.Talk(new TalkOptions { Speaker = "안내 요원", Role = "출입구", Lines = { "어떻게 도와드릴까요?" } });
                        await ctx.Ui.Talk(new TalkOptions { Speaker = "오서연", Role = "도움 요청", Lines = { "C까지 옆에서 같이 걸어 주세요. 보조기는 제가 밀게요.", ctx.Facts.Text("people_ask") } });
                    }
                    ctx.Toast("C 경사로로 계획을 바꿨다.", "self");
                    return null;
                },
            });
        }
    }
}
