using UnityEngine;

/// <summary>
/// 근접 적의 행동을 정하는 IEnemyBrain 구현. 상태 다섯 개짜리 상태 기계다.
///
///   추적 → 공격 → 후퇴 → 재정비 → 추적   (기본 순환)
///   추적 → 배회 → 추적                    (플레이어가 계속 도망칠 때의 곁가지)
///
/// [전제] 플레이어를 한 번 인지하면 계속 교전 상태다. 스폰 지점으로 돌아가는 디아그로는 없고,
/// 배회조차 플레이어 근처에서 일어난다. 그래서 배회는 "관심을 껐다"가 아니라 "지금은 붙지 않고
/// 거리를 둔다"에 가깝고, 플레이어가 다가오면 곧바로 다시 붙는다.
///
/// [몸과 두뇌] 이동 / 공격 판정 / 피격 처리는 전부 EnemyController(+ MovementCore, CombatCore)의 일이고,
/// 이 클래스는 CharacterIntent만 만들어 돌려준다. 여기서 transform을 직접 옮기거나 공격 판정을 하지 말 것.
/// 공격 쿨다운도 여기 없다 — AttackData가 소유하고 CombatCore가 강제한다.
///
/// [준비물]
/// - EnemyController와 같은 오브젝트에 붙일 것
/// - 행동 데이터(EnemyBehaviorData)를 인스펙터에 연결할 것. 없으면 아무 행동도 하지 않는다.
/// - 씬에 PlayerController가 하나 있어야 한다 (PlayerController.Instance로 자동 탐색, 인스펙터 연결 불필요)
/// </summary>
[RequireComponent(typeof(EnemyController))]
public class MeleeEnemyBrain : MonoBehaviour, IEnemyBrain
{
    [KoreanLabel("행동 데이터")]
    [Tooltip("언제 어떤 행동을 할지 담은 에셋. 같은 적 프리팹이라도 이것만 바꿔 끼우면 성향이 달라진다. " +
        "연결하지 않으면 이 적은 아무 행동도 하지 않는다.")]
    public EnemyBehaviorData behavior;

    /// <summary>
    /// 지금 하고 있는 행동. 서로 배타적이라 bool 여러 개 대신 enum 하나로 둔다 —
    /// "후퇴 중이면서 동시에 재정비 중" 같은 무효 조합이 아예 표현되지 않게 하려는 것이고,
    /// CharacterControllerBase의 ActionPhase가 정확히 같은 이유로 만들어졌다.
    ///
    /// 몸의 상태(피격 경직 / 공중 / 공격 재생)는 여기 없다. 그건 EnemyController가 소유하고
    /// owner.CanAct / owner.IsAttacking으로 읽어온다.
    /// </summary>
    public enum AiState
    {
        /// <summary>슬롯으로 이동해 사거리를 노린다. 기본 상태.</summary>
        Chase,
        /// <summary>공격을 시전하고 콤보가 끝날 때까지 기다린다.</summary>
        Attack,
        /// <summary>공격 직후 플레이어 반대쪽으로 물러나는 중.</summary>
        Retreat,
        /// <summary>물러난 자리에서 잠시 정지. 플레이어의 반격 창이 여기서 나온다.</summary>
        Recover,
        /// <summary>붙기를 포기하고 플레이어 근처 자기 구역에서 서성이는 중.</summary>
        Wander,
    }

    // 아래 둘은 튜닝하라고 있는 값이 아니라 각각 특정 고장 하나만 막는 값이라 인스펙터에 내지 않는다.

    // 목표 지점 도착 판정의 데드존. 없으면 목표를 지나쳤다 되돌아오기를 반복하며 제자리에서 진동한다.
    const float ArrivalTolerance = 0.1f;

    // 후퇴가 영영 안 끝나는 것을 막는 안전장치. 벽이나 다른 적에 막히면 아무리 밀어도 거리가 안 벌어진다.
    const float RetreatTimeout = 1.5f;

    AiState state = AiState.Chase;

    /// <summary>
    /// 지금 어떤 상태인지. 디버그 표시(AiStateDebugDisplay)가 읽는다.
    /// 다섯 상태 중 셋은 겉모습이 전부 "걷는 중"이라 화면만 보고는 구분되지 않아서 밖으로 열어둔다.
    /// 읽기 전용이다 — 상태를 바꾸는 것은 이 클래스 안의 Enter* 메서드만 한다.
    /// </summary>
    public AiState CurrentState => state;

    // 이번 추적을 시작한 뒤 흐른 시간. 사거리에 한 번이라도 닿으면 0으로 돌아간다.
    float chaseTimer;

    // 공격 의도를 이미 냈는지. 낸 의도가 실제 공격으로 이어지지 않았을 때(AttackData의 쿨다운이
    // 아직 안 끝난 경우 등) 공격 상태에 갇히지 않게 하는 표시다.
    bool attackRequested;

    Vector3 retreatStartPos;
    float retreatTimer;

    float recoverTimer;

    float wanderTimer;
    float wanderStepTimer;
    Vector3 wanderOffset;
    bool hasWanderOffset;

    // 공격(콤보 전체)이 끝나는 순간을 잡기 위한 직전 프레임의 값.
    bool wasAttacking;

    void Awake()
    {
        if (behavior == null)
            Debug.LogWarning($"{name}: 행동 데이터(EnemyBehaviorData)가 연결되지 않아 아무 행동도 하지 않습니다.");
    }

    public CharacterIntent Think(EnemyController owner, float deltaTime)
    {
        if (behavior == null)
            return CharacterIntent.None;

        PlayerController player = PlayerController.Instance;
        if (player == null)
            return CharacterIntent.None;

        Vector3 selfPos = owner.Position;
        Vector3 playerPos = player.transform.position;

        // 이동 방향과 무관하게 항상 플레이어를 바라본다. 후퇴 중에도 마찬가지라 뒷걸음질처럼 보인다
        // (EnemyController는 이동 방향으로 facing을 자동 갱신하지 않고 이 값을 쓴다).
        // 아래 모든 반환 경로가 이 intent를 그대로 물려받으므로 대기 중이든 공격 중이든 방향은 유지된다.
        CharacterIntent intent = CharacterIntent.None;
        intent.FacingDirection = playerPos - selfPos;

        // 얻어맞아 경직 / 다운 / 기상 중이면 하던 판단을 접고 처음부터 다시 시작한다. 몸이 잠긴 동안에는
        // 어차피 의도가 무시되므로, 그대로 두면 타이머만 헛돌다 엉뚱한 시점에 끝난다.
        // 공격 재생 중에도 CanAct는 false지만 그건 경직이 아니라 자기가 하는 행동이라 여기서 제외한다.
        if (!owner.CanAct && !owner.IsAttacking)
        {
            EnterChase();
            wasAttacking = false;
            return intent;
        }

        // 공격(콤보 전체)이 막 끝난 순간을 잡아 후퇴로 넘긴다. 이게 없으면 마지막 타격 직후
        // 텀 없이 재공격이 이어붙는 것처럼 보인다.
        if (wasAttacking && !owner.IsAttacking)
            EnterRetreat(selfPos);
        wasAttacking = owner.IsAttacking;

        switch (state)
        {
            case AiState.Attack:  return TickAttack(owner, intent, selfPos, playerPos);
            case AiState.Retreat: return TickRetreat(intent, selfPos, playerPos, deltaTime);
            case AiState.Recover: return TickRecover(owner, intent, selfPos, playerPos, deltaTime);
            case AiState.Wander:  return TickWander(owner, intent, selfPos, playerPos, deltaTime);
            default:              return TickChase(owner, intent, selfPos, playerPos, deltaTime);
        }
    }

    // ===== 상태별 처리 =====
    //
    // 상태가 바뀌는 프레임에는 새 상태의 Tick을 곧바로 이어서 호출한다. 다음 프레임까지 기다리면
    // 그 한 프레임 동안 아무 의도도 내지 않아 움직임이 뚝뚝 끊긴다.
    // 이어붙는 깊이는 배회 → 추적 → 공격까지 셋이 최대이며, 공격이 실패했을 때만은 TickChase가 아니라
    // MoveToChaseSlot으로 빠진다. 그러지 않으면 사거리 안에서 추적 ↔ 공격이 무한히 서로를 부른다.

    /// <summary>
    /// 슬롯으로 다가가며 사거리를 노린다. 사거리에 닿으면 그 프레임에 바로 공격으로 넘어가고,
    /// 아무리 쫓아도 못 닿은 채 시간이 다 가면 배회로 넘어간다.
    /// </summary>
    CharacterIntent TickChase(EnemyController owner, CharacterIntent intent, Vector3 selfPos, Vector3 playerPos, float deltaTime)
    {
        chaseTimer += deltaTime;

        if (EnemyAiMath.IsInAttackRange(selfPos, playerPos, owner.AttackRange, owner.AttackRadius))
        {
            EnterAttack();
            return TickAttack(owner, intent, selfPos, playerPos);
        }

        // 0은 "배회하지 않고 계속 추적"이라는 뜻이다. 이 검사가 없으면 0일 때 추적과 배회가
        // 같은 프레임에 서로를 무한히 호출한다.
        if (behavior.chaseGiveUpTime > 0f && chaseTimer >= behavior.chaseGiveUpTime)
        {
            EnterWander();
            return TickWander(owner, intent, selfPos, playerPos, deltaTime);
        }

        return MoveToChaseSlot(intent, selfPos, playerPos);
    }

    /// <summary>
    /// 공격을 한 번 요청하고, 콤보가 끝날 때까지 아무 의도도 내지 않고 기다린다.
    /// 콤보가 끝나는 순간은 Think 앞쪽의 wasAttacking 검사가 잡아서 후퇴로 넘긴다.
    /// </summary>
    CharacterIntent TickAttack(EnemyController owner, CharacterIntent intent, Vector3 selfPos, Vector3 playerPos)
    {
        if (owner.IsAttacking)
            return intent;

        // 지난 프레임에 낸 공격 의도가 시작되지 않았다(AttackData의 쿨다운이 아직 안 끝난 경우 등).
        // 여기서 멈춰 선 채로 재시도하면 뚝뚝 끊겨 보이므로 같은 프레임에 추적 이동으로 되돌린다.
        if (attackRequested)
        {
            EnterChase();
            return MoveToChaseSlot(intent, selfPos, playerPos);
        }

        intent.WantsAttack = true;
        attackRequested = true;
        return intent;
    }

    /// <summary>
    /// 플레이어 반대쪽으로 물러난다. 바라보는 방향은 그대로 플레이어를 향하므로 뒷걸음질처럼 보인다
    /// (facing을 이동과 분리해둔 덕에 별도 처리가 필요 없다).
    ///
    /// 목표 거리를 벌었거나 제한 시간이 지나면 재정비로 넘어간다. 시간 안전장치가 반드시 필요한데,
    /// 벽이나 다른 적에 막히면 아무리 밀어도 거리가 안 벌어져서 영영 후퇴만 하게 되기 때문이다.
    /// </summary>
    CharacterIntent TickRetreat(CharacterIntent intent, Vector3 selfPos, Vector3 playerPos, float deltaTime)
    {
        retreatTimer += deltaTime;

        if (EnemyAiMath.GroundDistance(selfPos, retreatStartPos) >= behavior.retreatDistance
            || retreatTimer >= RetreatTimeout)
        {
            EnterRecover();
            return intent;
        }

        // 좌우로만 물러난다. z까지 같이 빼면 다음 접근 경로가 매번 달라져서 붙었다 빠지는 리듬이 흐트러진다.
        float awaySign = selfPos.x >= playerPos.x ? 1f : -1f;
        intent.MoveInput = new Vector2(awaySign, 0f);
        return intent;
    }

    /// <summary>물러난 자리에서 제자리에 선다. 여기가 플레이어의 반격 창이다.</summary>
    CharacterIntent TickRecover(EnemyController owner, CharacterIntent intent, Vector3 selfPos, Vector3 playerPos, float deltaTime)
    {
        recoverTimer -= deltaTime;
        if (recoverTimer > 0f)
            return intent;

        EnterChase();
        return TickChase(owner, intent, selfPos, playerPos, deltaTime);
    }

    /// <summary>
    /// 플레이어 근처 자기 구역에서 서성인다. 플레이어가 다가오거나 인내 시간이 다하면 추적으로 돌아간다.
    /// </summary>
    CharacterIntent TickWander(EnemyController owner, CharacterIntent intent, Vector3 selfPos, Vector3 playerPos, float deltaTime)
    {
        wanderTimer += deltaTime;

        bool playerCameClose = EnemyAiMath.GroundDistance(selfPos, playerPos) <= behavior.reAggroRange;
        bool ranOutOfPatience = behavior.wanderPatience > 0f && wanderTimer >= behavior.wanderPatience;

        if (playerCameClose || ranOutOfPatience)
        {
            EnterChase();
            return TickChase(owner, intent, selfPos, playerPos, deltaTime);
        }

        // 모서리에 도착한 뒤 그대로 두면 배회가 아니라 정지가 되므로, 주기마다 목표를 조금씩 흔든다.
        // 절대 좌표가 아니라 모서리로부터의 오프셋으로 들고 있어야 아래 모서리 갱신과 같이 움직인다.
        wanderStepTimer -= deltaTime;
        if (!hasWanderOffset || wanderStepTimer <= 0f)
        {
            Vector2 jitter = Random.insideUnitCircle * behavior.wanderJitter;
            wanderOffset = new Vector3(jitter.x, 0f, jitter.y);
            wanderStepTimer = behavior.wanderStepInterval;
            hasWanderOffset = true;
        }

        // 목표 구역은 매 프레임 다시 고른다. 플레이어가 움직여 좌우나 앞뒤 부호가 뒤집히면
        // 목표도 옆 구역으로 따라 옮겨가고, 적은 그쪽으로 자연스럽게 흘러간다.
        Vector3 corner = EnemyAiMath.WanderTarget(selfPos, playerPos, behavior.wanderRadiusX, behavior.wanderRadiusZ);
        return MoveToward(intent, selfPos, corner + wanderOffset);
    }

    // ===== 상태 진입 =====
    //
    // 각 상태가 쓰는 타이머를 진입 시점에 한곳에서 초기화한다. 나가는 쪽에서 정리하면
    // 나가는 경로가 늘어날 때마다 빠뜨릴 자리가 같이 늘어난다.

    void EnterChase()
    {
        state = AiState.Chase;
        chaseTimer = 0f;
        attackRequested = false;
        hasWanderOffset = false;
    }

    void EnterAttack()
    {
        state = AiState.Attack;
        attackRequested = false;
    }

    void EnterRetreat(Vector3 selfPos)
    {
        state = AiState.Retreat;
        retreatStartPos = selfPos;
        retreatTimer = 0f;
        attackRequested = false;
    }

    void EnterRecover()
    {
        state = AiState.Recover;
        recoverTimer = behavior.recoverTime;
    }

    void EnterWander()
    {
        state = AiState.Wander;
        wanderTimer = 0f;
        wanderStepTimer = 0f;
        hasWanderOffset = false;
    }

    // ===== 이동 =====

    CharacterIntent MoveToChaseSlot(CharacterIntent intent, Vector3 selfPos, Vector3 playerPos)
    {
        Vector3 slot = EnemyAiMath.ChaseTarget(selfPos, playerPos, behavior.stopDistance);
        return MoveToward(intent, selfPos, slot);
    }

    /// <summary>목표 지점 쪽으로의 이동 의도를 채운다. 이미 도착했으면 아무것도 채우지 않아 제자리에 선다.</summary>
    static CharacterIntent MoveToward(CharacterIntent intent, Vector3 selfPos, Vector3 target)
    {
        Vector3 toTarget = new Vector3(target.x - selfPos.x, 0f, target.z - selfPos.z);
        if (toTarget.sqrMagnitude <= ArrivalTolerance * ArrivalTolerance)
            return intent;

        Vector3 dir = toTarget.normalized;
        intent.MoveInput = new Vector2(dir.x, dir.z);
        return intent;
    }
}
