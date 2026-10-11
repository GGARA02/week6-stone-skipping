using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Serialization;

// 자동차의 특수 동작. SPACE 성공 판정 시 창문 스포너를 통해 도둑 Fish를 공용 시스템으로 사출한다.
public class Car : EjectFishAbility
{
    [Header("스포너")]
    [FormerlySerializedAs("spawnPos")]
    [SerializeField]
    private List<DodukSpawner> _spawnPos = new List<DodukSpawner>();
    private ThrowContext _context;
    private SkipJudge _lastJudge = SkipJudge.Good;

    /// <summary>
    /// 현재 오브젝트와 하위 오브젝트의 모든 DodukSpawner를 수집한다.
    /// 비활성 오브젝트를 포함해 _spawnPos 리스트를 갱신한다.
    /// </summary>
    [ContextMenu("Collect Child Spawners")]
    private void CollectChildRenderers()
    {
        _spawnPos.Clear();
        GetComponentsInChildren<DodukSpawner>(true, _spawnPos);
    }

    /// <summary>
    /// 던진 상태에서 SPACE 판정에 성공했을 때 도둑들을 소환한다.
    /// context와 judge를 사용하며, SpawnDoduk을 호출하여 도둑을 사출한다.
    /// </summary>
    public override void OnJudgeSuccess(ThrowContext context, SkipJudge judge)
    {
        _context = context;
        _lastJudge = judge;

        // 소환할 도둑 수를 4~5마리 범위에서 랜덤으로 정한다.
        int count = Random.Range(4, 6);
        SpawnDoduk(count);
    }

    public override void OnObstacleCollision(ThrowContext context, Collision collision)
    {
        base.OnObstacleCollision(context, collision);
        _context = context;
        _lastJudge = SkipJudge.Good; // 사출 통과를 위한 판정 등급 부여                                                                                                                                                                                                                                                                                                                                                                                                                                                                    
        int crashCount = Random.Range(12, 16);
        SpawnDoduk(crashCount);
    }

    /// <summary>
    /// 지정된 수량만큼 등록된 스포너 중 무작위 위치를 선택하여 도둑 소환을 요청한다.
    /// count와 _spawnPos를 사용하며, 각 DodukSpawner의 SpawnDoDuk을 호출한다.
    /// </summary>
    public void SpawnDoduk(int count)
    {
        if (_spawnPos.Count == 0) return;

        ThrowContext context = GetValidContext();
        for (int i = 0; i < count; i++)
        {
            int index = Random.Range(0, _spawnPos.Count);
            _spawnPos[index].SpawnDoDuk(context, this, _lastJudge);
        }
    }

    /// <summary>
    /// 유효한 ThrowContext를 반환하거나 상위 컴포넌트에서 찾아 구성한다.
    /// _context를 사용하며, PlayerController 기반의 ThrowContext를 반환한다.
    /// </summary>
    private ThrowContext GetValidContext()
    {
        if (_context.Player != null) return _context;
        PlayerController player = GetComponentInParent<PlayerController>();
        FishSpawner spawner = FindFirstObjectByType<FishSpawner>();
        return new ThrowContext(player, null, transform, spawner);
    }
}
