using UnityEngine;

/// <summary>
/// 적이 죽으면 일정 시간 뒤 GameObject를 파괴한다. <see cref="Health.OnDeath"/>만 구독할 뿐,
/// 사망 상태 전이(<see cref="CharacterState.Dead"/>)와 몸 정지는 각각 Health와 Actor가 맡는다.
///
/// [붙이는 곳] 적 루트(Health와 같은 오브젝트). 플레이어에는 붙이지 않는다 — 플레이어 사망은
/// 게임오버 처리로 이어져야지 오브젝트가 사라지면 안 된다.
/// </summary>
[RequireComponent(typeof(Health))]
public class EnemyDespawn : MonoBehaviour
{
    [KoreanLabel("소멸까지 시간(초)")]
    [Tooltip("죽고 나서 이 시간이 지나면 GameObject를 파괴한다. 이 동안 적은 넉다운 포즈로 쓰러져 있다.")]
    [Min(0f)]
    public float despawnDelay = 2f;

    Health health;

    void Awake()
    {
        health = GetComponent<Health>();
        health.OnDeath += HandleDeath;
    }

    void OnDestroy()
    {
        if (health != null)
            health.OnDeath -= HandleDeath;
    }

    void HandleDeath() => Destroy(gameObject, despawnDelay);
}
