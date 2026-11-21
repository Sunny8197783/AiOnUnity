using System.Linq;
using AiOn.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AiOn.Tests
{
    public class MenuLogicTests
    {
        // ---------- landing ----------

        [Test]
        public void Landing_NoSave_NewIsAutofocusAndGalleryHidden()
        {
            var b = MenuLogic.LandingButtons(false, 0, 2, 13);
            Assert.That(b.Select(x => x.Id), Is.EqualTo(new[] { "new", "scenes", "teacher", "more", "records", "lounge", "settings", "about" }));
            Assert.That(b[0].Autofocus && b[0].Primary, Is.True);
            Assert.That(b[1].Label, Is.EqualTo("장면 바로가기"));
            Assert.That(b[2].Label, Is.EqualTo("교사용 30분 모드"));
            Assert.That(b[3].Label, Is.EqualTo("더 알아보기")); // 기록관, 지도와 사람들, 훈련 달력, 우리 집 안전 준비 under one entry
            Assert.That(b[4].Label, Is.EqualTo("도전 과제 2/13"));
        }

        [Test]
        public void Landing_WithSaveAndEndings_ContinueFocusedGalleryShown()
        {
            var b = MenuLogic.LandingButtons(true, 3, 0, 13);
            Assert.That(b.Select(x => x.Id), Is.EqualTo(new[] { "new", "continue", "scenes", "teacher", "more", "records", "lounge", "gallery", "settings", "about" }));
            Assert.That(b.Single(x => x.Autofocus).Id, Is.EqualTo("continue"));
            Assert.That(b.Single(x => x.Id == "gallery").Label, Is.EqualTo($"엔딩 기록 3/{AiOn.Game.Endings.All.Count}"));
        }

        [Test]
        public void Landing_Options_DefaultsAndTruthiness()
        {
            Assert.That(MenuLogic.LandingOptions(new JObject()), Is.EqualTo((false, 0, 0)));
            Assert.That(MenuLogic.LandingOptions(JObject.Parse("{hasSave:1,endings:4,challenges:7}")), Is.EqualTo((true, 4, 7)));
            Assert.That(MenuLogic.Chapters.Length, Is.EqualTo(7));
            Assert.That(MenuLogic.Chapters[6][2], Is.EqualTo(""));
        }

        // ---------- pause ----------

        [Test]
        public void Pause_EscClosesConfirmFirstThenResumes()
        {
            bool confirm = true;
            Assert.That(MenuLogic.PauseEscape(ref confirm), Is.Null);
            Assert.That(confirm, Is.False);
            Assert.That(MenuLogic.PauseEscape(ref confirm), Is.EqualTo("resume"));
            Assert.That(MenuLogic.PauseButtons().Select(x => x.Id), Is.EqualTo(new[] { "resume", "settings", "save", "lounge", "menu?" }));
            Assert.That(MenuLogic.PauseKeys.Length, Is.EqualTo(UiKeys.All.Length));
        }

        // ---------- settings ----------

        [Test]
        public void Settings_UnchangedSubmit_IsDefaultsPlusQualityAuto()
        {
            var f = new SettingsForm(new JObject());
            TestSupport.AssertJson(f.Submit(), @"{quality:'medium',volume:0.7,mute:false,captions:true,reduceMotion:false,textScale:1,view:'third',
                sensitivity:1,minimap:true,volAmbience:1,volEffects:1,volUi:1,profile:'none',memoryCue:true,
                easyKorean:false,companion:false,qualityAuto:false}"); // competition upgrade: 쉬운 한국어 · 도우미 모드 after the original's keys
            Assert.That(f.Submit().Properties().Last().Name, Is.EqualTo("qualityAuto"), "qualityAuto is appended after cur's keys");
            Assert.That((string)f.Submit()["quality"], Is.EqualTo("medium"));
        }

        [Test]
        public void Settings_ReadsCurrent_AndSubmitsChanges()
        {
            var f = new SettingsForm(JObject.Parse("{quality:'high',qualityAuto:true,volume:0.35,textScale:1.15,profile:'deaf',sensitivity:1.5,memoryCue:false,extra:'kept'}"));
            Assert.That(f.Radio["quality"], Is.EqualTo("high"));
            Assert.That(f.Radio["textScale"], Is.EqualTo("1.15"));
            Assert.That(f.Radio["profile"], Is.EqualTo("deaf"));
            Assert.That(f.Range["volume"], Is.EqualTo(35));
            Assert.That(f.RangeText("sensitivity"), Is.EqualTo("1.50배"));
            Assert.That(f.Toggle["memoryCue"], Is.False);
            Assert.That(f.LeftRadios[0].Note, Does.StartWith("처음 실행할 때 이 컴퓨터에 맞춰 골랐어요. "));

            var same = f.Submit();
            Assert.That((bool)same["qualityAuto"], Is.True, "same tier keeps qualityAuto");
            Assert.That((string)same["extra"], Is.EqualTo("kept"));

            f.Radio["quality"] = "low"; f.Toggle["mute"] = true; f.SetRange("volUi", 42); f.Radio["view"] = "first";
            var r = f.Submit();
            Assert.That((string)r["quality"], Is.EqualTo("low"));
            Assert.That((bool)r["qualityAuto"], Is.False);
            Assert.That((bool)r["mute"], Is.True);
            Assert.That((double)r["volUi"], Is.EqualTo(0.4), "42 snaps to the step of 5 -> 40");
            Assert.That((string)r["view"], Is.EqualTo("first"));
            Assert.That((double)r["textScale"], Is.EqualTo(1.15));
        }

        [Test]
        public void Settings_UnknownValues_FallBackLikeTheForm()
        {
            // no radio matches: quality keeps cur, textScale -> 1, profile -> 'none'
            var f = new SettingsForm(JObject.Parse("{quality:'auto',textScale:1.2,profile:'x',view:'top'}"));
            Assert.That(f.Radio["quality"], Is.Null);
            var r = f.Submit();
            Assert.That((string)r["quality"], Is.EqualTo("auto"));
            Assert.That((double)r["textScale"], Is.EqualTo(1));
            Assert.That((string)r["profile"], Is.EqualTo("none"));
            Assert.That((string)r["view"], Is.EqualTo("top"));
        }

        [Test]
        public void Settings_RangeSanitize_ClampSnapAndNaN()
        {
            var f = new SettingsForm(JObject.Parse("{sensitivity:5,volume:0.73,volEffects:'x'}"));
            Assert.That(f.Range["sensitivity"], Is.EqualTo(300), "500 clamps to max");
            Assert.That(f.Range["volume"], Is.EqualTo(75), "73 snaps to 75");
            Assert.That(f.Range["volEffects"], Is.EqualTo(50), "NaN -> midpoint");
            f.StepRange("sensitivity", +1);
            Assert.That(f.Range["sensitivity"], Is.EqualTo(300));
            f.StepRange("volume", -1);
            Assert.That(f.RangeText("volume"), Is.EqualTo("70%"));
            f.SetRange("sensitivity", 0);
            Assert.That(f.Range["sensitivity"], Is.EqualTo(30));
            Assert.That((double)f.Submit()["sensitivity"], Is.EqualTo(0.3));
        }

        [Test]
        public void Settings_Cancel_ReturnsCurUnchanged()
        {
            var f = new SettingsForm(JObject.Parse("{volume:0.2}"));
            f.SetRange("volume", 90); f.Toggle["captions"] = false;
            var c = f.Cancel();
            Assert.That((double)c["volume"], Is.EqualTo(0.2));
            Assert.That((bool)c["captions"], Is.True);
            Assert.That(c["qualityAuto"], Is.Null);
        }

        [Test]
        public void Settings_StoryFields_MapProfileNoneToEmpty()
        {
            Assert.That(MenuLogic.StoryFields(JObject.Parse("{captions:false,profile:'none',memoryCue:true,reduceMotion:true}")), Is.EqualTo((false, "", true, true)));
            Assert.That(MenuLogic.StoryFields(JObject.Parse("{profile:'deaf',memoryCue:false}")), Is.EqualTo((true, "deaf", false, false)));
            Assert.That(MenuLogic.StoryFields(JObject.Parse("{profile:'bogus'}")).profile, Is.EqualTo(""));
        }

        // ---------- about ----------

        [Test]
        public void About_GroupsByPublisherInFirstSeenOrder()
        {
            var src = JArray.Parse("[{publisher:'행정안전부',title:'A',url:'https://a',checkedAt:'2025-01-02'},{publisher:'소방청',title:'B'},{publisher:'행정안전부',title:'C'}]");
            var g = MenuLogic.GroupSources(src, out var total);
            Assert.That(total, Is.EqualTo(3));
            Assert.That(g.Select(x => x.publisher), Is.EqualTo(new[] { "행정안전부", "소방청" }));
            Assert.That(g[0].list.Select(s => s.Title), Is.EqualTo(new[] { "A", "C" }));
            Assert.That(g[0].list[0].CheckedText, Is.EqualTo(" · 확인 2025-01-02"));
            Assert.That(g[1].list[0].Url, Is.Null);
            Assert.That(MenuLogic.AboutSourcesHead(total), Is.EqualTo("참고한 공식 자료 3건"));
            MenuLogic.GroupSources(null, out var none);
            Assert.That(none, Is.EqualTo(0));

            // no publisher: an empty heading (undefined and null stay apart, like Map keys)
            var odd = MenuLogic.GroupSources(JArray.Parse("[{title:'X'},{publisher:null,title:'Y'},{title:'Z'}]"), out _);
            Assert.That(odd.Select(x => x.publisher), Is.EqualTo(new[] { "", "" }));
            Assert.That(odd[0].list.Select(s => s.Title), Is.EqualTo(new[] { "X", "Z" }));
        }

        // ---------- skyline ----------

        [Test]
        public void Skyline_LayoutMatchesTheOriginalSeed()
        {
            // reference numbers from running the original layout() at 1920 x 1080 in node
            var s = new Skyline(1920, 1080);
            Assert.That(s.Layers.Select(l => l.Count), Is.EqualTo(new[] { 28, 10, 20 }));
            Assert.That(s.Layers.Select(l => l.Sum(b => b.Win.Count)), Is.EqualTo(new[] { 1168, 923, 199 }));
            Assert.That(s.Poles.Count, Is.EqualTo(7));
            Assert.That(s.Layers[1][0].X, Is.EqualTo(-28.1644).Within(1e-9));
            Assert.That(s.Layers[1][0].W, Is.EqualTo(176.843).Within(1e-9));
            Assert.That(s.Layers[1][0].Win[0].Col, Is.EqualTo("#ffc56b"));
            Assert.That(s.Layers[1][0].Win[0].K, Is.EqualTo(0.50428).Within(1e-12));
            Assert.That(s.Layers[1].Last().Label, Is.EqualTo("110"));
            Assert.That(s.Stars[139].X, Is.EqualTo(319.6032).Within(1e-9));
            Assert.That(s.Poles.Last(), Is.EqualTo(1809.2456).Within(1e-9));
        }

        [Test]
        public void Skyline_Timeline()
        {
            Assert.That(Skyline.Phase(9000, false), Is.EqualTo(0.5));
            Assert.That(Skyline.Phase(9000, true), Is.EqualTo(0.2));
            Assert.That(Skyline.Dark(0.2), Is.EqualTo(0));
            Assert.That(Skyline.Dark(0.47), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(Skyline.Wave(0.6), Is.EqualTo(2));
            Assert.That(Skyline.Dark(0.92), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(Skyline.Mix("#8a4d3f", "#1a1626", 0.5), Is.EqualTo((82, 50, 51)));
            Assert.That(Skyline.Mix("#0d1530", "#05070f", 0.3), Is.EqualTo((11, 17, 38)));
            var s = new Skyline(1920, 1080);
            var w = new Skyline.Window { X = 960, K = 0.5, Off = 0.5 };
            Assert.That(s.WindowOn(w, 0.2, 0), Is.True);
            Assert.That(s.WindowOn(w, 0.44, 0), Is.True, "front at 0.2 of the width: x 0.5 still lit");
            Assert.That(s.WindowOn(w, 0.51, 0), Is.False, "front at 0.9 passed it");
            Assert.That(s.WindowOn(w, 0.7, 0), Is.False);
            Assert.That(s.WindowOn(w, 0.95, 0), Is.True, "k 0.5 < 0.6875");
        }

        [Test]
        public void Js_Semantics()
        {
            Assert.That(UiJs.NumStr(1.15), Is.EqualTo("1.15"));
            Assert.That(UiJs.NumStr(1), Is.EqualTo("1"));
            Assert.That(UiJs.Str(new JValue(0.9)), Is.EqualTo("0.9"));
            Assert.That(UiJs.Truthy(new JValue("")), Is.False);
            Assert.That(UiJs.Num(JValue.CreateNull()), Is.EqualTo(0));
            Assert.That(UiJs.Fixed(2.25, 1), Is.EqualTo("2.3").Or.EqualTo("2.2"));
        }
    }
}
