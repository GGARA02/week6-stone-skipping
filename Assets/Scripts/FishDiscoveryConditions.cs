using System;
using System.Collections.Generic;

using UnityEngine;

using Newtonsoft.Json.Linq;

public sealed class FishDiscoveryConditions
{
    [Header("발견 조건")]
    private readonly JObject _conditions;
    private readonly PlayerProgress _progress;
    private readonly HashSet<float> _distanceThresholds = new HashSet<float>();

    /// <summary>
    /// conditions의 발견 대상과 선행 ID를 fishIds로 확인하고 progress를 조건 판정에 연결한다.
    /// 거리 조건을 수집하며 출현 해금과 획득 기록은 변경하지 않는다.
    /// </summary>
    public FishDiscoveryConditions(JObject conditions, PlayerProgress progress, IEnumerable<string> fishIds)
    {
        _conditions = conditions;
        _progress = progress;
        HashSet<string> ids = new HashSet<string>(fishIds);
        foreach (JProperty entry in _conditions.Properties())
        {
            if (!ids.Contains(entry.Name))
                throw new InvalidOperationException($"FishSpawnConditions: 발견 대상 노드가 없습니다. {entry.Name}");
            string prerequisite = entry.Value.Value<string>("afterAcquiringFish");
            if (prerequisite != null && !ids.Contains(prerequisite))
                throw new InvalidOperationException($"FishSpawnConditions: 발견 선행 노드가 없습니다. {prerequisite}");
            float? threshold = entry.Value.Value<float?>("distanceExceeded");
            if (!threshold.HasValue) continue;
            if (threshold.Value < 0f)
                throw new InvalidOperationException($"FishSpawnConditions: 발견 거리는 음수일 수 없습니다. {entry.Name}");
            _distanceThresholds.Add(threshold.Value);
        }
    }

    /// <summary>
    /// fishId의 상시 발견, 선행 획득 또는 저장된 거리 조건 충족 여부를 반환한다.
    /// JSON과 progress를 사용하며 출현 허용이나 획득 상태는 변경하지 않는다.
    /// </summary>
    public bool CanDiscover(string fishId)
    {
        JToken condition = _conditions[fishId];
        if (condition == null) return false;
        if (condition.Value<bool?>("always") == true) return true;
        string prerequisite = condition.Value<string>("afterAcquiringFish");
        if (prerequisite != null && _progress.IsFishRegistered(prerequisite)) return true;
        float? threshold = condition.Value<float?>("distanceExceeded");
        return threshold.HasValue && _progress.DiscoveryDistance > threshold.Value;
    }

    /// <summary>
    /// fishId가 나머지 도감을 모두 모아야 발견되는 도감 완성 보상인지 반환한다.
    /// JSON의 afterCompletingCatalog 값을 사용하며 상태는 변경하지 않는다.
    /// </summary>
    public bool RequiresCatalogCompletion(string fishId)
    {
        JToken condition = _conditions[fishId];
        return condition != null && condition.Value<bool?>("afterCompletingCatalog") == true;
    }

    /// <summary>
    /// 현재 이동 거리 distance가 JSON의 거리 기준을 초과한 경우 달성 기록을 저장한다.
    /// 등록된 기준마다 progress를 갱신하며 같은 기준은 반복 저장하지 않는다.
    /// </summary>
    public void RecordDistance(float distance)
    {
        foreach (float threshold in _distanceThresholds)
            _progress.RecordDiscoveryDistance(distance, threshold);
    }
}
