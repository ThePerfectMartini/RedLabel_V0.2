using UnityEngine;

/// <summary>
/// 공격에 맞을 수 있는 대상이 구현하는 인터페이스.
/// 플레이어, 적, 보스 모두 이걸 구현하면 CombatCore가 공통으로 처리 가능.
/// </summary>
public interface IHittable
{
    void OnHit(HitData hit);
}

/// <summary>
/// 한 번의 피격 정보. 데미지 + 넉백 벡터만 최소 구성.
///
/// 넉백 벡터는 대상이 지상에 있을 때와 공중에 있을 때 두 가지가 함께 전달된다.
/// 공격자(CombatCore)는 맞은 대상이 지금 떠 있는지 알 수 없고, 그건 대상 자신만 아는 정보이므로
/// 둘 다 실어 보내고 어느 쪽을 쓸지는 대상의 OnHit에서 고른다 (ResolveKnockbackVelocity 참고).
/// </summary>
public struct HitData
{
    public int Damage;
    public Vector3 KnockbackVelocity; // 대상이 지상에 있을 때 이 속도로 즉시 튕겨나감 (Y축 포함 -> 에어본)
    public Vector3 AirborneKnockbackVelocity; // 대상이 이미 공중에 있을 때 적용할 속도
    public float GroundSlideDeceleration; // 그라운드 넉백일 때 초당 감속량 (에어본이면 사용 안 함)
    public GameObject Attacker;

    /// <summary>
    /// 맞은 대상이 지금 공중에 있는지(isTargetAirborne)에 따라 실제로 적용할 넉백 속도를 고른다.
    /// </summary>
    public Vector3 ResolveKnockbackVelocity(bool isTargetAirborne)
    {
        return isTargetAirborne ? AirborneKnockbackVelocity : KnockbackVelocity;
    }
}