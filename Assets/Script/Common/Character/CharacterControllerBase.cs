using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 플레이어와 적이 공유하는 "몸" 역할의 베이스 클래스.
/// MovementCore/CombatCore/CharacterStateMachine을 조립하고 애니메이션·피격·상태 전환을 연결한다.
///
/// 이 클래스는 "무엇을 할지"는 전혀 판단하지 않는다. 그건 매 프레임 UpdateIntent()가 돌려주는
/// CharacterIntent에 담겨 오고, 그 의도가 키보드에서 왔는지 AI에서 왔는지는 구분하지 않는다.
/// - PlayerController: InputAction(키보드/마우스) 입력을 CharacterIntent로 포장해서 넘긴다.
/// - EnemyController: 같은 오브젝트에 붙은 IEnemyBrain에게 물어본 결과를 그대로 넘긴다.
///
/// 이름에 Base를 붙인 이유: Unity 빌트인 UnityEngine.CharacterController와 이름이 겹치지 않게 하기 위함.
///
/// [준비물]
/// - 씬 어딘가(맵 오브젝트)에 MapBounds 컴포넌트가 붙어 있어야 함 (자동으로 찾아서 참조함)
/// - targetLayer는 이 캐릭터가 때릴 대상의 Layer로 지정
/// - 애니메이션을 쓰려면 CharacterAnimatorBridge를 같은 오브젝트에,
///   AttackAnimationEventReceiver / JumpAnimationEventReceiver / KnockdownAnimationEventReceiver를
///   Animator가 붙은 자식 오브젝트에 추가
/// </summary>
public abstract class CharacterControllerBase : MonoBehaviour,
    IHittable, IStateMachineOwner, IAttackEventListener, IAttackClipSource,
    IJumpEventListener, IKnockdownEventListener, IAttackRangeDebugInfo
{
    [Header("공격 대상 레이어")]
    [KoreanLabel("대상 레이어")]
    [Tooltip("이 캐릭터가 때릴 대상의 Layer. 플레이어라면 Enemy, 적이라면 Player.")]
    [FormerlySerializedAs("enemyLayer")]
    public LayerMask targetLayer;

    [Header("스탯 데이터")]
    [KoreanLabel("캐릭터 스탯")]
    public CharacterStatData characterStatData;
    [KoreanLabel("이동 스탯")]
    public MovementStatData movementStatData;
    [KoreanLabel("공격 1 (콤보 시작)")]
    [FormerlySerializedAs("combatStatData")]
    public AttackData firstAttackData;

    protected readonly MovementCore movement = new MovementCore();
    protected readonly CombatCore combat = new CombatCore();
    readonly CharacterStateMachine stateMachine = new CharacterStateMachine();

    /// <summary>
    /// CharacterAnimatorBridge 등 외부에서 현재 상태를 읽거나 OnStateChanged를 구독할 때 사용.
    /// </summary>
    public CharacterStateMachine StateMachine => stateMachine;

    /// <summary>CharacterAnimatorBridge가 구독해서 콤보 단계별 클립을 CrossFade하는 데 사용.</summary>
    public event Action<AnimationClip> OnAttackClipChanged;

    /// <summary>디버그 표시(AttackRangeGizmo 등)가 실제 타격 판정이 일어난 시점을 구독할 때 사용.</summary>
    public event Action OnAttackHitFrameFired;

    // currentHealth는 HealthDebugDisplay가 리플렉션으로 읽는다. private으로 두면
    // Type.GetField(NonPublic|Instance)가 기반 클래스의 private 필드는 못 찾으므로 protected여야 한다.
    protected int currentHealth;

    protected bool isFacingRight = true; // 스프라이트 좌우 반전 + 공격 판정 방향(FacingDir)의 유일한 기준.

    SpriteRenderer spriteRenderer;

    // 이번 프레임에 확정된 이동 의도(잠금 적용 전 원본). IsMovingForState의 기본 판정에 쓰인다.
    protected Vector2 CurrentMoveIntent { get; private set; }

    // 공격 애니메이션 재생 중임을 나타내는 타이머. combat.AttackDuration(공격 클립 길이)만큼 유지된다.
    protected float attackStateTimer;

    // 현재 공격이 재생되는 동안 같은 공격 의도가 다시 들어왔는지 여부. true면 현재 공격이 끝나는 순간
    // CurrentAttack.nextAttack으로 이어간다 (없으면 콤보가 거기서 끝남).
    protected bool comboInputBuffered;

    // 지금 진행 중인 콤보를 시작한 공격 데이터. "같은 공격을 또 눌렀다(= 콤보 진행)"와
    // "다른 공격을 눌렀다(= 캔슬)"를 구분하는 기준이다.
    // 시작 공격이 하나뿐인 적은 항상 같은 값이라 캔슬 경로를 타지 않는다.
    AttackData currentComboStarter;

    // 현재 공격이 타격 프레임(OnAttackHitFrame)을 이미 지났는지. 다른 공격으로의 캔슬은
    // 이 시점 이후에만 허용된다. StartAttack에서 false로 초기화되고 타격 프레임에 true가 된다.
    bool hasFiredHitFrame;

    // 현재 그라운드 슬라이드가 피격이 아니라 자기 Impulse 공격(돌진)으로 인한 것인지 구분하는 플래그.
    // 없으면 UpdateCharacterState가 IsGroundSliding만 보고 돌진 중에도 Stun으로 잘못 표시한다.
    // (돌진 도중 실제로 피격당하는 것까지는 구분하지 않음 — 그 경우 공격이 끝날 때까지 Attack으로 표시됨)
    protected bool selfImpulseActive;

    // 점프 준비(JumpStart) 애니메이션 재생 중인지. 이 동안엔 아직 뜨지 않고 이동도 막힌다.
    // JumpStart 클립 안의 "발이 떨어지는" 프레임에 걸린 Animation Event(OnJumpLaunchFrame)가 호출되는
    // 순간에 false로 바뀌면서 비로소 movement.Jump()가 실행된다.
    protected bool isJumpWindingUp;

    // 점프 의도가 들어온 그 순간의 좌우 이동 속도. 준비 동작 중엔 이동이 잠겨 movement.Velocity.x가
    // 0으로 지워지므로, 실제로 뜨는 순간(OnJumpLaunchFrame)에 이 값을 다시 넣어줘야 그 방향으로
    // 점프한 것처럼 보인다. 의도가 들어온 순간 이동 입력이 없었으면 0 -> 제자리 점프.
    protected float jumpHorizontalVelocity;

    // 착지 경직(JumpLand) 재생 중인지. 점프로 떴다가 땅에 닿는 순간 켜지고,
    // JumpLand 클립의 Animation Event(OnJumpLandEndFrame)가 호출될 때 꺼진다.
    // 준비 동작과 마찬가지로 이 동안엔 이동이 막힌다.
    protected bool isLandRecovering;

    // OnJumpLaunchFrame으로 실제 점프가 발사된 뒤, 아직 착지 처리를 안 한 상태인지.
    // "공중에 있다가 땅에 닿았다"만으로 착지 경직을 트리거하면 스폰 위치가 바닥보다 살짝 위일 때의
    // 첫 낙하까지 점프 착지로 오인해버린다. 그래서 반드시 실제 점프 발사(OnJumpLaunchFrame)를 거친
    // 경우에만 착지 경직으로 이어지도록 이 플래그로 구분한다. (실제로 겪은 버그)
    protected bool isJumpAirborne;

    // 넉백으로 공중에 떴다가 착지해 쓰러져 있는(Landed) 중인지. 이 동안엔 이동/공격/점프가 막힌다.
    // Landed 클립의 Animation Event(OnKnockdownGetUpStartFrame)가 호출되면 isGettingUp으로 넘어간다.
    protected bool isKnockdownLanded;

    // 쓰러진 상태에서 몸을 일으키는(GetUp) 중인지. 이 동안에도 이동/공격/점프가 막힌다.
    // GetUp 클립의 Animation Event(OnKnockdownGetUpEndFrame)가 호출되면 꺼지고 다시 행동 가능해진다.
    protected bool isGettingUp;

    // ===== 외부(AI/디버그 표시)가 읽는 정보 =====

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

    // ===== 파생 클래스가 채우는 부분 =====

    /// <summary>
    /// 이번 프레임의 행동 의도를 확정해서 반환한다. 매 프레임 Update 맨 앞에서 한 번 호출된다.
    /// 여기서 이동/공격 판정을 직접 하지 말 것 — 반환한 의도를 실제 동작으로 옮기는 건 이 클래스의 일이다.
    /// </summary>
    protected abstract CharacterIntent UpdateIntent();

    /// <summary>
    /// 이동 입력을 기준으로 8방향(45도 단위) 스냅을 적용할지. 키보드로 조작하는 플레이어는 스냅해야
    /// 자연스럽지만, 임의의 각도로 목표 지점을 향해 움직이는 AI는 스냅 없이 그대로 이동해야 한다.
    /// </summary>
    protected virtual bool Uses8DirectionSnap => true;

    /// <summary>
    /// 이동 의도를 반영해 바라보는 방향(isFacingRight)을 갱신한다.
    /// 이동 방향과 바라보는 방향이 항상 같지는 않으므로(예: 적이 뒷걸음질 치며 대상을 계속 바라봄)
    /// 갱신 규칙 자체를 파생 클래스에 맡긴다. 기본은 아무것도 하지 않음.
    /// </summary>
    protected virtual void UpdateFacing(Vector2 effectiveMoveInput) { }

    /// <summary>
    /// Move 상태로 볼지 Idle 상태로 볼지의 판정. 기본은 "이동 의도가 있는가".
    /// 의도가 실제 이동으로 이어졌는지가 더 중요한 경우(적) 파생 클래스가 수평 속도 기준으로 바꾼다.
    /// </summary>
    protected virtual bool IsMovingForState() => CurrentMoveIntent.sqrMagnitude > 0.01f;

    /// <summary>체력이 0 이하가 되었을 때 호출된다. 기본은 아무것도 하지 않음.</summary>
    protected virtual void OnDeath() { }

    // ===== 생명주기 =====

    protected virtual void Awake()
    {
        movement.Position = transform.position;

        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer == null)
            Debug.LogWarning($"{name}: SpriteRenderer가 없어 좌우 반전이 적용되지 않습니다. (아직 프리미티브를 쓰는 중이면 정상)");

        if (characterStatData == null)
            Debug.LogWarning($"{name}: characterStatData가 연결되지 않아 기본 체력(100)을 사용합니다.");

        currentHealth = characterStatData != null ? characterStatData.maxHealth : 100;

        movement.Init(movementStatData);
        movement.Use8DirectionSnap = Uses8DirectionSnap;
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

    protected virtual void Update()
    {
        CharacterIntent intent = UpdateIntent();
        CurrentMoveIntent = intent.MoveInput;

        if (intent.WantsAttack)
            TryStartCombo(intent.AttackToStart != null ? intent.AttackToStart : firstAttackData);

        if (intent.WantsJump)
            TryStartJump(intent.MoveInput);

        // 이동이 막힌 상태(공격/점프 준비/착지 경직/기상/Stun/Airborne)면 이동 의도를 무시한다.
        Vector2 effectiveMoveInput = IsMovementLocked() ? Vector2.zero : intent.MoveInput;

        // 이동을 허용하는 공격 중이면 그 공격이 지정한 이동 속도 배율을 적용한다 (공격 중이 아니면 1 = 평소 속도).
        movement.MoveSpeedMultiplier = attackStateTimer > 0f && combat.CurrentAttack != null
            ? combat.CurrentAttack.MoveSpeedMultiplier
            : 1f;

        movement.SetMoveInput(effectiveMoveInput);

        UpdateFacing(effectiveMoveInput);

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

    // ===== 이동 / 방향 =====

    /// <summary>
    /// 이동 의도를 무시해야 하는 상태인지: 이동을 막는 공격(Locked/Impulse) 중, 점프 준비/착지 경직/기상 중,
    /// 또는 Stun(얻어맞아 미끄러지는 중)/Airborne(넉백으로 뜬 중).
    ///
    /// 실제 이동 자체는 MovementCore.SetMoveInput이 공중/슬라이드 중이면 내부적으로 이미 무시하지만,
    /// 여기서 막지 않으면 이동 의도만으로 바라보는 방향(isFacingRight)이 계속 바뀌어버린다.
    /// 그래서 SetFacing도 이 상태에서는 요청을 무시한다.
    /// </summary>
    protected bool IsMovementLocked()
    {
        bool lockedByAttack = attackStateTimer > 0f
            && combat.CurrentAttack != null
            && !combat.CurrentAttack.AllowsPlayerMovement;

        return lockedByAttack || isJumpWindingUp || isLandRecovering || isKnockdownLanded || isGettingUp
            || movement.IsKnockedBackAirborne || (movement.IsGroundSliding && !selfImpulseActive);
    }

    /// <summary>
    /// 바라볼 좌/우를 설정한다. 이동 방향과 무관하게 외부(Brain 등)가 원하는 방향을 지정하는 용도
    /// (예: 너무 가까워서 뒷걸음질칠 때도 대상을 계속 바라보게). X 성분만 보고 판단하며
    /// (위/아래로만 향하는 방향이 오면 부호가 없어 기존 좌우를 그대로 유지), 이동이 막힌 상태(Stun 등)면
    /// 요청을 무시한다.
    /// </summary>
    public void SetFacing(Vector3 direction)
    {
        if (IsMovementLocked()) return;

        if (Mathf.Abs(direction.x) > 0.01f)
            isFacingRight = direction.x > 0f;
    }

    // ===== 공격 / 콤보 =====

    /// <summary>
    /// 공격 의도 처리. 세 갈래로 나뉜다.
    /// 1. 공격 중이 아니면 startData로 콤보를 새로 시작한다.
    /// 2. 공격 중인데 지금 콤보를 시작한 것과 <b>다른</b> 공격을 요청했으면 캔슬을 시도한다.
    ///    단 그 공격이 canCancelOtherAttacks를 켠 경우에만 (예: X 콤보 도중 Z키 -> Z가 캔슬 권한을
    ///    가졌으면 진행 중인 X 클립을 끊고 곧바로 Z로 갈아탄다. 반대로 Z 도중 X는 권한이 없어 무시).
    /// 3. 공격 중이면서 <b>같은</b> 공격을 요청했으면 콤보 진행으로 보고 버퍼링만 한다
    ///    (실제 전환은 현재 클립이 끝나는 AdvanceComboOrEnd 시점).
    /// </summary>
    void TryStartCombo(AttackData startData)
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
            // 콤보 시작만 쿨타임 체크를 한다. 콤보 도중 이어지는 공격은 현재 공격의
            // 지속시간이 곧 다음 공격을 받을 수 있는 시점이므로 별도 쿨타임 체크가 필요 없다.
            if (!combat.TryStartAttack(Time.time)) return;
            currentComboStarter = startData;
            StartAttack(startData);
            return;
        }

        // 다른 공격으로 갈아타기(캔슬). 두 가지 조건을 모두 통과해야 한다.
        // 1. 들어오는 공격이 캔슬 권한을 가질 것(canCancelOtherAttacks). 캔슬은 특수 공격에만 주는
        //    특권이라서, 이 값이 꺼진 평범한 공격은 상대 공격이 끝날 때까지 기다려야 한다.
        // 2. 상대 공격이 타격 프레임을 이미 지났을 것. 안 그러면 타격 판정이 나가기 전에 계속
        //    갈아탈 수 있어서 공격이 한 번도 안 맞는 무한 취소가 가능해진다.
        //    즉 캔슬 윈도우는 "타격 프레임 ~ 클립 끝"이며, 그 시작점은 클립에 심어둔
        //    Animation Event 위치가 그대로 결정한다.
        if (startData != null && startData != currentComboStarter)
        {
            if (!startData.canCancelOtherAttacks) return;
            if (!hasFiredHitFrame) return;

            currentComboStarter = startData;
            CancelIntoAttack(startData);
            return;
        }

        // 같은 공격 = 콤보를 다음 단계로 이어가려는 요청.
        if (combat.CurrentAttack != null && combat.CurrentAttack.nextAttack != null)
        {
            comboInputBuffered = true;
        }
        // 후속 공격이 없는 상태에서 재요청하면(마지막 콤보 단계 도중) 그냥 무시된다.
    }

    /// <summary>
    /// 진행 중이던 공격을 끊고 곧바로 다른 공격을 시작한다.
    /// 클립 교체는 StartAttack이 알아서 하므로, 여기서는 이전 공격이 남긴 것들만 정리한다.
    /// </summary>
    void CancelIntoAttack(AttackData data)
    {
        // 이전 공격이 Impulse였다면 남은 돌진 슬라이드를 끊는다 (AdvanceComboOrEnd와 같은 이유 —
        // 안 끊으면 IsGroundSliding이 남아 새 공격 중에 Stun으로 잘못 표시된다).
        if (selfImpulseActive)
            movement.StopGroundSlide();

        // 끊어버린 공격의 콤보 예약은 무효다. 안 지우면 새 공격이 끝나는 순간
        // 엉뚱하게 이전 콤보의 다음 단계로 이어진다.
        comboInputBuffered = false;

        StartAttack(data);
    }

    /// <summary>
    /// data로 CombatCore를 갈아끼우고 지속시간 타이머를 재생, 애니메이션 클립 변경을 알린다.
    /// 콤보 시작과 콤보 진행(다음 공격으로 전환) 모두 이 메서드를 거친다.
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
        hasFiredHitFrame = false; // 새 공격이 시작됐으니 캔슬 가능 시점도 다시 닫힌다.
        OnAttackClipChanged?.Invoke(data.attackClip);
        // 실제 타격 판정은 공격 애니메이션 클립의 Animation Event -> OnAttackHitFrame()에서 수행된다.

        selfImpulseActive = false;
        data.ApplySelfMovement(movement, FacingDir);
        if (movement.IsGroundSliding)
            selfImpulseActive = true;
    }

    /// <summary>
    /// 공격 지속시간이 끝나는 프레임에 호출된다. 공격 의도가 버퍼링되어 있고 후속 공격이 있으면
    /// 그 공격으로 이어가고, 아니면 콤보를 끝낸다(다음 의도는 콤보 시작부터 새로 시작).
    /// </summary>
    void AdvanceComboOrEnd()
    {
        // 돌진(Impulse)은 그 공격의 일부이므로 공격이 끝나면 남은 슬라이드도 같이 끊는다.
        // 안 끊으면 selfImpulseActive만 꺼지고 IsGroundSliding은 true로 남아,
        // UpdateCharacterState가 그 잔여 슬라이드를 피격 경직(Stun)으로 잘못 표시한다.
        // 돌진 지속시간(selfMoveSpeed / selfMoveDeceleration)이 공격 클립 길이보다 길면 반드시 발생한다.
        if (selfImpulseActive)
            movement.StopGroundSlide();

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
        hasFiredHitFrame = true; // 이 시점부터 다른 공격으로 캔슬할 수 있다.
        OnAttackHitFrameFired?.Invoke();
    }

    // ===== 점프 =====

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

        // 지금 이 순간 좌우 이동 의도가 있으면 그 방향으로, 없으면(가만히 서 있었으면) 제자리 점프.
        jumpHorizontalVelocity = Mathf.Abs(moveInputAtRequest.x) > 0.01f
            ? Mathf.Sign(moveInputAtRequest.x) * movement.MoveSpeed
            : 0f;
    }

    /// <summary>
    /// JumpStart 클립 안의 "발이 떨어지는" 프레임의 Animation Event(JumpAnimationEventReceiver 경유)가 호출한다.
    /// 의도가 들어온 시점이 아니라 이 시점에 비로소 실제로 위로 뜬다.
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

    // ===== 넉백 다운 / 기상 =====

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

    // ===== 상태 / 피격 =====

    /// <summary>
    /// 매 프레임 현재 상황을 보고 캐릭터 상태를 판단해 반영한다.
    /// 우선순위: Airborne(공중 넉백) > Landed(쓰러짐) > GetUp(기상) > Stun(그라운드 슬라이드) > Attack
    /// > InAir(공중) > JumpStart(준비) > JumpLand(착지) > Move/Idle.
    /// JumpStart/JumpLand가 InAir보다 뒤에 오는 이유: 둘 다 지상에서만 켜지므로(IsGrounded == true) InAir와 겹치지 않는다.
    /// 단, 그라운드 슬라이드가 자기 Impulse 공격의 돌진으로 인한 것이면(selfImpulseActive) Stun 대신 Attack으로 본다.
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
        else if (IsMovingForState())
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

        // 지금 떠 있는지는 나만 아는 정보라, 공격자가 보낸 지상용/공중용 넉백 중 어느 쪽을 쓸지 여기서 고른다.
        // 넉백으로 뜬 상태(Airborne)뿐 아니라 점프로 떠 있는 중(InAir)에 맞은 것도 공중 피격으로 본다.
        Vector3 knockback = hit.ResolveKnockbackVelocity(!movement.IsGrounded);

        // 점프 준비/착지 경직/쓰러짐/기상 중 맞으면 그 동작은 무산된다. 여기서 끄지 않으면 피격으로 해당
        // 클립이 중단되어 Animation Event가 호출되지 못하고, 플래그가 켜진 채 남아 이동이 영구히 잠긴다.
        // isJumpAirborne도 함께 꺼야, 점프 중 피격 -> 넉백 착지가 (점프 발사도 안 했으면서) 다음
        // 착지 때 엉뚱하게 착지 경직으로 이어지는 걸 막을 수 있다.
        isJumpWindingUp = false;
        isLandRecovering = false;
        isJumpAirborne = false;
        isKnockdownLanded = false;
        isGettingUp = false;

        movement.ApplyKnockback(knockback, hit.GroundSlideDeceleration);

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
            OnDeath();
        }
    }
}
