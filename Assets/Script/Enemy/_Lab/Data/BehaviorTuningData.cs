using UnityEngine;

/// <summary>
/// 행동 순환 각 칸의 시간과 임계값. 개체 하나의 "성격"에 해당한다.
///
/// 기존 EnemyPersonalityData와 같은 규칙을 따른다 —
/// 이동 속도(MovementStatData), 공격 성능(AttackData), 체력(CharacterStatData) 같은
/// **절대값은 여기 넣지 않는다.** 같은 값이 두 곳에 생기면 어느 쪽이 이기는지 코드를 읽어야만
/// 알 수 있게 된다. 여기 들어올 수 있는 건 시간, 거리 임계값, 배수뿐이다.
///
/// (stopDistance 같은 거리는 "어디에 설지"를 정하는 판정 임계값이지 성능값이 아니다.
///  실제 사거리는 여전히 AttackData가 소유하고, 이 값은 그것에 맞춰 조절하는 것이다.)
///
/// [주의] 동시 공격 수와 토큰 간격은 여기가 아니라 EncounterDirectorHost에 있다.
/// 그건 개체의 성격이 아니라 무리 전체의 규칙이기 때문이다.
/// </summary>
[CreateAssetMenu(fileName = "BehaviorTuningData", menuName = "DoitMySelf/Lab/Behavior Tuning Data")]
public class BehaviorTuningData : ScriptableObject
{
    [Header("접근 — 어디에 설지")]

    [KoreanLabel("정지 거리(최전열)")]
    [Tooltip("대상의 x축 기준 앞 또는 뒤, 최전열 슬롯이 위치할 거리. 공격 사거리와 맞춰 조절한다.")]
    public float stopDistance = 1.5f;

    [KoreanLabel("대기열 간격")]
    [Tooltip("같은 쪽에서 순서를 기다리는 적들끼리의 간격. 최전열 뒤로 이 간격씩 더 떨어져 줄을 선다.")]
    public float queueSpacing = 1.2f;

    [KoreanLabel("도착 판정 허용 오차")]
    [Tooltip("슬롯과의 거리가 이 값 이하면 도착으로 본다. 목표 지점에서 진동하는 것을 막는 데드존.")]
    public float arrivalTolerance = 0.15f;

    [KoreanLabel("재접근 거리")]
    [Tooltip("견제 중에 슬롯에서 이만큼 멀어지면 접근부터 다시 시작한다. 도착 오차보다 넉넉하게 잡아야 " +
        "접근과 견제를 오가며 덜덜 떨지 않는다.")]
    public float reapproachDistance = 0.8f;

    [Header("반응 — 얼마나 늦게 알아차릴지")]

    [KoreanLabel("반응 주기(초)")]
    [Tooltip("이 주기마다 한 번씩만 대상 좌표를 새로 읽는다. 매 프레임 실시간으로 정확히 쫓아가면 " +
        "너무 기계적이고 어렵게 느껴져서 일부러 지연을 준다. 사거리 판정은 지연되지 않는다.")]
    public float reactionInterval = 0.2f;

    [KoreanLabel("깊이축(z) 정렬 오차")]
    [Tooltip("목표 지점의 z를 이 범위 안에서 어긋나게 잡는다. 0이면 대상의 깊이에 정확히 붙어서 " +
        "'위아래로 반 칸 비켜서 헛치게 만들기'라는 이 장르의 기본 회피가 성립하지 않는다.")]
    public float zAlignError = 0.35f;

    [KoreanLabel("z 오차 재추첨 주기(초)")]
    [Tooltip("z 오차를 다시 뽑는 주기. 짧으면 계속 흔들려 보이고, 길면 한 자리를 고집하는 것처럼 보인다.")]
    public float zOffsetRerollInterval = 1.5f;

    [Header("견제 — 빈틈을 재는 구간")]

    [KoreanLabel("견제 시간 최소(초)")]
    [Tooltip("슬롯에 선 뒤 예고로 넘어가기까지 최소한 재는 시간. 개체마다 이 값과 최대 사이에서 뽑으므로 " +
        "같은 성격의 적들이 한꺼번에 들어오지 않는다.")]
    public float poiseDurationMin = 0.6f;

    [KoreanLabel("견제 시간 최대(초)")]
    public float poiseDurationMax = 1.6f;

    [KoreanLabel("빈틈 즉시 반응")]
    [Tooltip("대상이 공격 후딜이나 착지 경직을 보이면 견제 시간을 기다리지 않고 곧바로 예고에 들어간다. " +
        "'헛치면 반격당한다'는 이 장르의 기본 문법을 만드는 값이다.")]
    public bool punishOpenings = true;

    [Header("예고 — 읽을 시간")]

    [KoreanLabel("예고 길이(초)")]
    [Tooltip("공격 직전에 아무것도 하지 않고 서 있는 시간. **난이도 조절은 데미지가 아니라 이 값으로 한다.** " +
        "쉬움 0.6 / 보통 0.45 / 어려움 0.3 정도. 등 뒤에서 접근하면 조율자가 배수를 곱한다.")]
    public float telegraphDuration = 0.45f;

    [KoreanLabel("예고 취소 거리")]
    [Tooltip("예고가 끝난 시점에 대상이 이보다 멀면 헛치지 않고 접근부터 다시 한다. " +
        "너무 작게 잡으면 절대 헛치지 않아서 기계적으로 보인다 — 어느 정도의 헛침은 자연스러움의 일부다.")]
    public float telegraphAbortDistance = 4f;

    [Header("공격")]

    [KoreanLabel("공격 시작 포기 시간(초)")]
    [Tooltip("공격을 내려 했는데 쿨타임 등으로 시작되지 않을 때 이만큼 기다렸다가 포기한다. " +
        "무한정 조르지 않게 하는 안전장치라 튜닝용 값이 아니다.")]
    public float attackStartTimeout = 0.6f;

    [Header("후퇴 · 숨고르기 — 플레이어의 반격 창")]

    [KoreanLabel("후퇴 거리")]
    [Tooltip("공격을 끝낸 뒤 대상 반대쪽으로 물러나는 거리. 0이면 물러나지 않는다. " +
        "붙었다 빠지는 리듬을 만드는 값이라, 크게 잡을수록 플레이어가 숨 돌릴 여유가 커진다.")]
    public float retreatDistance = 1.5f;

    [KoreanLabel("후퇴 제한 시간(초)")]
    [Tooltip("벽이나 다른 적에 막혀 거리가 안 벌어질 때 영영 후퇴만 하는 것을 막는다. 튜닝용 값이 아니다.")]
    public float retreatTimeout = 1.5f;

    [KoreanLabel("숨고르기 최소(초)")]
    [Tooltip("물러난 자리에서 쉬는 시간. 플레이어에게 주는 반격 창의 길이이기도 해서, " +
        "너무 줄이면 난이도보다 스트레스가 먼저 오른다.")]
    public float recoverDurationMin = 0.5f;

    [KoreanLabel("숨고르기 최대(초)")]
    public float recoverDurationMax = 1.2f;

    [Header("관망 — 상대가 쓰러졌을 때")]

    [KoreanLabel("기상 공간 확보 거리")]
    [Tooltip("대상이 쓰러져 있는 동안 최소한 이만큼 떨어져서 기다린다. " +
        "일어나자마자 다시 맞는 무한 콤보를 구조적으로 막는 값이다.")]
    public float observeClearance = 2f;

    [Header("안전장치")]

    [KoreanLabel("스텝 워치독(초)")]
    [Tooltip("한 스텝에 이보다 오래 머무르면 에러를 찍고 순환의 첫 칸으로 되돌린다. " +
        "조용히 멈춰 서서 원인을 못 찾는 상황을 막는 것이 목적이라 튜닝용 값이 아니다. 0이면 끈다.")]
    public float stepWatchdogSeconds = 8f;
}
