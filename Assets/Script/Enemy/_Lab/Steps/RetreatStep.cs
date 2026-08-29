using UnityEngine;

/// <summary>
/// 후퇴 — 공격이 끝나면 맞았든 헛쳤든 대상 반대쪽으로 물러난다.
///
/// 성패와 무관하게 항상 물러나는 것이 핵심이다. "맞으면 더 밀어붙이고 빗나가면 물러난다" 같은
/// 조건을 달면 플레이어가 맞을수록 압박이 강해져서, 한 번 실수하면 회복할 수 없게 된다.
///
/// 바라보는 방향은 브레인이 이미 대상 쪽으로 채워뒀으므로 뒷걸음질처럼 보인다
/// (EnemyController가 방향을 이동과 분리해둔 덕에 별도 처리가 필요 없다).
///
/// 좌우로만 물러난다. z까지 같이 빼면 대형이 흐트러져서 다음 접근 경로가 매번 달라진다.
/// </summary>
public sealed class RetreatStep : IBehaviorStep
{
    Vector3 startPosition;
    float timer;

    public void OnEnter(in StepContext ctx)
    {
        startPosition = ctx.SelfPosition;
        timer = 0f;
    }

    public StepOutcome Tick(in StepContext ctx, ref CharacterIntent intent)
    {
        timer += ctx.DeltaTime;

        float dx = ctx.SelfPosition.x - startPosition.x;
        float dz = ctx.SelfPosition.z - startPosition.z;
        float retreated = Mathf.Sqrt(dx * dx + dz * dz);

        if (retreated >= ctx.Tuning.retreatDistance)
            return StepOutcome.Done;

        // 시간 안전장치가 반드시 필요하다. 벽이나 다른 적에 막히면 아무리 밀어도 거리가
        // 안 벌어져서 영영 후퇴만 하게 된다.
        if (timer >= ctx.Tuning.retreatTimeout)
            return StepOutcome.Done;

        float awaySign = ctx.SelfPosition.x >= ctx.TargetPosition.x ? 1f : -1f;
        intent.MoveInput = new Vector2(awaySign, 0f);
        return StepOutcome.Running;
    }

    public void OnExit() { }
}
