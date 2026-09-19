using UnityEngine;

/// <summary>
/// 설계서 4장 ②. 제자리에 서서 근접 공격을 <b>한 번</b> 내고 끝날 때까지 기다린다.
///
/// 이 노드는 실행만 한다 — "언제 공격할지"는 접근 노드의 종료 조건과 행동 선택이 이미 정했고,
/// 여기서는 정지 + 공격 요청 + 끝나면 성공 반환만 담당한다. 판정 · 넉백 · 애니메이션은 전부
/// 기존 <see cref="Fighter"/> / <see cref="AttackData"/>가 하던 그대로다(AI가 새로 하는 일이 없다).
///
/// <b>커밋형이라 중간에 스스로 취소하지 않는다.</b> 플레이어가 범위를 빠져나가도 끝까지 휘두른다 —
/// 트리거 범위를 히트 범위보다 좁게 둔 이유가 바로 "빠져나가서 빗나가는" 그림을 만들기 위해서다.
/// 끊는 것은 오직 피격뿐이고, 그건 트리 바깥에서 <see cref="BTNode.Abort"/>로 들어온다.
///
/// 정지는 따로 처리하지 않는다. 적 공격이 LockedAttackData면 Actor가 이동 의도를 알아서 무시하고,
/// 그 전(요청만 하고 아직 시작 안 된 프레임)에도 이 노드가 MoveInput을 건드리지 않으므로 제자리다.
/// </summary>
public class MeleeAttackNode : BTNode
{
    float elapsed;
    bool started; // Fighter가 실제로 공격을 시작한 것을 확인했는지

    protected override void OnEnter(MeleeEnemyContext context)
    {
        elapsed = 0f;
        started = false;

        // 접근 단계에서 이미 받아 왔으면 그대로 유지된다(TryTake는 이미 들고 있으면 true).
        context.TryTakeAttackSlot();
    }

    protected override BTStatus OnTick(MeleeEnemyContext context, float deltaTime)
    {
        if (context.Data == null || context.Fighter == null) return BTStatus.Failure;

        if (context.Data.meleeAttack == null)
        {
            WarnMissingAttack(context);
            return BTStatus.Failure;
        }

        // 얻어맞아 끊긴 것을 "공격이 끝난 것"으로 착각하면 안 된다. 아래 IsAttacking 검사보다 먼저 본다.
        if (context.IsStaggered) return BTStatus.Failure;

        // 공격권이 없으면 때리지 않는다. OnEnter에서 못 받았거나 중간에 빼앗긴 경우.
        if (!context.HoldsAttackSlot) return BTStatus.Failure;

        elapsed += deltaTime;

        if (!started)
        {
            // Fighter가 공격을 시작한 것은 다음 프레임에야 보인다 — 이 노드는 Actor.Update 맨 앞
            // (GetIntent)에서 도는데 TryAttack은 그 뒤에 불리기 때문. 그래서 요청과 확인이 한 프레임 어긋난다.
            if (context.Fighter.IsAttacking)
            {
                started = true;
                return BTStatus.Running;
            }

            if (elapsed >= context.Data.attackStartTimeout)
                return BTStatus.Failure; // 쿨타임이 안 풀렸거나 계속 못 움직이는 상태. 행동을 다시 고른다.

            // 쿨타임 때문에 한 번에 안 나갈 수 있으므로 시작될 때까지 매 프레임 요청한다.
            context.Intent.AttackToStart = context.Data.meleeAttack;
            return BTStatus.Running;
        }

        // 재생 중. Fighter가 자기 타이머로 끝낼 때까지 아무것도 하지 않고 기다린다.
        return context.Fighter.IsAttacking ? BTStatus.Running : BTStatus.Success;
    }

    /// <summary>끝나든 끊기든 공격권은 여기서 반납한다(Abort도 OnExit를 거친다).</summary>
    protected override void OnExit(MeleeEnemyContext context)
    {
        context.ReleaseAttackSlot();
    }

    static bool warnedMissingAttack;

    static void WarnMissingAttack(MeleeEnemyContext context)
    {
        if (warnedMissingAttack) return;

        warnedMissingAttack = true;
        Debug.LogWarning($"{context.Self.name}: AI 수치 에셋의 '근접 공격 데이터'가 비어 있어 공격할 수 없습니다.", context.Data);
    }
}
