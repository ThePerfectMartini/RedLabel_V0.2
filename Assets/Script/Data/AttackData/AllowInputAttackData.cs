using UnityEngine;

/// <summary>공격 중에도 플레이어 입력으로 자유롭게 이동 가능한 공격.</summary>
[CreateAssetMenu(fileName = "AttackData_AllowInput", menuName = "DoitMySelf/Attack Data/Allow Input")]
public class AllowInputAttackData : AttackData
{
    [Header("이동 (Allow Input 전용)")]
    [KoreanLabel("이동 속도 배율")]
    [Tooltip("이 공격을 하는 동안의 이동 속도를 기본 이동 속도(MovementStatData.moveSpeed)의 몇 배로 할지. " +
        "1이면 평소와 같고, 0.5면 절반 속도로 느릿하게 움직이며 공격한다.")]
    [Min(0f)]
    public float moveSpeedMultiplier = 1f;

    public override float MoveSpeedMultiplier => moveSpeedMultiplier;
}
