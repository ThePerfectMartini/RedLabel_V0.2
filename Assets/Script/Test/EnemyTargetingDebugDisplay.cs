using UnityEngine;

/// <summary>
/// TEMP: EnemyTargeting이 물고 있는 대상과 XZ 거리를 오브젝트 머리 위에 표시.
/// EnemyTargeting이 붙은 오브젝트에 같이 붙인다. 제거할 때는 이 파일 삭제 + 씬에서 컴포넌트만 떼면 된다.
/// </summary>
public class EnemyTargetingDebugDisplay : MonoBehaviour
{
    [KoreanLabel("표시 위치 오프셋")]
    public Vector3 offset = Vector3.up * 2.8f;

    EnemyTargeting targeting;

    void Awake()
    {
        targeting = GetComponent<EnemyTargeting>();
        if (targeting == null)
            Debug.LogWarning($"{name}: EnemyTargeting이 없어 타깃 정보를 표시할 수 없습니다.");
    }

    void OnGUI()
    {
        if (targeting == null || Camera.main == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + offset);
        if (screenPos.z <= 0f) return; // 카메라 뒤면 표시 안 함

        string text = targeting.HasTarget
            ? $"→ {targeting.Target.name}  {targeting.Distance:F1}"
            : "→ (대상 없음)";

        GUI.Label(new Rect(screenPos.x - 60f, Screen.height - screenPos.y, 200f, 20f), text);
    }
}
