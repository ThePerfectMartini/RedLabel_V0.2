using UnityEngine;

/// <summary>
/// CombatCore 공격 판정 테스트용 더미.
/// MovementCore를 플레이어와 동일하게 재사용해서 중력/바닥/맵 경계가 똑같이 적용된다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class TestDummy : MonoBehaviour, IHittable, IStateMachineOwner
{
    [Header("스탯 데이터")]
    [KoreanLabel("캐릭터 스탯")]
    public CharacterStatData characterStatData;
    [KoreanLabel("이동 스탯")]
    public MovementStatData movementStatData;

    int health;

    readonly MovementCore movement = new MovementCore();
    readonly CharacterStateMachine stateMachine = new CharacterStateMachine();
    public CharacterStateMachine StateMachine => stateMachine;

    // ===== TEMP: 넉백 색상 테스트 (제거 시 이 블록 전부 + Update 안의 호출부 삭제) =====
    [KoreanLabel("넉백 중 색상 (테스트)")]
    public Color tempKnockbackColor = Color.red;
    Renderer tempRenderer;
    Color tempOriginalColor;
    // ===== TEMP 끝 =====

    void Awake()
    {
        movement.Position = transform.position;

        // ===== TEMP: 넉백 색상 테스트 =====
        tempRenderer = GetComponentInChildren<Renderer>();
        if (tempRenderer != null)
            tempOriginalColor = tempRenderer.material.color;
        // ===== TEMP 끝 =====

        if (characterStatData == null)
            Debug.LogWarning($"{name}: characterStatData가 연결되지 않아 기본 체력(1000)을 사용합니다.");

        health = characterStatData != null ? characterStatData.maxHealth : 1000;

        movement.Init(movementStatData);

        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Vector3 extents = col.bounds.extents;
            movement.BoundaryRadius = Mathf.Max(extents.x, extents.z);
            movement.GroundOffset = extents.y;
        }
    }

    void Update()
    {
        Bounds bounds = MapBounds.Instance != null
            ? MapBounds.Instance.Bounds
            : new Bounds(Vector3.zero, Vector3.one * 1000f);

        movement.Tick(Time.deltaTime, bounds);
        transform.position = movement.Position;

        UpdateCharacterState();

        // ===== TEMP: 넉백 색상 테스트 =====
        if (tempRenderer != null)
            tempRenderer.material.color = movement.IsInKnockback ? tempKnockbackColor : tempOriginalColor;
        // ===== TEMP 끝 =====
    }

    /// <summary>
    /// TestDummy는 입력/공격이 없어 이동/공격/점프 계열 상태는 없고, 피격 관련 상태만 판단한다.
    /// </summary>
    void UpdateCharacterState()
    {
        if (movement.IsKnockedBackAirborne)
            stateMachine.SetState(CharacterState.Airborne);
        else if (movement.IsGroundSliding)
            stateMachine.SetState(CharacterState.Stun);
        else
            stateMachine.SetState(CharacterState.Idle);
    }

    public void OnHit(HitData hit)
    {
        health -= hit.Damage;
        Debug.Log($"{name} 피격: {hit.Damage} 데미지, 남은 체력 {health}, 넉백 {hit.KnockbackVelocity}");

        movement.ApplyKnockback(hit.KnockbackVelocity, hit.GroundSlideDeceleration);

        if (health <= 0)
        {
            Debug.Log($"{name} 파괴됨");
            Destroy(gameObject);
        }
    }
}