using UnityEngine;

// 물을 담은 물고기(꽉 찬 양동이, 물 블록)의 특수 동작. SPACE를 누를 때마다 판정과 관계없이 물을 흘린다.
// 흘리는 양은 Inspector에서 정하며, 물방울 파티클은 월드 공간에 남아 뒤로 흩어진다.
public class SpillAbility : FishAbility
{
    [Header("흘리기")]
    [Tooltip("물방울 파티클. 시뮬레이션 공간은 World로 둔다")]
    [SerializeField]
    private ParticleSystem _spillParticle;
    [Tooltip("SPACE 한 번에 흘리는 물방울 수")]
    [SerializeField]
    private int _spillCount = 15;

    /// <summary>
    /// SPACE를 누를 때마다 판정 성공 때와 같이 물을 흘린다.
    /// context는 쓰지 않으며, 물방울 파티클을 방출한다.
    /// </summary>
    public override void OnSpacePressed(ThrowContext context)
    {
        Spill();
    }

    /// <summary>
    /// 정해진 수만큼 물방울을 한 번에 내보낸다.
    /// _spillParticle과 _spillCount를 사용하며, 파티클을 방출한다.
    /// </summary>
    private void Spill()
    {
        _spillParticle.Emit(_spillCount);
    }
}
