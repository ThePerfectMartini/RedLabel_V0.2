using UnityEngine;

/// <summary>
/// 모든 캐릭터가 공유하는 전역 물리값. 중력은 이 에셋 하나로 통일하고,
/// 캐릭터별 "무게"는 MovementStatData.gravityScale 배율로만 다르게 준다.
/// 프로젝트에 이 타입의 에셋은 하나만 만들고, 모든 캐릭터의 컨트롤러가 같은 에셋을 참조해야 한다.
/// </summary>
[CreateAssetMenu(fileName = "GlobalPhysicsData", menuName = "DoitMySelf/Global Physics Data")]
public class GlobalPhysicsData : ScriptableObject
{
    [KoreanLabel("중력")]
    [Tooltip("모든 캐릭터가 공유하는 기본 중력 가속도. 캐릭터별 '무게'는 각 MovementStatData의 중력 스케일로 조절한다.")]
    public float gravity = 20f;

    static GlobalPhysicsData instance;

    /// <summary>
    /// Resources 폴더의 GlobalPhysicsData 에셋을 찾아 캐싱해서 반환.
    /// MapBounds.Instance와 같은 방식 — 인스펙터에서 따로 연결할 필요 없이 첫 접근 시점에 자동으로 찾는다.
    /// 에셋은 반드시 어떤 Resources 폴더 밑에 "GlobalPhysicsData"라는 이름으로 있어야 한다.
    /// </summary>
    public static GlobalPhysicsData Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Resources.Load<GlobalPhysicsData>("GlobalPhysicsData");
                if (instance == null)
                    Debug.LogWarning("GlobalPhysicsData를 찾지 못했습니다. Resources 폴더 밑에 'GlobalPhysicsData'라는 이름의 에셋이 있는지 확인하세요.");
            }
            return instance;
        }
    }
}
