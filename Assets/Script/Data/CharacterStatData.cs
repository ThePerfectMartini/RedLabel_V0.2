using UnityEngine;

/// <summary>
/// 캐릭터(플레이어/더미/적 공용) 체력 데이터.
/// 순수 데이터 컨테이너 — 로직 없음.
/// </summary>
[CreateAssetMenu(fileName = "CharacterStatData", menuName = "DoitMySelf/Character Stat Data")]
public class CharacterStatData : ScriptableObject
{
    [KoreanLabel("최대 체력")]
    public int maxHealth = 100;
}
