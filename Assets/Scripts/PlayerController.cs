using System;

using UnityEngine;
using UnityEngine.Serialization;

public enum SkipJudge
{
    None,
    Miss,
    Good,
    Perfect,
}

public enum GameOverReason
{
    Sunk,
    Stopped,
    HitGround,
    OutOfBounds,
}

// 물수제비 물고기 물리. 물에 닿으면 아랫면 법선 방향 양력, 표면 마찰, 잠김 저항이 작용한다.
// 스핀이 클수록 기울기가 유지되고, 접촉 순간 SPACE 타이밍을 맞추면 보너스를 받는다.
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("참조")]
    [SerializeField]
    private GameObject _water;
    [FormerlySerializedAs("_stone")]
    [SerializeField]
    private FishMeshGenerator _fish;
    private Rigidbody _playerRB;
    private FishSpawner _fishSpawner;
    private Transform _fishTransform;
    private Matrix4x4 _fishLocalMatrix = Matrix4x4.identity;
    private Quaternion _fishLocalRotation = Quaternion.identity;
    private float _waterY;
    private Vector3 _startPosition;
    private Quaternion _startRotation;

    [Header("던지기")]
    [SerializeField]
    private Vector3 _throwVelocity = new Vector3(0f, 10f, 50f);
    [Tooltip("던질 때 스핀 속도 (rad/s)")]
    [SerializeField]
    private float _throwSpin = 30f;
    [Tooltip("판마다 시작 앞뒤 기울기를 이 범위에서 랜덤으로 정한다(도)")]
    [SerializeField]
    private Vector2 _randomPitchRange = new Vector2(0f, 30f);
    [Tooltip("판마다 시작 좌우 기울기를 ±이 범위에서 랜덤으로 정한다(도)")]
    [SerializeField]
    private float _randomRollRange = 25f;
    [Tooltip("판마다 스핀 세기에 곱하는 랜덤 배율 범위. 방향(시계/반시계)도 랜덤이다")]
    [SerializeField]
    private Vector2 _randomSpinRange = new Vector2(0.7f, 1.3f);
    private bool _isThrown;
    private int _throwFrame = -1;
    private float _spinScale = 1f;
    private Vector3 _velocity;
    private ThrowModifiers _modifiers = ThrowModifiers.ForFish(1f, 1f);

    [Header("조작 (A/D·왼쪽 스틱 커브, 방향키·오른쪽 스틱 자세)")]
    [Tooltip("커브: 진행 방향이 초당 휘는 각도(도)")]
    [SerializeField]
    private float _steerRate = 15f;
    [Tooltip("커브: 커브 방향으로 물고기를 기울이는 각도(도)")]
    [SerializeField]
    private float _bankAngle = 12f;
    [Tooltip("위/아래: 초당 앞뒤 기울기 변화(도). 위를 누르면 앞쪽이 들린다")]
    [SerializeField]
    private float _pitchControlRate = 80f;
    [Tooltip("음수면 앞쪽이 숙여져 물에 박히기 쉽다")]
    [SerializeField]
    private Vector2 _pitchLimits = new Vector2(-10f, 55f);
    [Tooltip("좌/우: 초당 좌우 기울기 변화(도). 누른 쪽이 내려간다")]
    [SerializeField]
    private float _rollControlRate = 100f;
    [SerializeField]
    private float _rollLimit = 60f;
    [SerializeField]
    private float _stickDeadZone = 0.15f;
    private Vector3 _heading = Vector3.forward;
    private float _steer;
    private float _targetPitch;
    private float _targetRoll;

    [Header("자세 안정 (스핀이 클수록 단단하게 유지)")]
    [SerializeField]
    private float _attitudeStiffness = 90f;
    [SerializeField]
    private float _attitudeDamping = 15f;
    [Tooltip("튕길 때 물결에 기운 수면 법선 쪽으로 물고기를 비트는 세기 (수직 충돌 속도당 rad/s)")]
    [SerializeField]
    private float _impactWobble = 0.6f;
    [Tooltip("물결 때문에 수면 법선이 기울어 있다고 보는 최대 정도")]
    [SerializeField]
    private float _waveNormalTilt = 0.3f;
    [Tooltip("이 스핀(rad/s) 이상이면 자세 유지력 100%")]
    [SerializeField]
    private float _spinForFullStability = 20f;
    [Range(0f, 1f)]
    [SerializeField]
    private float _minStability = 0.2f;

    [Header("수면 반발")]
    [Tooltip("양력 = 계수 x 속력 x (아랫면이 물을 파고드는 속도) x 잠긴 면적, 아랫면 법선 방향")]
    [SerializeField]
    private float _liftCoefficient = 1f;
    [Tooltip("물고기 면을 따라 미끄러질 때의 마찰")]
    [SerializeField]
    private float _skinFriction = 0.002f;
    [Tooltip("물고기가 깊이 잠길수록 커지는 저항")]
    [SerializeField]
    private float _formDrag = 0.03f;
    [Tooltip("물에 닿아 있는 동안 속도와 상관없이 더하는 감속(m/s²). 느릴 때 수면을 오래 미끄러지지 않게 한다")]
    [SerializeField]
    private float _slideDeceleration = 5f;
    [Tooltip("양력이 무게중심에서 벗어난 곳에 걸려 생기는 회전 비율. 0이면 물고기 모양이 흔들림에 영향 없음")]
    [Range(0f, 0.2f)]
    [SerializeField]
    private float _liftTorqueFactor = 0.03f;
    [Tooltip("물에 닿아 있는 동안 스핀 감소율 (1/s)")]
    [SerializeField]
    private float _spinWaterDamping = 1.5f;
    [Tooltip("수면 위로 이만큼 떠야 접촉이 끝난 것으로 판정 (떨림 방지)")]
    [SerializeField]
    private float _exitClearance = 0.05f;
    private bool _inContact;
    private float _bottomHeight = float.PositiveInfinity;
    private float _topHeight = float.PositiveInfinity;
    private float _submersion;
    private float _submergedArea;
    private Vector3 _submergedCenter;
    private float _contactStartTime;
    private float _contactEntrySpeed;
    public event Action<Vector3, float> OnWaterContact;
    public event Action<int, SkipJudge> OnSkip;

    [Header("SPACE 타이밍 보너스 (초)")]
    [SerializeField]
    private float _perfectWindow = 0.06f;
    [SerializeField]
    private float _goodWindow = 0.15f;
    [Tooltip("PERFECT 시 추가 상승 속도 (GOOD은 절반)")]
    [SerializeField]
    private float _bonusLift = 4f;
    [Tooltip("PERFECT 시 이번 접촉에서 잃은 수평 속도 중 돌려받는 비율 (GOOD은 절반)")]
    [Range(0f, 1f)]
    [SerializeField]
    private float _bonusSpeedRefund = 0.6f;
    [Tooltip("MISS 뒤 다음 SPACE 입력을 받지 않는 시간(초). 연타로 판정 구간을 덮지 못하게 한다")]
    [SerializeField]
    private float _missCooldown = 0.5f;
    private SkipJudge _contactJudge;
    private bool _jumpPending;
    private float _jumpPressTime;
    private float _cooldownEndTime;
    public event Action<SkipJudge, float> OnJudge;

    [Header("미끄러짐 (SPACE 연타)")]
    [Tooltip("물에 이 시간(초) 이상 계속 닿아 있으면 미끄러짐으로 판정한다. GOOD 판정 범위보다 길어야 한다")]
    [SerializeField]
    private float _slideEnterTime = 0.3f;
    [Tooltip("미끄러지는 동안 첫 SPACE에 진행 방향으로 더하는 속도(m/s). 누를 때마다 줄어든다")]
    [SerializeField]
    private float _slidePushSpeed = 0.8f;
    [Tooltip("미끄러지는 동안 SPACE로 밀 수 있는 횟수. 누를 때마다 추진력이 일정하게 줄어 이 횟수에서 0이 된다")]
    [SerializeField]
    private int _slideMaxPushes = 20;
    [Tooltip("미끄러지는 동안 물고기 아랫면을 띄워 두는 수면 아래 깊이(m). 0보다 커야 물 마찰로 감속한다")]
    [SerializeField]
    private float _slideFloatDepth = 0.02f;
    [Tooltip("미끄러지기 시작할 때 수면까지 떠오르는 최대 속도(m/s)")]
    [SerializeField]
    private float _slideRiseSpeed = 2f;
    [Tooltip("미끄러지다 수평 속력이 이 아래로 떨어지면 더 떠 있지 않고 가라앉는다(m/s)")]
    [SerializeField]
    private float _slideSinkSpeed = 1f;
    [Tooltip("미끄러지는 동안 A/D 커브로 진행 방향이 초당 휘는 각도(도). 비행 중 값은 Steer Rate")]
    [SerializeField]
    private float _slideSteerRate = 25f;
    [Tooltip("미끄러지는 동안에만 수평 속력에서 추가로 빼는 감속도(m/s²). 물 마찰과 Slide Deceleration에 더해진다")]
    [SerializeField]
    private float _slideBrakeDeceleration = 3f;
    private bool _isSliding;
    private bool _isSlideSinking;
    private int _slidePushesUsed;
    private int _slidePushCount;
    public event Action OnSlidePush;

    [Header("게임오버")]
    [Tooltip("물고기 윗면이 수면 아래로 이만큼 내려가면 가라앉은 것으로 판정")]
    [SerializeField]
    private float _sinkDepth = 0.8f;
    [SerializeField]
    private float _stopSpeed = 0.5f;
    [SerializeField]
    private float _stopTime = 1.5f;
    [SerializeField]
    private float _killY = -40f;
    private bool _isGameOver;
    private float _stopTimer;
    public event Action<GameOverReason> OnGameOver;
    public event Action<float> OnObstacleHit;

    public bool IsThrown => _isThrown;
    public bool IsGameOver => _isGameOver;
    public bool InContact => _inContact;
    public int SkipCount { get; private set; }
    public float Distance { get; private set; }
    public float Speed => _velocity.magnitude;
    public Vector3 Velocity => _velocity;
    public Vector3 WaterContactPoint => new Vector3(_playerRB.position.x, _waterY, _playerRB.position.z);
    public float TargetPitch => _targetPitch;
    public float TargetRoll => _targetRoll;
    public int FishSeed => _fish.CurrentSeed;
    public float ContactElapsed => _inContact ? Time.time - _contactStartTime : 0f;
    public float CurrentPitch => Mathf.Asin(Mathf.Clamp(-Vector3.Dot(FishUp(), _heading), -1f, 1f)) * Mathf.Rad2Deg;
    public float SpinRate => Vector3.Dot(_playerRB.angularVelocity, FishUp());
    public float Stability => Mathf.Lerp(_minStability, 1f, Mathf.Clamp01(Mathf.Abs(SpinRate) / Mathf.Max(_spinForFullStability, 0.01f)));
    public bool IsSliding => _isSliding;
    // 남은 밀기 힘 (1이면 처음, 0이면 더 밀리지 않음)
    public float SlidePower => 1f - Mathf.Clamp01((float)_slidePushesUsed / Mathf.Max(_slideMaxPushes, 1));

    // 지금 SPACE를 판정 입력으로 받는지 (MISS 쿨타임이 아니고, 접촉 중이면 판정 전이며 GOOD 범위까지)
    public bool CanJudge => _isThrown && !_isGameOver && Time.time >= _cooldownEndTime
        && (!_inContact || (_contactJudge == SkipJudge.None && ContactElapsed <= _goodWindow));

    // 지금 누르면 GOOD 이상을 받는 구간인지
    public bool IsInJudgeWindow => CanJudge && (_inContact || TimeToWaterImpact <= _goodWindow);

    // 지금 궤적대로 갈 때 수면에 닿기까지 남은 시간(초). 닿아 있으면 0
    public float TimeToWaterImpact
    {
        get
        {
            if (!_isThrown || _isGameOver) return float.PositiveInfinity;
            if (_inContact || _bottomHeight <= 0f) return 0f;
            float gravity = -Physics.gravity.y * _modifiers.AirGravityScale;
            float up = _velocity.y;
            return (up + Mathf.Sqrt(up * up + 2f * gravity * _bottomHeight)) / gravity;
        }
    }

    void Update()
    {
        if (Time.timeScale == 0f) return;
        // 던지기 전에는 방향키로 자세를 맞추고, 떠 있는 물고기에 바로 보여준다.
        if (!_isThrown)
        {
            ReadAttitudeInput(Time.deltaTime);
            ApplyAimPose();
            return;
        }
        if (_isGameOver) return;

        UpdateAbilityFlight(Time.deltaTime);
        // 던지는 데 쓴 SPACE 입력을 첫 타이밍 입력으로 세지 않는다.
        if (Time.frameCount == _throwFrame) return;

        if (!Managers.Input.JumpPressed) return;

        // 미끄러지는 동안은 타이밍 판정 대신 누른 횟수만큼 밀어준다.
        if (_isSliding)
        {
            _slidePushCount++;
            OnSlidePush?.Invoke();
        }
        // 판정을 기다리는 입력이 있거나 MISS 쿨타임 중이면 새 입력은 받지 않는다.
        else if (!_jumpPending && Time.time >= _cooldownEndTime)
        {
            _jumpPending = true;
            _jumpPressTime = Time.time;
        }
    }

    void FixedUpdate()
    {
        if (!_isThrown) return;

        float dt = Time.fixedDeltaTime;
        _velocity = _playerRB.linearVelocity;

        if (_isGameOver)
        {
            _steer = 0f;
        }
        else
        {
            ReadControls(dt);
        }

        SampleWater();
        if (!_isGameOver)
        {
            UpdateContact();
            if (_isSliding)
            {
                ApplySlidePush();
            }
        }
        if (_submersion > 0f)
        {
            ApplyWaterForces(dt);
        }
        else
        {
            // 공중 중력 배율이 1보다 작으면 중력 일부를 상쇄해 활공한다.
            _velocity -= Physics.gravity * ((1f - _modifiers.AirGravityScale) * dt);
        }
        if (_isSliding)
        {
            ApplySlideDeceleration(dt);
            ApplySlideFloat(dt);
        }
        ApplySteering(dt);
        _playerRB.linearVelocity = _velocity;
        ApplyAttitude();

        if (!_isGameOver)
        {
            UpdateDistance();
            CheckGameOver(dt);
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!enabled || !_isThrown || Time.timeScale == 0f) return;

        FishAbility ability = _fish.CurrentAbility;
        if (ability != null)
        {
            ability.OnObstacleCollision(AbilityContext(), collision);
        }

        OnObstacleHit?.Invoke(collision.relativeVelocity.magnitude);
        if (collision.gameObject.CompareTag("Finish"))
        {
            GameOver(GameOverReason.HitGround);
        }
    }

    void OnDestroy()
    {
        _fish.OnGenerated -= HandleFishGenerated;
    }

    /// <summary>
    /// 물과 물고기 메시 참조를 준비하고 시작 위치를 기록한 뒤 던지기 전 상태로 되돌린다.
    /// fishSpawner를 사용하며, 물 높이와 시작 포즈, 물고기 로컬 변환과 사출용 스포너를 저장한다.
    /// </summary>
    public void Initialize(FishSpawner fishSpawner)
    {
        _fishSpawner = fishSpawner;
        _playerRB = GetComponent<Rigidbody>();
        _fishTransform = _fish.transform;
        _fishLocalMatrix = transform.worldToLocalMatrix * _fishTransform.localToWorldMatrix;
        _fishLocalRotation = Quaternion.Inverse(transform.rotation) * _fishTransform.rotation;
        _waterY = _water.GetComponent<Collider>().bounds.max.y;
        _playerRB.maxAngularVelocity = Mathf.Max(_playerRB.maxAngularVelocity, _throwSpin * 2f);
        _startPosition = transform.position;
        _startRotation = transform.rotation;

        _fish.OnGenerated += HandleFishGenerated;
        HandleFishGenerated();
        ResetToStart();
    }

    /// <summary>
    /// 물고기를 시작 위치에 멈춘 상태로 되돌리고 판정, 기록, 접촉 상태를 초기화한 뒤 시작 자세와 스핀을 랜덤으로 정한다.
    /// 시작 포즈와 랜덤 범위를 사용하며, Rigidbody 속도와 위치, 목표 자세, 스핀 배율, SkipCount, Distance를 변경한다.
    /// </summary>
    public void ResetToStart()
    {
        _isThrown = false;
        _isGameOver = false;
        _inContact = false;
        _jumpPending = false;
        _cooldownEndTime = 0f;
        _contactJudge = SkipJudge.None;
        _contactStartTime = float.NegativeInfinity;
        _isSliding = false;
        _isSlideSinking = false;
        _slidePushesUsed = 0;
        _slidePushCount = 0;
        _stopTimer = 0f;
        _steer = 0f;
        SkipCount = 0;
        Distance = 0f;
        _bottomHeight = float.PositiveInfinity;
        _topHeight = float.PositiveInfinity;
        _submersion = 0f;
        _submergedArea = 0f;
        _velocity = Vector3.zero;
        _heading = ThrowHeading();

        _targetPitch = UnityEngine.Random.Range(_randomPitchRange.x, _randomPitchRange.y);
        _targetRoll = UnityEngine.Random.Range(-_randomRollRange, _randomRollRange);
        float spinSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
        _spinScale = spinSign * UnityEngine.Random.Range(_randomSpinRange.x, _randomSpinRange.y);

        _playerRB.useGravity = false;
        _playerRB.linearVelocity = Vector3.zero;
        _playerRB.angularVelocity = Vector3.zero;
        _playerRB.position = _startPosition;
        _playerRB.rotation = _startRotation;
        transform.SetPositionAndRotation(_startPosition, _startRotation);
    }

    /// <summary>
    /// 외부 연출(시네마 등)을 위해 플레이어의 물리 이동과 속도를 즉시 멈추고 게임오버 상태로 전환한다.
    /// 이벤트를 발생시키지 않고 비행과 물리 연산만 중단한다.
    /// </summary>
    public void StopImmediately()
    {
        _velocity = Vector3.zero;
        _isGameOver = true;
        _isThrown = false;
        _inContact = false;
        _isSliding = false;
        _jumpPending = false;
        if (_playerRB != null)
        {
            _playerRB.linearVelocity = Vector3.zero;
            _playerRB.angularVelocity = Vector3.zero;
            _playerRB.isKinematic = true;
        }
    }

    /// <summary>
    /// 선택 파닥임을 복구하고 맞춘 자세와 랜덤 스핀으로 물고기를 던진다.
    /// modifiers와 목표 자세, 던지기 설정을 사용하며, Rigidbody 속도와 회전, _isThrown을 변경한다.
    /// </summary>
    public void Throw(ThrowModifiers modifiers)
    {
        if (_isThrown) return;

        _fish.SetFlopping(false);
        _modifiers = modifiers;
        float spin = _throwSpin * modifiers.SpinMultiplier * _spinScale;
        _playerRB.maxAngularVelocity = Mathf.Max(_playerRB.maxAngularVelocity, Mathf.Abs(spin) * 2f);
        Vector3 velocity = _startRotation * _throwVelocity * modifiers.SpeedMultiplier;
        _heading = ThrowHeading();

        Quaternion fishRotation = AttitudeRotation(0f);
        Quaternion rotation = fishRotation * Quaternion.Inverse(_fishLocalRotation);
        _playerRB.rotation = rotation;
        transform.rotation = rotation;

        _playerRB.useGravity = true;
        _playerRB.linearVelocity = velocity;
        _playerRB.angularVelocity = (fishRotation * Vector3.up) * spin;
        _velocity = velocity;
        _isThrown = true;
        _throwFrame = Time.frameCount;
        UpdateAbilityFlight(0f);
    }

    /// <summary>
    /// 접촉 직전 incomingVelocity의 수평 성분을 반전해 33Fisher에게 튕겨 나간다.
    /// 수직 속도는 유지하고 Rigidbody 속도, 내부 속도와 진행 방향, 대기 조작을 갱신한다.
    /// </summary>
    public void ReboundFromFisher(Vector3 incomingVelocity)
    {
        _velocity = new Vector3(-incomingVelocity.x, incomingVelocity.y, -incomingVelocity.z);
        _playerRB.linearVelocity = _velocity;
        Vector3 horizontal = Horizontal(_velocity);
        if (horizontal.sqrMagnitude > 0f) _heading = horizontal.normalized;
        _steer = 0f;
        _jumpPending = false;
        _slidePushCount = 0;
        _stopTimer = 0f;
    }

    /// <summary>
    /// 지금 진행 방향 앞으로 forwardSpeed를 더하고 위로 upSpeed 속도로 세게 튀어 나가게 한다.
    /// forwardSpeed, upSpeed와 _heading을 사용하며, Rigidbody 속도와 내부 속도를 변경한다.
    /// </summary>
    public void Burst(float forwardSpeed, float upSpeed)
    {
        _velocity = Horizontal(_playerRB.linearVelocity) + _heading * forwardSpeed + Vector3.up * upSpeed;
        _playerRB.linearVelocity = _velocity;
    }

    /// <summary>
    /// 비행 중 공중 중력 배율을 바꾼다. 다음에 던질 때는 던지는 물체의 원래 배율로 돌아간다.
    /// scale을 사용하며, _modifiers의 공중 중력 배율을 변경한다.
    /// </summary>
    public void SetAirGravityScale(float scale)
    {
        _modifiers = new ThrowModifiers(
            _modifiers.SpeedMultiplier,
            _modifiers.SpinMultiplier,
            _modifiers.LiftMultiplier,
            _modifiers.FrictionMultiplier,
            scale);
    }

    /// <summary>
    /// 비행 중 커브 방향과 목표 자세를 갱신한다. 커브는 A/D 또는 왼쪽 스틱과 트리거로 받는다.
    /// dt와 키보드, 게임패드 입력을 사용하며, _steer, _targetPitch, _targetRoll을 변경한다.
    /// </summary>
    private void ReadControls(float dt)
    {
        _steer = Managers.Input.ReadSteer(_stickDeadZone);
        ReadAttitudeInput(dt);
    }

    /// <summary>
    /// 방향키와 오른쪽 스틱으로 목표 자세를 바꾼다. 위/아래는 앞뒤 기울기, 좌/우는 좌우 기울기다.
    /// dt와 키보드, 게임패드 입력을 사용하며, _targetPitch와 _targetRoll을 변경한다.
    /// </summary>
    private void ReadAttitudeInput(float dt)
    {
        Vector2 input = Managers.Input.ReadAttitude(_stickDeadZone);

        _targetPitch = Mathf.Clamp(_targetPitch + input.y * _pitchControlRate * dt, _pitchLimits.x, _pitchLimits.y);
        _targetRoll = Mathf.Clamp(_targetRoll + input.x * _rollControlRate * dt, -_rollLimit, _rollLimit);
    }

    /// <summary>
    /// 던지기 전 떠 있는 물고기를 방향키로 맞춘 자세로 돌려 보여준다.
    /// 목표 자세와 진행 방향을 사용하며, Rigidbody와 transform 회전을 변경한다.
    /// </summary>
    private void ApplyAimPose()
    {
        Quaternion rotation = AttitudeRotation(0f) * Quaternion.Inverse(_fishLocalRotation);
        _playerRB.rotation = rotation;
        transform.rotation = rotation;
    }

    /// <summary>
    /// 진행 방향 기준 목표 자세(앞뒤 기울기, 좌우 기울기)를 회전으로 만든다.
    /// extraRoll(커브로 더 기울이는 각도)과 _heading, _targetPitch, _targetRoll을 사용하며, 물고기의 목표 회전을 반환한다.
    /// </summary>
    private Quaternion AttitudeRotation(float extraRoll)
    {
        // 좌우 기울기가 양수면 오른쪽이 내려간다.
        return Quaternion.LookRotation(_heading, Vector3.up) * Quaternion.Euler(-_targetPitch, 0f, -(_targetRoll + extraRoll));
    }

    /// <summary>
    /// 물리 포즈 기준으로 물고기의 최저/최고 높이와 물에 잠긴 아랫면 면적, 그 중심을 계산한다.
    /// 물고기 외형 점과 아랫면 샘플점을 사용하며, _bottomHeight, _topHeight, _submersion, _submergedArea, _submergedCenter를 변경한다.
    /// </summary>
    private void SampleWater()
    {
        // 보간된 transform 대신 물리 포즈를 써야 접촉 판정이 한 스텝 늦지 않는다.
        Matrix4x4 toWorld = Matrix4x4.TRS(_playerRB.position, _playerRB.rotation, Vector3.one) * _fishLocalMatrix;

        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        Vector3[] hull = _fish.HullPoints;
        for (int i = 0; i < hull.Length; i++)
        {
            float y = toWorld.MultiplyPoint3x4(hull[i]).y;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        _bottomHeight = minY - _waterY;
        _topHeight = maxY - _waterY;
        _submersion = Mathf.Clamp01((_waterY - minY) / Mathf.Max(maxY - minY, 0.01f));

        _submergedArea = 0f;
        Vector3 center = Vector3.zero;
        Vector3[] samples = _fish.BottomSamples;
        float[] areas = _fish.SampleAreas;
        for (int i = 0; i < samples.Length; i++)
        {
            Vector3 point = toWorld.MultiplyPoint3x4(samples[i]);
            if (point.y < _waterY)
            {
                _submergedArea += areas[i];
                center += point * areas[i];
            }
        }
        _submergedCenter = _submergedArea > 0f ? center / _submergedArea : _playerRB.worldCenterOfMass;
    }

    /// <summary>
    /// 접촉 시작과 끝, 미끄러짐 시작을 판정하고, 대기 중인 SPACE 입력을 지금 또는 방금 끝난 접촉으로 판정하거나 만료시킨다.
    /// _bottomHeight와 SPACE 입력 시각, 착수 시각을 사용하며, 접촉 상태와 _isSliding, _jumpPending을 변경하고 미끄러짐 시작을 특수 동작에 알린다.
    /// </summary>
    private void UpdateContact()
    {
        if (!_inContact && _bottomHeight < 0f)
        {
            BeginContact();
        }
        // 미끄러지는 중에는 자세가 바뀌어 아랫면이 살짝 떠도 접촉을 끝내지 않는다.
        else if (_inContact && !_isSliding && _bottomHeight > _exitClearance)
        {
            EndContact();
        }

        // 튕겨 나가지 못하고 계속 닿아 있으면 미끄러짐으로 바꾸고, 기다리던 타이밍 입력은 버린다.
        if (_inContact && !_isSliding && ContactElapsed >= _slideEnterTime)
        {
            _isSliding = true;
            _jumpPending = false;
            _slidePushCount = 0;

            FishAbility ability = _fish.CurrentAbility;
            if (ability != null)
            {
                ability.OnSlideStart(AbilityContext());
            }
        }

        if (!_jumpPending) return;

        // 접촉 중이거나, 방금 끝난 접촉이 판정 전이고 그 착수 후 GOOD 범위 안에 눌렀으면 그 접촉으로 판정한다.
        float timingError = _jumpPressTime - _contactStartTime;
        if (_contactJudge == SkipJudge.None && timingError >= -_goodWindow && (_inContact || timingError <= _goodWindow))
        {
            Judge(timingError);
        }
        else if (Time.time - _jumpPressTime > _goodWindow)
        {
            // 앞뒤 어느 착수와도 판정 범위가 맞지 않았다.
            _jumpPending = false;
            ReportJudge(SkipJudge.Miss, float.NegativeInfinity);
        }
    }

    /// <summary>
    /// 물에 닿기 시작한 순간의 시각과 수평 속도를 기록하고 착수 이벤트를 보낸다.
    /// _velocity와 물고기 위치를 사용하며, _inContact, _contactStartTime, _contactEntrySpeed, _contactJudge를 변경한다.
    /// </summary>
    private void BeginContact()
    {
        _inContact = true;
        _contactStartTime = Time.time;
        _contactEntrySpeed = Horizontal(_velocity).magnitude;
        _contactJudge = SkipJudge.None;
        KickOnImpact();
        OnWaterContact?.Invoke(WaterContactPoint, _velocity.magnitude);
    }

    /// <summary>
    /// 착수 순간 물결에 살짝 기운 수면에 부딪힌 것처럼 물고기를 비튼다. 물고기 윗면이 그 수면 법선 쪽으로 꺾이고 스핀도 조금 흔들린다.
    /// 수직 충돌 속도, _impactWobble, _waveNormalTilt를 사용하며, Rigidbody 각속도를 변경한다.
    /// </summary>
    private void KickOnImpact()
    {
        float impact = Mathf.Max(0f, -_velocity.y);
        Vector2 waveTilt = UnityEngine.Random.insideUnitCircle * _waveNormalTilt;
        Vector3 waterNormal = new Vector3(waveTilt.x, 1f, waveTilt.y).normalized;
        Vector3 up = FishUp();
        if (up.y < 0f) up = -up;

        // 윗면을 수면 법선 쪽으로 돌리는 축이다. 기울기 차이가 클수록, 세게 부딪힐수록 크게 꺾인다.
        Vector3 kick = Vector3.Cross(up, waterNormal) * (impact * _impactWobble);
        float spinShake = UnityEngine.Random.Range(-0.15f, 0.15f) * Vector3.Dot(_playerRB.angularVelocity, up);
        _playerRB.angularVelocity += kick + up * spinShake;
    }

    /// <summary>
    /// 물에서 벗어난 순간 튕김으로 세고, 판정 보너스가 있으면 잃은 수평 속도 일부를 돌려준다.
    /// 판정 결과는 다음 착수 전까지 남겨, 늦은 입력이 같은 접촉을 다시 판정하지 않게 한다.
    /// _contactJudge를 사용하며, _velocity, SkipCount, 접촉 상태를 변경한다.
    /// </summary>
    private void EndContact()
    {
        _inContact = false;

        if (_velocity.y > 0f)
        {
            float quality = JudgeQuality(_contactJudge);
            if (quality > 0f)
            {
                RefundContactSpeed(quality);
            }
            SkipCount++;
            OnSkip?.Invoke(SkipCount, _contactJudge);
        }
    }

    /// <summary>
    /// 이번 접촉에서 잃은 수평 속도 중 판정 배율만큼을 진행 방향으로 돌려준다.
    /// quality와 _contactEntrySpeed, _bonusSpeedRefund를 사용하며, _velocity를 변경한다.
    /// </summary>
    private void RefundContactSpeed(float quality)
    {
        float lost = _contactEntrySpeed - Horizontal(_velocity).magnitude;
        if (lost > 0f)
        {
            _velocity += _heading * (lost * _bonusSpeedRefund * quality);
        }
    }

    /// <summary>
    /// 미끄러지는 동안 쌓인 SPACE 입력마다 진행 방향으로 밀어준다. 누를 때마다 추진력이 줄어 _slideMaxPushes번째에 0이 된다.
    /// _slidePushCount, _slidePushSpeed, SlidePower를 사용하며, _velocity, _slidePushesUsed, _slidePushCount를 변경한다.
    /// </summary>
    private void ApplySlidePush()
    {
        for (int i = 0; i < _slidePushCount; i++)
        {
            _velocity += _heading * (_slidePushSpeed * SlidePower);
            _slidePushesUsed++;
        }
        _slidePushCount = 0;
    }

    /// <summary>
    /// 미끄러지는 동안 수평 속력을 일정한 감속도로 줄인다. 멈춘 뒤 반대로 밀리지는 않는다.
    /// dt와 _slideBrakeDeceleration을 사용하며, _velocity의 수평 성분을 변경한다.
    /// </summary>
    private void ApplySlideDeceleration(float dt)
    {
        Vector3 horizontal = Horizontal(_velocity);
        float speed = horizontal.magnitude;
        if (speed < 0.0001f) return;

        float slowed = Mathf.Max(speed - _slideBrakeDeceleration * dt, 0f);
        horizontal *= slowed / speed;
        _velocity.x = horizontal.x;
        _velocity.z = horizontal.z;
    }

    /// <summary>
    /// 미끄러지는 동안 물고기 아랫면을 수면 바로 아래에 띄워 보이게 하고, 느려지거나 게임오버가 되면 더 띄우지 않고 가라앉힌다.
    /// 어느 쪽이든 위로 튀어 오르지는 못한다.
    /// dt, _bottomHeight, 수평 속력, _slideFloatDepth, _slideRiseSpeed, _slideSinkSpeed를 사용하며, _velocity.y와 _isSlideSinking을 변경한다.
    /// </summary>
    private void ApplySlideFloat(float dt)
    {
        if (!_isSlideSinking && (_isGameOver || Horizontal(_velocity).magnitude < _slideSinkSpeed))
        {
            _isSlideSinking = true;
        }

        if (_isSlideSinking)
        {
            _velocity.y = Mathf.Min(_velocity.y, 0f);
            return;
        }

        // 다음 물리 스텝의 중력까지 상쇄해, 아랫면이 목표 깊이에 머물게 한다.
        float rise = (-_slideFloatDepth - _bottomHeight) / dt - Physics.gravity.y * dt;
        _velocity.y = Mathf.Min(rise, _slideRiseSpeed);
    }

    /// <summary>
    /// 착수 시각과 SPACE 입력 시각의 차이로 판정하고, 성공하면 상승 속도와 스핀을 보충한다.
    /// 물에서 이미 벗어난 뒤의 늦은 판정이면 수평 속도 환급도 바로 준다.
    /// timingError(초, 빠르면 음수)를 사용하며, _contactJudge, _velocity, Rigidbody 각속도를 변경하고 특수 동작과 OnJudge로 결과를 보낸다.
    /// </summary>
    private void Judge(float timingError)
    {
        _jumpPending = false;
        float error = Mathf.Abs(timingError);
        if (error <= _perfectWindow) _contactJudge = SkipJudge.Perfect;
        else if (error <= _goodWindow) _contactJudge = SkipJudge.Good;
        else _contactJudge = SkipJudge.Miss;

        float quality = JudgeQuality(_contactJudge);
        if (quality > 0f)
        {
            _velocity += Vector3.up * (_bonusLift * quality);
            if (!_inContact)
            {
                RefundContactSpeed(quality);
            }

            // 스핀을 다시 살려 자세를 잡아준다.
            Vector3 up = FishUp();
            Vector3 angular = _playerRB.angularVelocity;
            float spin = Vector3.Dot(angular, up);
            float sign = spin < 0f ? -1f : 1f;
            float throwSpin = _throwSpin * _modifiers.SpinMultiplier;
            float restored = Mathf.Max(Mathf.Abs(spin), Mathf.Lerp(Mathf.Abs(spin), throwSpin, quality)) * sign;
            _playerRB.angularVelocity = angular + up * (restored - spin);
        }
        ReportJudge(_contactJudge, timingError);
    }

    /// <summary>
    /// 판정 결과를 던진 물고기의 특수 동작에 먼저 알리고 OnJudge로 보낸다. Perfect와 Good은 성공, Miss는 실패다.
    /// judge와 timingError(초, 빠르면 음수)를 사용하며, Miss면 _cooldownEndTime을 갱신하고 특수 동작 상태를 변경한 뒤 OnJudge를 보낸다.
    /// </summary>
    private void ReportJudge(SkipJudge judge, float timingError)
    {
        // MISS 뒤에는 잠시 입력을 받지 않아 연타로 판정 구간을 덮지 못하게 한다.
        if (judge == SkipJudge.Miss)
        {
            _cooldownEndTime = Time.time + _missCooldown;
        }

        FishAbility ability = _fish.CurrentAbility;
        if (ability != null)
        {
            if (judge == SkipJudge.Miss)
            {
                ability.OnJudgeMiss(AbilityContext());
            }
            else
            {
                ability.OnJudgeSuccess(AbilityContext(), judge);
            }
        }
        OnJudge?.Invoke(judge, timingError);
    }

    /// <summary>
    /// 던진 뒤 게임오버 전까지 던진 물고기의 특수 동작을 갱신한다.
    /// dt와 현재 특수 동작을 사용하며, 특수 동작 상태를 변경한다.
    /// </summary>
    private void UpdateAbilityFlight(float dt)
    {
        FishAbility ability = _fish.CurrentAbility;
        if (ability == null) return;

        ability.UpdateFlight(AbilityContext(), dt);
    }

    /// <summary>
    /// 특수 동작에 넘길 대상 묶음을 만든다.
    /// 이 컨트롤러와 _fish를 사용하며, ThrowContext를 반환한다.
    /// </summary>
    private ThrowContext AbilityContext()
    {
        return new ThrowContext(this, _fish, _fish.FishBody, _fishSpawner);
    }

    /// <summary>
    /// 물에 잠긴 정도에 따라 양력, 표면 마찰, 잠김 저항을 속도에 반영하고 스핀을 줄인다.
    /// dt와 SampleWater 결과를 사용하며, _velocity와 Rigidbody 각속도를 변경한다.
    /// </summary>
    private void ApplyWaterForces(float dt)
    {
        float speed = _velocity.magnitude;
        if (speed < 0.0001f) return;

        Vector3 up = FishUp();
        Vector3 normal = up.y >= 0f ? up : -up;

        // 한 스텝에 파고드는 속도 이상은 밀지 않는다. 그래서 물고기는 자기 기울기만큼의 각도로 튀어 나간다.
        float intoWater = -Vector3.Dot(_velocity, normal);
        if (intoWater > 0f && _submergedArea > 0f)
        {
            float lift = Mathf.Min(_liftCoefficient * _modifiers.LiftMultiplier * speed * intoWater * _submergedArea * dt, intoWater);
            _velocity += normal * lift;
            Vector3 arm = _submergedCenter - _playerRB.worldCenterOfMass;
            _playerRB.AddTorque(Vector3.Cross(arm, normal * (lift * _playerRB.mass)) * _liftTorqueFactor, ForceMode.Impulse);
        }

        if (_submergedArea > 0f)
        {
            Vector3 along = _velocity - Vector3.Dot(_velocity, normal) * normal;
            float alongSpeed = along.magnitude;
            if (alongSpeed > 0.0001f)
            {
                // 속도 제곱 마찰은 느릴 때 거의 사라지므로 일정한 미끄럼 감속을 더한다.
                float friction = _skinFriction * _modifiers.FrictionMultiplier * speed * alongSpeed * _submergedArea * dt
                    + _slideDeceleration * dt;
                friction = Mathf.Min(friction, alongSpeed);
                _velocity -= along / alongSpeed * friction;
            }
        }

        float newSpeed = _velocity.magnitude;
        if (newSpeed > 0.0001f)
        {
            float drag = Mathf.Min(_formDrag * newSpeed * newSpeed * _submersion * _submersion * dt, newSpeed);
            _velocity -= _velocity / newSpeed * drag;
        }

        float areaRatio = _submergedArea / _fish.TotalBottomArea;
        Vector3 angular = _playerRB.angularVelocity;
        float spin = Vector3.Dot(angular, up);
        float damped = spin * Mathf.Exp(-_spinWaterDamping * areaRatio * dt);
        _playerRB.angularVelocity = angular + up * (damped - spin);
    }

    /// <summary>
    /// A/D 입력만큼 수평 진행 방향을 휘게 하고 현재 진행 방향을 갱신한다. 미끄러지는 동안은 더 세게 휜다.
    /// dt와 _steer, _steerRate, _slideSteerRate를 사용하며, _velocity의 수평 성분과 _heading을 변경한다.
    /// </summary>
    private void ApplySteering(float dt)
    {
        Vector3 horizontal = Horizontal(_velocity);
        if (horizontal.sqrMagnitude < 0.01f) return;

        if (Mathf.Abs(_steer) > 0.01f)
        {
            float steerRate = _isSliding ? _slideSteerRate : _steerRate;
            Vector3 turned = Quaternion.AngleAxis(_steer * steerRate * dt, Vector3.up) * horizontal;
            _velocity.x = turned.x;
            _velocity.z = turned.z;
            horizontal = turned;
        }
        _heading = horizontal.normalized;
    }

    /// <summary>
    /// 진행 방향 기준 목표 자세(받음각, 커브 기울기)로 물고기 윗면을 끌어당기는 토크를 준다.
    /// _targetPitch, _steer, Stability를 사용하며, Rigidbody에 토크를 더한다.
    /// </summary>
    private void ApplyAttitude()
    {
        Vector3 up = FishUp();
        Vector3 targetUp = AttitudeRotation(_steer * _bankAngle) * Vector3.up;

        Vector3 axis = Vector3.Cross(up, targetUp);
        float axisLength = axis.magnitude;
        Vector3 error = axisLength > 0.0001f
            ? axis / axisLength * (Vector3.Angle(up, targetUp) * Mathf.Deg2Rad)
            : Vector3.zero;

        // 스핀 축 회전은 그대로 두고, 축이 기우는 흔들림만 잡는다.
        Vector3 angular = _playerRB.angularVelocity;
        Vector3 wobble = angular - Vector3.Dot(angular, up) * up;
        Vector3 correction = (error * _attitudeStiffness - wobble * _attitudeDamping) * Stability;
        _playerRB.AddTorque(correction, ForceMode.Acceleration);
    }

    /// <summary>
    /// 시작 위치에서 가장 멀리 간 수평 거리를 갱신한다.
    /// Rigidbody 위치를 사용하며, Distance를 변경한다.
    /// </summary>
    private void UpdateDistance()
    {
        Vector3 offset = Horizontal(_playerRB.position - _startPosition);
        Distance = Mathf.Max(Distance, offset.magnitude);
    }

    /// <summary>
    /// 가라앉음, 맵 이탈, 정지 조건을 검사해 게임오버를 발생시킨다.
    /// dt, _topHeight, 위치, 속도를 사용하며, _stopTimer를 변경한다.
    /// </summary>
    private void CheckGameOver(float dt)
    {
        if (_topHeight < -_sinkDepth)
        {
            GameOver(GameOverReason.Sunk);
            return;
        }
        if (_playerRB.position.y < _killY)
        {
            GameOver(GameOverReason.OutOfBounds);
            return;
        }

        if (_velocity.magnitude < _stopSpeed)
        {
            _stopTimer += dt;
            if (_stopTimer >= _stopTime)
            {
                GameOver(GameOverReason.Stopped);
            }
        }
        else
        {
            _stopTimer = 0f;
        }
    }

    /// <summary>
    /// 한 번만 게임오버 상태로 바꾸고 이유와 함께 이벤트를 보낸다.
    /// reason을 사용하며, _isGameOver와 _jumpPending을 변경한다.
    /// </summary>
    public void GameOver(GameOverReason reason)
    {
        if (_isGameOver) return;
        _isGameOver = true;
        _jumpPending = false;
        OnGameOver?.Invoke(reason);
    }

    /// <summary>
    /// 물고기 모양이 바뀌면 무게중심과 관성을 새 모양 기준으로 다시 계산한다.
    /// 입력값은 없으며, Rigidbody 질량 특성을 변경한다.
    /// </summary>
    private void HandleFishGenerated()
    {
        _playerRB.ResetCenterOfMass();
        _playerRB.ResetInertiaTensor();
    }

    /// <summary>
    /// 시작 회전 기준 던지기 속도의 수평 방향을 구한다.
    /// _startRotation과 _throwVelocity를 사용하며, 정규화된 수평 방향을 반환한다.
    /// </summary>
    private Vector3 ThrowHeading()
    {
        Vector3 horizontal = Horizontal(_startRotation * _throwVelocity);
        return horizontal.sqrMagnitude > 0.0001f ? horizontal.normalized : Vector3.forward;
    }

    /// <summary>
    /// 물리 포즈 기준 물고기 윗면 방향을 구한다.
    /// Rigidbody 회전과 물고기 로컬 회전을 사용하며, 월드 방향 벡터를 반환한다.
    /// </summary>
    private Vector3 FishUp()
    {
        return _playerRB.rotation * _fishLocalRotation * Vector3.up;
    }

    /// <summary>
    /// 판정을 보너스 배율로 바꾼다.
    /// judge를 사용하며, PERFECT 1, GOOD 0.5, 그 외 0을 반환한다.
    /// </summary>
    private static float JudgeQuality(SkipJudge judge)
    {
        if (judge == SkipJudge.Perfect) return 1f;
        if (judge == SkipJudge.Good) return 0.5f;
        return 0f;
    }

    /// <summary>
    /// 벡터에서 수직 성분을 제거한다.
    /// v를 사용하며, y가 0인 벡터를 반환한다.
    /// </summary>
    private static Vector3 Horizontal(Vector3 v)
    {
        return new Vector3(v.x, 0f, v.z);
    }
}
