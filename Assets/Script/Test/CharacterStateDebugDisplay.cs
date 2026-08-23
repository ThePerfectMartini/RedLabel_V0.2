using UnityEngine;

/// <summary>
/// TEMP: CharacterStateMachine의 현재 상태와 바라보는 방향(좌/우 화살표)을 오브젝트 머리 위
/// 화면 좌표에 텍스트로 표시하는 디버그용 컴포넌트.
/// IStateMachineOwner를 구현한 컴포넌트(PlayerController, EnemyController 등)와 같은 오브젝트에 붙이면 자동으로 찾아서 표시한다.
/// 방향 표시는 같은 오브젝트가 IAttackRangeDebugInfo(FacingDir)도 구현하고 있을 때만 나오고,
/// 없으면 상태 텍스트만 표시된다.
/// 제거할 때는 이 스크립트 파일 삭제 + 씬에서 이 컴포넌트만 떼어내면 된다 (다른 코드는 안 건드림).
/// </summary>
public class CharacterStateDebugDisplay : MonoBehaviour
{
    [KoreanLabel("표시 위치 오프셋")]
    public Vector3 offset = Vector3.up * 2.5f;

    IStateMachineOwner stateOwner;
    IAttackRangeDebugInfo facingSource;

    void Awake()
    {
        stateOwner = GetComponent<IStateMachineOwner>();
        if (stateOwner == null)
            Debug.LogWarning($"{name}: IStateMachineOwner를 구현한 컴포넌트가 없어 상태를 표시할 수 없습니다.");

        facingSource = GetComponent<IAttackRangeDebugInfo>();
        // facingSource가 없어도 상태 텍스트는 그대로 표시되므로 경고하지 않는다.
    }

    void OnGUI()
    {
        if (stateOwner == null || Camera.main == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + offset);
        if (screenPos.z <= 0f) return; // 카메라 뒤에 있으면 표시 안 함

        string arrow = facingSource == null ? ""
            : facingSource.FacingDir.x >= 0f ? " →" : " ←";

        GUI.Label(new Rect(screenPos.x - 50f, Screen.height - screenPos.y, 100f, 20f), stateOwner.StateMachine.CurrentState + arrow);
    }
}
