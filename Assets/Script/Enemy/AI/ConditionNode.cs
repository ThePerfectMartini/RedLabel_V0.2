using System;

/// <summary>
/// 조건을 재서 <see cref="BTStatus.Success"/> / <see cref="BTStatus.Failure"/>만 돌려주는 잎.
/// 아무것도 하지 않고 한 프레임도 붙들지 않는다(절대 Running을 반환하지 않는다).
///
/// <see cref="SequenceNode"/>는 Failure에서 멈추므로, 시퀀스 중간에 끼워 넣으면
/// <b>"여기까지 왔는데 조건이 아니면 나머지는 건너뛴다"</b>가 된다 — 비헤이비어 트리에서 분기를 만드는 기본 방식이다.
///
/// 조건을 <see cref="Func{T, TResult}"/>로 받는 이유는 가중치와 같다: 만들 때 값을 읽어두면
/// 그 순간의 판단이 굳어버린다. 실행되는 시점에 다시 재야 한다.
/// </summary>
public class ConditionNode : BTNode
{
    readonly Func<MeleeEnemyContext, bool> predicate;

    public ConditionNode(Func<MeleeEnemyContext, bool> predicate)
    {
        this.predicate = predicate;
    }

    protected override BTStatus OnTick(MeleeEnemyContext context, float deltaTime)
    {
        return predicate != null && predicate(context) ? BTStatus.Success : BTStatus.Failure;
    }
}
