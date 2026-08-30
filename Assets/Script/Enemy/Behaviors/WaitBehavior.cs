using System;
using UnityEngine;

/// <summary>
/// 행동 ④ 대기. 제자리에서 플레이어를 보며 시간만 잰다. <b>플레이어의 반격 창이 여기서 생긴다.</b>
///
/// [이 행동이 하는 일은 시간을 재는 것뿐이다] 6단계에서 모든 전이가 이 행동을 거쳐 가게 되지만,
/// 그렇다고 여기에 "가까우면 다음은 접근" 같은 판단이 들어오면 안 된다. 그 순간 대기가 다른 행동들의
/// 이름을 알게 되고 조립이 불가능해진다. 다음이 무엇인지는 끝까지 브레인만 안다.
/// 조기 종료도 마찬가지다 — 이유가 무엇이든 밖에 보고하는 것은 Done 하나뿐이다.
///
/// [피격은 여기서 다루지 않는다] 대기 도중 얻어맞으면 대기가 끝나는 것이 아니라, 브레인이 진행 중이던
/// 행동을 통째로 버리고 경직이 풀린 뒤 조건표부터 다시 읽는다 (6단계 인터럽트 규칙).
/// 그래서 이 클래스는 몸이 멀쩡하다는 전제 위에서 시간만 세면 된다.
/// 4단계에서는 여기에 피격 검사가 있었지만, 브레인이 그 일을 가져가면서 닿지 않는 코드가 되어 걷어냈다.
/// </summary>
[Serializable]
public class WaitBehavior : IEnemyBehavior
{
    [KoreanLabel("대기 시간(초)")]
    [Tooltip("다음 행동으로 넘어가기 전에 제자리에서 보내는 시간. 이 값이 곧 플레이어의 반격 창이라 난이도 조절의 핵심 노브다. " +
        "0으로 두면 행동 사이에 틈이 없어 가장 공격적이 된다.")]
    public float duration = 0.8f;

    [KoreanLabel("사거리 안이면 즉시 종료")]
    [Tooltip("대기 중에 플레이어가 지금 치면 맞는 자리까지 들어오면 남은 시간을 버리고 끝낸다. " +
        "조율자에게 공격 권한을 받지 못한 상태에서는 어차피 칠 수 없으므로 끝내지 않는다. " +
        "끄면 무슨 일이 있어도 대기 시간을 다 채운다 (더 쉬움).")]
    public bool endEarlyWhenInRange = true;

    float timer;

    public void OnEnter(in BehaviorContext ctx)
    {
        timer = 0f;
    }

    public BehaviorResult Tick(in BehaviorContext ctx, ref CharacterIntent intent)
    {
        // 이동 의도를 내지 않는 것 자체가 "제자리에 선다"는 뜻이다. 바라보는 방향은 브레인이 이미 채웠다.

        // 권한도 함께 본다. 권한이 없는데 사거리만 보고 끝내면, 다음 판단이 다시 대기를 고르고
        // 그 대기가 또 즉시 끝나면서 줄 서 있는 적이 매 프레임 헛도는 상태가 된다.
        if (endEarlyWhenInRange && ctx.CanAttack && ctx.IsTargetInHitRange)
            return BehaviorResult.Done;

        timer += ctx.DeltaTime;
        return timer >= duration ? BehaviorResult.Done : BehaviorResult.Running;
    }
}
