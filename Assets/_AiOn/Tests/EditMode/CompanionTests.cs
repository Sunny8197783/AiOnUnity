using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AiOn.Game;
using AiOn.Story;
using AiOn.Story.Testing;
using NUnit.Framework;

namespace AiOn.Tests
{
    /// <summary>
    /// 도우미 모드 (competition upgrade): the "함께 생각해 보기" card at a judgement and the F1 hint. The helper's words are
    /// open questions and control tips; a hint's safety line is always a checked fact by id.
    /// </summary>
    public class CompanionTests
    {
        [TearDown]
        public void Clean() { Companion.Reset(); Companion.On = false; EasyKorean.On = false; }

        [Test]
        public void Topics_come_from_the_words_on_screen()
        {
            Assert.AreEqual("fire", Companion.Topic("우리 집 안으로 연기나 불길이 들어오지는 않았다. 어떻게 할까?"));
            Assert.AreEqual("rumor", Companion.Topic("확인되지 않은 말이다. 어떻게 할까?"));
            Assert.AreEqual("alert", Companion.Topic("사이렌이 울린다"));
            Assert.AreEqual("alert", Companion.Topic("지하시설이나 유리창 없는 건물 안으로. 엘리베이터는 쓰지 않습니다."));
            Assert.AreEqual("shelter", Companion.Topic("접수대에서 명부를 쓰고 자리를 잡습니다."));
            Assert.AreEqual("cpr", Companion.Topic("할아버지가 쓰러졌다"));
            Assert.AreEqual("report", Companion.Topic("수상한 사람을 봤다"));
            Assert.AreEqual("evac", Companion.Topic("남을까, 떠날까"));
            Assert.IsNull(Companion.Topic("오늘 저녁은 무엇을 먹을까"));
            Assert.IsNull(Companion.Topic(null));
            Assert.AreEqual("fire", Companion.ScreenTopic("lookfirst"));
            Assert.AreEqual("report", Companion.ScreenTopic("sighting"));
            Assert.IsNull(Companion.ScreenTopic("inventory"));
        }

        [Test]
        public void A_judgement_asks_what_you_would_do_then_two_talking_points()
        {
            var t = Companion.Discuss("확인되지 않은 말이다. 어떻게 할까?", new[] { "단톡방에 바로 전한다", "", "공식 안내를 확인한다" });
            Assert.AreEqual(Companion.DiscussTitle, t.Title);
            Assert.AreEqual("rumor", t.Topic);
            Assert.AreEqual(3, t.Prompts.Count);
            Assert.AreEqual(Companion.FirstPrompt, t.Prompts[0]);
            CollectionAssert.AreEqual(new[] { "단톡방에 바로 전한다", "공식 안내를 확인한다" }, t.Choices, "blank choices dropped, order kept");
            // the topic can come from the choices, or be given (a screen's own judgement)
            Assert.AreEqual("fire", Companion.Discuss("어떻게 할까?", new[] { "계단으로 나간다", "칸막이를 넘는다" }).Topic);
            Assert.AreEqual("report", Companion.Discuss(null, null, "report").Topic);
            Assert.AreEqual("evac", Companion.Discuss("지금 내 상황", new[] { "집에 남는다", "생활문화센터 임시 거처로 옮긴다" }).Topic, "4장's decision card");
            Assert.AreEqual("어떻게 할까?", Companion.Discuss(" ", null).Question);
            Assert.AreEqual(3, Companion.Discuss("아무 말", null).Prompts.Count, "a default pair when no topic fits");
        }

        [Test]
        public void The_helpers_words_are_questions_and_tips_never_new_safety_numbers()
        {
            var prompts = new[] { "cpr", "fire", "report", "rumor", "shelter", "alert", "evac", "bag", "care", "reflect", null }.SelectMany(Companion.PromptsFor).ToList();
            prompts.Add(Companion.FirstPrompt);
            foreach (var p in prompts)
            {
                Assert.That(p.TrimEnd(), Does.EndWith("?").Or.EndWith("요."), p);
                Assert.IsFalse(Regex.IsMatch(p, @"\d"), "no numbers in a talking point: " + p);
            }
            foreach (var tip in Companion.ScreenTips.Values)
                Assert.IsFalse(Regex.IsMatch(tip, @"(?<!F)\d"), "a tip explains the controls, it states no figure: " + tip);
            foreach (var p in prompts.Concat(Companion.ScreenTips.Values))
                CollectionAssert.IsEmpty(EasyKorean.HardWords.Where(p.Contains), p);
        }

        [Test]
        public void Every_hint_fact_is_a_checked_fact()
        {
            var facts = FakeStoryHost.LoadFacts();
            foreach (var kv in Companion.TopicFact) Assert.IsNotNull(facts.Text(kv.Value), kv.Key + " -> " + kv.Value);
        }

        [Test]
        public void The_hint_follows_the_objective_and_its_next_step()
        {
            var steps = new List<ObjectiveStep> { new("명부 쓰기 (접수대)", true), new("자리 잡기") };
            var h = Companion.Hint(null, "대피소 안에서", "접수대에서 명부를 쓰고 자리를 잡습니다.", steps);
            Assert.AreEqual("도우미 힌트 · 대피소 안에서", h.Title);
            CollectionAssert.AreEqual(new[] { "접수대에서 명부를 쓰고 자리를 잡습니다.", "다음 할 일: 자리 잡기" }, h.Lines);
            Assert.AreEqual("shelter", h.Topic, "명부, 접수대");
            Assert.AreEqual("shelter_rules", h.FactId);

            // shown plain in 쉬운 한국어
            EasyKorean.On = true;
            var plain = Companion.Hint(null, "대피소 안에서", "접수대에서 명부를 쓰고 자리를 잡습니다.", steps, EasyKorean.Of);
            Assert.AreEqual("다음 할 일: 앉을 자리 정하기", plain.Lines[1]);
        }

        [Test]
        public void Choices_on_the_objective_are_listed_not_ordered()
        {
            var steps = new List<ObjectiveStep> { new("집에 남기") { Choice = true }, new("대피소로 가기") { Choice = true } };
            var h = Companion.Hint(null, "오늘 어떻게 할까", "", steps);
            Assert.AreEqual("고를 수 있는 것: 집에 남기 / 대피소로 가기", h.Lines.Single());
        }

        [Test]
        public void An_open_screen_wins_and_the_quiz_gets_no_answers()
        {
            Companion.PushScreen("cpr");
            var h = Companion.Hint(Companion.Screen, "대피소 안에서", "쓰러진 할아버지 곁을 지킵니다.", null);
            Assert.AreEqual("도우미 힌트 · 이 화면", h.Title);
            Assert.AreEqual(Companion.ScreenTips["cpr"], h.Lines.Single());
            Assert.AreEqual("cpr_point", h.FactId);

            Companion.PushScreen("quiz");
            var q = Companion.Hint(Companion.Screen, null, null, null);
            Assert.AreEqual("quiz", q.Topic);
            Assert.IsNull(q.FactId, "no fact during the quiz: it would give answers away");

            Companion.PopScreen("quiz");
            Assert.AreEqual("cpr", Companion.Screen);
            Companion.PopScreen("cpr");
            Assert.IsNull(Companion.Screen);
            Companion.PopScreen("nothing"); // popping what is not there is harmless
        }

        [Test]
        public void Nothing_to_do_says_so_and_the_objective_is_remembered()
        {
            var h = Companion.Hint(null, null, null, null);
            CollectionAssert.AreEqual(new[] { Companion.NothingToDo }, h.Lines);
            Assert.IsNull(h.FactId);
            Companion.SetObjective("복도의 불", "판단하세요. 작은 불이면 소화기로, 커졌다면 알리고 대피합니다.", null);
            Assert.AreEqual("복도의 불", Companion.ObjectiveTitle);
            Assert.AreEqual("fire_look", Companion.Hint(null, Companion.ObjectiveTitle, Companion.ObjectiveBody, Companion.ObjectiveSteps).FactId);
            Companion.SetObjective(null, null, null);
            Assert.IsNull(Companion.ObjectiveTitle);
            Assert.IsEmpty(Companion.ObjectiveSteps);
        }

        [Test]
        public void Only_choices_the_player_can_still_pick_are_listed()
        {
            var choices = new List<TalkChoice>
            {
                new("handle", "현관 문손잡이를 손등으로 만져 본다", true) { Taken = true },
                new("locked", "잠긴 선택", true, "아직 못 함"),
                null,
                new("stay", "창문을 닫고 119에 신고한 뒤 안내방송에 따른다"),
            };
            CollectionAssert.AreEqual(new[] { "창문을 닫고 119에 신고한 뒤 안내방송에 따른다" }, Companion.OpenChoices(choices));
            CollectionAssert.IsEmpty(Companion.OpenChoices(null));
        }

        [Test]
        public void A_judgement_asked_again_in_a_loop_gets_the_card_once()
        {
            // Evac's smoke question: asked again after feeling the door, the handle now taken
            var first = new List<TalkChoice> { new("handle", "손잡이"), new("fight", "끄러 간다"), new("run", "뛰어나간다"), new("stay", "창문을 닫는다") };
            var again = new List<TalkChoice> { new("handle", "손잡이", true) { Taken = true }, new("fight", "끄러 간다"), new("run", "뛰어나간다"), new("stay", "창문을 닫는다") };
            Assert.IsTrue(Companion.FirstPass(Companion.OpenChoices(first)));
            Assert.IsFalse(Companion.FirstPass(Companion.OpenChoices(again)), "the same question again: straight to the choices");
            // a different judgement gets its own card
            Assert.IsTrue(Companion.FirstPass(new[] { "그래도 먼저 확인하고, 준비하고, 어르신부터 챙긴다", "또 오발령이겠지. 다음엔 그냥 잔다" }));
            Companion.Reset();
            Assert.IsTrue(Companion.FirstPass(Companion.OpenChoices(again)), "a new run talks again");
        }

        [Test]
        public void One_loop_of_twenty_two_minutes_raises_at_most_one_card()
        {
            foreach (bool deaf in new[] { false, true })
            {
                Companion.Reset();
                var run = new TwentyTwoRun { Deaf = deaf, Minute = TwentyTwoLogic.SentAt + 1 };
                int cards = 0, picks = 0;
                while (!run.Over && picks < 20)
                {
                    var offered = run.Available();
                    if (Companion.FirstPass(offered.Select(a => a.Label))) cards++;
                    run.Pick(offered[0].Id);
                    picks++;
                }
                Assert.That(picks, Is.GreaterThan(2));
                Assert.AreEqual(1, cards, "deaf " + deaf);
            }
        }

        [Test]
        public void An_offer_to_play_an_optional_scene_is_not_a_judgement_card()
        {
            var offer = new CardOptions
            {
                Kind = "decision", Title = ElderPovLogic.OfferTitle, Meta = "선택 · 약 3분",
                Actions = new List<CardAction> { new("view", "어르신의 걸음으로", true), new("skip", "건너뛰기") },
            };
            Assert.IsFalse(Companion.CardDiscusses(offer), "304호 offer");
            var decide = new CardOptions
            {
                Kind = "decision", Title = "지금 내 상황", Meta = "판단 재료",
                Actions = new List<CardAction> { new("stay", "집에 남는다"), new("move", "생활문화센터 임시 거처로 옮긴다") },
            };
            Assert.IsTrue(Companion.CardDiscusses(decide), "4장");
            Assert.IsFalse(Companion.CardDiscusses(new CardOptions { Kind = "info", Title = "알림" }));
            Assert.IsFalse(Companion.CardDiscusses(null));
        }

        [Test]
        public void The_chip_is_offered_only_in_a_run_and_the_title_forgets_the_last_one()
        {
            Companion.On = true;
            Assert.IsFalse(Companion.Offered, "the title: no run yet");
            Companion.RunActive = true;
            Assert.IsTrue(Companion.Offered);
            Companion.SetObjective("복도의 불", "판단하세요.", null);
            Companion.PushScreen("cpr");
            Companion.Reset(); // the title (StoryDirector.TitleOnce, TitleReturn.ToTitle)
            Assert.IsFalse(Companion.Offered);
            Assert.IsNull(Companion.ObjectiveTitle);
            Assert.IsNull(Companion.Screen);
        }

        [Test]
        public void A_look_back_and_an_alert_by_text_get_prompts_that_fit()
        {
            var back = Companion.Discuss("다음에 또 경보 문자가 오면?", new[] { "그래도 먼저 확인하고, 준비하고, 어르신부터 챙긴다", "또 오발령이겠지. 다음엔 그냥 잔다" });
            Assert.AreEqual("reflect", back.Topic, "a look back, not a siren now");
            Assert.That(string.Join(" ", Companion.PromptsFor("alert")), Does.Not.Contain("들리는"), "a 경계경보 comes with no siren; the deaf profile reads it");
            Assert.That(string.Join(" ", Companion.PromptsFor("alert")), Does.Contain("문자"));
            Assert.That(Companion.FirstPrompt, Does.Not.Contain("당신"));
        }

        [Test]
        public void The_settings_words_fit_and_say_what_the_mode_does()
        {
            Assert.That(Companion.Sub, Does.Contain(Companion.HintKey));
            Assert.That(Companion.ChipText, Does.Contain(Companion.HintKey));
            Assert.That(Companion.DiscussNote, Does.Contain("이유"));
        }
    }
}
