using UnityEngine;

public class BlackHoleEnding : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private GameFlowManager flowManager;
    [SerializeField] private PlayerProgress _progress;
    [SerializeField] private FishSpawner _spawner;
    [Tooltip("설치 블랙홀의 획득 여부를 확인할 투척용 프리팹")]
    [SerializeField] private Fish _fishPrefab;

    [Header("접촉 상태")]
    private bool _triggered;

    void Awake()
    {
        if (flowManager == null)
        {
            flowManager = FindFirstObjectByType<GameFlowManager>();
        }
        if (_progress == null) _progress = FindFirstObjectByType<PlayerProgress>();
        if (_spawner == null) _spawner = FindFirstObjectByType<FishSpawner>();
        if (_spawner != null)
        {
            _spawner.OnCleared += HandleCleared;
        }
    }

    void OnEnable()
    {
        _triggered = false;
    }

    void OnDestroy()
    {
        if (_spawner != null)
        {
            _spawner.OnCleared -= HandleCleared;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (_triggered) return;
        if (_spawner != null && !_spawner.CanCollectFish) return;
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsGameOver)
            return;
        _triggered = true;
        flowManager.StartEndingCinema();
    }

    /// <summary>
    /// 게임 재시작 시 접촉 상태를 초기화하여 다시 엔딩을 볼 수 있도록 한다.
    /// 입력값은 없으며, _triggered를 false로 변경한다.
    /// </summary>
    private void HandleCleared()
    {
        _triggered = false;
    }
}
