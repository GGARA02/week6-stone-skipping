using UnityEngine;

using TMPro;
using Unity.Cinemachine;

public class ShipAideController : MonoBehaviour
{
    private const string USED_KEY = "SkipStoneV2.ShipNormal.FirstThrow";
    private const string SUNK_KEY = "SkipStoneV2.ShipNormal.AideSunk";

    [Header("등록 및 대사")]
    [SerializeField] private Fish _shipPrefab;
    [SerializeField] private Fish _aidePrefab;
    [SerializeField] private AideDialogueProfile _dialogues;

    [Header("초상화 UI")]
    [SerializeField] private RectTransform _panel;
    [SerializeField] private AideCharacterAnimator _character;
    [SerializeField] private GameObject _noise;
    [SerializeField] private GameObject _portraitDialogue;
    [SerializeField] private TMP_Text _portraitText;
    private Vector2 _panelHome;

    [Header("침몰 연출")]
    [SerializeField, Min(0.1f)] private float _sinkDuration = 2.2f;
    [SerializeField, Min(0f)] private float _sinkDistance = 420f;
    private bool _sinking;
    private float _sinkElapsed;

    [Header("투척물 말풍선")]
    [SerializeField] private RectTransform _projectileBubble;
    [SerializeField] private TMP_Text _projectileText;
    [SerializeField, Min(0f)] private float _bubbleGap = 12f;
    private Canvas _canvas;
    private Camera _camera;
    private AidePortrait _projectilePortrait;
    private Transform _projectileTarget;
    private float _projectileRadius;

    [Header("게임 연결")]
    private PlayerController _player;
    private FishMeshGenerator _generator;
    private FishSpawner _spawner;
    private FishAbility _selectedAbility;
    private bool _hasUsedShip;
    private bool _shipFirstFlight;
    private bool _shipSunk;
    private int _failureCount;
    private int _lastObstacleFrame = -1;

    void Awake()
    {
        _panelHome = _panel.anchoredPosition;
        _canvas = GetComponent<Canvas>();
        _panel.gameObject.SetActive(false);
        _projectileBubble.gameObject.SetActive(false);
        _character.SetExpressionPreview(false);
    }

    void Start()
    {
        _player = FindFirstObjectByType<PlayerController>();
        _generator = FindFirstObjectByType<FishMeshGenerator>();
        _spawner = FindFirstObjectByType<FishSpawner>();
        if (_player == null || _generator == null || _spawner == null) return;

        _camera = Camera.main;
        _spawner.RegisterPrefab(_shipPrefab);
        _spawner.RegisterPrefab(_aidePrefab);
        _hasUsedShip = PlayerPrefs.GetInt(USED_KEY, 0) > 0;
        _shipSunk = PlayerPrefs.GetInt(SUNK_KEY, 0) > 0;
        if (_hasUsedShip && !_shipSunk)
            CompleteFirstUse();

        _generator.OnGenerated += HandleGenerated;
        _player.OnObstacleHit += HandleObstacleHit;
        _player.OnGameOver += HandleGameOver;
        _spawner.OnClearing += HandleClearing;
        // 메인 카메라는 CinemachineBrain이 LateUpdate에서 옮기므로 갱신이 끝난 뒤 말풍선 위치를 계산한다.
        CinemachineCore.CameraUpdatedEvent.AddListener(HandleCameraUpdated);
        HandleGenerated();
    }

    void Update()
    {
        if (!_sinking) return;
        _sinkElapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(_sinkElapsed / Mathf.Max(0.1f, _sinkDuration));
        float eased = progress * progress;
        _panel.anchoredPosition = _panelHome + Vector2.down * (_sinkDistance * eased);
        if (progress < 1f) return;
        _sinking = false;
        _panel.gameObject.SetActive(false);
    }

    /// <summary>
    /// CinemachineBrain이 메인 카메라를 갱신한 직후 투척물 옆에 말풍선을 맞춘다.
    /// brain은 사용하지 않으며, 투척물 위치와 갱신된 _camera로 말풍선의 표시 여부와 위치를 변경한다.
    /// </summary>
    private void HandleCameraUpdated(CinemachineBrain brain)
    {
        if (_projectileTarget == null || _player == null || _player.IsGameOver) return;
        Vector3 anchor = _projectileTarget.position + _camera.transform.right * _projectileRadius;
        Vector3 screen = _camera.WorldToScreenPoint(anchor);
        _projectileBubble.gameObject.SetActive(screen.z > 0f);
        if (screen.z <= 0f) return;

        Camera uiCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)_projectileBubble.parent, screen, uiCamera, out Vector2 position);
        _projectileBubble.anchoredPosition = position + Vector2.right * _bubbleGap;
    }

    void OnDestroy()
    {
        CinemachineCore.CameraUpdatedEvent.RemoveListener(HandleCameraUpdated);
        if (_generator != null) _generator.OnGenerated -= HandleGenerated;
        if (_player != null)
        {
            _player.OnObstacleHit -= HandleObstacleHit;
            _player.OnGameOver -= HandleGameOver;
        }
        if (_spawner != null) _spawner.OnClearing -= HandleClearing;
    }

    /// <summary>
    /// 실제 투척을 시작한 source가 현재 선택한 ShipNormal이면 첫 사용을 기록한다.
    /// 최초 사용 여부를 저장하고 이번 판의 실패 누적과 상호작용을 시작한다.
    /// </summary>
    public void BeginShipThrow(ShipNormalAbility source)
    {
        if (source != _selectedAbility || _hasUsedShip) return;
        _hasUsedShip = true;
        _shipFirstFlight = true;
        _failureCount = 0;
        PlayerPrefs.SetInt(USED_KEY, 1);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 선택된 source의 성공 판정에 맞는 표정과 대사를 표시한다.
    /// 최초 ShipNormal 또는 AideJjang 비행에 적용하며 AideJjang의 마지막 연타 구간에서는 Panic을 유지한다.
    /// </summary>
    public void ReactSuccess(FishAbility source)
    {
        if (!CanReact(source)) return;
        ShowExpression(source is AideProjectileAbility && _player.IsSliding
            ? AideExpression.Panic : _dialogues.PickSuccess());
    }

    /// <summary>
    /// 선택된 source의 실패 판정에 대응하는 표정과 대사를 표시한다.
    /// ShipNormal은 세 번째 실패에 침몰하고 AideJjang은 마지막 연타 구간에서 Panic, 그 외에는 무작위 실패 표정을 표시한다.
    /// </summary>
    public void ReactFailure(FishAbility source)
    {
        if (!CanReact(source)) return;
        if (source is AideProjectileAbility)
        {
            ShowExpression(_player.IsSliding ? AideExpression.Panic : _dialogues.PickFailure());
            return;
        }

        _failureCount++;
        if (_failureCount >= 3)
        {
            BeginSinking();
            return;
        }
        ShowExpression(_failureCount == 1 ? AideExpression.Embarrassed : AideExpression.Angry);
    }

    /// <summary>
    /// 선택된 AideJjang인 source의 비행 시작 또는 마지막 연타 구간에 Panic을 표시한다.
    /// 반응 가능한 현재 투척물의 표정과 대사를 함께 변경한다.
    /// </summary>
    public void ReactPanic(AideProjectileAbility source)
    {
        if (!CanReact(source)) return;
        ShowExpression(AideExpression.Panic);
    }

    /// <summary>
    /// source의 판정이 현재 상호작용 대상인지 확인한다.
    /// 현재 선택, 실제 비행, 게임오버와 침몰 상태를 사용해 반응 가능 여부를 반환한다.
    /// </summary>
    private bool CanReact(FishAbility source)
    {
        return source == _selectedAbility && _player.IsThrown && !_player.IsGameOver
            && !(source is ShipNormalAbility && _shipSunk)
            && (source is AideProjectileAbility || _shipFirstFlight);
    }

    /// <summary>
    /// 생성된 투척물의 능력을 확인하고 맞는 초상화 또는 말풍선을 표시한다.
    /// 같은 개체의 메시 재생성은 무시하며 새 선택이면 연결과 선택 대사를 갱신한다.
    /// </summary>
    private void HandleGenerated()
    {
        FishAbility ability = _generator.CurrentAbility;
        if (ReferenceEquals(ability, _selectedAbility)) return;
        DisconnectSelection();
        _selectedAbility = ability;
        _panel.anchoredPosition = _panelHome;
        _panel.gameObject.SetActive(false);
        _projectileBubble.gameObject.SetActive(false);

        if (ability is ShipNormalAbility ship)
        {
            ship.Bind(this);
            _panel.gameObject.SetActive(true);
            _noise.SetActive(_hasUsedShip);
            _portraitDialogue.SetActive(!_hasUsedShip);
            _character.SetConnectionStatus(!_hasUsedShip);
            if (!_hasUsedShip) ShowExpression(_dialogues.PickSelection());
        }
        else if (ability is AideProjectileAbility aide)
        {
            aide.Bind(this);
            _projectilePortrait = aide.GetComponent<AidePortrait>();
            _projectileTarget = aide.transform;
            _projectileRadius = aide.GetComponentInChildren<Renderer>().bounds.extents.magnitude;
            _projectileBubble.gameObject.SetActive(true);
            ShowExpression(_dialogues.PickSelection());
        }
    }

    /// <summary>
    /// 미획득 투척물 선택 시 기존 부관의 선택 연결과 표시를 해제한다.
    /// 입력값 없이 선택 능력, 말풍선 추적과 침몰 상태를 정리하고 초상화 패널과 말풍선을 숨긴다.
    /// </summary>
    public void ClearSelection()
    {
        DisconnectSelection();
        _panel.anchoredPosition = _panelHome;
        _panel.gameObject.SetActive(false);
        _projectileBubble.gameObject.SetActive(false);
    }

    /// <summary>
    /// 선택 능력과 연결된 컨트롤러 참조를 해제한다.
    /// 현재 선택을 사용해 투척물 추적, 침몰 애니메이션과 최초 비행 상태를 정리한다.
    /// </summary>
    private void DisconnectSelection()
    {
        if (_selectedAbility is ShipNormalAbility ship) ship.Bind(null);
        else if (_selectedAbility is AideProjectileAbility aide) aide.Bind(null);
        _selectedAbility = null;
        _projectileTarget = null;
        _projectilePortrait = null;
        _shipFirstFlight = false;
        _sinking = false;
        _lastObstacleFrame = -1;
    }

    /// <summary>
    /// 벽 등 장애물과 부딪힌 통지를 현재 능력의 실패로 전달한다.
    /// impactSpeed 통지를 사용하며 같은 프레임의 중복 충돌은 한 번만 반영한다.
    /// </summary>
    private void HandleObstacleHit(float impactSpeed)
    {
        if (_selectedAbility == null || _lastObstacleFrame == Time.frameCount) return;
        _lastObstacleFrame = Time.frameCount;
        ReactFailure(_selectedAbility);
    }

    /// <summary>
    /// reason으로 전달된 게임오버에 맞춰 UI를 종료한다.
    /// ShipNormal 최초 비행은 침몰하고 나머지는 Panel과 투척물 말풍선을 숨긴다.
    /// </summary>
    private void HandleGameOver(GameOverReason reason)
    {
        _projectileBubble.gameObject.SetActive(false);
        _projectileTarget = null;
        if (_shipFirstFlight && !_shipSunk) BeginSinking();
        else if (!_sinking) _panel.gameObject.SetActive(false);
    }

    /// <summary>
    /// 다음 판 정리 직전에 중단된 첫 비행을 완료하고 UI를 초기화한다.
    /// 현재 비행 상태를 사용해 Aide 해금이 이번 정리에서 반영되도록 예약한다.
    /// </summary>
    private void HandleClearing()
    {
        if (_shipFirstFlight && !_shipSunk) CompleteFirstUse();
        DisconnectSelection();
        _panel.gameObject.SetActive(false);
        _projectileBubble.gameObject.SetActive(false);
        _panel.anchoredPosition = _panelHome;
    }

    /// <summary>
    /// 최초 사용의 침몰 연출을 한 번 시작한다.
    /// 현재 초상화를 Panic과 OFFLINE으로 바꾸고 다음 판 Aide 출현을 예약한다.
    /// </summary>
    private void BeginSinking()
    {
        if (_shipSunk) return;
        ShowExpression(AideExpression.Panic);
        _character.SetConnectionStatus(false);
        _sinkElapsed = 0f;
        _sinking = true;
        CompleteFirstUse();
    }

    /// <summary>
    /// 첫 사용 완료와 AideJjang의 바다 출현 예약을 저장한다.
    /// 연결된 Aide 프리팹을 사용하며 현재 판에서는 출현시키지 않는다.
    /// </summary>
    private void CompleteFirstUse()
    {
        _shipSunk = true;
        PlayerPrefs.SetInt(SUNK_KEY, 1);
        _spawner.QueueSeaUnlock(_aidePrefab.Type);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// expression과 현재 사용 형태에 대응하는 표정·대사를 함께 적용한다.
    /// 선택이 AideJjang이면 3D Portrait와 추적 말풍선, 아니면 초상화 UI를 갱신한다.
    /// </summary>
    private void ShowExpression(AideExpression expression)
    {
        bool projectile = _selectedAbility is AideProjectileAbility;
        string text = _dialogues.GetDialogue(expression, projectile);
        if (projectile)
        {
            _projectilePortrait.SetExpression(expression.ToString());
            _projectileText.text = text;
        }
        else
        {
            _character.SetExpression(expression.ToString());
            _portraitText.text = text;
        }
    }
}
