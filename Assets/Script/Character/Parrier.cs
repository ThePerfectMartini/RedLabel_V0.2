using System;
using UnityEngine;

/// <summary>
/// 패링(반격자세) 하나를 실행하는 컴포넌트. Dodger와 같은 틀이다: 입력은 Actor가 <see cref="TryParry"/>로 넘기고,
/// sticky 상태(<see cref="CharacterState.Parry"/> / <see cref="CharacterState.ParrySuccess"/>)의 권한은
/// CharacterStateMachine이, Parrier는 타이머와 판정 창만 관리한다.
///
/// <b>타이트한 창 모델:</b> 반격자세는 [시동 → 판정 창 → 후딜] 세 구간으로 나뉜다.
/// 판정 창(<see cref="IsParryWindowActive"/>) 안에서 적 공격이 닿아야만 패링 성공이고, 시동·후딜에 맞으면 정상 피격이다.
///
/// <b>적 스턴은 기존 피격 경로를 재사용한다:</b> 성공 시 공격자의 <see cref="IHittable.OnHit"/>을 데미지 0짜리
/// <see cref="HitData"/>로 호출한다 → 공격자는 CancelAttack + 그라운드 슬라이드(Stun) + Interrupt를 그대로 받는다.
/// <see cref="staggerDeceleration"/>을 일반 피격보다 낮게 둬서 스턴이 더 길다 = 확정 반격 기회.
///
/// [준비물] Animator에 "Parry" / "ParrySuccess" State. 없으면 CharacterStateMachine이 경고 1회, 로직은 정상.
/// </summary>
[RequireComponent(typeof(Locomotion), typeof(CharacterStateMachine))]
public class Parrier : MonoBehaviour
{
    [KoreanLabel("반격자세 지속시간(초)")]
    [Tooltip("자세 전체 길이. 시동 + 판정 창 + 후딜을 모두 포함한다.")]
    public float parryStanceDuration = 0.4f;

    [KoreanLabel("판정 시작 지연(초)")]
    [Tooltip("자세 시작 후 이만큼 지나야 패링 판정이 열린다. 이 전에 맞으면 정상 피격(시동 취약).")]
    public float activeStartDelay = 0.06f;

    [KoreanLabel("판정 창 길이(초)")]
    [Tooltip("실제로 패링되는 구간. 창이 끝나고 자세가 끝날 때까지는 다시 취약(후딜 처벌).")]
    public float activeDuration = 0.15f;

    [KoreanLabel("성공 경직(초)")]
    [Tooltip("패링 성공 시 ParrySuccess 상태로 이 시간만큼 입력이 잠긴 뒤 복귀한다.")]
    public float successLockDuration = 0.3f;

    [KoreanLabel("재사용 대기시간(초)")]
    [Tooltip("반격자세가 끝난 뒤 다시 패링할 수 있기까지의 시간.")]
    public float cooldown = 0.6f;

    [KoreanLabel("적 밀치는 힘")]
    [Tooltip("패링당한 적을 밀어내는 수평 속도. y=0이라 대상 쪽에서 그라운드 슬라이드(Stun)로 처리된다.")]
    public float staggerForce = 5f;

    [KoreanLabel("적 스턴 감속(초당)")]
    [Tooltip("낮을수록 적 슬라이드가 오래 유지된다 = 스턴이 길다. 일반 피격(약 30)보다 훨씬 낮게 둬서 확정 반격 기회를 준다.")]
    public float staggerDeceleration = 8f;

    Locomotion locomotion;
    CharacterStateMachine stateMachine;

    float elapsed;        // 지금 상태(Parry 또는 ParrySuccess)에 들어온 뒤 지난 시간
    float cooldownUntil;  // 이 시각 전에는 새 패링을 시작할 수 없다

    /// <summary>패링 성공 순간 발생. 인자 = 패링당한 공격자(없으면 null). 이펙트 · SE · 자동 반격 등을 나중에 여기 물린다.</summary>
    public event Action<GameObject> OnParrySuccess;

    bool IsParrying => stateMachine != null && stateMachine.CurrentState == CharacterState.Parry;
    bool IsParrySuccess => stateMachine != null && stateMachine.CurrentState == CharacterState.ParrySuccess;

    /// <summary>지금 패링 판정 창 안인지. Health.OnHit이 읽어 이 프레임의 피격을 패링으로 처리할지 정한다.</summary>
    public bool IsParryWindowActive =>
        IsParrying &&
        elapsed >= activeStartDelay &&
        elapsed < activeStartDelay + activeDuration;

    void Awake()
    {
        locomotion = GetComponent<Locomotion>();
        stateMachine = GetComponent<CharacterStateMachine>();
    }

    /// <summary>
    /// 패링 의도 처리. Actor가 <c>intent.WantsParry</c>일 때 호출한다.
    /// 이미 패링 중이거나, 쿨다운이 안 지났거나, 공중이거나, 행동 불가 상태(공격 · 피격 여파 등)면 무시한다.
    /// 회피와 달리 자기 공격을 캔슬하지 않는다 — 중립/이동에서만 발동.
    /// </summary>
    public void TryParry()
    {
        if (IsParrying || IsParrySuccess) return;
        if (Time.time < cooldownUntil) return;
        if (!locomotion.IsGrounded) return;
        if (!stateMachine.CanAct) return;

        elapsed = 0f;
        cooldownUntil = Time.time + parryStanceDuration + cooldown;
        stateMachine.EnterParry();
    }

    /// <summary>Actor가 매 프레임 호출. 반격자세 / 성공 연출의 타이머를 재고, 다 되면 물리 파생 상태로 되돌린다.</summary>
    public void Tick(float deltaTime)
    {
        if (IsParrying)
        {
            elapsed += deltaTime;
            if (elapsed >= parryStanceDuration)
                stateMachine.ExitParry();
        }
        else if (IsParrySuccess)
        {
            elapsed += deltaTime;
            if (elapsed >= successLockDuration)
                stateMachine.ExitParry();
        }
    }

    /// <summary>
    /// 판정 창 안에서 공격이 닿았을 때 Health.OnHit이 호출한다(플레이어 쪽 피격은 Health가 무시하고 return).
    /// 성공 연출로 전환하고, 공격자를 스턴시킨다.
    /// </summary>
    public void NotifyParried(GameObject attacker)
    {
        elapsed = 0f; // ParrySuccess 타이머 새로 시작
        stateMachine.EnterParrySuccess();

        if (attacker != null)
        {
            // 적을 플레이어 반대쪽으로 밀어낸다. y=0이라 공격자 Health가 그라운드 슬라이드로 처리 → Stun.
            Vector3 away = attacker.transform.position - transform.position;
            away.y = 0f;
            away = away.sqrMagnitude > 0.0001f ? away.normalized : locomotion.FacingDir;

            Vector3 push = away * staggerForce;

            IHittable hittable = attacker.GetComponent<IHittable>();
            if (hittable != null)
            {
                hittable.OnHit(new HitData
                {
                    Damage = 0,
                    KnockbackVelocity = push,
                    AirborneKnockbackVelocity = push,
                    GroundSlideDeceleration = staggerDeceleration,
                    Attacker = gameObject,
                });
            }
        }

        OnParrySuccess?.Invoke(attacker);
    }
}
