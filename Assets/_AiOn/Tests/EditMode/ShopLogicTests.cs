using System.Linq;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    /// <summary>The shelf screen's rules, checked against ui/grab3d.js shop3d() (and grab-layout.js for the rows).</summary>
    public class ShopLogicTests
    {
        static JObject Opts(double money = 10000) => JObject.Parse(@"{
            ""title"": ""생수·음료"", ""money"": " + money + @",
            ""items"": [
              { ""id"": ""water_2l"", ""name"": ""생수 2L"", ""price"": 1200, ""unit"": ""병"", ""weightKg"": 2.1, ""volumeL"": 2.2, ""stock"": ""low"", ""note"": """", ""label"": """" },
              { ""id"": ""water_500"", ""name"": ""생수 500mL"", ""price"": 600, ""unit"": ""병"", ""weightKg"": 0.55, ""volumeL"": 0.6, ""stock"": ""ok"", ""note"": """", ""label"": """" },
              { ""id"": ""radio"", ""name"": ""휴대용 라디오"", ""price"": 25000, ""weightKg"": 0.4, ""stock"": ""out"", ""note"": ""건전지 별매"" }
            ],
            ""info"": [ { ""text"": ""생수 1인 6병 한정"", ""label"": ""site"" } ],
            ""budgetNote"": ""남은 돈 10,000원""
          }");

        [Test]
        public void ParsesTheOptionObject()
        {
            var o = ShopOptions.Parse(Opts());
            Assert.AreEqual("생수·음료", o.Title);
            Assert.AreEqual(10000, o.Money);
            Assert.AreEqual(3, o.Items.Count);
            Assert.AreEqual("건전지 별매", o.Items[2].Note);
            Assert.IsTrue(o.Items[2].Out);
            CollectionAssert.AreEqual(new[] { "생수 1인 6병 한정" }, o.Info);
        }

        [Test]
        public void DefaultsLikeTheJs()
        {
            var s = new ShopState(ShopOptions.Parse(new JObject()));
            Assert.AreEqual("장보기 · 진열대", s.Header);
            Assert.AreEqual("합계 0원 / 가진 돈 0원", s.SumText);
            Assert.AreEqual("들고 갈 무게 약 0.0kg", s.WeightText);
            Assert.IsNull(s.Step(1));
            Assert.AreEqual(0, s.Result()["total"].Value<int>());
            Assert.AreEqual("장보기 · null", new ShopState(ShopOptions.Parse(JObject.Parse("{\"title\":null}"))).Header); // JS default only for undefined
        }

        [Test]
        public void TagsShowPriceLowAndSoldOut()
        {
            var s = new ShopState(ShopOptions.Parse(Opts()));
            Assert.AreEqual("1,200원 · 적음", ShopState.Sub(s.Items[0]));
            Assert.AreEqual("600원", ShopState.Sub(s.Items[1]));
            Assert.AreEqual("품절", ShopState.Sub(s.Items[2]));
            Assert.AreEqual("25,000원", ShopState.Won(24999.5));
            Assert.AreEqual("1,234,567원", ShopState.Won(1234567));
        }

        [Test]
        public void TakingAddsToTotalsAndHeld()
        {
            var s = new ShopState(ShopOptions.Parse(Opts()));
            Assert.AreEqual((true, "생수 2L 하나 담음"), s.Take(0));
            s.Take(0); s.Take(1);
            Assert.AreEqual(18, s.Src("water_2l"));
            Assert.AreEqual(2, s.Dst("water_2l"));
            Assert.AreEqual("합계 3,000원 / 가진 돈 10,000원", s.SumText);
            Assert.AreEqual("들고 갈 무게 약 4.8kg", s.WeightText); // 2*2.1 + 0.55 = 4.75 -> 4.8
            CollectionAssert.AreEqual(new[] { "생수 2L ×2", "생수 500mL ×1" }, s.Held().Select(h => h.text).ToArray());
        }

        [Test]
        public void RefusalsInTheOriginalOrder()
        {
            var s = new ShopState(ShopOptions.Parse(Opts(1500)));
            Assert.AreEqual((false, "품절입니다."), s.Take(2));
            Assert.AreEqual((true, "생수 2L 하나 담음"), s.Take(0));
            Assert.AreEqual((false, "가진 돈이 모자란다."), s.Take(0)); // 2400 > 1500
            Assert.AreEqual((false, "가진 돈이 모자란다."), s.Take(1)); // 1800 > 1500
            var exact = new ShopState(ShopOptions.Parse(Opts(1800)));
            exact.Take(0);
            Assert.IsTrue(exact.Take(1).ok); // total + price == money is fine
        }

        [Test]
        public void ShelfRunsOutAtTwenty()
        {
            var s = new ShopState(ShopOptions.Parse(Opts(1e9)));
            for (int i = 0; i < ShopState.MaxQty; i++) Assert.IsTrue(s.Take(1).ok);
            Assert.AreEqual((false, "더 없습니다."), s.Take(1));
            Assert.AreEqual(0, s.Shown(1));
            Assert.IsTrue(s.Faded(1));
            Assert.IsFalse(s.Faded(2)); // sold out keeps a full-strength tag
            Assert.AreEqual(4, s.Shown(0));
        }

        [Test]
        public void PuttingBack()
        {
            var s = new ShopState(ShopOptions.Parse(Opts()));
            Assert.AreEqual((false, (string)null), s.PutBack(0));
            s.Take(0);
            Assert.AreEqual((true, "생수 2L 하나 내려놓음"), s.PutBack(0));
            Assert.AreEqual(20, s.Src("water_2l"));
            Assert.AreEqual(0, s.Held().Count);
        }

        [Test]
        public void StepWrapsAndAnnounces()
        {
            var s = new ShopState(ShopOptions.Parse(Opts()));
            Assert.AreEqual("생수 500mL · 600원", s.Step(1));
            s.Step(1);
            Assert.AreEqual(2, s.Sel);
            Assert.AreEqual("생수 2L · 1,200원 · 적음", s.Step(1));
            Assert.AreEqual("품절", s.Step(-1).Split(" · ").Last());
            Assert.IsFalse(s.Pick(2));
            Assert.IsTrue(s.Pick(0));
        }

        [Test]
        public void ResultIsHeldCartAndTotal()
        {
            var s = new ShopState(ShopOptions.Parse(Opts()));
            s.Take(0); s.Take(1); s.Take(1); s.PutBack(1);
            var r = s.Result();
            Assert.AreEqual(1, r["cart"]["water_2l"].Value<int>());
            Assert.AreEqual(1, r["cart"]["water_500"].Value<int>());
            Assert.IsNull(r["cart"]["radio"]);
            Assert.AreEqual(1800, r["total"].Value<int>());
            Assert.AreEqual(JTokenType.Integer, r["total"].Type);
        }

        [Test]
        public void ShelfRowsMatchGrabLayout()
        {
            Assert.AreEqual((1, 2), ShopState.Shelf(2));
            Assert.AreEqual((1, 5), ShopState.Shelf(5));
            Assert.AreEqual((2, 3), ShopState.Shelf(6));
            Assert.AreEqual((3, 4), ShopState.Shelf(11));
            Assert.AreEqual((2, 2, 3), ShopState.Slot(10, 11)); // last row of 11: three items, centred
            Assert.AreEqual((0, 3, 4), ShopState.Slot(3, 11));
        }
    }
}
