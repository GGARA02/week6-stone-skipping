using System.Collections.Generic;

using UnityEngine;

using Newtonsoft.Json.Linq;

[DisallowMultipleComponent]
public sealed class SeaSpawnRegistry : MonoBehaviour
{
    private const string SEA_KEY_PREFIX = "SkipStoneV2.Sea.";
    private const string CAPTURE_LIMIT_KEY_PREFIX = "SkipStoneV2.SeaCaptureLimit.";

    [Header("바다 출현 등록")]
    private readonly HashSet<string> _requiredTypes = new HashSet<string>();
    private readonly HashSet<string> _unlockedTypes = new HashSet<string>();
    private readonly HashSet<string> _pendingTypes = new HashSet<string>();
    private readonly Dictionary<string, int> _captureLimits = new Dictionary<string, int>();
    private readonly Dictionary<string, int> _pendingCaptureLimits = new Dictionary<string, int>();

    /// <summary>
    /// catalogEntries의 등록 정책과 기존 저장 키를 읽어 이번 판의 바다 출현 상태를 준비한다.
    /// 종류별 등록 요구와 해금 목록을 복원하고 이전 예약 목록은 비운다.
    /// </summary>
    public void Initialize(JObject catalogEntries)
    {
        _requiredTypes.Clear();
        _unlockedTypes.Clear();
        _pendingTypes.Clear();
        _captureLimits.Clear();
        _pendingCaptureLimits.Clear();
        foreach (JProperty entry in catalogEntries.Properties())
        {
            if (entry.Value.Value<bool?>("requiresSeaRegistration") == true)
                _requiredTypes.Add(entry.Name);
            if (entry.Value.Value<bool?>("rearmOnCutWaterEntry") == true)
                _captureLimits.Add(entry.Name, PlayerPrefs.GetInt(CAPTURE_LIMIT_KEY_PREFIX + entry.Name, 1));
            if (PlayerPrefs.GetInt(SEA_KEY_PREFIX + entry.Name, 0) > 0)
                _unlockedTypes.Add(entry.Name);
        }
    }

    /// <summary>
    /// fishId가 자연 출현 전에 바다 등록을 요구하는 종류인지 반환한다.
    /// JSON에서 읽은 정책을 사용하며 등록 상태는 변경하지 않는다.
    /// </summary>
    public bool RequiresRegistration(string fishId)
    {
        return _requiredTypes.Contains(fishId);
    }

    /// <summary>
    /// fishId가 이번 판부터 자연 출현할 수 있도록 등록되었는지 반환한다.
    /// 다음 판에 반영할 예약은 포함하지 않으며 등록 상태는 변경하지 않는다.
    /// </summary>
    public bool IsUnlocked(string fishId)
    {
        return _unlockedTypes.Contains(fishId);
    }

    /// <summary>
    /// fishId가 이미 등록되었거나 다음 판 등록을 예약했는지 반환한다.
    /// 반복 사출 제한에 사용할 두 목록을 확인하며 상태는 변경하지 않는다.
    /// </summary>
    public bool HasRegistration(string fishId)
    {
        return _unlockedTypes.Contains(fishId) || _pendingTypes.Contains(fishId);
    }

    /// <summary>
    /// fishId가 획득 후 새 절단 조각의 입수로 다시 출현해야 하는 종류인지 반환한다.
    /// JSON에서 읽은 획득 제한 정책을 사용하며 저장 및 예약 상태는 변경하지 않는다.
    /// </summary>
    public bool RequiresCutToRearm(string fishId)
    {
        return _captureLimits.ContainsKey(fishId);
    }

    /// <summary>
    /// fishId의 fishCount가 이번 run에 허용된 누적 획득 한도보다 작은지 반환한다.
    /// 절단 입수 정책이 없는 종류는 허용하며 다음 run의 예약 한도는 사용하지 않는다.
    /// </summary>
    public bool AllowsCapture(string fishId, int fishCount)
    {
        return !_captureLimits.TryGetValue(fishId, out int limit) || fishCount < limit;
    }

    /// <summary>
    /// fishId의 입수를 받아 다음 run 출현을 예약하고 절단 재등록 종류는 새 조각만 허용한다.
    /// isCutPiece와 fishCount를 사용해 다음 획득 한도를 저장하며 현재 run의 한도는 유지한다.
    /// </summary>
    public void RegisterWaterEntry(string fishId, bool isCutPiece, int fishCount)
    {
        if (!RequiresRegistration(fishId)) return;
        if (RequiresCutToRearm(fishId))
        {
            if (!isCutPiece) return;
            int limit = fishCount + 1;
            _pendingCaptureLimits[fishId] = limit;
            PlayerPrefs.SetInt(CAPTURE_LIMIT_KEY_PREFIX + fishId, limit);
            PlayerPrefs.Save();
        }
        Queue(fishId);
    }

    /// <summary>
    /// fishId의 다음 판 출현을 입수 또는 별도 연출 경로에서 한 번만 예약한다.
    /// 이번 판 해금 목록은 유지하고 예약 및 기존 PlayerPrefs 키를 저장한다.
    /// </summary>
    public void Queue(string fishId)
    {
        if (_unlockedTypes.Contains(fishId) || !_pendingTypes.Add(fishId)) return;
        PlayerPrefs.SetInt(SEA_KEY_PREFIX + fishId, 1);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 입력값 없이 예약된 종류와 획득 한도를 새 run의 자연 출현 상태에 반영한다.
    /// 해금 및 한도 목록에 예약을 병합하고 다음 run 예약을 비운다.
    /// </summary>
    public void CommitPending()
    {
        _unlockedTypes.UnionWith(_pendingTypes);
        _pendingTypes.Clear();
        foreach (KeyValuePair<string, int> entry in _pendingCaptureLimits)
            _captureLimits[entry.Key] = entry.Value;
        _pendingCaptureLimits.Clear();
    }
}
