using UnityEngine;

// 물에 닿을 때 물보라와 물결을 만들고, 튕길 때 카메라 흔들림과 화면 번쩍임을 준다.
// 물결은 WaterV2 셰이더가 전역 배열 _SkipRippleData로 읽는다.
public class SkipEffect : MonoBehaviour
{
    private const int RIPPLE_COUNT = 16;
    private const float INACTIVE_RIPPLE_TIME = -1000f;

    private static readonly int RIPPLES_PROPERTY_ID = Shader.PropertyToID("_SkipRippleData");

    [Header("참조")]
    [SerializeField]
    private PlayerController _player;
    [SerializeField]
    private CameraController _cameraController;
    [SerializeField]
    private Rumble _rumble;
    [SerializeField]
    private Material _splashMaterial;

    [Header("물보라")]
    [SerializeField]
    private int _splashCount = 45;
    [SerializeField]
    private Vector2 _splashSpeedRange = new Vector2(3f, 11f);
    [Tooltip("이 착수 속도(m/s)에서 물보라가 가장 크다")]
    [SerializeField]
    private float _splashFullSpeed = 50f;
    private ParticleSystem _splash;

    [Header("물결")]
    private readonly Vector4[] _ripples = new Vector4[RIPPLE_COUNT];
    private int _rippleIndex;

    [Header("물살 (수면을 쓸고 갈 때)")]
    [Tooltip("물에 닿아 있는 동안 지나간 자리에 물결을 남기는 간격(초)")]
    [SerializeField]
    private float _wakeInterval = 0.07f;
    [SerializeField]
    private float _wakeRippleStrength = 0.35f;
    [Tooltip("전속력일 때 초당 물줄기 파티클 수")]
    [SerializeField]
    private float _wakeSprayRate = 160f;
    [Tooltip("물줄기가 옆으로 퍼지는 속도")]
    [SerializeField]
    private float _wakeSideSpeed = 4f;
    private float _nextWakeTime;
    private float _sprayAccumulator;

    [Header("화면 번쩍임")]
    [SerializeField]
    private float _flashDuration = 0.18f;
    [SerializeField]
    private Color _skipFlashColor = new Color(0.8f, 0.95f, 1f, 0.22f);
    [SerializeField]
    private Color _perfectFlashColor = new Color(1f, 0.85f, 0.3f, 0.32f);
    [SerializeField]
    private Color _obstacleFlashColor = new Color(1f, 0.35f, 0.25f, 0.3f);
    [Tooltip("이 충돌 속도(m/s)에서 벽·기둥 충돌 효과가 가장 세다")]
    [SerializeField]
    private float _obstacleFullSpeed = 30f;
    private Color _flashColor;
    private float _flashTime = -10f;

    void Awake()
    {
        _splash = CreateSplash();
        for (int i = 0; i < RIPPLE_COUNT; i++)
        {
            _ripples[i] = new Vector4(0f, 0f, INACTIVE_RIPPLE_TIME, 0f);
        }
        // 첫 설정 때 배열 크기가 고정되므로 처음부터 RIPPLE_COUNT 길이로 넘긴다.
        Shader.SetGlobalVectorArray(RIPPLES_PROPERTY_ID, _ripples);
    }

    void Start()
    {
        _player.OnWaterContact += HandleWaterContact;
        _player.OnSkip += HandleSkip;
        _player.OnObstacleHit += HandleObstacleHit;
        _player.OnJudge += HandleJudge;
        _player.OnSlidePush += HandleSlidePush;
    }

    void OnDestroy()
    {
        _player.OnWaterContact -= HandleWaterContact;
        _player.OnSkip -= HandleSkip;
        _player.OnObstacleHit -= HandleObstacleHit;
        _player.OnJudge -= HandleJudge;
        _player.OnSlidePush -= HandleSlidePush;
    }

    void Update()
    {
        _rumble.SetContinuous(_player.isActiveAndEnabled && _player.IsThrown && _player.IsSliding
            && !_player.IsGameOver && Time.timeScale > 0f
            ? Mathf.Lerp(0.12f, 0.3f, Mathf.Clamp01(_player.Speed / _splashFullSpeed)) : 0f);
        if (!_player.InContact)
        {
            _sprayAccumulator = 0f;
            return;
        }

        float power = Mathf.Clamp01(_player.Speed / Mathf.Max(_splashFullSpeed, 0.01f));
        Vector3 point = _player.WaterContactPoint;

        _sprayAccumulator += _wakeSprayRate * power * Time.deltaTime;
        int count = Mathf.FloorToInt(_sprayAccumulator);
        if (count > 0)
        {
            _sprayAccumulator -= count;
            EmitWakeSpray(point, count, power);
        }

        if (Time.time >= _nextWakeTime)
        {
            _nextWakeTime = Time.time + _wakeInterval;
            AddRipple(point, _wakeRippleStrength * Mathf.Lerp(0.4f, 1f, power));
        }
    }

    void OnGUI()
    {
        float t = (Time.unscaledTime - _flashTime) / Mathf.Max(_flashDuration, 0.01f);
        if (t >= 1f) return;

        Color color = _flashColor;
        color.a *= 1f - t;
        GUI.color = color;
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    /// <summary>
    /// 물보라용 파티클 시스템을 자식으로 만들고 설정한다. 직접 Emit할 때만 파티클이 나온다.
    /// _splashMaterial을 사용하며, 만든 ParticleSystem을 반환한다.
    /// </summary>
    private ParticleSystem CreateSplash()
    {
        GameObject splashObject = new GameObject("SplashParticles");
        splashObject.transform.SetParent(transform, false);
        ParticleSystem particles = splashObject.AddComponent<ParticleSystem>();
        // 추가 직후 자동 재생 중이면 duration 변경이 막히므로 먼저 멈춘다.
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(_splashSpeedRange.x, _splashSpeedRange.y);
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.55f);
        main.startColor = new Color(0.92f, 0.97f, 1f, 0.9f);
        main.gravityModifier = 1.3f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 600;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.8f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = fade;

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.4f));

        ParticleSystemRenderer particleRenderer = splashObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.material = _splashMaterial;
        return particles;
    }

    /// <summary>
    /// 지정한 세기로 물보라와 물결을 만든다. 물고기가 튀어 오르거나 잡힐 때도 쓴다.
    /// point(수면 위 지점), power(0~1), rippleScale(물결 세기 배율)을 사용하며, 파티클을 방출하고 물결 배열을 변경한다.
    /// </summary>
    public void PlaySplash(Vector3 point, float power, float rippleScale = 1f)
    {
        _splash.transform.position = point;
        ParticleSystem.MainModule main = _splash.main;
        main.startSpeedMultiplier = Mathf.Lerp(0.4f, 1f, power);
        _splash.Emit(Mathf.RoundToInt(_splashCount * Mathf.Lerp(0.3f, 1f, power)));
        AddRipple(point, Mathf.Lerp(0.3f, 1f, power) * rippleScale);
    }

    /// <summary>
    /// 착수 지점에 착수 속도에 비례한 물보라와 물결을 만든다.
    /// point(수면 위 착수 지점)와 speed를 사용하며, PlaySplash를 호출한다.
    /// </summary>
    private void HandleWaterContact(Vector3 point, float speed)
    {
        PlaySplash(point, Mathf.Clamp01(speed / Mathf.Max(_splashFullSpeed, 0.01f)));
    }

    /// <summary>
    /// 수면을 쓸고 가는 동안 진행 방향 양옆 뒤로 갈라지는 물줄기를 방출한다.
    /// point, count, power(0~1 속도 비율)와 물고기 속도를 사용하며, 물보라 파티클을 방출한다.
    /// </summary>
    private void EmitWakeSpray(Vector3 point, int count, float power)
    {
        Vector3 velocity = _player.Velocity;
        velocity.y = 0f;
        Vector3 forward = velocity.sqrMagnitude > 0.01f ? velocity.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, forward);

        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            // 배가 물을 가르듯 좌우로 번갈아 갈라지고, 물고기 속도의 일부를 이어받아 앞으로도 흐른다.
            float sideSign = (i & 1) == 0 ? 1f : -1f;
            emitParams.position = point + side * (sideSign * Random.Range(0.3f, 0.9f));
            emitParams.velocity = forward * (velocity.magnitude * Random.Range(0.1f, 0.3f))
                + side * (sideSign * _wakeSideSpeed * Random.Range(0.5f, 1.2f) * power)
                + Vector3.up * Random.Range(1.5f, 4.5f) * power;
            emitParams.startSize = Random.Range(0.12f, 0.32f);
            emitParams.startLifetime = Random.Range(0.3f, 0.7f);
            _splash.Emit(emitParams, 1);
        }
    }

    /// <summary>
    /// 카메라 흔들림, 화면 번쩍임, 게임패드 진동을 시작한다. 튕김, 충돌, 물고기 포획에 쓴다.
    /// strength(0~1), color와 rumbleScale을 사용하며, 화면 효과와 배율이 적용된 진동을 변경한다.
    /// </summary>
    public void PlayImpact(float strength, Color color, float rumbleScale = 1f)
    {
        _cameraController.Punch(strength);
        _rumble.Pulse(strength * rumbleScale, Mathf.Lerp(0.08f, 0.3f, strength));
        _flashColor = color;
        _flashTime = Time.unscaledTime;
    }

    /// <summary>
    /// 자동 튕김은 속도에 비례해 진동하고 GOOD 이상은 화면을 흔들고 번쩍인다.
    /// count, judge와 속도를 사용하며 판정 진동은 중복 재생하지 않고 화면 상태를 변경한다.
    /// </summary>
    private void HandleSkip(int count, SkipJudge judge)
    {
        float strength = Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(_player.Speed / Mathf.Max(_splashFullSpeed, 0.01f)));
        if (judge != SkipJudge.Perfect && judge != SkipJudge.Good)
        {
            if (judge == SkipJudge.None) _rumble.Pulse(strength * 0.5f, Mathf.Lerp(0.08f, 0.3f, strength));
            return;
        }
        _cameraController.Punch(strength);
        _flashColor = judge == SkipJudge.Perfect ? _perfectFlashColor : _skipFlashColor;
        _flashTime = Time.unscaledTime;
    }

    /// <summary>
    /// judge에 따라 MISS는 약하고 짧게, GOOD은 중간, PERFECT는 강하고 길게 진동한다.
    /// timingError는 사용하지 않으며 패드의 일시 진동 상태를 변경한다.
    /// </summary>
    private void HandleJudge(SkipJudge judge, float timingError)
    {
        switch (judge)
        {
            case SkipJudge.Miss: _rumble.Pulse(0.125f, 0.08f); break;
            case SkipJudge.Good: _rumble.Pulse(0.275f, 0.16f); break;
            case SkipJudge.Perfect: _rumble.Pulse(0.5f, 0.28f); break;
        }
    }

    /// <summary>
    /// 슬라이딩 밀기 입력마다 남은 추진력에 비례해 짧은 진동을 더한다.
    /// 플레이어의 SlidePower를 사용하며 일시 진동 상태를 변경한다.
    /// </summary>
    private void HandleSlidePush()
    {
        _rumble.Pulse(Mathf.Lerp(0.2f, 0.5f, _player.SlidePower), 0.09f);
    }

    /// <summary>
    /// 벽, 기둥, 지형에 부딪히면 충돌 속도에 비례한 세기로 효과를 준다.
    /// impactSpeed를 사용하며, PlayImpact를 호출한다.
    /// </summary>
    private void HandleObstacleHit(float impactSpeed)
    {
        float strength = Mathf.Clamp01(impactSpeed / Mathf.Max(_obstacleFullSpeed, 0.01f));
        if (strength < 0.05f) return;
        PlayImpact(strength, _obstacleFlashColor);
    }

    /// <summary>
    /// 가장 오래된 물결 자리에 새 물결을 넣고 셰이더 전역 배열을 갱신한다.
    /// point와 strength를 사용하며, _ripples와 _rippleIndex를 변경한다.
    /// </summary>
    private void AddRipple(Vector3 point, float strength)
    {
        // 셰이더의 _Time.y와 같은 기준 시간이다.
        _ripples[_rippleIndex] = new Vector4(point.x, point.z, Time.timeSinceLevelLoad, strength);
        _rippleIndex = (_rippleIndex + 1) % RIPPLE_COUNT;
        Shader.SetGlobalVectorArray(RIPPLES_PROPERTY_ID, _ripples);
    }
}
