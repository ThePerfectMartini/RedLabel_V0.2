using System;
using UnityEngine;

/// <summary>
/// 행동 ① 접근. 배정받은 자리까지 다가가서 멈춘다. 도착하면 Done을 보고한다.
///
/// 어느 쪽 몇 번째에 설지는 밖에서 정해져 BehaviorContext.Order로 넘어온다. 조율자가 있으면 조율자가,
/// 없으면 브레인이 혼자 정한다 — 이 행동은 둘을 구분하지 않는다.
///
/// 이 행동은 <b>공격 권한을 가진 적</b>의 것이다. 권한이 없는 적은 공격 준비(StandbyBehavior)로 가므로
/// 여기서 대기 자리를 신경 쓸 필요가 없다.
///
/// [왜 목표 지점이 "플레이어 x ± 정지 거리, 플레이어와 같은 z"인가]
/// 공격 판정(CombatCore.PerformHitScan)이 좌/우(FacingDir, x축)로만 나가기 때문에 z가 어긋나 있으면
/// 아무리 가까워도 판정 반경 밖이라 맞지 않는다. 그래서 "플레이어를 향해 직선으로"가 아니라
/// 좌우 어느 한쪽의 지점을 목표로 잡아서 z를 맞춘 채로 접근한다.
/// </summary>
[Serializable]
public class ApproachBehavior : IEnemyBehavior
{
    [KoreanLabel("사거리 여유")]
    [Tooltip("공격 사거리에서 이만큼 안쪽에 멈춰 선다. 0이면 사거리와 정확히 같은 거리에 선다. " +
        "도착 판정에 데드존이 있어 경계에 딱 맞추면 사거리 밖에서 멈출 수 있으므로, 도착 판정 허용 오차보다 크게 두는 것이 안전하다.")]
    public float rangeMargin = 0.3f;

    [KoreanLabel("도착 판정 허용 오차")]
    [Tooltip("목표 지점까지 남은 거리가 이 값 이하면 도착으로 보고 멈춘다 (진동 방지용 데드존). " +
        "한 프레임에 이동하는 거리(이동 속도 x deltaTime)보다 커야 목표점 위에서 떨지 않는다. " +
        "또한 MovementCore가 크기 0.1 미만의 입력을 정지로 처리하므로 0.1보다 작게 내려도 의미가 없다.")]
    public float arrivalTolerance = 0.2f;

    // 어느 쪽에 설지를 이 행동이 기억하지 않는다. 그 판단은 조율자(또는 조율자가 없을 때 브레인)의 몫이고,
    // 매 프레임 Order로 넘어온다. 여기서 또 들고 있으면 두 벌이 어긋날 자리가 생긴다.
    public void OnEnter(in BehaviorContext ctx)
    {
    }

    public BehaviorResult Tick(in BehaviorContext ctx, ref CharacterIntent intent)
    {
        Vector3 self = ctx.Owner.Position;
        Vector3 target = ctx.TargetPosition;

        // 멈춰 설 거리는 인스펙터에 따로 두지 않고 이 적의 공격 사거리에서 파생시킨다. 공격 에셋의 사거리를
        // 바꿨는데 브레인의 거리를 같이 안 고쳐서 "붙었는데 안 맞는" 상태가 되는 것을 구조적으로 막는다.
        // Owner.AttackRange는 firstAttackData의 값이며, 에셋이 비어 있으면 CombatCore의 코드 기본값이다.
        float stopDistance = Mathf.Max(0f, ctx.Owner.AttackRange - rangeMargin);

        Vector3 goal = new Vector3(target.x + ctx.Order.Side * stopDistance, self.y, target.z);

        Vector3 offset = goal - self;
        offset.y = 0f;

        if (offset.sqrMagnitude <= arrivalTolerance * arrivalTolerance)
            return BehaviorResult.Done;

        // 정규화하지 않고 넘긴다 — MovementCore.SetMoveInput이 방향만 보고 자기 속도로 정규화한다.
        intent.MoveInput = new Vector2(offset.x, offset.z);
        return BehaviorResult.Running;
    }
}
