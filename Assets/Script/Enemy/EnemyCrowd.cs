using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 지금 살아 있는 적들의 위치를 모아 두는 명부. 적이 <b>목적지를 고르는 순간</b>에
/// "거기 이미 누가 서 있는가"를 물어보려고 존재한다.
///
/// 캐릭터끼리 밀어내는 물리 충돌은 없다 — 겹친 뒤에 떼어놓는 대신 애초에 남이 있는 자리를
/// 목적지로 고르지 않게 하는 쪽이다. 그래서 매 프레임이 아니라 목표를 새로 잡을 때만 불린다.
/// 그래도 겹치는 순간(둘이 동시에 같은 곳으로 밀려오는 등)은 남는다. 그건 허용한다.
///
/// 정적 목록이라 도메인 리로드를 끈 설정에서는 플레이 모드를 나갔다 들어와도 남는다. 진입 시 한 번 비운다.
/// </summary>
public static class EnemyCrowd
{
    static readonly List<Transform> members = new List<Transform>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ClearOnEnterPlayMode() => members.Clear();

    public static void Register(Transform member)
    {
        if (member != null && !members.Contains(member))
            members.Add(member);
    }

    public static void Unregister(Transform member) => members.Remove(member);

    /// <summary>
    /// <paramref name="flatPoint"/>(xz 평면)에서 가장 가까운 <b>다른</b> 적까지의 거리.
    /// 아무도 없으면 float.MaxValue — "여긴 완전히 비었다".
    /// </summary>
    public static float ClearanceAt(Vector3 flatPoint, Transform ignore)
    {
        float nearest = float.MaxValue;

        for (int i = 0; i < members.Count; i++)
        {
            Transform member = members[i];

            // == null 은 파괴된 오브젝트(가짜 null)까지 걸러낸다. ?. 로 바꾸면 안 된다.
            if (member == null || member == ignore) continue;

            float dx = member.position.x - flatPoint.x;
            float dz = member.position.z - flatPoint.z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);

            if (distance < nearest)
                nearest = distance;
        }

        return nearest;
    }
}
