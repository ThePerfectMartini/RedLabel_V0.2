using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 조율자. 여러 적이 한 대상을 둘러쌀 때 "누가 어디에 설지"(슬롯)와 "누가 언제 칠지"(토큰)를
/// 한곳에서 정한다.
///
/// [왜 필요한가] 개체가 각자 최적으로 판단하면 결과적으로 다 같이 최적 타이밍에 들어온다.
/// 그건 난이도가 아니라 회피 불가능한 벽이다. 벨트스크롤에서 난이도는 개체의 지능이 아니라
/// 군집의 안무에서 나오고, 그 안무를 거는 자리가 여기다.
///
/// [토큰] 예고로 넘어갈 권한. 조율자만 소유한다. 스텝은 EngagementOrder.HasAttackToken을
/// 읽기만 하고 직접 요청하거나 반납하지 않는다. 이 규칙 덕분에 "동시에 몇이 칠지"를 한 곳에서
/// 바꿀 수 있고, 스텝은 아무것도 몰라도 된다.
///
/// [난이도 손잡이] DirectorSettings의 세 값이 사실상 난이도 전부다.
/// 데미지나 체력이 아니라 "플레이어가 반응할 시간"이라는 하나의 축 위에 있어서 체감이 정직하다.
///
/// MonoBehaviour가 아니다 (MovementCore/CombatCore와 같은 원칙). 프레임 번호와 deltaTime을
/// 파라미터로 받으므로 UnityEngine.Time에도 의존하지 않는다.
///
/// [비용] 순번 계산은 개체 수 n에 대해 프레임당 O(n^2)이다. 기존 EnemyEngagementDirector와
/// 같은 비용이며, 벨트스크롤 한 화면의 적 수에서는 문제되지 않는다.
/// </summary>
public sealed class EncounterDirector
{
    public static EncounterDirector Instance { get; } = new EncounterDirector();

    public DirectorSettings Settings = DirectorSettings.Default;

    /// <summary>지금 토큰을 들고 있는 적의 수. 디버그 표시가 읽는다.</summary>
    public int TokenCount => holders.Count;

    /// <summary>등록된 적의 수. 디버그 표시가 읽는다.</summary>
    public int MemberCount => members.Count;

    struct TokenHolder
    {
        public int Id;

        /// <summary>토큰을 받은 뒤 지난 시간. 받아놓고 안 쓰는 적에게서 회수하는 데 쓴다.</summary>
        public float Held;

        /// <summary>한 번이라도 예고/공격에 실제로 들어갔는가.</summary>
        public bool HasCommitted;
    }

    readonly List<IEncounterMember> members = new List<IEncounterMember>();
    readonly Dictionary<int, EngagementOrder> ordersById = new Dictionary<int, EngagementOrder>();
    readonly List<TokenHolder> holders = new List<TokenHolder>();
    readonly List<IEncounterMember> deadMembers = new List<IEncounterMember>();

    int lastTickedFrame = -1;
    float grantCooldown;

    // 같은 적이 연달아 토큰을 독점하지 않게 하는 최소한의 교대 장치.
    int lastGrantedId = -1;

    public void Register(IEncounterMember member)
    {
        if (member == null || members.Contains(member)) return;
        members.Add(member);
    }

    public void Unregister(IEncounterMember member)
    {
        if (member == null) return;

        members.Remove(member);
        ordersById.Remove(member.Id);
        ReleaseToken(member.Id);
    }

    /// <summary>
    /// 이번 프레임의 대형과 토큰을 아직 계산하지 않았다면 계산한다. 적들이 각자 판단 앞에서
    /// 호출하며, 먼저 호출한 하나가 전체를 계산하고 나머지는 결과를 읽기만 한다.
    /// frameId와 deltaTime을 호출자가 넘겨주는 것은 이 클래스가 Unity 시간에 의존하지 않게 하려는 것이다.
    /// </summary>
    public void EnsureTicked(int frameId, float deltaTime, in TargetSnapshot target)
    {
        if (lastTickedFrame == frameId) return;
        lastTickedFrame = frameId;

        PruneDeadMembers();
        RecomputeSlots(in target);
        UpdateTokens(deltaTime);
        AssignRoles();
    }

    /// <summary>배정된 자리를 읽는다. 아직 계산 전이거나 등록되지 않았으면 권한 없는 기본값을 준다.</summary>
    public EngagementOrder GetOrder(IEncounterMember member)
    {
        if (member != null && ordersById.TryGetValue(member.Id, out EngagementOrder order))
            return order;

        return EngagementOrder.None;
    }

    /// <summary>
    /// 파괴된 적을 걷어낸다. 보통은 OnDisable의 Unregister로 빠지지만, 도메인 리로드를 끈
    /// 설정에서는 static 상태가 플레이 세션을 넘어 살아남아 유령이 남을 수 있다.
    /// 유령이 하나만 있어도 순번 계산이 통째로 어긋난다.
    /// </summary>
    void PruneDeadMembers()
    {
        deadMembers.Clear();

        for (int i = 0; i < members.Count; i++)
        {
            IEncounterMember member = members[i];

            // MonoBehaviour가 파괴되면 "가짜 null"이 되므로 UnityEngine.Object로 보고 명시적으로 비교한다.
            if (member == null || (member is Object unityObject && unityObject == null))
                deadMembers.Add(member);
        }

        for (int i = 0; i < deadMembers.Count; i++)
        {
            IEncounterMember dead = deadMembers[i];
            members.Remove(dead);

            if (dead != null)
            {
                ordersById.Remove(dead.Id);
                ReleaseToken(dead.Id);
            }
        }
    }

    /// <summary>
    /// 각 적이 대상의 어느 쪽에 있는지(Side)와 그 쪽에서 몇 번째로 가까운지(Rank)를 계산한다.
    ///
    /// 순위는 고정 배정이 아니라 매번 거리로 다시 매긴다. 서로 위치가 바뀌면 순번도 따라 바뀌고,
    /// 앞의 적이 죽으면 뒤의 적이 바로 앞자리로 당겨진다.
    /// 거리가 같을 때는 Id로 동점을 깨서 매 프레임 순위가 흔들리지 않게 한다.
    /// </summary>
    void RecomputeSlots(in TargetSnapshot target)
    {
        ordersById.Clear();

        for (int i = 0; i < members.Count; i++)
        {
            IEncounterMember me = members[i];
            float mySide = me.Position.x >= target.Position.x ? 1f : -1f;
            float myDistance = Mathf.Abs(me.Position.x - target.Position.x);

            int rank = 0;
            for (int j = 0; j < members.Count; j++)
            {
                if (j == i) continue;

                IEncounterMember other = members[j];
                float otherSide = other.Position.x >= target.Position.x ? 1f : -1f;
                if (!Mathf.Approximately(otherSide, mySide)) continue;

                float otherDistance = Mathf.Abs(other.Position.x - target.Position.x);
                bool otherIsCloser = otherDistance < myDistance
                    || (otherDistance == myDistance && other.Id < me.Id);

                if (otherIsCloser)
                    rank++;
            }

            // 대상이 보고 있는 쪽이면 예고를 그대로, 등 뒤면 배수만큼 길게.
            bool inFront = Mathf.Approximately(mySide, target.FacingX);

            ordersById[me.Id] = new EngagementOrder
            {
                Side = mySide,
                Rank = rank,
                Role = EnemyRole.Waiter,
                HasAttackToken = false,
                TelegraphScale = inFront ? 1f : Mathf.Max(1f, Settings.backsideTelegraphScale),
            };
        }
    }

    /// <summary>
    /// 토큰을 회수하고 부여한다. 이 메서드가 화면 전체의 리듬을 만든다 —
    /// 언제나 예고 중인 적이 maxConcurrentAttackers 이하이고, 그 사이에 tokenGrantInterval만큼
    /// 빈 구간이 생긴다.
    /// </summary>
    void UpdateTokens(float deltaTime)
    {
        grantCooldown -= deltaTime;

        for (int i = holders.Count - 1; i >= 0; i--)
        {
            TokenHolder holder = holders[i];
            IEncounterMember member = FindMember(holder.Id);

            // 사라졌거나 얻어맞았으면 즉시 회수. 맞은 적이 토큰을 붙들면 압박이 통째로 끊긴다.
            if (member == null || !member.IsAvailable)
            {
                holders.RemoveAt(i);
                continue;
            }

            holder.Held += deltaTime;

            if (member.IsCommitted)
            {
                holder.HasCommitted = true;
            }
            else if (holder.HasCommitted)
            {
                // 예고와 공격을 끝내고 나왔다. 여기서 다음 적에게 순서가 넘어간다.
                holders.RemoveAt(i);
                continue;
            }
            else if (holder.Held >= Settings.tokenClaimTimeout)
            {
                // 받아놓고 쓰지 않는다(사거리에 못 들어가는 등). 자리만 막고 있으므로 회수한다.
                holders.RemoveAt(i);
                continue;
            }

            holders[i] = holder;
        }

        if (grantCooldown > 0f) return;
        if (holders.Count >= Settings.maxConcurrentAttackers) return;

        IEncounterMember candidate = PickCandidate();
        if (candidate == null) return;

        holders.Add(new TokenHolder { Id = candidate.Id, Held = 0f, HasCommitted = false });
        lastGrantedId = candidate.Id;
        grantCooldown = Settings.tokenGrantInterval;
    }

    /// <summary>
    /// 토큰을 줄 적을 고른다. 최전열(Rank 0)이고, 몸이 성하고, 이미 들고 있지 않아야 한다.
    /// 앞뒤 동시 금지가 켜져 있으면 기존 보유자의 반대쪽에는 주지 않는다 —
    /// 플레이어는 한쪽밖에 못 보므로 등 뒤 공격은 읽을 수가 없다.
    /// </summary>
    IEncounterMember PickCandidate()
    {
        IEncounterMember fallback = null;

        for (int i = 0; i < members.Count; i++)
        {
            IEncounterMember member = members[i];
            if (!member.IsAvailable) continue;
            if (HoldsToken(member.Id)) continue;

            if (!ordersById.TryGetValue(member.Id, out EngagementOrder order)) continue;
            if (order.Rank != 0) continue;

            if (Settings.forbidOppositeSideSimultaneous && HasHolderOnOppositeSide(order.Side)) continue;

            // 방금 친 적이 곧바로 또 치지 않게 다른 적을 우선한다. 후보가 그 적뿐이면 결국 그가 받는다.
            if (member.Id != lastGrantedId)
                return member;

            if (fallback == null)
                fallback = member;
        }

        return fallback;
    }

    bool HasHolderOnOppositeSide(float side)
    {
        for (int i = 0; i < holders.Count; i++)
        {
            if (!ordersById.TryGetValue(holders[i].Id, out EngagementOrder order)) continue;
            if (!Mathf.Approximately(order.Side, side))
                return true;
        }
        return false;
    }

    /// <summary>토큰 보유 여부와 순번으로 역할을 정한다. 역할은 결과이지 원인이 아니다.</summary>
    void AssignRoles()
    {
        for (int i = 0; i < members.Count; i++)
        {
            int id = members[i].Id;
            if (!ordersById.TryGetValue(id, out EngagementOrder order)) continue;

            if (HoldsToken(id))
            {
                order.HasAttackToken = true;
                order.Role = EnemyRole.Attacker;
            }
            else
            {
                order.Role = order.Rank == 0 ? EnemyRole.Pressure : EnemyRole.Waiter;
            }

            ordersById[id] = order;
        }
    }

    bool HoldsToken(int id)
    {
        for (int i = 0; i < holders.Count; i++)
        {
            if (holders[i].Id == id)
                return true;
        }
        return false;
    }

    void ReleaseToken(int id)
    {
        for (int i = holders.Count - 1; i >= 0; i--)
        {
            if (holders[i].Id == id)
                holders.RemoveAt(i);
        }
    }

    IEncounterMember FindMember(int id)
    {
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].Id == id)
                return members[i];
        }
        return null;
    }
}

/// <summary>
/// 조율자의 손잡이. 이 셋이 사실상 난이도 전부다.
///
/// 쉬움  : 동시 1 / 간격 1.2초 / 예고 0.6초 (예고 길이는 BehaviorTuningData에 있다)
/// 보통  : 동시 1 / 간격 0.8초 / 예고 0.45초
/// 어려움: 동시 2 / 간격 0.4초 / 예고 0.3초
///
/// 데미지와 체력은 건드리지 않는다. 데미지를 올리면 어려워지는 게 아니라 짜증만 는다.
/// </summary>
[System.Serializable]
public struct DirectorSettings
{
    [KoreanLabel("동시 공격 허용 수")]
    [Tooltip("동시에 예고→공격까지 갈 수 있는 적의 수. 1이면 플레이어가 읽어야 할 대상이 항상 하나뿐이다.")]
    public int maxConcurrentAttackers;

    [KoreanLabel("토큰 부여 간격(초)")]
    [Tooltip("한 적이 공격을 끝낸 뒤 다음 적이 시작하기까지의 최소 간격. 파도 사이의 숨 쉴 틈이다.")]
    public float tokenGrantInterval;

    [KoreanLabel("앞뒤 동시 금지")]
    [Tooltip("이미 한쪽에서 공격 중이면 반대쪽에는 토큰을 주지 않는다. 플레이어는 한쪽밖에 못 보기 때문이다.")]
    public bool forbidOppositeSideSimultaneous;

    [KoreanLabel("등 뒤 예고 배수")]
    [Tooltip("플레이어가 보고 있지 않은 쪽에서 접근할 때 예고를 몇 배로 길게 줄지. 1 미만은 무시된다.")]
    public float backsideTelegraphScale;

    [KoreanLabel("토큰 회수 유예(초)")]
    [Tooltip("토큰을 받고도 이 시간 안에 예고에 들어가지 않으면 회수한다. 자리만 막는 것을 막는 안전장치라 튜닝용이 아니다.")]
    public float tokenClaimTimeout;

    public static DirectorSettings Default => new DirectorSettings
    {
        maxConcurrentAttackers = 1,
        tokenGrantInterval = 0.8f,
        forbidOppositeSideSimultaneous = true,
        backsideTelegraphScale = 1.5f,
        tokenClaimTimeout = 4f,
    };
}
