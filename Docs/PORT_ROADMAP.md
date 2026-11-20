# 이식 로드맵 — safe-route(Three.js) → Unity 6.3 URP

원작: https://github.com/Sunny8197783/AiOn (`safe-route/dist`, 약 24,000줄). 기획 방향은 원작 저장소의 `게임_통합설명서.md`를 따른다.

## 원칙

- **분위기는 그대로**: 가상의 한국 동네 '연무동', 평상시 따뜻한 실내등 → 경보 뒤 낮은 채도와 어두운 거리, 황색·청록색 UI, 충격 연출에 기대지 않는 차분한 톤.
- **규칙은 원작과 같게, 증명은 테스트로**: 순수 게임 로직은 파일 단위로 C#에 옮기고, 원작 JS를 실제로 돌려 만든 정답 데이터(golden)와 비교한다. 안전 문구는 원작의 검증된 `facts`에서만 가져온다.
- **업그레이드는 그 위에**: 그래픽(URP 조명·후처리·재질), 시스템(가방 칸 미리보기 같은 UI 개선), 재미(조작감·피드백)는 원작 규칙을 깨지 않는 선에서 더한다.
- **레벨은 원작 모델을 그대로**: 원작 레벨 빌더가 만든 3D 모델을 GLB로 내보내 화면에 쓰고, 충돌·바닥·상호작용 지점은 원작 데이터로 보이지 않게 깐다.

## 원작 구성과 현재 상태 (2026-11-19 코드 대조)

| 원작 폴더 | 내용 | Unity 상태 |
|---|---|---|
| `game/` | 상태, 아이템, 가방, 훈련, 장부, 릴레이, 결말, 복기, 소문, 마을 지도 | 완료. `Scripts/Game` 23개 파일, 모듈마다 테스트와 원작 정답 데이터 비교(`GoldenRuns`, `ParityTests`) |
| `levels/` | 아파트, 마트, 편의점, 사무실, 마을, 대피소 | 완료. 6개 레벨, 진열 상품(재질별 정적 병합 메시, 품절 교체), 상태 변화(`LevelStateVariants`, `LevelPointStates`: 통제 구역·커튼·화재·재고) |
| `engine/` | 조작, 카메라, 상호작용, 사람·군중, 하늘·후처리, 효과 | 완료. 사람·군중(`People/`, `Levels/Crowd`), 불·연기(`StoryFx`) 포함 |
| `story/` | 프롤로그부터 결말까지, 컷신 | 완료. `StoryDirector`와 챕터 전부, 플레이스루·정답 테스트 |
| `ui/` | 대화, HUD, 가방, 휴대폰, 라디오, 수첩, 계획판, 지도, 소화기, 복기, 메뉴 | 완료. `UI/Screens`마다 Logic + Screen + 테스트 |
| `audio.js` `app.js` | 소리, 앱 시작·저장·메뉴 | 완료. `AudioLibrary`, `ProceduralAudio`, 무료 효과음(`ThirdParty/Audio`) |

## 단계

1. **기반 (완료)**
2. **세계 반입 (완료)**: 레벨 간 이동은 `UnityStoryHost.Enter`가 씬을 바꿔 처리한다.
3. **게임 로직 (완료)**
4. **진행과 화면 (완료)**
5. **소리 (완료)**
6. **업그레이드와 마감 (진행 중)**
   - [x] 무료 에셋(글꼴·재질·하늘·효과음), 조작감, 접근성 옵션(글자 크기, 감도, 소리 구분, 청각·저시력 모드, 움직임 줄이기)
   - [x] 그래픽 2차(2026-11-19, `Scripts/Rendering`): 차가운 기본 룩, 긴급 프로파일(공습·폭발·연기), 유도 조명, 밤 안개, 1인칭 그림자 캐스케이드, HDR 색보정, 아날로그 신호·마스크 화면 효과(URP 렌더러 피처), 실시간 반사 프로브
   - [x] 인게임 HUD 2차(2026-11-19): 슬레이트 패널 테마(`UI/HudTheme`), 바이탈 패널(체력·기력·연기 노출 + 갈증·피로·긴장), 반응하는 크로스헤어와 키캡 프롬프트, 대상 브래킷과 외곽선(`Rendering/OutlineHighlight`), 위험 펄스와 심장박동(`UI/DangerMath`), 숨찰 때 카메라 호흡
   - [x] 단말기 UI(2026-11-19, `UI/Screens/TerminalKit`·`TerminalScreens`): 생존 가이드(K, 상황별 카드, 문장은 facts 그대로), 판단 순간 슬로 모션과 블러(`UI/DecisionMoment`), 생존 학습 리포트(`Game/LearningReport`: 기록된 이벤트만으로 O/X 판정, 엔딩 화면의 '학습 리포트' 버튼). 화생방 카드는 facts에 검증된 출처가 없어 비어 있음: `facts.json`에 topic `cbrn` 문장을 넣으면 채워진다.
   - [x] 빌드 스크립트: `AiOn → Build → Windows Player`, 명령줄 `-executeMethod AiOn.EditorTools.BuildScript.BuildWindows`
   - [ ] 조명 굽기: 하지 않기로 함. 시간대와 정전이 실행 중에 바뀌어 구운 GI가 틀린 방을 보여 준다. 대신 카메라를 따라가는 실시간 반사 프로브(`LevelReflections`)를 쓴다.
   - [ ] 색각 이상 대응 옵션
   - [ ] 실제 플레이어 빌드 테스트와 성능 측정

## 그래픽 구조 (`Scripts/Rendering`)

- `AtmosphereDirector`: 레벨 프리셋 위에 볼륨 4개를 얹는다. 모두 `Resources/Look`의 프로파일(`AiOn → Graphics → Build Look Assets`로 다시 만듦).
  - `AiOn_BaseLook`(우선순위 5): 섀도우는 슬레이트 청록, 하이라이트는 약간 따뜻하게, 먼 거리 약한 피사계 심도. 평시 실내는 35%, 밤과 어두운 곳은 100%.
  - `AiOn_Emergency`(30): 붉은 색보정, 강한 비네팅, 필름 그레인, 색수차. `Blast(거리)`, `Siren(on)`, `SetEmergency(0..1)`로 제어하고 연기 탈출 중에는 자동으로 들어간다.
  - `AiOn_RadioSignal`(31): `AnalogSignal`(keijiro/KinoGlitch 방식의 스캔라인 지터, 수직 롤, 색 번짐, 잡음). 라디오 화면이 열려 있는 동안 켜지고, 재난문자가 오면 잠깐 화면이 찢어진다(`Glitch`).
  - `AiOn_MaskView`(32): `MaskView`. 젖은 수건(연기 탈출에서 수건을 적시면 켜짐)과 방독면(`Mask("respirator")`). 숨을 내쉴 때마다 김이 서린다.
- `ScreenFxFeature`: 위 두 효과를 후처리 뒤 한 번의 전체 화면 패스(Render Graph)로 그린다. 둘 다 꺼져 있으면 패스를 넣지 않는다.
- `GuidingLights`: 출구·계단(초록), 대피소(호박색), 물자(차가운 흰색), 소화기(빨강)에 은은한 빛과 후광. 어두울수록 강해지고, 플레이어가 바로 옆에 있으면 사라진다. 실제 광원은 가장 가까운 3개만.
- `LevelReflections`: 카메라를 따라가는 실시간 반사 프로브. 룩이 바뀌거나 6 m 움직이면 한 면씩 다시 그린다. 품질 '보통' 이상.
- 움직임 줄이기 설정을 켜면 지터·롤·흔들림과 붉은 맥동이 꺼진다.

## 도구

- `Tools/golden/`: 원작 JS를 돌려 테스트용 정답 데이터를 만드는 스크립트.
- `Tools/level-export/`: 원작 레벨을 GLB·JSON으로 내보내는 도구(사용법은 그 폴더의 README). 원작 레벨이 바뀌면 다시 내보내 `Assets/_AiOn/Art/Levels`와 `Assets/_AiOn/Data/Source`를 교체하고 `AiOn → Rebuild All Level Scenes`를 실행한다.
- Unity 메뉴 `AiOn/`: 아이템·레벨 데이터 가져오기, 레벨 씬 재빌드, 플레이어 배치.
