using UnityEngine;

/// <summary>
/// 공격. Active 진입 첫 프레임에 공격 하나를 요청하고(예비 동작은 Startup 위상이 담당), 그 공격 모션이
/// 끝나면 Active 종료 → 회복 프레임. 공격 데이터가 없으면 아무 공격도 안 하고 시간만 보낸다.
///
/// 스쿼드가 있으면 OnEnter에서 공격 슬롯을 받아야 실제로 공격한다 — 못 받으면 즉시 <see cref="Abort"/>해
/// 다른 적에게 순번을 양보한다(교대 공격).
/// </summary>
[System.Serializable]
public class AttackBehavior : EnemyBehavior
{
    [KoreanLabel("공격 데이터")]
    [Tooltip("이 행동이 낼 공격. 비우면 아무 공격도 안 한다.")]
    public AttackData attackData;

    [KoreanLabel("공격 선택 사거리")]
    [Min(0f)]
    [Tooltip("대상이 이 거리 안에 있을 때만 셀렉터가 이 행동을 후보로 삼는다.")]
    public float attackSelectRange = 2.2f;

    bool issued;
    bool attackStarted;
    bool hasSlot;

    public override bool CanBeSelected()
    {
        if (!Brain.Targeting.HasTarget || Brain.Targeting.Distance > attackSelectRange)
            return false;
        return Brain.Squad == null || Brain.Squad.HasFreeAttackSlot();
    }

    protected override void OnEnter()
    {
        issued = false;
        attackStarted = false;
        hasSlot = Brain.Squad == null || Brain.Squad.TryClaimAttackSlot(Brain);
        if (!hasSlot)
            Abort(); // 슬롯 못 얻음 — 순번 양보
    }

    protected override CharacterIntent OnTick(float deltaTime)
    {
        CharacterIntent intent = NeutralIntent(); // 제자리에서 대상 주시

        if (attackData != null && !issued)
        {
            intent.AttackToStart = attackData;
            issued = true;
        }

        if (Brain.IsAttacking)
            attackStarted = true;

        return intent;
    }

    protected override void OnExit()
    {
        if (hasSlot && Brain.Squad != null)
            Brain.Squad.ReleaseAttackSlot(Brain);
        hasSlot = false;
    }

    // 공격을 냈고, 그게 실제로 시작됐다가 끝났으면 Active 종료.
    protected override bool WantsToExit(float activeElapsed)
        => issued && attackStarted && !Brain.IsAttacking;
}
