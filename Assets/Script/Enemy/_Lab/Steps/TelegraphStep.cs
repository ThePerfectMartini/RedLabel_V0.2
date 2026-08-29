using UnityEngine;

/// <summary>
/// 예고 — 공격 직전에 아무것도 하지 않고 서 있는 구간. 플레이어가 읽고 대응할 시간이다.
///
/// **난이도 조절은 데미지가 아니라 이 길이로 한다.** 같은 적을 예고 0.6초에서 0.3초로 줄이면
/// 체감 난이도가 정직하게 올라가고, 플레이어는 "빨라졌다"고 느끼지 자기가 손해봤다고 느끼지 않는다.
///
/// 등 뒤에서 접근한 경우 조율자가 TelegraphScale로 배수를 곱해준다 — 화면에서 읽기 어려운
/// 방향의 공격은 그만큼 더 길게 줘야 공정하다.
///
/// [헛침에 대해] 예고가 끝났는데 대상이 사거리를 벗어났어도 아주 멀지 않으면 그냥 친다.
/// 절대 헛치지 않는 적은 정확도가 완벽해 보여서 오히려 기계적으로 느껴진다.
/// 어느 정도의 헛침은 자연스러움의 일부이고, 플레이어가 "피했다"고 느끼는 순간이기도 하다.
/// </summary>
public sealed class TelegraphStep : IBehaviorStep, IAttackCommitStep, IProgressReporting
{
    float timer;
    float duration;

    public float Progress01 => duration <= 0f ? 1f : Mathf.Clamp01(timer / duration);

    public void OnEnter(in StepContext ctx)
    {
        timer = 0f;
        duration = ctx.Tuning.telegraphDuration * Mathf.Max(1f, ctx.Order.TelegraphScale);
    }

    public StepOutcome Tick(in StepContext ctx, ref CharacterIntent intent)
    {
        timer += ctx.DeltaTime;

        if (timer < duration)
            return StepOutcome.Running;

        // 아주 멀어졌을 때만 접는다. 조금 벗어난 정도면 그대로 쳐서 헛친다.
        if (ctx.DistanceToTarget > ctx.Tuning.telegraphAbortDistance)
            return StepOutcome.Abort;

        return StepOutcome.Done;
    }

    public void OnExit() { }
}
