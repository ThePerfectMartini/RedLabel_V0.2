using UnityEngine;

/// <summary>공격 중 이동 입력을 받지 않고 제자리에 고정되는 공격.</summary>
[CreateAssetMenu(fileName = "AttackData_Locked", menuName = "DoitMySelf/Attack Data/Locked")]
public class LockedAttackData : AttackData
{
    public override bool AllowsPlayerMovement => false;
}
