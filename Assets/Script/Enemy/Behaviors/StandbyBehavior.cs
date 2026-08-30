using System;
using UnityEngine;

/// <summary>
/// 행동 ⑥ 공격 준비. 조율자에게 공격 권한을 받지 못한 적이 안전한 거리에서 자기 자리를 지키며 기다린다.
/// 권한이 넘어오는 순간 Done을 보고하고, 브레인이 곧바로 다음 행동(사거리 안이면 공격)을 고른다.
///
/// [그냥 서 있는 것이 아니다] 이 상태의 적은 계속 사거리를 재고 있다. 플레이어가 다가와서 이 적이
/// 가장 가까워지면 조율자가 권한을 넘기고, 그 프레임에 바로 공격으로 이어진다. 대기를 한 번 더 거치지
/// 않기 때문에 "기다리고 있다가 덮친다"가 된다.
///
/// [플레이어를 실시간으로 쫓지 않는다] 자리 계산을 매 프레임 하면 세 마리가 플레이어에게 딱 붙어
/// 같은 속도로 따라다니는, 기계 같고 압박만 심한 그림이 된다. 그래서 목표 지점은 일정 텀마다 한 번만
/// 갱신하고, 그 사이에는 낡은 목표를 향해 움직인다. 이미 자리 근처에 있으면 아예 움직이지 않는다.
///
/// [적마다 박자와 목표가 다르다] 주기가 고정이고 목표가 정확한 한 점이면, 같은 순간에 대기로 들어온
/// 적들이 영영 같은 타이밍에 같은 지점으로 움직여서 한 몸처럼 보인다. 그래서 주기는 매번 범위 안에서
/// 새로 뽑고, 목표 지점도 조금씩 흩뜨린다. 흔들림이 허용 오차보다 작게 나오는 개체는 그 주기 동안
/// 아예 움직이지 않으므로, 멈춰 있는 놈과 움직이는 놈이 자연스럽게 섞인다.
///
/// [안전 거리는 직접 입력한다] 플레이어 공격 데이터에서 값을 읽어오지 않는다. 플레이어 공격이 바뀔
/// 때마다 적의 대기 위치가 따라 움직이면 적을 튜닝하는 사람이 통제할 수 없는 값이 되기 때문이다.
/// 대신 플레이어의 공격이 닿지 않을 만큼 넉넉히 잡는다.
///
/// [끝나는 시점을 자기가 정하지 않는다] 권한이 오기 전까지는 계속 Running이다. 그래서
/// IUnboundedBehavior를 달아 워치독에서 제외한다.
/// </summary>
[Serializable]
public class StandbyBehavior : IEnemyBehavior, IUnboundedBehavior
{
    [KoreanLabel("대기 거리")]
    [Tooltip("플레이어로부터 x축으로 이만큼 떨어진 곳에서 기다린다. 공격자보다 뒤쪽이며, " +
        "플레이어의 공격이 닿지 않을 만큼 넉넉히 잡는다 (플레이어 공격 수치를 읽어오지 않고 직접 정하는 값).")]
    public float standbyDistance = 4f;

    [KoreanLabel("위/아래 자리 z 거리")]
    [Tooltip("위 자리와 아래 자리가 플레이어의 z에서 얼마나 떨어질지. 맵의 z 폭보다 크면 경계에 몰리므로 맵에 맞춰 조절한다.")]
    public float standbyZDistance = 3f;

    [KoreanLabel("자리 갱신 간격 최소(초)")]
    [Tooltip("목표 지점을 다시 계산하는 주기의 최소값. 클수록 플레이어를 느슨하게 따라다닌다.")]
    public float repositionIntervalMin = 0.8f;

    [KoreanLabel("자리 갱신 간격 최대(초)")]
    [Tooltip("주기의 최대값. 최소값과 벌려 둘수록 적들이 서로 다른 박자로 움직여서 한 몸처럼 보이지 않는다. " +
        "최소값과 같게 두면 전부 같은 타이밍에 움직인다.")]
    public float repositionIntervalMax = 1.8f;

    [KoreanLabel("자리 흔들림")]
    [Tooltip("갱신할 때마다 목표 지점을 이 반경 안에서 무작위로 흩뜨린다. " +
        "0이면 매번 정확히 같은 점으로 모여서 대열이 자로 잰 듯 반듯해진다.")]
    public float slotJitter = 0.8f;

    [KoreanLabel("자리 허용 오차")]
    [Tooltip("자리와의 거리가 이 값 이하면 움직이지 않는다. 작게 두면 계속 종종거리고, 크게 두면 대충 그 근처에 머문다.")]
    public float slotTolerance = 1f;

    // 맵 경계에서 안쪽으로 물릴 거리. 튜닝 값이 아니라 도달 불가능한 목표를 막는 값이라 인스펙터에 내지 않는다.
    const float BoundaryPadding = 1f;

    Vector3 goal;
    bool hasGoal;
    float timer;
    float interval;

    public void OnEnter(in BehaviorContext ctx)
    {
        // 들어오자마자 한 번 잡는다. 방금 공격하고 물러난 자리에서 한 텀을 통째로 서 있으면
        // 플레이어 바로 앞에 멍하니 있는 그림이 된다.
        hasGoal = false;
        interval = 0f;
        timer = 0f;
    }

    public BehaviorResult Tick(in BehaviorContext ctx, ref CharacterIntent intent)
    {
        // 권한이 오면 즉시 끝낸다. 다음이 무엇인지는 여전히 브레인이 정한다.
        if (ctx.CanAttack)
            return BehaviorResult.Done;

        timer += ctx.DeltaTime;
        if (timer >= interval)
        {
            timer = 0f;

            // 다음 박자는 매번 새로 뽑는다. 고정 주기를 쓰면 같은 순간에 대기로 들어온 적들이 영영
            // 같은 타이밍에 함께 움직여서 한 몸처럼 보인다. 매번 다시 뽑으면 저절로 어긋난 채로 유지된다.
            interval = UnityEngine.Random.Range(repositionIntervalMin, repositionIntervalMax);

            goal = SlotPosition(ctx);
            hasGoal = true;
        }

        if (!hasGoal)
            return BehaviorResult.Running;

        Vector3 offset = goal - ctx.Owner.Position;
        offset.y = 0f;

        // 자리 근처면 가만히 있는다. 이 정지 구간이 플레이어에게 "지금은 저쪽이 안 온다"를 읽히게 한다.
        if (offset.sqrMagnitude > slotTolerance * slotTolerance)
            intent.MoveInput = new Vector2(offset.x, offset.z);

        return BehaviorResult.Running;
    }

    /// <summary>
    /// 배정받은 자리의 월드 좌표. 세 자리 모두 공격자와 같은 쪽, 공격자보다 뒤에 있고 z만 다르다.
    /// 자리를 못 받았으면(넷째부터) 뒤 자리를 더 물러난 곳에 잡아 서로 겹치지 않게 한다.
    /// </summary>
    Vector3 SlotPosition(in BehaviorContext ctx)
    {
        Vector3 target = ctx.TargetPosition;

        float z = ctx.Order.Slot switch
        {
            StandbySlot.Up => target.z + standbyZDistance,
            StandbySlot.Down => target.z - standbyZDistance,
            _ => target.z,
        };

        float distance = ctx.Order.Slot == StandbySlot.None
            ? standbyDistance * 1.5f
            : standbyDistance;

        Vector3 point = new Vector3(
            target.x + ctx.Order.Side * distance,
            ctx.Owner.Position.y,
            z);

        // 갱신할 때마다 목표를 조금씩 흩뜨린다. 정확한 한 점으로 모으면 대열이 자로 잰 듯 반듯해지고,
        // 자리를 이미 잡은 적은 다시는 움직이지 않아서 완전히 굳은 것처럼 보인다.
        // 흔들림이 허용 오차보다 작게 나오는 경우가 섞이므로 "가만히 있기도 하고 움직이기도 하는" 그림이 된다.
        Vector2 jitter = UnityEngine.Random.insideUnitCircle * slotJitter;
        point.x += jitter.x;
        point.z += jitter.y;

        return ClampIntoMap(point);
    }

    /// <summary>목표 지점을 맵 경계 안쪽으로 물린다. 맵 밖을 목표로 잡으면 영영 도착하지 못한다.</summary>
    static Vector3 ClampIntoMap(Vector3 point)
    {
        if (MapBounds.Instance == null) return point;

        Bounds bounds = MapBounds.Instance.Bounds;
        point.x = Mathf.Clamp(point.x, bounds.min.x + BoundaryPadding, bounds.max.x - BoundaryPadding);
        point.z = Mathf.Clamp(point.z, bounds.min.z + BoundaryPadding, bounds.max.z - BoundaryPadding);

        return point;
    }
}
