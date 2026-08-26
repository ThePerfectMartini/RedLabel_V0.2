using UnityEngine;

/// <summary>
/// 적 전용 조율 스크립트. 공통 처리(이동/공격/점프/피격/상태 전환)는 전부 CharacterControllerBase에 있고,
/// 이 클래스는 "무엇을 할지"를 정하는 주체만 담당한다.
///
/// PlayerController가 키보드 입력을 CharacterIntent로 바꾸는 자리에서,
/// 이 클래스는 같은 오브젝트에 붙은 IEnemyBrain에게 매 프레임 물어본다.
/// 베이스 입장에서는 그 의도가 사람에게서 왔는지 AI에게서 왔는지 구분하지 않는다.
///
/// [준비물]
/// - 씬 어딘가(맵 오브젝트)에 MapBounds 컴포넌트 (자동으로 찾아서 참조함)
/// - 대상 레이어는 이 적이 공격할 대상(보통 Player)의 Layer로 지정
/// - 애니메이션을 쓰려면 CharacterAnimatorBridge를 같은 오브젝트에,
///   AttackAnimationEventReceiver / JumpAnimationEventReceiver / KnockdownAnimationEventReceiver를
///   Animator가 붙은 자식 오브젝트에 추가
/// - AI는 IEnemyBrain을 구현한 컴포넌트를 같은 오브젝트에 붙이면 자동으로 인식된다.
///   없어도 에러 없이 동작하며, 그 경우 제자리에 가만히 서 있는다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class EnemyController : CharacterControllerBase
{
    IEnemyBrain brain;

    /// <summary>AI는 플레이어와 달리 임의의 각도로 목표를 향해 곧장 이동한다.</summary>
    protected override bool Uses8DirectionSnap => false;

    protected override void Awake()
    {
        base.Awake();

        brain = GetComponent<IEnemyBrain>();
        // Brain은 없어도 정상 동작한다(가만히 서 있음). AI는 나중에 붙이는 것이 전제라 경고하지 않는다.
    }

    /// <summary>
    /// 이번 프레임의 의도를 Brain에게 물어본다. Brain이 없으면 아무 의도도 없는 것으로 처리한다.
    /// 시작 공격은 하나뿐이라 Brain은 AttackToStart를 비워두고, 베이스가 firstAttackData로 채운다.
    /// </summary>
    protected override CharacterIntent UpdateIntent()
    {
        return brain != null
            ? brain.Think(this, Time.deltaTime)
            : CharacterIntent.None;
    }

    /// <summary>
    /// 바라보는 방향은 이동 방향으로 자동 갱신하지 않는다 (예: 추적 중 너무 가까우면 플레이어를
    /// 바라본 채로 뒷걸음질쳐야 하므로 이동 방향과 바라보는 방향이 다를 수 있다).
    /// 그 대신 Brain이 Think()에서 SetFacing을 직접 호출해서 원하는 방향을 지정한다.
    /// </summary>
    protected override void UpdateFacing(Vector2 effectiveMoveInput) { }

    /// <summary>
    /// 플레이어는 입력(이동 의도)을 보고 판단하지만, 적은 Brain의 의도가 실제 이동으로
    /// 이어졌는지가 중요하므로 결과인 수평 속도를 본다.
    /// </summary>
    protected override bool IsMovingForState()
        => new Vector2(movement.Velocity.x, movement.Velocity.z).sqrMagnitude > 0.01f;

    /// <summary>TODO: 사망 연출(사망 애니메이션 재생 후 제거 등). 지금은 즉시 제거한다.</summary>
    protected override void OnDeath()
    {
        Destroy(gameObject);
    }
}
