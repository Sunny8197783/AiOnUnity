// Prologue (story/prologue.js) played through on FakeStoryHost with two different scripts.
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class PrologueTests
    {
        static (FakeStoryHost host, StoryContext ctx) Rig()
        {
            var host = new FakeStoryHost { Strict = true };
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(7));
            return (host, ctx);
        }

        [Test]
        public void SortsEverySignalRight_TextsTheDriver_AnswersOnlyWhatIsKnown()
        {
            var (host, ctx) = Rig();
            var g = ctx.G;
            foreach (var s in Prologue.Signals) host.AnswerSpeaker(s.From, s.Answer);
            host.AnswerSpeaker("창밖", "text");
            host.AnswerWhen(o => o.Speaker == "윤 팀장" && o.Choices != null, "ok");
            host.AnswerCard("ok");
            // Out of order first: the lead before the desk, the exit before the talk, the desk twice.
            host.QueuePresses("office:coworker", "window", "officeExit", "desk", "desk", "officeExit", "coworker", "coworker", "officeExit");
            host.Run(() => Prologue.Run(ctx));

            Assert.That(host.EventTypes(), Is.EqualTo(new[] { "deed", "signal_sorted", "signal_sorted", "signal_sorted", "signal_sorted", "signal_sorted", "encounter", "talk_lead" }));
            Assert.That((string)g.Events[0].Details["id"], Is.EqualTo("driver_text"));
            Assert.That(g.Events.Where(e => e.Type == "signal_sorted").All(e => (bool)e.Details["ok"]));
            Assert.That(g.Events.Where(e => e.Type == "signal_sorted").Select(e => (string)e.Details["id"]), Is.EqualTo(new[] { "cancel", "partner", "cause", "water", "embassy" }));
            Assert.That((string)g.Skill["info"].Single()["result"], Is.EqualTo("sorted"));
            Assert.That((string)g.Events.Last().Details["pick"], Is.EqualTo("ok"));
            Assert.That(g.Chapter, Is.EqualTo("prologue"));
            Assert.That((g.Day, g.Minute), Is.EqualTo((-2, 18 * 60 + 30)));
            Assert.That(g.Info.Select(i => (i.Text, i.Label, i.Place)), Is.EqualTo(new[]
            {
                ("오늘 배송 취소 접수 1,284건. 지난주 같은 요일의 3.2배입니다.", "site", "office"),
                ("내일부터 서해안 노선 운송이 지연됩니다. (사유는 적혀 있지 않음)", "site", "office"),
            }));

            Assert.That(host.Toasts, Does.Contain("먼저 대시보드를 확인해 보세요."));
            Assert.That(host.Toasts, Does.Contain("이미 확인했습니다. 팀장과 이야기해 보세요."));
            Assert.That(host.Toasts.Count(t => t == "아직 할 일이 남았습니다."), Is.EqualTo(2));
            Assert.That(host.Toasts, Does.Contain("\"내일 봐요. 건전지 잊지 말고.\""));
            Assert.That(host.Toasts.Count(t => t.StartsWith("맞아요 · ")), Is.EqualTo(5));
            Assert.That(host.Talks.Any(t => t.Speaker.EndsWith("에 가깝습니다")), Is.False, "no miss, no explanation");
            Assert.That(host.Talks.Single(t => t.Speaker == "윤 팀장" && t.Lines.Count == 2).Lines[0], Is.EqualTo("그래, 그게 맞아. 모르는 건 모른다고 하는 게 일이야."));

            // The office: dashboard data with three alerts after the sort, entered at its entry spawn.
            Assert.That(host.Calls("enter").First().Text, Is.EqualTo("office:entry"));
            var office = host.Level("office").State;
            Assert.That((int)office["alerts"], Is.EqualTo(3));
            var dash = (JObject)office["dashboard"];
            Assert.That((string)dash["kind"], Is.EqualTo("dashboard"));
            Assert.That((string)dash["updated"], Is.EqualTo("D-3 (화) 17:25 기준"));
            Assert.That(((JArray)dash["rows"]).Select(r => (bool)r[3]), Is.EqualTo(new[] { true, true, false, true, false, true }));
            Assert.That(host.Calls("state").Count(c => c.Text.StartsWith("office.dashboard=")), Is.EqualTo(2));
            Assert.That(host.Chapter, Is.EqualTo(("프롤로그", "평소와 다른 화요일")));
            Assert.That(host.Objectives.Select(o => o.title), Is.EqualTo(new[] { "대시보드 확인하기", "팀장과 이야기하기", "퇴근하기" }));
            Assert.That(host.TimeSkips.Single().from, Is.EqualTo("D-3 화 18:40"));
            Assert.That(host.TimeSkips.Single().text.Count, Is.EqualTo(2));
            Assert.That(host.Warnings.Where(w => !w.Contains("dashboard")), Is.Empty);
            Assert.That(host.PendingSteps + host.PendingTalkAnswers + host.PendingCardAnswers, Is.EqualTo(0));
        }

        [Test]
        public void CallsEverythingAFact_GetsThreeExplanations_AsksAboutWar()
        {
            var (host, ctx) = Rig();
            var g = ctx.G;
            host.Strict = false; // default policy: the first choice everywhere (확인된 사실, 전쟁 질문, text the driver)
            host.AnswerSpeaker("창밖", "look");
            host.QueuePresses("desk", "coworker", "window", "officeExit");
            host.Run(() => Prologue.Run(ctx));

            Assert.That(host.EventTypes(), Is.EqualTo(new[] { "signal_sorted", "signal_sorted", "signal_sorted", "signal_sorted", "signal_sorted", "encounter", "talk_lead" }));
            Assert.That(g.Events.Where(e => e.Type == "signal_sorted").Select(e => (bool)e.Details["ok"]), Is.EqualTo(new[] { true, true, false, false, false }));
            Assert.That((string)g.Skill["info"].Single()["result"], Is.EqualTo("checked"));
            Assert.That((string)g.Events.Last().Details["pick"], Is.EqualTo("war"));
            var explained = host.Talks.Where(t => t.Speaker.EndsWith("에 가깝습니다")).ToList();
            Assert.That(explained.Select(t => t.Speaker), Is.EqualTo(new[] { "\"추정·해석\"에 가깝습니다", "\"소문·미확인\"에 가깝습니다", "\"소문·미확인\"에 가깝습니다" }));
            Assert.That(explained[0].Role, Is.EqualTo("추정·해석"));
            Assert.That(explained[0].Lines.Single(), Is.EqualTo("취소 증가(사실)에 이유를 붙인 해석입니다. 그럴 수도 있지만 확인된 것은 아닙니다."));
            Assert.That(host.Talks.Single(t => t.Speaker == "윤 팀장" && t.Lines.Count == 2).Lines[0], Is.EqualTo("나도 몰라. 모르는 걸 아는 척하면 듣는 사람이 더 불안해져."));
            Assert.That(host.Talks.First(t => t.Speaker == "운영 대시보드").Choices.Select(c => c.Label), Is.EqualTo(new[] { "확인된 사실", "추정·해석", "소문·미확인" }));
            Assert.That(g.Events.Any(e => e.Type == "deed"), Is.False);
            Assert.That((g.Day, g.Minute), Is.EqualTo((-2, 18 * 60 + 30)));
        }
    }
}
