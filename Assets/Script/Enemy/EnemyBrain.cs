using UnityEngine;

/// <summary>
/// 적 AI의 8단계 — 행동들을 <b>대기를 허브로</b> 엮고, 설 자리와 공격 권한은 조율자에게 물어본다.
///
/// 이 클래스가 하는 일은 넷이다.
/// 1. 행동이 판단에 쓸 정보(BehaviorContext)를 매 프레임 한 번 채운다
/// 2. 현재 행동에게 이번 프레임을 맡기고 결과를 받는다
/// 3. 행동이 끝나면 <b>예외 없이 대기로 보낸다</b>
/// 4. 그 대기가 끝나는 순간에만 조건표를 읽어 다음 행동을 정한다
///
/// [왜 모든 행동이 대기를 거치는가] 그래야 "다음은 어디서 정해지는가"의 답이 한 곳뿐이 된다.
/// 조건이 여기저기 흩어지면 같은 판단(거리)이 두 군데서 서로 다르게 자라기 시작한다.
/// 덤으로 헛스윙 뒤의 멈칫, 접근 직후의 예비 동작 같은 리듬이 규칙 하나에서 공짜로 나온다.
///
/// 실제 판단 내용은 전부 행동 쪽(Behaviors 폴더)에 있고, 튜닝 값도 각 행동이 자기 것을 갖는다.
/// 행동은 끝까지 다음 행동의 이름을 모른다 — 그 지식은 이 파일의 ChooseNext에만 있다.
///
/// [준비물] EnemyController와 같은 오브젝트에 붙일 것. 씬에 PlayerController가 하나 있어야 한다
/// (PlayerController.Instance로 자동 탐색, 인스펙터 연결 불필요).
/// </summary>
[RequireComponent(typeof(EnemyController))]
public class EnemyBrain : MonoBehaviour, IEnemyBrain
{
    [Header("판단 기준")]
    [KoreanLabel("전투 시작 거리")]
    [Tooltip("배회하던 적이 이 거리 안으로 들어온 플레이어를 상대하기 시작한다.")]
    public float combatRange = 12f;

    [KoreanLabel("전투 유지 여유")]
    [Tooltip("이미 싸우는 중일 때는 '전투 시작 거리 + 이 값'을 넘어야 전투를 그만둔다. " +
        "들어오는 거리와 나가는 거리를 다르게 두는 것이며, 경계선 위에서 전투와 배회가 번갈아 나오는 것을 막는다.")]
    public float combatExitMargin = 3f;

    [KoreanLabel("맞은 뒤 추격 여유")]
    [Tooltip("맞은 직후, 공격 판정 구를 이만큼 부풀린 범위 안이면 물러나지 않고 붙어서 반격한다 " +
        "(접근 -> 대기 -> 공격). 넉백으로 그 밖까지 밀려났을 때만 후퇴한다. " +
        "0이면 사거리를 조금만 벗어나도 곧바로 물러난다.")]
    public float pursueAfterHitMargin = 2f;

    [Header("행동")]
    [KoreanLabel("접근")]
    public ApproachBehavior approach = new ApproachBehavior();

    [KoreanLabel("공격")]
    public AttackBehavior attack = new AttackBehavior();

    [KoreanLabel("후퇴")]
    public RetreatBehavior retreat = new RetreatBehavior();

    [KoreanLabel("대기")]
    public WaitBehavior wait = new WaitBehavior();

    [KoreanLabel("공격 준비")]
    public StandbyBehavior standby = new StandbyBehavior();

    [KoreanLabel("배회")]
    public WanderBehavior wander = new WanderBehavior();

    // 어떤 행동도 이만큼 오래 붙들려 있으면 안 된다. 접근은 스스로 끝나는 시간 제한이 없어서
    // 벽에 막히면 영영 Running을 보고할 수 있다. 튜닝 값이 아니라 조용한 멈춤을 잡는 값이라
    // 인스펙터에 내지 않는다. 지금은 "정상인데 오래 걸리는" 행동이 없으므로 예외 목록도 없다.
    const float BehaviorTimeout = 10f;

    // 매 프레임 탐색하지 않도록 시작할 때 한 번만 찾는다 (Update 안의 Find는 이 프로젝트에서 금지).
    PlayerController target;

    IEnemyBehavior current;
    float behaviorTimer;

    /// <summary>
    /// 조율자가 없을 때 혼자 기억하는 좌/우. 조율자가 있으면 이 값은 쓰이지 않는다.
    /// 판단 규칙 자체는 EnemyDirector.PickSide 한 곳에만 있어서 두 경로가 갈라지지 않는다.
    /// </summary>
    int soloSide;

    /// <summary>
    /// 지금 공격 행동에 들어가 있는지. 조율자가 "이미 휘두르는 중인 적"에게 공격 권한을 계속 주기 위해
    /// 읽어간다. 중간에 권한을 뺏으면 진행 중인 공격이 붕 뜬다.
    /// </summary>
    public bool IsAttackCommitted => current == attack;

    /// <summary>
    /// 지금 물러나는 중인지. 조율자가 공격 권한을 놓을 시점으로 읽어간다 —
    /// 물러나는 순간이 곧 상대를 다른 적에게 넘겨주는 순간이다.
    /// </summary>
    public bool IsRetreating => current == retreat;

    /// <summary>
    /// 지금 플레이어를 상대하는 중인지. 조율자가 읽어간다 — 멀리서 배회하는 적이 공격 권한이나
    /// 대기 자리를 차지해버리면 정작 붙어 있는 적이 싸우지 못한다.
    /// </summary>
    public bool IsEngaged => inCombat;

    /// <summary>
    /// 지금 전투 중으로 보고 있는지. 이 값이 있어야 "들어오는 거리"와 "나가는 거리"를 다르게 쓸 수 있다.
    /// 없으면 경계선 위에 선 플레이어 하나로 전투와 배회가 번갈아 나온다.
    /// </summary>
    bool inCombat;

    /// <summary>얻어맞은 뒤 아직 조건표가 그 사실을 읽어가지 않았는지. 후퇴를 고르는 유일한 근거다.</summary>
    bool wasHit;


    void OnEnable()
    {
        // 조율자는 없어도 된다. 그 경우 이 적은 혼자 자리를 정한다.
        EnemyDirector director = EnemyDirector.Instance;
        if (director != null)
            director.Register(this);
    }

    void OnDisable()
    {
        EnemyDirector director = EnemyDirector.Instance;
        if (director != null)
            director.Unregister(this);
    }

    void Awake()
    {
        target = PlayerController.Instance;
        if (target == null)
            Debug.LogWarning($"{name}: 씬에 PlayerController가 없어 접근할 대상이 없습니다. 제자리에 서 있습니다.");

        // 이 둘이 뒤집히면 적이 "물러났다가 전투를 포기하는" 동작을 반복한다. 증상만 보면
        // 원인을 짐작하기 어려운 조합이라 시작할 때 잡아준다.
        float exitRange = combatRange + combatExitMargin;
        if (exitRange <= retreat.retreatDistance)
            Debug.LogWarning($"{name}: 전투 이탈 거리({exitRange})가 후퇴 거리({retreat.retreatDistance})보다 작거나 같습니다. " +
                             $"후퇴할 때마다 전투를 그만두고 배회로 빠집니다.", this);
    }

    public CharacterIntent Think(EnemyController owner, float deltaTime)
    {
        // 대상이 없으면(아직 없거나 파괴됨) 아무 의도도 내지 않는다. UnityEngine.Object라 == null로 검사한다.
        if (target == null)
            return CharacterIntent.None;

        // 얻어맞아 몸이 통제를 잃은 동안은 판단 자체를 멈추고 진행 중이던 행동을 버린다.
        //
        // IsAttacking을 같이 보는 이유: 자기 공격을 재생하는 중에도 CanAct는 거짓이다(phase가 Attack).
        // 그것까지 피격으로 치면 적이 자기 공격 때마다 스스로를 중단시킨다.
        //
        // 판단을 회복 이후로 미루는 것은 정확성 때문이기도 하다. 넉백 중에 후퇴를 시작하면 밀려난 거리가
        // "내가 물러난 거리"로 계산되어 후퇴가 시작하자마자 끝나고, 거리 판정도 계속 변하는 값을 보게 된다.
        if (!owner.CanAct && !owner.IsAttacking)
        {
            wasHit = true;
            current = null;
            return CharacterIntent.None;
        }

        // 매 프레임 갱신한다. 판단 시점에만 보면 배회 중인 적이 플레이어가 다가온 것을 몇 초씩 모른다.
        UpdateCombatState(owner);

        EnemyDirector director = EnemyDirector.Instance;

        BehaviorContext ctx = new BehaviorContext(
            owner,
            target.Position,
            deltaTime,
            CurrentOrder(owner, director),
            director == null || director.HasAttackPermission(this),
            inCombat);

        // 경직에서 막 회복했거나 게임이 막 시작된 경우. 경직 뒤에 대기를 한 번 더 거치지 않는 이유는
        // 경직이 이미 그 쉼표 역할을 했기 때문이다.
        if (current == null)
            Enter(ChooseNext(ctx), ctx);

        CharacterIntent intent = CharacterIntent.None;

        // 어느 행동이든 플레이어를 바라보는 것이 기본이라 여기서 한 번만 채운다. 다른 곳을 봐야 하는
        // 행동(배회)은 자기 Tick에서 덮어쓴다. x 성분만 쓰이므로 z는 채우지 않는다 (SetFacing 참고).
        intent.FacingDirection = new Vector3(target.Position.x - owner.Position.x, 0f, 0f);

        BehaviorResult result = current.Tick(ctx, ref intent);

        behaviorTimer += deltaTime;
        if (result == BehaviorResult.Running && !(current is IUnboundedBehavior) && behaviorTimer >= BehaviorTimeout)
        {
            Debug.LogWarning($"{name}: 한 행동({current.GetType().Name})이 {BehaviorTimeout}초 동안 끝나지 않아 대기로 되돌립니다. " +
                             $"막혀 있거나 끝나는 조건이 성립하지 않는 상태입니다.", this);
            result = BehaviorResult.Done;
        }

        if (result != BehaviorResult.Running)
        {
            // 허브 규칙. 그 외에는 무조건 대기로 돌아간다.
            //
            // 대기와 공격 준비는 예외다. 둘 다 이미 기다리는 상태라 끝난 뒤에 또 대기를 끼우면 의미 없이
            // 한 박자가 늘어난다. 특히 공격 준비는 "권한이 왔다"는 이유로 끝나므로, 여기서 대기를 거치면
            // 차례가 온 적이 멀뚱히 서 있다가 뒤늦게 달려드는 그림이 된다.
            bool wasWaiting = current == wait || current == standby;
            Enter(wasWaiting ? ChooseNext(ctx) : wait, ctx);
        }

        return intent;
    }

    /// <summary>
    /// 지금 플레이어를 상대할 거리인지 갱신한다. 싸우는 중이면 더 멀어져야 놓아준다 —
    /// 들어오는 거리와 나가는 거리가 같으면 경계선 위에 선 플레이어 하나로 전투와 배회가 번갈아 나온다.
    /// </summary>
    void UpdateCombatState(EnemyController owner)
    {
        float threshold = inCombat ? combatRange + combatExitMargin : combatRange;

        Vector3 offset = target.Position - owner.Position;
        offset.y = 0f;

        inCombat = offset.sqrMagnitude <= threshold * threshold;
    }

    /// <summary>
    /// 이번 프레임에 설 자리. 조율자가 있으면 그쪽 배정을 그대로 쓰고, 없으면 같은 규칙으로 혼자 정한다.
    /// 적이 한 마리일 때의 결과는 두 경로가 같다.
    /// </summary>
    EngagementOrder CurrentOrder(EnemyController owner, EnemyDirector director)
    {
        if (director != null)
            return director.GetOrder(this);

        soloSide = EnemyDirector.PickSide(soloSide, owner.Position.x, target.Position.x);
        // 조율자가 없으면 늘 자기가 공격자다. 기다릴 자리는 필요 없다.
        return new EngagementOrder(soloSide, StandbySlot.None);
    }

    void Enter(IEnemyBehavior next, in BehaviorContext ctx)
    {
        current = next;
        behaviorTimer = 0f;
        current.OnEnter(ctx);
    }

    /// <summary>
    /// 조건표. 다음 행동이 무엇인지 아는 <b>유일한 자리</b>이며, 행동 쪽에는 이 지식이 한 조각도 없다.
    ///
    /// 1. 전투 범위 밖이면 관심을 끊고 배회한다
    /// 2. 공격 권한이 있고 사거리 안이면 친다 — 맞은 직후라도 칠 수 있으면 반격이 먼저다
    /// 3. 방금 맞았고 <b>붙어봐야 소용없을 만큼</b> 밀려났으면 물러난다 ("맞아야 물러난다")
    /// 4. 공격 권한이 없으면 안전한 거리에서 공격을 준비하며 차례를 기다린다
    /// 5. 그 외에는 붙는다 — 맞았더라도 조금만 움직이면 칠 수 있는 거리면 후퇴 대신 접근이다.
    ///    너무 밀착한 경우도 여기서 처리된다. 접근의 목표 지점이 플레이어 옆 사거리 안쪽이라,
    ///    겹쳐 있으면 알아서 뒤로 물러나 자리를 잡는다
    /// </summary>
    IEnemyBehavior ChooseNext(in BehaviorContext ctx)
    {
        // 읽는 순간 소비한다. 안 그러면 한 번 맞은 것으로 계속 후퇴하게 된다.
        bool hit = wasHit;
        wasHit = false;

        // 조율자가 동시에 칠 수 있는 수를 제한한다.
        // 전투 범위 밖이면 관심을 끊는다. 미리 배치해 둔 적이 화면 밖에서부터 달려오지 않게 하는 것이 목적이다.
        if (!ctx.InCombat)
            return wander;

        if (ctx.CanAttack && ctx.IsTargetInHitRange)
            return attack;

        // 맞았더라도 판정 구를 조금 부풀린 범위 안이면 아직 승산이 있다고 보고 다시 붙는다.
        // 절대 거리를 따로 두지 않고 판정 구에서 파생시키므로, 공격 에셋의 사거리를 바꾸면 이 경계도 따라온다.
        if (hit && !ctx.IsTargetWithinHitRange(pursueAfterHitMargin))
            return retreat;

        // 권한이 없으면 파고들지 않는다. 안전한 거리에서 자리를 잡고 사거리를 재며 차례를 기다린다.
        if (!ctx.CanAttack)
            return standby;

        return approach;
    }
}
