using System;
using UnityEngine;

/// <summary>
/// 체력과 피격 처리. 맞으면 진행 중이던 동작을 <b>무조건</b> 끊고(CharacterStateMachine.Interrupt),
/// 넉백을 Locomotion에 넘긴다. 어느 쪽 넉백(지상/공중)을 쓸지는 지금 떠 있는지 아는 이쪽이 고른다.
///
/// 사망은 이벤트(OnDeath)로만 알린다 — 소멸/연출은 이 이벤트를 구독하는 별도 컴포넌트가 맡는다
/// (스켈레톤 범위에선 없음. 죽은 캐릭터는 dead 플래그로 더 이상 안 맞고 제자리에 남는다).
/// </summary>
[RequireComponent(typeof(Locomotion), typeof(Fighter), typeof(CharacterStateMachine))]
public class Health : MonoBehaviour, IHittable
{
    [KoreanLabel("캐릭터 스탯")]
    public CharacterStatData characterStatData;

    public int CurrentHealth { get; private set; }
    public int MaxHealth => characterStatData != null ? characterStatData.maxHealth : 100;

    /// <summary>체력이 0 이하가 된 순간 한 번 발생.</summary>
    public event Action OnDeath;

    Locomotion locomotion;
    Fighter fighter;
    CharacterStateMachine stateMachine;
    bool dead;

    void Awake()
    {
        locomotion = GetComponent<Locomotion>();
        fighter = GetComponent<Fighter>();
        stateMachine = GetComponent<CharacterStateMachine>();

        if (characterStatData == null)
            Debug.LogWarning($"{name}: characterStatData가 없어 기본 체력(100)을 사용합니다.");

        CurrentHealth = MaxHealth;
    }

    public void OnHit(HitData hit)
    {
        if (dead) return;

        CurrentHealth -= hit.Damage;

        fighter.CancelAttack();
        Vector3 knockback = hit.ResolveKnockbackVelocity(!locomotion.IsGrounded);
        locomotion.ApplyKnockback(knockback, hit.GroundSlideDeceleration);
        stateMachine.Interrupt();

        if (CurrentHealth <= 0)
        {
            dead = true;
            Debug.Log($"{name} 사망");
            OnDeath?.Invoke();
        }
    }
}
