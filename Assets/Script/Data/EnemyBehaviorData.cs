using UnityEngine;

/// <summary>
/// 근접 적 AI의 행동 튜닝 데이터. "언제 / 얼마나 오래 / 어디까지"만 담는다.
///
/// 기존 스탯 에셋과 축이 겹치지 않게 나눠 둔다:
/// - CharacterStatData  : 체력
/// - MovementStatData   : 물리 / 이동 속도
/// - AttackData         : 공격 한 방의 성능 (사거리 / 판정 반경 / 쿨다운)
/// - EnemyBehaviorData  : 언제 무엇을 할지
///
/// [규칙] 이동 속도, 공격 사거리, 공격 쿨다운을 여기 넣지 말 것. 전부 위 세 에셋이 소유한다.
/// 같은 값이 두 곳에 생기면 어느 쪽이 이기는지 코드를 읽어야만 알 수 있게 된다.
/// 여기 들어올 수 있는 건 시간, 거리, 판정 임계값뿐이다.
/// </summary>
[CreateAssetMenu(fileName = "EnemyBehaviorData", menuName = "DoitMySelf/Enemy Behavior Data")]
public class EnemyBehaviorData : ScriptableObject
{
    [Header("추적")]

    [KoreanLabel("정지 거리")]
    [Tooltip("플레이어의 x축 기준 앞 또는 뒤로 이만큼 떨어진 지점이 추적 목표다. " +
        "AttackData의 '공격 사거리 + 판정 반경'에 맞춰 잡을 것 — 이 자리에 서면 곧바로 사거리가 성립해야 한다.")]
    public float stopDistance = 1.8f;

    [KoreanLabel("추적 포기 시간(초)")]
    [Tooltip("이 시간 동안 추적했는데 한 번도 공격 사거리에 못 들어갔으면 배회로 넘어간다. " +
        "플레이어가 계속 도망칠 때 적이 영원히 뒤꽁무니만 쫓는 그림을 막는 값이다. 0이면 배회하지 않고 계속 추적한다.")]
    public float chaseGiveUpTime = 5f;

    [Header("후퇴 · 재정비")]

    [KoreanLabel("후퇴 거리")]
    [Tooltip("공격(콤보 전체)이 끝난 뒤 플레이어 반대쪽으로 물러나는 거리. 0이면 물러나지 않고 그 자리에서 바로 쉰다. " +
        "붙었다 빠지는 리듬을 만드는 값이라, 크게 잡을수록 플레이어가 숨 돌릴 여유가 커진다.")]
    public float retreatDistance = 1.5f;

    [KoreanLabel("재정비 시간(초)")]
    [Tooltip("후퇴가 끝난 뒤 제자리에 멈춰 있는 시간. 플레이어에게 주는 반격 창의 길이이기도 해서, " +
        "너무 줄이면 난이도보다 스트레스가 먼저 오른다.")]
    public float recoverTime = 0.5f;

    [Header("배회")]

    [KoreanLabel("배회 반경 X(좌우)")]
    [Tooltip("배회 목표 박스의 가로 절반 크기. 플레이어를 원점으로 (±X, ±Z) 네 모서리가 각 구역의 배회 목표 지점이 된다.")]
    public float wanderRadiusX = 5f;

    [KoreanLabel("배회 반경 Z(원근)")]
    [Tooltip("배회 목표 박스의 세로 절반 크기. 벨트스크롤이라 z(원근)로 움직일 수 있는 폭이 좁으므로 " +
        "보통 좌우 반경보다 훨씬 작게 잡는다.")]
    public float wanderRadiusZ = 1.5f;

    [KoreanLabel("추적 복귀 거리")]
    [Tooltip("배회 중 플레이어가 이 거리 안으로 들어오면 곧바로 추적으로 복귀한다. " +
        "[주의] 배회 목표까지의 거리(√(X²+Z²))보다 반드시 작아야 한다. 크게 잡으면 적이 배회 지점에 도착하는 " +
        "순간 복귀 조건을 만족해버려서 배회가 한 프레임 만에 끝난다.")]
    public float reAggroRange = 3.5f;

    [KoreanLabel("배회 인내 시간(초)")]
    [Tooltip("배회를 시작한 뒤 이 시간이 지나면 플레이어가 다가오지 않아도 추적을 재시도한다. " +
        "0이면 플레이어가 가까이 올 때까지 무한히 배회한다 — 계속 도망다니는 플레이어에게 적이 영영 안 붙어서 밋밋해진다.")]
    public float wanderPatience = 4f;

    [KoreanLabel("배회 흔들림 주기(초)")]
    [Tooltip("배회 목표 지점 주변에서 서성이는 주기. 이 간격마다 아래 흔들림 반경 안에서 목표를 다시 뽑는다.")]
    public float wanderStepInterval = 1.2f;

    [KoreanLabel("배회 흔들림 반경")]
    [Tooltip("배회 목표 지점 주변으로 흔들리는 반경. 0이면 모서리에 도착한 뒤 그대로 멈춰 서 있어서 " +
        "'배회'가 아니라 '정지'가 된다.")]
    public float wanderJitter = 0.8f;
}
