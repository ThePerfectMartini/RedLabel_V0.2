using System.Collections.Generic;
using UnityEngine;

/// <summary>캐릭터(플레이어/적 공용)의 현재 상태. 논리(행동 가능 여부)와 표현(애니메이션)을 동시에 구동한다.</summary>
public enum CharacterState
{
    // --- 물리 파생: 매 프레임 Locomotion 상태에서 다시 계산된다 ---
    Idle,
    Move,
    InAir,
    Stun,      // 가벼운 피격 — 그라운드 슬라이드 중
    Airborne,  // 강한 피격 — 넉백으로 공중에 뜸

    // --- 고정(sticky): 명시적 호출/애니메이션 이벤트로만 빠져나간다 ---
    JumpStart, // 점프 준비. OnJumpLaunchFrame에서 탈출
    JumpLand,  // 착지 경직. OnJumpLandEndFrame에서 탈출
    Attack,    // 공격 재생. Fighter의 타이머로 스스로 탈출 (워치독 대상 아님)
    Landed,    // 넉백으로 쓰러짐. OnKnockdownGetUpStartFrame에서 탈출
    GetUp,     // 일어나는 중. OnKnockdownGetUpEndFrame에서 탈출
}

/// <summary>
/// 상태 하나로 <b>논리 상태</b>(CanAct / IsMovementLocked)와 <b>애니메이션</b>(상태 이름 == 클립 이름으로 CrossFade,
/// 스프라이트 좌우 반전)을 동시에 굴린다. 예전엔 ActionPhase(논리) + CharacterState(표현) 두 개를 두고
/// 매 프레임 "phase → 같은 이름 state"로 옮겨적었는데, 그 번역 사다리를 없앤 것이 이 통합의 핵심이다.
///
/// 애니메이션 이벤트로만 끝나는 고정 상태(JumpStart/JumpLand/Landed/GetUp)는 이벤트가 빠지면 캐릭터가
/// 영구히 잠기므로 워치독으로 강제 해제한다. Attack은 Fighter의 타이머로 자력 종료하므로 제외.
///
/// [준비물] 자식(또는 자신)에 Animator + SpriteRenderer. Animator State 이름은 CharacterState 값 및
///          각 공격 클립 이름과 정확히 일치해야 한다. State 사이 Transition 화살표는 필요 없다(전부 코드로 CrossFade).
/// </summary>
[RequireComponent(typeof(Locomotion))]
public class CharacterStateMachine : MonoBehaviour
{
    const float MoveEpsilon = 0.1f; // 이 속력 이상이면 Move로 본다

    [KoreanLabel("전환 블렌드 시간(초)")]
    [Tooltip("스프라이트 교체 애니메이션은 프레임 사이를 보간할 수 없다. 0보다 크게 두면 전환 구간 동안 " +
             "두 클립의 프레임이 번갈아 보여 깜빡거린다(특히 loop 클립). 스프라이트 기반이면 0으로 둘 것.")]
    public float transitionDuration = 0f;

    [KoreanLabel("동작 최대 지속시간(초)")]
    [Tooltip("점프 준비 / 착지 경직 / 다운 / 기상은 Animation Event가 도착해야 끝난다. 클립에 이벤트가 빠졌거나 " +
             "도중에 교체되면 그 이벤트가 영영 안 오고 캐릭터가 영구히 잠긴다. 이 시간이 지나도 이벤트가 없으면 " +
             "강제로 해제하고 Console에 경고를 남긴다. 정상 상태에서는 절대 발동하지 않아야 하는 값.")]
    public float actionPhaseTimeout = 2f;

    public CharacterState CurrentState { get; private set; } = CharacterState.Idle;

    /// <summary>새 행동(이동/공격/점프)을 시작할 수 있는 상태인지. 고정 상태도 아니고 얻어맞은 여파도 없어야 한다.</summary>
    public bool CanAct => !IsSticky(CurrentState)
        && CurrentState != CharacterState.Stun
        && CurrentState != CharacterState.Airborne;

    /// <summary>이동 의도를 무시해야 하는 상태인지. Attack은 여기 없다 — 공격이 이동을 막는지는 Fighter가 판단한다.</summary>
    public bool IsMovementLocked =>
        CurrentState == CharacterState.JumpStart ||
        CurrentState == CharacterState.JumpLand ||
        CurrentState == CharacterState.Landed ||
        CurrentState == CharacterState.GetUp ||
        CurrentState == CharacterState.Stun ||
        CurrentState == CharacterState.Airborne;

    Locomotion locomotion;
    Animator animator;
    SpriteRenderer spriteRenderer;

    float stickyElapsed;

    // "Animator에 그 State가 없다"를 이미 경고한 대상. CrossFade는 대상이 없어도 조용히 실패하므로 한 번만 찍는다.
    readonly HashSet<string> warnedMissing = new HashSet<string>();

    static bool IsSticky(CharacterState s) =>
        s == CharacterState.JumpStart || s == CharacterState.JumpLand ||
        s == CharacterState.Attack || s == CharacterState.Landed || s == CharacterState.GetUp;

    void Awake()
    {
        locomotion = GetComponent<Locomotion>();
        animator = GetComponentInChildren<Animator>();
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (animator == null)
            Debug.LogWarning($"{name}: Animator를 찾지 못해 애니메이션을 재생할 수 없습니다.");
        if (spriteRenderer == null)
            Debug.LogWarning($"{name}: SpriteRenderer가 없어 좌우 반전이 적용되지 않습니다. (아직 프리미티브면 정상)");
    }

    // ===== Actor / Fighter / AnimationEventRelay가 부르는 진입점 =====

    /// <summary>Fighter가 공격을 시작/콤보 진행할 때 호출. 상태는 Attack, 애니메이션은 콤보 단계별 클립 이름으로 CrossFade.</summary>
    public void EnterAttack(AnimationClip clip)
    {
        SetState(CharacterState.Attack); // 이미 Attack이면 상태는 그대로, 아래에서 클립만 다시 CrossFade
        if (clip != null)
            CrossFade(clip.name);
    }

    /// <summary>Fighter가 콤보를 끝낼 때 호출. 물리 파생 상태로 되돌린다.</summary>
    public void ExitAttack()
    {
        if (CurrentState == CharacterState.Attack)
            SetState(DerivePhysicsState());
    }

    public void EnterJumpStart() => SetState(CharacterState.JumpStart);
    public void EnterJumpLand() => SetState(CharacterState.JumpLand);
    public void EnterLanded() => SetState(CharacterState.Landed);

    /// <summary>
    /// 피격 시 Health가 호출. 지금이 어떤 고정 상태든 상관없이 즉시 물리 파생 상태로 되돌린다.
    /// <b>어떤 상태였는지 분기하지 않는 것이 핵심</b> — 예전엔 피격 쪽이 플래그를 하나씩 껐고 하나라도
    /// 빠뜨리면 그 동작의 Animation Event가 영영 안 와서 이동이 영구히 잠겼다.
    /// </summary>
    public void Interrupt() => SetState(DerivePhysicsState());

    // 애니메이션 이벤트(AnimationEventRelay 경유). 엉뚱한 이벤트가 무관한 동작을 망가뜨리지 않게 상태를 가드한다.

    public void OnJumpLaunchFrame()
    {
        if (CurrentState != CharacterState.JumpStart) return;
        locomotion.Jump();               // 여기서 실제로 뜬다
        SetState(DerivePhysicsState());  // 뜬 직후엔 InAir로 파생
    }

    public void OnJumpLandEndFrame()
    {
        if (CurrentState != CharacterState.JumpLand) return;
        SetState(DerivePhysicsState());
    }

    public void OnKnockdownGetUpStartFrame()
    {
        if (CurrentState != CharacterState.Landed) return;
        SetState(CharacterState.GetUp);
    }

    public void OnKnockdownGetUpEndFrame()
    {
        if (CurrentState != CharacterState.GetUp) return;
        SetState(DerivePhysicsState());
    }

    /// <summary>Actor가 매 프레임 마지막에 호출. 워치독 → (고정 상태가 아니면) 물리에서 상태 재판정 → 스프라이트 동기화.</summary>
    public void Evaluate()
    {
        TickWatchdog();

        if (!IsSticky(CurrentState))
            SetState(DerivePhysicsState());

        if (spriteRenderer != null)
            spriteRenderer.flipX = !locomotion.FacingRight;
    }

    // ===== 내부 =====

    CharacterState DerivePhysicsState()
    {
        if (locomotion.IsKnockedBackAirborne) return CharacterState.Airborne;
        if (locomotion.IsGroundSliding)       return CharacterState.Stun;
        if (!locomotion.IsGrounded)            return CharacterState.InAir;
        if (locomotion.HorizontalSpeed > MoveEpsilon) return CharacterState.Move;
        return CharacterState.Idle;
    }

    void SetState(CharacterState next)
    {
        if (next == CurrentState) return;

        CurrentState = next;
        stickyElapsed = 0f;

        // Attack은 EnterAttack이 클립 이름으로 직접 CrossFade한다(콤보 단계별로 클립이 다르므로).
        if (next != CharacterState.Attack)
            CrossFade(next.ToString());
    }

    void CrossFade(string stateName)
    {
        if (animator == null) return;

        int hash = Animator.StringToHash(stateName);
        if (!animator.HasState(0, hash))
        {
            if (warnedMissing.Add(stateName))
                Debug.LogWarning($"{name}: Animator에 '{stateName}' State가 없어 애니메이션이 바뀌지 않습니다. " +
                    "State 이름은 CharacterState 값 / 공격 클립 이름과 정확히 같아야 합니다.");
            return;
        }

        if (transitionDuration > 0f)
        {
            // CrossFade가 아니라 CrossFadeInFixedTime: CrossFade의 duration은 "초"가 아니라 "현재 클립 길이에 대한 비율"이라
            // 나가는 클립에 따라 블렌드 시간이 제각각이 된다. 초 단위로 고정해야 전환 느낌이 일정하다.
            animator.CrossFadeInFixedTime(hash, transitionDuration);
        }
        else
        {
            // 블렌드 시간 0 = 하드 컷. CrossFadeInFixedTime(_, 0)은 1프레임짜리 전환 상태를 남겨
            // 스프라이트 애니메이션에서 나가는 클립 프레임이 한 번 더 보이는 원인이 되므로 Play로 즉시 전환한다.
            animator.Play(hash, 0, 0f);
        }
    }

    void TickWatchdog()
    {
        // Attack은 Fighter 타이머로 자력 종료하므로 감시 대상이 아니다.
        if (!IsSticky(CurrentState) || CurrentState == CharacterState.Attack)
        {
            stickyElapsed = 0f;
            return;
        }
        if (actionPhaseTimeout <= 0f) return; // 0 이하면 워치독 끈 것으로 본다

        stickyElapsed += Time.deltaTime;
        if (stickyElapsed < actionPhaseTimeout) return;

        Debug.LogWarning($"{name}: {CurrentState} 상태가 {actionPhaseTimeout}초 동안 끝나지 않아 강제로 해제합니다. " +
            $"'{ExpectedEvent(CurrentState)}' Animation Event가 해당 클립에 있는지 확인하세요.");

        // 다운 중 갇혔으면 곧장 조작 가능으로 두는 대신 기상 동작을 거치게 한다(GetUp마저 이벤트가 없으면 다음 워치독이 푼다).
        SetState(CurrentState == CharacterState.Landed ? CharacterState.GetUp : DerivePhysicsState());
    }

    static string ExpectedEvent(CharacterState s)
    {
        switch (s)
        {
            case CharacterState.JumpStart: return nameof(OnJumpLaunchFrame);
            case CharacterState.JumpLand:  return nameof(OnJumpLandEndFrame);
            case CharacterState.Landed:    return nameof(OnKnockdownGetUpStartFrame);
            case CharacterState.GetUp:     return nameof(OnKnockdownGetUpEndFrame);
            default:                       return "(없음)";
        }
    }
}
