using UnityEngine;

/// <summary>
/// 적 "두뇌"의 성향 데이터. 언제 / 얼마나 자주 행동할지만 담는다.
///
/// 기존 스탯 에셋과 축이 겹치지 않게 나눠 둔다:
/// - CharacterStatData        : 체력
/// - MovementStatData         : 물리 / 이동 속도
/// - AttackData               : 공격 한 방의 성능
/// - EnemyPersonalityData(여기): 언제 무엇을 할지
///
/// [규칙] 여기에 이동 속도나 공격 사거리 같은 "절대값"을 넣지 말 것. 그건 위 세 에셋이 소유한다.
/// 같은 값이 두 곳에 생기면 어느 쪽이 이기는지 코드를 읽어야만 알 수 있게 된다.
/// 여기 들어올 수 있는 건 배율과 판정 임계값, 그리고 시간/확률뿐이다.
/// </summary>
[CreateAssetMenu(fileName = "EnemyPersonalityData", menuName = "DoitMySelf/Enemy Personality Data")]
public class EnemyPersonalityData : ScriptableObject
{
    [KoreanLabel("후퇴 거리")]
    [Tooltip("공격을 끝낸 뒤 플레이어 반대쪽으로 물러나는 거리. 0이면 물러나지 않고 그 자리에서 바로 쉰다. " +
        "붙었다 빠지는 리듬을 만드는 값이라, 크게 잡을수록 플레이어가 숨 돌릴 여유가 커진다.")]
    public float retreatDistance = 1.5f;

    [KoreanLabel("공격 후 대기 시간(초)")]
    [Tooltip("공격(콤보 전체)이 끝난 직후 다음 행동(재공격 또는 추적 재개)까지 쉬는 시간. " +
        "플레이어에게 주는 반격 창의 길이이기도 해서, 너무 줄이면 난이도보다 스트레스가 먼저 오른다.")]
    public float postAttackPause = 0.4f;

    [KoreanLabel("감지 범위")]
    [Tooltip("이 거리 안에 플레이어가 있으면 경계 스택이 쌓이기 시작한다. 공격 사거리보다 훨씬 넓게 잡을 것 " +
        "— 멀리서부터 천천히 노리고 있다가, 가까워질수록 빨리 준비를 끝내는 것이 목적이다.")]
    public float detectionRange = 8f;

    [KoreanLabel("경계 갱신 주기(초)")]
    [Tooltip("경계 스택을 다시 계산하는 주기. 매 프레임이 아니라 이 간격으로만 갱신되므로, 플레이어가 " +
        "잠깐 스쳐 지나가면 한 번도 갱신되지 않아 스택이 거의 안 쌓인다.")]
    public float alertTickInterval = 1f;

    [KoreanLabel("감지 범위 끝 증가량")]
    [Tooltip("감지 범위의 가장 바깥에서 한 번 갱신될 때 쌓이는 경계 스택. 스택은 0~1이고 1이 되면 만충이다. " +
        "0.15면 가장 먼 거리에서 다 채우는 데 7번(기본 7초) 걸린다.")]
    public float alertGainAtEdge = 0.15f;

    [KoreanLabel("밀착 시 증가량")]
    [Tooltip("플레이어와 완전히 붙어 있을 때 한 번 갱신될 때 쌓이는 경계 스택. 이 값과 위 값 사이를 " +
        "거리에 따라 선형 보간한다. 가까울수록 빨리 차기 때문에 사거리 근처에선 잠깐만 머물러도 만충이 된다.")]
    public float alertGainAtContact = 0.5f;

    [KoreanLabel("틱당 경계 감소량")]
    [Tooltip("갱신할 때마다 먼저 빠지는 경계 스택. 거리에 따른 증가량에서 이 값을 뺀 것이 실제 변화량이라, " +
        "멀면 순감소 / 가까우면 순증가가 되어 거리마다 평형점이 생긴다. 스쳐 지나가며 우연히 쌓인 경계가 " +
        "저절로 풀리게 하는 것이 이 값의 역할이다.")]
    public float alertDecayPerTick = 0.1f;

    [KoreanLabel("반응 주기(초)")]
    [Tooltip("이 주기마다 한 번씩만 플레이어 좌표를 새로 읽어와 추적한다. 매 프레임 실시간으로 정확히 " +
        "쫓아가면 너무 기계적이고 어렵게 느껴져서, 일부러 약간의 반응 지연을 준다.")]
    public float reactionInterval = 0.2f;
}
