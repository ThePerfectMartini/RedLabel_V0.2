/// <summary>
/// 어느 스텝에 있든 검사되는 끼어들기 조건들.
///
/// 람다가 아니라 정적 메서드로 두는 이유는 조립 시점(Awake)에 델리게이트가 한 번만
/// 만들어지게 하기 위해서다. 매 프레임 호출되는 자리라 여기서 할당이 생기면 곤란하다.
/// </summary>
public static class Interrupts
{
    /// <summary>얻어맞아 몸이 잠겼다. 하던 것을 전부 접는다.</summary>
    public static bool Staggered(in StepContext ctx) => ctx.IsStaggered;

    /// <summary>대상이 쓰러졌다. 달려들지 않고 물러나 기다린다.</summary>
    public static bool TargetDown(in StepContext ctx) => ctx.TargetIsDown;
}

/// <summary>
/// 조립된 행동 순환들. **여기가 순환을 조립하는 자리다.**
///
/// 스텝은 서로를 모르고 다음 칸의 이름도 모르므로, 새 적 타입을 만드는 일은 여기에
/// 메서드를 하나 더 쓰는 것으로 끝난다. 스텝 코드는 건드리지 않는다.
///
/// 예를 들어:
/// - 물러나지 않고 계속 붙는 적 → Retreat와 Recover를 Add에서 빼면 된다
/// - 예고 없이 기습하는 적      → Telegraph를 빼면 된다 (그만큼 불공정해진다)
/// - 절대 먼저 치지 않는 적     → Telegraph 이후를 빼고 Poise에서 순환을 닫으면 된다
///
/// 순서를 바꾸는 것만으로 성격이 바뀌고, 잘못 엮으면 Build()가 무엇이 어디로 못 가는지
/// 콘솔에 찍어준다.
/// </summary>
public static class EnemyCyclePresets
{
    public const string Approach = "접근";
    public const string Poise = "견제";
    public const string Telegraph = "예고";
    public const string Attack = "공격";
    public const string Retreat = "후퇴";
    public const string Recover = "숨고르기";
    public const string Stagger = "경직";
    public const string Observe = "관망";

    /// <summary>
    /// 기본 잡졸. 접근 → 견제 → 예고 → 공격 → 후퇴 → 숨고르기를 돌고 다시 접근으로.
    /// 경직과 관망은 사슬 밖에 두고 인터럽트로만 들어온다.
    ///
    /// 전제가 무너지는 경우(대상이 멀어짐, 공격이 시작되지 않음)는 전부 접근으로 되돌린다.
    /// 중간부터 다시 이어붙이면 어긋난 상태에서 예고에 들어가는 경우가 생긴다.
    /// </summary>
    public static BehaviorCycle Grunt()
    {
        return new BehaviorCycle()
            .Add(Approach, new ApproachStep())
            .Add(Poise, new PoiseStep())
            .Add(Telegraph, new TelegraphStep())
            .Add(Attack, new AttackStep())
            .Add(Retreat, new RetreatStep())
            .Add(Recover, new RecoverStep())
            .Chain()

            .Add(Stagger, new StaggerStep())
            .Add(Observe, new ObserveStep())

            .On(Poise, StepOutcome.Abort, Approach)
            .On(Telegraph, StepOutcome.Abort, Approach)
            .On(Attack, StepOutcome.Abort, Approach)
            .On(Stagger, StepOutcome.Done, Approach)
            .On(Observe, StepOutcome.Done, Approach)

            // 등록 순서가 우선순위다. 얻어맞은 적은 관망할 처지가 아니므로 경직이 먼저다.
            .Interrupt(Interrupts.Staggered, Stagger)
            .Interrupt(Interrupts.TargetDown, Observe)

            .Build();
    }
}
