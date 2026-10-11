using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

// 게임패드를 짧게 진동시키고, 시간이 지나면 끈다. 게임패드가 없으면 아무것도 하지 않는다.
public class Rumble : MonoBehaviour
{
    [Header("진동")]
    [Tooltip("세기 1일 때 저주파(묵직한) 모터 세기")]
    [SerializeField]
    private float _maxLowFrequency = 0.8f;
    [Tooltip("세기 1일 때 고주파(찌릿한) 모터 세기")]
    [SerializeField]
    private float _maxHighFrequency = 1f;
    private float _currentStrength;
    private readonly List<(float strength, float endTime)> _pulses = new List<(float, float)>();
    private float _continuousStrength;
    private float _motorStrength;
    private Gamepad _gamepad;
    private bool _hasFocus = true;
    private bool _paused;

    void OnEnable()
    {
        InputSystem.onDeviceChange += HandleDeviceChange;
    }

    void LateUpdate()
    {
        if (!_hasFocus || _paused || Time.timeScale <= 0f)
        {
            StopMotors();
            return;
        }
        ApplyMotors();
    }

    void OnDisable()
    {
        InputSystem.onDeviceChange -= HandleDeviceChange;
        StopMotors();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        _hasFocus = hasFocus;
        if (!hasFocus) StopMotors();
    }

    void OnApplicationPause(bool paused)
    {
        _paused = paused;
        if (paused) StopMotors();
    }

    /// <summary>
    /// 게임패드를 strength 세기로 duration 동안 진동시킨다. 겹친 요청은 각자 만료할 때까지 유지한다.
    /// strength(0~1)와 duration(초)을 사용하며, 모터 세기와 종료 시각을 변경한다.
    /// </summary>
    public void Pulse(float strength, float duration)
    {
        if (!isActiveAndEnabled || !_hasFocus || _paused || Time.timeScale <= 0f) return;

        _pulses.Add((Mathf.Clamp01(strength), Time.unscaledTime + duration));
        ApplyMotors();
    }

    /// <summary>
    /// 슬라이딩용 지속 진동을 strength(0~1)로 설정한다.
    /// 일시 진동과 겹치면 더 강한 값을 사용하며 0이면 지속 진동을 종료한다.
    /// </summary>
    public void SetContinuous(float strength)
    {
        _continuousStrength = Mathf.Clamp01(strength);
    }

    /// <summary>
    /// 중앙 입력 매니저의 활성 패드에 일시·지속 진동 중 큰 세기를 보낸다.
    /// 입력값 없이 장치 교체 시 이전 모터를 끄고 현재 모터 상태를 갱신한다.
    /// </summary>
    private void ApplyMotors()
    {
        _currentStrength = 0f;
        for (int i = _pulses.Count - 1; i >= 0; i--)
        {
            if (Time.unscaledTime >= _pulses[i].endTime) _pulses.RemoveAt(i);
            else _currentStrength = Mathf.Max(_currentStrength, _pulses[i].strength);
        }
        Gamepad target = Managers.Input.ActiveGamepad;
        if (_gamepad != target)
        {
            _gamepad?.ResetHaptics();
            _gamepad = target;
            _motorStrength = -1f;
        }
        float strength = Mathf.Max(_currentStrength, _continuousStrength);
        if (_gamepad == null || !_gamepad.enabled || Mathf.Approximately(strength, _motorStrength)) return;
        _gamepad.SetMotorSpeeds(strength * _maxLowFrequency, strength * _maxHighFrequency);
        _motorStrength = strength;
    }

    /// <summary>
    /// device의 제거·분리·비활성화가 change로 전달되면 해당 패드 진동을 끝낸다.
    /// 현재 진동 대상일 때만 모터와 진동 예약 상태를 초기화한다.
    /// </summary>
    private void HandleDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device == _gamepad && (change == InputDeviceChange.Removed
            || change == InputDeviceChange.Disconnected || change == InputDeviceChange.Disabled)) StopMotors();
    }

    /// <summary>
    /// 진동을 멈춘다.
    /// 입력값 없이 모터를 끄고 모든 일시·지속 진동 요청을 비운다.
    /// </summary>
    public void StopMotors()
    {
        _pulses.Clear();
        _currentStrength = 0f;
        _continuousStrength = 0f;
        _gamepad?.ResetHaptics();
        _gamepad = null;
        _motorStrength = 0f;
    }
}
