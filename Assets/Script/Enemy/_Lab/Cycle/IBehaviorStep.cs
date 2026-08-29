/// <summary>
/// 스텝이 이번 프레임의 결과로 보고하는 값. 스텝이 외부에 말할 수 있는 것은 이 셋뿐이다.
/// </summary>
public enum StepOutcome
{
    /// <summary>아직 진행 중. 다음 프레임에도 같은 스텝이 돈다.</summary>
    Running,

    /// <summary>정상적으로 끝났다. 순환의 다음 칸으로 넘어간다.</summary>
    Done,

    /// <summary>전제가 무너져서 중단한다 (예: 목표가 멀어짐). 보통 접근부터 다시 시작한다.</summary>
    Abort,
}

/// <summary>
/// 행동 순환의 한 칸. 접근 / 견제 / 예고 / 공격 / 후퇴 / 숨고르기 각각이 이걸 구현한다.
///
/// [핵심 규칙] 스텝은 **다음 스텝의 이름을 절대 모른다.** 결과(StepOutcome)만 보고하고,
/// 어디로 갈지는 BehaviorCycle이 소유한 전이 테이블이 정한다. 이 규칙 덕분에 스텝을
/// 하나씩 독립적으로 만들고 순서만 바꿔 다른 적을 조립할 수 있다.
/// 스텝끼리 서로를 참조하기 시작하면 그 순간 조립이 불가능해진다.
///
/// [또 하나의 규칙] 여기서 직접 위치를 옮기거나 공격 판정을 하지 말 것.
/// 그건 EnemyController와 MovementCore/CombatCore의 일이다. 스텝은 intent만 채운다.
///
/// MonoBehaviour가 아니다 (MovementCore/CombatCore와 같은 원칙). Unity 시간에도 의존하지
/// 않으며, 필요한 것은 전부 StepContext로 넘어온다.
/// </summary>
public interface IBehaviorStep
{
    /// <summary>이 스텝에 막 들어왔을 때 한 번. 타이머 초기화나 시작 위치 기록에 쓴다.</summary>
    void OnEnter(in StepContext ctx);

    /// <summary>
    /// 매 프레임 한 번. 이번 프레임에 하고 싶은 것을 intent에 채우고 결과를 보고한다.
    /// intent는 브레인이 미리 바라볼 방향을 채워둔 상태로 넘어오므로, 덮어쓰지 말고 필요한
    /// 필드만 건드린다.
    /// </summary>
    StepOutcome Tick(in StepContext ctx, ref CharacterIntent intent);

    /// <summary>이 스텝에서 나갈 때 한 번. 정리할 것이 있으면 여기서 한다.</summary>
    void OnExit();
}

/// <summary>
/// "이 스텝에 있는 동안 공격 토큰을 점유한다"는 표시. 예고와 공격이 구현한다.
///
/// 조율자가 토큰을 회수할 시점을 알아야 하는데, 스텝 이름 문자열로 판단하면 프리셋에서
/// 이름을 바꾸는 순간 조용히 깨진다. 그래서 타입으로 표시한다.
/// </summary>
public interface IAttackCommitStep
{
}

/// <summary>
/// "이 스텝은 오래 머물러도 정상"이라는 표시. 워치독이 건너뛴다.
///
/// 견제(토큰을 못 받으면 계속 기다린다), 경직(얼마나 맞을지 모른다), 관망(상대가 언제
/// 일어날지 모른다)은 끝나는 시점을 자기가 정하지 않는다. 이런 스텝까지 워치독에 걸면
/// 정상 동작이 매번 에러로 찍혀서, 진짜 멈춘 경우를 알아볼 수 없게 된다.
/// </summary>
public interface IUnboundedStep
{
}

/// <summary>
/// 진행률(0~1)을 밖에 보여줄 수 있는 스텝. 예고 막대를 그리기 위해 디버그 표시가 읽어간다.
/// 모든 스텝이 구현할 필요는 없다.
/// </summary>
public interface IProgressReporting
{
    float Progress01 { get; }
}
