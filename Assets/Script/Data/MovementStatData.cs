using UnityEngine;

/// <summary>
/// MovementCore에 주입할 이동 관련 스탯 데이터.
/// BoundaryRadius/GroundOffset은 콜라이더 크기로 자동 계산되는 값이라 여기 포함하지 않는다.
/// </summary>
[CreateAssetMenu(fileName = "MovementStatData", menuName = "DoitMySelf/Movement Stat Data")]
public class MovementStatData : ScriptableObject
{
    [KoreanLabel("이동 속도")]
    public float moveSpeed = 5f;

    [KoreanLabel("중력 스케일")]
    [Tooltip("캐릭터 '무게' 배율. GlobalPhysicsData의 공용 중력에 이 값을 곱해서 적용한다. 1이 기본, 크면 무겁게(더 빨리 떨어짐), 작으면 가볍게.")]
    public float gravityScale = 1f;

    [KoreanLabel("점프 힘")]
    public float jumpForce = 8f;

    [KoreanLabel("바운스 반발력")]
    [Tooltip("바닥/벽에 부딪혔을 때 튕기는 비율(0~1). 0.5면 충돌 직전 속도의 절반으로 반사된다.")]
    public float bounceRestitution = 0.5f;

    [KoreanLabel("바운스 최소 속도")]
    [Tooltip("충돌 속도(절대값)가 이 값 이하면 바운스하지 않고 그대로 멈춘다. 무한 바운스 방지용.")]
    public float bounceVelocityThreshold = 1f;

    [Header("그라운드 넉백(히트슬라이드)")]
    [KoreanLabel("공중 판정 임계값")]
    [Tooltip("넉백의 수직 속도 절대값이 이 값 이하면 공중으로 띄우지 않고 그라운드 슬라이드로 처리")]
    public float airborneLaunchThreshold = 0.5f;
}
