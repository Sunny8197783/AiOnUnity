// Port of story/prologue.js: 프롤로그 · D-3 화요일 17:30 · 한결물류 연무센터 운영팀.
// Signals pile up at work. None of them proves a war; the player sorts what is known from what is guessed.
// The office dashboard the original drew on a canvas is data here: the level state 'dashboard' is
// { kind: "dashboard", title, updated, rows: [[label, value, delta, hot], ...] } and the host draws it.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public static class Prologue
    {
        public sealed class Signal
        {
            public string Id, From, Meta, Text, Answer, Why;
            public Signal(string id, string from, string meta, string text, string answer, string why) { Id = id; From = from; Meta = meta; Text = text; Answer = answer; Why = why; }
        }

        public static readonly IReadOnlyList<Signal> Signals = new[]
        {
            new Signal("cancel", "운영 대시보드", "사내 데이터 · 17:25 집계", "오늘 배송 취소 접수 1,284건. 지난주 같은 요일의 3.2배입니다.", "fact",
                "우리 시스템에서 직접 센 숫자라 확인된 사실입니다. 다만 왜 늘었는지는 이 숫자만으로 알 수 없습니다."),
            new Signal("partner", "협력사 공지", "B물류 공식 메일 · 16:50", "내일부터 서해안 노선 운송이 지연됩니다. (사유는 적혀 있지 않음)", "fact",
                "공식 공지가 왔다는 것은 사실입니다. 사유를 짐작해 덧붙이면 그때부터는 추정이 됩니다."),
            new Signal("cause", "옆자리 동료", "대화", "\"취소가 많은 건 다들 전쟁 날 줄 알아서야. 뻔하지.\"", "guess",
                "취소 증가(사실)에 이유를 붙인 해석입니다. 그럴 수도 있지만 확인된 것은 아닙니다."),
            new Signal("water", "동네 단톡방", "사진·출처 없음", "\"○○동 대형마트 생수 다 털렸대ㅋㅋ 빨리 사러 가\"", "rumor",
                "누가 언제 본 것인지 알 수 없는 말입니다. 확인 전까지는 소문으로 두는 것이 안전합니다."),
            new Signal("embassy", "거래처 담당자", "전화 통화", "\"어느 나라 대사관 직원들이 철수한다던데요.\"", "rumor",
                "출처가 확인되지 않았습니다. 설령 사실이어도 이런 신호 하나가 전쟁을 확정하지는 않습니다."),
        };

        /// <summary>LABELS, in the original's order (the choices are offered in this order).</summary>
        public static readonly IReadOnlyList<(string id, string label)> Labels = new[] { ("fact", "확인된 사실"), ("guess", "추정·해석"), ("rumor", "소문·미확인") };
        static string LabelOf(string id) => Labels.First(l => l.id == id).label;

        /// <summary>The office dashboard (the original's dashboardCanvas(alerts)) as data for the host to draw.</summary>
        public static JObject Dashboard(int alerts)
        {
            JArray Row(string a, string b, string d, bool hot) => new JArray(a, b, d, hot);
            return new JObject
            {
                ["kind"] = "dashboard",
                ["title"] = "한결물류 · 연무센터 운영 대시보드",
                ["updated"] = "D-3 (화) 17:25 기준",
                ["rows"] = new JArray
                {
                    Row("배송 취소 접수", "1,284건", "+220%", true), Row("서해안 노선", "운송 지연 공지", "내일부터", true), Row("수도권 간선", "정상", "—", false),
                    Row("거래처 휴업 알림", "3곳", "+3", alerts > 1), Row("반품 회수", "412건", "+12%", false), Row("기사 결근 연락", "9명", "+6", alerts > 2),
                },
            };
        }

        static Func<StoryPoint, Task<JToken>> H(Func<Task<JToken>> f) => _ => f();
        static List<ObjectiveStep> Steps(bool a, bool b, bool c) =>
            new() { new("대시보드 확인", a), new("팀장과 이야기", b), new("퇴근", c) };

        public static async Task Run(StoryContext ctx)
        {
            var g = ctx.G;
            g.Chapter = "prologue";
            ctx.OutdoorEnv = "afternoon"; ctx.IndoorEnv = "interior"; ctx.Power = true;
            ctx.SetLevelState("office", "dashboard", Dashboard(1));
            ctx.SetLevelState("office", "alerts", 1);
            await ctx.Goto("office", "entry", "interior");
            ctx.Hud.SetChapter("프롤로그", "평소와 다른 화요일");
            ctx.Refresh();
            ctx.Toast("자리로 가서 대시보드를 확인하자.", "self", 4200);

            int sorted = 0; bool talked = false;
            ctx.Objective("대시보드 확인하기", "내 자리의 모니터를 확인합니다.", Steps(false, false, false));
            // The way out is no prompt until the day's work is done (the first E near the door said "퇴근하기").
            World.SetPointEnabled(ctx.Engine, "officeExit", false);
            await ctx.Roam(new RoamOptions
            {
                Doors = false,
                Tick = _ => { if (!talked) World.SetPointEnabled(ctx.Engine, "officeExit", false); return null; },
                On =
                {
                    ["desk"] = H(async () =>
                    {
                        if (sorted > 0) { ctx.Toast("이미 확인했습니다. 팀장과 이야기해 보세요."); return null; }
                        await ctx.Ui.Card(new CardOptions
                        {
                            Kind = "notice", Title = "오늘 들어온 신호 다섯 가지", Meta = "무엇이 확인된 것이고 무엇이 짐작일까?",
                            Body = { "각 신호를 확인된 사실 · 추정·해석 · 소문·미확인 가운데 하나로 나눠 보세요.", "정답보다 \"어떻게 알게 됐는가\"가 중요합니다." },
                            Actions = new List<CardAction> { new("ok", "나눠 보기", true) },
                        });
                        int right = 0, nth = 0;
                        foreach (var s in Signals)
                        {
                            nth++;
                            var pick = await ctx.Ui.Talk(new TalkOptions
                            {
                                Speaker = s.From, Role = s.Meta, Progress = $"신호 {nth}/{Signals.Count}", Lines = { s.Text }, Choices = Labels.Select(l => new TalkChoice(l.id, l.label)).ToList(),
                            });
                            bool ok = pick == s.Answer;
                            right += ok ? 1 : 0;
                            ctx.Event("signal_sorted", new JObject { ["id"] = s.Id, ["pick"] = pick, ["ok"] = ok });
                            if (ok) ctx.Toast($"맞아요 · {LabelOf(s.Answer)}", "self", 2200); // only a miss stops to explain
                            else await ctx.Ui.Talk(new TalkOptions { Speaker = $"\"{LabelOf(s.Answer)}\"에 가깝습니다", Role = LabelOf(s.Answer), Lines = { s.Why }, Tone = "miss" });
                            if (s.Answer == "fact") ctx.Note(s.Text, "site");
                        }
                        sorted = Signals.Count;
                        // The first of the 정보 가려 듣기 encounters (guided): the rumours of D+3 and D+12 build on it.
                        Drills.RecordEncounter(g, "info", right >= Signals.Count - 1 ? "sorted" : "checked");
                        ctx.SetLevelState("office", "dashboard", Dashboard(3));
                        ctx.SetLevelState("office", "alerts", 3);
                        ctx.Objective("팀장과 이야기하기", "윤 팀장이 자리에 있습니다.", Steps(true, false, false));
                        return null;
                    }),
                    ["coworker"] = H(async () =>
                    {
                        if (sorted == 0) { ctx.Toast("먼저 대시보드를 확인해 보세요."); return null; }
                        if (talked) { ctx.Toast("\"내일 봐요. 건전지 잊지 말고.\"", "neighbor"); return null; }
                        await ctx.Ui.Talk(new TalkOptions { Speaker = "윤 팀장", Role = "운영팀장", Lines = { "정민 씨, 서해안 노선은 내일 전부 보류야. 고객 문의 오면 \"지연\"까지만 안내하고, 이유는 짐작해서 말하지 마." } });
                        var pick = await ctx.Ui.Talk(new TalkOptions
                        {
                            Speaker = "윤 팀장", Role = "운영팀장", Lines = { "…다들 뭐라고 하는지는 나도 들었어." },
                            Choices = new List<TalkChoice> { new("war", "다들 전쟁 난다고 하던데요?"), new("ok", "확인된 것만 안내할게요.") },
                        });
                        ctx.Event("talk_lead", new JObject { ["pick"] = pick });
                        await ctx.Ui.Talk(new TalkOptions
                        {
                            Speaker = "윤 팀장", Role = "운영팀장",
                            Lines =
                            {
                                pick == "war" ? "나도 몰라. 모르는 걸 아는 척하면 듣는 사람이 더 불안해져." : "그래, 그게 맞아. 모르는 건 모른다고 하는 게 일이야.",
                                "그래도 집에 비상용품은 한번 봐 둬. 나는 오늘 라디오 건전지부터 사려고. 있는 줄 알았는데 다 썼더라고.",
                            },
                        });
                        talked = true;
                        World.SetPointEnabled(ctx.Engine, "officeExit", true);
                        ctx.Objective("퇴근하기", "사무실 출입문으로 나갑니다.", Steps(true, true, false));
                        return null;
                    }),
                    ["window"] = H(async () =>
                    {
                        var pick = await ctx.Ui.Talk(new TalkOptions
                        {
                            Speaker = "창밖", Role = "물류센터 마당", Lines = { "출차를 기다리던 트럭들이 그대로 서 있다. 상자들이 마당에 쌓여 간다." },
                            Choices = new List<TalkChoice> { new("text", "출차 못 한 김 기사님께 \"무슨 일 있으면 연락 주세요\" 문자를 보낸다"), new("look", "그냥 본다") },
                        });
                        if (pick == "text") ctx.Remember("driver_text");
                        return null;
                    }),
                    ["officeExit"] = H(() =>
                    {
                        if (!talked) { ctx.Toast("아직 할 일이 남았습니다."); return Task.FromResult<JToken>(null); }
                        return Task.FromResult<JToken>("leave");
                    }),
                },
            });

            await ctx.Skip("D-3 화 18:40", "D-2 수 18:30", new[]
            {
                "그날 밤, 정부는 일부 국가로의 출국을 제한한다고 발표했다. (이야기를 위한 가상 조건 · 실제 상황이 아닙니다)",
                "이틀 동안 뉴스는 엇갈렸다. 회사는 단축 근무에 들어갔다. 정민은 오늘 저녁과 내일 하루를 준비에 쓰기로 했다.",
            });
            g.Day = -2; g.Minute = 18 * 60 + 30;
        }
    }
}
