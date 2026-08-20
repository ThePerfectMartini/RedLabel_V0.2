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
/// </summary>
public struct HitData
{
    public int Damage;
    public Vector3 KnockbackVelocity; // 이 속도로 즉시 튕겨나감 (Y축 포함 -> 에어본)
    public float GroundSlideDeceleration; // 그라운드 넉백일 때 초당 감속량 (에어본이면 사용 안 함)
    public GameObject Attacker;
}