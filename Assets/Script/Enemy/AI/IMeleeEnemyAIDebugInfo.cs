/// <summary>
/// <see cref="MeleeEnemyAIGizmo"/> 같은 디버그용 컴포넌트가 실제 AI에 붙어도, 노드 하나만 돌리는
/// 테스터에 붙어도 똑같이 동작하도록 시각화에 필요한 것만 뽑아낸 인터페이스.
/// <see cref="IAttackRangeDebugInfo"/>와 같은 역할이다.
///
/// 트리 구조를 통째로 넘기지 않고 "지금 도는 가지의 뿌리"만 넘긴다 —
/// 실제로 실행 중인 잎은 <see cref="BTNode.FindActiveLeaf"/>가 따라 내려가 찾는다.
/// </summary>
public interface IMeleeEnemyAIDebugInfo
{
    /// <summary>노드들이 공유하는 작업판. 목표·트리거 범위 계산이 전부 여기 있다. 준비 전이면 null.</summary>
    MeleeEnemyContext Context { get; }

    /// <summary>지금 실행 중인 가지의 뿌리. 아직 아무것도 안 돌고 있으면 null.</summary>
    BTNode ActiveNode { get; }
}
