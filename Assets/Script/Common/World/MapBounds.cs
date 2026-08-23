using UnityEngine;

/// <summary>
/// 맵 경계(벽 역할)를 정의하는 컴포넌트.
/// 맵 오브젝트(바닥 등)에 직접 붙이면, 그 오브젝트의 Renderer 크기로 경계를 자동 계산한다.
/// 씬에는 하나만 존재한다고 가정하고, 컨트롤러들은 인스펙터 연결 없이 MapBounds.Instance로 참조한다.
/// </summary>
public class MapBounds : MonoBehaviour
{
    [Tooltip("비워두면(0,0,0) 이 오브젝트와 자식들의 Renderer 크기를 합쳐서 자동 계산")]
    public Vector3 manualSize = Vector3.zero;

    static MapBounds instance;

    /// <summary>
    /// 씬 안의 MapBounds를 찾아 캐싱해서 반환.
    /// Awake 실행 순서에 의존하지 않도록 첫 접근 시점에 지연 탐색한다.
    /// </summary>
    public static MapBounds Instance
    {
        get
        {
            if (instance == null)
                instance = FindFirstObjectByType<MapBounds>();
            return instance;
        }
    }

    Bounds cachedBounds;

    void Awake()
    {
        if (instance != null && instance != this)
            Debug.LogWarning($"씬에 MapBounds가 여러 개 있습니다. '{instance.name}'을 계속 사용합니다.");
        else
            instance = this;

        cachedBounds = CalculateBounds();
    }

    public Bounds Bounds => cachedBounds;

    Bounds CalculateBounds()
    {
        if (manualSize != Vector3.zero)
            return new Bounds(transform.position, manualSize);

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b;
        }

        Debug.LogWarning($"{name}: 크기를 계산할 Renderer가 없어 기본값(20,10,20)을 사용합니다. manualSize를 지정하세요.");
        return new Bounds(transform.position, new Vector3(20f, 10f, 20f));
    }

    void OnDrawGizmosSelected()
    {
        Bounds b = Application.isPlaying ? cachedBounds : CalculateBounds();
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(b.center, b.size);
    }
}