using UnityEngine;

/// <summary>단독 테스트할 잎 노드. 노드를 하나 만들 때마다 여기에 한 줄씩 늘어난다.</summary>
public enum MeleeEnemyTestNode
{
    [InspectorName("① 공격 접근 (빠름 · 지연 실시간 · ㄱ자)")]
    Approach,
    [InspectorName("⑥ 자리 재정렬 (보통 · 실시간 · 반원)")]
    Reposition,
    [InspectorName("⑤ 공격 후 느린 이동 (느림 · 스냅샷 · 직선)")]
    SlowStep,
    [InspectorName("② 근접 공격 (정지 · 커밋형)")]
    MeleeAttack,
    [InspectorName("④ 공격 후 대기 (제자리 · 랜덤 시간)")]
    PostAttackWait,
    [InspectorName("④⑤ 공격 후 2택 (대기 / 느린 이동)")]
    PostAttackChoice,
    [InspectorName("⑦ 피격 경직 (맞아야 시작됨)")]
    HitStun,
    [InspectorName("⑧ 피격 후 이탈 (매우 빠름 · 스냅샷 · 벽 피함)")]
    Escape,
    [InspectorName("⑦⑧ 피격 분기 (경직 → 넘어졌으면 이탈)")]
    HitBranch,
    [InspectorName("⑨ 대기 줄 서기 (빈 슬롯 쪽 링으로 조금씩)")]
    Standby,
    [InspectorName("⑨ 대기 루프 (대기 / 줄 서기 / 자리 재배치 랜덤)")]
    StandbyLoop,
    [InspectorName("⑨ 대기 자리 재배치 (대기 링 위에서 크게 돌기)")]
    StandbyReposition,
}

/// <summary>
/// TEMP: 비헤이비어 트리 잎 노드를 <b>하나만</b> 골라 무한 반복시키는 테스트용 IIntentSource.
/// 트리가 완성되기 전에 노드를 따로따로 눈으로 확인하려고 만든 것이고, 조립이 끝나면 실제 AI 컴포넌트로 교체된다.
///
/// [쓰는 법] 적 루트(Actor가 있는 오브젝트)에서 EnemyBrain을 끄고 이걸 붙인다.
///          목표에 Player를, AI 수치에 MeleeEnemyAIData 에셋을 넣고 실행할 노드를 고른 뒤 플레이.
///          노드가 끝나면(Success/Failure) Console에 결과를 찍고 '재시작 간격' 뒤에 처음부터 다시 돈다.
///
/// [주의] Locomotion의 '8방향 스냅'을 꺼야 한다. 켜져 있으면 모든 경로가 45도 단위로 꺾여서
///        ㄱ자 곡선도 반원도 제대로 보이지 않는다. 켜져 있으면 Awake에서 경고한다.
/// [같이 쓰면 좋은 것] MeleeEnemyAIGizmo — 경로·목표·트리거 범위를 씬 뷰에 그려준다.
/// </summary>
public class MeleeEnemyNodeTester : MonoBehaviour, IIntentSource, IMeleeEnemyAIDebugInfo
{
    [KoreanLabel("목표 (플레이어)")]
    public Transform target;

    [KoreanLabel("AI 수치")]
    public MeleeEnemyAIData aiData;

    [KoreanLabel("실행할 노드")]
    public MeleeEnemyTestNode testNode = MeleeEnemyTestNode.Approach;

    [KoreanLabel("재시작 간격(초)")]
    [Tooltip("노드가 끝난 뒤 다시 처음부터 실행하기까지의 대기 시간. 0이면 쉬지 않고 반복한다.")]
    [Min(0f)]
    public float restartDelay = 1f;

    [KoreanLabel("결과를 Console에 찍기")]
    public bool logResult = true;

    MeleeEnemyContext context;
    BTNode node;
    bool wasAttacking; // Fighter가 실제로 Attack 상태에 들어갔다 나온 순간을 찍기 위한 직전 값
    Health health;
    MeleeEnemyTestNode builtFor;
    float restartCountdown;

    // ===== IMeleeEnemyAIDebugInfo (MeleeEnemyAIGizmo가 읽는다) =====

    public MeleeEnemyContext Context => context;
    public BTNode ActiveNode => node;

    void Awake()
    {
        if (target == null)
            Debug.LogWarning($"{name}: 목표가 비어 있어 노드가 아무것도 하지 않습니다.", this);
        if (aiData == null)
            Debug.LogWarning($"{name}: AI 수치 에셋이 비어 있어 노드가 아무것도 하지 않습니다.", this);
        else if (aiData.meleeAttack == null)
            Debug.LogWarning($"{name}: AI 수치 에셋의 '근접 공격 데이터'가 비어 있습니다. 근접 공격 노드는 바로 실패합니다.", aiData);

        Locomotion locomotion = GetComponent<Locomotion>();
        if (locomotion != null && locomotion.use8DirectionSnap)
            Debug.LogWarning($"{name}: Locomotion의 '8방향 스냅'이 켜져 있어 이동 경로가 45도 단위로 꺾입니다. AI가 쓰는 캐릭터는 꺼 주세요.", this);

        context = new MeleeEnemyContext(gameObject, target, aiData);

        health = GetComponent<Health>();
        if (health != null)
            health.OnHitTaken += OnHitTaken;
    }

    void OnDestroy()
    {
        if (health != null)
            health.OnHitTaken -= OnHitTaken;
    }

    void OnHitTaken(HitData hit)
    {
        context.NotifyHit();
        if (logResult)
            Debug.Log($"{name}: 피격 (데미지 {hit.Damage})", this);
    }

    // 실제 AI와 같이 명부에 올린다 — 테스터로 여러 마리를 돌릴 때도 서로의 자리를 피하게.
    void OnEnable() => EnemyCrowd.Register(transform);

    void OnDisable() => EnemyCrowd.Unregister(transform);

    public CharacterIntent GetIntent(float deltaTime)
    {
        if (context == null || aiData == null || target == null)
            return CharacterIntent.None;

        context.Target = target;
        context.BeginFrame();

        // 공격이 실제로 나갔는지를 노드 바깥에서 직접 확인한다 — 노드가 Success를 줬는데도
        // 아무 일이 없어 보이면, 애초에 Attack 상태에 들어가긴 했는지가 제일 먼저 궁금해지는 정보다.
        bool attacking = context.Fighter != null && context.Fighter.IsAttacking;
        if (logResult && attacking != wasAttacking)
            Debug.Log($"{name}: Fighter 공격 {(attacking ? "시작" : "종료")}", this);
        wasAttacking = attacking;

        if (node == null || builtFor != testNode)
        {
            // 인스펙터에서 노드를 바꾸면 즉시 갈아탄다. 돌던 노드는 중간 상태를 남기지 않게 끊어준다.
            node?.Abort(context);
            node = BuildNode(testNode);
            builtFor = testNode;
            restartCountdown = 0f;
        }

        if (restartCountdown > 0f)
        {
            restartCountdown -= deltaTime;
            return context.Intent; // 제자리 정지 + 플레이어 바라보기만
        }

        // ⑦ 피격 경직은 맞았을 때만 들어가는 분기라, 평소엔 아예 돌리지 않는다.
        // 그냥 돌리면 맞지도 않았는데 매 프레임 Success가 찍혀서 아무것도 확인할 수 없다.
        if (StartsWithHitStun(testNode) && !node.IsRunning && !context.HitPending)
            return context.Intent;

        BTStatus status = node.Tick(context, deltaTime);

        if (status != BTStatus.Running)
        {
            if (logResult)
                Debug.Log($"{name}: {testNode}{DescribeChoice()} → {status} " +
                          $"(남은 거리 {context.DistanceToTarget:0.00}, 상태 {context.StateMachine.CurrentState}, {DescribeSlot()})", this);
            restartCountdown = restartDelay;
        }

        return context.Intent;
    }

    /// <summary>피격으로 시작하는 분기인지. 맞기 전에는 돌리지 않는다.</summary>
    static bool StartsWithHitStun(MeleeEnemyTestNode which) =>
        which == MeleeEnemyTestNode.HitStun || which == MeleeEnemyTestNode.HitBranch;

    /// <summary>2택에서 무엇이 뽑혔는지. 합성 노드가 아니면 빈 문자열.</summary>
    string DescribeChoice()
    {
        BTNode leaf = BTNode.FindActiveLeaf(node);
        if (leaf == node) return ""; // 잎을 직접 돌리는 중이면 덧붙일 것이 없다

        switch (leaf)
        {
            case PostAttackWaitNode wait: return $"[{(wait.Purpose == WaitPurpose.Standby ? "대기 중 멈춤" : "대기")} {wait.Duration:0.00}초]";
            case MoveToTargetNode move:   return $"[이동 · {move.Goal}]";
            case MeleeAttackNode _:       return "[근접 공격]";
            case HitStunNode _:           return "[경직]";
            default:                      return $"[{leaf.GetType().Name}]";
        }
    }

    /// <summary>공격권 상태를 있는 그대로 적는다. 관리자가 없으면 "보유"가 아니라 없다고 말해야 오해가 없다.</summary>
    string DescribeSlot()
    {
        if (context.Slots == null) return "슬롯 관리자 없음(항상 허용)";

        return context.Slots.TryGetSide(aiData.slotReach, gameObject, out AttackSide side)
            ? $"공격권 {(side == AttackSide.Right ? "오른쪽" : "왼쪽")}"
            : "공격권 없음";
    }

    /// <summary>
    /// 실제 AI의 대기 전용 루프. 슬롯이 찼을 때 공격 행동 루프 대신 도는 3택이다
    /// (실제 AI에서는 자리가 빌 때까지 이 표만 반복된다).
    /// </summary>
    WeightedRandomNode BuildStandbyLoop()
    {
        return new WeightedRandomNode(
            new WeightedRandomNode.Option(() => aiData.standbyWaitWeight, new PostAttackWaitNode(WaitPurpose.Standby)),
            new WeightedRandomNode.Option(() => aiData.standbyQueueStepWeight, new MoveToTargetNode(aiData.standby)),
            new WeightedRandomNode.Option(() => aiData.standbyRepositionWeight, new MoveToTargetNode(aiData.standbyReposition)));
    }

    /// <summary>
    /// 설계서 8장의 "공격 후 2택". 공격이 끝나면 곧바로 행동 3택으로 가지 않고
    /// 먼저 대기 / 느린 이동 중 하나를 확률로 고른 뒤, 그것이 끝나야 다음 선택으로 넘어간다.
    /// </summary>
    WeightedRandomNode BuildPostAttackChoice()
    {
        return new WeightedRandomNode(
            new WeightedRandomNode.Option(() => aiData.postAttackWaitWeight, new PostAttackWaitNode()),
            new WeightedRandomNode.Option(() => aiData.postAttackSlowStepWeight, new MoveToTargetNode(aiData.slowStep)));
    }

    /// <summary>
    /// 실제 AI와 같은 피격 분기. 이탈은 <b>넘어졌다 일어날 때만</b> 따라붙는다 —
    /// 가벼운 지상 경직이면 조건에서 걸러져 경직만 하고 끝난다.
    /// </summary>
    BTNode BuildHitBranch()
    {
        HitStunNode hitStun = new HitStunNode();
        return new SequenceNode(
            hitStun,
            new ConditionNode(_ => hitStun.WasKnockedDown),
            new MoveToTargetNode(aiData.escape));
    }

    BTNode BuildNode(MeleeEnemyTestNode which)
    {
        switch (which)
        {
            case MeleeEnemyTestNode.Reposition:  return new MoveToTargetNode(aiData.reposition);
            case MeleeEnemyTestNode.SlowStep:    return new MoveToTargetNode(aiData.slowStep);
            case MeleeEnemyTestNode.MeleeAttack: return new MeleeAttackNode();
            case MeleeEnemyTestNode.PostAttackWait: return new PostAttackWaitNode();
            case MeleeEnemyTestNode.PostAttackChoice: return BuildPostAttackChoice();
            case MeleeEnemyTestNode.HitStun: return new HitStunNode();
            case MeleeEnemyTestNode.Escape: return new MoveToTargetNode(aiData.escape);
            case MeleeEnemyTestNode.HitBranch: return BuildHitBranch();
            case MeleeEnemyTestNode.Standby: return new MoveToTargetNode(aiData.standby);
            case MeleeEnemyTestNode.StandbyLoop: return BuildStandbyLoop();
            case MeleeEnemyTestNode.StandbyReposition: return new MoveToTargetNode(aiData.standbyReposition);
            default:                             return new MoveToTargetNode(aiData.approach);
        }
    }
}
