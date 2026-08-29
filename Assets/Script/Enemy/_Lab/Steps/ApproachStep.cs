using UnityEngine;

/// <summary>
/// 접근 — 조율자가 배정한 슬롯까지 이동한다. 도착하면 Done.
///
/// 목표 지점은 브레인이 계산해서 SlotPosition으로 넘겨준다 (반응 지연과 z 오차가 이미 반영된 값).
/// 이 스텝은 "거기까지 간다"만 안다. 어디가 왜 그 자리인지는 조율자의 일이다.
/// </summary>
public sealed class ApproachStep : IBehaviorStep
{
    public void OnEnter(in StepContext ctx) { }

    public StepOutcome Tick(in StepContext ctx, ref CharacterIntent intent)
    {
        Vector3 toSlot = ctx.SlotPosition - ctx.SelfPosition;
        toSlot.y = 0f;

        float tolerance = ctx.Tuning.arrivalTolerance;
        if (toSlot.sqrMagnitude <= tolerance * tolerance)
            return StepOutcome.Done;

        Vector3 direction = toSlot.normalized;
        intent.MoveInput = new Vector2(direction.x, direction.z);
        return StepOutcome.Running;
    }

    public void OnExit() { }
}
