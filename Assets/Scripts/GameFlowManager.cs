using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;
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
    private const int DEFAULT_FISH_INDEX = -1;
    // 게임오버 직후 SPACE 연타로 바로 재시작되지 않게 기다리는 시간(초)
    private const float RETRY_INPUT_DELAY = 0.6f;
    private const string CREDIT_FISH_ID = "endingcredit";
    private const string MONSTER_FISH_ID = "monster";
    private const string FISH_KEY_PREFIX = "SkipStoneV2.Fish.";

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
    private InputSystem_Actions _inputActions;

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
    private bool _catalogOpen;
    private bool _catalogCapturePending;
    private FishType _catalogSelectedFish;
    private JObject _catalogEntries;
    private Vector2 _catalogScroll;
    private Vector2 _resultScroll;
    private readonly List<int> _ownedOptions = new List<int>();
    private readonly List<FishType> _newFish = new List<FishType>();
    private bool _endingCinemaRunning;
    private GameObject _endingAlienInstance;

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
    private string _popupText;
    private Color _popupColor;
    private float _popupTime = -10f;
    private GUIStyle _bigStyle;
    private GUIStyle _mediumStyle;
    private GUIStyle _smallStyle;
    private GUIStyle _centerStyle;
    private GUIStyle _centerSubStyle;
    private GUIStyle _popupStyle;
    private GUIStyle _debugStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _previewStyle;
    private GUIStyle _catalogDetailStyle;
    private float _styleScale = -1f;
    private FishSelectionPreview _fishSelectionPreview;
    private FishSelectionPreview _ownedFishSelectionPreview;

    [Header("화면 투척 전환")]
    [SerializeField, Min(0.1f)] private float _panelShrinkDuration = 0.65f;
    private bool _screenThrowPending;

    [Header("엔딩 크레딧 UI")]
    [SerializeField] private EndingCreditUI _endingCredit;

    void Start()
    {
        LockCursor(true);

        _inputActions = new InputSystem_Actions();
        _inputActions.UI.Enable();
        _inputActions.Player.Enable();

        _playerController.Initialize(_inputActions, _fishSpawner);
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
        _cameraController.Initialize(_fishGenerator);

        _bestSkips = PlayerPrefs.GetInt(BEST_SKIPS_KEY, 0);
        _bestDistance = PlayerPrefs.GetFloat(BEST_DISTANCE_KEY, 0f);
        _state = State.Ready;
        if (_environmentController != null) _environmentController.Roll(SelectedFish());
        _fishSelectionPreview = new FishSelectionPreview();
        _ownedFishSelectionPreview = new FishSelectionPreview(true);
        _ownedFishSelectionPreview.PrepareDefaultFishTexture(_fishGenerator.GetComponent<MeshFilter>().sharedMesh,
            _fishGenerator.GetComponent<MeshRenderer>().sharedMaterials);
        _catalogEntries = JObject.Parse(Resources.Load<TextAsset>("FishCatalog").text);
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
        if (_endingCredit != null)
        {
            _endingCredit.OnEndingCompleted -= HandleEndingCompleted;
            _endingCredit.OnEndingSkipped -= HandleEndingSkipped;
        }
        _inputActions.Dispose();
        _fishSelectionPreview.Dispose();
        _ownedFishSelectionPreview.Dispose();
    }

    void Update()
    {
        if (_screenThrowPending || _catalogCapturePending || _fishSpawner.ScreenCapture.IsCapturing || Time.timeScale == 0f) return;
        Keyboard keyboard = Keyboard.current;
        Gamepad gamepad = Gamepad.current;
        if (_state == State.Ready && keyboard != null && keyboard.tabKey.wasPressedThisFrame)
        {
            _catalogOpen = !_catalogOpen;
            LockCursor(!_catalogOpen);
            return;
        }
        if (_catalogOpen)
        {
            if (_inputActions.Player.Jump.WasPressedThisFrame())
            {
                StartCoroutine(CaptureCatalogAndThrow());
                return;
            }
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                _catalogOpen = false;
                LockCursor(true);
            }
            return;
        }
        if (_endingCredit != null && _endingCredit.gameObject.activeInHierarchy)
        {
            if (_inputActions.Player.Jump.WasPressedThisFrame())
            {
                ThrowCreditFish();
                return;
            }
            return;
        }
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            LockCursor(Cursor.lockState != CursorLockMode.Locked);
        }
        bool restartPressed = (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            || (gamepad != null && (gamepad.buttonNorth.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame));
        if (restartPressed)
        {
            if (_state == State.Flying && keyboard != null && keyboard.rKey.wasPressedThisFrame)
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
        bool jumpPressed = _inputActions.Player.Jump.WasPressedThisFrame();
        if (_state == State.GameOver)
        {
            if (jumpPressed && Time.unscaledTime - _gameOverTime >= RETRY_INPUT_DELAY) Restart();
            return;
        }
        if (_state != State.Ready) return;

        if ((keyboard != null && keyboard.qKey.wasPressedThisFrame) || (gamepad != null && gamepad.leftShoulder.wasPressedThisFrame))
        {
            CycleProjectile(-1);
        }
        if ((keyboard != null && keyboard.eKey.wasPressedThisFrame) || (gamepad != null && gamepad.rightShoulder.wasPressedThisFrame))
        {
            CycleProjectile(1);
        }

        if (jumpPressed)
        {
            Throw();
            return;
        }
        if (!_inputActions.UI.Click.WasPressedThisFrame()) return;

        // 커서가 풀려 있을 때의 클릭은 다시 잠그는 용도로만 쓴다.
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            LockCursor(true);
            return;
        }
        Throw();
    }

    void LateUpdate()
    {
        if (_showHud && _state == State.GameOver)
        {
            foreach (FishType fish in _newFish)
            {
                if (fish.Id == "screen" || fish.Id == "catalog") continue;
                Fish prefab = _fishSpawner.GetFishPrefab(fish);
                if (prefab != null) _ownedFishSelectionPreview.PrepareTexture(prefab);
            }
            return;
        }
        if (!_showHud || _state != State.Ready) return;
        RefreshOwnedOptions();
        for (int i = 0; i < _fishSpawner.FishTypes.Count; i++)
        {
            FishType fish = _fishSpawner.FishTypes[i];
            if (fish.Id != "screen" && fish.Id != "catalog"
                && (_progress.IsFishRegistered(fish)
                    || (_catalogOpen && _fishSpawner.GetCatalogState(fish) != FishCatalogState.Undiscovered)))
            {
                FishSelectionPreview previews = _progress.IsFishRegistered(fish) ? _ownedFishSelectionPreview : _fishSelectionPreview;
                previews.PrepareTexture(_fishSpawner.FishPrefabs[i]);
            }
        }
    }

    void OnGUI()
    {
        if (_screenThrowPending && !_catalogOpen) return;
        if (_fishSpawner.ScreenCapture.IsCapturing && _fishSpawner.ScreenCapture.HasFrame) return;
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

        ShadowLabel(new Rect(24f * s, 16f * s, 600f * s, 64f * s), $"SKIP  {_playerController.SkipCount}", _bigStyle, Color.white);
        ShadowLabel(new Rect(24f * s, 76f * s, 600f * s, 44f * s), $"{_playerController.Distance:0.0} m", _mediumStyle, Color.white);
        ShadowLabel(new Rect(24f * s, 118f * s, 800f * s, 36f * s), $"BEST  {_bestSkips} skips / {_bestDistance:0.0} m", _smallStyle, new Color(1f, 1f, 1f, 0.75f));

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
            ShadowLabel(new Rect(0f, height * _spaceHintHeight, width, 80f * s), "MASH SPACE!", _centerStyle, slideColor);
        }
        else if (_playerController.CanJudge && _playerController.TimeToWaterImpact <= _spaceHintLeadTime)
        {
            // 닿을수록 진해지고, GOOD 이상 구간에 들어오면 금색으로 바뀐다.
            float approach = 1f - Mathf.Clamp01(_playerController.TimeToWaterImpact / Mathf.Max(_spaceHintLeadTime, 0.01f));
            Color hintColor = _playerController.IsInJudgeWindow
                ? new Color(1f, 0.85f, 0.25f, 1f)
                : new Color(1f, 1f, 1f, 0.35f + 0.5f * approach);
            ShadowLabel(new Rect(0f, height * _spaceHintHeight, width, 80f * s), "SPACE", _centerStyle, hintColor);
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
                $"speed {_playerController.Speed:0.0} m/s\n" +
                $"tilt {_playerController.CurrentPitch:0} / {_playerController.TargetPitch:0} deg  roll {_playerController.TargetRoll:0}\n" +
                $"spin {_playerController.SpinRate:0.0} rad/s  stab {_playerController.Stability:0.00}\n" +
                $"seed {_playerController.FishSeed}";
            ShadowLabel(new Rect(width - 420f * s, 16f * s, 400f * s, 140f * s), debug, _debugStyle, new Color(1f, 1f, 1f, 0.8f));
        }
    }

    /// <summary>
    /// 선택한 던질 거리(기본 물고기 또는 도감에 등록된 물고기)를 최대 강화 기준 능력치로 던진다.
    /// _projectileIndex와 진행 상황을 사용하며, _state를 변경한다.
    /// </summary>
    private void Throw()
    {
        FishType fish = SelectedFish();
        ThrowModifiers modifiers = fish != null
            ? fish.ToThrowModifiers(_progress.PowerMultiplier, _progress.SpinMultiplier)
            : ThrowModifiers.ForFish(_progress.PowerMultiplier, _progress.SpinMultiplier);
        _state = State.Flying;
        _screenCaptureTriggered = false;
        _playerController.Throw(modifiers);
    }

    /// <summary>
    /// 열린 도감 프레임의 캡처와 등록을 기다린 뒤 화면을 투척 크기로 축소하고 던진다.
    /// 입력값 없이 캡처 중 입력을 막고 도감, 커서, 투척 외형과 게임 상태를 변경한다.
    /// </summary>
    private IEnumerator CaptureCatalogAndThrow()
    {
        _catalogCapturePending = true;
        _screenThrowPending = true;
        Texture2D screenshot = null;
        try
        {
            yield return _fishSpawner.CatalogCapture.CapturePanel();
            screenshot = ScreenCapture.CaptureScreenshotAsTexture();
            for (int i = 0; i < _fishSpawner.FishTypes.Count; i++)
            {
                if (_fishSpawner.FishTypes[i].Id != "catalog") continue;
                _projectileIndex = i;
                break;
            }
            _catalogOpen = false;
            LockCursor(true);
            ApplyProjectileShape();
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
    /// 입력값은 없으며, 물고기, 던질 거리 메시, 물고기 상태, 카메라, _state, 날씨를 변경한다.
    /// </summary>
    private void Restart()
    {
        _newFish.Clear();
        _resultScroll = Vector2.zero;
        _catalogOpen = false;
        if (_endingAlienInstance != null)
        {
            Destroy(_endingAlienInstance);
            _endingAlienInstance = null;
        }
        _endingCinemaRunning = false;
        _fishSpawner.Clear();
        ApplyProjectileShape();
        _playerController.ResetToStart();
        _cameraController.SnapBehindTarget();
        _popupTime = -10f;
        _perfectCount = 0;
        _goodCount = 0;
        _missCount = 0;
        _state = State.Ready;
        if (_environmentController != null) _environmentController.Roll(SelectedFish());
        LockCursor(true);
    }

    /// <summary>
    /// 기본 물고기와 도감에 등록된 물고기 사이에서 던질 거리를 바꾼다. 등록되지 않은 종류는 건너뛴다.
    /// direction(-1 또는 1)을 사용하며, _projectileIndex, 던질 거리 메시와 고른 물고기에 따른 목표 날씨를 변경한다.
    /// </summary>
    private void CycleProjectile(int direction)
    {
        RefreshOwnedOptions();
        int option = _ownedOptions.IndexOf(_projectileIndex);
        int index = _ownedOptions[(option + direction + _ownedOptions.Count) % _ownedOptions.Count];
        if (index == _projectileIndex) return;
        _projectileIndex = index;
        ApplyProjectileShape();
        if (_environmentController != null) _environmentController.ChangeFish(SelectedFish());
    }

    /// <summary>
    /// 현재 도감 등록 상태를 사용해 기본 물고기와 보유 종류만 선택 목록에 넣는다.
    /// 입력값 없이 _ownedOptions를 갱신하며 미보유 종류는 제외한다.
    /// </summary>
    private void RefreshOwnedOptions()
    {
        _ownedOptions.Clear();
        _ownedOptions.Add(DEFAULT_FISH_INDEX);
        for (int i = 0; i < _fishSpawner.FishTypes.Count; i++)
        {
            if (_progress.IsFishRegistered(_fishSpawner.FishTypes[i])) _ownedOptions.Add(i);
        }
    }

    /// <summary>
    /// 최초 등록된 fish를 현재 판의 신규 획득 목록에 추가한다.
    /// 모든 등록 경로의 이벤트를 받아 _newFish와 획득 팝업을 변경한다.
    /// </summary>
    private void HandleFishRegistered(FishType fish)
    {
        _newFish.Add(fish);
        ShowPopup($"{GetCatalogDisplayName(fish)} 획득했다!", new Color(1f, 0.85f, 0.3f));
    }

    /// <summary>
    /// 선택한 던질 거리에 맞게 기본 물고기 또는 프리팹 메시를 만든다. 프리팹은 던지기 전부터 파닥이게 한다.
    /// _projectileIndex를 사용하며, 던질 거리 메시, 파닥임 상태와 스포너가 아는 플레이어 물고기를 변경한다.
    /// </summary>
    private void ApplyProjectileShape()
    {
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
            ShowPopup($"SKIP {count}", Color.white);
        }
    }

    /// <summary>
    /// SPACE 판정 결과를 표시하고, 성공하면 선택한 프리팹의 애니메이션 또는 레이저를 재생한다.
    /// judge와 timingError(초, 빠르면 음수)를 사용하며, 팝업, 판정 횟수와 프리팹 연출 상태를 변경한다.
    /// </summary>
    private void HandleJudge(SkipJudge judge, float timingError)
    {
        if (judge == SkipJudge.Perfect || judge == SkipJudge.Good)
        {
            PlayFishInteraction();
        }

        string direction = timingError < 0f ? "EARLY" : "LATE";
        switch (judge)
        {
            case SkipJudge.Perfect:
                _perfectCount++;
                ShowPopup("PERFECT!", JudgeColor(judge));
                break;
            case SkipJudge.Good:
                _goodCount++;
                ShowPopup($"GOOD  {direction}", JudgeColor(judge));
                break;
            case SkipJudge.Miss:
                _missCount++;
                ShowPopup($"MISS  TOO {direction}", JudgeColor(judge));
                break;
        }
    }

    /// <summary>
    /// SCREEN 캡처 완료 이벤트를 받아 해금 팝업을 표시한다.
    /// 입력값 없이 팝업 상태를 변경한다.
    /// </summary>
    private void HandleScreenUnlocked()
    {
        ShowPopup("SCREEN 획득했다!", new Color(1f, 0.85f, 0.3f));
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
    /// 선택한 물고기의 공격, 발 교대 또는 레이저 연출을 실행한다.
    /// 현재 던질 거리의 자식 컴포넌트를 사용하며, 각 프리팹의 애니메이션과 레이저 표시 상태를 변경한다.
    /// </summary>
    private void PlayFishInteraction()
    {
        BallController ballController = _fishGenerator.GetComponentInChildren<BallController>();
        if (ballController != null)
        {
            ballController.PlayAttackSequence();
        }
        HopakJumpAnimation hopakAnimation = _fishGenerator.GetComponentInChildren<HopakJumpAnimation>();
        if (hopakAnimation != null)
        {
            hopakAnimation.PlayJumpSegment();
        }
        AlkagiLaser laser = _fishGenerator.GetComponentInChildren<AlkagiLaser>();
        if (laser != null)
        {
            laser.Fire();
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
        ShowPopup($"{GetCatalogDisplayName(fish)} 획득했다!", new Color(1f, 0.85f, 0.3f));
    }

    /// <summary>
    /// 정산 상태로 바꾸고 최고 기록을 저장한 뒤, 다시하기 버튼을 누를 수 있게 커서를 푼다.
    /// reason과 현재 튕김 횟수, 거리를 사용하며, _state와 최고 기록, PlayerPrefs, 커서를 변경한다.
    /// </summary>
    private void HandleGameOver(GameOverReason reason)
    {
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
    /// 던지기 전 안내와 지금 던질 거리를 그린다.
    /// 화면 크기와 scale을 사용하며, 화면에 라벨을 그린다.
    /// </summary>
    private void DrawReady(float width, float height, float scale)
    {
        ShadowLabel(new Rect(0f, height * 0.18f, width, 80f * scale), "SPACE TO THROW", _centerStyle, Color.white);
        ShadowLabel(new Rect(0f, height * 0.18f + 80f * scale, width, 40f * scale),
            "ARROWS tilt    A/D curve    SPACE when it hits the water    R new fish", _centerSubStyle, Color.white);
        ShadowLabel(new Rect(0f, height * 0.18f + 112f * scale, width, 40f * scale),
            "PAD   A throw/skip    R-stick tilt    L-stick/triggers curve    Y new fish", _centerSubStyle, new Color(1f, 1f, 1f, 0.7f));

        ShadowLabel(new Rect(0f, height * _spaceHintHeight, width, 40f * scale),
            "<  Q/LB   THROW   E/RB  >    TAB 도감", _centerSubStyle, new Color(1f, 0.92f, 0.7f));
        DrawFishPreviews(width, height, scale);
    }

    /// <summary>
    /// 보유 선택 목록에서 현재 항목과 앞뒤 최대 두 개를 표시한다.
    /// 화면 크기와 scale을 사용하며 보유 모델과 이름만 그린다.
    /// </summary>
    private void DrawFishPreviews(float width, float height, float scale)
    {
        int optionCount = _ownedOptions.Count;
        int selectedOption = _ownedOptions.IndexOf(_projectileIndex);
        float slotWidth = Mathf.Min(234f * scale, width * 0.16f);
        float slotHeight = slotWidth * 2f / 3f;
        float slotSpacing = width * 0.21f;
        float top = height * _spaceHintHeight - slotHeight - 58f * scale;
        for (int offset = -Mathf.Min(2, (optionCount - 1) / 2); offset <= Mathf.Min(2, optionCount / 2); offset++)
        {
            Rect rect = new Rect(width * 0.5f + offset * slotSpacing - slotWidth * 0.5f, top, slotWidth, slotHeight);
            if (offset == 0)
            {
                FishType selectedFish = SelectedFish();
                ShadowLabel(new Rect(rect.x, rect.yMax, slotWidth, 54f * scale),
                    _fishSpawner.GetCatalogState(selectedFish) != FishCatalogState.Undiscovered
                        ? GetCatalogDisplayName(selectedFish) : "?", _previewStyle, new Color(1f, 0.92f, 0.7f));
                continue;
            }
            int index = _ownedOptions[(selectedOption + offset + optionCount) % optionCount];
            if (index == DEFAULT_FISH_INDEX)
            {
                GUI.DrawTexture(rect, _ownedFishSelectionPreview.DefaultFishTexture, ScaleMode.ScaleToFit, true);
                ShadowLabel(new Rect(rect.x, rect.yMax, slotWidth, 54f * scale),
                    GetCatalogDisplayName(null), _previewStyle, new Color(0.75f, 0.75f, 0.75f, 0.7f));
                continue;
            }

            FishType fish = _fishSpawner.FishTypes[index];
            Texture preview = fish.Id == "screen" ? _fishSpawner.ScreenCapture.Image
                : fish.Id == "catalog" ? _fishSpawner.CatalogCapture.Image
                : _ownedFishSelectionPreview.GetTexture(_fishSpawner.FishPrefabs[index]);
            if (preview != null) GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit, true);
            ShadowLabel(new Rect(rect.x, rect.yMax, slotWidth, 54f * scale), GetCatalogDisplayName(fish), _previewStyle,
                new Color(0.75f, 0.75f, 0.75f, 0.7f));
        }
    }

    /// <summary>
    /// 게임오버 결과와 이번 판에 처음 획득한 종류의 컬러 그리드, 다시하기 버튼을 그린다.
    /// 화면 크기, scale과 _newFish를 사용하며 버튼을 누르면 재시작한다.
    /// </summary>
    private void DrawShop(float width, float height, float scale)
    {
        ShadowLabel(new Rect(0f, height * 0.12f, width, 80f * scale), "GAME OVER", _centerStyle, Color.white);
        string result = $"{ReasonText(_gameOverReason)}    {_playerController.SkipCount} skips / {_playerController.Distance:0.0} m";
        ShadowLabel(new Rect(0f, height * 0.12f + 80f * scale, width, 40f * scale), result, _centerSubStyle, Color.white);
        string timing = $"PERFECT {_perfectCount}    GOOD {_goodCount}    MISS {_missCount}";
        ShadowLabel(new Rect(0f, height * 0.12f + 115f * scale, width, 40f * scale), timing, _centerSubStyle, new Color(1f, 0.92f, 0.7f));

        ShadowLabel(new Rect(0f, height * 0.39f, width, 40f * scale), $"새로 획득한 종류  {_newFish.Count}", _centerSubStyle, Color.white);
        Rect viewport = new Rect(width * 0.08f, height * 0.45f, width * 0.84f, height * 0.3f);
        int columns = Mathf.Max(1, Mathf.FloorToInt(viewport.width / (240f * scale)));
        float contentWidth = viewport.width - 24f * scale;
        float cellWidth = contentWidth / columns;
        float cellHeight = 220f * scale;
        int rows = Mathf.CeilToInt((float)_newFish.Count / columns);
        _resultScroll = GUI.BeginScrollView(viewport, _resultScroll,
            new Rect(0f, 0f, contentWidth, Mathf.Max(viewport.height, rows * cellHeight)));
        if (_newFish.Count == 0)
            ShadowLabel(new Rect(0f, 0f, contentWidth, 38f * scale), "새로 획득한 종류가 없습니다", _centerSubStyle, new Color(1f, 1f, 1f, 0.7f));
        for (int i = 0; i < _newFish.Count; i++)
        {
            FishType fish = _newFish[i];
            Fish prefab = _fishSpawner.GetFishPrefab(fish);
            Texture texture = fish.Id == "screen" ? _fishSpawner.ScreenCapture.Image
                : fish.Id == "catalog" ? _fishSpawner.CatalogCapture.Image
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
                fish.DisplayName, _previewStyle, new Color(1f, 0.92f, 0.7f));
        }
        GUI.EndScrollView();
        float buttonWidth = 620f * scale;
        float buttonHeight = 60f * scale;
        float left = (width - buttonWidth) * 0.5f;
        if (GUI.Button(new Rect(left, height * 0.8f, buttonWidth, buttonHeight), "RETRY   (SPACE / A / R)", _buttonStyle))
        {
            Restart();
        }
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
        return string.IsNullOrWhiteSpace(displayName) ? fish == null ? "FISH" : fish.DisplayName : displayName;
    }

    /// <summary>
    /// 전체 종류를 보유 상태와 함께 그리드로 그리고 선택한 항목의 JSON 상세 정보를 표시한다.
    /// 화면 크기, scale과 마우스 입력을 사용하며 선택 항목과 도감·커서 상태를 변경한다.
    /// </summary>
    private void DrawCatalog(float width, float height, float scale)
    {
        Color original = GUI.color;
        GUI.color = new Color(0.06f, 0.09f, 0.12f, 0.96f);
        GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
        GUI.color = original;
        ShadowLabel(new Rect(0f, 24f * scale, width, 80f * scale), "도감", _centerStyle, Color.white);
        ShadowLabel(new Rect(0f, 108f * scale, width, 40f * scale),
            $"보유  {_ownedOptions.Count} / {_fishSpawner.FishTypes.Count + 1}    TAB / ESC 닫기", _centerSubStyle, new Color(1f, 0.92f, 0.7f));
        Rect viewport = new Rect(width * 0.08f, 170f * scale, width * 0.84f, height - 430f * scale);
        bool pointerInViewport = viewport.Contains(Event.current.mousePosition);
        int columns = Mathf.Max(1, Mathf.FloorToInt(viewport.width / (240f * scale)));
        float cellWidth = (viewport.width - 24f * scale) / columns;
        float cellHeight = 220f * scale;
        int count = _fishSpawner.FishTypes.Count + 1;
        int rows = Mathf.CeilToInt((float)count / columns);
        _catalogScroll = GUI.BeginScrollView(viewport, _catalogScroll,
            new Rect(0f, 0f, viewport.width - 24f * scale, rows * cellHeight));
        for (int option = 0; option < count; option++)
        {
            FishType fish = option == 0 ? null : _fishSpawner.FishTypes[option - 1];
            FishCatalogState catalogState = _fishSpawner.GetCatalogState(fish);
            bool owned = catalogState == FishCatalogState.Acquired;
            bool revealed = catalogState != FishCatalogState.Undiscovered;
            Rect cell = new Rect(option % columns * cellWidth, option / columns * cellHeight, cellWidth - 12f * scale, cellHeight - 12f * scale);
            bool hovered = pointerInViewport && cell.Contains(Event.current.mousePosition);
            if (hovered && !_catalogCapturePending && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                _catalogSelectedFish = fish;
                Event.current.Use();
            }
            bool selected = fish == _catalogSelectedFish;
            GUI.color = new Color(1f, 1f, 1f, selected ? 0.32f : hovered ? 0.2f : owned ? 0.12f : 0.04f);
            GUI.DrawTexture(cell, Texture2D.whiteTexture);
            GUI.color = original;
            Rect image = new Rect(cell.x + 12f * scale, cell.y + 8f * scale, cell.width - 24f * scale, 132f * scale);
            Texture texture = fish == null ? _ownedFishSelectionPreview.DefaultFishTexture
                : fish.Id == "screen" ? _fishSpawner.ScreenCapture.Image
                : fish.Id == "catalog" ? _fishSpawner.CatalogCapture.Image
                : (owned ? _ownedFishSelectionPreview : _fishSelectionPreview).GetTexture(_fishSpawner.FishPrefabs[option - 1]);
            if (revealed && texture != null) GUI.DrawTexture(image, texture, ScaleMode.ScaleToFit, true);
            else ShadowLabel(image, "?", _centerStyle, new Color(1f, 1f, 1f, 0.5f));
            string name = revealed ? GetCatalogDisplayName(fish) : "?";
            ShadowLabel(new Rect(cell.x, cell.y + 142f * scale, cell.width, 34f * scale), name, _previewStyle, Color.white);
            ShadowLabel(new Rect(cell.x, cell.y + 180f * scale, cell.width, 28f * scale),
                owned ? "획득" : revealed ? "발견 및 출현" : "미발견", _centerSubStyle,
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
            : selectedState == FishCatalogState.Undiscovered ? "?    미발견" : $"{selectedName}    해금 힌트";
        string text = selectedState == FishCatalogState.Undiscovered
            ? string.Empty : entry.Value<string>(selectedOwned ? "description" : "unlockHint");
        ShadowLabel(new Rect(details.x + 24f * scale, details.y + 14f * scale, details.width - 48f * scale, 36f * scale),
            heading, _smallStyle, new Color(1f, 0.92f, 0.7f));
        ShadowLabel(new Rect(details.x + 24f * scale, details.y + 54f * scale, details.width - 48f * scale, 76f * scale),
            text, _catalogDetailStyle, Color.white);
        GUI.enabled = !_catalogCapturePending;
        if (GUI.Button(new Rect(width * 0.5f - 130f * scale, height - 70f * scale, 260f * scale, 48f * scale), "닫기 (TAB)", _buttonStyle))
        {
            _catalogOpen = false;
            LockCursor(true);
        }
        GUI.enabled = true;
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
            case GameOverReason.Sunk: return "Sank";
            case GameOverReason.Stopped: return "Stopped";
            case GameOverReason.HitGround: return "Hit the ground";
            case GameOverReason.OutOfBounds: return "Out of bounds";
            default: return reason.ToString();
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
        _popupStyle = MakeStyle(52, TextAnchor.MiddleCenter, scale);
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
        return style;
    }

    /// <summary>
    /// 그림자를 깔고 라벨을 그린다.
    /// rect, text, style, color를 사용하며, 화면에 라벨을 그린다.
    /// </summary>
    private static void ShadowLabel(Rect rect, string text, GUIStyle style, Color color)
    {
        Color original = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.6f * color.a);
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, style);
        style.normal.textColor = color;
        GUI.Label(rect, text, style);
        style.normal.textColor = original;
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
            CompleteEndingCredit();
            yield return ShrinkPanelAndThrow(screenshot);
        }
        finally
        {
            if (screenshot != null) Destroy(screenshot);
            _screenThrowPending = false;
        }
    }

    /// <summary>
    /// 엔딩 크레딧을 정리하고 크레딧 물고기를 해금 및 선택한 뒤 재시작 상태로 전환한다.
    /// 입력값은 없으며, 크레딧 물고기 해금, 선택 상태, HUD 및 카메라 복구, 게임 재시작을 수행한다.
    /// </summary>
    private void CompleteEndingCredit()
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
            Rigidbody rb = _playerController.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
            }
        }
        _showHud = true;
        Restart();
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

        foreach (FishType fish in _fishSpawner.FishTypes)
        {
            if (fish.Id != "blackhole") continue;
            _fishSpawner.HandlePlacedFishCaught(fish, _playerController.transform.position);
            break;
        }

        _state = State.GameOver;
        _showHud = false;

        if (_playerController != null)
        {
            Rigidbody rb = _playerController.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
        }

        StartCoroutine(PlayEndingCinemaRoutine());
    }

    /// <summary>
    /// 미지의 행성 컷신을 순차적으로 재생하고 외계인 획득 및 엔딩 크레딧을 연결한다.
    /// 시간 경과에 따라 카메라 구도, 돌 낙하, 외계인 반응 및 돌에 의한 흡수를 연출하고 정적 후 크레딧을 연다.
    /// </summary>
    private IEnumerator PlayEndingCinemaRoutine()
    {
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

        // 7. 정적 (1.5초) - 외계인을 먹은 돌만 남은 채 정적 유지
        yield return new WaitForSeconds(1.5f);

        // 8. 엔딩 크레딧 오픈
        _endingCinemaRunning = false;
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
        }
        else
        {
            PlayerPrefs.SetInt(FISH_KEY_PREFIX + MONSTER_FISH_ID, 1);
            PlayerPrefs.Save();
        }
    }
}
