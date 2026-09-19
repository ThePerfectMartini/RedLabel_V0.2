using UnityEngine;

/// <summary>
/// 캐릭터가 "이번 프레임에 뭘 하고 싶은지"를 담는 값. 플레이어와 적이 공용으로 쓴다.
///
/// Actor는 이 값이 사람이 누른 키에서 왔는지 AI가 정한 것인지 구분하지 않고 똑같이 처리한다.
/// 덕분에 "무엇을 할지"를 정하는 주체(IIntentSource 구현체)와 "몸"(Locomotion / Fighter /
/// CharacterStateMachine / Health)이 완전히 분리된다 — 조종 주체가 바뀌어도 몸 쪽은 그대로다.
/// </summary>
public struct CharacterIntent
{
    /// <summary>이동 입력(-1~1). Locomotion이 필요에 따라 8방향으로 스냅한다.</summary>
    public Vector2 MoveInput;

    /// <summary>
    /// 이번 프레임에 바라보고 싶은 방향. X 성분만 쓰이며 0이면 "요청 없음"이라 기존 좌우가 유지된다.
    /// 이동 방향과 바라보는 방향이 다를 수 있어서(예: 뒷걸음질 치며 계속 상대를 바라봄) MoveInput과 분리한다.
    /// </summary>
    public Vector3 FacingDirection;

    /// <summary>
    /// 이번 프레임에 시작(또는 콤보 진행)하고 싶은 공격. null이면 "공격 안 함".
    /// 예전엔 WantsAttack bool을 따로 뒀지만 "공격하고 싶다"의 유일한 신호는 이 값이 null이 아닌 것뿐이다.
    /// </summary>
    public AttackData AttackToStart;

    /// <summary>
    /// 이번 프레임의 이동 속도 배율(기본 이동 속도 기준). <b>0이면 "지정 없음"이라 1과 같다</b> —
    /// CharacterIntent.None이 default라서 0이 기본값이 될 수밖에 없기 때문이다.
    ///
    /// MoveInput은 방향만 전달되고 크기는 Locomotion에서 버려지므로(항상 normalize), 걷기/달리기처럼
    /// 같은 방향을 다른 속도로 가려면 이 값이 따로 필요하다. AI의 행동별 속도 차이(빠른 접근 / 느린 후퇴)가 이걸 쓴다.
    /// </summary>
    public float MoveSpeedScale;

    /// <summary>이번 프레임에 점프하고 싶은지.</summary>
    public bool WantsJump;

    /// <summary>이번 프레임에 회피(대시)하고 싶은지.</summary>
    public bool WantsDodge;

    /// <summary>이번 프레임에 패링(반격자세)하고 싶은지.</summary>
    public bool WantsParry;

    /// <summary>아무것도 하지 않는 의도. IIntentSource가 없거나 입력이 없을 때 쓴다.</summary>
    public static CharacterIntent None => default;
}
