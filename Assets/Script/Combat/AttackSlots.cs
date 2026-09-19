using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 공격 슬롯의 층. 리치가 다른 공격끼리는 서로 자리를 막지 않으므로 층을 나눠서 센다.
/// 점프대쉬처럼 슬롯을 쓰지 않는 공격은 여기에 아예 묻지 않는다.
/// </summary>
public enum AttackReach
{
    [InspectorName("짧은 근접")]
    ShortMelee,
    [InspectorName("긴 근접")]
    LongMelee,
}

/// <summary>
/// 플레이어 양옆에서 동시에 때릴 수 있는 적 수를 제한하는 "공격권" 관리자 (설계서 7장).
/// 여러 적이 공유해야 하므로 적 개체가 아니라 <b>플레이어 루트</b>에 붙인다.
///
/// <b>슬롯은 "공격권"이지 "고정된 자리"가 아니다.</b> 왼쪽 슬롯을 잡았다고 끝까지 왼쪽만 쫓는 게 아니라,
/// 잡는다는 것은 때릴 권리를 얻었다는 뜻이고 실제로 어느 옆구리로 갈지는 그 순간 가까운 쪽이다
/// (플레이어가 적을 지나쳐 좌우가 뒤집히면 목표만 바뀌고 공격권은 유지된다).
/// 그래서 좌/우를 따로 예약하지 않고 층별 <b>동시 공격 수</b>만 센다 — 기본 2 = 왼쪽 1 + 오른쪽 1.
///
/// 이 컴포넌트가 씬에 없으면 각 적은 "슬롯이 항상 비어 있다"로 보고 평소대로 동작한다
/// (적이 하나뿐인 테스트에서는 어차피 결과가 같다).
/// </summary>
public class AttackSlots : MonoBehaviour
{
    [KoreanLabel("짧은 근접 동시 공격 수")]
    [Tooltip("설계서 기준 2(왼쪽 1 + 오른쪽 1). 늘리면 같은 리치의 적이 그만큼 한꺼번에 달려든다.")]
    [Min(0)]
    public int shortMeleeCapacity = 2;

    [KoreanLabel("긴 근접 동시 공격 수")]
    [Tooltip("짧은 근접과 별개로 센다. 리치가 다르면 서로 자리를 막지 않는다.")]
    [Min(0)]
    public int longMeleeCapacity = 2;

    // 층별 공격권 보유자. 적이 파괴돼도 자리가 영영 잠기지 않게 획득 시점에 청소한다.
    readonly HashSet<GameObject>[] holders =
    {
        new HashSet<GameObject>(),
        new HashSet<GameObject>(),
    };

    // 파괴된 보유자를 걸러낼 때 쓰는 임시 목록. HashSet은 순회 중 제거가 안 되므로 따로 모았다가 뺀다.
    readonly List<GameObject> stale = new List<GameObject>();

    /// <summary>지금 이 층의 공격권을 몇 개나 내줬는지. 디버그 표시용.</summary>
    public int UsedCount(AttackReach reach)
    {
        Prune(reach);
        return HoldersOf(reach).Count;
    }

    public int CapacityOf(AttackReach reach) =>
        reach == AttackReach.ShortMelee ? shortMeleeCapacity : longMeleeCapacity;

    /// <summary><paramref name="claimant"/>가 지금 이 층의 공격권을 들고 있는지.</summary>
    public bool Holds(AttackReach reach, GameObject claimant) =>
        claimant != null && HoldersOf(reach).Contains(claimant);

    /// <summary>
    /// 지금 공격권을 새로 받을 수 있는지. 이미 들고 있으면 true다 —
    /// 묻는 쪽 입장에서는 "지금 공격하러 가도 되는가"가 알고 싶은 것이기 때문.
    /// </summary>
    public bool HasFreeSlot(AttackReach reach, GameObject claimant)
    {
        if (Holds(reach, claimant)) return true;

        Prune(reach);
        return HoldersOf(reach).Count < CapacityOf(reach);
    }

    /// <summary>공격권을 하나 받는다. 이미 들고 있으면 그대로 true. 자리가 없으면 false.</summary>
    public bool TryAcquire(AttackReach reach, GameObject claimant)
    {
        if (claimant == null) return false;
        if (!HasFreeSlot(reach, claimant)) return false;

        HoldersOf(reach).Add(claimant);
        return true;
    }

    /// <summary>공격권 반납. 안 들고 있었으면 아무 일도 없다.</summary>
    public void Release(AttackReach reach, GameObject claimant)
    {
        if (claimant == null) return;
        HoldersOf(reach).Remove(claimant);
    }

    HashSet<GameObject> HoldersOf(AttackReach reach) => holders[(int)reach];

    /// <summary>
    /// 파괴된 보유자를 걸러낸다. 적이 죽어 사라졌는데 공격권이 남아 있으면 그 자리가 영영 잠긴다.
    /// UnityEngine.Object의 == null은 "파괴됨"까지 잡아내므로(가짜 null) 여기서는 그 동작이 필요하다 —
    /// ?. 나 ?? 로 바꾸면 파괴된 오브젝트를 걸러내지 못한다.
    /// </summary>
    void Prune(AttackReach reach)
    {
        HashSet<GameObject> set = HoldersOf(reach);
        stale.Clear();

        foreach (GameObject holder in set)
        {
            if (holder == null)
                stale.Add(holder);
        }

        foreach (GameObject dead in stale)
            set.Remove(dead);
    }
}
