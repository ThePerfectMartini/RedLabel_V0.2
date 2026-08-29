using UnityEngine;

/// <summary>
/// 숨고르기 — 물러난 자리에서 잠시 아무것도 하지 않는다. **플레이어의 반격 창이 여기다.**
///
/// 이 칸이 없으면 적은 공격이 끝나자마자 다시 붙어서, 플레이어가 "잘했다"고 느낄 순간이
/// 한 번도 생기지 않는다. 난이도가 아니라 스트레스가 오르는 전형적인 형태다.
///
/// 시간은 최소~최대 사이에서 뽑는다. 고정값이면 여러 마리가 같은 박자로 붙었다 빠지기를
/// 반복해서 기계적으로 보인다.
/// </summary>
public sealed class RecoverStep : IBehaviorStep
{
    float timer;
    float duration;

    public void OnEnter(in StepContext ctx)
    {
        timer = 0f;
        duration = Random.Range(ctx.Tuning.recoverDurationMin, ctx.Tuning.recoverDurationMax);
    }

    public StepOutcome Tick(in StepContext ctx, ref CharacterIntent intent)
    {
        timer += ctx.DeltaTime;
        return timer >= duration ? StepOutcome.Done : StepOutcome.Running;
    }

    public void OnExit() { }
}
