using UnityEngine;

/// <summary>
/// EncounterDirector가 대형을 짜고 공격 토큰을 배급할 때 읽어가는 최소 정보. 적 AI가 구현한다.
///
/// Director를 MonoBehaviour에서 떼어놓기 위한 경계다. Director가 매 틱 각자의 상태를
/// "직접 읽어가는" 구조라서, 적들이 각자 다른 시점에 생각해도 대형 계산은 항상 같은 순간의
/// 값으로 이뤄진다. 반대로 각자가 자기 상태를 Director에 밀어넣는 방식이었다면 먼저 생각한
/// 적이 아직 갱신되지 않은 다른 적의 값으로 판단하게 되어 한 프레임씩 어긋난다.
///
/// (기존 IEngagementMember와 이름이 겹치지 않게 나눈 것이다. 이 프로젝트엔 asmdef가 없어
/// 전부 한 어셈블리이므로 이름이 같으면 컴파일이 안 된다.)
/// </summary>
public interface IEncounterMember
{
    /// <summary>동점 처리에 쓰는 안정적인 키. 매 프레임 순위가 흔들리지 않게 하는 최후의 기준.</summary>
    int Id { get; }

    /// <summary>대형 계산에 쓰는 현재 위치.</summary>
    Vector3 Position { get; }

    /// <summary>
    /// 지금 토큰을 실제로 쓰고 있는가 (예고 또는 공격 중). Director는 이 값이 true였다가
    /// false가 되는 순간을 "공격이 끝났다"로 보고 토큰을 회수해 다음 적에게 넘긴다.
    /// </summary>
    bool IsCommitted { get; }

    /// <summary>
    /// 토큰을 받을 수 있는 몸 상태인가. 얻어맞아 경직/다운 중이면 false다.
    /// 맞은 적이 토큰을 붙들고 있으면 그동안 아무도 공격하지 못해 압박이 끊긴다.
    /// </summary>
    bool IsAvailable { get; }
}

/// <summary>
/// 조율자가 배정한 역할. 개체의 상태 순환은 셋 다 똑같이 돌고, 예고로 넘어갈 수 있는지만 다르다.
/// </summary>
public enum EnemyRole
{
    /// <summary>토큰 보유. 예고 → 공격까지 갈 수 있다. 동시에 몇이 될 수 있는지가 곧 난이도다.</summary>
    Attacker,

    /// <summary>최전열이지만 토큰이 없다. 자리를 지키며 도주로만 막는다. 절대 예고로 넘어가지 않는다.</summary>
    Pressure,

    /// <summary>뒷줄. 배회하며 순서를 기다린다. 위협이 아니라는 것이 보여야 한다.</summary>
    Waiter,
}

/// <summary>
/// 한 적에게 이번 프레임에 배정된 자리와 권한. Director가 매 프레임 다시 계산해서 나눠준다.
/// </summary>
public struct EngagementOrder
{
    /// <summary>대상 기준 어느 쪽에 설지. +1이면 대상의 오른쪽, -1이면 왼쪽.</summary>
    public float Side;

    /// <summary>같은 쪽에서의 대기 순번. 0이면 최전열.</summary>
    public int Rank;

    public EnemyRole Role;

    /// <summary>예고로 넘어가도 되는가. 스텝은 이 값을 읽기만 하고 요청하거나 반납하지 않는다.</summary>
    public bool HasAttackToken;

    /// <summary>
    /// 예고 길이에 곱할 배수. 플레이어가 보고 있지 않은 쪽에서 접근하면 1보다 크다 —
    /// 등 뒤의 예고는 화면에서 읽기 어려우므로 그만큼 더 길게 준다.
    /// </summary>
    public float TelegraphScale;

    /// <summary>아직 계산 전이거나 등록되지 않았을 때의 안전한 기본값. 공격 권한이 없다.</summary>
    public static EngagementOrder None => new EngagementOrder
    {
        Side = 1f,
        Rank = 0,
        Role = EnemyRole.Waiter,
        HasAttackToken = false,
        TelegraphScale = 1f,
    };
}

/// <summary>
/// 조율자가 대형을 짤 기준이 되는 대상(플레이어)의 한 순간. 실시간 좌표를 쓴다.
/// </summary>
public readonly struct TargetSnapshot
{
    public readonly Vector3 Position;

    /// <summary>대상이 보고 있는 방향의 x 부호. +1이면 오른쪽.</summary>
    public readonly float FacingX;

    public TargetSnapshot(Vector3 position, float facingX)
    {
        Position = position;
        FacingX = facingX;
    }
}
