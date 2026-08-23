using UnityEngine;

/// <summary>
/// Landed 클립과 GetUp 클립, 서로 다른 두 클립에 각각 하나씩 걸린 Animation Event를 받아
/// IKnockdownEventListener로 전달만 하는 다리 역할. JumpAnimationEventReceiver와 같은 이유로
/// 준비/착지를 별도 클립·State로 분리한 것과 마찬가지로, 쓰러짐/일어남도 별도 클립·State로 분리한다.
/// Animation Event는 Animator 컴포넌트가 붙은 바로 그 오브젝트로만 전달되므로,
/// 이 스크립트는 반드시 Animator와 같은 오브젝트(CharacterAnimatorBridge가
/// GetComponentInChildren&lt;Animator&gt;()로 찾는 그 자식 오브젝트)에 붙여야 한다.
///
/// [준비물]
/// - Landed 클립: 몸을 일으키기 시작하는 프레임에 Function Name "OnKnockdownGetUpStartFrame" Animation Event
/// - GetUp 클립: 완전히 일어나는 프레임(보통 마지막 프레임)에 Function Name "OnKnockdownGetUpEndFrame" Animation Event
/// - 부모 계층 어딘가에 IKnockdownEventListener를 구현한 컴포넌트(PlayerController/EnemyController 등) 존재
/// </summary>
public class KnockdownAnimationEventReceiver : MonoBehaviour
{
    IKnockdownEventListener listener;

    void Awake()
    {
        listener = GetComponentInParent<IKnockdownEventListener>();

        if (listener == null)
            Debug.LogWarning($"{name}: IKnockdownEventListener를 구현한 컴포넌트를 부모에서 찾지 못해 기상 이벤트를 전달할 수 없습니다.");
    }

    /// <summary>Landed 클립의 "몸을 일으키기 시작하는" 프레임에 걸린 Animation Event가 이 이름으로 호출한다.</summary>
    public void OnKnockdownGetUpStartFrame()
    {
        listener?.OnKnockdownGetUpStartFrame();
    }

    /// <summary>GetUp 클립의 "완전히 일어나는" 프레임에 걸린 Animation Event가 이 이름으로 호출한다.</summary>
    public void OnKnockdownGetUpEndFrame()
    {
        listener?.OnKnockdownGetUpEndFrame();
    }
}
