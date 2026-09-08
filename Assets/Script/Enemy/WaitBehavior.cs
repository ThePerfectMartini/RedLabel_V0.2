using UnityEngine;

/// <summary>
/// 관망. 제자리에서 대상만 주시하며 시간을 보낸다. maxActiveDuration이 유일한 종료 조건 —
/// 지침서 3장의 "아무것도 안 하고 접근만 하는(혹은 그냥 서 있는) 통제된 dry 구간".
/// </summary>
[System.Serializable]
public class WaitBehavior : EnemyBehavior
{
    protected override CharacterIntent OnTick(float deltaTime) => NeutralIntent();
}
