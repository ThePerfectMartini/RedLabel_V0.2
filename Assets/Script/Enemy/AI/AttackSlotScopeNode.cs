/// <summary>
/// 감싼 자식이 도는 동안 <b>공격권을 쥐고 있게</b> 하는 껍데기 노드 (설계서 7장).
///
/// 공격권은 공격 순간이 아니라 <b>접근을 시작할 때</b> 받아야 한다 — 안 그러면 여러 적이 동시에
/// 달려든 뒤 마지막에 가서야 자리가 없다는 걸 알게 된다. 그래서 "접근 + 공격" 묶음 전체를 이걸로 감싼다.
///
/// 반납을 여기서 보장하는 것이 핵심이다. 접근이 시간 초과로 실패하든, 공격 중에 얻어맞아 끊기든,
/// 어떤 경로로 빠져나가도 OnExit은 반드시 지나가므로 공격권이 새지 않는다.
/// 새면 그 자리가 영영 잠겨서 다른 적들이 영원히 못 때린다.
///
/// 정상 흐름에서는 <see cref="MeleeAttackNode"/>가 공격이 끝나는 순간 먼저 반납한다
/// (설계서: "공격권은 공격이 끝나면 반납한다" — 그 뒤의 공격 후 행동까지 붙들고 있으면 안 된다).
/// 여기 반납은 그때 이미 처리됐으면 아무 일도 하지 않는 안전망이다.
/// </summary>
public class AttackSlotScopeNode : BTNode
{
    readonly BTNode child;
    bool acquired;

    public AttackSlotScopeNode(BTNode child)
    {
        this.child = child;
    }

    public override BTNode ActiveChild => acquired ? child : null;

    protected override void OnEnter(MeleeEnemyContext context)
    {
        acquired = context.TryTakeAttackSlot();
    }

    protected override BTStatus OnTick(MeleeEnemyContext context, float deltaTime)
    {
        // 자리가 없으면 시작도 하지 않는다. 행동 선택이 이미 걸렀을 상황이지만,
        // 고르는 순간과 실제로 시작하는 순간 사이에 다른 적이 채갈 수 있다.
        if (!acquired) return BTStatus.Failure;

        return child.Tick(context, deltaTime);
    }

    protected override void OnExit(MeleeEnemyContext context)
    {
        child.Abort(context);
        context.ReleaseAttackSlot();
        acquired = false;
    }
}
