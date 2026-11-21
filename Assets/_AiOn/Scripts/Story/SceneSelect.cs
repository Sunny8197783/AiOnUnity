// Judge entry (competition upgrade): the title's "장면 바로가기" (start any signature moment from a prepared state) and
// "교사용 30분 모드" (a short curated route through those moments with a one-line lesson card between them).
//
// A prepared state is a save exactly like the director's own ({ index, G, snap }, SaveCodec v2), taken from a scripted
// playthrough (StoryPlaythroughTests.DemoSaves_*) at a checkpoint, or for the shelter right after arriving (a resume key
// only prepared saves carry: DDay.ShelterResume). The files live in Resources/Demo/<id>.json; the host hands their text
// to the director (StoryDirector.PreparedSave). A run started from one saves under its own key (SaveKey below), never
// the player's main save, and records no ending or challenge (StoryDirector.DemoRun).
//
// Pure: no UnityEngine, so the EditMode tests read it directly.
using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Data;
using AiOn.Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    /// <summary>One signature moment on the scene-select screen.</summary>
    public sealed class DemoMoment
    {
        public string Id;
        /// <summary>The chapter tag ("2장") and the chapter id the prepared save resumes in.</summary>
        public string No, Chapter;
        public string Title, When, Line;
        /// <summary>False: a slot a later package fills (shown as "준비 중", not startable).</summary>
        public bool Ready = true;
        /// <summary>The resume key the prepared save must carry (its checkpoint key).</summary>
        public string Key;
    }

    /// <summary>One stop of the teacher route: the moment, then the lesson card (a sourced fact) after it.</summary>
    public sealed class TeacherStep
    {
        public string Moment, LessonTitle, Fact;
        /// <summary>The story mark (StoryContext.Reached) that ends the moment; null plays its chapter to the end.</summary>
        public string StopAt;
        public TeacherStep(string moment, string lessonTitle, string fact, string stopAt = null) { Moment = moment; LessonTitle = lessonTitle; Fact = fact; StopAt = stopAt; }
    }

    /// <summary>A prepared save, read and checked: where the run starts.</summary>
    public sealed class PreparedStart
    {
        public JObject Saved;
        public int Index;
        /// <summary>The resume key (null = the chapter's own start).</summary>
        public string Resume;
        public string Key;
    }

    public static class SceneSelect
    {
        /// <summary>Where a run started from a prepared state saves (never StoryDirector.SaveKey).</summary>
        public const string SaveKey = "gyeongbo.demo.v1";
        /// <summary>The player's own profile and captions while a teacher-mode class runs (put back after it, or at the next title).</summary>
        public const string TeacherRestoreKey = "gyeongbo.teacher.v1";
        /// <summary>Resources folder of the prepared saves (Resources/Demo/&lt;id&gt;.json).</summary>
        public const string ResourceFolder = "Demo";

        public const string Eyebrow = "심사·수업용";
        public const string Title = "장면 바로가기";
        public const string Note = "미리 준비한 상태에서 바로 시작합니다. 이어하기 저장은 바뀌지 않습니다.";
        public const string SoonLabel = "준비 중";

        public static readonly IReadOnlyList<DemoMoment> Moments = new[]
        {
            new DemoMoment { Id = "twentytwo", No = "1장", Chapter = "prep", Key = Prep.TwentyTwoResume, Title = "22분", When = "D-1 목 06:41",
                Line = "이유도 장소도 없는 경보 문자, 먹통인 대피소 앱. 22분 동안 무엇부터 할까요?" },
            new DemoMoment { Id = "alert", No = "2장", Chapter = "dday", Key = "alert", Title = "공습경보와 계단", When = "D-day 금 20:05",
                Line = "정전된 밤, 사이렌이 울립니다. 엘리베이터 대신 계단으로 가까운 대피소까지." },
            new DemoMoment { Id = "shelter", No = "2장", Chapter = "dday", Key = DDay.ShelterResume, Title = "대피소", When = "D-day 금 밤",
                Line = "명부를 쓰고 자리를 잡습니다. 그사이 서연은 다른 길로 오고 있습니다." },
            new DemoMoment { Id = "elderpov", No = "2장", Chapter = "dday", Key = DDay.ElderResume, Title = "304호의 계단", When = "D-day 금 20:05",
                Line = "같은 사이렌, 같은 계단. 이번에는 304호 어르신이 되어, 도움을 받는 사람의 걸음으로 내려갑니다." },
            new DemoMoment { Id = "cpr", No = "2장", Chapter = "dday", Key = DDay.CprResume, Title = "대피소 심폐소생술", When = "D-day 금 밤",
                Line = "쓰러진 사람 앞에서 119와 AED를 부탁하고, 지치기 전에 교대를 요청합니다." },
            new DemoMoment { Id = "mountain", No = "3장", Chapter = "endure", Key = Endure.MountainResume, Title = "산에서 내려온 사람들", When = "D+2 일 08:10",
                Line = "통합방위 병종사태 문자와 단톡방 소문. 어제 본 흔적은 다가가지 않고 집에서, 본 것만 112에 알립니다." },
            new DemoMoment { Id = "fire", No = "3장", Chapter = "endure", Key = "fire", Title = "복도 화재", When = "D+2 일 21:40",
                Line = "어두운 복도에 연기가 찹니다. 먼저 살핀 뒤 복도로 나가, 소화기로 끌지 물러날지 판단합니다." },
            new DemoMoment { Id = "look", No = "3장", Chapter = "endure", Key = Endure.LookResume, Title = "살펴서 대피", When = "D+2 일 21:40",
                Line = "불이 난 곳과 불길, 연기를 살피고 문손잡이를 손등으로 만져 본 뒤, 나갈지 집 안에서 기다릴지 정합니다." },
            new DemoMoment { Id = "extinguisher", No = "3장", Chapter = "endure", Key = Endure.ExtResume, Title = "소화기 쓰기", When = "D+2 일 21:40",
                Line = "복도의 작은 불 앞에서 안전핀을 뽑고, 출구를 등진 채 불이 난 곳을 겨눠 쓸듯이 뿌립니다. 불이 커지면 물러나 알립니다." },
            new DemoMoment { Id = "decision", No = "4장", Chapter = "crossroads", Key = "crossroads", Title = "남을까, 떠날까", When = "D+6 목",
                Line = "남은 물과 먹을 것, 들려오는 소문. 지금의 조건으로 고릅니다." },
            new DemoMoment { Id = "shelterlife", No = "4장", Chapter = "crossroads", Key = Evac.ShelterLifeResume, Title = "임시 거처의 하루", When = "D+6 목 18:40",
                Line = "돗자리 한 장의 경계, 잠 못 드는 밤, 날마다 바뀌는 안내문, 옆 돗자리 아이. 마음이 불안할 땐 숨을 고릅니다." },
            new DemoMoment { Id = "evac", No = "5장", Chapter = "evac", Key = "evac", Title = "공식 대피", When = "D+12 수",
                Line = "대피 안내 문자를 받고 가방 하나를 꾸려 집결지로 갑니다." },
        };

        public static DemoMoment Get(string id) => id == null ? null : Moments.FirstOrDefault(m => m.Id == id);

        // ---------- teacher mode ----------

        public const string TeacherTitle = "교사용 30분 모드";
        public const string TeacherMeta = "대표 장면 4개 · 약 30분 · 장면 사이에 한 줄 정리";
        public static readonly string[] TeacherIntro =
        {
            "공습경보, 복도 화재, 남을지 떠날지의 결정, 공식 대피까지 네 장면을 이어서 플레이합니다.",
            "장면이 끝날 때마다 공식 자료에서 가져온 한 줄을 함께 읽고 넘어갑니다.",
            "준비 단계는 미리 해 둔 상태로 시작합니다. 이어하기 저장과 엔딩 기록은 바뀌지 않습니다.",
        };
        public const string TeacherSummaryTitle = "오늘 함께 본 것";

        /// <summary>
        /// The route, in play order. A lesson is a fact (facts.json or facts-ext.json), never a sentence of our own; its
        /// title names the moment, not the fact's first words. alert ends once seated in the shelter (before 서연's part),
        /// fire after the corridor fire (before D+3), decision plays 4장 out (a class that moves gets the original's one-line
        /// skip, not 「임시 거처의 하루」: StoryContext.TeacherRun), evac runs to the ending document.
        /// </summary>
        public static readonly IReadOnlyList<TeacherStep> TeacherRoute = new[]
        {
            new TeacherStep("alert", "경보 속 계단", "alert_stairs", DDay.ShelteredMark),
            new TeacherStep("fire", "불이 나면 먼저 살피기", "fire_look", Endure.FireDoneMark),
            new TeacherStep("decision", "들은 말은 확인부터", "info_rumor"),
            new TeacherStep("evac", "대피명령이 내려지면", "evac_order"),
        };

        /// <summary>The lesson card's meta line: "교사용 30분 모드 · 1/4".</summary>
        public static string LessonMeta(int i) => $"{TeacherTitle} · {i + 1}/{TeacherRoute.Count}";

        // ---------- prepared saves ----------

        /// <summary>The screen's options: one entry per moment (id, no, title, when, line, ready).</summary>
        public static JObject ScreenOptions(Func<string, bool> hasSave) => new JObject
        {
            ["moments"] = new JArray(Moments.Select(m => new JObject
            {
                ["id"] = m.Id, ["no"] = m.No, ["title"] = m.Title, ["when"] = m.When, ["line"] = m.Line,
                ["ready"] = m.Ready && (hasSave == null || hasSave(m.Id)),
            })),
        };

        /// <summary>
        /// Reads a prepared save for a moment and checks it against the chapter list: it must load, carry the moment's
        /// key as its checkpoint, and point at the moment's chapter. Throws (with the reason) otherwise.
        /// </summary>
        public static PreparedStart Prepare(DemoMoment m, string json, IReadOnlyList<string> chapterIds, ItemDatabase db)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));
            if (string.IsNullOrEmpty(json)) throw new InvalidOperationException($"prepared save '{m.Id}' is missing");
            var saved = JToken.Parse(json) as JObject ?? throw new InvalidOperationException($"prepared save '{m.Id}' is not an object");
            if (saved["G"] is not JObject sg) throw new InvalidOperationException($"prepared save '{m.Id}' has no game");
            var g = SaveCodec.Load(sg.ToString(Formatting.None), db); // throws on a save the codec rejects
            var key = g.Checkpoint?.Key ?? throw new InvalidOperationException($"prepared save '{m.Id}' has no checkpoint");
            if (key != m.Key) throw new InvalidOperationException($"prepared save '{m.Id}' is at '{key}', expected '{m.Key}'");
            var idx = saved["index"];
            int index = idx != null && idx.Type == JTokenType.Integer ? (int)(long)idx : -1;
            if (index < 0 || index >= chapterIds.Count || chapterIds[index] != m.Chapter)
                throw new InvalidOperationException($"prepared save '{m.Id}' points at chapter {index}, expected '{m.Chapter}'");
            return new PreparedStart { Saved = saved, Index = index, Key = key, Resume = chapterIds[index] == key ? null : key };
        }
    }
}
