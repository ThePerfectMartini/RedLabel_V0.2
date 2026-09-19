/// <summary>
/// 자식을 <b>순서대로</b> 실행하는 합성 노드. 하나가 끝나면 다음으로 넘어가고, 하나라도 실패하면 거기서 멈춰 실패한다.
/// "공격 접근 → 근접 공격 → 공격 후 행동"처럼 정해진 순서가 있는 묶음이 이것이다.
///
/// 자식이 같은 프레임에 끝나면 다음 자식도 그 프레임에 바로 시작한다(한 프레임 쉬지 않는다) —
/// 접근이 끝나는 순간 공격이 나가야 반응이 굼떠 보이지 않기 때문.
/// </summary>
public class SequenceNode : BTNode
{
    readonly BTNode[] children;
    int index;

    public SequenceNode(params BTNode[] children)
    {
        this.children = children;
    }

    public override BTNode ActiveChild => index >= 0 && index < children.Length ? children[index] : null;

    protected override void OnEnter(MeleeEnemyContext context)
    {
        index = 0;
    }

    protected override BTStatus OnTick(MeleeEnemyContext context, float deltaTime)
    {
        while (index < children.Length)
        {
            BTStatus status = children[index].Tick(context, deltaTime);

            if (status == BTStatus.Running) return BTStatus.Running;
            if (status == BTStatus.Failure) return BTStatus.Failure;

            index++;
        }

        return BTStatus.Success;
    }

    /// <summary>끊길 때 돌고 있던 자식도 같이 끊는다. 이미 끝난 자식에게는 Abort가 아무 일도 하지 않는다.</summary>
    protected override void OnExit(MeleeEnemyContext context)
    {
        if (index < children.Length)
            children[index].Abort(context);
    }
}
