using UnityEngine;

using Unity.Cinemachine;

// 던진 동안 LockOnTarget이 있으면 자동으로 화면 가운데에 가장 가까운 대상을 바라보는 시네머신 카메라.
// 플레이어 뒤에서 대상 반대쪽에 자리만 잡고, 조준은 RotationComposer가, 추적 카메라와의 전환은 CinemachineBrain 블렌드가 맡는다.
[RequireComponent(typeof(CinemachineCamera))]
public class LockOnCamera : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private Transform _focalPoint;
    [Tooltip("큰 투척물 안으로 들어가지 않도록 투척물 반경을 받아 쓰는 추적 카메라")]
    [SerializeField] private CameraController _followCamera;
    private CinemachineCamera _camera;
    private PlayerController _player;
    private Transform _view;
    private LockOnTarget _target;

    [Header("배치")]
    [Tooltip("투척물 표면에서 대상 반대쪽으로 떨어지는 수평 거리")]
    [SerializeField, Min(0f)] private float _distance = 8f;
    [Tooltip("투척물 중심 위로 올리는 높이. 투척물 반경의 절반이 더해진다")]
    [SerializeField, Min(0f)] private float _height = 1.5f;
    [SerializeField, Min(0f)] private float _minHeightAboveWater = 2f;
    [Tooltip("자리를 따라가는 부드러움(초). 클수록 튕김과 방향 변화에 덜 흔들린다")]
    [SerializeField, Min(0f)] private float _positionSmoothTime = 0.3f;
    private Vector3 _positionVelocity;

    void Awake()
    {
        _camera = GetComponent<CinemachineCamera>();
        _player = _focalPoint.GetComponent<PlayerController>();
        _view = Camera.main.transform;
    }

    void LateUpdate()
    {
        if (!CanLockOn())
        {
            Release();
            return;
        }
        if (_target == null || !_target.isActiveAndEnabled) _target = FindTarget();
        if (_target == null)
        {
            Release();
            return;
        }

        PlaceBehindFocus();
        // LookAt으로 넣어야 별도 조준 대상 사용이 켜져 RotationComposer가 이 대상을 바라본다.
        _camera.LookAt = _target.transform;
        _camera.enabled = true;
    }

    /// <summary>
    /// 플레이어 상태로 지금 락온할 수 있는지 판단한다.
    /// 투척 여부, 게임 오버 여부와 시간 배율을 사용하며 가능 여부를 반환한다.
    /// </summary>
    private bool CanLockOn()
    {
        // 시간이 멈춘 연출은 추적 카메라를 직접 움직이므로 락온하지 않는다.
        return _player.IsThrown && !_player.IsGameOver && Time.timeScale > 0f;
    }

    /// <summary>
    /// 켜져 있는 락온 대상 중 현재 화면 가운데에 가장 가까운 대상을 고른다.
    /// LockOnTarget.Targets와 메인 카메라 방향을 사용하며 고른 대상 또는 없으면 null을 반환한다.
    /// </summary>
    private LockOnTarget FindTarget()
    {
        LockOnTarget best = null;
        float bestDot = float.MinValue;
        foreach (LockOnTarget target in LockOnTarget.Targets)
        {
            float dot = Vector3.Dot(_view.forward, (target.transform.position - _view.position).normalized);
            if (dot <= bestDot) continue;
            bestDot = dot;
            best = target;
        }
        return best;
    }

    /// <summary>
    /// 투척물을 기준으로 대상 반대쪽 뒤, 위에 카메라를 둔다. 큰 투척물은 반경만큼 더 물러난다.
    /// _focalPoint, _target 위치, 투척물 반경과 배치 설정, 수면 높이, _positionSmoothTime을 사용하며 transform 위치와 _positionVelocity를 변경한다.
    /// </summary>
    private void PlaceBehindFocus()
    {
        Vector3 focus = _focalPoint.position;
        Vector3 away = focus - _target.transform.position;
        away.y = 0f;
        away.Normalize();
        float radius = _followCamera.ProjectileRadius;
        Vector3 position = focus + away * (_distance + radius) + Vector3.up * (_height + radius * 0.5f);
        // 카메라가 물속으로 들어가지 않게 한다.
        position.y = Mathf.Max(position.y, _player.WaterContactPoint.y + _minHeightAboveWater);

        // 켜지는 순간에는 바로 자리를 잡고, 켜진 뒤에는 튕김과 방향 변화를 부드럽게 따라간다.
        if (!_camera.enabled)
        {
            _positionVelocity = Vector3.zero;
            transform.position = position;
            return;
        }
        transform.position = Vector3.SmoothDamp(transform.position, position, ref _positionVelocity,
            _positionSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
    }

    /// <summary>
    /// 락온을 풀어 추적 카메라로 블렌드되어 돌아가게 한다.
    /// 입력값 없이 시네머신 카메라 활성 상태와 _target을 변경한다.
    /// </summary>
    private void Release()
    {
        _camera.enabled = false;
        _target = null;
    }
}
