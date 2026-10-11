using UnityEngine;

// 게의 특수 동작. SPACE 판정에 성공하면 입에서 게거품 파티클을 순간적으로 뿜어낸다.
public class CrabAbility : FishAbility
{
    [Header("이펙트")]
    [Tooltip("SPACE 판정 성공 시 재생할 게거품 파티클 시스템")]
    [SerializeField]
    private ParticleSystem _crabBubble;
    [Header("방출량 설정")]
    [Tooltip("판정에 따른 파티클 수")]
    [SerializeField] private int _judgeBubbleCount = 20;
    [Tooltip("충돌에 따른 파티클 수")]
    [SerializeField] private int _collisionBubbleCount = 80;

    /// 기존 Partivle System의 Play 방식에서 파티클 N개를 생성하라고 직접 명령을 내리는 방식으로 변경

    public override void OnJudgeSuccess(ThrowContext context, SkipJudge judge)
    {
        //_crabBubble.Play();
        _crabBubble.Emit(_judgeBubbleCount);
    }

    public override void OnObstacleCollision(ThrowContext context, Collision collision)
    {
        _crabBubble.Emit(_collisionBubbleCount);
    }
}
