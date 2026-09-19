using System;
using UnityEngine;

/// <summary>이동 노드가 목표 지점을 어떻게 추적하는지. 설계서 5장의 "세 가지 목표 추적 방식".</summary>
public enum MoveTracking
{
    [InspectorName("실시간 (매 프레임 갱신)")]
    Realtime,
    [InspectorName("지연 실시간 (갱신 주기마다)")]
    Delayed,
    [InspectorName("스냅샷 (시작 시 1회 고정)")]
    Snapshot,
}

/// <summary>이동 노드가 목표 지점까지 그리는 경로 모양.</summary>
public enum MovePath
{
    [InspectorName("직선")]
    Straight,
    [InspectorName("ㄱ자 (z를 먼저 맞추고 꺾음)")]
    ZFirst,
    [InspectorName("원호 (가는 길에 플레이어가 있으면 비켜 돌기)")]
    SemiCircle,
}

/// <summary>'ㄱ자' 경로에서 z를 맞추던 이동이 x로 꺾이는 방식.</summary>
public enum ZTurnBlend
{
    [InspectorName("점진적 (경계 없이 서서히 꺾임)")]
    Gradual,
    [InspectorName("단계 전환 (기준 거리에서 꺾기 시작)")]
    Stepped,
}

/// <summary>
/// 이동 노드가 노리는 목표 지점의 종류. 목표를 "어디로 잡을지"는 이것이 정하고,
/// "어떻게 갈지"는 <see cref="MovePath"/>가, "언제 다시 볼지"는 <see cref="MoveTracking"/>가 정한다.
/// </summary>
public enum MoveGoal
{
    [InspectorName("공격 위치 (플레이어 옆, 가까운 쪽)")]
    AttackSide,
    [InspectorName("원 둘레의 한 점 (플레이어 주변, 랜덤)")]
    OrbitPoint,
    [InspectorName("한 발 물러남 (가까우면 뒤로, 멀면 플레이어 쪽)")]
    StepBack,
    [InspectorName("이탈 (플레이어 반대, 벽에 막히면 트인 쪽으로)")]
    Escape,
}

/// <summary>
/// 이동 노드 하나의 설정 묶음. 설계서 5장대로 이동 노드는 하나뿐이고, 공격 접근 / 자리 재정렬 /
/// 공격 후 느린 이동의 차이는 전부 이 값의 차이로만 표현된다.
/// </summary>
[Serializable]
public class MoveProfile
{
    [KoreanLabel("목표 지점")]
    public MoveGoal goal = MoveGoal.AttackSide;

    [KoreanLabel("이동 속도(/초)")]
    [Tooltip("Locomotion의 기본 이동 속도를 이 값으로 덮어쓴다(내부적으로 배율로 환산). 0이면 기본 속도 그대로.")]
    public float speed = 6f;

    [KoreanLabel("목표 추적 방식")]
    public MoveTracking tracking = MoveTracking.Delayed;

    [KoreanLabel("목표 갱신 주기(초)")]
    [Tooltip("'지연 실시간'일 때만 쓴다. 클수록 플레이어를 더 늦게 따라간다.")]
    public float trackingRefreshInterval = 0.15f;

    [KoreanLabel("경로 모양")]
    public MovePath path = MovePath.ZFirst;

    [KoreanLabel("ㄱ자 꺾임 방식")]
    [Tooltip("'점진적'은 경계 없이 x 비중이 z 거리에 따라 연속으로 커진다 — 직선에서 곡선으로 넘어가는 " +
             "전환점 자체가 없다. '단계 전환'은 기준 거리 밖에선 순수 z, 안으로 들어오면 대각선으로 " +
             "한 번에 바뀌는 예전 방식이다(롤백용).")]
    public ZTurnBlend zTurnBlend = ZTurnBlend.Gradual;

    [KoreanLabel("꺾임 기준 거리(z)")]
    [Tooltip("'ㄱ자' 경로 전용. z(깊이) 차이가 이 값일 때 진행 방향이 z와 x의 딱 중간(45도)이 된다. " +
             "크게 잡을수록 더 멀리서부터 완만하게 휘어 들어오고, 작게 잡을수록 z로 곧장 가다가 늦게 꺾는다.\n\n" +
             "'단계 전환' 방식에서는 이 값이 꺾기 시작하는 경계 그 자체다. " +
             "어느 쪽이든 선회 반지름보다 작게는 적용되지 않는다 — 그보다 급하게는 꺾을 수 없으므로.")]
    public float zPriorityThreshold = 1.5f;

    [KoreanLabel("선회 속도(도/초)")]
    [Tooltip("이동 방향을 1초에 최대 몇 도까지 꺾을 수 있는지. 이 제한이 있어야 꺾이는 지점이 " +
             "직각이 아니라 반지름을 가진 곡선이 된다. 곡선 반지름 ≈ 이동 속도 ÷ 선회 속도(라디안) " +
             "— 속도 6 · 선회 360도/초면 반지름 약 1. 0이면 제한 없음 = 한 프레임에 꺾임 = 직각.")]
    public float turnRateDegPerSecond = 360f;

    [KoreanLabel("원 반지름")]
    [Tooltip("'원 둘레의 한 점' 목표 전용. 플레이어를 중심으로 한 이 반지름의 원 위에서 목적지를 고른다. " +
             "'원호' 경로로 비켜 돌 때의 반지름이기도 하다.")]
    public float orbitRadius = 3f;

    [KoreanLabel("플레이어 회피 폭")]
    [Tooltip("'원호' 경로 전용. 목적지까지 직선으로 갈 때 플레이어와 이 거리보다 가깝게 스쳐 지나가게 되면, " +
             "뚫고 가지 않고 원호를 그려 비켜 간다. 길이 비어 있으면 그냥 직선으로 간다. 0이면 항상 직선.")]
    public float avoidClearance = 1.5f;

    [KoreanLabel("재정렬 최소 이동 각도(도)")]
    [Tooltip("'원 둘레의 한 점' 목표 전용. 지금 내가 있는 각도에서 최소한 이만큼은 떨어진 점을 고른다. " +
             "0이면 바로 옆도 뽑혀 재정렬이 시작하자마자 끝나버리고, 180이면 항상 정반대편으로만 간다.")]
    public float repositionMinSweep = 90f;

    [KoreanLabel("도착 허용 오차")]
    [Tooltip("목표 지점까지 이 거리 안에 들어오면 도착으로 본다. '공격 위치' 목표는 이 값 대신 트리거 범위로 판정한다.")]
    public float arrivalTolerance = 0.15f;

    [KoreanLabel("이동 거리 제한")]
    [Tooltip("0보다 크면 시작 지점에서 이만큼 이동한 순간 목표 도달 여부와 무관하게 끝난다(느린 이동의 종료 조건).")]
    public float stopAfterDistance = 0f;

    [KoreanLabel("최대 지속시간(초)")]
    [Tooltip("이 시간 안에 못 끝나면 실패로 끝내고 행동을 다시 고른다. 벽에 끼거나 목표가 계속 도망칠 때의 안전장치.")]
    public float timeout = 5f;
}

/// <summary>
/// 일반 근접 적 AI의 모든 수치. 로직에는 숫자를 하나도 박지 않고 전부 여기서 읽는다 —
/// 리치가 길거나 느린 변형 적은 이 에셋만 새로 만들어서 찍어낸다.
///
/// 초기값은 설계서 10장의 임시값이다(플레이어 몸통 폭 = 1 기준). 플레이하며 조정할 것.
/// </summary>
[CreateAssetMenu(fileName = "MeleeEnemyAIData", menuName = "DoitMySelf/Melee Enemy AI Data")]
public class MeleeEnemyAIData : ScriptableObject
{
    [Header("이동 — 공격 접근 (빠름 · 지연 실시간 · ㄱ자)")]
    [KoreanLabel("공격 접근")]
    public MoveProfile approach = new MoveProfile
    {
        goal = MoveGoal.AttackSide,
        speed = 6f,
        tracking = MoveTracking.Delayed,
        trackingRefreshInterval = 0.15f,
        path = MovePath.ZFirst,
        zPriorityThreshold = 1.5f,
        turnRateDegPerSecond = 360f,
        arrivalTolerance = 0.15f,
        stopAfterDistance = 0f,
        timeout = 5f,
    };

    [Header("이동 — 자리 재정렬 (보통 · 실시간 · 반원)")]
    [KoreanLabel("자리 재정렬")]
    public MoveProfile reposition = new MoveProfile
    {
        goal = MoveGoal.OrbitPoint,
        speed = 4f,
        tracking = MoveTracking.Realtime,
        path = MovePath.SemiCircle,
        orbitRadius = 3f,
        arrivalTolerance = 0.4f,
        stopAfterDistance = 0f,
        timeout = 4f,
    };

    [Header("이동 — 공격 후 느린 이동 (느림 · 스냅샷 · 직선)")]
    [KoreanLabel("공격 후 느린 이동")]
    public MoveProfile slowStep = new MoveProfile
    {
        goal = MoveGoal.StepBack,
        speed = 2f,
        tracking = MoveTracking.Snapshot,
        path = MovePath.Straight,
        arrivalTolerance = 0.15f,
        stopAfterDistance = 1.5f,
        timeout = 3f,
    };

    [Header("이동 — 피격 후 이탈 (매우 빠름 · 스냅샷 · 직선)")]
    [KoreanLabel("피격 후 이탈")]
    public MoveProfile escape = new MoveProfile
    {
        goal = MoveGoal.Escape,
        speed = 8.75f, // 설계서 10장: 3.5만큼을 0.4초에 = 8.75/초. 점프였을 때의 체공 시간에서 환산한 값
        tracking = MoveTracking.Snapshot,
        path = MovePath.Straight,
        turnRateDegPerSecond = 720f, // 맞자마자 곧장 빠져야 하므로 방향 전환을 거의 즉시
        arrivalTolerance = 0.15f,
        stopAfterDistance = 3.5f,
        timeout = 1.5f,
    };

    [KoreanLabel("느린 이동 전진 전환 거리")]
    [Tooltip("공격 후 느린 이동을 시작할 때 플레이어와의 거리가 이 값보다 멀면 물러나는 대신 플레이어 쪽으로 다가간다. " +
             "(설계서 8장의 '가까우면 뒤로, 이미 멀면 플레이어 쪽으로'를 가르는 기준. 10장 표에는 없던 값이라 임의로 잡았다.)")]
    public float slowStepForwardDistance = 3f;

    [Header("공격")]
    [KoreanLabel("근접 공격 데이터")]
    [Tooltip("이 적이 트리거 범위 안에서 쓰는 공격 하나. 판정·넉백·애니메이션은 전부 이 에셋이 갖고 있고 " +
             "AI는 '언제 낼지'만 정한다. 비워두면 근접 공격 노드가 바로 실패한다.")]
    public AttackData meleeAttack;

    [KoreanLabel("차지하는 슬롯 종류")]
    [Tooltip("이 적이 플레이어 옆에서 점유하는 공격권의 층. 리치가 다른 적끼리는 서로 자리를 막지 않는다. " +
             "일반 근접 적은 '짧은 근접'이다.")]
    public AttackReach slotReach = AttackReach.ShortMelee;

    [KoreanLabel("공격 시작 대기 한계(초)")]
    [Tooltip("공격을 요청했는데 이 시간 안에 시작되지 않으면(쿨타임이 길거나 계속 얻어맞는 중이면) " +
             "포기하고 행동을 다시 고른다. 정상 상황에서는 발동하지 않아야 하는 안전장치.")]
    public float attackStartTimeout = 1f;

    [Header("공격 범위")]
    [KoreanLabel("트리거 범위 중심 거리")]
    [Tooltip("공격을 시작할 위치. 적 자신에게서 바라보는 방향으로 이만큼 떨어진 곳이 트리거 범위의 중심이다. " +
             "히트 판정 범위(AttackData의 공격 사거리)와는 별개다.")]
    public float triggerCenterDistance = 1.5f;

    [KoreanLabel("트리거 범위 크기 (가로 x, 깊이 z)")]
    [Tooltip("플레이어가 이 범위 안에 들어와야 공격이 시작된다. 히트 범위보다 좁게 두면 " +
             "'공격은 시작했는데 플레이어가 빠져나가 빗나가는' 느낌이 난다.")]
    public Vector2 triggerRangeSize = new Vector2(0.6f, 0.4f);

    [KoreanLabel("점프대쉬 발동 최소 거리")]
    [Tooltip("플레이어와 이 거리 이상 떨어져 있을 때만 점프대쉬가 행동 후보에 들어간다.")]
    public float jumpDashMinDistance = 4f;

    [Header("랜덤 — 공격 후 대기")]
    [KoreanLabel("대기 시간 최소(초)")]
    public float waitDurationMin = 0.2f;

    [KoreanLabel("대기 시간 최대(초)")]
    public float waitDurationMax = 1.5f;

    [Header("랜덤 — 공격 후 2택 가중치")]
    [KoreanLabel("제자리 대기")]
    public float postAttackWaitWeight = 50f;

    [KoreanLabel("느린 이동")]
    public float postAttackSlowStepWeight = 50f;

    [Header("랜덤 — 행동 선택 가중치 (원거리: 점프 발동 거리 이상)")]
    [KoreanLabel("근접 공격")]
    public float farMeleeWeight = 50f;

    [KoreanLabel("점프대쉬")]
    public float farJumpDashWeight = 25f;

    [KoreanLabel("자리 재정렬")]
    public float farRepositionWeight = 25f;

    [Header("랜덤 — 행동 선택 가중치 (근거리)")]
    [KoreanLabel("근접 공격")]
    public float nearMeleeWeight = 65f;

    [KoreanLabel("자리 재정렬")]
    public float nearRepositionWeight = 35f;

    [Header("랜덤 — 행동 선택 가중치 (슬롯이 모두 찼을 때)")]
    [KoreanLabel("제자리 대기")]
    public float blockedWaitWeight = 35f;

    [KoreanLabel("느린 이동")]
    public float blockedSlowStepWeight = 30f;

    [KoreanLabel("자리 재정렬")]
    public float blockedRepositionWeight = 35f;
}
