using UnityEngine;

/// <summary>
/// 캐릭터의 "몸"을 조율하는 유일한 컴포넌트이자, <c>Update()</c>를 가진 유일한 컴포넌트.
/// 매 프레임 IIntentSource에게 의도를 묻고 Fighter / Locomotion / CharacterStateMachine에 정해진 순서로 넘긴다.
/// 그 의도가 사람 입력인지 AI인지는 구분하지 않는다.
///
/// 각 몸 컴포넌트가 자기 Update()를 갖지 않는 이유: 상태 변화는 (a) 이 Update의 명시적 호출,
/// (b) AnimationEventRelay(Animator가 비동기 호출), (c) 다른 Actor의 공격(직접 호출)으로만 일어난다.
/// 어느 것도 "이번 프레임에 다른 컴포넌트의 Update가 먼저 돌았는지"에 의존하지 않으므로 실행 순서 지정이 필요 없다.
///
/// IIntentSource가 없으면 CharacterIntent.None으로 동작한다(제자리에 서서 맞아주는 훈련 더미).
/// </summary>
[RequireComponent(typeof(Locomotion), typeof(Fighter), typeof(CharacterStateMachine))]
[RequireComponent(typeof(Health))]
public class Actor : MonoBehaviour
{
    Locomotion locomotion;
    Fighter fighter;
    CharacterStateMachine stateMachine;
    Dodger dodger;
    Parrier parrier;
    IIntentSource intentSource;

    void Awake()
    {
        locomotion = GetComponent<Locomotion>();
        fighter = GetComponent<Fighter>();
        stateMachine = GetComponent<CharacterStateMachine>();
        dodger = GetComponent<Dodger>();   // 회피는 플레이어만 가짐. 없어도 됨(훈련 더미·적)
        parrier = GetComponent<Parrier>(); // 패링도 플레이어만. 없어도 됨
        intentSource = GetComponent<IIntentSource>();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        CharacterIntent intent = intentSource != null ? intentSource.GetIntent(dt) : CharacterIntent.None;

        // 바라보는 방향은 이번 프레임의 공격이 시작되기 <b>전</b> 잠금 상태로 판단한다 —
        // "→ + X"를 한 프레임에 눌렀을 때 공격이 그 방향으로 나가야 하므로.
        bool facingLocked = stateMachine.IsMovementLocked || !fighter.MovementAllowed;
        locomotion.SetFacing(facingLocked ? Vector3.zero : intent.FacingDirection);

        // 방어(회피·패링)는 공격보다 먼저 — 같은 프레임에 같이 눌렸으면 방어가 우선.
        if (intent.WantsDodge && dodger != null)
            dodger.TryDodge();

        if (intent.WantsParry && parrier != null)
            parrier.TryParry();

        if (intent.AttackToStart != null)
            fighter.TryAttack(intent.AttackToStart); // 방금 정한 방향으로 공격이 나간다

        if (intent.WantsJump && locomotion.IsGrounded && stateMachine.CanAct)
        {
            locomotion.PrepareJump(intent.MoveInput.x);
            stateMachine.EnterJumpStart();
        }

        // 이동은 이번 프레임에 시작된 공격/점프까지 반영해 다시 계산한 잠금으로 판단한다.
        bool moveLocked = stateMachine.IsMovementLocked || !fighter.MovementAllowed;
        locomotion.MoveSpeedMultiplier = fighter.MoveSpeedMultiplier;
        locomotion.SetMoveInput(moveLocked ? Vector2.zero : intent.MoveInput);

        // 착지 엣지 감지를 위해 Tick 직전 값을 기억. LaunchedByJump/IsKnockedBackAirborne은 착지하면 꺼진다.
        bool wasJumpAirborne = locomotion.LaunchedByJump;
        bool wasKnockedAirborne = locomotion.IsKnockedBackAirborne;

        locomotion.Tick(dt);

        // 점프 착지: 진행 중 다른 동작이 없을 때만 착지 경직으로 (공중 공격 중 착지면 그 공격의 후딜이 대신).
        if (wasJumpAirborne && locomotion.IsGrounded && stateMachine.CurrentState != CharacterState.Attack)
            stateMachine.EnterJumpLand();
        // 넉백 착지는 무엇을 하고 있었든 무조건 다운.
        if (wasKnockedAirborne && !locomotion.IsKnockedBackAirborne)
            stateMachine.EnterLanded();

        fighter.Tick(dt);
        if (dodger != null)
            dodger.Tick(dt);
        if (parrier != null)
            parrier.Tick(dt);
        stateMachine.Evaluate();
    }
}
