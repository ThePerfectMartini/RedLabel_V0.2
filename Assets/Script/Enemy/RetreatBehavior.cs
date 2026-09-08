using UnityEngine;

/// <summary>
/// 후퇴. 현재 각도는 유지한 채 오빗 반경만 늘려 대상에게서 뒤로 물러난다.
/// 물러날 거리에 도달하면 조기 종료.
/// </summary>
[System.Serializable]
public class RetreatBehavior : EnemyBehavior
{
    [KoreanLabel("후퇴 발동 거리")]
    [Min(0f)]
    [Tooltip("대상이 이 거리 안으로 들어왔을 때만 셀렉터가 후퇴를 후보로 삼는다. 접근 반경보다 작게.")]
    public float triggerRange = 1.5f;

    [KoreanLabel("후퇴 반경")]
    [Min(0f)]
    [Tooltip("대상으로부터 이 거리까지 물러난다.")]
    public float retreatRadius = 4f;

    public override bool CanBeSelected()
        => Brain.Targeting.HasTarget && Brain.Targeting.Distance < triggerRange;

    protected override void OnEnter()
    {
        Brain.Orbit.SyncToCurrentPosition();
        Brain.Orbit.desiredAngle = Brain.Orbit.CurrentAngle; // 각도 유지 — 뒤로만
        Brain.Orbit.orbitRadius = retreatRadius;
    }

    protected override CharacterIntent OnTick(float deltaTime)
    {
        CharacterIntent intent = CharacterIntent.None;
        if (!Brain.Targeting.HasTarget)
            return intent;

        intent.FacingDirection = new Vector3(Brain.Targeting.ToTarget.x, 0f, 0f);
        intent.MoveInput = Brain.Orbit.Advance(deltaTime);
        return intent;
    }

    protected override bool WantsToExit(float activeElapsed) => Brain.Orbit.HasArrived;
}
