using UnityEngine;

/// <summary>
/// 적 AI가 쓰는 위치 계산. MonoBehaviour도 ScriptableObject도 아닌 순수 함수 모음이라
/// (MovementCore/CombatCore와 같은 원칙) Unity 시간이나 씬 상태에 의존하지 않는다.
/// "언제 무엇을 할지"의 판단 흐름은 MeleeEnemyBrain이 갖고, 좌표를 만드는 계산만 여기로 뺀다.
///
/// 모든 함수가 y를 무시하고 바닥 평면(x, z)에서만 계산한다. 벨트스크롤이라 높이는 점프에만 쓰이고
/// 거리 판단에는 관여하지 않는다 — 플레이어가 점프 중이라고 덜 위협적인 것은 아니다.
/// </summary>
public static class EnemyAiMath
{
    /// <summary>높이를 무시한 바닥 평면 거리.</summary>
    public static float GroundDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// 지금 공격을 내면 실제로 맞는 위치인지.
    ///
    /// CombatCore.PerformHitScan은 "정면(FacingDir)으로 attackRange만큼 떨어진 지점에 반경 attackRadius인 구"를
    /// 놓는다. 그 정면이 좌우로만 향하므로 x는 사거리까지, z는 판정 반경까지 허용된다 — 축이 비대칭인 건
    /// 실수가 아니라 판정 모양을 그대로 옮긴 것이다.
    /// </summary>
    public static bool IsInAttackRange(Vector3 selfPos, Vector3 targetPos, float attackRange, float attackRadius)
    {
        return Mathf.Abs(targetPos.x - selfPos.x) <= attackRange
            && Mathf.Abs(targetPos.z - selfPos.z) <= attackRadius;
    }

    /// <summary>
    /// 추적 목표 지점. 지금 서 있는 쪽(대상의 좌 / 우)을 유지한 채 stopDistance만큼 떨어진 곳이다.
    ///
    /// z는 대상과 같게 맞춘다. 공격 판정이 좌우로만 나가기 때문에 z가 어긋나면 사거리 안에 있어도
    /// 판정 반경 밖이라 빗나가기 때문이다.
    /// </summary>
    public static Vector3 ChaseTarget(Vector3 selfPos, Vector3 targetPos, float stopDistance)
    {
        float side = SideSign(selfPos.x, targetPos.x);
        return new Vector3(targetPos.x + side * stopDistance, targetPos.y, targetPos.z);
    }

    /// <summary>
    /// 배회 목표 지점. 대상을 원점으로 좌/우 × 앞/뒤 네 구역으로 나누고,
    /// 지금 자기가 속한 구역의 모서리(대상 ± (radiusX, radiusZ))를 돌려준다.
    ///
    /// 무작위로 고르지 않는 것이 핵심이다. 매번 아무 구역이나 뽑으면 적이 대상 주위를 가로지르며
    /// 헤매게 되고, 그 경로가 하필 대상을 관통한다. 자기 구역을 유지하면 그런 일이 없고,
    /// 대신 대상이 움직여 좌우나 앞뒤 부호가 뒤집히면 목표도 옆 구역으로 따라 옮겨가서
    /// 적이 자연스럽게 그쪽으로 흘러간다.
    /// </summary>
    public static Vector3 WanderTarget(Vector3 selfPos, Vector3 targetPos, float radiusX, float radiusZ)
    {
        float sideX = SideSign(selfPos.x, targetPos.x);
        float sideZ = SideSign(selfPos.z, targetPos.z);
        return new Vector3(targetPos.x + sideX * radiusX, targetPos.y, targetPos.z + sideZ * radiusZ);
    }

    /// <summary>
    /// 대상 기준 어느 쪽에 있는지. +1이면 대상보다 크고(오른쪽 / 뒤), -1이면 작다(왼쪽 / 앞).
    /// 정확히 겹칠 때도 0이 나오지 않게 +1로 기울인다 — Mathf.Sign과 다른 점이며,
    /// 0이 섞이면 목표 지점이 대상 위로 겹쳐서 적이 대상을 파고든다.
    /// </summary>
    static float SideSign(float self, float target) => self >= target ? 1f : -1f;
}
