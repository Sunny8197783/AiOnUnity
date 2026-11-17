using System.Linq;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class DocsLogicTests
    {
        static JObject Opts(string mode = "guided", string carry = "['bag','home']") => JObject.Parse($@"{{
            mode: '{mode}', carry: {carry},
            fields: [
              {{ id: 'me', label: '개인사항', value: '정민', why: 'w1', recommended: true }},
              {{ id: 'blood', label: '혈액형', value: 'A형', why: '', recommended: false }},
              {{ id: 'meet', label: '만남의 장소', value: '① 대피소', why: 'w3', recommended: true }}
            ] }}");

        [Test]
        public void Defaults()
        {
            var d = DocsLogic.Parse(null);
            Assert.AreEqual("guided", d.Mode);
            Assert.AreEqual("bag", d.Where);
            Assert.IsTrue(d.HelpOpen);
            CollectionAssert.AreEqual(new[] { "bag", "home" }, d.WhereOptions);
            Assert.AreEqual("{\"card\":{},\"where\":\"bag\"}", d.Result().ToString(Newtonsoft.Json.Formatting.None));
        }

        [Test]
        public void Where_FirstCarryWhenNoBag()
        {
            Assert.AreEqual("home", DocsLogic.Parse(Opts(carry: "['home']")).Where);
            Assert.AreEqual("home", DocsLogic.Parse(Opts(carry: "[]")).Where);
            var d = DocsLogic.Parse(Opts(carry: "['car','home']"));
            Assert.AreEqual("car", d.Where);
            CollectionAssert.AreEqual(new[] { "home" }, d.WhereOptions);
        }

        [Test]
        public void ToggleAnnouncesAndBuildsResultInFieldOrder()
        {
            var d = DocsLogic.Parse(Opts());
            Assert.AreEqual("만남의 장소을(를) 적었다.", d.Toggle(2));
            Assert.AreEqual("개인사항을(를) 적었다.", d.Toggle(0));
            Assert.AreEqual("2/3개 항목", d.PhoneCount);
            CollectionAssert.AreEqual(new[] { "me", "meet" }, d.Written.Select(f => f.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "blood" }, d.PhoneOnly.Select(f => f.Id).ToArray());
            d.SetWhere("home");
            Assert.AreEqual("{\"card\":{\"me\":\"정민\",\"meet\":\"① 대피소\"},\"where\":\"home\"}", d.Result().ToString(Newtonsoft.Json.Formatting.None));
            Assert.AreEqual("개인사항을(를) 지웠다.", d.Toggle(0));
            Assert.AreEqual(1, d.OnCount);
        }

        [Test]
        public void ModeLadder()
        {
            var g = DocsLogic.Parse(Opts("guided"));
            Assert.AreEqual("안내 있음", g.ModeChip);
            Assert.IsTrue(g.ShowBadge(g.Fields[0]) && !g.ShowBadge(g.Fields[1]));
            Assert.IsTrue(g.ShowWhy(g.Fields[0]) && !g.ShowWhy(g.Fields[1]));
            Assert.IsTrue(g.HasHelp && !g.HasHintButton && g.ShowWhereDesc);

            var h = DocsLogic.Parse(Opts("hint"));
            Assert.AreEqual("힌트 가능", h.ModeChip);
            Assert.IsFalse(h.ShowBadge(h.Fields[0]));
            Assert.IsTrue(h.ShowWhy(h.Fields[0]));
            Assert.IsTrue(h.HasHintButton && !h.HelpOpen);
            Assert.AreEqual("힌트 보기", h.HintButtonText);
            h.ToggleHelp();
            Assert.AreEqual("힌트 닫기", h.HintButtonText);

            var f = DocsLogic.Parse(Opts("free"));
            Assert.IsFalse(f.HasHelp || f.ShowWhy(f.Fields[0]) || f.ShowWhereDesc);
            Assert.AreEqual("혼자 판단", f.ModeChip);
            Assert.IsNull(DocsLogic.Parse(Opts("odd")).ModeChip);
        }

        [Test]
        public void KeyboardStops()
        {
            var d = DocsLogic.Parse(Opts("hint"));
            d.InitFocus();
            Assert.AreEqual((DocsStop.Field, 0), d.Focused);
            d.Tab(true);
            Assert.AreEqual((DocsStop.Hint, 0), d.Focused);
            Assert.AreEqual("", d.Space(out var done));
            Assert.IsFalse(done);
            Assert.IsTrue(d.HelpOpen);
            d.Tab(false); d.Tab(false);
            Assert.AreEqual("혈액형을(를) 적었다.", d.Space(out done));
            d.Tab(false); d.Tab(false);
            Assert.AreEqual((DocsStop.Where, 0), d.Focused);
            d.Tab(false);
            Assert.AreEqual((DocsStop.Done, 0), d.Focused);
            d.Space(out done);
            Assert.IsTrue(done);
            d.Tab(false);
            Assert.AreEqual((DocsStop.Hint, 0), d.Focused, "Tab wraps");
        }

        [Test]
        public void ArrowsCycleWhere()
        {
            var d = DocsLogic.Parse(Opts());
            d.InitFocus();
            d.Arrow(1);
            Assert.AreEqual("home", d.Where);
            Assert.AreEqual((DocsStop.Where, 0), d.Focused);
            d.Arrow(1);
            Assert.AreEqual("bag", d.Where);
            d.Arrow(-1);
            Assert.AreEqual("home", d.Where);
        }

        [Test]
        public void NullMode_IsNotGuided()
        {
            var d = DocsLogic.Parse(JObject.Parse("{ mode: null, fields: [ { id: 'a', label: 'A', value: 'v', why: 'w', recommended: true } ] }"));
            Assert.IsNull(d.ModeChip);
            Assert.IsFalse(d.HasHelp || d.HelpOpen || d.ShowBadge(d.Fields[0]));
            Assert.IsTrue(d.ShowWhy(d.Fields[0]) && d.ShowWhereDesc);
        }

        [Test]
        public void SpaceOnUncheckedRadioGroup_ChecksFirst()
        {
            var d = DocsLogic.Parse(Opts(carry: "['car','home']"));
            d.FocusOn(DocsStop.Where, 0);
            Assert.AreEqual("", d.Space(out var done));
            Assert.IsFalse(done);
            Assert.AreEqual("home", d.Where);
            Assert.IsNull(d.Space(out done), "already checked: nothing new");
        }

        [Test]
        public void NoWhereOptions_NoRadioStop()
        {
            var d = DocsLogic.Parse(Opts(carry: "['car']"));
            Assert.IsFalse(d.Stops().Any(s => s.kind == DocsStop.Where));
            d.Arrow(1);
            Assert.AreEqual("car", d.Where);
            Assert.AreEqual("{\"card\":{},\"where\":\"car\"}", d.Result().ToString(Newtonsoft.Json.Formatting.None));
        }
    }
}
