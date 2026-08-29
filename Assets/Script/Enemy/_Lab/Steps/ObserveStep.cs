using UnityEngine;

/// <summary>
/// 관망 — 대상이 쓰러져 있는 동안 반 걸음 물러나 기다린다. 인터럽트로만 들어온다.
///
/// **일어나자마자 다시 맞는 무한 콤보를 구조적으로 막는 칸이다.** 겉보기에는 적이 주위를
/// 둘러싸고 기다려주는 것처럼 자연스럽게 보이지만, 실제로는 공정성을 위해 명시적으로 건 규칙이다.
/// 이 장르에서 플레이어가 가장 억울해하는 순간이 정확히 여기라서, 우연에 맡기면 안 된다.
///
/// 이미 충분히 떨어져 있으면 그 자리에 선다. 필요 이상으로 물러나면 기상 후 다시 붙는 데
/// 시간이 걸려서 흐름이 끊긴다.
///
/// 대상이 얼마나 오래 누워 있을지는 이 스텝이 정하지 않으므로 워치독에서 제외한다.
/// </summary>
public sealed class ObserveStep : IBehaviorStep, IUnboundedStep
{
    public void OnEnter(in StepContext ctx) { }

    public StepOutcome Tick(in StepContext ctx, ref CharacterIntent intent)
    {
        if (!ctx.TargetIsDown)
            return StepOutcome.Done;

        float distance = Mathf.Abs(ctx.SelfPosition.x - ctx.LiveTargetPosition.x);
        if (distance < ctx.Tuning.observeClearance)
        {
            float awaySign = ctx.SelfPosition.x >= ctx.LiveTargetPosition.x ? 1f : -1f;
            intent.MoveInput = new Vector2(awaySign, 0f);
        }

        return StepOutcome.Running;
    }

    public void OnExit() { }
}
