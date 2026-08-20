using UnityEngine;

/// <summary>
/// TEMP: CharacterStateMachine의 현재 상태를 오브젝트 머리 위 화면 좌표에 텍스트로 표시하는 디버그용 컴포넌트.
/// IStateMachineOwner를 구현한 컴포넌트(PlayerController, TestDummy 등)와 같은 오브젝트에 붙이면 자동으로 찾아서 표시한다.
/// 제거할 때는 이 스크립트 파일 삭제 + 씬에서 이 컴포넌트만 떼어내면 된다 (다른 코드는 안 건드림).
/// </summary>
public class CharacterStateDebugDisplay : MonoBehaviour
{
    [KoreanLabel("표시 위치 오프셋")]
    public Vector3 offset = Vector3.up * 2.5f;

    IStateMachineOwner stateOwner;

    void Awake()
    {
        stateOwner = GetComponent<IStateMachineOwner>();
        if (stateOwner == null)
            Debug.LogWarning($"{name}: IStateMachineOwner를 구현한 컴포넌트가 없어 상태를 표시할 수 없습니다.");
    }

    void OnGUI()
    {
        if (stateOwner == null || Camera.main == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + offset);
        if (screenPos.z <= 0f) return; // 카메라 뒤에 있으면 표시 안 함

        GUI.Label(new Rect(screenPos.x - 50f, Screen.height - screenPos.y, 100f, 20f), stateOwner.StateMachine.CurrentState.ToString());
    }
}
