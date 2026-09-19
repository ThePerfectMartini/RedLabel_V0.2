using System.Collections.Generic;
using UnityEngine;

/// <summary>캐릭터(플레이어/적 공용)의 현재 상태. 논리(행동 가능 여부)와 표현(애니메이션)을 동시에 구동한다.</summary>
public enum CharacterState
{
    // --- 물리 파생: 매 프레임 Locomotion 상태에서 다시 계산된다 ---
    Idle,
    Move,
    InAir,
    Airborne,  // 강한 피격 — 넉백으로 공중에 뜸. 착지하면 Landed로 이어진다

    // --- 고정(sticky): 명시적 호출/애니메이션 이벤트로만 빠져나간다 ---
    Stun,         // 가벼운 피격 — 지상 경직. Stun 클립 길이만큼 유지되고 스스로 탈출 (워치독 대상 아님)
    JumpStart,    // 점프 준비. OnJumpLaunchFrame에서 탈출
    JumpLand,     // 착지 경직. OnJumpLandEndFrame에서 탈출
    Attack,       // 공격 재생. Fighter의 타이머로 스스로 탈출 (워치독 대상 아님)
    Dodge,        // 회피(대시). Dodger의 타이머로 스스로 탈출 (워치독 대상 아님)
    Parry,        // 반격자세. Parrier의 타이머로 스스로 탈출 (워치독 대상 아님)
    ParrySuccess, // 패링 성공 연출. Parrier의 타이머로 스스로 탈출 (워치독 대상 아님)
    Landed,       // 넉백으로 쓰러짐. OnKnockdownGetUpStartFrame에서 탈출
    GetUp,        // 일어나는 중. OnKnockdownGetUpEndFrame에서 탈출
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

    [KoreanLabel("피격 경직 시간 직접 지정(초)")]
    [Tooltip("0이면 Animator의 'Stun' 클립 길이를 그대로 쓴다(기본). 0보다 큰 값을 넣으면 클립 길이 대신 그 값이 경직 시간이 된다. " +
             "클립 길이를 쓰면 편하지만, 애니메이터가 클립을 손보는 순간 전투 밸런스까지 같이 흔들린다. " +
             "밸런스를 클립과 분리해 고정하고 싶으면 값을 넣을 것.")]
    public float hitStunDurationOverride = 0f;

    public CharacterState CurrentState { get; private set; } = CharacterState.Idle;

    /// <summary>새 행동(이동/공격/점프)을 시작할 수 있는 상태인지. 고정 상태도 아니고 얻어맞은 여파도 없어야 한다.</summary>
    public bool CanAct => !IsSticky(CurrentState)
        && CurrentState != CharacterState.Stun
        && CurrentState != CharacterState.Airborne;

    /// <summary>이동 의도를 무시해야 하는 상태인지. Attack은 여기 없다 — 공격이 이동을 막는지는 Fighter가 판단한다.</summary>
    public bool IsMovementLocked =>
        CurrentState == CharacterState.JumpStart ||
        CurrentState == CharacterState.JumpLand ||
        CurrentState == CharacterState.Dodge ||
        CurrentState == CharacterState.Parry ||
        CurrentState == CharacterState.ParrySuccess ||
        CurrentState == CharacterState.Landed ||
        CurrentState == CharacterState.GetUp ||
        CurrentState == CharacterState.Stun ||
        CurrentState == CharacterState.Airborne;

    Locomotion locomotion;
    Animator animator;
    SpriteRenderer spriteRenderer;

    float stickyElapsed;

    float stunRemaining;         // 지상 경직이 끝나기까지 남은 시간
    float stunClipLength = -1f;  // 'Stun' 클립 길이 캐시. -1이면 아직 안 찾아봄
    bool warnedMissingStunClip;

    // "Animator에 그 State가 없다"를 이미 경고한 대상. CrossFade는 대상이 없어도 조용히 실패하므로 한 번만 찍는다.
    readonly HashSet<string> warnedMissing = new HashSet<string>();

    static bool IsSticky(CharacterState s) =>
        s == CharacterState.Stun ||
        s == CharacterState.JumpStart || s == CharacterState.JumpLand ||
        s == CharacterState.Attack || s == CharacterState.Dodge ||
        s == CharacterState.Parry || s == CharacterState.ParrySuccess ||
        s == CharacterState.Landed || s == CharacterState.GetUp;

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

    /// <summary>Dodger가 회피를 시작할 때 호출. 상태는 Dodge, 애니메이션은 "Dodge" State로 CrossFade.</summary>
    public void EnterDodge() => SetState(CharacterState.Dodge);

    /// <summary>Dodger가 회피 타이머를 소진했을 때 호출. 지금이 Dodge면 물리 파생 상태로 되돌린다.</summary>
    public void ExitDodge()
    {
        if (CurrentState == CharacterState.Dodge)
            SetState(DerivePhysicsState());
    }

    /// <summary>Parrier가 반격자세를 시작할 때 호출.</summary>
    public void EnterParry() => SetState(CharacterState.Parry);

    /// <summary>Parrier가 패링에 성공한 순간 호출. Parry → ParrySuccess.</summary>
    public void EnterParrySuccess() => SetState(CharacterState.ParrySuccess);

    /// <summary>Parrier가 반격자세/성공 연출 타이머를 소진했을 때 호출. 지금이 Parry/ParrySuccess면 물리 파생 상태로.</summary>
    public void ExitParry()
    {
        if (CurrentState == CharacterState.Parry || CurrentState == CharacterState.ParrySuccess)
            SetState(DerivePhysicsState());
    }

    /// <summary>
    /// 피격 시 Health가 호출. 지금이 어떤 고정 상태든 상관없이 즉시 물리 파생 상태로 되돌린다.
    /// <b>어떤 상태였는지 분기하지 않는 것이 핵심</b> — 예전엔 피격 쪽이 플래그를 하나씩 껐고 하나라도
    /// 빠뜨리면 그 동작의 Animation Event가 영영 안 와서 이동이 영구히 잠겼다.
    /// </summary>
    public void Interrupt()
    {
        // 공중으로 떠오른 피격은 경직이 "착지할 때까지"라 물리에서 파생한 그대로 둔다(Airborne → Landed → GetUp).
        if (locomotion.IsKnockedBackAirborne)
        {
            SetState(DerivePhysicsState());
            return;
        }

        // 지상 피격의 경직 시간은 Stun 클립 길이가 정한다. 넉백이 얼마나 세든, 미끄러짐이 언제 멈추든 무관하다.
        float duration = ResolveHitStunDuration();
        if (duration <= 0f)
        {
            WarnMissingStunClip();
            SetState(DerivePhysicsState()); // 길이를 모르면 잠기는 것보다 즉시 푸는 쪽이 낫다
            return;
        }

        stunRemaining = duration;

        // 이미 Stun이면 SetState가 무시되므로(같은 상태) 클립만 처음부터 다시 재생한다 — 연타로 맞으면 경직이 새로 시작돼야 한다.
        if (CurrentState == CharacterState.Stun)
            CrossFade(nameof(CharacterState.Stun));
        else
            SetState(CharacterState.Stun);
    }

    /// <summary>
    /// 적용할 경직 시간(초). 직접 지정이 0보다 크면 그 값을, 아니면 'Stun' 클립 길이를 쓴다.
    /// AttackData.ResolveDuration()과 같은 규칙이다.
    /// </summary>
    float ResolveHitStunDuration()
    {
        if (hitStunDurationOverride > 0f) return hitStunDurationOverride;

        if (stunClipLength < 0f)
            stunClipLength = FindClipLength(nameof(CharacterState.Stun));

        return stunClipLength;
    }

    /// <summary>
    /// 컨트롤러에 실린 클립 중 이름이 같은 것의 길이. 이 프로젝트는 State 이름 == 클립 이름이 불변식이라
    /// 이름으로 찾을 수 있다. 런타임에는 State에서 클립을 직접 얻는 API가 없어서(에디터 전용) 이 방법을 쓴다.
    /// </summary>
    float FindClipLength(string clipName)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return 0f;

        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip != null && clip.name == clipName)
                return clip.length;
        }

        return 0f;
    }

    void WarnMissingStunClip()
    {
        if (warnedMissingStunClip) return;

        warnedMissingStunClip = true;
        Debug.LogWarning($"{name}: Animator에 '{nameof(CharacterState.Stun)}' 클립이 없어 피격 경직 시간을 알 수 없습니다. " +
            "경직 없이 즉시 풀립니다. 클립을 넣거나 '피격 경직 시간 직접 지정'에 값을 넣으세요.");
    }

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
        TickHitStun();

        if (!IsSticky(CurrentState))
            SetState(DerivePhysicsState());

        if (spriteRenderer != null)
            spriteRenderer.flipX = !locomotion.FacingRight;
    }

    // ===== 내부 =====

    CharacterState DerivePhysicsState()
    {
        if (locomotion.IsKnockedBackAirborne) return CharacterState.Airborne;
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

    /// <summary>
    /// 지상 경직 타이머. 다 되면 남은 미끄러짐도 같이 끊는다 — 안 그러면
    /// "움직일 수는 있는데 아직 밀려나는 중"인 애매한 구간이 생긴다(경직보다 미끄러짐이 긴 센 공격에서).
    /// </summary>
    void TickHitStun()
    {
        if (CurrentState != CharacterState.Stun) return;

        stunRemaining -= Time.deltaTime;
        if (stunRemaining > 0f) return;

        locomotion.StopGroundSlide();
        SetState(DerivePhysicsState());
    }

    void TickWatchdog()
    {
        // Stun / Attack / Dodge / Parry / ParrySuccess는 각자의 타이머로 자력 종료하므로 감시 대상이 아니다.
        if (!IsSticky(CurrentState)
            || CurrentState == CharacterState.Stun
            || CurrentState == CharacterState.Attack
            || CurrentState == CharacterState.Dodge
            || CurrentState == CharacterState.Parry
            || CurrentState == CharacterState.ParrySuccess)
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
