using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 후보 행동들 사이에서 <see cref="EnemyBehavior.selectionWeight"/>에 비례해 하나를 무작위로 고른다.
/// <see cref="dryFireWeight"/> 몫이 뽑히면 null을 돌려준다 — "헛방(dry-fire)". 셸은 이때 기본 행동으로 계속 간다.
///
/// 지침서 8.5: 단순 우선순위 셀렉터 대신 확률 가중치 셀렉터를 하나 만들어 모든 적이 재사용한다.
/// 적 성향(공격적/유인형/관망형/돌격형)은 이 셀렉터가 아니라 각 행동의 selectionWeight 분포로 낸다.
/// </summary>
[System.Serializable]
public class WeightedBehaviorSelector
{
    [KoreanLabel("dry-fire 가중치")]
    [Min(0f)]
    [Tooltip("이 몫이 뽑히면 아무 행동도 고르지 않는다(null). 패턴을 흐리는 '헛방' — 적이 계속 접근만 하는 순간.")]
    public float dryFireWeight = 1f;

    /// <summary>가중치에 비례해 후보 하나를 고른다. dry-fire거나 뽑을 후보가 없으면 null.</summary>
    public EnemyBehavior Select(IReadOnlyList<EnemyBehavior> candidates)
    {
        float total = dryFireWeight;
        for (int i = 0; i < candidates.Count; i++)
            if (candidates[i].CanBeSelected())
                total += candidates[i].selectionWeight;

        if (total <= 0f)
            return null;

        float roll = Random.value * total;

        if (roll < dryFireWeight)
            return null;
        roll -= dryFireWeight;

        for (int i = 0; i < candidates.Count; i++)
        {
            EnemyBehavior b = candidates[i];
            if (!b.CanBeSelected())
                continue;
            if (roll < b.selectionWeight)
                return b;
            roll -= b.selectionWeight;
        }

        return null; // 부동소수 오차로 드물게 도달 — dry-fire로 처리
    }
}
