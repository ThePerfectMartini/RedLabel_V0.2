/// <summary>비헤이비어 트리 노드 한 번의 실행 결과.</summary>
public enum BTStatus
{
    /// <summary>아직 진행 중. 다음 프레임에도 같은 노드가 이어서 실행된다.</summary>
    Running,
    /// <summary>의도한 일을 끝냈다.</summary>
    Success,
    /// <summary>끝내지 못했다(시간 초과, 조건 불성립 등). 상위 노드가 다른 선택을 한다.</summary>
    Failure,
}

/// <summary>
/// 비헤이비어 트리 노드의 기반. 잎(leaf) 노드는 <see cref="OnTick"/>만 구현하면 되고,
/// "처음 실행된 프레임"과 "끝난 프레임"은 이 클래스가 알아서 <see cref="OnEnter"/> / <see cref="OnExit"/>로 갈라준다.
///
/// 노드는 MonoBehaviour가 아니라 평범한 C# 객체다 — 씬에 얹을 것이 없어 단독 테스트가 쉽고,
/// 트리 조립도 생성자 호출만으로 끝난다. 프레임 진행은 <see cref="MeleeEnemyContext"/>를 든 소유자가 밀어준다.
///
/// 상태(경과 시간, 스냅샷 좌표 등)는 각 노드가 필드로 들고 있으므로 <b>한 인스턴스를 여러 적이 공유하면 안 된다</b>.
/// </summary>
public abstract class BTNode
{
    bool running;

    /// <summary>지금 이 노드가 실행 중인지(OnEnter는 지났고 아직 끝나지 않았는지).</summary>
    public bool IsRunning => running;

    /// <summary>
    /// 지금 이 노드가 실행을 위임하고 있는 자식. 잎 노드는 null이다.
    /// 합성 노드(순서·가중치 랜덤·행동 선택 등)가 겹겹이 쌓여 있어도 이걸 따라 내려가면
    /// 실제로 도는 잎에 닿는다 — 디버그 표시가 타입별로 분기하지 않아도 되게 하는 통로다.
    /// </summary>
    public virtual BTNode ActiveChild => null;

    /// <summary>합성 노드를 따라 내려가 실제로 실행 중인 잎을 찾는다. 잎을 넣으면 그대로 돌려준다.</summary>
    public static BTNode FindActiveLeaf(BTNode node)
    {
        while (node != null && node.ActiveChild != null)
            node = node.ActiveChild;

        return node;
    }

    /// <summary>
    /// 매 프레임 호출. 처음이면 OnEnter를, 끝났으면 OnExit를 끼워 넣는다.
    /// Running이 아닌 값을 돌려주는 순간 이 노드는 초기화되어 다음 호출 때 다시 OnEnter부터 시작한다.
    /// </summary>
    public BTStatus Tick(MeleeEnemyContext context, float deltaTime)
    {
        if (!running)
        {
            running = true;
            OnEnter(context);
        }

        BTStatus status = OnTick(context, deltaTime);

        if (status != BTStatus.Running)
        {
            running = false;
            OnExit(context);
        }

        return status;
    }

    /// <summary>
    /// 밖에서 강제로 끊는다(피격 등). 실행 중이었으면 OnExit를 거쳐 초기화되므로
    /// 다음에 다시 불릴 때 중간부터가 아니라 처음부터 시작한다.
    /// </summary>
    public void Abort(MeleeEnemyContext context)
    {
        if (!running) return;

        running = false;
        OnExit(context);
    }

    /// <summary>이 노드가 실행되기 시작한 프레임에 한 번. 스냅샷·타이머 초기화 자리.</summary>
    protected virtual void OnEnter(MeleeEnemyContext context) { }

    /// <summary>실제 동작. 이번 프레임의 의도를 context.Intent에 써 넣는다.</summary>
    protected abstract BTStatus OnTick(MeleeEnemyContext context, float deltaTime);

    /// <summary>끝나거나 끊긴 프레임에 한 번. 뒷정리 자리(공격권 반납 등).</summary>
    protected virtual void OnExit(MeleeEnemyContext context) { }
}
