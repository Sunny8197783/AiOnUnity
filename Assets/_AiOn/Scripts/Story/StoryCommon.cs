// Port of story/common.js: scenes several chapters reuse — the radio, the phone, emergency texts, the light in your
// hand, the home stock, the photo album and the day ledger before a time skip.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AiOn.Data;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class StoryCommon
    {
        public static bool Has(GameState g, string id) => g.Items.TryGetValue(id, out var e) && e != null && e.Home + e.Bag > 0;

        public const string Area = "[연무구(가상)]";
        /// <summary>% of the radio's charge one listen uses (design M3).</summary>
        public const double RadioDrain = 4;
        /// <summary>RADIO_FREQ of story/prep-actions.js: game frequency of the disaster-broadcast station (fictional).
        /// Kept here so the radio scene does not depend on PrepActions; PrepActions.RadioFreq should be this value.</summary>
        public const double RadioFreq = 102.1;

        /// <summary>JS truthiness of a JSON value (missing, null, false, 0, NaN and "" are false).</summary>
        static bool Truthy(JToken t) => t != null && t.Type switch
        {
            JTokenType.Null or JTokenType.Undefined => false,
            JTokenType.Boolean => (bool)t,
            JTokenType.Integer => (long)t != 0,
            JTokenType.Float => (double)t != 0 && !double.IsNaN((double)t),
            JTokenType.String => ((string)t).Length > 0,
            _ => true,
        };

        static string F(double x) => x.ToString(CultureInfo.InvariantCulture);

        /// <summary>BROADCASTS: script -> lines. Every line is fiction for the scenario; official advice lines quote facts.</summary>
        public static readonly IReadOnlyDictionary<string, Func<GameState, Facts, string[]>> Broadcasts = new Dictionary<string, Func<GameState, Facts, string[]>>
        {
            ["normal"] = (g, f) => new[] { "지금은 평상시 프로그램입니다. (가상 방송)", "비상시에는 이 방송에서 정부와 지자체 안내를 전합니다." },
            ["blackout"] = (g, f) => new[] { "연무구청이 알려 드립니다. (가상 방송)", "오늘 19시 40분쯤 연무동 일대에 정전이 발생했습니다. 원인을 확인하고 있으며 복구 시간은 아직 정해지지 않았습니다.", "라디오와 재난문자로 전달되는 안내를 계속 들어 주십시오." },
            ["shelter"] = (g, f) => new[] { "공습경보가 발령 중입니다. (가상 방송)", "대피소 안에서는 관리자의 안내를 따르고, 출입구와 통로를 막지 마십시오.", f.Text("radio_listen") },
            ["water"] = (g, f) => new[] { "연무구청이 알려 드립니다. (가상 방송)", "단수가 이어지고 있어 연무동 행정복지센터 마당의 비상급수시설을 운영합니다.", $"운영 시간은 오전 9시 30분부터 오후 5시까지, 물통을 가져오십시오. {f.Text("water_point")}" },
            ["rumor"] = (g, f) => new[] { "연무구청이 알려 드립니다. (가상 방송)", "생활문화센터 대피소는 정상 운영 중입니다. 확인되지 않은 정보에 주의해 주십시오.", f.Text("info_rumor") },
            ["crossroads"] = (g, f) => new[] { "연무구청이 알려 드립니다. (가상 방송)", "희망하는 주민은 생활문화센터 임시 거처로 옮길 수 있습니다. 권고이며 명령이 아닙니다.", "집에 머무는 주민도 행정복지센터 비상급수시설을 계속 이용할 수 있습니다.", f.Text("info_rumor") },
            // Today's closures come from this run's conditions, so the broadcast never names an open road.
            ["evac"] = (g, f) => new[] { "연무구청이 알려 드립니다. (가상 방송)", "연무동 주민 이동 지원을 시작합니다. 행정복지센터 앞 임시 집결지에서 오후 2시까지 버스가 출발합니다.",
                $"한 사람당 가방 하나, 신분증을 챙기십시오. 오늘 통제 구간: {string.Join(", ", g.Cond.Closures.Evac.Select(id => Regex.Replace(TownMap.Closures[id].Label, " 통제$", "")))}. 현장 안내를 따르십시오." },
        };

        public static string[] BroadcastLines(StoryContext ctx, string script) =>
            Broadcasts.TryGetValue(script ?? "", out var b) ? b(ctx.G, ctx.Facts) : null;

        /// <summary>What the disaster-broadcast station is saying right now (for the phone radio opened with P).</summary>
        public static string CurrentBroadcast(GameState g)
        {
            bool After(string t) => g.Events.Any(e => e.Type == t);
            if (g.Chapter == "dday") return After("alert") ? "shelter" : After("blackout") ? "blackout" : "normal";
            if (g.Chapter == "endure") return g.Day <= 1 ? "water" : "rumor";
            if (g.Chapter == "crossroads") return "crossroads";
            if (g.Chapter == "evac" || g.Chapter == "ending") return "evac";
            return "normal";
        }

        /// <summary>
        /// Radio scene. Returns the heard lines, or null when it cannot be played or the official station was missed.
        /// Without a working radio the phone's FM app (earphones as the antenna) is offered while the phone has battery;
        /// it costs battery. device "phone" skips the portable radio (the P key). Talks through ctx.Ui: call inside ctx.Modal.
        /// The radio screen is ctx.Ui.Open("radio", { mode, start, battery, extraSeconds, device, stations }) and returns
        /// { official, noted: [{ text, kind }] }. The original passed an onSignal callback for the static sound; the host's
        /// radio screen drives Audio.Loop("radio", on, { clarity }) itself.
        /// </summary>
        public static async Task<IReadOnlyList<string>> RadioScene(StoryContext ctx, string script, string title = "라디오", string device = null)
        {
            var g = ctx.G;
            var dev = device;
            double extraSeconds = 0;
            if (dev == null && Has(g, "radio"))
            {
                dev = "radio";
                if (!g.Items["radio"].Checked)
                {
                    bool flat = g.Items["radio"].Charge == 0; // it ran down (DrainCharge), not one that never had batteries
                    var r = ctx.Rules.Check(g, "radio");
                    if (r.Ok) { extraSeconds = 40; ctx.Pass(2); ctx.Toast(flat ? "다 닳은 건전지를 빼고 새 건전지로 갈아 끼웠다." : "어둠 속에서 건전지를 찾아 넣었다. 시간이 조금 걸렸다."); }
                    else
                    {
                        await ctx.Ui.Talk(new TalkOptions { Speaker = title, Role = "휴대용 라디오", Lines = { flat ? "건전지가 다 닳아 켜지지 않는다. 갈아 끼울 건전지가 없다." : "건전지가 없어 켜지지 않는다.", ctx.Facts.Text("radio_batt") } });
                        GameRules.RecordEvent(g, "radio_blocked", new JObject { ["script"] = script, ["flat"] = flat });
                        dev = null;
                    }
                }
            }
            if (dev == null)
            {
                if (g.Phone.Battery <= 0) { await ctx.Ui.Talk(new TalkOptions { Speaker = title, Role = "라디오 없음", Lines = { "라디오가 없고, 휴대폰도 꺼져 있다." } }); return null; }
                var pick = await ctx.Ui.Talk(new TalkOptions
                {
                    Speaker = title, Role = "라디오 없음",
                    Lines = { "쓸 수 있는 라디오가 없다. 휴대폰 FM 라디오 앱으로 들어 볼까?", $"(배터리가 닳는다 · 지금 {F(g.Phone.Battery)}%)" },
                    Choices = new List<TalkChoice> { new("phone", "휴대폰 FM 라디오를 켠다"), new("no", "그만둔다") },
                });
                if (pick != "phone") return null;
                dev = "phone";
            }
            if (dev == "phone" && g.Phone.Battery <= 0) { await ctx.Ui.Talk(new TalkOptions { Speaker = title, Role = "휴대폰", Lines = { "휴대폰이 꺼져 있다." } }); return null; }
            var lines = BroadcastLines(ctx, script);
            var mode = Drills.LadderStage(g, "radio");
            double start = mode == "free" ? 89.5 : g.Know.TryGetValue("radioFreq", out var rf) && rf != null && rf.Type is JTokenType.Float or JTokenType.Integer ? (double)rf : 89.5;
            var res = await ctx.Ui.Open("radio", new JObject
            {
                ["mode"] = mode, ["start"] = start, ["battery"] = true, ["extraSeconds"] = extraSeconds, ["device"] = dev,
                ["stations"] = new JArray
                {
                    new JObject { ["freq"] = 95.1, ["name"] = "음악 방송", ["kind"] = "music", ["lines"] = new JArray("(잔잔한 음악이 흐른다)") },
                    new JObject { ["freq"] = 99.3, ["name"] = "알 수 없는 방송", ["kind"] = "rumor", ["lines"] = new JArray("…들은 얘긴데요, 그쪽 대피소는 이미 꽉 찼다더라고요… (출처를 밝히지 않는 방송)") },
                    new JObject { ["freq"] = RadioFreq, ["name"] = $"재난방송 주관방송사 라디오 (게임 속 주파수 {F(RadioFreq)}MHz)", ["kind"] = "official", ["lines"] = new JArray(lines ?? Array.Empty<string>()) },
                },
            });
            // The app costs battery only once it was switched on (opening and closing it at once is free); a result without the flag (an older screen) still pays.
            if (dev == "phone") { if (!(res is JObject rp && rp["powered"]?.Type == JTokenType.Boolean && !(bool)rp["powered"])) { DrainBattery(g, 3); ctx.Refresh(); } }
            else
            {
                ctx.Rules.Use(g, "radio");
                if (ctx.Rules.DrainCharge(g, "radio", RadioDrain) == 0) ctx.Toast("라디오 소리가 점점 작아지더니 꺼졌다. 건전지가 다 닳았다.", "self", 4800);
            }
            bool official = res is JObject ro && Truthy(ro["official"]);
            Drills.RecordEncounter(g, "radio", official ? "tuned" : "missed");
            if (official) g.SetKnow("radioFreq", RadioFreq);
            if (res is JObject rj && rj["noted"] is JArray noted)
                foreach (var n in noted)
                    ctx.Note((string)n["text"], (string)n["kind"] == "official" ? "official" : "rumor");
            return official ? lines : null;
        }

        /// <summary>Light in the dark: 'flashlight' if usable, else 'phone' (drains battery), else null.</summary>
        public static string LightSource(StoryContext ctx)
        {
            var g = ctx.G;
            if (g.Items.TryGetValue("flashlight", out var f) && f.Checked && Has(g, "flashlight")) return "flashlight";
            return g.Phone.Battery > 0 ? "phone" : null;
        }

        /// <summary>A disaster text: recorded, noted as official and shown (Ui.Open("emergencyAlert", { kind, text, time })).</summary>
        public static async Task EmergencyText(StoryContext ctx, string text, string time = null, string kind = "위급재난문자")
        {
            var g = ctx.G;
            GameRules.RecordEvent(g, "sms", new JObject { ["kind"] = kind, ["time"] = time });
            if (g.Phone.Battery <= 0) { ctx.Toast("휴대폰이 꺼져 있어 문자를 볼 수 없다. 주변 안내를 따른다.", "site"); return; }
            AiOn.Rendering.AtmosphereDirector.Glitch(0.35f); // the alert tears the picture for a moment (play mode only)
            ctx.Audio.Cue("phoneAlert", new JObject { ["kind"] = kind == "위급재난문자" ? "emergency" : "safety" });
            ctx.Note(text, "official");
            await ctx.Modal(() => ctx.Ui.Open("emergencyAlert", new JObject { ["kind"] = kind, ["text"] = text, ["time"] = time }));
        }

        /// <summary>Official guidance arrives as notices that do not stop play (one toast every 2.6 s), and lands in the phone's messages.</summary>
        public static Task FactCard(StoryContext ctx, string title, IEnumerable<string> ids)
        {
            var lines = ids.Select(id => ctx.Facts.Text(id)).Where(t => !string.IsNullOrEmpty(t)).ToList();
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i];
                GameRules.Note(ctx.G, t, "official", ctx.Engine.LevelId);
                var text = i > 0 ? t : $"{title} — {t}";
                if (i == 0) ctx.Toast(text, "official", 6500);
                else ctx.After(i * 2.6, () => ctx.Toast(text, "official", 6500));
            }
            return Task.CompletedTask;
        }

        public static void DrainBattery(GameState g, double n) => g.Phone.Battery = Math.Max(0, g.Phone.Battery - n);

        /// <summary>Last year's bag snack is past its date (old_bars): eating one is unpleasant, never a fail.</summary>
        public static bool EatOldBars(StoryContext ctx)
        {
            var g = ctx.G;
            if (!g.Items.TryGetValue("old_bars", out var e) || e.Home + e.Bag <= 0) return false;
            ctx.Rules.Consume(g, "old_bars", 1, e.Home > 0 ? "home" : "bag");
            ctx.Stat("health", -4); ctx.Stat("tension", 4);
            GameRules.RecordEvent(g, "ate_old_bars", new JObject());
            ctx.Toast("맛이 조금 이상하고 속이 더부룩하다. 비상식량은 기한을 확인해 바꿔 두면 좋겠다.", "self", 5200);
            return true;
        }

        /// <summary>
        /// 베란다 수납장: what is stored at home. Last year's bag snack carries its warning until the dates are read
        /// (prep '집 안 대비'), and one can be eaten here. Talks through ctx.Ui: call inside ctx.Modal.
        /// </summary>
        public static async Task StockLook(StoryContext ctx)
        {
            var g = ctx.G;
            var db = ctx.Rules.Db;
            string Tag(string id, ItemEntry v)
            {
                var d = db.Get(id);
                if (id == "old_bars") return g.Knows("expiryChecked") ? " (기한 지남)" : " (작년 가방에서 나온 것 · 기한 확인 필요)";
                if (d != null && d.needsCheck == ItemCheck.Batteries && v.Charge == 0) return " (건전지 다 닳음)";
                if (d != null && d.needsCheck != ItemCheck.None && !v.Checked) return " (아직 점검 안 함)";
                return "";
            }
            var lines = g.Items.Where(kv => kv.Value.Home > 0 && db.Get(kv.Key) != null && !(db.Get(kv.Key).fixture && g.Knows("curtain")))
                .Select(kv => $"{db.Get(kv.Key).displayName} {kv.Value.Home}{db.Get(kv.Key).unit ?? ""}{Tag(kv.Key, kv.Value)}").ToList();
            bool old = g.Items.TryGetValue("old_bars", out var ob) && ob.Home > 0;
            var pick = await ctx.Ui.Talk(new TalkOptions
            {
                Speaker = "베란다 수납장", Role = "집 비축품", Lines = { lines.Count > 0 ? string.Join(" · ", lines) : "비어 있다." },
                Choices = old ? new List<TalkChoice> { new("eat", "작년 건빵을 하나 먹어 본다"), new("close", "닫는다") } : null,
            });
            if (pick == "eat") EatOldBars(ctx);
        }

        /// <summary>The family photo album, on a quiet night at home or in the shelter: a little calmer, once a run.</summary>
        public static async Task<bool> LookAlbum(StoryContext ctx)
        {
            var g = ctx.G;
            if (!g.Items.TryGetValue("photo_album", out var e) || e.Home + e.Bag <= 0) return false;
            bool first = !g.Knows("albumSeen");
            await ctx.Ui.Talk(new TalkOptions
            {
                Speaker = "가족사진 앨범", Role = e.Bag > 0 && ctx.Engine.LevelId != "apartment" ? "가방에서 꺼내" : "책장에서 꺼내",
                Lines = { "대구 부모님 집 마당, 졸업식, 회사 첫 출근 날. 한 장씩 넘겨 본다.", first ? "마음이 조금 가라앉는다. 다들 무사할 거라고 믿기로 했다." : "오늘은 한 장만 보고 덮었다." },
            });
            if (first) { g.SetKnow("albumSeen", true); ctx.Stat("tension", -5); GameRules.RecordEvent(g, "album", new JObject()); }
            return true;
        }

        static int Count(GameState g, string id, string where) =>
            g.Items.TryGetValue(id, out var e) && e != null ? (where == "home" ? e.Home : e.Bag) : 0;

        static double Litres(GameState g, string[] from) => from.Sum(k => Count(g, "water_2l", k) * 2 + Count(g, "water_500", k) * 0.5);
        static int Meals(GameState g, string[] from) => new[] { "canned", "rice_instant", "bars", "ramen" }.Sum(id => from.Sum(k => Count(g, id, k)));

        /// <summary>
        /// Day ledger (Ledger) before a time skip: three picks (water, meals, the power hour), then the days are played out.
        /// Returns the result lines for the time-skip text. Talks through ctx.Ui: wrap it in ctx.Modal.
        /// The power choices carry a short note (the original's choice.note); TalkChoice has no such field, so it rides in
        /// Why on an enabled choice — hosts show Why under a choice whether or not it is disabled.
        /// </summary>
        public static async Task<List<string>> DayLedger(StoryContext ctx, int days, string place = "home")
        {
            var g = ctx.G;
            var from = place == "home" ? new[] { "home", "bag" } : new[] { "bag" };
            var at = place == "home" ? "집" : "임시 거처";
            var role = $"앞으로 {days}일 · {at}";
            List<TalkChoice> Opts<T>(LedgerPick<T>[] picks) => picks.Select(p => new TalkChoice(Convert.ToString(p.Id, CultureInfo.InvariantCulture), p.Label)).ToList();
            int Int(string s, int dflt) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : dflt;

            var waterPick = await ctx.Ui.Talk(new TalkOptions
            {
                Speaker = "하루 장부", Role = role,
                Lines =
                {
                    $"앞으로 {days}일을 어떻게 나눠 쓸까? 물, 끼니, 전기 쓰는 시간을 정한다.",
                    place == "home" ? $"마실 물은 지금 약 {F(Litres(g, from))}L. 모자라면 급수소에 다녀와야 한다." : "임시 거처에서는 마실 물을 받을 수 있다.",
                    ctx.Facts.Text("water_sphere"), "하루에 물을 얼마나 마실까?",
                },
                Choices = Opts(Ledger.Picks.Water),
            });
            int water = Int(waterPick, 2);
            bool stove = place == "home" && Count(g, "gas_stove", "home") + Count(g, "gas_stove", "bag") > 0;
            var foodPick = await ctx.Ui.Talk(new TalkOptions
            {
                Speaker = "하루 장부", Role = role,
                Lines =
                {
                    $"먹을 것은 {Meals(g, from)}개 남았다.",
                    place == "shelter" ? "임시 거처에서는 하루 두 번 배식이 있다. 세 번째 끼니는 가방에서 꺼내 먹는다." : stove ? "휴대용 가스레인지가 있어 라면을 끓일 수 있다." : "조리 없이 먹을 수 있는 것부터 먹게 된다.",
                    "하루에 몇 끼를 먹을까?",
                },
                Choices = Opts(Ledger.Picks.Meals),
            });
            int food = Int(foodPick, 2);
            bool radioOk = g.Items.TryGetValue("radio", out var radio) && radio.Checked;
            bool bank = Count(g, "powerbank", "home") + Count(g, "powerbank", "bag") > 0;
            string NoteOf(string id) => id == "radio" ? (radioOk ? $"건전지가 닳는다 (지금 {F(radio.Charge ?? 100)}%)" : "라디오가 켜지지 않는다")
                : id == "powerbank" ? (bank ? "휴대폰 대신 보조배터리를 채워 둔다 · 밖에서 꺼질 때 대비" : "보조배터리가 없다")
                : id == "phone" ? $"지금 {F(g.Phone.Battery)}%" : "";
            var power = await ctx.Ui.Talk(new TalkOptions
            {
                Speaker = "하루 장부", Role = role, Lines = { "전기가 들어오는 하루 한두 시간에는 무엇을 할까?" },
                Choices = Ledger.Picks.Power.Select(p => new TalkChoice(p.Id, p.Label, false, NoteOf(p.Id) is { Length: > 0 } n ? n : null)).ToList(),
            }) ?? "phone";
            var res = Ledger.SimulateDays(g, new LedgerPlan(water, food, power), days, place);
            Ledger.ApplyLedger(ctx.Rules, g, res);
            ctx.Refresh();
            return res.Lines;
        }
    }
}
