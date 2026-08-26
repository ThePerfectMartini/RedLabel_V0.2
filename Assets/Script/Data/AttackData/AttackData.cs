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
    [Header("위력")]
    [KoreanLabel("데미지")]
    public int damage = 10;

    [Header("판정 범위")]
    [KoreanLabel("공격 사거리")]
    public float attackRange = 1.5f;

    [KoreanLabel("공격 판정 반경")]
    public float attackRadius = 1f;

    [Header("넉백 - 대상이 지상에 있을 때")]
    [KoreanLabel("밀치는 힘")]
    public float knockbackForce = 12f;  // 상대를 밀어내는 힘 (수평)

    [KoreanLabel("띄우는 힘")]
    public float launchForce = 6f;      // 상대를 띄우는 힘 (수직)

    [Header("넉백 - 대상이 공중에 있을 때")]
    [KoreanLabel("밀치는 힘(공중)")]
    [Tooltip("이미 공중에 뜬 대상을 맞혔을 때 적용할 수평 힘. 추가타로 더 멀리 날려보내거나(값을 키움) " +
        "제자리에서 계속 띄우는(값을 줄임) 식으로 지상 넉백과 다르게 조절할 수 있다.")]
    public float airborneKnockbackForce = 12f;

    [KoreanLabel("띄우는 힘(공중)")]
    [Tooltip("이미 공중에 뜬 대상을 맞혔을 때 적용할 수직 힘.")]
    public float airborneLaunchForce = 6f;

    [Header("넉백 - 공통")]
    [KoreanLabel("그라운드 슬라이드 감속")]
    [Tooltip("그라운드 슬라이드 중 수평 속도를 초당 이만큼 줄여서 감속 정지시킨다 (에어본 넉백일 땐 사용 안 함)")]
    public float groundSlideDeceleration = 30f;

    [Header("타이밍")]
    [KoreanLabel("공격 쿨타임")]
    public float attackCooldown = 0.1f;

    [Header("애니메이션 / 콤보")]
    [KoreanLabel("공격 애니메이션 클립")]
    [Tooltip("공격 상태 지속시간을 이 클립의 길이에서 자동으로 계산한다(아래 '지속시간 직접 지정'이 0일 때). Animator State 이름도 이 클립과 같아야 한다.")]
    public AnimationClip attackClip;

    [KoreanLabel("지속시간 직접 지정(초)")]
    [Tooltip("0이면 공격 애니메이션 클립의 길이를 그대로 쓴다(기본). " +
             "0보다 큰 값을 넣으면 클립 길이 대신 그 값이 공격 지속시간이 된다.\n\n" +
             "클립 길이를 쓰면 편하지만, 애니메이터가 클립을 손보는 순간 전투 밸런스까지 같이 흔들린다. " +
             "밸런스를 클립과 분리해 고정하고 싶은 공격에만 값을 넣을 것.")]
    public float durationOverride = 0f;

    /// <summary>
    /// 실제로 적용할 공격 지속시간(초). durationOverride가 0보다 크면 그 값을, 아니면 클립 길이를 쓴다.
    /// 둘 다 없으면 0을 반환하며, 호출하는 쪽에서 경고를 남긴다.
    /// </summary>
    public float ResolveDuration()
    {
        if (durationOverride > 0f) return durationOverride;
        return attackClip != null ? attackClip.length : 0f;
    }

    /// <summary>
    /// 이 공격의 클립에 타격 판정용 Animation Event("OnAttackHitFrame")가 실제로 심어져 있는지.
    /// 이게 없으면 공격은 재생되지만 판정이 한 번도 나가지 않고, 캔슬 허용 시점도 영영 열리지 않는다.
    /// durationOverride로 지속시간만 정해둔 경우에도 클립 자체는 필요하다.
    /// </summary>
    public bool HasHitFrameEvent()
    {
        if (attackClip == null) return false;

        foreach (AnimationEvent evt in attackClip.events)
        {
            if (evt.functionName == "OnAttackHitFrame")
                return true;
        }

        return false;
    }

    [KoreanLabel("후속 공격")]
    [Tooltip("공격 지속시간 안에 공격 입력이 들어오면 이어질 다음 공격. 비워두면 이 공격에서 콤보가 끝난다.")]
    public AttackData nextAttack;

    [KoreanLabel("다른 공격을 끊고 나갈 수 있음")]
    [Tooltip("켜면 다른 공격이 재생되는 도중에도 이 공격으로 곧바로 갈아탈 수 있다 (예: 일반 공격 콤보 중 특수 공격). " +
             "끄면 다른 공격이 끝날 때까지 기다려야 한다. 캔슬은 상대 공격이 타격 프레임을 지난 뒤에만 허용된다. " +
             "콤보를 시작하는 공격에만 의미가 있다.")]
    public bool canCancelOtherAttacks = false;

    /// <summary>공격 지속시간 동안 플레이어 입력으로 이동 가능한지. false면 컨트롤러가 이동 의도를 무시한다.</summary>
    public virtual bool AllowsPlayerMovement => true;

    /// <summary>
    /// 이 공격을 하는 동안 적용할 이동 속도 배율(기본 이동 속도 기준). 이동이 막히는 공격에서는
    /// 어차피 이동 자체가 무시되므로 의미가 없고, AllowInputAttackData에서만 실제로 지정한다.
    /// </summary>
    public virtual float MoveSpeedMultiplier => 1f;

    /// <summary>공격 시작 시 자기 자신에게 걸리는 강제 이동(돌진 등). 기본은 아무 것도 하지 않음.</summary>
    public virtual void ApplySelfMovement(MovementCore movement, Vector3 facingDir) { }
}
