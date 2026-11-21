// Port of story/rumor-scene.js — 소식 가려듣기 (design M5) as it is played: a claim reaches 정민, the player names
// what kind of information it is with the notebook labels, then either acts on that judgement or checks once more
// (the radio, or a notice board reached on foot). The posts and the rules live in Rumors; this file only stages them.
//
// HearClaim(ctx, id, lead) -> { Pick, Res, Act: 'go'|'check', Taken, Claim } (talks through ctx.Ui: wrap it in ctx.Modal)
//   Taken = the player took the claim as true and acted on it (Rumors.Believe ran; know.misled when it is false).
// BoardWalk(ctx, title, body, read) -> roam until a notice board (aptNotice / centerNotice) has been read.
//
// The act choices carry a short note in the original (choice.note); TalkChoice has no such field, so the note rides
// in Why on an enabled choice (as StoryCommon.DayLedger does).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    public sealed class ClaimHearing
    {
        public string Pick;
        public RumorJudgement Res;
        /// <summary>go | check</summary>
        public string Act;
        public bool Taken;
        public RumorPost Claim;
    }

    public static class RumorScene
    {
        /// <summary>The four labels the notebook uses (GameRules.Labels), the same wording as Neighbors.</summary>
        public static readonly IReadOnlyList<TalkChoice> LabelChoices = new[]
        {
            new TalkChoice("official", "공식 안내"), new TalkChoice("site", "현장에서 확인한 것"), new TalkChoice("neighbor", "이웃에게 들은 것"), new TalkChoice("rumor", "소문 · 확인 안 됨"),
        };

        /// <summary>A fresh copy of <see cref="LabelChoices"/> for one talk (a host may mark choices).</summary>
        public static List<TalkChoice> LabelChoiceList() => LabelChoices.Select(c => new TalkChoice(c.Id, c.Label)).ToList();

        /// <summary>True when a talk offers exactly the notebook labels (the original tests' o.choices === LABEL_CHOICES).</summary>
        public static bool IsLabels(TalkOptions o) => o?.Choices != null && o.Choices.Select(c => c.Id).SequenceEqual(LabelChoices.Select(c => c.Id));

        /// <summary>Pure: labelling a claim 'rumor' sets it aside; any other label takes it as something to act on.</summary>
        public static bool TakesAsTrue(string pick) => pick != "rumor";

        /// <summary>Pure: acting on a claim taken as true is believing it.</summary>
        public static bool Believes(string pick, string act) => act == "go" && TakesAsTrue(pick);

        public static async Task<ClaimHearing> HearClaim(StoryContext ctx, string id, IReadOnlyList<string> lead = null)
        {
            var g = ctx.G;
            var claim = Rumors.Posts(g, id)[0];
            bool phoneOn = g.Phone.Battery > 0; // without the phone the same post reaches 정민 by word of mouth
            bool guided = Drills.LadderStage(g, "info") != "free";
            // 휴대폰 배터리 자원: in the blackout a chat thread read on the phone costs battery (PhoneBattery "chat"), only
            // while there is some to spare: at low battery the notification is read as a preview, free (never empties it)
            if (phoneOn && PhoneBattery.Live && PhoneBattery.Blackout(g.Chapter, ctx.Power) && PhoneBattery.ChargesIncoming(g.Phone.Battery))
            {
                var cost = PhoneBattery.Use(g, "chat");
                ctx.Refresh();
                ctx.Toast(PhoneBattery.UseLine("chat", cost, g.Phone.Battery), "self", 3600);
            }
            var lines = new List<string>();
            lines.AddRange(phoneOn ? (lead ?? Array.Empty<string>()) : new[] { "복도에서 누군가 큰 소리로 전하는 말이 들린다." });
            lines.Add($"\"{claim.Text}\"");
            lines.Add(guided ? "누가 말했나? 언제 확인했나? 어디를 두고 한 말인가? 이 말은 어떤 정보일까?" : "이 말은 어떤 정보일까?");
            var pick = await ctx.Ui.Talk(new TalkOptions
            {
                Speaker = phoneOn ? claim.From : "복도에서 들린 말", Role = phoneOn ? claim.Meta : "누가 확인했는지 모름",
                Lines = lines, Choices = LabelChoiceList(),
            }) ?? "rumor";
            var res = Rumors.Judge(g, id, pick);
            Drills.RecordEncounter(g, "info", res.Result);
            ctx.Note($"{claim.From}: {claim.Text}", "rumor");
            if (!res.Ok) ctx.Toast("다시 떠올려 보니, 누가 언제 확인한 말인지는 적혀 있지 않았다.", "self", 4200);
            var act = await ctx.Ui.Talk(new TalkOptions
            {
                Speaker = "정민", Role = "판단", Lines = { TakesAsTrue(pick) ? "이 말을 믿고 움직여도 될까?" : "확인되지 않은 말이다. 어떻게 할까?" },
                Choices = new List<TalkChoice>
                {
                    new("go", "이대로 움직인다", false, TakesAsTrue(pick) ? "이 말대로 한다" : "이 말은 두고 내 판단대로 한다"),
                    new("check", "한 번 더 확인한다 (라디오·게시판)", false, "시간이 조금 든다"),
                },
            }) ?? "check";
            bool taken = Believes(pick, act);
            if (taken) Rumors.Believe(g, id);
            return new ClaimHearing { Pick = pick, Res = res, Act = act, Taken = taken, Claim = claim };
        }

        /// <summary>Walk out to a notice board. Power is off by then, so the stairs; the doors of 302호 still work.</summary>
        public static async Task BoardWalk(StoryContext ctx, string title, string body, Func<Task> read)
        {
            ctx.Objective(title, body, new List<ObjectiveStep> { new("단지 게시판 또는 행정복지센터 게시판 읽기") });
            async Task<JToken> Board(StoryPoint _) { await read(); return "read"; }
            await ctx.Roam(new RoamOptions
            {
                On =
                {
                    ["homeExitDoor"] = async _ => { await ctx.Goto("apartment", "corridor3F"); return null; },
                    ["homeDoor"] = async _ => { await ctx.Goto("apartment", "homeEntry"); return null; },
                    ["aptNotice"] = Board, ["centerNotice"] = Board,
                },
            });
        }
    }
}
