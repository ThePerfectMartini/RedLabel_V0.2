/// <summary>
/// 점프 준비 애니메이션 클립의 Animation Event를 전달받는 컴포넌트가 구현하는 인터페이스.
/// JumpAnimationEventReceiver가 Animator와 같은 오브젝트에서 이벤트를 받아,
/// 부모 오브젝트에서 이 인터페이스를 찾아 실제 점프(수직 속도 부여)를 위임한다.
/// </summary>
public interface IJumpEventListener
{
    /// <summary>점프 준비 애니메이션 클립에서 캐릭터가 실제로 뜨는 프레임에 호출된다.</summary>
    void OnJumpLaunchFrame();

    /// <summary>착지 애니메이션 클립에서 착지 경직이 풀리는 프레임(보통 마지막 프레임)에 호출된다.</summary>
    void OnJumpLandEndFrame();
}
