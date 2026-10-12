using UnityEngine;

// 게의 특수 동작. SPACE를 누르면 입에서 게거품 파티클을 순간적으로 뿜어낸다.
public class CrabAbility : FishAbility
{
    [Header("이펙트")]
    [Tooltip("SPACE 입력 시 재생할 게거품 파티클 시스템")]
    [SerializeField]
    private ParticleSystem _crabBubble;
    [Header("방출량 설정")]
    [Tooltip("판정에 따른 파티클 수")]
    [SerializeField] private int _judgeBubbleCount = 20;
    [Tooltip("충돌에 따른 파티클 수")]
    [SerializeField] private int _collisionBubbleCount = 80;

    /// 기존 Partivle System의 Play 방식에서 파티클 N개를 생성하라고 직접 명령을 내리는 방식으로 변경

    /// <summary>
    /// 던진 상태에서 SPACE를 누를 때마다 판정 성공 때와 같은 수의 게거품을 뿜는다.
    /// _judgeBubbleCount를 사용하며, context는 쓰지 않는다. 게거품 파티클을 방출한다.
    /// </summary>
    public override void OnSpacePressed(ThrowContext context)
    {
        //_crabBubble.Play();
        _crabBubble.Emit(_judgeBubbleCount);
    }

    public override void OnObstacleCollision(ThrowContext context, Collision collision)
    {
        _crabBubble.Emit(_collisionBubbleCount);
    }
}
