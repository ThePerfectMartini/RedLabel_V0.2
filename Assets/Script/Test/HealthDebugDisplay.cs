using UnityEngine;

/// <summary>
/// TEMP: 오브젝트 머리 위에 현재 체력을 숫자로 표시하는 디버그용 컴포넌트.
/// Health가 붙은 오브젝트(= Actor가 있는 루트)에 같이 붙이면 자동으로 찾는다.
/// 제거할 때는 이 파일 삭제 + 씬에서 컴포넌트만 떼면 된다.
/// </summary>
public class HealthDebugDisplay : MonoBehaviour
{
    [KoreanLabel("표시 위치 오프셋")]
    public Vector3 offset = Vector3.up * 2f;

    Health health;

    void Awake()
    {
        health = GetComponent<Health>();
        if (health == null)
            Debug.LogWarning($"{name}: Health가 없어 체력을 표시할 수 없습니다.");
    }

    void OnGUI()
    {
        if (health == null || Camera.main == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + offset);
        if (screenPos.z <= 0f) return; // 카메라 뒤면 표시 안 함

        GUI.Label(new Rect(screenPos.x - 50f, Screen.height - screenPos.y, 100f, 20f),
            $"{health.CurrentHealth} / {health.MaxHealth}");
    }
}
