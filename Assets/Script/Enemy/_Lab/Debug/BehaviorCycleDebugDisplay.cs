using UnityEngine;

/// <summary>
/// TEMP: 적 머리 위에 현재 스텝 이름과 예고 진행 막대를 띄우는 디버그용 컴포넌트.
///
/// **예고 애니메이션이 아직 없어서 이 막대가 예고를 대신한다.** 막대가 차는 동안 물러나면
/// 피할 수 있다는 것이 이 아키텍처의 핵심 주장이고, 표시가 없으면 그 주장을 검증할 수가 없다.
/// (거꾸로 말하면, 애니메이션이 붙기 전까지 "게임처럼 읽히는가"는 이걸로 판정할 수 없다.)
///
/// AlertStackDebugDisplay와 달리 리플렉션을 쓰지 않는다. BehaviorCycleBrain이 필요한 것을
/// 이미 공개하고 있기 때문이다.
///
/// [준비물] BehaviorCycleBrain과 같은 오브젝트에 붙일 것.
/// 제거할 때는 이 스크립트 파일 삭제 + 씬에서 컴포넌트만 떼어내면 된다.
/// </summary>
public class BehaviorCycleDebugDisplay : MonoBehaviour
{
    [KoreanLabel("표시 위치 오프셋")]
    public Vector3 offset = Vector3.up * 3f;

    [KoreanLabel("막대 크기(픽셀)")]
    public Vector2 barSize = new Vector2(60f, 6f);

    BehaviorCycleBrain brain;

    void Awake()
    {
        brain = GetComponent<BehaviorCycleBrain>();
        if (brain == null)
            Debug.LogWarning($"{name}: BehaviorCycleBrain이 없어 표시할 것이 없습니다.");
    }

    void OnGUI()
    {
        if (brain == null || Camera.main == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + offset);
        if (screenPos.z <= 0f) return; // 카메라 뒤에 있으면 표시 안 함

        float x = screenPos.x - barSize.x * 0.5f;
        float y = Screen.height - screenPos.y;

        // 예고처럼 진행률이 있는 스텝이면 막대를 그린다. 다 차는 순간 공격이 나간다.
        if (brain.CurrentStep is IProgressReporting progress)
        {
            Color previous = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x, y, barSize.x, barSize.y), Texture2D.whiteTexture);

            GUI.color = Color.red;
            GUI.DrawTexture(new Rect(x, y, barSize.x * Mathf.Clamp01(progress.Progress01), barSize.y), Texture2D.whiteTexture);

            GUI.color = previous;
        }

        EngagementOrder order = EncounterDirector.Instance.GetOrder(brain);
        string side = order.Side >= 0f ? "R" : "L";
        string token = order.HasAttackToken ? "*" : " ";

        GUI.Label(
            new Rect(x, y + barSize.y, barSize.x + 120f, 20f),
            $"{side}{order.Rank}{token} {brain.CurrentStepName}");
    }
}
