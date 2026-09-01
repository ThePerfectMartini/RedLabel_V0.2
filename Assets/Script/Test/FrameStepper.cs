using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// TEMP 디버그: 플레이 중 게임을 프레임 단위로 진행시킨다. 애니메이션 전환 같은 한두 프레임짜리 현상을 눈으로 잡을 때.
///
/// [P] 일시정지 토글 / [→] 또는 [Space] 한 프레임 진행 / [→] 길게 누르면 지연 후 연속 진행
///
/// Time.timeScale = 0으로 멈추고, 스텝 요청 시 딱 한 프레임만 원래 속도로 돌린 뒤 다시 0으로.
/// Animator(updateMode Normal)와 Animation Event도 timeScale을 따르므로 같이 한 프레임씩 진행된다.
///
/// 씬 아무 오브젝트에나 하나만 붙이면 된다. 제거: 이 파일 삭제 + 컴포넌트 제거.
/// </summary>
public class FrameStepper : MonoBehaviour
{
    [KoreanLabel("시작 시 일시정지")]
    public bool pauseOnStart = false;

    [KoreanLabel("연속 진행 시작 지연(초)")]
    public float holdRepeatDelay = 0.35f;

    [KoreanLabel("연속 진행 간격(초)")]
    public float holdRepeatInterval = 0.07f;

    [KoreanLabel("상태 로그 대상 Animator (선택)")]
    [Tooltip("지정하면 스텝될 때마다 현재 클립 이름 / normalizedTime / 전환 여부를 Console에 찍는다.")]
    public Animator logAnimator;

    bool paused;
    bool advancing;      // 이번 프레임이 '스텝된 한 프레임'인지
    int steppedFrames;
    float normalScale = 1f;
    float holdTime;
    float lastRepeatAt;

    void Start()
    {
        normalScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        if (pauseOnStart) SetPaused(true);
    }

    // 컴포넌트가 꺼지거나 플레이 모드를 나갈 때 timeScale이 0인 채로 남지 않게 원복.
    void OnDisable() => Time.timeScale = normalScale;
    void OnApplicationQuit() => Time.timeScale = normalScale;

    void Update()
    {
        // 직전 프레임에 스텝을 요청했다면 이번 프레임이 바로 그 한 프레임. 지나갔으니 다시 멈춘다.
        if (advancing)
        {
            advancing = false;
            steppedFrames++;
            LogAnimatorState();
            if (paused) Time.timeScale = 0f;
            return;
        }

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb.pKey.wasPressedThisFrame)
            SetPaused(!paused);

        if (!paused) return;

        bool step = kb.rightArrowKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;

        if (kb.rightArrowKey.isPressed)
        {
            holdTime += Time.unscaledDeltaTime;
            if (holdTime >= holdRepeatDelay && holdTime - lastRepeatAt >= holdRepeatInterval)
            {
                step = true;
                lastRepeatAt = holdTime;
            }
        }
        else
        {
            holdTime = 0f;
            lastRepeatAt = 0f;
        }

        if (step)
        {
            Time.timeScale = normalScale; // 이번에 오는 한 프레임만 정상 속도
            advancing = true;
        }
    }

    void SetPaused(bool value)
    {
        paused = value;
        Time.timeScale = paused ? 0f : normalScale;
        holdTime = 0f;
        lastRepeatAt = 0f;
    }

    void LogAnimatorState()
    {
        if (logAnimator == null) return;

        AnimatorStateInfo st = logAnimator.GetCurrentAnimatorStateInfo(0);
        AnimatorClipInfo[] clips = logAnimator.GetCurrentAnimatorClipInfo(0);
        string clip = clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : "?";

        string transition = "";
        if (logAnimator.IsInTransition(0))
        {
            AnimatorClipInfo[] nextClips = logAnimator.GetNextAnimatorClipInfo(0);
            string nextClip = nextClips.Length > 0 && nextClips[0].clip != null ? nextClips[0].clip.name : "?";
            float t = logAnimator.GetAnimatorTransitionInfo(0).normalizedTime;
            transition = $"  ->transition-> '{nextClip}' ({t:F2})";
        }

        Debug.Log($"[step {steppedFrames}] clip='{clip}' normalizedTime={st.normalizedTime:F3}{transition}");
    }

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        style.normal.textColor = paused ? Color.yellow : new Color(0.5f, 1f, 0.5f);

        string msg = paused
            ? $"⏸ PAUSED   step {steppedFrames}      [P] resume    [→]/[Space] step 1    [→ hold] run"
            : "▶ RUNNING      [P] pause";

        GUI.Label(new Rect(10, 10, 1000, 24), msg, style);
    }
}
