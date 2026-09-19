using UnityEngine;

/// <summary>
/// 트리의 모든 노드가 공유하는 작업판. 적 자신의 몸 컴포넌트, 목표(플레이어), 수치 에셋을 한 번 찾아두고,
/// 노드들이 이번 프레임의 <see cref="CharacterIntent"/>를 여기에 써 넣는다. 소유자(<c>IIntentSource</c> 구현체)가
/// 프레임 시작에 <see cref="BeginFrame"/>으로 지우고, 트리를 한 번 굴린 뒤 <see cref="Intent"/>를 Actor에 돌려준다.
///
/// 노드가 Locomotion을 직접 만지지 않고 이 Intent만 채우는 이유: 플레이어와 완전히 같은 경로로 몸이 움직여야
/// 공격 잠금·피격 경직·상태 전이가 전부 공짜로 따라오기 때문이다(<see cref="IIntentSource"/> 주석 참고).
///
/// 벨트스크롤이라 판단은 전부 xz 평면에서만 한다 — y는 점프/넉백의 결과일 뿐 목표 계산에 들어가지 않는다.
/// </summary>
public class MeleeEnemyContext
{
    public readonly Transform Self;
    public readonly Locomotion Locomotion;
    public readonly CharacterStateMachine StateMachine;
    public readonly Fighter Fighter;
    public readonly MeleeEnemyAIData Data;

    /// <summary>쫓을 대상(플레이어). 런타임에 바뀔 수 있어 readonly가 아니다.</summary>
    public Transform Target;

    /// <summary>이번 프레임에 노드들이 채우는 의도. BeginFrame에서 초기화된다.</summary>
    public CharacterIntent Intent;

    // 공격권 관리자는 목표(플레이어)가 들고 있다. 목표가 바뀔 수 있으므로 어느 목표에서 찾은 것인지 같이 기억한다.
    AttackSlots cachedSlots;
    Transform cachedSlotsSource;

    public MeleeEnemyContext(GameObject owner, Transform target, MeleeEnemyAIData data)
    {
        Self = owner.transform;
        Locomotion = owner.GetComponent<Locomotion>();
        StateMachine = owner.GetComponent<CharacterStateMachine>();
        Fighter = owner.GetComponent<Fighter>();
        Target = target;
        Data = data;
    }

    public bool HasTarget => Target != null;

    /// <summary>적 자신의 위치를 xz 평면으로 눌러 놓은 것(y = 0).</summary>
    public Vector3 FlatSelfPosition => Flatten(Self.position);

    /// <summary>목표의 위치를 xz 평면으로 눌러 놓은 것(y = 0). 목표가 없으면 자신의 위치.</summary>
    public Vector3 FlatTargetPosition => HasTarget ? Flatten(Target.position) : FlatSelfPosition;

    public Vector3 ToTarget => FlatTargetPosition - FlatSelfPosition;

    public float DistanceToTarget => ToTarget.magnitude;

    /// <summary>
    /// 플레이어를 기준으로 내가 지금 어느 쪽에 있는지(+1 오른쪽 / -1 왼쪽). 설계서 7장의
    /// "방향은 그 순간 가까운 쪽"이 이 값이다. x가 정확히 겹치면 지금 바라보는 방향의 반대쪽으로 친다
    /// (겹친 상태에서 부호가 매 프레임 튀는 것을 막는다).
    /// </summary>
    public float NearSideSign
    {
        get
        {
            float dx = Self.position.x - (HasTarget ? Target.position.x : Self.position.x);
            if (Mathf.Abs(dx) > 0.01f)
                return Mathf.Sign(dx);

            return Locomotion != null && Locomotion.FacingRight ? -1f : 1f;
        }
    }

    /// <summary>
    /// 아직 처리하지 않은 피격이 있는지. <see cref="Health.OnHitTaken"/>을 구독한 쪽이 켜고,
    /// 피격 분기가 실제로 들어가면서 <see cref="ConsumeHit"/>로 끈다.
    ///
    /// 상태(<see cref="IsStaggered"/>)만으로는 부족하다 — 넉백이 0인 공격에 맞으면 경직 상태가
    /// 아예 안 생겨서 "맞았다"는 사실 자체를 놓친다. 그래서 신호는 따로 받아 둔다.
    /// </summary>
    public bool HitPending { get; private set; }

    /// <summary>피격을 알린다. Health의 OnHitTaken 구독자가 호출.</summary>
    public void NotifyHit() => HitPending = true;

    /// <summary>피격 신호를 소비한다. 피격 분기가 시작될 때 호출.</summary>
    public void ConsumeHit() => HitPending = false;

    /// <summary>
    /// 지금 얻어맞은 여파(경직 · 에어본 · 다운 · 기상)로 아무것도 못 하는 상태인지.
    /// 노드가 "내 동작이 끝난 것"과 "얻어맞아서 끊긴 것"을 구분하는 데 쓴다.
    /// </summary>
    public bool IsStaggered =>
        StateMachine != null && (
            StateMachine.CurrentState == CharacterState.Stun ||
            IsKnockedDownState);

    /// <summary>
    /// 지금 <b>넘어지는 계열</b>의 상태인지 — 공중에 떴거나(Airborne), 쓰러졌거나(Landed), 일어나는 중(GetUp).
    /// 가벼운 지상 경직(Stun)과 구분한다. 넉백의 수직 속도가 임계값을 넘은 피격만 이 계열로 들어온다.
    /// </summary>
    public bool IsKnockedDownState =>
        StateMachine != null && (
            StateMachine.CurrentState == CharacterState.Airborne ||
            StateMachine.CurrentState == CharacterState.Landed ||
            StateMachine.CurrentState == CharacterState.GetUp);

    // ===== 공격권 (설계서 7장) =====

    /// <summary>
    /// 플레이어가 들고 있는 공격권 관리자. 없으면 null이고, 그때는 "슬롯이 항상 비어 있다"로 본다.
    /// 목표가 바뀌면 다시 찾는다.
    /// </summary>
    public AttackSlots Slots
    {
        get
        {
            if (!HasTarget) return null;

            if (cachedSlotsSource != Target)
            {
                cachedSlotsSource = Target;
                cachedSlots = Target.GetComponentInParent<AttackSlots>();
            }

            return cachedSlots;
        }
    }

    /// <summary>지금 내가 가까운 플레이어의 옆구리.</summary>
    public AttackSide NearSide => AttackSlots.SideFromSign(NearSideSign);

    /// <summary>지금 공격권을 들고 있는지.</summary>
    public bool HoldsAttackSlot => Slots == null || Slots.Holds(Data.slotReach, Self.gameObject);

    /// <summary>
    /// 공격 접근이 노려야 하는 옆구리의 부호(+1 오른쪽 / -1 왼쪽). 공격권을 들고 있으면 <b>그 슬롯의 쪽</b>이고,
    /// 아직 없으면 지금 가까운 쪽이다. 잡은 쪽과 가까운 쪽이 어긋나는 것은 잠깐뿐이다 —
    /// <see cref="RefreshAttackSlotSide"/>가 가까워진 쪽이 비는 대로 옮겨 앉힌다.
    /// </summary>
    public float AttackSideSign
    {
        get
        {
            if (Slots != null && Slots.TryGetSide(Data.slotReach, Self.gameObject, out AttackSide side))
                return AttackSlots.SignOf(side);

            return NearSideSign;
        }
    }

    /// <summary>
    /// 지금 공격하러 가도 되는지. <b>가까운 쪽</b> 자리가 비었는지만 본다 —
    /// 반대쪽이 비어 있어도 접근 단계에서 그걸 잡지는 않는다. 그 자리로 가려면 플레이어를 뚫고
    /// 지나가야 하기 때문이다. 대신 대기 행동이 반원을 그려 그쪽으로 데려다 준다
    /// (<see cref="QueueSideSign"/>). 거기 도착하면 그 자리가 곧 "가까운 쪽"이 된다.
    /// </summary>
    public bool CanTakeAttackSlot =>
        Slots == null
        || Slots.Holds(Data.slotReach, Self.gameObject)
        || Slots.IsSideFree(Data.slotReach, NearSide, Self.gameObject);

    /// <summary>
    /// 대기하는 동안 줄 서러 갈 쪽. 비어 있는 자리가 있으면 그쪽이고(먼 쪽이면 반원으로 돌아간다),
    /// 둘 다 차 있으면 지금 가까운 쪽 바깥에서 기다린다.
    /// </summary>
    public float QueueSideSign
    {
        get
        {
            if (Slots == null) return NearSideSign;

            AttackSide near = NearSide;
            if (Slots.IsSideFree(Data.slotReach, near, Self.gameObject))
                return AttackSlots.SignOf(near);

            AttackSide far = AttackSlots.Opposite(near);
            if (Slots.IsSideFree(Data.slotReach, far, Self.gameObject))
                return AttackSlots.SignOf(far);

            return NearSideSign;
        }
    }

    /// <summary>가까운 쪽 공격권을 받는다. 이미 (어느 쪽이든) 들고 있으면 그대로 true. 관리자가 없으면 항상 true.</summary>
    public bool TryTakeAttackSlot()
    {
        if (Slots == null) return true;

        return Slots.TryAcquire(Data.slotReach, NearSide, Self.gameObject)
            || Slots.Holds(Data.slotReach, Self.gameObject);
    }

    /// <summary>
    /// 접근 도중 플레이어가 나를 지나쳐 좌우가 뒤집혔을 때, 가까워진 쪽이 비어 있으면 그쪽으로 옮겨 앉는다.
    /// 남이 앉아 있으면 잡은 쪽을 그대로 들고 간다 — 설계서 7장의 "공격권은 유지, 목표 슬롯만 갱신"이다.
    /// 공격권이 없으면 아무 일도 하지 않는다(여기서 새로 잡지는 않는다).
    /// </summary>
    public void RefreshAttackSlotSide()
    {
        if (Slots == null) return;
        if (!Slots.Holds(Data.slotReach, Self.gameObject)) return;

        Slots.TryAcquire(Data.slotReach, NearSide, Self.gameObject);
    }

    /// <summary>공격권 반납. 안 들고 있었으면 아무 일도 없다.</summary>
    public void ReleaseAttackSlot()
    {
        if (Slots != null)
            Slots.Release(Data.slotReach, Self.gameObject);
    }

    /// <summary>
    /// 프레임 시작. 의도를 지우고 "플레이어를 바라본다"만 기본값으로 깔아 둔다 —
    /// 이 적은 접근하든 물러나든 항상 플레이어를 보고 있으므로 노드마다 되풀이하지 않는다.
    /// </summary>
    public void BeginFrame()
    {
        Intent = CharacterIntent.None;

        if (HasTarget)
            Intent.FacingDirection = new Vector3(Target.position.x - Self.position.x, 0f, 0f);
    }

    /// <summary>
    /// 이번 프레임에 <paramref name="worldDirection"/> 쪽으로 <paramref name="speedPerSecond"/>의 속력으로 가겠다는 의도.
    /// Locomotion은 방향만 보고 자기 기본 속도로 움직이므로, 원하는 절대 속도를 기본 속도에 대한 배율로 환산해 같이 넘긴다.
    /// </summary>
    public void RequestMove(Vector3 worldDirection, float speedPerSecond)
    {
        Vector3 flat = Flatten(worldDirection);
        if (flat.sqrMagnitude < 0.000001f)
        {
            Intent.MoveInput = Vector2.zero;
            return;
        }

        flat.Normalize();
        Intent.MoveInput = new Vector2(flat.x, flat.z); // Locomotion은 y 성분을 월드 z로 읽는다

        float baseSpeed = Locomotion != null ? Locomotion.MoveSpeed : 0f;
        Intent.MoveSpeedScale = speedPerSecond > 0f && baseSpeed > 0f ? speedPerSecond / baseSpeed : 1f;
    }

    /// <summary>
    /// 플레이어가 지금 내 트리거 범위(설계서 6장) 안에 있는지. 범위는 내가 바라보는 방향으로
    /// 트리거 중심 거리만큼 떨어진 곳에 놓인, 가로 x · 깊이 z 크기의 상자다.
    /// 히트 판정(Fighter의 OverlapSphere)과는 별개이며 AI는 이쪽만 본다.
    /// </summary>
    public bool IsTargetInTriggerRange()
    {
        if (!HasTarget || Data == null) return false;

        Vector3 center = TriggerRangeCenter;
        Vector3 offset = FlatTargetPosition - center;

        return Mathf.Abs(offset.x) <= Data.triggerRangeSize.x * 0.5f
            && Mathf.Abs(offset.z) <= Data.triggerRangeSize.y * 0.5f;
    }

    /// <summary>트리거 범위의 중심 좌표(xz 평면). 디버그 기즈모도 이 값을 쓴다.</summary>
    public Vector3 TriggerRangeCenter
    {
        get
        {
            Vector3 facing = Locomotion != null ? Locomotion.FacingDir : Vector3.right;
            float distance = Data != null ? Data.triggerCenterDistance : 0f;
            return FlatSelfPosition + facing * distance;
        }
    }

    static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
