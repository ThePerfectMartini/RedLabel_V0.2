using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 설계서 5장의 <b>유일한 지상 이동 노드</b>. 공격 접근 / 자리 재정렬 / 공격 후 느린 이동 / 피격 후 이탈은
/// 별개의 노드가 아니라 이 노드에 서로 다른 <see cref="MoveProfile"/>을 물린 것이다. 갈리는 축은 셋뿐이다:
///
///   목표를 어디로 잡나(MoveGoal) · 언제 다시 보나(MoveTracking) · 어떻게 가나(MovePath)
///
/// 설계서는 이탈을 점프로 그렸지만 지상 이동으로 구현했다 — 그래서 이 프로필 하나로 표현되고
/// 점프 클립이 필요 없다. 아직 안 만든 점프대쉬는 공중 제어가 전혀 달라 여기 섞지 않고 별도 노드로 뺀다.
///
/// 반환값: 목표 도달 또는 이동 거리 소진 → Success / 최대 지속시간 초과 → Failure(상위가 행동을 다시 고른다).
/// </summary>
public class MoveToTargetNode : BTNode
{
    // 경로 미리보기를 몇 걸음, 몇 초 간격으로 굴려볼지. 기즈모 전용이라 정확도보다 가벼움이 우선.
    const int PreviewSteps = 80;
    const float PreviewStepTime = 0.04f;

    readonly MoveProfile profile;

    Vector3 goalPosition;   // 지금 향하고 있는 목표 좌표(xz). 추적 방식에 따라 갱신 주기가 다르다
    Vector3 trackedTarget;  // 목표를 잡은 순간의 플레이어 위치. 반원의 공전 중심이 된다
    Vector3 startPosition;  // 이동 거리 제한을 재는 기준점
    Vector3 heading;        // 지금 실제로 가고 있는 방향(정규화). 선회 속도 제한을 거친 결과
    float refreshCountdown; // '지연 실시간'의 다음 갱신까지 남은 시간
    float elapsed;
    float orbitSign;        // 반원을 어느 쪽으로 돌지(+1 / -1). 시작할 때 정하고 도는 내내 바꾸지 않는다
    float stepBackSign;     // 느린 이동이 뒤로 갈지(-1) 앞으로 갈지(+1). 시작할 때 한 번 정한다
    float orbitAngleDeg;    // 원 둘레에서 고른 목적지의 각도(+z 기준). 시작할 때 한 번 뽑는다
    bool arcAroundTarget;   // 원호 경로에서 실제로 돌지(길이 막힘), 곧장 갈지(길이 비어 있음)
    float goalSideSign;     // 지금 목표가 플레이어의 어느 쪽인지. 여기가 뒤집히면 경로를 새로 짠다
    bool turningIn;         // ㄱ자 경로에서 z를 다 맞추고 꺾어 들어가는 단계인지

    public MoveToTargetNode(MoveProfile profile)
    {
        this.profile = profile;
    }

    /// <summary>지금 향하고 있는 목표 좌표. 디버그 표시용.</summary>
    public Vector3 GoalPosition => goalPosition;

    /// <summary>이 이동이 무엇을 노리는지. 디버그 표시용.</summary>
    public MoveGoal Goal => profile.goal;

    /// <summary>
    /// 목적지를 고르는 원의 반지름(= 비켜 돌 때 유지하는 거리).
    /// 원을 쓰지 않는 프로필이면 0 — 디버그 표시가 0인지만 보고 그릴지 말지 정할 수 있게.
    /// </summary>
    public float OrbitRadius => profile.path == MovePath.SemiCircle || profile.goal == MoveGoal.OrbitPoint
        ? profile.orbitRadius
        : 0f;

    /// <summary>
    /// 그 원의 중심. 실시간 플레이어 위치가 아니라 <b>목표를 잡은 순간</b>의 위치다 —
    /// 스냅샷 추적이면 플레이어가 움직여도 원은 제자리에 남는다. 디버그 표시용.
    /// </summary>
    public Vector3 OrbitCenter => trackedTarget;

    /// <summary>
    /// 선회 속도 제한이 만들어내는 곡선의 반지름. 이 값이 곧 "목표선보다 얼마나 앞에서 꺾기 시작해야
    /// 넘어가지 않고 접선으로 안착하는가"이기도 하다 — 반지름 r인 호로 90도 돌면 딱 r만큼 전진하므로.
    /// </summary>
    public float TurnRadius => profile.turnRateDegPerSecond > 0f && profile.speed > 0f
        ? profile.speed / (profile.turnRateDegPerSecond * Mathf.Deg2Rad)
        : 0f;

    protected override void OnEnter(MeleeEnemyContext context)
    {
        startPosition = context.FlatSelfPosition;
        elapsed = 0f;
        refreshCountdown = profile.trackingRefreshInterval;
        heading = Vector3.zero; // 첫 프레임엔 원하는 방향을 그대로 쓴다(시작하자마자 휘는 것 방지)
        turningIn = false;

        // 순서가 있다. 목적지 각도를 먼저 뽑아야 목표 좌표가 나오고,
        // 목표 좌표가 있어야 어느 쪽으로 도는 것이 가까운지 정할 수 있다.
        DecideStepBackSign(context);
        DecideOrbitAngle(context);
        CaptureGoal(context);
        DecideOrbitSign(context);
        DecideArcMode(context);
    }

    protected override BTStatus OnTick(MeleeEnemyContext context, float deltaTime)
    {
        if (!context.HasTarget) return BTStatus.Failure;

        elapsed += deltaTime;
        if (profile.timeout > 0f && elapsed >= profile.timeout)
            return BTStatus.Failure;

        UpdateGoal(context, deltaTime);

        // 이동 거리 제한이 먼저다 — 느린 이동은 "정해진 거리만큼만" 가고 목표 도달 여부를 따지지 않는다.
        if (profile.stopAfterDistance > 0f
            && Vector3.Distance(context.FlatSelfPosition, startPosition) >= profile.stopAfterDistance)
            return BTStatus.Success;

        if (HasArrived(context))
            return BTStatus.Success;

        Vector3 self = context.FlatSelfPosition;
        Vector3 desired = ResolveDirection(self, goalPosition, trackedTarget, ref turningIn);
        if (desired.sqrMagnitude < 0.000001f)
            return BTStatus.Running; // 갈 방향이 없는 프레임. 제자리.

        heading = Steer(heading, desired.normalized, goalPosition - self, deltaTime);
        context.RequestMove(heading, profile.speed);
        return BTStatus.Running;
    }

    // ===== 목표 갱신 =====

    void UpdateGoal(MeleeEnemyContext context, float deltaTime)
    {
        // 플레이어가 나를 지나쳐 좌우가 뒤집힌 순간은 "목표가 조금 움직인 것"이 아니라 경로를 새로 짜야 하는 사건이다.
        // 추적 주기를 기다리지 않고 즉시 반응하고(낡은 목표로 0.15초를 더 달리면 반대쪽으로 크게 벗어난다),
        // 돌던 곡선도 버린다 — 선회 제한을 그대로 걸면 반대편으로 넘어가느라 큰 호를 그리며 빙 돌게 된다.
        if (profile.goal == MoveGoal.AttackSide
            && profile.tracking != MoveTracking.Snapshot
            && context.NearSideSign != goalSideSign)
        {
            CaptureGoal(context);
            refreshCountdown = profile.trackingRefreshInterval;

            heading = Vector3.zero; // 다음 프레임엔 새 목표 방향을 곧장 쓴다(선회 제한 한 번 건너뜀)
            turningIn = false;      // ㄱ자 경로를 처음부터 다시
            return;
        }

        switch (profile.tracking)
        {
            case MoveTracking.Realtime:
                CaptureGoal(context);
                break;

            case MoveTracking.Delayed:
                // 갱신 주기 동안은 낡은 좌표로 달린다 = 플레이어보다 살짝 뒤처져 따라오는 느낌.
                refreshCountdown -= deltaTime;
                if (refreshCountdown <= 0f)
                {
                    CaptureGoal(context);
                    refreshCountdown = profile.trackingRefreshInterval;
                }
                break;

            case MoveTracking.Snapshot:
                // OnEnter에서 한 번 찍은 좌표를 끝까지 쓴다. 플레이어가 어디로 가든 무시.
                break;
        }
    }

    /// <summary>
    /// 목표를 새로 잡는다. <b>목표 좌표 하나만 잡는 게 아니라, 그 목표를 계산한 순간의 플레이어 위치도 같이 고정한다.</b>
    ///
    /// 반원 경로는 플레이어를 중심으로 도는데, 그 중심이 목표와 따로 놀면 스냅샷으로 찍어둔 목표 지점은
    /// 그대로인데 궤도만 플레이어를 따라 끌려다닌다 — 찍은 점으로는 영영 가지 않게 된다.
    /// 목표와 중심은 항상 같은 순간의 값이어야 한다.
    /// </summary>
    void CaptureGoal(MeleeEnemyContext context)
    {
        goalPosition = ResolveGoal(context);
        trackedTarget = context.FlatTargetPosition;
        goalSideSign = context.NearSideSign;
    }

    Vector3 ResolveGoal(MeleeEnemyContext context)
    {
        Vector3 target = context.FlatTargetPosition;

        switch (profile.goal)
        {
            // 플레이어의 옆구리 — 지금 가까운 쪽. 플레이어가 나를 지나쳐 좌우가 뒤집히면 목표도 같이 뒤집힌다.
            case MoveGoal.AttackSide:
                return target + Vector3.right * (context.NearSideSign * context.Data.triggerCenterDistance);

            // 플레이어를 중심으로 한 원 둘레의 한 점. 각도는 시작할 때 한 번 뽑아 두고 여기서는 쓰기만 한다 —
            // 실시간 추적이면 이 함수가 매 프레임 불리므로, 여기서 뽑으면 목적지가 매 프레임 바뀐다.
            case MoveGoal.OrbitPoint:
            {
                float radians = orbitAngleDeg * Mathf.Deg2Rad;
                return target + new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * profile.orbitRadius;
            }

            // 플레이어 반대쪽으로 도망친다. 막혀 있으면 트인 쪽을 찾아 간다.
            case MoveGoal.Escape:
                return context.FlatSelfPosition + ResolveEscapeDirection(context) * EscapeDistance;

            // 플레이어에게서 멀어지는(또는 가까워지는) 직선상의 한 점.
            case MoveGoal.StepBack:
            default:
                Vector3 away = context.FlatSelfPosition - target;
                if (away.sqrMagnitude < 0.0001f)
                    away = Vector3.right * context.NearSideSign;
                away.Normalize();

                float distance = profile.stopAfterDistance > 0f ? profile.stopAfterDistance : profile.arrivalTolerance;
                return context.FlatSelfPosition + away * (stepBackSign * distance);
        }
    }

    // ===== 이탈 방향 =====

    // 도망갈 방향을 고를 때 훑어보는 후보 수. 360도를 이만큼 균등하게 나눠 본다.
    const int EscapeDirectionSamples = 16;

    float EscapeDistance => profile.stopAfterDistance > 0f ? profile.stopAfterDistance : profile.orbitRadius;

    /// <summary>
    /// 플레이어 반대 방향이 기본이되, 그쪽이 벽에 막혀 있으면 <b>더 멀리 갈 수 있는 쪽</b>으로 튼다(설계서 ⑧).
    /// 360도를 균등하게 훑어보고 "원하는 거리를 채울 수 있는가"를 먼저, "플레이어 반대쪽에 가까운가"를 나중에 본다.
    ///
    /// 플레이어 쪽으로는 절대 도망치지 않는다 — 반대 방향에서 90도 넘게 벌어진 후보는 아예 빼고 고른다.
    /// 구석에 몰려 뒤가 다 막혔으면 옆(90도)으로 벽을 타고 빠지게 된다.
    /// </summary>
    Vector3 ResolveEscapeDirection(MeleeEnemyContext context)
    {
        Vector3 away = context.FlatSelfPosition - context.FlatTargetPosition;
        away = away.sqrMagnitude < 0.0001f
            ? Vector3.right * context.NearSideSign // 완전히 겹쳤으면 가까운 옆구리 쪽으로
            : away.normalized;

        Vector3 self = context.FlatSelfPosition;
        float margin = context.Locomotion != null ? context.Locomotion.BoundaryRadius : 0f;
        float wanted = EscapeDistance;

        Vector3 best = away;
        float bestScore = -1f;

        for (int i = 0; i < EscapeDirectionSamples; i++)
        {
            float angle = i * (360f / EscapeDirectionSamples) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

            // 플레이어 쪽으로 도망치는 것은 이탈이 아니다.
            float alignment = Vector3.Dot(dir, away);
            if (alignment < 0f) continue;

            // 갈 수 있는 거리가 최우선, 같으면 플레이어 반대쪽에 가까운 쪽. 거리 항의 배율이 커서 순서가 뒤집히지 않는다.
            float reach = Mathf.Min(ReachWithinBounds(self, dir, margin), wanted);
            float score = reach * 10f + alignment;

            if (score > bestScore)
            {
                bestScore = score;
                best = dir;
            }
        }

        return best;
    }

    /// <summary>
    /// <paramref name="origin"/>에서 <paramref name="dir"/> 방향으로 맵 경계에 닿기까지 갈 수 있는 거리.
    /// Locomotion이 경계에서 콜라이더 반경만큼 떨어뜨려 멈추므로 여기서도 같은 여유를 빼고 잰다.
    /// </summary>
    static float ReachWithinBounds(Vector3 origin, Vector3 dir, float margin)
    {
        Bounds bounds = MapBounds.Instance != null
            ? MapBounds.Instance.Bounds
            : new Bounds(Vector3.zero, Vector3.one * 1000f);

        float reach = AxisReach(origin.x, dir.x, bounds.min.x + margin, bounds.max.x - margin);
        reach = Mathf.Min(reach, AxisReach(origin.z, dir.z, bounds.min.z + margin, bounds.max.z - margin));

        return Mathf.Max(0f, reach);
    }

    /// <summary>한 축에서 벽에 닿기까지의 거리. 그 축으로 움직이지 않으면 제한이 없다.</summary>
    static float AxisReach(float position, float direction, float min, float max)
    {
        if (Mathf.Abs(direction) < 0.0001f) return float.MaxValue;

        float edge = direction > 0f ? max : min;
        return (edge - position) / direction;
    }

    /// <summary>
    /// 원 둘레에서 목적지를 하나 고른다. 지금 내가 있는 각도에서 최소 이동 각도만큼은 떨어진 곳을 뽑는다 —
    /// 안 그러면 바로 옆이 뽑혀 재정렬이 시작하자마자 끝나버린다.
    ///
    /// <b>시작할 때 한 번만 뽑는다.</b> 실시간 추적이면 목표를 매 프레임 다시 계산하는데,
    /// 그때마다 각도를 새로 뽑으면 목적지가 원 위를 마구 튀어 다녀 영영 도착하지 못한다.
    /// 각도를 고정해 두면 실시간 추적은 "플레이어를 따라다니는 원 위의 같은 지점"이 된다.
    /// </summary>
    void DecideOrbitAngle(MeleeEnemyContext context)
    {
        if (profile.goal != MoveGoal.OrbitPoint)
        {
            orbitAngleDeg = 0f;
            return;
        }

        Vector3 fromTarget = context.FlatSelfPosition - context.FlatTargetPosition;
        float currentAngle = fromTarget.sqrMagnitude < 0.0001f
            ? Random.value * 360f // 완전히 겹쳤으면 기준 각도가 없다
            : Mathf.Atan2(fromTarget.x, fromTarget.z) * Mathf.Rad2Deg;

        float minSweep = Mathf.Clamp(profile.repositionMinSweep, 0f, 180f);
        orbitAngleDeg = currentAngle + Random.Range(minSweep, 360f - minSweep);
    }

    /// <summary>
    /// 목적지까지 <b>곧장 갈 때 플레이어를 뚫고 지나가게 되는지</b>를 보고 원호와 직선을 가른다.
    /// 원호가 존재하는 이유가 애초에 "플레이어를 스쳐 지나가지 않으려고"이므로, 길이 비어 있으면
    /// 굳이 돌 이유가 없다 — 돌아가는 길일 뿐이다.
    ///
    /// 시작할 때 한 번만 정한다. 가는 도중에 다시 재면 비켜 도는 순간 길이 비어 보여서
    /// 직선으로 바뀌고, 그러면 다시 플레이어 쪽으로 붙는 진동이 생긴다.
    /// </summary>
    void DecideArcMode(MeleeEnemyContext context)
    {
        if (profile.path != MovePath.SemiCircle)
        {
            arcAroundTarget = false;
            return;
        }

        float clearance = DistanceToSegment(trackedTarget, context.FlatSelfPosition, goalPosition);
        arcAroundTarget = clearance < profile.avoidClearance;
    }

    /// <summary>점에서 선분까지의 최단 거리(xz 평면). 선분 <b>밖</b>으로 벗어나면 가까운 끝점까지의 거리다 —
    /// 덕분에 "플레이어가 내 뒤에 있다"거나 "목적지 너머에 있다"는 경우가 따로 분기 없이 걸러진다.</summary>
    static float DistanceToSegment(Vector3 point, Vector3 from, Vector3 to)
    {
        Vector3 segment = to - from;
        float lengthSq = segment.sqrMagnitude;
        if (lengthSq < 0.0001f) return Vector3.Distance(point, from);

        float t = Mathf.Clamp01(Vector3.Dot(point - from, segment) / lengthSq);
        return Vector3.Distance(point, from + segment * t);
    }

    void DecideOrbitSign(MeleeEnemyContext context)
    {
        if (profile.path != MovePath.SemiCircle)
        {
            orbitSign = 1f;
            return;
        }

        Vector3 radial = context.FlatSelfPosition - context.FlatTargetPosition;
        Vector3 goalRadial = goalPosition - context.FlatTargetPosition;

        // 목표까지 <b>가까운 쪽</b>으로 돈다. xz 평면의 외적 부호가 곧 도는 방향이다
        // (tangent(+1) = radial을 +90도 돌린 것이므로 부호가 그대로 맞는다).
        float cross = radial.x * goalRadial.z - radial.z * goalRadial.x;
        if (Mathf.Abs(cross) > 0.0001f)
        {
            orbitSign = cross >= 0f ? 1f : -1f;
            return;
        }

        // 정확히 정반대(또는 겹침)라 양쪽이 똑같이 멀다. 맵 바깥으로 밀려나지 않게 z 기준 중앙 쪽을 고른다.
        float centerZ = MapBounds.Instance != null ? MapBounds.Instance.Bounds.center.z : 0f;
        bool preferForward = context.Self.position.z <= centerZ;

        if (radial.sqrMagnitude < 0.0001f)
            radial = Vector3.right * context.NearSideSign;
        radial.Normalize();

        Vector3 tangent = new Vector3(-radial.z, 0f, radial.x);
        orbitSign = (tangent.z >= 0f) == preferForward ? 1f : -1f;
    }

    void DecideStepBackSign(MeleeEnemyContext context)
    {
        if (profile.goal != MoveGoal.StepBack)
        {
            stepBackSign = 1f;
            return;
        }

        // 가까우면 뒤로 물러나고, 플레이어가 공격을 피해 이미 멀어져 있으면 오히려 다가간다(설계서 8장).
        stepBackSign = context.DistanceToTarget > context.Data.slowStepForwardDistance ? -1f : 1f;
    }

    // ===== 종료 판정 =====

    bool HasArrived(MeleeEnemyContext context)
    {
        // 공격 위치로 가는 중이라면 "좌표에 닿았는지"가 아니라 "플레이어가 트리거 범위에 들어왔는지"가 종료 조건이다.
        if (profile.goal == MoveGoal.AttackSide)
            return context.IsTargetInTriggerRange();

        return Vector3.Distance(context.FlatSelfPosition, goalPosition) <= profile.arrivalTolerance;
    }

    // ===== 조향 =====

    /// <summary>
    /// 원하는 방향으로 곧장 꺾지 않고 선회 속도만큼만 돌린다. 이 제한이 꺾이는 지점을 직각이 아니라
    /// 반지름을 가진 곡선으로 만든다. 마지막에 z 목표선을 넘어가려 하면 그 프레임의 z 성분을 깎아 막는다.
    /// </summary>
    Vector3 Steer(Vector3 current, Vector3 desired, Vector3 delta, float deltaTime)
    {
        Vector3 next = current.sqrMagnitude < 0.000001f || profile.turnRateDegPerSecond <= 0f
            ? desired
            : Vector3.RotateTowards(current, desired, profile.turnRateDegPerSecond * Mathf.Deg2Rad * deltaTime, 0f);

        return profile.path == MovePath.ZFirst
            ? ClampZOvershoot(next, delta, deltaTime)
            : next;
    }

    /// <summary>
    /// ㄱ자 경로에서 z 목표선을 <b>넘어가지 않게</b> 이번 프레임의 z 성분을 깎는다.
    /// 꺾기 시작하는 거리를 아무리 잘 잡아도 프레임 단위 이산 오차가 남는데, 한 번 넘어가면
    /// 되돌아오며 출렁여서(S자) 눈에 크게 띈다. 속도는 유지해야 하므로 깎은 만큼 x 성분으로 돌린다 —
    /// 결과적으로 목표선에 닿는 순간 진행 방향이 정확히 x가 된다.
    /// </summary>
    Vector3 ClampZOvershoot(Vector3 dir, Vector3 delta, float deltaTime)
    {
        float step = profile.speed * deltaTime;
        if (step <= 0.000001f) return dir;

        // 목표선에서 멀어지는 중이면 넘어갈 일이 없다.
        if (Mathf.Abs(delta.z) > 0.0001f && Mathf.Sign(dir.z) != Mathf.Sign(delta.z))
            return dir;

        float maxZ = Mathf.Abs(delta.z) / step; // 목표선을 넘지 않는 최대 z 성분
        if (maxZ >= 1f || Mathf.Abs(dir.z) <= maxZ) return dir;

        float xSign = Mathf.Abs(delta.x) > 0.0001f ? Mathf.Sign(delta.x)
                    : Mathf.Abs(dir.x) > 0.0001f ? Mathf.Sign(dir.x)
                    : 0f;
        if (xSign == 0f) return dir; // x로도 갈 곳이 없으면 곧 도착 판정에 걸린다

        float z = Mathf.Sign(dir.z) * maxZ;
        float x = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z)) * xSign;
        return new Vector3(x, 0f, z);
    }

    // ===== 경로 =====

    /// <summary>
    /// 이번 순간 가고 싶은 방향(정규화 전). 실제 이동 방향은 여기에 선회 속도 제한이 걸린 결과다.
    /// 자기 위치를 파라미터로 받는 이유: 기즈모 미리보기가 가상의 위치로 같은 계산을 굴려야 하기 때문.
    /// </summary>
    Vector3 ResolveDirection(Vector3 self, Vector3 goal, Vector3 target, ref bool turningInState)
    {
        Vector3 delta = goal - self;

        switch (profile.path)
        {
            case MovePath.ZFirst:
            {
                if (profile.zTurnBlend == ZTurnBlend.Gradual)
                    return ResolveGradualTurn(delta);

                // --- 단계 전환(예전 방식). 인스펙터에서 되돌릴 수 있게 남겨둔다 ---
                // 꺾기 시작하는 거리 = 선회 반지름. 반지름 r인 호로 90도 돌면 딱 r만큼 전진하므로,
                // 목표선보다 r 앞에서 꺾기 시작해야 넘어가지 않고 접선으로 안착한다(= J자).
                // 여유를 조금 더 두는 이유: 꺾는 동안에도 목표의 z가 남아 있어 실제 호가 이상보다 완만해진다.
                float anticipation = Mathf.Max(profile.zPriorityThreshold, TurnRadius * 1.3f);
                float absZ = Mathf.Abs(delta.z);

                // 한 번 꺾어 들어가기 시작하면 되돌아가지 않는다(경계에서 깔짝거리면 그게 곧 S자다).
                // 플레이어가 레인을 확실히 바꿨을 때(예측 거리의 2배 밖)만 다시 z부터 맞추러 간다.
                if (!turningInState && absZ <= anticipation) turningInState = true;
                else if (turningInState && absZ > anticipation * 2f) turningInState = false;

                // z를 맞추는 단계에선 순수 z로만 간다. 꺾는 단계에선 대각선으로 두어 남은 z 오차까지 흡수한다.
                return turningInState ? new Vector3(delta.x, 0f, delta.z) : new Vector3(0f, 0f, delta.z);
            }

            case MovePath.SemiCircle:
            {
                // 가는 길에 플레이어가 없으면 돌지 않고 목적지로 곧장 간다.
                if (!arcAroundTarget) return delta;

                // 접선(도는 방향) + 반지름 오차 보정(유지 거리로 붙거나 떨어지기)을 더해 원호를 그린다.
                Vector3 radial = self - target;
                float radius = radial.magnitude;
                if (radius < 0.0001f)
                {
                    // 플레이어와 완전히 겹친 순간. 임의의 방향을 잡되 반지름 오차 보정은 0이 되게 둔다.
                    radial = Vector3.right;
                    radius = profile.orbitRadius;
                }
                else
                {
                    radial /= radius;
                }

                Vector3 tangent = new Vector3(-radial.z, 0f, radial.x) * orbitSign;
                float radiusError = Mathf.Clamp((radius - profile.orbitRadius) / Mathf.Max(profile.orbitRadius, 0.0001f), -1f, 1f);
                return tangent - radial * radiusError;
            }

            case MovePath.Straight:
            default:
                return delta;
        }
    }

    /// <summary>
    /// 'ㄱ자' 경로의 <b>점진적</b> 꺾임. 직선 구간과 곡선 구간을 가르는 경계가 아예 없고,
    /// x 비중이 z 거리에 따라 연속으로 커지면서 기울기만 서서히 늘어난다.
    ///
    /// 기준 거리 s에 대해 x 비중을  w = min(1, s^2 / dz^2),  z 비중을 dz에 비례하게 두면
    /// dz = s에서 정확히 45도가 되고, 멀수록 순수 z에, 가까울수록 순수 x에 수렴한다.
    ///
    /// <b>z 비중을 dz에 비례한 값 하나로만 두는 것이 핵심이다.</b> 여기에 (1 - w)를 한 번 더 곱하면
    /// 목표 근처에서 두 항이 동시에 0으로 가 z가 이중으로 억눌린다. 그러면 z 오차가 거의 안 줄어든 채
    /// x만 좁혀지다가, x가 다 떨어진 마지막에 남은 z를 몰아서 처리하느라 갈고리처럼 꺾인다.
    /// 비례항 하나만 두면 z 오차가 x 이동에 따라 지수적으로 줄어(길이상수 = s) 끝에서 몰릴 것이 남지 않는다.
    /// </summary>
    Vector3 ResolveGradualTurn(Vector3 delta)
    {
        float scale = Mathf.Max(profile.zPriorityThreshold, TurnRadius);
        if (scale <= 0.0001f) return delta;

        float scaleSq = scale * scale;
        float zSq = delta.z * delta.z;
        float w = zSq > scaleSq ? scaleSq / zSq : 1f; // 기준 거리 안으로 들어오면 x 비중은 최대로 고정

        // 각 성분을 기준 거리로 정규화해 포화시킨다. 멀리 있을 땐 ±1로 포화해 방향이 오직 w로만
        // 정해지고(거리가 얼마나 남았든 같은 모양), 목표에 가까워지면 알아서 줄어들어 부드럽게 안착한다.
        float xTerm = Mathf.Clamp(delta.x / scale, -1f, 1f);
        float zTerm = Mathf.Clamp(delta.z / scale, -1f, 1f);

        Vector3 blended = new Vector3(xTerm * w, 0f, zTerm);
        return blended.sqrMagnitude < 0.0001f ? delta : blended; // 둘 다 0이면 그냥 목표 쪽으로
    }

    // ===== 디버그 =====

    /// <summary>
    /// 지금 상태에서 이 노드가 앞으로 그릴 경로를 미리 굴려 <paramref name="into"/>에 점으로 담는다.
    /// 노드의 실제 상태는 건드리지 않는다(전부 지역 변수로 복사해서 돌린다).
    ///
    /// 목표 좌표는 현재 값으로 고정한 채 굴리므로, 플레이어가 움직이면 실제 경로는 여기서 갈라진다.
    /// 경로 <b>모양</b>(J자인지 S자인지, 곡선 반지름이 적당한지)을 눈으로 보려는 용도다.
    /// </summary>
    public void PredictPath(MeleeEnemyContext context, List<Vector3> into)
    {
        into.Clear();
        if (context == null || !context.HasTarget || profile.speed <= 0f) return;

        Vector3 self = context.FlatSelfPosition;
        Vector3 goal = goalPosition;
        Vector3 target = trackedTarget; // 실제 이동과 같은 중심을 써야 미리보기가 실제 경로와 일치한다
        Vector3 h = heading;
        bool turning = turningIn;
        float tolerance = Mathf.Max(profile.arrivalTolerance, 0.05f);

        into.Add(self);

        for (int i = 0; i < PreviewSteps; i++)
        {
            Vector3 desired = ResolveDirection(self, goal, target, ref turning);
            if (desired.sqrMagnitude < 0.000001f) break;

            h = Steer(h, desired.normalized, goal - self, PreviewStepTime);
            self += h * (profile.speed * PreviewStepTime);
            into.Add(self);

            if (Vector3.Distance(self, goal) <= tolerance) break;
        }
    }
}
