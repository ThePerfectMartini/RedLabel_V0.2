using UnityEngine;

/// <summary>
/// TEMP: 플레이어 양옆 공격권 자리가 <b>지금 누구 것인지</b>를 씬 뷰에 그린다.
/// 적이 여럿일 때 "왜 저 적은 안 덤비지"의 답이 거의 항상 여기 있어서, 눈으로 볼 수 있어야 수치를 맞출 수 있다.
///
/// [붙이는 곳] AttackSlots와 같은 오브젝트(플레이어 루트).
/// [보이는 것] 초록 = 빈 자리 / 빨강 = 누가 쥐고 있는 자리. 자리에서 그 적까지 선을 긋는다.
/// </summary>
[RequireComponent(typeof(AttackSlots))]
public class AttackSlotsGizmo : MonoBehaviour
{
    [KoreanLabel("표시할 슬롯 층")]
    public AttackReach reach = AttackReach.ShortMelee;

    [KoreanLabel("표시 거리")]
    [Tooltip("자리를 플레이어에게서 좌우로 이만큼 떨어뜨려 그린다. 표시 전용이라 로직과는 무관하다 " +
             "— 적의 '트리거 범위 중심 거리'와 비슷하게 맞춰 두면 읽기 편하다.")]
    public float sideDistance = 1.5f;

    [KoreanLabel("표시 높이")]
    public float drawHeight = 0.1f;

    void OnDrawGizmos()
    {
        AttackSlots slots = GetComponent<AttackSlots>();
        if (slots == null) return;

        Vector3 lift = Vector3.up * drawHeight;

        DrawSide(slots, AttackSide.Left, lift);
        DrawSide(slots, AttackSide.Right, lift);
    }

    void DrawSide(AttackSlots slots, AttackSide side, Vector3 lift)
    {
        Vector3 spot = transform.position + Vector3.right * (AttackSlots.SignOf(side) * sideDistance) + lift;
        GameObject holder = slots.HolderOf(reach, side);

        Gizmos.color = holder == null ? Color.green : Color.red;
        Gizmos.DrawWireSphere(spot, 0.3f);

        if (holder != null)
            Gizmos.DrawLine(spot, holder.transform.position + lift);
    }
}
