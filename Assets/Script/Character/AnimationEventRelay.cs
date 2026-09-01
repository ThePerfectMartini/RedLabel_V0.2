using UnityEngine;

/// <summary>
/// 애니메이션 클립에 박힌 Animation Event를 받아 루트의 담당 컴포넌트로 넘기기만 하는 다리.
/// Animation Event는 Animator가 붙은 바로 그 오브젝트로만 전달되므로, 이 스크립트는 반드시
/// Animator와 같은 오브젝트("Visual" 자식)에 붙어야 한다.
///
/// [준비물] 부모 계층에 Fighter / CharacterStateMachine (= Actor가 있는 루트).
/// [클립 이벤트]
///   공격 클립  : OnAttackHitFrame
///   점프 준비  : OnJumpLaunchFrame     / 착지 : OnJumpLandEndFrame
///   다운 클립  : OnKnockdownGetUpStartFrame / 기상 : OnKnockdownGetUpEndFrame
/// </summary>
public class AnimationEventRelay : MonoBehaviour
{
    Fighter fighter;
    CharacterStateMachine stateMachine;

    void Awake()
    {
        fighter = GetComponentInParent<Fighter>();
        stateMachine = GetComponentInParent<CharacterStateMachine>();

        if (fighter == null || stateMachine == null)
            Debug.LogWarning($"{name}: 부모에서 Fighter / CharacterStateMachine을 찾지 못해 애니메이션 이벤트를 전달할 수 없습니다.");
    }

    public void OnAttackHitFrame()
    {
        if (fighter != null) fighter.OnAttackHitFrame();
    }

    public void OnJumpLaunchFrame()
    {
        if (stateMachine != null) stateMachine.OnJumpLaunchFrame();
    }

    public void OnJumpLandEndFrame()
    {
        if (stateMachine != null) stateMachine.OnJumpLandEndFrame();
    }

    public void OnKnockdownGetUpStartFrame()
    {
        if (stateMachine != null) stateMachine.OnKnockdownGetUpStartFrame();
    }

    public void OnKnockdownGetUpEndFrame()
    {
        if (stateMachine != null) stateMachine.OnKnockdownGetUpEndFrame();
    }
}
