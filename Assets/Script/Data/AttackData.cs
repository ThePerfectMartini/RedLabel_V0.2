using UnityEngine;

/// <summary>
/// 공격 하나의 데이터. 공격 하나 = 이동 방식별 구체 서브클래스(AllowInputAttackData/LockedAttackData/
/// ImpulseAttackData, 각각 별도 파일) 에셋 하나. Unity는 CreateAssetMenu로 만드는 타입이 자기 파일 이름과
/// 이름이 같아야만 인식하기 때문에 서브클래스는 이 파일에 같이 둘 수 없다 -> 서브클래스별 파일 참고.
/// 서브클래스에만 있는 필드가 인스펙터에 자동으로 나뉘어 보이는 효과는 그대로 유지된다
/// (Custom PropertyDrawer 없이 타입 자체로 분리).
/// nextAttack으로 다음 콤보 공격을 연결할 수 있다 (없으면 이 공격만 수행하고 콤보 종료).
/// </summary>
public abstract class AttackData : ScriptableObject
{
    [KoreanLabel("데미지")]
    public int damage = 10;

    [KoreanLabel("공격 사거리")]
    public float attackRange = 1.5f;

    [KoreanLabel("공격 판정 반경")]
    public float attackRadius = 1f;

    [KoreanLabel("밀치는 힘")]
    public float knockbackForce = 12f;  // 상대를 밀어내는 힘 (수평)

    [KoreanLabel("띄우는 힘")]
    public float launchForce = 6f;      // 상대를 띄우는 힘 (수직)

    [KoreanLabel("그라운드 슬라이드 감속")]
    [Tooltip("그라운드 슬라이드 중 수평 속도를 초당 이만큼 줄여서 감속 정지시킨다 (에어본 넉백일 땐 사용 안 함)")]
    public float groundSlideDeceleration = 30f;

    [KoreanLabel("공격 쿨타임")]
    public float attackCooldown = 0.1f;

    [KoreanLabel("공격 애니메이션 클립")]
    [Tooltip("공격 상태 지속시간을 이 클립의 길이에서 자동으로 계산한다. 별도로 지속시간 숫자를 입력할 필요 없음. Animator State 이름도 이 클립과 같아야 한다.")]
    public AnimationClip attackClip;

    [KoreanLabel("후속 공격")]
    [Tooltip("공격 지속시간 안에 공격 입력이 들어오면 이어질 다음 공격. 비워두면 이 공격에서 콤보가 끝난다.")]
    public AttackData nextAttack;

    /// <summary>공격 지속시간 동안 플레이어 입력으로 이동 가능한지. false면 PlayerController가 이동 입력을 무시한다.</summary>
    public virtual bool AllowsPlayerMovement => true;

    /// <summary>공격 시작 시 자기 자신에게 걸리는 강제 이동(돌진 등). 기본은 아무 것도 하지 않음.</summary>
    public virtual void ApplySelfMovement(MovementCore movement, Vector3 facingDir) { }
}
