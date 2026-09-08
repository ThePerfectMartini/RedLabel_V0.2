using UnityEngine;

/// <summary>
/// 적을 대상(<see cref="EnemyTargeting"/>.Target) 주위의 궤도 위에 세우고 움직이는 이동 모듈.
/// 지침서의 5개 파라미터(Origin / CurrentAngle / DesiredAngle / RotationSpeed / OrbitRadius)를 그대로 구현한다.
///
/// 비헤이비어 트리와 무관한 독립 컴포넌트다 — 이후 의사결정 노드는 이 모듈의 <see cref="desiredAngle"/> /
/// <see cref="orbitRadius"/>만 바꾸고, "어떻게 그 위치로 가는가"는 전부 여기가 맡는다.
///
/// 스스로 움직이지 않는다. <see cref="EnemyBrain"/>이 매 프레임 GetIntent 안에서 <see cref="Advance"/>(dt)를
/// 부르고, 돌려받은 XZ 이동 입력을 CharacterIntent.MoveInput에 실어 Actor → Locomotion으로 흘려보낸다.
///
/// [붙이는 곳] 적 루트(EnemyTargeting과 같은 오브젝트).
/// [각도 규약] 0° = 대상의 +X 방향(같은 레인, 오른쪽) · 90° = +Z(안쪽 레인) · 180° = 왼쪽 · 270° = 바깥쪽 레인.
/// [알려진 한계] Advance는 이동 잠금(공격 중·넉백 중)을 모른다. 그 사이에도 CurrentAngle이 굴러가므로
///              잠금이 풀리면 적이 궤도 목표점으로 한 번에 붙는다. 조립 단계에서 이동 노드에서만 Advance를 부르게 한다.
/// </summary>
[RequireComponent(typeof(EnemyTargeting))]
public class EnemyOrbitMovement : MonoBehaviour
{
    [KoreanLabel("목표 각도(도)")]
    [Tooltip("CurrentAngle이 회전해 도달하려는 각도. 0=같은 레인 오른쪽, 90=안쪽, 180=왼쪽, 270=바깥쪽.")]
    public float desiredAngle;

    [KoreanLabel("궤도 반경")]
    [Min(0f)]
    [Tooltip("대상으로부터 유지할 거리. 줄이면 다가가고, 늘리면 물러난다.")]
    public float orbitRadius = 2.5f;

    [KoreanLabel("회전 속도(도/초)")]
    [Min(0f)]
    [Tooltip("CurrentAngle이 DesiredAngle로 회전하는 속도.")]
    public float rotationSpeed = 120f;

    [KoreanLabel("도착 판정 거리")]
    [Min(0f)]
    [Tooltip("궤도 목표 지점에 이 거리 안으로 들어오면 이동 입력을 0으로 (파르르 떠는 것 방지).")]
    public float arrivalThreshold = 0.15f;

    EnemyTargeting targeting;
    bool angleInitialized;

    /// <summary>대상 기준 적이 현재 위치한 각도. Advance가 매 프레임 DesiredAngle 쪽으로 굴린다.</summary>
    public float CurrentAngle { get; private set; }

    /// <summary>이번 프레임 CurrentAngle·OrbitRadius로 계산된 궤도 위 목표 월드 좌표.</summary>
    public Vector3 DesiredPosition { get; private set; }

    /// <summary>궤도 목표 지점에 도착해 있는지(arrivalThreshold 이내).</summary>
    public bool HasArrived { get; private set; }

    void Awake() => targeting = GetComponent<EnemyTargeting>();

    void Start()
    {
        if (targeting.HasTarget)
            SyncToCurrentPosition();
    }

    /// <summary>
    /// CurrentAngle을 적의 "실제" 현재 위치에서 계산한 값으로 맞춘다(스냅 방지).
    /// 노드가 궤도 제어를 넘겨받는 순간 등에서 부른다.
    /// </summary>
    public void SyncToCurrentPosition()
    {
        if (!targeting.HasTarget)
        {
            angleInitialized = false;
            return;
        }

        Vector3 d = transform.position - targeting.Target.position;
        CurrentAngle = Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
        angleInitialized = true;
    }

    /// <summary>
    /// CurrentAngle을 DesiredAngle 쪽으로 dt만큼 회전시키고, 궤도 위 목표 지점으로 향하는 XZ 이동 입력을
    /// 돌려준다(정규화, 길이 0~1). 대상이 없으면 Vector2.zero.
    /// </summary>
    public Vector2 Advance(float deltaTime)
    {
        if (!targeting.HasTarget)
        {
            angleInitialized = false;
            HasArrived = false;
            return Vector2.zero;
        }

        if (!angleInitialized)
            SyncToCurrentPosition();

        CurrentAngle = Mathf.MoveTowardsAngle(CurrentAngle, desiredAngle, rotationSpeed * deltaTime);

        Vector3 origin = targeting.Target.position;
        float rad = CurrentAngle * Mathf.Deg2Rad;
        DesiredPosition = new Vector3(
            origin.x + Mathf.Cos(rad) * orbitRadius,
            transform.position.y,
            origin.z + Mathf.Sin(rad) * orbitRadius);

        Vector3 delta = DesiredPosition - transform.position;
        Vector2 planar = new Vector2(delta.x, delta.z);

        HasArrived = planar.magnitude <= arrivalThreshold;
        return HasArrived ? Vector2.zero : planar.normalized;
    }

    void OnDrawGizmosSelected()
    {
        if (targeting == null || !targeting.HasTarget) return;

        Vector3 origin = targeting.Target.position;

        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.5f);
        DrawCircleXZ(origin, orbitRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, DesiredPosition);
        Gizmos.DrawWireSphere(DesiredPosition, 0.15f);
    }

    static void DrawCircleXZ(Vector3 center, float radius)
    {
        const int seg = 48;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
