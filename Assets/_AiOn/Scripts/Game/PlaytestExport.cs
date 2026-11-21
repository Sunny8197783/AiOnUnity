// 플레이테스트 기록 내보내기 (competition upgrade): one run (or one class) as a local JSON file and a flat CSV beside it,
// for counting playtests by hand. Nothing leaves the computer (no network), and nothing personal goes in: an anonymous
// run id made from the run's random seed, the quiz answers, the learning report's rows, the ending reached and the
// minigames' recorded summaries (a whitelist of event types; free text the player typed is never read). Pure logic
// plus System.IO; the screens pass Application.persistentDataPath + "/exports".
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Game
{
    public static class PlaytestExport
    {
        public const string Format = "aion-playtest/1";
        public const string Folder = "exports";

        /// <summary>
        /// The recorded summaries copied as they are (the latest event of each type): the event types the scenes record,
        /// by their own constants where they have one ("ext_use" is ExtinguisherScene.UseEvent, in the story layer; a test
        /// checks it and that every name here is recorded somewhere).
        /// </summary>
        public static readonly string[] MinigameEvents =
        {
            "bag_score", ExtinguisherCheck.Event, TwentyTwoLogic.Event, ElderPovLogic.Event, "cpr", "lookfirst", "fire_report", "ext_use",
            "sighting", "intercom", "unknown_object", ShelterLifeLogic.Event, "decision",
        };

        /// <summary>An anonymous, stable id for a run: a hash of its random seed (no device, no name, no clock).</summary>
        public static string RunId(uint seed)
        {
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < 4; i++) { h ^= (seed >> (i * 8)) & 0xFF; h *= 16777619u; }
                return "run-" + h.ToString("x8");
            }
        }

        static JObject QuizJson(QuizAnswers a) => a?.ToJson();

        /// <summary>
        /// The export of a run. g may be null (a class whose last state is gone): then only the quiz and the mode are in it.
        /// pre/post default to the run's own "quiz" events. mode: "story", "demo" (장면 바로가기) or "teacher".
        /// </summary>
        public static JObject Build(GameState g, string mode, DateTime nowUtc, QuizAnswers pre = null, QuizAnswers post = null, uint? seed = null,
            Func<GameState, Fate> ending = null)
        {
            pre ??= QuizLogic.Find(g, QuizLogic.Pre);
            post ??= QuizLogic.Find(g, QuizLogic.Post);
            uint s = seed ?? g?.Seed ?? pre?.Seed ?? post?.Seed ?? 0;
            var o = new JObject
            {
                ["format"] = Format,
                ["runId"] = RunId(s),
                ["mode"] = mode ?? "story",
                ["exportedAt"] = nowUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm'Z'", CultureInfo.InvariantCulture),
            };
            if (g != null) o["progress"] = new JObject { ["chapter"] = g.Chapter, ["day"] = g.Day };

            o["quiz"] = QuizSection(pre, post);

            if (g == null) return o;

            var rep = LearningReport.Build(g);
            o["report"] = new JObject
            {
                ["ok"] = rep.Ok, ["total"] = rep.Total, ["rate"] = Math.Round(rep.Rate, 3),
                ["goldenMinutes"] = rep.GoldenMinutes, ["goldenOk"] = rep.GoldenOk,
                ["axes"] = new JArray(rep.Axes.Select(a => new JObject { ["id"] = a.Id, ["ok"] = a.Ok, ["total"] = a.Total })),
                ["rows"] = new JArray(rep.Rows.Select(r => new JObject { ["id"] = r.Id, ["axis"] = r.Axis, ["ok"] = r.Ok, ["fact"] = r.FactId, ["label"] = r.Label })),
            };

            bool ended = g.Chapter == "ending" || g.Events.Any(e => e.Type == "ending");
            if (ended)
            {
                Fate f = null;
                try { f = (ending ?? (x => Endings.PickEnding(x, null)))(g); } catch (Exception) { f = null; }
                o["ending"] = f == null ? null : new JObject { ["id"] = f.Id, ["no"] = f.No, ["title"] = f.Title, ["kind"] = f.Kind, ["path"] = f.Path };
            }
            else o["ending"] = null;

            var games = new JObject();
            foreach (var type in MinigameEvents)
            {
                var e = g.Events.LastOrDefault(x => x.Type == type && x.Details != null);
                if (e != null) games[type] = e.Details.DeepClone();
            }
            o["minigames"] = games;
            return o;
        }

        /// <summary>The "quiz" part: both sittings (null when skipped) and the concept-by-concept comparison.</summary>
        public static JObject QuizSection(QuizAnswers pre, QuizAnswers post)
        {
            var cmp = QuizLogic.Compare(pre, post);
            var quiz = new JObject { ["total"] = cmp?.Total ?? QuizLogic.Items.Count, ["pre"] = QuizJson(pre), ["post"] = QuizJson(post) };
            if (cmp != null)
                quiz["compare"] = new JArray(cmp.Rows.Select(r => new JObject
                {
                    ["concept"] = r.Concept, ["label"] = r.Label, ["pre"] = r.Pre, ["post"] = r.Post, ["change"] = r.Change,
                }));
            return quiz;
        }

        /// <summary>An export with its quiz part replaced (the post quiz's screen adds the answers it just took).</summary>
        public static JObject WithQuiz(JObject export, QuizAnswers pre, QuizAnswers post)
        {
            export ??= new JObject();
            export["quiz"] = QuizSection(pre, post);
            return export;
        }

        /// <summary>
        /// A class's export (교사용 모드): the report rows of every step it played (moment id, then that step's state at its
        /// stop), beside the "report" part, which is the last step's alone (marked so).
        /// </summary>
        public static JObject WithSteps(JObject export, IEnumerable<(string moment, GameState g)> steps)
        {
            export ??= new JObject();
            if (export["report"] is JObject rep) rep["scope"] = "lastStep";
            var arr = new JArray();
            foreach (var (moment, g) in steps ?? Enumerable.Empty<(string, GameState)>())
            {
                if (g == null) continue;
                var r = LearningReport.Build(g);
                arr.Add(new JObject
                {
                    ["moment"] = moment, ["ok"] = r.Ok, ["total"] = r.Total,
                    ["rows"] = new JArray(r.Rows.Select(x => new JObject { ["id"] = x.Id, ["axis"] = x.Axis, ["ok"] = x.Ok, ["fact"] = x.FactId, ["label"] = x.Label })),
                });
            }
            export["steps"] = arr;
            return export;
        }

        /// <summary>The export flattened to "section,key,value" lines (UTF-8 text; leaves of objects joined by '.').</summary>
        public static string ToCsv(JObject export)
        {
            var sb = new StringBuilder();
            sb.Append("section,key,value\n");
            void Line(string section, string key, string value) =>
                sb.Append(Csv(section)).Append(',').Append(Csv(key)).Append(',').Append(Csv(value)).Append('\n');
            string V(JToken t) => t == null || t.Type == JTokenType.Null ? "" : t.Type == JTokenType.String ? (string)t
                : t.Type == JTokenType.Boolean ? ((bool)t ? "true" : "false") : t.Type == JTokenType.Float ? ((double)t).ToString("0.###", CultureInfo.InvariantCulture) : t.ToString(Formatting.None);

            foreach (var k in new[] { "format", "runId", "mode", "exportedAt" }) Line("run", k, V(export[k]));
            if (export["progress"] is JObject pr) foreach (var p in pr.Properties()) Line("run", p.Name, V(p.Value));

            if (export["quiz"] is JObject q)
            {
                foreach (var phase in new[] { QuizLogic.Pre, QuizLogic.Post })
                {
                    if (q[phase] is not JObject a) { Line("quiz_" + phase, "skipped", "true"); continue; }
                    // score and total on their own lines: a spreadsheet reads a bare "3/7" as a date
                    Line("quiz_" + phase, "score", V(a["score"]));
                    Line("quiz_" + phase, "total", V(a["total"]));
                    if (a["answers"] is JArray arr)
                        foreach (var x in arr.OfType<JObject>()) Line("quiz_" + phase, V(x["concept"]), $"{V(x["option"])} ({V(x["state"])})");
                }
                if (q["compare"] is JArray cmp) foreach (var x in cmp.OfType<JObject>()) Line("quiz_compare", V(x["concept"]), V(x["change"]));
            }

            if (export["report"] is JObject r)
            {
                foreach (var k in new[] { "ok", "total", "rate", "goldenMinutes", "goldenOk" }) Line("report", k, V(r[k]));
                if (r["scope"] != null) Line("report", "scope", V(r["scope"]));
                if (r["rows"] is JArray rows) foreach (var x in rows.OfType<JObject>()) Line("report_row", V(x["id"]), V(x["ok"]) == "true" ? "O" : "X");
            }
            if (export["steps"] is JArray steps)
                foreach (var st in steps.OfType<JObject>())
                {
                    string m = V(st["moment"]);
                    Line("step", m, $"{V(st["ok"])} of {V(st["total"])}");
                    if (st["rows"] is JArray rows) foreach (var x in rows.OfType<JObject>()) Line("step_row", m + "." + V(x["id"]), V(x["ok"]) == "true" ? "O" : "X");
                }
            if (export["ending"] is JObject en) foreach (var p in en.Properties()) Line("ending", p.Name, V(p.Value));
            else if (export.ContainsKey("ending")) Line("ending", "id", "");

            if (export["minigames"] is JObject games)
                foreach (var p in games.Properties()) Flatten(p.Value, p.Name, (k, v) => Line("minigame", k, v), V);
            return sb.ToString();
        }

        static void Flatten(JToken t, string path, Action<string, string> line, Func<JToken, string> value)
        {
            if (t is JObject o) { foreach (var p in o.Properties()) Flatten(p.Value, path + "." + p.Name, line, value); return; }
            line(path, value(t)); // arrays stay one JSON cell
        }

        /// <summary>A CSV cell: quoted when it holds a comma, a quote or a line break (quotes doubled).</summary>
        public static string Csv(string s)
        {
            s ??= "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>"playtest-run-1a2b3c4d-20261121-0930" (local time of the export; no other detail).</summary>
        public static string FileStem(JObject export, DateTime nowLocal) =>
            $"playtest-{(string)export["runId"] ?? "run"}-{nowLocal.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";

        /// <summary>
        /// The line a screen shows after a write: the folder and file name only, never the full path (on Windows it holds
        /// the computer's account name, and the screen may be on a projector).
        /// </summary>
        public static string SavedLine(string jsonPath) =>
            $"저장했습니다 (JSON·CSV) · 게임 기록 폴더의 {Folder}/{Path.GetFileName(jsonPath ?? "")}";

        /// <summary>Writes the JSON and the CSV into dir (made if missing); returns the JSON file's full path.</summary>
        public static string Write(JObject export, string dir, DateTime nowLocal)
        {
            Directory.CreateDirectory(dir);
            string stem = FileStem(export, nowLocal), json = Path.Combine(dir, stem + ".json"), csv = Path.Combine(dir, stem + ".csv");
            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(json, export.ToString(Formatting.Indented), utf8);
            File.WriteAllText(csv, ToCsv(export), new UTF8Encoding(true)); // a BOM so a spreadsheet reads the Korean labels
            return Path.GetFullPath(json);
        }
    }
}
