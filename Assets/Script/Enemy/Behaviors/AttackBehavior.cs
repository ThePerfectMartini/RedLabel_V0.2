using System;
using UnityEngine;

/// <summary>
/// 행동 ② 공격. 사거리 안이면 공격을 요청하고, 그 공격이 끝날 때까지 제자리에서 기다린 뒤 Done을 보고한다.
/// 사거리 밖이면 Abort — 브레인이 다시 접근부터 시키게 된다.
///
/// [왜 요청을 한 프레임만 하지 않는가] 쿨타임이나 착지 경직처럼 몸이 아직 공격을 받아주지 못하는
/// 상황이 몇 프레임 이어질 수 있다. 한 번 요청하고 시작됐다고 가정하면 그 프레임에 조용히 씹히고,
/// 공격이 한 번도 안 나갔는데 "공격했다"고 넘어가버린다. 그래서 실제로 시작된 것을 확인할 때까지 계속 요청한다.
/// </summary>
[Serializable]
public class AttackBehavior : IEnemyBehavior
{
    // 계속 요청했는데도 이만큼 지나도록 공격이 시작되지 않으면, 기다려서 풀릴 상황이 아니라고 보고
    // 접근부터 다시 한다. 안 그러면 이 행동이 영원히 Running을 보고하면서 적이 조용히 굳는다.
    // 튜닝 값이 아니라 조용한 멈춤을 막는 값이라 인스펙터에 내지 않는다.
    const float StartTimeout = 1f;

    /// <summary>공격이 실제로 재생되는 것을 한 번이라도 봤는지. 이게 참이어야 "공격했다"고 말할 수 있다.</summary>
    bool attackSeen;

    float startTimer;

    // 경고는 이 적당 한 번만. Abort하면 다시 접근 -> 공격으로 돌아오므로, 안 막으면 매번 다시 찍힌다.
    bool warned;

    public void OnEnter(in BehaviorContext ctx)
    {
        attackSeen = false;
        startTimer = 0f;
    }

    public BehaviorResult Tick(in BehaviorContext ctx, ref CharacterIntent intent)
    {
        EnemyController owner = ctx.Owner;

        // 공격이 재생되는 동안은 할 일이 없다. 이동 의도를 내지 않는 것 자체가 제자리에서 휘두른다는 뜻이다.
        // (LockedAttackData면 어차피 몸이 이동을 막지만, 이동을 허용하는 공격이어도 여기선 움직이지 않는다.)
        if (owner.IsAttacking)
        {
            attackSeen = true;
            return BehaviorResult.Running;
        }

        // 재생되던 공격이 끝났다. 맞았든 빗나갔든 이 행동의 목적은 끝난 것이다.
        // 맞았는지 여부로 판단하지 않는 이유: 판정 결과는 몸도 브레인도 갖고 있지 않고,
        // 빗나갔을 때 계속 휘두르게 만들면 후퇴/대기가 영영 오지 않는다.
        if (attackSeen)
            return BehaviorResult.Done;

        if (!ctx.IsTargetInHitRange)
            return BehaviorResult.Abort;

        intent.WantsAttack = true;

        startTimer += ctx.DeltaTime;
        if (startTimer < StartTimeout)
            return BehaviorResult.Running;

        if (!warned)
        {
            Debug.LogWarning($"{owner.name}: 공격을 계속 요청했지만 {StartTimeout}초 동안 시작되지 않았습니다. " +
                             $"'공격 1 (콤보 시작)'이 연결되어 있는지 확인하세요.", owner);
            warned = true;
        }

        return BehaviorResult.Abort;
    }
}
