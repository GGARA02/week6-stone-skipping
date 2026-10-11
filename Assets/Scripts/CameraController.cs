using UnityEngine;

using Unity.Cinemachine;

// 조작 없이 알아서 따라가는 카메라. 진행 방향 뒤에서 보고, 빠를수록 멀리서 앞을 더 보여주고,
// 물고기가 높이 뜨면 각도를 높여 앞쪽 수면이 보이게 한다. 튕김 FOV 펀치와 속도에 따른 FOV도 처리한다.
// 시네머신 카메라의 Transform과 Lens를 직접 움직이고, 메인 카메라의 CinemachineBrain이 이를 화면에 반영한다.
// 판정, 벽 충돌, 포획 흔들림은 임펄스로 발동해 추적 카메라와 락온 카메라의 ImpulseListener가 함께 받는다.
[RequireComponent(typeof(CinemachineCamera))]
public class CameraController : MonoBehaviour
{
    [Header("참조")]
    [SerializeField]
    private Transform _focalPoint;
    [SerializeField]
    private GameObject _water;
    [SerializeField]
    private FishSpawner _fishSpawner;
    private CinemachineCamera _cinemachineCamera;
    private Rigidbody _targetBody;
    private PlayerController _targetPlayer;
    private float _waterY;

    [Header("자동 추적")]
    [Tooltip("느릴 때 / 가장 빠를 때 물고기와의 거리")]
    [SerializeField]
    private Vector2 _distanceRange = new Vector2(16f, 26f);
    [Tooltip("이 수평 속도(m/s)에서 거리, 앞보기, FOV가 최대가 된다")]
    [SerializeField]
    private float _fullSpeed = 60f;
    [Tooltip("가장 빠를 때 물고기보다 얼마나 앞을 바라볼지(m)")]
    [SerializeField]
    private float _lookAhead = 14f;
    [SerializeField]
    private float _basePitch = 14f;
    [Tooltip("물고기가 수면 위로 1m 뜰 때마다 높아지는 내려다보는 각도(도)")]
    [SerializeField]
    private float _pitchPerHeight = 1.2f;
    [SerializeField]
    private float _minPitch = 8f;
    [SerializeField]
    private float _maxPitch = 40f;
    [Tooltip("진행 방향을 따라 도는 빠르기")]
    [SerializeField]
    private float _yawFollowSpeed = 3f;
    [Tooltip("각도와 거리가 바뀌는 빠르기")]
    [SerializeField]
    private float _framingSpeed = 2f;
    [Tooltip("물고기를 따라가는 부드러움 (작을수록 딱 붙음)")]
    [SerializeField]
    private float _followSmoothTime = 0.12f;
    [SerializeField]
    private float _minHeightAboveWater = 2f;
    private float _yaw;
    private float _pitch;
    private float _distance;
    private Vector3 _focus;
    private Vector3 _focusVelocity;

    [Header("큰 투척물 거리 보정")]
    [Tooltip("플레이어 기준 실제 외형 지름이 이 값 이하이면 기존 카메라 거리를 유지한다")]
    [Min(0.1f)]
    [SerializeField]
    private float _largeProjectileThreshold = 10f;
    [Tooltip("크기의 제곱근에 따른 거리 보정 강도. 크기에 비례해서 멀어지지 않아 큰 물체가 더 크게 보인다")]
    [Range(0f, 1f)]
    [SerializeField]
    private float _largeProjectileDistanceStrength = 0.75f;
    [Tooltip("큰 투척물 내부로 카메라가 들어가지 않도록 확보하는 여유 거리")]
    [Min(0f)]
    [SerializeField]
    private float _largeProjectileClearance = 2f;
    private FishMeshGenerator _projectileShape;
    private float _projectileRadius;
    public float ProjectileRadius => _projectileRadius;

    [Header("속도감")]
    [SerializeField]
    private float _baseFov = 60f;
    [SerializeField]
    private float _maxExtraFov = 12f;
    private float _smoothedFov;

    [Header("튕김 FOV 펀치")]
    [SerializeField]
    private float _shakeDuration = 0.25f;
    [Tooltip("튕길 때 순간적으로 넓어지는 FOV")]
    [SerializeField]
    private float _punchFov = 6f;
    private float _punchTime = -10f;
    private float _punchStrength;

    [Header("임펄스")]
    [Tooltip("SPACE 판정과 물고기 포획 때 발동하는 짧은 임펄스")]
    [SerializeField]
    private CinemachineImpulseSource _judgeImpulse;
    [Tooltip("벽에 부딪힐 때 발동하는 임펄스")]
    [SerializeField]
    private CinemachineImpulseSource _wallImpulse;
    [SerializeField]
    private float _perfectImpulseForce = 1f;
    [SerializeField]
    private float _goodImpulseForce = 0.5f;
    [SerializeField]
    private float _catchImpulseForce = 0.8f;
    [Tooltip("이 충돌 속도(m/s) 이하에서는 벽 임펄스를 내지 않는다")]
    [SerializeField]
    private float _wallImpulseMinSpeed = 2f;
    [Tooltip("이 충돌 속도(m/s)에서 벽 임펄스가 가장 세다")]
    [SerializeField]
    private float _wallImpulseFullSpeed = 30f;
    [SerializeField]
    private float _wallImpulseMaxForce = 1f;

    [Header("33Fisher QTE 카메라")]
    [SerializeField, Min(0f)] private float _qteShoulderSide = 1.5f;
    [SerializeField, Min(0f)] private float _qteShoulderHeight = 2f;
    [SerializeField, Min(0.1f)] private float _qteShoulderDistance = 3f;
    [SerializeField, Min(0f)] private float _qteConfrontationGap = 3f;
    [SerializeField, Range(30f, 90f)] private float _qteFov = 55f;
    [SerializeField, Min(0.1f)] private float _qteReturnDuration = 0.6f;
    private Transform _qteFisher;
    private Vector3 _qteFisherOriginalPosition;
    private Vector3 _qteStartPosition;
    private Quaternion _qteStartRotation;
    private float _qteStartFov;
    private Vector3 _qteTargetPosition;
    private Quaternion _qteTargetRotation;
    private Vector3 _qteReturnPosition;
    private Quaternion _qteReturnRotation;
    private float _qteReturnFov;
    private float _qteMoveDuration;
    private float _qteElapsed;
    private bool _qteViewActive;
    private bool _qteReturning;

    void LateUpdate()
    {
        if (_qteViewActive)
        {
            UpdateFisherQteCamera(Time.unscaledDeltaTime);
            return;
        }
        if (Time.timeScale == 0f) return;
        float dt = Time.unscaledDeltaTime;
        if (!_targetPlayer.IsThrown)
        {
            UpdateSelectionCamera(dt);
            ApplyFisherQteReturn(dt);
            return;
        }

        Vector3 velocity = _targetBody.linearVelocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
        float speed01 = Mathf.Clamp01(horizontal.magnitude / Mathf.Max(_fullSpeed, 0.01f));
        float follow = 1f - Mathf.Exp(-_yawFollowSpeed * dt);
        float framing = 1f - Mathf.Exp(-_framingSpeed * dt);

        // 움직일 때만 진행 방향 뒤로 돈다. 멈춰 있으면 지금 방향을 유지한다.
        if (horizontal.sqrMagnitude > 1f)
        {
            float targetYaw = Mathf.Atan2(horizontal.x, horizontal.z) * Mathf.Rad2Deg;
            _yaw = Mathf.LerpAngle(_yaw, targetYaw, follow);
        }

        float height = _focalPoint.position.y - _waterY;
        float targetPitch = Mathf.Clamp(_basePitch + Mathf.Max(0f, height) * _pitchPerHeight, _minPitch, _maxPitch);
        _pitch = Mathf.Lerp(_pitch, targetPitch, framing);
        _distance = Mathf.Lerp(_distance, TargetDistance(speed01), framing);

        Vector3 ahead = horizontal.sqrMagnitude > 1f ? horizontal.normalized * (_lookAhead * speed01) : Vector3.zero;
        _focus = Vector3.SmoothDamp(_focus, _focalPoint.position + ahead, ref _focusVelocity, _followSmoothTime, Mathf.Infinity, dt);
        UpdateTransform();

        float punch = PunchAmount();
        _smoothedFov = Mathf.Lerp(_smoothedFov, _baseFov + _maxExtraFov * speed01, 1f - Mathf.Exp(-4f * dt));
        _cinemachineCamera.Lens.FieldOfView = _smoothedFov + _punchFov * punch;
        ApplyFisherQteReturn(dt);
    }

    /// <summary>
    /// fisher의 외형과 플레이어 크기, duration을 사용해 대치 위치와 어깨 시점을 준비한다.
    /// 원래 카메라와 Fisher 위치를 저장하고 정지 중에도 진행할 카메라 전환을 시작한다.
    /// </summary>
    public void BeginFisherQte(Transform fisher, float duration)
    {
        _qteFisher = fisher;
        _qteFisherOriginalPosition = fisher.position;
        _qteStartPosition = transform.position;
        _qteStartRotation = transform.rotation;
        _qteStartFov = _cinemachineCamera.Lens.FieldOfView;
        Vector3 direction = _targetBody.linearVelocity;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f)
            direction = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
        direction.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, direction);
        Renderer[] renderers = fisher.GetComponentsInChildren<Renderer>();
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        Vector3 playerCenter = _focalPoint.position;
        Vector3 fisherCenter = playerCenter + direction * (_projectileRadius + bounds.extents.magnitude + _qteConfrontationGap);
        fisher.position += fisherCenter - bounds.center;
        _qteTargetPosition = playerCenter - direction * (_projectileRadius + _qteShoulderDistance)
            + side * (_projectileRadius * 0.65f + _qteShoulderSide)
            + Vector3.up * (_projectileRadius * 0.6f + _qteShoulderHeight);
        _qteTargetPosition.y = Mathf.Max(_qteTargetPosition.y, _waterY + _minHeightAboveWater);
        _qteTargetRotation = Quaternion.LookRotation(fisherCenter - _qteTargetPosition, Vector3.up);
        _qteMoveDuration = Mathf.Max(0.1f, duration);
        _qteElapsed = 0f;
        _qteViewActive = true;
        _qteReturning = false;
    }

    /// <summary>
    /// smoothReturn으로 일반 추적 뷰의 보간 복귀 또는 원래 카메라의 즉시 복원을 선택한다.
    /// 대치 연출 위치를 원래 Fisher 위치로 되돌리고 카메라 전환 상태를 변경한다.
    /// </summary>
    public void EndFisherQte(bool smoothReturn)
    {
        if (!_qteViewActive && !_qteReturning) return;
        if (_qteViewActive) _qteFisher.position = _qteFisherOriginalPosition;
        _qteViewActive = false;
        _qteReturning = smoothReturn;
        _qteElapsed = 0f;
        if (smoothReturn)
        {
            _qteReturnPosition = transform.position;
            _qteReturnRotation = transform.rotation;
            _qteReturnFov = _cinemachineCamera.Lens.FieldOfView;
        }
        else
        {
            transform.SetPositionAndRotation(_qteStartPosition, _qteStartRotation);
            _cinemachineCamera.Lens.FieldOfView = _qteStartFov;
        }
    }

    /// <summary>
    /// 실제 프레임 시간 dt와 저장한 시작 및 목표 구도로 어깨 뷰를 보간한다.
    /// 게임 시간 정지 중에도 위치, 회전과 FOV를 갱신하고 이동 완료 후 시점을 유지한다.
    /// </summary>
    private void UpdateFisherQteCamera(float dt)
    {
        _qteElapsed += dt;
        float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_qteElapsed / _qteMoveDuration));
        transform.SetPositionAndRotation(Vector3.Lerp(_qteStartPosition, _qteTargetPosition, blend),
            Quaternion.Slerp(_qteStartRotation, _qteTargetRotation, blend));
        _cinemachineCamera.Lens.FieldOfView = Mathf.Lerp(_qteStartFov, _qteFov, blend);
    }

    /// <summary>
    /// dt와 현재 일반 추적 결과를 사용해 QTE 종료 위치에서 카메라를 부드럽게 복귀시킨다.
    /// 저장한 시작 구도와 새 추적 구도를 보간하고 완료 시 복귀 상태를 해제한다.
    /// </summary>
    private void ApplyFisherQteReturn(float dt)
    {
        if (!_qteReturning) return;
        _qteElapsed += dt;
        float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_qteElapsed / _qteReturnDuration));
        transform.SetPositionAndRotation(Vector3.Lerp(_qteReturnPosition, transform.position, blend),
            Quaternion.Slerp(_qteReturnRotation, transform.rotation, blend));
        _cinemachineCamera.Lens.FieldOfView = Mathf.Lerp(_qteReturnFov, _cinemachineCamera.Lens.FieldOfView, blend);
        if (blend >= 1f) _qteReturning = false;
    }

    /// <summary>
    /// 대상과 물 높이를 준비하고 카메라를 물고기 뒤로 맞춘다.
    /// projectileShape를 사용하며, 시네머신 카메라 참조와 _waterY, 카메라 위치, 임펄스 이벤트 구독을 변경한다.
    /// </summary>
    public void Initialize(FishMeshGenerator projectileShape)
    {
        _cinemachineCamera = GetComponent<CinemachineCamera>();
        _targetBody = _focalPoint.GetComponent<Rigidbody>();
        _targetPlayer = _focalPoint.GetComponent<PlayerController>();
        _targetPlayer.OnJudge += HandleJudge;
        _targetPlayer.OnWallHit += HandleWallHit;
        _fishSpawner.OnFishCaught += HandleFishCaught;
        // 시간이 멈추는 연출 직전에 시작한 흔들림이 그 자리에 굳지 않고 실제 시간으로 사라지게 한다.
        CinemachineImpulseManager.Instance.IgnoreTimeScale = true;
        _waterY = _water.GetComponent<Collider>().bounds.max.y;
        _projectileShape = projectileShape;
        _projectileShape.OnGenerated += RefreshProjectileSize;
        RefreshProjectileSize();
        SnapBehindTarget();
    }

    void OnDestroy()
    {
        _projectileShape.OnGenerated -= RefreshProjectileSize;
        _targetPlayer.OnJudge -= HandleJudge;
        _targetPlayer.OnWallHit -= HandleWallHit;
        _fishSpawner.OnFishCaught -= HandleFishCaught;
    }

    /// <summary>
    /// SPACE 판정 결과에 맞는 세기로 판정 임펄스를 발동한다. MISS는 세기 0이라 발동하지 않는다.
    /// judge와 판정별 임펄스 세기를 사용하며, 두 시네머신 카메라가 받는 임펄스를 만든다.
    /// </summary>
    private void HandleJudge(SkipJudge judge, float timingError)
    {
        float force = 0f;
        if (judge == SkipJudge.Perfect) force = _perfectImpulseForce;
        else if (judge == SkipJudge.Good) force = _goodImpulseForce;
        if (force > 0f) _judgeImpulse.GenerateImpulseWithForce(force);
    }

    /// <summary>
    /// 벽 충돌 속도에 비례한 세기로 벽 임펄스를 발동한다.
    /// impactSpeed와 벽 임펄스 속도 범위, 최대 세기를 사용하며, 느린 충돌은 무시하고 나머지는 임펄스를 만든다.
    /// </summary>
    private void HandleWallHit(float impactSpeed)
    {
        float strength = Mathf.InverseLerp(_wallImpulseMinSpeed, _wallImpulseFullSpeed, impactSpeed);
        if (strength <= 0f) return;
        _wallImpulse.GenerateImpulseWithForce(strength * _wallImpulseMaxForce);
    }

    /// <summary>
    /// 물고기를 잡으면 판정 임펄스를 포획 세기로 발동한다.
    /// _catchImpulseForce를 사용하며 type은 쓰지 않고, 두 시네머신 카메라가 받는 임펄스를 만든다.
    /// </summary>
    private void HandleFishCaught(FishType type)
    {
        _judgeImpulse.GenerateImpulseWithForce(_catchImpulseForce);
    }

    /// <summary>
    /// 실제 메시 외형의 월드 크기를 선택 또는 형상 변경 시에만 측정한다.
    /// HullPoints와 플레이어 위치, 메시 변환을 사용하며, 포획용 Trigger를 제외한 _projectileRadius를 저장한다.
    /// </summary>
    private void RefreshProjectileSize()
    {
        float maxSquaredRadius = 0f;
        foreach (Vector3 point in _projectileShape.HullPoints)
        {
            Vector3 offset = _projectileShape.transform.TransformPoint(point) - _focalPoint.position;
            maxSquaredRadius = Mathf.Max(maxSquaredRadius, offset.sqrMagnitude);
        }
        _projectileRadius = Mathf.Sqrt(maxSquaredRadius);
    }

    /// <summary>
    /// 기존 속도별 거리에 큰 투척물의 완만한 크기 보정을 적용한다.
    /// speed01, 투척물 반경과 Inspector 설정을 사용하며, 작은 물체는 기존 거리, 큰 물체는 보정한 거리를 반환한다.
    /// </summary>
    private float TargetDistance(float speed01)
    {
        float baseDistance = Mathf.Lerp(_distanceRange.x, _distanceRange.y, speed01);
        float diameter = _projectileRadius * 2f;
        if (diameter <= _largeProjectileThreshold) return baseDistance;

        float sizeRatio = diameter / _largeProjectileThreshold;
        float distanceScale = 1f + (Mathf.Sqrt(sizeRatio) - 1f) * _largeProjectileDistanceStrength;
        float clearanceDistance = _projectileRadius + _lookAhead * speed01 + _cinemachineCamera.Lens.NearClipPlane + _largeProjectileClearance;
        return Mathf.Max(baseDistance * distanceScale, clearanceDistance);
    }

    /// <summary>
    /// 투척물 선택 중에는 월드 -Z 방향과 기본 내려다보기 각도를 고정하고 크기에 맞춰 거리만 보간한다.
    /// dt와 대상 위치, 투척물 크기를 사용하며, 카메라 위치와 초점, 거리, FOV를 변경한다.
    /// </summary>
    private void UpdateSelectionCamera(float dt)
    {
        _yaw = 0f;
        _pitch = _basePitch;
        _distance = Mathf.Lerp(_distance, TargetDistance(0f), 1f - Mathf.Exp(-_framingSpeed * dt));
        _focus = _focalPoint.position;
        _focusVelocity = Vector3.zero;
        _smoothedFov = _baseFov;
        _cinemachineCamera.Lens.FieldOfView = _baseFov;
        UpdateTransform();
    }

    /// <summary>
    /// 카메라를 대상의 월드 -Z 방향으로 즉시 옮기고 흔들림과 FOV를 초기화한다.
    /// 대상의 현재 위치와 투척물 반경을 사용하며, _yaw, _pitch, _distance, _focus, 카메라 위치와 FOV, 진행 중인 임펄스를 변경한다.
    /// </summary>
    public void SnapBehindTarget()
    {
        // 재시작 전에 남은 판정, 벽 충돌, 포획, 레이저 흔들림이 선택 화면까지 이어지지 않게 끊는다.
        CinemachineImpulseManager.Instance.Clear();
        EndFisherQte(false);
        _yaw = 0f;
        _pitch = _basePitch;
        _distance = TargetDistance(0f);
        _focus = _focalPoint.position;
        _focusVelocity = Vector3.zero;
        _punchTime = -10f;
        _smoothedFov = _baseFov;
        _cinemachineCamera.Lens.FieldOfView = _baseFov;
        UpdateTransform();
    }

    /// <summary>
    /// 튕김 순간 FOV 펀치를 시작한다. 화면 흔들림은 임펄스가 맡는다.
    /// strength(0~1 권장)를 사용하며, _punchTime과 _punchStrength를 변경한다.
    /// </summary>
    public void Punch(float strength)
    {
        _punchTime = Time.unscaledTime;
        _punchStrength = strength;
    }

    /// <summary>
    /// 현재 FOV 펀치 세기를 구한다. 시작 직후 가장 세고 지속 시간 동안 줄어든다.
    /// _punchTime, _punchStrength, _shakeDuration을 사용하며, 0 이상 세기를 반환한다.
    /// </summary>
    private float PunchAmount()
    {
        float t = (Time.unscaledTime - _punchTime) / Mathf.Max(_shakeDuration, 0.01f);
        if (t >= 1f) return 0f;
        float fade = 1f - t;
        return _punchStrength * fade * fade;
    }

    /// <summary>
    /// 궤도 각도와 초점, 거리로 카메라 위치를 정하고 초점을 바라보게 한다.
    /// _yaw, _pitch, _focus, _distance를 사용하며, transform 위치와 회전을 변경한다.
    /// </summary>
    private void UpdateTransform()
    {
        Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 position = _focus + rotation * Vector3.back * _distance;
        // 카메라가 물속으로 들어가지 않게 한다.
        position.y = Mathf.Max(position.y, _waterY + _minHeightAboveWater);
        transform.position = position;
        transform.LookAt(_focus);
    }
}
