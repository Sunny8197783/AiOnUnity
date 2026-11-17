// RumorScene (story/rumor-scene.js): the hearClaim part of tests/evac-story.test.mjs (the depart() tests that use it
// belong to the Evac port), plus the phone-off wording and BoardWalk.
using System.Linq;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class RumorSceneTests
    {
        static (FakeStoryHost host, StoryContext ctx) Rig(string labelPick, string act = "go")
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(11));
            host.TalkPolicy = o => RumorScene.IsLabels(o) ? labelPick : act;
            return (host, ctx);
        }

        [Test]
        public void AClaimLabelledAnythingButRumourAndActedOnIsBelieved_ARumourLabelSetsItAside()
        {
            Assert.That(RumorScene.TakesAsTrue("rumor"), Is.False); Assert.That(RumorScene.TakesAsTrue("site"), Is.True);
            Assert.That(RumorScene.Believes("official", "go"), Is.True); Assert.That(RumorScene.Believes("official", "check"), Is.False);
            Assert.That(RumorScene.Believes("rumor", "go"), Is.False);

            var (a, actx) = Rig("official");
            var r = a.Run(() => RumorScene.HearClaim(actx, "evac_shortcut"));
            Assert.That(r.Taken, Is.True); Assert.That(Rumors.Misled(a.G), Is.EqualTo("evac_shortcut"));
            Assert.That((string)a.G.Skill["info"].Last()["result"], Is.EqualTo("misled"));

            var (b, bctx) = Rig("rumor");
            var s = b.Run(() => RumorScene.HearClaim(bctx, "evac_shortcut"));
            Assert.That(s.Taken, Is.False); Assert.That(Rumors.Misled(b.G), Is.Null);
            Assert.That((string)b.G.Skill["info"].Last()["result"], Is.EqualTo("sorted"));
            StringAssert.Contains("풀림", string.Join(" ", b.Talks[0].Lines));
        }

        [Test]
        public void TheTalksOfferTheNotebookLabels_ThenGoOrCheck_WithTheirNotes()
        {
            var (h, ctx) = Rig("site", "check");
            var r = h.Run(() => RumorScene.HearClaim(ctx, "evac_shortcut", new[] { "단톡방에 글이 올라왔다." }));
            Assert.That(r.Act, Is.EqualTo("check")); Assert.That(r.Taken, Is.False);
            Assert.That(h.Talks[0].Choices.Select(c => c.Label), Is.EqualTo(new[] { "공식 안내", "현장에서 확인한 것", "이웃에게 들은 것", "소문 · 확인 안 됨" }));
            Assert.That(h.Talks[0].Lines[0], Is.EqualTo("단톡방에 글이 올라왔다."));
            Assert.That(h.Talks[0].Lines.Last(), Is.EqualTo("누가 말했나? 언제 확인했나? 어디를 두고 한 말인가? 이 말은 어떤 정보일까?"));
            Assert.That(h.Talks[1].Lines, Is.EqualTo(new[] { "이 말을 믿고 움직여도 될까?" }));
            Assert.That(h.Talks[1].Choices.Select(c => c.Why), Is.EqualTo(new[] { "이 말대로 한다", "시간이 조금 든다" }));
            Assert.That(h.G.Info.Any(n => n.Label == "rumor" && n.Text.StartsWith(r.Claim.From + ": ")), Is.True);
        }

        [Test]
        public void WithThePhoneDeadTheClaimComesByWordOfMouth_AndNoAnswerMeansRumourThenCheck()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(11));
            host.G.Phone.Battery = 0;
            host.TalkPolicy = _ => null;
            var r = host.Run(() => RumorScene.HearClaim(ctx, "evac_shortcut", new[] { "무시되는 줄" }));
            Assert.That(r.Pick, Is.EqualTo("rumor")); Assert.That(r.Act, Is.EqualTo("check"));
            Assert.That(host.Talks[0].Speaker, Is.EqualTo("복도에서 들린 말"));
            Assert.That(host.Talks[0].Role, Is.EqualTo("누가 확인했는지 모름"));
            Assert.That(host.Talks[0].Lines[0], Is.EqualTo("복도에서 누군가 큰 소리로 전하는 말이 들린다."));
        }

        [Test]
        public void BoardWalk_RoamsUntilANoticeBoardIsRead()
        {
            var host = new FakeStoryHost();
            var ctx = host.CreateContext(TestSupport.Rules, TestSupport.Rules.CreateGame(11));
            int read = 0;
            host.QueuePresses("apartment:homeExitDoor", "apartment:lobbyExit", "town:aptNotice");
            host.Run(async () =>
            {
                await ctx.Goto("apartment", "home");
                await RumorScene.BoardWalk(ctx, "게시판 확인", "게시판으로 갑니다.", () => { read++; return System.Threading.Tasks.Task.CompletedTask; });
            });
            Assert.That(read, Is.EqualTo(1));
            Assert.That(host.LevelId, Is.EqualTo("town"));
            Assert.That(host.Objectives[0].steps[0].Text, Is.EqualTo("단지 게시판 또는 행정복지센터 게시판 읽기"));
        }
    }
}
