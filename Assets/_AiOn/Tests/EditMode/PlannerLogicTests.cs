using System.Linq;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class PlannerLogicTests
    {
        // Like story/prep.js openPlanner on slot 1 with 2 of 6 used and 'shelter' done.
        static JObject Prep() => JObject.Parse(@"{
            day: 'D-1 오전', slots: { total: 6, used: 2 }, hint: '',
            actions: [
              { id: 'mart', label: '연무마트 장보기', cost: 2, icon: '🛒', desc: 'd1', done: false, disabled: false, reason: '' },
              { id: 'shelter', label: '대피소·출입구 확인', cost: 1, icon: '🧭', desc: 'd2', done: true, disabled: true, reason: '이미 했습니다.' },
              { id: 'equip', label: '집 장비 점검', cost: 1, desc: 'd3', done: false, disabled: false, reason: '' },
              { id: 'big', label: '큰 일', cost: 4, desc: 'd4', done: false, disabled: false, reason: '' },
              { id: 'locked', label: '잠긴 일', cost: 1, desc: 'd5', done: false, disabled: true, reason: '' }
            ] }");

        [Test]
        public void Defaults_WhenNoOptions()
        {
            var p = PlannerLogic.Parse(null);
            Assert.AreEqual("", p.Day);
            Assert.AreEqual(6, p.Total);
            Assert.AreEqual(0, p.Used);
            Assert.AreEqual(6, p.Slots().Count);
            Assert.AreEqual(-1, p.Focus);
            Assert.AreEqual(-1, p.DetailIndex);
            Assert.AreEqual("남은 칸 6 / 6 · 모든 일을 다 할 수는 없습니다.", p.Sub);
        }

        [Test]
        public void Used_IsClampedToTotal()
        {
            var p = PlannerLogic.Parse(JObject.Parse("{ slots: { total: 3, used: 9 } }"));
            Assert.AreEqual(3, p.Used);
            Assert.AreEqual(0, p.Left);
            p = PlannerLogic.Parse(JObject.Parse("{ slots: { total: -2, used: -1 } }"));
            Assert.AreEqual(0, p.Total);
            Assert.AreEqual(0, p.Used);
            p = PlannerLogic.Parse(JObject.Parse("{ slots: null }"));
            Assert.AreEqual(6, p.Total);
        }

        [Test]
        public void StartsFocusedOnFirstOpenCard_WithItsPreview()
        {
            var p = PlannerLogic.Parse(Prep());
            Assert.AreEqual(0, p.Focus);
            Assert.AreEqual(0, p.DetailIndex);
            Assert.IsFalse(p.Confirming);
            var s = p.Slots();
            CollectionAssert.AreEqual(new[] { "지남", "지남", "사용", "사용", "", "" }, s.Select(x => x.State).ToArray());
            Assert.IsTrue(s[2].Preview && s[3].Preview && !s[4].Preview);
        }

        [Test]
        public void AllBlocked_FocusesFirstCard()
        {
            var p = PlannerLogic.Parse(JObject.Parse("{ actions: [ { id: 'a', cost: 1, done: true }, { id: 'b', cost: 1, disabled: true } ] }"));
            Assert.AreEqual(0, p.Focus);
            Assert.IsFalse(p.Slots().Any(x => x.Preview), "a blocked card previews no cost");
        }

        [Test]
        public void CardTexts()
        {
            var p = PlannerLogic.Parse(Prep());
            Assert.AreEqual((true, "이미 했음"), PlannerLogic.CardStatus(p.Actions[1]));
            Assert.AreEqual((false, "지금은 할 수 없음"), PlannerLogic.CardStatus(p.Actions[4]));
            Assert.IsNull(PlannerLogic.CardStatus(p.Actions[0]));
            Assert.AreEqual("2칸", PlannerLogic.CostText(p.Actions[0]));
            Assert.AreEqual("이미 했습니다.", PlannerLogic.BlockedText(p.Actions[1]));
            Assert.AreEqual("지금은 할 수 없습니다.", PlannerLogic.BlockedText(p.Actions[4]));
            Assert.AreEqual("준비 시간 6칸 가운데 2칸을 썼고 4칸이 남았습니다", p.SlotAria);
        }

        [Test]
        public void AfterAndLost()
        {
            var p = PlannerLogic.Parse(Prep());
            Assert.AreEqual("2칸", p.AfterText(p.Actions[0]));
            // after mart: 2 left -> 'big' (4) no longer fits; blocked ones are never listed
            CollectionAssert.AreEqual(new[] { "big" }, p.Lost(p.Actions[0]).Select(a => (string)a.Id).ToArray());
            // big uses all 4: nothing left
            Assert.AreEqual("없음 · 준비 시간이 끝납니다", p.AfterText(p.Actions[3]));
            CollectionAssert.AreEqual(new[] { "mart", "equip" }, p.Lost(p.Actions[3]).Select(a => (string)a.Id).ToArray());
        }

        [Test]
        public void ChooseConfirm_ReturnsId()
        {
            var p = PlannerLogic.Parse(Prep());
            Assert.IsTrue(p.Choose(2));
            Assert.AreEqual(2, p.Selected);
            Assert.AreEqual(2, p.DetailIndex);
            Assert.IsTrue(p.Confirming);
            Assert.AreEqual("equip", (string)p.Confirm());
        }

        [Test]
        public void HoverWhileSelected_MovesTilesNotDetail()
        {
            var p = PlannerLogic.Parse(Prep());
            p.Choose(0);
            p.Hover(3);
            Assert.AreEqual(0, p.DetailIndex);
            Assert.IsTrue(p.Confirming);
            Assert.AreEqual(4, p.Slots().Count(s => s.Preview));
            p.Leave();
            Assert.AreEqual(2, p.Slots().Count(s => s.Preview));
        }

        [Test]
        public void ChooseBlocked_ShowsWhyAndDropsChoice()
        {
            var p = PlannerLogic.Parse(Prep());
            p.Choose(0);
            Assert.IsFalse(p.Choose(1));
            Assert.AreEqual(-1, p.Selected);
            Assert.AreEqual(1, p.DetailIndex);
            Assert.IsFalse(p.Confirming);
            Assert.IsNull(p.Confirm());
        }

        [Test]
        public void EscapeGoesBackThenCloses()
        {
            var p = PlannerLogic.Parse(Prep());
            p.Choose(3);
            Assert.IsFalse(p.Escape());
            Assert.AreEqual(-1, p.Selected);
            Assert.AreEqual(3, p.Focus);
            Assert.AreEqual(3, p.DetailIndex, "focusing the card again previews it");
            Assert.IsFalse(p.Confirming);
            Assert.IsTrue(p.Escape());
        }

        [Test]
        public void ArrowsWrap_AndStopWhileConfirming()
        {
            var p = PlannerLogic.Parse(Prep());
            p.Step(-1);
            Assert.AreEqual(4, p.Focus);
            p.Step(1);
            Assert.AreEqual(0, p.Focus);
            p.Step(1);
            Assert.AreEqual(1, p.Focus);
            Assert.AreEqual(1, p.DetailIndex);
            p.Choose(2);
            p.Step(1);
            Assert.AreEqual(2, p.Focus);
        }

        [Test]
        public void MissingId_ConfirmsNull_AndTruthyFlags()
        {
            var p = PlannerLogic.Parse(JObject.Parse("{ actions: [ { label: 'x', cost: 1, done: 0, disabled: '' } ] }"));
            Assert.IsFalse(p.Actions[0].Blocked);
            p.Choose(0);
            Assert.IsNull(p.Confirm());
            Assert.AreEqual("1.5", PlannerLogic.JsNum(1.5));
        }
    }
}
