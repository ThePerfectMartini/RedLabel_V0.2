using UnityEngine;

/// <summary>
/// 순수 이동 로직. MonoBehaviour가 아니며 Unity API에 최소 의존.
/// PlayerController(또는 EnemyController)가 이 값을 읽어서 transform.position에 반영한다.
/// Rigidbody 사용하지 않음 -> 모든 이동/중력/넉백을 직접 적분한다.
/// </summary>
public class MovementCore
{
    public Vector3 Position;
    public Vector3 Velocity;

    public float MoveSpeed = 5f;

    /// <summary>
    /// 이 캐릭터의 "무게" 배율. GlobalPhysicsData.Instance.gravity에 곱해져서 실제 낙하 가속도가 된다.
    /// </summary>
    public float GravityScale = 1f;

    public bool IsGrounded = false;

    /// <summary>
    /// 캐릭터 콜라이더의 반경(또는 절반 두께). 맵 경계 클램핑 시 중심점이 아니라
    /// 콜라이더 바깥쪽 면이 벽에 닿아 멈추도록 이 값만큼 안쪽에서 클램핑한다.
    /// </summary>
    public float BoundaryRadius = 0.5f;

    /// <summary>
    /// 피벗(오브젝트 중심)에서 콜라이더 바닥면까지의 거리. 콜라이더의 절반 높이.
    /// 바닥 충돌 시 중심점이 아니라 이 값만큼 위에서 멈춰야 콜라이더 바닥이 y=0에 닿는다.
    /// </summary>
    public float GroundOffset = 0f;

    /// <summary>
    /// 점프 시 Y축에 즉시 부여하는 초기 속도.
    /// </summary>
    public float JumpForce = 8f;

    /// <summary>
    /// 바닥/벽에 부딪혔을 때 튕기는 비율(0~1). 0.5면 충돌 직전 속도의 절반으로 반사.
    /// </summary>
    public float BounceRestitution = 0.5f;

    /// <summary>
    /// 충돌 속도(절대값)가 이 값 이하면 바운스하지 않고 그대로 멈춘다. 무한 바운스 방지용.
    /// </summary>
    public float BounceVelocityThreshold = 1f;

    /// <summary>
    /// 넉백의 수직 속도 절대값이 이 값 이하면 공중으로 띄우지 않고 그라운드 슬라이드로 처리한다.
    /// </summary>
    public float AirborneLaunchThreshold = 0.5f;

    // 그라운드 슬라이드 중 적용할 감속량. 공격마다 다를 수 있어 ApplyKnockback 호출 시점에 전달받는다.
    float currentGroundSlideDeceleration;

    // 현재 공중 상태가 "점프"가 아니라 "넉백"인지 구분하는 내부 플래그.
    // 바운스는 넉백일 때만 일어나야 한다 (점프는 바운스 없이 그냥 착지).
    bool isKnockedBack = false;

    /// <summary>
    /// 그라운드에 붙은 채로 수평 슬라이드만 하는 넉백(에어본 아님) 상태인지.
    /// 이 상태에서는 SetMoveInput이 무시되고, Tick에서 마찰로 서서히 감속한다.
    /// </summary>
    public bool IsGroundSliding { get; private set; }

    /// <summary>
    /// 공중에 뜬 상태가 "점프"가 아니라 "넉백으로 띄워진 것"인지. Jump 상태와 Airborne 상태를 구분할 때 사용.
    /// </summary>
    public bool IsKnockedBackAirborne => isKnockedBack;

    /// <summary>
    /// MovementStatData의 값을 이 인스턴스에 복사한다. data가 null이면 기본값을 그대로 둔다.
    /// BoundaryRadius/GroundOffset은 콜라이더에서 계산되는 값이라 여기서 건드리지 않는다.
    /// 중력은 여기서 받지 않고, Tick에서 GlobalPhysicsData.Instance를 직접 읽는다.
    /// </summary>
    public void Init(MovementStatData data)
    {
        if (data == null) return;

        MoveSpeed = data.moveSpeed;
        GravityScale = data.gravityScale;
        JumpForce = data.jumpForce;
        BounceRestitution = data.bounceRestitution;
        BounceVelocityThreshold = data.bounceVelocityThreshold;
        AirborneLaunchThreshold = data.airborneLaunchThreshold;
    }

    /// <summary>
    /// 입력 벡터(-1~1)를 받아 8방향으로 스냅한 뒤 수평 속도를 갱신한다.
    /// 공중에서는 호출해도 무시된다 (공중 이동 제어 없음 확정 사항 반영).
    /// </summary>
    public void SetMoveInput(Vector2 rawInput)
    {
        if (!IsGrounded || IsGroundSliding)
            return; // 공중이거나 그라운드 슬라이드 중에는 입력이 velocity에 영향 없음

        if (rawInput.sqrMagnitude < 0.01f)
        {
            Velocity.x = 0f;
            Velocity.z = 0f;
            return;
        }

        Vector3 dir = SnapTo8Directions(new Vector3(rawInput.x, 0f, rawInput.y));
        Velocity.x = dir.x * MoveSpeed;
        Velocity.z = dir.z * MoveSpeed;
    }

    /// <summary>
    /// 임의의 입력 방향을 8방향(45도 단위) 중 가장 가까운 방향으로 스냅.
    /// </summary>
    public static Vector3 SnapTo8Directions(Vector3 rawDir)
    {
        if (rawDir.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        rawDir.Normalize();
        float angle = Mathf.Atan2(rawDir.x, rawDir.z) * Mathf.Rad2Deg;
        float snappedAngle = Mathf.Round(angle / 45f) * 45f;
        float rad = snappedAngle * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
    }

    /// <summary>
    /// 넉백 적용. 수직 속도가 AirborneLaunchThreshold를 넘으면 기존처럼 공중으로 띄우고,
    /// 그 이하(거의 0)이면서 이미 지상에 있었다면 뜨지 않고 그라운드 슬라이드로 처리한다.
    /// groundSlideDeceleration은 그라운드 슬라이드로 처리될 때만 사용되며, 공격(CombatCore)마다 다르게 넘길 수 있다.
    /// </summary>
    public void ApplyKnockback(Vector3 knockbackVelocity, float groundSlideDeceleration)
    {
        if (IsGrounded && Mathf.Abs(knockbackVelocity.y) <= AirborneLaunchThreshold)
        {
            // 그라운드 넉백(히트슬라이드): 공중으로 띄우지 않고 수평 속도만 덮어써서 미끄러뜨린다.
            Velocity.x = knockbackVelocity.x;
            Velocity.z = knockbackVelocity.z;
            IsGroundSliding = true;
            currentGroundSlideDeceleration = groundSlideDeceleration;
        }
        else
        {
            Velocity = knockbackVelocity;
            IsGrounded = false;
            isKnockedBack = true;
            IsGroundSliding = false;
        }
    }

    /// <summary>
    /// 점프. 지상에 있을 때만 동작하며(이단 점프 없음), Y축 속도를 새로 부여한다.
    /// horizontalVelocity를 지정하면 점프 순간 수평(X) 속도도 그 값으로 같이 덮어쓴다
    /// (점프 키를 누르기 직전 좌우로 이동 중이었다면 그 방향으로 뜨는 용도). 기본값 0이면 제자리에서 뜬다.
    /// 공중에서는 SetMoveInput이 무시되므로(공중 이동 제어 없음), 여기서 정한 수평 속도가
    /// 착지할 때까지 그대로 유지된다.
    /// 이후 중력은 Tick에서 기존 로직 그대로 적용된다.
    /// </summary>
    public void Jump(float horizontalVelocity = 0f)
    {
        if (!IsGrounded)
            return;

        Velocity.x = horizontalVelocity;
        Velocity.y = JumpForce;
        IsGrounded = false;
        isKnockedBack = false; // 점프는 넉백이 아니므로 수평 속도 감쇠 대상에서 제외
        IsGroundSliding = false; // 슬라이드 도중 점프하면 슬라이드 상태는 종료
    }

    /// <summary>
    /// 매 프레임 위치 갱신. 그라운드 슬라이드 감속 -> 중력 적용 -> 위치 적분 -> 바닥 체크 -> 맵 경계 클램핑 순서.
    /// </summary>
    public void Tick(float deltaTime, Bounds mapBounds)
    {
        if (IsGroundSliding)
        {
            Vector3 horizontal = new Vector3(Velocity.x, 0f, Velocity.z);
            float newSpeed = Mathf.Max(0f, horizontal.magnitude - currentGroundSlideDeceleration * deltaTime);

            if (newSpeed <= 0f)
            {
                Velocity.x = 0f;
                Velocity.z = 0f;
                IsGroundSliding = false;
            }
            else
            {
                Vector3 newHorizontal = horizontal.normalized * newSpeed;
                Velocity.x = newHorizontal.x;
                Velocity.z = newHorizontal.z;
            }
        }

        if (!IsGrounded)
        {
            float gravity = GlobalPhysicsData.Instance != null ? GlobalPhysicsData.Instance.gravity : 20f;
            Velocity.y -= gravity * GravityScale * deltaTime;
        }

        Position += Velocity * deltaTime;

        // 바닥 충돌 / 바운스. 넉백으로 인한 낙하일 때만 튕기고, 점프 착지는 절대 튕기지 않는다.
        if (Position.y <= GroundOffset)
        {
            Position.y = GroundOffset;
            bool wasAirborne = !IsGrounded;

            if (wasAirborne && isKnockedBack && Mathf.Abs(Velocity.y) > BounceVelocityThreshold)
            {
                // 넉백 중 충분히 빠른 속도로 떨어졌으면 수직/수평 속도를 같은 비율로 줄이며 튕겨나감
                Velocity.y = -Velocity.y * BounceRestitution;
                Velocity.x *= BounceRestitution;
                Velocity.z *= BounceRestitution;
            }
            else
            {
                Velocity.y = 0f;

                // 공중 -> 지상으로 막 착지하는 순간에만 수평 속도도 함께 제거.
                // 안 그러면 넉백으로 받은 수평 속도가 착지 후에도 안 사라지고 계속 밀림
                // (플레이어는 매 프레임 SetMoveInput이 덮어써서 안 보였을 뿐, 입력 없는 대상은 무한 슬라이딩됨)
                if (wasAirborne)
                {
                    Velocity.x = 0f;
                    Velocity.z = 0f;
                }

                IsGrounded = true;
                isKnockedBack = false; // 착지했으니 넉백 상태 종료
            }
        }

        // 맵 경계 = 벽 충돌 / 바운스. 넉백 상태에서 부딪힌 경우에만 바운스하고,
        // 걸어서 부딪히거나 점프 중에 부딪힌 경우는 바운스 없이 제자리에 멈춘다.
        float minX = mapBounds.min.x + BoundaryRadius;
        float maxX = mapBounds.max.x - BoundaryRadius;
        if (Position.x < minX || Position.x > maxX)
        {
            Position.x = Mathf.Clamp(Position.x, minX, maxX);
            if (isKnockedBack)
            {
                Velocity.x = Mathf.Abs(Velocity.x) > BounceVelocityThreshold
                    ? -Velocity.x * BounceRestitution
                    : 0f;
            }
        }

        float minZ = mapBounds.min.z + BoundaryRadius;
        float maxZ = mapBounds.max.z - BoundaryRadius;
        if (Position.z < minZ || Position.z > maxZ)
        {
            Position.z = Mathf.Clamp(Position.z, minZ, maxZ);
            if (isKnockedBack)
            {
                Velocity.z = Mathf.Abs(Velocity.z) > BounceVelocityThreshold
                    ? -Velocity.z * BounceRestitution
                    : 0f;
            }
        }
    }
}