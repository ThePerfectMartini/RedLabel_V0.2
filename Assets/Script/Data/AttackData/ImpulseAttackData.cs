using UnityEngine;

/// <summary>
/// ImpulseAttackData의 돌진 방향.
/// Forward/Backward는 바라보는 방향(FacingDir, x축) 기준 상대 방향이고,
/// Up/Down은 벨트스크롤 화면에서의 위/아래 = 월드 z축(깊이) 방향이다 (수직 점프가 아님).
/// </summary>
public enum ImpulseDirection
{
    [InspectorName("전방(바라보는 방향)")]
    Forward,
    [InspectorName("후방(바라보는 반대 방향)")]
    Backward,
    [InspectorName("위(화면 안쪽, +z)")]
    Up,
    [InspectorName("아래(화면 바깥쪽, -z)")]
    Down,
}

/// <summary>공격 중 이동 입력을 받지 않고, 지정한 방향으로 강제 이동(돌진 등)하는 공격.</summary>
[CreateAssetMenu(fileName = "AttackData_Impulse", menuName = "DoitMySelf/Attack Data/Impulse")]
public class ImpulseAttackData : AttackData
{
    [Header("돌진 (Impulse 전용)")]
    [KoreanLabel("돌진 방향")]
    [Tooltip("Forward/Backward는 바라보는 방향(FacingDir, 좌우) 기준이고, Up/Down은 좌우 반전과 무관한 화면상의 위/아래(월드 z축, 깊이) 방향이다.")]
    public ImpulseDirection direction = ImpulseDirection.Forward;

    [KoreanLabel("돌진 속도")]
    public float selfMoveSpeed = 10f;

    [KoreanLabel("돌진 감속")]
    [Tooltip("돌진 속도가 초당 이만큼 줄어든다 (MovementCore.ApplyKnockback의 그라운드 슬라이드 감속을 그대로 재사용)")]
    public float selfMoveDeceleration = 20f;

    public override bool AllowsPlayerMovement => false;

    public override void ApplySelfMovement(MovementCore movement, Vector3 facingDir)
    {
        Vector3 worldDir = ResolveWorldDirection(facingDir);
        movement.ApplyKnockback(worldDir.normalized * selfMoveSpeed, selfMoveDeceleration);
    }

    Vector3 ResolveWorldDirection(Vector3 facingDir)
    {
        switch (direction)
        {
            case ImpulseDirection.Forward: return facingDir;
            case ImpulseDirection.Backward: return -facingDir;
            // 벨트스크롤 화면에서의 위/아래는 수직(y)이 아니라 깊이(z) 이동이다.
            case ImpulseDirection.Up: return Vector3.forward;
            case ImpulseDirection.Down: return Vector3.back;
            default: return facingDir;
        }
    }
}
