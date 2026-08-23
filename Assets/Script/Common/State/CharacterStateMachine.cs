using System;

/// <summary>
/// 캐릭터(플레이어/적 공용)의 현재 상태. 이후 애니메이션 컨트롤러가 이 값에 따라 클립을 재생한다.
/// </summary>
public enum CharacterState
{
    Idle,
    Move,
    Attack,
    JumpStart,
    InAir,
    JumpLand,
    Stun,
    Airborne,
    Landed,
    GetUp
}

/// <summary>
/// CharacterStateMachine을 갖고 있는 컴포넌트가 구현하는 인터페이스.
/// PlayerController, EnemyController처럼 종류가 달라도
/// 이 인터페이스만 보고 현재 상태를 읽거나 구독할 수 있게 해준다 (IHittable과 같은 용도).
/// </summary>
public interface IStateMachineOwner
{
    CharacterStateMachine StateMachine { get; }
}

/// <summary>
/// 범용 캐릭터 상태 컨테이너. MonoBehaviour가 아니며, 어떤 상태로 전환할지 판단하는 규칙(우선순위 등)은 갖지 않는다.
/// 그 판단은 컨트롤러(PlayerController/EnemyController)가 매 프레임 직접 하고, 결과만 SetState로 반영한다.
/// 상태가 바뀌면 OnStateChanged가 발생하므로, 나중에 애니메이션 컨트롤러는 여기 구독해서 클립을 재생하면 된다.
/// </summary>
public class CharacterStateMachine
{
    public CharacterState CurrentState { get; private set; } = CharacterState.Idle;

    /// <summary>이전 상태, 새 상태 순서로 전달된다.</summary>
    public event Action<CharacterState, CharacterState> OnStateChanged;

    public void SetState(CharacterState newState)
    {
        if (newState == CurrentState)
            return;

        CharacterState previous = CurrentState;
        CurrentState = newState;
        OnStateChanged?.Invoke(previous, newState);
    }
}
