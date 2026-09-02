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
    Dodger dodger;
    Parrier parrier;
    bool dead;

    void Awake()
    {
        locomotion = GetComponent<Locomotion>();
        fighter = GetComponent<Fighter>();
        stateMachine = GetComponent<CharacterStateMachine>();
        dodger = GetComponent<Dodger>();   // 회피 무적 프레임 판정용. 없어도 됨(플레이어만 회피함)
        parrier = GetComponent<Parrier>(); // 패링 판정 창 판정용. 없어도 됨

        if (characterStatData == null)
            Debug.LogWarning($"{name}: characterStatData가 없어 기본 체력(100)을 사용합니다.");

        CurrentHealth = MaxHealth;
    }

    public void OnHit(HitData hit)
    {
        if (dead) return;

        // 패링 판정 창 중이면 이 타격을 통째로 무시하고 Parrier에 넘긴다 — Parrier가 성공 연출 +
        // 공격자를 스턴시킨다(공격자의 OnHit을 데미지 0으로 다시 호출). Parry / Dodge는 서로 배타적 상태라 순서는 무관.
        if (parrier != null && parrier.IsParryWindowActive)
        {
            parrier.NotifyParried(hit.Attacker);
            return;
        }

        // 회피 무적 프레임 중이면 이 타격을 통째로 무시한다 — 데미지·넉백·경직 전부.
        // 흘려낸 사실은 Dodger에 알린다("회피 성공" 훅 — 반격/슬로우모션 등을 나중에 여기 붙임).
        if (dodger != null && dodger.IsInvulnerable)
        {
            dodger.NotifyDodgedAttack(hit.Attacker);
            return;
        }

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
