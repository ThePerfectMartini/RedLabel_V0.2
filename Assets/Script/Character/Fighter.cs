using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 한 번에 하나의 <see cref="AttackData"/>를 실행하는 컴포넌트: 쿨타임 게이트, 콤보 체인 진행, 캔슬 윈도우,
/// 타격 판정(Physics.OverlapSphere → IHittable). "지금 공격 중인가"의 권한은 CharacterStateMachine이 갖고
/// (CurrentState == Attack), Fighter는 어떤 공격 데이터인지와 다음 단계로 언제 넘길지를 관리한다.
///
/// 입력 시점(TryAttack)과 판정 시점(OnAttackHitFrame)이 분리돼 있다 — 판정은 공격 클립의 Animation Event가
/// 부르는 타이밍에 그 시점의 위치/방향으로 수행된다.
/// </summary>
[RequireComponent(typeof(Locomotion), typeof(CharacterStateMachine))]
public class Fighter : MonoBehaviour, IAttackRangeDebugInfo
{
    [KoreanLabel("공격 대상 레이어")]
    [Tooltip("이 캐릭터가 때릴 대상의 Layer. 플레이어라면 Enemy, 적이라면 Player.")]
    public LayerMask targetLayer;

    Locomotion locomotion;
    CharacterStateMachine stateMachine;

    AttackData currentAttack;
    float attackTimer;      // 현재 공격 클립의 남은 지속시간
    float cooldownUntil;    // 이 시각 전에는 새 콤보를 시작할 수 없다
    bool comboBuffered;     // 지속시간 안에 같은 공격이 다시 들어왔는지 (다음 단계 예약)
    AttackData comboStarter; // 이번 콤보를 시작한 공격. "같은 공격 재입력"과 "다른 공격 = 캔슬"을 가른다
    bool hasFiredHitFrame;   // 현재 공격이 타격 프레임을 지났는지. 다른 공격으로의 캔슬은 이후에만 허용

    /// <summary>타격 판정(OnAttackHitFrame)이 실제로 일어난 시점에 발생. 디버그 기즈모가 구독.</summary>
    public event Action OnAttackHitFrameFired;

    // 에셋당 한 번만 경고하기 위한 목록 (StartAttack은 콤보 단계마다 호출되므로).
    static readonly HashSet<AttackData> warned = new HashSet<AttackData>();

    // 한 번의 타격 판정에서 이미 맞힌 대상. 대상이 콜라이더를 여러 개 가지면 OverlapSphere가
    // 그 수만큼 반환하는데, 그대로 두면 OnHit이 여러 번 불려 마지막 호출이 넉백을 덮어쓴다.
    readonly HashSet<IHittable> hitThisScan = new HashSet<IHittable>();

    // ===== 외부가 읽는 정보 (IAttackRangeDebugInfo 포함) =====
    // null 가드가 있는 이유: AttackRangeGizmo.OnDrawGizmos는 플레이 중이 아닐 때도 돌아서
    // Awake 전(참조 미해결) 상태로 이 프로퍼티들을 읽는다.

    public bool IsAttacking => stateMachine != null && stateMachine.CurrentState == CharacterState.Attack;

    /// <summary>이 공격이 이동 입력을 허용하는지. Actor가 이동 잠금 계산에 쓴다.</summary>
    public bool MovementAllowed => !IsAttacking || currentAttack == null || currentAttack.AllowsPlayerMovement;

    /// <summary>공격 중 적용할 이동 속도 배율. 공격 중이 아니면 1.</summary>
    public float MoveSpeedMultiplier => IsAttacking && currentAttack != null ? currentAttack.MoveSpeedMultiplier : 1f;

    public Vector3 FacingDir => locomotion != null ? locomotion.FacingDir : Vector3.right;
    public float AttackRange => currentAttack != null ? currentAttack.attackRange : 0f;
    public float AttackRadius => currentAttack != null ? currentAttack.attackRadius : 0f;

    void Awake()
    {
        locomotion = GetComponent<Locomotion>();
        stateMachine = GetComponent<CharacterStateMachine>();
    }

    // ===== 진입점 =====

    /// <summary>
    /// 공격 의도 처리. 세 갈래:
    /// 1. 공격 중이 아니면 requested로 콤보를 새로 시작(CanAct + 쿨타임 통과 시).
    /// 2. 공격 중인데 <b>다른</b> 공격이면 그 공격이 캔슬 권한을 갖고 상대가 타격 프레임을 지났을 때만 즉시 캔슬.
    /// 3. 공격 중이면서 <b>같은</b> 공격이면 콤보 진행으로 보고 버퍼링만(실제 전환은 클립 끝에서).
    /// </summary>
    public void TryAttack(AttackData requested)
    {
        if (requested == null) return;

        if (!IsAttacking)
        {
            if (!stateMachine.CanAct) return;
            if (Time.time < cooldownUntil) return;
            comboStarter = requested;
            StartAttack(requested);
            return;
        }

        if (requested != comboStarter)
        {
            // 캔슬 윈도우 = 타격 프레임 ~ 클립 끝. 캔슬 권한은 특정 공격만 갖는 특권(canCancelOtherAttacks).
            if (!requested.canCancelOtherAttacks) return;
            if (!hasFiredHitFrame) return;
            comboStarter = requested;
            StartAttack(requested);
            return;
        }

        if (currentAttack != null && currentAttack.nextAttack != null)
            comboBuffered = true;
    }

    /// <summary>Actor가 매 프레임 호출. 공격 중이면 지속시간을 재고, 끝나면 다음 단계로 잇거나 콤보를 종료한다.</summary>
    public void Tick(float deltaTime)
    {
        if (!IsAttacking) return;

        attackTimer -= deltaTime;
        if (attackTimer <= 0f)
            AdvanceOrEnd();
    }

    /// <summary>공격 클립의 타격 프레임 Animation Event(AnimationEventRelay 경유)가 호출.</summary>
    public void OnAttackHitFrame()
    {
        if (currentAttack == null) return;

        PerformHitScan();
        hasFiredHitFrame = true; // 이 시점부터 다른 공격으로 캔슬 가능
        OnAttackHitFrameFired?.Invoke();
    }

    /// <summary>피격 시 Health가 호출. 진행 중이던 공격의 뒷정리만 한다(상태 전이는 CharacterStateMachine.Interrupt 담당).</summary>
    public void CancelAttack()
    {
        attackTimer = 0f;
        comboBuffered = false;
        comboStarter = null;
        hasFiredHitFrame = false;
        locomotion.StopGroundSlide();
    }

    // ===== 내부 =====

    void StartAttack(AttackData data)
    {
        locomotion.StopGroundSlide(); // 이전 Impulse 돌진의 잔여 슬라이드 정리
        comboBuffered = false;
        hasFiredHitFrame = false;
        currentAttack = data;
        attackTimer = data.ResolveDuration();
        cooldownUntil = Time.time + data.attackCooldown;

        WarnIfMisconfigured(data);

        stateMachine.EnterAttack(data.attackClip);
        data.ApplySelfMovement(locomotion, locomotion.FacingDir); // 돌진 등
    }

    void AdvanceOrEnd()
    {
        AttackData next = comboBuffered && currentAttack != null ? currentAttack.nextAttack : null;

        if (next != null)
        {
            StartAttack(next);
        }
        else
        {
            comboBuffered = false;
            comboStarter = null;
            hasFiredHitFrame = false;
            locomotion.StopGroundSlide();
            stateMachine.ExitAttack();
        }
    }

    void PerformHitScan()
    {
        Vector3 dir = locomotion.FacingDir.normalized;
        Vector3 center = transform.position + dir * currentAttack.attackRange;
        Collider[] hits = Physics.OverlapSphere(center, currentAttack.attackRadius, targetLayer);

        hitThisScan.Clear();
        foreach (Collider col in hits)
        {
            IHittable hittable = col.GetComponentInParent<IHittable>();
            if (hittable == null) continue;
            if (!hitThisScan.Add(hittable)) continue; // 콜라이더 여러 개인 대상 중복 타격 방지

            // 맞은 대상이 공중인지는 대상만 아는 정보라 지상용/공중용 넉백을 둘 다 실어 보내고 대상이 고른다.
            HitData hit = new HitData
            {
                Damage = currentAttack.damage,
                KnockbackVelocity = dir * currentAttack.knockbackForce + Vector3.up * currentAttack.launchForce,
                AirborneKnockbackVelocity = dir * currentAttack.airborneKnockbackForce + Vector3.up * currentAttack.airborneLaunchForce,
                GroundSlideDeceleration = currentAttack.groundSlideDeceleration,
                Attacker = gameObject
            };
            hittable.OnHit(hit);
        }
    }

    static void WarnIfMisconfigured(AttackData data)
    {
        if (!warned.Add(data)) return;

        if (data.ResolveDuration() <= 0f)
            Debug.LogWarning($"{data.name}: 공격 클립도 '지속시간 직접 지정'도 없어 공격이 시작하자마자 끝납니다.", data);
        if (!data.HasHitFrameEvent())
            Debug.LogWarning($"{data.name}: 클립에 'OnAttackHitFrame' Animation Event가 없어 타격 판정이 나가지 않고 " +
                             "이 공격으로 다른 공격을 캔슬할 수도 없습니다.", data);
    }
}
