/// <summary>
/// 경직 — 얻어맞아 몸이 잠긴 동안. 인터럽트로만 들어온다.
///
/// 아무 의도도 내지 않는다. 어차피 몸이 잠겨 있어 무시되지만, 하던 예고나 후퇴 타이머가
/// 뒤에서 계속 도는 것을 막는 것이 진짜 목적이다. 그대로 두면 몸이 풀렸을 때 엉뚱한 시점에
/// 타이머가 끝나서 뜬금없는 행동이 나온다.
///
/// **예고 중에 맞으면 여기로 끌려오면서 공격이 취소된다.** 플레이어가 예고를 읽고 선제공격한
/// 것에 대한 보상이 이 한 줄에서 나온다.
///
/// 몸이 풀리면 Done. 얼마나 오래 잠겨 있을지는 이 스텝이 정하지 않으므로 워치독에서 제외한다.
/// </summary>
public sealed class StaggerStep : IBehaviorStep, IUnboundedStep
{
    public void OnEnter(in StepContext ctx) { }

    public StepOutcome Tick(in StepContext ctx, ref CharacterIntent intent)
    {
        return ctx.IsStaggered ? StepOutcome.Running : StepOutcome.Done;
    }

    public void OnExit() { }
}
