using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 스텝들을 순환으로 엮고, 전이와 인터럽트를 소유하는 조립 결과물.
///
/// 스텝은 자기 결과만 보고하고 다음 칸을 모른다. 그 배선을 전부 여기가 갖는다.
/// 덕분에 새 적 타입은 스텝을 새로 만들 필요 없이 Add 목록과 순서만 바꾸면 된다.
///
/// <code>
/// new BehaviorCycle()
///     .Add("접근",     new ApproachStep())
///     .Add("견제",     new PoiseStep())
///     .Add("예고",     new TelegraphStep())
///     .Add("공격",     new AttackStep())
///     .Add("후퇴",     new RetreatStep())
///     .Add("숨고르기", new RecoverStep())
///     .Chain()                                    // 위 여섯 칸을 순서대로, 마지막은 처음으로
///     .Add("경직", new StaggerStep())             // Chain 뒤에 더한 것은 사슬 밖이다
///     .On("경직", StepOutcome.Done, "접근")
///     .Interrupt(Interrupts.Staggered, "경직")
///     .Build();
/// </code>
///
/// MonoBehaviour가 아니고 Unity 시간에도 의존하지 않는다. deltaTime은 StepContext로 들어온다.
/// </summary>
public sealed class BehaviorCycle
{
    /// <summary>
    /// 어느 스텝에 있든 검사되는 끼어들기 조건. 매 프레임 호출되므로 람다 대신
    /// 정적 메서드를 넘겨서(Interrupts 클래스) 조립 시점에 한 번만 델리게이트가 만들어지게 한다.
    /// </summary>
    public delegate bool InterruptCondition(in StepContext ctx);

    sealed class Entry
    {
        public string Name;
        public IBehaviorStep Step;
        public string OnDone;
        public string OnAbort;
    }

    struct Interruption
    {
        public InterruptCondition Condition;
        public string Target;
    }

    readonly List<Entry> entries = new List<Entry>();
    readonly List<Interruption> interrupts = new List<Interruption>();

    // Chain()이 어디까지를 사슬로 묶을지. Chain 이후에 더한 스텝(경직/관망)은 사슬에 끼지 않는다.
    int chainedCount = -1;

    Entry current;
    bool hasEntered;
    float stepElapsed;
    bool built;

    // 워치독이 이미 울린 스텝. 같은 프레임마다 로그가 쏟아지는 것을 막는다.
    readonly HashSet<string> watchdogWarned = new HashSet<string>();

    public string CurrentStepName => current == null ? "(없음)" : current.Name;
    public IBehaviorStep CurrentStep => current?.Step;

    public BehaviorCycle Add(string name, IBehaviorStep step)
    {
        if (string.IsNullOrEmpty(name) || step == null)
        {
            Debug.LogError("BehaviorCycle.Add: 이름과 스텝이 모두 있어야 합니다.");
            return this;
        }

        if (Find(name) != null)
        {
            Debug.LogError($"BehaviorCycle.Add: '{name}'이 이미 등록되어 있습니다. 이름은 유일해야 합니다.");
            return this;
        }

        entries.Add(new Entry { Name = name, Step = step });
        return this;
    }

    /// <summary>
    /// 지금까지 등록된 스텝을 등록 순서대로 Done → 다음으로 잇고, 마지막은 처음으로 되돌린다.
    /// 기본 순환은 이 한 줄로 완성되고, 예외 전이만 On으로 따로 적으면 된다.
    /// </summary>
    public BehaviorCycle Chain()
    {
        if (entries.Count == 0)
        {
            Debug.LogError("BehaviorCycle.Chain: 엮을 스텝이 없습니다.");
            return this;
        }

        chainedCount = entries.Count;

        for (int i = 0; i < chainedCount; i++)
            entries[i].OnDone = entries[(i + 1) % chainedCount].Name;

        return this;
    }

    /// <summary>Chain이 깔아둔 기본 전이를 덮어쓰거나, 사슬 밖 스텝의 전이를 지정한다.</summary>
    public BehaviorCycle On(string from, StepOutcome outcome, string to)
    {
        Entry entry = Find(from);
        if (entry == null)
        {
            Debug.LogError($"BehaviorCycle.On: '{from}'이 등록되어 있지 않습니다.");
            return this;
        }

        if (outcome == StepOutcome.Done) entry.OnDone = to;
        else if (outcome == StepOutcome.Abort) entry.OnAbort = to;
        else Debug.LogError("BehaviorCycle.On: Running에는 전이를 걸 수 없습니다.");

        return this;
    }

    /// <summary>
    /// 어느 스텝에 있든 조건이 참이면 target으로 끌고 간다. 등록 순서가 곧 우선순위다.
    /// 이미 target에 있으면 다시 들어가지 않으므로, target 스텝이 스스로 Done을 낼 때까지 유지된다.
    /// </summary>
    public BehaviorCycle Interrupt(InterruptCondition condition, string target)
    {
        if (condition == null || string.IsNullOrEmpty(target))
        {
            Debug.LogError("BehaviorCycle.Interrupt: 조건과 대상이 모두 있어야 합니다.");
            return this;
        }

        interrupts.Add(new Interruption { Condition = condition, Target = target });
        return this;
    }

    /// <summary>
    /// 배선을 검증하고 순환을 완성한다. 갈 곳 없는 전이를 조용히 두지 않고 여기서 전부 찍는다.
    /// </summary>
    public BehaviorCycle Build()
    {
        if (entries.Count == 0)
        {
            Debug.LogError("BehaviorCycle.Build: 스텝이 하나도 없습니다.");
            return this;
        }

        if (chainedCount < 0)
            Debug.LogWarning("BehaviorCycle.Build: Chain()을 부르지 않아 기본 전이가 비어 있습니다.");

        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];

            if (string.IsNullOrEmpty(entry.OnDone))
                Debug.LogWarning($"BehaviorCycle: '{entry.Name}'이 Done일 때 갈 곳이 없습니다. 그 자리에 머뭅니다.");
            else if (Find(entry.OnDone) == null)
                Debug.LogError($"BehaviorCycle: '{entry.Name}' → '{entry.OnDone}'(Done) 대상이 없습니다.");

            if (!string.IsNullOrEmpty(entry.OnAbort) && Find(entry.OnAbort) == null)
                Debug.LogError($"BehaviorCycle: '{entry.Name}' → '{entry.OnAbort}'(Abort) 대상이 없습니다.");
        }

        for (int i = 0; i < interrupts.Count; i++)
        {
            if (Find(interrupts[i].Target) == null)
                Debug.LogError($"BehaviorCycle: 인터럽트 대상 '{interrupts[i].Target}'이 없습니다.");
        }

        current = entries[0];
        hasEntered = false;
        built = true;
        return this;
    }

    /// <summary>
    /// 한 프레임 진행한다. 인터럽트를 먼저 보고, 그 다음 현재 스텝을 한 번 돌린다.
    /// 인터럽트로 스텝이 바뀌면 바뀐 스텝이 같은 프레임에 바로 한 번 돈다.
    /// </summary>
    public void Tick(in StepContext ctx, ref CharacterIntent intent)
    {
        if (!built || current == null) return;

        for (int i = 0; i < interrupts.Count; i++)
        {
            if (!interrupts[i].Condition(in ctx)) continue;

            // 이미 그 스텝이면 재진입하지 않는다. 재진입하면 OnEnter가 매 프레임 불려서
            // 타이머가 영영 0으로 리셋된다.
            if (current.Name != interrupts[i].Target)
                Transition(interrupts[i].Target, in ctx);

            break;
        }

        EnsureEntered(in ctx);

        stepElapsed += ctx.DeltaTime;
        if (TripWatchdog(in ctx)) return;

        StepOutcome outcome = current.Step.Tick(in ctx, ref intent);
        if (outcome == StepOutcome.Running) return;

        string next = outcome == StepOutcome.Done ? current.OnDone : current.OnAbort;

        // Abort에 전이가 없으면 Done 쪽으로 흘려보낸다. 사슬만 엮고 예외를 안 적은 스텝이
        // 갈 곳을 잃고 멈춰버리는 것을 막는다.
        if (string.IsNullOrEmpty(next) && outcome == StepOutcome.Abort)
            next = current.OnDone;

        if (string.IsNullOrEmpty(next)) return;

        Transition(next, in ctx);
    }

    /// <summary>
    /// 스텝이 비정상적으로 오래 머무르면 순환의 첫 칸으로 되돌린다.
    /// CharacterControllerBase의 actionPhaseTimeout과 같은 성격의 안전장치다 —
    /// 조용히 멈춰 서서 원인을 못 찾는 상황을 만들지 않는 것이 목적이라, 튜닝용 값이 아니다.
    /// </summary>
    bool TripWatchdog(in StepContext ctx)
    {
        // 끝나는 시점을 자기가 정하지 않는 스텝(견제/경직/관망)은 오래 머무는 것이 정상이다.
        if (current.Step is IUnboundedStep) return false;

        float limit = ctx.Tuning == null ? 0f : ctx.Tuning.stepWatchdogSeconds;
        if (limit <= 0f || stepElapsed < limit) return false;

        if (watchdogWarned.Add(current.Name))
        {
            Debug.LogError($"{ctx.Owner.name}: 스텝 '{current.Name}'이 {limit:F1}초를 넘겨 " +
                           $"'{entries[0].Name}'으로 되돌립니다. 그 스텝이 Done/Abort를 못 내는 조건을 확인하세요.");
        }

        Transition(entries[0].Name, in ctx);
        return true;
    }

    void EnsureEntered(in StepContext ctx)
    {
        if (hasEntered) return;

        hasEntered = true;
        stepElapsed = 0f;
        current.Step.OnEnter(in ctx);
    }

    void Transition(string target, in StepContext ctx)
    {
        Entry next = Find(target);
        if (next == null) return;

        if (hasEntered)
            current.Step.OnExit();

        current = next;
        hasEntered = true;
        stepElapsed = 0f;
        current.Step.OnEnter(in ctx);
    }

    Entry Find(string name)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Name == name)
                return entries[i];
        }
        return null;
    }
}
