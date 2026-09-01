using UnityEngine;

/// <summary>
/// TEMP: CharacterStateMachine의 현재 상태와 바라보는 방향(좌/우 화살표)을 오브젝트 머리 위에 텍스트로 표시.
/// CharacterStateMachine이 붙은 오브젝트(= Actor가 있는 루트)에 같이 붙이면 자동으로 찾는다.
/// 방향 화살표는 같은 오브젝트가 IAttackRangeDebugInfo(Fighter)도 구현할 때만 나온다.
/// 제거할 때는 이 파일 삭제 + 씬에서 컴포넌트만 떼면 된다.
/// </summary>
public class CharacterStateDebugDisplay : MonoBehaviour
{
    [KoreanLabel("표시 위치 오프셋")]
    public Vector3 offset = Vector3.up * 2.5f;

    CharacterStateMachine stateMachine;
    IAttackRangeDebugInfo facingSource;

    void Awake()
    {
        stateMachine = GetComponent<CharacterStateMachine>();
        if (stateMachine == null)
            Debug.LogWarning($"{name}: CharacterStateMachine이 없어 상태를 표시할 수 없습니다.");

        facingSource = GetComponent<IAttackRangeDebugInfo>();
        // facingSource가 없어도 상태 텍스트는 그대로 나오므로 경고하지 않는다.
    }

    void OnGUI()
    {
        if (stateMachine == null || Camera.main == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + offset);
        if (screenPos.z <= 0f) return; // 카메라 뒤면 표시 안 함

        string arrow = facingSource == null ? ""
            : facingSource.FacingDir.x >= 0f ? " →" : " ←";

        GUI.Label(new Rect(screenPos.x - 50f, Screen.height - screenPos.y, 100f, 20f), stateMachine.CurrentState + arrow);
    }
}
