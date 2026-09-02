using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어 입력을 <see cref="CharacterIntent"/>로 포장하는 IIntentSource 구현체. Actor가 GetComponent로 찾는다.
///
/// PlayerInput 컴포넌트나 별도 .inputactions 에셋을 쓰지 않고 코드에서 직접 바인딩한다 —
/// 키 배치가 한 파일에 다 보이는 게 이 프로젝트 규모엔 더 단순하다.
///
/// [조작] WASD·방향키 이동 / X키 공격 1(콤보 시작) / Z키 공격 2(특수) / C키 점프 / Shift키 회피 / V키 패링
/// </summary>
public class PlayerInput : MonoBehaviour, IIntentSource
{
    [KoreanLabel("공격 1 (X, 콤보 시작)")]
    public AttackData attack1Data;

    [KoreanLabel("공격 2 (Z, 특수 — 선택)")]
    [Tooltip("비워두면 Z키는 무시된다. 전용 클립이 준비되면 연결.")]
    public AttackData attack2Data;

    InputAction moveAction;
    InputAction attackAction;
    InputAction attack2Action;
    InputAction jumpAction;
    InputAction dodgeAction;
    InputAction parryAction;

    // 공격/점프/회피/패링 입력은 콜백에서 걸어두기만 하고, 같은 프레임의 GetIntent에서 소비된다.
    AttackData pendingAttack;
    bool jumpRequested;
    bool dodgeRequested;
    bool parryRequested;

    void Awake()
    {
        moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");

        attackAction = new InputAction("Attack", InputActionType.Button, binding: "<Keyboard>/x");
        attack2Action = new InputAction("Attack2", InputActionType.Button, binding: "<Keyboard>/z");
        jumpAction = new InputAction("Jump", InputActionType.Button, binding: "<Keyboard>/c");
        dodgeAction = new InputAction("Dodge", InputActionType.Button, binding: "<Keyboard>/shift");
        parryAction = new InputAction("Parry", InputActionType.Button, binding: "<Keyboard>/v");
    }

    void OnEnable()
    {
        attackAction.performed += OnAttack;
        attack2Action.performed += OnAttack2;
        jumpAction.performed += OnJump;
        dodgeAction.performed += OnDodge;
        parryAction.performed += OnParry;

        moveAction.Enable();
        attackAction.Enable();
        attack2Action.Enable();
        jumpAction.Enable();
        dodgeAction.Enable();
        parryAction.Enable();
    }

    void OnDisable()
    {
        attackAction.performed -= OnAttack;
        attack2Action.performed -= OnAttack2;
        jumpAction.performed -= OnJump;
        dodgeAction.performed -= OnDodge;
        parryAction.performed -= OnParry;

        moveAction.Disable();
        attackAction.Disable();
        attack2Action.Disable();
        jumpAction.Disable();
        dodgeAction.Disable();
        parryAction.Disable();
    }

    void OnDestroy()
    {
        moveAction?.Dispose();
        attackAction?.Dispose();
        attack2Action?.Dispose();
        jumpAction?.Dispose();
        dodgeAction?.Dispose();
        parryAction?.Dispose();
    }

    void OnAttack(InputAction.CallbackContext _) => pendingAttack = attack1Data;

    void OnAttack2(InputAction.CallbackContext _)
    {
        if (attack2Data != null) pendingAttack = attack2Data;
    }

    void OnJump(InputAction.CallbackContext _) => jumpRequested = true;

    void OnDodge(InputAction.CallbackContext _) => dodgeRequested = true;

    void OnParry(InputAction.CallbackContext _) => parryRequested = true;

    public CharacterIntent GetIntent(float deltaTime)
    {
        CharacterIntent intent = CharacterIntent.None;
        intent.MoveInput = moveAction.ReadValue<Vector2>();
        intent.FacingDirection = new Vector3(intent.MoveInput.x, 0f, 0f);

        if (pendingAttack != null)
        {
            intent.AttackToStart = pendingAttack;
            pendingAttack = null;
        }

        if (jumpRequested)
        {
            intent.WantsJump = true;
            jumpRequested = false;
        }

        if (dodgeRequested)
        {
            intent.WantsDodge = true;
            dodgeRequested = false;
        }

        if (parryRequested)
        {
            intent.WantsParry = true;
            parryRequested = false;
        }

        return intent;
    }
}
