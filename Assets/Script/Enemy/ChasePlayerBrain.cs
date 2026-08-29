using UnityEngine;

/// <summary>
/// 적이 플레이어를 쫓아가 공격 가능한 위치에 도착하면 공격을 시전하는 기본 IEnemyBrain 구현.
///
/// [슬롯 시스템] 여러 마리가 동시에 쫓아오면 전부 같은 지점(플레이어 앞/뒤 stopDistance)으로 몰려서
/// 겹쳐버린다. 그래서 플레이어 기준 좌/우 두 "1순위 슬롯"(stopDistance 지점)만 실제로 공격할 수 있고,
/// 같은 쪽에 더 있는 적들은 1순위 슬롯 뒤로 queueSpacing씩 더 떨어진 자리에서 순서를 기다린다.
/// 어느 쪽에 설지와 순번은 EnemyEngagementDirector가 정해서 나눠준다. 고정 배정이 아니라 거리 기반이라
/// 서로 위치가 바뀌면 순위도 자연스럽게 바뀌고, 앞의 적이 죽으면 뒤의 적이 바로 앞자리로 당겨진다.
///
/// [경계 스택] 슬롯에 도착하면 공격한다는 규칙만으로는 적이 늘 똑같은 속도로 반응한다. 그래서 플레이어가
/// 근처에 얼마나 머물렀는지를 0~1의 "경계 스택"으로 따로 쌓고, 만충(1)이 되어야 사거리에 들어오는 순간
/// 곧바로 친다. 체류 시간이 곧 위험도가 된다.
///
/// 갱신은 매 프레임이 아니라 alertTickInterval마다 한 번이고, 그때마다 감소분을 먼저 뺀 뒤 거리만큼 더한다.
/// 멀면 순감소 / 가까우면 순증가가 되어 거리마다 평형점이 생기므로, 그냥 빠르게 지나쳐 가면 한두 번
/// 쌓인 것이 저절로 도로 빠진다.
///
/// [주의] "슬롯에 도착했으면 만충"이라는 특례를 두면 안 된다. 슬롯은 플레이어를 따라다니는 지점이라
/// 가만히 선 적 옆을 지나가기만 해도 그 거리를 반드시 통과하게 되고, 그 0.1초짜리 우연이 만충으로
/// 점프해버려서 체류 시간 모델이 통째로 무의미해진다. 실제로 그렇게 만들었다가 되돌린 자리다.
///
/// 공격(CombatCore.PerformHitScan)은 좌/우(FacingDir, x축)로만 나가고 z가 어긋나면 판정 반경 밖이라
/// 맞지 않으므로, 목표 지점 자체를 "플레이어 x ± (stopDistance + 대기 순번 * queueSpacing), 플레이어와
/// 같은 z"로 고정해서 이동시킨다.
///
/// [준비물] EnemyController와 같은 오브젝트에 붙일 것. 씬에 PlayerController가 하나 있어야 한다
/// (PlayerController.Instance로 자동 탐색, 인스펙터 연결 불필요).
/// 성격 프로필(EnemyPersonalityData)은 인스펙터에 직접 연결해야 하며, 없으면 아무 행동도 하지 않는다.
/// </summary>
[RequireComponent(typeof(EnemyController))]
public class ChasePlayerBrain : MonoBehaviour, IEnemyBrain, IEngagementMember
{
    [KoreanLabel("정지 거리(1순위 슬롯)")]
    [Tooltip("플레이어의 x축 기준 앞 또는 뒤, 1순위 슬롯이 위치할 거리. 공격 사거리와 맞춰 조절.")]
    public float stopDistance = 1.5f;

    [KoreanLabel("대기열 간격")]
    [Tooltip("같은 쪽에서 순서를 기다리는 적들끼리의 간격. 1순위 슬롯 뒤로 이 간격씩 더 떨어져서 줄을 선다.")]
    public float queueSpacing = 1.2f;

    [KoreanLabel("도착 판정 허용 오차")]
    [Tooltip("목표 지점과의 거리가 이 값 이하면 도착한 것으로 보고 이동을 멈춘다 (진동 방지용 데드존).")]
    public float arrivalTolerance = 0.1f;

    [KoreanLabel("성격 프로필")]
    [Tooltip("언제/얼마나 자주 행동할지를 담은 데이터. 같은 적 프리팹이라도 이것만 바꿔 끼우면 성향이 달라진다. " +
        "연결하지 않으면 이 적은 아무 행동도 하지 않는다.")]
    public EnemyPersonalityData personality;

    /// <summary>
    /// 지금 하고 있는 행동. 서로 배타적이라 bool 여러 개 대신 enum 하나로 둔다 —
    /// "후퇴 중이면서 동시에 복귀 중" 같은 무효 조합이 아예 표현되지 않게 하려는 것이고,
    /// CharacterControllerBase의 ActionPhase가 정확히 같은 이유로 만들어졌다.
    ///
    /// 몸의 상태(피격 경직 / 공중 / 공격 재생)는 여기 없다. 그건 EnemyController가 소유하고
    /// owner.CanAct / owner.IsAttacking으로 읽어온다.
    /// </summary>
    enum AiState
    {
        /// <summary>슬롯으로 이동하거나 자리에서 경계를 쌓으며 대기. 기본 상태.</summary>
        Chase,
        /// <summary>공격 직후 플레이어 반대쪽으로 물러나는 중.</summary>
        Retreat,
        /// <summary>물러난 자리에서 잠시 정지. 플레이어의 반격 창이 여기서 나온다.</summary>
        Recover,
    }

    // 후퇴가 영영 안 끝나는 것을 막는 안전장치. 벽이나 다른 적에 막혀 목표 거리를 못 벌 수 있다.
    // 튜닝하라고 있는 값이 아니라 갇힘만 막는 값이라 인스펙터에 내지 않는다.
    const float RetreatTimeout = 1.5f;

    AiState state = AiState.Chase;
    Vector3 retreatStartPos;
    float retreatTimer;

    bool wasAttacking;
    float postAttackTimer;

    // 플레이어를 얼마나 가까이/오래 노려왔는지. 0~1이고 1이면 만충 — 사거리에 들어오는 즉시 친다.
    float alertStack;
    float alertTickTimer;

    bool hasTrackedPosition;
    Vector3 trackedPlayerPos;
    float positionUpdateTimer;

    void Awake()
    {
        if (personality == null)
            Debug.LogWarning($"{name}: 성격 프로필(EnemyPersonalityData)이 연결되지 않아 아무 행동도 하지 않습니다.");
    }

    // ===== IEngagementMember: Director가 대형을 짜면서 읽어가는 정보 =====

    public int Id => GetInstanceID();
    public Vector3 Position => transform.position;

    void OnEnable() => EnemyEngagementDirector.Instance.Register(this);
    void OnDisable() => EnemyEngagementDirector.Instance.Unregister(this);

    public CharacterIntent Think(EnemyController owner, float deltaTime)
    {
        if (personality == null)
            return CharacterIntent.None;

        PlayerController player = PlayerController.Instance;
        if (player == null)
            return CharacterIntent.None;

        Vector3 enemyPos = owner.Position;

        // 플레이어 좌표를 매 프레임 실시간으로 읽지 않고, 성격 프로필의 반응 주기마다 한 번씩만 다시
        // 샘플링해서 그 사이엔 이 값을 그대로 목표로 쓴다. 방향 전환/이동 둘 다 이 값을 기준으로 하므로
        // 플레이어가 움직여도 다음 갱신 시점까지는 조금 늦게 반응하는 것처럼 보인다.
        positionUpdateTimer -= deltaTime;
        if (!hasTrackedPosition || positionUpdateTimer <= 0f)
        {
            trackedPlayerPos = player.transform.position;
            positionUpdateTimer = personality.reactionInterval;
            hasTrackedPosition = true;
        }
        Vector3 playerPos = trackedPlayerPos;

        // 대형 계산은 프레임당 한 번만 돌면 되고, 누가 먼저 물어보든 같은 답이 나온다.
        // 공격 후 쉬는 중이어도 대형은 계속 갱신되어야 하므로 아래 조기 반환보다 앞에 둔다.
        // 대형은 지연된 좌표가 아니라 실시간 좌표로 짠다 — 개체마다 다른 시점의 좌표를 쓰면
        // 같은 프레임인데도 서로 다른 대형을 상정하게 된다. 반응 지연은 이동 목표와 방향에만 남긴다.
        Vector3 livePlayerPos = player.transform.position;

        EnemyEngagementDirector director = EnemyEngagementDirector.Instance;
        director.EnsureTicked(Time.frameCount, livePlayerPos);

        // 이동 방향과 무관하게 항상 플레이어를 바라본다. 너무 가까워서 목표 지점까지 뒤로 물러나야 할
        // 때도 여전히 플레이어 쪽을 보고 있어야 하기 때문(EnemyController는 더 이상 이동 방향으로 자동
        // 갱신하지 않음). Stun 등 이동이 막힌 상태에서는 베이스의 SetFacing이 알아서 무시한다.
        // 아래 모든 반환 경로가 이 intent를 그대로 돌려주므로, 대기 중이든 공격 중이든 방향은 유지된다.
        CharacterIntent intent = CharacterIntent.None;
        intent.FacingDirection = playerPos - enemyPos;

        // 경계 스택은 후퇴/복귀 중에도 계속 갱신되어야 한다. 안 그러면 물러나 있는 동안
        // 플레이어가 따라붙어도 다시 노리기 시작하지 못한다.
        TickAlert(owner, livePlayerPos, deltaTime);

        // 얻어맞아 경직/다운되면 하던 후퇴/복귀는 접고 처음부터 다시 판단한다. 몸이 잠긴 동안에는
        // 어차피 이동 의도가 무시되므로, 그대로 두면 타이머만 헛돌다 엉뚱한 시점에 끝난다.
        if (!owner.CanAct && !owner.IsAttacking)
            state = AiState.Chase;

        // 공격(콤보 전체)이 막 끝난 순간을 잡아 후퇴로 넘긴다. 이게 없으면 마지막 타격 직후
        // 쿨타임이 이미 지나 있어서 텀 없이 재공격이 이어붙는 것처럼 보인다.
        if (wasAttacking && !owner.IsAttacking)
        {
            state = AiState.Retreat;
            retreatStartPos = enemyPos;
            retreatTimer = 0f;

            // 한 번 치고 나면 노리던 것이 통째로 풀려서 처음부터 다시 쌓는다.
            alertStack = 0f;
        }
        wasAttacking = owner.IsAttacking;

        if (state == AiState.Retreat)
            return TickRetreat(intent, enemyPos, playerPos, deltaTime);

        if (state == AiState.Recover)
        {
            postAttackTimer -= deltaTime;
            if (postAttackTimer <= 0f)
                state = AiState.Chase;

            return intent;
        }

        // ===== 여기부터 Chase =====
        // 어느 쪽에 설지와 대기 순번은 Director가 정한다. 여기서 각자 계산하면 대형에 대한 판단이
        // 개체마다 흩어져서 전체 규칙을 걸 자리가 없어진다.
        EngagementSlot slot = director.GetSlot(this);

        float assignedDistance = stopDistance + slot.Rank * queueSpacing;
        float targetX = playerPos.x + slot.Side * assignedDistance;

        Vector3 toTarget = new Vector3(targetX - enemyPos.x, 0f, playerPos.z - enemyPos.z);

        // 경계가 만충이면 슬롯 도착을 기다리지 않고 사거리에 들어오는 순간 곧바로 친다.
        // 사거리 판정만 지연되지 않은 실시간 좌표를 쓴다 — 이 경로는 "피하기 어려움"이 목적이라
        // 반응 지연을 그대로 두면 의도가 사라진다. 이동/방향은 여전히 지연된 좌표를 따른다.
        if (slot.Rank == 0 && alertStack >= 1f && IsInAttackRange(owner, livePlayerPos))
        {
            intent.WantsAttack = true;
            return intent;
        }

        // 자리에는 섰지만 아직 경계가 덜 찼거나 뒷줄이면 그냥 멈춰서 기다린다.
        if (toTarget.sqrMagnitude <= arrivalTolerance * arrivalTolerance)
            return intent;

        Vector3 dir = toTarget.normalized;
        intent.MoveInput = new Vector2(dir.x, dir.z);
        return intent;
    }
    /// <summary>
    /// 플레이어 반대쪽으로 물러난다. 바라보는 방향은 그대로 플레이어를 향하므로 뒷걸음질처럼 보인다
    /// (facing을 이동과 분리해둔 덕에 별도 처리가 필요 없다).
    ///
    /// 목표 거리를 벌었거나 제한 시간이 지나면 복귀로 넘어간다. 시간 안전장치가 반드시 필요한데,
    /// 벽이나 다른 적에 막히면 아무리 밀어도 거리가 안 벌어져서 영영 후퇴만 하게 되기 때문이다.
    /// </summary>
    CharacterIntent TickRetreat(CharacterIntent intent, Vector3 enemyPos, Vector3 playerPos, float deltaTime)
    {
        retreatTimer += deltaTime;

        float dx = enemyPos.x - retreatStartPos.x;
        float dz = enemyPos.z - retreatStartPos.z;
        float retreated = Mathf.Sqrt(dx * dx + dz * dz);

        if (retreated >= personality.retreatDistance || retreatTimer >= RetreatTimeout)
        {
            state = AiState.Recover;
            postAttackTimer = personality.postAttackPause;
            return intent;
        }

        // 좌우로만 물러난다. z까지 같이 빼면 대형이 흐트러져서 다음 접근 경로가 매번 달라진다.
        float awaySign = enemyPos.x >= playerPos.x ? 1f : -1f;
        intent.MoveInput = new Vector2(awaySign, 0f);
        return intent;
    }

    /// <summary>
    /// 경계 스택을 갱신한다. 매 프레임이 아니라 alertTickInterval마다 한 번만 갱신하는 것, 그리고 갱신 때마다
    /// 감소분을 먼저 빼는 것 두 가지가 핵심이다. 잠깐 스쳐 지나가면 갱신이 한두 번밖에 일어나지 않고,
    /// 그렇게 쌓인 것도 멀어지면 순감소로 돌아서서 도로 빠진다.
    /// </summary>
    void TickAlert(EnemyController owner, Vector3 livePlayerPos, float deltaTime)
    {
        alertTickTimer -= deltaTime;
        if (alertTickTimer > 0f) return;
        alertTickTimer = personality.alertTickInterval;

        // 얻어맞아 경직/다운/기상 중이면 노리던 것이 통째로 풀린다. 플레이어 입장에서는 선제 공격이
        // 곧 방어가 된다. 공격 재생 중(phase == Attack)도 CanAct는 false지만 그건 경직이 아니라
        // 자기가 하는 행동이므로 여기서 제외한다.
        if (!owner.CanAct && !owner.IsAttacking)
        {
            alertStack = 0f;
            return;
        }

        // 높이는 무시하고 바닥 평면 거리로만 잰다. 플레이어가 점프 중이라고 덜 위협적인 것은 아니다.
        float dx = livePlayerPos.x - owner.Position.x;
        float dz = livePlayerPos.z - owner.Position.z;
        float distance = Mathf.Sqrt(dx * dx + dz * dz);

        // 감지 범위 밖이면 쌓이는 것이 없고 감소만 남는다.
        float gain = 0f;
        if (distance <= personality.detectionRange)
        {
            float closeness = 1f - distance / personality.detectionRange;
            gain = Mathf.Lerp(personality.alertGainAtEdge, personality.alertGainAtContact, closeness);
        }

        // 감소를 먼저 빼고 거리만큼 더한다. 이 뺄셈이 "스쳐 지나가며 우연히 쌓인 것은 저절로 풀린다"를
        // 보장한다. 거리별 평형점(감소 == 증가가 되는 거리)보다 가까워야만 경계가 실제로 오른다.
        alertStack = Mathf.Clamp01(alertStack - personality.alertDecayPerTick + gain);
    }

    /// <summary>
    /// 지금 공격을 내면 실제로 맞는 위치인지. CombatCore는 "정면 사거리 지점에 반경 R짜리 구"로 판정하므로
    /// x는 사거리까지, z는 판정 반경까지 허용한다.
    /// </summary>
    bool IsInAttackRange(EnemyController owner, Vector3 livePlayerPos)
    {
        return Mathf.Abs(livePlayerPos.x - owner.Position.x) <= owner.AttackRange
            && Mathf.Abs(livePlayerPos.z - owner.Position.z) <= owner.AttackRadius;
    }
}
