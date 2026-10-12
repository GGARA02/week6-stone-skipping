using System.Collections.Generic;

using UnityEngine;

public sealed class CuttableWall : MonoBehaviour
{
    private const float MIN_CUT_FRACTION = 0.02f;
    private const float EJECTION_SPEED = 12f;
    private const float UPWARD_SPEED = 4f;

    [Header("상태")]
    [SerializeField] private MeshCollider _remainingCollider;
    private FishSpawner _spawner;
    private FishType _pieceType;
    private MeshFilter _meshFilter;
    private Mesh _originalMesh;
    private Collider _originalCollider;
    private Mesh _remainingMesh;
    private readonly List<GameObject> _bandPieces = new List<GameObject>();
    private readonly List<Mesh> _bandMeshes = new List<Mesh>();
    public FishType PieceType => _pieceType;

    void OnDestroy()
    {
        if (_remainingMesh != null) Destroy(_remainingMesh);
        DestroyBandPieces();
    }

    /// <summary>
    /// spawner와 pieceType을 저장하고 재사용 때 복원할 원본 메쉬와 충돌체를 보관한다.
    /// 밑둥용 충돌체를 비활성 상태로 준비하여 최초 벽의 렌더링과 충돌은 유지한다.
    /// </summary>
    public void Initialize(FishSpawner spawner, FishType pieceType)
    {
        _spawner = spawner;
        _pieceType = pieceType;
        _meshFilter = GetComponent<MeshFilter>();
        _originalMesh = _meshFilter.sharedMesh;
        _originalCollider = GetComponent<Collider>();
        if (_remainingCollider == null) _remainingCollider = gameObject.AddComponent<MeshCollider>();
        _remainingCollider.convex = true;
        _remainingCollider.enabled = false;
    }

    /// <summary>
    /// 보관한 원본 메쉬와 충돌체를 사용해 새 throw의 온전한 벽으로 복원한다.
    /// 입력값은 없으며 밑둥 메쉬와 띠 절단으로 만든 별도 장애물을 해제하고 재절단 가능한 상태로 되돌리되 분리된 조각은 유지한다.
    /// </summary>
    public void ResetWall()
    {
        _meshFilter.sharedMesh = _originalMesh;
        _remainingCollider.enabled = false;
        _remainingCollider.sharedMesh = null;
        _originalCollider.enabled = true;
        if (_remainingMesh != null) Destroy(_remainingMesh);
        _remainingMesh = null;
        DestroyBandPieces();
    }

    /// <summary>
    /// 월드 중심점 point, 법선 normal과 반폭 halfWidth로 정한 두 평행 경계면 사이의 띠를 벽에서 없앤다.
    /// 띠 안 조각은 사라지고 바깥 조각은 제자리에 고정 장애물로 남으며, 바깥 조각이 둘이면 하나는 별도 장애물로 만든다. 벽을 바꿨으면 true를 반환한다.
    /// </summary>
    public bool CutBand(Vector3 point, Vector3 normal, float halfWidth)
    {
        normal.Normalize();
        Mesh mesh = _meshFilter.sharedMesh;
        Vector3 localNormal = transform.localToWorldMatrix.transpose.MultiplyVector(normal).normalized;
        Vector3 positivePoint = transform.InverseTransformPoint(point + normal * halfWidth);
        Vector3 negativePoint = transform.InverseTransformPoint(point - normal * halfWidth);
        float minProjection = float.PositiveInfinity;
        float maxProjection = float.NegativeInfinity;
        foreach (Vector3 vertex in mesh.vertices)
        {
            float projection = Vector3.Dot(localNormal, vertex);
            minProjection = Mathf.Min(minProjection, projection);
            maxProjection = Mathf.Max(maxProjection, projection);
        }

        // 띠가 벽을 거의 덮지 않으면 자르지 않고, 경계가 벽 끝에 거의 붙어 있으면 그쪽 바깥 조각은 없는 것으로 본다.
        float inset = (maxProjection - minProjection) * MIN_CUT_FRACTION;
        float positiveBoundary = Vector3.Dot(localNormal, positivePoint);
        float negativeBoundary = Vector3.Dot(localNormal, negativePoint);
        if (positiveBoundary <= minProjection + inset || negativeBoundary >= maxProjection - inset) return false;

        List<Mesh> outside = new List<Mesh>(2);
        Mesh band = mesh;
        if (positiveBoundary < maxProjection - inset)
        {
            if (!WallMeshCutter.Slice(band, new Plane(localNormal, positivePoint), out Mesh positiveOutside, out Mesh rest)) return false;
            outside.Add(positiveOutside);
            band = rest;
        }
        if (negativeBoundary > minProjection + inset)
        {
            if (!WallMeshCutter.Slice(band, new Plane(localNormal, negativePoint), out Mesh middle, out Mesh negativeOutside))
            {
                foreach (Mesh piece in outside) Destroy(piece);
                if (band != mesh) Destroy(band);
                return false;
            }
            outside.Add(negativeOutside);
            if (band != mesh) Destroy(band);
            band = middle;
        }
        // 띠 안 조각은 증발시킨다.
        if (band != mesh) Destroy(band);

        Mesh previous = _remainingMesh;
        _remainingMesh = outside.Count > 0 ? outside[0] : null;
        _meshFilter.sharedMesh = _remainingMesh;
        _originalCollider.enabled = false;
        _remainingCollider.sharedMesh = null;
        _remainingCollider.sharedMesh = _remainingMesh;
        _remainingCollider.enabled = _remainingMesh != null;
        if (outside.Count > 1) CreateBandPiece(outside[1]);
        if (previous != null) Destroy(previous);
        return true;
    }

    /// <summary>
    /// 월드 접촉점 point와 절단면 normal로 벽을 두 개의 밀폐된 조각으로 나눈다.
    /// 위 조각만 움직이는 Wall 물고기로 분리하고 아래 조각은 원본의 고정 장애물로 유지한다.
    /// </summary>
    public bool Cut(Vector3 point, Vector3 normal)
    {
        if (normal.sqrMagnitude < 0.000001f) return false;

        normal.Normalize();
        if (Mathf.Abs(normal.y) < 0.0001f) normal = Vector3.up;
        Mesh mesh = _meshFilter.sharedMesh;
        Vector3 localPoint = transform.InverseTransformPoint(point);
        Vector3 localNormal = transform.localToWorldMatrix.transpose.MultiplyVector(normal).normalized;
        float minProjection = float.PositiveInfinity;
        float maxProjection = float.NegativeInfinity;
        foreach (Vector3 vertex in mesh.vertices)
        {
            float projection = Vector3.Dot(localNormal, vertex);
            minProjection = Mathf.Min(minProjection, projection);
            maxProjection = Mathf.Max(maxProjection, projection);
        }

        // 모서리 접촉에서는 축별 보정 대신 절단면 방향으로 여유를 확보해 부피 없는 조각을 피한다.
        float inset = (maxProjection - minProjection) * MIN_CUT_FRACTION;
        float pointProjection = Vector3.Dot(localNormal, localPoint);
        float cutProjection = Mathf.Clamp(pointProjection, minProjection + inset, maxProjection - inset);
        localPoint += localNormal * (cutProjection - pointProjection);
        if (!WallMeshCutter.Slice(mesh, new Plane(localNormal, localPoint), out Mesh positive, out Mesh negative)) return false;

        bool positiveIsUpper = GetWorldCentroid(positive).y >= GetWorldCentroid(negative).y;
        Mesh upper = positiveIsUpper ? positive : negative;
        Mesh previous = _remainingMesh;
        _remainingMesh = positiveIsUpper ? negative : positive;
        CreatePiece(upper, positiveIsUpper ? normal : -normal);

        _meshFilter.sharedMesh = _remainingMesh;
        _originalCollider.enabled = false;
        _remainingCollider.sharedMesh = null;
        _remainingCollider.sharedMesh = _remainingMesh;
        _remainingCollider.enabled = true;
        if (previous != null) Destroy(previous);
        return true;
    }

    /// <summary>
    /// 밀폐된 mesh의 삼각형 부피를 사용해 월드 좌표의 무게중심을 반환한다.
    /// 원본 변환을 적용하여 기울어진 절단에서도 위쪽 조각을 판정한다.
    /// </summary>
    private Vector3 GetWorldCentroid(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        float volume = 0f;
        Vector3 weightedCenter = Vector3.zero;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]];
            Vector3 b = vertices[triangles[i + 1]];
            Vector3 c = vertices[triangles[i + 2]];
            float tetrahedron = Vector3.Dot(a, Vector3.Cross(b, c));
            volume += tetrahedron;
            weightedCenter += (a + b + c) * (tetrahedron * 0.25f);
        }
        return transform.TransformPoint(weightedCenter / volume);
    }

    /// <summary>
    /// mesh와 separation 방향을 사용해 위 조각을 중력과 분리 속도를 가진 Wall 물고기로 만든다.
    /// 원본 머티리얼과 월드 크기를 유지하며 접촉과 메쉬 수명은 Fish가 관리한다.
    /// </summary>
    private void CreatePiece(Mesh mesh, Vector3 separation)
    {
        Vector3 center = mesh.bounds.center;
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] -= center;
        mesh.vertices = vertices;
        mesh.RecalculateBounds();

        GameObject piece = new GameObject(_pieceType.DisplayName) { layer = gameObject.layer };
        piece.transform.SetParent(transform.parent, false);
        piece.transform.localScale = transform.localScale;
        piece.transform.rotation = transform.rotation;
        piece.transform.position = transform.TransformPoint(center) + separation * 0.04f;
        piece.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = piece.AddComponent<MeshRenderer>();
        MeshRenderer sourceRenderer = GetComponent<MeshRenderer>();
        renderer.sharedMaterials = sourceRenderer.sharedMaterials;
        renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        renderer.receiveShadows = sourceRenderer.receiveShadows;
        MeshCollider collider = piece.AddComponent<MeshCollider>();
        collider.convex = true;
        collider.sharedMesh = mesh;
        collider.isTrigger = true;
        Rigidbody body = piece.AddComponent<Rigidbody>();
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        Fish fish = piece.AddComponent<Fish>();
        fish.InitializeWall(_spawner, _pieceType, mesh);
        _spawner.RegisterCutPiece(fish);
        body.AddForce(separation * EJECTION_SPEED + Vector3.up * UPWARD_SPEED, ForceMode.VelocityChange);
    }

    /// <summary>
    /// 띠 절단으로 남은 두 번째 바깥 조각 mesh를 원본과 같은 자리의 고정 장애물로 만든다.
    /// 원본의 변환, 레이어, 머티리얼과 조각 종류를 사용하며 만든 장애물과 메시는 ResetWall에서 함께 제거한다.
    /// </summary>
    private void CreateBandPiece(Mesh mesh)
    {
        GameObject piece = new GameObject(name) { layer = gameObject.layer };
        piece.SetActive(false);
        piece.transform.SetParent(transform.parent, false);
        piece.transform.SetPositionAndRotation(transform.position, transform.rotation);
        piece.transform.localScale = transform.localScale;
        piece.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = piece.AddComponent<MeshRenderer>();
        MeshRenderer sourceRenderer = GetComponent<MeshRenderer>();
        renderer.sharedMaterials = sourceRenderer.sharedMaterials;
        renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        renderer.receiveShadows = sourceRenderer.receiveShadows;
        MeshCollider collider = piece.AddComponent<MeshCollider>();
        collider.convex = true;
        collider.sharedMesh = mesh;
        piece.AddComponent<CuttableWall>().Initialize(_spawner, _pieceType);
        piece.SetActive(true);
        _bandPieces.Add(piece);
        _bandMeshes.Add(mesh);
    }

    /// <summary>
    /// 띠 절단으로 만든 별도 장애물과 그 메시를 제거한다.
    /// 입력값 없이 _bandPieces와 _bandMeshes를 비운다.
    /// </summary>
    private void DestroyBandPieces()
    {
        foreach (GameObject piece in _bandPieces)
        {
            // 씬 종료 때는 별도 장애물이 먼저 파괴되어 있을 수 있다.
            if (piece != null) Destroy(piece);
        }
        foreach (Mesh mesh in _bandMeshes) Destroy(mesh);
        _bandPieces.Clear();
        _bandMeshes.Clear();
    }
}
