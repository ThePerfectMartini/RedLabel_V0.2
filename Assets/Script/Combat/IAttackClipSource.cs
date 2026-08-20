using System;
using UnityEngine;

/// <summary>
/// 현재 재생해야 할 공격 애니메이션 클립이 바뀔 때마다 알려주는 컴포넌트가 구현하는 인터페이스.
/// CharacterState.Attack은 "공격 중"이라는 큰 상태만 나타낼 뿐, 콤보 단계별로 다른 클립을
/// 재생해야 하는 건 별도로 전달해야 하므로 CharacterStateMachine과는 분리되어 있다.
/// </summary>
public interface IAttackClipSource
{
    /// <summary>새 공격(콤보 진행 포함)이 시작될 때, 그 공격의 클립과 함께 발생한다.</summary>
    event Action<AnimationClip> OnAttackClipChanged;
}
