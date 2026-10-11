using System;

using UnityEngine;
using UnityEngine.InputSystem;

public class EndingCreditUI : MonoBehaviour
{
    private const float DISPLAYDURATION = 5f;

    [Header("Settings")]
    private float _timer;
    private bool _skipRequested;

    public event Action OnEndingCompleted;
    public event Action OnEndingSkipped;

    void OnEnable()
    {
        _timer = 0f;
        _skipRequested = false;
    }

    void Update()
    {
        if (_skipRequested) return;
        HandleSkipInput();
        if (!_skipRequested) UpdateTimer();
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
        if (_skipRequested) return;
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
    /// Keyboard와 Gamepad 입력을 사용하며, 스킵 조건 만족 시 SkipCredits를 호출한다.
    /// </summary>
    private void HandleSkipInput()
    {
        Keyboard keyboard = Keyboard.current;
        Gamepad gamepad = Gamepad.current;
        bool skipPressed = (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            || (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame);

        if (skipPressed)
        {
            SkipCredits();
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
}
