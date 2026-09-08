using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 적 AI의 뼈대(브레인 셸). 지침서 1.2의 상태 전환 패턴을 구현한다 —
/// <c>current</c> / <c>desired</c> / <c>previous</c> 행동을 분리하고, 행동을 바꿀 때는 <c>current</c>를
/// 바로 갈아치우지 않고 <b>[reset]</b>(<see cref="RunReset"/>)을 거쳐 <c>desired</c>로 넘어간다.
///
/// 매 프레임 현재 행동(<see cref="EnemyBehavior"/>) 하나를 Tick해 CharacterIntent를 만든다.
/// <c>IIntentSource</c> 구현이라 이 파일이 어떻게 바뀌든 <see cref="Actor"/>는 무변경.
///
/// [붙이는 곳] 적 루트. EnemyTargeting + EnemyOrbitMovement 필요.
/// [진행 중] 행동 선택은 <see cref="WeightedBehaviorSelector"/>. 조건 체크(CanBeSelected)는 공격 사거리만 —
///          나머지 거리·체력·그로기 조건은 7번(조립)에서.
/// </summary>
[RequireComponent(typeof(EnemyTargeting), typeof(EnemyOrbitMovement))]
[RequireComponent(typeof(CharacterStateMachine), typeof(Fighter))]
public class EnemyBrain : MonoBehaviour, IIntentSource
{
    [KoreanLabel("접근")]
    [SerializeField] ApproachBehavior approach = new ApproachBehavior();

    [KoreanLabel("대기")]
    [SerializeField] WaitBehavior wait = new WaitBehavior();

    [KoreanLabel("후퇴")]
    [SerializeField] RetreatBehavior retreat = new RetreatBehavior();

    [KoreanLabel("공격")]
    [SerializeField] AttackBehavior attack = new AttackBehavior();

    [KoreanLabel("행동 셀렉터")]
    [SerializeField] WeightedBehaviorSelector selector = new WeightedBehaviorSelector();

    [KoreanLabel("성향 (선택)")]
    [Tooltip("지정하면 Awake에서 각 행동의 선택 가중치와 dry-fire 가중치를 이 성향 값으로 덮어쓴다. " +
             "비우면 위 행동별 가중치를 그대로 쓴다.")]
    [SerializeField] EnemyArchetype archetype;

    [KoreanLabel("히트스턴 복구 로그")]
    [Tooltip("켜면 적이 히트스턴에서 벗어나 즉시 재결정한 시점을 Console에 남긴다(검증용).")]
    [SerializeField] bool logHitstunRecovery = false;

    public EnemyTargeting Targeting { get; private set; }
    public EnemyOrbitMovement Orbit { get; private set; }

    /// <summary>지금 공격 모션 재생 중인지(Fighter 위임). 공격 행동이 "언제 끝났는지" 판단에 쓴다.</summary>
    public bool IsAttacking => fighter != null && fighter.IsAttacking;

    /// <summary>지금 피격 여파(Stun/Airborne/Landed/GetUp)로 얻어맞는 중인지(StateMachine 위임).</summary>
    public bool IsHitStunned => stateMachine != null && stateMachine.IsHitStunned;

    public EnemyBehavior Current => current;
    public EnemyBehavior Previous => previous;

    /// <summary>스쿼드가 있으면 그 인스턴스. 없으면 null(솔로 동작).</summary>
    public SquadController Squad { get; private set; }

    /// <summary>블랙보드: 스쿼드가 배정한 포위 슬롯 각도. <see cref="HasSlotAssignment"/>가 true일 때만 유효.</summary>
    public float SlotAngle { get; set; }

    /// <summary>블랙보드: 스쿼드가 슬롯을 배정했는지. false면 행동이 자기 기본 각도를 쓴다.</summary>
    public bool HasSlotAssignment { get; set; }

    Fighter fighter;
    CharacterStateMachine stateMachine;
    EnemyBehavior[] behaviors;
    EnemyBehavior current;
    EnemyBehavior desired;
    EnemyBehavior previous;
    bool resetting;
    bool wasHitStunned;

    void Awake()
    {
        Targeting = GetComponent<EnemyTargeting>();
        Orbit = GetComponent<EnemyOrbitMovement>();
        fighter = GetComponent<Fighter>();
        stateMachine = GetComponent<CharacterStateMachine>();

        behaviors = new EnemyBehavior[] { approach, wait, retreat, attack };
        foreach (EnemyBehavior b in behaviors)
            b.Bind(this);

        if (archetype != null)
            ApplyArchetype(archetype);
    }

    /// <summary>성향 값으로 각 행동의 선택 가중치와 셀렉터 dry-fire 가중치를 덮어쓴다.</summary>
    void ApplyArchetype(EnemyArchetype a)
    {
        approach.selectionWeight = a.approachWeight;
        wait.selectionWeight = a.waitWeight;
        retreat.selectionWeight = a.retreatWeight;
        attack.selectionWeight = a.attackWeight;
        selector.dryFireWeight = a.dryFireWeight;
    }

    void Start()
    {
        Squad = SquadController.Instance;
        if (Squad != null)
            Squad.Register(this);

        desired = Decide();
        resetting = true; // 첫 GetIntent에서 [reset] → current = desired
    }

    void OnDestroy()
    {
        if (Squad != null)
            Squad.Unregister(this);
    }

    public CharacterIntent GetIntent(float deltaTime)
    {
        // 얻어맞는 중 — 현재 행동의 위상 타이머를 진행시키지 않는다(Tick 안 함). 벗어나면 즉시 재결정하도록 표시.
        if (stateMachine.IsHitStunned)
        {
            wasHitStunned = true;
            return CharacterIntent.None;
        }

        // 히트스턴에서 막 벗어났다 → 텀 없이 [reset] 강제. 맞기 전 진행 중이던 행동은 버린다.
        if (wasHitStunned)
        {
            wasHitStunned = false;
            if (current != null)
                current.Exit();
            desired = Decide();
            resetting = true;
            if (logHitstunRecovery)
                Debug.Log($"{name}: 히트스턴 종료 → 즉시 재결정 (frame {Time.frameCount})");
        }

        if (resetting)
            RunReset();

        if (current == null)
            return CharacterIntent.None;

        CharacterIntent intent = current.Tick(deltaTime);

        if (current.IsDone)
        {
            current.Exit();
            desired = Decide();
            resetting = true;
        }

        return intent;
    }

    /// <summary>다음 행동 선택. 확률 가중 셀렉터 → dry-fire(null)면 기본 행동(계속 접근).</summary>
    EnemyBehavior Decide()
    {
        EnemyBehavior picked = selector.Select(behaviors);
        return picked != null ? picked : approach;
    }

    [ContextMenu("셀렉터 분포 테스트 (1000회)")]
    void LogSelectorDistribution()
    {
        if (behaviors == null)
        {
            Debug.LogWarning($"{name}: Play 모드에서 실행하세요 (behaviors 미초기화).");
            return;
        }

        const int n = 1000;
        Dictionary<string, int> counts = new Dictionary<string, int>();
        for (int i = 0; i < n; i++)
        {
            EnemyBehavior b = selector.Select(behaviors);
            string key = b != null ? b.GetType().Name : "(dry-fire)";
            counts.TryGetValue(key, out int c);
            counts[key] = c + 1;
        }

        string head = archetype != null ? $"{name} [{archetype.name}]" : name;
        StringBuilder sb = new StringBuilder($"{head} 셀렉터 분포 ({n}회):\n");
        foreach (KeyValuePair<string, int> kv in counts)
            sb.AppendLine($"  {kv.Key}: {kv.Value} ({kv.Value * 100f / n:F1}%)");
        Debug.Log(sb.ToString());
    }

    /// <summary>[reset]: previous 기록 + current = desired + Enter. current를 직접 재할당하는 유일한 지점.</summary>
    void RunReset()
    {
        previous = current;
        current = desired;
        resetting = false;
        current.Enter();
    }
}
