using UnityEngine;

/// <summary>
/// 행동이 이번 프레임의 결과로 보고하는 값. 행동이 밖에 말할 수 있는 것은 이 셋뿐이다.
/// </summary>
public enum BehaviorResult
{
    /// <summary>아직 진행 중. 다음 프레임에도 같은 행동이 돈다.</summary>
    Running,

    /// <summary>정상적으로 끝났다.</summary>
    Done,

    /// <summary>전제가 무너져서 중단한다 (예: 공격하려 했는데 목표가 사거리 밖으로 벗어남).</summary>
    Abort,
}

/// <summary>
/// 행동이 판단에 쓰는 정보. 행동이 PlayerController.Instance 같은 싱글턴을 직접 만지지 않도록
/// 브레인이 매 프레임 한 번 채워서 넘긴다.
///
/// 반응 지연처럼 아직 필요하지 않은 값은 그때 추가한다.
/// 자기 위치는 여기 담지 않는다 — Owner.Position이 곧 그 값이라 두 벌로 두면 어긋날 자리가 생긴다.
///
/// readonly struct라서 in으로 넘기면 복사가 일어나지 않는다.
/// </summary>
public readonly struct BehaviorContext
{
    /// <summary>이 브레인이 조종하는 몸. 상태를 읽기만 하고 직접 조작하지 말 것.</summary>
    public readonly EnemyController Owner;

    /// <summary>목표(플레이어)의 현재 위치.</summary>
    public readonly Vector3 TargetPosition;

    public readonly float DeltaTime;

    /// <summary>
    /// 이번 프레임에 이 적이 서야 할 자리. 조율자가 있으면 조율자가, 없으면 브레인이 혼자 정한다.
    /// 행동은 그 자리가 어디서 왔는지 알 필요가 없다.
    /// </summary>
    public readonly EngagementOrder Order;

    /// <summary>
    /// 지금 공격을 시작해도 되는지. 조율자가 동시에 칠 수 있는 수를 제한하며, 조율자가 없으면 항상 참이다.
    /// 이미 시작한 공격에는 영향이 없다 — 이 값은 "새로 시작해도 되는가"만 말한다.
    /// </summary>
    public readonly bool CanAttack;

    /// <summary>
    /// 지금 플레이어를 상대하는 중인지. 들어오는 거리와 나가는 거리가 달라서(히스테리시스) 경계에서
    /// 깜빡이지 않는다. 매 프레임 갱신되므로 배회는 이 값이 참이 되는 즉시 끝낼 수 있다.
    /// </summary>
    public readonly bool InCombat;

    /// <summary>
    /// 지금 공격하면 맞는 위치에 목표가 있는지. CombatCore.PerformHitScan과 같은 계산을 쓴다 —
    /// 판정 구의 중심은 "자기 위치 + 바라보는 방향 x 공격 사거리"이고 반지름은 공격 판정 반경이다.
    /// 여기서 참이면 실제 판정도 (대상 콜라이더 크기만큼 더 여유가 있으므로) 반드시 닿는다.
    ///
    /// 공격이 "칠 수 있는가"를 묻고 대기가 "지금 반응해야 하는가"를 묻는 데 같은 기준을 쓰기 때문에
    /// 여기 둔다. 행동마다 각자 계산하면 두 기준이 조용히 어긋난다.
    /// </summary>
    public bool IsTargetInHitRange => IsTargetWithinHitRange(0f);

    /// <summary>
    /// 판정 구를 extraRadius만큼 부풀린 범위 안에 목표가 있는지. 중심은 그대로 두고 반지름만 키운다.
    /// "지금은 못 치지만 조금만 움직이면 칠 수 있는 거리"를 묻는 데 쓴다.
    ///
    /// 판정과 같은 중심을 쓰는 것이 중요하다. 자기 위치에서 재면 뒤쪽이나 옆쪽까지 같은 거리로 취급되어,
    /// 실제로 칠 수 있는 방향과 다른 모양의 범위가 된다.
    /// </summary>
    public bool IsTargetWithinHitRange(float extraRadius)
    {
        Vector3 center = Owner.Position + Owner.FacingDir * Owner.AttackRange;
        Vector3 offset = TargetPosition - center;
        offset.y = 0f;

        float radius = Owner.AttackRadius + extraRadius;
        return offset.sqrMagnitude <= radius * radius;
    }

    public BehaviorContext(EnemyController owner, Vector3 targetPosition, float deltaTime,
        EngagementOrder order, bool canAttack, bool inCombat)
    {
        Owner = owner;
        TargetPosition = targetPosition;
        DeltaTime = deltaTime;
        Order = order;
        CanAttack = canAttack;
        InCombat = inCombat;
    }
}

/// <summary>
/// 적 행동 하나. 접근 / 공격 / 후퇴 / 대기 / 배회가 각각 이걸 구현한다.
///
/// [핵심 규칙] 행동은 <b>다음 행동이 무엇인지 모른다.</b> 결과(BehaviorResult)만 보고하고,
/// 어디로 갈지는 브레인이 정한다. 이 규칙이 있어야 행동을 하나씩 독립적으로 만들고
/// 순서만 바꿔 다른 적을 조립할 수 있다. 행동끼리 서로를 참조하는 순간 조립이 불가능해진다.
///
/// [또 하나의 규칙] 여기서 직접 위치를 옮기거나 공격 판정을 하지 말 것.
/// 그건 EnemyController와 MovementCore/CombatCore의 일이다. 행동은 intent만 채운다.
///
/// MonoBehaviour가 아니다 (MovementCore/CombatCore와 같은 원칙). Unity 시간에도 의존하지 않으며,
/// 필요한 것은 전부 BehaviorContext로 넘어온다. 튜닝 값은 각 행동이 자기 필드로 갖고,
/// [Serializable]이라 브레인의 인스펙터에 그대로 접혀서 나온다.
/// </summary>
public interface IEnemyBehavior
{
    /// <summary>이 행동에 막 들어왔을 때 한 번. 타이머 초기화나 시작 시점 기록에 쓴다.</summary>
    void OnEnter(in BehaviorContext ctx);

    /// <summary>
    /// 매 프레임 한 번. 이번 프레임에 하고 싶은 것을 intent에 채우고 결과를 보고한다.
    /// intent는 브레인이 바라볼 방향을 미리 채워둔 상태로 넘어오므로, 통째로 덮어쓰지 말고
    /// 필요한 필드만 건드린다.
    /// </summary>
    BehaviorResult Tick(in BehaviorContext ctx, ref CharacterIntent intent);
}

/// <summary>
/// "이 행동은 오래 머물러도 정상"이라는 표식. 브레인의 워치독이 건너뛴다.
///
/// 공격 준비처럼 끝나는 시점을 자기가 정하지 않는 행동(권한이 올 때까지 기다린다)까지 워치독에 걸면
/// 정상 동작이 매번 경고로 찍혀서, 진짜로 멈춘 경우를 알아볼 수 없게 된다.
/// </summary>
public interface IUnboundedBehavior
{
}
