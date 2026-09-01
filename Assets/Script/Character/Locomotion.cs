using UnityEngine;

/// <summary>
/// 캐릭터의 위치/속도를 직접 적분하는 컴포넌트. Rigidbody를 쓰지 않고 중력·점프·넉백·바운스·
/// 맵 경계 클램프를 전부 손으로 계산해 <c>transform.position</c>에 반영한다.
///
/// "무엇을 할지"는 판단하지 않는다 — Actor가 매 프레임 SetMoveInput / SetFacing / (Prepare)Jump /
/// ApplyKnockback을 호출하고 마지막에 Tick 한 번으로 이동을 확정한다.
///
/// [준비물] 같은 오브젝트에 Collider 하나(타입 무관 — 크기로 BoundaryRadius/GroundOffset 계산),
///          씬에 MapBounds 하나(없어도 넓은 기본 경계로 동작).
/// </summary>
[RequireComponent(typeof(Collider))]
public class Locomotion : MonoBehaviour
{
    [KoreanLabel("이동 스탯")]
    public MovementStatData movementStatData;

    [KoreanLabel("8방향 스냅")]
    [Tooltip("켜면 이동 입력을 45도 단위로 스냅한다(키보드 플레이어용). 끄면 입력 방향 그대로(임의 각도로 목표를 향하는 AI용).")]
    public bool use8DirectionSnap = true;

    // MovementStatData에서 Awake 때 복사. 에셋이 없으면 아래 코드 기본값을 그대로 쓴다.
    float moveSpeed = 5f;
    float gravityScale = 1f;
    float jumpForce = 8f;
    float bounceRestitution = 0.5f;
    float bounceVelocityThreshold = 1f;
    float airborneLaunchThreshold = 0.5f;

    // Collider 크기에서 계산. 맵 경계/바닥 충돌 시 중심점이 아니라 콜라이더 면이 닿아 멈추게 하는 오프셋.
    float boundaryRadius = 0.5f;
    float groundOffset = 0f;

    Vector3 velocity;
    bool isGrounded;
    bool isFacingRight = true;

    // 그라운드 슬라이드(에어본 아닌 수평 넉백) 중 초당 감속량. 공격마다 다를 수 있어 ApplyKnockback에서 받는다.
    float groundSlideDeceleration;
    bool isGroundSliding;

    // 점프 의도가 들어온 순간의 좌우 속도. 준비 동작 중엔 이동이 잠겨 velocity.x가 지워지므로,
    // 실제로 뜨는 순간(Jump())에 이 값을 다시 넣어줘야 그 방향으로 점프한 것처럼 보인다.
    float pendingJumpHorizontalVelocity;

    /// <summary>MoveSpeed에 곱해지는 일시 배율. 이동 허용 공격 중 Actor가 매 프레임 설정한다(기본 1).</summary>
    public float MoveSpeedMultiplier = 1f;

    /// <summary>
    /// 지금 공중에 뜬 것이 <b>무엇 때문인지</b>. 공중 진입점은 Jump()와 ApplyKnockback() 둘뿐이라 출처는 셋 중 하나로 결정된다.
    /// 예전엔 isKnockedBack / LaunchedByJump 두 bool이었는데 (true,true) 무효 조합이 표현 가능했다 → enum으로 합쳐 소멸.
    /// </summary>
    public enum AirborneOrigin
    {
        /// <summary>지상, 혹은 이유 없이 그냥 떨어지는 중(스폰 위치가 바닥보다 살짝 위 등).</summary>
        None,
        /// <summary>Jump()로 뜬 것. 바운스 없이 착지하고, 착지하면 착지 경직으로 이어진다.</summary>
        Jump,
        /// <summary>ApplyKnockback()으로 띄워진 것. 바닥/벽에서 바운스하고, 착지하면 쓰러진다.</summary>
        Knockback,
    }

    AirborneOrigin origin = AirborneOrigin.None;

    // ===== 외부(Actor / CharacterStateMachine / Fighter)가 읽는 정보 =====

    public Vector3 Velocity => velocity;
    public bool IsGrounded => isGrounded;
    public bool IsGroundSliding => isGroundSliding;
    public bool IsKnockedBackAirborne => origin == AirborneOrigin.Knockback;

    /// <summary>지금 공중에 뜬 것이 실제로 Jump()를 거친 결과인지. 착지 경직으로 이어질지를 가른다.</summary>
    public bool LaunchedByJump => origin == AirborneOrigin.Jump;

    /// <summary>현재 수평 속력. CharacterStateMachine이 Move/Idle을 가르는 기준.</summary>
    public float HorizontalSpeed => new Vector2(velocity.x, velocity.z).magnitude;

    public bool FacingRight => isFacingRight;

    /// <summary>공격 판정이 나가는 방향. 스프라이트가 좌우로만 뒤집히므로 좌/우 둘 중 하나.</summary>
    public Vector3 FacingDir => isFacingRight ? Vector3.right : Vector3.left;

    void Awake()
    {
        if (movementStatData != null)
        {
            moveSpeed = movementStatData.moveSpeed;
            gravityScale = movementStatData.gravityScale;
            jumpForce = movementStatData.jumpForce;
            bounceRestitution = movementStatData.bounceRestitution;
            bounceVelocityThreshold = movementStatData.bounceVelocityThreshold;
            airborneLaunchThreshold = movementStatData.airborneLaunchThreshold;
        }
        else
        {
            Debug.LogWarning($"{name}: movementStatData가 없어 Locomotion의 코드 기본값을 사용합니다.");
        }

        Collider col = GetComponent<Collider>();
        Vector3 extents = col.bounds.extents;
        boundaryRadius = Mathf.Max(extents.x, extents.z);
        groundOffset = extents.y; // 피벗이 콜라이더 중심에 있다고 가정
    }

    // ===== 이동 / 방향 =====

    /// <summary>
    /// 이동 입력을 받아 수평 속도를 갱신한다. 공중이거나 그라운드 슬라이드 중이면 무시된다(공중 이동 제어 없음).
    /// </summary>
    public void SetMoveInput(Vector2 rawInput)
    {
        if (!isGrounded || isGroundSliding)
            return;

        if (rawInput.sqrMagnitude < 0.01f)
        {
            velocity.x = 0f;
            velocity.z = 0f;
            return;
        }

        Vector3 rawDir = new Vector3(rawInput.x, 0f, rawInput.y);
        Vector3 dir = use8DirectionSnap ? SnapTo8Directions(rawDir) : rawDir.normalized;
        float speed = moveSpeed * MoveSpeedMultiplier;
        velocity.x = dir.x * speed;
        velocity.z = dir.z * speed;
    }

    /// <summary>
    /// 바라볼 좌/우를 설정한다. X 성분만 보고(0이면 유지), <b>지상에 있을 때만</b> 반영한다 —
    /// 공중에서는 방향을 못 바꾼다. 이동이 잠긴 상태에선 Actor가 Vector3.zero를 넘겨 자연히 무시된다.
    /// </summary>
    public void SetFacing(Vector3 direction)
    {
        if (!isGrounded) return;
        if (Mathf.Abs(direction.x) > 0.01f)
            isFacingRight = direction.x > 0f;
    }

    /// <summary>임의의 입력 방향을 8방향(45도 단위) 중 가장 가까운 쪽으로 스냅.</summary>
    public static Vector3 SnapTo8Directions(Vector3 rawDir)
    {
        if (rawDir.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        rawDir.Normalize();
        float angle = Mathf.Atan2(rawDir.x, rawDir.z) * Mathf.Rad2Deg;
        float snapped = Mathf.Round(angle / 45f) * 45f;
        float rad = snapped * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
    }

    // ===== 점프 =====

    /// <summary>
    /// 점프 의도가 들어온 순간 Actor가 호출한다. 아직 뜨지 않고, 이 순간의 좌우 이동 방향만 기억해둔다.
    /// 실제로 뜨는 건 점프 준비 클립의 Animation Event가 Jump()를 부를 때다.
    /// </summary>
    public void PrepareJump(float horizontalInputX)
    {
        pendingJumpHorizontalVelocity = Mathf.Abs(horizontalInputX) > 0.01f
            ? Mathf.Sign(horizontalInputX) * moveSpeed
            : 0f;
    }

    /// <summary>
    /// 점프 준비 클립의 "발이 떨어지는" 프레임에 CharacterStateMachine이 호출한다. 여기서 비로소 위로 뜬다.
    /// PrepareJump에서 기억해둔 방향으로 수평 속도도 같이 부여한다(제자리 점프면 0).
    /// </summary>
    public void Jump()
    {
        if (!isGrounded) return;

        velocity.x = pendingJumpHorizontalVelocity;
        velocity.y = jumpForce;
        isGrounded = false;
        origin = AirborneOrigin.Jump; // 넉백이 아니므로 바운스 대상 아님, 착지하면 착지 경직으로
        isGroundSliding = false;
    }

    // ===== 넉백 =====

    /// <summary>
    /// 넉백 적용. 수직 속도가 임계값을 넘으면 공중으로 띄우고(바운스 대상), 그 이하이고 이미 지상이면
    /// 뜨지 않고 그라운드 슬라이드로 처리한다. 어느 쪽이든 origin을 새로 덮어써서 진행 중이던 점프를 무효화한다
    /// (얻어맞았는데 점프 착지 경직으로 이어지면 안 되므로).
    /// </summary>
    public void ApplyKnockback(Vector3 knockbackVelocity, float slideDeceleration)
    {
        if (isGrounded && Mathf.Abs(knockbackVelocity.y) <= airborneLaunchThreshold)
        {
            velocity.x = knockbackVelocity.x;
            velocity.z = knockbackVelocity.z;
            origin = AirborneOrigin.None;
            isGroundSliding = true;
            groundSlideDeceleration = slideDeceleration;
        }
        else
        {
            velocity = knockbackVelocity;
            isGrounded = false;
            origin = AirborneOrigin.Knockback;
            isGroundSliding = false;
        }
    }

    /// <summary>
    /// 진행 중인 그라운드 슬라이드를 즉시 끝내고 남은 수평 속도를 지운다. 돌진(Impulse) 공격처럼
    /// "슬라이드가 그 동작의 일부"인 경우 동작이 끝나는 시점에 Fighter가 호출한다.
    /// 슬라이드 중이 아니면 아무것도 안 하므로 피격 슬라이드를 실수로 끊을 걱정은 없다.
    /// </summary>
    public void StopGroundSlide()
    {
        if (!isGroundSliding) return;

        isGroundSliding = false;
        velocity.x = 0f;
        velocity.z = 0f;
    }

    // ===== 매 프레임 적분 =====

    /// <summary>
    /// 그라운드 슬라이드 감속 → 중력 → 위치 적분 → 바닥 충돌/바운스 → 맵 경계 클램프/바운스 순서.
    /// Actor가 매 프레임 마지막에 한 번 호출한다.
    /// </summary>
    public void Tick(float deltaTime)
    {
        Vector3 pos = transform.position;

        if (isGroundSliding)
        {
            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            float newSpeed = Mathf.Max(0f, horizontal.magnitude - groundSlideDeceleration * deltaTime);
            if (newSpeed <= 0f)
            {
                velocity.x = 0f;
                velocity.z = 0f;
                isGroundSliding = false;
            }
            else
            {
                Vector3 h = horizontal.normalized * newSpeed;
                velocity.x = h.x;
                velocity.z = h.z;
            }
        }

        if (!isGrounded)
        {
            float gravity = GlobalPhysicsData.Instance != null ? GlobalPhysicsData.Instance.gravity : 20f;
            velocity.y -= gravity * gravityScale * deltaTime;
        }

        pos += velocity * deltaTime;

        // 바닥 충돌 / 바운스. 넉백 낙하만 튕기고, 점프 착지와 그냥 낙하는 절대 안 튕긴다.
        if (pos.y <= groundOffset)
        {
            pos.y = groundOffset;
            bool wasAirborne = !isGrounded;

            if (wasAirborne && origin == AirborneOrigin.Knockback && Mathf.Abs(velocity.y) > bounceVelocityThreshold)
            {
                velocity.y = -velocity.y * bounceRestitution;
                velocity.x *= bounceRestitution;
                velocity.z *= bounceRestitution;
            }
            else
            {
                velocity.y = 0f;
                // 막 착지하는 순간에만 수평 속도 제거. 안 그러면 넉백으로 받은 수평 속도가 착지 후에도 안 사라진다.
                if (wasAirborne)
                {
                    velocity.x = 0f;
                    velocity.z = 0f;
                }
                isGrounded = true;
                origin = AirborneOrigin.None; // 착지했으니 공중 출처 소멸. Actor가 Tick 직전 값을 보고 착지 경직/다운을 건다.
            }
        }

        Bounds bounds = MapBounds.Instance != null
            ? MapBounds.Instance.Bounds
            : new Bounds(Vector3.zero, Vector3.one * 1000f);

        float minX = bounds.min.x + boundaryRadius;
        float maxX = bounds.max.x - boundaryRadius;
        if (pos.x < minX || pos.x > maxX)
        {
            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            if (origin == AirborneOrigin.Knockback)
                velocity.x = Mathf.Abs(velocity.x) > bounceVelocityThreshold ? -velocity.x * bounceRestitution : 0f;
        }

        float minZ = bounds.min.z + boundaryRadius;
        float maxZ = bounds.max.z - boundaryRadius;
        if (pos.z < minZ || pos.z > maxZ)
        {
            pos.z = Mathf.Clamp(pos.z, minZ, maxZ);
            if (origin == AirborneOrigin.Knockback)
                velocity.z = Mathf.Abs(velocity.z) > bounceVelocityThreshold ? -velocity.z * bounceRestitution : 0f;
        }

        transform.position = pos;
    }
}
