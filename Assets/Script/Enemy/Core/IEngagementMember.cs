using UnityEngine;

/// <summary>
/// EnemyEngagementDirector가 대형을 짤 때 필요한 최소 정보. 적 AI 컴포넌트가 구현한다.
///
/// Director를 MonoBehaviour에서 떼어놓기 위한 경계다(MovementCore/CombatCore와 같은 이유).
/// Director가 매 틱 각자의 위치를 "직접 읽어가는" 구조라서, 적들이 각자 다른 시점에 생각해도
/// 대형 계산은 항상 같은 순간의 좌표로 이뤄진다.
/// 반대로 각자가 자기 위치를 Director에 밀어넣는 방식이었다면, 먼저 생각한 적은 아직 갱신되지 않은
/// 다른 적의 좌표로 자기 순번을 정하게 되어 한 프레임씩 어긋난다.
/// </summary>
public interface IEngagementMember
{
    /// <summary>동점 처리에 쓰는 안정적인 키. 매 프레임 순위가 흔들리지 않게 하는 최후의 기준이다.</summary>
    int Id { get; }

    /// <summary>대형 계산에 쓰는 현재 위치.</summary>
    Vector3 Position { get; }
}

/// <summary>
/// 한 적에게 배정된 자리. Director가 매 프레임 다시 계산해서 나눠준다.
/// </summary>
public struct EngagementSlot
{
    /// <summary>대상 기준 어느 쪽에 설지. +1이면 대상의 오른쪽, -1이면 왼쪽.</summary>
    public float Side;

    /// <summary>같은 쪽에서의 대기 순번. 0이면 최전열(공격 가능한 자리), 1 이상이면 그만큼 뒤에서 대기.</summary>
    public int Rank;
}
