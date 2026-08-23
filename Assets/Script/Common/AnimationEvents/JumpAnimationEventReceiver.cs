using UnityEngine;

/// <summary>
/// JumpStart 클립과 JumpLand 클립, 서로 다른 두 클립에 각각 하나씩 걸린 Animation Event를 받아
/// IJumpEventListener로 전달만 하는 다리 역할. 예전엔 점프 전체를 클립 하나로 이어 붙였지만,
/// 공중에서 공격이 끼어들면 재생 중이던 점프 클립이 공격 클립으로 덮이면서 착지 시점에 클립이
/// 처음부터 다시 재생돼 점프 시작 이벤트가 또 호출되는 문제가 있었다. 그래서 준비/착지를
/// 별도 클립·State로 분리했다 (CharacterAnimatorBridge 헤더 주석 참고).
/// Animation Event는 Animator 컴포넌트가 붙은 바로 그 오브젝트로만 전달되므로,
/// 이 스크립트는 반드시 Animator와 같은 오브젝트(CharacterAnimatorBridge가
/// GetComponentInChildren&lt;Animator&gt;()로 찾는 그 자식 오브젝트)에 붙여야 한다.
///
/// [준비물]
/// - JumpStart 클립: 발이 땅에서 떨어지는 프레임에 Function Name "OnJumpLaunchFrame" Animation Event
/// - JumpLand 클립: 착지 경직이 풀리는 프레임(보통 마지막 프레임)에 Function Name "OnJumpLandEndFrame" Animation Event
/// - 부모 계층 어딘가에 IJumpEventListener를 구현한 컴포넌트(PlayerController/EnemyController 등) 존재
/// </summary>
public class JumpAnimationEventReceiver : MonoBehaviour
{
    IJumpEventListener listener;

    void Awake()
    {
        listener = GetComponentInParent<IJumpEventListener>();

        if (listener == null)
            Debug.LogWarning($"{name}: IJumpEventListener를 구현한 컴포넌트를 부모에서 찾지 못해 점프 발사 이벤트를 전달할 수 없습니다.");
    }

    /// <summary>JumpStart 클립의 "발이 떨어지는" 프레임에 걸린 Animation Event가 이 이름으로 호출한다.</summary>
    public void OnJumpLaunchFrame()
    {
        listener?.OnJumpLaunchFrame();
    }

    /// <summary>JumpLand 클립의 "착지 경직이 풀리는" 프레임에 걸린 Animation Event가 이 이름으로 호출한다.</summary>
    public void OnJumpLandEndFrame()
    {
        listener?.OnJumpLandEndFrame();
    }
}
