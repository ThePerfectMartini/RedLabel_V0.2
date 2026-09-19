using UnityEngine;

/// <summary>
/// 설계서 4장 ④. 제자리에 서서 <b>범위 안 랜덤 시간</b>만큼 숨을 고른다.
///
/// 고정값이 아니라 범위인 것이 이 적의 성격을 만든다 — 아주 짧게 쉬고 바로 다시 덤비기도 하고
/// 꽤 오래 기다리기도 해서, 플레이어가 "다음 공격이 언제 올지"를 외울 수 없게 된다.
/// 공격적인 느낌을 원하면 최소·최대를 둘 다 낮추고, 신중한 변형 적은 둘 다 올리면 된다.
///
/// 제자리 정지는 따로 처리하지 않는다. MoveInput을 건드리지 않으면 그대로 0이고,
/// 플레이어를 바라보는 것은 <see cref="MeleeEnemyContext.BeginFrame"/>이 이미 깔아 둔다.
/// </summary>
public enum WaitPurpose
{
    /// <summary>공격 직후의 숨 고르기. 꽤 길게 나올 수 있다.</summary>
    PostAttack,
    /// <summary>공격권을 기다리는 동안의 짧은 멈춤. 곧바로 다시 조금 움직인다.</summary>
    Standby,
}

public class PostAttackWaitNode : BTNode
{
    readonly WaitPurpose purpose;

    float duration;
    float elapsed;

    public PostAttackWaitNode(WaitPurpose purpose = WaitPurpose.PostAttack)
    {
        this.purpose = purpose;
    }

    /// <summary>이번에 뽑힌 대기 시간(초). 디버그 표시용.</summary>
    public float Duration => duration;

    /// <summary>이 대기가 무엇을 위한 것인지. 디버그 표시용.</summary>
    public WaitPurpose Purpose => purpose;

    protected override void OnEnter(MeleeEnemyContext context)
    {
        elapsed = 0f;

        float min = purpose == WaitPurpose.Standby ? context.Data.standbyWaitMin : context.Data.waitDurationMin;
        float max = purpose == WaitPurpose.Standby ? context.Data.standbyWaitMax : context.Data.waitDurationMax;
        if (min > max) (min, max) = (max, min); // 인스펙터에서 뒤집어 넣어도 동작하게

        duration = Random.Range(min, max);
    }

    protected override BTStatus OnTick(MeleeEnemyContext context, float deltaTime)
    {
        // 얻어맞았으면 숨 고르기는 의미가 없다. 피격 분기가 트리 바깥에서 끊어주지만,
        // 단독으로 돌릴 때도 같은 판단을 하도록 여기서도 본다.
        if (context.IsStaggered) return BTStatus.Failure;

        elapsed += deltaTime;
        return elapsed >= duration ? BTStatus.Success : BTStatus.Running;
    }
}
