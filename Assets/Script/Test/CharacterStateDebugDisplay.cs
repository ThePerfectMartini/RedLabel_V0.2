using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// TEMP: 오브젝트 머리 위에 디버그 정보를 세 줄까지 겹쳐 표시하는 컴포넌트.
///
/// 1. 상태 + 바라보는 방향 화살표 (IStateMachineOwner / IAttackRangeDebugInfo)
/// 2. 체력 (현재 / 최대) — CharacterControllerBase가 있을 때
/// 3. 지금 수행 중인 행동 — EnemyBrain이 있을 때, 즉 적일 때만
///
/// 각 줄은 필요한 것이 있을 때만 나오고, 없으면 그 줄만 빠진다.
///
/// 적이면 씬 뷰에 조건표의 거리 경계(전투 시작 / 전투 이탈 / 맞은 뒤 추격 한계)와
/// 플레이어까지의 실제 거리도 그린다.
/// 여러 마리가 동시에 그리면 씬이 지저분해지므로 <b>선택했을 때만</b> 나온다.
/// 공격 판정 구는 AttackRangeGizmo가 따로 그리므로 여기서 중복해 그리지 않는다.
/// 제거할 때는 이 스크립트 파일 삭제 + 씬에서 이 컴포넌트만 떼어내면 된다 (다른 코드는 안 건드림).
///
/// [리플렉션을 쓰는 이유] 체력(currentHealth)은 protected, 현재 행동(current)은 private이라 밖에서
/// 읽을 방법이 없다. 디버그 표시를 위해 런타임 코드에 공개 API를 뚫으면 "이 스크립트 하나만 지우면
/// 흔적이 남지 않는다"는 위 성질이 깨진다. 대신 필드를 못 찾으면 조용히 비지 않고 경고를 남긴다.
///
/// (2026-08-30: 같은 자리에 겹쳐 그리던 HealthDebugDisplay를 여기로 합쳤다.)
/// </summary>
public class CharacterStateDebugDisplay : MonoBehaviour
{
    [KoreanLabel("표시 위치 오프셋")]
    public Vector3 offset = Vector3.up * 2.5f;

    [Header("거리 경계 기즈모 (적 전용, 선택했을 때만)")]
    [KoreanLabel("맞은 뒤 추격 한계 색")]
    public Color pursueLimitColor = Color.cyan;

    [KoreanLabel("전투 시작 거리 색")]
    public Color combatEnterColor = Color.yellow;

    [KoreanLabel("전투 이탈 거리 색")]
    public Color combatExitColor = new Color(1f, 0.5f, 0f);

    const float LineHeight = 18f;

    IStateMachineOwner stateOwner;
    IAttackRangeDebugInfo facingSource;

    CharacterControllerBase controller;
    FieldInfo healthField;

    // 아래 셋은 적일 때만 채워진다. brain이 null이면 행동 줄 자체를 그리지 않는다.
    EnemyBrain brain;
    FieldInfo currentBehaviorField;

    // 브레인이 들고 있는 행동 인스턴스와, 그 필드에 붙은 KoreanLabel을 미리 뽑아둔 것.
    // 행동 이름을 여기에 문자열로 적어두면 행동이 늘 때마다 이 파일도 같이 고쳐야 하므로,
    // 인스펙터에 나오는 라벨을 그대로 재사용한다.
    object[] behaviorInstances;
    string[] behaviorLabels;

    void Awake()
    {
        stateOwner = GetComponent<IStateMachineOwner>();
        if (stateOwner == null)
            Debug.LogWarning($"{name}: IStateMachineOwner를 구현한 컴포넌트가 없어 상태를 표시할 수 없습니다.");

        facingSource = GetComponent<IAttackRangeDebugInfo>();
        // facingSource가 없어도 상태 텍스트는 그대로 표시되므로 경고하지 않는다.

        controller = GetComponent<CharacterControllerBase>();
        if (controller != null)
        {
            healthField = typeof(CharacterControllerBase)
                .GetField("currentHealth", BindingFlags.NonPublic | BindingFlags.Instance);

            if (healthField == null)
                Debug.LogWarning($"{name}: CharacterControllerBase에서 currentHealth 필드를 찾지 못해 체력을 표시할 수 없습니다.");
        }

        brain = GetComponent<EnemyBrain>();
        if (brain != null)
            CacheBehaviorLookup();
    }

    void CacheBehaviorLookup()
    {
        Type brainType = typeof(EnemyBrain);

        currentBehaviorField = brainType.GetField("current", BindingFlags.NonPublic | BindingFlags.Instance);
        if (currentBehaviorField == null)
        {
            Debug.LogWarning($"{name}: EnemyBrain에서 현재 행동 필드를 찾지 못해 행동 이름을 표시할 수 없습니다. " +
                             $"필드 이름이 바뀌었다면 이 디버그 스크립트도 같이 고쳐야 합니다.");
            return;
        }

        FieldInfo[] fields = brainType.GetFields(BindingFlags.Public | BindingFlags.Instance);

        int count = 0;
        foreach (FieldInfo field in fields)
        {
            if (typeof(IEnemyBehavior).IsAssignableFrom(field.FieldType))
                count++;
        }

        behaviorInstances = new object[count];
        behaviorLabels = new string[count];

        int i = 0;
        foreach (FieldInfo field in fields)
        {
            if (!typeof(IEnemyBehavior).IsAssignableFrom(field.FieldType))
                continue;

            KoreanLabelAttribute label =
                (KoreanLabelAttribute)Attribute.GetCustomAttribute(field, typeof(KoreanLabelAttribute));

            behaviorInstances[i] = field.GetValue(brain);
            behaviorLabels[i] = label != null ? label.Label : field.Name;
            i++;
        }
    }

    /// <summary>현재 체력 / 최대 체력. 스탯 데이터가 없으면 현재 체력만.</summary>
    string HealthText()
    {
        if (controller == null || healthField == null) return null;

        int current = (int)healthField.GetValue(controller);

        return controller.characterStatData != null
            ? $"{current} / {controller.characterStatData.maxHealth}"
            : current.ToString();
    }

    /// <summary>
    /// 지금 수행 중인 행동의 표시 이름. 적이 아니거나 아직 첫 판단 전이면 null.
    /// </summary>
    string CurrentBehaviorLabel()
    {
        if (currentBehaviorField == null) return null;

        object current = currentBehaviorField.GetValue(brain);
        if (current == null) return null; // 아직 Think가 한 번도 안 돌았다

        for (int i = 0; i < behaviorInstances.Length; i++)
        {
            if (ReferenceEquals(behaviorInstances[i], current))
                return behaviorLabels[i];
        }

        // 브레인이 인스펙터에 없는 행동을 쓰고 있는 경우. 라벨은 없지만 타입 이름은 보여준다.
        return current.GetType().Name;
    }

    void OnGUI()
    {
        if (Camera.main == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + offset);
        if (screenPos.z <= 0f) return; // 카메라 뒤에 있으면 표시 안 함

        float x = screenPos.x - 50f;
        float y = Screen.height - screenPos.y;

        if (stateOwner != null)
        {
            string arrow = facingSource == null ? ""
                : facingSource.FacingDir.x >= 0f ? " →" : " ←";

            DrawLine(x, ref y, stateOwner.StateMachine.CurrentState + arrow);
        }

        DrawLine(x, ref y, HealthText());

        if (brain != null)
            DrawLine(x, ref y, CurrentBehaviorLabel());
    }

    /// <summary>내용이 있을 때만 한 줄 그리고 다음 줄 위치로 내린다. 없는 줄은 빈칸을 남기지 않는다.</summary>
    void DrawLine(float x, ref float y, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        GUI.Label(new Rect(x, y, 100f, 20f), text);
        y += LineHeight;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 조건표가 쓰는 거리 경계를 바닥에 원으로 그린다. 벨트스크롤이라 구체보다 평면 원이 읽기 쉬워서
    /// Gizmos 대신 Handles를 쓴다 (그래서 에디터 전용).
    ///
    /// 플레이어까지 선을 긋고 실제 거리도 같이 찍는다 — 원 안이냐 밖이냐가 곧 다음 행동이 무엇이 될지이므로,
    /// 값을 튜닝할 때 이 숫자를 보고 맞추게 된다.
    /// </summary>
    void OnDrawGizmosSelected()
    {
        // 에디트 모드에서는 Awake를 안 거쳤으므로 직접 찾는다 (AttackRangeGizmo와 같은 방식).
        EnemyBrain source = brain != null ? brain : GetComponent<EnemyBrain>();
        if (source == null) return;

        Vector3 origin = transform.position;

        UnityEditor.Handles.color = combatEnterColor;
        UnityEditor.Handles.DrawWireDisc(origin, Vector3.up, source.combatRange);

        UnityEditor.Handles.color = combatExitColor;
        UnityEditor.Handles.DrawWireDisc(origin, Vector3.up, source.combatRange + source.combatExitMargin);

        DrawPursueLimit(source, origin);

        // 이 스크립트는 대상을 따로 들고 있지 않다. 브레인과 같은 방식으로 씬에서 찾는다.
        PlayerController player = PlayerController.Instance;
        if (player == null) return;

        Vector3 offset = player.transform.position - origin;
        offset.y = 0f;

        UnityEditor.Handles.color = pursueLimitColor;
        UnityEditor.Handles.DrawLine(origin, origin + offset);
        UnityEditor.Handles.Label(origin + offset * 0.5f, offset.magnitude.ToString("0.0"));
    }

    /// <summary>
    /// 맞은 뒤 이 원 안이면 붙어서 반격하고, 밖이면 물러난다.
    /// 판정 구를 여유만큼 부풀린 것이므로 중심도 판정 구와 같다 — 자기 위치가 아니라 앞쪽이다.
    ///
    /// 사거리는 브레인처럼 CombatCore에서 읽지 않고 공격 에셋에서 직접 읽는다. 그래야 플레이 중이
    /// 아닐 때도 같은 원이 나온다 (CombatCore는 Awake 전까지 코드 기본값을 들고 있다).
    /// 콤보로 다른 공격에 갈아탄 순간에는 둘이 달라질 수 있지만, 지금 적은 후속 공격이 없다.
    /// </summary>
    void DrawPursueLimit(EnemyBrain source, Vector3 origin)
    {
        CharacterControllerBase body = controller != null ? controller : GetComponent<CharacterControllerBase>();
        if (body == null || body.firstAttackData == null) return;

        Vector3 center = origin + body.FacingDir.normalized * body.firstAttackData.attackRange;
        float radius = body.firstAttackData.attackRadius + source.pursueAfterHitMargin;

        UnityEditor.Handles.color = pursueLimitColor;
        UnityEditor.Handles.DrawWireDisc(center, Vector3.up, radius);
    }
#endif
}
