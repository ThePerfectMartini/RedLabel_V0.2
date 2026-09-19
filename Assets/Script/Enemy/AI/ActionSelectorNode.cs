/// <summary>행동 선택이 놓인 상황. 어떤 가중치 표를 쓸지가 이것으로 갈린다.</summary>
public enum ActionSituation
{
    /// <summary>원거리 — 점프 발동 거리 이상 떨어져 있다.</summary>
    Far,
    /// <summary>근거리 — 붙어 있고 공격권도 받을 수 있다.</summary>
    Near,
    /// <summary>내 쪽 옆구리 자리가 차 있다. 공격 후보를 빼고 대기 행동만 고른다.</summary>
    SlotBlocked,
}

/// <summary>
/// 설계서 8장의 행동 선택. <b>랜덤이 먼저가 아니라 거리·슬롯 필터가 먼저다</b> —
/// 근접 거리에서 점프대쉬가 절대 안 나오는 것이 그 증거다. 상황을 먼저 정하고,
/// 그 상황의 가중치 표 안에서만 뽑는다.
///
/// 상황 판정은 <b>들어올 때 한 번만</b> 한다. 뽑은 행동은 끝까지 수행하고, 다음 선택은 루트가 다시 들여보낼 때 한다 —
/// 매 프레임 다시 판정하면 거리 경계에서 행동이 깜빡이며 아무것도 못 하게 된다.
/// </summary>
public class ActionSelectorNode : BTNode
{
    readonly BTNode farChoice;
    readonly BTNode nearChoice;
    readonly BTNode blockedChoice;

    BTNode chosen;

    public ActionSelectorNode(BTNode farChoice, BTNode nearChoice, BTNode blockedChoice)
    {
        this.farChoice = farChoice;
        this.nearChoice = nearChoice;
        this.blockedChoice = blockedChoice;
    }

    /// <summary>이번에 판정된 상황. 디버그 표시용.</summary>
    public ActionSituation Situation { get; private set; }

    public override BTNode ActiveChild => chosen;

    protected override void OnEnter(MeleeEnemyContext context)
    {
        Situation = ResolveSituation(context);

        switch (Situation)
        {
            case ActionSituation.SlotBlocked: chosen = blockedChoice; break;
            case ActionSituation.Far:         chosen = farChoice;     break;
            default:                          chosen = nearChoice;    break;
        }
    }

    protected override BTStatus OnTick(MeleeEnemyContext context, float deltaTime)
    {
        if (chosen == null) return BTStatus.Failure;
        return chosen.Tick(context, deltaTime);
    }

    protected override void OnExit(MeleeEnemyContext context)
    {
        if (chosen != null)
            chosen.Abort(context);
    }

    /// <summary>
    /// 슬롯을 거리보다 먼저 본다. 설계서 그림 6은 근거리에서만 슬롯을 묻지만, 8장 본문은
    /// "슬롯이 찼으면 공격 후보를 빼고 고른다"라고 거리와 무관하게 말한다. 본문 쪽을 따랐다 —
    /// 자리도 없는데 원거리에서 접근부터 시작하면 다 가서야 못 때린다는 걸 알게 되기 때문.
    ///
    /// 묻는 것은 "<b>내 쪽</b> 자리가 비었는가"다(<see cref="MeleeEnemyContext.CanTakeAttackSlot"/>).
    /// 반대쪽만 비어 있으면 여기서는 막힘으로 보고, 대기 행동이 반원을 그려 그쪽으로 데려다 준다.
    ///
    /// (점프대쉬는 슬롯을 안 쓰므로, 나중에 점프대쉬가 붙으면 "원거리 + 슬롯 막힘"은
    ///  점프대쉬와 재정렬만 남는 별도 상황이 되어야 한다.)
    /// </summary>
    static ActionSituation ResolveSituation(MeleeEnemyContext context)
    {
        if (!context.CanTakeAttackSlot) return ActionSituation.SlotBlocked;

        return context.DistanceToTarget >= context.Data.jumpDashMinDistance
            ? ActionSituation.Far
            : ActionSituation.Near;
    }
}
