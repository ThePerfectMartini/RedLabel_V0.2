using UnityEngine;

/// <summary>
/// 대상에게 다가가는 행동. 오빗 모듈에 목표 각도/반경을 넘기고, 그 궤도 위 목표점으로 이동한다.
/// 궤도 목표점에 도착하면(<see cref="EnemyOrbitMovement.HasArrived"/>) 조기 종료.
/// </summary>
[System.Serializable]
public class ApproachBehavior : EnemyBehavior
{
    [KoreanLabel("접근 각도(도)")]
    [Tooltip("오빗 목표 각도. 0=대상 오른쪽 같은 레인, 90=안쪽, 180=왼쪽, 270=바깥쪽.")]
    public float approachAngle = 0f;

    [KoreanLabel("접근 반경")]
    [Min(0f)]
    [Tooltip("대상으로부터 이 거리까지 좁힌다. 공격 선택 사거리보다 작아야 접근 후 공격이 이어진다.")]
    public float approachRadius = 1.8f;

    // 접근 반경 바로 바깥에서 후보 자격이 깜빡거리지 않게 두는 여유.
    const float ReselectMargin = 0.5f;

    public override bool CanBeSelected()
        => Brain.Targeting.HasTarget && Brain.Targeting.Distance > approachRadius + ReselectMargin;

    protected override void OnEnter()
    {
        Brain.Orbit.SyncToCurrentPosition();
        Brain.Orbit.desiredAngle = TargetAngle();
        Brain.Orbit.orbitRadius = approachRadius;
    }

    protected override CharacterIntent OnTick(float deltaTime)
    {
        Brain.Orbit.desiredAngle = TargetAngle(); // 스쿼드 슬롯 재배정을 계속 반영

        CharacterIntent intent = CharacterIntent.None;
        if (!Brain.Targeting.HasTarget)
            return intent;

        intent.FacingDirection = new Vector3(Brain.Targeting.ToTarget.x, 0f, 0f);
        intent.MoveInput = Brain.Orbit.Advance(deltaTime);
        return intent;
    }

    // 스쿼드가 포위 슬롯을 배정했으면 그쪽, 아니면 자기 기본 각도.
    float TargetAngle() => Brain.HasSlotAssignment ? Brain.SlotAngle : approachAngle;

    protected override bool WantsToExit(float activeElapsed) => Brain.Orbit.HasArrived;
}
