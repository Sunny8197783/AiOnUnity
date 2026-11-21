// 쉬운 한국어 모드 (competition upgrade, accessibility): a setting that shows a short, plain-language version of the key
// interface and story text (tutorial, objectives, minigame instructions, lesson cards, quiz, the important dialogue and
// fact lines) for readers at about an upper-primary level. Nothing is rewritten in place: the story, the saves and the
// tests keep the original strings, and the plain wording is swapped in only where text is shown (StoryOverlay, UiKit.Label,
// a few screens). Facts keep their verified text too: their plain wordings live in facts-ext.json's "plain" map (checked
// against the verified sentence, numbers and phone numbers kept) and are looked up by the fact's text. Pure, no UnityEngine.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AiOn.Story;

namespace AiOn.Game
{
    public static class EasyKorean
    {
        /// <summary>The settings key (MenuLogic.SettingsForm, StoryDirector.ApplySettings).</summary>
        public const string SettingKey = "easyKorean";
        public const string Label = "쉬운 한국어";
        public const string Sub = "안내와 안전 문장을 쉬운 말로";

        /// <summary>The setting in force (the director sets it; the views read it).</summary>
        public static bool On { get; set; }

        /// <summary>
        /// Original line -> plain line, for the interface and the story. Keys are the exact strings the game shows (a test
        /// checks each still appears in the code), values keep every number, phone number and safety condition.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
        {
            // ---- tutorial: the prologue's first task (sorting what is known) and the controls
            ["각 신호를 확인된 사실 · 추정·해석 · 소문·미확인 가운데 하나로 나눠 보세요."] = "신호 하나하나를 '확인된 사실', '추정', '소문' 가운데 하나로 나눠 보세요.",
            ["정답보다 \"어떻게 알게 됐는가\"가 중요합니다."] = "맞히는 것보다 \"어떻게 알게 됐는지\"가 더 중요해요.",
            ["무엇이 확인된 것이고 무엇이 짐작일까?"] = "무엇이 확실하고, 무엇이 짐작일까?",
            ["우리 시스템에서 직접 센 숫자라 확인된 사실입니다. 다만 왜 늘었는지는 이 숫자만으로 알 수 없습니다."] = "회사 시스템이 직접 센 숫자라서 사실이에요. 하지만 왜 늘었는지는 이 숫자로는 알 수 없어요.",
            ["공식 공지가 왔다는 것은 사실입니다. 사유를 짐작해 덧붙이면 그때부터는 추정이 됩니다."] = "공식 공지가 온 것은 사실이에요. 이유를 짐작해서 붙이면, 그 부분은 추정이 돼요.",
            ["취소 증가(사실)에 이유를 붙인 해석입니다. 그럴 수도 있지만 확인된 것은 아닙니다."] = "취소가 늘었다는 사실에 이유를 붙인 생각이에요. 그럴 수도 있지만 확인되지 않았어요.",
            ["누가 언제 본 것인지 알 수 없는 말입니다. 확인 전까지는 소문으로 두는 것이 안전합니다."] = "누가 언제 봤는지 모르는 말이에요. 확인하기 전까지는 소문으로 두는 게 안전해요.",
            ["출처가 확인되지 않았습니다. 설령 사실이어도 이런 신호 하나가 전쟁을 확정하지는 않습니다."] = "어디서 나온 말인지 몰라요. 사실이라 해도, 이런 신호 하나로 전쟁이 난다고 할 수는 없어요.",
            ["이 조작법 다시 보기"] = "조작 방법 다시 보기",
            ["둘러보기 — 화면을 한 번 클릭하면 마우스만 움직여도 시점이 돕니다 (Esc로 커서 되찾기)"] = "둘러보기 — 화면을 한 번 누르면, 마우스만 움직여도 주변을 볼 수 있어요 (Esc: 마우스 화살표 다시 보기)",
            ["엎드리기/웅크리기 (폭발·파편 피해가 줄어듭니다. Space로 일어서기)"] = "엎드리기/웅크리기 (몸을 낮추면 덜 다쳐요. Space로 일어나기)",

            // ---- objectives (HUD)
            ["내 자리의 모니터를 확인합니다."] = "내 자리의 모니터를 봅니다.",
            ["거실 벽의 달력(계획판)에서 고릅니다. 집 안을 둘러볼 수도 있습니다."] = "거실 벽 달력(계획판)에서 할 일을 고릅니다. 집 안을 둘러봐도 됩니다.",
            ["집 안을 돌며 하나씩 해 둡니다. 다 마치면 달력(계획판)에서 끝냅니다."] = "집 안을 돌며 하나씩 해 둡니다. 다 하면 달력(계획판)에서 끝냅니다.",
            ["진열대에서 고르고 계산대에서 계산합니다. 필요한 만큼, 들고 올 수 있는 만큼."] = "진열대에서 물건을 고르고 계산대에서 돈을 냅니다. 필요한 만큼만, 들 수 있는 만큼만 삽니다.",
            ["세 사람을 만나 이야기합니다. 다 만나면 집으로 돌아옵니다."] = "이웃 세 사람을 만나 이야기합니다. 다 만나면 집으로 갑니다.",
            ["지하시설이나 유리창 없는 건물 안으로. 엘리베이터는 쓰지 않습니다."] = "땅 아래 시설이나 유리창 없는 건물 안으로 갑니다. 엘리베이터는 타지 않습니다.",
            ["접수대에서 명부를 쓰고 자리를 잡습니다."] = "접수대에서 명부(이름 적는 종이)를 쓰고, 앉을 자리를 정합니다.",
            ["명부 쓰기 (접수대)"] = "명부(이름 적는 종이) 쓰기 (접수대)",
            ["자리 잡기"] = "앉을 자리 정하기",
            ["자리를 잡습니다."] = "앉을 자리를 정합니다.",
            ["자리를 지키며 경보가 풀리기를 기다립니다."] = "자리에 앉아 경보가 끝나기를 기다립니다.",
            ["경보가 해제될 때까지 대피소 안에 머뭅니다."] = "경보가 끝날 때(경보해제)까지 대피소 안에 있습니다.",
            ["쓰러진 할아버지 곁을 지킵니다."] = "쓰러진 할아버지 곁에 있습니다.",
            ["몸을 낮추고(C) 계단으로 내려갑니다. 엘리베이터는 쓰지 않습니다."] = "몸을 낮추고(C) 계단으로 내려갑니다. 엘리베이터는 타지 않습니다.",
            ["몸을 낮추고 계단으로. 작은 불이면 소화기로, 커졌다면 알리고 대피합니다. 엘리베이터는 쓰지 않습니다."] = "몸을 낮추고 계단으로 갑니다. 작은 불이면 소화기로 끕니다. 불이 커졌으면 알리고 대피합니다. 엘리베이터는 타지 않습니다.",
            ["판단하세요. 작은 불이면 소화기로, 커졌다면 알리고 대피합니다."] = "잘 보고 정하세요. 작은 불이면 소화기로 끕니다. 불이 커졌으면 알리고 대피합니다.",
            ["작은 불이면 순서대로 끄고, 커지면 물러나 알립니다 (Q 두 번)."] = "작은 불이면 순서대로 끕니다. 불이 커지면 뒤로 물러나 알립니다 (Q 두 번).",
            ["작은 불이면 끄고, 커지면 물러나 알립니다."] = "작은 불이면 끕니다. 불이 커지면 뒤로 물러나 알립니다.",
            ["작은 불이면 끄고, 커지면 물러나 알립니다. Y: 다음 순서 힌트"] = "작은 불이면 끕니다. 불이 커지면 뒤로 물러나 알립니다. Y: 다음 순서 힌트",
            ["끄기 어려운 불입니다. 물러나 알리고 대피합니다."] = "끄기 어려운 불입니다. 뒤로 물러나 알리고 대피합니다.",
            ["행정복지센터 마당 급수대에서 줄을 섭니다."] = "행정복지센터(주민센터) 마당에서 물을 받으려고 줄을 섭니다.",
            ["냉장고를 정리하고, 물을 마시고, 오늘의 안내를 확인합니다. 다 했으면 달력에서 하루를 시작합니다."] = "냉장고를 정리하고, 물을 마시고, 오늘 안내를 봅니다. 다 하면 달력에서 하루를 시작합니다.",
            ["외출 자제 · 집에서"] = "밖에 나가지 않기 · 집에서",
            ["정민의 문자는 참고만 합니다. 현장 안내판을 보고, 서연에게 맞는 출입구로 갑니다."] = "정민의 문자는 참고만 합니다. 그곳의 안내판을 보고, 서연이 갈 수 있는 출입구로 갑니다.",
            ["임시 거처 첫날 밤"] = "임시 거처(잠시 지내는 곳) · 첫날 밤",
            ["임시 거처 이튿날 아침"] = "임시 거처(잠시 지내는 곳) · 둘째 날 아침",
            ["게시판을 읽고, 충전하고, 자리를 정리합니다. 다 하지 않아도 잠자리에 들 수 있습니다."] = "게시판을 읽고, 휴대폰을 충전하고, 자리를 정리합니다. 다 하지 않아도 잘 수 있습니다.",
            ["아침 7시 식수 배급이 시작됩니다."] = "아침 7시에 마실 물을 나눠 줍니다.",
            ["소등했습니다."] = "불을 껐습니다.",
            ["단지 게시판 또는 행정복지센터 게시판 읽기"] = "아파트 게시판이나 행정복지센터(주민센터) 게시판 읽기",

            // ---- story lines that teach (alerts, shelter)
            ["대피소: 연무 생활문화센터 지하주차장. 유도표지판의 화살표를 따라가세요."] = "대피소는 연무 생활문화센터 지하주차장입니다. 길에 있는 대피소 안내판의 화살표를 따라가세요.",
            ["확인한 사실: 이 출입구가 닫혀 있음. 이것만으로는 시설 전체의 운영 여부를 알 수 없다."] = "알게 된 것: 이 출입구는 닫혀 있다. 하지만 이것만으로 대피소 전체가 닫혔는지는 알 수 없다.",
            ["이름이랑 연락처, 복용하는 약 있으면 여기 적어 주세요."] = "이름과 연락처를 여기 적어 주세요. 먹는 약이 있으면 그것도요.",
            ["우리 집 안으로 연기나 불길이 들어오지는 않았다. 어떻게 할까?"] = "우리 집 안으로 연기나 불이 들어오지는 않았다. 어떻게 할까?",

            // ---- minigame instructions: CPR
            ["할아버지가 돗자리에 반듯이 누워 있다. 바닥은 평평하고 주변은 안전하다."] = "할아버지가 돗자리에 똑바로 누워 있다. 바닥은 평평하고, 주변은 안전하다.",
            ["대답이 없다. 가까이에 대피소 관리자, 노란 조끼의 자원봉사자, 파란 점퍼 입은 이웃이 있다."] = "대답이 없다. 가까이에 대피소 관리자, 노란 조끼를 입은 봉사자, 파란 점퍼를 입은 이웃이 있다.",
            ["119 상황요원: \"가슴 한가운데를 세게, 빠르게 누르세요. 제가 박자를 세어 드릴게요.\""] = "119 전화 속 안내원: \"가슴 한가운데를 세게, 빠르게 누르세요. 제가 박자를 세어 드릴게요.\"",
            ["Space · 클릭 · 화면 터치로 박자에 맞춰 누릅니다. 누르는 시간만큼 깊이 눌리고, 다음 박자 전에 손을 끝까지 떼야 가슴이 다시 올라옵니다."] = "Space, 클릭, 화면 터치로 박자에 맞춰 누릅니다. 오래 누를수록 깊이 눌립니다. 다음 박자 전에 손을 끝까지 떼야 가슴이 다시 올라옵니다.",
            ["원버튼: 박자만 맞추면 됩니다. 길게 누르면 교대를 요청합니다."] = "원버튼: 박자만 맞추면 됩니다. 길게 누르면 다른 사람에게 바꿔 달라고 합니다.",
            ["1분이 넘었습니다. 지치기 전에 교대를 요청할 수 있습니다."] = "1분이 넘었어요. 지치기 전에 교대를 요청할 수 있어요.",
            ["그러면 할아버지 곁이 빈다. 주변 사람 가운데 한 명을 정확히 지목해 부탁합니다."] = "그러면 할아버지 곁에 아무도 없게 된다. 주변 사람 한 명을 콕 집어 부탁합니다.",
            ["사람들이 서로 얼굴만 본다. 누가 할지 정해지지 않았다."] = "사람들이 서로 얼굴만 본다. 누가 할지 아무도 정하지 않았다.",
            ["헐떡이는 숨은 정상 호흡이 아니다. 바로 압박을 시작한다."] = "헐떡이는 숨은 제대로 쉬는 숨이 아니다. 바로 가슴을 누르기 시작한다.",
            ["이 게임은 실습을 대신하지 않습니다. 실제 손 위치와 깊이는 실습 교육에서 마네킹으로 익힙니다."] = "이 게임으로 실습을 대신할 수 없어요. 손 위치와 깊이는 실습 교육에서 마네킹으로 직접 익혀요.",

            // ---- minigame instructions: look first, extinguisher, bag, breathe
            ["타는 냄새가 난다. 바로 뛰어나가기 전에, 불이 어디서 났는지부터 살핀다. 살피는 데도 시간이 든다."] = "타는 냄새가 난다. 뛰어나가기 전에 불이 어디서 났는지부터 살펴본다. 살펴보는 데도 시간이 걸린다.",
            ["연기가 들어오고 있고, 복도와 계단은 아직 지나갈 수 있다. 이럴 때는 대피가 먼저다. 집 안에서 버티는 것은 나갈 수 없을 때의 방법이다."] = "연기가 들어오지만, 복도와 계단은 아직 지나갈 수 있다. 이럴 때는 먼저 대피한다. 집 안에서 버티는 것은 나갈 수 없을 때 하는 방법이다.",
            ["복도와 계단에 연기가 찼다. 이럴 때는 구조를 요청하는 것이 먼저다. 꼭 나가야 한다면 젖은 수건으로 코와 입을 막고 몸을 최대한 낮춘다."] = "복도와 계단에 연기가 가득하다. 이럴 때는 먼저 구조를 요청한다. 꼭 나가야 한다면 젖은 수건으로 코와 입을 막고 몸을 아주 낮춘다.",
            ["복도와 계단에 연기가 찼다. 문을 닫고 젖은 수건으로 틈을 막은 뒤, 119에 위치와 상황을 알리고 구조를 기다린다."] = "복도와 계단에 연기가 가득하다. 문을 닫고 젖은 수건으로 틈을 막는다. 119에 내가 있는 곳과 상황을 알리고 구조를 기다린다.",
            ["다른 집의 불이고 우리 집에는 연기가 들어오지 않는다. 무조건 나가기보다 집 안에서 상황을 살피는 것이 먼저다."] = "다른 집에서 난 불이고, 우리 집에는 연기가 들어오지 않는다. 무작정 나가기보다 집 안에서 상황을 먼저 살핀다.",
            ["건물마다 피난시설이 다릅니다. 우리 집 베란다에 경량칸막이·대피공간·하향식 피난구가 있는지, 그 앞이 비어 있는지 확인해 보세요."] = "건물마다 대피할 수 있는 시설이 달라요. 우리 집 베란다에 경량칸막이, 대피공간, 하향식 피난구가 있는지 보세요. 그 앞이 비어 있는지도 보세요.",
            ["소화기를 들고 있다. 불이 난 곳(아래)을 겨누고 손잡이를 쥐자."] = "소화기를 들고 있다. 불이 난 곳(아래쪽)을 겨누고 손잡이를 꽉 쥐자.",
            ["분말이 불길 위로 흩어진다. 불이 난 곳(아래)을 덮도록 겨누자."] = "가루가 불 위로 흩어진다. 불이 난 곳(아래쪽)을 덮도록 겨누자.",
            ["불길이 천장까지 닿았다. 소화기 한 대로는 어렵다. 물러나 알리자 (Q 두 번)."] = "불이 천장까지 닿았다. 소화기 하나로는 끌 수 없다. 뒤로 물러나 알리자 (Q 두 번).",
            ["열기와 연기에 밀려났다. 끄기 어려운 불 앞에 너무 오래 있었다."] = "뜨거운 열과 연기 때문에 뒤로 밀려났다. 끄기 어려운 불 앞에 너무 오래 있었다.",
            ["Space나 게임패드 A, 또는 화면의 원을 마우스·손가락으로 누르고 있는 동안 들이쉬고, 떼면 내쉽니다."] = "Space나 게임패드 A, 또는 화면의 원을 누르고 있는 동안 숨을 들이쉽니다. 떼면 숨을 내쉽니다.",

            // ---- lesson cards (교사용 모드, 장면 바로가기)
            ["공습경보, 복도 화재, 남을지 떠날지의 결정, 공식 대피까지 네 장면을 이어서 플레이합니다."] = "공습경보, 복도 화재, 남을지 떠날지 정하기, 공식 대피. 이 네 장면을 이어서 합니다.",
            ["장면이 끝날 때마다 공식 자료에서 가져온 한 줄을 함께 읽고 넘어갑니다."] = "장면이 끝날 때마다, 공식 자료에서 가져온 문장 하나를 함께 읽습니다.",
            ["준비 단계는 미리 해 둔 상태로 시작합니다. 이어하기 저장과 엔딩 기록은 바뀌지 않습니다."] = "준비는 미리 해 둔 상태로 시작합니다. 이어하기 저장과 엔딩 기록은 그대로입니다.",
            ["미리 준비한 상태에서 바로 시작합니다. 이어하기 저장은 바뀌지 않습니다."] = "미리 준비해 둔 장면에서 바로 시작합니다. 이어하기 저장은 그대로입니다.",
            ["정전된 밤, 사이렌이 울립니다. 엘리베이터 대신 계단으로 가까운 대피소까지."] = "전기가 끊긴 밤, 사이렌이 울립니다. 엘리베이터 말고 계단으로 내려가 가까운 대피소로 갑니다.",
            ["명부를 쓰고 자리를 잡습니다. 그사이 서연은 다른 길로 오고 있습니다."] = "명부(이름 적는 종이)를 쓰고 자리를 정합니다. 그사이 서연은 다른 길로 오고 있습니다.",
            ["쓰러진 사람 앞에서 119와 AED를 부탁하고, 지치기 전에 교대를 요청합니다."] = "쓰러진 사람을 보면 한 사람을 콕 집어 119와 AED를 부탁합니다. 지치기 전에 교대를 요청합니다.",
            ["어두운 복도에 연기가 찹니다. 먼저 살핀 뒤 복도로 나가, 소화기로 끌지 물러날지 판단합니다."] = "어두운 복도에 연기가 찹니다. 먼저 살펴본 뒤 복도로 나갑니다. 소화기로 끌지, 물러날지 정합니다.",
            ["불이 난 곳과 불길, 연기를 살피고 문손잡이를 손등으로 만져 본 뒤, 나갈지 집 안에서 기다릴지 정합니다."] = "불이 난 곳, 불길, 연기를 살펴봅니다. 손등으로 문손잡이를 만져 봅니다. 그다음 나갈지, 집 안에서 기다릴지 정합니다.",
            ["남은 물과 먹을 것, 들려오는 소문. 지금의 조건으로 고릅니다."] = "남은 물과 먹을 것, 들려오는 소문. 지금 상황을 보고 고릅니다.",
            ["대피 안내 문자를 받고 가방 하나를 꾸려 집결지로 갑니다."] = "대피하라는 문자를 받았습니다. 가방 하나를 챙겨 모이는 곳으로 갑니다.",

            // ---- quiz (questions; the options stay as written: their wording is what is checked)
            ["사이렌이 1분 동안 울리고 음성방송이 함께 나온다. 두 경보 가운데 어느 쪽일까요?"] = "사이렌이 1분 동안 울리고, 목소리 방송도 나온다. 어떤 경보일까요?",
            ["고층 아파트에서 공습경보가 울렸다. 아래층으로 어떻게 내려갈까요?"] = "높은 아파트에서 공습경보가 울렸다. 아래층으로 어떻게 내려갈까요?",
            ["다른 집에서 불이 났다. 우리 집에는 불길도 연기도 들어오지 않는다. 어떻게 할까요?"] = "다른 집에 불이 났다. 우리 집에는 불도 연기도 들어오지 않는다. 어떻게 할까요?",
            ["어른이 쓰러져 숨을 제대로 쉬지 않는다. 맞는 대응은?"] = "어른이 쓰러졌고, 숨을 제대로 쉬지 않는다. 어떻게 해야 할까요?",
            ["이유도 장소도 없는 경보 문자가 왔다. 단톡방에는 \"전쟁 났대\"라는 말이 돈다. 먼저 할 일은?"] = "왜, 어디로 가야 하는지 적혀 있지 않은 경보 문자가 왔다. 단톡방에는 \"전쟁 났대\"라는 말이 돈다. 먼저 무엇을 할까요?",

            // ---- interface
            ["메인 메뉴로 나가면 마지막 체크포인트 이후의 진행은 사라집니다."] = "메인 메뉴로 나가면, 마지막으로 저장된 곳 다음부터 한 것은 사라져요.",
        };

        // fact text -> plain (Facts.Plain), filled from the merged facts the game runs on
        static readonly Dictionary<string, string> s_facts = new();

        /// <summary>Lets <see cref="Plain"/> find the facts' plain wordings by their text (GameSession calls it).</summary>
        public static void UseFacts(Facts facts)
        {
            s_facts.Clear();
            if (facts == null) return;
            foreach (var f in facts.All)
            {
                var p = f?.Id == null ? null : facts.Plain(f.Id);
                if (p != null && !string.IsNullOrEmpty(f.Text)) s_facts[f.Text] = p;
            }
        }

        /// <summary>What to show: the plain wording when the mode is on and one exists, else the text itself.</summary>
        public static string Of(string text) => !On || string.IsNullOrEmpty(text) ? text : Plain(text) ?? text;

        /// <summary>A fact's text as shown: its plain wording in the mode, else the verified text.</summary>
        public static string FactText(Facts facts, string id) => facts == null ? null : On ? facts.Plain(id) ?? facts.Text(id) : facts.Text(id);

        /// <summary>
        /// The plain wording of a line (mode on or off), or null. Exact matches first (the map, then the facts); a block of
        /// lines is matched line by line; a "head — fact" line (a lesson summary) keeps its head and plains the rest.
        /// </summary>
        public static string Plain(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (Map.TryGetValue(text, out var p) || s_facts.TryGetValue(text, out p)) return p;
            if (text.IndexOf('\n') >= 0)
            {
                var parts = text.Split('\n');
                bool any = false;
                for (int i = 0; i < parts.Length; i++)
                {
                    var q = Plain(parts[i]);
                    if (q != null) { parts[i] = q; any = true; }
                }
                return any ? string.Join("\n", parts) : null;
            }
            int dash = text.IndexOf(" — ", StringComparison.Ordinal);
            if (dash > 0)
            {
                var rest = text.Substring(dash + 3);
                if (Map.TryGetValue(rest, out var r) || s_facts.TryGetValue(rest, out r)) return text.Substring(0, dash + 3) + r;
            }
            return null;
        }

        // ------------------------------------------------------------------ the story's typed options, as shown

        /// <summary>The talk as shown (a copy when anything changes; the same object when the mode is off).</summary>
        public static TalkOptions Talk(TalkOptions o)
        {
            if (!On || o == null) return o;
            var c = new TalkOptions { Speaker = o.Speaker, Role = o.Role, Portrait = o.Portrait, Tone = o.Tone, Progress = o.Progress };
            foreach (var l in o.Lines ?? new List<string>()) c.Lines.Add(Of(l));
            if (o.Choices != null)
                c.Choices = o.Choices.Select(x => x == null ? null : new TalkChoice(x.Id, Of(x.Label), x.Disabled, Of(x.Why)) { Taken = x.Taken }).ToList();
            return c;
        }

        /// <summary>The card as shown (a copy; the same object when the mode is off).</summary>
        public static CardOptions Card(CardOptions o)
        {
            if (!On || o == null) return o;
            var c = new CardOptions { Kind = o.Kind, Title = Of(o.Title), Meta = Of(o.Meta), Extra = o.Extra };
            foreach (var b in o.Body ?? new List<string>()) c.Body.Add(Of(b));
            if (o.Actions != null)
                c.Actions = o.Actions.Select(a => a == null ? null : new CardAction(a.Id, Of(a.Label), a.Primary) { Hint = Of(a.Hint) }).ToList();
            return c;
        }

        /// <summary>Objective steps as shown.</summary>
        public static IReadOnlyList<ObjectiveStep> Steps(IReadOnlyList<ObjectiveStep> steps)
        {
            if (!On || steps == null) return steps;
            var list = new List<ObjectiveStep>(steps.Count);
            foreach (var s in steps) list.Add(s == null ? null : new ObjectiveStep(Of(s.Text), s.Done) { Choice = s.Choice });
            return list;
        }

        // ------------------------------------------------------------------ the rules a plain line keeps (tested)

        /// <summary>
        /// Words a plain line avoids (hanja-heavy, official-notice words a young reader stumbles on), unless glossed right
        /// where it first appears: an official name the game uses elsewhere (명부 on the 접수대 sign) is kept and explained,
        /// "명부(이름 적는 종이)", so one thing never goes by two names.
        /// </summary>
        public static readonly string[] HardWords =
        {
            "즉시", "신속", "지체 없이", "숙지", "유의", "요망", "구비", "지참", "소지", "의거", "상기", "가급적", "인근", "부근",
            "개시", "소등", "명부", "자제", "미상", "착용", "탑승", "하차", "당해", "해당", "시행", "실시", "대기하", "우회", "통제 중",
        };

        /// <summary>The longest sentence a plain line may have (characters, spaces included).</summary>
        public const int MaxSentence = 64;

        static readonly Regex Numbers = new(@"\d+", RegexOptions.Compiled);

        /// <summary>The sentences of a line (split after . ! ?).</summary>
        public static IEnumerable<string> Sentences(string text) =>
            Regex.Split(text ?? "", @"(?<=[.!?])\s+").Where(s => s.Trim().Length > 0);

        /// <summary>
        /// What is wrong with a plain wording of an original line (empty = fine): it must differ, keep every number of the
        /// original (amounts, times, 112 / 119 / 1338...), avoid the hard words and keep each sentence short.
        /// </summary>
        public static List<string> Problems(string original, string plain)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(plain)) { list.Add("empty"); return list; }
            if (plain == original) list.Add("same as the original");
            var have = new HashSet<string>(Numbers.Matches(plain).Cast<Match>().Select(m => m.Value));
            foreach (Match m in Numbers.Matches(original ?? ""))
                if (!have.Contains(m.Value)) list.Add("number lost: " + m.Value);
            foreach (var w in HardWords)
            {
                int at = plain.IndexOf(w, StringComparison.Ordinal);
                bool glossed = at >= 0 && at + w.Length < plain.Length && plain[at + w.Length] == '(';
                if (at >= 0 && !glossed) list.Add("hard word: " + w);
            }
            foreach (var s in Sentences(plain))
                if (s.Length > MaxSentence) list.Add($"long sentence ({s.Length}): {s}");
            return list;
        }
    }
}
