/// <summary>
/// "이번 프레임에 뭘 할지"를 정하는 주체가 구현하는 인터페이스. Actor와 같은 오브젝트에 붙은 컴포넌트에서 찾는다.
///
/// Actor("몸")는 이동 / 공격 / 피격 / 상태 관리만 하고, 판단은 전부 여기로 위임된다.
/// 플레이어는 PlayerInput이, 적은 나중에 붙일 EnemyBrain이 구현한다 — Actor는 한 글자도 안 바뀐다.
/// 구현체가 없으면 Actor는 CharacterIntent.None으로 동작한다(제자리에 가만히 서 있음. 훈련 더미가 이 경우다).
///
/// 판단에 자기 정보(위치, 상태 등)가 필요하면 구현체가 직접 GetComponent로 가져온다.
/// 그래서 예전 IEnemyBrain.Think(owner)와 달리 소유자를 파라미터로 넘기지 않는다.
/// </summary>
public interface IIntentSource
{
    /// <summary>매 프레임 Actor가 호출한다. 이번 프레임의 행동 의도를 반환.</summary>
    CharacterIntent GetIntent(float deltaTime);
}
