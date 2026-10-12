using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

using Unity.Cinemachine;

// 하늘 너머에 흐릿하게 떠 있는 거대 ALKAGI. 카메라를 수평으로 따라다녀 항상 같은 방향 먼 하늘에 보이고 가까워지지 않는다.
// 물고기 ALKAGI를 얻는 수단이다. 나머지 도감을 모두 모았고 물고기 ALKAGI는 아직 얻지 못했을 때만 나타나며, 판을 시작할 때마다 다시 판단한다.
// 던진 동안 주기적으로 눈을 충전해 레이저를 예고한 뒤 구름과 바다를 가르며, 레이저에 닿으면 물고기 ALKAGI를 획득하고 사라진다.
public class SkyAlkagi : MonoBehaviour
{
    private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");
    private static readonly int EMISSION_ID = Shader.PropertyToID("_Emission");
    private static readonly int WATER_Y_ID = Shader.PropertyToID("_WaterY");
    private static readonly int SIZE_ID = Shader.PropertyToID("_Size");
    private static readonly int INTENSITY_ID = Shader.PropertyToID("_Intensity");

    private enum AttackPhase { Idle, Charging, Firing, Cooling }

    [Header("참조")]
    [Tooltip("하늘에 띄울 외형의 원본. 메시 계층만 복제하고 스크립트와 충돌체는 가져오지 않는다")]
    [SerializeField] private GameObject _sourcePrefab;
    [Tooltip("원본에서 눈으로 취급할 머티리얼")]
    [SerializeField] private Material _eyeSourceMaterial;
    [SerializeField] private Material _bodyMaterial;
    [SerializeField] private Material _eyeMaterial;
    [SerializeField] private Material _glowMaterial;
    [SerializeField] private Mesh _glowMesh;
    [SerializeField] private Fish _fishPrefab;
    [SerializeField] private Transform _giant;
    [Tooltip("레이저가 나가는 기준. 매 프레임 앞쪽 눈 위치로 옮겨 조준 방향을 바라본다")]
    [SerializeField] private Transform _aim;
    [SerializeField] private PlacedAlkagiLaser _laser;
    [SerializeField] private AlkagiWaterHole _holePrefab;
    [SerializeField] private SkyAlkagiClouds _clouds;
    [Tooltip("레이저 발사 때 카메라를 흔드는 시네머신 임펄스. 카메라 쪽 CinemachineImpulseListener가 받는다")]
    [SerializeField] private CinemachineImpulseSource _impulse;
    private FishSpawner _spawner;
    private EnvironmentController _environment;
    private PlayerController _player;
    private Transform _eye;
    private Renderer _glow;
    private readonly List<Renderer> _bodyRenderers = new List<Renderer>();
    private readonly List<Renderer> _eyeRenderers = new List<Renderer>();
    private MaterialPropertyBlock _block;
    private bool _present;

    [Header("하늘 배치")]
    [Tooltip("카메라에서 본 수평 방향. 기본값은 북쪽(+Z)")]
    [SerializeField] private Vector3 _direction = Vector3.forward;
    [Tooltip("카메라와의 수평 거리. 구름층 반지름(650)보다 멀고 카메라 Far(1000)보다 가까워야 한다")]
    [SerializeField, Min(1f)] private float _distance = 900f;
    [Tooltip("수면 위 중심 높이. 카메라가 내려다보므로 수평선 위 약 16도 안에 눈이 들어오게 잡는다")]
    [SerializeField] private float _height = 110f;
    [Tooltip("원본 프리팹 대비 배율")]
    [SerializeField, Min(0.01f)] private float _scale = 24f;
    [Tooltip("플레이어 쪽으로 몸을 돌리는 빠르기")]
    [SerializeField, Min(0f)] private float _turnSpeed = 1.5f;

    [Header("눈")]
    [SerializeField, Min(0f)] private float _idleEyeIntensity = 4f;
    [SerializeField, Min(0f)] private float _chargedEyeIntensity = 60f;
    [Tooltip("후광 사각형의 월드 크기(m)")]
    [SerializeField, Min(0f)] private float _idleGlowSize = 60f;
    [SerializeField, Min(0f)] private float _chargedGlowSize = 260f;
    [SerializeField, Min(0f)] private float _idleGlowIntensity = 0.6f;
    [SerializeField, Min(0f)] private float _chargedGlowIntensity = 4f;

    [Header("공격")]
    [Tooltip("던진 뒤 첫 충전까지 기다리는 시간")]
    [SerializeField, Min(0f)] private float _firstAttackDelay = 3f;
    [Tooltip("공격이 끝난 뒤 다음 충전까지 기다리는 시간")]
    [SerializeField, Min(0.01f)] private float _attackInterval = 6f;
    [Tooltip("눈이 빛나며 레이저를 예고하는 시간")]
    [SerializeField, Min(0.01f)] private float _chargeDuration = 2f;
    [Tooltip("레이저가 ALKAGI 발밑에서 플레이어 위 하늘까지 쳐올리는 시간")]
    [SerializeField, Min(0.01f)] private float _sweepDuration = 0.8f;
    [SerializeField, Min(0.01f)] private float _coolDuration = 1.5f;
    [Tooltip("발사 끝에 레이저가 겨누는 플레이어 위 하늘 높이")]
    [SerializeField, Min(0f)] private float _sweepHeight = 400f;
    [Tooltip("빔 길이. 길수록 바다가 멀리까지 베인다")]
    [SerializeField, Min(1f)] private float _beamLength = 1500f;
    [Tooltip("비 오는 날 레이저 발사 중 빔에서 이 반경(m) 안의 빗방울을 없앤다")]
    [SerializeField, Min(0f)] private float _rainClearRadius = 12f;
    private AttackPhase _phase;
    private float _phaseTime;
    private float _cooldown;
    private Vector3 _sweepStart;
    private Vector3 _sweepEnd;
    private Quaternion _holeRotation;

    [Header("물 구멍")]
    [SerializeField] private float _waterSurfaceOffset = 0.02f;
    [SerializeField, Min(1f)] private float _holeDepth = 200f;
    private AlkagiWaterHole _activeHole;
    private readonly List<AlkagiWaterHole> _holes = new List<AlkagiWaterHole>();

    void Awake()
    {
        _spawner = FindFirstObjectByType<FishSpawner>();
        _block = new MaterialPropertyBlock();
        _spawner.OnCleared += HandleCleared;
        _laser.OnWaterContact += HandleWaterContact;
        BuildVisual();
    }

    void Start()
    {
        _player = _spawner.PlayerBody.GetComponent<PlayerController>();
        _environment = FindFirstObjectByType<EnvironmentController>();
        _cooldown = _firstAttackDelay;
        RefreshPresence();
        // 씬을 불러오는 순간 CinemachineBrain이 Start보다 먼저 카메라 갱신 이벤트를 보내므로 참조를 다 잡은 뒤 구독한다.
        CinemachineCore.CameraUpdatedEvent.AddListener(HandleCameraUpdated);
    }

    void Update()
    {
        if (!_present || Time.timeScale == 0f) return;
        if (!_player.IsThrown || _player.IsGameOver)
        {
            if (_phase != AttackPhase.Idle) ResetAttack();
            _cooldown = _firstAttackDelay;
            return;
        }

        float dt = Time.deltaTime;
        _phaseTime += dt;
        switch (_phase)
        {
            case AttackPhase.Idle:
                _cooldown -= dt;
                if (_cooldown <= 0f) SetPhase(AttackPhase.Charging);
                break;
            case AttackPhase.Charging:
                if (_phaseTime >= _chargeDuration) BeginFire();
                break;
            case AttackPhase.Firing:
                AimAt(Vector3.Lerp(_sweepStart, _sweepEnd, Mathf.SmoothStep(0f, 1f, _phaseTime / _sweepDuration)));
                ClearRainAroundBeam();
                if (CheckLaser() || _phaseTime < _sweepDuration) break;
                EndFire();
                SetPhase(AttackPhase.Cooling);
                break;
            case AttackPhase.Cooling:
                if (_phaseTime < _coolDuration) break;
                SetPhase(AttackPhase.Idle);
                _cooldown = _attackInterval;
                break;
        }
    }

    void OnDisable()
    {
        ResetAttack();
    }

    void OnDestroy()
    {
        CinemachineCore.CameraUpdatedEvent.RemoveListener(HandleCameraUpdated);
        _spawner.OnCleared -= HandleCleared;
        _laser.OnWaterContact -= HandleWaterContact;
        ClearHoles();
    }

    /// <summary>
    /// 원본 프리팹의 메시 계층을 _giant 아래에 복제하고 가장 앞쪽 눈을 레이저 기준으로 정해 후광을 단다.
    /// _sourcePrefab, _scale과 _direction을 사용하며 외형 크기와 방향, 렌더러 목록, _eye와 _glow를 변경한다.
    /// </summary>
    private void BuildVisual()
    {
        _giant.localScale = Vector3.one * _scale;
        _giant.rotation = Quaternion.LookRotation(-HorizontalDirection());
        Transform root = CopyVisual(_sourcePrefab.transform, _giant);
        root.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        float front = float.MinValue;
        foreach (Renderer renderer in _eyeRenderers)
        {
            float forward = _giant.InverseTransformPoint(renderer.transform.position).z;
            if (forward <= front) continue;
            front = forward;
            _eye = renderer.transform;
        }
        _glow = CreateGlow(_eye.position);
    }

    /// <summary>
    /// source와 그 자식의 위치, 회전, 크기와 메시만 parent 아래에 복제한다.
    /// 원본 머티리얼이 _eyeSourceMaterial이면 눈, 아니면 몸체 머티리얼을 입히고 원본 색을 속성 블록으로 옮기며 만든 Transform을 반환한다.
    /// </summary>
    private Transform CopyVisual(Transform source, Transform parent)
    {
        Transform copy = new GameObject(source.name).transform;
        copy.SetParent(parent, false);
        copy.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
        copy.localScale = source.localScale;
        if (source.TryGetComponent(out MeshFilter filter) && source.TryGetComponent(out MeshRenderer sourceRenderer))
        {
            copy.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            MeshRenderer renderer = copy.gameObject.AddComponent<MeshRenderer>();
            bool isEye = sourceRenderer.sharedMaterial == _eyeSourceMaterial;
            renderer.sharedMaterial = isEye ? _eyeMaterial : _bodyMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.GetPropertyBlock(_block);
            _block.SetColor(BASE_COLOR_ID, sourceRenderer.sharedMaterial.GetColor(BASE_COLOR_ID));
            renderer.SetPropertyBlock(_block);
            (isEye ? _eyeRenderers : _bodyRenderers).Add(renderer);
        }
        foreach (Transform child in source)
        {
            CopyVisual(child, copy);
        }
        return copy;
    }

    /// <summary>
    /// position에 항상 카메라를 향하는 가산 후광 사각형을 만들어 _giant 아래에 둔다.
    /// _glowMesh와 _glowMaterial을 사용하며 만든 렌더러를 반환한다.
    /// </summary>
    private Renderer CreateGlow(Vector3 position)
    {
        GameObject glow = new GameObject("EyeGlow");
        glow.transform.SetParent(_giant, false);
        glow.transform.position = position;
        glow.AddComponent<MeshFilter>().sharedMesh = _glowMesh;
        MeshRenderer renderer = glow.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _glowMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    /// <summary>
    /// CinemachineBrain이 카메라를 옮긴 직후 외형을 카메라 기준 하늘 위치로 옮기고 구름층에 현재 상태를 넘긴다.
    /// brain의 카메라 위치, 플레이어 위치와 수면 높이를 사용하며 외형의 위치와 회전, 눈 밝기, 구름층 머티리얼 속성을 변경한다.
    /// </summary>
    private void HandleCameraUpdated(CinemachineBrain brain)
    {
        Vector3 cameraPosition = brain.transform.position;
        if (_present)
        {
            float waterY = _player.WaterContactPoint.y;
            Vector3 position = cameraPosition + HorizontalDirection() * _distance;
            position.y = waterY + _height;
            Quaternion look = Quaternion.LookRotation(_spawner.PlayerBody.position - position);
            _giant.SetPositionAndRotation(position, Quaternion.Slerp(_giant.rotation, look, 1f - Mathf.Exp(-_turnSpeed * Time.deltaTime)));
            ApplyVisual(waterY);
        }
        _clouds.UpdateView(cameraPosition, _giant.position, Charge());
    }

    /// <summary>
    /// 충전 정도에 맞춰 눈 발광과 후광 크기, 세기를 바꾸고 수면 높이를 셰이더에 넘긴다.
    /// waterY와 Charge 값을 사용하며 외형 렌더러와 후광의 속성 블록, 후광 크기를 변경한다.
    /// </summary>
    private void ApplyVisual(float waterY)
    {
        float charge = Charge();
        float eased = charge * charge;
        // 충전이 진행될수록 맥동이 빨라지고 세진다.
        float pulse = 1f + charge * 0.3f * Mathf.Sin(Time.time * Mathf.Lerp(2f, 24f, charge));
        foreach (Renderer renderer in _bodyRenderers)
        {
            ApplyBlock(renderer, waterY, 0f);
        }
        float eyeIntensity = Mathf.Lerp(_idleEyeIntensity, _chargedEyeIntensity, eased) * pulse;
        foreach (Renderer renderer in _eyeRenderers)
        {
            ApplyBlock(renderer, waterY, eyeIntensity);
        }

        float glowSize = Mathf.Lerp(_idleGlowSize, _chargedGlowSize, eased) * pulse;
        // 셰이더가 크기를 직접 정하므로 Transform 크기는 화면 밖 판정용 범위만 맞춘다.
        _glow.transform.localScale = Vector3.one * (glowSize / _scale);
        _glow.GetPropertyBlock(_block);
        _block.SetFloat(SIZE_ID, glowSize);
        _block.SetFloat(INTENSITY_ID, Mathf.Lerp(_idleGlowIntensity, _chargedGlowIntensity, eased) * pulse);
        _glow.SetPropertyBlock(_block);
    }

    /// <summary>
    /// renderer의 기존 속성 블록에 수면 높이 waterY와 발광 세기 emission을 더해 적용한다.
    /// 입력값을 사용하며 renderer의 속성 블록을 변경한다.
    /// </summary>
    private void ApplyBlock(Renderer renderer, float waterY, float emission)
    {
        renderer.GetPropertyBlock(_block);
        _block.SetFloat(WATER_Y_ID, waterY);
        _block.SetFloat(EMISSION_ID, emission);
        renderer.SetPropertyBlock(_block);
    }

    /// <summary>
    /// 현재 공격 단계와 경과 시간으로 눈 충전 정도를 구한다.
    /// _phase와 _phaseTime을 사용하며 0(대기)에서 1(발사)까지의 값을 반환한다.
    /// </summary>
    private float Charge()
    {
        switch (_phase)
        {
            case AttackPhase.Charging: return Mathf.Clamp01(_phaseTime / _chargeDuration);
            case AttackPhase.Firing: return 1f;
            case AttackPhase.Cooling: return 1f - Mathf.Clamp01(_phaseTime / _coolDuration);
            default: return 0f;
        }
    }

    /// <summary>
    /// _direction의 수평 성분을 단위 벡터로 반환한다.
    /// _direction을 사용하며 상태는 변경하지 않는다.
    /// </summary>
    private Vector3 HorizontalDirection()
    {
        Vector3 direction = _direction;
        direction.y = 0f;
        return direction.normalized;
    }

    /// <summary>
    /// phase로 공격 단계를 전환하고 단계 경과 시간을 초기화한다.
    /// 입력된 단계로 _phase와 _phaseTime을 변경한다.
    /// </summary>
    private void SetPhase(AttackPhase phase)
    {
        _phase = phase;
        _phaseTime = 0f;
    }

    /// <summary>
    /// 눈 바로 아래 수면에서 플레이어 위 하늘까지 이어지는 조준선을 월드에 고정하고 레이저를 켠다.
    /// 눈과 플레이어 수면 위치를 사용하며 조준선, 물 구멍 방향, 구름 걷힘, 빔 길이와 공격 단계, 빔 주변 빗방울을 변경하고 카메라 흔들림을 보낸다.
    /// </summary>
    private void BeginFire()
    {
        // ALKAGI 발밑에서 플레이어 쪽으로 한 번 쳐올려 그 사이 바다를 모두 가른다.
        // 조준선을 월드에 고정해 옆으로 비켜 난 플레이어는 피할 수 있게 한다.
        Vector3 player = _player.WaterContactPoint;
        Vector3 below = new Vector3(_eye.position.x, player.y, _eye.position.z);
        Vector3 toPlayer = (player - below).normalized;
        // 바로 아래를 겨누면 회전 기준이 정해지지 않으므로 플레이어 쪽으로 조금 비켜 겨눈다.
        _sweepStart = below + toPlayer;
        _sweepEnd = player + Vector3.up * _sweepHeight;
        _holeRotation = Quaternion.LookRotation(toPlayer);
        _clouds.BeginCut();
        AimAt(_sweepStart);
        ClearRainAroundBeam();
        _laser.BeginFire(_beamLength);
        _impulse.GenerateImpulse();
        SetPhase(AttackPhase.Firing);
        CheckLaser();
    }

    /// <summary>
    /// 앞쪽 눈 위치에서 target 지점을 바라보도록 레이저 기준을 옮긴다.
    /// target과 _eye 위치를 사용하며 _aim의 위치와 회전을 변경한다.
    /// </summary>
    private void AimAt(Vector3 target)
    {
        _aim.SetPositionAndRotation(_eye.position, Quaternion.LookRotation(target - _eye.position));
    }

    /// <summary>
    /// 비 오는 날 현재 빔 주변의 빗방울을 없애 레이저가 지나간 자리에 비가 비게 한다.
    /// _aim의 위치와 방향, _beamLength와 _rainClearRadius를 사용하며 환경의 빗방울을 변경한다.
    /// </summary>
    private void ClearRainAroundBeam()
    {
        _environment.ClearRain(_aim.position, _aim.position + _aim.forward * _beamLength, _rainClearRadius);
    }

    /// <summary>
    /// 빔이 플레이어 몸에 닿았는지 검사하고 닿으면 포획 처리한다.
    /// 플레이어 몸과 수면 높이를 사용하며 포획했으면 true를 반환한다.
    /// </summary>
    private bool CheckLaser()
    {
        if (!_laser.Sweep(_spawner.PlayerBody, _player.WaterContactPoint.y)) return false;
        Capture();
        return true;
    }

    /// <summary>
    /// 물고기 ALKAGI를 획득시키고 거대 외형을 즉시 숨긴 뒤 주변 구름을 걷는다.
    /// _fishPrefab과 플레이어 위치를 사용하며 도감 등록, 레이저와 물 구멍, 공격 단계, 외형 활성 상태를 변경한다.
    /// </summary>
    private void Capture()
    {
        _present = false;
        _spawner.HandlePlacedFishCaught(_fishPrefab.Type, _spawner.PlayerBody.position);
        EndFire();
        SetPhase(AttackPhase.Idle);
        _giant.gameObject.SetActive(false);
        _clouds.FadeOut();
    }

    /// <summary>
    /// 수면 접촉점 point에 이번 공격의 물 구멍을 만들고 레이저가 지나간 쪽으로 늘린다.
    /// 빔 너비와 깊이, 수면 오프셋과 _holeRotation을 사용하며 활성 구멍과 정리 목록을 변경한다.
    /// </summary>
    private void HandleWaterContact(Vector3 point)
    {
        point.y += _waterSurfaceOffset;
        if (_activeHole == null)
        {
            _activeHole = Instantiate(_holePrefab, point, _holeRotation);
            _activeHole.Initialize(point, _laser.Width, _holeDepth);
            _holes.RemoveAll(hole => hole == null);
            _holes.Add(_activeHole);
        }
        _activeHole.ExtendTo(point);
    }

    /// <summary>
    /// 빔을 끄고 이번 공격의 물 구멍에 축소를 요청한다.
    /// 입력값 없이 레이저 표시와 활성 구멍 참조를 변경한다.
    /// </summary>
    private void EndFire()
    {
        _laser.EndFire();
        if (_activeHole == null) return;
        _activeHole.BeginClosing();
        _activeHole = null;
    }

    /// <summary>
    /// 진행 중인 공격을 종료하고 대기 단계로 되돌린다.
    /// 입력값 없이 공격 단계와 다음 충전까지의 대기 시간을 초기화한다.
    /// </summary>
    private void ResetAttack()
    {
        EndFire();
        SetPhase(AttackPhase.Idle);
        _cooldown = _attackInterval;
    }

    /// <summary>
    /// 재시작 시 남은 물 구멍을 제거하고 갈라진 구름을 바로 덮으며 공격을 처음 상태로 되돌린 뒤 이번 판 등장 여부를 다시 정한다.
    /// 입력값 없이 물 구멍 목록, 구름 갈라짐, 공격 단계, 첫 충전 대기 시간과 등장 상태를 변경한다.
    /// </summary>
    private void HandleCleared()
    {
        ClearHoles();
        ResetAttack();
        _clouds.ResetCut();
        _cooldown = _firstAttackDelay;
        RefreshPresence();
    }

    /// <summary>
    /// 나머지 도감을 모두 모았고 물고기 ALKAGI는 아직 얻지 못했을 때만 하늘에 나타나게 한다.
    /// 도감 등록 상태를 사용하며 _present, 외형 활성 상태와 주변 구름을 변경한다. 외형이 꺼지면 락온 대상에서도 빠진다.
    /// </summary>
    private void RefreshPresence()
    {
        _present = _spawner.HasCompletedCatalog() && !_spawner.Progress.IsFishRegistered(_fishPrefab.Type);
        _giant.gameObject.SetActive(_present);
        if (_present) _clouds.Show();
        else _clouds.Hide();
    }

    /// <summary>
    /// 이 ALKAGI가 만든 남아 있는 물 구멍을 제거한다.
    /// 저장 목록을 사용하며 구멍 오브젝트와 목록을 정리한다.
    /// </summary>
    private void ClearHoles()
    {
        foreach (AlkagiWaterHole hole in _holes)
        {
            if (hole != null) Destroy(hole.gameObject);
        }
        _holes.Clear();
        _activeHole = null;
    }
}
