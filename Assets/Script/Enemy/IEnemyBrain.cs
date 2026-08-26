/// <summary>
/// 적 AI가 구현할 인터페이스. EnemyController와 같은 오브젝트에 붙은 컴포넌트에서 찾는다.
///
/// EnemyController는 "몸"(이동/공격/피격/상태 관리)만 담당하고, "무엇을 할지"의 판단은 전부 여기로 위임된다.
/// 덕분에 추격형/원거리형/보스 등 AI 종류가 늘어나도 EnemyController는 건드릴 필요가 없다.
/// 판단에 필요한 자기 정보(위치, 상태, 지상 여부 등)는 Think에 넘어오는 owner에서 읽는다.
///
/// 반환하는 CharacterIntent는 플레이어의 키 입력이 변환되는 것과 완전히 같은 타입이다.
/// 즉 컨트롤러 입장에서는 사람이 조종하든 AI가 조종하든 처리 경로가 하나다.
///
/// 구현체가 없으면 EnemyController는 CharacterIntent.None으로 동작한다 (제자리에 가만히 서 있음).
/// </summary>
public interface IEnemyBrain
{
    /// <summary>
    /// 매 프레임 호출되어 이번 프레임의 행동 의도를 반환한다.
    /// 여기서 직접 위치를 옮기거나 공격 판정을 하지 말 것 — 그건 EnemyController와 MovementCore/CombatCore의 일이다.
    ///
    /// 시작 공격이 하나뿐이므로 CharacterIntent.AttackToStart는 비워두면 된다
    /// (컨트롤러가 자기 firstAttackData로 알아서 채운다).
    /// </summary>
    CharacterIntent Think(EnemyController owner, float deltaTime);
}
