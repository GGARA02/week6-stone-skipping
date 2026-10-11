using UnityEngine;

public class BlackHoleEnding : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private GameFlowManager flowManager;
    [SerializeField] private PlayerProgress _progress;
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
        HideIfAcquired();
    }

    void OnEnable()
    {
        _triggered = false;
        HideIfAcquired();
    }

    void OnTriggerEnter(Collider other)
    {
        if (_triggered) return;
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;
        _triggered = true;
        flowManager.StartEndingCinema();
        Destroy(gameObject);
    }

    /// <summary>
    /// 투척용 블랙홀 프리팹의 저장된 획득 기록을 확인해 설치 블랙홀을 숨긴다.
    /// _progress와 _fishPrefab을 사용하며 획득된 경우 개체를 비활성화하고 재접촉을 차단한다.
    /// </summary>
    private void HideIfAcquired()
    {
        if (!_progress.IsFishRegistered(_fishPrefab.Type)) return;

        _triggered = true;
        gameObject.SetActive(false);
    }
}
