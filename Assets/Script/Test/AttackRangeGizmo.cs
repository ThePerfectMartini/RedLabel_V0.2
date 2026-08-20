using UnityEngine;

/// <summary>
/// 테스트용: 플레이어의 공격 판정 범위(CombatCore.PerformHitScan과 동일한 위치/반경)를
/// 씬 뷰에 기즈모 구체로 표시한다. 지금이 공격의 어느 타이밍인지 색으로 구분한다.
/// - 회색: 공격 중이 아님
/// - 흰색: 공격 애니메이션 재생 중 (아직 타격 판정 전)
/// - 빨간색: 타격 판정(OnAttackHitFrame) 발생 직후 hitFlashDuration 동안
///   (실제 판정은 애니메이션의 단일 이벤트 프레임에서 한 번만 일어나 눈으로 보기 어려우므로,
///   보기 편하게 붉은 표시를 잠깐 유지시킨다)
///
/// [준비물] PlayerController와 같은 오브젝트에 붙일 것.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class AttackRangeGizmo : MonoBehaviour
{
    [KoreanLabel("타격 판정 표시 유지 시간")]
    public float hitFlashDuration = 0.1f;

    [KoreanLabel("공격 안 할 때 색")]
    public Color idleColor = Color.gray;
    [KoreanLabel("공격 애니메이션 재생 중 색")]
    public Color attackingColor = Color.white;
    [KoreanLabel("타격 판정 색")]
    public Color hitFrameColor = Color.red;

    PlayerController player;
    float hitFlashTimer;

    void OnEnable()
    {
        player = GetComponent<PlayerController>();
        player.OnAttackHitFrameFired += HandleHitFrame;
    }

    void OnDisable()
    {
        if (player != null)
            player.OnAttackHitFrameFired -= HandleHitFrame;
    }

    void HandleHitFrame()
    {
        hitFlashTimer = hitFlashDuration;
    }

    void Update()
    {
        if (hitFlashTimer > 0f)
            hitFlashTimer -= Time.deltaTime;
    }

    void OnDrawGizmos()
    {
        // 에디트 모드(플레이 중이 아닐 때)에도 표시되도록, OnEnable을 못 거쳤으면 직접 찾는다.
        if (player == null)
            player = GetComponent<PlayerController>();
        if (player == null) return;

        Color color = hitFlashTimer > 0f ? hitFrameColor
            : player.IsAttacking ? attackingColor
            : idleColor;

        Vector3 origin = transform.position;
        Vector3 center = origin + player.FacingDir.normalized * player.AttackRange;

        Gizmos.color = color;
        Gizmos.DrawLine(origin, center);
        Gizmos.DrawWireSphere(center, player.AttackRadius);
    }
}
