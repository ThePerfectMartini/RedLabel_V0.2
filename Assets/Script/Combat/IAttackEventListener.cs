/// <summary>
/// 공격 애니메이션 클립의 Animation Event를 전달받는 컴포넌트가 구현하는 인터페이스.
/// AttackAnimationEventReceiver가 Animator와 같은 오브젝트에서 이벤트를 받아,
/// 부모 오브젝트에서 이 인터페이스를 찾아 실제 타격 판정을 위임한다.
/// </summary>
public interface IAttackEventListener
{
    /// <summary>공격 애니메이션 클립의 타격 프레임에서 호출된다.</summary>
    void OnAttackHitFrame();
}
