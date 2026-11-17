using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static AiOn.Tests.TestSupport;

namespace AiOn.Tests
{
    /// <summary>
    /// Port of the original tests/relay.test.mjs, plus parity with the original (Fixtures/golden-relay.json,
    /// regenerate with Tools/golden/make-golden-relay.mjs).
    /// </summary>
    public class RelayTests
    {
        const int Gate = 20 * 60 + 9;
        /// <summary>Words that would blame someone: no pin, closing line or claim row may use them.</summary>
        const string Blame = @"실수|잘못|탓|서연(이|은)\s*못";

        GameRules R;

        static Facts _facts;
        static Facts F => _facts ??= Facts.Load(Fixture("../../../Data/Source/facts.json"));
        static JObject _golden;
        static JObject Golden => _golden ??= JObject.Parse(Fixture("golden-relay.json"));

        [SetUp]
        public void SetUp() => R = Rules;

        /// <summary>정민 sees the east gate, enters A or B; the D-day chapter then sets G.relay.</summary>
        GameState Played(string route = "stairs", string via = "sign", int gate = Gate, int sent = Gate + 4)
        {
            var g = R.CreateGame(1);
            GameRules.RecordEvent(g, "gate_observed", new JObject { ["who"] = "jeongmin", ["via"] = via, ["minute"] = gate });
            GameRules.RecordEvent(g, "route_attempt", new JObject { ["who"] = "jeongmin", ["route"] = route, ["minute"] = gate + 2 });
            Relay.StoreTimes(g, Relay.RelayTimes(sent, gate));
            return g;
        }

        /// <summary>Every combination of chips the compose screen allows.</summary>
        static IEnumerable<NoteChips> AllChips()
        {
            foreach (var what in new[] { "gate_closed", "all_closed" })
                foreach (var time in new[] { false, true })
                    foreach (var route in new[] { false, true })
                        foreach (var unknowns in new[] { false, true })
                            foreach (var advice in new[] { null, "follow_me", "direction", "check_notice" })
                                yield return new NoteChips { What = what, Time = time, Route = route, Unknowns = unknowns, Advice = advice };
        }

        static bool Before(string a, string b) => string.CompareOrdinal(a, b) < 0;

        [Test]
        public void RelayTimes_C_opens_after_the_note_is_sent_and_before_Seoyeon_reads_it()
        {
            AssertJson(Relay.RelayTimes(20 * 60 + 13), "{gateSeenAt:'20:10',sentAt:'20:13',accessOpenAt:'20:16',receivedAt:'20:17'}");
            AssertJson(Relay.RelayTimes(20 * 60 + 30, Gate), "{gateSeenAt:'20:09',sentAt:'20:30',accessOpenAt:'20:33',receivedAt:'20:34'}");
            Assert.AreEqual("20:08", Relay.RelayTimes(20 * 60 + 5, 20 * 60 + 8).SentAt, "never sent before the gate was seen");
            for (int sent = 20 * 60; sent <= 21 * 60 + 30; sent++)
                for (int gate = sent - 20; gate <= sent + 5; gate++)
                {
                    var t = Relay.RelayTimes(sent, gate);
                    string at = $"sent {sent} gate {gate}";
                    Assert.That(!Before(t.SentAt, t.GateSeenAt) && Before(t.SentAt, t.AccessOpenAt) && Before(t.AccessOpenAt, t.ReceivedAt), at);
                    Assert.That(!Before(t.AccessOpenAt, "20:16"), at);
                }
            foreach (var m in new[] { 0, 59, 600, 1209, 1439 }) Assert.AreEqual(GameRules.Hhmm(m), Relay.Hhmm(m), "the same clock text as the state");
        }

        [Test]
        public void Entrances_open_to_Jeongmin_are_outside_Seoyeons_card_only_C_fits_it()
        {
            CollectionAssert.AreEqual(new[] { "stairs", "ramp", "access" }, Relay.Entrances.Keys);
            foreach (var id in new[] { "stairs", "ramp" })
            {
                var e = Relay.Entrances[id];
                Assert.IsFalse(e.SeoyeonOk);
                Assert.That(!string.IsNullOrEmpty(e.Card) && !string.IsNullOrEmpty(e.Short) && !string.IsNullOrEmpty(e.Label), id);
            }
            Assert.IsTrue(Relay.Entrances["access"].SeoyeonOk);
            AssertSameJson(JObject.FromObject(Relay.Entrances), Golden["entrances"], "ENTRANCES");
            Assert.AreEqual((int)Golden["maxNote"], Relay.MaxNote);
        }

        [Test]
        public void NoteContext_reads_Jeongmins_real_entrance_and_how_the_gate_was_learned()
        {
            AssertJson(Relay.NoteContext(Played("ramp", "notice_after")), "{route:'ramp',observedVia:'notice_after'}");
            var g = Played("stairs");
            GameRules.RecordEvent(g, "route_attempt", new JObject { ["who"] = "seoyeon", ["route"] = "ramp" });
            GameRules.RecordEvent(g, "route_attempt", new JObject { ["who"] = "jeongmin", ["route"] = "access" });
            GameRules.RecordEvent(g, "gate_observed", new JObject { ["who"] = "jeongmin", ["via"] = "notice_after" });
            AssertJson(Relay.NoteContext(g), "{route:'stairs',observedVia:'sign'}", "서연's moves, C and later notices do not count");
            GameRules.RecordEvent(g, "route_attempt", new JObject { ["who"] = "jeongmin", ["route"] = "ramp" });
            Assert.AreEqual("ramp", Relay.NoteContext(g).Route, "the last entrance 정민 used");
            foreach (var g0 in new[] { R.CreateGame(2), null }) AssertJson(Relay.NoteContext(g0), "{route:'stairs',observedVia:'sign'}");
        }

        [Test]
        public void Every_chip_note_fits_never_mentions_C_and_parses_back_to_exactly_the_chips()
        {
            foreach (var gateSeenAt in new[] { "20:09", Relay.RelayTimes(20 * 60 + 14).GateSeenAt })
                foreach (var route in new[] { "stairs", "ramp" })
                    foreach (var observedVia in new[] { "sign", "notice_after" })
                        foreach (var chips in AllChips())
                        {
                            var text = Relay.ComposeNote(chips, route, observedVia, gateSeenAt);
                            Assert.That(text.Length, Is.LessThanOrEqualTo(Relay.MaxNote), text);
                            Assert.That(text, Does.Not.Match("C|경사로"));
                            Assert.AreEqual(chips.Time, text.Contains(gateSeenAt), text);
                            var c = Relay.ParseNote(text);
                            Assert.AreEqual(chips.What, c.What, text);
                            Assert.AreEqual(chips.Time, !string.IsNullOrEmpty(c.Time), text);
                            Assert.AreEqual(chips.Route ? route : null, c.Route, text);
                            Assert.AreEqual(chips.Unknowns, c.Unknowns, text);
                            Assert.AreEqual(chips.Advice, c.Advice, text);
                            foreach (var quote in c.Quotes.Values) Assert.That(text.Contains(quote), quote);
                        }
            Assert.AreEqual("", Relay.ComposeNote());
            Assert.AreEqual(Relay.ComposeNote(new NoteChips { Route = true }, "stairs"), Relay.ComposeNote(new NoteChips { Route = true }, "access"), "unknown route falls back to stairs");
        }

        [Test]
        public void ParseNote_reads_free_text_by_rule()
        {
            var cases = new (string text, string expected)[]
            {
                ("동쪽문 막힘ㅠ 난 계단으로 옴 이쪽으로 ㄱㄱ", "{what:'gate_closed',route:'stairs'}"),
                ("대피소 전부 닫힘", "{what:'all_closed'}"),
                ("다른 입구는 모두 확인 못 했어", "{what:null,unknowns:true}"),
                ("C로 와", "{route:'access'}"),
                ("ㅋㅋㅋ", "{what:null,route:null,advice:null,time:null}"),
                ("2시 10분에 동쪽 출입구 통제 중", "{what:'gate_closed',time:'2시 10분'}"),
                ("20:09 대피소 동쪽 출입구 닫힘. 다른 입구는 몰라", "{what:'gate_closed',time:'20:09',unknowns:true}"),
                ("나 대피소에 막 도착했어", "{what:null}"),
                ("대피시설이 닫혀 있었어", "{what:'all_closed'}"),
                ("20:09 동쪽 닫힘. 나머지 다 닫혔는지 확인 못 함", "{what:'gate_closed',unknowns:true}"),
                ("한빛 생활문화센터 지하주차장으로 와", "{route:null}"),
                ("계단 말고 다른 입구로 가", "{route:null}"),
                ("1시간 전에 동쪽 문 닫혀 있었어", "{time:null,what:'gate_closed'}"),
                ("20:09 동쪽 폐쇄. B 진출입로 운영. C는 몰라. 안내 다시 확인!", "{what:'gate_closed',time:'20:09',route:'ramp',unknowns:true,advice:'check_notice'}"),
            };
            foreach (var (text, expected) in cases)
            {
                var c = JObject.FromObject(Relay.ParseNote(text));
                foreach (var p in JObject.Parse(expected).Properties()) AssertSameJson(c[p.Name], p.Value, $"{text} → {p.Name}");
            }
            foreach (var empty in new[] { "", null })
                AssertJson(Relay.ParseNote(empty), "{what:null,time:null,route:null,unknowns:false,advice:null,quotes:{}}");
            Assert.AreEqual("precise", Relay.NoteKind(Relay.ParseNote("20:09 대피소 동쪽 출입구 닫힘. 다른 입구는 몰라")));
            Assert.AreEqual("unparsed", Relay.NoteKind(Relay.ParseNote("ㅋㅋㅋ")));
            Assert.AreEqual("generalized", Relay.NoteKind(Relay.ParseNote("대피소 전부 닫힘")));
            Assert.AreEqual("partial", Relay.NoteKind(Relay.ParseNote("동쪽 출입구가 닫혀 있었어")));
            Assert.AreEqual("precise", Relay.NoteKind(Relay.ParseNote("20:09에 동쪽 출입구가 닫혀 있었어. 다른 입구 상태는 확인 못 했어.")));
        }

        [Test]
        public void MakeHandover_keeps_the_players_words_sanitizes_edits_and_falls_back_to_chips()
        {
            var chips = new NoteChips { What = "gate_closed", Time = true, Route = true, Unknowns = true, Advice = null };
            var g = Played("ramp");
            var h = Relay.MakeHandover(g, chips);
            var relay = Relay.StoredTimes(g);
            Assert.AreEqual(Relay.ComposeNote(chips, "ramp", "sign", relay.GateSeenAt), h.Text);
            Assert.That(h.Text, Does.Contain("20:09"));
            Assert.AreEqual("precise", h.Kind);
            Assert.AreEqual("chips", h.Claims.ParsedBy);
            Assert.AreEqual("ramp", h.ActualRoute);
            Assert.AreEqual(relay.GateSeenAt, h.GateSeenAt);
            Assert.AreEqual(relay.SentAt, h.SentAt);
            Assert.AreEqual(relay.AccessOpenAt, h.AccessOpenAt);
            Assert.AreEqual(relay.ReceivedAt, h.ReceivedAt);
            AssertSameJson(g.Extra["handover"], JObject.FromObject(h), "G.handover is the handover");
            AssertJson(g.Events.Last().Details, "{kind:'precise',edited:false,length:" + h.Text.Length + "}");
            Assert.AreEqual("handover_written", g.Events.Last().Type);
            chips.What = "all_closed";
            Assert.AreEqual("gate_closed", (string)h.Chips["what"], "chips are copied");
            Assert.AreEqual("gate_closed", (string)Relay.StoredHandover(g).Chips["what"]);

            var edited = Relay.MakeHandover(Played(), chips, "  동쪽" + (char)7 + " 막힘 " + new string('가', 200), true);
            Assert.AreEqual(Relay.MaxNote, edited.Text.Length);
            Assert.IsFalse(edited.Text.Any(ch => ch <= 0x1F || ch == 0x7F), "no control characters");
            Assert.AreEqual("rules", edited.Claims.ParsedBy);
            Assert.IsTrue(edited.Edited);

            foreach (var text in new[] { "   ", "" + (char)0 + (char)7 + "\n", null })
            {
                var blank = Relay.MakeHandover(Played(), chips, text, true);
                Assert.AreEqual(Relay.ComposeNote(chips, "stairs", "sign", "20:09"), blank.Text);
                Assert.IsFalse(blank.Edited);
            }
            Assert.AreEqual("chips", Relay.MakeHandover(Played(), chips, "무시됨").Claims.ParsedBy, "text counts only when edited");

            var noRelay = R.CreateGame(3);
            Assert.AreEqual(Relay.RelayTimes(20 * 60 + 13).GateSeenAt, Relay.MakeHandover(noRelay, chips).GateSeenAt, "default times without G.relay");
            Assert.AreEqual("unparsed", Relay.MakeHandover(R.CreateGame(3)).Kind);
            Assert.Catch<ArgumentException>(() => Relay.MakeHandover(null, chips));
            var broken = R.CreateGame(3);
            broken.Events = null;
            Assert.Catch<ArgumentException>(() => Relay.MakeHandover(broken, chips));
        }

        [Test]
        public void BuildPins_always_gives_body_time_and_one_more_reason_whatever_was_written()
        {
            var texts = new[] { "동쪽문 막힘ㅠ 난 계단으로 옴 이쪽으로 ㄱㄱ", "C로 와", "ㅋㅋㅋ", "대피소 전부 닫힘", "20:09 동쪽 폐쇄. B 진출입로 운영. C 경사로는 몰라. 안내 다시 확인" };
            var games = new List<GameState>();
            foreach (var route in new[] { "stairs", "ramp" })
            {
                foreach (var chips in AllChips()) { var g = Played(route); Relay.MakeHandover(g, chips); games.Add(g); }
                foreach (var text in texts) { var g = Played(route); Relay.MakeHandover(g, null, text, true); games.Add(g); }
            }
            games.Add(R.CreateGame(4));
            games.Add(Played("ramp"));
            var anchors = new HashSet<string> { "stairs", "ramp", "notice", "gate", "access", "start0", "start1" };
            foreach (var g in games)
            {
                var pins = Relay.BuildPins(g);
                Assert.AreEqual(3, pins.Count);
                Assert.AreEqual("body", pins[0].Factor);
                Assert.AreEqual("time", pins[1].Factor);
                CollectionAssert.Contains(new[] { "scope", "advice", "direction", "place" }, pins[2].Factor);
                foreach (var pin in pins)
                {
                    Assert.That(anchors.Contains(pin.Anchor), pin.Anchor);
                    Assert.That(!string.IsNullOrEmpty(pin.Was) && !string.IsNullOrEmpty(pin.Now));
                    Assert.That(pin.Was + pin.Now, Does.Not.Match(Blame));
                }
                Assert.IsFalse(Relay.Entrances[pins[0].Anchor].SeoyeonOk);
                if (pins[1].Anchor == "notice") Assert.That(pins[1].Now, Does.Contain(Relay.StoredTimes(g)?.ReceivedAt ?? "20:17"));
            }
            var scoped = Played();
            Relay.MakeHandover(scoped, new NoteChips { What = "all_closed" });
            Assert.AreEqual("scope", Relay.BuildPins(scoped)[2].Factor);
            var access = Played();
            Relay.MakeHandover(access, null, "C로 와", true);
            Assert.AreEqual("access", Relay.BuildPins(access)[1].Anchor);
            Assert.AreEqual("stairs", Relay.BuildPins(access)[0].Anchor, "body pin falls back to where 정민 entered");
            // 정민 who only read the notice after arriving never "saw" the gate.
            var told = Played("stairs", "notice_after");
            Relay.MakeHandover(told, new NoteChips { What = "all_closed" });
            Assert.That(Relay.BuildPins(told)[2].Now + string.Join(" ", Relay.ReadClaims(told).Select(r => r.Why)), Does.Not.Contain("직접"));
        }

        [Test]
        public void ClosingLines_two_calm_lines_for_every_kind()
        {
            foreach (var kind in new[] { "precise", "partial", "generalized", "unparsed", null })
            {
                var lines = Relay.ClosingLines(kind);
                Assert.AreEqual(2, lines.Length);
                foreach (var l in lines) Assert.That(l, Does.Not.Match(Blame));
            }
            CollectionAssert.AreNotEqual(Relay.ClosingLines("generalized"), Relay.ClosingLines("precise"));
            foreach (var p in ((JObject)Golden["closing"]).Properties())
                CollectionAssert.AreEqual(p.Value.Select(t => (string)t), Relay.ClosingLines(p.Name), p.Name);
            CollectionAssert.AreEqual(Golden["closingDefault"].Select(t => (string)t), Relay.ClosingLines(null));
        }

        [Test]
        public void ReadClaims_always_lists_C_as_after_the_note_and_cites_real_sources()
        {
            var statuses = new HashSet<string> { "helped", "recheck", "mismatch", "missing" };
            var g = Played();
            Relay.MakeHandover(g, new NoteChips { What = "gate_closed", Route = true, Advice = "follow_me" });
            var rows = Relay.ReadClaims(g);
            Assert.That(rows.Any(r => r.Label == "C 경사로" && r.Status == "missing" && r.Why.Contains(Relay.StoredTimes(g).AccessOpenAt)));
            Assert.That(rows.Any(r => r.Label == "들어간 길" && r.Status == "mismatch"));
            Assert.That(rows.Any(r => r.Label == "시각" && r.Status == "missing"));
            Assert.That(rows.Any(r => r.Label == "본 것" && r.Status == "recheck"));
            var precise = Played();
            Relay.MakeHandover(precise, new NoteChips { What = "gate_closed", Time = true, Unknowns = true, Advice = "check_notice" });
            var p = Relay.ReadClaims(precise);
            Assert.That(p.Any(r => r.Label == "본 것" && r.Status == "helped"));
            Assert.That(!p.Any(r => r.Status == "mismatch"));
            var access = Played();
            Relay.MakeHandover(access, null, "C로 와", true);
            Assert.AreEqual("recheck", Relay.ReadClaims(access).First(r => r.Label == "C 경사로").Status);
            foreach (var row in rows.Concat(p).Concat(Relay.ReadClaims(access)))
            {
                Assert.IsNotNull(F.Source(row.SourceId), row.SourceId);
                Assert.That(statuses.Contains(row.Status), row.Status);
                Assert.That(row.Why, Does.Not.Match(Blame));
            }
            Assert.IsEmpty(Relay.ReadClaims(R.CreateGame(5)));
            Assert.IsEmpty(Relay.ReadClaims(null));
        }

        // ---------- parity with the original ----------

        [Test]
        public void Golden_times_and_clock_texts_match_the_original()
        {
            var cases = (JArray)Golden["times"];
            Assert.That(cases.Count, Is.GreaterThan(300));
            foreach (var c in cases)
            {
                var t = c["gate"].Type == JTokenType.Null ? Relay.RelayTimes((int)c["sent"]) : Relay.RelayTimes((int)c["sent"], (int)c["gate"]);
                AssertSameJson(JObject.FromObject(t), c["times"], $"sent {c["sent"]} gate {c["gate"]}");
            }
            foreach (var c in Golden["clock"]) Assert.AreEqual((string)c["text"], Relay.Hhmm((int)c["minute"]));
        }

        [Test]
        public void Golden_histories_give_the_original_note_context()
        {
            var cases = (JArray)Golden["contexts"];
            Assert.AreEqual(8, cases.Count);
            foreach (var c in cases)
                AssertSameJson(JObject.FromObject(Relay.NoteContext(SaveCodec.Load(c["save"].ToString(), Db))), c["context"], (string)c["label"]);
        }

        [Test]
        public void Every_golden_chip_note_reads_and_parses_like_the_original()
        {
            var cases = (JArray)Golden["composed"];
            Assert.AreEqual(512, cases.Count);
            foreach (var c in cases)
            {
                var chips = c["chips"].ToObject<NoteChips>();
                var text = Relay.ComposeNote(chips, (string)c["route"], (string)c["observedVia"], (string)c["gateSeenAt"]);
                Assert.AreEqual((string)c["text"], text);
                var claims = Relay.ParseNote(text);
                AssertSameJson(JObject.FromObject(claims), c["claims"], text);
                Assert.AreEqual((string)c["kind"], Relay.NoteKind(claims), text);
            }
            var d = Golden["composeDefaults"];
            Assert.AreEqual((string)d["empty"], Relay.ComposeNote());
            Assert.AreEqual((string)d["route"], Relay.ComposeNote(new NoteChips { Route = true }));
            Assert.AreEqual((string)d["unknownRoute"], Relay.ComposeNote(new NoteChips { Route = true, Advice = "direction" }, "access"));
        }

        [Test]
        public void Every_golden_free_text_is_read_like_the_original()
        {
            var cases = (JArray)Golden["free"];
            Assert.That(cases.Count, Is.GreaterThan(170));
            var kinds = new Dictionary<string, int>();
            foreach (var c in cases)
            {
                var text = (string)c["text"];
                var claims = Relay.ParseNote(text);
                AssertSameJson(JObject.FromObject(claims), c["claims"], JToken.FromObject(text).ToString(Newtonsoft.Json.Formatting.None));
                var kind = Relay.NoteKind(claims);
                Assert.AreEqual((string)c["kind"], kind, text);
                kinds[kind] = (kinds.TryGetValue(kind, out var n) ? n : 0) + 1;
            }
            CollectionAssert.AreEquivalent(new[] { "precise", "partial", "generalized", "unparsed" }, kinds.Keys, "every kind is reached");
            foreach (var c in Golden["kinds"])
                Assert.AreEqual((string)c["kind"], Relay.NoteKind(c["claims"].ToObject<NoteClaims>()), c["claims"].ToString(Newtonsoft.Json.Formatting.None));
        }

        [Test]
        public void Every_golden_handover_is_written_pinned_and_read_like_the_original()
        {
            var games = (JArray)Golden["games"];
            var cases = (JArray)Golden["handovers"];
            var pinSets = (JArray)Golden["pinSets"];
            var claimSets = (JArray)Golden["claimSets"];
            Assert.That(cases.Count, Is.GreaterThan(500));
            int written = 0, reloaded = 0;
            foreach (var c in cases)
            {
                var input = c["input"];
                string at = $"game {c["game"]} {input.ToString(Newtonsoft.Json.Formatting.None)}";
                var pins = pinSets[(int)c["pins"]];
                var claims = claimSets[(int)c["claims"]];

                // Partial chips are a shape only the original writes; everything else the port writes itself.
                if ((bool?)c["jsOnly"] != true)
                {
                    var g = SaveCodec.Load(games[(int)c["game"]]["save"].ToString(), Db);
                    var h = (bool)input["none"]
                        ? Relay.MakeHandover(g)
                        : Relay.MakeHandover(g, (input["chips"] as JObject)?.ToObject<NoteChips>(), (string)input["text"], (bool)input["edited"]);
                    AssertSameJson(JObject.FromObject(h), c["handover"], at);
                    AssertSameJson(g.Extra["handover"], c["handover"], at + " (G.handover)");
                    AssertSameJson(JObject.FromObject(g.Events.Last()), c["event"], at + " (event)");
                    AssertSameJson(JArray.FromObject(Relay.BuildPins(g)), pins, at + " (pins)");
                    AssertSameJson(JArray.FromObject(Relay.ReadClaims(g)), claims, at + " (claims)");
                    if (c["after"] != null) AssertSameJson(SaveCodec.ToJObject(g), c["after"], at + " (save)");
                    written++;
                }

                // The original's own save: the handover and the times survive load and save, and read the same.
                if (c["after"] != null)
                {
                    var g = SaveCodec.Load(c["after"].ToString(), Db);
                    AssertSameJson(JObject.FromObject(Relay.StoredHandover(g)), c["handover"], at + " (loaded)");
                    AssertSameJson(JArray.FromObject(Relay.BuildPins(g)), pins, at + " (loaded pins)");
                    AssertSameJson(JArray.FromObject(Relay.ReadClaims(g)), claims, at + " (loaded claims)");
                    AssertSameJson(SaveCodec.ToJObject(g), c["after"], at + " (resaved)");
                    reloaded++;
                }
            }
            Assert.That(written, Is.GreaterThan(500));
            Assert.That(reloaded, Is.GreaterThan(20));
        }

        [Test]
        public void Without_a_message_the_pins_and_rows_are_the_originals()
        {
            var games = (JArray)Golden["games"];
            foreach (var c in Golden["bare"])
            {
                var game = games[(int)c["game"]];
                var g = SaveCodec.Load(game["save"].ToString(), Db);
                AssertSameJson(JArray.FromObject(Relay.BuildPins(g)), c["pins"], (string)game["label"]);
                AssertSameJson(JArray.FromObject(Relay.ReadClaims(g)), c["claims"], (string)game["label"]);
                Assert.IsNull(Relay.StoredHandover(g));
            }
            AssertSameJson(JArray.FromObject(Relay.BuildPins(null)), Golden["nothing"]["pins"], "no game at all");
            AssertSameJson(JArray.FromObject(Relay.ReadClaims(null)), Golden["nothing"]["claims"], "no game at all");
        }

        [Test]
        public void Stored_times_and_handover_live_in_the_save_under_their_original_names()
        {
            var g = R.CreateGame(7);
            Assert.IsNull(Relay.StoredTimes(g));
            Assert.IsNull(Relay.StoredHandover(g));
            Relay.StoreTimes(g, Relay.RelayTimes(20 * 60 + 13));
            Relay.ClearHandover(g);
            Assert.AreEqual("20:13", Relay.StoredTimes(g).SentAt);
            Assert.IsNull(Relay.StoredHandover(g), "cleared: no message yet");
            var saved = SaveCodec.ToJObject(g);
            AssertJson(saved["relay"], "{gateSeenAt:'20:10',sentAt:'20:13',accessOpenAt:'20:16',receivedAt:'20:17'}");
            Assert.AreEqual(JTokenType.Null, saved["handover"].Type);
            Relay.MakeHandover(g, new NoteChips { What = "gate_closed", Time = true });
            var back = SaveCodec.Load(SaveCodec.Save(g), Db);
            Assert.AreEqual("20:10에 동쪽 출입구가 닫혀 있었어.", Relay.StoredHandover(back).Text);
            Assert.AreEqual("20:10", Relay.StoredHandover(back).Claims.Time);
        }
    }
}
