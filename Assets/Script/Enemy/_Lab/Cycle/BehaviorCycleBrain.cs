using UnityEngine;

/// <summary>
/// 조립된 행동 순환을 돌리는 브레인. **이 아키텍처에서 MonoBehaviour는 이것 하나뿐이다.**
/// 스텝도 조율자도 전부 순수 C#이다 (MovementCore/CombatCore와 같은 원칙).
///
/// 기존 ChasePlayerBrain을 대체하는 후보이며, 같은 IEnemyBrain을 구현하므로
/// EnemyController는 어느 쪽이 붙어 있는지 구분하지 않는다.
///
/// 이 클래스가 실제로 하는 일은 셋뿐이다:
/// 1. 조율자에게 이번 프레임의 대형·토큰을 계산시키고 자기 배정을 받아온다
/// 2. 반응 지연과 z 오차를 반영해 목표 지점을 만든다
/// 3. StepContext를 채워 순환을 한 프레임 돌린다
///
/// 판단은 전부 스텝에 있고, 조율은 전부 조율자에 있다. 여기엔 규칙이 없다.
///
/// [준비물]
/// - EnemyController와 같은 오브젝트에 붙일 것
/// - 행동 튜닝 프로필(BehaviorTuningData)을 인스펙터에 연결할 것. 없으면 아무 행동도 하지 않는다
/// - 씬에 PlayerController가 하나 있어야 한다 (자동 탐색, 인스펙터 연결 불필요)
/// - 조율자 손잡이를 바꾸려면 씬에 EncounterDirectorHost를 하나 놓을 것 (없어도 기본값으로 동작)
///
/// [주의] 같은 오브젝트에 ChasePlayerBrain과 함께 붙이지 말 것.
/// EnemyController.Awake는 GetComponent로 **처음 찾은 하나**를 쓰고 비활성 컴포넌트도 찾아낸다.
/// </summary>
[RequireComponent(typeof(EnemyController))]
public class BehaviorCycleBrain : MonoBehaviour, IEnemyBrain, IEncounterMember
{
    [KoreanLabel("행동 튜닝 프로필")]
    [Tooltip("순환 각 칸의 시간과 임계값. 연결하지 않으면 아무 행동도 하지 않는다.")]
    public BehaviorTuningData tuning;

    EnemyController controller;
    BehaviorCycle cycle;

    // 반응 지연 — 이 좌표만 늦게 갱신된다. 사거리 판정과 대형은 실시간 좌표를 쓴다.
    bool hasTrackedPosition;
    Vector3 trackedTargetPosition;
    float positionUpdateTimer;

    // 깊이축 정렬 오차 — 0이면 대상의 z에 정확히 붙어서 '비켜서 헛치게 만들기'가 성립하지 않는다.
    float zOffset;
    float zOffsetTimer;

    /// <summary>디버그 표시가 읽는다.</summary>
    public string CurrentStepName => cycle == null ? "(정지)" : cycle.CurrentStepName;

    /// <summary>디버그 표시가 읽는다. 예고 진행률을 그리려면 IProgressReporting으로 캐스팅한다.</summary>
    public IBehaviorStep CurrentStep => cycle == null ? null : cycle.CurrentStep;

    void Awake()
    {
        controller = GetComponent<EnemyController>();

        if (tuning == null)
        {
            Debug.LogWarning($"{name}: 행동 튜닝 프로필(BehaviorTuningData)이 연결되지 않아 아무 행동도 하지 않습니다.");
            return;
        }

        cycle = EnemyCyclePresets.Grunt();

        // 타이머 위상을 개체마다 흩어놓는다. 전부 0에서 시작하면 같이 스폰된 같은 성격의 적들이
        // 같은 프레임에 갱신되고 같은 순간에 준비를 마쳐서, 한꺼번에 들어오는 "합창"이 된다.
        // 개체 하나하나는 자연스러운데 무리가 기계적으로 보이는 전형적인 원인이다.
        positionUpdateTimer = Random.value * tuning.reactionInterval;
        zOffsetTimer = Random.value * tuning.zOffsetRerollInterval;
        zOffset = Random.Range(-tuning.zAlignError, tuning.zAlignError);
    }

    // ===== IEncounterMember: 조율자가 대형과 토큰을 정하면서 읽어가는 정보 =====

    public int Id => GetInstanceID();

    public Vector3 Position => transform.position;

    /// <summary>예고나 공격 중이면 토큰을 실제로 쓰고 있는 것이다. 스텝 이름이 아니라 타입으로 판단한다.</summary>
    public bool IsCommitted => cycle != null && cycle.CurrentStep is IAttackCommitStep;

    /// <summary>경직/다운 중이 아니어야 토큰을 받을 수 있다. 공격 재생 중은 경직이 아니다.</summary>
    public bool IsAvailable => controller != null && (controller.CanAct || controller.IsAttacking);

    void OnEnable()
    {
        // 프로필이 없어 아무것도 못 하는 적이 대형에 자리와 토큰을 차지하면 나머지가 못 친다.
        if (cycle == null) return;

        EncounterDirector.Instance.Register(this);
    }

    void OnDisable() => EncounterDirector.Instance.Unregister(this);

    public CharacterIntent Think(EnemyController owner, float deltaTime)
    {
        if (cycle == null || tuning == null)
            return CharacterIntent.None;

        PlayerController player = PlayerController.Instance;
        if (player == null)
            return CharacterIntent.None;

        Vector3 livePlayerPosition = player.transform.position;

        // 대형과 토큰은 프레임당 한 번만 계산하고, 실시간 좌표로 짠다. 개체마다 다른 시점의
        // 좌표를 쓰면 같은 프레임인데도 서로 다른 대형을 상정하게 된다.
        EncounterDirector director = EncounterDirector.Instance;
        director.EnsureTicked(
            Time.frameCount,
            deltaTime,
            new TargetSnapshot(livePlayerPosition, player.FacingDir.x >= 0f ? 1f : -1f));

        EngagementOrder order = director.GetOrder(this);

        // 반응 지연 — 이동과 방향이 쓸 좌표만 이 주기로 갱신한다.
        positionUpdateTimer -= deltaTime;
        if (!hasTrackedPosition || positionUpdateTimer <= 0f)
        {
            trackedTargetPosition = livePlayerPosition;
            positionUpdateTimer = tuning.reactionInterval;
            hasTrackedPosition = true;
        }

        zOffsetTimer -= deltaTime;
        if (zOffsetTimer <= 0f)
        {
            zOffset = Random.Range(-tuning.zAlignError, tuning.zAlignError);
            zOffsetTimer = tuning.zOffsetRerollInterval;
        }

        Vector3 selfPosition = owner.Position;

        // 공격 판정이 좌우(x축)로만 나가므로 목표 지점을 "대상 x ± 배정 거리, 대상과 비슷한 z"로 잡는다.
        // z에 오차를 섞는 것이 이 장르의 회피(깊이축으로 비켜서기)를 성립시킨다.
        float assignedDistance = tuning.stopDistance + order.Rank * tuning.queueSpacing;
        Vector3 slotPosition = new Vector3(
            trackedTargetPosition.x + order.Side * assignedDistance,
            selfPosition.y,
            trackedTargetPosition.z + zOffset);

        // 이동 방향과 무관하게 항상 대상을 바라본다. 뒷걸음질 칠 때도 마주 보고 있어야 하기 때문이다.
        // 스텝들은 이 intent를 이어받아 필요한 필드만 덧쓰므로, 어느 칸에 있든 방향은 유지된다.
        CharacterIntent intent = CharacterIntent.None;
        intent.FacingDirection = trackedTargetPosition - selfPosition;

        StepContext context = new StepContext(
            owner,
            tuning,
            selfPosition,
            trackedTargetPosition,
            livePlayerPosition,
            player.CurrentState,
            order,
            slotPosition,
            deltaTime);

        cycle.Tick(in context, ref intent);
        return intent;
    }
}
