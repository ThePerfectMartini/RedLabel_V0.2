using UnityEngine;

/// <summary>
/// <b>임시 테스트용 적 AI.</b> 제자리에 서서 일정 간격으로 공격만 한다 — 플레이어의 회피/피격/넉백을
/// 상대할 표적이 필요해서 만든 최소 구현이다. 추격·거리 판단·패턴 같은 진짜 AI는 나중에 이 파일을
/// 갈아엎으며 들어온다(<see cref="IIntentSource"/> 계약만 지키면 Actor는 건드릴 필요 없다).
///
/// [붙이는 곳] Enemy 루트(Actor와 같은 오브젝트). Actor가 <c>GetComponent&lt;IIntentSource&gt;()</c>로 찾는다.
/// [필요] <see cref="attackData"/>에 적 전용 AttackData 에셋 하나. 없으면 공격을 시도하지 않는다.
/// </summary>
public class EnemyBrain : MonoBehaviour, IIntentSource
{
    [KoreanLabel("공격 데이터")]
    [Tooltip("이 적이 반복할 공격 하나. 비워두면 아무 공격도 안 한다.")]
    public AttackData attackData;

    [KoreanLabel("공격 간격(초)")]
    [Min(0.1f)]
    public float attackInterval = 3f;

    [KoreanLabel("바라볼 대상 (선택)")]
    [Tooltip("지정하면 매 프레임 이 대상 쪽을 바라본다(제자리에서 좌우만). 비워두면 스폰 시 방향 유지. " +
             "플레이어가 뒤로 돌아가도 공격이 맞게 하려면 Player를 넣을 것.")]
    public Transform faceTarget;

    float nextAttackTime;

    void Start()
    {
        // 스폰 직후 곧바로 때리지 않게 한 박자 뒤부터.
        nextAttackTime = Time.time + attackInterval;
    }

    public CharacterIntent GetIntent(float deltaTime)
    {
        CharacterIntent intent = CharacterIntent.None; // MoveInput = 0 → 제자리

        if (faceTarget != null)
            intent.FacingDirection = new Vector3(faceTarget.position.x - transform.position.x, 0f, 0f);

        if (attackData != null && Time.time >= nextAttackTime)
        {
            intent.AttackToStart = attackData;
            nextAttackTime = Time.time + attackInterval;
        }

        return intent;
    }
}
