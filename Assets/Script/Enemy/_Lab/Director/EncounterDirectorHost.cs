using UnityEngine;

/// <summary>
/// 조율자의 손잡이를 인스펙터에 내놓는 씬 오브젝트. EncounterDirector 자체는 MonoBehaviour가
/// 아니라서 인스펙터가 없기 때문에, 값을 밀어넣는 역할만 하는 얇은 껍데기다.
///
/// **난이도 조절은 여기서 한다.** 데미지나 체력이 아니라 이 세 값이다:
/// 동시 공격 허용 수 / 토큰 부여 간격 / (예고 길이는 BehaviorTuningData에 있다)
///
/// 플레이 중에 인스펙터에서 값을 바꾸면 OnValidate로 즉시 반영되므로, 게임을 멈추지 않고
/// 난이도를 만져보며 맞출 수 있다.
///
/// [준비물] 씬의 아무 빈 오브젝트에 하나만 붙인다. 없어도 조율자는 기본값으로 동작한다.
/// </summary>
public class EncounterDirectorHost : MonoBehaviour
{
    [KoreanLabel("조율 규칙")]
    public DirectorSettings settings = DirectorSettings.Default;

    [KoreanLabel("현황 표시")]
    [Tooltip("화면 우상단에 등록된 적 수와 지금 공격 권한을 가진 적 수를 띄운다. " +
        "'동시 예고 중인 적 수가 항상 설정값 이하'인지 눈으로 확인하는 용도다.")]
    public bool showStatus = true;

    void Awake() => Apply();

    // 플레이 중 인스펙터에서 값을 바꾸면 여기로 들어온다. 멈추지 않고 난이도를 맞출 수 있다.
    void OnValidate() => Apply();

    void Apply()
    {
        if (settings.maxConcurrentAttackers < 1)
            settings.maxConcurrentAttackers = 1;

        EncounterDirector.Instance.Settings = settings;
    }

    void OnGUI()
    {
        if (!showStatus) return;

        EncounterDirector director = EncounterDirector.Instance;

        GUILayout.BeginArea(new Rect(Screen.width - 210f, 10f, 200f, 80f), GUI.skin.box);
        GUILayout.Label("[조율자]");
        GUILayout.Label($"등록된 적       {director.MemberCount}");
        GUILayout.Label($"공격 권한 보유  {director.TokenCount} / {settings.maxConcurrentAttackers}");
        GUILayout.EndArea();
    }
}
