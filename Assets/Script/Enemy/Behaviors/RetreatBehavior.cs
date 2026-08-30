using System;
using UnityEngine;

/// <summary>
/// 행동 ③ 후퇴. 공격 직후 플레이어 반대쪽으로 물러난다. 바라보는 방향은 브레인이 플레이어 쪽으로
/// 채워두므로 뒤돌지 않고 뒷걸음질치는 모양이 된다 — CharacterIntent가 MoveInput과 FacingDirection을
/// 따로 들고 있는 이유가 여기서 처음 쓰인다.
///
/// [끝나는 조건이 둘인 이유] 목표한 거리만큼 물러나면 끝나는 것이 정상이지만, 벽이나 다른 적에
/// 막히면 그 거리를 영영 못 번다. 시간 제한이 없으면 여기서 조용히 굳는다. 막혀서 끝나는 것도
/// 실패가 아니라 정상 종료로 본다 — 다음 판단은 어차피 그때의 위치를 보고 다시 하기 때문이다.
///
/// [왜 x축으로만 물러나는가] z를 플레이어와 맞춘 채로 물러나야 다음 접근이 z를 다시 맞추는 일 없이
/// 곧바로 사거리로 들어갈 수 있다. 벨트스크롤에서 뒷걸음질은 원래 좌우 방향이기도 하다.
/// </summary>
[Serializable]
public class RetreatBehavior : IEnemyBehavior
{
    [KoreanLabel("후퇴 거리")]
    [Tooltip("이만큼 물러나면 끝난다. 플레이어와의 거리가 아니라 시작 위치에서 실제로 이동한 거리로 재기 때문에, " +
        "플레이어가 따라붙어도 언젠가는 끝난다.")]
    public float retreatDistance = 2f;

    [KoreanLabel("최대 후퇴 시간(초)")]
    [Tooltip("벽이나 다른 적에 막혀 후퇴 거리를 못 채울 때를 위한 시간 제한. 이 시간이 지나면 그 자리에서 정상 종료한다.")]
    public float maxDuration = 1.5f;

    Vector3 startPosition;
    float timer;

    public void OnEnter(in BehaviorContext ctx)
    {
        startPosition = ctx.Owner.Position;
        timer = 0f;
    }

    public BehaviorResult Tick(in BehaviorContext ctx, ref CharacterIntent intent)
    {
        Vector3 self = ctx.Owner.Position;

        Vector3 moved = self - startPosition;
        moved.y = 0f;
        if (moved.sqrMagnitude >= retreatDistance * retreatDistance)
            return BehaviorResult.Done;

        timer += ctx.DeltaTime;
        if (timer >= maxDuration)
            return BehaviorResult.Done;

        // 플레이어의 반대쪽. x가 거의 같아 방향을 정할 수 없으면 지금 바라보는 방향의 반대로 물러난다.
        float dx = self.x - ctx.TargetPosition.x;
        float away = Mathf.Abs(dx) > 0.01f ? Mathf.Sign(dx) : -Mathf.Sign(ctx.Owner.FacingDir.x);
        intent.MoveInput = new Vector2(away, 0f);

        return BehaviorResult.Running;
    }
}
