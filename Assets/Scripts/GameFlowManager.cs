using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

using Unity.Cinemachine;

using Newtonsoft.Json.Linq;

// 던지기, 비행, 도감, 정산, 재시작 흐름과 HUD를 담당한다.
// 게임오버 시 씬을 다시 불러오지 않고 제자리에서 리셋하고 새 물고기를 만든다.
public class GameFlowManager : MonoBehaviour
{
    private enum State
    {
        Ready,
        Flying,
        GameOver,
    }

    private const string BEST_SKIPS_KEY = "SkipStoneV2.BestSkips";
    private const string BEST_DISTANCE_KEY = "SkipStoneV2.BestDistance";
    private const float POPUP_DURATION = 1.1f;
    private const float RESTART_HINT_IDLE_DELAY = 3f;
    private const int DEFAULT_FISH_INDEX = -1;
    // 게임오버 직후 SPACE 연타로 바로 재시작되지 않게 기다리는 시간(초)
    private const float RETRY_INPUT_DELAY = 0.6f;
    private const float CATALOG_NAVIGATION_DELAY = 0.3f;
    private const float CATALOG_NAVIGATION_INTERVAL = 0.12f;
    private const string BLACKHOLE_FISH_ID = "blackhole";
    private const string CREDIT_FISH_ID = "endingcredit";
    private const string MONSTER_FISH_ID = "monster";
    private const string SKYBOX_FISH_ID = "skybox";
    private const string FISH_KEY_PREFIX = "SkipStoneV2.Fish.";
    private const float CINEMATIC_ASPECT = 21f / 9f;
    private const int NOTE_TEXTURE_SIZE = 256;
    // 리듬 노트 고리 두께 (반지름 대비 비율)
    private const float NOTE_RING_THICKNESS = 0.1f;

    [Header("참조")]
    [SerializeField]
    private PlayerController _playerController;
    [SerializeField]
    private CameraController _cameraController;
    [FormerlySerializedAs("_stoneGenerator")]
    [SerializeField]
    private FishMeshGenerator _fishGenerator;
    [SerializeField]
    private PlayerProgress _progress;
    [SerializeField]
    private FishSpawner _fishSpawner;
    [SerializeField]
    private EnvironmentController _environmentController;
    private InputManager _input;
    private Rumble _rumble;

    [Header("진동")]
    [Tooltip("도감 신규 등록 진동 (x 저주파, y 고주파)")]
    [SerializeField]
    private Vector2 _registerRumble = new Vector2(0.03f, 0.08f);

    [Header("흐름")]
    private State _state;
    private GameOverReason _gameOverReason;
    private float _gameOverTime;
    private int _bestSkips;
    private float _bestDistance;
    // -1은 기본 물고기, 0 이상은 FishSpawner.FishTypes 인덱스
    private int _projectileIndex = DEFAULT_FISH_INDEX;
    private int _perfectCount;
    private int _goodCount;
    private int _missCount;
    private bool _screenCaptureTriggered;
    // 게임을 켠 뒤 한 번이라도 던졌는지. 던지기 전 재시작은 날씨를 다시 뽑지 않는다.
    private bool _hasThrown;
    private bool _pauseOpen;
    private bool _resetConfirmOpen;
    // 일시정지 메뉴에서 고른 버튼 순서와, 길게 누를 때 한 칸씩만 움직이도록 직전 프레임의 위아래 입력 방향.
    private int _pauseSelection;
    private int _pauseNavigateDirection;
    private bool _catalogOpen;
    private bool _catalogCapturePending;
    private FishType _catalogSelectedFish;
    private JObject _catalogEntries;
    private Vector2 _catalogScroll;
    private Vector2 _catalogDetailScroll;
    private Vector2Int _catalogNavigateDirection;
    private float _catalogNavigateTime;
    private bool _catalogScrollToSelection;
    private Vector2 _resultScroll;
    private readonly List<int> _ownedOptions = new List<int>();
    private readonly List<int> _selectionOptions = new List<int>();
    private readonly List<int> _catalogOptions = new List<int>();
    private readonly Dictionary<string, int> _catalogOrder = new Dictionary<string, int>();
    private readonly List<FishType> _newFish = new List<FishType>();
    private bool _endingCinemaRunning;
    private GameObject _endingAlienInstance;
    private Coroutine _endingCinemaCoroutine;

    [Header("HUD")]
    [SerializeField]
    private bool _showHud = true;
    [SerializeField]
    private bool _showDebug = true;
    [Tooltip("수면에 닿기 이 시간(초) 전부터 SPACE 안내를 띄운다")]
    [SerializeField]
    private float _spaceHintLeadTime = 0.6f;
    [Tooltip("판정 문구 세로 위치 (화면 높이 비율). 물고기가 화면 가운데 있으므로 위쪽에 둔다")]
    [Range(0f, 1f)]
    [SerializeField]
    private float _popupHeight = 0.13f;
    [Tooltip("SPACE 안내 세로 위치 (화면 높이 비율)")]
    [Range(0f, 1f)]
    [SerializeField]
    private float _spaceHintHeight = 0.8f;
    [Tooltip("SPACE 리듬 노트 중심 위치 (화면 비율, 왼쪽 위가 0). 물고기가 화면 가운데 있으므로 중앙 왼쪽에 둔다")]
    [SerializeField]
    private Vector2 _notePosition = new Vector2(0.3f, 0.5f);
    [Tooltip("SPACE 리듬 노트 원의 반지름 (1080p 기준 픽셀)")]
    [SerializeField]
    private float _noteRadius = 64f;
    [Tooltip("SPACE 안내가 뜰 때 접근 원의 크기 (노트 원 대비 배율). 수면에 닿는 순간 1배가 되어 노트 원과 겹친다")]
    [SerializeField]
    private float _approachStartScale = 3f;
    [Tooltip("판정 성공 시 노트 원이 퍼지며 사라지는 시간(초)")]
    [SerializeField]
    private float _noteHitDuration = 0.25f;
    private Texture2D _noteDiscTexture;
    private Texture2D _noteRingTexture;
    private Color _noteHitColor;
    private float _noteHitTime = -10f;
    private string _popupText;
    private Color _popupColor;
    private float _popupTime = -10f;
    private string _fishPopupText;
    private float _fishPopupTime = -10f;
    private GUIStyle _bigStyle;
    private GUIStyle _mediumStyle;
    private GUIStyle _smallStyle;
    private GUIStyle _centerStyle;
    private GUIStyle _centerSubStyle;
    private GUIStyle _popupStyle;
    private GUIStyle _noteStyle;
    private GUIStyle _debugStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _previewStyle;
    private GUIStyle _catalogDetailStyle;
    private GUIStyle _selectionDetailStyle;
    private float _styleScale = -1f;
    private FishSelectionPreview _fishSelectionPreview;
    private FishSelectionPreview _ownedFishSelectionPreview;
    private readonly List<Renderer> _hiddenProjectileRenderers = new List<Renderer>();
    private readonly List<Canvas> _hiddenProjectileCanvases = new List<Canvas>();

    [Header("재시작 안내")]
    [Range(0f, 1f)]
    [SerializeField]
    private float _restartHintHeight = 0.9f;
    private float _lastRestartHintInputTime;
    private GUIStyle _restartHintStyle;

    [Header("블랙홀 방향 안내")]
    [SerializeField] private BlackHoleEnding _blackHoleTarget;
    [SerializeField] private GameObject _blackHoleArrowPrefab;
    [SerializeField] private Material _blackHoleArrowWarningMaterial;
    [SerializeField, Range(0f, 180f)] private float _blackHoleArrowMinAngle = 45f;
    [SerializeField, Min(5f)] private float _blackHoleArrowDuration = 5f;
    [SerializeField, Min(0f)] private float _blackHoleArrowHeight = 2f;
    [SerializeField, Min(0.01f)] private float _blackHoleArrowScale = 1.2f;
    private Transform _blackHoleArrow;
    private float _blackHoleArrowEndTime = float.NegativeInfinity;
    private Renderer[] _blackHoleArrowRenderers;
    private Material _blackHoleArrowNormalMaterial;
    private Fish _blackHoleArrowFish;
    private float _blackHoleArrowStateTime;
    private bool _blackHoleArrowWarning;
    private bool _blackHoleArrowDetached;

    [Header("물 밖 낙하")]
    [Tooltip("물 밖으로 나가 게임오버된 뒤 카메라가 떨어지는 플레이어를 비추다가 스카이박스를 얻기까지 기다리는 실제 시간(초). 이동안 재시작을 받지 않는다")]
    [SerializeField, Min(0f)] private float _skyboxAcquireDelay = 5f;
    [Tooltip("스카이박스를 얻은 뒤에도 재시작을 받지 않고 더 기다리는 실제 시간(초)")]
    [SerializeField, Min(0f)] private float _skyboxRestartLockAfterAcquire = 2f;
    private Bounds _waterBounds;
    // 물 밖으로 떨어지기 시작해 스카이박스를 얻고 잠시 뒤까지 켜져 있으며, 켜져 있는 동안 재시작 입력을 받지 않는다.
    private bool _skyboxFallPending;

    [Header("화면 투척 전환")]
    [SerializeField, Min(0.1f)] private float _panelShrinkDuration = 0.65f;
    private bool _screenThrowPending;
    private RenderTexture _menuPopupFrame;
    private Rect _menuPopupRect;

    [Header("엔딩 크레딧 UI")]
    [SerializeField] private EndingCreditUI _endingCredit;

    [Header("시네마틱 연출")]
    [Tooltip("오프닝 연출에서 응시할 엔딩 블랙홀 트랜스폼")]
    [SerializeField] private Transform _endingBlackHole;
    [Tooltip("오프닝 시작 시 블랙홀과의 근접 거리")]
    [SerializeField] private float _openingCloseDistance;
    [Tooltip("오프닝 시작 시 카메라 높이(Y축) 추가 오프셋")]
    [SerializeField] private float _openingHeightOffset;
    [Tooltip("오프닝 시작 시 카메라 X축 추가 회전 각도(양수일 때 아래를 내려다봄)")]
    [SerializeField] private float _openingPitchOffset;
    [Tooltip("오프닝 시작 후 카메라 후진 전 정지 대기 시간(초)")]
    [SerializeField] private float _openingHoldDuration = 1.0f;
    [Tooltip("오프닝 카메라 후진 이동 시간(초)")]
    [SerializeField] private float _openingTravelDuration = 3.0f;
    [Tooltip("블랙홀 흡수 연출 시간(초)")]
    [SerializeField] private float _blackHoleSuctionDuration = 1.7f;
    [Tooltip("엔딩 시네마 동안 21:9 레터박스 연출을 엔딩 크레딧까지 유지할지 여부")]
    [SerializeField] private bool _maintainCinemaDuringCredits = true;
    [Tooltip("21:9 레터박스 전환 시간 (초)")]
    [SerializeField] private float _cinemaTransitionDuration = 0.6f;
    private GameObject _suctionOverlay;
    private RenderTexture _suctionFrame;
    private Material _suctionMaterial;
    private GameObject _letterboxOverlay;
    private RectTransform _topLetterboxBar;
    private RectTransform _bottomLetterboxBar;
    private RectTransform _leftLetterboxBar;
    private RectTransform _rightLetterboxBar;
    private Coroutine _letterboxCoroutine;
    private Coroutine _openingCinemaCoroutine;
    private bool _openingCinemaRunning;
    private Rect _originalCameraRect = new Rect(0f, 0f, 1f, 1f);

    void Start()
    {
        LockCursor(true);

        _input = Managers.Input;
        _rumble = GetComponent<Rumble>();
        if (_blackHoleTarget == null) _blackHoleTarget = FindFirstObjectByType<BlackHoleEnding>(FindObjectsInactive.Include);
        if (_blackHoleArrowPrefab == null) _blackHoleArrowPrefab = Resources.Load<GameObject>("Prefabs/BlackHoleDirectionArrow");
        _blackHoleArrow = Instantiate(_blackHoleArrowPrefab, transform).transform;
        _blackHoleArrow.gameObject.SetActive(false);
        _blackHoleArrowRenderers = _blackHoleArrow.GetComponentsInChildren<Renderer>(true);
        _blackHoleArrowNormalMaterial = _blackHoleArrowRenderers[0].sharedMaterial;
        if (_blackHoleArrowWarningMaterial == null)
            _blackHoleArrowWarningMaterial = Resources.Load<Material>("Prefabs/BlackHoleDirectionArrowWarning");
        _blackHoleArrowFish = Resources.Load<GameObject>("Prefabs/Fish/Fish_Arrow").GetComponent<Fish>();

        _playerController.Initialize(_fishSpawner);
        _waterBounds = _playerController.WaterBounds;
        _playerController.OnSkip += HandleSkip;
        _playerController.OnJudge += HandleJudge;
        _playerController.OnSlidePush += HandleSlidePush;
        _playerController.OnGameOver += HandleGameOver;
        _playerController.OnWaterContact += HandleWaterContact;
        _fishSpawner.OnFishCaught += HandleFishCaught;
        _progress.OnFishRegistered += HandleFishRegistered;
        _fishSpawner.ScreenCapture.OnUnlocked += HandleScreenUnlocked;

        if (_endingCredit != null)
        {
            _endingCredit.OnEndingCompleted += HandleEndingCompleted;
            _endingCredit.OnEndingSkipped += HandleEndingSkipped;
            _endingCredit.gameObject.SetActive(false);
        }
        if (Camera.main != null)
        {
            _originalCameraRect = Camera.main.rect;
        }
        _cameraController.Initialize(_fishGenerator);

        _bestSkips = PlayerPrefs.GetInt(BEST_SKIPS_KEY, 0);
        _bestDistance = PlayerPrefs.GetFloat(BEST_DISTANCE_KEY, 0f);
        _state = State.Ready;
        // 게임을 켠 첫 판은 맑은 낮으로 시작한다.
        if (_environmentController != null) _environmentController.Roll(SelectedFish(), false);
        _fishSelectionPreview = new FishSelectionPreview();
        _ownedFishSelectionPreview = new FishSelectionPreview(true);
        _ownedFishSelectionPreview.PrepareDefaultFishTexture(_fishGenerator.GetComponent<MeshFilter>().sharedMesh,
            _fishGenerator.GetComponent<MeshRenderer>().sharedMaterials);
        _catalogEntries = JObject.Parse(Resources.Load<TextAsset>("FishCatalog").text);
        foreach (JProperty entry in _catalogEntries.Properties())
        {
            _catalogOrder.Add(entry.Name, _catalogOrder.Count);
        }
    }

    void OnDestroy()
    {
        _playerController.OnSkip -= HandleSkip;
        _playerController.OnJudge -= HandleJudge;
        _playerController.OnSlidePush -= HandleSlidePush;
        _playerController.OnGameOver -= HandleGameOver;
        _playerController.OnWaterContact -= HandleWaterContact;
        _fishSpawner.OnFishCaught -= HandleFishCaught;
        _progress.OnFishRegistered -= HandleFishRegistered;
        _fishSpawner.ScreenCapture.OnUnlocked -= HandleScreenUnlocked;
        // 노트 텍스처는 HUD를 처음 그릴 때 만들어지므로 한 번도 그리지 않았으면 없다.
        if (_noteRingTexture != null)
        {
            Destroy(_noteDiscTexture);
            Destroy(_noteRingTexture);
        }
        if (_endingCredit != null)
        {
            _endingCredit.OnEndingCompleted -= HandleEndingCompleted;
            _endingCredit.OnEndingSkipped -= HandleEndingSkipped;
        }
        if (_openingCinemaCoroutine != null)
        {
            StopCoroutine(_openingCinemaCoroutine);
            _openingCinemaCoroutine = null;
        }
        if (Camera.main != null)
        {
            Camera.main.rect = _originalCameraRect;
        }
        if (_letterboxOverlay != null)
        {
            Destroy(_letterboxOverlay);
        }
        ReleaseScreenSuction();
        _fishSelectionPreview.Dispose();
        _ownedFishSelectionPreview.Dispose();
    }

    void Update()
    {
        if (_state != State.Flying || _playerController.IsControlling || _pauseOpen
            || _endingCinemaRunning || _screenThrowPending || _catalogCapturePending
            || _fishSpawner.ScreenCapture.IsCapturing || Time.timeScale == 0f)
        {
            _lastRestartHintInputTime = Time.unscaledTime;
        }
        _fishSpawner.RecordDiscoveryDistance(_playerController.Distance);
        // 엔딩 시네마 및 오프닝 연출 중에는 플레이어 및 게임 흐름 키 입력을 전면 차단한다.
        if (_endingCinemaRunning || _openingCinemaRunning) return;

        if (_screenThrowPending || _catalogCapturePending || _fishSpawner.ScreenCapture.IsCapturing || Time.timeScale == 0f) return;
        if (_pauseOpen)
        {
            UpdatePauseMenu();
            return;
        }
        if (_endingCredit != null && _endingCredit.gameObject.activeInHierarchy)
        {
            if (_endingCredit.CanSkip && _input.JumpPressed)
            {
                ThrowCreditFish();
                return;
            }
            return;
        }
        if (_state == State.Flying) CheckWaterEdge();
        if (_state == State.GameOver)
        {
            if ((_input.RestartPressed || _input.JumpPressed) && !_skyboxFallPending
                && Time.unscaledTime - _gameOverTime >= RETRY_INPUT_DELAY) Restart();
            return;
        }
        if (_state == State.Ready && _input.CatalogPressed)
        {
            _catalogOpen = !_catalogOpen;
            _catalogNavigateDirection = Vector2Int.zero;
            _catalogScrollToSelection = _catalogOpen;
            LockCursor(!_catalogOpen);
            return;
        }
        if (_catalogOpen)
        {
            if (_input.JumpPressed)
            {
                StartCoroutine(CapturePanelAndThrow(_fishSpawner.CatalogCapture, "catalog"));
                return;
            }
            if (_input.CancelPressed)
            {
                _catalogOpen = false;
                LockCursor(true);
            }
            if (_catalogOpen) UpdateCatalogNavigation();
            return;
        }
        if (_input.CancelPressed)
        {
            // 준비 화면 안내의 menu 키(Cancel 액션)로 일시정지 메뉴를 연다.
            if (_state == State.Ready)
            {
                OpenPauseMenu();
                return;
            }
            LockCursor(Cursor.lockState != CursorLockMode.Locked);
        }
        if (_input.RestartPressed)
        {
            if (_state == State.Flying)
            {
                _playerController.GameOver(GameOverReason.Stopped);
            }
            else
            {
                Restart();
            }
            return;
        }

        // SPACE와 게임패드 A는 Jump 액션에 묶여 있다.
        bool jumpPressed = _input.JumpPressed;
        if (_state != State.Ready) return;

        if (_input.PreviousPressed)
        {
            CycleProjectile(-1);
        }
        if (_input.NextPressed)
        {
            CycleProjectile(1);
        }

        if (jumpPressed)
        {
            Throw();
            return;
        }
    }

    void LateUpdate()
    {
        UpdateBlackHoleArrow();
        if (_showHud && _state == State.GameOver)
        {
            foreach (FishType fish in _newFish)
            {
                if (fish.Id == "screen" || fish.Id == "catalog" || fish.Id == "menu") continue;
                Fish prefab = _fishSpawner.GetFishPrefab(fish);
                if (prefab != null)
                    _ownedFishSelectionPreview.PrepareTexture(prefab, fish.Id == "water_block" || fish.Id == "week4arrow");
            }
            // 획득 화면에서도 물결과 화살 아이들 모션이 보이도록 다시 그린다.
            _ownedFishSelectionPreview.RefreshAnimatedTextures();
            return;
        }
        if (!_showHud || _state != State.Ready) return;
        // 도감에서 고른 칸이나 선택창 가운데 칸이 되면 하이라이트된 것으로 보고 "새로운!"을 지운다.
        FishType highlighted = _catalogOpen ? _catalogSelectedFish : _pauseOpen ? null : SelectedFish();
        if (highlighted != null) _progress.MarkFishSeen(highlighted);
        RefreshOwnedOptions();
        for (int i = 0; i < _fishSpawner.FishTypes.Count; i++)
        {
            FishType fish = _fishSpawner.FishTypes[i];
            if (fish.Id != "screen" && fish.Id != "catalog" && fish.Id != "menu"
                && _fishSpawner.GetCatalogState(fish) != FishCatalogState.Undiscovered)
            {
                FishSelectionPreview previews = _progress.IsFishRegistered(fish) ? _ownedFishSelectionPreview : _fishSelectionPreview;
                previews.PrepareTexture(_fishSpawner.FishPrefabs[i], fish.Id == "water_block" || fish.Id == "week4arrow");
            }
        }
        // 물 블록과 Week4 화살 미리보기는 선택 창과 도감이 보이는 동안 매 프레임 다시 그린다.
        // 물은 물결이 흐르고, 화살은 아이들 모션으로 위아래로 흔들려 트레일 형상이 보인다.
        // 미획득 화살도 회색 트레일로 흔들어야 보이므로 미획득 미리보기도 다시 그린다.
        _ownedFishSelectionPreview.RefreshAnimatedTextures();
        _fishSelectionPreview.RefreshAnimatedTextures();
    }

    void OnGUI()
    {
        // 도감이나 메뉴를 캡처하는 동안에는 그 화면을 계속 그려 캡처에 담는다.
        if (_screenThrowPending && !_catalogOpen && !_pauseOpen) return;
        if (_fishSpawner.ScreenCapture.IsCapturing && _fishSpawner.ScreenCapture.HasFrame) return;

        // 물고기 획득 알림은 시네마 중이거나 정산 중이어도 항상 화면에 표시한다.
        DrawFishPopup();

        if (_pauseOpen)
        {
            EnsureStyles();
            // 메뉴 투척을 요청한 프레임에는 화면과 별도로 배경 없는 팝업을 렌더 텍스처에 그린다.
            if (_menuPopupFrame != null && Event.current.type == EventType.Repaint) RenderMenuPopup(_styleScale);
            DrawPauseMenu(Screen.width, Screen.height, _styleScale);
            return;
        }
        if (!_showHud) return;

        EnsureStyles();
        float s = _styleScale;
        float width = Screen.width;
        float height = Screen.height;
        if (_catalogOpen)
        {
            DrawCatalog(width, height, s);
            return;
        }

        ShadowLabel(new Rect(24f * s, 16f * s, 600f * s, 64f * s), $"건너뛰기  {_playerController.SkipCount}", _bigStyle, Color.white);
        ShadowLabel(new Rect(24f * s, 76f * s, 600f * s, 44f * s), $"{_playerController.Distance:0.0} m", _mediumStyle, Color.white);
        ShadowLabel(new Rect(24f * s, 118f * s, 800f * s, 36f * s), $"최상의  {_bestSkips} 건너뛰기 / {_bestDistance:0.0} m", _smallStyle, new Color(1f, 1f, 1f, 0.75f));

        if (_state == State.Ready)
        {
            DrawReady(width, height, s);
        }
        else if (_state == State.GameOver)
        {
            DrawShop(width, height, s);
        }
        else if (_playerController.IsSliding && _playerController.SlidePower > 0f)
        {
            // 미끄러지는 동안은 남은 밀기 힘만큼 진하게 연타 안내를 띄운다.
            Color slideColor = new Color(1f, 1f, 1f, 0.35f + 0.65f * _playerController.SlidePower);
            ShadowLabel(new Rect(0f, height * _spaceHintHeight, width, 80f * s), "발열 시간!", _centerStyle, slideColor);
        }
        else
        {
            EnsureNoteTextures();
            Vector2 noteCenter = new Vector2(width * _notePosition.x, height * _notePosition.y);
            if (_playerController.CanJudge && _playerController.TimeToWaterImpact <= _spaceHintLeadTime)
            {
                DrawSpaceNote(noteCenter, s);
            }
            DrawSpaceNoteHit(noteCenter, s);
        }
        if (_state == State.Flying && !_endingCinemaRunning && !_playerController.IsControlling
            && Time.unscaledTime - Mathf.Max(_lastRestartHintInputTime, _input.LastInputTime) >= RESTART_HINT_IDLE_DELAY)
        {
            ShadowLabel(new Rect(0f, height * _restartHintHeight, width, 80f * s),
                _input.ReplaceBindingName("재시작 {Restart}키를 눌러"), _restartHintStyle, Color.white);
        }

        // 게임오버 화면에서는 제목과 겹치지 않도록 팝업을 그리지 않는다.
        float popupAge = Time.unscaledTime - _popupTime;
        if (_state != State.GameOver && _popupText != null && popupAge < POPUP_DURATION)
        {
            float t = popupAge / POPUP_DURATION;
            Color color = _popupColor;
            color.a = 1f - t * t;
            float rise = 30f * s * t;
            ShadowLabel(new Rect(0f, height * _popupHeight - rise, width, 80f * s), _popupText, _popupStyle, color);
        }

        if (_showDebug)
        {
            string debug =
                $"속력 {_playerController.Speed:0.0} m/s\n" +
                $"기울기 {_playerController.CurrentPitch:0} / {_playerController.TargetPitch:0} 도  구르기 {_playerController.TargetRoll:0}\n" +
                $"회전 {_playerController.SpinRate:0.0} rad/s  안정 {_playerController.Stability:0.00}\n" +
                $"씨앗 {_playerController.FishSeed}";
            ShadowLabel(new Rect(width - 420f * s, 16f * s, 400f * s, 140f * s), debug, _debugStyle, new Color(1f, 1f, 1f, 0.8f));
        }
    }

    /// <summary>
    /// 투척 중 플레이어 속도와 블랙홀 방향의 각도가 설정값 이상이면 안내 시간을 갱신한다.
    /// 현재 속도, 위치와 최소 각도를 사용하며 프리팹 표시를 갱신하고 마지막 조건 충족부터 최소 5초 유지한다.
    /// </summary>
    private void UpdateBlackHoleArrow()
    {
        if (_state != State.Flying || !_playerController.IsThrown || _playerController.IsGameOver
            || _endingCinemaRunning || _blackHoleTarget == null || _blackHoleArrowDetached)
        {
            _blackHoleArrowEndTime = float.NegativeInfinity;
            if (!_playerController.IsThrown) _blackHoleArrowDetached = false;
            HideBlackHoleArrow();
            return;
        }

        Vector3 direction = _blackHoleTarget.transform.position - _playerController.transform.position;
        if (Vector3.Angle(_playerController.Velocity, direction) >= _blackHoleArrowMinAngle)
        {
            _blackHoleArrowEndTime = Time.time + Mathf.Max(5f, _blackHoleArrowDuration);
        }
        UpdateBlackHoleArrowTransform();
    }

    /// <summary>
    /// 플레이어 위의 화살표 프리팹을 현재 화면에서 보이는 블랙홀 방향으로 회전한다.
    /// 카메라 투영과 높이·크기 설정을 사용하며 프리팹의 위치, 회전, 크기와 활성 상태를 변경한다.
    /// </summary>
    private void UpdateBlackHoleArrowTransform()
    {
        Camera camera = Camera.main;
        bool visible = Time.time < _blackHoleArrowEndTime && _showHud && !_pauseOpen && !_catalogOpen
            && !_screenThrowPending && !(_fishSpawner.ScreenCapture.IsCapturing && _fishSpawner.ScreenCapture.HasFrame)
            && camera != null;
        if (!visible)
        {
            HideBlackHoleArrow();
            return;
        }

        Vector3 playerScreen = camera.WorldToScreenPoint(_playerController.transform.position);
        if (playerScreen.z <= 0f)
        {
            HideBlackHoleArrow();
            return;
        }
        Vector3 targetScreen = camera.WorldToScreenPoint(_blackHoleTarget.transform.position);
        Vector2 direction = new Vector2(targetScreen.x - playerScreen.x, targetScreen.y - playerScreen.y);
        if (targetScreen.z <= 0f) direction = -direction;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = targetScreen.z > playerScreen.z ? Vector2.up : Vector2.down;
        }

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        _blackHoleArrow.SetPositionAndRotation(_playerController.transform.position + Vector3.up * _blackHoleArrowHeight,
            camera.transform.rotation * Quaternion.Euler(0f, 0f, angle));
        _blackHoleArrow.localScale = _blackHoleArrowPrefab.transform.localScale * _blackHoleArrowScale;
        bool wasVisible = _blackHoleArrow.gameObject.activeSelf;
        _blackHoleArrow.gameObject.SetActive(true);
        if (wasVisible) _blackHoleArrowStateTime += Time.deltaTime;
        if (_blackHoleArrowStateTime >= 5f)
        {
            _blackHoleArrowStateTime = 0f;
            if (_blackHoleArrowWarning)
            {
                DetachBlackHoleArrow();
                return;
            }
            SetBlackHoleArrowWarning(true);
        }
        if (_blackHoleArrowWarning)
        {
            _blackHoleArrow.position += camera.transform.right * (Mathf.Sin(Time.time * 55f) * 0.06f)
                + camera.transform.up * (Mathf.Cos(Time.time * 71f) * 0.04f);
        }
    }

    /// <summary>
    /// 입력값 없이 안내를 숨기고 연속 노출 시간을 초기화한다.
    /// 경고 머티리얼을 원래 상태로 복원하며 분리 여부와 출현 등록은 유지한다.
    /// </summary>
    private void HideBlackHoleArrow()
    {
        _blackHoleArrow.gameObject.SetActive(false);
        _blackHoleArrowStateTime = 0f;
        SetBlackHoleArrowWarning(false);
    }

    /// <summary>
    /// warning에 따라 큐브 세 개의 공유 머티리얼을 경고색 또는 원래 색으로 교체한다.
    /// 새 머티리얼 인스턴스를 만들지 않으며 현재 경고 상태를 변경한다.
    /// </summary>
    private void SetBlackHoleArrowWarning(bool warning)
    {
        if (_blackHoleArrowWarning == warning) return;
        _blackHoleArrowWarning = warning;
        foreach (Renderer renderer in _blackHoleArrowRenderers)
            renderer.sharedMaterial = warning ? _blackHoleArrowWarningMaterial : _blackHoleArrowNormalMaterial;
    }

    /// <summary>
    /// 연속 경고 5초를 유지한 안내를 독립적인 낙하 조각으로 분리하고 작은 입자 폭발을 만든다.
    /// 현재 안내 위치와 플레이어 속도를 사용하며 이번 Throw의 안내를 종료하고 실제 입수를 기다린다.
    /// </summary>
    private void DetachBlackHoleArrow()
    {
        GameObject piece = Instantiate(_blackHoleArrow.gameObject, _blackHoleArrow.position, _blackHoleArrow.rotation);
        piece.name = "Detached Arrow";
        Rigidbody body = piece.AddComponent<Rigidbody>();
        Vector3 side = Camera.main.transform.right;
        body.linearVelocity = _playerController.Velocity * 0.15f + side * 4f + Vector3.up * 2f;
        body.angularVelocity = new Vector3(3f, 4f, 2f);
        Fish fish = piece.AddComponent<Fish>();
        fish.InitializeWall(_fishSpawner, _blackHoleArrowFish.Type, null);
        _fishSpawner.RegisterCutPiece(fish);

        var burst = new GameObject("Arrow Burst").AddComponent<ParticleSystem>();
        burst.transform.position = piece.transform.position;
        burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = burst.main;
        main.loop = false;
        main.duration = 0.3f;
        main.startLifetime = 0.4f;
        main.startSpeed = 3f;
        main.startSize = 0.12f;
        main.gravityModifier = 0.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var emission = burst.emission;
        emission.enabled = false;
        var shape = burst.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.1f;
        var particles = burst.GetComponent<ParticleSystemRenderer>();
        particles.renderMode = ParticleSystemRenderMode.Mesh;
        particles.mesh = _blackHoleArrow.GetComponentInChildren<MeshFilter>().sharedMesh;
        particles.sharedMaterial = _blackHoleArrowWarningMaterial;
        burst.Play();
        burst.Emit(12);
        _blackHoleArrowDetached = true;
        HideBlackHoleArrow();
    }

    /// <summary>
    /// 선택한 던질 거리가 획득 상태일 때만 최대 강화 기준 능력치로 던진다. 최초 투척 시에는 오프닝 연출을 먼저 재생한다.
    /// _projectileIndex와 진행 상황을 사용하며, _state와 _hasThrown을 변경한다.
    /// </summary>
    private void Throw()
    {
        if (!CanThrowSelectedFish()) return;
        if (!_hasThrown)
        {
            StartOpeningCinema();
            return;
        }
        FishType fish = SelectedFish();
        ThrowModifiers modifiers = fish != null
            ? fish.ToThrowModifiers(_progress.PowerMultiplier, _progress.SpinMultiplier)
            : ThrowModifiers.ForFish(_progress.PowerMultiplier, _progress.SpinMultiplier);
        _state = State.Flying;
        _blackHoleArrowDetached = false;
        _blackHoleArrowEndTime = float.NegativeInfinity;
        HideBlackHoleArrow();
        _hasThrown = true;
        _screenCaptureTriggered = false;
        _playerController.Throw(modifiers);
    }

    /// <summary>
    /// 열린 도감이나 일시정지 메뉴 프레임을 capture로 캡처하고 등록을 기다린 뒤 화면을 투척 크기로 축소하고 fishId 종류로 던진다.
    /// capture와 fishId를 사용하며, 캡처 중 입력을 막고 도감과 메뉴, 커서, 투척 외형, 환경과 게임 상태를 변경한다.
    /// </summary>
    private IEnumerator CapturePanelAndThrow(ScreenFishCapture capture, string fishId)
    {
        _catalogCapturePending = true;
        _screenThrowPending = true;
        Texture2D screenshot = null;
        try
        {
            yield return capture.CapturePanel();
            screenshot = ScreenCapture.CaptureScreenshotAsTexture();
            for (int i = 0; i < _fishSpawner.FishTypes.Count; i++)
            {
                if (_fishSpawner.FishTypes[i].Id != fishId) continue;
                _projectileIndex = i;
                break;
            }
            _catalogOpen = false;
            _pauseOpen = false;
            _resetConfirmOpen = false;
            LockCursor(true);
            ApplyProjectileShape();
            if (_environmentController != null) _environmentController.ChangeFishInstant(SelectedFish());
            yield return ShrinkPanelAndThrow(screenshot);
        }
        finally
        {
            _catalogCapturePending = false;
            _screenThrowPending = false;
            if (screenshot != null) Destroy(screenshot);
        }
    }

    /// <summary>
    /// 열린 일시정지 메뉴를 배경 없는 팝업 이미지로 그려 메뉴 fish에 등록하고, 화면 축소 없이 바로 menu 종류로 던진다.
    /// 입력값 없이 캡처 중 입력을 막고 메뉴와 커서, 투척 외형, 환경과 게임 상태를 변경한다.
    /// </summary>
    private IEnumerator ThrowMenuPopup()
    {
        _catalogCapturePending = true;
        _screenThrowPending = true;
        _menuPopupFrame = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
        try
        {
            // 이번 프레임 OnGUI가 팝업을 렌더 텍스처에 그린 뒤 잘라 낸다.
            yield return new WaitForEndOfFrame();
            _fishSpawner.MenuCapture.SavePopup(_menuPopupFrame, _menuPopupRect);
            RenderTexture.ReleaseTemporary(_menuPopupFrame);
            _menuPopupFrame = null;
            for (int i = 0; i < _fishSpawner.FishTypes.Count; i++)
            {
                if (_fishSpawner.FishTypes[i].Id != "menu") continue;
                _projectileIndex = i;
                break;
            }
            _pauseOpen = false;
            _resetConfirmOpen = false;
            LockCursor(true);
            ApplyProjectileShape();
            if (_environmentController != null) _environmentController.ChangeFishInstant(SelectedFish());
            Throw();
        }
        finally
        {
            if (_menuPopupFrame != null)
            {
                RenderTexture.ReleaseTemporary(_menuPopupFrame);
                _menuPopupFrame = null;
            }
            _catalogCapturePending = false;
            _screenThrowPending = false;
        }
    }

    /// <summary>
    /// screenshot 전체를 회전 없이 실제 투척물의 화면 위치와 크기로 축소한다.
    /// 카메라와 물고기 외형을 사용하며, 축소 완료 프레임부터 기존 회전과 비행을 시작한다.
    /// </summary>
    private IEnumerator ShrinkPanelAndThrow(Texture2D screenshot)
    {
        GameObject overlay = new GameObject("Screen Throw Overlay", typeof(Canvas));
        Canvas canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        GameObject panel = new GameObject("Captured Panel", typeof(RectTransform), typeof(RawImage));
        panel.transform.SetParent(overlay.transform, false);
        RawImage image = panel.GetComponent<RawImage>();
        image.texture = screenshot;
        image.raycastTarget = false;
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(Screen.width, Screen.height);
        Renderer[] renderers = _fishGenerator.GetComponentsInChildren<Renderer>();
        bool[] visible = new bool[renderers.Length];
        Canvas[] fishCanvases = _fishGenerator.GetComponentsInChildren<Canvas>();
        bool[] canvasVisible = new bool[fishCanvases.Length];
        try
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                visible[i] = renderers[i].enabled;
                renderers[i].enabled = false;
            }
            for (int i = 0; i < fishCanvases.Length; i++)
            {
                canvasVisible[i] = fishCanvases[i].enabled;
                fishCanvases[i].enabled = false;
            }
            yield return new WaitForEndOfFrame();
            Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            Camera camera = Camera.main;
            foreach (Vector3 point in _fishGenerator.HullPoints)
            {
                Vector3 screen = camera.WorldToScreenPoint(_fishGenerator.transform.TransformPoint(point));
                minimum = Vector2.Min(minimum, screen);
                maximum = Vector2.Max(maximum, screen);
            }
            Vector2 destination = (minimum + maximum) * 0.5f - new Vector2(Screen.width, Screen.height) * 0.5f;
            float targetScale = Mathf.Max((maximum.x - minimum.x) / Screen.width, (maximum.y - minimum.y) / Screen.height);
            float elapsed = 0f;
            while (elapsed < _panelShrinkDuration)
            {
                float progress = Mathf.SmoothStep(0f, 1f, elapsed / _panelShrinkDuration);
                rect.localScale = Vector3.one * Mathf.Lerp(1f, targetScale, progress);
                rect.anchoredPosition = Vector2.Lerp(Vector2.zero, destination, progress);
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            rect.localScale = Vector3.one * targetScale;
            rect.anchoredPosition = destination;
            yield return new WaitForEndOfFrame();
        }
        finally
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = visible[i];
            for (int i = 0; i < fishCanvases.Length; i++)
                if (fishCanvases[i] != null) fishCanvases[i].enabled = canvasVisible[i];
            overlay.SetActive(false);
            Destroy(overlay);
        }
        Throw();
    }

    /// <summary>
    /// 새 던질 거리를 만들고 물고기와 카메라를 던지기 전 상태로 되돌린 뒤 이번 판 날씨를 새로 정한다.
    /// 한 번도 던지지 않았으면(_hasThrown이 false) 날씨를 뽑지 않고 맑은 낮을 유지한다.
    /// clearNewFish가 true면 이번 판 신규 획득 목록을 비운다.
    /// 입력값은 없으며, 물고기, 던질 거리 메시, 물고기 상태, 카메라, _state, 날씨를 변경한다.
    /// </summary>
    private void Restart(bool clearNewFish = true)
    {
        _rumble.StopMotors();
        _fishSpawner.SetAcquisitionEnabled(true);
        if (clearNewFish)
        {
            _newFish.Clear();
        }
        _resultScroll = Vector2.zero;
        _catalogOpen = false;
        if (_endingCinemaCoroutine != null)
        {
            StopCoroutine(_endingCinemaCoroutine);
            _endingCinemaCoroutine = null;
        }
        if (_openingCinemaCoroutine != null)
        {
            StopCoroutine(_openingCinemaCoroutine);
            _openingCinemaCoroutine = null;
        }
        _openingCinemaRunning = false;
        if (_endingAlienInstance != null)
        {
            Destroy(_endingAlienInstance);
            _endingAlienInstance = null;
        }
        _endingCinemaRunning = false;
        SetCinematicLetterbox(false, immediate: true);
        ReleaseScreenSuction();
        if (_fishSpawner != null)
        {
            _fishSpawner.enabled = true;
            _fishSpawner.Clear();
        }
        ApplyProjectileShape();
        if (_cameraController != null)
        {
            _cameraController.enabled = true;
        }
        if (_playerController != null)
        {
            _playerController.enabled = true;
            Rigidbody rb = _playerController.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
            }
        }
        _playerController.ResetToStart();
        _cameraController.SnapBehindTarget();
        _popupTime = -10f;
        _fishPopupTime = -10f;
        _perfectCount = 0;
        _goodCount = 0;
        _missCount = 0;
        _state = State.Ready;
        if (_environmentController != null) _environmentController.Roll(CanThrowSelectedFish() ? SelectedFish() : null, _hasThrown);
        LockCursor(true);
    }

    /// <summary>
    /// direction(-1 또는 1)으로 획득하거나 발견한 물고기 사이에서 선택 항목을 바꾼다.
    /// _projectileIndex와 외형을 변경하며 미획득 항목은 기본 환경과 도감용 미리보기를 사용한다.
    /// </summary>
    private void CycleProjectile(int direction)
    {
        RefreshOwnedOptions();
        int option = _selectionOptions.IndexOf(_projectileIndex);
        int index = _selectionOptions[(option + direction + _selectionOptions.Count) % _selectionOptions.Count];
        if (index == _projectileIndex) return;
        _projectileIndex = index;
        ApplyProjectileShape();
        if (_environmentController != null) _environmentController.ChangeFish(CanThrowSelectedFish() ? SelectedFish() : null);
    }

    /// <summary>
    /// JSON 순서로 숨김 항목을 뺀 도감, 획득 목록과 미발견을 제외한 선택 목록을 구성한다.
    /// 등록된 종류와 도감 상태를 사용해 _catalogOptions, _ownedOptions와 _selectionOptions를 갱신한다.
    /// </summary>
    private void RefreshOwnedOptions()
    {
        _ownedOptions.Clear();
        _selectionOptions.Clear();
        _catalogOptions.Clear();
        _catalogOptions.Add(DEFAULT_FISH_INDEX);
        for (int i = 0; i < _fishSpawner.FishTypes.Count; i++)
        {
            // 도감 완성 보상은 나머지를 다 모으기 전까지 도감에 칸도 만들지 않는다.
            if (_fishSpawner.IsHiddenInCatalog(_fishSpawner.FishTypes[i])) continue;
            _catalogOptions.Add(i);
        }
        _catalogOptions.Sort((left, right) =>
        {
            int comparison = GetCatalogOrder(left).CompareTo(GetCatalogOrder(right));
            return comparison != 0 ? comparison : left.CompareTo(right);
        });
        foreach (int index in _catalogOptions)
        {
            if (index == DEFAULT_FISH_INDEX || _progress.IsFishRegistered(_fishSpawner.FishTypes[index]))
                _ownedOptions.Add(index);
            if (index == DEFAULT_FISH_INDEX
                || _fishSpawner.GetCatalogState(_fishSpawner.FishTypes[index]) != FishCatalogState.Undiscovered)
                _selectionOptions.Add(index);
        }
    }

    /// <summary>
    /// 등록 목록의 index를 물고기 ID에 대응하는 JSON 순번으로 변환한다.
    /// 기본 짱돌은 default 순번을 사용하며 JSON에 없는 종류는 목록 끝 순번을 반환한다.
    /// </summary>
    private int GetCatalogOrder(int index)
    {
        string id = index == DEFAULT_FISH_INDEX ? "default" : _fishSpawner.FishTypes[index].Id;
        return _catalogOrder.TryGetValue(id, out int order) ? order : int.MaxValue;
    }

    /// <summary>
    /// 최초 등록된 fish를 현재 판의 신규 획득 목록에 추가한다.
    /// 모든 등록 경로의 이벤트를 받아 _newFish와 획득 팝업을 변경한다.
    /// </summary>
    private void HandleFishRegistered(FishType fish)
    {
        _rumble.Pulse(_registerRumble, 0.24f);
        _newFish.Add(fish);
        ShowFishPopup($"{GetCatalogDisplayName(fish)}을(를) 얻은!");
    }

    /// <summary>
    /// 선택 항목이 획득 상태이면 투척 메시를 만들고 미획득이면 실제 모델을 숨긴다.
    /// _projectileIndex와 획득 기록을 사용하며 외형, 파닥임과 스포너의 플레이어 물고기를 변경한다.
    /// </summary>
    private void ApplyProjectileShape()
    {
        if (!CanThrowSelectedFish())
        {
            SetProjectileVisualsVisible(false);
            _fishSpawner.SetPlayerFish(null);
            return;
        }
        SetProjectileVisualsVisible(true);
        FishType fish = SelectedFish();
        if (fish != null)
        {
            _fishGenerator.GenerateFish(_fishSpawner.FishPrefabs[_projectileIndex]);
            _fishGenerator.SetFlopping(true);
            _fishSpawner.SetPlayerFish(_fishSpawner.FishPrefabs[_projectileIndex]);
        }
        else
        {
            _fishGenerator.Generate();
            _ownedFishSelectionPreview.PrepareDefaultFishTexture(_fishGenerator.GetComponent<MeshFilter>().sharedMesh,
                _fishGenerator.GetComponent<MeshRenderer>().sharedMaterials);
            _fishSpawner.SetPlayerFish(null);
        }
    }

    /// <summary>
    /// 현재 선택 항목의 획득 기록을 확인해 투척 가능 여부를 반환한다.
    /// 기본 짱돌은 항상 허용하며 발견 상태만으로는 투척을 허용하지 않는다.
    /// </summary>
    private bool CanThrowSelectedFish()
    {
        FishType fish = SelectedFish();
        return fish == null || _progress.IsFishRegistered(fish);
    }

    /// <summary>
    /// visible로 실제 투척 모델의 렌더러와 Canvas를 숨기거나 이전 활성 상태로 복원한다.
    /// 생성기 자식의 표시 상태를 보관하며 원래 꺼진 파츠와 게임 동작은 변경하지 않는다.
    /// </summary>
    private void SetProjectileVisualsVisible(bool visible)
    {
        if (visible)
        {
            foreach (Renderer renderer in _hiddenProjectileRenderers) renderer.enabled = true;
            foreach (Canvas canvas in _hiddenProjectileCanvases) canvas.enabled = true;
            _hiddenProjectileRenderers.Clear();
            _hiddenProjectileCanvases.Clear();
            return;
        }
        foreach (Renderer renderer in _fishGenerator.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled) continue;
            _hiddenProjectileRenderers.Add(renderer);
            renderer.enabled = false;
        }
        foreach (Canvas canvas in _fishGenerator.GetComponentsInChildren<Canvas>(true))
        {
            if (!canvas.enabled) continue;
            _hiddenProjectileCanvases.Add(canvas);
            canvas.enabled = false;
        }
    }

    /// <summary>
    /// 현재 선택된 물고기 종류를 구한다.
    /// _projectileIndex를 사용하며, 기본 물고기면 null을 반환한다.
    /// </summary>
    private FishType SelectedFish()
    {
        return _projectileIndex == DEFAULT_FISH_INDEX ? null : _fishSpawner.FishTypes[_projectileIndex];
    }

    /// <summary>
    /// 튕김 횟수 문구를 띄운다. 판정이나 포획 문구가 막 떴으면 덮어쓰지 않는다.
    /// count와 judge를 사용하며, 팝업 상태를 변경한다.
    /// </summary>
    private void HandleSkip(int count, SkipJudge judge)
    {
        if (Time.unscaledTime - _popupTime > 0.4f)
        {
            ShowPopup($"건너뛰기 {count}", Color.white);
        }
    }

    /// <summary>
    /// SPACE 판정 결과를 표시하고, 성공하면 선택한 프리팹의 발 교대 애니메이션을 재생한다.
    /// judge와 timingError(초, 빠르면 음수)를 사용하며, 팝업, 판정 횟수와 프리팹 연출 상태를 변경한다.
    /// </summary>
    private void HandleJudge(SkipJudge judge, float timingError)
    {
        if (judge == SkipJudge.Perfect || judge == SkipJudge.Good)
        {
            PlayFishInteraction();
            _noteHitTime = Time.unscaledTime;
            _noteHitColor = JudgeColor(judge);
        }

        string direction = timingError < 0f ? "이른" : "늦은";
        switch (judge)
        {
            case SkipJudge.Perfect:
                _perfectCount++;
                ShowPopup("완벽한!", JudgeColor(judge));
                break;
            case SkipJudge.Good:
                _goodCount++;
                ShowPopup($"좋은  {direction}", JudgeColor(judge));
                break;
            case SkipJudge.Miss:
                _missCount++;
                ShowPopup($"놓친  너무 {direction}", JudgeColor(judge));
                break;
        }
    }

    /// <summary>
    /// SCREEN 캡처 완료 이벤트를 받아 해금 팝업을 표시한다.
    /// 입력값 없이 팝업 상태를 변경한다.
    /// </summary>
    private void HandleScreenUnlocked()
    {
        ShowFishPopup("화면을(를) 얻은!");
    }

    /// <summary>
    /// 미끄러지는 동안 SPACE를 누를 때 선택한 물고기의 성공 동작과 프리팹 연출을 실행한다.
    /// 현재 플레이어와 물고기를 사용하며, 판정 기록과 팝업을 변경하지 않고 특수 동작 상태만 변경한다.
    /// </summary>
    private void HandleSlidePush()
    {
        FishAbility ability = _fishGenerator.CurrentAbility;
        if (ability != null)
        {
            ThrowContext context = new ThrowContext(_playerController, _fishGenerator, _fishGenerator.FishBody, _fishSpawner);
            ability.OnJudgeSuccess(context, SkipJudge.Good);
        }
        PlayFishInteraction();
    }

    /// <summary>
    /// 판정 성공이나 미끄러짐 밀기 때 선택한 물고기의 발 교대 연출을 실행한다.
    /// 현재 던질 거리의 자식 컴포넌트를 사용하며, 호팍 애니메이션 상태를 변경한다.
    /// </summary>
    private void PlayFishInteraction()
    {
        HopakJumpAnimation hopakAnimation = _fishGenerator.GetComponentInChildren<HopakJumpAnimation>();
        if (hopakAnimation != null)
        {
            hopakAnimation.PlayJumpSegment();
        }
    }
    /// <summary>
    /// 물에 닿으면 파닥임을 멈추고 블랙홀의 투척별 최초 접촉 화면을 흡입한다.
    /// point와 speed는 쓰지 않으며, 캡처 시작 여부와 던질 거리의 파닥임을 변경한다.
    /// </summary>
    private void HandleWaterContact(Vector3 point, float speed)
    {
        _fishGenerator.SetFlopping(false);
        if (!_screenCaptureTriggered && SelectedFish()?.Id == "blackhole")
        {
            _screenCaptureTriggered = true;
            _fishSpawner.ScreenCapture.Capture(_fishGenerator.FishBody);
        }
    }

    /// <summary>
    /// 물고기를 획득했을 때 이름과 획득 문구를 띄운다.
    /// fish를 사용하며, 팝업 상태를 변경한다.
    /// </summary>
    private void HandleFishCaught(FishType fish)
    {
        if (fish.Id == "wall") _fishSelectionPreview.InvalidateTexture(_fishSpawner.WallFish);
        if (fish.Id == "ice") _fishSelectionPreview.InvalidateTexture(_fishSpawner.IceFish);
        if (fish.Id == "wall") _ownedFishSelectionPreview.InvalidateTexture(_fishSpawner.WallFish);
        if (fish.Id == "ice") _ownedFishSelectionPreview.InvalidateTexture(_fishSpawner.IceFish);
        ShowFishPopup($"{GetCatalogDisplayName(fish)}을(를) 얻은!");
    }

    /// <summary>
    /// 정산 상태로 바꾸고 최고 기록을 저장한 뒤, 다시하기 버튼을 누를 수 있게 커서를 푼다.
    /// reason과 현재 튕김 횟수, 거리를 사용하며, _state와 최고 기록, PlayerPrefs, 커서를 변경한다.
    /// </summary>
    private void HandleGameOver(GameOverReason reason)
    {
        _rumble.StopMotors();
        _fishSpawner.SetAcquisitionEnabled(false);
        _state = State.GameOver;
        _gameOverReason = reason;
        _gameOverTime = Time.unscaledTime;

        if (_playerController.SkipCount > _bestSkips)
        {
            _bestSkips = _playerController.SkipCount;
            PlayerPrefs.SetInt(BEST_SKIPS_KEY, _bestSkips);
        }
        if (_playerController.Distance > _bestDistance)
        {
            _bestDistance = _playerController.Distance;
            PlayerPrefs.SetFloat(BEST_DISTANCE_KEY, _bestDistance);
        }
        PlayerPrefs.Save();
        LockCursor(false);
    }

    /// <summary>
    /// 던진 물고기가 물 박스 범위 밖으로 나가면 경계 밖 게임오버를 보내고, 조종을 꺼서 수면 판정 없이 물 밖으로 떨어지게 한다.
    /// 스카이박스 획득은 떨어지기 시작하고 _skyboxAcquireDelay초 뒤로 미루며, 얻은 뒤 _skyboxRestartLockAfterAcquire초까지 재시작을 막는다.
    /// 플레이어 위치와 _waterBounds를 사용하며, PlayerController 활성 상태, 게임 상태와 _skyboxFallPending을 변경하고 획득 코루틴을 시작한다.
    /// </summary>
    private void CheckWaterEdge()
    {
        Vector3 position = _playerController.transform.position;
        if (position.x >= _waterBounds.min.x && position.x <= _waterBounds.max.x
            && position.z >= _waterBounds.min.z && position.z <= _waterBounds.max.z) return;

        // 게임오버 뒤에도 PlayerController는 수면 힘을 계속 주므로, 꺼서 던질 때 켠 중력만 받아 Trigger인 물 밖으로 떨어지게 한다.
        // 던진 상태는 그대로라 카메라는 떨어지는 플레이어를 계속 따라가며 수면 높이 위에서 내려다본다.
        _playerController.enabled = false;
        _skyboxFallPending = true;
        _playerController.GameOver(GameOverReason.OutOfBounds);
        StartCoroutine(AcquireSkyboxWhileFalling());
    }

    /// <summary>
    /// 물 밖으로 떨어지기 시작하고 _skyboxAcquireDelay초 뒤에 스카이박스를 얻게 하며 그 하늘과 날씨 효과를 바로 적용하고,
    /// 다시 _skyboxRestartLockAfterAcquire초 뒤에 재시작 막기를 푼다. 기다리는 동안 다른 경로로 재시작해 게임오버 상태가 아니면 획득하지 않고 바로 막기를 푼다.
    /// _state와 스카이박스 종류를 사용하며, _skyboxFallPending, 도감 등록, 획득 팝업, 하늘과 날씨를 변경한다.
    /// </summary>
    private IEnumerator AcquireSkyboxWhileFalling()
    {
        // 재시작 입력 대기처럼 실제 시간으로 기다린다.
        yield return new WaitForSecondsRealtime(_skyboxAcquireDelay);
        if (_state != State.GameOver)
        {
            _skyboxFallPending = false;
            yield break;
        }

        FishType skybox = GetFishTypeById(SKYBOX_FISH_ID);
        _progress.AddFish(skybox);
        ShowFishPopup($"{GetCatalogDisplayName(skybox)}을(를) 얻은!");
        _environmentController.ApplyAcquiredFish(skybox);

        yield return new WaitForSecondsRealtime(_skyboxRestartLockAfterAcquire);
        _skyboxFallPending = false;
    }

    /// <summary>
    /// 던지기 전 안내와 지금 던질 거리를 그린다. 한 번이라도 던졌으면 재시작으로 날씨를 다시 뽑는 안내도 그린다.
    /// 화면 크기와 scale, _hasThrown 및 획득 상태를 사용해 선택 항목의 설명 또는 힌트를 중앙 정렬로 그린다.
    /// </summary>
    private void DrawReady(float width, float height, float scale)
    {
        bool canThrow = CanThrowSelectedFish();
        ShadowLabel(new Rect(0f, height * 0.18f, width, 80f * scale),
            canThrow ? _input.ReplaceBindingName("{Jump} 던지기 위하여") : "미획득 · 획득 후 투척 가능", _centerStyle, Color.white);
        ShadowLabel(new Rect(0f, height * 0.18f + 80f * scale, width, 40f * scale),
            _input.ReplaceBindingName(_input.IsUsingGamepad
                ? "{Steer} / {CurveTriggers} 굽히는    {Jump} 물 점프    {Restart} 새 물고기"
                : "A/D 굽히는    {Jump} 물에 닿을 때    {Restart} 새 물고기"), _centerSubStyle, Color.white);
        ShadowLabel(new Rect(0f, height * 0.18f + 112f * scale, width, 40f * scale),
            _input.ReplaceBindingName(_input.IsUsingGamepad ? "{Cancel} 차림표" : "ESC 차림표"), _centerSubStyle, new Color(1f, 1f, 1f, 0.7f));
        if (_hasThrown)
        {
            ShadowLabel(new Rect(0f, height * 0.18f + 144f * scale, width, 40f * scale),
                _input.ReplaceBindingName("{Restart}   날씨 · 시간 · 장소 재설정"), _centerSubStyle, new Color(1f, 0.92f, 0.7f));
        }
        ShadowLabel(new Rect(0f, height * _spaceHintHeight, width, 40f * scale),
            _input.ReplaceBindingName("<  {Previous}   던지는   {Next}  >    {Catalog} 도감"), _centerSubStyle, new Color(1f, 0.92f, 0.7f));
        DrawFishPreviews(width, height, scale);
        string fishId = SelectedFish()?.Id ?? "default";
        string details = _catalogEntries[fishId]?.Value<string>(canThrow ? "description" : "unlockHint");
        float top = height * _spaceHintHeight + 44f * scale;
        ShadowLabel(new Rect(width * 0.15f, top, width * 0.7f, Mathf.Max(0f, height - top - 12f * scale)),
            details, _selectionDetailStyle, Color.white);
    }

    /// <summary>
    /// 선택 목록의 현재 항목과 앞뒤 최대 두 개를 화면 크기와 scale에 맞춰 표시한다.
    /// 획득 항목은 원본 외형을, 미획득 항목은 도감용 미리보기와 이름을 그린다.
    /// </summary>
    private void DrawFishPreviews(float width, float height, float scale)
    {
        int optionCount = _selectionOptions.Count;
        int selectedOption = _selectionOptions.IndexOf(_projectileIndex);
        float slotWidth = Mathf.Min(234f * scale, width * 0.16f);
        float slotHeight = slotWidth * 2f / 3f;
        float slotSpacing = width * 0.21f;
        float top = height * _spaceHintHeight - slotHeight - 58f * scale;
        for (int offset = -Mathf.Min(2, (optionCount - 1) / 2); offset <= Mathf.Min(2, optionCount / 2); offset++)
        {
            Rect rect = new Rect(width * 0.5f + offset * slotSpacing - slotWidth * 0.5f, top, slotWidth, slotHeight);
            if (offset == 0 && CanThrowSelectedFish())
            {
                FishType selectedFish = SelectedFish();
                ShadowLabel(new Rect(rect.x, rect.yMax, slotWidth, 54f * scale),
                    _fishSpawner.GetCatalogState(selectedFish) != FishCatalogState.Undiscovered
                        ? GetCatalogDisplayName(selectedFish) : "?", _previewStyle, new Color(1f, 0.92f, 0.7f));
                continue;
            }
            int index = _selectionOptions[(selectedOption + offset + optionCount) % optionCount];
            if (index == DEFAULT_FISH_INDEX)
            {
                GUI.DrawTexture(rect, _ownedFishSelectionPreview.DefaultFishTexture, ScaleMode.ScaleToFit, true);
                ShadowLabel(new Rect(rect.x, rect.yMax, slotWidth, 54f * scale),
                    GetCatalogDisplayName(null), _previewStyle, new Color(0.75f, 0.75f, 0.75f, 0.7f));
                continue;
            }

            FishType fish = _fishSpawner.FishTypes[index];
            bool owned = _progress.IsFishRegistered(fish);
            Texture preview = !owned ? _fishSelectionPreview.GetTexture(_fishSpawner.FishPrefabs[index])
                : fish.Id == "screen" ? _fishSpawner.ScreenCapture.Image
                : fish.Id == "catalog" ? _fishSpawner.CatalogCapture.Image
                : fish.Id == "menu" ? _fishSpawner.MenuCapture.Image
                : _ownedFishSelectionPreview.GetTexture(_fishSpawner.FishPrefabs[index]);
            if (preview != null) GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit, true);
            // 처음 얻은 뒤 아직 하이라이트되지 않은 종류는 칸 위에 강조 문구를 띄운다.
            if (_progress.IsFishNew(fish))
                ShadowLabel(new Rect(rect.x, rect.y - 34f * scale, slotWidth, 30f * scale), "새로운!", _centerSubStyle,
                    new Color(1f, 0.75f, 0.2f));
            ShadowLabel(new Rect(rect.x, rect.yMax, slotWidth, 54f * scale), GetCatalogDisplayName(fish), _previewStyle,
                offset == 0 ? new Color(1f, 0.92f, 0.7f) : new Color(0.75f, 0.75f, 0.75f, 0.7f));
        }
    }

    /// <summary>
    /// 게임오버 결과와 이번 판에 처음 획득한 종류의 컬러 그리드, 다시하기 버튼을 그린다.
    /// 화면 크기, scale, _newFish와 _skyboxFallPending을 사용하며 버튼을 누르면 재시작하고, 물 밖으로 떨어져 재시작을 막는 동안은 버튼을 막는다.
    /// </summary>
    private void DrawShop(float width, float height, float scale)
    {
        ShadowLabel(new Rect(0f, height * 0.12f, width, 80f * scale), "GAME 너머", _centerStyle, Color.white);
        string result = $"{ReasonText(_gameOverReason)}    {_playerController.SkipCount} 건너뛰기 / {_playerController.Distance:0.0} m";
        ShadowLabel(new Rect(0f, height * 0.12f + 80f * scale, width, 40f * scale), result, _centerSubStyle, Color.white);
        string timing = $"완벽한 {_perfectCount}    좋은 {_goodCount}    놓친 {_missCount}";
        ShadowLabel(new Rect(0f, height * 0.12f + 115f * scale, width, 40f * scale), timing, _centerSubStyle, new Color(1f, 0.92f, 0.7f));

        ShadowLabel(new Rect(0f, height * 0.39f, width, 40f * scale), $"잠금 해제된! {_newFish.Count}", _centerSubStyle, Color.white);
        Rect viewport = new Rect(width * 0.08f, height * 0.45f, width * 0.84f, height * 0.3f);
        int columns = Mathf.Max(1, Mathf.FloorToInt(viewport.width / (240f * scale)));
        float contentWidth = viewport.width - 24f * scale;
        float cellWidth = contentWidth / columns;
        float cellHeight = 220f * scale;
        int rows = Mathf.CeilToInt((float)_newFish.Count / columns);
        _resultScroll = GUI.BeginScrollView(viewport, _resultScroll,
            new Rect(0f, 0f, contentWidth, Mathf.Max(viewport.height, rows * cellHeight)));
        if (_newFish.Count == 0)
            ShadowLabel(new Rect(0f, 0f, contentWidth, 38f * scale), "잠금 해제된 것 없는", _centerSubStyle, new Color(1f, 1f, 1f, 0.7f));
        for (int i = 0; i < _newFish.Count; i++)
        {
            FishType fish = _newFish[i];
            Fish prefab = _fishSpawner.GetFishPrefab(fish);
            Texture texture = fish.Id == "screen" ? _fishSpawner.ScreenCapture.Image
                : fish.Id == "catalog" ? _fishSpawner.CatalogCapture.Image
                : fish.Id == "menu" ? _fishSpawner.MenuCapture.Image
                : prefab != null ? _ownedFishSelectionPreview.GetTexture(prefab) : null;
            Rect cell = new Rect(i % columns * cellWidth, i / columns * cellHeight,
                cellWidth - 12f * scale, cellHeight - 12f * scale);
            Color original = GUI.color;
            GUI.color = new Color(0.06f, 0.09f, 0.12f, 0.8f);
            GUI.DrawTexture(cell, Texture2D.whiteTexture);
            GUI.color = original;
            Rect image = new Rect(cell.x + 12f * scale, cell.y + 8f * scale,
                cell.width - 24f * scale, 132f * scale);
            if (texture != null) GUI.DrawTexture(image, texture, ScaleMode.ScaleToFit, true);
            else ShadowLabel(image, "?", _centerStyle, new Color(1f, 1f, 1f, 0.5f));
            ShadowLabel(new Rect(cell.x, cell.y + 142f * scale, cell.width, 60f * scale),
                GetCatalogDisplayName(fish), _previewStyle, new Color(1f, 0.92f, 0.7f));
        }
        GUI.EndScrollView();
        float buttonWidth = 620f * scale;
        float buttonHeight = 60f * scale;
        float left = (width - buttonWidth) * 0.5f;
        // 물 밖으로 떨어지는 동안은 스카이박스를 얻고 잠시 뒤까지 버튼을 누를 수 없다.
        GUI.enabled = !_skyboxFallPending;
        if (GUI.Button(new Rect(left, height * 0.8f, buttonWidth, buttonHeight), _input.ReplaceBindingName("다시 시도하는   ({Jump} / {Restart})"), _buttonStyle))
        {
            Restart();
        }
        GUI.enabled = true;
    }

    /// <summary>
    /// 판정별 표시 색을 구한다.
    /// judge를 사용하며, PERFECT 금색, GOOD 초록, MISS 회색을 반환한다.
    /// </summary>
    private static Color JudgeColor(SkipJudge judge)
    {
        if (judge == SkipJudge.Perfect) return new Color(1f, 0.85f, 0.2f);
        if (judge == SkipJudge.Good) return new Color(0.4f, 1f, 0.5f);
        return new Color(0.75f, 0.75f, 0.75f);
    }

    /// <summary>
    /// fish의 ID로 도감 JSON의 표시 이름을 조회하고 비어 있으면 기존 이름을 반환한다.
    /// 기본 물수제비는 default 항목을 사용하며 이름 조회만 수행하고 상태는 변경하지 않는다.
    /// </summary>
    private string GetCatalogDisplayName(FishType fish)
    {
        string id = fish == null ? "default" : fish.Id;
        string displayName = _catalogEntries[id]?.Value<string>("displayName");
        return string.IsNullOrWhiteSpace(displayName) ? fish == null ? "물고기" : fish.DisplayName : displayName;
    }

    /// <summary>
    /// 전체 종류와 선택한 항목의 JSON 상세 정보를 그리며 선택 칸을 화면 안으로 스크롤한다.
    /// 화면 크기, scale과 마우스 입력을 사용하며 선택 항목과 도감·커서 상태를 변경한다.
    /// </summary>
    private void DrawCatalog(float width, float height, float scale)
    {
        RefreshOwnedOptions();
        Color original = GUI.color;
        GUI.color = new Color(0.06f, 0.09f, 0.12f, 0.96f);
        GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
        GUI.color = original;
        ShadowLabel(new Rect(0f, 24f * scale, width, 80f * scale), "도감", _centerStyle, Color.white);
        ShadowLabel(new Rect(0f, 108f * scale, width, 40f * scale),
            $"보유  {_ownedOptions.Count} / {_catalogOptions.Count}    " + _input.ReplaceBindingName(_input.IsUsingGamepad
                ? "{Navigate} 선택    {Catalog} / {Cancel} 닫기    {Jump} 투척" : "WASD 선택    {Catalog} / ESC 닫기    {Jump} 투척"), _centerSubStyle, new Color(1f, 0.92f, 0.7f));
        Rect viewport = new Rect(width * 0.08f, 170f * scale, width * 0.84f, height - 430f * scale);
        bool pointerInViewport = viewport.Contains(Event.current.mousePosition);
        int columns = GetCatalogColumns(width, scale);
        float cellWidth = (viewport.width - 24f * scale) / columns;
        float cellHeight = 220f * scale;
        int count = _catalogOptions.Count;
        int rows = Mathf.CeilToInt((float)count / columns);
        if (_catalogScrollToSelection)
            ScrollCatalogSelectionIntoView(viewport.height, cellHeight, columns, rows);
        _catalogScroll = GUI.BeginScrollView(viewport, _catalogScroll,
            new Rect(0f, 0f, viewport.width - 24f * scale, rows * cellHeight));
        for (int option = 0; option < count; option++)
        {
            int index = _catalogOptions[option];
            FishType fish = index == DEFAULT_FISH_INDEX ? null : _fishSpawner.FishTypes[index];
            FishCatalogState catalogState = _fishSpawner.GetCatalogState(fish);
            bool owned = catalogState == FishCatalogState.Acquired;
            bool revealed = catalogState != FishCatalogState.Undiscovered;
            Rect cell = new Rect(option % columns * cellWidth, option / columns * cellHeight, cellWidth - 12f * scale, cellHeight - 12f * scale);
            bool hovered = pointerInViewport && cell.Contains(Event.current.mousePosition);
            if (hovered && !_catalogCapturePending && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                SelectCatalogFish(fish);
                Event.current.Use();
            }
            bool selected = fish == _catalogSelectedFish;
            GUI.color = new Color(1f, 1f, 1f, selected ? 0.32f : owned ? 0.12f : 0.04f);
            GUI.DrawTexture(cell, Texture2D.whiteTexture);
            GUI.color = original;
            Rect image = new Rect(cell.x + 12f * scale, cell.y + 8f * scale, cell.width - 24f * scale, 132f * scale);
            Texture texture = fish == null ? _ownedFishSelectionPreview.DefaultFishTexture
                : fish.Id == "screen" ? _fishSpawner.ScreenCapture.Image
                : fish.Id == "catalog" ? _fishSpawner.CatalogCapture.Image
                : fish.Id == "menu" ? _fishSpawner.MenuCapture.Image
                : (owned ? _ownedFishSelectionPreview : _fishSelectionPreview).GetTexture(_fishSpawner.FishPrefabs[index]);
            if (revealed && texture != null) GUI.DrawTexture(image, texture, ScaleMode.ScaleToFit, true);
            else ShadowLabel(image, "?", _centerStyle, new Color(1f, 1f, 1f, 0.5f));
            // 처음 얻은 뒤 아직 하이라이트되지 않은 종류는 칸 위쪽에 강조 문구를 띄운다.
            if (fish != null && _progress.IsFishNew(fish))
                ShadowLabel(new Rect(cell.x, cell.y + 6f * scale, cell.width, 30f * scale), "새로운!", _centerSubStyle,
                    new Color(1f, 0.75f, 0.2f));
            string name = revealed ? GetCatalogDisplayName(fish) : "?";
            ShadowLabel(new Rect(cell.x, cell.y + 142f * scale, cell.width, 34f * scale), name, _previewStyle, Color.white);
            ShadowLabel(new Rect(cell.x, cell.y + 180f * scale, cell.width, 28f * scale),
                owned ? "획득" : revealed ? "미획득" : "???", _centerSubStyle,
                owned ? new Color(1f, 0.92f, 0.7f) : new Color(1f, 1f, 1f, 0.45f));
        }
        GUI.EndScrollView();
        Rect details = new Rect(viewport.x, height - 235f * scale, viewport.width, 145f * scale);
        GUI.color = new Color(1f, 1f, 1f, 0.08f);
        GUI.DrawTexture(details, Texture2D.whiteTexture);
        GUI.color = original;
        FishType selectedFish = _catalogSelectedFish;
        FishCatalogState selectedState = _fishSpawner.GetCatalogState(selectedFish);
        bool selectedOwned = selectedState == FishCatalogState.Acquired;
        string selectedId = selectedFish == null ? "default" : selectedFish.Id;
        string selectedName = selectedState != FishCatalogState.Undiscovered ? GetCatalogDisplayName(selectedFish) : "?";
        JToken entry = _catalogEntries[selectedId];
        string heading = selectedOwned
            ? $"{selectedName}    누적 획득 {(selectedFish == null ? 0 : _progress.GetFishCount(selectedFish))}개"
            : selectedState == FishCatalogState.Undiscovered ? "???" : $"{selectedName}    해금 힌트";
        string text = selectedState == FishCatalogState.Undiscovered
            ? string.Empty : entry.Value<string>(selectedOwned ? "description" : "unlockHint");
        ShadowLabel(new Rect(details.x + 24f * scale, details.y + 14f * scale, details.width - 48f * scale, 36f * scale),
            heading, _smallStyle, new Color(1f, 0.92f, 0.7f));
        Rect detailViewport = new Rect(details.x + 24f * scale, details.y + 54f * scale,
            details.width - 48f * scale, 76f * scale);
        float textWidth = detailViewport.width - 24f * scale;
        float textHeight = Mathf.Max(detailViewport.height,
            _catalogDetailStyle.CalcHeight(new GUIContent(text), textWidth) + 2f);
        _catalogDetailScroll = GUI.BeginScrollView(detailViewport, _catalogDetailScroll,
            new Rect(0f, 0f, textWidth, textHeight));
        ShadowLabel(new Rect(0f, 0f, textWidth, textHeight), text, _catalogDetailStyle, Color.white);
        GUI.EndScrollView();
        GUI.enabled = !_catalogCapturePending;
        if (GUI.Button(new Rect(width * 0.5f - 130f * scale, height - 70f * scale, 260f * scale, 48f * scale), _input.ReplaceBindingName("닫기 ({Catalog})"), _buttonStyle))
        {
            _catalogOpen = false;
            LockCursor(true);
        }
        GUI.enabled = true;
    }

    /// <summary>
    /// Navigate 입력으로 도감 선택 칸을 이동하고 길게 누르면 일정 간격으로 반복한다.
    /// 입력 방향, 화면 열 수와 JSON 순서를 사용하며 선택 항목과 반복 대기 시간을 변경한다.
    /// </summary>
    private void UpdateCatalogNavigation()
    {
        Vector2 input = _input.NavigateInput;
        Vector2Int direction = Vector2Int.zero;
        if (Mathf.Abs(input.y) >= Mathf.Abs(input.x) && Mathf.Abs(input.y) > 0.5f)
            direction.y = input.y > 0f ? -1 : 1;
        else if (Mathf.Abs(input.x) > 0.5f)
            direction.x = input.x > 0f ? 1 : -1;
        if (direction == Vector2Int.zero)
        {
            _catalogNavigateDirection = direction;
            return;
        }
        bool firstInput = direction != _catalogNavigateDirection;
        if (!firstInput && Time.unscaledTime < _catalogNavigateTime) return;
        _catalogNavigateDirection = direction;
        _catalogNavigateTime = Time.unscaledTime
            + (firstInput ? CATALOG_NAVIGATION_DELAY : CATALOG_NAVIGATION_INTERVAL);

        RefreshOwnedOptions();
        int option = GetCatalogSelectedOption();
        float scale = Mathf.Max(0.5f, Screen.height / 1080f);
        int columns = GetCatalogColumns(Screen.width, scale);
        int next = option;
        if (direction.x < 0 && option % columns > 0) next--;
        else if (direction.x > 0 && option % columns < columns - 1 && option + 1 < _catalogOptions.Count) next++;
        else if (direction.y < 0 && option >= columns) next -= columns;
        else if (direction.y > 0 && (option / columns + 1) * columns < _catalogOptions.Count)
            next = Mathf.Min(option + columns, _catalogOptions.Count - 1);
        if (next == option) return;
        int index = _catalogOptions[next];
        SelectCatalogFish(index == DEFAULT_FISH_INDEX ? null : _fishSpawner.FishTypes[index]);
    }

    /// <summary>
    /// fish를 도감 선택 항목으로 설정하고 변경 시 상세 스크롤을 초기화한다.
    /// 선택 상태를 저장하며 다음 그리기에서 선택 칸을 표시 범위에 넣도록 예약한다.
    /// </summary>
    private void SelectCatalogFish(FishType fish)
    {
        if (_catalogSelectedFish != fish) _catalogDetailScroll = Vector2.zero;
        _catalogSelectedFish = fish;
        _catalogScrollToSelection = true;
    }

    /// <summary>
    /// 현재 선택 물고기를 JSON 순서의 도감 목록에서 찾아 선택 위치를 반환한다.
    /// _catalogOptions와 선택 상태를 사용하며 해당 항목이 없으면 첫 위치를 반환한다.
    /// </summary>
    private int GetCatalogSelectedOption()
    {
        for (int option = 0; option < _catalogOptions.Count; option++)
        {
            int index = _catalogOptions[option];
            FishType fish = index == DEFAULT_FISH_INDEX ? null : _fishSpawner.FishTypes[index];
            if (fish == _catalogSelectedFish) return option;
        }
        return 0;
    }

    /// <summary>
    /// width와 scale로 그리기와 입력 처리에 공통으로 사용할 도감 열 수를 반환한다.
    /// 화면 너비의 84퍼센트와 기준 칸 너비를 사용하며 최소 한 열을 확보한다.
    /// </summary>
    private int GetCatalogColumns(float width, float scale)
    {
        return Mathf.Max(1, Mathf.FloorToInt(width * 0.84f / (240f * scale)));
    }

    /// <summary>
    /// 선택 행을 viewportHeight 안에 표시하도록 도감 스크롤 위치를 보정한다.
    /// cellHeight, columns, rows와 선택 위치를 사용하며 표시 범위와 갱신 예약을 변경한다.
    /// </summary>
    private void ScrollCatalogSelectionIntoView(float viewportHeight, float cellHeight, int columns, int rows)
    {
        float top = GetCatalogSelectedOption() / columns * cellHeight;
        float bottom = top + cellHeight;
        if (top < _catalogScroll.y) _catalogScroll.y = top;
        else if (bottom > _catalogScroll.y + viewportHeight) _catalogScroll.y = bottom - viewportHeight;
        _catalogScroll.y = Mathf.Clamp(_catalogScroll.y, 0f, Mathf.Max(0f, rows * cellHeight - viewportHeight));
        _catalogScrollToSelection = false;
    }

    /// <summary>
    /// 화면 위쪽에 잠깐 떠오르는 문구를 설정한다.
    /// text와 color를 사용하며, _popupText, _popupColor, _popupTime을 변경한다.
    /// </summary>
    private void ShowPopup(string text, Color color)
    {
        _popupText = text;
        _popupColor = color;
        _popupTime = Time.unscaledTime;
    }

    /// <summary>
    /// 획득 안내를 판정 팝업과 독립된 영역에 표시하도록 설정한다.
    /// text를 사용하며, _fishPopupText와 _fishPopupTime을 변경한다.
    /// </summary>
    private void ShowFishPopup(string text)
    {
        _fishPopupText = text;
        _fishPopupTime = Time.unscaledTime;
    }

    /// <summary>
    /// 물고기 획득 알림 팝업 텍스트를 화면에 렌더링한다.
    /// _fishPopupText와 _fishPopupTime을 사용하며, 시간 경과에 따라 떠오르고 페이드아웃되는 라벨을 그린다.
    /// </summary>
    private void DrawFishPopup()
    {
        float fishPopupAge = Time.unscaledTime - _fishPopupTime;
        if (_fishPopupText == null || fishPopupAge >= POPUP_DURATION) return;

        EnsureStyles();
        float s = _styleScale;
        float width = Screen.width;
        float height = Screen.height;
        float t = fishPopupAge / POPUP_DURATION;
        float top = Mathf.Max(height * 0.38f, height * _popupHeight + 100f * s);
        ShadowLabel(new Rect(0f, top - 30f * s * t, width, 60f * s),
            _fishPopupText, _centerSubStyle, new Color(1f, 0.85f, 0.3f, 1f - t * t));
    }

    /// <summary>
    /// 준비 화면에서 일시정지 메뉴를 열고 첫 버튼을 고른 뒤 마우스로도 누를 수 있게 커서를 푼다.
    /// 입력값 없이 _pauseOpen, _resetConfirmOpen, _pauseSelection과 커서 잠금 상태를 변경한다.
    /// </summary>
    private void OpenPauseMenu()
    {
        _pauseOpen = true;
        _resetConfirmOpen = false;
        _pauseSelection = 0;
        _pauseNavigateDirection = 0;
        LockCursor(false);
    }

    /// <summary>
    /// 일시정지 메뉴를 닫고 준비 화면 조작으로 돌아간다.
    /// 입력값 없이 _pauseOpen, _resetConfirmOpen과 커서 잠금 상태를 변경한다.
    /// </summary>
    private void ClosePauseMenu()
    {
        _pauseOpen = false;
        _resetConfirmOpen = false;
        LockCursor(true);
    }

    /// <summary>
    /// 일시정지 메뉴가 열린 동안의 입력을 처리한다. Navigate 위아래로 버튼을 고르고 Submit으로 누르며 Cancel로 한 단계 닫고, Jump로 메뉴를 던진다.
    /// _input의 Navigate, Submit, Cancel, Jump 입력과 장치 종류를 사용하며, _pauseSelection, _pauseNavigateDirection과 메뉴 상태를 변경하거나 메뉴 투척을 시작한다.
    /// </summary>
    private void UpdatePauseMenu()
    {
        if (_input.CancelPressed)
        {
            if (!_resetConfirmOpen)
            {
                ClosePauseMenu();
                return;
            }
            // 삭제 확인에서 취소하면 메뉴의 삭제 버튼으로 돌아간다.
            _resetConfirmOpen = false;
            _pauseSelection = 1;
            return;
        }

        // 키보드 Space는 언제나 메뉴 화면을 fish로 던진다. 게임패드 A는 Submit과 같은 버튼이라 RESUME을 고른 상태에서만 던지고, 그 밖에는 아래 Submit으로 버튼을 누른다.
        if (_input.JumpPressed && (!_input.IsUsingGamepad || (!_resetConfirmOpen && _pauseSelection == 0)))
        {
            StartCoroutine(ThrowMenuPopup());
            return;
        }

        float vertical = _input.NavigateInput.y;
        int direction = vertical > 0.5f ? -1 : vertical < -0.5f ? 1 : 0;
        int optionCount = _resetConfirmOpen ? 2 : 3;
        if (direction != 0 && direction != _pauseNavigateDirection)
        {
            _pauseSelection = (_pauseSelection + direction + optionCount) % optionCount;
        }
        _pauseNavigateDirection = direction;

        if (_input.SubmitPressed) ActivatePauseOption(_pauseSelection);
    }

    /// <summary>
    /// index 순서의 일시정지 메뉴 버튼을 실행한다. 메뉴는 RESUME, 세이브 삭제, EXIT이고 삭제 확인은 삭제, 취소다.
    /// index와 _resetConfirmOpen을 사용하며, 메뉴 상태를 바꾸거나 게임 종료, 세이브 삭제를 실행한다.
    /// </summary>
    private void ActivatePauseOption(int index)
    {
        if (_resetConfirmOpen)
        {
            if (index == 0)
            {
                DeleteAllSaveData();
                return;
            }
            _resetConfirmOpen = false;
            _pauseSelection = 1;
            return;
        }

        if (index == 0)
        {
            ClosePauseMenu();
        }
        else if (index == 1)
        {
            // 실수로 지우지 않도록 확인 화면을 띄우고 취소를 먼저 고른다.
            _resetConfirmOpen = true;
            _pauseSelection = 1;
        }
        else
        {
            QuitGame();
        }
    }

    /// <summary>
    /// 화면을 어둡게 덮고 일시정지 메뉴 또는 삭제 확인 버튼을 그린다. 고른 버튼은 테두리로 표시한다.
    /// 화면 크기와 scale, _resetConfirmOpen, _pauseSelection과 연결된 장치 종류를 사용하며, 마우스로 누른 버튼을 실행한다.
    /// </summary>
    private void DrawPauseMenu(float width, float height, float scale)
    {
        Color original = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
        GUI.color = original;

        string cancelName = _input.ReplaceBindingName(_input.IsUsingGamepad ? "{Cancel}" : "ESC");
        // RESUME은 메뉴를 던지는 키를 보여 준다. 키보드는 SPACE, 게임패드는 Jump와 같은 아래쪽 버튼 이름을 쓴다.
        string throwName = _input.ReplaceBindingName(_input.IsUsingGamepad ? "{Jump}" : "SPACE");
        string[] labels = _resetConfirmOpen
            ? new[] { "삭제", $"취소 ({cancelName})" }
            : new[] { $"재개하는 ({throwName})", "모든 세이브 데이터 삭제", "나가는" };
        float buttonWidth = 520f * scale;
        float buttonHeight = 60f * scale;
        float left = (width - buttonWidth) * 0.5f;
        float top = height * 0.4f;
        float step = buttonHeight + 20f * scale;

        if (_resetConfirmOpen)
        {
            ShadowLabel(new Rect(0f, top - 140f * scale, width, 80f * scale), "세이브 데이터 삭제", _centerStyle, Color.white);
            ShadowLabel(new Rect(0f, top - 56f * scale, width, 40f * scale),
                "획득한 물고기와 최고 기록 등 모든 진행 상황이 지워지고 처음부터 다시 시작합니다.", _centerSubStyle, new Color(1f, 0.6f, 0.5f));
        }
        else
        {
            ShadowLabel(new Rect(0f, top - 140f * scale, width, 80f * scale), "일시정지된", _centerStyle, Color.white);
        }

        for (int i = 0; i < labels.Length; i++)
        {
            Rect rect = new Rect(left, top + step * i, buttonWidth, buttonHeight);
            if (i == _pauseSelection)
            {
                GUI.color = new Color(1f, 0.92f, 0.7f);
                GUI.DrawTexture(new Rect(rect.x - 4f * scale, rect.y - 4f * scale, rect.width + 8f * scale, rect.height + 8f * scale),
                    Texture2D.whiteTexture);
                GUI.color = original;
            }
            if (GUI.Button(rect, labels[i], _buttonStyle))
            {
                _pauseSelection = i;
                ActivatePauseOption(i);
                return;
            }
        }

        // 시작 안내와 같이 장치별 문구를 고른다. Submit은 와일드카드 바인딩이라 키보드는 ENTER, 게임패드는 같은 아래쪽 버튼인 Jump 이름을 쓴다.
        ShadowLabel(new Rect(0f, top + step * labels.Length + 20f * scale, width, 40f * scale),
            _input.ReplaceBindingName(_input.IsUsingGamepad
                ? "D-PAD select    {Jump} confirm    {Cancel} close"
                : "ARROWS select    ENTER confirm    ESC close"), _centerSubStyle, new Color(1f, 1f, 1f, 0.7f));
    }

    /// <summary>
    /// 일시정지 메뉴의 제목과 버튼만 배경 없이 _menuPopupFrame에 화면과 같은 위치로 그린다.
    /// scale, _resetConfirmOpen, _pauseSelection을 사용하며, _menuPopupFrame 내용과 팝업 영역 _menuPopupRect를 변경한다.
    /// </summary>
    private void RenderMenuPopup(float scale)
    {
        float width = Screen.width;
        float height = Screen.height;
        string cancelName = _input.ReplaceBindingName(_input.IsUsingGamepad ? "{Cancel}" : "ESC");
        string throwName = _input.ReplaceBindingName(_input.IsUsingGamepad ? "{Jump}" : "SPACE");
        string[] labels = _resetConfirmOpen
            ? new[] { "삭제", $"취소 ({cancelName})" }
            : new[] { $"재개하는 ({throwName})", "모든 세이브 데이터 삭제", "나가는" };
        float buttonWidth = 520f * scale;
        float buttonHeight = 60f * scale;
        float left = (width - buttonWidth) * 0.5f;
        float top = height * 0.4f;
        float step = buttonHeight + 20f * scale;
        float padding = 40f * scale;
        // 제목부터 마지막 버튼까지 감싸고, ReadPixels로 자를 수 있게 정수 픽셀로 맞춘다.
        Rect popup = Rect.MinMaxRect(
            Mathf.Floor(left - padding), Mathf.Floor(top - 140f * scale - padding * 0.5f),
            Mathf.Ceil(left + buttonWidth + padding), Mathf.Ceil(top + step * (labels.Length - 1) + buttonHeight + padding));
        _menuPopupRect = popup;

        Color original = GUI.color;
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = _menuPopupFrame;
        GL.PushMatrix();
        try
        {
            GL.LoadPixelMatrix(0f, width, height, 0f);
            GL.Clear(true, true, Color.clear);
            GUI.color = Color.white;
            ShadowLabel(new Rect(0f, top - 140f * scale, width, 80f * scale),
                _resetConfirmOpen ? "세이브 데이터 삭제" : "일시정지된", _centerStyle, Color.white);
            for (int i = 0; i < labels.Length; i++)
            {
                Rect rect = new Rect(left, top + step * i, buttonWidth, buttonHeight);
                if (i == _pauseSelection)
                {
                    GUI.color = new Color(1f, 0.92f, 0.7f);
                    GUI.DrawTexture(new Rect(rect.x - 4f * scale, rect.y - 4f * scale, rect.width + 8f * scale, rect.height + 8f * scale),
                        Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
                // 마우스 위치에 따라 눌린 모양이 담기지 않도록 기본 상태로만 그린다.
                _buttonStyle.Draw(rect, new GUIContent(labels[i]), false, false, false, false);
            }
        }
        finally
        {
            GUI.color = original;
            GL.PopMatrix();
            RenderTexture.active = previous;
        }
    }

    /// <summary>
    /// 게임을 종료한다. 에디터에서는 플레이 모드를 끝낸다.
    /// 입력값 없이 애플리케이션 실행 상태를 변경한다.
    /// </summary>
    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>
    /// PlayerPrefs와 화면, 도감 투척물 이미지 파일을 모두 지우고 현재 씬을 다시 불러와 처음 상태로 시작한다.
    /// 입력값 없이 저장 데이터, 디스크의 이미지 파일과 로드된 씬을 변경한다.
    /// </summary>
    private void DeleteAllSaveData()
    {
        // 진행 상황 키가 여러 클래스에 흩어져 있어 Tools/Reset Progress처럼 전부 지운다.
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        _fishSpawner.ScreenCapture.DeleteSavedImage();
        _fishSpawner.CatalogCapture.DeleteSavedImage();
        _fishSpawner.MenuCapture.DeleteSavedImage();

        Scene scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
        // 에디터에서는 Build Settings에 없는 씬도 다시 불러올 수 있도록 경로로 불러온다.
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
        SceneManager.LoadScene(scene.buildIndex);
#endif
    }

    /// <summary>
    /// 마우스 커서를 잠그거나 푼다.
    /// locked를 사용하며, Cursor.lockState와 Cursor.visible을 변경한다.
    /// </summary>
    private static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    /// <summary>
    /// 게임오버 이유를 화면 표시용 문구로 바꾼다.
    /// reason을 사용하며, 표시 문자열을 반환한다.
    /// </summary>
    private static string ReasonText(GameOverReason reason)
    {
        switch (reason)
        {
            case GameOverReason.Sunk: return "가라앉은";
            case GameOverReason.Stopped: return "멈추어진";
            case GameOverReason.HitGround: return "땅을 때린";
            case GameOverReason.OutOfBounds: return "경계 밖의";
            default: return "끝나버린";
        }
    }

    /// <summary>
    /// 화면 높이에 맞춘 HUD 글자 스타일을 만든다. 해상도가 그대로면 다시 만들지 않는다.
    /// Screen.height를 사용하며, GUIStyle 필드와 _styleScale을 변경한다.
    /// </summary>
    private void EnsureStyles()
    {
        float scale = Mathf.Max(0.5f, Screen.height / 1080f);
        if (_bigStyle != null && Mathf.Approximately(scale, _styleScale)) return;

        _styleScale = scale;
        _bigStyle = MakeStyle(48, TextAnchor.UpperLeft, scale);
        _mediumStyle = MakeStyle(32, TextAnchor.UpperLeft, scale);
        _smallStyle = MakeStyle(22, TextAnchor.UpperLeft, scale);
        _centerStyle = MakeStyle(56, TextAnchor.MiddleCenter, scale);
        _centerSubStyle = MakeStyle(22, TextAnchor.UpperCenter, scale);
        _previewStyle = MakeStyle(24, TextAnchor.UpperCenter, scale);
        _previewStyle.wordWrap = true;
        _catalogDetailStyle = MakeStyle(22, TextAnchor.UpperLeft, scale);
        _catalogDetailStyle.fontStyle = FontStyle.Normal;
        _catalogDetailStyle.wordWrap = true;
        _selectionDetailStyle = new GUIStyle(_catalogDetailStyle)
        {
            alignment = TextAnchor.UpperCenter,
        };
        _popupStyle = MakeStyle(52, TextAnchor.MiddleCenter, scale);
        _restartHintStyle = MakeStyle(52, TextAnchor.MiddleCenter, scale);
        _noteStyle = MakeStyle(26, TextAnchor.MiddleCenter, scale);
        _debugStyle = MakeStyle(18, TextAnchor.UpperRight, scale);
        _debugStyle.fontStyle = FontStyle.Normal;
        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = Mathf.RoundToInt(22 * scale),
            fontStyle = FontStyle.Bold,
        };
    }

    /// <summary>
    /// 굵은 흰색 라벨 스타일을 만든다.
    /// size, anchor, scale을 사용하며, 새 GUIStyle을 반환한다.
    /// </summary>
    private static GUIStyle MakeStyle(int size, TextAnchor anchor, float scale)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(size * scale),
            alignment = anchor,
            fontStyle = FontStyle.Bold,
            wordWrap = false,
        };
        style.normal.textColor = Color.white;
        style.hover = style.normal;
        style.active = style.normal;
        style.focused = style.normal;
        style.onNormal = style.normal;
        style.onHover = style.normal;
        style.onActive = style.normal;
        style.onFocused = style.normal;
        return style;
    }

    /// <summary>
    /// 그림자를 깔고 라벨을 그린다.
    /// rect, text, style, color를 사용하며, 화면에 라벨을 그린다.
    /// </summary>
    private static void ShadowLabel(Rect rect, string text, GUIStyle style, Color color)
    {
        Color original = style.normal.textColor;
        Color originalHover = style.hover.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.6f * color.a);
        style.hover.textColor = style.normal.textColor;
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);
        style.normal.textColor = color;
        style.hover.textColor = color;
        GUI.Label(rect, text, style);
        style.normal.textColor = original;
        style.hover.textColor = originalHover;
    }

    /// <summary>
    /// osu! 히트 서클처럼 고정된 노트 원과, 수면에 닿는 순간 노트 원과 겹치도록 줄어드는 접근 원을 그린다.
    /// center, s와 TimeToWaterImpact, IsInJudgeWindow, _spaceHintLeadTime, _approachStartScale을 사용하며, 화면에 노트를 그린다.
    /// </summary>
    private void DrawSpaceNote(Vector2 center, float s)
    {
        // 닿을수록 진해지고, GOOD 이상 구간에 들어오면 금색으로 바뀐다.
        float approach = 1f - Mathf.Clamp01(_playerController.TimeToWaterImpact / Mathf.Max(_spaceHintLeadTime, 0.01f));
        Color noteColor = _playerController.IsInJudgeWindow
            ? new Color(1f, 0.85f, 0.25f, 1f)
            : new Color(1f, 1f, 1f, 0.35f + 0.5f * approach);
        float radius = _noteRadius * s;
        float approachRadius = radius * Mathf.Lerp(Mathf.Max(_approachStartScale, 1f), 1f, approach);

        DrawCircle(_noteDiscTexture, center, radius, new Color(0f, 0f, 0f, 0.45f * noteColor.a));
        DrawCircle(_noteRingTexture, center, radius, noteColor);
        DrawCircle(_noteRingTexture, center, approachRadius, noteColor);
        ShadowLabel(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f),
            _input.ReplaceBindingName("{Jump}"), _noteStyle, noteColor);
    }

    /// <summary>
    /// 판정 성공 직후 노트 원이 판정 색으로 커지며 사라지는 연출을 그린다.
    /// center, s와 _noteHitTime, _noteHitColor, _noteHitDuration을 사용하며, 연출 시간이 지나면 아무것도 그리지 않는다.
    /// </summary>
    private void DrawSpaceNoteHit(Vector2 center, float s)
    {
        float t = (Time.unscaledTime - _noteHitTime) / Mathf.Max(_noteHitDuration, 0.01f);
        if (t >= 1f) return;

        Color color = _noteHitColor;
        color.a = 1f - t;
        float radius = _noteRadius * s * (1f + 0.5f * t);
        DrawCircle(_noteDiscTexture, center, radius, new Color(color.r, color.g, color.b, 0.35f * color.a));
        DrawCircle(_noteRingTexture, center, radius, color);
    }

    /// <summary>
    /// 리듬 노트에 쓰는 원판과 고리 텍스처를 처음 한 번만 만든다.
    /// NOTE_RING_THICKNESS를 사용하며, _noteDiscTexture와 _noteRingTexture를 변경한다.
    /// </summary>
    private void EnsureNoteTextures()
    {
        if (_noteRingTexture != null) return;
        _noteDiscTexture = CreateCircleTexture(0f);
        _noteRingTexture = CreateCircleTexture(NOTE_RING_THICKNESS);
    }

    /// <summary>
    /// 가장자리를 부드럽게 처리한 흰색 원 텍스처를 만든다.
    /// thickness(반지름 대비 고리 두께, 0이면 꽉 찬 원판)와 NOTE_TEXTURE_SIZE를 사용하며, 새 Texture2D를 반환한다.
    /// </summary>
    private static Texture2D CreateCircleTexture(float thickness)
    {
        Texture2D texture = new Texture2D(NOTE_TEXTURE_SIZE, NOTE_TEXTURE_SIZE, TextureFormat.RGBA32, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        Color32[] pixels = new Color32[NOTE_TEXTURE_SIZE * NOTE_TEXTURE_SIZE];
        float half = NOTE_TEXTURE_SIZE * 0.5f;
        float inner = 1f - thickness;
        float edge = 1.5f / half;
        for (int y = 0; y < NOTE_TEXTURE_SIZE; y++)
        {
            for (int x = 0; x < NOTE_TEXTURE_SIZE; x++)
            {
                float distance = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half;
                // 경계에서 1.5픽셀에 걸쳐 투명도를 줄여 계단 현상을 없앤다.
                float alpha = Mathf.Clamp01((1f - distance) / edge);
                if (thickness > 0f) alpha *= Mathf.Clamp01((distance - inner) / edge);
                pixels[y * NOTE_TEXTURE_SIZE + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }

    /// <summary>
    /// texture를 center 중심, radius 반지름 크기로 color를 입혀 그린다.
    /// 그린 뒤 GUI.color를 원래 값으로 되돌린다.
    /// </summary>
    private static void DrawCircle(Texture2D texture, Vector2 center, float radius, Color color)
    {
        Color original = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), texture);
        GUI.color = original;
    }

    /// <summary>
    /// 엔딩 크레딧 UI가 연결된 경우 기존 HUD를 숨기고 엔딩 연출을 시작한다.
    /// 입력값 없이 _showHud와 _endingCredit의 활성화 상태를 변경하며, 참조가 없으면 경고를 출력한다.
    /// </summary>
    public void StartEndingCredit()
    {
        // 기존에 켜져 있는 모든 HUD를 화면에서 숨긴다.
        _showHud = false;
        _endingCredit.gameObject.SetActive(true);
    }

    /// <summary>
    /// 엔딩 크레딧 스킵 이벤트를 받아 크레딧 물고기(endingcredit)를 해금 및 선택하여 즉시 투척한다.
    /// 입력값은 없으며, ThrowCreditFish를 호출한다.
    /// </summary>
    private void HandleEndingSkipped()
    {
        ThrowCreditFish();
    }

    /// <summary>
    /// 엔딩 크레딧 정상 완료 이벤트를 받아 크레딧 물고기를 획득하고 투척 없이 재시작 대기 상태로 전환한다.
    /// 입력값은 없으며, CompleteEndingCredit을 호출한다.
    /// </summary>
    private void HandleEndingCompleted()
    {
        CompleteEndingCredit();
    }

    /// <summary>
    /// 크레딧 화면을 유지한 채 축소 후 투척 전환을 한 번 시작한다.
    /// 입력값 없이 중복 입력을 막고 크레딧 캡처 코루틴을 실행한다.
    /// </summary>
    private void ThrowCreditFish()
    {
        if (_screenThrowPending) return;
        _screenThrowPending = true;
        StartCoroutine(CaptureCreditAndThrow());
    }

    /// <summary>
    /// 크레딧이 그려진 프레임을 캡처한 뒤 기존 재시작 준비와 화면 축소를 순서대로 실행한다.
    /// 입력값 없이 캡처 이미지를 해제하고 전환 중 입력 차단 상태를 복원한다.
    /// </summary>
    private IEnumerator CaptureCreditAndThrow()
    {
        Texture2D screenshot = null;
        try
        {
            yield return new WaitForEndOfFrame();
            screenshot = ScreenCapture.CaptureScreenshotAsTexture();
            PrepareCreditFishForThrow();
            yield return ShrinkPanelAndThrow(screenshot);

            // 화면 축소 연출이 완료된 직후 크레딧 물고기 획득 팝업을 표시한다.
            FishType creditFish = GetFishTypeById(CREDIT_FISH_ID);
            if (creditFish != null)
            {
                ShowFishPopup($"{GetCatalogDisplayName(creditFish)}을(를) 얻은!");
            }
        }
        finally
        {
            if (screenshot != null) Destroy(screenshot);
            _screenThrowPending = false;
        }
    }

    /// <summary>
    /// 크레딧 물고기를 해금 및 선택하고 투척할 수 있도록 재시작 상태로 준비한다.
    /// 입력값은 없으며, 크레딧 물고기 해금, 선택 상태, HUD 및 카메라 복구, 게임 재시작을 수행한다.
    /// </summary>
    private void PrepareCreditFishForThrow()
    {
        UnlockCreditFish();
        SelectCreditFish();
        if (_endingCredit != null)
        {
            _endingCredit.gameObject.SetActive(false);
        }
        if (_cameraController != null)
        {
            _cameraController.enabled = true;
        }
        if (_playerController != null)
        {
            _playerController.gameObject.SetActive(true);
            _playerController.enabled = true;
            Rigidbody rb = _playerController.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
            }
        }
        _showHud = true;
        SetCinematicLetterbox(false);
        Restart(clearNewFish: false);
    }

    /// <summary>
    /// 엔딩 크레딧을 정리하고 크레딧 물고기를 해금 및 선택한 뒤 게임오버(정산) 상태로 전환한다.
    /// 새로 획득한 물고기 목록을 확인할 수 있도록 GameOver 화면을 표시한다.
    /// </summary>
    private void CompleteEndingCredit()
    {
        UnlockCreditFish();
        SelectCreditFish();
        if (_endingCredit != null)
        {
            _endingCredit.gameObject.SetActive(false);
        }
        _showHud = true;
        SetCinematicLetterbox(false);

        // 크레딧 감상이 끝난 직후 크레딧 물고기 획득 팝업을 표시한다.
        FishType creditFish = GetFishTypeById(CREDIT_FISH_ID);
        if (creditFish != null)
        {
            ShowFishPopup($"{GetCatalogDisplayName(creditFish)}을(를) 얻은!");
        }

        HandleGameOver(GameOverReason.Stopped);
    }

    /// <summary>
    /// 지정된 식별자(ID)와 일치하는 물고기 타입을 FishSpawner 목록에서 찾아 반환한다.
    /// id를 사용하며, 일치하는 FishType 또는 null을 반환한다.
    /// </summary>
    private FishType GetFishTypeById(string id)
    {
        if (_fishSpawner == null || _fishSpawner.FishTypes == null) return null;

        for (int i = 0; i < _fishSpawner.FishTypes.Count; i++)
        {
            FishType fish = _fishSpawner.FishTypes[i];
            if (fish != null && fish.Id == id)
            {
                return fish;
            }
        }
        return null;
    }

    /// <summary>
    /// 엔딩 크레딧 물고기(endingcredit)를 현재 투척물로 강제 지정한다.
    /// _fishSpawner를 탐색하며, _projectileIndex를 변경한다.
    /// </summary>
    private void SelectCreditFish()
    {
        if (_fishSpawner == null || _fishSpawner.FishTypes == null) return;

        for (int i = 0; i < _fishSpawner.FishTypes.Count; i++)
        {
            if (_fishSpawner.FishTypes[i].Id == CREDIT_FISH_ID)
            {
                _projectileIndex = i;
                return;
            }
        }
    }

    /// <summary>
    /// 엔딩 크레딧 물고기(endingcredit)를 도감에 등록하고 투척물로 선택할 수 있도록 활성화한다.
    /// _fishSpawner와 _progress를 사용하며, 도감 등록 상태를 변경하고 PlayerPrefs에 저장한다.
    /// </summary>
    private void UnlockCreditFish()
    {
        if (_fishSpawner != null && _fishSpawner.FishTypes != null)
        {
            for (int i = 0; i < _fishSpawner.FishTypes.Count; i++)
            {
                FishType fish = _fishSpawner.FishTypes[i];
                if (fish.Id == CREDIT_FISH_ID)
                {
                    _progress.AddFish(fish);
                    PlayerPrefs.SetInt(FISH_KEY_PREFIX + CREDIT_FISH_ID, 1);
                    PlayerPrefs.Save();
                    if (!_newFish.Contains(fish))
                    {
                        _newFish.Add(fish);
                    }
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 블랙홀 접촉 시 해당 물고기를 획득 처리하고 미지의 행성 엔딩 시네마 연출을 시작한다.
    /// 등록된 물고기 목록과 플레이어 위치로 도감을 갱신하고 비행과 HUD를 정지한 뒤 컷신을 실행한다.
    /// </summary>
    public void StartEndingCinema()
    {
        if (_endingCinemaRunning) return;
        _endingCinemaRunning = true;

        FishType blackholeFish = GetFishTypeById(BLACKHOLE_FISH_ID);
        if (blackholeFish != null)
        {
            _fishSpawner.HandlePlacedFishCaught(blackholeFish, _playerController.transform.position);
            if (!_newFish.Contains(blackholeFish))
            {
                _newFish.Add(blackholeFish);
            }
            // 블랙홀 접촉 즉시 획득 팝업을 표시한다.
            ShowFishPopup($"{GetCatalogDisplayName(blackholeFish)}을(를) 얻은!");
        }

        _state = State.GameOver;
        _showHud = false;

        if (_fishSpawner != null)
        {
            _fishSpawner.Clear();
            _fishSpawner.enabled = false;
        }

        if (_playerController != null)
        {
            _playerController.StopImmediately();
            _playerController.enabled = false;
        }

        _endingCinemaCoroutine = StartCoroutine(PlayEndingCinemaRoutine());
    }

    /// <summary>
    /// 블랙홀 화면 흡수 연출과 미지의 행성 컷신을 순차적으로 재생하고 외계인 획득 및 엔딩 크레딧을 연결한다.
    /// ScreenSuction 연출 후 우주 환경 전환, 카메라 구도, 외계인 반응 및 크레딧 상태를 순차적으로 변경한다.
    /// </summary>
    private IEnumerator PlayEndingCinemaRoutine()
    {
        // 0. 블랙홀 화면 흡수 연출: 화면 전체가 검은 배경 위에서 블랙홀 중심으로 일그러지며 흡수된다.
        yield return StartCoroutine(PlayScreenSuctionRoutine());
        // 21:9 비율은 유지하고 줌아웃 전환 연출은 제거하여 즉시 적용한다.
        SetCinematicLetterbox(true, immediate: true);

        // 1. 우주 환경 전환 및 시네머신 카메라 제어권 확보
        if (_environmentController != null)
        {
            _environmentController.SetSpace(true);
        }

        Vector3 landingSpot = new Vector3(0f, 2f, 3200f);
        Vector3 stoneStartPos = landingSpot + new Vector3(0f, 18f, 0f);
        Vector3 alienStartPos = landingSpot + new Vector3(15.5f, 0f, 14.5f);

        if (_cameraController != null)
        {
            _cameraController.enabled = false;
            Vector3 camPos = landingSpot + new Vector3(-17f, 6.0f, -17f);
            Vector3 camTarget = Vector3.Lerp(landingSpot, alienStartPos, 0.45f) + Vector3.up * 2.5f;
            _cameraController.transform.position = camPos;
            _cameraController.transform.rotation = Quaternion.LookRotation(camTarget - camPos, Vector3.up);

            CinemachineCamera cmCam = _cameraController.GetComponent<CinemachineCamera>();
            if (cmCam != null)
            {
                cmCam.Lens.FieldOfView = 50f;
            }
        }

        // 2. 외계인(Monster_Ending) 배치 및 돌 위치 설정
        GameObject monsterPrefab = Resources.Load<GameObject>("Prefabs/CCTV/Monster/Monster_Ending");
        if (monsterPrefab != null)
        {
            Quaternion alienRot = Quaternion.LookRotation((landingSpot - alienStartPos).normalized, Vector3.up);
            _endingAlienInstance = Instantiate(monsterPrefab, alienStartPos, alienRot);
            _endingAlienInstance.transform.localScale = new Vector3(5f, 5f, 5f);
        }

        if (_playerController != null)
        {
            _playerController.transform.position = stoneStartPos;
            _playerController.transform.rotation = Quaternion.Euler(30f, 45f, 0f);
        }

        yield return new WaitForSeconds(0.3f);

        // 3. 하늘에서 돌 낙하 연출
        if (_playerController != null)
        {
            float fallDuration = 1.2f;
            float elapsed = 0f;
            Quaternion startRot = _playerController.transform.rotation;
            Quaternion endRot = Quaternion.Euler(0f, 180f, 15f);

            while (elapsed < fallDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fallDuration);
                float fallT = t * t;
                _playerController.transform.position = Vector3.Lerp(stoneStartPos, landingSpot, fallT);
                _playerController.transform.rotation = Quaternion.Slerp(startRot, endRot, t);
                yield return null;
            }
            _playerController.transform.position = landingSpot;

            // 착지 시 작은 바운스
            float bounceDuration = 0.25f;
            elapsed = 0f;
            while (elapsed < bounceDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / bounceDuration);
                float yOffset = Mathf.Sin(t * Mathf.PI) * 0.35f;
                _playerController.transform.position = landingSpot + Vector3.up * yOffset;
                yield return null;
            }
            _playerController.transform.position = landingSpot;
        }

        yield return new WaitForSeconds(0.2f);

        // 4. 외계인 반응: 돌을 보고 기뻐하며 점프
        if (_endingAlienInstance != null)
        {
            Vector3 toStone = (landingSpot - alienStartPos);
            toStone.y = 0f;
            if (toStone.sqrMagnitude > 0.01f)
            {
                _endingAlienInstance.transform.rotation = Quaternion.LookRotation(toStone.normalized, Vector3.up);
            }

            int hopCount = 2;
            float hopDuration = 0.45f;
            float hopHeight = 1.8f;

            for (int h = 0; h < hopCount; h++)
            {
                float elapsed = 0f;
                while (elapsed < hopDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / hopDuration);
                    float yOffset = Mathf.Sin(t * Mathf.PI) * hopHeight;
                    _endingAlienInstance.transform.position = alienStartPos + Vector3.up * yOffset;
                    yield return null;
                }
                _endingAlienInstance.transform.position = alienStartPos;
                yield return new WaitForSeconds(0.1f);
            }
        }

        // 5. 외계인이 돌을 줍기 위해 다가감
        if (_endingAlienInstance != null)
        {
            Vector3 startMovePos = _endingAlienInstance.transform.position;
            Vector3 pickUpPos = landingSpot + (alienStartPos - landingSpot).normalized * 2.2f;
            float moveDuration = 2.5f;
            float elapsed = 0f;

            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / moveDuration);
                float hopY = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 4f)) * 0.4f;
                Vector3 currentPos = Vector3.Lerp(startMovePos, pickUpPos, t);
                currentPos.y += hopY;
                _endingAlienInstance.transform.position = currentPos;
                yield return null;
            }
            _endingAlienInstance.transform.position = pickUpPos;
        }

        // 6. 반전 및 획득: 돌이 외계인을 먹는 설정으로 외계인만 즉시 소멸하고 돌은 유지 & 도감 등록
        if (_endingAlienInstance != null)
        {
            Destroy(_endingAlienInstance);
        }

        UnlockMonsterFish();
        FishType monsterFish = GetFishTypeById(MONSTER_FISH_ID);
        if (monsterFish != null)
        {
            ShowFishPopup($"{GetCatalogDisplayName(monsterFish)}을(를) 얻은!");
        }

        // 7. 정적 (1.5초) - 외계인을 먹은 돌만 남은 채 정적 유지
        yield return new WaitForSeconds(1.5f);

        // 8. 엔딩 크레딧 오픈
        _endingCinemaCoroutine = null;
        _endingCinemaRunning = false;
        if (!_maintainCinemaDuringCredits)
        {
            SetCinematicLetterbox(false);
        }
        StartEndingCredit();
    }

    /// <summary>
    /// 외계인 물고기(monster)를 도감에 등록하고 영구 저장한다.
    /// _fishSpawner와 _progress를 사용하며, 도감 등록 상태를 변경하고 PlayerPrefs에 저장한다.
    /// </summary>
    private void UnlockMonsterFish()
    {
        FishType monsterType = null;
        if (_fishSpawner != null && _fishSpawner.FishTypes != null)
        {
            for (int i = 0; i < _fishSpawner.FishTypes.Count; i++)
            {
                if (_fishSpawner.FishTypes[i] != null && _fishSpawner.FishTypes[i].Id == MONSTER_FISH_ID)
                {
                    monsterType = _fishSpawner.FishTypes[i];
                    break;
                }
            }
        }

        if (monsterType != null && _progress != null)
        {
            _progress.AddFish(monsterType);
            if (!_newFish.Contains(monsterType))
            {
                _newFish.Add(monsterType);
            }
        }
        else
        {
            PlayerPrefs.SetInt(FISH_KEY_PREFIX + MONSTER_FISH_ID, 1);
            PlayerPrefs.Save();
            if (monsterType != null && !_newFish.Contains(monsterType))
            {
                _newFish.Add(monsterType);
            }
        }
    }

    /// <summary>
    /// 21:9 시네마틱 레터박스 연출을 활성화하거나 비활성화한다.
    /// active와 immediate를 사용하며, _letterboxCoroutine을 시작하여 카메라 뷰포트와 레터박스를 전환한다.
    /// </summary>
    public void SetCinematicLetterbox(bool active, bool immediate = false)
    {
        if (_letterboxCoroutine != null)
        {
            StopCoroutine(_letterboxCoroutine);
            _letterboxCoroutine = null;
        }

        EnsureLetterboxOverlay();
        _letterboxOverlay.SetActive(true);

        float duration = immediate ? 0f : _cinemaTransitionDuration;
        _letterboxCoroutine = StartCoroutine(TransitionLetterboxRoutine(active, duration));
    }

    /// <summary>
    /// 지정된 시간 동안 카메라 뷰포트와 레터박스 바 크기를 보간하여 부드러운 시네마틱 전환을 수행한다.
    /// active와 duration을 사용하며, Camera.main.rect와 레터박스 트랜스폼 크기를 변경한다.
    /// </summary>
    private IEnumerator TransitionLetterboxRoutine(bool active, float duration)
    {
        float elapsed = 0f;
        float start = active ? 0f : 1f;
        float target = active ? 1f : 0f;

        if (duration <= 0f)
        {
            UpdateLetterboxLayout(target);
        }
        else
        {
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float smoothT = Mathf.SmoothStep(start, target, t);
                UpdateLetterboxLayout(smoothT);
                yield return null;
            }
            UpdateLetterboxLayout(target);
        }

        // 비활성화 완료 시 오버레이를 끈다.
        if (!active && _letterboxOverlay != null)
        {
            _letterboxOverlay.SetActive(false);
        }

        _letterboxCoroutine = null;
    }

    /// <summary>
    /// 상하좌우를 검은색으로 덮을 수 있는 시네마틱 레터박스 오버레이 캔버스와 바 오브젝트들을 준비한다.
    /// 입력값 없이 _letterboxOverlay 및 각 방향의 RectTransform을 생성 및 초기화한다.
    /// </summary>
    private void EnsureLetterboxOverlay()
    {
        if (_letterboxOverlay != null) return;

        _letterboxOverlay = new GameObject("CinematicLetterboxOverlay", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = _letterboxOverlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        // 상단 바를 생성하고 검은색으로 채운다.
        GameObject topObj = new GameObject("TopBar", typeof(RectTransform), typeof(Image));
        topObj.transform.SetParent(_letterboxOverlay.transform, false);
        _topLetterboxBar = topObj.GetComponent<RectTransform>();
        _topLetterboxBar.anchorMin = new Vector2(0f, 1f);
        _topLetterboxBar.anchorMax = new Vector2(1f, 1f);
        _topLetterboxBar.pivot = new Vector2(0.5f, 1f);
        _topLetterboxBar.sizeDelta = Vector2.zero;
        Image topImg = topObj.GetComponent<Image>();
        topImg.color = Color.black;
        topImg.raycastTarget = false;

        // 하단 바를 생성하고 검은색으로 채운다.
        GameObject bottomObj = new GameObject("BottomBar", typeof(RectTransform), typeof(Image));
        bottomObj.transform.SetParent(_letterboxOverlay.transform, false);
        _bottomLetterboxBar = bottomObj.GetComponent<RectTransform>();
        _bottomLetterboxBar.anchorMin = new Vector2(0f, 0f);
        _bottomLetterboxBar.anchorMax = new Vector2(1f, 0f);
        _bottomLetterboxBar.pivot = new Vector2(0.5f, 0f);
        _bottomLetterboxBar.sizeDelta = Vector2.zero;
        Image bottomImg = bottomObj.GetComponent<Image>();
        bottomImg.color = Color.black;
        bottomImg.raycastTarget = false;

        // 좌측 바를 생성한다.
        GameObject leftObj = new GameObject("LeftBar", typeof(RectTransform), typeof(Image));
        leftObj.transform.SetParent(_letterboxOverlay.transform, false);
        _leftLetterboxBar = leftObj.GetComponent<RectTransform>();
        _leftLetterboxBar.anchorMin = new Vector2(0f, 0f);
        _leftLetterboxBar.anchorMax = new Vector2(0f, 1f);
        _leftLetterboxBar.pivot = new Vector2(0f, 0.5f);
        _leftLetterboxBar.sizeDelta = Vector2.zero;
        Image leftImg = leftObj.GetComponent<Image>();
        leftImg.color = Color.black;
        leftImg.raycastTarget = false;

        // 우측 바를 생성한다.
        GameObject rightObj = new GameObject("RightBar", typeof(RectTransform), typeof(Image));
        rightObj.transform.SetParent(_letterboxOverlay.transform, false);
        _rightLetterboxBar = rightObj.GetComponent<RectTransform>();
        _rightLetterboxBar.anchorMin = new Vector2(1f, 0f);
        _rightLetterboxBar.anchorMax = new Vector2(1f, 1f);
        _rightLetterboxBar.pivot = new Vector2(1f, 0.5f);
        _rightLetterboxBar.sizeDelta = Vector2.zero;
        Image rightImg = rightObj.GetComponent<Image>();
        rightImg.color = Color.black;
        rightImg.raycastTarget = false;
    }

    /// <summary>
    /// 진행도에 따라 카메라 뷰포트 rect와 상하좌우 레터박스 바의 크기를 갱신한다.
    /// progress(0~1)를 사용하며, Camera.main.rect와 레터박스 RectTransform의 크기를 변경한다.
    /// </summary>
    private void UpdateLetterboxLayout(float progress)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        EnsureLetterboxOverlay();

        float screenAspect = (float)Screen.width / Screen.height;
        float targetY = 0f;
        float targetX = 0f;

        if (screenAspect < CINEMATIC_ASPECT)
        {
            // 16:9 등 세로가 더 긴 화면에서는 상하단 레터박스를 적용한다.
            float normalizedHeight = screenAspect / CINEMATIC_ASPECT;
            targetY = (1f - normalizedHeight) * 0.5f;
        }
        else
        {
            // 21:9보다 넓은 화면에서는 좌우 필러박스를 적용한다.
            float normalizedWidth = CINEMATIC_ASPECT / screenAspect;
            targetX = (1f - normalizedWidth) * 0.5f;
        }

        float currentY = Mathf.Lerp(0f, targetY, progress);
        float currentX = Mathf.Lerp(0f, targetX, progress);

        // 카메라의 뷰포트 영역을 21:9 비율에 맞춰 조정한다.
        cam.rect = new Rect(currentX, currentY, 1f - currentX * 2f, 1f - currentY * 2f);

        // 빈 영역을 채우는 레터박스 바의 픽셀 크기를 설정한다.
        float barHeight = Screen.height * currentY;
        float barWidth = Screen.width * currentX;

        _topLetterboxBar.sizeDelta = new Vector2(0f, barHeight);
        _bottomLetterboxBar.sizeDelta = new Vector2(0f, barHeight);
        _leftLetterboxBar.sizeDelta = new Vector2(barWidth, 0f);
        _rightLetterboxBar.sizeDelta = new Vector2(barWidth, 0f);
    }

    #region 흡수 연출
    /// <summary>
    /// 블랙홀 접촉 시 화면 전체를 캡처하여 검은 배경 위에서 블랙홀 중심으로 일그러지며 흡수되는 연출을 실행한다.
    /// _endingBlackHole과 ScreenSuction 셰이더를 사용하며, 화면 캡처 오버레이 생성, 흡수 진행도 갱신 및 리소스 해제를 수행한다.
    /// </summary>
    private IEnumerator PlayScreenSuctionRoutine()
    {
        try
        {
            // 접촉 프레임 렌더가 완료되기를 기다려 전체 화면을 캡처한다.
            yield return new WaitForEndOfFrame();

            _suctionFrame = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32)
            {
                name = "Ending Screen Suction Capture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            _suctionFrame.Create();

            RenderTexture rawFrame = RenderTexture.GetTemporary(_suctionFrame.width, _suctionFrame.height, 0, RenderTextureFormat.ARGB32);
            try
            {
                UnityEngine.ScreenCapture.CaptureScreenshotIntoRenderTexture(rawFrame);
                Vector2 scale = SystemInfo.graphicsUVStartsAtTop ? new Vector2(1f, -1f) : Vector2.one;
                Vector2 offset = SystemInfo.graphicsUVStartsAtTop ? Vector2.up : Vector2.zero;
                Graphics.Blit(rawFrame, _suctionFrame, scale, offset);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rawFrame);
            }

            // 카메라 기준으로 블랙홀의 뷰포트 좌표를 계산한다.
            Camera cam = Camera.main;
            Vector2 holePosition = new Vector2(0.5f, 0.5f);
            if (cam != null)
            {
                Vector3 viewport = cam.WorldToViewportPoint(_endingBlackHole.position);
                holePosition = viewport.z > 0f
                    ? new Vector2(Mathf.Clamp01(viewport.x), Mathf.Clamp01(viewport.y))
                    : new Vector2(0.5f, 0.5f);
            }

            // 오버레이 캔버스를 최상단에 생성한다.
            _suctionOverlay = new GameObject("Ending Screen Suction Overlay", typeof(Canvas));
            Canvas canvas = _suctionOverlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            // 검은색 배경: 화면이 일그러지며 흡수되는 동안 드러나는 바탕을 검은색으로 처리한다.
            GameObject background = new GameObject("Black Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(_suctionOverlay.transform, false);
            StretchSuctionRect(background.GetComponent<RectTransform>());
            Image bgImg = background.GetComponent<Image>();
            bgImg.color = Color.black;
            bgImg.raycastTarget = false;

            // 흡수되는 천 메시(Captured Cloth)를 생성하고 ScreenSuction 셰이더를 연결한다.
            GameObject cloth = new GameObject("Captured Cloth", typeof(RectTransform), typeof(CanvasRenderer), typeof(ScreenSuctionGraphic));
            cloth.transform.SetParent(_suctionOverlay.transform, false);
            StretchSuctionRect(cloth.GetComponent<RectTransform>());

            Shader suctionShader = Resources.Load<Shader>("Shader/ScreenSuction");
            _suctionMaterial = new Material(suctionShader);
            _suctionMaterial.SetTexture("_MainTex", _suctionFrame);
            _suctionMaterial.SetVector("_SuctionCenter", new Vector4(holePosition.x, holePosition.y, 0f, 0f));
            _suctionMaterial.SetFloat("_Aspect", (float)_suctionFrame.width / _suctionFrame.height);
            _suctionMaterial.SetFloat("_SuctionStrength", 2.4f);
            _suctionMaterial.SetFloat("_SuctionRadius", 0.65f);
            _suctionMaterial.SetFloat("_Falloff", 1.8f);
            _suctionMaterial.SetFloat("_StretchStrength", 1.6f);
            _suctionMaterial.SetFloat("_WrinkleStrength", 0.065f);
            _suctionMaterial.SetFloat("_TwistStrength", 0.045f);
            _suctionMaterial.SetFloat("_FinalCollapseSpeed", 3.5f);

            ScreenSuctionGraphic graphic = cloth.GetComponent<ScreenSuctionGraphic>();
            graphic.material = _suctionMaterial;
            graphic.raycastTarget = false;
            graphic.SetTexture(_suctionFrame);

            // 흡수 진행도를 0에서 1까지 보간하여 화면이 블랙홀로 빨려 들어가게 한다.
            float duration = _blackHoleSuctionDuration;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < duration)
            {
                float progress = Mathf.Clamp01((Time.realtimeSinceStartup - start) / duration);
                _suctionMaterial.SetFloat("_Progress", progress);
                yield return null;
            }

            _suctionMaterial.SetFloat("_Progress", 1f);

            // 완전 흡수 후 정적(검은 화면)을 잠시 유지한다.
            yield return new WaitForSecondsRealtime(0.2f);
        }
        finally
        {
            ReleaseScreenSuction();
        }
    }

    /// <summary>
    /// RectTransform의 앵커와 여백을 부모 전체 화면 크기로 맞춘다.
    /// rect를 사용하며, anchorMin, anchorMax, offsetMin, offsetMax를 변경한다.
    /// </summary>
    private void StretchSuctionRect(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// 화면 흡수 연출에 사용된 오버레이 캔버스, 머티리얼 및 렌더 텍스처를 안전하게 해제한다.
    /// 입력값은 없으며, _suctionOverlay, _suctionMaterial, _suctionFrame을 정리한다.
    /// </summary>
    private void ReleaseScreenSuction()
    {
        if (_suctionOverlay != null)
        {
            Destroy(_suctionOverlay);
            _suctionOverlay = null;
        }
        if (_suctionMaterial != null)
        {
            Destroy(_suctionMaterial);
            _suctionMaterial = null;
        }
        if (_suctionFrame != null)
        {
            _suctionFrame.Release();
            Destroy(_suctionFrame);
            _suctionFrame = null;
        }
    }
    #endregion

    #region 오프닝 연출
    /// <summary>
    /// 최초 투척 입력 시 블랙홀을 조명하고 플레이 지점으로 이동하는 오프닝 연출을 시작한다.
    /// 입력값은 없으며, _openingCinemaRunning과 _showHud를 변경하고 오프닝 코루틴을 실행한다.
    /// </summary>
    private void StartOpeningCinema()
    {
        if (_openingCinemaRunning) return;
        _openingCinemaRunning = true;
        _showHud = false;
        _openingCinemaCoroutine = StartCoroutine(PlayOpeningCinemaRoutine());
    }

    /// <summary>
    /// 블랙홀을 응시하며 21:9 비율로 시작하여 플레이 위치로 카메라를 후진 이동시킨 후 첫 투척을 실행한다.
    /// _endingBlackHole 위치, 높이 및 피치 오프셋 설정을 사용하며, 카메라 위치, 레터박스, HUD 표시 상태와 _hasThrown을 변경하고 Throw를 호출한다.
    /// </summary>
    private IEnumerator PlayOpeningCinemaRoutine()
    {
        SetCinematicLetterbox(true, immediate: true);
        _cameraController.enabled = false;

        Vector3 playCamPos = _cameraController.transform.position;
        Quaternion playCamRot = _cameraController.transform.rotation;
        Vector3 blackHolePos = _endingBlackHole.position;

        Vector3 toPlayCam = playCamPos - blackHolePos;
        Vector3 startCamPos = blackHolePos + toPlayCam.normalized * _openingCloseDistance;
        startCamPos.y += _openingHeightOffset;

        Quaternion startCamRot = Quaternion.LookRotation((blackHolePos - startCamPos).normalized, Vector3.up);
        if (_openingPitchOffset != 0f)
        {
            startCamRot *= Quaternion.Euler(_openingPitchOffset, 0f, 0f);
        }

        _cameraController.transform.position = startCamPos;
        _cameraController.transform.rotation = startCamRot;

        // 1. 블랙홀을 클로즈업 상태로 응시하며 정지 대기
        float elapsed = 0f;
        while (elapsed < _openingHoldDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // 2. 블랙홀을 바라보며 플레이 시작 위치로 부드럽게 후진 이동
        elapsed = 0f;
        while (elapsed < _openingTravelDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _openingTravelDuration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            Vector3 currentPos = Vector3.Lerp(startCamPos, playCamPos, smoothT);
            Quaternion lookAtHole = Quaternion.LookRotation((blackHolePos - currentPos).normalized, Vector3.up);
            if (_openingPitchOffset != 0f)
            {
                lookAtHole *= Quaternion.Euler(Mathf.Lerp(_openingPitchOffset, 0f, smoothT), 0f, 0f);
            }
            Quaternion currentRot = Quaternion.Slerp(lookAtHole, playCamRot, smoothT);

            _cameraController.transform.position = currentPos;
            _cameraController.transform.rotation = currentRot;
            yield return null;
        }

        _cameraController.transform.position = playCamPos;
        _cameraController.transform.rotation = playCamRot;
        _cameraController.enabled = true;
        _cameraController.SnapBehindTarget();

        // 3. 21:9 레터박스 해제 및 최초 투척 실행
        SetCinematicLetterbox(false);
        _showHud = true;
        _openingCinemaRunning = false;
        _openingCinemaCoroutine = null;
        _hasThrown = true;
        Throw();
    }
    #endregion
}
