/// <summary>
/// 공격 — 의도만 내고, 몸이 실제로 공격을 끝낼 때까지 기다린다. 캔슬은 없다.
///
/// 시작 공격은 하나뿐이라 AttackToStart는 비워둔다 (EnemyController가 자기 firstAttackData로 채운다).
///
/// [순서에 주의] IsAttacking을 먼저 보고 그 다음에 WantsAttack을 낸다. 순서를 뒤집으면
/// 공격이 시작된 프레임에도 WantsAttack이 한 번 더 서서 콤보가 진행되어 버린다.
/// StepContext는 몸이 이번 프레임 의도를 처리하기 **전에** 만들어지므로 IsAttacking은 한 프레임 늦다.
/// </summary>
public sealed class AttackStep : IBehaviorStep, IAttackCommitStep
{
    bool started;
    float startTimer;

    public void OnEnter(in StepContext ctx)
    {
        started = false;
        startTimer = 0f;
    }

    public StepOutcome Tick(in StepContext ctx, ref CharacterIntent intent)
    {
        if (!started)
        {
            if (ctx.IsAttacking)
            {
                started = true;
                return StepOutcome.Running;
            }

            intent.WantsAttack = true;

            // 쿨타임 등으로 시작이 안 될 수 있다. 무한정 조르지 않고 포기해서 순환을 다시 돌린다.
            startTimer += ctx.DeltaTime;
            return startTimer >= ctx.Tuning.attackStartTimeout
                ? StepOutcome.Abort
                : StepOutcome.Running;
        }

        // 콤보 전체가 끝나는 순간을 잡는다. 맞았든 헛쳤든 여기서 후퇴로 넘어간다.
        return ctx.IsAttacking ? StepOutcome.Running : StepOutcome.Done;
    }

    public void OnExit() { }
}
