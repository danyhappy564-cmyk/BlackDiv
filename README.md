# BlackDiv
Adds Black Division using MoreBotsAPI

---

<26/09/04 상세 변경점 — 포크 수정사항>

- **블랙디비전/웨지에 SAIN이 전혀 적용되지 않던 문제 수정** (`SainBrainLayerPatch`)

  증상: SAIN 설치·설정이 정상인데도 블디와 웨지만 계속 바닐라 AI로 싸움. BigBrain
  디버그 오버레이로 보면 진짜 PMC는 `Layer:SAIN : Combat Layer`인데, 블디는
  `Layer:Pmc`, `AdvAssaultTarget`, `AssaultHaveEnemy` 같은 바닐라 레이어만 뜸.
  맵과 무관하게(쇄빙선/랩 동일) 재현.

  원인은 **등록 문제가 아니라 우선순위 문제**였음. BigBrain 레지스트리를 덤프해보니
  SAIN 레이어는 블디 6종 역할 전부에 이미 정상 등록되어 있었고(MoreBotsAPI의
  `AddSAINLayers()`는 제 역할을 하고 있었음), 각 봇에 SAIN `BotComponent`도 붙어
  있었음. 문제는 이것:

  ```
  SAIN CombatSoloLayer    prio 20
  SAIN CombatSquadLayer   prio 22
  바닐라 Pmc / AdvAssaultTarget / AssaultHaveEnemy    ← 훨씬 위
  ```

  SAIN은 바닐라를 **이기도록** 만들어진 게 아니라 **제거해서 자리를 비우는** 구조라,
  그 제거가 없으면 20/22짜리 레이어는 평생 차례가 안 옴. MoreBotsAPI도 그 제거를
  요청하지만 `TarkovApplication.Init` 시점 1회뿐이라, 그 뒤에 도는 SAIN 자신의
  `BigBrainHandler.Init()`이 제외 목록을 다시 만들면서 덮어써 버림.

  수정: 라이드 시작(`GameWorld.OnGameStarted`) 시점에 SAIN 레이어 등록 + 바닐라
  전투 레이어 제외를 다시 적용. 부수효과를 없애려고 SAIN 래퍼 대신
  `BrainManager.RemoveLayers`를 직접 호출.

  적용 범위는 **브레인 `PMC` + 블디 6종 역할로만** 한정. 개발 중 브레인 목록에
  `ExUsec`를 같이 넣었다가 **진짜 로그(Rogue)들의 바닐라 레이어까지 벗겨져서** 로그들이
  `PatrolFollower`만 남아 서로 졸졸 따라다니며 한곳에 뭉쳐 멈추는 사고가 있었음
  (덤으로 `PersonActiveClass.CheckAlive` NRE가 라이드당 4000회 폭주). 로그는 브레인도
  역할도 이 패치가 건드리는 범위에 안 걸리게 되어 있으니 이 목록은 넓히지 말 것.

  검증(실전 라이드): `blackDivIb`/`bossWedge` 모두 `SAIN : Combat Layer`,
  `SAIN : Avoid Threat` 진입 확인. 쇄빙선 자체 레이어(`IceCrewRush`/`IceCrewHold`/
  `WedgeRooms`)도 그대로 번갈아 작동. `ExUsec` 브레인 제외 0건, SAIN NRE 0건.

- **빌드 경로 하드코딩 제거** (`Plugin`/`Prepatch`/`Server` csproj)

  `..\..\..\` 상대경로로 박혀 있어서 폴더 깊이가 다르면 게임 어셈블리를 못 찾던 문제.
  `SptRoot` MSBuild 속성으로 빼서 `-p:SptRoot=...` 또는 환경변수로 덮어쓸 수 있게 함
  (기본값 `E:\SPT 4.0.10`). `DrakiaXYZ-BigBrain`/`SAIN` 참조가 `..\..\plugins\...`로
  `BepInEx` 경로 한 단계를 빠뜨리고 있던 것도 같이 정정.

  참고: `Prepatch`의 `AssemblyName`은 `Plugin`과 동일하게 `BlackDiv`로 두어야 함.
  NuGet의 `Ambiguous project name` 오류를 피하려고 잠깐 다른 이름으로 바꿨더니,
  `Plugin`이 `Prepatch`를 `ProjectReference`(기본 `Private=true`)로 참조하는 탓에
  빌드 출력에 DLL이 하나 더 생겨서 실제 배포 파일 구성이 달라짐. 그 오류는 여기서
  말고 복원 쪽에서 해결할 것.
