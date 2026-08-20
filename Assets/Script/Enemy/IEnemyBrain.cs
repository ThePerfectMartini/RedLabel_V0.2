using UnityEngine;

/// <summary>
/// 적의 "이번 프레임에 뭘 하고 싶은지"를 담는 구조체.
/// 플레이어에게 키보드 입력이 있는 자리에, 적에게는 이 값이 들어간다.
/// EnemyController는 이 값이 사람이 누른 것인지 AI가 정한 것인지 구분하지 않고 똑같이 처리한다.
/// </summary>
public struct EnemyIntent
{
    /// <summary>플레이어의 이동 입력(-1~1)과 같은 의미. MovementCore가 8방향으로 스냅한다.</summary>
    public Vector2 MoveInput;

    /// <summary>이번 프레임에 공격을 시작(또는 콤보 진행)하고 싶은지.</summary>
    public bool WantsAttack;

    /// <summary>이번 프레임에 점프하고 싶은지.</summary>
    public bool WantsJump;

    /// <summary>아무것도 하지 않는 의도. Brain이 없을 때 EnemyController가 사용한다.</summary>
    public static EnemyIntent None => default;
}

/// <summary>
/// 적 AI가 구현할 인터페이스. EnemyController와 같은 오브젝트에 붙은 컴포넌트에서 찾는다.
///
/// EnemyController는 "몸"(이동/공격/피격/상태 관리)만 담당하고, "무엇을 할지"의 판단은 전부 여기로 위임된다.
/// 덕분에 추격형/원거리형/보스 등 AI 종류가 늘어나도 EnemyController는 건드릴 필요가 없다.
/// 판단에 필요한 자기 정보(위치, 상태, 지상 여부 등)는 Think에 넘어오는 owner에서 읽는다.
///
/// [주의] 이 인터페이스는 자리만 잡아둔 것이고, 실제 AI 구현체는 아직 없다.
/// 구현체가 없으면 EnemyController는 EnemyIntent.None으로 동작한다 (제자리에 가만히 서 있음).
/// </summary>
public interface IEnemyBrain
{
    /// <summary>
    /// 매 프레임 호출되어 이번 프레임의 행동 의도를 반환한다.
    /// 여기서 직접 위치를 옮기거나 공격 판정을 하지 말 것 — 그건 EnemyController와 MovementCore/CombatCore의 일이다.
    /// </summary>
    EnemyIntent Think(EnemyController owner, float deltaTime);
}
