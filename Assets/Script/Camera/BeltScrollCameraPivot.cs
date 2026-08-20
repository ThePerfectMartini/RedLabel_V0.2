using UnityEngine;

public class BeltScrollCameraPivot : MonoBehaviour
{
    [Header("Target & Tracking")]
    [Tooltip("카메라가 따라갈 대상 (플레이어)")]
    public Transform target;
    [Tooltip("카메라 이동의 부드러운 정도")]
    public float smoothTime = 0.2f;

    [Header("Deadzone (안전지대)")]
    [Tooltip("X축(좌우) 데드존 크기")]
    public float deadzoneX = 2f;
    [Tooltip("Y축(높이) 데드존 크기. 일반 점프 높이보다 살짝 높게 설정하면 좋습니다.")]
    public float deadzoneY = 1.5f;
    [Tooltip("Z축(깊이) 데드존 크기")]
    public float deadzoneZ = 1f;

    [Header("Stage Limits")]
    public float minX = -100f;
    public float maxX = 100f;
    [Tooltip("카메라가 내려갈 수 있는 최하단 Y 좌표 (지하로 꺼지지 않게)")]
    public float minY = 0f;
    [Tooltip("카메라가 올라갈 수 있는 최상단 Y 좌표")]
    public float maxY = 15f;
    public float minZ = -10f;
    public float maxZ = 10f;

    [Header("Stage Limits")] public bool testGizmo;

    private Vector3 offset;
    private Vector3 currentVelocity = Vector3.zero;
    private Vector3 focusPoint;

    private void Start()
    {
        if (target != null)
        {
            // 게임 시작 시 피벗과 플레이어 사이의 초기 앵글/간격 저장
            offset = transform.position - target.position;
            focusPoint = target.position;
        }
    }

    private void LateUpdate()
    {
        if (!target) return;

        // 1. 데드존 로직: 3차원 축 모두 적용
        float deltaX = target.position.x - focusPoint.x;
        float deltaY = target.position.y - focusPoint.y;
        float deltaZ = target.position.z - focusPoint.z;

        // X축 갱신
        if (deltaX > deadzoneX) focusPoint.x = target.position.x - deadzoneX;
        else if (deltaX < -deadzoneX) focusPoint.x = target.position.x + deadzoneX;

        // Y축 갱신 (점프 시 살짝 따라가는 효과)
        if (deltaY > deadzoneY) focusPoint.y = target.position.y - deadzoneY;
        else if (deltaY < -deadzoneY) focusPoint.y = target.position.y + deadzoneY;

        // Z축 갱신
        if (deltaZ > deadzoneZ) focusPoint.z = target.position.z - deadzoneZ;
        else if (deltaZ < -deadzoneZ) focusPoint.z = target.position.z + deadzoneZ;

        // 2. 최종 목표 위치 계산
        Vector3 targetPosition = focusPoint + offset;

        // 3. 스테이지 제한 적용 (X, Y, Z 모두 제한)
        targetPosition.x = Mathf.Clamp(targetPosition.x, minX, maxX);
        targetPosition.y = Mathf.Clamp(targetPosition.y, minY, maxY);
        targetPosition.z = Mathf.Clamp(targetPosition.z, minZ, maxZ);

        // 4. SmoothDamp를 사용하여 목표 위치로 부드럽게 이동
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref currentVelocity, smoothTime);
    }

    // 유니티 에디터에서 3D 데드존 영역을 시각적으로 확인하기 위한 기즈모
    private void OnDrawGizmosSelected()
    {
        if (target != null && testGizmo)
        {
            Gizmos.color = new Color(0, 1, 0, 0.3f);
            Vector3 center = focusPoint != Vector3.zero ? focusPoint : target.position;
            // Y축 데드존까지 포함된 3D 큐브 형태로 기즈모를 그립니다.
            Gizmos.DrawCube(center, new Vector3(deadzoneX * 2, deadzoneY * 2, deadzoneZ * 2));
        }
    }
}