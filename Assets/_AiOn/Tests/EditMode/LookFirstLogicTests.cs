// "불나면 살펴서 대피" rules (Game/LookFirstLogic.cs): the 소방청 guide as a verdict table, the clues and their time,
// the where-judgement, the choices and the elevator correction, sealing the door, the partition (boxes first unless 1장
// cleared them; press or hold), the 119 card call, and the result the story reads back.
using System.Linq;
using System.Text.RegularExpressions;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class LookFirstLogicTests
    {
        static LookSituation S(string at, bool smoke, bool way) => new() { FireAt = at, SmokeIn = smoke, WayClear = way };

        static LookFirstLogic Fresh(bool knocked = false, bool cleared = false, bool towel = true, bool tub = false, bool bottle = false, string profile = "", string mode = "look") =>
            new(new LookSetup { Knocked = knocked, PartitionCleared = cleared, Towel = towel, Tub = tub, Bottle = bottle, Profile = profile, Mode = mode });

        // ---------------------------------------------------------------- the guide as a table

        [Test]
        public void OwnHome_LeaveIfYouCan_ElseThePartition_NeverSitItOut()
        {
            Assert.That(LookFirstLogic.Judge(S("home", true, true), "leave").Verdict, Is.EqualTo(LookVerdict.Good));
            Assert.That(LookFirstLogic.Judge(S("home", true, true), "leave").Facts, Does.Contain("fire_own_home"));
            Assert.That(LookFirstLogic.Judge(S("home", true, true), "stay").Verdict, Is.EqualTo(LookVerdict.Wrong));
            Assert.That(LookFirstLogic.Judge(S("home", true, true), "partition").Verdict, Is.EqualTo(LookVerdict.Caution));
            Assert.That(LookFirstLogic.Judge(S("home", true, false), "leave").Verdict, Is.EqualTo(LookVerdict.Wrong));
            Assert.That(LookFirstLogic.Judge(S("home", true, false), "partition").Verdict, Is.EqualTo(LookVerdict.Good));
            Assert.That(LookFirstLogic.Judge(S("home", true, false), "partition").Facts, Does.Contain("fire_own_trapped"));
            Assert.That(LookFirstLogic.Judge(S("home", true, false), "stay").Verdict, Is.EqualTo(LookVerdict.Caution), "sealing is the last resort");
        }

        [Test]
        public void AnotherHome_NoSmoke_StayIn_SmokeAndAClearWay_Leave_SmokeEverywhere_SealOrThePartition()
        {
            Assert.That(LookFirstLogic.Judge(S("other", false, true), "stay").Verdict, Is.EqualTo(LookVerdict.Good));
            Assert.That(LookFirstLogic.Judge(S("other", false, true), "stay").Facts, Does.Contain("fire_stay"));
            Assert.That(LookFirstLogic.Judge(S("other", false, true), "leave").Verdict, Is.EqualTo(LookVerdict.Caution));
            Assert.That(LookFirstLogic.Judge(S("other", true, true), "leave").Verdict, Is.EqualTo(LookVerdict.Good));
            Assert.That(LookFirstLogic.Judge(S("other", true, true), "leave").Facts, Does.Contain("fire_other_smoke"));
            Assert.That(LookFirstLogic.Judge(S("other", true, true), "stay").Verdict, Is.EqualTo(LookVerdict.Caution));
            Assert.That(LookFirstLogic.Judge(S("other", true, false), "stay").Verdict, Is.EqualTo(LookVerdict.Good));
            Assert.That(LookFirstLogic.Judge(S("other", true, false), "stay").Facts, Does.Contain("fire_seal"));
            Assert.That(LookFirstLogic.Judge(S("other", true, false), "partition").Verdict, Is.EqualTo(LookVerdict.Good));
            Assert.That(LookFirstLogic.Judge(S("other", true, false), "leave").Verdict, Is.EqualTo(LookVerdict.Caution), "allowed through the smoke, never the first choice");
        }

        [Test]
        public void TheElevator_IsAlwaysACorrection_ByANeighbour_NeverAPunishment()
        {
            foreach (var s in new[] { S("home", true, true), S("other", false, true), S("other", true, false) })
            {
                var f = LookFirstLogic.Judge(s, "elevator");
                Assert.That(f.Verdict, Is.EqualTo(LookVerdict.Corrected));
                Assert.That(f.Speaker, Is.Not.Empty);
                Assert.That(f.Facts, Is.EqualTo(new[] { "fire_lift" }));
            }
            var L = Fresh(knocked: true);
            L.ToWhere(); L.JudgeWhere("corridor");
            L.Choose("elevator"); L.Choose("elevator");
            Assert.That(L.Phase, Is.EqualTo(LookPhase.Act), "you choose again");
            Assert.That(L.Elevator, Is.EqualTo(2));
            Assert.That(L.Act, Is.Null);
        }

        [Test]
        public void ThePartitionVerdict_SaysTheBoxesComeFirst_WhenTheyWereNotCleared()
        {
            Assert.That(LookFirstLogic.Judge(S("other", true, false), "partition", partitionCleared: false).Text, Does.Contain("짐부터"));
            Assert.That(LookFirstLogic.Judge(S("other", true, false), "partition", partitionCleared: true).Text, Does.Not.Contain("짐부터"));
        }

        // ---------------------------------------------------------------- looking

        [Test]
        public void Looking_TakesTime_AndTheFireGrowsUnlessSomeoneKnockedEarly()
        {
            var L = Fresh();
            L.Examine("handle"); L.Examine("gap");
            Assert.That(L.LookSeconds, Is.EqualTo(6));
            Assert.That(L.Size, Is.EqualTo("small"));
            L.Examine("gap"); // again: free
            Assert.That(L.LookSeconds, Is.EqualTo(6));
            L.Examine("sound"); L.Examine("home");
            Assert.That(L.Size, Is.EqualTo("large"), $"grows after {LookFirstLogic.GrowAfter} s of looking");
            Assert.That(L.Grew, Is.True);
            Assert.That(L.Feedback.Text, Does.Contain("번졌다"));
            Assert.That(L.Notes["handle"], Does.Contain("미지근"), "what was seen stays as it was then");
            Assert.That(L.Now.WayClear, Is.False);

            var k = Fresh(knocked: true);
            foreach (var c in k.AllClues) k.Examine(c.Id);
            Assert.That(k.Size, Is.EqualTo("small"), "the elder knocked: it was caught small");
            Assert.That(k.Examined, Has.Count.EqualTo(6));
        }

        [Test]
        public void TheHandle_IsTouchedWithTheBackOfTheHand_AndPointsAtFireDoor()
        {
            var L = Fresh();
            var f = L.Examine("handle");
            Assert.That(L.Clue("handle").Label, Does.Contain("손등"));
            Assert.That(f.Facts, Is.EqualTo(new[] { "fire_door" }));
            Assert.That(f.Text, Does.Contain("뜨겁지는 않다"));
        }

        [Test]
        public void TheDeafMode_FeelsTheDoorInsteadOfListening()
        {
            var L = Fresh(profile: "deaf");
            Assert.That(L.Clue("sound").Label, Does.Contain("진동"));
            Assert.That(L.Clue("broadcast").Label, Does.Contain("인터폰"));
        }

        [Test]
        public void Where_RightWithTwoClues_IsGood_WithOne_Caution_Wrong_IsExplained()
        {
            var a = Fresh(knocked: true);
            a.Examine("handle"); a.Examine("gap"); a.ToWhere();
            Assert.That(a.JudgeWhere("corridor").Verdict, Is.EqualTo(LookVerdict.Good));
            Assert.That(a.Phase, Is.EqualTo(LookPhase.Act));

            var b = Fresh(knocked: true);
            b.Examine("gap"); b.ToWhere();
            Assert.That(b.JudgeWhere("corridor").Verdict, Is.EqualTo(LookVerdict.Caution));

            var c = Fresh(knocked: true);
            c.ToWhere();
            var f = c.JudgeWhere("home");
            Assert.That(f.Verdict, Is.EqualTo(LookVerdict.Wrong));
            Assert.That(f.Text, Does.Contain("304호 앞"));
            Assert.That(c.Phase, Is.EqualTo(LookPhase.Act), "a wrong guess is corrected and the step goes on");

            var d = Fresh();
            d.ToWhere();
            Assert.That(d.JudgeWhere("nowhere"), Is.Null, "not a choice");
            Assert.That(d.Phase, Is.EqualTo(LookPhase.Where));
        }

        // ---------------------------------------------------------------- the choices

        [Test]
        public void Leaving_WhileItIsSmall_IsGood_AndEndsTheScreen()
        {
            var L = Fresh(knocked: true);
            L.Examine("handle"); L.Examine("gap"); L.ToWhere(); L.JudgeWhere("corridor");
            var f = L.Choose("leave");
            Assert.That(f.Verdict, Is.EqualTo(LookVerdict.Good));
            Assert.That(L.Phase, Is.EqualTo(LookPhase.Done));
            var o = LookFirstLogic.Outcome.Read(L.Result());
            Assert.That(o.Played, Is.True);
            Assert.That(o.Act, Is.EqualTo("leave"));
            Assert.That(o.Verdict, Is.EqualTo("good"));
            Assert.That(o.WhereOk, Is.True);
            Assert.That(o.Clues, Is.EqualTo(2));
        }

        [Test]
        public void TheWheelchairMode_LeavesWithHelp()
        {
            Assert.That(LookFirstLogic.ActLabel("leave", "wheelchair"), Does.Contain("도움"));
            Assert.That(LookFirstLogic.ActLabel("leave", ""), Does.Contain("계단"));
        }

        [Test]
        public void Staying_SealsTheGap_ShutsTheWindows_MovesAway_ThenCalls119()
        {
            var L = Fresh(towel: true, tub: true);
            L.ToWhere(); L.JudgeWhere("corridor"); L.Choose("stay");
            Assert.That(L.Phase, Is.EqualTo(LookPhase.Seal));
            L.DoSeal("towel");
            Assert.That(L.Wet, Is.EqualTo("tub"), "the water kept in 1장 wets the towel");
            L.DoSeal("towel");
            Assert.That(L.Sealed, Has.Count.EqualTo(1), "once");
            L.DoSeal("window");
            Assert.That(L.Phase, Is.EqualTo(LookPhase.Seal));
            L.DoSeal("away");
            Assert.That(L.Phase, Is.EqualTo(LookPhase.Report));
            Assert.That(L.Cards.Single(c => c.Id == "me").Text, Does.Contain("302호 안"));
        }

        [Test]
        public void Sealing_WithWhatThereIs()
        {
            LookFirstLogic Sealed(bool towel, bool tub, bool bottle)
            {
                var L = Fresh(towel: towel, tub: tub, bottle: bottle);
                L.ToWhere(); L.JudgeWhere("corridor"); L.Choose("stay"); L.DoSeal("towel");
                return L;
            }
            Assert.That(Sealed(true, false, true).Wet, Is.EqualTo("bottle"));
            Assert.That(Sealed(true, false, false).Wet, Is.EqualTo("dry"));
            Assert.That(Sealed(true, false, false).Feedback.Verdict, Is.EqualTo(LookVerdict.Caution));
            Assert.That(Sealed(false, true, false).Wet, Is.EqualTo("cloth"));
            Assert.That(Sealed(false, false, false).Wet, Is.EqualTo("dry"));
        }

        [Test]
        public void ThePartition_BoxesFirstUnlessCleared_ThenPressOrHold_ThenTheNextDoorCall()
        {
            var L = Fresh(cleared: false);
            L.ToWhere(); L.JudgeWhere("corridor"); L.Choose("partition");
            Assert.That(L.Phase, Is.EqualTo(LookPhase.Partition));
            Assert.That(L.BoxesLeft, Is.EqualTo(LookFirstLogic.Boxes));
            L.PushPartition();
            Assert.That(L.Push, Is.EqualTo(0), "the boxes are in the way");
            for (int i = 0; i < LookFirstLogic.Boxes; i++) L.MoveBox();
            Assert.That(L.BoxesLeft, Is.EqualTo(0));
            for (int i = 0; i < LookFirstLogic.Pushes - 1; i++) L.PushPartition();
            Assert.That(L.PartitionOpen, Is.False);
            L.PushPartition();
            Assert.That(L.PartitionOpen, Is.True);
            Assert.That(L.Phase, Is.EqualTo(LookPhase.Report));
            Assert.That(L.Cards.Single(c => c.Id == "me").Text, Does.Contain("301호"), "the call is made from next door");
            Assert.That((int)L.Result()["partition"]["boxes"], Is.EqualTo(LookFirstLogic.Boxes));

            var held = Fresh(cleared: true);
            held.ToWhere(); held.JudgeWhere("corridor"); held.Choose("partition");
            Assert.That(held.BoxesLeft, Is.EqualTo(0), "cleared in 1장");
            for (int i = 0; i < 60 && !held.PartitionOpen; i++) held.PushPartition(0.1);
            Assert.That(held.PartitionOpen, Is.True, "holding (or the toggle) gets through too");
            Assert.That((int)held.Result()["partition"]["boxes"], Is.EqualTo(0));
        }

        // ---------------------------------------------------------------- the 119 call

        static LookFirstLogic AtReport(string mode = "report", string caller = "lobby") => new(new LookSetup { Mode = mode, Caller = caller });

        [Test]
        public void TheCall_PlaceWhatPeopleAndStayingOnTheLine_IsFull()
        {
            var L = AtReport();
            Assert.That(L.Phase, Is.EqualTo(LookPhase.Report), "report mode opens on the call");
            Assert.That(L.Cards.Single(c => c.Id == "me").Text, Does.Contain("1층"));
            foreach (var id in new[] { "fire", "place", "what", "me", "line" }) L.Toggle(id);
            var f = L.Submit();
            Assert.That(L.Report.Score, Is.EqualTo(LookReport.Full));
            Assert.That(f.Verdict, Is.EqualTo(LookVerdict.Good));
            Assert.That(f.Facts, Does.Contain("fire_report"));
            Assert.That(L.Phase, Is.EqualTo(LookPhase.Done));
            Assert.That(L.Cards.Single(c => c.Id == "place").Text, Does.Contain("103동 3층"));
        }

        [Test]
        public void TheCall_HangingUp_AVagueAddress_AndAGuess_AreNamed()
        {
            var L = AtReport();
            foreach (var id in new[] { "apt", "what", "guess", "line", "hangup" }) L.Toggle(id);
            var f = L.Submit();
            Assert.That(L.Report.Place, Is.False);
            Assert.That(L.Report.Vague, Is.True);
            Assert.That(L.Report.Line, Is.False, "hanging up cancels staying on the line");
            Assert.That(L.Report.Guess, Is.True);
            Assert.That(f.Text, Does.Contain("동과 층, 호수"));
            Assert.That(f.Text, Does.Contain("끊지 않는다"));
            Assert.That(f.Verdict, Is.Not.EqualTo(LookVerdict.Good));
        }

        [Test]
        public void TheCall_TogglesCards_UpToTheLimit_AndNeedsOneToSpeak()
        {
            var L = AtReport();
            Assert.That(L.Submit(), Is.Null, "nothing to say yet");
            Assert.That(L.Phase, Is.EqualTo(LookPhase.Report));
            L.Toggle("fire"); L.Toggle("fire");
            Assert.That(L.Picked, Is.Empty);
            foreach (var c in L.Cards) L.Toggle(c.Id);
            Assert.That(L.Picked, Has.Count.EqualTo(LookFirstLogic.MaxCards));
        }

        // ---------------------------------------------------------------- result and words

        [Test]
        public void Outcome_ReadsTheResult_AndANothingResult()
        {
            var none = LookFirstLogic.Outcome.Read(new JObject());
            Assert.That(none.Played, Is.False);
            Assert.That(none.Act, Is.Null);
            var nulls = LookFirstLogic.Outcome.Read(Fresh().Result()); // seal / partition / report are JSON nulls
            Assert.That(nulls.Played, Is.False);
            Assert.That(nulls.Wet, Is.Null);

            var L = Fresh(towel: true, bottle: true);
            L.Examine("gap"); L.Examine("handle"); L.ToWhere(); L.JudgeWhere("corridor"); L.Choose("elevator"); L.Choose("stay");
            foreach (var s in LookFirstLogic.SealSteps) L.DoSeal(s.id);
            foreach (var id in new[] { "fire", "place", "what", "me", "line" }) L.Toggle(id);
            L.Submit();
            var o = LookFirstLogic.Outcome.Read(JToken.Parse(L.Result().ToString()));
            Assert.That(o.Act, Is.EqualTo("stay"));
            Assert.That(o.Verdict, Is.EqualTo("caution"), "the way was still clear: leaving came first");
            Assert.That(o.Wet, Is.EqualTo("bottle"));
            Assert.That(o.Elevator, Is.EqualTo(1));
            Assert.That(o.Report.Played, Is.True);
            Assert.That(o.Report.Score, Is.EqualTo(LookReport.Full));
            Assert.That(o.Located, Is.EqualTo(2));
        }

        [Test]
        public void EveryFactTheStepNames_IsARealSourcedFact()
        {
            var facts = FakeStoryHost.LoadFacts();
            var named = LookFirstScene.FactIds.ToList();
            foreach (var s in new[] { S("home", true, true), S("home", true, false), S("other", false, true), S("other", true, true), S("other", true, false) })
                foreach (var a in LookFirstLogic.ActIds) named.AddRange(LookFirstLogic.Judge(s, a).Facts);
            named.AddRange(LookFirstLogic.Clues(new LookSetup()).Where(c => c.Fact != null).Select(c => c.Fact));
            foreach (var id in named.Distinct())
            {
                Assert.That(facts.Text(id), Is.Not.Empty, id);
                Assert.That(facts.Cite(id)?.Source, Is.Not.Empty, id);
            }
            Assert.That(named.Distinct().Except(LookFirstScene.FactIds), Is.Empty, "the screen is given every fact it may show");
        }

        [Test]
        public void TheWords_UseNoUncheckedNumbersOrPlaces()
        {
            var L = Fresh();
            var words = L.AllClues.SelectMany(c => new[] { c.Label, c.Small, c.Large })
                .Concat(LookFirstLogic.Clues(new LookSetup { Profile = "deaf" }).SelectMany(c => new[] { c.Label, c.Small, c.Large }))
                .Concat(LookFirstLogic.ReportCards("home").Concat(LookFirstLogic.ReportCards("next")).Concat(LookFirstLogic.ReportCards("lobby")).Select(c => c.Text))
                .Concat(LookFirstLogic.ActIds.Select(a => LookFirstLogic.ActLabel(a, "")))
                .Concat(new[] { "home", "other" }.SelectMany(at => new[] { true, false }.SelectMany(sm => new[] { true, false }.SelectMany(w => LookFirstLogic.ActIds.Select(a => LookFirstLogic.Judge(S(at, sm, w), a, false).Text)))))
                .Concat(new[] { LookFirstLogic.LookBody, LookFirstLogic.ReportBody });
            foreach (var w in words)
            {
                Assert.That(w, Does.Not.Contain("욕실"), w);
                Assert.That(w, Does.Not.Contain("2분"), w);
                Assert.That(Regex.IsMatch(w, @"\b(1337|111|113)\b"), Is.False, w);
                Assert.That(Regex.IsMatch(w, @"\d+\s*(cm|㎝|㎡)|\d+\s*m\b"), Is.False, w);
            }
        }

        // ---------------------------------------------------------------- review fixes (11-21)

        [Test]
        public void TheWhatCard_SaysOnlyWhatIsSeen_AndHowBigItIs()
        {
            foreach (var caller in new[] { "home", "next", "lobby" })
                foreach (var size in new[] { "small", "large" })
                {
                    var what = LookFirstLogic.ReportCards(caller, size).Single(c => c.Kind == "what").Text;
                    Assert.That(what, Does.Not.Contain("촛불"), "no cause in the right card");
                    Assert.That(what, Does.Contain("304호 앞"));
                    Assert.That(what.Contains("천장"), Is.EqualTo(size == "large"));
                }
            var L = Fresh(cleared: true);
            for (int i = 0; i < 3; i++) L.Examine(new[] { "broadcast", "sound", "handle" }[i]); // 16 s: it grew
            L.ToWhere(); L.JudgeWhere("corridor"); L.Choose("stay");
            Assert.That(L.Cards.Single(c => c.Id == "what").Text, Does.Contain("천장"));
            var lobby = new LookFirstLogic(new LookSetup { Mode = "report", Caller = "lobby", Size = "large" });
            Assert.That(lobby.Size, Is.EqualTo("large"));
            Assert.That(lobby.Cards.Single(c => c.Id == "what").Text, Does.Contain("천장"));
            Assert.That(LookSetup.FromOptions(new LookSetup { Size = "large" }.ToJson()).Size, Is.EqualTo("large"));
        }

        [Test]
        public void TheWhereFeedback_NamesOnlyTheCluesLookedAt()
        {
            var two = Fresh();
            two.Examine("gap"); two.Examine("sound");
            two.ToWhere();
            var f = two.JudgeWhere("corridor");
            Assert.That(f.Verdict, Is.EqualTo(LookVerdict.Good));
            Assert.That(f.Text, Does.Not.Contain("손잡이"), "the handle was never touched");
            Assert.That(f.Text, Does.Contain("문틈").And.Contain("소리"));

            var one = Fresh();
            one.Examine("handle"); one.ToWhere();
            Assert.That(one.JudgeWhere("corridor").Text, Does.Contain("단서 하나로는"));
            var none = Fresh();
            none.ToWhere();
            Assert.That(none.JudgeWhere("corridor").Text, Does.Contain("아무 단서 없이"));

            var wrong = Fresh();
            wrong.ToWhere();
            Assert.That(wrong.JudgeWhere("home").Tag, Is.EqualTo("✕ 다시 보기"), "a wrong guess is not 'against the guide'");

            var late = Fresh();
            late.Examine("broadcast"); late.Examine("sound"); late.Examine("handle"); // the handle first touched after the fire grew
            Assert.That(late.Notes["handle"], Does.Not.Contain("아까보다"));
        }

        [Test]
        public void HoldingThePartition_RaisesNoChange_UntilItGives()
        {
            var L = Fresh(cleared: true);
            L.ToWhere(); L.JudgeWhere("corridor"); L.Choose("partition");
            int changed = 0;
            L.Changed += () => changed++;
            L.PushPartition(0.1);
            L.PushPartition(0.1);
            Assert.That(changed, Is.EqualTo(0), "the screen moves its own gauge; Changed would rebuild the page");
            Assert.That(L.Push, Is.GreaterThan(0));
            L.PushPartition();
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(L.Feedback.Verdict, Is.EqualTo(LookVerdict.Step));
            Assert.That(L.Feedback.Text, Does.Contain("발로 찬다"));
            for (int i = 0; i < 60 && !L.PartitionOpen; i++) L.PushPartition(0.1);
            Assert.That(L.Phase, Is.EqualTo(LookPhase.Report));
            Assert.That(changed, Is.EqualTo(2), "breaking through is one change");
            Assert.That(LookFirstLogic.KickLabel(""), Does.Contain("발로 차서"));
            Assert.That(LookFirstLogic.KickLabel("wheelchair"), Does.Not.Contain("발로"));
        }

        [Test]
        public void DeafMode_ReadsTheIntercom_AndTheElevatorCorrectionIsSeen()
        {
            var deaf = LookFirstLogic.Clues(new LookSetup { Profile = "deaf" }).Single(c => c.Id == "broadcast");
            Assert.That(deaf.Small, Does.StartWith("(인터폰 화면 글자)"));
            Assert.That(deaf.Small, Does.Not.Contain("방송"));
            var L = Fresh(profile: "deaf");
            L.ToWhere(); L.JudgeWhere("corridor");
            var f = L.Choose("elevator");
            Assert.That(f.Text, Does.Contain("가리킨다").And.Not.Contain("외친다"));
            Assert.That(Fresh().AllClues.Single(c => c.Id == "broadcast").Small, Does.StartWith("(비상 방송)"));
        }

        [Test]
        public void TheCall_DoesNotClaimAnOrder_AndAThinCallIsNotAgainstTheGuide()
        {
            Assert.That(LookFirstLogic.ReportBody, Does.Not.Contain("순서"));
            var L = new LookFirstLogic(new LookSetup { Mode = "report", Caller = "lobby" });
            L.Toggle("hurry");
            var f = L.Submit();
            Assert.That(f.Verdict, Is.EqualTo(LookVerdict.Wrong));
            Assert.That(f.Tag, Is.EqualTo("✕ 다시 보기"));
            Assert.That(f.Text, Does.Not.Contain("가장 먼저"));
        }
    }
}
