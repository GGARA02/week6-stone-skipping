using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Serialization;

using Newtonsoft.Json.Linq;

// 던진 물고기가 날아가는 동안, 곧 지나갈 자리 근처 수면에서 물고기가 튀어 오르게 한다.
// 던진 물고기로 맞추면 돈을 받고 물고기를 도감에 등록한다.
public class FishSpawner : MonoBehaviour
{
    [Header("참조")]
    [SerializeField]
    private PlayerController _player;
    [SerializeField]
    private PlayerProgress _progress;
    [SerializeField]
    private SkipEffect _effect;
    [SerializeField]
    private GameObject _water;
    [Tooltip("비와 밤 출현 조건을 확인할 때 쓴다")]
    [SerializeField]
    private EnvironmentController _environment;
    private Rigidbody _playerBody;
    private float _waterY;
    // 하늘에서 떨어지는 종류가 바라볼 메인 카메라. 컷어웨이로 메인 카메라가 꺼져도 쓰도록 시작할 때 저장한다.
    private Transform _cameraTransform;
    // 플레이어가 지금 고른 던질 거리 프리팹. 기본 물고기면 null이다.
    private Fish _playerFish;

    [Header("물고기 종류")]
    [Tooltip("출현하는 물고기 프리팹. 이 순서대로 던질 거리 선택 목록에 나온다")]
    [SerializeField]
    private Fish[] _fishPrefabs;
    // 던질 거리 선택 목록. 처음에는 _fishPrefabs와 같고, ReplaceSelection으로 자리만 바뀐다.
    private Fish[] _selectablePrefabs;
    private FishType[] _fishTypes;
    private Fish _wallFish;
    private Fish _iceFish;
    private ScreenFishCapture _screenCapture;
    private ScreenFishCapture _catalogCapture;
    private Mesh _wallCatalogMesh;
    private Mesh _wallSpawnMesh;
    private Mesh _iceCatalogMesh;
    private ProceduralObstacleSpawner _obstacles;
    private SeaSpawnRegistry _seaSpawnRegistry;
    private FishUnlockGraph _unlockGraph;
    private readonly Dictionary<string, float> _spawnWeights = new Dictionary<string, float>();
    private readonly Dictionary<string, Mesh> _seaSpawnMeshes = new Dictionary<string, Mesh>();
    private readonly Dictionary<string, Mesh> _pendingSeaSpawnMeshes = new Dictionary<string, Mesh>();

    [Header("출현")]
    [Tooltip("물고기가 튀어 오르는 간격 범위(초)")]
    [SerializeField]
    private Vector2 _spawnIntervalRange = new Vector2(0.35f, 0.9f);
    [Tooltip("한 번에 튀어 오르는 마릿수 범위")]
    [SerializeField]
    private Vector2Int _schoolSizeRange = new Vector2Int(1, 3);
    [Tooltip("몇 초 뒤 던진 물고기가 지나갈 자리에서 물고기가 최고점에 오를지")]
    [SerializeField]
    private Vector2 _leadTimeRange = new Vector2(1f, 2f);
    [Tooltip("던진 물고기 진행 경로에서 옆으로 벗어나는 최대 거리. A/D로 맞추러 갈 수 있는 정도")]
    [SerializeField]
    private float _maxSideOffset = 9f;
    [SerializeField]
    private Vector2 _jumpHeightRange = new Vector2(3f, 8f);
    [Tooltip("33Fisher가 바다에서 솟아오르는 높이 배율")]
    [Min(1f)]
    [SerializeField]
    private float _fisherJumpHeightMultiplier = 2f;
    [Tooltip("물고기가 던진 물고기 경로를 가로지르는 속도 범위")]
    [SerializeField]
    private Vector2 _crossSpeedRange = new Vector2(2f, 5f);
    [SerializeField]
    private int _maxFish = 16;
    [Tooltip("던진 물고기가 이보다 느리면 물고기가 나오지 않는다")]
    [FormerlySerializedAs("_minStoneSpeed")]
    [SerializeField]
    private float _minFishSpeed = 10f;
    [SerializeField]
    private Color _catchFlashColor = new Color(1f, 0.85f, 0.3f, 0.3f);
    private readonly List<Fish> _activeFish = new List<Fish>();
    private readonly List<Fish> _cutPieces = new List<Fish>();
    private float _nextSpawnTime;
    public event Action<FishType> OnFishCaught;
    public event Action OnClearing;
    public event Action OnCleared;

    [Header("하늘 출현")]
    [Tooltip("하늘에서 떨어지는 종류가 처음 나타나는 수면 위 높이")]
    [SerializeField]
    private float _skyFallHeight = 30f;
    [Tooltip("하늘에서 떨어지는 종류의 낙하 속도")]
    [SerializeField]
    private float _skyFallSpeed = 8f;

    [Header("물결")]
    [Tooltip("물고기가 튀어 오르거나 물에 들어갈 때 물결 세기 배율")]
    [SerializeField]
    private float _fishRippleScale = 2.5f;

    public Rigidbody PlayerBody => _playerBody;
    public float WaterY => _waterY;
    public IReadOnlyList<Fish> FishPrefabs => _selectablePrefabs;
    public IReadOnlyList<FishType> FishTypes => _fishTypes;
    public Fish WallFish => _wallFish;
    public Fish IceFish => _iceFish;
    public ScreenFishCapture ScreenCapture => _screenCapture;
    public ScreenFishCapture CatalogCapture => _catalogCapture;
    public PlayerProgress Progress => _progress;

    /// <summary>
    /// type의 도감 상태를 그래프 해금, 습득 기록과 이번 판의 바다 출현 등록으로 반환한다.
    /// 사출 후 다음 판에 바다 출현이 허용된 특수 종류도 발견 상태로 표시하며 습득 기록은 변경하지 않는다.
    /// </summary>
    public FishCatalogState GetCatalogState(FishType type)
    {
        FishCatalogState state = _unlockGraph.GetCatalogState(type);
        if (state == FishCatalogState.Undiscovered && _seaSpawnRegistry.RequiresRegistration(type.Id)
            && _seaSpawnRegistry.IsUnlocked(type.Id))
            return FishCatalogState.Discovered;
        return state;
    }

    void Awake()
    {
        JObject catalogEntries = JObject.Parse(Resources.Load<TextAsset>("FishCatalog").text);
        _seaSpawnRegistry = GetComponent<SeaSpawnRegistry>();
        if (_seaSpawnRegistry == null) _seaSpawnRegistry = gameObject.AddComponent<SeaSpawnRegistry>();
        _seaSpawnRegistry.Initialize(catalogEntries);
        foreach (JProperty entry in catalogEntries.Properties())
        {
            _spawnWeights.Add(entry.Name, entry.Value.Value<float>("spawnWeight"));
        }
        _playerBody = _player.GetComponent<Rigidbody>();
        _waterY = _water.GetComponent<Collider>().bounds.max.y;
        _cameraTransform = Camera.main.transform;
        InitializeWalls();
        _screenCapture = GetComponent<ScreenFishCapture>();
        if (_screenCapture == null) _screenCapture = gameObject.AddComponent<ScreenFishCapture>();
        Fish screenFish = _screenCapture.Initialize(_progress, FishType.CreateScreen());
        _catalogCapture = gameObject.AddComponent<ScreenFishCapture>();
        Fish catalogFish = _catalogCapture.Initialize(_progress, FishType.CreateCatalog());
        int count = _fishPrefabs.Length;
        Array.Resize(ref _fishPrefabs, count + 2);
        _fishPrefabs[count] = screenFish;
        _fishPrefabs[count + 1] = catalogFish;
        RegisterEjectedPrefabs();
        _selectablePrefabs = (Fish[])_fishPrefabs.Clone();
        _fishTypes = new FishType[_selectablePrefabs.Length];
        for (int i = 0; i < _selectablePrefabs.Length; i++)
        {
            _fishTypes[i] = _selectablePrefabs[i].Type;
        }
        _unlockGraph = new FishUnlockGraph(_progress);
        RegisterCatalogPrefabs();
        _unlockGraph.RestoreUnlocks(_fishPrefabs);
    }

    void OnDestroy()
    {
        if (_wallFish != null) Destroy(_wallFish.gameObject);
        if (_wallCatalogMesh != null) Destroy(_wallCatalogMesh);
        if (_iceCatalogMesh != null) Destroy(_iceCatalogMesh);
        foreach (Mesh mesh in _seaSpawnMeshes.Values) Destroy(mesh);
        foreach (Mesh mesh in _pendingSeaSpawnMeshes.Values) Destroy(mesh);
    }

    /// <summary>
    /// Wall 프리팹과 저장 메시로 벽과 유빙의 선택 항목 및 주변 생성기를 준비한다.
    /// 비활성 템플릿을 선택 목록에 추가하고 고정 배치 대신 풀 기반 생성과 절단을 연결한다.
    /// </summary>
    private void InitializeWalls()
    {
        MeshFilter wall = Resources.Load<GameObject>("Prefabs/Wall").GetComponent<MeshFilter>();
        _wallSpawnMesh = wall.sharedMesh;

        GameObject template = new GameObject("Wall Fish Catalog");
        template.SetActive(false);
        template.transform.SetParent(transform, false);
        MeshFilter filter = template.AddComponent<MeshFilter>();
        _wallCatalogMesh = _progress.LoadWallMesh();
        filter.sharedMesh = _wallCatalogMesh != null ? _wallCatalogMesh : wall.sharedMesh;
        template.AddComponent<MeshRenderer>().sharedMaterials = wall.GetComponent<MeshRenderer>().sharedMaterials;
        template.AddComponent<Rigidbody>().isKinematic = true;
        _wallFish = template.AddComponent<Fish>();
        _wallFish.InitializeWall(this, FishType.CreateWall(), null);
        Mesh wallSeaMesh = _progress.LoadSeaMesh(_wallFish.Type);
        if (wallSeaMesh != null) _seaSpawnMeshes.Add(_wallFish.Type.Id, wallSeaMesh);
        int count = _fishPrefabs.Length;
        Array.Resize(ref _fishPrefabs, count + 1);
        _fishPrefabs[count] = _wallFish;
        _obstacles = GetComponent<ProceduralObstacleSpawner>();
        if (_obstacles == null) _obstacles = gameObject.AddComponent<ProceduralObstacleSpawner>();
        FishType iceType = FishType.CreateIce();
        _obstacles.Initialize(this, _player, _waterY, _water.transform, wall, iceType);
        GameObject iceTemplate = new GameObject("Ice Floe Fish Catalog");
        iceTemplate.SetActive(false);
        iceTemplate.transform.SetParent(transform, false);
        _iceCatalogMesh = _progress.LoadIceMesh();
        iceTemplate.AddComponent<MeshFilter>().sharedMesh = _iceCatalogMesh != null ? _iceCatalogMesh : _obstacles.IceMesh;
        iceTemplate.AddComponent<MeshRenderer>().sharedMaterial = _obstacles.IceMaterial;
        iceTemplate.AddComponent<Rigidbody>().isKinematic = true;
        _iceFish = iceTemplate.AddComponent<Fish>();
        _iceFish.InitializeWall(this, iceType, null);
        Mesh iceSeaMesh = _progress.LoadSeaMesh(iceType);
        if (iceSeaMesh != null) _seaSpawnMeshes.Add(iceType.Id, iceSeaMesh);
        Array.Resize(ref _fishPrefabs, _fishPrefabs.Length + 1);
        _fishPrefabs[_fishPrefabs.Length - 1] = _iceFish;
    }

    /// <summary>
    /// 주변 생성기를 사용해 모든 벽과 유빙을 풀로 반환한다.
    /// 입력값 없이 이전 조각을 제거하며 도감의 메시와 해금 상태는 유지한다.
    /// </summary>
    public void ResetWalls()
    {
        if (_obstacles != null) _obstacles.ResetObstacles();
    }

    void Update()
    {
        if (!_player.IsThrown || _player.IsGameOver || Time.time < _nextSpawnTime) return;

        _nextSpawnTime = Time.time + UnityEngine.Random.Range(_spawnIntervalRange.x, _spawnIntervalRange.y);
        if (_player.Speed < _minFishSpeed) return;

        int schoolSize = UnityEngine.Random.Range(_schoolSizeRange.x, _schoolSizeRange.y + 1);
        for (int i = 0; i < schoolSize && _activeFish.Count < _maxFish; i++)
        {
            Spawn();
        }
    }

    /// <summary>
    /// 재시작 시 떠 있는 물고기를 없애고 입수 종류를 다음 판 출현에 반영한 뒤 맵 물고기의 재등장을 알린다.
    /// 입력값은 없으며, 활성 목록과 입수 모양, 출현 가능 종류와 시간을 갱신하고 OnCleared를 보낸다.
    /// </summary>
    public void Clear()
    {
        OnClearing?.Invoke();
        ResetWalls();
        foreach (Fish fish in _activeFish)
        {
            Destroy(fish.gameObject);
        }
        _activeFish.Clear();
        foreach (Fish piece in _cutPieces)
        {
            if (piece == null) continue;
            piece.gameObject.SetActive(false);
            Destroy(piece.gameObject);
        }
        _cutPieces.Clear();
        foreach (KeyValuePair<string, Mesh> entry in _pendingSeaSpawnMeshes)
        {
            if (_seaSpawnMeshes.TryGetValue(entry.Key, out Mesh previous)) Destroy(previous);
            _seaSpawnMeshes[entry.Key] = entry.Value;
        }
        _pendingSeaSpawnMeshes.Clear();
        _seaSpawnRegistry.CommitPending();
        _unlockGraph.RestoreUnlocks(_fishPrefabs);
        _nextSpawnTime = Time.time;
        OnCleared?.Invoke();
    }

    /// <summary>
    /// 물고기가 수면 위로 튀어 오를 때 물보라를 만든다.
    /// point와 _fishRippleScale을 사용하며, 물보라와 물결을 만든다.
    /// </summary>
    public void HandleFishSurfaced(Vector3 point)
    {
        _effect.PlaySplash(point, 0.35f, _fishRippleScale);
    }

    /// <summary>
    /// 물고기가 입수하면 대상 종류의 다음 run 출현과 벽 또는 유빙의 마지막 입수 모양을 예약한다.
    /// fish의 메시와 스케일을 별도로 저장하며 획득 모양은 유지하고 물보라를 만든 뒤 개체를 없앤다.
    /// </summary>
    public void HandleFishLanded(Fish fish)
    {
        if (fish.Type.Id == "wall" || fish.Type.Id == "ice")
        {
            Mesh mesh = _progress.SaveSeaMesh(fish.Type, fish.GetComponent<MeshFilter>().sharedMesh,
                fish.transform.lossyScale, fish.IsCutPiece);
            if (_pendingSeaSpawnMeshes.TryGetValue(fish.Type.Id, out Mesh previous)) Destroy(previous);
            _pendingSeaSpawnMeshes[fish.Type.Id] = mesh;
        }
        _seaSpawnRegistry.RegisterWaterEntry(fish.Type.Id, fish.IsCutPiece, _progress.GetFishCount(fish.Type));
        Vector3 point = fish.transform.position;
        point.y = _waterY;
        _effect.PlaySplash(point, 0.3f, _fishRippleScale);
        Remove(fish);
    }

    /// <summary>
    /// 던진 물고기에 맞은 물고기를 도감에 등록하고 돈을 주며 화면 효과와 포획 이벤트를 보낸다.
    /// fish를 사용하며, 진행 상황(돈, 도감 등록)을 변경하고 _activeFish에서 제거한다.
    /// </summary>
    public void HandleFishCaught(Fish fish)
    {
        if (!CanAcquire(fish.Type))
        {
            fish.gameObject.SetActive(false);
            Remove(fish);
            return;
        }
        if (fish.Type.Id == "wall")
        {
            Mesh previous = _wallCatalogMesh;
            _wallCatalogMesh = _progress.SaveWallMesh(fish.GetComponent<MeshFilter>().sharedMesh, fish.transform.lossyScale);
            _wallFish.GetComponent<MeshFilter>().sharedMesh = _wallCatalogMesh;
            if (previous != null) Destroy(previous);
        }
        else if (fish.Type.Id == "ice")
        {
            Mesh previous = _iceCatalogMesh;
            _iceCatalogMesh = _progress.SaveIceMesh(fish.GetComponent<MeshFilter>().sharedMesh, fish.transform.lossyScale);
            _iceFish.GetComponent<MeshFilter>().sharedMesh = _iceCatalogMesh;
            if (previous != null) Destroy(previous);
        }
        _progress.AddFish(fish.Type);
        _effect.PlaySplash(fish.transform.position, 0.6f);
        _effect.PlayImpact(0.8f, _catchFlashColor, 0.5f);
        OnFishCaught?.Invoke(fish.Type);
        Remove(fish);
        if (fish.Type.StopAfterCatch || _seaSpawnRegistry.RequiresCutToRearm(fish.Type.Id)
            || fish.TryGetComponent<FisherQteController>(out _))
            RemoveActiveFish(fish.Type);
    }

    /// <summary>
    /// 맵에 놓인 물고기에 던진 물고기가 닿으면 도감에 등록하고 돈을 주며 화면 효과와 포획 이벤트를 보낸다.
    /// type과 효과 위치 point를 사용하며, 진행 상황(돈, 도감 등록)을 변경한다. 오브젝트는 없애지 않는다.
    /// </summary>
    public void HandlePlacedFishCaught(FishType type, Vector3 point)
    {
        if (!CanAcquire(type)) return;
        _progress.AddFish(type);
        _effect.PlaySplash(point, 0.6f);
        _effect.PlayImpact(0.8f, _catchFlashColor, 0.5f);
        OnFishCaught?.Invoke(type);
        if (type.StopAfterCatch || _seaSpawnRegistry.RequiresCutToRearm(type.Id))
            RemoveActiveFish(type);
    }

    /// <summary>
    /// 플레이어가 고른 던질 거리를 기억해 특정 물고기를 골랐을 때만 나오는 종류의 출현 조건에 쓴다.
    /// prefab(기본 물고기면 null)을 사용하며, _playerFish를 변경한다.
    /// </summary>
    public void SetPlayerFish(Fish prefab)
    {
        _playerFish = prefab;
    }

    /// <summary>
    /// to가 별도로 등록되지 않은 경우 던질 거리 선택 목록에서 from 자리를 to로 바꾼다.
    /// 두 프리팹을 사용하며 별도 항목이 있으면 유지하고 없으면 선택 목록과 종류를 변경한다.
    /// </summary>
    public void ReplaceSelection(Fish from, Fish to)
    {
        if (Array.IndexOf(_selectablePrefabs, to) >= 0) return;
        int index = Array.IndexOf(_selectablePrefabs, from);
        _selectablePrefabs[index] = to;
        _fishTypes[index] = to.Type;
    }

    /// <summary>
    /// type의 ID로 현재 선택 목록과 원래 출현 목록에서 프리팹을 찾는다.
    /// 교체된 이전 종류도 조회하며 일치하는 Fish를 반환하고 없는 종류는 null을 반환한다.
    /// </summary>
    public Fish GetFishPrefab(FishType type)
    {
        foreach (Fish prefab in _selectablePrefabs)
        {
            if (prefab.Type.Id == type.Id) return prefab;
        }
        foreach (Fish prefab in _fishPrefabs)
        {
            if (prefab.Type.Id == type.Id) return prefab;
        }
        return null;
    }

    /// <summary>
    /// prefab을 종류 ID 기준으로 출현 및 투척 선택 목록에 한 번만 등록한다.
    /// 기존 선택 교체 결과를 유지하며 새 종류의 프리팹과 선택 목록을 확장한다.
    /// </summary>
    public void RegisterPrefab(Fish prefab)
    {
        foreach (Fish registered in _fishPrefabs)
        {
            if (registered.Type.Id == prefab.Type.Id) return;
        }

        Array.Resize(ref _fishPrefabs, _fishPrefabs.Length + 1);
        _fishPrefabs[_fishPrefabs.Length - 1] = prefab;
        Array.Resize(ref _selectablePrefabs, _selectablePrefabs.Length + 1);
        _selectablePrefabs[_selectablePrefabs.Length - 1] = prefab;
        Array.Resize(ref _fishTypes, _fishTypes.Length + 1);
        _fishTypes[_fishTypes.Length - 1] = prefab.Type;
    }

    /// <summary>
    /// 등록된 type의 자연 출현을 다음 판부터 허용하도록 예약한다.
    /// 현재 판의 출현 목록은 유지하고 예약 상태와 PlayerPrefs를 저장한다.
    /// </summary>
    public void QueueSeaUnlock(FishType type)
    {
        _seaSpawnRegistry.Queue(type.Id);
    }

    /// <summary>
    /// 던진 물고기가 몇 초 뒤 지나갈 자리 근처에서 최고점에 오르도록 물고기 프리팹을 만들어 튀어 오르게 한다.
    /// 하늘에서 떨어지는 종류는 DropFromSky로 넘긴다.
    /// 던진 물고기 위치와 속도, 출현 설정을 사용하며, 새 물고기를 _activeFish에 추가한다.
    /// </summary>
    private void Spawn()
    {
        Fish prefab = PickPrefab();
        if (prefab == null) return;
        Vector3 horizontal = _player.Velocity;
        horizontal.y = 0f;
        Vector3 heading = horizontal.normalized;
        Vector3 side = Vector3.Cross(Vector3.up, heading);

        float lead = UnityEngine.Random.Range(_leadTimeRange.x, _leadTimeRange.y);
        float sideOffset = UnityEngine.Random.Range(-_maxSideOffset, _maxSideOffset);
        if (prefab.Type.SpawnFallsFromSky)
        {
            DropFromSky(prefab, horizontal, side * sideOffset);
            return;
        }
        Vector3 apex = _playerBody.position + horizontal * lead + side * sideOffset;
        apex.y = _waterY;

        float height = UnityEngine.Random.Range(_jumpHeightRange.x, _jumpHeightRange.y);
        bool isFisher = prefab.TryGetComponent<FisherQteController>(out _);
        if (isFisher) height *= _fisherJumpHeightMultiplier;
        float gravity = -Physics.gravity.y;
        float upSpeed = Mathf.Sqrt(2f * gravity * height);
        float timeToApex = upSpeed / gravity;

        // 던진 물고기 경로 쪽으로 가로질러 헤엄치게 해서, 옆으로 벗어난 물고기도 경로를 지나가게 한다.
        float crossSign = sideOffset > 0f ? -1f : 1f;
        Vector3 cross = side * (crossSign * UnityEngine.Random.Range(_crossSpeedRange.x, _crossSpeedRange.y));
        if (isFisher) cross = Vector3.zero;
        Vector3 start = apex - cross * timeToApex;
        float delay = Mathf.Max(0f, lead - timeToApex);

        Fish fish = Instantiate(prefab);
        if (prefab == _wallFish || prefab == _iceFish)
        {
            Mesh sourceMesh = _seaSpawnMeshes.TryGetValue(prefab.Type.Id, out Mesh seaMesh)
                ? seaMesh : prefab == _wallFish ? _wallSpawnMesh : _obstacles.IceMesh;
            fish.PrepareMeshSpawn(sourceMesh);
        }
        FishAbility ability = fish.GetComponent<FishAbility>();
        if (ability != null) ability.OnWaterSpawn();
        fish.Launch(this, start, cross + Vector3.up * upSpeed, delay, _waterY);
        fish.gameObject.SetActive(true);
        _activeFish.Add(fish);
    }

    /// <summary>
    /// 던진 물고기 높이까지 내려왔을 때 그 자리를 지나가도록 앞쪽 하늘에서 프리팹을 떨어뜨린다.
    /// prefab, 던진 물고기 수평 속도 horizontal, 옆 거리 sideOffset, 하늘 출현 설정과 _cameraTransform을 사용하며, 새 개체를 _activeFish에 추가한다.
    /// </summary>
    private void DropFromSky(Fish prefab, Vector3 horizontal, Vector3 sideOffset)
    {
        float startY = _waterY + _skyFallHeight;
        float targetY = Mathf.Max(_playerBody.position.y, _waterY);
        float fallTime = (startY - targetY) / _skyFallSpeed;
        Vector3 start = _playerBody.position + horizontal * fallTime + sideOffset;
        start.y = startY;

        Fish fish = Instantiate(prefab);
        FishAbility ability = fish.GetComponent<FishAbility>();
        if (ability != null) ability.OnSkyFall();
        fish.Drop(this, start, _skyFallSpeed, _waterY, _cameraTransform);
        _activeFish.Add(fish);
    }

    /// <summary>
    /// 출현 가중치에 따라 물고기 프리팹을 고른다.
    /// 종류별 출현 정책과 FishCatalog의 spawnWeight를 사용하며, 고른 프리팹 또는 후보가 없으면 null을 반환한다.
    /// </summary>
    private Fish PickPrefab()
    {
        float total = 0f;
        foreach (Fish prefab in _fishPrefabs)
        {
            if (CanSpawn(prefab.Type)) total += Mathf.Max(0f, _spawnWeights[prefab.Type.Id]);
        }

        if (total <= 0f) return null;
        float pick = UnityEngine.Random.Range(0f, total);
        foreach (Fish prefab in _fishPrefabs)
        {
            if (!CanSpawn(prefab.Type)) continue;
            float weight = _spawnWeights[prefab.Type.Id];
            if (weight <= 0f) continue;
            pick -= weight;
            if (pick < 0f) return prefab;
        }
        return _fishPrefabs[0];
    }

    /// <summary>
    /// 사출 능력에 연결된 프리팹을 종류별로 한 번씩 선택 목록에 추가한다.
    /// _fishPrefabs의 사출 참조를 사용하며, 연결된 2차 프리팹까지 목록을 확장한다.
    /// </summary>
    private void RegisterEjectedPrefabs()
    {
        List<Fish> prefabs = new List<Fish>(_fishPrefabs);
        HashSet<string> ids = new HashSet<string>();
        foreach (Fish prefab in prefabs) ids.Add(prefab.Type.Id);
        for (int i = 0; i < prefabs.Count; i++)
        {
            foreach (EjectFishAbility ability in prefabs[i].GetComponents<EjectFishAbility>())
            {
                Fish childPrefab = ability.FishPrefab;
                if (ids.Add(childPrefab.Type.Id)) prefabs.Add(childPrefab);
            }
        }
        _fishPrefabs = prefabs.ToArray();
    }

    /// <summary>
    /// JSON 노드에 해당하는 Fish 폴더의 투척용 프리팹을 종류별로 등록한다.
    /// Resources의 투척용 자산을 사용하며 씬에 등록한 기존 프리팹은 우선 유지한다.
    /// </summary>
    private void RegisterCatalogPrefabs()
    {
        foreach (GameObject asset in Resources.LoadAll<GameObject>("Prefabs/Fish"))
        {
            if (asset.TryGetComponent(out Fish fish) && _unlockGraph.ContainsFish(fish.Type.Id)) RegisterPrefab(fish);
        }
    }

    /// <summary>
    /// JSON 해금 그래프와 바다 등록, 날씨, 사용 물고기 및 개체 제한을 확인한다.
    /// type과 현재 게임 상태를 사용하며 모든 출현 조건을 만족하는지 반환한다.
    /// </summary>
    private bool CanSpawn(FishType type)
    {
        if (type.Id == "arrow") return false;
        if (!_unlockGraph.ContainsFish(type.Id)) return false;
        if (_seaSpawnRegistry.RequiresRegistration(type.Id))
        {
            if (!_seaSpawnRegistry.IsUnlocked(type.Id)) return false;
        }
        else if (!_unlockGraph.AllowsNaturalSpawn(type)) return false;
        if (type.Id == "ice" && _player.Distance <= _obstacles.IceStartDistance) return false;
        if (_obstacles != null && type.Id == "blackhole") return false;
        if (type.SpawnOnlyInRain && (_environment == null || !_environment.IsRaining)) return false;
        if (type.SpawnOnlyAtNight && (_environment == null || !_environment.IsNight)) return false;
        if (type.RequiredPlayerFish != null
            && (_playerFish == null || _playerFish.gameObject != type.RequiredPlayerFish)) return false;
        return CanCreate(type);
    }

    /// <summary>
    /// 획득 후 중단 설정과 종류별 활성 개체 수를 확인한다.
    /// type을 사용하며, 사출 또는 출수 개체를 추가할 수 있는지 반환한다.
    /// </summary>
    private bool CanCreate(FishType type)
    {
        if (!CanAcquire(type)) return false;
        if (type.MaxConcurrent <= 0) return true;
        int count = 0;
        foreach (Fish fish in _activeFish)
        {
            if (fish.Type.Id == type.Id) count++;
        }
        return count < type.MaxConcurrent;
    }

    /// <summary>
    /// type의 누적 획득 수로 영구 중단과 절단 입수당 한 번 획득 정책을 확인한다.
    /// 이번 run의 등록 한도를 사용하며 자연 출현과 사출 또는 포획을 허용할지 반환한다.
    /// </summary>
    private bool CanAcquire(FishType type)
    {
        int count = _progress.GetFishCount(type);
        return (!type.StopAfterCatch || count == 0) && _seaSpawnRegistry.AllowsCapture(type.Id, count);
    }

    /// <summary>
    /// type과 같은 종류의 활성 개체를 포획 후 즉시 비활성화하고 모두 제거한다.
    /// 활성 목록을 뒤에서 순회하며 중복 포획 및 이전 개체의 재입수를 막는다.
    /// </summary>
    private void RemoveActiveFish(FishType type)
    {
        for (int i = _activeFish.Count - 1; i >= 0; i--)
        {
            Fish fish = _activeFish[i];
            if (fish.Type.Id != type.Id) continue;
            fish.gameObject.SetActive(false);
            Remove(fish);
        }
    }

    /// <summary>
    /// 종류별 제한을 만족하면 지정한 위치와 크기로 독립 물고기를 사출한다.
    /// prefab, 위치, 회전, 배율과 velocity를 사용하며, 활성 목록에 추가하고 성공 여부를 반환한다.
    /// </summary>
    public bool TryEject(Fish prefab, Vector3 position, Quaternion rotation, Vector3 scale, Vector3 velocity)
    {
        FishType type = prefab.Type;
        if (!CanCreate(type)) return false;
        if (!type.AllowRepeatEjection)
        {
            if (_seaSpawnRegistry.HasRegistration(type.Id) || _progress.IsFishRegistered(type)) return false;
            foreach (Fish active in _activeFish)
            {
                if (active.Type.Id == type.Id && active.IsEjected) return false;
            }
        }

        Fish fish = Instantiate(prefab, position, rotation);
        fish.transform.localScale = scale;
        FishAbility ability = fish.GetComponent<FishAbility>();
        if (ability != null) velocity = ability.OnEjected(velocity);
        fish.Eject(this, position, velocity, _waterY);
        _activeFish.Add(fish);
        return true;
    }

    /// <summary>
    /// 물고기를 목록에서 빼고 오브젝트를 없앤다.
    /// fish를 사용하며, _activeFish를 변경한다.
    /// </summary>
    private void Remove(Fish fish)
    {
        _activeFish.Remove(fish);
        _cutPieces.Remove(fish);
        Destroy(fish.gameObject);
    }

    /// <summary>
    /// fish를 원본 장애물과 독립된 절단 조각 목록에 등록한다.
    /// 입수까지 낙하를 유지하고 run 종료 시 정리하며 자연 스폰 개체 수에는 포함하지 않는다.
    /// </summary>
    public void RegisterCutPiece(Fish fish)
    {
        _cutPieces.Add(fish);
    }
}
