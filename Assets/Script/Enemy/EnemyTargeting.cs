using UnityEngine;

/// <summary>
/// 적이 겨냥할 대상 하나를 물고 있는 컴포넌트. 지금은 "씬의 플레이어"를 자동으로 찾는 게 전부지만,
/// 이후 오빗 이동 모듈 · 의사결정 노드 · 스쿼드 컨트롤러가 전부 여기서 대상 위치/거리를 읽는다.
/// 그래서 <see cref="EnemyBrain"/>과 분리한다 — 브레인을 갈아엎어도 타깃 획득은 그대로다.
///
/// [붙이는 곳] 적 루트(Actor와 같은 오브젝트).
/// [가정] 플레이어는 하나뿐이고 <see cref="PlayerInput"/>을 가진다. 씬 배치 플레이어는 Start 시점에
///        이미 존재하므로 1회 탐색으로 충분하다. 런타임 리스폰이 생기면 <see cref="Reacquire"/>를 부른다.
/// </summary>
public class EnemyTargeting : MonoBehaviour
{
    [KoreanLabel("대상 지정 (선택)")]
    [Tooltip("지정하면 플레이어 대신 이 대상을 겨냥한다(다른 적/오브젝트 겨냥 테스트용). " +
             "비워두면 씬에서 PlayerInput을 가진 오브젝트를 자동으로 찾는다.")]
    public Transform overrideTarget;

    Transform target;

    /// <summary>지금 겨냥 중인 대상이 있는지. 파괴된 대상은 자동으로 없는 것으로 처리된다.</summary>
    public bool HasTarget => target != null;

    /// <summary>겨냥 중인 대상의 Transform. 없으면 null.</summary>
    public Transform Target => target;

    /// <summary>이 적에서 대상으로 향하는 벡터(3D). 대상이 없으면 Vector3.zero.</summary>
    public Vector3 ToTarget => target != null ? target.position - transform.position : Vector3.zero;

    /// <summary>벨트스크롤 평면(XZ) 기준 대상까지의 거리. 대상이 없으면 0.</summary>
    public float Distance
    {
        get
        {
            if (target == null) return 0f;
            Vector3 d = target.position - transform.position;
            return new Vector2(d.x, d.z).magnitude;
        }
    }

    void Start() => Reacquire();

    /// <summary>대상을 다시 찾는다. overrideTarget이 있으면 그것, 없으면 씬의 플레이어.</summary>
    public void Reacquire()
    {
        if (overrideTarget != null)
        {
            target = overrideTarget;
            return;
        }

        PlayerInput player = FindFirstObjectByType<PlayerInput>();
        target = player != null ? player.transform : null;

        if (target == null)
            Debug.LogWarning($"{name}: 씬에서 플레이어(PlayerInput)를 찾지 못했습니다.");
    }

    void OnDrawGizmosSelected()
    {
        if (target == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, target.position);
    }
}
