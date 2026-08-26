using UnityEngine;

/// <summary>
/// 캐릭터가 "이번 프레임에 뭘 하고 싶은지"를 담는 구조체. 플레이어와 적이 공용으로 쓴다.
///
/// CharacterControllerBase는 이 값이 사람이 누른 키에서 온 것인지 AI가 정한 것인지 구분하지 않고
/// 똑같이 처리한다. 덕분에 "무엇을 할지"를 정하는 주체(입력 / AI)와 "몸"(이동·공격·피격·상태 관리)이
/// 완전히 분리되어, 조종 주체가 바뀌어도 몸 쪽 코드는 그대로다.
/// </summary>
public struct CharacterIntent
{
    /// <summary>이동 입력(-1~1). MovementCore가 필요에 따라 8방향으로 스냅한다.</summary>
    public Vector2 MoveInput;

    /// <summary>이번 프레임에 공격을 시작(또는 콤보 진행)하고 싶은지.</summary>
    public bool WantsAttack;

    /// <summary>
    /// 콤보를 시작할 공격 데이터. null이면 컨트롤러의 firstAttackData가 쓰인다.
    /// 공격 시작 키가 여러 개인 플레이어(X키/Z키)만 이 값을 지정하며,
    /// 시작 공격이 하나뿐인 적 AI는 WantsAttack만 켜고 이 값은 비워둔다.
    /// </summary>
    public AttackData AttackToStart;

    /// <summary>이번 프레임에 점프하고 싶은지.</summary>
    public bool WantsJump;

    /// <summary>아무것도 하지 않는 의도. Brain이 없거나 입력이 없을 때 사용한다.</summary>
    public static CharacterIntent None => default;
}
