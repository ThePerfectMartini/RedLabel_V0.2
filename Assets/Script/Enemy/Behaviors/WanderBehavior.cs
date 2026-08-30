using System;
using UnityEngine;

/// <summary>
/// 행동 ⑤ 배회. 지금 위치 근처의 임의 지점 하나를 골라 그곳까지 걸어가고 끝난다. 전투 밖에서 쓰는 행동이라
/// 플레이어를 쳐다보지 않고 가는 방향을 본다.
///
/// 전투가 시작되면(플레이어가 전투 범위 안으로 들어오면) 그 자리에서 즉시 끝낸다.
///
/// [한 번의 이동으로 끝나는 이유] "이동 -> 잠깐 정지 -> 다시 이동"을 이 안에서 반복하게 만들면
/// 배회가 스스로 끝나지 않는 행동이 되고, 그동안 브레인이 상황을 다시 볼 기회가 없어서 플레이어가
/// 다가와도 반응하지 못한다. 정지는 이미 대기가 하는 일이므로, 배회는 한 구간만 걷고 넘긴다.
/// (6단계에서 배회 -> 대기 -> 배회로 이어지면 그 자체가 "걷다 멈추다"가 된다.)
///
/// [목표를 맵 경계 안쪽으로 물리는 이유] 경계에 딱 붙은 지점을 고르면 몸 반지름 때문에 그 지점에
/// 영영 닿지 못하고 벽을 비비며 시간 제한까지 버틴다.
/// </summary>
[Serializable]
public class WanderBehavior : IEnemyBehavior
{
    [KoreanLabel("배회 반경")]
    [Tooltip("지금 위치를 기준으로 이 반경 안에서 다음 목표 지점을 고른다. 너무 작으면 제자리걸음처럼 보인다.")]
    public float radius = 4f;

    [KoreanLabel("도착 판정 허용 오차")]
    [Tooltip("목표 지점까지 남은 거리가 이 값 이하면 도착으로 본다 (진동 방지용 데드존).")]
    public float arrivalTolerance = 0.2f;

    [KoreanLabel("최대 이동 시간(초)")]
    [Tooltip("벽이나 다른 적에 막혀 목표에 못 닿을 때를 위한 시간 제한. 이 시간이 지나면 그 자리에서 정상 종료한다.")]
    public float maxDuration = 3f;

    // 맵 경계에서 안쪽으로 물릴 거리. 튜닝 값이 아니라 도달 불가능한 목표를 막는 값이라 인스펙터에 내지 않는다.
    const float BoundaryPadding = 1f;

    Vector3 destination;
    float timer;

    public void OnEnter(in BehaviorContext ctx)
    {
        destination = PickDestination(ctx.Owner.Position);
        timer = 0f;
    }

    public BehaviorResult Tick(in BehaviorContext ctx, ref CharacterIntent intent)
    {
        // 플레이어가 전투 범위 안으로 들어왔으면 걸어가던 것을 그 자리에서 그만둔다.
        // 한 구간을 끝까지 걷고 나서야 알아채면 다가온 플레이어를 몇 초씩 무시하는 것처럼 보인다.
        if (ctx.InCombat)
            return BehaviorResult.Done;

        timer += ctx.DeltaTime;
        if (timer >= maxDuration)
            return BehaviorResult.Done;

        Vector3 offset = destination - ctx.Owner.Position;
        offset.y = 0f;

        if (offset.sqrMagnitude <= arrivalTolerance * arrivalTolerance)
            return BehaviorResult.Done;

        intent.MoveInput = new Vector2(offset.x, offset.z);

        // 전투 중이 아니므로 플레이어가 아니라 가는 쪽을 본다. 브레인이 채워둔 방향을 여기서 덮어쓴다.
        intent.FacingDirection = new Vector3(offset.x, 0f, 0f);

        return BehaviorResult.Running;
    }

    /// <summary>지금 위치 기준으로 다음 목표 지점 하나를 고른다. 너무 가까운 지점은 제자리걸음처럼 보여서 제외한다.</summary>
    Vector3 PickDestination(Vector3 origin)
    {
        Vector2 direction = UnityEngine.Random.insideUnitCircle.normalized;
        float distance = UnityEngine.Random.Range(radius * 0.5f, radius);
        Vector3 point = origin + new Vector3(direction.x, 0f, direction.y) * distance;

        if (MapBounds.Instance != null)
        {
            Bounds bounds = MapBounds.Instance.Bounds;
            point.x = Mathf.Clamp(point.x, bounds.min.x + BoundaryPadding, bounds.max.x - BoundaryPadding);
            point.z = Mathf.Clamp(point.z, bounds.min.z + BoundaryPadding, bounds.max.z - BoundaryPadding);
        }

        point.y = origin.y;
        return point;
    }
}
