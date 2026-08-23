/// <summary>
/// 넉백으로 쓰러진 뒤 일어나는(Landed -> GetUp) 애니메이션 클립의 Animation Event를 전달받는
/// 컴포넌트가 구현하는 인터페이스. KnockdownAnimationEventReceiver가 Animator와 같은 오브젝트에서
/// 이벤트를 받아, 부모 오브젝트에서 이 인터페이스를 찾아 실제 상태 전환을 위임한다.
/// </summary>
public interface IKnockdownEventListener
{
    /// <summary>Landed(쓰러져 있는) 클립에서 캐릭터가 몸을 일으키기 시작하는 프레임에 호출된다.</summary>
    void OnKnockdownGetUpStartFrame();

    /// <summary>GetUp(일어나는) 클립에서 완전히 일어나 다시 움직일 수 있게 되는 프레임(보통 마지막 프레임)에 호출된다.</summary>
    void OnKnockdownGetUpEndFrame();
}
