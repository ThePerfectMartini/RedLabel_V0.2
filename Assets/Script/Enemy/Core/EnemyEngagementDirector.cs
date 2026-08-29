using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 여러 적이 한 대상을 둘러쌀 때 "누가 어디에 설지"를 한곳에서 정하는 조율자.
///
/// 예전에는 각 ChasePlayerBrain이 static 리스트를 직접 순회해서 자기 순번을 셌다. 계산 결과는 같지만
/// 대형에 대한 판단이 개체마다 흩어져 있어서, "동시에 몇 명만 공격시킬지" 같은 전체 규칙을 걸 자리가
/// 없었다. 그 자리를 만드는 것이 이 클래스의 목적이다.
///
/// MonoBehaviour가 아니다(MovementCore/CombatCore와 같은 원칙). 씬에 오브젝트를 놓지 않아도 되고,
/// 프레임 번호를 파라미터로 받으므로 UnityEngine.Time에도 의존하지 않는다.
///
/// [주의] 현재 순번 계산은 개체 수 n에 대해 프레임당 O(n^2)이다. 옮기기 전과 같은 비용이며
/// (예전에도 적마다 O(n)씩 걸려 합이 O(n^2)이었다), 벨트스크롤 한 화면의 적 수에서는 문제되지 않는다.
/// 줄인 것은 계산량이 아니라 판단이 흩어져 있던 것이다.
/// </summary>
public sealed class EnemyEngagementDirector
{
    public static EnemyEngagementDirector Instance { get; } = new EnemyEngagementDirector();

    readonly List<IEngagementMember> members = new List<IEngagementMember>();
    readonly Dictionary<int, EngagementSlot> slotsById = new Dictionary<int, EngagementSlot>();

    // 이번 프레임에 이미 대형을 계산했는지. 적들이 각자 다른 시점에 물어봐도 계산은 프레임당 한 번이고,
    // 그 프레임 안에서는 누가 먼저 물어보든 같은 답을 받는다.
    int lastTickedFrame = -1;

    /// <summary>적이 활성화될 때 등록한다. 등록 수명은 그대로 대형 참여 수명이다.</summary>
    public void Register(IEngagementMember member)
    {
        if (member == null || members.Contains(member)) return;
        members.Add(member);
    }

    /// <summary>적이 비활성화/파괴될 때 해제한다. 죽은 적이 대형에 자리를 남기지 않게 한다.</summary>
    public void Unregister(IEngagementMember member)
    {
        if (member == null) return;
        members.Remove(member);
        slotsById.Remove(member.Id);
    }

    /// <summary>
    /// 이번 프레임의 대형을 아직 계산하지 않았다면 계산한다. 적들이 각자 Think() 앞에서 호출하며,
    /// 먼저 호출한 하나가 전체를 계산하고 나머지는 그 결과를 읽기만 한다.
    /// frameId는 호출자가 Time.frameCount를 넘겨준다(이 클래스가 Unity 시간에 의존하지 않게 하려고).
    /// </summary>
    public void EnsureTicked(int frameId, Vector3 targetPosition)
    {
        if (lastTickedFrame == frameId) return;
        lastTickedFrame = frameId;

        RecomputeSlots(targetPosition);
    }

    /// <summary>배정된 자리를 읽는다. 아직 계산 전이거나 등록되지 않았으면 기본값(오른쪽 최전열)을 준다.</summary>
    public EngagementSlot GetSlot(IEngagementMember member)
    {
        if (member != null && slotsById.TryGetValue(member.Id, out EngagementSlot slot))
            return slot;

        return new EngagementSlot { Side = 1f, Rank = 0 };
    }

    /// <summary>
    /// 각 적이 대상의 어느 쪽에 있는지(Side)와, 그 쪽에서 몇 번째로 가까운지(Rank)를 계산한다.
    ///
    /// 순위는 고정 배정이 아니라 매번 거리로 다시 매긴다. 서로 위치가 바뀌면 순번도 따라 바뀌고,
    /// 앞의 적이 죽으면 뒤의 적이 바로 앞자리로 당겨진다.
    /// 거리가 같을 때는 Id로 동점을 깨서 매 프레임 순위가 흔들리지 않게 한다.
    /// </summary>
    void RecomputeSlots(Vector3 targetPosition)
    {
        slotsById.Clear();

        for (int i = 0; i < members.Count; i++)
        {
            IEngagementMember me = members[i];
            float mySide = me.Position.x >= targetPosition.x ? 1f : -1f;
            float myDistance = Mathf.Abs(me.Position.x - targetPosition.x);

            int rank = 0;
            for (int j = 0; j < members.Count; j++)
            {
                if (j == i) continue;

                IEngagementMember other = members[j];
                float otherSide = other.Position.x >= targetPosition.x ? 1f : -1f;
                if (!Mathf.Approximately(otherSide, mySide)) continue;

                float otherDistance = Mathf.Abs(other.Position.x - targetPosition.x);
                bool otherIsCloser = otherDistance < myDistance
                    || (otherDistance == myDistance && other.Id < me.Id);

                if (otherIsCloser)
                    rank++;
            }

            slotsById[me.Id] = new EngagementSlot { Side = mySide, Rank = rank };
        }
    }
}
