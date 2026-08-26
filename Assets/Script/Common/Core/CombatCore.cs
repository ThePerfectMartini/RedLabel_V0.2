using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 순수 전투 로직. MonoBehaviour가 아니며, MovementCore와 서로 참조하지 않는다.
/// 공격 하나(AttackData)의 판정만 담당한다. 콤보 진행(다음 공격으로 넘어가는 타이밍 판단,
/// 입력 버퍼링)은 이 클래스의 책임이 아니라 CharacterControllerBase가 CurrentAttack.nextAttack을
/// 보고 판단한 뒤 Init()을 다시 호출하는 방식으로 이뤄진다.
/// </summary>
public class CombatCore
{
    public int Damage = 10;
    public float AttackRange = 1.5f;   // 캐릭터 앞으로 얼마나 떨어진 지점을 때리는지
    public float AttackRadius = 1f;    // 판정 구체 반지름
    public float KnockbackForce = 12f; // 대상이 지상에 있을 때 밀리는 힘 (수평)
    public float LaunchForce = 6f;     // 대상이 지상에 있을 때 뜨는 힘 (수직)
    public float AirborneKnockbackForce = 12f; // 대상이 이미 공중에 있을 때 밀리는 힘 (수평)
    public float AirborneLaunchForce = 6f;     // 대상이 이미 공중에 있을 때 뜨는 힘 (수직)
    public float GroundSlideDeceleration = 30f; // 그라운드 넉백일 때 초당 감속량
    public float AttackCooldown = 0.1f;
    public float AttackDuration = 0f; // 공격 애니메이션 클립 길이에서 자동 계산됨 (Init 참고)

    /// <summary>현재 적용된 AttackData. 콤보 진행 시 CurrentAttack.nextAttack으로 다음 공격을 찾는다.</summary>
    public AttackData CurrentAttack { get; private set; }

    float lastAttackTime = -999f;

    /// <summary>
    /// AttackData의 값을 이 인스턴스에 복사한다. data가 null이면 아무것도 하지 않는다.
    /// AttackDuration은 데이터 안의 attackClip 길이로부터 자동 계산한다 (수동 입력 없음).
    /// 콤보 진행 시 다음 공격으로 넘어갈 때도 이 메서드를 다시 호출해서 갈아끼운다.
    /// </summary>
    public void Init(AttackData data)
    {
        if (data == null) return;

        CurrentAttack = data;
        Damage = data.damage;
        AttackRange = data.attackRange;
        AttackRadius = data.attackRadius;
        KnockbackForce = data.knockbackForce;
        LaunchForce = data.launchForce;
        AirborneKnockbackForce = data.airborneKnockbackForce;
        AirborneLaunchForce = data.airborneLaunchForce;
        GroundSlideDeceleration = data.groundSlideDeceleration;
        AttackCooldown = data.attackCooldown;

        AttackDuration = data.ResolveDuration();

        WarnOnceIfMisconfigured(data);
    }

    // 이미 경고를 띄운 AttackData. Init은 콤보 단계마다 호출되므로, 이게 없으면 같은 경고가
    // 공격할 때마다 Console을 뒤덮어 정작 봐야 할 로그가 묻힌다. 에셋당 한 번만 알리면 충분하다.
    static readonly HashSet<AttackData> warnedAttacks = new HashSet<AttackData>();

    /// <summary>
    /// 에셋 설정 실수를 재생 중에 잡아준다. 둘 다 컴파일로는 걸러지지 않고,
    /// 증상만 보면 원인을 짐작하기 어려운 것들이다.
    /// </summary>
    static void WarnOnceIfMisconfigured(AttackData data)
    {
        if (!warnedAttacks.Add(data)) return;

        if (data.ResolveDuration() <= 0f)
        {
            Debug.LogWarning($"{nameof(AttackData)}({data.name})에 공격 애니메이션 클립도 '지속시간 직접 지정'도 없어 " +
                             $"공격 지속시간을 정할 수 없습니다. 공격이 시작하자마자 끝납니다.", data);
        }

        // 타격 이벤트가 없으면 공격은 재생되지만 판정이 한 번도 나가지 않는다.
        // 캔슬 허용 시점(타격 프레임 이후)도 영영 열리지 않아 "이 공격만 캔슬이 안 되는" 증상으로 나타난다.
        if (!data.HasHitFrameEvent())
        {
            Debug.LogWarning($"{nameof(AttackData)}({data.name})의 클립에 'OnAttackHitFrame' Animation Event가 없습니다. " +
                             $"타격 판정이 나가지 않고, 이 공격으로는 다른 공격을 캔슬할 수도 없습니다.", data);
        }
    }

    /// <summary>
    /// 공격 시작 가능 여부를 확인하고, 가능하면 쿨타임 기준 시각만 기록한다.
    /// 실제 타격 판정은 하지 않는다 — 판정은 애니메이션 이벤트 시점에 PerformHitScan으로 별도 수행한다.
    /// </summary>
    public bool TryStartAttack(float currentTime)
    {
        if (currentTime - lastAttackTime < AttackCooldown)
            return false;

        lastAttackTime = currentTime;
        return true;
    }

    /// <summary>
    /// 실제 타격 판정. origin에서 facingDir 방향으로 판정 구체를 만들어 targetLayer의 IHittable에게 HitData 전달.
    /// 공격 애니메이션 클립의 Animation Event가 호출하는 타이밍에 실행된다 (TryStartAttack과 분리됨).
    /// </summary>
    public void PerformHitScan(Vector3 origin, Vector3 facingDir, GameObject attacker, LayerMask targetLayer)
    {
        Vector3 center = origin + facingDir.normalized * AttackRange;
        Collider[] hits = Physics.OverlapSphere(center, AttackRadius, targetLayer);

        foreach (Collider col in hits)
        {
            IHittable hittable = col.GetComponentInParent<IHittable>();
            if (hittable == null)
                continue;

            // 맞은 대상이 지금 공중에 있는지는 대상만 아는 정보라, 지상용/공중용 넉백을 둘 다 실어 보내고
            // 실제로 어느 쪽을 쓸지는 대상의 OnHit에서 고르게 한다.
            Vector3 dir = facingDir.normalized;
            HitData hit = new HitData
            {
                Damage = Damage,
                KnockbackVelocity = dir * KnockbackForce + Vector3.up * LaunchForce,
                AirborneKnockbackVelocity = dir * AirborneKnockbackForce + Vector3.up * AirborneLaunchForce,
                GroundSlideDeceleration = GroundSlideDeceleration,
                Attacker = attacker
            };
            hittable.OnHit(hit);
        }
    }
}