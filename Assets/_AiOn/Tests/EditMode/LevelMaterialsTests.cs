// LevelMaterials: the pure parts of the level's state/lit materials (rule choice like LevelStateVariants, the order
// rules of several keys overlay, core.js lit levels with grid power, kit.js backlit 'dim' panels, colour retargeting).
using System.Collections.Generic;
using AiOn.Levels;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AiOn.Tests
{
    public class LevelMaterialsTests
    {
        const float Eps = 1e-4f;

        static void AssertColor(Color expected, Color actual, string why = null)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(Eps), why);
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(Eps), why);
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(Eps), why);
        }

        // ---------- names

        [Test]
        public void ParseName_ReadsLitGroupDimAndIndex()
        {
            Assert.That(LevelMaterials.ParseName("lit:indoor~m26", out var g, out var dim, out var n), Is.True);
            Assert.That((g, dim, n), Is.EqualTo(("indoor", -1f, 26)));

            Assert.That(LevelMaterials.ParseName("lit:shops:dim=0.35~m7", out g, out dim, out n), Is.True);
            Assert.That(g, Is.EqualTo("shops"));
            Assert.That(dim, Is.EqualTo(0.35f).Within(Eps));
            Assert.That(n, Is.EqualTo(7));

            Assert.That(LevelMaterials.ParseName("m~m164 (Instance)", out g, out dim, out n), Is.True);
            Assert.That((g, dim, n), Is.EqualTo(("", -1f, 164)));

            Assert.That(LevelMaterials.ParseName("Blockout_Floor", out _, out _, out _), Is.False);
            Assert.That(LevelMaterials.ParseName("odd~m", out _, out _, out _), Is.False);
        }

        // ---------- rule choice

        static List<string> Values(params string[] json)
        {
            var l = new List<string>();
            foreach (var j in json) l.Add(LevelMaterials.CanonicalJson(j));
            return l;
        }

        [Test]
        public void SelectRule_StringValue_ExactMatchOrNone()
        {
            var v = Values("\"off\"", "\"emergency\"");
            Assert.That(LevelMaterials.SelectRule(v, "off"), Is.EqualTo(0));
            Assert.That(LevelMaterials.SelectRule(v, "emergency"), Is.EqualTo(1));
            Assert.That(LevelMaterials.SelectRule(v, "on"), Is.EqualTo(-1));
            Assert.That(LevelMaterials.SelectRule(v, null), Is.EqualTo(-1));
            Assert.That(LevelMaterials.SelectRule(v, JValue.CreateNull()), Is.EqualTo(-1));
        }

        [Test]
        public void SelectRule_NumbersMatchWhetherWrittenAsIntOrFloat()
        {
            var v = Values("0", "1", "2", "3");
            Assert.That(LevelMaterials.SelectRule(v, new JValue(2)), Is.EqualTo(2));
            Assert.That(LevelMaterials.SelectRule(v, new JValue(2.0)), Is.EqualTo(2));
            Assert.That(LevelMaterials.SelectRule(v, new JValue(2.5)), Is.EqualTo(-1));
        }

        [Test]
        public void SelectRule_ListValue_ExactThenFirstElementWithARule()
        {
            var v = Values("[\"danji\"]", "[\"sijang\"]", "[\"danji\",\"sijang\"]");
            Assert.That(LevelMaterials.SelectRule(v, new JArray("danji", "sijang")), Is.EqualTo(2), "exact list wins");
            Assert.That(LevelMaterials.SelectRule(v, new JArray("other", "sijang")), Is.EqualTo(1));
            Assert.That(LevelMaterials.SelectRule(v, new JArray("sijang", "danji")), Is.EqualTo(1), "first element with a rule");
            Assert.That(LevelMaterials.SelectRule(v, new JArray("other")), Is.EqualTo(-1));
            Assert.That(LevelMaterials.SelectRule(v, new JArray()), Is.EqualTo(-1));
        }

        [Test]
        public void SelectRule_ObjectValue_ExactThenFieldByField()
        {
            var v = Values("{\"east\":\"open\"}", "{\"east\":\"closed\"}", "{\"stairs\":\"closed\"}");
            Assert.That(LevelMaterials.SelectRule(v, JObject.Parse("{\"east\":\"closed\"}")), Is.EqualTo(1));
            Assert.That(LevelMaterials.SelectRule(v, JObject.Parse("{\"ramp\":\"open\",\"stairs\":\"closed\"}")), Is.EqualTo(2));
            Assert.That(LevelMaterials.SelectRule(v, JObject.Parse("{\"east\":\"open\",\"stairs\":\"closed\"}")), Is.EqualTo(0), "first field with a rule");
            Assert.That(LevelMaterials.SelectRule(v, JObject.Parse("{\"ramp\":\"open\"}")), Is.EqualTo(-1));
        }

        // ---------- overlay of several keys

        static LevelMaterials.Entry Entry() => new LevelMaterials.Entry
        {
            importedColor = new Color(0.5f, 0.5f, 0.5f), importedEmission = new Color(1, 1, 1), importedOpacity = 1,
        };

        static LevelMaterials.MatState Defaults(LevelMaterials.Entry e) => new LevelMaterials.MatState
        {
            Color = e.importedColor, Emission = e.importedEmission, Opacity = e.importedOpacity,
        };

        [Test]
        public void Compose_RulesOverlayInTheOrderTheirKeysWereLastApplied()
        {
            var e = Entry();
            var powerOff = new LevelMaterials.Rule { key = "power", value = "\"off\"", hasEmission = true, emission = Color.black };
            var night = new LevelMaterials.Rule { key = "windowView", value = "\"night\"", hasEmission = true, emission = new Color(0.2f, 0.3f, 0.4f), hasColor = true, color = new Color(0.1f, 0.1f, 0.1f) };
            var active = new Dictionary<string, LevelMaterials.Rule> { ["power"] = powerOff, ["windowView"] = night };

            var s = LevelMaterials.Compose(e, Defaults(e), new[] { "power", "windowView" }, active);
            AssertColor(new Color(0.2f, 0.3f, 0.4f), s.Emission, "windowView applied last");
            AssertColor(new Color(0.1f, 0.1f, 0.1f), s.Color);

            s = LevelMaterials.Compose(e, Defaults(e), new[] { "windowView", "power" }, active);
            AssertColor(Color.black, s.Emission, "power applied last");
            AssertColor(new Color(0.1f, 0.1f, 0.1f), s.Color, "power leaves the colour to windowView");
        }

        [Test]
        public void Compose_AKeyWithoutAnActiveRuleFallsBackToTheDefault()
        {
            var e = Entry();
            var powerOff = new LevelMaterials.Rule { key = "power", value = "\"off\"", hasEmission = true, emission = Color.black, hasOpacity = true, opacity = 0.5f };
            var active = new Dictionary<string, LevelMaterials.Rule> { ["power"] = powerOff };
            var order = new[] { "power" };
            Assert.That(LevelMaterials.Compose(e, Defaults(e), order, active).Opacity, Is.EqualTo(0.5f));

            active.Remove("power"); // the state went back to a value without a rule
            var s = LevelMaterials.Compose(e, Defaults(e), order, active);
            AssertColor(e.importedEmission, s.Emission);
            AssertColor(e.importedColor, s.Color);
            Assert.That(s.Opacity, Is.EqualTo(1f));
        }

        // ---------- lit levels

        [Test]
        public void MergeLit_PresetGroupsTakeItsValues_OthersKeepTheirs()
        {
            var prev = new Dictionary<string, float> { ["yard"] = 0.3f, ["indoor"] = 0.5f, ["street"] = 1f };
            var preset = new Dictionary<string, float> { ["indoor"] = 1f, ["shops"] = 0.5f, ["signs"] = 0.4f };
            var next = LevelMaterials.MergeLit(prev, preset, power: true);
            Assert.That(next["indoor"], Is.EqualTo(1f));
            Assert.That(next["shops"], Is.EqualTo(0.5f));
            Assert.That(next["signs"], Is.EqualTo(0.4f));
            Assert.That(next["yard"], Is.EqualTo(0.3f), "not named: kept");
            Assert.That(next["street"], Is.EqualTo(1f), "not named: kept");
            Assert.That(prev["indoor"], Is.EqualTo(0.5f), "previous levels untouched");
        }

        [Test]
        public void MergeLit_PowerOffZeroesOnlyTheGridGroupsThePresetNames()
        {
            var prev = new Dictionary<string, float> { ["street"] = 1f, ["yard"] = 0.7f };
            var preset = new Dictionary<string, float> { ["windows"] = 1f, ["shops"] = 0.65f, ["signs"] = 1f, ["indoor"] = 1f, ["emergency"] = 1f };
            var off = LevelMaterials.MergeLit(prev, preset, power: false);
            Assert.That(off["windows"], Is.EqualTo(0f));
            Assert.That(off["shops"], Is.EqualTo(0f));
            Assert.That(off["signs"], Is.EqualTo(0f));
            Assert.That(off["indoor"], Is.EqualTo(1f), "battery/indoor groups stay");
            Assert.That(off["emergency"], Is.EqualTo(1f));
            Assert.That(off["street"], Is.EqualTo(1f), "a grid group the preset does not name keeps its level (core.js litTarget)");
            Assert.That(off["yard"], Is.EqualTo(0.7f));

            var on = LevelMaterials.MergeLit(off, preset, power: true);
            Assert.That(on["windows"], Is.EqualTo(1f), "power back: the preset value returns");
            Assert.That(on["shops"], Is.EqualTo(0.65f));
        }

        [Test]
        public void MergeLit_HandlesMissingTables()
        {
            Assert.That(LevelMaterials.MergeLit(null, null, true), Is.Empty);
            var next = LevelMaterials.MergeLit(null, new Dictionary<string, float> { ["street"] = 1f }, false);
            Assert.That(next["street"], Is.EqualTo(0f));
        }

        // ---------- dim panels and lit emission

        [Test]
        public void DimFactor_FollowsKitPut()
        {
            Assert.That(LevelMaterials.DimFactor(0.2f, 0f), Is.EqualTo(0.2f).Within(Eps));
            Assert.That(LevelMaterials.DimFactor(0.2f, 1f), Is.EqualTo(1f).Within(Eps));
            Assert.That(LevelMaterials.DimFactor(0.2f, 0.5f), Is.EqualTo(0.6f).Within(Eps));
            Assert.That(LevelMaterials.DimFactor(0.2f, 3f), Is.EqualTo(1f).Within(Eps), "min(1, v/base)");
        }

        [Test]
        public void Finish_ScalesEmissionByLevel_AndDimsBacklitColour()
        {
            var c = new Color(0.8f, 0.6f, 0.4f);
            var e = new Color(2f, 1f, 0.5f);

            var (c1, e1) = LevelMaterials.Finish(c, e, 0.5f, -1f);
            AssertColor(c, c1, "no dim: colour unchanged");
            AssertColor(new Color(1f, 0.5f, 0.25f), e1);

            var (c2, e2) = LevelMaterials.Finish(c, e, 0f, 0.25f);
            AssertColor(new Color(0.2f, 0.15f, 0.1f), c2, "off: colour * dim");
            AssertColor(Color.black, e2);

            var (c3, e3) = LevelMaterials.Finish(c, e, 0.0004f, 0.25f);
            AssertColor(new Color(0.2f, 0.15f, 0.1f), c3, "below 0.0005 the group is off");
            AssertColor(Color.black, e3);

            var (c4, _) = LevelMaterials.Finish(c, e, 1f, 0.25f);
            AssertColor(c, c4, "full level: the exported colour");
        }

        // ---------- colour retargeting

        [Test]
        public void Retarget_UsesTheRatioToTheExportedDefault()
        {
            var imported = new Color(0.4f, 0.2f, 0.1f);
            var def = new Color(0.5f, 0.25f, 0.125f); // e.g. the import encoded the colour a little differently
            var r = LevelMaterials.Retarget(imported, true, def, new Color(1f, 0.25f, 0f));
            AssertColor(new Color(0.8f, 0.2f, 0f), r);
            AssertColor(imported, LevelMaterials.Retarget(imported, true, def, def), "the default maps to the import");
        }

        [Test]
        public void Retarget_ZeroOrMissingDefaultsUseTheValueItself()
        {
            var imported = new Color(0.3f, 0f, 0f);
            var r = LevelMaterials.Retarget(imported, true, new Color(0.3f, 0f, 0f), new Color(0.6f, 4f, 1.4f));
            AssertColor(new Color(0.6f, 4f, 1.4f), r, "zero channels: absolute");
            r = LevelMaterials.Retarget(imported, false, Color.black, new Color(0.6f, 0.5f, 0.4f));
            AssertColor(new Color(0.6f, 0.5f, 0.4f), r, "no default: absolute");
        }

        [Test]
        public void Overlay_RetargetsRuleColoursAndKeepsUnlistedFields()
        {
            var e = new LevelMaterials.Entry
            {
                importedColor = new Color(0.5f, 0.5f, 0.5f), importedEmission = new Color(2f, 2f, 2f), importedOpacity = 1,
                hasDef = true, hasDefColor = true, defColor = new Color(0.25f, 0.25f, 0.25f), hasDefEmission = true, defEmission = new Color(1f, 1f, 0f),
            };
            var rule = new LevelMaterials.Rule { hasEmission = true, emission = new Color(0.5f, 0f, 3f) };
            var s = LevelMaterials.Overlay(Defaults(e), e, rule);
            AssertColor(new Color(1f, 0f, 3f), s.Emission, "ratio on r/g, absolute on the zero b default");
            AssertColor(e.importedColor, s.Color, "colour not in the rule");
        }

        // ---------- state pictures (flipped copies, default-equal detection)

        [Test]
        public void FlipRows_MirrorsRowsVertically()
        {
            Color32 A = new(1, 0, 0, 255), B = new(2, 0, 0, 255), C = new(3, 0, 0, 255), D = new(4, 0, 0, 255), E = new(5, 0, 0, 255), F = new(6, 0, 0, 255);
            var px = new[] { A, B, C, D, E, F }; // 2 wide, 3 high: rows AB, CD, EF
            var o = LevelMaterials.FlipRows(px, 2, 3);
            Assert.That(o, Is.EqualTo(new[] { E, F, C, D, A, B }));
            Assert.That(LevelMaterials.FlipRows(o, 2, 3), Is.EqualTo(px), "flipping twice restores the picture");
        }

        [Test]
        public void MeanAbsDiff_AveragesChannelsAndRejectsSizeMismatch()
        {
            var a = new[] { new Color32(10, 20, 30, 255), new Color32(0, 0, 0, 0) };
            var b = new[] { new Color32(14, 20, 30, 255), new Color32(0, 0, 0, 4) };
            Assert.That(LevelMaterials.MeanAbsDiff(a, a), Is.EqualTo(0f));
            Assert.That(LevelMaterials.MeanAbsDiff(a, b), Is.EqualTo(1f).Within(Eps), "(4 + 4) / 8 channels");
            Assert.That(LevelMaterials.MeanAbsDiff(a, new[] { a[0] }), Is.EqualTo(-1f));
        }
    }
}
