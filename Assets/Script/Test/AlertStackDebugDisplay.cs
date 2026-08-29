using System.Reflection;
using UnityEngine;

/// <summary>
/// TEMP: 적의 경계 스택과 배정된 슬롯(좌/우 + 대기 순번)을 머리 위에 막대와 숫자로 표시하는 디버그용 컴포넌트.
///
/// 경계 스택은 눈에 보이는 연출이 없어서(다 차면 "사거리에 닿는 순간 바로 친다"는 판정이 달라질 뿐이다)
/// 표시가 없으면 "지금 즉시공격 상태인가"를 플레이 화면만 보고는 구분할 수 없다.
///
/// ChasePlayerBrain의 alertStack은 private이고 외부에 공개된 API가 없는데, 이 스크립트는 기존 코드를
/// 전혀 건드리지 않고 독립적으로 동작해야 하므로 HealthDebugDisplay와 같이 리플렉션으로 직접 읽는다.
/// 제거할 때는 이 스크립트 파일 삭제 + 씬에서 이 컴포넌트만 떼어내면 된다 (다른 코드는 안 건드림).
///
/// [준비물] ChasePlayerBrain과 같은 오브젝트에 붙일 것.
/// </summary>
public class AlertStackDebugDisplay : MonoBehaviour
{
    [KoreanLabel("표시 위치 오프셋")]
    public Vector3 offset = Vector3.up * 3f;

    [KoreanLabel("막대 크기(픽셀)")]
    public Vector2 barSize = new Vector2(60f, 6f);

    const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    ChasePlayerBrain brain;
    FieldInfo alertStackField;
    FieldInfo stateField;

    void Awake()
    {
        brain = GetComponent<ChasePlayerBrain>();
        if (brain == null)
        {
            Debug.LogWarning($"{name}: ChasePlayerBrain이 없어 경계 스택을 표시할 수 없습니다.");
            return;
        }

        alertStackField = typeof(ChasePlayerBrain).GetField("alertStack", PrivateInstance);
        if (alertStackField == null)
            Debug.LogWarning($"{name}: ChasePlayerBrain에서 alertStack 필드를 찾지 못했습니다. 이름이 바뀌었다면 이 스크립트도 같이 고쳐야 합니다.");

        // 후퇴/복귀는 겉모습만으로는 "그냥 이동 중"과 구분되지 않아서 상태 이름을 같이 띄운다.
        stateField = typeof(ChasePlayerBrain).GetField("state", PrivateInstance);
        if (stateField == null)
            Debug.LogWarning($"{name}: ChasePlayerBrain에서 state 필드를 찾지 못했습니다. 상태 이름 없이 경계 스택만 표시합니다.");
    }

    void OnGUI()
    {
        if (brain == null || alertStackField == null || Camera.main == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + offset);
        if (screenPos.z <= 0f) return; // 카메라 뒤에 있으면 표시 안 함

        float stack = Mathf.Clamp01((float)alertStackField.GetValue(brain));
        EngagementSlot slot = EnemyEngagementDirector.Instance.GetSlot(brain);
        bool ready = stack >= 1f;

        float x = screenPos.x - barSize.x * 0.5f;
        float y = Screen.height - screenPos.y;

        Color previous = GUI.color;

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(x, y, barSize.x, barSize.y), Texture2D.whiteTexture);

        // 만충이면 빨강 — 지금 사거리에 들어가면 곧바로 맞는다는 뜻이다.
        GUI.color = ready ? Color.red : Color.yellow;
        GUI.DrawTexture(new Rect(x, y, barSize.x * stack, barSize.y), Texture2D.whiteTexture);

        GUI.color = previous;

        string side = slot.Side >= 0f ? "R" : "L";
        string stateName = stateField == null ? "" : " " + stateField.GetValue(brain);
        GUI.Label(new Rect(x, y + barSize.y, barSize.x + 80f, 20f), $"{side}{slot.Rank} {stack:0.00}{stateName}");
    }
}
