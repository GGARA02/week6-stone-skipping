using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

public class InputManager
{
    [Header("입력 액션")]
    private InputSystem_Actions _inputActions;

    [Header("입력 장치")]
    private Gamepad _activeGamepad;
    public InputSystem_Actions.PlayerActions PlayerMap => _inputActions.Player;
    public InputSystem_Actions.UIActions UIMap => _inputActions.UI;
    public bool GamePadConnected => Gamepad.all.Count > 0;
    public bool IsUsingGamepad { get; private set; }
    public float LastInputTime { get; private set; }
    public Gamepad ActiveGamepad => _activeGamepad != null && _activeGamepad.added && _activeGamepad.enabled
        ? _activeGamepad : Gamepad.current;

    public Vector2 MoveInput => PlayerMap.Move.ReadValue<Vector2>();
    public Vector2 LookInput => PlayerMap.Look.ReadValue<Vector2>();
    public bool AttackPressed => PlayerMap.Attack.WasPressedThisFrame();
    public bool InteractPressed => PlayerMap.Interact.WasPerformedThisFrame();
    public bool CrouchPressed => PlayerMap.Crouch.WasPressedThisFrame();
    public bool JumpPressed => PlayerMap.Jump.WasPressedThisFrame();
    public bool JumpHeld => PlayerMap.Jump.IsPressed();
    public bool PreviousPressed => PlayerMap.Previous.WasPressedThisFrame();
    public bool NextPressed => PlayerMap.Next.WasPressedThisFrame();
    public bool SprintHeld => PlayerMap.Sprint.IsPressed();
    public bool RestartPressed => PlayerMap.Restart.WasPressedThisFrame();
    public bool CatalogPressed => PlayerMap.Catalog.WasPressedThisFrame();
    public Vector2 NavigateInput => UIMap.Navigate.ReadValue<Vector2>();
    public bool SubmitPressed => UIMap.Submit.WasPressedThisFrame();
    public bool CancelPressed => UIMap.Cancel.WasPressedThisFrame();
    public Vector2 PointInput => UIMap.Point.ReadValue<Vector2>();
    public bool ClickPressed => UIMap.Click.WasPressedThisFrame();
    public bool RightClickPressed => UIMap.RightClick.WasPressedThisFrame();
    public bool MiddleClickPressed => UIMap.MiddleClick.WasPressedThisFrame();
    public Vector2 ScrollInput => UIMap.ScrollWheel.ReadValue<Vector2>();
    public Vector3 TrackedDevicePosition => UIMap.TrackedDevicePosition.ReadValue<Vector3>();
    public Quaternion TrackedDeviceOrientation => UIMap.TrackedDeviceOrientation.ReadValue<Quaternion>();

    /// <summary>
    /// 입력값 없이 모든 Player·UI 액션을 생성하고 활성화한다.
    /// 장치 추적을 구독하고 씬 UI를 같은 액션 자산에 연결한다.
    /// </summary>
    public void Init()
    {
        _inputActions = new InputSystem_Actions();
        PlayerMap.Get().actionTriggered += HandleAction;
        UIMap.Get().actionTriggered += HandleAction;
        InputSystem.onDeviceChange += HandleDeviceChange;
        _inputActions.Enable();
        BindUI();
    }

    /// <summary>
    /// 입력값 없이 장치 추적과 모든 액션을 해제한다.
    /// 활성 게임패드의 진동을 정지하고 생성한 액션 자산을 폐기한다.
    /// </summary>
    public void Clear()
    {
        ActiveGamepad?.ResetHaptics();
        InputSystem.onDeviceChange -= HandleDeviceChange;
        PlayerMap.Get().actionTriggered -= HandleAction;
        UIMap.Get().actionTriggered -= HandleAction;
        _inputActions.Disable();
        _inputActions.Dispose();
        _activeGamepad = null;
        IsUsingGamepad = false;
    }

    /// <summary>
    /// 입력값 없이 씬의 UI 입력 모듈을 중앙 자산에 연결한다.
    /// 비활성 UI까지 같은 액션 인스턴스를 사용하도록 변경한다.
    /// </summary>
    public void BindUI()
    {
        foreach (InputSystemUIInputModule module in Object.FindObjectsByType<InputSystemUIInputModule>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            module.actionsAsset = _inputActions.asset;
        }
    }

    /// <summary>
    /// 커브 액션과 트리거 입력을 합쳐 -1~1의 조향 값을 반환한다.
    /// deadZone은 게임패드 스틱에만 적용하며 입력 상태는 변경하지 않는다.
    /// </summary>
    public float ReadSteer(float deadZone)
    {
        return Mathf.Clamp(ReadStick(PlayerMap.Steer, deadZone).x + PlayerMap.CurveTriggers.ReadValue<Vector2>().x, -1f, 1f);
    }

    /// <summary>
    /// 방향키 또는 오른쪽 스틱의 자세 입력을 반환한다.
    /// deadZone은 게임패드에만 적용하며 입력 상태는 변경하지 않는다.
    /// </summary>
    public Vector2 ReadAttitude(float deadZone)
    {
        return Vector2.ClampMagnitude(ReadStick(PlayerMap.Attitude, deadZone), 1f);
    }

    /// <summary>
    /// key에 해당하는 실제 키보드의 누름 상태를 반환한다.
    /// 키캡 시각화용이며 키보드가 없거나 None이면 false를 반환한다.
    /// </summary>
    public bool IsKeyHeld(Key key)
    {
        return key != Key.None && Keyboard.current != null && Keyboard.current[key].isPressed;
    }

    /// <summary>
    /// text의 액션 이름 자리표시자를 마지막으로 입력한 장치의 바인딩 이름으로 치환한다.
    /// {Jump} 같은 자리표시자를 사용하며 키보드 또는 게임패드 안내 문자열을 반환한다.
    /// </summary>
    public string ReplaceBindingName(string text)
    {
        foreach (InputAction action in _inputActions)
        {
            string placeholder = $"{{{action.name}}}";
            if (text.Contains(placeholder)) text = text.Replace(placeholder, GetBindingName(action));
        }
        return text;
    }

    /// <summary>
    /// action에서 마지막으로 입력한 장치(IsUsingGamepad)에 해당하는 첫 바인딩의 표시 이름을 반환한다.
    /// 합성 입력은 구성 바인딩의 장치를 확인하며 액션 상태는 변경하지 않는다.
    /// </summary>
    private string GetBindingName(InputAction action)
    {
        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding binding = action.bindings[i];
            if (binding.isPartOfComposite) continue;
            bool isGamepad = binding.effectivePath.StartsWith("<Gamepad>/");
            if (binding.isComposite)
            {
                for (int j = i + 1; j < action.bindings.Count && action.bindings[j].isPartOfComposite; j++)
                {
                    if (action.bindings[j].effectivePath.StartsWith("<Gamepad>/"))
                    {
                        isGamepad = true;
                        break;
                    }
                }
            }
            // 게임패드가 꽂혀 있어도 키보드로 조작하면 키보드 이름을 보여 주도록 연결 여부 대신 마지막 입력 장치를 쓴다.
            if (IsUsingGamepad != isGamepad) continue;
            if (!binding.isComposite) return action.GetBindingDisplayString(i);

            string displayName = string.Empty;
            for (int j = i + 1; j < action.bindings.Count && action.bindings[j].isPartOfComposite; j++)
            {
                if (displayName.Length > 0) displayName += "/";
                displayName += action.GetBindingDisplayString(j);
            }
            return displayName;
        }
        return action.GetBindingDisplayString();
    }

    /// <summary>
    /// action의 벡터를 읽고 게임패드 스틱에 추가 deadZone을 적용한다.
    /// 보정된 벡터를 반환하며 키보드 입력은 그대로 유지한다.
    /// </summary>
    private static Vector2 ReadStick(InputAction action, float deadZone)
    {
        Vector2 value = action.ReadValue<Vector2>();
        if (!(action.activeControl?.device is Gamepad)) return value;
        float magnitude = value.magnitude;
        return magnitude <= deadZone ? Vector2.zero : value / magnitude * ((magnitude - deadZone) / (1f - deadZone));
    }

    /// <summary>
    /// context로 마지막 입력 시각과 실제 수행 장치의 입력 종류를 기록한다.
    /// LastInputTime을 변경하며, 게임패드 입력이면 다음 진동을 보낼 장치도 갱신한다.
    /// </summary>
    private void HandleAction(InputAction.CallbackContext context)
    {
        LastInputTime = Time.unscaledTime;
        if (!context.performed) return;
        IsUsingGamepad = context.control.device is Gamepad;
        if (context.control.device is Gamepad gamepad) _activeGamepad = gamepad;
    }

    /// <summary>
    /// device의 제거·분리·비활성화를 change로 감지한다.
    /// 선택된 게임패드가 사라지면 진동과 활성 장치 상태를 초기화한다.
    /// </summary>
    private void HandleDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device != _activeGamepad || (change != InputDeviceChange.Removed
            && change != InputDeviceChange.Disconnected && change != InputDeviceChange.Disabled)) return;
        _activeGamepad.ResetHaptics();
        _activeGamepad = null;
        IsUsingGamepad = false;
    }
}
