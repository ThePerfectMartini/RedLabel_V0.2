using System.Reflection;
using UnityEngine;

/// <summary>
/// TEMP: 오브젝트 머리 위에 현재 체력을 숫자로 표시하는 디버그용 컴포넌트.
/// PlayerController/EnemyController의 currentHealth는 private이고 외부에 공개된 API가 없는데,
/// 이 스크립트는 기존 코드를 전혀 건드리지 않고 독립적으로 동작해야 하므로 리플렉션으로 직접 읽는다.
/// 제거할 때는 이 스크립트 파일 삭제 + 씬에서 이 컴포넌트만 떼어내면 된다 (다른 코드는 안 건드림).
///
/// [준비물] PlayerController 또는 EnemyController와 같은 오브젝트에 붙일 것.
/// </summary>
public class HealthDebugDisplay : MonoBehaviour
{
    [KoreanLabel("표시 위치 오프셋")]
    public Vector3 offset = Vector3.up * 2f;

    const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    Component owner;
    FieldInfo healthField;
    FieldInfo statDataField;

    void Awake()
    {
        owner = GetComponent<PlayerController>();
        if (owner == null)
            owner = GetComponent<EnemyController>();

        if (owner == null)
        {
            Debug.LogWarning($"{name}: PlayerController/EnemyController가 없어 체력을 표시할 수 없습니다.");
            return;
        }

        System.Type type = owner.GetType();
        healthField = type.GetField("currentHealth", PrivateInstance);
        statDataField = type.GetField("characterStatData", PublicInstance);

        if (healthField == null)
            Debug.LogWarning($"{name}: {type.Name}에서 currentHealth 필드를 찾지 못했습니다.");
    }

    void OnGUI()
    {
        if (owner == null || healthField == null || Camera.main == null) return;

        int current = (int)healthField.GetValue(owner);
        string text = current.ToString();

        if (statDataField != null && statDataField.GetValue(owner) is CharacterStatData stat && stat != null)
            text += $" / {stat.maxHealth}";

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + offset);
        if (screenPos.z <= 0f) return; // 카메라 뒤에 있으면 표시 안 함

        GUI.Label(new Rect(screenPos.x - 50f, Screen.height - screenPos.y, 100f, 20f), text);
    }
}
