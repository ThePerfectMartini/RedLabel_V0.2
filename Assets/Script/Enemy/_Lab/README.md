# _Lab — 조립형 행동 순환 적 AI (실험용)

기존 적 AI(`Enemy/ChasePlayerBrain.cs` + `Enemy/Core/EnemyEngagementDirector.cs`)를
**대체할 후보**를 기존 코드와 나란히 돌려보기 위한 임시 폴더다.

## 왜 나란히 돌리는가

새 아키텍처가 실제로 더 나은지 플레이해 보기 전에는 알 수 없다. 그래서 기존 것을
지우지 않고 둘 다 씬에 올려놓고 번갈아 켜서 같은 시나리오로 비교한다.

## 기존 코드를 한 줄도 건드리지 않는다

`IEnemyBrain.Think(EnemyController, float) → CharacterIntent`가 이미 필요한 이음매라서,
이 폴더 전체가 그 인터페이스 뒤에 들어간다. 따라서 아래 파일들은 **수정 대상이 아니다**:

- `Enemy/EnemyController.cs`
- `Enemy/IEnemyBrain.cs`
- `Common/Character/CharacterControllerBase.cs`

브레인이 필요로 하는 정보는 전부 이미 public이다 —
`Position` / `CanAct` / `IsAttacking` / `AttackRange` / `AttackRadius` /
`CurrentState` / `StateMachine.OnStateChanged`. 리플렉션이 필요 없다.

`Test/TestAutoAttackBrain.cs`가 같은 패턴의 선례다.

## 구조

```
Cycle/     스텝 인터페이스, 전이 테이블, 브레인, 조립 프리셋
Steps/     순환의 각 칸. 서로를 모르고, 다음 스텝의 이름도 모른다
Director/  조율자 — 슬롯 / 역할 / 공격 토큰
Data/      튜닝 ScriptableObject
Debug/     오버레이. CombatMetricsDisplay는 기존 AI 계측에도 쓴다
```

### 이름 충돌 주의

이 프로젝트엔 asmdef가 없어 전부 한 어셈블리다. 기존
`EnemyEngagementDirector` / `IEngagementMember` / `EngagementSlot`과 겹치지 않게
`EncounterDirector` / `IEncounterMember` / `EngagementOrder`로 이름을 나눴다.

## 씬 구성

`EnemyController.Awake`는 `GetComponent<IEnemyBrain>()`으로 **처음 찾은 하나**를 쓰고
비활성 컴포넌트도 찾아낸다. 한 오브젝트에 브레인 두 개를 붙이면 어느 쪽이 쓰일지
알 수 없으므로 **반드시 오브젝트를 분리**한다.

1. `Enemies_Old` — 기존 적들 (`ChasePlayerBrain` + `AlertStackDebugDisplay`)
2. `Enemies_New` — 복제본, **같은 좌표** (`BehaviorCycleBrain` + `BehaviorCycleDebugDisplay`)
3. 빈 오브젝트 하나에 `EncounterDirectorHost`
4. 아무 오브젝트에나 `CombatMetricsDisplay` 하나 (양쪽 공용)
5. 두 그룹 중 **하나만 활성화**

## 채택 / 폐기

**채택**: 이 폴더 내용을 `Enemy/`로 승격 →
`ChasePlayerBrain.cs`, `Core/EnemyEngagementDirector.cs`, `Core/IEngagementMember.cs`,
`Test/AlertStackDebugDisplay.cs` 삭제 → `Enemies_Old` 그룹 제거.

**폐기**: 이 폴더와 `Enemies_New` 그룹, `EncounterDirectorHost`만 삭제.
기존 코드가 무손상이므로 되돌릴 것이 없다.

**부분 채택**: 스텝 중 일부(가장 유력한 건 `TelegraphStep` = 예고)만 기존
`ChasePlayerBrain`으로 옮기는 것도 가능하다.
