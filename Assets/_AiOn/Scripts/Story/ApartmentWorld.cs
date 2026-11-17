using System;
using System.Collections.Generic;
using System.Linq;
using AiOn.Game;
using Newtonsoft.Json.Linq;

namespace AiOn.Story
{
    /// <summary>What using a point does on screen: a dialog (speaker, role, lines) and/or a toast.</summary>
    public sealed class WorldResponse
    {
        public string Speaker, Role;
        public List<string> Lines = new();
        public string Toast;
        public float ToastSeconds = 3.5f;

        public bool IsEmpty => Lines.Count == 0 && string.IsNullOrEmpty(Toast);
    }

    /// <summary>
    /// The things the player can use or look at in the apartment in any chapter (original story/world.js): the
    /// wallet with the ID card, and a short description for the rest. Chapters will override points later.
    /// Pure logic over the game state, so it can be tested without a scene.
    /// </summary>
    public static class ApartmentWorld
    {
        public sealed class Context
        {
            public GameRules Rules;
            public GameState G;
            public bool Power = true;
            public Func<string, string> Fact = _ => null;
        }

        static bool Has(GameState g, string id) => g.Items.TryGetValue(id, out var e) && e.Home + e.Bag > 0;

        // level point id -> lines (DESC in the original).
        static readonly Dictionary<string, Func<Context, string[]>> Desc = new()
        {
            ["homeSink"] = c => c.G.Day >= 1 ? new[] { "수도꼭지를 돌려도 물이 나오지 않는다. 단수다." } : new[] { "설거지가 조금 남아 있다. 물은 잘 나온다." },
            ["homeWindow"] = c => c.G.Knows("curtain")
                ? new[] { c.G.Knows("curtainSealed") ? "암막 커튼 가장자리를 테이프로 막아 두었다. 틈으로 새는 불빛이 없다." : "암막 커튼이 쳐져 있다. 가장자리 틈으로 빛이 조금 샌다." }
                : c.Power ? new[] { "건너편 동 창문마다 불이 켜져 있다. 평소와 같은 저녁 풍경." }
                : new[] { "건너편 동이 캄캄하다. 창문 몇 곳에서 손전등 불빛이 흔들린다." },
            ["homeFridge"] = c => c.Power ? new[] { "반찬 통 몇 개와 남은 고기. 냉장고는 잘 돌아간다." }
                : new[] { "냉장고가 조용하다. 문을 자주 열면 안의 냉기가 빨리 빠진다.", c.Fact("food_door") },
            ["homeGas"] = _ => new[] { "레인지 옆 가스 중간밸브. 손잡이가 배관과 나란하면 열림, 직각이면 잠김이다." },
            ["homeBreaker"] = _ => new[] { "현관 위 분전반. 누전차단기와 개폐기가 줄지어 있다." },
            ["homeDesk"] = c => c.Power ? new[] { "노트북과 스탠드. 회사 메일은 확인할 게 없다." } : new[] { "노트북 화면이 꺼져 있다. 배터리를 아끼려면 켜지 않는 게 낫다." },
            ["homeBed"] = _ => new[] { "아직 잘 시간은 아니다." },
            ["homeStock"] = _ => new[] { "베란다 수납장. 생수와 비상용품을 넣어 두는 곳이다." },
            ["homePlanner"] = _ => new[] { "이번 주 계획판. 관리비 25일까지, 분리수거 화·금." },
            ["homeRadio"] = c => Has(c.G, "radio") ? new[] { "거실 탁자 위 휴대용 라디오." } : new[] { "라디오가 없다. 휴대폰 FM 라디오 앱은 쓸 수 있다 (P)." },
            ["homeBag"] = _ => new[] { "현관 옆 배낭. 대피할 때 들고 나갈 것을 담아 두는 곳이다." },
            ["lobbyNotice"] = _ => new[] { "단지 게시판: 관리비 안내, 분리수거 요일, 승강기 정기 점검 일정." },
            ["managerWindow"] = _ => new[] { "관리사무소 창구. 박 소장이 전화를 받고 있다." },
            ["elevator"] = c => c.Power ? new[] { "엘리베이터. 정전되면 멈추니 비상시에는 계단을 쓴다." }
                : new[] { "엘리베이터가 멈춰 있다. 버튼에 불이 들어오지 않는다.", c.Fact("alert_stairs") },
            ["stairs1F"] = _ => new[] { "계단실. 층마다 비상조명등이 있다." },
            ["stairs3F"] = _ => new[] { "계단실. 4층 쪽은 물청소 중이라 막혀 있다." },
            ["extinguisher"] = _ => new[] { "복도 옥내소화전 옆 분말소화기. 안전핀 → 노즐 → 손잡이 → 쓸듯이." },
            ["seoyeonDoor"] = _ => new[] { "303호. 오서연 씨 집이다." },
            ["elderDoor"] = _ => new[] { "304호. 문 옆에 신발장이 있다." },
            ["homeDoor"] = _ => new[] { "302호, 우리 집." },
        };

        public static bool HasDescription(string pointId) => Desc.ContainsKey(pointId);

        /// <summary>What using <paramref name="pointId"/> (labelled <paramref name="label"/>) does now.</summary>
        public static WorldResponse Use(Context c, string pointId, string label)
        {
            if (pointId == "homeWallet") return Wallet(c);
            var r = new WorldResponse { Speaker = label, Role = "살펴보기" };
            if (Desc.TryGetValue(pointId, out var f))
                r.Lines.AddRange(f(c).Where(l => !string.IsNullOrEmpty(l)));
            return r;
        }

        /// <summary>
        /// The wallet on the living-room table: the ID card goes into the bag (it counts as checked: an ID needs no
        /// batteries), know.idTaken is set and 'id_taken' recorded. Taking it twice only says so.
        /// </summary>
        static WorldResponse Wallet(Context c)
        {
            var g = c.G;
            if (!g.Items.TryGetValue("id_card", out var e)) g.Items["id_card"] = e = new ItemEntry { Checked = true };
            if (e.Home + e.Bag > 0) return new WorldResponse { Toast = "신분증은 이미 챙겼다." };
            e.Bag = 1;
            g.SetKnow("idTaken", true);
            GameRules.RecordEvent(g, "id_taken", new JObject());
            return new WorldResponse { Toast = $"지갑과 신분증을 챙겼다. {c.Fact("docs_take") ?? ""}".TrimEnd(), ToastSeconds = 5f };
        }
    }
}
