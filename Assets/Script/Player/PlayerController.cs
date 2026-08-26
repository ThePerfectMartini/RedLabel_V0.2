using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어 전용 조율 스크립트. 공통 처리(이동/공격/점프/피격/상태 전환)는 전부 CharacterControllerBase에 있고,
/// 이 클래스는 "무엇을 할지"를 정하는 주체만 담당한다.
///
/// EnemyController가 IEnemyBrain에게 의도를 물어보는 자리에서,
/// 이 클래스는 키보드/마우스 입력을 CharacterIntent로 포장해서 넘긴다.
/// PlayerInput 컴포넌트를 쓰지 않고, InputAction을 코드에서 직접 생성/구독한다.
///
/// [조작] WASD·방향키 이동 / X키·좌클릭 공격 1 / Z키 공격 2 / C키 점프
///
/// [준비물]
/// - 씬 어딘가(맵 오브젝트)에 MapBounds 컴포넌트가 붙어 있어야 함 (자동으로 찾아서 참조함)
/// - 대상 레이어는 적이 속한 Layer로 지정
/// - 별도의 Input Actions 에셋이나 PlayerInput 컴포넌트는 필요 없음 (키 바인딩이 코드에 직접 있음)
/// </summary>
public class PlayerController : CharacterControllerBase
{
    static PlayerController instance;

    /// <summary>
    /// 씬 안의 PlayerController를 찾아 캐싱해서 반환. MapBounds.Instance와 같은 방식 —
    /// 인스펙터에서 따로 연결할 필요 없이 첫 접근 시점에 지연 탐색한다. 적 AI가 추적 대상을 찾을 때 사용.
    /// </summary>
    public static PlayerController Instance
    {
        get
        {
            if (instance == null)
                instance = FindFirstObjectByType<PlayerController>();
            return instance;
        }
    }

    [Header("플레이어 전용 공격")]
    [KoreanLabel("공격 2 (콤보 시작, Z키)")]
    [Tooltip("공격 1은 베이스의 '공격 1 (콤보 시작)' 필드이며 X키·좌클릭에 대응한다.")]
    public AttackData secondAttackData;

    InputAction moveAction;
    InputAction attackAction;
    InputAction secondAttackAction;
    InputAction jumpAction;

    Vector2 moveInput;

    // 공격/점프 입력은 InputAction 콜백에서 받아 여기에 걸어두기만 하고,
    // 실제 처리는 같은 프레임의 UpdateIntent에서 의도로 포장되어 베이스가 한다.
    // (Input System 콜백은 MonoBehaviour.Update보다 먼저 돌므로 프레임 지연은 없다.)
    AttackData pendingAttack;
    bool jumpRequested;

    protected override void Awake()
    {
        if (instance != null && instance != this)
            Debug.LogWarning($"씬에 PlayerController가 여러 개 있습니다. '{instance.name}'을 계속 사용합니다.");
        else
            instance = this;

        base.Awake();

        BuildInputActions();
    }

    /// <summary>
    /// PlayerInput 컴포넌트 대신 코드에서 직접 InputAction을 구성.
    /// WASD/방향키 -> 8방향 이동, X키/좌클릭 -> 공격 1, Z키 -> 공격 2, C키 -> 점프.
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

        secondAttackAction = new InputAction("AttackZ", InputActionType.Button, binding: "<Keyboard>/z");

        jumpAction = new InputAction("Jump", InputActionType.Button, binding: "<Keyboard>/c");
    }

    void OnEnable()
    {
        moveAction.performed += OnMoveChanged;
        moveAction.canceled += OnMoveChanged;
        attackAction.performed += OnAttackPerformed;
        secondAttackAction.performed += OnSecondAttackPerformed;
        jumpAction.performed += OnJumpPerformed;

        moveAction.Enable();
        attackAction.Enable();
        secondAttackAction.Enable();
        jumpAction.Enable();
    }

    void OnDisable()
    {
        moveAction.performed -= OnMoveChanged;
        moveAction.canceled -= OnMoveChanged;
        attackAction.performed -= OnAttackPerformed;
        secondAttackAction.performed -= OnSecondAttackPerformed;
        jumpAction.performed -= OnJumpPerformed;

        moveAction.Disable();
        attackAction.Disable();
        secondAttackAction.Disable();
        jumpAction.Disable();
    }

    void OnDestroy()
    {
        moveAction?.Dispose();
        attackAction?.Dispose();
        secondAttackAction?.Dispose();
        jumpAction?.Dispose();
    }

    void OnMoveChanged(InputAction.CallbackContext ctx)
    {
        moveInput = ctx.ReadValue<Vector2>();
    }

    /// <summary>X키·좌클릭. 어느 공격으로 콤보를 시작할지만 걸어두고, 판단은 베이스가 한다.</summary>
    void OnAttackPerformed(InputAction.CallbackContext ctx) => pendingAttack = firstAttackData;

    /// <summary>Z키. X키와 동일한 처리를 타되, 콤보를 시작하는 데이터만 다르다.</summary>
    void OnSecondAttackPerformed(InputAction.CallbackContext ctx) => pendingAttack = secondAttackData;

    void OnJumpPerformed(InputAction.CallbackContext ctx) => jumpRequested = true;

    /// <summary>
    /// 이번 프레임 들어온 입력을 의도로 포장한다. 공격/점프 요청은 여기서 소비(초기화)되므로
    /// 한 프레임에 두 번 눌러도 한 번으로 처리된다.
    /// </summary>
    protected override CharacterIntent UpdateIntent()
    {
        CharacterIntent intent = CharacterIntent.None;
        intent.MoveInput = moveInput;

        if (pendingAttack != null)
        {
            intent.WantsAttack = true;
            intent.AttackToStart = pendingAttack;
            pendingAttack = null;
        }

        if (jumpRequested)
        {
            intent.WantsJump = true;
            jumpRequested = false;
        }

        return intent;
    }

    /// <summary>
    /// 좌우 판단은 이동 의도의 X 성분만 본다 (위/아래로만 움직일 땐 기존 좌우를 유지, 의도가 없을 땐 그대로 유지).
    /// 이동이 잠긴 상태에서는 effectiveMoveInput이 0으로 들어오므로 방향도 바뀌지 않는다.
    /// </summary>
    protected override void UpdateFacing(Vector2 effectiveMoveInput)
    {
        if (IsGrounded && Mathf.Abs(effectiveMoveInput.x) > 0.01f)
            isFacingRight = effectiveMoveInput.x > 0f;
    }

    protected override void OnDeath()
    {
        // TODO: 사망 처리
    }
}
