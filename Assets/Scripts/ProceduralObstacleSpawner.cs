using System.Collections.Generic;

using UnityEngine;

public sealed class ProceduralObstacleSpawner : MonoBehaviour
{
    private const float BOOST_FREQUENCY_RATIO = 1f / 40f;
    private const float MAX_SPAWN_Z = 3000f;

    [Header("거리 구역")]
    [Min(0f)][SerializeField] private float _iceStartDistance = 1500f;
    [Min(0f)][SerializeField] private float _blackHoleStartDistance = 3000f;

    [Header("플레이어 주변 생성")]
    [Min(20f)][SerializeField] private float _cellSize = 60f;
    [Range(1, 6)][SerializeField] private int _cellRadius = 6;
    [Range(0f, 1f)][SerializeField] private float _density = 0.65f;
    [Min(0f)][SerializeField] private float _safeRadius = 25f;
    [SerializeField] private Vector2 _wallWidthRange = new Vector2(6f, 12f);
    [SerializeField] private Vector2 _wallHeightRange = new Vector2(16f, 32f);
    [SerializeField] private Vector2 _iceWidthRange = new Vector2(12f, 24f);
    [SerializeField] private Vector2 _iceHeightRange = new Vector2(12f, 20f);

    [Header("런타임 풀")]
    private FishSpawner _spawner;
    private PlayerController _player;
    private FishType _iceType;
    private Transform _water;
    private Vector3 _waterStartPosition;
    private Vector3 _startPosition;
    private CuttableWall _wallPrefab;
    private float _waterY;
    private int _layer;
    private int _seed;
    private Vector2Int _center;
    private bool _hasCenter;
    private bool _initialized;
    private GameObject _boostPrefab;
    private readonly Dictionary<Vector2Int, GameObject> _activeBoosts = new Dictionary<Vector2Int, GameObject>();
    private readonly Stack<GameObject> _boostPool = new Stack<GameObject>();
    private readonly Dictionary<Vector2Int, CuttableWall> _active = new Dictionary<Vector2Int, CuttableWall>();
    private readonly List<Vector2Int> _releaseCells = new List<Vector2Int>();
    private readonly Stack<CuttableWall> _wallPool = new Stack<CuttableWall>();
    private readonly Stack<CuttableWall> _icePool = new Stack<CuttableWall>();
    public Mesh IceMesh { get; private set; }
    public Material IceMaterial { get; private set; }
    public float IceStartDistance => _iceStartDistance;

    void Update()
    {
        if (!_initialized || _player.IsGameOver) return;

        Vector3 position = _spawner.PlayerBody.position;
        _water.position = new Vector3(position.x, _waterStartPosition.y, position.z);

        Vector2Int center = new Vector2Int(Mathf.FloorToInt(position.x / _cellSize), Mathf.FloorToInt(position.z / _cellSize));
        if (_hasCenter && center == _center) return;
        _center = center;
        _hasCenter = true;
        RefreshCells(position);
    }

    void OnDestroy()
    {
        if (IceMesh != null) Destroy(IceMesh);
        if (IceMaterial != null) Destroy(IceMaterial);
    }

    /// <summary>
    /// spawner, player, 수면 높이와 water, 원본 벽, 유빙 종류로 주변 생성기를 준비한다.
    /// 벽 프리팹을 보관하고 공유 유빙 메시와 머티리얼을 만들며 풀과 셀 추적 상태를 초기화한다.
    /// </summary>
    public void Initialize(FishSpawner spawner, PlayerController player, float waterY, Transform water, MeshFilter wall, FishType iceType)
    {
        _spawner = spawner;
        _player = player;
        _startPosition = player.transform.position;
        _waterY = waterY;
        _water = water;
        _waterStartPosition = water.position;
        _wallPrefab = wall.GetComponent<CuttableWall>();
        _layer = wall.gameObject.layer;
        _iceType = iceType;
        _boostPrefab = Resources.Load<GameObject>("Prefabs/Boost/Boost");
        IceMesh = CreateIceMesh();
        IceMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Procedural Ice" };
        IceMaterial.SetColor("_BaseColor", new Color(0.65f, 0.9f, 1f));
        IceMaterial.SetFloat("_Smoothness", 0.65f);
        _initialized = true;
        ResetObstacles();
    }

    /// <summary>
    /// 입력값 없이 활성 벽, 유빙과 점프대를 풀로 반환하고 수면을 복원해 다음 투척을 준비한다.
    /// 생성 시드와 셀 추적 상태를 초기화하며 기존 풀의 오브젝트는 재사용한다.
    /// </summary>
    public void ResetObstacles()
    {
        if (!_initialized) return;

        ReleaseAll();
        _water.position = _waterStartPosition;
        _seed = Random.Range(0, int.MaxValue);
        _hasCenter = false;
        SpawnObstacles(_startPosition);
    }

    /// <summary>
    /// 활성 셀 목록과 장애물 종류를 사용해 절단 상태를 복원하고 각각의 풀로 돌려보낸다.
    /// 입력값 없이 활성 목록을 비우며 절단 조각과 밑둥 메시를 정리한다.
    /// </summary>
    private void ReleaseAll()
    {
        foreach (CuttableWall wall in _active.Values) Release(wall);
        _active.Clear();
        foreach (GameObject boost in _activeBoosts.Values) ReleaseBoost(boost);
        _activeBoosts.Clear();
    }

    /// <summary>
    /// boost를 비활성화하고 점프대 풀로 반환한다.
    /// 전달된 오브젝트를 다음 셀 배치에서 재사용할 수 있게 저장한다.
    /// </summary>
    private void ReleaseBoost(GameObject boost)
    {
        boost.SetActive(false);
        _boostPool.Push(boost);
    }

    /// <summary>
    /// wall의 절단 상태를 복원하고 비활성화해 해당 조각 종류의 풀에 저장한다.
    /// 활성 조각을 정리하며 다음 셀에서 같은 오브젝트와 충돌체를 재사용하게 한다.
    /// </summary>
    private void Release(CuttableWall wall)
    {
        wall.ResetWall();
        wall.gameObject.SetActive(false);
        (wall.PieceType.Id == "ice" ? _icePool : _wallPool).Push(wall);
    }

    /// <summary>
    /// playerPosition과 현재 중심 셀로 주변 장애물과 점프대를 갱신한다.
    /// 범위 밖 오브젝트를 풀로 반환하고 새로 들어온 셀의 오브젝트를 배치한다.
    /// </summary>
    private void RefreshCells(Vector3 playerPosition)
    {
        _releaseCells.Clear();
        foreach (KeyValuePair<Vector2Int, CuttableWall> entry in _active)
        {
            Vector3 offset = entry.Value.transform.position - playerPosition;
            offset.y = 0f;
            if (offset.sqrMagnitude > _blackHoleStartDistance * _blackHoleStartDistance)
                _releaseCells.Add(entry.Key);
        }
        foreach (Vector2Int cell in _releaseCells)
        {
            Release(_active[cell]);
            _active.Remove(cell);
        }
        _releaseCells.Clear();
        foreach (Vector2Int cell in _activeBoosts.Keys)
        {
            if (Mathf.Abs(cell.x - _center.x) > _cellRadius || Mathf.Abs(cell.y - _center.y) > _cellRadius)
                _releaseCells.Add(cell);
        }
        foreach (Vector2Int cell in _releaseCells)
        {
            ReleaseBoost(_activeBoosts[cell]);
            _activeBoosts.Remove(cell);
        }

        for (int x = -_cellRadius; x <= _cellRadius; x++)
        {
            for (int z = -_cellRadius; z <= _cellRadius; z++)
                TrySpawnBoost(_center + new Vector2Int(x, z), playerPosition);
        }
        SpawnObstacles(playerPosition);
    }

    /// <summary>
    /// playerPosition과 생성 시드로 플레이어 주변 3000m의 모든 셀을 판정한다.
    /// 시작점 +Z 1500m 경계와 밀도 조건에 맞는 벽과 유빙을 월드 Z 3000m 이하에서 활성 목록에 저장한다.
    /// </summary>
    private void SpawnObstacles(Vector3 playerPosition)
    {
        int minX = Mathf.FloorToInt((playerPosition.x - _blackHoleStartDistance) / _cellSize);
        int maxX = Mathf.FloorToInt((playerPosition.x + _blackHoleStartDistance) / _cellSize);
        int minZ = Mathf.FloorToInt((playerPosition.z - _blackHoleStartDistance) / _cellSize);
        int maxZ = Mathf.FloorToInt((playerPosition.z + _blackHoleStartDistance) / _cellSize);
        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (_active.ContainsKey(cell)) continue;
                System.Random random = new System.Random(unchecked(_seed ^ cell.x * 73856093 ^ cell.y * 19349663));
                if (random.NextDouble() > _density) continue;
                Vector3 position = new Vector3(
                    (cell.x + 0.5f + ((float)random.NextDouble() - 0.5f) * 0.5f) * _cellSize,
                    0f,
                    (cell.y + 0.5f + ((float)random.NextDouble() - 0.5f) * 0.5f) * _cellSize);
                if (position.z > MAX_SPAWN_Z) continue;
                Vector3 startOffset = position - _startPosition;
                startOffset.y = 0f;
                Vector3 playerOffset = position - playerPosition;
                playerOffset.y = 0f;
                if (playerOffset.sqrMagnitude > _blackHoleStartDistance * _blackHoleStartDistance) continue;
                if (startOffset.sqrMagnitude < _safeRadius * _safeRadius) continue;

                bool ice = position.z > _startPosition.z + _iceStartDistance;
                Vector2 widths = ice ? _iceWidthRange : _wallWidthRange;
                Vector2 heights = ice ? _iceHeightRange : _wallHeightRange;
                Vector3 scale = new Vector3(
                    Mathf.Lerp(widths.x, widths.y, (float)random.NextDouble()),
                    Mathf.Lerp(heights.x, heights.y, (float)random.NextDouble()),
                    Mathf.Lerp(widths.x, widths.y, (float)random.NextDouble()));
                position.y = _waterY + scale.y * (ice ? 0.2f : 0.4f);
                Stack<CuttableWall> pool = ice ? _icePool : _wallPool;
                CuttableWall obstacle = pool.Count > 0 ? pool.Pop() : CreateObstacle(ice);
                obstacle.transform.localScale = scale;
                Quaternion rotation = ice ? Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f) : Quaternion.identity;
                obstacle.transform.SetPositionAndRotation(position, rotation);
                obstacle.gameObject.SetActive(true);
                _active.Add(cell, obstacle);
            }
        }
    }

    /// <summary>
    /// cell과 playerPosition으로 안전 거리와 생성 구역을 확인해 점프대를 배치한다.
    /// 벽과 유빙 밀도의 1/40 확률로 프리팹을 생성하거나 재사용하고 활성 셀에 기록한다.
    /// </summary>
    private void TrySpawnBoost(Vector2Int cell, Vector3 playerPosition)
    {
        if (_activeBoosts.ContainsKey(cell)) return;
        System.Random random = new System.Random(unchecked(_seed ^ cell.x * 73856093 ^ cell.y * 19349663 ^ 83492791));
        if (random.NextDouble() > _density * BOOST_FREQUENCY_RATIO) return;
        Vector3 position = new Vector3(
            (cell.x + 0.5f + ((float)random.NextDouble() - 0.5f) * 0.5f) * _cellSize,
            6.5f,
            (cell.y + 0.5f + ((float)random.NextDouble() - 0.5f) * 0.5f) * _cellSize);
        if (position.z > MAX_SPAWN_Z) return;
        Vector3 startOffset = position - _startPosition;
        startOffset.y = 0f;
        if (startOffset.magnitude > _blackHoleStartDistance) return;
        Vector3 offset = position - playerPosition;
        offset.y = 0f;
        if (offset.sqrMagnitude < _safeRadius * _safeRadius) return;

        GameObject boost;
        if (_boostPool.Count > 0) boost = _boostPool.Pop();
        else
        {
            boost = Instantiate(_boostPrefab, transform);
            boost.GetComponent<PlacedFish>().SetRespawnOnRestart(false);
        }
        boost.transform.SetPositionAndRotation(position, Quaternion.identity);
        boost.SetActive(true);
        _activeBoosts.Add(cell, boost);
    }

    /// <summary>
    /// ice 여부에 맞는 공유 메시, 머티리얼과 충돌체로 새 풀 항목을 만든다.
    /// 비활성 CuttableWall을 반환하며 유빙과 벽 모두 같은 절단 경로를 사용한다.
    /// </summary>
    private CuttableWall CreateObstacle(bool ice)
    {
        if (!ice)
        {
            CuttableWall instance = Instantiate(_wallPrefab, transform);
            instance.gameObject.SetActive(false);
            instance.Initialize(_spawner, _spawner.WallFish.Type);
            return instance;
        }

        GameObject obstacle = new GameObject("Ice Floe") { layer = _layer };
        obstacle.SetActive(false);
        obstacle.transform.SetParent(transform, false);
        obstacle.AddComponent<MeshFilter>().sharedMesh = IceMesh;
        obstacle.AddComponent<MeshRenderer>().sharedMaterials = new[] { IceMaterial };
        MeshCollider collider = obstacle.AddComponent<MeshCollider>();
        collider.sharedMesh = IceMesh;
        collider.convex = true;
        CuttableWall wall = obstacle.AddComponent<CuttableWall>();
        wall.Initialize(_spawner, _iceType);
        return wall;
    }

    /// <summary>
    /// 입력값 없이 모서리가 깎인 팔각형 유빙의 밀폐된 볼록 메시를 만든다.
    /// 단위 크기 정점, UV와 삼각형을 가진 공유 메시를 반환하며 수명은 생성기가 관리한다.
    /// </summary>
    private static Mesh CreateIceMesh()
    {
        const int SIDES = 8;
        Vector3[] vertices = new Vector3[SIDES * 2];
        Vector2[] uv = new Vector2[vertices.Length];
        List<int> triangles = new List<int>();
        for (int i = 0; i < SIDES; i++)
        {
            float angle = (i + 0.5f) * Mathf.PI * 2f / SIDES;
            float x = Mathf.Cos(angle) * 0.5f;
            float z = Mathf.Sin(angle) * 0.5f;
            vertices[i] = new Vector3(x, -0.5f, z);
            vertices[i + SIDES] = new Vector3(x, 0.5f, z);
            uv[i] = uv[i + SIDES] = new Vector2(x + 0.5f, z + 0.5f);
            int next = (i + 1) % SIDES;
            triangles.AddRange(new[] { i, i + SIDES, next + SIDES, i, next + SIDES, next });
        }
        for (int i = 1; i < SIDES - 1; i++)
            triangles.AddRange(new[] { 0, i, i + 1, SIDES, SIDES + i + 1, SIDES + i });
        Mesh mesh = new Mesh { name = "Ice Floe" };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
