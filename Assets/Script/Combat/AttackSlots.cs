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

/// <summary>플레이어를 기준으로 한 옆구리. 슬롯은 층마다 이 둘이 하나씩 있다.</summary>
public enum AttackSide
{
    [InspectorName("왼쪽")]
    Left,
    [InspectorName("오른쪽")]
    Right,
}

/// <summary>
/// 플레이어 양옆에서 동시에 때릴 수 있는 적 수를 제한하는 "공격권" 관리자 (설계서 7장).
/// 여러 적이 공유해야 하므로 적 개체가 아니라 <b>플레이어 루트</b>에 붙인다.
///
/// 층마다 <b>왼쪽 1 · 오른쪽 1</b>로 자리가 나뉜다. 수를 세기만 하면(동시 2명) 적 둘이 모두
/// 플레이어 오른쪽에 있을 때 둘 다 오른쪽에서 때려 겹치므로, 쪽을 따로 붙잡게 한다.
///
/// <b>슬롯은 "공격권"이지 "고정된 자리"가 아니다.</b> 왼쪽 슬롯을 잡았다고 끝까지 왼쪽만 쫓는 게 아니라,
/// 플레이어가 적을 지나쳐 좌우가 뒤집히면 가까워진 쪽이 비어 있는 한 그쪽으로 옮겨 앉는다
/// (<see cref="MeleeEnemyContext.RefreshAttackSlotSide"/>). 차 있으면 잡은 쪽을 그대로 들고 간다.
///
/// 이 컴포넌트가 씬에 없으면 각 적은 "슬롯이 항상 비어 있다"로 보고 평소대로 동작한다
/// (적이 하나뿐인 테스트에서는 어차피 결과가 같다).
/// </summary>
public class AttackSlots : MonoBehaviour
{
    // 층 × 좌우. 인덱스는 IndexOf가 만든다. 적이 파괴돼도 자리가 영영 잠기지 않게 읽을 때마다 청소한다.
    readonly GameObject[] holders = new GameObject[4];

    /// <summary>+1이면 오른쪽, -1이면 왼쪽. 방향 부호와 슬롯 쪽을 오가는 유일한 통로.</summary>
    public static AttackSide SideFromSign(float sign) => sign >= 0f ? AttackSide.Right : AttackSide.Left;

    public static float SignOf(AttackSide side) => side == AttackSide.Right ? 1f : -1f;

    public static AttackSide Opposite(AttackSide side) =>
        side == AttackSide.Right ? AttackSide.Left : AttackSide.Right;

    /// <summary>
    /// 지금 이 자리를 쥐고 있는 적. 비었으면 null.
    ///
    /// UnityEngine.Object의 == null은 "파괴됨"까지 잡아내므로(가짜 null) 여기서 진짜 null로 바꿔 둔다 —
    /// 그러지 않으면 죽어 사라진 적이 자리를 영영 잠가서 다른 적들이 영원히 못 때린다.
    /// ?. 나 ?? 로 바꾸면 파괴된 오브젝트를 걸러내지 못한다.
    /// </summary>
    public GameObject HolderOf(AttackReach reach, AttackSide side)
    {
        int index = IndexOf(reach, side);
        if (holders[index] == null)
            holders[index] = null;

        return holders[index];
    }

    /// <summary>이 자리가 비었는지. <paramref name="claimant"/>가 이미 앉아 있으면 true다 —
    /// 묻는 쪽 입장에서는 "지금 여기서 때려도 되는가"가 알고 싶은 것이기 때문.</summary>
    public bool IsSideFree(AttackReach reach, AttackSide side, GameObject claimant)
    {
        GameObject holder = HolderOf(reach, side);
        return holder == null || holder == claimant;
    }

    /// <summary><paramref name="claimant"/>가 이 층의 공격권을 (어느 쪽이든) 들고 있는지.</summary>
    public bool Holds(AttackReach reach, GameObject claimant) => TryGetSide(reach, claimant, out _);

    /// <summary><paramref name="claimant"/>가 앉아 있는 쪽. 없으면 false.</summary>
    public bool TryGetSide(AttackReach reach, GameObject claimant, out AttackSide side)
    {
        if (claimant != null)
        {
            if (HolderOf(reach, AttackSide.Left) == claimant)
            {
                side = AttackSide.Left;
                return true;
            }

            if (HolderOf(reach, AttackSide.Right) == claimant)
            {
                side = AttackSide.Right;
                return true;
            }
        }

        side = AttackSide.Left;
        return false;
    }

    /// <summary>
    /// 이 쪽 자리를 받는다. 이미 앉아 있으면 그대로 true, 남이 앉아 있으면 false.
    /// 반대쪽을 들고 있었다면 그 자리를 놓고 옮겨 앉는다(공격권을 새로 받는 게 아니라 이동이다).
    /// </summary>
    public bool TryAcquire(AttackReach reach, AttackSide side, GameObject claimant)
    {
        if (claimant == null) return false;
        if (!IsSideFree(reach, side, claimant)) return false;

        Release(reach, claimant);
        holders[IndexOf(reach, side)] = claimant;
        return true;
    }

    /// <summary>공격권 반납. 안 들고 있었으면 아무 일도 없다.</summary>
    public void Release(AttackReach reach, GameObject claimant)
    {
        if (claimant == null) return;

        if (TryGetSide(reach, claimant, out AttackSide side))
            holders[IndexOf(reach, side)] = null;
    }

    static int IndexOf(AttackReach reach, AttackSide side) => (int)reach * 2 + (int)side;
}
