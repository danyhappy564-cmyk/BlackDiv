# BlackDiv (fork)

> **원작자 · 원본 레포**
> **TacticalToaster** — https://github.com/TacticalToaster/BlackDiv
>
> **라이선스: MIT** (Copyright (c) 2025 Matthew Ryan Christensen II)
>
> 이 레포는 위 원작의 **포크**입니다. 봇도 로드아웃도 퀘스트도 전부 원작자의 것이고,
> 여기서 한 건 SAIN이 블랙디비전에 안 붙던 문제를 고치고 빌드 경로를 이식 가능하게
> 만든 것뿐입니다.

MoreBotsAPI 기반으로 Black Division 팩션을 추가하는 모드입니다.

현재 기준: **upstream 1.3.1 / SPT 4.1**

---

## 이 포크가 원작과 다른 점

| 수정 | 내용 |
|---|---|
| `SainBrainLayerPatch` | 블디/웨지에 SAIN이 전혀 안 붙던 문제. 원작 1.3.x가 넣은 서버측 등록만으로는 안 고쳐집니다 (아래 26/09/07 항목) |
| `SptRoot` MSBuild 속성 | 세 csproj의 `..\..\..\` 상대경로 제거 |
| 서버 배포 경로 | `$(SptRoot)\SPT_Runtime\user\mods\` — 4.1에서 서버가 `SPT_Runtime\` 밑으로 옮겨간 것 반영 |

---

## 빌드

```
dotnet build BlackDiv.sln
```

경로는 각 csproj의 `SptRoot`에서 나옵니다. 기본값 `E:\SPT 4.1`, `-p:SptRoot=...`
또는 동명의 환경변수로 덮어쓸 수 있습니다.

| 프로젝트 | 배포 위치 |
|---|---|
| `Plugin` | `$(SptRoot)\BepInEx\plugins\BlackDiv\` |
| `Prepatch` | `$(SptRoot)\BepInEx\patchers\` |
| `Server` | `$(SptRoot)\SPT_Runtime\user\mods\BlackDivServer\` |

`Plugin`은 설치된 `MoreBotsPlugin`/`SAIN`/`DrakiaXYZ-BigBrain`을, `Server`는 설치된
`MoreBotsServer`를 참조합니다. 레포의 `Reference/MoreBotsServer/MoreBotsServer.dll`은
4.0 시절 것이라 최신 `MoreBotsServer.Interop` 타입이 없습니다 — 원작이 4.1에서 참조를
설치본 쪽으로 옮긴 이유입니다. 빌드에는 안 쓰이니 그대로 둡니다.

---

## 변경점

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
  `ExUsec`를 같이 넣었다가 **진짜 로그(Rogue)들이** `PatrolFollower`만 남아 서로 졸졸
  따라다니며 한곳에 뭉쳐 멈추는 사고가 있었음 (덤으로 `PersonActiveClass.CheckAlive`
  NRE가 라이드당 4000회 폭주).

  > **26/09/07 정정**: 당시 원인을 "`ExUsec` 브레인이 통째로 벗겨져서"로 적었는데,
  > 그 커밋(`3887051`)을 다시 읽어보니 **역할 인자를 똑같이 넘기고 있었습니다**. 역할
  > 스코프가 걸려 있었으면 진짜 로그(역할 `exUsec`)는 애초에 안 걸렸어야 합니다.
  > 그 시도는 브레인 목록 말고도 SAIN의 `ToggleVanillaLayersForBrainsAndRoles` 래퍼를
  > 거쳤다는 차이(`RestoreLayers`를 추가로 호출)가 있었고, 둘 중 뭐가 로그를 망가뜨린
  > 건지는 끝내 못 갈랐습니다. 확실한 건 **지금 형태(브레인 `PMC` 단독 +
  > `BrainManager.RemoveLayers` 직접 호출)가 실전 라이드에서 로그 정상 동작과 함께
  > 검증됐다**는 것뿐이라, 넓히지 않고 그대로 둡니다.

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

---

<26/09/07 상세 변경점>

- 원작 1.3.1 (SPT 4.1)을 **머지**로 받음. 충돌은 csproj 3개뿐이었고 `SainBrainLayerPatch`
  와 `Plugin.cs` 등록은 자동 머지됨. 충돌 처리:
  `Prepatch`의 `TargetFramework`는 원작 것(`net471` → `netstandard2.1`) 채택,
  `HintPath`와 PostBuild는 이쪽의 `SptRoot` 형태 유지(체크아웃 위치에 안 묶이는 쪽),
  `MoreBotsPlugin` 참조는 원작을 따라 레포 내 `Reference/` 대신 설치본을 보게 하되
  경로만 `$(SptRoot)`로 통일. `SptRoot` 기본값 `E:\SPT 4.0.10` → `E:\SPT 4.1`

- **서버 배포 경로가 4.0 레이아웃에 멈춰 있던 것 수정.** 이쪽 PostBuild가
  `$(SptRoot)\user\mods\`를 쓰고 있었는데, 4.1에서 서버가 `SPT_Runtime\` 밑으로
  옮겨갔습니다 (원작의 상대경로 `..\..\..\SPT_Runtime\user\mods\`가 근거).
  `$(SptRoot)\SPT_Runtime\user\mods\`로 정정

- **원작 1.3.x의 SAIN 작업이 이 패치를 대체하지 못하는 이유 확인.** 원작이
  `Server/SAIN/BlackDivSainRegistrations.cs`를 새로 넣어서 블디 6종 역할을
  MoreBotsAPI의 `SainInteropRegistration`에 등록합니다. 방향은 맞고 이 포크도 그대로
  두지만, 증상은 안 고쳐집니다:

  - **레이어 목록은 원래부터 문제가 아니었음.** MoreBotsAPI가 등록값 앞에 자기
    `commonVanillaLayersToRemove`(`Help`, `AdvAssaultTarget`, `Hit`, `Simple Target`,
    `Pmc`, `AssaultHaveEnemy`, `Assault Building`, `Enemy Building`, `PushAndSup`,
    `Pursuit`)를 붙이므로, 실제로 봇을 잡고 있던 `Pmc`/`AdvAssaultTarget`/
    `AssaultHaveEnemy`는 이미 요청에 들어 있습니다. 이 패치의
    `VanillaLayersToExclude` 16개는 그 합집합과 **정확히 일치**하도록 맞춰뒀습니다
    (스크립트로 대조 확인).
  - **문제는 타이밍.** MoreBotsAPI는 이 전부를 `TarkovApplication.Init` 포스트픽스
    한 번에 적용하는데, 그 뒤에 도는 SAIN 자신의 `BigBrainHandler` 초기화가 제외
    목록을 다시 만들면서 덮어써 버립니다. 라이드 시작(`GameWorld.OnGameStarted`)에
    다시 적용하는 게 이 패치의 존재 이유입니다.
  - MoreBotsAPI 2.1.1의 interop 자체가 반쯤 꺼져 있습니다 — 4.1 커밋 제목이
    "4.1 update (minus SAIN interop being broken AF)"이고, `CreateCustomBotTypes`
    안의 `BotTypeDefinitions.AddBotType`과 `AddBotTypeToSettings`가 둘 다 주석
    처리되어 있습니다

- **4.1 API 확인.** 이 패치가 부르는 두 함수 모두 살아 있습니다. SAIN 4.5.1의
  `BigBrainHandler.ToggleVanillaLayers`가 `BrainManager.RemoveLayers(layerNames,
  brainNames, roles)` — 이 패치가 쓰는 3인자 역할 스코프 오버로드 — 를 그대로
  호출하고, MoreBotsAPI 2.1.1이 `AddCustomLayersToBrainsAndRoles`를 같은 시그니처로
  부릅니다. 역할 ID 6개도 원작 등록값과 일치 확인

- 09/04 항목의 `ExUsec` 원인 설명을 정정 (해당 항목 안 인용 블록 참고)

- 검증: `Server` 프로젝트를 실제 SPT 4.1 패키지 + 최신 `MoreBotsServer`로 빌드해
  에러 0 확인. `Plugin`/`Prepatch`는 EFT 어셈블리가 필요해서 여기서는 구문 파싱
  (16개 파일, 에러 0)과 심볼 대조까지만 — 실기 빌드 필요
