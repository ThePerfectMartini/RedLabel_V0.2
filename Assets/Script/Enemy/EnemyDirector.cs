using System.Collections.Generic;
using UnityEngine;

/// <summary>대기 중인 적이 설 자리. 공격자를 기준으로 뒤 / 위 / 아래 세 곳뿐이다.</summary>
public enum StandbySlot
{
    /// <summary>자리를 못 받았다. 세 자리가 이미 찼을 때 넷째부터가 여기 해당한다.</summary>
    None = -1,

    /// <summary>공격자 뒤. 플레이어와 같은 z.</summary>
    Back = 0,

    /// <summary>공격자 뒤의 위쪽(z+).</summary>
    Up = 1,

    /// <summary>공격자 뒤의 아래쪽(z-).</summary>
    Down = 2,
}

/// <summary>
/// 조율자가 적 한 마리에게 나눠주는 자리. 좌표가 아니라 "어느 쪽 어느 자리"만 준다 —
/// 실제 좌표는 각자의 사거리와 튜닝에 따라 달라지므로 적 본인이 계산하는 편이 정확하다.
/// </summary>
public readonly struct EngagementOrder
{
    /// <summary>플레이어의 어느 쪽에 설지. +1이면 오른쪽, -1이면 왼쪽.</summary>
    public readonly int Side;

    /// <summary>공격 권한이 없을 때 기다릴 자리.</summary>
    public readonly StandbySlot Slot;

    public EngagementOrder(int side, StandbySlot slot)
    {
        Side = side;
        Slot = slot;
    }
}

/// <summary>
/// 적들이 겹치지 않게 자리를 나누고, 동시에 덤비지 않게 공격 권한을 나눠주는 조율자. 씬에 하나만 둔다.
///
/// [무엇을 정하는가] 세 가지뿐이다. 어느 쪽에 설지, 지금 칠 수 있는지, 못 칠 때 어느 자리에서 기다릴지.
/// 그 외의 판단(언제 물러날지, 언제 접근할지)에는 관여하지 않는다 — 그건 브레인의 조건표가 정한다.
///
/// [권한은 붙잡고 있는 것이다] 한 번 공격했다고 권한이 바로 넘어가지 않는다. 매번 다른 적이 한 대씩
/// 치고 빠지면 누가 상대인지 알 수 없는 난전이 된다. 그래서 권한을 받은 적이 물러날 때까지 계속
/// 들고 있고, 기다리던 적이 더 가까워졌을 때만 넘어간다.
///
/// [전투에 들어온 적만 센다] 아직 배회 중인 적은 자리 배정과 권한 계산에서 통째로 빠진다.
/// 멀리서 서성이는 적이 권한을 차지해버리면 정작 붙어 있는 적이 싸우지 못한다.
///
/// [없어도 된다] 조율자가 씬에 없으면 각 적이 스스로 가까운 쪽에 서서 항상 공격 권한을 갖는다.
/// 적이 한 마리일 때의 동작은 조율자가 있든 없든 완전히 같다.
///
/// [실행 순서에 기대지 않는다] 브레인이 물어보는 시점에 프레임당 한 번만 다시 계산한다.
///
/// [준비물] 씬의 아무 오브젝트에나 하나 붙일 것. 씬에 PlayerController가 하나 있어야 한다.
/// </summary>
public class EnemyDirector : MonoBehaviour
{
    [KoreanLabel("동시에 공격할 수 있는 적 수")]
    [Tooltip("한 번에 공격 권한을 가질 수 있는 적의 수. 나머지는 대기 자리에서 공격을 준비한다.")]
    public int maxSimultaneousAttackers = 1;

    static EnemyDirector instance;

    /// <summary>씬 안의 조율자를 찾아 캐싱해서 반환. 없으면 null이며, 그 경우 적들은 각자 판단한다.</summary>
    public static EnemyDirector Instance
    {
        get
        {
            if (instance == null)
                instance = FindFirstObjectByType<EnemyDirector>();
            return instance;
        }
    }

    // 좌우 판단이 흔들리지 않게 하는 값. 플레이어가 적을 통과해 지나갈 때 x 차이가 0 근처를 오가면서
    // 자리가 좌우로 튀는 것을 막는다. 튜닝 값이 아니라 튐 방지용이라 인스펙터에 내지 않는다.
    const float SideSwitchThreshold = 0.5f;

    // 기다리던 적이 이만큼은 더 가까워야 공격 권한을 뺏어온다. 없으면 거의 같은 거리의 두 적이
    // 매 프레임 권한을 주고받으면서 둘 다 제대로 공격하지 못한다.
    const float PermissionStealMargin = 1f;

    class Member
    {
        public EnemyBrain Brain;
        public int Side;
        public float Distance;
        public bool CanAttack;
        public StandbySlot Slot;

        /// <summary>지금 플레이어를 상대하는 중인지. 아직 배회 중인 적은 자리도 권한도 받지 않는다.</summary>
        public bool Engaged;
    }

    readonly List<Member> members = new List<Member>();

    PlayerController target;
    int lastComputedFrame = -1;

    void Awake()
    {
        if (instance != null && instance != this)
            Debug.LogWarning("씬에 EnemyDirector가 여러 개 있습니다. 먼저 찾은 것을 계속 사용합니다.");
        else
            instance = this;

        target = PlayerController.Instance;
        if (target == null)
            Debug.LogWarning($"{name}: 씬에 PlayerController가 없어 자리를 계산할 수 없습니다.");

        // 이 컴포넌트보다 먼저 켜진 적들은 등록 시점을 놓쳤을 수 있으므로 한 번 훑어서 채운다.
        // 시작할 때 한 번뿐이라 Find를 써도 된다.
        foreach (EnemyBrain brain in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            Register(brain);
    }

    public void Register(EnemyBrain brain)
    {
        if (brain == null || FindMember(brain) != null) return;

        members.Add(new Member { Brain = brain, Side = 0, Slot = StandbySlot.None });
    }

    public void Unregister(EnemyBrain brain)
    {
        Member member = FindMember(brain);
        if (member != null)
            members.Remove(member);
    }

    /// <summary>이 적이 이번 프레임에 받을 자리. 등록되지 않은 적이 물어보면 자기 쪽 앞자리를 준다.</summary>
    public EngagementOrder GetOrder(EnemyBrain brain)
    {
        RecomputeIfNeeded();

        Member member = FindMember(brain);
        return member != null
            ? new EngagementOrder(AttackerSide(member), member.Slot)
            : new EngagementOrder(PickSide(0, brain.transform.position.x, TargetX), StandbySlot.None);
    }

    /// <summary>
    /// 이 적이 지금 공격을 시작해도 되는지. 등록되지 않은 적에게는 허락한다 — 조율자가 모르는 적을
    /// 묶어둘 이유가 없다.
    /// </summary>
    public bool HasAttackPermission(EnemyBrain brain)
    {
        RecomputeIfNeeded();

        Member member = FindMember(brain);
        return member == null || member.CanAttack;
    }

    /// <summary>
    /// 플레이어의 어느 쪽에 설지 정한다. 지금 있는 쪽을 그대로 쓰되, 어느 쪽인지 애매할 만큼
    /// 가까울 때는 이전 결정을 유지한다. 조율자가 없을 때 브레인도 이 규칙을 그대로 쓴다.
    /// </summary>
    public static int PickSide(int currentSide, float selfX, float targetX)
    {
        float dx = selfX - targetX;

        if (Mathf.Abs(dx) > SideSwitchThreshold)
            return dx > 0f ? 1 : -1;

        return currentSide != 0 ? currentSide : (dx >= 0f ? 1 : -1);
    }

    float TargetX => target != null ? target.Position.x : 0f;

    /// <summary>
    /// 대기 자리는 공격자 뒤에 만들어지므로, 기다리는 적에게는 자기 쪽이 아니라 공격자의 쪽을 알려준다.
    /// 공격자가 아직 없으면 자기 쪽을 쓴다.
    /// </summary>
    int AttackerSide(Member member)
    {
        if (member.CanAttack) return member.Side;

        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].CanAttack)
                return members[i].Side;
        }

        return member.Side;
    }

    void RecomputeIfNeeded()
    {
        if (lastComputedFrame == Time.frameCount) return;
        lastComputedFrame = Time.frameCount;

        if (target == null) return;

        Vector3 targetPosition = target.Position;

        // 죽어서 사라진 적을 걷어내면서 각자의 좌/우와 거리를 갱신한다.
        for (int i = members.Count - 1; i >= 0; i--)
        {
            Member member = members[i];

            if (member.Brain == null)
            {
                members.RemoveAt(i);
                continue;
            }

            Vector3 offset = member.Brain.transform.position - targetPosition;
            offset.y = 0f;

            member.Distance = offset.magnitude;
            member.Side = PickSide(member.Side, member.Brain.transform.position.x, targetPosition.x);

            // 아직 전투에 들어오지 않은 적(배회 중)은 아래 계산에서 통째로 빠진다. 멀리서 서성이는 적이
            // 공격 권한이나 대기 자리를 차지해버리면, 정작 붙어 있는 적이 싸우지 못한다.
            member.Engaged = member.Brain.IsEngaged;

            if (!member.Engaged)
            {
                member.CanAttack = false;
                member.Slot = StandbySlot.None;
            }
        }

        UpdateAttackPermission();
        AssignStandbySlots();
    }

    /// <summary>
    /// 공격 권한을 갱신한다. 세 규칙이 전부다.
    ///
    /// 1. 물러나는 적은 권한을 놓는다 — 그게 상대를 넘겨주는 순간이다
    /// 2. 빈 자리는 가장 가까운 적이 채운다
    /// 3. 기다리던 적이 충분히 더 가까워졌으면 넘겨받는다. 단 지금 휘두르는 중인 적에게서는 뺏지 않는다
    ///
    /// [빌리고 반납하는 방식을 쓰지 않은 이유] 명시적 대여는 반납을 빠뜨리는 경로가 반드시 생긴다 —
    /// 공격 도중 죽거나, 얻어맞아 끊기거나, 오브젝트가 꺼지거나. 그 한 번의 누수로 아무도 공격하지 못하는
    /// 상태에 빠지고 원인을 찾기가 매우 어렵다. 여기서는 매 프레임 상태를 보고 다시 정하므로 반납이 없다.
    /// </summary>
    void UpdateAttackPermission()
    {
        for (int i = 0; i < members.Count; i++)
        {
            Member member = members[i];
            if (member.CanAttack && member.Brain.IsRetreating)
                member.CanAttack = false;
        }

        int held = 0;
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].CanAttack) held++;
        }

        while (held < maxSimultaneousAttackers)
        {
            Member nearest = NearestWithoutPermission();
            if (nearest == null) break;

            nearest.CanAttack = true;
            held++;
        }

        // 교체는 한 프레임에 한 번이면 충분하다. 여러 쌍을 한꺼번에 뒤집으면 그 프레임에 누가 상대인지
        // 알아볼 수 없게 바뀐다.
        Member challenger = NearestWithoutPermission();
        Member weakest = FarthestHolder();

        if (challenger != null && weakest != null &&
            challenger.Distance + PermissionStealMargin < weakest.Distance)
        {
            weakest.CanAttack = false;
            challenger.CanAttack = true;
        }
    }

    /// <summary>공격 권한이 없는 적 중 플레이어에게 가장 가까운 하나.</summary>
    Member NearestWithoutPermission()
    {
        Member best = null;

        for (int i = 0; i < members.Count; i++)
        {
            Member candidate = members[i];
            if (candidate.CanAttack || !candidate.Engaged) continue;

            if (best == null || candidate.Distance < best.Distance ||
                (candidate.Distance == best.Distance && candidate.Brain.GetInstanceID() < best.Brain.GetInstanceID()))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>권한을 가진 적 중 가장 먼 하나. 휘두르는 중인 적은 뺏을 대상에서 제외한다.</summary>
    Member FarthestHolder()
    {
        Member worst = null;

        for (int i = 0; i < members.Count; i++)
        {
            Member holder = members[i];
            if (!holder.CanAttack || holder.Brain.IsAttackCommitted) continue;

            if (worst == null || holder.Distance > worst.Distance)
                worst = holder;
        }

        return worst;
    }

    /// <summary>
    /// 기다리는 적들에게 뒤 / 위 / 아래 세 자리를 하나씩 나눠준다. 한 자리에 한 마리가 규칙이고,
    /// 넷째부터는 자리 없이 더 뒤에서 거리만 지킨다.
    ///
    /// 자리를 거리 순서에서 계산하지 않고 따로 배정해 유지하는 이유는, 적들이 움직이는 동안 순서가 계속
    /// 바뀌는데 자리까지 같이 바뀌면 서로의 자리를 향해 가운데를 가로지르며 엇갈리기 때문이다.
    /// </summary>
    void AssignStandbySlots()
    {
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].CanAttack)
                members[i].Slot = StandbySlot.None;
        }

        // 같은 자리를 든 적이 둘이면 나중 하나를 비워서 아래에서 새로 받게 한다.
        // 기준을 InstanceID로 고정해서 매 프레임 같은 쪽이 자리를 지키게 한다.
        for (int i = 0; i < members.Count; i++)
        {
            Member member = members[i];
            if (member.CanAttack || member.Slot == StandbySlot.None) continue;

            for (int j = 0; j < members.Count; j++)
            {
                if (i == j) continue;

                Member other = members[j];
                if (other.CanAttack || other.Slot != member.Slot) continue;

                if (other.Brain.GetInstanceID() < member.Brain.GetInstanceID())
                {
                    member.Slot = StandbySlot.None;
                    break;
                }
            }
        }

        // 가까운 적부터 빈 자리를 채운다 (뒤 -> 위 -> 아래 순).
        while (true)
        {
            StandbySlot free = FirstFreeSlot();
            if (free == StandbySlot.None) break;

            Member next = NearestUnslotted();
            if (next == null) break;

            next.Slot = free;
        }
    }

    StandbySlot FirstFreeSlot()
    {
        if (!IsSlotTaken(StandbySlot.Back)) return StandbySlot.Back;
        if (!IsSlotTaken(StandbySlot.Up)) return StandbySlot.Up;
        if (!IsSlotTaken(StandbySlot.Down)) return StandbySlot.Down;

        return StandbySlot.None;
    }

    bool IsSlotTaken(StandbySlot slot)
    {
        for (int i = 0; i < members.Count; i++)
        {
            if (!members[i].CanAttack && members[i].Engaged && members[i].Slot == slot)
                return true;
        }

        return false;
    }

    Member NearestUnslotted()
    {
        Member best = null;

        for (int i = 0; i < members.Count; i++)
        {
            Member candidate = members[i];
            if (candidate.CanAttack || !candidate.Engaged || candidate.Slot != StandbySlot.None) continue;

            if (best == null || candidate.Distance < best.Distance ||
                (candidate.Distance == best.Distance && candidate.Brain.GetInstanceID() < best.Brain.GetInstanceID()))
            {
                best = candidate;
            }
        }

        return best;
    }

    Member FindMember(EnemyBrain brain)
    {
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].Brain == brain)
                return members[i];
        }

        return null;
    }
}
