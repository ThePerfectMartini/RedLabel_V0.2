using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CharacterStateMachine과 Animator를 이어주는 브릿지.
/// 게임 로직(상태)과 표현(애니메이션)을 분리하기 위해, 상태가 바뀔 때마다
/// 해당 상태 이름과 같은 이름의 Animator State로 CrossFade만 해준다.
/// IStateMachineOwner를 구현한 컴포넌트(CharacterControllerBase 파생 클래스)와
/// 같은 오브젝트에 붙이면 캐릭터 종류와 무관하게 동일하게 작동한다.
///
/// [준비물]
/// - Animator 컴포넌트 (이 오브젝트 또는 자식 오브젝트에 있어도 됨)
/// - Animator Controller의 State 이름이 CharacterState enum 값과 정확히 일치해야 함
///   (Idle, Move, Attack, JumpStart, InAir, JumpLand, Stun, Airborne, Landed, GetUp)
/// - JumpStart/InAir/JumpLand는 각각 독립된 State + 클립이어야 한다. 공중에서 공격이 끼어들면
///   Attack 클립이 재생 중이던 점프 클립을 덮어쓸 수 있는데, 셋이 클립을 공유하고 있으면
///   착지 시점에 클립이 처음부터 다시 재생되면서 점프 시작 이벤트가 또 호출되어 착지하자마자
///   재점프하는 문제가 생긴다. 반드시 세 클립으로 분리할 것.
/// - Landed/GetUp도 같은 이유로 독립된 State + 클립이어야 하며, KnockdownAnimationEventReceiver를
///   Animator와 같은 오브젝트에 붙이고 Landed 클립에 "OnKnockdownGetUpStartFrame",
///   GetUp 클립에 "OnKnockdownGetUpEndFrame" Animation Event를 걸어야 한다.
/// - 공격 State는 이름을 CharacterState로 맞추는 대신, 공격마다 AnimationClip과 같은 이름의
///   State를 만들어야 함 (IAttackClipSource 참고 — 콤보 단계별로 다른 클립을 재생하기 위함)
/// - State 사이에 화살표(Transition)는 필요 없음. 전환은 전부 이 스크립트가 코드로 강제한다.
/// </summary>
public class CharacterAnimatorBridge : MonoBehaviour
{
    [KoreanLabel("전환 블렌드 시간(초)")]
    [Tooltip("한 상태 애니메이션에서 다음 상태 애니메이션으로 섞이는 데 걸리는 시간(초). " +
             "나가는 클립의 길이와 무관하게 항상 이 초만큼 블렌드된다.")]
    public float transitionDuration = 0.1f;

    Animator animator;
    IStateMachineOwner stateOwner;
    IAttackClipSource attackClipSource;
    readonly Dictionary<CharacterState, int> stateHashes = new Dictionary<CharacterState, int>();

    // "Animator에 그 State가 없다"를 이미 경고한 대상. CrossFade는 대상이 없어도 조용히 실패하는데,
    // 이건 매 프레임 상태가 바뀔 때마다 반복되므로 한 번만 찍고 만다.
    // (실제로 적 Animator에 Move State가 없어서 추적 내내 Idle 자세로 미끄러지는 것을 한참 못 알아챘다.)
    readonly HashSet<CharacterState> warnedMissingStates = new HashSet<CharacterState>();
    readonly HashSet<string> warnedMissingClipStates = new HashSet<string>();

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        stateOwner = GetComponent<IStateMachineOwner>();
        attackClipSource = GetComponent<IAttackClipSource>();

        if (animator == null)
            Debug.LogWarning($"{name}: Animator를 찾지 못해 애니메이션을 재생할 수 없습니다.");
        if (stateOwner == null)
            Debug.LogWarning($"{name}: IStateMachineOwner를 구현한 컴포넌트가 없어 상태 변화를 감지할 수 없습니다.");
        if (attackClipSource == null)
            Debug.LogWarning($"{name}: IAttackClipSource를 구현한 컴포넌트가 없어 공격별 애니메이션 클립을 재생할 수 없습니다.");

        foreach (CharacterState state in Enum.GetValues(typeof(CharacterState)))
            stateHashes[state] = Animator.StringToHash(state.ToString());
    }

    void OnEnable()
    {
        if (stateOwner != null)
            stateOwner.StateMachine.OnStateChanged += HandleStateChanged;
        if (attackClipSource != null)
            attackClipSource.OnAttackClipChanged += HandleAttackClipChanged;
    }

    void OnDisable()
    {
        if (stateOwner != null)
            stateOwner.StateMachine.OnStateChanged -= HandleStateChanged;
        if (attackClipSource != null)
            attackClipSource.OnAttackClipChanged -= HandleAttackClipChanged;
    }

    void HandleStateChanged(CharacterState previous, CharacterState next)
    {
        // Attack은 HandleAttackClipChanged가 실제 클립으로 CrossFade를 대신 처리하므로 여기서는 건너뛴다.
        if (next == CharacterState.Attack) return;

        if (animator == null) return;

        int hash = stateHashes[next];

        // Animator에 해당 State가 없으면 CrossFade는 아무 일도 하지 않고 조용히 넘어간다.
        // 그러면 이전 클립이 그대로 재생되어 "움직이는데 Idle 자세" 같은 증상이 되므로 원인을 찍어준다.
        if (!animator.HasState(0, hash))
        {
            if (warnedMissingStates.Add(next))
                Debug.LogWarning($"{name}: Animator에 '{next}' State가 없어 애니메이션이 바뀌지 않습니다. " +
                    "State 이름은 CharacterState enum 값과 정확히 같아야 합니다.");
            return;
        }

        // CrossFade가 아니라 CrossFadeInFixedTime을 쓰는 이유:
        // CrossFade(hash, duration)의 duration은 초가 아니라 "지금 재생 중인 클립 길이에 대한 비율"이다.
        // 그대로 두면 0.27초짜리 공격 클립에서 나갈 땐 0.027초, 1초짜리 Idle에서 나갈 땐 0.1초로
        // 블렌드 시간이 제각각이 된다. 초 단위로 고정해야 전환 느낌이 일정하다.
        animator.CrossFadeInFixedTime(hash, transitionDuration);
    }

    /// <summary>
    /// 공격 시작/콤보 진행 시 호출된다. State 이름 대신 클립 이름으로 CrossFade해서
    /// 콤보 단계마다 다른 클립을 재생할 수 있게 한다 (Animator State 이름 == 클립 이름이어야 함).
    /// </summary>
    void HandleAttackClipChanged(AnimationClip clip)
    {
        if (animator == null || clip == null) return;

        // 공격은 State 이름이 클립 이름과 같아야 한다. 같은 이유로 없으면 조용히 실패하므로 확인한다.
        int hash = Animator.StringToHash(clip.name);
        if (!animator.HasState(0, hash))
        {
            if (warnedMissingClipStates.Add(clip.name))
                Debug.LogWarning($"{name}: Animator에 '{clip.name}' State가 없어 공격 애니메이션이 재생되지 않습니다. " +
                    "공격 State는 이름을 AnimationClip 이름과 똑같이 맞춰야 합니다.");
            return;
        }

        animator.CrossFadeInFixedTime(hash, transitionDuration);
    }
}
