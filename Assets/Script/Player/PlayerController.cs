using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// 플레이어 전용 조율 스크립트.
/// PlayerInput 컴포넌트를 쓰지 않고, InputAction을 코드에서 직접 생성/구독한다.
/// MovementCore/CombatCore는 서로 모르고, PlayerController만 둘을 알고 있다.
///
/// [준비물]
/// - 씬 어딘가(맵 오브젝트)에 MapBounds 컴포넌트가 붙어 있어야 함 (자동으로 찾아서 참조함)
/// - enemyLayer는 적이 속한 Layer로 지정
/// - 별도의 Input Actions 에셋이나 PlayerInput 컴포넌트는 필요 없음 (키 바인딩이 코드에 직접 있음)
/// </summary>
public class PlayerController : MonoBehaviour, IHittable, IStateMachineOwner, IAttackEventListener, IAttackClipSource, IJumpEventListener
{
    [Header("공격 대상 레이어")]
    [KoreanLabel("적 레이어")]
    public LayerMask enemyLayer;

    [Header("스탯 데이터")]
    [KoreanLabel("캐릭터 스탯")]
    public CharacterStatData characterStatData;
    [KoreanLabel("이동 스탯")]
    public MovementStatData movementStatData;
    [KoreanLabel("공격 1 (콤보 시작)")]
    [FormerlySerializedAs("combatStatData")]
    public AttackData firstAttackData;

    readonly MovementCore movement = new MovementCore();
    readonly CombatCore combat = new CombatCore();
    readonly CharacterStateMachine stateMachine = new CharacterStateMachine();

    /// <summary>
    /// 애니메이션 컨트롤러 등 외부에서 현재 상태를 읽거나 OnStateChanged를 구독할 때 사용.
    /// </summary>
    public CharacterStateMachine StateMachine => stateMachine;

    /// <summary>CharacterAnimatorBridge가 구독해서 콤보 단계별 클립을 CrossFade하는 데 사용.</summary>
    public event Action<AnimationClip> OnAttackClipChanged;

    /// <summary>디버그 표시(AttackRangeGizmo 등) 등 외부에서 실제 타격 판정이 일어난 시점을 구독할 때 사용.</summary>
    public event Action OnAttackHitFrameFired;

    // ===== 테스트/디버그 스크립트(AttackRangeGizmo 등)가 읽는 읽기 전용 정보 =====

    /// <summary>현재 CombatCore에 적용된 공격의 사거리. CombatCore.PerformHitScan과 동일한 계산에 쓰인다.</summary>
    public float AttackRange => combat.AttackRange;

    /// <summary>현재 CombatCore에 적용된 공격의 판정 반경.</summary>
    public float AttackRadius => combat.AttackRadius;

    /// <summary>공격 판정이 나가는 방향. 스프라이트가 좌우로만 뒤집히는 것과 맞춰 좌/우 둘 중 하나만 나온다.</summary>
    public Vector3 FacingDir => isFacingRight ? Vector3.right : Vector3.left;

    /// <summary>공격 애니메이션이 재생 중인지 (콤보 대기 포함, 타격 판정 여부와는 무관).</summary>
    public bool IsAttacking => attackStateTimer > 0f;

    InputAction moveAction;
    InputAction attackAction;
    InputAction jumpAction;

    Vector2 moveInput;
    bool isFacingRight = true; // 스프라이트 좌우 반전 + 공격 판정 방향(FacingDir)의 유일한 기준.
    int currentHealth;

    SpriteRenderer spriteRenderer;

    // 공격 애니메이션 재생 중임을 나타내는 타이머. combat.AttackDuration(공격 클립 길이)만큼 유지된다.
    float attackStateTimer;

    // 현재 공격이 재생되는 동안 공격 입력이 다시 들어왔는지 여부. true면 현재 공격이 끝나는 순간
    // CurrentAttack.nextAttack으로 이어간다 (없으면 콤보가 거기서 끝남).
    bool comboInputBuffered;

    // 현재 그라운드 슬라이드가 피격이 아니라 내 Impulse 공격 자체(돌진)로 인한 것인지 구분하는 플래그.
    // 없으면 UpdateCharacterState가 IsGroundSliding만 보고 돌진 중에도 Stun으로 잘못 표시한다.
    // (돌진 도중 실제로 피격당하는 것까지는 구분하지 않음 — 그 경우 공격이 끝날 때까지 Attack으로 표시됨)
    bool selfImpulseActive;

    // 점프 준비(JumpStart) 동작 중인지. 이 동안엔 아직 실제로 뜨지 않고, 이동 입력도 막힌다.
    // JumpStart 클립 안의 "발이 떨어지는" 프레임에 걸린 Animation Event(OnJumpLaunchFrame)가 호출되는
    // 순간에 false로 바뀌면서 비로소 movement.Jump()가 실행된다.
    bool isJumpWindingUp;

    // 점프 키를 누른 그 순간의 좌우 이동 속도. 준비 동작 중엔 이동이 잠겨 movement.Velocity.x가
    // 0으로 지워지므로, 실제로 뜨는 순간(OnJumpLaunchFrame)에 이 값을 다시 넣어줘야 그 방향으로
    // 점프한 것처럼 보인다. 누르는 순간 이동 입력이 없었으면 0 -> 제자리 점프.
    float jumpHorizontalVelocity;

    // 착지 경직(JumpLand) 재생 중인지. 점프로 떴다가 땅에 닿는 순간 켜지고,
    // JumpLand 클립의 Animation Event(OnJumpLandEndFrame)가 호출될 때 꺼진다.
    // 준비 동작과 마찬가지로 이 동안엔 이동 입력이 막힌다.
    bool isLandRecovering;

    // OnJumpLaunchFrame으로 실제 점프가 발사된 뒤, 아직 착지 처리를 안 한 상태인지.
    // "공중에 있다가 땅에 닿았다"만으로 착지 경직을 트리거하면 스폰 시 바닥보다 살짝 위에서
    // 시작하는 것처럼 점프한 적 없는 낙하까지 착지로 오인해버린다. 그래서 반드시 실제 점프
    // 발사(OnJumpLaunchFrame)를 거친 경우에만 착지 경직으로 이어지도록 이 플래그로 구분한다.
    bool isJumpAirborne;

    // ===== TEMP: 넉백 색상 테스트 (제거 시 이 블록 전부 + Update 안의 호출부 삭제) =====
    [KoreanLabel("넉백 중 색상 (테스트)")]
    public Color tempKnockbackColor = Color.red;
    Renderer tempRenderer;
    Color tempOriginalColor;
    // ===== TEMP 끝 =====

    void Awake()
    {
        movement.Position = transform.position;

        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer == null)
            Debug.LogWarning($"{name}: SpriteRenderer가 없어 좌우 반전이 적용되지 않습니다. (아직 프리미티브를 쓰는 중이면 정상)");

        // ===== TEMP: 넉백 색상 테스트 =====
        tempRenderer = GetComponentInChildren<Renderer>();
        if (tempRenderer != null)
            tempOriginalColor = tempRenderer.material.color;
        // ===== TEMP 끝 =====

        if (characterStatData == null)
            Debug.LogWarning($"{name}: characterStatData가 연결되지 않아 MovementCore/CombatCore 기본값을 사용합니다.");

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

        BuildInputActions();
    }

    /// <summary>
    /// PlayerInput 컴포넌트 대신 코드에서 직접 InputAction을 구성.
    /// WASD/방향키 -> 8방향 이동, X키/좌클릭 -> 공격.
    /// </summary>
    void BuildInputActions()
    {
        moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");

        attackAction = new InputAction("Attack", InputActionType.Button, binding: "<Keyboard>/x");
        attackAction.AddBinding("<Mouse>/leftButton");

        jumpAction = new InputAction("Jump", InputActionType.Button, binding: "<Keyboard>/c");
    }

    void OnEnable()
    {
        moveAction.performed += OnMoveChanged;
        moveAction.canceled += OnMoveChanged;
        attackAction.performed += OnAttackPerformed;
        jumpAction.performed += OnJumpPerformed;

        moveAction.Enable();
        attackAction.Enable();
        jumpAction.Enable();
    }

    void OnDisable()
    {
        moveAction.performed -= OnMoveChanged;
        moveAction.canceled -= OnMoveChanged;
        attackAction.performed -= OnAttackPerformed;
        jumpAction.performed -= OnJumpPerformed;

        moveAction.Disable();
        attackAction.Disable();
        jumpAction.Disable();
    }

    void OnDestroy()
    {
        moveAction?.Dispose();
        attackAction?.Dispose();
        jumpAction?.Dispose();
    }

    void OnMoveChanged(InputAction.CallbackContext ctx)
    {
        moveInput = ctx.ReadValue<Vector2>();
    }

    void OnAttackPerformed(InputAction.CallbackContext ctx)
    {
        // 점프 준비 동작/착지 경직 중엔 공격 입력을 무시한다. 여기서 공격이 시작되면 해당 클립이
        // 공격 클립으로 교체되어 OnJumpLaunchFrame / OnJumpLandEndFrame이 영영 호출되지 않는다.
        if (isJumpWindingUp || isLandRecovering) return;

        bool isAttacking = attackStateTimer > 0f;

        if (!isAttacking)
        {
            // 콤보 시작(공격 1)만 쿨타임 체크를 한다. 콤보 도중 이어지는 공격은 현재 공격의
            // 지속시간이 곧 다음 입력을 받을 수 있는 시점이므로 별도 쿨타임 체크가 필요 없다.
            if (!combat.TryStartAttack(Time.time)) return;
            StartAttack(firstAttackData);
        }
        else if (combat.CurrentAttack != null && combat.CurrentAttack.nextAttack != null)
        {
            comboInputBuffered = true;
        }
        // 후속 공격이 없는 상태에서 재입력하면(마지막 콤보 단계 도중) 그냥 무시된다.
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
    /// 공격 지속시간이 끝나는 프레임에 호출된다. 입력이 버퍼링되어 있고 후속 공격이 있으면
    /// 그 공격으로 이어가고, 아니면 콤보를 끝낸다(다음 입력은 공격 1부터 새로 시작).
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
    /// 입력 시점이 아니라 이 시점의 위치/방향으로 실제 타격 판정을 수행한다.
    /// </summary>
    public void OnAttackHitFrame()
    {
        combat.PerformHitScan(transform.position, FacingDir, gameObject, enemyLayer);
        OnAttackHitFrameFired?.Invoke();
    }

    /// <summary>
    /// 점프 입력. 이 시점엔 아직 뜨지 않고 JumpStart(준비 동작) 재생만 시작한다.
    /// 실제로 뜨는 건 OnJumpLaunchFrame에서다.
    /// </summary>
    void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        if (!movement.IsGrounded || isJumpWindingUp || isLandRecovering) return;
        if (attackStateTimer > 0f) return; // 공격 중엔 점프 불가 (공격 클립이 재생 중인 JumpStart 클립을 덮어쓰는 것 방지)

        isJumpWindingUp = true;

        // 지금 이 순간 좌우 이동 입력이 있으면 그 방향으로, 없으면(가만히 서 있었으면) 제자리 점프.
        jumpHorizontalVelocity = Mathf.Abs(moveInput.x) > 0.01f
            ? Mathf.Sign(moveInput.x) * movement.MoveSpeed
            : 0f;
    }

    /// <summary>
    /// JumpStart 클립 안의 "발이 떨어지는" 프레임의 Animation Event(JumpAnimationEventReceiver 경유)가 호출한다.
    /// 입력 시점이 아니라 이 시점에 비로소 실제로 위로 뜬다.
    /// </summary>
    public void OnJumpLaunchFrame()
    {
        isJumpWindingUp = false;
        isJumpAirborne = true;
        movement.Jump(jumpHorizontalVelocity);
    }

    /// <summary>
    /// JumpLand 애니메이션 클립의 Animation Event(JumpAnimationEventReceiver 경유)가 호출한다.
    /// 이 시점에 착지 경직이 풀리고 다시 이동/공격/점프가 가능해진다.
    /// </summary>
    public void OnJumpLandEndFrame()
    {
        isLandRecovering = false;
    }

    void Update()
    {
        // 현재 공격이 이동을 막는 공격(Locked/Impulse)이면 이동 입력을 0으로 무시한다.
        bool movementLockedByAttack = attackStateTimer > 0f
            && combat.CurrentAttack != null
            && !combat.CurrentAttack.AllowsPlayerMovement;

        // 점프 준비 동작과 착지 경직 중에도 이동 입력을 막는다.
        bool movementLocked = movementLockedByAttack || isJumpWindingUp || isLandRecovering;
        Vector2 effectiveMoveInput = movementLocked ? Vector2.zero : moveInput;

        movement.SetMoveInput(effectiveMoveInput);

        // 좌우 판단은 입력의 X 성분만 본다 (위/아래로만 움직일 땐 기존 좌우를 유지, 입력 없을 땐 그대로 유지).
        if (movement.IsGrounded && Mathf.Abs(effectiveMoveInput.x) > 0.01f)
            isFacingRight = effectiveMoveInput.x > 0f;

        Bounds bounds = MapBounds.Instance != null
            ? MapBounds.Instance.Bounds
            : new Bounds(Vector3.zero, Vector3.one * 1000f); // 씬에 MapBounds가 없을 때의 안전장치

        // Tick이 바닥 충돌을 처리하면서 IsGrounded를 바꾸므로, 착지 순간을 잡으려면 그 직전 값을 기억해둬야 한다.
        // isJumpAirborne(실제 점프 발사 여부)로 판단하지, "공중에 있었는가"만으로 판단하지 않는다.
        // 안 그러면 스폰 위치가 바닥보다 살짝 위일 때의 첫 낙하까지 점프 착지로 오인해서
        // JumpLand 상태에 갇혀버린다 (실제로 겪은 버그).
        bool wasJumpAirborne = isJumpAirborne;

        movement.Tick(Time.deltaTime, bounds);

        if (wasJumpAirborne && movement.IsGrounded)
        {
            isJumpAirborne = false;
            isLandRecovering = true;
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

        // ===== TEMP: 넉백 색상 테스트 =====
        // movement.IsInKnockback 대신 상태 머신(stateMachine.CurrentState)을 보는 이유는 아래 설명 참고.
        if (tempRenderer != null)
        {
            bool isKnockbackVisual = stateMachine.CurrentState == CharacterState.Stun
                || stateMachine.CurrentState == CharacterState.Airborne;
            tempRenderer.material.color = isKnockbackVisual ? tempKnockbackColor : tempOriginalColor;
        }
        // ===== TEMP 끝 =====
    }

    /// <summary>
    /// 매 프레임 현재 상황을 보고 캐릭터 상태를 판단해 반영한다.
    /// 우선순위: Airborne(공중 넉백) > Stun(그라운드 슬라이드) > Attack > InAir(공중) > JumpStart(준비) > JumpLand(착지) > Move/Idle.
    /// JumpStart/JumpLand가 InAir보다 뒤에 오는 이유: 둘 다 지상에서만 켜지므로(IsGrounded == true) InAir와 겹치지 않는다.
    /// 단, 그라운드 슬라이드가 내 Impulse 공격 자신의 돌진으로 인한 것이면(selfImpulseActive) Stun 대신 Attack으로 본다.
    /// </summary>
    void UpdateCharacterState()
    {
        if (movement.IsKnockedBackAirborne)
        {
            stateMachine.SetState(CharacterState.Airborne);
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
        else if (moveInput.sqrMagnitude > 0.01f)
        {
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

        // 점프 준비/착지 경직 중 맞으면 그 동작은 무산된다. 여기서 끄지 않으면 피격으로 해당 클립이
        // 중단되어 Animation Event가 호출되지 못하고, 플래그가 켜진 채 남아 이동이 영구히 잠긴다.
        // isJumpAirborne도 함께 꺼야, 점프 중 피격 -> 넉백 착지가 (점프 발사도 안 했으면서) 다음
        // 착지 때 엉뚱하게 착지 경직으로 이어지는 걸 막을 수 있다.
        isJumpWindingUp = false;
        isLandRecovering = false;
        isJumpAirborne = false;

        movement.ApplyKnockback(hit.KnockbackVelocity, hit.GroundSlideDeceleration);

        if (currentHealth <= 0)
        {
            Debug.Log($"{name} 사망");
            // TODO: 사망 처리
        }
    }
}