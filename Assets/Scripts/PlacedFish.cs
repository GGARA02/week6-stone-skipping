using UnityEngine;

// 맵에 놓여 있다가 던진 물고기가 닿으면 잡히는 물고기. 잡히면 숨고, 재시작할 때 다시 나타난다.
// 잡으면 지정한 물고기 프리팹 종류로 도감에 등록되어 그 물고기를 던질 수 있게 된다.
public class PlacedFish : MonoBehaviour
{
    [Header("참조")]
    [SerializeField]
    private FishSpawner _spawner;
    [Tooltip("잡았을 때 도감에 등록할 물고기 프리팹. FishSpawner의 물고기 목록에 있어야 던질 수 있다")]
    [SerializeField]
    private Fish _fishPrefab;

    [Header("재등장")]
    [Tooltip("켜면 잡힌 뒤 재시작할 때 다시 나타나고, 끄면 한 번 잡히면 다시 나타나지 않는다")]
    [SerializeField]
    private bool _respawnOnRestart = true;
    [Tooltip("켜면 저장된 도감에 획득 기록이 있는 동안 배치 개체를 숨긴다")]
    [SerializeField]
    private bool _hideAfterAcquisition;

    void Awake()
    {
        if (_spawner == null) _spawner = FindFirstObjectByType<FishSpawner>();
        if (_spawner == null) return;
        // 숨어 있는 동안에도 다시 나타날 수 있도록 활성 상태와 상관없이 구독을 유지한다.
        _spawner.OnCleared += HandleCleared;
        HideIfAcquired();
    }

    void OnEnable()
    {
        HideIfAcquired();
    }

    void OnDestroy()
    {
        if (_spawner != null) _spawner.OnCleared -= HandleCleared;
    }

    void OnTriggerEnter(Collider other)
    {
        // 같은 물리 단계에서 접촉이 겹쳐 두 번 잡히지 않도록 이미 숨었으면 무시한다.
        if (_spawner == null || !gameObject.activeSelf || other.attachedRigidbody != _spawner.PlayerBody) return;

        gameObject.SetActive(false);
        _spawner.HandlePlacedFishCaught(_fishPrefab.Type, transform.position);
    }

    /// <summary>
    /// respawnOnRestart로 재시작 시 자동 재등장 여부를 설정한다.
    /// 풀에서 관리되는 오브젝트의 활성 상태는 생성기가 직접 제어하게 한다.
    /// </summary>
    public void SetRespawnOnRestart(bool respawnOnRestart)
    {
        _respawnOnRestart = respawnOnRestart;
    }

    /// <summary>
    /// 재시작으로 물고기가 정리되면 잡혀서 숨었던 오브젝트를 다시 보이게 한다. 재등장을 끈 경우에는 숨은 채로 둔다.
    /// _respawnOnRestart를 사용하며, 오브젝트 활성 상태를 변경한다.
    /// </summary>
    private void HandleCleared()
    {
        if (HideIfAcquired()) return;
        if (!_respawnOnRestart) return;

        gameObject.SetActive(true);
    }

    /// <summary>
    /// 영구 숨김 설정과 지정 프리팹의 저장된 획득 기록을 확인해 배치 개체를 숨긴다.
    /// _hideAfterAcquisition, _fishPrefab과 진행 기록을 사용하며 숨긴 경우 true를 반환한다.
    /// </summary>
    private bool HideIfAcquired()
    {
        if (!_hideAfterAcquisition || _spawner == null || !_spawner.Progress.IsFishRegistered(_fishPrefab.Type))
            return false;

        gameObject.SetActive(false);
        return true;
    }
}
