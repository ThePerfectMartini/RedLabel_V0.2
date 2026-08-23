using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 적이 플레이어를 쫓아가 공격 가능한 위치에 도착하면 공격을 시전하는 기본 IEnemyBrain 구현.
///
/// [슬롯 시스템] 여러 마리가 동시에 쫓아오면 전부 같은 지점(플레이어 앞/뒤 stopDistance)으로 몰려서
/// 겹쳐버린다. 그래서 플레이어 기준 좌/우 두 "1순위 슬롯"(stopDistance 지점)만 실제로 공격할 수 있고,
/// 같은 쪽에 더 있는 적들은 1순위 슬롯 뒤로 queueSpacing씩 더 떨어진 자리에서 순서를 기다린다.
/// 순위는 활성화된 모든 ChasePlayerBrain을 정적 리스트로 모아두고, 매 Think()마다 "플레이어 기준
/// 같은 쪽에 있으면서 나보다 가까운 적의 수"로 즉석에서 계산한다(고정 배정이 아니라 거리 기반이라
/// 서로 위치가 바뀌면 순위도 자연스럽게 바뀐다).
///
/// 공격(CombatCore.PerformHitScan)은 좌/우(FacingDir, x축)로만 나가고 z가 어긋나면 판정 반경 밖이라
/// 맞지 않으므로, 목표 지점 자체를 "플레이어 x ± (stopDistance + 대기 순번 * queueSpacing), 플레이어와
/// 같은 z"로 고정해서 이동시킨다.
///
/// [준비물] EnemyController와 같은 오브젝트에 붙일 것. 씬에 PlayerController가 하나 있어야 한다
/// (PlayerController.Instance로 자동 탐색, 인스펙터 연결 불필요).
/// </summary>
[RequireComponent(typeof(EnemyController))]
public class ChasePlayerBrain : MonoBehaviour, IEnemyBrain
{
    [KoreanLabel("정지 거리(1순위 슬롯)")]
    [Tooltip("플레이어의 x축 기준 앞 또는 뒤, 1순위 슬롯이 위치할 거리. 공격 사거리와 맞춰 조절.")]
    public float stopDistance = 1.5f;

    [KoreanLabel("대기열 간격")]
    [Tooltip("같은 쪽에서 순서를 기다리는 적들끼리의 간격. 1순위 슬롯 뒤로 이 간격씩 더 떨어져서 줄을 선다.")]
    public float queueSpacing = 1.2f;

    [KoreanLabel("도착 판정 허용 오차")]
    [Tooltip("목표 지점과의 거리가 이 값 이하면 도착한 것으로 보고 이동을 멈춘다 (진동 방지용 데드존).")]
    public float arrivalTolerance = 0.1f;

    [KoreanLabel("공격 후 대기 시간")]
    [Tooltip("공격(콤보 전체)이 끝난 직후 다음 행동(재공격 또는 추적 재개)까지 쉬는 시간(초).")]
    public float postAttackPause = 0.4f;

    [KoreanLabel("플레이어 위치 갱신 주기(초)")]
    [Tooltip("이 주기마다 한 번씩만 플레이어 좌표를 새로 읽어와 추적한다. 매 프레임 실시간으로 정확히 " +
        "쫓아가면 너무 기계적이고 어렵게 느껴져서, 일부러 약간의 반응 지연을 준다.")]
    public float positionUpdateInterval = 0.2f;

    // 슬롯 순위 계산에 참여하는 모든 활성 인스턴스. 씬에 있는 적 수만큼만 있으므로 매 프레임
    // O(n^2)로 순위를 다시 계산해도 부담 없다.
    static readonly List<ChasePlayerBrain> activeBrains = new List<ChasePlayerBrain>();

    bool wasAttacking;
    float postAttackTimer;

    bool hasTrackedPosition;
    Vector3 trackedPlayerPos;
    float positionUpdateTimer;

    void OnEnable() => activeBrains.Add(this);
    void OnDisable() => activeBrains.Remove(this);

    public EnemyIntent Think(EnemyController owner, float deltaTime)
    {
        PlayerController player = PlayerController.Instance;
        if (player == null)
            return EnemyIntent.None;

        Vector3 enemyPos = owner.Position;

        // 플레이어 좌표를 매 프레임 실시간으로 읽지 않고, positionUpdateInterval마다 한 번씩만 다시
        // 샘플링해서 그 사이엔 이 값을 그대로 목표로 쓴다. 방향 전환/이동 둘 다 이 값을 기준으로 하므로
        // 플레이어가 움직여도 다음 갱신 시점까지는 조금 늦게 반응하는 것처럼 보인다.
        positionUpdateTimer -= deltaTime;
        if (!hasTrackedPosition || positionUpdateTimer <= 0f)
        {
            trackedPlayerPos = player.transform.position;
            positionUpdateTimer = positionUpdateInterval;
            hasTrackedPosition = true;
        }
        Vector3 playerPos = trackedPlayerPos;

        // 이동 방향과 무관하게 항상 플레이어를 바라본다. 너무 가까워서 목표 지점까지 뒤로 물러나야 할
        // 때도 여전히 플레이어 쪽을 보고 있어야 하기 때문(EnemyController는 더 이상 이동 방향으로 자동
        // 갱신하지 않음). Stun 등 이동이 막힌 상태에서는 SetFacing 내부에서 알아서 무시된다.
        owner.SetFacing(playerPos - enemyPos);

        // 공격(콤보 전체)이 막 끝난 시점을 감지해서 대기 타이머를 건다. 이게 없으면 마지막 타격
        // 직후 쿨타임이 이미 지나 있어서 바로 재공격/추적이 이어져 텀 없이 이어붙는 것처럼 보인다.
        if (wasAttacking && !owner.IsAttacking)
            postAttackTimer = postAttackPause;
        wasAttacking = owner.IsAttacking;

        if (postAttackTimer > 0f)
        {
            postAttackTimer -= deltaTime;
            return EnemyIntent.None;
        }

        // 지금 서 있는 쪽(플레이어의 왼쪽/오른쪽)을 기준으로 목표 x를 정한다.
        // 플레이어를 실제로 넘어서기 전까지는 같은 쪽에서 계속 접근한다.
        float side = enemyPos.x >= playerPos.x ? 1f : -1f;

        // 같은 쪽에서 나보다 플레이어에 가까운 적의 수 = 내 대기 순번(0이면 1순위 슬롯).
        int queueIndex = GetQueueIndex(side, player.transform.position);
        float assignedDistance = stopDistance + queueIndex * queueSpacing;
        float targetX = playerPos.x + side * assignedDistance;

        Vector3 toTarget = new Vector3(targetX - enemyPos.x, 0f, playerPos.z - enemyPos.z);

        if (toTarget.sqrMagnitude <= arrivalTolerance * arrivalTolerance)
        {
            // 1순위 슬롯(queueIndex == 0)만 공격한다. 대기열에 있는 적은 자기 순번 자리에 도착하면
            // 그냥 멈춰서 기다린다 (공격 사거리 밖이라 공격해도 안 맞으므로 시도조차 하지 않는다).
            if (queueIndex > 0)
                return EnemyIntent.None;

            EnemyIntent attackIntent = EnemyIntent.None;
            attackIntent.WantsAttack = true;
            return attackIntent;
        }

        Vector3 dir = toTarget.normalized;

        EnemyIntent intent = EnemyIntent.None;
        intent.MoveInput = new Vector2(dir.x, dir.z);
        return intent;
    }

    /// <summary>
    /// 같은 쪽(side)에 있는 다른 활성 ChasePlayerBrain 중, 지금 나보다 플레이어에 더 가까운
    /// 적의 수를 센다. 0이면 그 쪽의 1순위(공격) 슬롯, 1 이상이면 그만큼 뒤에서 대기.
    /// 거리가 같으면 InstanceID로 동점을 깨서 매 프레임 순위가 흔들리지 않게 한다.
    /// </summary>
    int GetQueueIndex(float side, Vector3 livePlayerPos)
    {
        float myDistance = Mathf.Abs(transform.position.x - livePlayerPos.x);
        int index = 0;

        foreach (ChasePlayerBrain other in activeBrains)
        {
            if (other == this) continue;

            float otherSide = other.transform.position.x >= livePlayerPos.x ? 1f : -1f;
            if (!Mathf.Approximately(otherSide, side)) continue;

            float otherDistance = Mathf.Abs(other.transform.position.x - livePlayerPos.x);
            bool otherIsCloser = otherDistance < myDistance
                || (otherDistance == myDistance && other.GetInstanceID() < GetInstanceID());

            if (otherIsCloser)
                index++;
        }

        return index;
    }
}
