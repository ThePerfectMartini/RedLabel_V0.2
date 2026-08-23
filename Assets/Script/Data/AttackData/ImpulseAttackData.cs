using UnityEngine;

/// <summary>공격 중 이동 입력을 받지 않고, 바라보는 방향으로 강제 이동(돌진 등)하는 공격.</summary>
[CreateAssetMenu(fileName = "AttackData_Impulse", menuName = "DoitMySelf/Attack Data/Impulse")]
public class ImpulseAttackData : AttackData
{
    [KoreanLabel("돌진 속도")]
    public float selfMoveSpeed = 10f;

    [KoreanLabel("돌진 감속")]
    [Tooltip("돌진 속도가 초당 이만큼 줄어든다 (MovementCore.ApplyKnockback의 그라운드 슬라이드 감속을 그대로 재사용)")]
    public float selfMoveDeceleration = 20f;

    public override bool AllowsPlayerMovement => false;

    public override void ApplySelfMovement(MovementCore movement, Vector3 facingDir)
    {
        movement.ApplyKnockback(facingDir.normalized * selfMoveSpeed, selfMoveDeceleration);
    }
}
