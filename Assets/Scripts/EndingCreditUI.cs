using System;
using System.Collections;

using UnityEngine;
using UnityEngine.UI;

using TMPro;

public class EndingCreditUI : MonoBehaviour
{
    private const float DISPLAYDURATION = 16f;
    private const float PULSE_SPEED = 4f;
    private const float MIN_SCALE = 0.92f;
    private const float MAX_SCALE = 1.12f;
    private const string SKIP_PROMPT_TEXT = "<b>누르는 <color=#FFD700>[ SPACE ]</color> <color=#FFFFFF>제작 명단을 건너뛰기 위하여 ▼▼</color></b>";

    [Header("Settings")]
    private float _timer;
    private bool _skipRequested;

    [Header("Skip UI")]
    [SerializeField] private TMP_FontAsset _font;
    [SerializeField] private TMP_Text _skipMessage;
    private float _skipPromptDelay = 9f;
    private RectTransform _promptRect;
    private Coroutine _showPromptCoroutine;
    private bool _canSkip;
    private float _pulseTimer;

    public event Action OnEndingCompleted;
    public event Action OnEndingSkipped;

    public bool CanSkip => _canSkip;

    void Awake()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.sortingOrder = 550;
        }
        CreateSkipPrompt();
    }

    void OnEnable()
    {
        _timer = 0f;
        _skipRequested = false;
        _canSkip = false;
        _pulseTimer = 0f;

        if (_promptRect != null)
        {
            _promptRect.localScale = Vector3.one;
            _promptRect.gameObject.SetActive(false);
        }

        if (_showPromptCoroutine != null)
        {
            StopCoroutine(_showPromptCoroutine);
        }
        _showPromptCoroutine = StartCoroutine(ShowSkipPromptRoutine());
    }

    void OnDisable()
    {
        if (_showPromptCoroutine != null)
        {
            StopCoroutine(_showPromptCoroutine);
            _showPromptCoroutine = null;
        }
    }

    void Update()
    {
        if (_skipRequested) return;
        HandleSkipInput();
        UpdateTimer();
        if (_canSkip)
        {
            UpdatePulseEffect();
        }
    }

    /// <summary>
    /// 엔딩 크레딧 지속 시간을 모두 채워 정상 종료하고 완료 이벤트를 호출한다.
    /// 입력값은 없으며, gameObject를 비활성화하고 OnEndingCompleted 이벤트를 호출한다.
    /// </summary>
    public void FinishCredits()
    {
        gameObject.SetActive(false);
        OnEndingCompleted?.Invoke();
    }

    /// <summary>
    /// 스킵 입력 또는 버튼 클릭 시 화면 캡처를 위해 크레딧을 유지하고 스킵 이벤트를 한 번 호출한다.
    /// 입력값 없이 중복 스킵과 자동 완료를 막으며, 화면 종료는 전환 흐름에서 처리한다.
    /// </summary>
    public void SkipCredits()
    {
        if (!_canSkip || _skipRequested) return;
        _skipRequested = true;
        OnEndingSkipped?.Invoke();
    }

    /// <summary>
    /// 외부 버튼 클릭 등에서 크레딧을 즉시 스킵할 때 호출한다.
    /// 입력값은 없으며, SkipCredits를 호출한다.
    /// </summary>
    public void SkipButton()
    {
        SkipCredits();
    }

    /// <summary>
    /// 스페이스바 또는 패드 버튼 입력을 받아 크레딧을 즉시 스킵한다.
    /// 중앙 Jump 액션을 사용하며, 스킵 조건 만족 시 SkipCredits를 호출한다.
    /// </summary>
    private void HandleSkipInput()
    {
        if (!_canSkip) return;

        if (Managers.Input.JumpPressed)
        {
            SkipCredits();
        }
    }

    /// <summary>
    /// 설정된 대기 시간 동안 대기한 후 스킵 안내 UI를 활성화하고 스킵 키 입력을 허용한다.
    /// _skipPromptDelay를 사용하며, _canSkip 상태와 _promptRect의 활성화 상태를 변경한다.
    /// </summary>
    private IEnumerator ShowSkipPromptRoutine()
    {
        // 크레딧이 올라오기 시작한 후 설정된 초 동안 대기한다.
        yield return new WaitForSecondsRealtime(_skipPromptDelay);

        _canSkip = true;
        _showPromptCoroutine = null;

        if (_promptRect != null)
        {
            _promptRect.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// 지속 시간을 측정하여 설정된 DISPLAYDURATION이 지나면 크레딧을 정상 완료한다.
    /// DISPLAYDURATION과 Time.unscaledDeltaTime을 사용하며, 시간 도달 시 FinishCredits를 호출한다.
    /// </summary>
    private void UpdateTimer()
    {
        _timer += Time.unscaledDeltaTime;
        if (_timer >= DISPLAYDURATION)
        {
            FinishCredits();
        }
    }

    /// <summary>
    /// 외곽 테두리와 반투명 배경을 포함한 버튼 형태의 스킵 안내 UI를 동적으로 생성한다.
    /// TextMeshProUGUI, Image, RectTransform, _font를 사용하며, _skipMessage와 _promptRect를 생성 및 초기화한다.
    /// </summary>
    private void CreateSkipPrompt()
    {
        // 버튼 전체를 감싸는 루트 컨테이너를 생성하고 하단 중앙에 배치한다.
        GameObject rootObj = new GameObject("SkipPromptButton", typeof(RectTransform));
        rootObj.transform.SetParent(transform, false);

        RectTransform rootRect = rootObj.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 0f);
        rootRect.anchorMax = new Vector2(0.5f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = new Vector2(0f, 45f);
        rootRect.sizeDelta = new Vector2(600f, 50f);

        _promptRect = rootRect;

        // 외곽선 테두리용 이미지를 생성하여 루트 전체 영역에 채운다.
        GameObject borderObj = new GameObject("Border", typeof(RectTransform), typeof(Image));
        borderObj.transform.SetParent(rootRect, false);

        RectTransform borderRect = borderObj.GetComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.sizeDelta = Vector2.zero;

        Image borderImage = borderObj.GetComponent<Image>();
        borderImage.color = new Color(1f, 0.84f, 0f, 0.85f);
        borderImage.raycastTarget = false;

        // 테두리 안쪽을 덮는 어두운 반투명 배경 이미지를 생성한다.
        GameObject bgObj = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bgObj.transform.SetParent(borderRect, false);

        RectTransform bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = new Vector2(2f, 2f);
        bgRect.offsetMax = new Vector2(-2f, -2f);

        Image bgImage = bgObj.GetComponent<Image>();
        bgImage.color = new Color(0.08f, 0.08f, 0.08f, 0.85f);
        bgImage.raycastTarget = false;

        // 버튼 중앙에 스킵 텍스트를 배치한다.
        GameObject textObj = new GameObject("SkipText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(bgRect, false);

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;

        _skipMessage = textObj.GetComponent<TextMeshProUGUI>();
        _skipMessage.font = _font;
        _skipMessage.text = SKIP_PROMPT_TEXT;
        _skipMessage.fontSize = 20f;
        _skipMessage.alignment = TextAlignmentOptions.Center;
        _skipMessage.raycastTarget = false;

        // 초기에는 스킵 대기 시간 동안 보이지 않도록 비활성화한다.
        rootObj.SetActive(false);
    }

    /// <summary>
    /// 스킵 버튼 전체의 크기를 시간에 따라 주기적으로 변경하여 시선을 끄는 펄스 연출을 재생한다.
    /// Time.unscaledDeltaTime과 PULSE_SPEED를 사용하며, _promptRect의 localScale을 변경한다.
    /// </summary>
    private void UpdatePulseEffect()
    {
        // 시간에 따라 크기가 부드럽게 커졌다 작아지는 사인 곡선을 계산한다.
        _pulseTimer += Time.unscaledDeltaTime * PULSE_SPEED;
        float wave = (Mathf.Sin(_pulseTimer) + 1f) * 0.5f;
        float scale = Mathf.Lerp(MIN_SCALE, MAX_SCALE, wave);

        _promptRect.localScale = new Vector3(scale, scale, 1f);
    }
}
