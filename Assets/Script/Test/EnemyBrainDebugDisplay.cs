using UnityEngine;

/// <summary>
/// TEMP: EnemyBrain의 직전→현재 행동, 위상, 위상 경과 시간을 머리 위에 표시.
/// EnemyBrain이 붙은 오브젝트에 같이 붙인다. 제거할 때는 이 파일 삭제 + 씬에서 컴포넌트만 떼면 된다.
/// </summary>
public class EnemyBrainDebugDisplay : MonoBehaviour
{
    [KoreanLabel("표시 위치 오프셋")]
    public Vector3 offset = Vector3.up * 3.1f;

    EnemyBrain brain;

    void Awake()
    {
        brain = GetComponent<EnemyBrain>();
        if (brain == null)
            Debug.LogWarning($"{name}: EnemyBrain이 없어 행동 정보를 표시할 수 없습니다.");
    }

    void OnGUI()
    {
        if (brain == null || Camera.main == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + offset);
        if (screenPos.z <= 0f) return;

        EnemyBehavior b = brain.Current;
        string prev = brain.Previous != null ? Short(brain.Previous) : "-";
        string text = b != null
            ? $"{prev} → {Short(b)}  {b.CurrentPhase}  {b.PhaseElapsed:F1}"
            : "(no behavior)";
        if (brain.IsHitStunned)
            text = "[HITSTUN] " + text;

        GUI.Label(new Rect(screenPos.x - 90f, Screen.height - screenPos.y, 300f, 20f), text);
    }

    static string Short(EnemyBehavior b) => b.GetType().Name.Replace("Behavior", "");
}
