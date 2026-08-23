using UnityEngine;

/// <summary>
/// TEMP: AI 없이 공격 기능 자체(판정/애니메이션 연결)만 테스트하기 위한 임시 IEnemyBrain 구현.
/// 이동/점프 의도는 절대 내지 않고, attackInterval마다 한 번씩 WantsAttack만 true로 반환한다.
/// 테스트가 끝나면 이 스크립트 파일 삭제 + 씬에서 컴포넌트만 떼어내면 된다 (EnemyController는 안 건드림).
///
/// [준비물] EnemyController와 같은 오브젝트에 붙일 것.
/// </summary>
[RequireComponent(typeof(EnemyController))]
public class TestAutoAttackBrain : MonoBehaviour, IEnemyBrain
{
    [KoreanLabel("공격 명령 주기(초)")]
    public float attackInterval = 3f;

    [KoreanLabel("표시 위치 오프셋")]
    public Vector3 displayOffset = Vector3.up * 3f;

    float timer;

    public EnemyIntent Think(EnemyController owner, float deltaTime)
    {
        timer += deltaTime;

        if (timer < attackInterval)
            return EnemyIntent.None;

        timer = 0f;

        EnemyIntent intent = EnemyIntent.None;
        intent.WantsAttack = true;
        return intent;
    }

    void OnGUI()
    {
        if (Camera.main == null) return;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position + displayOffset);
        if (screenPos.z <= 0f) return; // 카메라 뒤에 있으면 표시 안 함

        float remaining = Mathf.Max(0f, attackInterval - timer);
        GUI.Label(new Rect(screenPos.x - 50f, Screen.height - screenPos.y, 100f, 20f), $"공격까지 {remaining:F1}s");
    }
}
