using System;
using UnityEngine;

/// <summary>
/// AttackRangeGizmo 같은 디버그용 컴포넌트가 플레이어/적 어느 쪽에 붙어도 동작하도록,
/// 공격 판정 시각화에 필요한 정보만 뽑아낸 인터페이스.
/// </summary>
public interface IAttackRangeDebugInfo
{
    /// <summary>공격 판정이 나가는 방향.</summary>
    Vector3 FacingDir { get; }

    /// <summary>현재 CombatCore에 적용된 공격의 사거리.</summary>
    float AttackRange { get; }

    /// <summary>현재 CombatCore에 적용된 공격의 판정 반경.</summary>
    float AttackRadius { get; }

    /// <summary>공격 애니메이션이 재생 중인지 (콤보 대기 포함, 타격 판정 여부와는 무관).</summary>
    bool IsAttacking { get; }

    /// <summary>실제 타격 판정(OnAttackHitFrame)이 일어난 시점에 발생.</summary>
    event Action OnAttackHitFrameFired;
}
