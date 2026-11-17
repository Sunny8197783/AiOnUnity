using System.Linq;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class NotebookLogicTests
    {
        static JArray Entries() => JArray.Parse(@"[
            {day:-2,minute:600,text:'b',label:'rumor',place:'town'},
            {day:-3,minute:1050,text:'a',label:'official',place:'office'},
            {day:-2,minute:540,text:'c',label:'weird'},
            {day:-2,minute:600,text:'d',label:'site'},
            {day:0,minute:1395,text:'',label:'site'},
            null,
            {day:1,text:'e',label:'neighbor',place:'nowhere'}]");

        [Test]
        public void Notebook_DropsEmpty_SortsStableByDayThenMinute()
        {
            var list = NotebookLogic.Prepare(Entries());
            Assert.That(list.Select(e => e.Text), Is.EqualTo(new[] { "a", "c", "b", "d", "e" }));
        }

        [Test]
        public void Notebook_LabelsCountsAndFilter()
        {
            var list = NotebookLogic.Prepare(Entries());
            var counts = NotebookLogic.Counts(list);
            Assert.That(counts["self"], Is.EqualTo(1), "unknown label counts as self");
            Assert.That(counts["official"] + counts["site"] + counts["neighbor"] + counts["rumor"] + counts["self"], Is.EqualTo(5));
            Assert.That(NotebookLogic.Filter(list, "rumor").Single().Text, Is.EqualTo("b"));
            Assert.That(NotebookLogic.LabelText("weird"), Is.EqualTo("내 기록"));
            Assert.That(NotebookLogic.EmptyText(list, "all"), Is.Null);
            Assert.That(NotebookLogic.EmptyText(NotebookLogic.Filter(list, "official"), "rumor"), Is.EqualTo("이 분류의 기록이 없습니다."));
            Assert.That(NotebookLogic.EmptyText(NotebookLogic.Prepare(new JArray()), "all"), Is.EqualTo("아직 적은 것이 없습니다. 확인한 정보는 여기에 쌓입니다."));
        }

        [Test]
        public void Notebook_DaysTimesPlaces()
        {
            var list = NotebookLogic.Prepare(Entries());
            var days = NotebookLogic.ByDay(list);
            Assert.That(days.Select(d => NotebookLogic.DayLabel(d.day)), Is.EqualTo(new[] { "D-3", "D-2", "D+1" }));
            Assert.That(NotebookLogic.DayLabel(0), Is.EqualTo("D-day"));
            Assert.That(list[0].Time, Is.EqualTo("17:30"));
            Assert.That(list[0].PlaceText, Is.EqualTo("사무실"));
            Assert.That(list.Last().Time, Is.EqualTo("00:00"), "missing minute");
            Assert.That(list.Last().PlaceText, Is.Null, "unknown place is not shown");
            Assert.That(NotebookLogic.Hhmm(1500), Is.EqualTo("01:00"), "hours wrap at 24");
            Assert.That(NotebookLogic.Hhmm(65), Is.EqualTo("01:05"));
        }

        [Test]
        public void Notebook_Options_EntriesOrInfo()
        {
            Assert.That(NotebookLogic.EntriesOf(JObject.Parse("{entries:[{text:'x'}]}")).Count, Is.EqualTo(1));
            Assert.That(NotebookLogic.EntriesOf(JObject.Parse("{info:[{text:'x'},{text:'y'}]}")).Count, Is.EqualTo(2));
            Assert.That(NotebookLogic.EntriesOf(new JObject()).Count, Is.EqualTo(0));
        }

        // ---------- inventory ----------

        static JObject Catalog() => JObject.Parse(@"{
            water:{name:'생수',unit:'병',category:'water'},
            radio:{name:'휴대용 라디오',unit:'대',category:'info',needsCheck:'batteries'},
            gas:{name:'부탄가스',unit:'개',category:'fire',risk:'fire'},
            milk:{name:'우유',unit:'팩',category:'food',risk:'spoil'},
            bread:{name:'건빵',unit:'봉',category:'food'},
            odd:{name:'가위',category:'craft'}}");

        [Test]
        public void Inventory_RowsSortedByCategoryThenName()
        {
            var items = JObject.Parse("{water:{home:6,bag:2},radio:{home:1,bag:0,checked:true},gas:{home:2},milk:{home:1},bread:{home:3,bag:1},odd:{home:1},torch:{home:1}}");
            var home = InventoryLogic.Rows("home", items, items, Catalog());
            // unknown category (-1) first, then water, food (건빵 < 우유), info, tool (torch: default catalog), fire
            Assert.That(home.Select(r => r.Id), Is.EqualTo(new[] { "odd", "water", "bread", "milk", "radio", "torch", "gas" }));
            Assert.That(home[0].CategoryText, Is.EqualTo("기타"));
            Assert.That(home[0].Qty, Is.EqualTo("1개"));
            Assert.That(home[1].Qty, Is.EqualTo("6병"));
            Assert.That(home.Single(r => r.Id == "torch").Name, Is.EqualTo("torch"));
            Assert.That(home.Single(r => r.Id == "radio").Tags.Single().Text, Is.EqualTo("점검 완료"));
            Assert.That(home.Single(r => r.Id == "gas").Tags.Single().Text, Is.EqualTo("화기 주의"));
            Assert.That(home.Single(r => r.Id == "milk").Tags.Single().Text, Is.EqualTo("정전 땐 오래 못 둠"));

            var bag = InventoryLogic.Rows("bag", items, items, Catalog());
            Assert.That(bag.Select(r => r.Id), Is.EqualTo(new[] { "water", "bread" }));
        }

        [Test]
        public void Inventory_NumbersAndUncheckedTag()
        {
            var home = JObject.Parse("{radio:2}");
            var bag = JObject.Parse("{radio:{bag:'1'}}");
            var rows = InventoryLogic.Rows("home", home, bag, Catalog());
            Assert.That(rows.Single().Tags.Single().Text, Is.EqualTo("점검 전 · 건전지를 넣어 봐야 쓸 수 있음"), "entry is home's number: not checked");
            Assert.That(InventoryLogic.Rows("bag", home, bag, Catalog()).Single().Qty, Is.EqualTo("1대"));
            Assert.That(InventoryLogic.Rows("bag", new JObject(), new JObject(), Catalog()), Is.Empty);
            Assert.That(InventoryLogic.EmptyText("bag"), Is.EqualTo("가방이 비어 있습니다."));
            Assert.That(InventoryLogic.Count(JObject.Parse("{home:'x'}"), "home"), Is.EqualTo(0));
        }

        [Test]
        public void Inventory_Meters()
        {
            var m = InventoryLogic.Meters(JObject.Parse("{weightKg:4.25,volumeL:12,over:{weight:false,volume:true}}"), JObject.Parse("{weightKg:5,volumeL:20}"), null);
            Assert.That(m.Select(x => x.Label), Is.EqualTo(new[] { "무게", "부피" }));
            Assert.That(m[0].State, Is.EqualTo("near"));
            Assert.That(m[0].Pct, Is.EqualTo(85).Within(1e-9));
            Assert.That(m[0].Value, Does.StartWith("4.").And.EndWith(" / 5kg · 거의 참"));
            Assert.That(m[1].State, Is.EqualTo("over"));
            Assert.That(m[1].Value, Is.EqualTo("12.0 / 20L · 초과"));

            var c = InventoryLogic.Meters(JObject.Parse("{weightKg:1,over:{}}"), JObject.Parse("{weightKg:5}"), JObject.Parse("{used:12,total:50}"));
            Assert.That(c[1].Label, Is.EqualTo("공간"));
            Assert.That(c[1].Value, Is.EqualTo("12 / 50칸"));
            Assert.That(c[0].Value, Is.EqualTo("1.0 / 5kg"));
            Assert.That(InventoryLogic.Meters(null, JObject.Parse("{weightKg:5}"), null), Is.Null);
            // any truthy cells picks the room meter, even without used/total
            var odd = InventoryLogic.Meters(JObject.Parse("{weightKg:1}"), JObject.Parse("{weightKg:5,volumeL:20}"), new JValue(3));
            Assert.That(odd[1].Label, Is.EqualTo("공간"));
            Assert.That(odd[1].Value, Is.EqualTo("0 / undefined칸"));

            var zero = InventoryLogic.MakeMeter("무게", 3, new JValue(0), "kg", false);
            Assert.That(zero.Pct, Is.EqualTo(0));
            Assert.That(zero.Value, Is.EqualTo("3.0 / 0kg"));
        }
    }
}
