using UnityEngine;

/// <summary>
/// 일반 근접 적의 행동 AI. 설계서 9장의 트리를 조립해 매 프레임 한 번 굴리고,
/// 그 결과를 <see cref="CharacterIntent"/>로 포장해 Actor에게 넘긴다.
///
/// <b>의사결정만 한다.</b> 공격 판정 · 넉백 · 피격 처리 · 애니메이션은 전부 기존
/// Fighter / AttackData / Health / CharacterStateMachine이 하던 그대로다.
///
/// [붙이는 곳] 적 루트(Actor와 같은 오브젝트). Actor가 GetComponent&lt;IIntentSource&gt;()로 찾는다.
///            임시 더미였던 EnemyBrain은 <b>컴포넌트를 제거</b>해야 한다 — 체크만 해제하면
///            GetComponent가 그걸 그대로 집어서 이 AI가 아예 호출되지 않는다.
/// [필요] 목표(플레이어)와 MeleeEnemyAIData 에셋. 수치는 전부 그 에셋에 있다.
/// [주의] Locomotion의 '8방향 스냅'을 꺼야 경로가 45도로 꺾이지 않는다.
///
/// 스폰 즉시 전투 루프에 들어간다 — 설계서대로 순찰·대기 같은 초기 상태도, 추격 포기 거리도 없다.
/// </summary>
public class MeleeEnemyAI : MonoBehaviour, IIntentSource, IMeleeEnemyAIDebugInfo
{
    [KoreanLabel("목표 (플레이어)")]
    public Transform target;

    [KoreanLabel("AI 수치")]
    public MeleeEnemyAIData aiData;

    [KoreanLabel("결정을 Console에 찍기")]
    [Tooltip("행동을 새로 고를 때마다 상황과 결과를 남긴다. 수치를 맞추는 동안만 켤 것.")]
    public bool logDecisions;

    MeleeEnemyContext context;
    Health health;

    // 루트의 두 갈래. 피격 분기는 트리 안의 조건이 아니라 바깥에서 끼어드는 신호로 들어온다.
    ActionSelectorNode combatLoop;
    BTNode hitBranch;
    BTNode active;

    // ===== IMeleeEnemyAIDebugInfo (MeleeEnemyAIGizmo가 읽는다) =====

    public MeleeEnemyContext Context => context;
    public BTNode ActiveNode => active;

    void Awake()
    {
        if (target == null)
            Debug.LogWarning($"{name}: 목표가 비어 있어 AI가 아무것도 하지 않습니다.", this);
        if (aiData == null)
            Debug.LogWarning($"{name}: AI 수치 에셋이 비어 있어 AI가 아무것도 하지 않습니다.", this);
        else if (aiData.meleeAttack == null)
            Debug.LogWarning($"{name}: AI 수치 에셋의 '근접 공격 데이터'가 비어 있어 근접 공격이 나가지 않습니다.", aiData);

        Locomotion locomotion = GetComponent<Locomotion>();
        if (locomotion != null && locomotion.use8DirectionSnap)
            Debug.LogWarning($"{name}: Locomotion의 '8방향 스냅'이 켜져 있어 이동 경로가 45도 단위로 꺾입니다. 꺼 주세요.", this);

        context = new MeleeEnemyContext(gameObject, target, aiData);

        if (aiData != null)
            BuildTree();

        health = GetComponent<Health>();
        if (health != null)
            health.OnHitTaken += OnHitTaken;
    }

    void OnDestroy()
    {
        if (health != null)
            health.OnHitTaken -= OnHitTaken;
    }

    // 다른 적이 "거기 누가 서 있나"를 물어볼 수 있게 명부에 올린다. 대기 자리와 재정렬 목적지가 이걸 본다.
    void OnEnable() => EnemyCrowd.Register(transform);

    void OnDisable() => EnemyCrowd.Unregister(transform);

    void OnHitTaken(HitData hit) => context.NotifyHit();

    /// <summary>
    /// 설계서 9장 그림 7의 골격. 노드는 상태를 들고 있으므로 원거리용과 근거리용 근접 묶음은
    /// 같은 인스턴스를 돌려쓰지 않고 따로 만든다.
    /// </summary>
    void BuildTree()
    {
        // 원거리 표: 근접(접근+공격) / 자리 재정렬.
        // 점프대쉬 자리가 여기 하나 더 들어간다 — 점프 클립이 준비되면 farJumpDashWeight로 붙인다.
        BTNode farChoice = new WeightedRandomNode(
            new WeightedRandomNode.Option(() => aiData.farMeleeWeight, BuildMeleeBranch()),
            new WeightedRandomNode.Option(() => aiData.farRepositionWeight, new MoveToTargetNode(aiData.reposition)));

        // 근거리 표: 근접 / 자리 재정렬.
        BTNode nearChoice = new WeightedRandomNode(
            new WeightedRandomNode.Option(() => aiData.nearMeleeWeight, BuildMeleeBranch()),
            new WeightedRandomNode.Option(() => aiData.nearRepositionWeight, new MoveToTargetNode(aiData.reposition)));

        combatLoop = new ActionSelectorNode(farChoice, nearChoice, BuildStandbyLoop());

        // 피격 분기 (설계서 ⑦ → ⑧). 끊긴 행동을 이어서 하지 않는다.
        //
        // 설계서는 피격 뒤에 항상 이탈이 따라온다고 했지만, 실제로는 <b>넘어졌다 일어날 때만</b> 이탈한다.
        // 가벼운 지상 경직까지 매번 물러나면 한 대 칠 때마다 적이 도망쳐 전투가 늘어지고,
        // 큰 공격으로 넘어뜨렸을 때의 "거리가 벌어진다"는 신호도 같이 묻힌다.
        // (설계서는 이탈을 점프로 그렸지만 지상 이동으로 구현했다 — 프로필 하나로 표현되고 점프 클립이 필요 없다.)
        HitStunNode hitStun = new HitStunNode();
        hitBranch = new SequenceNode(
            hitStun,
            new ConditionNode(_ => hitStun.WasKnockedDown), // 안 넘어졌으면 여기서 끝, 바로 행동 선택으로
            new MoveToTargetNode(aiData.escape));
    }

    /// <summary>
    /// 내 쪽 슬롯이 차 있을 때 도는 <b>대기 전용 루프</b>. 공격 행동 루프와는 표가 아예 다르다 —
    /// 여기 있는 셋 중 무엇이 뽑혀도 적은 공격 위치보다 바깥에 있는 <b>대기 링</b>을 벗어나지 않는다.
    /// 자리가 빌 때까지 루트가 이 표로 계속 돌려보낸다.
    ///
    /// 공격 후 행동(느린 이동)을 여기 섞지 않는 이유: 그건 "방금 때리고 물러난다"는 뜻이라
    /// 아직 한 대도 못 친 대기 적이 하면 의미가 어긋나고, 목표도 링과 무관해서 대기 공간을 벗어난다.
    ///
    /// 제자리 대기의 시간은 짧게(<c>standbyWait…</c>) 잡는다 — 길면 멈춰 서서 줄 서 있는 그림이 된다.
    /// </summary>
    BTNode BuildStandbyLoop()
    {
        return new WeightedRandomNode(
            new WeightedRandomNode.Option(() => aiData.standbyWaitWeight, new PostAttackWaitNode(WaitPurpose.Standby)),
            new WeightedRandomNode.Option(() => aiData.standbyQueueStepWeight, new MoveToTargetNode(aiData.standby)),
            new WeightedRandomNode.Option(() => aiData.standbyRepositionWeight, new MoveToTargetNode(aiData.standbyReposition)));
    }

    /// <summary>
    /// 근접 한 묶음: 공격권을 쥔 채 [접근 → 공격]을 하고, 끝나면 공격 후 2택(대기 / 느린 이동)까지 마친다.
    /// 공격 후 행동은 공격권 껍데기 <b>밖</b>에 둔다 — 공격이 끝나면 바로 반납해야 다른 적이 들어올 수 있다.
    /// </summary>
    BTNode BuildMeleeBranch()
    {
        BTNode approachAndAttack = new AttackSlotScopeNode(
            new SequenceNode(
                new MoveToTargetNode(aiData.approach),
                new MeleeAttackNode()));

        BTNode postAttack = new WeightedRandomNode(
            new WeightedRandomNode.Option(() => aiData.postAttackWaitWeight, new PostAttackWaitNode()),
            new WeightedRandomNode.Option(() => aiData.postAttackSlowStepWeight, new MoveToTargetNode(aiData.slowStep)));

        return new SequenceNode(approachAndAttack, postAttack);
    }

    /// <summary>지금 어느 쪽 공격권을 들고 있는지 한 줄로. logDecisions 전용.</summary>
    string DescribeSlot()
    {
        if (context.Slots == null) return "슬롯 관리자 없음";

        return context.Slots.TryGetSide(aiData.slotReach, gameObject, out AttackSide side)
            ? $"공격권 {(side == AttackSide.Right ? "오른쪽" : "왼쪽")}"
            : "공격권 없음";
    }

    public CharacterIntent GetIntent(float deltaTime)
    {
        if (context == null || aiData == null || target == null || combatLoop == null)
            return CharacterIntent.None;

        context.Target = target;
        context.BeginFrame();

        // 피격은 어떤 행동 중이든 즉시 끊는다. 트리 안의 조건이 아니라 바깥에서 들어오는 유일한 신호다.
        if (context.HitPending && active != hitBranch)
        {
            active?.Abort(context);
            active = hitBranch;
        }

        if (active == null)
            active = combatLoop;

        BTStatus status = active.Tick(context, deltaTime);

        if (status != BTStatus.Running)
        {
            if (logDecisions)
                Debug.Log($"{name}: {(active == hitBranch ? "피격 경직" : combatLoop.Situation.ToString())} → {status} " +
                          $"(거리 {context.DistanceToTarget:0.00}, {DescribeSlot()})", this);

            // 무엇이 끝났든 루트부터 다시 고른다. 이탈 점프 뒤의 복귀도 "완전 랜덤, 직전 행동과 무관"이어야 하므로
            // 끊긴 행동을 이어서 하지 않는다.
            active = combatLoop;
        }

        return context.Intent;
    }
}
