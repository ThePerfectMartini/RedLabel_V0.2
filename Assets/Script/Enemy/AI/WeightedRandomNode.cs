using System;
using UnityEngine;

/// <summary>
/// 자식 중 하나를 <b>가중치 랜덤</b>으로 골라 끝까지 돌리는 합성 노드.
/// 설계서 8장의 랜덤은 세 군데뿐이고(공격 후 2택 · 행동 3택 · 대기 시간 범위), 앞의 둘이 이것이다.
///
/// 가중치를 숫자가 아니라 <see cref="Func{T}"/>로 받는 이유: 값을 생성자에서 한 번 읽어두면
/// 플레이 중 인스펙터로 가중치를 바꿔도 반영되지 않는다. 수치를 굴려가며 맞추는 것이 목적인 값이라
/// 매번 에셋에서 다시 읽어야 한다.
///
/// 고른 자식이 Success든 Failure든 그 결과를 그대로 위로 올린다 — 여기서 다시 고르지 않는다.
/// 다시 고르는 것은 트리의 루프가 할 일이지 이 노드의 일이 아니다.
/// </summary>
public class WeightedRandomNode : BTNode
{
    /// <summary>후보 하나. 가중치가 0 이하면 이번 추첨에서 빠진다.</summary>
    public readonly struct Option
    {
        public readonly Func<float> Weight;
        public readonly BTNode Node;

        public Option(Func<float> weight, BTNode node)
        {
            Weight = weight;
            Node = node;
        }
    }

    readonly Option[] options;
    BTNode chosen;

    public WeightedRandomNode(params Option[] options)
    {
        this.options = options;
    }

    public override BTNode ActiveChild => chosen;

    protected override void OnEnter(MeleeEnemyContext context)
    {
        chosen = Pick();
    }

    protected override BTStatus OnTick(MeleeEnemyContext context, float deltaTime)
    {
        if (chosen == null) return BTStatus.Failure;
        return chosen.Tick(context, deltaTime);
    }

    /// <summary>끊길 때 고른 자식도 같이 끊는다. 이미 끝났으면 Abort는 아무 일도 하지 않는다.</summary>
    protected override void OnExit(MeleeEnemyContext context)
    {
        if (chosen != null)
            chosen.Abort(context);
    }

    BTNode Pick()
    {
        float total = 0f;
        for (int i = 0; i < options.Length; i++)
            total += Mathf.Max(0f, options[i].Weight());

        if (total <= 0f)
        {
            WarnAllZero();
            return null;
        }

        // using System과 using UnityEngine이 같이 있어 Random은 모호하다. 명시적으로 적는다.
        float roll = UnityEngine.Random.value * total;
        for (int i = 0; i < options.Length; i++)
        {
            float weight = Mathf.Max(0f, options[i].Weight());
            if (weight <= 0f) continue;

            roll -= weight;
            if (roll <= 0f) return options[i].Node;
        }

        // 부동소수 오차로 마지막까지 못 고르는 경우. 가중치가 있는 마지막 후보로 떨어뜨린다.
        for (int i = options.Length - 1; i >= 0; i--)
        {
            if (options[i].Weight() > 0f) return options[i].Node;
        }

        return null;
    }

    static bool warnedAllZero;

    static void WarnAllZero()
    {
        if (warnedAllZero) return;

        warnedAllZero = true;
        Debug.LogWarning("가중치가 전부 0이라 행동을 고를 수 없습니다. AI 수치 에셋의 가중치를 확인하세요.");
    }
}
