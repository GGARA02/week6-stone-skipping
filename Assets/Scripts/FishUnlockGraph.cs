using System;
using System.Collections.Generic;

using UnityEngine;

using Newtonsoft.Json.Linq;

public enum FishCatalogState
{
    Undiscovered,
    Discovered,
    Acquired
}

public sealed class FishUnlockGraph
{
    private const string RESOURCE_NAME = "FishSpawnConditions";

    [Header("해금 데이터")]
    private readonly PlayerProgress _progress;
    private readonly FishDiscoveryConditions _discoveryConditions;
    private readonly Dictionary<string, string[]> _connections = new Dictionary<string, string[]>();
    private readonly HashSet<string> _initialFish = new HashSet<string>();
    private readonly HashSet<string> _specialFish = new HashSet<string>();
    private readonly HashSet<string> _unlockedFish = new HashSet<string>();

    /// <summary>
    /// progress와 JSON의 초기 및 특수 목록과 연결을 읽어 해금 그래프를 준비한다.
    /// progress를 보관하며 판 시작 시 복원하기 전에는 출현 해금 상태를 변경하지 않는다.
    /// </summary>
    public FishUnlockGraph(PlayerProgress progress)
    {
        _progress = progress;
        JObject data = JObject.Parse(Resources.Load<TextAsset>(RESOURCE_NAME).text);
        _initialFish.UnionWith(data["initialFish"].ToObject<string[]>());
        _specialFish.UnionWith(data["specialFish"].ToObject<string[]>());
        foreach (JProperty node in ((JObject)data["connections"]).Properties())
        {
            _connections.Add(node.Name, node.Value.ToObject<string[]>());
        }
        ValidateConnections();
        _discoveryConditions = new FishDiscoveryConditions(
            data["discoveryConditions"] as JObject ?? new JObject(), progress, _connections.Keys);
    }

    /// <summary>
    /// fishId에 해당하는 노드가 JSON에 정의되어 있는지 반환한다.
    /// 프리팹 등록 대상 확인에 사용하며 그래프 상태는 변경하지 않는다.
    /// </summary>
    public bool ContainsFish(string fishId)
    {
        return _connections.ContainsKey(fishId);
    }

    /// <summary>
    /// type의 습득 기록, 이번 판의 해금 목록과 즉시 적용되는 발견 조건으로 도감 상태를 반환한다.
    /// 기본 짱돌은 획득으로 처리하고 발견 조건은 자연 출현 허용에 반영하지 않는다.
    /// </summary>
    public FishCatalogState GetCatalogState(FishType type)
    {
        if (type == null || type.Id == "default" || _progress.IsFishRegistered(type))
            return FishCatalogState.Acquired;
        return _unlockedFish.Contains(type.Id) || _discoveryConditions.CanDiscover(type.Id)
            ? FishCatalogState.Discovered : FishCatalogState.Undiscovered;
    }

    /// <summary>
    /// fishId가 나머지 도감을 모두 모아야 발견되는 도감 완성 보상인지 반환한다.
    /// 발견 조건 JSON을 사용하며 그래프 상태는 변경하지 않는다.
    /// </summary>
    public bool RequiresCatalogCompletion(string fishId)
    {
        return _discoveryConditions.RequiresCatalogCompletion(fishId);
    }

    /// <summary>
    /// 현재 이동 거리 distance를 발견 조건에 전달해 거리 기준 초과를 저장한다.
    /// 도감 공개용 진행 기록만 변경하며 이번 판의 자연 출현 목록은 유지한다.
    /// </summary>
    public void RecordDiscoveryDistance(float distance)
    {
        _discoveryConditions.RecordDistance(distance);
    }

    /// <summary>
    /// 판 시작 시 prefabs의 습득 기록으로 초기 노드와 직접 연결된 다음 노드의 해금을 복원한다.
    /// 현재 판의 출현 목록을 갱신하며 판 도중의 습득은 다음 호출까지 반영하지 않는다.
    /// </summary>
    public void RestoreUnlocks(IEnumerable<Fish> prefabs)
    {
        _unlockedFish.Clear();
        _unlockedFish.UnionWith(_initialFish);
        foreach (Fish prefab in prefabs)
        {
            if (_progress.IsFishRegistered(prefab.Type)) UnlockConnections(prefab.Type.Id);
        }
    }

    /// <summary>
    /// type의 자연 출현에 그래프 해금이 허용되는지 반환한다.
    /// 일반 종류는 초기 또는 연결 해금 상태를 사용하며 별도 등록이 필요한 특수 종류는 제외한다.
    /// </summary>
    public bool AllowsNaturalSpawn(FishType type)
    {
        if (!_connections.ContainsKey(type.Id)) return false;
        if (_specialFish.Contains(type.Id)) return false;
        return _unlockedFish.Contains(type.Id);
    }

    /// <summary>
    /// fishId에서 나가는 연결의 대상만 해금 집합에 추가한다.
    /// 연결된 대상 ID를 사용하며 재귀 해금과 자동 도감 등록은 수행하지 않는다.
    /// </summary>
    private void UnlockConnections(string fishId)
    {
        if (_connections.TryGetValue(fishId, out string[] targets)) _unlockedFish.UnionWith(targets);
    }

    /// <summary>
    /// 분류 목록과 모든 연결 대상이 정의된 노드인지 확인한다.
    /// 그래프 데이터를 사용하며 누락된 ID나 중복 분류가 있으면 초기화를 중단한다.
    /// </summary>
    private void ValidateConnections()
    {
        foreach (string fishId in _initialFish)
        {
            if (!_connections.ContainsKey(fishId) || _specialFish.Contains(fishId))
                throw new InvalidOperationException($"FishSpawnConditions: 초기 물고기 분류를 확인하세요. {fishId}");
        }
        foreach (string fishId in _specialFish)
        {
            if (!_connections.ContainsKey(fishId))
                throw new InvalidOperationException($"FishSpawnConditions: 특수 물고기 노드가 없습니다. {fishId}");
        }
        foreach (string[] targets in _connections.Values)
        {
            foreach (string target in targets)
            {
                if (!_connections.ContainsKey(target))
                    throw new InvalidOperationException($"FishSpawnConditions: 연결 대상 노드가 없습니다. {target}");
            }
        }
    }
}
