using System;
using UnityEngine;

/// <summary>
/// 적 전용 조율 스크립트. PlayerController와 같은 자리에 있는 컴포넌트로,
/// MovementCore/CombatCore/CharacterStateMachine을 조립하고 애니메이션·피격을 연결한다.
///
/// PlayerController와의 유일한 차이는 "무엇을 할지"를 정하는 주체다.
/// - PlayerController: InputAction(키보드/마우스)에서 의도를 받는다.
/// - EnemyController: 같은 오브젝트에 붙은 IEnemyBrain에게 매 프레임 물어본다(EnemyIntent).
/// 그 뒤의 처리(이동/공격/점프/상태 전환)는 둘이 동일하다.
///
/// [준비물]
/// - 씬 어딘가(맵 오브젝트)에 MapBounds 컴포넌트 (자동으로 찾아서 참조함)
/// - targetLayer는 이 적이 공격할 대상(보통 Player)의 Layer로 지정
/// - 애니메이션을 쓰려면 CharacterAnimatorBridge를 같은 오브젝트에,
///   AttackAnimationEventReceiver / JumpAnimationEventReceiver를 Animator가 붙은 자식 오브젝트에 추가
/// - AI는 IEnemyBrain을 구현한 컴포넌트를 같은 오브젝트에 붙이면 자동으로 인식된다.
///   없어도 에러 없이 동작하며, 그 경우 제자리에 가만히 서 있는다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class EnemyController : MonoBehaviour, IHittable, IStateMachineOwner, IAttackEventListener, IAttackClipSource, IJumpEventListener, IKnockdownEventListener, IAttackRangeDebugInfo
{
    [Header("공격 대상 레이어")]
    [KoreanLabel("대상 레이어")]
    [Tooltip("이 적이 때릴 대상의 Layer. 보통 Player.")]
    public LayerMask targetLayer;

    [Header("스탯 데이터")]
    [KoreanLabel("캐릭터 스탯")]
    public CharacterStatData characterStatData;
    [KoreanLabel("이동 스탯")]
    public MovementStatData movementStatData;
    [KoreanLabel("공격 1 (콤보 시작)")]
    public AttackData firstAttackData;

    readonly MovementCore movement = new MovementCore();
    readonly CombatCore combat = new CombatCore();
    readonly CharacterStateMachine stateMachine = new CharacterStateMachine();

    /// <summary>CharacterAnimatorBridge 등 외부에서 현재 상태를 읽거나 OnStateChanged를 구독할 때 사용.</summary>
    public CharacterStateMachine StateMachine => stateMachine;

    /// <summary>CharacterAnimatorBridge가 구독해서 콤보 단계별 클립을 CrossFade하는 데 사용.</summary>
    public event Action<AnimationClip> OnAttackClipChanged;

    /// <summary>디버그 표시(AttackRangeGizmo 등) 등 외부에서 실제 타격 판정이 일어난 시점을 구독할 때 사용.</summary>
    public event Action OnAttackHitFrameFired;

    IEnemyBrain brain;

    bool isFacingRight = true; // 스프라이트 좌우 반전 + 공격 판정 방향(FacingDir)의 유일한 기준.
    int currentHealth;

    SpriteRenderer spriteRenderer;

    // 공격 애니메이션 재생 중임을 나타내는 타이머. combat.AttackDuration(공격 클립 길이)만큼 유지된다.
    float attackStateTimer;

    // 현재 공격이 재생되는 동안 공격 의도가 다시 들어왔는지 여부. true면 현재 공격이 끝나는 순간
    // CurrentAttack.nextAttack으로 이어간다 (없으면 콤보가 거기서 끝남).
    bool comboInputBuffered;

    // 현재 그라운드 슬라이드가 피격이 아니라 자기 Impulse 공격(돌진)으로 인한 것인지 구분하는 플래그.
    bool selfImpulseActive;

    // 점프 준비(JumpStart) 애니메이션 재생 중인지. 이 동안엔 아직 뜨지 않고 이동도 막힌다.
    bool isJumpWindingUp;

    // 점프 의도가 들어온 그 순간의 좌우 이동 속도. 준비 동작 중엔 이동이 잠겨 movement.Velocity.x가
    // 0으로 지워지므로, 실제로 뜨는 순간(OnJumpLaunchFrame)에 이 값을 다시 넣어줘야 그 방향으로
    // 점프한 것처럼 보인다. 의도가 들어온 순간 이동 입력이 없었으면 0 -> 제자리 점프.
    float jumpHorizontalVelocity;

    // 착지 경직(JumpLand) 재생 중인지. 이 동안에도 이동/공격/점프가 막힌다.
    bool isLandRecovering;

    // OnJumpLaunchFrame으로 실제 점프가 발사된 뒤, 아직 착지 처리를 안 한 상태인지.
    // "공중에 있다가 땅에 닿았다"만으로 착지 경직을 트리거하면 스폰 위치가 바닥보다 살짝 위일 때의
    // 첫 낙하까지 점프 착지로 오인해버린다. 그래서 실제 점프 발사(OnJumpLaunchFrame)를 거친
    // 경우에만 착지 경직으로 이어지도록 이 플래그로 구분한다.
    bool isJumpAirborne;

    // 넉백으로 공중에 떴다가 착지해 쓰러져 있는(Landed) 중인지. 이 동안엔 이동/공격/점프가 막힌다.
    // Landed 클립의 Animation Event(OnKnockdownGetUpStartFrame)가 호출되면 isGettingUp으로 넘어간다.
    bool isKnockdownLanded;

    // 쓰러진 상태에서 몸을 일으키는(GetUp) 중인지. 이 동안에도 이동/공격/점프가 막힌다.
    // GetUp 클립의 Animation Event(OnKnockdownGetUpEndFrame)가 호출되면 꺼지고 다시 행동 가능해진다.
    bool isGettingUp;

    // ===== IEnemyBrain이 판단에 사용할 읽기 전용 정보 =====

    /// <summary>현재 상태. Brain이 "지금 공격 중이라 움직이면 안 된다" 같은 판단에 사용.</summary>
    public CharacterState CurrentState => stateMachine.CurrentState;

    /// <summary>MovementCore가 관리하는 현재 위치. transform.position과 같은 값이지만 이쪽이 원본이다.</summary>
    public Vector3 Position => movement.Position;

    /// <summary>지상에 있는지. 점프/공격 가능 여부 판단용.</summary>
    public bool IsGrounded => movement.IsGrounded;

    /// <summary>공격 판정이 나가는 방향. 스프라이트가 좌우로만 뒤집히는 것과 맞춰 좌/우 둘 중 하나만 나온다.</summary>
    public Vector3 FacingDir => isFacingRight ? Vector3.right : Vector3.left;

    /// <summary>현재 CombatCore에 적용된 공격의 사거리. AttackRangeGizmo 등 디버그용.</summary>
    public float AttackRange => combat.AttackRange;

    /// <summary>현재 CombatCore에 적용된 공격의 판정 반경. AttackRangeGizmo 등 디버그용.</summary>
    public float AttackRadius => combat.AttackRadius;

    /// <summary>공격 애니메이션이 재생 중인지 (콤보 대기 포함, 타격 판정 여부와는 무관).</summary>
    public bool IsAttacking => attackStateTimer > 0f;

    /// <summary>지금 새 행동(이동/공격/점프)을 시작할 수 있는 상태인지. Brain이 참고용으로 읽는다.</summary>
    public bool CanAct => attackStateTimer <= 0f
        && !isJumpWindingUp
        && !isLandRecovering
        && !isKnockdownLanded
        && !isGettingUp
        && !movement.IsKnockedBackAirborne
        && !movement.IsGroundSliding;

    void Awake()
    {
        movement.Position = transform.position;

        brain = GetComponent<IEnemyBrain>();
        // Brain은 없어도 정상 동작한다(가만히 서 있음). AI는 나중에 붙이는 것이 전제라 경고하지 않는다.

        spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (characterStatData == null)
            Debug.LogWarning($"{name}: characterStatData가 연결되지 않아 기본 체력(100)을 사용합니다.");

        currentHealth = characterStatData != null ? characterStatData.maxHealth : 100;

        movement.Init(movementStatData);
        combat.Init(firstAttackData);

        // 오브젝트에 이미 붙어있는 콜라이더를 그대로 인식 (타입 무관, 강제로 추가하지 않음)
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Vector3 extents = col.bounds.extents;
            movement.BoundaryRadius = Mathf.Max(extents.x, extents.z);
            movement.GroundOffset = extents.y; // 피벗이 콜라이더 중심에 있다고 가정
        }
        else
        {
            Debug.LogWarning($"{name}: Collider가 없어 기본 BoundaryRadius/GroundOffset을 사용합니다.");
        }
    }

    void Update()
    {
        // 플레이어의 키 입력 자리. Brain이 없으면 아무 의도도 없는 것으로 처리한다.
        EnemyIntent intent = brain != null
            ? brain.Think(this, Time.deltaTime)
            : EnemyIntent.None;

        if (intent.WantsAttack)
            TryStartAttack();

        if (intent.WantsJump)
            TryStartJump(intent.MoveInput);

        // 현재 공격이 이동을 막는 공격(Locked/Impulse)이거나, 점프 준비/착지 경직 중이면 이동 의도를 무시한다.
        bool movementLockedByAttack = attackStateTimer > 0f
            && combat.CurrentAttack != null
            && !combat.CurrentAttack.AllowsPlayerMovement;
        // Stun(얻어맞아 미끄러지는 중)과 Airborne(넉백으로 뜬 중)도 마찬가지로 막는다. 실제 이동 자체는
        // MovementCore.SetMoveInput이 내부적으로 이미 무시하지만, 여기서 막지 않으면 Brain의 이동 의도만으로
        // isFacingRight(바라보는 방향)가 계속 바뀌어버린다.
        bool movementLocked = movementLockedByAttack || isJumpWindingUp || isLandRecovering || isKnockdownLanded || isGettingUp
            || movement.IsKnockedBackAirborne || (movement.IsGroundSliding && !selfImpulseActive);
        Vector2 effectiveMoveInput = movementLocked ? Vector2.zero : intent.MoveInput;

        movement.SetMoveInput(effectiveMoveInput);

        if (movement.IsGrounded && effectiveMoveInput.sqrMagnitude > 0.01f)
            SetFacing(new Vector3(effectiveMoveInput.x, 0f, effectiveMoveInput.y));

        Bounds bounds = MapBounds.Instance != null
            ? MapBounds.Instance.Bounds
            : new Bounds(Vector3.zero, Vector3.one * 1000f); // 씬에 MapBounds가 없을 때의 안전장치

        // Tick이 바닥 충돌을 처리하면서 IsGrounded를 바꾸므로, 착지 순간을 잡으려면 그 직전 값을 기억해둬야 한다.
        // isJumpAirborne(실제 점프 발사 여부)로 판단하지, "공중에 있었는가"만으로 판단하지 않는다.
        // 안 그러면 스폰 위치가 바닥보다 살짝 위일 때의 첫 낙하까지 점프 착지로 오인해서
        // JumpLand 상태에 갇혀버린다.
        bool wasJumpAirborne = isJumpAirborne;

        // 넉백으로 공중에 뜬 상태(Airborne)가 이번 Tick에 끝나는지(착지하는지) 판단하려면
        // Tick 이전 값을 기억해둬야 한다. Tick 안에서 바운스가 남아있는 동안은 계속 true이므로
        // 여러 번 튕기다 최종 착지하는 그 프레임에만 한 번 Landed로 전환된다.
        bool wasKnockedBackAirborne = movement.IsKnockedBackAirborne;

        movement.Tick(Time.deltaTime, bounds);

        if (wasJumpAirborne && movement.IsGrounded)
        {
            isJumpAirborne = false;
            isLandRecovering = true;
        }

        if (wasKnockedBackAirborne && !movement.IsKnockedBackAirborne)
        {
            isKnockdownLanded = true;
        }

        transform.position = movement.Position;

        // 회전 대신 스프라이트만 수평 반전 (기본 스프라이트가 오른쪽을 보고 있다고 가정)
        if (spriteRenderer != null)
            spriteRenderer.flipX = !isFacingRight;

        if (attackStateTimer > 0f)
        {
            attackStateTimer -= Time.deltaTime;
            if (attackStateTimer <= 0f)
                AdvanceComboOrEnd();
        }

        UpdateCharacterState();
    }

    /// <summary>
    /// 바라볼 좌/우를 설정한다. 이동 중에는 자동으로 갱신되지만, 제자리에서 대상 쪽을 보게 하는 등
    /// Brain이 직접 지정해야 할 때 호출한다. X 성분만 보고 판단한다 (위/아래로만 향하는 방향이 오면
    /// 부호가 없어 기존 좌우를 그대로 유지).
    /// </summary>
    public void SetFacing(Vector3 direction)
    {
        if (Mathf.Abs(direction.x) > 0.01f)
            isFacingRight = direction.x > 0f;
    }

    /// <summary>
    /// 공격 의도 처리. 플레이어의 OnAttackPerformed와 동일한 규칙으로 콤보를 판단한다.
    /// </summary>
    void TryStartAttack()
    {
        // 점프 준비 동작/착지 경직/기상 중엔 공격을 무시한다. 여기서 공격이 시작되면 해당 클립이
        // 공격 클립으로 교체되어 OnJumpLaunchFrame / OnJumpLandEndFrame / 기상 이벤트가 영영 호출되지 않는다.
        if (isJumpWindingUp || isLandRecovering || isKnockdownLanded || isGettingUp) return;

        // 넉백으로 뜬 상태(Airborne)나 얻어맞아 미끄러지는 중(Stun)에는 공격 의도를 무시한다.
        // 자신의 Impulse 공격으로 인한 슬라이드(selfImpulseActive)는 Stun이 아니라 그 공격 자체의
        // 진행이므로 막지 않는다.
        if (movement.IsKnockedBackAirborne || (movement.IsGroundSliding && !selfImpulseActive)) return;

        bool isAttacking = attackStateTimer > 0f;

        if (!isAttacking)
        {
            // 콤보 시작(공격 1)만 쿨타임 체크를 한다. 콤보 도중 이어지는 공격은 현재 공격의
            // 지속시간이 곧 다음 공격을 받을 수 있는 시점이므로 별도 쿨타임 체크가 필요 없다.
            if (!combat.TryStartAttack(Time.time)) return;
            StartAttack(firstAttackData);
        }
        else if (combat.CurrentAttack != null && combat.CurrentAttack.nextAttack != null)
        {
            comboInputBuffered = true;
        }
        // 후속 공격이 없는 상태에서 재요청하면(마지막 콤보 단계 도중) 그냥 무시된다.
    }

    /// <summary>
    /// data로 CombatCore를 갈아끼우고 지속시간 타이머를 재생, 애니메이션 클립 변경을 알린다.
    /// 공격 1 시작과 콤보 진행(다음 공격으로 전환) 모두 이 메서드를 거친다.
    /// </summary>
    void StartAttack(AttackData data)
    {
        if (data == null)
        {
            Debug.LogWarning($"{name}: 공격 데이터가 없어 공격을 실행할 수 없습니다.");
            return;
        }

        combat.Init(data);
        attackStateTimer = combat.AttackDuration;
        OnAttackClipChanged?.Invoke(data.attackClip);
        // 실제 타격 판정은 공격 애니메이션 클립의 Animation Event -> OnAttackHitFrame()에서 수행된다.

        selfImpulseActive = false;
        data.ApplySelfMovement(movement, FacingDir);
        if (movement.IsGroundSliding)
            selfImpulseActive = true;
    }

    /// <summary>
    /// 공격 지속시간이 끝나는 프레임에 호출된다. 공격 의도가 버퍼링되어 있고 후속 공격이 있으면
    /// 그 공격으로 이어가고, 아니면 콤보를 끝낸다.
    /// </summary>
    void AdvanceComboOrEnd()
    {
        selfImpulseActive = false;
        AttackData next = comboInputBuffered ? combat.CurrentAttack?.nextAttack : null;
        comboInputBuffered = false;

        if (next != null)
            StartAttack(next); // 다음 공격이 Impulse면 이 안에서 selfImpulseActive가 다시 켜진다.
    }

    /// <summary>
    /// 공격 애니메이션 클립의 Animation Event(AttackAnimationEventReceiver 경유)가 호출한다.
    /// 의도가 들어온 시점이 아니라 이 시점의 위치/방향으로 실제 타격 판정을 수행한다.
    /// </summary>
    public void OnAttackHitFrame()
    {
        combat.PerformHitScan(transform.position, FacingDir, gameObject, targetLayer);
        OnAttackHitFrameFired?.Invoke();
    }

    /// <summary>
    /// 점프 의도 처리. 이 시점엔 아직 뜨지 않고 JumpStart(준비 동작) 재생만 시작한다.
    /// 실제로 뜨는 건 OnJumpLaunchFrame에서다. moveInputAtRequest는 점프 의도가 들어온 그 프레임의
    /// 이동 의도로, 좌우 성분이 있으면 그 방향으로 점프하고 없으면 제자리에서 뜬다.
    /// </summary>
    void TryStartJump(Vector2 moveInputAtRequest)
    {
        if (!movement.IsGrounded || isJumpWindingUp || isLandRecovering || isKnockdownLanded || isGettingUp) return;
        if (attackStateTimer > 0f) return; // 공격 중엔 점프 불가 (공격 클립이 재생 중인 JumpStart 클립을 덮어쓰는 것 방지)

        isJumpWindingUp = true;

        jumpHorizontalVelocity = Mathf.Abs(moveInputAtRequest.x) > 0.01f
            ? Mathf.Sign(moveInputAtRequest.x) * movement.MoveSpeed
            : 0f;
    }

    /// <summary>
    /// JumpStart 클립 안의 "발이 떨어지는" 프레임의 Animation Event(JumpAnimationEventReceiver 경유)가 호출한다.
    /// 이 시점에 비로소 실제로 위로 뜬다.
    /// </summary>
    public void OnJumpLaunchFrame()
    {
        isJumpWindingUp = false;
        isJumpAirborne = true;
        movement.Jump(jumpHorizontalVelocity);
    }

    /// <summary>
    /// JumpLand 애니메이션 클립의 Animation Event(JumpAnimationEventReceiver 경유)가 호출한다.
    /// 이 시점에 착지 경직이 풀린다.
    /// </summary>
    public void OnJumpLandEndFrame()
    {
        isLandRecovering = false;
    }

    /// <summary>
    /// Landed 클립의 "몸을 일으키기 시작하는" 프레임의 Animation Event(KnockdownAnimationEventReceiver 경유)가 호출한다.
    /// 이 시점에 쓰러진 상태(Landed)가 끝나고 일어나는 상태(GetUp)로 넘어간다.
    /// </summary>
    public void OnKnockdownGetUpStartFrame()
    {
        isKnockdownLanded = false;
        isGettingUp = true;
    }

    /// <summary>
    /// GetUp 클립의 "완전히 일어나는" 프레임의 Animation Event(KnockdownAnimationEventReceiver 경유)가 호출한다.
    /// 이 시점에 기상이 끝나고 다시 이동/공격/점프가 가능해진다.
    /// </summary>
    public void OnKnockdownGetUpEndFrame()
    {
        isGettingUp = false;
    }

    /// <summary>
    /// 매 프레임 현재 상황을 보고 캐릭터 상태를 판단해 반영한다.
    /// 우선순위는 PlayerController와 동일하다:
    /// Airborne(공중 넉백) > Landed(쓰러짐) > GetUp(기상) > Stun(그라운드 슬라이드) > Attack
    /// > InAir(공중) > JumpStart(준비) > JumpLand(착지) > Move/Idle.
    /// </summary>
    void UpdateCharacterState()
    {
        if (movement.IsKnockedBackAirborne)
        {
            stateMachine.SetState(CharacterState.Airborne);
        }
        else if (isKnockdownLanded)
        {
            stateMachine.SetState(CharacterState.Landed);
        }
        else if (isGettingUp)
        {
            stateMachine.SetState(CharacterState.GetUp);
        }
        else if (movement.IsGroundSliding && !selfImpulseActive)
        {
            stateMachine.SetState(CharacterState.Stun);
        }
        else if (attackStateTimer > 0f)
        {
            stateMachine.SetState(CharacterState.Attack);
        }
        else if (!movement.IsGrounded)
        {
            stateMachine.SetState(CharacterState.InAir);
        }
        else if (isJumpWindingUp)
        {
            stateMachine.SetState(CharacterState.JumpStart);
        }
        else if (isLandRecovering)
        {
            stateMachine.SetState(CharacterState.JumpLand);
        }
        else if (new Vector2(movement.Velocity.x, movement.Velocity.z).sqrMagnitude > 0.01f)
        {
            // 플레이어는 입력(moveInput)을 보고 판단하지만, 적은 Brain의 의도가 실제 이동으로
            // 이어졌는지가 중요하므로 결과인 수평 속도를 본다.
            stateMachine.SetState(CharacterState.Move);
        }
        else
        {
            stateMachine.SetState(CharacterState.Idle);
        }
    }

    public void OnHit(HitData hit)
    {
        currentHealth -= hit.Damage;

        // 점프 준비/착지 경직/쓰러짐/기상 중 맞으면 그 동작은 무산된다. 여기서 끄지 않으면 피격으로 해당
        // 클립이 중단되어 Animation Event가 호출되지 못하고, 플래그가 켜진 채 남아 이동이 영구히 잠긴다.
        // isJumpAirborne도 함께 꺼야, 점프 중 피격 -> 넉백 착지가 다음 착지 때 엉뚱하게
        // 착지 경직으로 이어지는 걸 막을 수 있다.
        isJumpWindingUp = false;
        isLandRecovering = false;
        isJumpAirborne = false;
        isKnockdownLanded = false;
        isGettingUp = false;

        movement.ApplyKnockback(hit.KnockbackVelocity, hit.GroundSlideDeceleration);

        // 공격 중에 맞아 Stun/Airborne으로 전환되면 진행 중이던 공격을 즉시 취소한다. 그대로 두면
        // attackStateTimer가 배경에서 계속 흐르다 만료되는 순간 AdvanceComboOrEnd가 새 공격 애니메이션을
        // 틀어버려서, Stun/Airborne 연출 위에 공격 클립이 갑자기 덮어씌워지는 문제가 생긴다.
        if (movement.IsKnockedBackAirborne || movement.IsGroundSliding)
        {
            attackStateTimer = 0f;
            comboInputBuffered = false;
            selfImpulseActive = false;
        }

        if (currentHealth <= 0)
        {
            Debug.Log($"{name} 사망");
            // TODO: 사망 연출(사망 애니메이션 재생 후 제거 등). 지금은 즉시 제거한다.
            Destroy(gameObject);
        }
    }
}
