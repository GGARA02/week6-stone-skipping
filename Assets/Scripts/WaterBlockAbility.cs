using UnityEngine;

public class WaterBlockAbility : SpillAbility
{
    [Header("얼음 진화")]
    [SerializeField] private Fish _evolvedPrefab;
    [SerializeField] private W02BallAbility _evolutionEffect;
    private bool _isEvolving;

    /// <summary>
    /// 물로 1500m 이후 유빙 지역에 진입하면 얼음으로 진화하는 연출을 시작한다.
    /// context의 거리와 시작점 기준 전방 위치를 사용하며, dt는 쓰지 않고 중복 연출을 막는 상태와 외형 및 도감을 변경한다.
    /// </summary>
    public override void UpdateFlight(ThrowContext context, float dt)
    {
        if (_isEvolving || context.Player.Distance <= 1500f) return;
        ProceduralObstacleSpawner obstacles = context.Spawner.GetComponent<ProceduralObstacleSpawner>();
        if (!obstacles.IsInIceRegion(context.Body.position)) return;

        _isEvolving = true;
        context.Player.StartCoroutine(_evolutionEffect.PlayEvolution(context, _evolvedPrefab, false));
    }
}
