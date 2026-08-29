using UnityEngine;

/// <summary>
/// 견제 — 슬롯에 서서 빈틈을 재는 구간. 순환에서 가장 오래 머무는 칸이고,
/// "적이 여러 마리인데도 한 번에 하나만 위협적"이라는 성질이 실제로 만들어지는 자리다.
///
/// 예고로 넘어가려면 **조율자에게 토큰을 받아야 한다.** 토큰이 없는 적(압박자)은 여기서
/// 계속 기다린다. 자리를 지키며 도주로를 막을 뿐 절대 공격하지 않는다.
///
/// 견제 시간은 개체마다 최소~최대 사이에서 뽑는다. 같은 성격의 적들이 같은 순간에
/// 준비를 마치고 한꺼번에 들어오는 "합창"을 막기 위한 것이다.
///
/// 오래 머무는 것이 정상이므로 워치독에서 제외한다(IUnboundedStep).
/// </summary>
public sealed class PoiseStep : IBehaviorStep, IUnboundedStep
{
    float timer;
    float duration;

    public void OnEnter(in StepContext ctx)
    {
        timer = 0f;
        duration = Random.Range(ctx.Tuning.poiseDurationMin, ctx.Tuning.poiseDurationMax);
    }

    public StepOutcome Tick(in StepContext ctx, ref CharacterIntent intent)
    {
        // 대상이 움직여 슬롯이 멀어졌으면 접근부터 다시. 도착 오차보다 넉넉한 값을 쓰기 때문에
        // 접근과 견제를 오가며 덜덜 떨지 않는다.
        if (ctx.DistanceToSlot > ctx.Tuning.reapproachDistance)
            return StepOutcome.Abort;

        timer += ctx.DeltaTime;

        // 토큰이 없으면 여기서 끝이다. 이 한 줄이 "동시에 하나만 친다"를 만든다.
        if (!ctx.Order.HasAttackToken)
            return StepOutcome.Running;

        // 대상이 후딜이나 착지 경직을 보이면 재던 것을 접고 곧바로 들어간다.
        if (ctx.Tuning.punishOpenings && ctx.TargetIsOpen && ctx.IsInAttackRange)
            return StepOutcome.Done;

        if (timer >= duration && ctx.IsInAttackRange)
            return StepOutcome.Done;

        return StepOutcome.Running;
    }

    public void OnExit() { }
}
