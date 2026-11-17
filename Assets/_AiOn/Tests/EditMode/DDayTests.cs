// DDay (story/dday.js) pure helpers: port of tests/r6-fix2-dday.test.mjs, plus ApplyPocket (ui/bag.js applyPocket).
using AiOn.Game;
using AiOn.Story;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class DDayTests
    {
        static ItemEntry Item(int bag, int home = 0) => new ItemEntry { Home = home, Bag = bag };

        [Test]
        public void ShelterReception_AContactCardInABagLeftAtHomeDoesNotCount()
        {
            var rules = TestSupport.Rules;
            var g = rules.CreateGame(4);
            g.Items["contact_card"] = Item(1);
            g.SetKnow("bagOn", false);
            Assert.That(DDay.ReceptionCard(g).Card, Is.False);
            StringAssert.Contains("휴대폰", DDay.ReceptionCard(g).Line);
            g.SetKnow("bagOn", true);
            Assert.That(DDay.ReceptionCard(g), Is.EqualTo((true, "(배낭에서 비상연락카드를 꺼내 보고 적었다.)")));
            rules.SetPocket(g, "contact_card", 1);
            StringAssert.Contains("앞주머니", DDay.ReceptionCard(g).Line);
        }

        [Test]
        public void ShelterArrival_BagWaterIsDrunk_TheLineNamesOnlyWhatWasInTheBag()
        {
            var rules = TestSupport.Rules;
            var g = rules.CreateGame(5);
            g.SetKnow("bagOn", true); g.Items["water_500"] = Item(2); g.Items.Remove("blanket");
            double t0 = g.Stats.Thirst;
            Assert.That(DDay.UnpackAtShelter(rules, g), Is.EqualTo("배낭에서 물을 꺼냈다"));
            Assert.That(g.Items["water_500"].Bag, Is.EqualTo(1)); Assert.That(g.Items["water_500"].Used, Is.EqualTo(1));
            Assert.That(g.Stats.Thirst, Is.EqualTo(System.Math.Max(0, t0 - 20)));
            g.Items["blanket"] = Item(1); g.Items["water_500"] = Item(0);
            Assert.That(DDay.UnpackAtShelter(rules, g), Is.EqualTo("배낭에서 담요를 꺼냈다"));
            g.Items["water_2l"] = Item(1);
            Assert.That(DDay.UnpackAtShelter(rules, g), Is.EqualTo("배낭에서 물과 담요를 꺼냈다"));
            g.SetKnow("bagOn", false); Assert.That(DDay.UnpackAtShelter(rules, g), Is.Null);
            Assert.That(SaveCodec.Save(SaveCodec.Load(SaveCodec.Save(g), rules.Db)), Is.EqualTo(SaveCodec.Save(g)));
        }

        [Test]
        public void ApplyPocket_EmptiesThenRefills_AndSkipsWhatIsNotAPocketItem()
        {
            var rules = TestSupport.Rules;
            var g = rules.CreateGame(5);
            g.Items["contact_card"] = Item(1); g.Items["water_2l"] = Item(1);
            DDay.ApplyPocket(rules, g, JObject.Parse("{ contact_card: 3, water_2l: 1 }"));
            Assert.That(g.Items["contact_card"].Pocket, Is.EqualTo(1), "clamped to what is in the bag");
            Assert.That(g.Items["water_2l"].Pocket ?? 0, Is.EqualTo(0), "not a pocket item");
            DDay.ApplyPocket(rules, g, JObject.Parse("{}"));
            Assert.That(g.Items["contact_card"].Pocket ?? 0, Is.EqualTo(0));
            DDay.ApplyPocket(rules, g, null); // no pocket in the result: nothing changes
        }
    }
}
