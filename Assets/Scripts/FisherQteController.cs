using System.Collections;

using UnityEngine;

public class FisherQteController : MonoBehaviour
{
    private const string SUCCESS_KEY = "SkipStoneV2.FisherQteSuccess";
    private const string ATTEMPT_KEY = "SkipStoneV2.FisherQteAttempt";
    private const float DURATION = 5f;
    private const float SUCCESS_SPIN_DURATION = 0.3f;

    private enum QtePhase
    {
        Inactive,
        CameraMove,
        Ready,
        Input,
        Resolving,
    }

    [Header("참조")]
    private FishSpawner _spawner;
    private PlayerController _player;
    private Fish _fish;
    private CameraController _cameraController;

    [Header("QTE 설정")]
    [SerializeField, Min(0.1f)] private float _cameraMoveDuration = 1f;
    [SerializeField, Min(0.1f)] private float _readyDuration = 1f;
    [Tooltip("최초, 보통, 극한 난이도의 심볼 편도 이동 시간")]
    [SerializeField] private Vector3 _symbolTravelTimes = new Vector3(1.6f, 1.2f, 0.1f);
    [Tooltip("최초, 보통, 극한 난이도의 중앙 판정 영역 비율")]
    [SerializeField] private Vector3 _successZoneWidths = new Vector3(0.2f, 0.15f, 0.001f);

    [Header("QTE 상태")]
    private Vector3 _incomingVelocity;
    private Quaternion _rotationBeforeQte;
    private QtePhase _phase;
    private float _inputStartTime;
    private float _deadline;
    private float _previousTimeScale;
    private float _symbolTravelTime;
    private float _successZoneWidth;
    private int _startedFrame;
    private bool _isInitialized;

    [Header("화면 표시")]
    [SerializeField] private FisherQteView _uiPrefab;
    private FisherQteView _ui;

    public bool IsResolving => _phase == QtePhase.Resolving;

    void Update()
    {
        if (_phase == QtePhase.Inactive) return;
        if (_player.IsGameOver)
        {
            Cancel();
            return;
        }
        if (_phase != QtePhase.Input) return;
        float elapsed = Time.unscaledTime - _inputStartTime;
        float symbolPosition = Mathf.PingPong(elapsed / _symbolTravelTime, 1f);
        _ui.SetTiming(symbolPosition, (_deadline - Time.unscaledTime) / DURATION);
        if (Time.unscaledTime >= _deadline)
        {
            StartCoroutine(Resolve(false));
            return;
        }
        if (Time.frameCount == _startedFrame) return;
        if (!Managers.Input.JumpPressed) return;
        StartCoroutine(Resolve(Mathf.Abs(symbolPosition - 0.5f) <= _successZoneWidth * 0.5f));
    }

    void OnDestroy()
    {
        if (_ui != null) Destroy(_ui.gameObject);
    }

    void OnDisable()
    {
        Cancel();
        if (!_isInitialized) return;
        _spawner.OnClearing -= Cancel;
        _isInitialized = false;
    }

    /// <summary>
    /// fish와 spawner로 33Fisher, 플레이어와 카메라 참조를 연결한다.
    /// 재시작 시 취소 이벤트를 구독하고 초기화 상태를 저장한다.
    /// </summary>
    public void Initialize(Fish fish, FishSpawner spawner)
    {
        if (_isInitialized) _spawner.OnClearing -= Cancel;
        _fish = fish;
        _spawner = spawner;
        _player = spawner.PlayerBody.GetComponent<PlayerController>();
        // 메인 카메라는 CinemachineBrain이 움직이므로 추적용 시네머신 카메라의 컨트롤러를 찾는다.
        _cameraController = FindFirstObjectByType<CameraController>();
        _spawner.OnClearing += Cancel;
        _isInitialized = true;
    }

    /// <summary>
    /// 현재 접촉 상태와 시도 기록으로 최초 난이도 또는 반반 확률의 보통 및 극한 난이도를 정한다.
    /// 접촉 속도와 시간 배율을 저장하고 게임을 정지하며 시작 여부를 반환한다.
    /// </summary>
    public bool TryBegin()
    {
        if (_phase != QtePhase.Inactive || !_isInitialized || !isActiveAndEnabled || _fish.IsEjected
            || !_player.IsThrown || _player.IsGameOver || _spawner.ScreenCapture.IsCapturing
            || Time.timeScale <= 0f) return false;
        int successes = PlayerPrefs.GetInt(SUCCESS_KEY, 0);
        int attempts = PlayerPrefs.GetInt(ATTEMPT_KEY, successes > 0 ? 1 : 0);
        int difficulty = attempts == 0 ? 0 : Random.Range(1, 3);
        _symbolTravelTime = Mathf.Max(0.1f, _symbolTravelTimes[difficulty]);
        _successZoneWidth = Mathf.Clamp(_successZoneWidths[difficulty], 0.0001f, 1f);
        _incomingVelocity = _spawner.PlayerBody.linearVelocity;
        _rotationBeforeQte = transform.rotation;
        _previousTimeScale = Time.timeScale;
        if (_ui == null) _ui = Instantiate(_uiPrefab);
        _ui.Hide();
        _phase = QtePhase.CameraMove;
        Time.timeScale = 0f;
        _cameraController.BeginFisherQte(transform, _cameraMoveDuration);
        StartCoroutine(PrepareInput(successes));
        return true;
    }

    /// <summary>
    /// successes를 기록으로 사용해 카메라 이동 이후 Ready를 보여주고 입력 단계를 시작한다.
    /// 실제 시간 대기 후 시도 횟수를 저장하고 5초 제한 시간과 심볼 이동을 시작한다.
    /// </summary>
    private IEnumerator PrepareInput(int successes)
    {
        yield return new WaitForSecondsRealtime(_cameraMoveDuration);
        _phase = QtePhase.Ready;
        _ui.ShowReady();
        yield return new WaitForSecondsRealtime(_readyDuration);
        _inputStartTime = Time.unscaledTime;
        _deadline = _inputStartTime + DURATION;
        _startedFrame = Time.frameCount;
        int attempts = PlayerPrefs.GetInt(ATTEMPT_KEY, successes > 0 ? 1 : 0);
        PlayerPrefs.SetInt(ATTEMPT_KEY, attempts + 1);
        PlayerPrefs.Save();
        _ui.ShowTiming(_successZoneWidth, successes);
        _phase = QtePhase.Input;
    }

    /// <summary>
    /// 입력값 없이 진행 중인 QTE와 회전을 취소하고 화면, 카메라와 시간 배율을 복원한다.
    /// 저장한 회전과 초기화된 참조를 사용하며 성공 기록과 획득 상태는 변경하지 않는다.
    /// </summary>
    private void Cancel()
    {
        if (_phase == QtePhase.Inactive) return;
        StopAllCoroutines();
        _ui.Hide();
        _cameraController.EndFisherQte(false);
        transform.rotation = _rotationBeforeQte;
        _phase = QtePhase.Inactive;
        Time.timeScale = _previousTimeScale;
    }

    /// <summary>
    /// success로 결과를 처리하는 코루틴을 반환하고 UI와 대치 구도를 해제한다.
    /// 시간을 재개하며 성공 시 즉시 반사와 기록 저장 후 회전하고 실패 시 33Fisher를 획득한다.
    /// </summary>
    private IEnumerator Resolve(bool success)
    {
        _phase = QtePhase.Resolving;
        _ui.Hide();
        // 결과 스페이스가 일반 플레이 입력으로 전달되지 않도록 다음 Update까지 기다린다.
        yield return null;
        _cameraController.EndFisherQte(true);
        Time.timeScale = _previousTimeScale;
        if (success)
        {
            _player.ReboundFromFisher(_incomingVelocity);
            PlayerPrefs.SetInt(SUCCESS_KEY, PlayerPrefs.GetInt(SUCCESS_KEY, 0) + 1);
            PlayerPrefs.Save();
            yield return SpinBeforeRebound();
        }
        _phase = QtePhase.Inactive;
        if (!success) _spawner.HandleFishCaught(_fish);
    }

    /// <summary>
    /// 실제 경과 시간과 저장한 회전으로 수직축 기준 한 바퀴 도는 코루틴을 반환한다.
    /// 게임 진행 중 0.3초 동안 회전하고 마지막에 원래 회전을 복원한다.
    /// </summary>
    private IEnumerator SpinBeforeRebound()
    {
        float elapsed = 0f;
        while (elapsed < SUCCESS_SPIN_DURATION)
        {
            elapsed += Time.unscaledDeltaTime;
            float angle = 360f * Mathf.Clamp01(elapsed / SUCCESS_SPIN_DURATION);
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.up) * _rotationBeforeQte;
            yield return null;
        }
        transform.rotation = _rotationBeforeQte;
    }
}
