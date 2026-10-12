using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

// 게임패드를 짧게 진동시키고, 시간이 지나면 끈다. 게임패드가 없으면 아무것도 하지 않는다.
// 세기는 Vector2로 받으며 x는 저주파(묵직한) 모터, y는 고주파(찌릿한) 모터 세기다.
public class Rumble : MonoBehaviour
{
    [Header("진동")]
    private Vector2 _currentStrength;
    private readonly List<(Vector2 strength, float endTime)> _pulses = new List<(Vector2, float)>();
    private Vector2 _continuousStrength;
    private Vector2 _motorStrength;
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
    /// strength(x 저주파, y 고주파, 0~1)와 duration(초)을 사용하며, 모터 세기와 종료 시각을 변경한다.
    /// </summary>
    public void Pulse(Vector2 strength, float duration)
    {
        if (!isActiveAndEnabled || !_hasFocus || _paused || Time.timeScale <= 0f) return;

        _pulses.Add((new Vector2(Mathf.Clamp01(strength.x), Mathf.Clamp01(strength.y)), Time.unscaledTime + duration));
        ApplyMotors();
    }

    /// <summary>
    /// 슬라이딩용 지속 진동을 strength(x 저주파, y 고주파, 0~1)로 설정한다.
    /// 일시 진동과 겹치면 모터별로 더 강한 값을 사용하며 0이면 지속 진동을 종료한다.
    /// </summary>
    public void SetContinuous(Vector2 strength)
    {
        _continuousStrength = new Vector2(Mathf.Clamp01(strength.x), Mathf.Clamp01(strength.y));
    }

    /// <summary>
    /// 중앙 입력 매니저의 활성 패드에 일시·지속 진동 중 모터별로 큰 세기를 보낸다.
    /// 입력값 없이 장치 교체 시 이전 모터를 끄고 현재 모터 상태를 갱신한다.
    /// </summary>
    private void ApplyMotors()
    {
        _currentStrength = Vector2.zero;
        for (int i = _pulses.Count - 1; i >= 0; i--)
        {
            if (Time.unscaledTime >= _pulses[i].endTime) _pulses.RemoveAt(i);
            else _currentStrength = Vector2.Max(_currentStrength, _pulses[i].strength);
        }
        Gamepad target = Managers.Input.ActiveGamepad;
        if (_gamepad != target)
        {
            _gamepad?.ResetHaptics();
            _gamepad = target;
            _motorStrength = -Vector2.one;
        }
        Vector2 strength = Vector2.Max(_currentStrength, _continuousStrength);
        if (_gamepad == null || !_gamepad.enabled || strength == _motorStrength) return;
        _gamepad.SetMotorSpeeds(strength.x, strength.y);
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
        _currentStrength = Vector2.zero;
        _continuousStrength = Vector2.zero;
        _gamepad?.ResetHaptics();
        _gamepad = null;
        _motorStrength = Vector2.zero;
    }
}
