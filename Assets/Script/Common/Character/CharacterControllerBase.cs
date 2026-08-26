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

    [Header("안전장치")]
    [KoreanLabel("동작 최대 지속시간(초)")]
    [Tooltip("점프 준비 / 착지 경직 / 다운 / 기상은 Animation Event가 도착해야 끝난다. " +
             "클립에 이벤트가 빠졌거나 클립이 도중에 교체되면 그 이벤트가 영영 안 오고 캐릭터가 영구히 잠긴다. " +
             "이 시간이 지나도 이벤트가 없으면 강제로 동작을 끝내고 Console에 경고를 남긴다. " +
             "정상 상태에서는 절대 발동하지 않아야 하는 값이므로, 가장 긴 클립보다 넉넉하게 잡을 것.")]
    public float actionPhaseTimeout = 2f;

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

    // 공격 애니메이션의 남은 재생 시간. combat.AttackDuration(공격 클립 길이)에서 시작해 줄어든다.
    // "공격 중인가"의 권한은 이 타이머가 아니라 phase == Attack이 갖는다. 이건 지속시간만 센다.
    float attackStateTimer;

    /// <summary>
    /// 지금 <b>진행 중인 동작</b>. 이 값들은 서로 배타적이라(한 번에 하나만 성립) enum 하나로 표현한다.
    ///
    /// 예전에는 이걸 bool 플래그 5개로 들고 있었는데, 그러면 "점프 준비 중이면서 동시에 기상 중" 같은
    /// 무효 조합이 코드상 표현 가능해진다. 실제로 피격 시 플래그를 하나라도 빠뜨리고 끄면
    /// 그 동작의 Animation Event가 영영 호출되지 않아 이동이 영구히 잠기는 버그가 반복됐다.
    /// enum으로 바꾸면 무효 조합 자체가 만들어질 수 없고, 이탈 정리도 SetPhase 한 곳에 모인다.
    ///
    /// [주의] Idle/Move/InAir/Stun/Airborne은 여기 없다. 그건 "동작"이 아니라 MovementCore가 소유한
    /// 물리적 사실이며(IsGrounded / IsGroundSliding / IsKnockedBackAirborne), None과 조합되어 표현된다.
    /// Attack만은 물리 상태와 공존할 수 있다 (공중 공격 = Attack + 공중, 돌진 = Attack + 슬라이드).
    /// </summary>
    enum ActionPhase
    {
        /// <summary>진행 중인 동작 없음. 서 있거나 걷거나 떠 있거나 얻어맞고 미끄러지는 중.</summary>
        None,
        /// <summary>점프 준비. 아직 뜨지 않았다. OnJumpLaunchFrame에서 벗어난다.</summary>
        JumpStart,
        /// <summary>점프 착지 경직. OnJumpLandEndFrame에서 벗어난다.</summary>
        JumpLand,
        /// <summary>공격 재생 중(콤보 대기 포함). 지속시간은 attackStateTimer가 센다.</summary>
        Attack,
        /// <summary>넉백으로 떴다가 착지해 쓰러져 있음. OnKnockdownGetUpStartFrame에서 GetUp으로.</summary>
        Landed,
        /// <summary>쓰러진 상태에서 일어나는 중. OnKnockdownGetUpEndFrame에서 벗어난다.</summary>
        GetUp,
    }

    ActionPhase phase = ActionPhase.None;

    // 현재 phase에 머문 시간(초). Animation Event로만 끝나는 동작이 이벤트를 못 받고
    // 영영 갇히는 것을 막는 워치독이다. SetPhase에서 0으로 리셋된다.
    float phaseElapsed;

    // 현재 공격이 재생되는 동안 같은 공격 의도가 다시 들어왔는지 여부. true면 현재 공격이 끝나는 순간
    // CurrentAttack.nextAttack으로 이어간다 (없으면 콤보가 거기서 끝남).
    bool comboInputBuffered;

    // 지금 진행 중인 콤보를 시작한 공격 데이터. "같은 공격을 또 눌렀다(= 콤보 진행)"와
    // "다른 공격을 눌렀다(= 캔슬)"를 구분하는 기준이다.
    // 시작 공격이 하나뿐인 적은 항상 같은 값이라 캔슬 경로를 타지 않는다.
    AttackData currentComboStarter;

    // 현재 공격이 타격 프레임(OnAttackHitFrame)을 이미 지났는지. 다른 공격으로의 캔슬은
    // 이 시점 이후에만 허용된다. StartAttack에서 false로 초기화되고 타격 프레임에 true가 된다.
    bool hasFiredHitFrame;

    // 점프 의도가 들어온 그 순간의 좌우 이동 속도. 준비 동작 중엔 이동이 잠겨 movement.Velocity.x가
    // 0으로 지워지므로, 실제로 뜨는 순간(OnJumpLaunchFrame)에 이 값을 다시 넣어줘야 그 방향으로
    // 점프한 것처럼 보인다. 의도가 들어온 순간 이동 입력이 없었으면 0 -> 제자리 점프.
    float jumpHorizontalVelocity;

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
    public bool IsAttacking => phase == ActionPhase.Attack;

    /// <summary>
    /// 지금 새 행동(이동/공격/점프)을 시작할 수 있는 상태인지. Brain이 참고용으로 읽는다.
    /// 진행 중인 동작이 없고(phase), 얻어맞은 여파도 없어야(물리) 한다.
    /// </summary>
    public bool CanAct => phase == ActionPhase.None
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
        // MovementCore.LaunchedByJump(실제 Jump()를 거쳤는지)로 판단하지, "공중에 있었는가"만으로 판단하지 않는다.
        // 안 그러면 스폰 위치가 바닥보다 살짝 위일 때의 첫 낙하까지 점프 착지로 오인해서
        // JumpLand에 갇혀버린다.
        bool wasLaunchedByJump = movement.LaunchedByJump;

        // 넉백으로 공중에 뜬 상태(Airborne)가 이번 Tick에 끝나는지(착지하는지) 판단하려면
        // Tick 이전 값을 기억해둬야 한다. Tick 안에서 바운스가 남아있는 동안은 계속 true이므로
        // 여러 번 튕기다 최종 착지하는 그 프레임에만 한 번 Landed로 전환된다.
        bool wasKnockedBackAirborne = movement.IsKnockedBackAirborne;

        movement.Tick(Time.deltaTime, bounds);

        // 점프 착지. 진행 중인 다른 동작이 없을 때만 착지 경직으로 넘어간다 —
        // 공중 공격 도중 착지했다면 그 공격의 후딜이 착지 경직 역할을 대신한다
        // (여기서 덮어쓰면 휘두르던 공격이 착지 순간 잘려버린다).
        if (wasLaunchedByJump && movement.IsGrounded && phase == ActionPhase.None)
            SetPhase(ActionPhase.JumpLand);

        // 넉백 착지는 무엇을 하고 있었든 무조건 다운으로 넘어간다.
        if (wasKnockedBackAirborne && !movement.IsKnockedBackAirborne)
            SetPhase(ActionPhase.Landed);

        transform.position = movement.Position;

        // 회전 대신 스프라이트만 수평 반전 (기본 스프라이트가 오른쪽을 보고 있다고 가정)
        if (spriteRenderer != null)
            spriteRenderer.flipX = !isFacingRight;

        if (phase == ActionPhase.Attack)
        {
            attackStateTimer -= Time.deltaTime;
            if (attackStateTimer <= 0f)
                AdvanceComboOrEnd();
        }

        TickPhaseWatchdog(Time.deltaTime);

        UpdateCharacterState();
    }

    // ===== 진행 중인 동작(phase) 전환 =====

    /// <summary>
    /// 진행 중인 동작을 바꾼다. <b>phase를 직접 대입하지 말고 반드시 이 메서드를 거칠 것.</b>
    ///
    /// 여기가 이탈 정리를 책임지는 유일한 지점이다. 예전에는 동작을 중단시키는 쪽(OnHit 등)이
    /// 그 동작이 남긴 플래그·타이머를 손으로 하나씩 치웠고, 하나라도 빠뜨리면 해당 클립의
    /// Animation Event가 영영 호출되지 않아 이동이 영구히 잠겼다.
    /// </summary>
    void SetPhase(ActionPhase next)
    {
        if (phase == next) return;

        if (phase == ActionPhase.Attack)
            CleanUpAttack();

        phase = next;
        phaseElapsed = 0f;
    }

    /// <summary>
    /// Animation Event로만 끝나는 동작이 이벤트를 못 받고 갇히지 않았는지 감시한다.
    ///
    /// 이 게임의 점프 준비 / 착지 경직 / 다운 / 기상은 탈출구가 클립에 심어둔 Animation Event뿐이라,
    /// 이벤트가 빠지거나 클립이 도중에 교체되면 캐릭터가 영구히 조작 불능이 된다.
    /// 여기서 강제로 풀어주고, 원인을 찾을 수 있게 어떤 이벤트가 안 왔는지 경고로 남긴다.
    ///
    /// Attack은 attackStateTimer로 스스로 끝나므로 감시 대상이 아니다.
    /// </summary>
    void TickPhaseWatchdog(float deltaTime)
    {
        if (phase == ActionPhase.None || phase == ActionPhase.Attack) return;
        if (actionPhaseTimeout <= 0f) return; // 0 이하면 워치독을 끈 것으로 본다.

        phaseElapsed += deltaTime;
        if (phaseElapsed < actionPhaseTimeout) return;

        Debug.LogWarning(
            $"{name}: {phase} 상태가 {actionPhaseTimeout}초 동안 끝나지 않아 강제로 해제합니다. " +
            $"'{ExpectedEventName(phase)}' Animation Event가 해당 클립에 있는지 확인하세요.");

        // 다운 도중 갇혔다면 곧바로 조작 가능으로 되돌리는 대신 기상 동작을 거치게 한다.
        // (GetUp 클립마저 이벤트가 없으면 다음 워치독이 거기서 다시 풀어준다.)
        SetPhase(phase == ActionPhase.Landed ? ActionPhase.GetUp : ActionPhase.None);
    }

    /// <summary>워치독 경고에서 "어떤 이벤트를 기다리고 있었는지" 알려주기 위한 이름 표.</summary>
    static string ExpectedEventName(ActionPhase phase)
    {
        switch (phase)
        {
            case ActionPhase.JumpStart: return nameof(OnJumpLaunchFrame);
            case ActionPhase.JumpLand:  return nameof(OnJumpLandEndFrame);
            case ActionPhase.Landed:    return nameof(OnKnockdownGetUpStartFrame);
            case ActionPhase.GetUp:     return nameof(OnKnockdownGetUpEndFrame);
            default:                    return "(없음)";
        }
    }

    /// <summary>
    /// 공격 동작이 끝나거나 중단될 때 남는 것들을 치운다.
    /// 콤보 예약을 안 지우면 다음 동작이 끝나는 순간 엉뚱하게 이전 콤보로 이어지고,
    /// 돌진 슬라이드를 안 끊으면 그 잔여 슬라이드가 피격 경직(Stun)으로 잘못 표시된다.
    /// </summary>
    void CleanUpAttack()
    {
        attackStateTimer = 0f;
        comboInputBuffered = false;
        currentComboStarter = null;
        hasFiredHitFrame = false;
        movement.StopGroundSlide();
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
        // 공격 중이면 그 공격이 이동을 허용하는지가 유일한 기준이다.
        // Impulse 돌진으로 미끄러지는 중이어도 그건 피격 경직이 아니므로 여기서 따로 막지 않는다.
        if (phase == ActionPhase.Attack)
            return combat.CurrentAttack != null && !combat.CurrentAttack.AllowsPlayerMovement;

        // 점프 준비 / 착지 경직 / 다운 / 기상 중에는 이동 불가.
        if (phase != ActionPhase.None)
            return true;

        // 진행 중인 동작이 없을 때 남는 건 물리적 사실뿐 —
        // 넉백으로 떠 있거나(Airborne) 얻어맞아 미끄러지는 중(Stun).
        return movement.IsKnockedBackAirborne || movement.IsGroundSliding;
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
        // 점프 준비 동작/착지 경직/다운/기상 중엔 공격을 무시한다. 여기서 공격이 시작되면 해당 클립이
        // 공격 클립으로 교체되어 OnJumpLaunchFrame / OnJumpLandEndFrame / 기상 이벤트가 영영 호출되지 않는다.
        if (phase != ActionPhase.None && phase != ActionPhase.Attack) return;

        // 넉백으로 뜬 상태(Airborne)나 얻어맞아 미끄러지는 중(Stun)에는 공격 의도를 무시한다.
        // 공격 중(phase == Attack)의 슬라이드는 자기 Impulse 돌진이므로 막지 않는다.
        if (movement.IsKnockedBackAirborne) return;
        if (movement.IsGroundSliding && phase != ActionPhase.Attack) return;

        if (phase != ActionPhase.Attack)
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
            StartAttack(startData); // 이전 공격의 잔여 슬라이드/콤보 예약은 StartAttack이 치운다.
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
    /// data로 CombatCore를 갈아끼우고 지속시간 타이머를 재생, 애니메이션 클립 변경을 알린다.
    /// 콤보 시작 / 콤보 진행 / 캔슬 세 경우 모두 이 메서드를 거치며,
    /// 이전 공격이 남긴 것을 치우는 일도 여기서 한 번에 처리한다.
    /// </summary>
    void StartAttack(AttackData data)
    {
        if (data == null)
        {
            Debug.LogWarning($"{name}: 공격 데이터가 없어 공격을 실행할 수 없습니다.");
            return;
        }

        // 이전 공격이 Impulse였다면 남은 돌진 슬라이드를 끊는다. 안 끊으면 새 공격 도중에도
        // 그 슬라이드가 계속 남아 이동/상태 판정을 흔든다. (공격을 시작할 수 있는 시점에
        // 미끄러지고 있다면 그건 반드시 자기 돌진이다 — 피격 슬라이드 중에는 위 가드에 막힌다.)
        movement.StopGroundSlide();

        // 끊어버린 공격의 콤보 예약은 무효다. 안 지우면 새 공격이 끝나는 순간
        // 엉뚱하게 이전 콤보의 다음 단계로 이어진다.
        comboInputBuffered = false;
        hasFiredHitFrame = false; // 새 공격이 시작됐으니 캔슬 가능 시점도 다시 닫힌다.

        phase = ActionPhase.Attack; // SetPhase가 아닌 직접 대입: 위에서 이미 이전 공격을 정리했다.

        combat.Init(data);
        attackStateTimer = combat.AttackDuration;
        OnAttackClipChanged?.Invoke(data.attackClip);
        // 실제 타격 판정은 공격 애니메이션 클립의 Animation Event -> OnAttackHitFrame()에서 수행된다.

        data.ApplySelfMovement(movement, FacingDir);
    }

    /// <summary>
    /// 공격 지속시간이 끝나는 프레임에 호출된다. 공격 의도가 버퍼링되어 있고 후속 공격이 있으면
    /// 그 공격으로 이어가고, 아니면 콤보를 끝낸다(다음 의도는 콤보 시작부터 새로 시작).
    /// </summary>
    void AdvanceComboOrEnd()
    {
        AttackData next = comboInputBuffered ? combat.CurrentAttack?.nextAttack : null;

        if (next != null)
            StartAttack(next);
        else
            SetPhase(ActionPhase.None); // 잔여 돌진 슬라이드·콤보 예약 정리는 CleanUpAttack이 맡는다.
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
        if (!movement.IsGrounded) return;

        // 진행 중인 동작이 있으면 점프 불가. 공격 중 점프도 여기서 함께 막힌다
        // (공격 클립이 재생 중인 JumpStart 클립을 덮어쓰는 것 방지).
        if (phase != ActionPhase.None) return;

        SetPhase(ActionPhase.JumpStart);

        // 지금 이 순간 좌우 이동 의도가 있으면 그 방향으로, 없으면(가만히 서 있었으면) 제자리 점프.
        jumpHorizontalVelocity = Mathf.Abs(moveInputAtRequest.x) > 0.01f
            ? Mathf.Sign(moveInputAtRequest.x) * movement.MoveSpeed
            : 0f;
    }

    /// <summary>
    /// JumpStart 클립 안의 "발이 떨어지는" 프레임의 Animation Event(JumpAnimationEventReceiver 경유)가 호출한다.
    /// 의도가 들어온 시점이 아니라 이 시점에 비로소 실제로 위로 뜬다.
    /// 지금 점프 준비 중일 때만 반응한다 — 다른 동작 중에 엉뚱한 이벤트가 날아와도 그 동작을 망가뜨리지 않게.
    /// </summary>
    public void OnJumpLaunchFrame()
    {
        if (phase != ActionPhase.JumpStart) return;

        SetPhase(ActionPhase.None); // 뜬 뒤의 InAir는 동작이 아니라 물리 상태다.
        movement.Jump(jumpHorizontalVelocity); // 여기서 LaunchedByJump가 켜진다.
    }

    /// <summary>
    /// JumpLand 애니메이션 클립의 Animation Event(JumpAnimationEventReceiver 경유)가 호출한다.
    /// 이 시점에 착지 경직이 풀리고 다시 이동/공격/점프가 가능해진다.
    /// </summary>
    public void OnJumpLandEndFrame()
    {
        if (phase != ActionPhase.JumpLand) return;

        SetPhase(ActionPhase.None);
    }

    // ===== 넉백 다운 / 기상 =====

    /// <summary>
    /// Landed 클립의 "몸을 일으키기 시작하는" 프레임의 Animation Event(KnockdownAnimationEventReceiver 경유)가 호출한다.
    /// 이 시점에 쓰러진 상태(Landed)가 끝나고 일어나는 상태(GetUp)로 넘어간다.
    /// </summary>
    public void OnKnockdownGetUpStartFrame()
    {
        if (phase != ActionPhase.Landed) return;

        SetPhase(ActionPhase.GetUp);
    }

    /// <summary>
    /// GetUp 클립의 "완전히 일어나는" 프레임의 Animation Event(KnockdownAnimationEventReceiver 경유)가 호출한다.
    /// 이 시점에 기상이 끝나고 다시 이동/공격/점프가 가능해진다.
    /// </summary>
    public void OnKnockdownGetUpEndFrame()
    {
        if (phase != ActionPhase.GetUp) return;

        SetPhase(ActionPhase.None);
    }

    // ===== 상태 / 피격 =====

    /// <summary>
    /// 매 프레임 현재 상황을 보고 캐릭터 상태를 판단해 반영한다.
    /// 우선순위: Airborne(공중 넉백) > Landed(쓰러짐) > GetUp(기상) > Stun(그라운드 슬라이드) > Attack
    /// > InAir(공중) > JumpStart(준비) > JumpLand(착지) > Move/Idle.
    /// JumpStart/JumpLand가 InAir보다 뒤에 오는 이유: 둘 다 지상에서만 켜지므로(IsGrounded == true) InAir와 겹치지 않는다.
    ///
    /// 이 사다리가 필요한 이유는 phase(진행 중인 동작)와 MovementCore의 물리 상태가 공존할 수 있기 때문이다
    /// (공중 공격 = Attack + 공중, 돌진 = Attack + 슬라이드). 그중 무엇을 보여줄지가 여기서 정해진다.
    /// 그라운드 슬라이드가 Stun이 아니라 자기 돌진인지는 phase == Attack 여부로 판별한다 —
    /// 피격 슬라이드가 시작될 땐 OnHit이 phase를 None으로 되돌리므로 둘은 절대 헷갈리지 않는다.
    /// </summary>
    void UpdateCharacterState()
    {
        if (movement.IsKnockedBackAirborne)
        {
            stateMachine.SetState(CharacterState.Airborne);
        }
        else if (phase == ActionPhase.Landed)
        {
            stateMachine.SetState(CharacterState.Landed);
        }
        else if (phase == ActionPhase.GetUp)
        {
            stateMachine.SetState(CharacterState.GetUp);
        }
        else if (movement.IsGroundSliding && phase != ActionPhase.Attack)
        {
            stateMachine.SetState(CharacterState.Stun);
        }
        else if (phase == ActionPhase.Attack)
        {
            stateMachine.SetState(CharacterState.Attack);
        }
        else if (!movement.IsGrounded)
        {
            stateMachine.SetState(CharacterState.InAir);
        }
        else if (phase == ActionPhase.JumpStart)
        {
            stateMachine.SetState(CharacterState.JumpStart);
        }
        else if (phase == ActionPhase.JumpLand)
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

        // 진행 중이던 동작(점프 준비 / 착지 경직 / 공격 / 다운 / 기상)은 피격으로 전부 무산된다.
        // 여기서 치우지 않으면 피격으로 해당 클립이 중단되어 Animation Event가 호출되지 못하고,
        // 그 동작이 켜진 채 남아 이동이 영구히 잠긴다.
        //
        // 예전에는 이 정리를 플래그 5개를 손으로 끄는 식으로 했고, 하나만 빠뜨려도 위 증상이 났다.
        // 지금은 이 한 줄이 전부다 — 공격 타이머·콤보 예약·잔여 돌진 슬라이드까지 CleanUpAttack이 맡는다.
        // (진행 중이던 공격을 안 끊으면 타이머가 배경에서 흐르다 만료되는 순간
        //  Stun/Airborne 연출 위에 공격 클립이 갑자기 덮어씌워진다.)
        SetPhase(ActionPhase.None);

        // 점프 중 피격이면 그 점프의 착지 처리도 무효가 되어야 하는데,
        // 그건 ApplyKnockback이 LaunchedByJump를 끄면서 알아서 처리한다.
        movement.ApplyKnockback(knockback, hit.GroundSlideDeceleration);

        if (currentHealth <= 0)
        {
            Debug.Log($"{name} 사망");
            OnDeath();
        }
    }
}
