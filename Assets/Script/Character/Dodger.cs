using System;
using UnityEngine;

/// <summary>
/// 회피(대시) 하나를 실행하는 컴포넌트. Fighter가 공격을 다루는 것과 대칭이다:
/// 입력은 Actor가 <see cref="TryDodge"/>로 넘기고, 상태(Attack처럼 sticky한 <see cref="CharacterState.Dodge"/>)의
/// 권한은 CharacterStateMachine이 가지며, 여기서는 "언제 끝낼지"(타이머)와 "언제 무적인지"만 관리한다.
///
/// 회피는 <b>지상에서만</b> 시작할 수 있고, 진행 중이던 공격을 캔슬한다. 점프 준비/공중/피격 여파
/// (Stun·Airborne·넉다운)는 캔슬하지 못한다 — 그 판정은 <c>stateMachine.CanAct</c>(고정·Stun·Airborne 제외)에
/// Attack만 다시 더한 것과 정확히 같다.
///
/// 무적은 회피 시작~끝 전체가 아니라 시작 후 <see cref="invulnerableStartDelay"/>부터
/// <see cref="invulnerableDuration"/> 동안만이다(앞뒤로 취약 프레임). Health.OnHit이 <see cref="IsInvulnerable"/>를
/// 보고 그 프레임의 피격을 통째로 무시한다.
///
/// [준비물] Animator에 "Dodge" State(이름은 CharacterState.Dodge와 일치). 없으면 CharacterStateMachine이 경고 1회.
/// </summary>
[RequireComponent(typeof(Locomotion), typeof(Fighter), typeof(CharacterStateMachine))]
public class Dodger : MonoBehaviour
{
    [KoreanLabel("회피 지속시간(초)")]
    [Tooltip("회피 모션 전체 길이. 이 시간이 지나면 물리 파생 상태로 돌아온다.")]
    public float dodgeDuration = 0.4f;

    [KoreanLabel("무적 시작 지연(초)")]
    [Tooltip("회피 시작 후 이 시간이 지나야 무적이 된다. 0이면 첫 프레임부터 무적.")]
    public float invulnerableStartDelay = 0.05f;

    [KoreanLabel("무적 지속시간(초)")]
    [Tooltip("무적이 유지되는 시간. (시작 지연 + 이 값)이 회피 지속시간보다 짧아야 뒤쪽 취약 프레임이 생긴다.")]
    public float invulnerableDuration = 0.25f;

    [KoreanLabel("재사용 대기시간(초)")]
    [Tooltip("회피가 끝난 뒤 다시 회피할 수 있기까지의 시간.")]
    public float cooldown = 0.6f;

    [KoreanLabel("대시 속도")]
    [Tooltip("회피 시작 시 바라보는 방향으로 실리는 초기 수평 속도.")]
    public float dashSpeed = 12f;

    [KoreanLabel("대시 감속(초당)")]
    [Tooltip("대시 속도가 줄어드는 비율. 넉백 그라운드 슬라이드와 같은 감속 경로를 쓴다.")]
    public float dashDeceleration = 30f;

    Locomotion locomotion;
    Fighter fighter;
    CharacterStateMachine stateMachine;

    float elapsed;        // 이번 회피가 시작된 뒤 지난 시간 (무적 창 계산용)
    float cooldownUntil;  // 이 시각 전에는 새 회피를 시작할 수 없다

    /// <summary>
    /// 회피 무적으로 공격 하나를 흘려낸 순간 발생. 인자는 공격자 GameObject(없으면 null).
    /// 반격 / 슬로우모션 / 게이지 충전 같은 "회피 성공 보상"을 나중에 여기 물린다.
    /// </summary>
    public event Action<GameObject> OnDodgeSuccess;

    /// <summary>지금 회피 중인지. 별도 플래그 없이 상태로 판단한다(Fighter.IsAttacking과 같은 방식).</summary>
    public bool IsDodging => stateMachine != null && stateMachine.CurrentState == CharacterState.Dodge;

    /// <summary>지금 공격 판정을 무시해야 하는지. Health.OnHit이 맨 앞에서 읽는다.</summary>
    public bool IsInvulnerable =>
        IsDodging &&
        elapsed >= invulnerableStartDelay &&
        elapsed < invulnerableStartDelay + invulnerableDuration;

    void Awake()
    {
        locomotion = GetComponent<Locomotion>();
        fighter = GetComponent<Fighter>();
        stateMachine = GetComponent<CharacterStateMachine>();
    }

    /// <summary>
    /// 회피 의도 처리. Actor가 <c>intent.WantsDodge</c>일 때 호출한다.
    /// 이미 회피 중이거나, 쿨다운이 안 지났거나, 공중이거나, 회피로 캔슬할 수 없는 상태면 무시한다.
    /// </summary>
    public void TryDodge()
    {
        if (IsDodging) return;
        if (Time.time < cooldownUntil) return;
        if (!locomotion.IsGrounded) return;

        // 회피가 캔슬할 수 있는 것 = 평상시 행동 가능 상태 + 공격.
        // (JumpStart·InAir·Stun·Airborne·넉다운은 CanAct가 이미 걸러낸다.)
        if (!stateMachine.CanAct && stateMachine.CurrentState != CharacterState.Attack)
            return;

        StartDodge();
    }

    /// <summary>
    /// 무적 프레임 중 피격을 무시할 때 <see cref="Health"/>가 호출한다 — 즉 회피가 실제로 공격을 흘려낸 순간.
    /// 지금은 콘솔 로그만. 파이프라인(<see cref="OnDodgeSuccess"/>)은 뚫어놨으니 나중에 보상 동작을 구독으로 붙이면 된다.
    /// </summary>
    public void NotifyDodgedAttack(GameObject attacker)
    {
        Debug.Log($"{name}: 회피 성공" + (attacker != null ? $" (공격자: {attacker.name})" : ""));
        OnDodgeSuccess?.Invoke(attacker);
    }

    /// <summary>Actor가 매 프레임 호출. 회피 중이면 타이머를 재고, 다 되면 물리 파생 상태로 되돌린다.</summary>
    public void Tick(float deltaTime)
    {
        if (!IsDodging) return;

        elapsed += deltaTime;
        if (elapsed >= dodgeDuration)
        {
            locomotion.StopGroundSlide(); // 남은 대시 속도 제거
            stateMachine.ExitDodge();
        }
    }

    void StartDodge()
    {
        fighter.CancelAttack();       // 진행 중이던 공격 뒷정리(상태 전이는 EnterDodge가 담당)
        locomotion.StopGroundSlide(); // 이전 슬라이드 잔재 정리

        elapsed = 0f;
        cooldownUntil = Time.time + dodgeDuration + cooldown;

        stateMachine.EnterDodge();
        // 바라보는 방향으로 대시. y=0이라 ApplyKnockback의 그라운드 슬라이드 경로를 타고,
        // dashDeceleration으로 감속한다(Impulse 공격의 ApplySelfMovement와 같은 방식).
        locomotion.ApplyKnockback(locomotion.FacingDir * dashSpeed, dashDeceleration);
    }
}
