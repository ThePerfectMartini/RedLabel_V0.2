using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TEMP: 근접 적 AI가 "지금 어디로 가려는지"를 씬 뷰에 그린다. 수치를 굴려가며 맞추는 동안만 쓰는 도구다.
///
/// 목표까지의 직선이 아니라 <b>앞으로 실제로 지나갈 곡선</b>을 미리 굴려서 그리는 것이 핵심이다 —
/// ㄱ자로 들어오는지 S자로 출렁이는지는 직선으로는 전혀 보이지 않아서 선회 속도를 맞출 수가 없다.
///
/// [붙이는 곳] IMeleeEnemyAIDebugInfo를 구현한 컴포넌트(MeleeEnemyAI 또는 MeleeEnemyNodeTester)와 같은 오브젝트.
/// [보이는 것]
///   노란 곡선 : 앞으로 지나갈 경로        노란 구 : 지금 향하는 목표 좌표
///   주황 원   : 선회 반지름(꺾이는 반경)   초록 상자 : 트리거 범위(플레이어가 들어오면 진해짐)
///   파란 원   : 자리 재정렬이 목적지를 고르는 원 (비켜 돌 때 유지하는 거리)
/// </summary>
public class MeleeEnemyAIGizmo : MonoBehaviour
{
    [KoreanLabel("경로 표시")]
    public bool drawPath = true;

    [KoreanLabel("선회 반지름 표시")]
    [Tooltip("이 원이 z 목표선에 닿는 지점에서 꺾이기 시작해야 넘어가지 않고 접선으로 안착한다.")]
    public bool drawTurnRadius = true;

    [KoreanLabel("트리거 범위 표시")]
    public bool drawTriggerRange = true;

    [KoreanLabel("원 반지름 표시")]
    [Tooltip("자리 재정렬이 목적지를 고르는 원. 비켜 돌 때 유지하는 거리이기도 하다. " +
             "원을 쓰지 않는 이동(접근·느린 이동·이탈)에서는 아무것도 그리지 않는다.")]
    public bool drawOrbitRadius = true;

    [KoreanLabel("경로 색")]
    public Color pathColor = Color.yellow;

    [KoreanLabel("선회 반지름 색")]
    public Color turnRadiusColor = new Color(1f, 0.6f, 0f, 0.5f);

    [KoreanLabel("트리거 범위 색")]
    public Color triggerRangeColor = Color.green;

    [KoreanLabel("원 반지름 색")]
    public Color orbitRadiusColor = new Color(0.3f, 0.7f, 1f, 0.9f);

    [KoreanLabel("표시 높이")]
    [Tooltip("바닥에 묻혀 안 보이지 않게 이만큼 띄워서 그린다.")]
    public float drawHeight = 0.1f;

    IMeleeEnemyAIDebugInfo source;

    // OnDrawGizmos는 매 프레임 불리므로 버퍼를 돌려쓴다.
    readonly List<Vector3> previewPath = new List<Vector3>();

    // 바닥에 눕힌 원을 그릴 때 쓰는 분할 수. 많을수록 매끄럽지만 기즈모라 이 정도면 충분하다.
    const int CircleSegments = 48;

    static void DrawFlatCircle(Vector3 center, float radius)
    {
        Vector3 previous = center + new Vector3(0f, 0f, radius);

        for (int i = 1; i <= CircleSegments; i++)
        {
            float angle = i * (Mathf.PI * 2f / CircleSegments);
            Vector3 point = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
    }

    void OnDrawGizmos()
    {
        // 기즈모는 Awake를 안 거친 상태로도 불릴 수 있어서 여기서 직접 찾는다.
        if (source == null)
            source = GetComponent<IMeleeEnemyAIDebugInfo>();
        if (source == null) return;

        MeleeEnemyContext context = source.Context;
        if (context == null || !context.HasTarget) return;

        Vector3 lift = Vector3.up * drawHeight;

        // 합성 노드를 따라 내려가 실제로 도는 잎을 찾는다. 이동 중이 아니면 경로는 그릴 것이 없다.
        if (BTNode.FindActiveLeaf(source.ActiveNode) is MoveToTargetNode moveNode)
        {
            if (drawPath)
            {
                moveNode.PredictPath(context, previewPath);

                Gizmos.color = pathColor;
                for (int i = 1; i < previewPath.Count; i++)
                    Gizmos.DrawLine(previewPath[i - 1] + lift, previewPath[i] + lift);

                Gizmos.DrawWireSphere(moveNode.GoalPosition + lift, 0.15f);
            }

            if (drawTurnRadius && moveNode.TurnRadius > 0f)
            {
                Gizmos.color = turnRadiusColor;
                Gizmos.DrawWireSphere(transform.position, moveNode.TurnRadius);
            }

            if (drawOrbitRadius && moveNode.OrbitRadius > 0f)
            {
                // 바닥에 눕힌 원으로 그린다. 구로 그리면 벨트스크롤 시점에서 "안쪽인지 바깥인지"가 안 읽힌다.
                Gizmos.color = orbitRadiusColor;
                DrawFlatCircle(moveNode.OrbitCenter + lift, moveNode.OrbitRadius);
            }
        }

        if (!drawTriggerRange || context.Data == null) return;

        // 플레이어가 안에 들어오면 진하게 — 공격이 시작될 수 있는 순간이 눈에 보인다.
        Color color = triggerRangeColor;
        if (!context.IsTargetInTriggerRange())
            color.a *= 0.35f;

        Gizmos.color = color;
        Gizmos.DrawWireCube(context.TriggerRangeCenter + lift,
            new Vector3(context.Data.triggerRangeSize.x, 0.2f, context.Data.triggerRangeSize.y));
    }
}
