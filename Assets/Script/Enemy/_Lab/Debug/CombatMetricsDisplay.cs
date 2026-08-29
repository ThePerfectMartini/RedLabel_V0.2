using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TEMP: 적 AI를 A/B로 비교하기 위한 계측 오버레이. 화면 좌상단에 숫자를 띄운다.
///
/// 기존 AI와 새 AI(_Lab) 양쪽에 똑같이 붙는다. 어느 쪽 아키텍처에도 의존하지 않고
/// CharacterStateMachine.OnStateChanged만 구독하므로 리플렉션이 필요 없다.
///
/// [세는 것]
/// - 총 피격      : 플레이어가 Stun / Airborne에 진입한 횟수
/// - 동시 공격    : 두 마리 이상이 simultaneousWindow 안에 Attack에 진입한 "사건"의 수.
///                  벨트스크롤에서 이 값이 0이 아니면 플레이어가 읽을 수 없는 공격이 있었다는 뜻이다.
/// - 기상 직후 피격: 일어나기(GetUp)가 끝난 뒤 wakeupGrace 안에 다시 맞은 횟수.
///                  무한 콤보에 갇히는지를 보는 값이라 0이어야 한다.
///
/// [쓰는 법] 씬의 아무 오브젝트에나 하나만 붙인다. label에 어느 쪽을 재는지 적어두면
/// 스크린샷만 봐도 어느 AI의 수치인지 구분된다.
///
/// 제거할 때는 이 스크립트 파일 삭제 + 씬에서 컴포넌트만 떼어내면 된다.
/// </summary>
public class CombatMetricsDisplay : MonoBehaviour
{
    [KoreanLabel("측정 대상 이름")]
    [Tooltip("화면에 같이 표시된다. '기존 AI' / '새 AI'처럼 적어두면 기록을 헷갈리지 않는다.")]
    public string label = "기존 AI";

    [KoreanLabel("동시 공격 판정 창(초)")]
    [Tooltip("이 시간 안에 두 마리 이상이 공격에 들어가면 '동시 공격' 1회로 센다.")]
    public float simultaneousWindow = 0.15f;

    [KoreanLabel("기상 유예(초)")]
    [Tooltip("일어나기가 끝난 뒤 이 시간 안에 맞으면 '기상 직후 피격'으로 센다.")]
    public float wakeupGrace = 0.5f;

    [KoreanLabel("적 재탐색 주기(초)")]
    [Tooltip("씬에 적이 새로 생기거나 그룹을 켜고 끌 수 있으므로 주기적으로 다시 찾는다.")]
    public float rescanInterval = 1f;

    int totalPlayerHits;
    int simultaneousAttacks;
    int wakeupPunishes;
    float elapsed;

    // 동시 공격 판정용 슬라이딩 창. 창 안에서 두 번째 적이 들어온 순간에만 1회 센다.
    float attackWindowStart = float.NegativeInfinity;
    int attackWindowCount;

    float lastGetUpEndTime = float.NegativeInfinity;

    PlayerController player;
    float rescanTimer;

    // 이미 구독한 적. 재탐색 때 중복 구독을 막고, 죽어서 파괴된 것은 걸러낸다.
    // 오브젝트를 비활성화만 한 경우에는 인스턴스가 살아 있으므로 구독이 그대로 유지된다.
    readonly Dictionary<int, EnemyController> subscribedEnemies = new Dictionary<int, EnemyController>();
    readonly List<int> destroyedIds = new List<int>();

    void Start()
    {
        player = PlayerController.Instance;
        if (player == null)
        {
            Debug.LogWarning($"{name}: 씬에 PlayerController가 없어 계측할 수 없습니다.");
            return;
        }

        player.StateMachine.OnStateChanged += OnPlayerStateChanged;
        RescanEnemies();
    }

    void OnDestroy()
    {
        if (player != null)
            player.StateMachine.OnStateChanged -= OnPlayerStateChanged;
    }

    void Update()
    {
        elapsed += Time.deltaTime;

        // 디버그 전용이라 주기적 FindObjectsByType을 허용한다. 씬에서 적 그룹을 켜고 끄는 것이
        // 이 비교의 전제라서, 한 번만 찾아두면 그룹을 바꾼 뒤 아무것도 세지 못한다.
        rescanTimer -= Time.deltaTime;
        if (rescanTimer <= 0f)
        {
            rescanTimer = rescanInterval;
            RescanEnemies();
        }
    }

    void RescanEnemies()
    {
        // 죽어서 파괴된 적을 먼저 걷어낸다. Destroy된 UnityEngine.Object는 "가짜 null"이라
        // 명시적인 == null 비교로만 걸러진다.
        destroyedIds.Clear();
        foreach (KeyValuePair<int, EnemyController> pair in subscribedEnemies)
        {
            if (pair.Value == null)
                destroyedIds.Add(pair.Key);
        }
        for (int i = 0; i < destroyedIds.Count; i++)
            subscribedEnemies.Remove(destroyedIds[i]);

        EnemyController[] enemies = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);

        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyController enemy = enemies[i];
            if (enemy == null) continue;

            int id = enemy.GetInstanceID();
            if (subscribedEnemies.ContainsKey(id)) continue;

            subscribedEnemies.Add(id, enemy);
            enemy.StateMachine.OnStateChanged += OnEnemyStateChanged;
        }
    }

    void OnPlayerStateChanged(CharacterState previous, CharacterState next)
    {
        // 일어나기가 끝난 시점. 여기서부터 wakeupGrace 동안 맞으면 기상 직후 피격이다.
        if (previous == CharacterState.GetUp && next != CharacterState.GetUp)
            lastGetUpEndTime = Time.time;

        bool hit = (next == CharacterState.Stun || next == CharacterState.Airborne)
                && previous != CharacterState.Stun
                && previous != CharacterState.Airborne;

        if (!hit) return;

        totalPlayerHits++;

        if (Time.time - lastGetUpEndTime <= wakeupGrace)
            wakeupPunishes++;
    }

    void OnEnemyStateChanged(CharacterState previous, CharacterState next)
    {
        if (next != CharacterState.Attack || previous == CharacterState.Attack) return;

        if (Time.time - attackWindowStart <= simultaneousWindow)
        {
            attackWindowCount++;

            // 창 안의 두 번째 적에서만 센다. 세 마리째가 더 들어와도 같은 사건이다.
            if (attackWindowCount == 2)
                simultaneousAttacks++;

            return;
        }

        attackWindowStart = Time.time;
        attackWindowCount = 1;
    }

    void ResetCounters()
    {
        totalPlayerHits = 0;
        simultaneousAttacks = 0;
        wakeupPunishes = 0;
        elapsed = 0f;
        attackWindowStart = float.NegativeInfinity;
        attackWindowCount = 0;
        lastGetUpEndTime = float.NegativeInfinity;
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10f, 10f, 240f, 150f), GUI.skin.box);

        GUILayout.Label($"[{label}]  {elapsed:F1}s");
        GUILayout.Label($"총 피격          {totalPlayerHits}");
        GUILayout.Label($"동시 공격        {simultaneousAttacks}");
        GUILayout.Label($"기상 직후 피격   {wakeupPunishes}");
        GUILayout.Label($"적 {subscribedEnemies.Count}마리 구독 중");

        if (GUILayout.Button("리셋"))
            ResetCounters();

        GUILayout.EndArea();
    }
}
