using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Serialization;

// 시드 기반으로 기본 물고기 메시를 만들고, 다른 던질 물고기는 프리팹을 사용한다.
// 렌더용 각진 메시, 충돌용 볼록 메시, 수면 접촉 계산용 아랫면 샘플점을 같이 만든다.
[RequireComponent(typeof(MeshFilter))]
public class FishMeshGenerator : MonoBehaviour
{
    private enum ShapeMode
    {
        Generated,
        Prefab,
    }

    private const float TAIL_LOBE_ANGLE = 0.32f;
    private const float TAIL_END_ANGLE = 0.5f;

    private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");

    [Header("시드")]
    [SerializeField]
    private bool _useFixedSeed = false;
    [SerializeField]
    private int _seed = 0;
    [SerializeField]
    private bool _generateOnAwake = true;
    public event Action OnGenerated;

    [Header("크기")]
    [SerializeField]
    private float _radiusX = 1.5f;
    [SerializeField]
    private float _radiusZ = 1.0f;
    [SerializeField]
    private float _thickness = 0.5f;
    [Tooltip("판마다 전체 크기에 곱해지는 랜덤 배율 범위")]
    [SerializeField]
    private Vector2 _sizeRandomRange = new Vector2(0.85f, 1.15f);
    [Tooltip("두께 중 윗면이 차지하는 비율 (나머지는 아랫면)")]
    [Range(0.1f, 0.9f)]
    [SerializeField]
    private float _topRatio = 0.6f;
    private float _sizeScale = 1f;

    [Header("외곽선 울퉁불퉁")]
    [Tooltip("큰 굴곡 세기 (반지름 대비)")]
    [SerializeField]
    private float _outlineNoise = 0.4f;
    [Tooltip("큰 굴곡 개수 (클수록 많음)")]
    [SerializeField]
    private float _outlineFrequency = 0.8f;
    [Tooltip("잔 요철 세기")]
    [SerializeField]
    private float _detailNoise = 0.15f;
    [SerializeField]
    private float _detailFrequency = 4f;
    private float _outlineOffsetX;
    private float _outlineOffsetY;
    private float _detailOffsetX;
    private float _detailOffsetY;

    [Header("표면 울퉁불퉁")]
    [SerializeField]
    private float _surfaceNoise = 0.18f;
    [SerializeField]
    private float _surfaceFrequency = 2.2f;
    [Tooltip("위치마다 두께가 달라지는 정도 (한쪽이 두꺼운 쐐기 모양)")]
    [SerializeField]
    private float _thicknessNoise = 0.45f;
    [Tooltip("아랫면 평평함 (클수록 평평하고 가장자리만 둥글다)")]
    [SerializeField]
    private float _bottomFlatness = 4f;
    private float _surfaceOffsetX;
    private float _surfaceOffsetY;
    private float _thicknessOffsetX;
    private float _thicknessOffsetY;

    [Header("색")]
    [FormerlySerializedAs("_stoneColor")]
    [SerializeField]
    private Color _generatedFishColor = new Color(0.66f, 0.63f, 0.58f);
    private MeshRenderer _meshRenderer;
    private MaterialPropertyBlock _propertyBlock;
    private ShapeMode _shapeMode;
    private FishType _fishType;
    private float _fishInflate = 1f;
    private Fish _fishInstance;
    private MeshFilter _fishMeshFilter;
    private Vector3 _fishBodyScale;

    [Header("파닥임 (물고기를 선택하는 동안)")]
    [Tooltip("초당 파닥이는 횟수")]
    [SerializeField]
    private float _flopFrequency = 6f;
    [Tooltip("머리와 꼬리가 휘는 정도 (몸길이 비율)")]
    [SerializeField]
    private float _flopBend = 0.35f;
    [Tooltip("몸 전체가 들썩이는 높이 (몸길이 비율)")]
    [SerializeField]
    private float _flopHop = 0.08f;
    private Vector3[] _baseVertices;
    private bool _isFlopping;
    private Mesh[] _fishFlopMeshes = Array.Empty<Mesh>();
    private Transform[] _fishFlopTransforms;
    private Vector3[][] _fishBaseVertices;
    private Vector3[][] _fishFlopVertices;
    private Bounds _fishFlopBounds;

    [Header("평평한 바닥 충돌체")]
    [Tooltip("최저점부터 박스가 덮는 높이 (외형 전체 높이 대비 비율)")]
    [Range(0.05f, 1f)]
    [SerializeField]
    private float _flatBottomHeightRatio = 0.5f;
    private BoxCollider _flatBottomCollider;

    [Header("해상도")]
    [SerializeField]
    private int _segments = 40;
    [SerializeField]
    private int _rings = 5;
    [Tooltip("충돌 메시 분할 수 (볼록 메시 255면 제한 때문에 최대 40)")]
    [SerializeField]
    private int _colliderSegments = 20;
    [SerializeField]
    private int _sampleSegments = 24;
    [SerializeField]
    private int _sampleRings = 4;
    private MeshFilter _meshFilter;
    private MeshCollider _meshCollider;
    private Mesh _renderMesh;
    private Mesh _colliderMesh;

    public int CurrentSeed { get; private set; }
    // 아랫면 샘플점(이 오브젝트 로컬 좌표)과 각 점이 대표하는 면적
    public Vector3[] BottomSamples { get; private set; } = Array.Empty<Vector3>();
    public float[] SampleAreas { get; private set; } = Array.Empty<float>();
    public float TotalBottomArea { get; private set; }
    // 물고기 외형을 감싸는 점들 (최저/최고 높이 계산용, 로컬 좌표)
    public Vector3[] HullPoints { get; private set; } = Array.Empty<Vector3>();
    // 던진 물고기의 특수 동작. 기본 물고기거나 특수 동작이 없는 물고기면 null이다.
    public FishAbility CurrentAbility { get; private set; }
    // 던진 물고기의 몸. 물고기 모양일 때만 쓴다.
    public Transform FishBody => _fishInstance.Body;

    void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshCollider = GetComponent<MeshCollider>();
        _flatBottomCollider = gameObject.AddComponent<BoxCollider>();
        _flatBottomCollider.sharedMaterial = _meshCollider.sharedMaterial;
        _meshRenderer = GetComponent<MeshRenderer>();
        _propertyBlock = new MaterialPropertyBlock();
        if (_generateOnAwake)
        {
            Generate();
        }
    }

    void Update()
    {
        if (!_isFlopping) return;
        Flop(Time.time);
    }

    void OnDestroy()
    {
        ClearFish();
        // _generateOnAwake를 끄고 한 번도 생성하지 않았을 수 있다.
        if (_renderMesh != null)
        {
            Destroy(_renderMesh);
            Destroy(_colliderMesh);
        }
    }

    /// <summary>
    /// 고정 시드 설정에 따라 시드를 정해 새 기본 물고기를 만든다.
    /// _useFixedSeed와 _seed를 사용하며, 메시와 샘플점을 새로 만든다.
    /// </summary>
    [ContextMenu("Generate New Fish")]
    public void Generate()
    {
        Generate(_useFixedSeed ? _seed : UnityEngine.Random.Range(int.MinValue, int.MaxValue));
    }

    /// <summary>
    /// 시드로 노이즈 오프셋과 크기를 정하고 기본 물고기 메시와 샘플점을 만든다.
    /// seed를 사용하며, 메시, 샘플점, 색을 변경하고 OnGenerated를 보낸다.
    /// </summary>
    public void Generate(int seed)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("FishMeshGenerator: 물고기 생성은 플레이 중에만 됩니다.");
            return;
        }

        CurrentSeed = seed;
        System.Random rng = new System.Random(seed);
        _outlineOffsetX = NextOffset(rng);
        _outlineOffsetY = NextOffset(rng);
        _detailOffsetX = NextOffset(rng);
        _detailOffsetY = NextOffset(rng);
        _surfaceOffsetX = NextOffset(rng);
        _surfaceOffsetY = NextOffset(rng);
        _thicknessOffsetX = NextOffset(rng);
        _thicknessOffsetY = NextOffset(rng);
        _sizeScale = Mathf.Lerp(_sizeRandomRange.x, _sizeRandomRange.y, (float)rng.NextDouble());

        ClearFish();
        _meshRenderer.enabled = true;
        _shapeMode = ShapeMode.Generated;
        _isFlopping = false;
        Rebuild(_generatedFishColor);
    }

    /// <summary>
    /// 물고기 프리팹을 생성기 아래에 생성하고 출현용 동작을 끈 뒤 옆으로 눕혀 던질 준비를 한다.
    /// prefab의 메시와 머티리얼을 사용하며, 충돌체, 수면 샘플, CurrentAbility를 변경하고 OnGenerated를 보낸다.
    /// </summary>
    public void GenerateFish(Fish prefab)
    {
        ClearFish();
        _shapeMode = ShapeMode.Prefab;
        _fishInstance = Instantiate(prefab, transform, false);
        _fishInstance.enabled = false;
        _fishInstance.gameObject.SetActive(true);
        CurrentAbility = _fishInstance.GetComponent<FishAbility>();
        _fishType = _fishInstance.Type;
        _fishInflate = 1f;
        _isFlopping = false;
        _fishInstance.transform.localPosition = Vector3.zero;
        _fishInstance.transform.localRotation = _fishInstance.Body == _fishInstance.transform
            ? prefab.transform.localRotation
            : Quaternion.Inverse(_fishInstance.Body.localRotation);
        if (CurrentAbility != null) CurrentAbility.OnPrepareThrow();
        _fishBodyScale = _fishInstance.Body.localScale;

        // 프리팹의 큰 Trigger는 포획용이므로 던질 때는 플레이어의 볼록 충돌체만 사용한다.
        foreach (Collider collider in _fishInstance.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }
        Rigidbody fishBody = _fishInstance.GetComponent<Rigidbody>();
        fishBody.detectCollisions = false;
        Destroy(fishBody);

        MeshFilter[] filters = _fishInstance.GetComponentsInChildren<MeshFilter>(true);
        SkinnedMeshRenderer[] skinnedRenderers = _fishInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        _fishFlopMeshes = new Mesh[filters.Length + skinnedRenderers.Length];
        _fishFlopTransforms = new Transform[_fishFlopMeshes.Length];
        _fishBaseVertices = new Vector3[_fishFlopMeshes.Length][];
        _fishFlopVertices = new Vector3[_fishFlopMeshes.Length][];
        for (int i = 0; i < _fishFlopMeshes.Length; i++)
        {
            Mesh mesh = Instantiate(i < filters.Length ? filters[i].sharedMesh : skinnedRenderers[i - filters.Length].sharedMesh);
            Transform meshTransform = i < filters.Length ? filters[i].transform : skinnedRenderers[i - filters.Length].transform;
            if (i < filters.Length) filters[i].sharedMesh = mesh;
            else skinnedRenderers[i - filters.Length].sharedMesh = mesh;
            _fishFlopMeshes[i] = mesh;
            _fishFlopTransforms[i] = meshTransform;
            _fishBaseVertices[i] = mesh.vertices;
            _fishFlopVertices[i] = new Vector3[mesh.vertexCount];
            Matrix4x4 toBody = _fishInstance.Body.worldToLocalMatrix * meshTransform.localToWorldMatrix;
            for (int j = 0; j < mesh.vertexCount; j++)
            {
                Vector3 vertex = toBody.MultiplyPoint3x4(_fishBaseVertices[i][j]);
                if (i == 0 && j == 0) _fishFlopBounds = new Bounds(vertex, Vector3.zero);
                else _fishFlopBounds.Encapsulate(vertex);
            }
        }
        _fishMeshFilter = filters.Length == 1 && skinnedRenderers.Length == 0 ? filters[0] : null;
        if (CurrentAbility != null && CurrentAbility.ThrowGeometry != null)
        {
            _fishMeshFilter = CurrentAbility.ThrowGeometry;
        }
        Mesh oldRender = _renderMesh;
        _renderMesh = _fishMeshFilter != null
            ? _fishMeshFilter.sharedMesh
            : BuildCompositeFishMesh();
        _baseVertices = _renderMesh.vertices;
        Destroy(oldRender);
        _meshRenderer.enabled = false;
        BuildPrefabGeometry();

        if (_fishType.Id == "kinghopak")
        {
            PlayerController player = GetComponentInParent<PlayerController>();
            foreach (Transform tip in _fishInstance.GetComponentsInChildren<Transform>())
            {
                if (tip.name == "mixamorig:LeftHand" || tip.name == "mixamorig:RightHand")
                {
                    tip.gameObject.AddComponent<WallCuttingTip>().Initialize(player);
                }
            }
        }
    }

    /// <summary>
    /// 이전 물고기 인스턴스를 즉시 숨긴 뒤 제거한다.
    /// 현재 인스턴스를 사용하며, 프리팹 참조, 특수 동작, 파닥임 상태를 초기화한다.
    /// </summary>
    private void ClearFish()
    {
        foreach (Mesh mesh in _fishFlopMeshes)
        {
            if (mesh != _renderMesh) Destroy(mesh);
        }
        _fishFlopMeshes = Array.Empty<Mesh>();
        if (_fishInstance == null) return;

        _fishInstance.gameObject.SetActive(false);
        Destroy(_fishInstance.gameObject);
        _fishInstance = null;
        _fishMeshFilter = null;
        CurrentAbility = null;
        _isFlopping = false;
    }

    /// <summary>
    /// 분리된 일반 메시를 몸 좌표계로 합치고 스킨드 모델은 현재 자세를 추출한다.
    /// 현재 프리팹의 MeshFilter와 Body를 사용하며, 원본 외형을 변경하지 않는 수면 판정용 메시를 반환한다.
    /// </summary>
    private Mesh BuildCompositeFishMesh()
    {
        MeshFilter[] filters = _fishInstance.GetComponentsInChildren<MeshFilter>(true);

        List<CombineInstance> parts = new List<CombineInstance>();
        foreach (MeshFilter filter in filters)
        {
            for (int i = 0; i < filter.sharedMesh.subMeshCount; i++)
            {
                parts.Add(new CombineInstance
                {
                    mesh = filter.sharedMesh,
                    subMeshIndex = i,
                    transform = _fishInstance.Body.worldToLocalMatrix * filter.transform.localToWorldMatrix,
                });
            }
        }

        Mesh mesh = new Mesh { name = "Fish_Composite_Geometry" };
        Mesh skinnedMesh = BakeSkinnedFishMesh();
        if (skinnedMesh.vertexCount > 0)
        {
            parts.Add(new CombineInstance { mesh = skinnedMesh, transform = Matrix4x4.identity });
        }
        mesh.CombineMeshes(parts.ToArray(), true, true);
        Destroy(skinnedMesh);
        return mesh;
    }

    /// <summary>
    /// 스킨드 물고기의 모든 몸 메시를 현재 자세로 합쳐 수면 판정용 메시를 만든다.
    /// 현재 프리팹의 렌더러와 Body 좌표계를 사용하며, 원본 외형을 변경하지 않는 새 메시를 반환한다.
    /// </summary>
    private Mesh BakeSkinnedFishMesh()
    {
        List<CombineInstance> parts = new List<CombineInstance>();
        List<Mesh> bakedMeshes = new List<Mesh>();
        foreach (SkinnedMeshRenderer renderer in _fishInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Mesh bakedMesh = new Mesh();
            renderer.BakeMesh(bakedMesh, true);
            bakedMeshes.Add(bakedMesh);
            for (int i = 0; i < bakedMesh.subMeshCount; i++)
            {
                parts.Add(new CombineInstance
                {
                    mesh = bakedMesh,
                    subMeshIndex = i,
                    transform = _fishInstance.Body.worldToLocalMatrix * renderer.transform.localToWorldMatrix,
                });
            }
        }

        Mesh mesh = new Mesh { name = "Fish_Skinned_Geometry" };
        mesh.CombineMeshes(parts.ToArray(), true, true);
        foreach (Mesh bakedMesh in bakedMeshes)
        {
            Destroy(bakedMesh);
        }
        return mesh;
    }

    /// <summary>
    /// 프리팹 메시의 실제 형상에서 수면 샘플과 저해상도 볼록 충돌 메시를 만든다.
    /// 몸 메시와 로컬 변환을 사용하며, HullPoints와 면적 데이터, 볼록 충돌체와 바닥 박스 충돌체를 변경한다.
    /// </summary>
    public void BuildPrefabGeometry()
    {
        Transform meshTransform = _fishMeshFilter != null ? _fishMeshFilter.transform : _fishInstance.Body;
        Matrix4x4 toLocal = transform.worldToLocalMatrix * meshTransform.localToWorldMatrix;
        HullPoints = new Vector3[_baseVertices.Length];
        for (int i = 0; i < _baseVertices.Length; i++)
        {
            HullPoints[i] = toLocal.MultiplyPoint3x4(_baseVertices[i]);
        }

        List<Vector3> samples = new List<Vector3>();
        List<float> areas = new List<float>();
        int[] triangles = _renderMesh.triangles;
        float total = 0f;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = HullPoints[triangles[i]];
            Vector3 b = HullPoints[triangles[i + 1]];
            Vector3 c = HullPoints[triangles[i + 2]];
            float area = -Vector3.Cross(b - a, c - a).y * 0.5f;
            if (area <= 0.000001f) continue;

            samples.Add((a + b + c) / 3f);
            areas.Add(area);
            total += area;
        }
        BottomSamples = samples.ToArray();
        SampleAreas = areas.ToArray();
        TotalBottomArea = total;

        Mesh oldCollider = _colliderMesh;
        BuildGrid(Mathf.Clamp(_colliderSegments, 6, 40), 2, PrefabSurfacePoint, out Vector3[,] top, out Vector3[,] bottom);
        _colliderMesh = BuildMesh(top, bottom, "Fish_Prefab_Collider");
        _meshCollider.sharedMesh = null;
        _meshCollider.convex = true;
        _meshCollider.sharedMesh = _colliderMesh;
        FitFlatBottomCollider();
        Destroy(oldCollider);
        OnGenerated?.Invoke();
    }

    /// <summary>
    /// 프리팹 외형에서 지정 방향으로 가장 멀리 있는 점을 찾아 볼록 충돌체를 근사한다.
    /// angle, t, top과 HullPoints를 사용하며, 메시 로컬 좌표의 지지점을 반환한다.
    /// </summary>
    private Vector3 PrefabSurfacePoint(float angle, float t, bool top)
    {
        Vector3 direction = new Vector3(Mathf.Cos(angle) * t, (top ? 1f : -1f) * Mathf.Sqrt(1f - t * t), Mathf.Sin(angle) * t);
        Vector3 point = HullPoints[0];
        float farthest = Vector3.Dot(point, direction);
        for (int i = 1; i < HullPoints.Length; i++)
        {
            float distance = Vector3.Dot(HullPoints[i], direction);
            if (distance <= farthest) continue;

            farthest = distance;
            point = HullPoints[i];
        }
        return point;
    }

    /// <summary>
    /// 외형의 최저점을 바닥면으로 하는 박스 충돌체를 외형 아래쪽에 맞춰 바닥을 평평하게 만든다.
    /// HullPoints와 _flatBottomHeightRatio를 사용하며, _flatBottomCollider의 center와 size를 변경한다.
    /// </summary>
    private void FitFlatBottomCollider()
    {
        Bounds bounds = new Bounds(HullPoints[0], Vector3.zero);
        for (int i = 1; i < HullPoints.Length; i++)
        {
            bounds.Encapsulate(HullPoints[i]);
        }

        // 바닥면을 최저점에 맞춰 수면 판정의 최저 높이와 충돌체 바닥이 어긋나지 않게 한다.
        float height = bounds.size.y * _flatBottomHeightRatio;
        _flatBottomCollider.center = new Vector3(bounds.center.x, bounds.min.y + height * 0.5f, bounds.center.z);
        _flatBottomCollider.size = new Vector3(bounds.size.x, height, bounds.size.z);
    }

    /// <summary>
    /// 물고기 모양일 때 파닥임을 켜거나 끈다. 끄면 휘지 않은 원래 모양으로 되돌린다.
    /// flopping과 현재 모양 모드를 사용하며, _isFlopping과 렌더 메시 정점을 변경한다.
    /// </summary>
    public void SetFlopping(bool flopping)
    {
        bool active = flopping && _shapeMode == ShapeMode.Prefab
            && _fishFlopMeshes.Length > 0
            && _fishType.Id != "wall"
            && _fishType.Id != "ice"
            && _fishType.Id != "screen"
            && _fishType.Id != "catalog"
            && _fishType.Id != "menu"
            && _fishInstance.GetComponentInChildren<HopakJumpAnimation>(true) == null;
        if (_isFlopping == active) return;

        _isFlopping = active;
        if (!active)
        {
            for (int i = 0; i < _fishFlopMeshes.Length; i++)
            {
                _fishFlopMeshes[i].vertices = _fishBaseVertices[i];
                _fishFlopMeshes[i].RecalculateNormals();
                _fishFlopMeshes[i].RecalculateBounds();
            }
        }
    }

    /// <summary>
    /// 땅 위 물고기처럼 머리와 꼬리를 C자로 들썩이게 렌더 메시만 휜다. 충돌 메시는 그대로라 물리에 영향이 없다.
    /// time과 파닥임 설정, 현재 물고기 크기를 사용하며, 렌더 메시 정점과 노멀을 변경한다.
    /// </summary>
    private void Flop(float time)
    {
        float length = Mathf.Max(_fishFlopBounds.size.z, 0.0001f);
        float pivot = _fishFlopBounds.center.z;
        float phase = time * _flopFrequency * Mathf.PI * 2f;
        // 같은 박자로만 움직이면 기계 같아서 세기를 노이즈로 흔든다.
        float strength = 0.35f + 0.65f * Mathf.PerlinNoise(time * 1.7f, 0.37f);
        float bend = Mathf.Sin(phase) * strength * _flopBend * length;
        float hop = Mathf.Abs(Mathf.Sin(phase * 0.5f)) * strength * _flopHop * length;

        for (int i = 0; i < _fishFlopMeshes.Length; i++)
        {
            Matrix4x4 toBody = _fishInstance.Body.worldToLocalMatrix * _fishFlopTransforms[i].localToWorldMatrix;
            Matrix4x4 fromBody = toBody.inverse;
            for (int j = 0; j < _fishBaseVertices[i].Length; j++)
            {
                Vector3 vertex = toBody.MultiplyPoint3x4(_fishBaseVertices[i][j]);
                // 모든 자식 메시를 같은 몸 좌표계에서 휘어 연결된 부위가 함께 움직인다.
                float along = (vertex.z - pivot) / length;
                vertex.y += along * along * bend + hop;
                _fishFlopVertices[i][j] = fromBody.MultiplyPoint3x4(vertex);
            }
            _fishFlopMeshes[i].vertices = _fishFlopVertices[i];
            _fishFlopMeshes[i].RecalculateNormals();
            _fishFlopMeshes[i].RecalculateBounds();
        }
    }

    /// <summary>
    /// 현재 모양 설정으로 렌더/충돌 메시와 샘플점을 다시 만들고 색을 입힌다.
    /// color와 현재 모양 모드를 사용하며, 메시, HullPoints, 바닥 박스 충돌체, 샘플점, 렌더러 색을 변경한다.
    /// </summary>
    private void Rebuild(Color color)
    {
        Mesh oldRender = _renderMesh;
        Mesh oldCollider = _colliderMesh;

        BuildGrid(Mathf.Max(8, _segments), Mathf.Max(1, _rings), SurfacePoint, out Vector3[,] top, out Vector3[,] bottom);
        _renderMesh = BuildMesh(top, bottom, "Fish_Render");

        BuildGrid(Mathf.Clamp(_colliderSegments, 6, 40), 2, SurfacePoint, out Vector3[,] colliderTop, out Vector3[,] colliderBottom);
        _colliderMesh = BuildMesh(colliderTop, colliderBottom, "Fish_Collider");
        HullPoints = CollectHullPoints(colliderTop, colliderBottom);

        _baseVertices = _renderMesh.vertices;

        _meshFilter.sharedMesh = _renderMesh;
        _meshCollider.sharedMesh = null;
        _meshCollider.convex = true;
        _meshCollider.sharedMesh = _colliderMesh;
        FitFlatBottomCollider();

        // 첫 생성 때는 이전 메시가 없다.
        if (oldRender != null)
        {
            Destroy(oldRender);
            Destroy(oldCollider);
        }

        // 공용 머티리얼을 바꾸지 않도록 렌더러별 색만 덮어쓴다.
        _propertyBlock.SetColor(BASE_COLOR_ID, color);
        _meshRenderer.SetPropertyBlock(_propertyBlock);

        BuildSamples();
        OnGenerated?.Invoke();
    }

    /// <summary>
    /// 현재 모양 모드에 맞는 외곽선 점을 구한다.
    /// angle(라디안)을 사용하며, 로컬 xz 좌표를 반환한다.
    /// </summary>
    private Vector2 OutlinePoint(float angle)
    {
        return _shapeMode == ShapeMode.Prefab ? FishOutlinePoint(angle, _fishType, _fishInflate) : GeneratedFishOutlinePoint(angle);
    }

    /// <summary>
    /// 현재 모양 모드에 맞는 윗면 또는 아랫면 점을 구한다.
    /// angle, t(0 중심 ~ 1 가장자리), top을 사용하며, 로컬 좌표를 반환한다.
    /// </summary>
    private Vector3 SurfacePoint(float angle, float t, bool top)
    {
        return _shapeMode == ShapeMode.Prefab ? FishSurfacePoint(angle, t, top, _fishType, _fishInflate) : GeneratedFishSurfacePoint(angle, t, top);
    }

    /// <summary>
    /// 각도 방향의 울퉁불퉁한 기본 물고기 외곽선 점을 구한다.
    /// angle(라디안)과 외곽선 노이즈 설정을 사용하며, 로컬 xz 좌표를 반환한다.
    /// </summary>
    private Vector2 GeneratedFishOutlinePoint(float angle)
    {
        float c = Mathf.Cos(angle);
        float s = Mathf.Sin(angle);
        // 원 위에서 노이즈를 샘플링해야 외곽선이 이음매 없이 한 바퀴 이어진다.
        float big = Noise(_outlineOffsetX + c * _outlineFrequency, _outlineOffsetY + s * _outlineFrequency);
        float small = Noise(_detailOffsetX + c * _detailFrequency, _detailOffsetY + s * _detailFrequency);
        float radius = Mathf.Max(0.3f, 1f + big * _outlineNoise + small * _detailNoise) * _sizeScale;
        return new Vector2(c * _radiusX * radius, s * _radiusZ * radius);
    }

    /// <summary>
    /// 기본 물고기의 윗면 또는 아랫면 위의 한 점을 구한다. 윗면은 볼록한 돔, 아랫면은 평평하고 가장자리만 둥글다.
    /// angle, t(0 중심 ~ 1 가장자리), top을 사용하며, 로컬 좌표를 반환한다.
    /// </summary>
    private Vector3 GeneratedFishSurfacePoint(float angle, float t, bool top)
    {
        Vector2 xz = GeneratedFishOutlinePoint(angle) * t;
        float thicknessScale = Mathf.Max(0.3f, 1f + Noise(_thicknessOffsetX + xz.x * 0.5f, _thicknessOffsetY + xz.y * 0.5f) * _thicknessNoise);
        float baseHeight = _thickness * _sizeScale * thicknessScale * (top ? _topRatio : 1f - _topRatio);
        float profile = top
            ? Mathf.Pow(Mathf.Max(0f, 1f - t * t), 0.6f)
            : Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(t, _bottomFlatness)), 0.5f);
        float bump = Noise(_surfaceOffsetX + (top ? 0f : 71.3f) + xz.x * _surfaceFrequency, _surfaceOffsetY + xz.y * _surfaceFrequency) * _surfaceNoise * _sizeScale;

        float height = baseHeight * profile;
        height = Mathf.Max(height * 0.3f, height + bump * profile);
        return new Vector3(xz.x, top ? height : -height, xz.y);
    }

    /// <summary>
    /// 물고기 종류 모양에 맞는 외곽선 점을 구한다.
    /// angle(라디안), fish, inflate(복어가 부푼 정도, 1이면 원래 모양)를 사용하며, 로컬 xz 좌표를 반환한다.
    /// </summary>
    private static Vector2 FishOutlinePoint(float angle, FishType fish, float inflate)
    {
        switch (fish.Shape)
        {
            case FishShape.Starfish:
            case FishShape.Crab: return CrabOutlinePoint(angle, fish);
            case FishShape.Tire: return TireOutlinePoint(angle, fish);
            case FishShape.Can: return CanOutlinePoint(angle, fish);
            default: return SwimmerOutlinePoint(angle, fish, inflate);
        }
    }

    /// <summary>
    /// 물고기 옆모습 외곽선 점을 구한다. 몸통은 타원, 꼬리(-z)는 가운데가 파인 두 갈래, 상어는 등지느러미가 솟는다.
    /// angle(라디안), fish 크기, inflate를 사용하며, 로컬 xz 좌표를 반환한다.
    /// </summary>
    private static Vector2 SwimmerOutlinePoint(float angle, FishType fish, float inflate)
    {
        // 부풀면 몸은 위아래로 크게, 앞뒤로는 조금만 커지고 꼬리는 상대적으로 작아진다.
        float halfHeight = fish.Height * 0.5f * inflate;
        float halfLength = fish.Length * 0.5f * (1f + (inflate - 1f) * 0.3f);
        float tailLength = fish.TailLength / inflate;
        float c = Mathf.Cos(angle);
        float s = Mathf.Sin(angle);
        float body = 1f / Mathf.Sqrt((c / halfHeight) * (c / halfHeight) + (s / halfLength) * (s / halfLength));

        float fromTail = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, -90f)) * Mathf.Deg2Rad;
        float tail = 0f;
        if (fromTail < TAIL_LOBE_ANGLE)
        {
            tail = Mathf.Lerp(halfLength + tailLength * 0.35f, halfLength + tailLength, fromTail / TAIL_LOBE_ANGLE);
        }
        else if (fromTail < TAIL_END_ANGLE)
        {
            tail = Mathf.Lerp(halfLength + tailLength, 0f, (fromTail - TAIL_LOBE_ANGLE) / (TAIL_END_ANGLE - TAIL_LOBE_ANGLE));
        }

        // 등(+x)에서 꼬리 쪽으로 조금 기운 자리에 삼각 지느러미를 세운다.
        float fin = Lobe(angle, -20f, 0.2f) * fish.DorsalFin;
        float radius = Mathf.Max(body, tail) + fin;
        return new Vector2(c * radius, s * radius);
    }

    /// <summary>
    /// 위에서 본 게 외곽선 점을 구한다. 납작한 타원 몸통 앞(+z)에 집게 두 개, 양옆에 다리 세 쌍이 붙는다.
    /// angle(라디안)과 crab 크기(높이는 좌우 폭, 꼬리 길이는 집게 길이)를 사용하며, 로컬 xz 좌표를 반환한다.
    /// </summary>
    private static Vector2 CrabOutlinePoint(float angle, FishType crab)
    {
        float halfWidth = crab.Height * 0.5f;
        float halfLength = crab.Length * 0.5f;
        float c = Mathf.Cos(angle);
        float s = Mathf.Sin(angle);
        float body = 1f / Mathf.Sqrt((c / halfWidth) * (c / halfWidth) + (s / halfLength) * (s / halfLength));

        float claws = Lobe(angle, 55f, 0.25f) + Lobe(angle, 125f, 0.25f);
        float legs = Lobe(angle, -10f, 0.13f) + Lobe(angle, -35f, 0.13f) + Lobe(angle, -60f, 0.13f)
            + Lobe(angle, 190f, 0.13f) + Lobe(angle, 215f, 0.13f) + Lobe(angle, 240f, 0.13f);
        float radius = body + claws * crab.TailLength + legs * crab.TailLength * 0.6f;
        return new Vector2(c * radius, s * radius);
    }

    /// <summary>
    /// 위에서 본 타이어 외곽선 점을 구한다. 고무가 살짝 뭉개진 원이다.
    /// angle(라디안)과 tire 크기(길이와 높이가 지름)를 사용하며, 로컬 xz 좌표를 반환한다.
    /// </summary>
    private static Vector2 TireOutlinePoint(float angle, FishType tire)
    {
        float c = Mathf.Cos(angle);
        float s = Mathf.Sin(angle);
        float radius = 1f + Mathf.Sin(angle * 3f + 0.7f) * 0.03f;
        return new Vector2(c * tire.Height * 0.5f * radius, s * tire.Length * 0.5f * radius);
    }

    /// <summary>
    /// 위에서 본 찌그러진 캔 외곽선 점을 구한다. 옆으로 누운 둥근 직사각형에 우그러진 자국이 난다.
    /// angle(라디안)과 can 크기(길이는 캔 길이, 높이는 폭)를 사용하며, 로컬 xz 좌표를 반환한다.
    /// </summary>
    private static Vector2 CanOutlinePoint(float angle, FishType can)
    {
        float c = Mathf.Cos(angle);
        float s = Mathf.Sin(angle);
        float halfWidth = can.Height * 0.5f;
        float halfLength = can.Length * 0.5f;
        // 지수 4인 초타원이라 모서리가 둥근 직사각형이 된다.
        float box = Mathf.Pow(Mathf.Pow(Mathf.Abs(c / halfWidth), 4f) + Mathf.Pow(Mathf.Abs(s / halfLength), 4f), -0.25f);
        float crumple = 1f + Mathf.Sin(angle * 5f) * 0.12f + Mathf.Sin(angle * 11f + 1.3f) * 0.07f;
        return new Vector2(c * box * crumple, s * box * crumple);
    }

    /// <summary>
    /// 물고기의 윗면 또는 아랫면 점을 구한다. 물고기는 머리 쪽이 두껍고 꼬리로 갈수록 얇다. 게는 위가 볼록하고 아래가 평평하다.
    /// angle, t, top, fish, inflate를 사용하며, 로컬 좌표를 반환한다.
    /// </summary>
    private static Vector3 FishSurfacePoint(float angle, float t, bool top, FishType fish, float inflate)
    {
        Vector2 xz = FishOutlinePoint(angle, fish, inflate) * t;
        float height;
        if (fish.Shape == FishShape.Tire)
        {
            // 바깥쪽 고리만 두껍고 가운데는 얇게 꺼져 있어서 구멍 난 타이어처럼 보인다.
            float ring = Mathf.Exp(-Mathf.Pow((t - 0.72f) / 0.2f, 2f));
            height = fish.Thickness * 0.5f * Mathf.Max(ring, 0.06f) * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(t, 8f)));
        }
        else if (fish.Shape == FishShape.Can)
        {
            // 납작하게 눌린 원통에 표면도 군데군데 우그러졌다.
            float dent = 1f + Mathf.Sin(angle * 7f + (top ? 0f : 2.1f)) * 0.25f * t;
            height = fish.Thickness * 0.5f * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(t, 4f))) * dent;
        }
        else if (fish.Shape == FishShape.Crab || fish.Shape == FishShape.Starfish)
        {
            float profile = top
                ? Mathf.Pow(Mathf.Max(0f, 1f - t * t), 0.5f)
                : Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(t, 4f)), 0.5f);
            height = fish.Thickness * (top ? 0.6f : 0.4f) * profile;
        }
        else
        {
            float halfLength = fish.Length * 0.5f;
            float tailFactor = Mathf.Clamp01((-xz.y - halfLength * 0.3f) / (halfLength * 0.7f + fish.TailLength));
            float halfThickness = fish.Thickness * 0.5f * inflate * Mathf.Lerp(1f, 0.12f, tailFactor);
            height = halfThickness * Mathf.Pow(Mathf.Max(0f, 1f - t * t), 0.6f);
        }
        return new Vector3(xz.x, top ? height : -height, xz.y);
    }

    /// <summary>
    /// 각도가 중심 각도에 가까울수록 1에 가까운 종 모양 값을 구한다. 지느러미, 집게, 다리 돌출에 쓴다.
    /// angle(라디안), centerDegrees, width(라디안)를 사용하며, 0~1 값을 반환한다.
    /// </summary>
    private static float Lobe(float angle, float centerDegrees, float width)
    {
        float delta = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, centerDegrees) * Mathf.Deg2Rad / width;
        return Mathf.Exp(-delta * delta);
    }
    /// <summary>
    /// 링과 분할 수에 맞춰 윗면/아랫면 격자점을 만든다.
    /// segments, rings, surface(각도, t, 윗면 여부 → 점)를 사용하며, top과 bottom 격자를 out으로 반환한다.
    /// </summary>
    private static void BuildGrid(int segments, int rings, Func<float, float, bool, Vector3> surface, out Vector3[,] top, out Vector3[,] bottom)
    {
        top = new Vector3[rings + 1, segments];
        bottom = new Vector3[rings + 1, segments];
        for (int i = 0; i <= rings; i++)
        {
            float t = (float)i / rings;
            for (int j = 0; j < segments; j++)
            {
                float angle = j * Mathf.PI * 2f / segments;
                top[i, j] = surface(angle, t, true);
                bottom[i, j] = surface(angle, t, false);
            }
        }
    }

    /// <summary>
    /// 격자점으로 정점을 공유하지 않는 각진 메시를 만든다.
    /// top, bottom 격자와 meshName을 사용하며, 새 Mesh를 반환한다.
    /// </summary>
    private static Mesh BuildMesh(Vector3[,] top, Vector3[,] bottom, string meshName)
    {
        int rings = top.GetLength(0) - 1;
        int segments = top.GetLength(1);
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        for (int j = 0; j < segments; j++)
        {
            int k = (j + 1) % segments;
            // 유니티는 시계방향이 앞면이다. 윗면은 위에서, 아랫면은 아래에서 봤을 때 시계방향으로 만든다.
            AddTriangle(vertices, triangles, top[0, j], top[1, k], top[1, j]);
            AddTriangle(vertices, triangles, bottom[0, j], bottom[1, j], bottom[1, k]);
            for (int i = 1; i < rings; i++)
            {
                AddTriangle(vertices, triangles, top[i, j], top[i + 1, k], top[i + 1, j]);
                AddTriangle(vertices, triangles, top[i, j], top[i, k], top[i + 1, k]);
                AddTriangle(vertices, triangles, bottom[i, j], bottom[i + 1, j], bottom[i + 1, k]);
                AddTriangle(vertices, triangles, bottom[i, j], bottom[i + 1, k], bottom[i, k]);
            }
        }

        List<Vector2> uvs = new List<Vector2>(vertices.Count);
        foreach (Vector3 v in vertices)
        {
            uvs.Add(new Vector2(v.x, v.z));
        }

        Mesh mesh = new Mesh { name = meshName };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// 삼각형 하나를 정점 공유 없이 추가한다.
    /// a, b, c 정점을 사용하며, vertices와 triangles 리스트를 변경한다.
    /// </summary>
    private static void AddTriangle(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
    {
        int index = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        triangles.Add(index);
        triangles.Add(index + 1);
        triangles.Add(index + 2);
    }

    /// <summary>
    /// 충돌 격자에서 중복 없이 외형 점을 모은다. 가장자리는 윗면과 아랫면이 같은 점이다.
    /// top, bottom 격자를 사용하며, 외형 점 배열을 반환한다.
    /// </summary>
    private static Vector3[] CollectHullPoints(Vector3[,] top, Vector3[,] bottom)
    {
        int rings = top.GetLength(0) - 1;
        int segments = top.GetLength(1);
        List<Vector3> points = new List<Vector3> { top[0, 0], bottom[0, 0] };
        for (int i = 1; i <= rings; i++)
        {
            for (int j = 0; j < segments; j++)
            {
                points.Add(top[i, j]);
                if (i < rings)
                {
                    points.Add(bottom[i, j]);
                }
            }
        }
        return points.ToArray();
    }

    /// <summary>
    /// 아랫면을 부채꼴 띠로 나눠 각 띠의 중심점과 면적을 샘플로 만든다.
    /// 샘플 분할 설정과 현재 외곽선을 사용하며, BottomSamples, SampleAreas, TotalBottomArea를 변경한다.
    /// </summary>
    private void BuildSamples()
    {
        int segments = Mathf.Max(6, _sampleSegments);
        int rings = Mathf.Max(1, _sampleRings);
        Vector3[] points = new Vector3[segments * rings];
        float[] areas = new float[segments * rings];
        float step = Mathf.PI * 2f / segments;
        float total = 0f;
        int n = 0;

        for (int i = 0; i < rings; i++)
        {
            float t0 = (float)i / rings;
            float t1 = (float)(i + 1) / rings;
            float tMid = (t0 + t1) * 0.5f;
            for (int j = 0; j < segments; j++)
            {
                // 이웃한 외곽선 두 점과 중심이 이루는 삼각형에서 t0~t1 띠 부분의 면적이다.
                Vector2 edge0 = OutlinePoint(j * step);
                Vector2 edge1 = OutlinePoint((j + 1) * step);
                float wedge = 0.5f * Mathf.Abs(edge0.x * edge1.y - edge0.y * edge1.x);
                float area = wedge * (t1 * t1 - t0 * t0);
                points[n] = SurfacePoint((j + 0.5f) * step, tMid, false);
                areas[n] = area;
                total += area;
                n++;
            }
        }

        BottomSamples = points;
        SampleAreas = areas;
        TotalBottomArea = total;
    }

    /// <summary>
    /// 노이즈 샘플 위치를 흩뜨리는 오프셋을 만든다.
    /// rng를 사용하며, 0~1000 사이 값을 반환한다.
    /// </summary>
    private static float NextOffset(System.Random rng)
    {
        return (float)rng.NextDouble() * 1000f;
    }

    /// <summary>
    /// Perlin 노이즈를 -1~1 범위로 바꿔 반환한다.
    /// x, y 좌표를 사용하며, -1~1 값을 반환한다.
    /// </summary>
    private static float Noise(float x, float y)
    {
        return Mathf.Clamp01(Mathf.PerlinNoise(x, y)) * 2f - 1f;
    }
}
