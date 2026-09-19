/// <summary>
/// 설계서 4장 ⑦. 얻어맞은 여파가 끝날 때까지 <b>기다리기만</b> 한다.
///
/// 경직 자체는 AI가 만드는 것이 아니다 — 데미지 · 넉백 · 애니메이션 전환은 이미 <see cref="Health"/>가
/// <c>CharacterStateMachine.Interrupt()</c>와 <c>Locomotion.ApplyKnockback()</c>으로 다 해놨다.
/// 이 노드가 담당하는 것은 설계서 문장 그대로 <b>"행동을 끊고 다시 고르게 한다"</b>뿐이고,
/// 실제로 끊는 일은 트리의 피격 분기가 <see cref="BTNode.Abort"/>로 한다.
///
/// 그래서 하는 일이 "몸이 다시 말을 들을 때까지 아무 의도도 내지 않기"밖에 없다.
/// 이 동안 MoveInput을 건드리지 않는 것이 중요하다 — 경직·에어본 중에는 Locomotion이 이동 입력을
/// 무시하지만, 슬라이드가 끝나는 프레임에 이동 의도가 남아 있으면 넉백이 끝나자마자 튀어 나간다.
///
/// 종료하면 설계서상 항상 이탈 점프로 이어진다(끊긴 행동을 이어서 하지 않는다).
///
/// 별도의 시간 제한을 두지 않는다. 경직에서 빠져나오는 길이 전부 막히지 않기 때문이다 —
/// 그라운드 슬라이드는 감속으로, 에어본은 중력으로 끝나고, 다운·기상은 CharacterStateMachine의
/// 워치독이 이미 지키고 있다.
/// </summary>
public class HitStunNode : BTNode
{
    /// <summary>
    /// 이번 경직이 <b>넘어졌다 일어나는</b> 피격이었는지. 공중에 뜨는 피격은 반드시
    /// Airborne → (착지) → Landed → GetUp 순서를 거치므로, 그 중 하나라도 지났으면 넘어진 것이다.
    ///
    /// 가벼운 지상 경직(Stun만)과 구분하려고 둔다 — 넘어졌을 때만 이탈이 따라붙는다.
    /// 노드가 끝난 뒤에도 값이 남아 있어야 상위 시퀀스가 읽을 수 있으므로 OnExit에서 지우지 않는다.
    /// </summary>
    public bool WasKnockedDown { get; private set; }

    protected override void OnEnter(MeleeEnemyContext context)
    {
        WasKnockedDown = false;

        // 이 신호를 보고 들어왔으므로 여기서 소비한다. 경직 도중에 또 맞으면 다시 켜지고,
        // 그건 이번 피격 반응을 끝낸 뒤 루트가 다시 이 분기로 들여보낸다.
        context.ConsumeHit();
    }

    protected override BTStatus OnTick(MeleeEnemyContext context, float deltaTime)
    {
        // 경직 중에 거쳐 간 상태를 계속 지켜본다. 첫 프레임만 보면 안 된다 —
        // 경직 도중 다시 맞아 그때 떠오르는 경우도 "넘어진 것"으로 쳐야 한다.
        if (context.IsKnockedDownState)
            WasKnockedDown = true;

        return context.IsStaggered ? BTStatus.Running : BTStatus.Success;
    }
}
