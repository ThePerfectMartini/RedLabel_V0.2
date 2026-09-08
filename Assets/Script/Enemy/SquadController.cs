using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 여러 적을 조율하는 상위 레이어 — 개별 <see cref="EnemyBrain"/> <b>바깥</b>에 있다(지침서 8.7).
/// 브레인끼리 직접 대화하지 않는다. 스쿼드가 각 브레인의 블랙보드 값(<see cref="EnemyBrain.SlotAngle"/> 등)에
/// 써주고, 공격 토큰을 발급/회수할 뿐이다. 그래서 각 행동은 스쿼드가 있든 없든 똑같이 독립 테스트된다.
///
/// 하는 일:
///   ① 포위 슬롯 — 등록된 적들을 대상 기준 현재 각도로 정렬해 360/N 간격 슬롯을 순서대로 배정(교차 없음)
///   ② 교대 공격 — 동시에 공격 상태로 들어갈 수 있는 인원을 maxSimultaneousAttackers로 제한
///
/// 씬에 하나. 없으면 각 적은 솔로로 동작한다.
/// </summary>
public class SquadController : MonoBehaviour
{
    [KoreanLabel("동시 공격 가능 수")]
    [Min(0)]
    [Tooltip("한 번에 공격 상태로 진입할 수 있는 적의 수. 나머지는 포위만 유지한다(교대 공격).")]
    public int maxSimultaneousAttackers = 1;

    [KoreanLabel("슬롯 재배정 간격(초)")]
    [Min(0.1f)]
    public float reassignInterval = 0.5f;

    static SquadController instance;

    /// <summary>씬 안의 SquadController를 지연 탐색해 캐싱. Awake 순서에 의존하지 않는다(MapBounds와 같은 패턴).</summary>
    public static SquadController Instance
    {
        get
        {
            if (instance == null)
                instance = FindFirstObjectByType<SquadController>();
            return instance;
        }
    }

    readonly List<EnemyBrain> members = new List<EnemyBrain>();
    readonly HashSet<EnemyBrain> attackers = new HashSet<EnemyBrain>();
    float nextReassign;

    public int MemberCount => members.Count;
    public int AttackerCount => attackers.Count;

    void Awake()
    {
        if (instance != null && instance != this)
            Debug.LogWarning($"씬에 SquadController가 여러 개 있습니다. '{instance.name}'을 계속 사용합니다.");
        else
            instance = this;
    }

    // ===== 등록 =====

    public void Register(EnemyBrain e)
    {
        if (!members.Contains(e))
            members.Add(e);
        nextReassign = 0f; // 다음 Update에서 즉시 재배정
    }

    public void Unregister(EnemyBrain e)
    {
        members.Remove(e);
        attackers.Remove(e);
        e.HasSlotAssignment = false;
        nextReassign = 0f;
    }

    // ===== 포위 슬롯 =====

    void Update()
    {
        if (Time.time < nextReassign)
            return;

        AssignSlots();
        nextReassign = Time.time + reassignInterval;
    }

    void AssignSlots()
    {
        int n = members.Count;
        if (n == 0)
            return;

        // 현재 각도로 정렬 → 슬롯을 순서대로 주면 서로 가로지르지 않는다.
        members.Sort((a, b) => CurrentAngleOf(a).CompareTo(CurrentAngleOf(b)));

        float step = 360f / n;
        // 첫 멤버가 이미 있는 위치에 가까운 슬롯 배수를 기준점으로 → 전체 이동 최소화.
        float phase = Mathf.Round(CurrentAngleOf(members[0]) / step) * step;

        for (int i = 0; i < n; i++)
        {
            members[i].SlotAngle = Mathf.Repeat(phase + step * i, 360f);
            members[i].HasSlotAssignment = true;
        }
    }

    static float CurrentAngleOf(EnemyBrain e) => e.Orbit != null ? e.Orbit.CurrentAngle : 0f;

    // ===== 교대 공격 토큰 =====

    public bool HasFreeAttackSlot() => attackers.Count < maxSimultaneousAttackers;

    public bool TryClaimAttackSlot(EnemyBrain e)
    {
        if (attackers.Contains(e))
            return true;
        if (attackers.Count >= maxSimultaneousAttackers)
            return false;
        attackers.Add(e);
        return true;
    }

    public void ReleaseAttackSlot(EnemyBrain e) => attackers.Remove(e);
}
