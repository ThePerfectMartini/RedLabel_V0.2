using UnityEngine;

/// <summary>
/// 공격 애니메이션 클립의 Animation Event를 받아 IAttackEventListener로 전달만 하는 다리 역할.
/// Animation Event는 Animator 컴포넌트가 붙은 바로 그 오브젝트로만 전달되므로,
/// 이 스크립트는 반드시 Animator와 같은 오브젝트(CharacterAnimatorBridge가
/// GetComponentInChildren&lt;Animator&gt;()로 찾는 그 자식 오브젝트)에 붙여야 한다.
///
/// [준비물]
/// - 공격 애니메이션 클립에 Function Name이 "OnAttackHitFrame"인 Animation Event 추가
/// - 부모 계층 어딘가에 IAttackEventListener를 구현한 컴포넌트(PlayerController 등) 존재
/// </summary>
public class AttackAnimationEventReceiver : MonoBehaviour
{
    IAttackEventListener listener;

    void Awake()
    {
        listener = GetComponentInParent<IAttackEventListener>();

        if (listener == null)
            Debug.LogWarning($"{name}: IAttackEventListener를 구현한 컴포넌트를 부모에서 찾지 못해 공격 판정 이벤트를 전달할 수 없습니다.");
    }

    /// <summary>공격 애니메이션 클립의 Animation Event가 이 이름으로 호출한다.</summary>
    public void OnAttackHitFrame()
    {
        listener?.OnAttackHitFrame();
    }
}
