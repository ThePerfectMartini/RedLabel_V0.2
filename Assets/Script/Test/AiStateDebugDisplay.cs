using UnityEngine;

/// <summary>
/// TEMP: MeleeEnemyBrain이 지금 순환의 어느 칸에 있는지를 머리 위 텍스트로,
/// 판단의 기준이 되는 거리들을 기즈모 도형으로 표시하는 디버그용 컴포넌트.
///
/// 다섯 상태 중 셋(추적 / 후퇴 / 배회)은 겉모습이 전부 "걷는 중"이라 화면만 봐서는 구분되지 않고,
/// 재정비와 공격 직전의 정지도 똑같이 "가만히 서 있음"으로 보인다. 거리 쪽도 마찬가지로 눈에 보이는
/// 것이 없어서, 값이 잘못 잡혔을 때 증상만 보고는 어느 값이 문제인지 알 수 없다.
///
/// [기즈모가 안 보일 때] 씬 뷰에는 그냥 나오지만 게임 뷰에서는 우측 상단 Gizmos 토글이 켜져 있어야 한다.
///
/// [주의] 텍스트에 뜨는 것은 두뇌의 상태이지 몸의 상태가 아니다. 얻어맞아 경직 중일 때 두뇌는 추적으로
/// 리셋되므로 이 표시는 "추적"인데 실제로는 못 움직이고 있을 수 있다. 몸 쪽은
/// CharacterStateDebugDisplay가 따로 보여준다.
///
/// 제거할 때는 이 스크립트 파일 삭제 + 씬에서 이 컴포넌트만 떼어내면 된다 (다른 코드는 안 건드림).
///
/// [준비물] MeleeEnemyBrain과 같은 오브젝트에 붙일 것. 거리 표시는 씬에 PlayerController가 있어야 나온다.
/// </summary>
public class AiStateDebugDisplay : MonoBehaviour
{
    [KoreanLabel("표시 위치 오프셋")]
    public Vector3 offset = Vector3.up * 3f;

    [Header("거리 표시")]

    [KoreanLabel("정지 거리 표시")]
    [Tooltip("추적이 향하는 목표 지점 두 곳(플레이어의 좌우). 이 자리에 섰을 때 곧바로 공격 사거리가 " +
        "성립해야 하므로, AttackRangeGizmo의 판정 구체와 겹쳐 보이는지로 값이 맞는지 확인할 수 있다.")]
    public bool showStopDistance = true;

    [KoreanLabel("배회 범위 표시")]
    [Tooltip("배회 목표 박스와 네 모서리(각 구역의 목표 지점). 가운데 십자선이 좌/우 × 앞/뒤 구역을 가른다.")]
    public bool showWanderRange = true;

    [KoreanLabel("추적 복귀 거리 표시")]
    [Tooltip("배회 중 이 구 안에 들어오면 추적으로 복귀한다. 배회 모서리가 이 구 밖에 있어야 정상이며, " +
        "안에 들어와 있으면 배회가 시작하자마자 끝난다.")]
    public bool showReAggroRange = true;

    // 값을 바꾸라고 연 것이 아니라 도형끼리 구분만 되면 되는 색이라 인스펙터에 내지 않는다.
    static readonly Color StopDistanceColor = new Color(1f, 0.55f, 0.3f);
    static readonly Color WanderRangeColor = new Color(0.2f, 0.8f, 0.65f);
    static readonly Color ReAggroRangeColor = new Color(0.65f, 0.65f, 0.65f);

    // 목표 지점은 점이라 그냥은 안 보이므로 이만한 구를 씌워서 표시한다.
    const float TargetMarkerRadius = 0.2f;

    MeleeEnemyBrain brain;

    void Awake()
    {
        brain = GetComponent<MeleeEnemyBrain>();
        if (brain == null)
            Debug.LogWarning($"{name}: MeleeEnemyBrain이 없어 AI 상태를 표시할 수 없습니다.");
    }

    void OnGUI()
    {
        if (brain == null || Camera.main == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + offset);
        if (screenPos.z <= 0f) return; // 카메라 뒤에 있으면 표시 안 함

        MeleeEnemyBrain.AiState state = brain.CurrentState;

        Color previous = GUI.color;
        GUI.color = ColorOf(state);
        GUI.Label(new Rect(screenPos.x - 50f, Screen.height - screenPos.y, 100f, 20f), LabelOf(state));
        GUI.color = previous;
    }

    void OnDrawGizmos()
    {
        // 에디트 모드(플레이 중이 아닐 때)에도 표시되도록, Awake를 못 거쳤으면 직접 찾는다.
        if (brain == null)
            brain = GetComponent<MeleeEnemyBrain>();
        if (brain == null || brain.behavior == null) return;

        // 세 도형 모두 플레이어를 원점으로 잡는다. EnemyAiMath가 목표 지점을 그렇게 계산하기 때문이고,
        // 적을 기준으로 그리면 화면에 보이는 도형과 코드가 쓰는 기준이 어긋난다.
        PlayerController player = PlayerController.Instance;
        if (player == null) return;

        Vector3 playerPos = player.transform.position;
        EnemyBehaviorData behavior = brain.behavior;

        if (showReAggroRange)
        {
            Gizmos.color = ReAggroRangeColor;
            Gizmos.DrawWireSphere(playerPos, behavior.reAggroRange);
        }

        if (showWanderRange)
            DrawWanderRange(playerPos, behavior);

        if (showStopDistance)
            DrawStopDistance(playerPos, behavior);
    }

    /// <summary>
    /// 배회 목표 박스를 그린다. 네 모서리가 각 구역의 목표 지점이고, 가운데 십자선이 구역을 가른다.
    /// 박스는 xz 평면에만 있으므로 두께 0짜리 납작한 큐브로 그린다.
    /// </summary>
    void DrawWanderRange(Vector3 playerPos, EnemyBehaviorData behavior)
    {
        float x = behavior.wanderRadiusX;
        float z = behavior.wanderRadiusZ;

        Gizmos.color = WanderRangeColor;
        Gizmos.DrawWireCube(playerPos, new Vector3(x * 2f, 0f, z * 2f));

        Gizmos.DrawLine(playerPos + new Vector3(-x, 0f, 0f), playerPos + new Vector3(x, 0f, 0f));
        Gizmos.DrawLine(playerPos + new Vector3(0f, 0f, -z), playerPos + new Vector3(0f, 0f, z));

        for (int sideX = -1; sideX <= 1; sideX += 2)
            for (int sideZ = -1; sideZ <= 1; sideZ += 2)
                Gizmos.DrawWireSphere(playerPos + new Vector3(sideX * x, 0f, sideZ * z), TargetMarkerRadius);
    }

    /// <summary>
    /// 추적 목표 지점 두 곳을 그린다. 원이 아니라 점 두 개인 이유는, 정지 거리가 x축으로만 적용되고
    /// z는 플레이어와 같게 맞추기 때문이다 (공격 판정이 좌우로만 나가서 z가 어긋나면 빗나간다).
    /// </summary>
    void DrawStopDistance(Vector3 playerPos, EnemyBehaviorData behavior)
    {
        Gizmos.color = StopDistanceColor;

        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 slot = playerPos + new Vector3(side * behavior.stopDistance, 0f, 0f);
            Gizmos.DrawLine(playerPos, slot);
            Gizmos.DrawWireSphere(slot, TargetMarkerRadius);
        }
    }

    /// <summary>
    /// 순환에서의 역할이 한눈에 들어오도록 셋으로 묶는다.
    /// 노랑은 붙거나 거리를 유지하는 중, 빨강은 지금 때리는 중, 하늘색은 플레이어의 반격 창이다.
    /// 추적과 배회가 같은 색인 것은 의도한 것이며, 둘의 구분은 글자가 한다.
    /// </summary>
    static Color ColorOf(MeleeEnemyBrain.AiState state)
    {
        switch (state)
        {
            case MeleeEnemyBrain.AiState.Attack:
                return Color.red;
            case MeleeEnemyBrain.AiState.Retreat:
            case MeleeEnemyBrain.AiState.Recover:
                return Color.cyan;
            default:
                return Color.yellow;
        }
    }

    static string LabelOf(MeleeEnemyBrain.AiState state)
    {
        switch (state)
        {
            case MeleeEnemyBrain.AiState.Chase:   return "추적";
            case MeleeEnemyBrain.AiState.Attack:  return "공격";
            case MeleeEnemyBrain.AiState.Retreat: return "후퇴";
            case MeleeEnemyBrain.AiState.Recover: return "재정비";
            case MeleeEnemyBrain.AiState.Wander:  return "배회";
            default: return state.ToString();
        }
    }
}
