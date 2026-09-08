using UnityEngine;

/// <summary>
/// 적의 "성향(니치)" — 같은 행동 목록·같은 셸을 쓰면서 셀렉터 확률 가중치만 다르게 줘 성격이 다른 적을 만든다
/// (지침서 3장). 예) 공격적: 접근·공격↑ / 유인형: 후퇴·대기↑ / 관망형: 대기·dry-fire↑ / 돌격형: 접근·공격↑ dry-fire↓.
///
/// <see cref="EnemyBrain"/>의 "성향" 필드에 지정하면 Awake에서 각 행동의 selectionWeight와 셀렉터의
/// dryFireWeight를 이 값으로 덮어쓴다. 비워두면 EnemyBrain 인스펙터에 직접 넣은 값을 그대로 쓴다.
/// </summary>
[CreateAssetMenu(fileName = "EnemyArchetype", menuName = "DoitMySelf/Enemy Archetype")]
public class EnemyArchetype : ScriptableObject
{
    [KoreanLabel("접근 가중치")]
    [Min(0f)]
    public float approachWeight = 1f;

    [KoreanLabel("대기 가중치")]
    [Min(0f)]
    public float waitWeight = 1f;

    [KoreanLabel("후퇴 가중치")]
    [Min(0f)]
    public float retreatWeight = 1f;

    [KoreanLabel("공격 가중치")]
    [Min(0f)]
    public float attackWeight = 1f;

    [KoreanLabel("dry-fire 가중치")]
    [Min(0f)]
    public float dryFireWeight = 1f;
}
