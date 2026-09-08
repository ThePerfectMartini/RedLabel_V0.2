using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 적 AI의 행동 하나(지침서의 "리프 노드"). <see cref="EnemyBrain"/>이 한 번에 하나만 활성화하고
/// 매 프레임 <see cref="Tick"/>한다.
///
/// 모든 행동은 4단계 위상을 거친다 — 베이스가 관리하고, 서브클래스는 <see cref="OnTick"/>(Active 구간)만 채운다.
///   Startup  : 진입 후 startupDuration 동안 정지 + 대상 주시 (예비 동작)
///   Active   : minActiveDuration ~ maxActiveDuration. 실제 행동. WantsToExit가 true면 min 이후 조기 종료
///   Recovery : recoveryDuration 동안 정지 + 대상 주시 (회복 프레임 / punish 창)
///   Done     : 셸이 다음 행동으로 전이
///
/// maxActiveDuration이 있어 <see cref="WantsToExit"/>가 영영 안 떠도 반드시 끝난다 —
/// "조건 대기만으로 끝나는 노드"가 구조적으로 불가능.
/// </summary>
[System.Serializable]
public abstract class EnemyBehavior
{
    public enum Phase { Startup, Active, Recovery, Done }

    [KoreanLabel("선택 가중치")]
    [Min(0f)]
    [Tooltip("셀렉터가 이 행동을 뽑을 확률의 상대적 무게. 0이면 절대 안 뽑힌다. 적 성향(니치)은 이 값들의 분포로 낸다.")]
    public float selectionWeight = 1f;

    [KoreanLabel("예비 동작(초)")]
    [Min(0f)]
    [Tooltip("진입 후 이 시간 동안 정지한 채 대상만 주시한다(격투 게임 기술의 예비 동작).")]
    public float startupDuration = 0.1f;

    [KoreanLabel("최소 지속(초)")]
    [Min(0f)]
    [FormerlySerializedAs("minDuration")]
    [Tooltip("Active 구간의 최소 시간. 이 전에는 WantsToExit가 true여도 안 끝난다.")]
    public float minActiveDuration = 0.3f;

    [KoreanLabel("최대 지속(초)")]
    [Min(0.1f)]
    [FormerlySerializedAs("maxDuration")]
    [Tooltip("Active 구간의 최대 시간. 넘기면 조건과 무관하게 회복 단계로. 조건 대기만으로 끝나는 노드를 막는 안전장치.")]
    public float maxActiveDuration = 2.5f;

    [KoreanLabel("회복 프레임(초)")]
    [Min(0f)]
    [Tooltip("Active가 끝난 뒤 이 시간 동안 정지한 채 재결정을 막는다(후딜).")]
    public float recoveryDuration = 0.15f;

    protected EnemyBrain Brain { get; private set; }

    Phase phase;
    float phaseElapsed;
    bool aborting;

    public Phase CurrentPhase => phase;
    public float PhaseElapsed => phaseElapsed;
    public bool IsDone => phase == Phase.Done;

    public void Bind(EnemyBrain brain) => Brain = brain;

    /// <summary>지금 이 행동을 후보로 삼을 수 있는지(조건 체크). 기본 true. 거리·체력 등으로 서브클래스가 좁힌다.</summary>
    public virtual bool CanBeSelected() => true;

    public void Enter()
    {
        phaseElapsed = 0f;
        aborting = false;
        phase = startupDuration > 0f ? Phase.Startup : Phase.Active;
        OnEnter();
    }

    /// <summary>이 행동이 제 일을 할 수 없다고 판단했을 때(예: 공격 슬롯 못 얻음) 즉시 Done으로. OnEnter/OnTick에서 호출.</summary>
    protected void Abort() => aborting = true;

    public CharacterIntent Tick(float deltaTime)
    {
        phaseElapsed += deltaTime;

        if (aborting && phase != Phase.Done)
        {
            AdvancePhase(Phase.Done);
            return NeutralIntent();
        }

        switch (phase)
        {
            case Phase.Startup:
                if (phaseElapsed >= startupDuration)
                    AdvancePhase(Phase.Active);
                return NeutralIntent();

            case Phase.Active:
            {
                CharacterIntent intent = OnTick(deltaTime);
                bool minPassed = phaseElapsed >= minActiveDuration;
                bool maxPassed = phaseElapsed >= maxActiveDuration;
                if (maxPassed || (minPassed && WantsToExit(phaseElapsed)))
                    AdvancePhase(recoveryDuration > 0f ? Phase.Recovery : Phase.Done);
                return intent;
            }

            case Phase.Recovery:
                if (phaseElapsed >= recoveryDuration)
                    AdvancePhase(Phase.Done);
                return NeutralIntent();

            default: // Done — 셸이 곧 전이시킨다
                return NeutralIntent();
        }
    }

    public void Exit() => OnExit();

    void AdvancePhase(Phase next)
    {
        phase = next;
        phaseElapsed = 0f;
    }

    /// <summary>정지 + 대상 주시. 예비 동작·회복·대상 없음일 때의 기본 의도.</summary>
    protected CharacterIntent NeutralIntent()
    {
        CharacterIntent intent = CharacterIntent.None;
        if (Brain.Targeting.HasTarget)
            intent.FacingDirection = new Vector3(Brain.Targeting.ToTarget.x, 0f, 0f);
        return intent;
    }

    /// <summary>행동 진입 첫 프레임. 파라미터·오빗 목표값을 여기서 세팅한다.</summary>
    protected virtual void OnEnter() { }

    /// <summary>Active 구간 매 프레임. 이 행동의 실제 CharacterIntent를 만든다.</summary>
    protected abstract CharacterIntent OnTick(float deltaTime);

    /// <summary>행동 종료. 남긴 상태를 되돌린다.</summary>
    protected virtual void OnExit() { }

    /// <summary>minActiveDuration이 지난 뒤, "이제 그만두고 싶냐". 기본 false(maxActiveDuration까지).</summary>
    protected virtual bool WantsToExit(float activeElapsed) => false;
}
