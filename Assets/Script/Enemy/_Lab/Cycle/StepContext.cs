using UnityEngine;

/// <summary>
/// 스텝이 판단에 쓰는 모든 정보. 스텝이 Unity 싱글턴(PlayerController.Instance 등)이나
/// 조율자를 직접 만지지 않게 하려고, 브레인이 매 프레임 한 번 채워서 넘긴다.
///
/// [좌표가 두 개인 이유] 기존 ChasePlayerBrain이 주석으로 남긴 교훈을 그대로 가져왔다.
/// - TargetPosition     : 반응 주기마다만 갱신되는 늦은 좌표. 이동과 방향이 쓴다.
///   매 프레임 실시간으로 정확히 쫓아가면 너무 기계적이고 어렵게 느껴지기 때문이다.
/// - LiveTargetPosition : 지연 없는 실시간 좌표. 사거리 판정과 대형 계산이 쓴다.
///   개체마다 다른 시점의 좌표로 대형을 짜면 같은 프레임인데도 서로 다른 대형을 상정하게 된다.
///
/// readonly struct라서 in으로 넘기면 복사가 일어나지 않는다.
/// </summary>
public readonly struct StepContext
{
    /// <summary>이 브레인이 조종하는 몸. 상태를 읽기만 하고 직접 조작하지 말 것.</summary>
    public readonly EnemyController Owner;

    public readonly BehaviorTuningData Tuning;

    public readonly Vector3 SelfPosition;

    /// <summary>반응 지연이 적용된 목표 좌표. 이동·방향용.</summary>
    public readonly Vector3 TargetPosition;

    /// <summary>지연 없는 실시간 목표 좌표. 사거리 판정용.</summary>
    public readonly Vector3 LiveTargetPosition;

    /// <summary>목표(플레이어)의 현재 표현 상태. 다운/후딜 판정에 쓴다.</summary>
    public readonly CharacterState TargetState;

    /// <summary>조율자가 이번 프레임에 배정한 자리와 권한.</summary>
    public readonly EngagementOrder Order;

    /// <summary>배정된 슬롯의 월드 좌표. 브레인이 반응 지연과 z 오차까지 반영해 계산해 준다.</summary>
    public readonly Vector3 SlotPosition;

    public readonly float DeltaTime;

    public StepContext(
        EnemyController owner,
        BehaviorTuningData tuning,
        Vector3 selfPosition,
        Vector3 targetPosition,
        Vector3 liveTargetPosition,
        CharacterState targetState,
        EngagementOrder order,
        Vector3 slotPosition,
        float deltaTime)
    {
        Owner = owner;
        Tuning = tuning;
        SelfPosition = selfPosition;
        TargetPosition = targetPosition;
        LiveTargetPosition = liveTargetPosition;
        TargetState = targetState;
        Order = order;
        SlotPosition = slotPosition;
        DeltaTime = deltaTime;
    }

    /// <summary>
    /// 얻어맞아 경직/다운/기상 중인가. 공격 재생 중(IsAttacking)도 CanAct는 false지만
    /// 그건 경직이 아니라 자기가 하는 행동이므로 제외한다 — 기존 코드와 같은 관용구다.
    /// </summary>
    public bool IsStaggered => !Owner.CanAct && !Owner.IsAttacking;

    /// <summary>
    /// 공격 모션이 재생 중인가. 몸이 이번 프레임 의도를 처리하기 전에 이 컨텍스트가 만들어지므로
    /// 한 프레임 늦은 값이다 (AttackStep이 그 점을 전제로 짜여 있다).
    /// </summary>
    public bool IsAttacking => Owner.IsAttacking;

    /// <summary>목표가 쓰러져 있거나 일어나는 중. 이때는 달려들지 않고 물러나 기다린다.</summary>
    public bool TargetIsDown => TargetState == CharacterState.Airborne
                             || TargetState == CharacterState.Landed
                             || TargetState == CharacterState.GetUp;

    /// <summary>
    /// 목표가 지금 빈틈을 보이는가. 공격 모션 중(후딜)이거나 착지 경직 중이면 지금이 기회다.
    /// 이 판정이 "헛치면 반격당한다"는 이 장르의 기본 문법을 만든다.
    /// </summary>
    public bool TargetIsOpen => TargetState == CharacterState.Attack
                             || TargetState == CharacterState.JumpLand
                             || TargetState == CharacterState.Stun;

    /// <summary>
    /// 지금 공격을 내면 실제로 맞는 위치인지. CombatCore는 "정면 사거리 지점에 반경 R짜리 구"로
    /// 판정하므로 x는 사거리까지, z는 판정 반경까지 허용한다.
    /// </summary>
    public bool IsInAttackRange => Mathf.Abs(LiveTargetPosition.x - SelfPosition.x) <= Owner.AttackRange
                                && Mathf.Abs(LiveTargetPosition.z - SelfPosition.z) <= Owner.AttackRadius;

    /// <summary>배정된 슬롯까지의 수평 거리. 높이는 무시한다.</summary>
    public float DistanceToSlot => PlanarDistance(SelfPosition, SlotPosition);

    /// <summary>목표까지의 실시간 수평 거리.</summary>
    public float DistanceToTarget => PlanarDistance(SelfPosition, LiveTargetPosition);

    static float PlanarDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
