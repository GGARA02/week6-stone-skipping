using System;
using System.Collections.Generic;

using UnityEngine;

using TMPro;

// 명령서 보드의 특수 동작. 던진 뒤 처음으로 SPACE 판정에 성공하면
// 붙어 있던 명령서를 모두 내리고 같은 자리에 숨겨 둔 에러 메시지를 띄운다.
// 생성기는 큐브의 양 끝 꼭짓점만 휘고 글자는 휘지 않으므로, 던지기 전까지 큐브를 잘게 나누고 글자는 생성기와 같은 공식으로 직접 휜다.
public class BoardAbility : FishAbility
{
    // 큐브를 몸 길이 방향으로 나누는 수
    private const int FLOP_SLICES = 32;

    [Header("명령서")]
    [Tooltip("처음 판정 성공 때 끄는 명령서 오브젝트")]
    [SerializeField]
    private GameObject[] _instructions;
    [Tooltip("처음 판정 성공 때 켜는 에러 메시지 오브젝트. 프리팹에서는 꺼 둔다")]
    [SerializeField]
    private GameObject[] _errorMessages;

    [Header("글자 꾸물거림 (던지기 전까지)")]
    [Tooltip("초당 파닥이는 횟수. 생성기와 같아야 글자가 판과 함께 휜다")]
    [SerializeField]
    private float _flopFrequency = 6f;
    [Tooltip("머리와 꼬리가 휘는 정도 (몸길이 비율). 생성기와 같아야 한다")]
    [SerializeField]
    private float _flopBend = 0.35f;
    [Tooltip("몸 전체가 들썩이는 높이 (몸길이 비율). 생성기와 같아야 한다")]
    [SerializeField]
    private float _flopHop = 0.08f;
    private readonly List<Mesh> _slicedMeshes = new List<Mesh>();
    private TMP_Text[] _texts = Array.Empty<TMP_Text>();
    private Vector3[][][] _textBaseVertices;
    private Transform _body;
    private float _flopPivot;
    private float _flopLength;
    private bool _isFlopping;

    [Header("상태")]
    private bool _errorShown;

    void Start()
    {
        // 던질 물고기로 만들 때 메시 필터가 빈 복사본으로 바뀌어 글자가 사라지므로 원래 글자 메시로 되돌린다.
        foreach (TextMeshPro text in GetComponentsInChildren<TextMeshPro>())
        {
            text.meshFilter.sharedMesh = text.mesh;
        }
    }

    void Update()
    {
        if (!_isFlopping) return;
        FlopTexts(Time.time);
    }

    void OnDestroy()
    {
        // 생성기가 복사해 간 뒤 남은 나눈 메시는 이 보드가 만들었으므로 함께 없앤다.
        foreach (Mesh mesh in _slicedMeshes)
        {
            Destroy(mesh);
        }
    }

    /// <summary>
    /// 던질 보드로 준비될 때 큐브 메시를 잘게 나누고 글자 꾸물거림을 켠다. 생성기는 이 직후 메시를 복사해 휜다.
    /// 부모 생성기의 몸 좌표계와 자식 메시를 사용하며, MeshFilter 메시, 꾸물거림 기준, _isFlopping을 변경한다.
    /// </summary>
    public override void OnPrepareThrow()
    {
        _body = transform.parent.GetComponent<FishMeshGenerator>().FishBody;
        _texts = GetComponentsInChildren<TMP_Text>(true);
        _textBaseVertices = new Vector3[_texts.Length][][];

        // 생성기와 같은 메시로 몸 길이 범위를 구해야 글자가 판과 같은 기준으로 휜다. 아직 비어 있는 TMP 글자 메시는 범위에 영향이 없다.
        List<MeshFilter> filters = new List<MeshFilter>();
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.TryGetComponent(out TMP_Text _)) continue;

            filters.Add(filter);
            Matrix4x4 toBody = _body.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
            {
                float z = toBody.MultiplyPoint3x4(vertex).z;
                minZ = Mathf.Min(minZ, z);
                maxZ = Mathf.Max(maxZ, z);
            }
        }
        _flopPivot = (minZ + maxZ) * 0.5f;
        _flopLength = Mathf.Max(maxZ - minZ, 0.0001f);

        // 큐브는 양 끝 꼭짓점만 휘어 평평하게 남으므로, 곡선을 따르는 글자가 파묻히지 않게 잘게 나눠 생성기에 넘긴다.
        foreach (MeshFilter filter in filters)
        {
            filter.sharedMesh = SliceMesh(filter, _flopLength / FLOP_SLICES);
            _slicedMeshes.Add(filter.sharedMesh);
        }
        _isFlopping = true;
    }

    /// <summary>
    /// 던진 직후 한 번 글자 꾸물거림을 멈추고 휘지 않은 글자로 되돌린다. 생성기도 던질 때 판을 원래 모양으로 되돌린다.
    /// _isFlopping과 기억한 글자 정점을 사용하며, context와 dt는 쓰지 않는다. _isFlopping과 글자 정점을 변경한다.
    /// </summary>
    public override void UpdateFlight(ThrowContext context, float dt)
    {
        if (!_isFlopping) return;

        _isFlopping = false;
        for (int i = 0; i < _texts.Length; i++)
        {
            // 한 번도 휘지 않은 글자는 되돌릴 것이 없다. 다시 만들면 휘지 않은 정점으로 돌아간다.
            if (_textBaseVertices[i] == null) continue;
            _texts[i].ForceMeshUpdate();
        }
    }

    /// <summary>
    /// 던진 보드가 처음으로 SPACE 판정에 성공하면 명령서를 끄고 에러 메시지를 켠다.
    /// _instructions, _errorMessages, _errorShown을 사용하며, context와 judge는 쓰지 않는다. 각 오브젝트 활성 상태와 _errorShown을 변경한다.
    /// </summary>
    public override void OnJudgeSuccess(ThrowContext context, SkipJudge judge)
    {
        if (_errorShown) return;

        _errorShown = true;
        foreach (GameObject instruction in _instructions)
        {
            instruction.SetActive(false);
        }
        foreach (GameObject errorMessage in _errorMessages)
        {
            errorMessage.SetActive(true);
        }
    }

    /// <summary>
    /// 메시 삼각형을 몸 길이(z) 방향 step 간격 평면마다 잘라 휠 때 곡선을 따르는 새 메시를 만든다.
    /// 모든 큐브가 같은 평면에서 잘려 겹쳐 놓인 부품이 같은 모양으로 휜다.
    /// filter의 메시와 몸 좌표계 변환, step을 사용하며, 잘린 새 Mesh를 반환한다.
    /// </summary>
    private Mesh SliceMesh(MeshFilter filter, float step)
    {
        Mesh source = filter.sharedMesh;
        Matrix4x4 toBody = _body.worldToLocalMatrix * filter.transform.localToWorldMatrix;
        List<Vector3> positions = new List<Vector3>(source.vertices);
        List<Vector2> uvs = new List<Vector2>(source.uv);
        List<Color> colors = new List<Color>(source.colors);
        List<float> depths = new List<float>(positions.Count);
        foreach (Vector3 position in positions)
        {
            depths.Add(toBody.MultiplyPoint3x4(position).z);
        }

        float epsilon = step * 0.0001f;
        Dictionary<(int, int, int), int> cuts = new Dictionary<(int, int, int), int>();
        List<int> outline = new List<int>();
        List<int>[] submeshes = new List<int>[source.subMeshCount];
        for (int s = 0; s < submeshes.Length; s++)
        {
            int[] triangles = source.GetTriangles(s);
            submeshes[s] = new List<int>(triangles.Length);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                // 삼각형 둘레를 따라 꼭짓점과 평면 교차점을 차례로 모은다. 이웃 삼각형과 같은 모서리의 교차점은 정점 하나를 같이 써 틈이 생기지 않는다.
                outline.Clear();
                for (int e = 0; e < 3; e++)
                {
                    int from = triangles[t + e];
                    int to = triangles[t + (e + 1) % 3];
                    outline.Add(from);
                    int low = Mathf.Min(from, to);
                    int high = Mathf.Max(from, to);
                    int first = Mathf.FloorToInt(Mathf.Min(depths[from], depths[to]) / step) + 1;
                    int last = Mathf.CeilToInt(Mathf.Max(depths[from], depths[to]) / step) - 1;
                    for (int n = 0; n <= last - first; n++)
                    {
                        int plane = depths[from] < depths[to] ? first + n : last - n;
                        if (!cuts.TryGetValue((low, high, plane), out int cut))
                        {
                            float blend = (plane * step - depths[low]) / (depths[high] - depths[low]);
                            cut = positions.Count;
                            positions.Add(Vector3.Lerp(positions[low], positions[high], blend));
                            if (uvs.Count > 0) uvs.Add(Vector2.Lerp(uvs[low], uvs[high], blend));
                            if (colors.Count > 0) colors.Add(Color.Lerp(colors[low], colors[high], blend));
                            depths.Add(plane * step);
                            cuts.Add((low, high, plane), cut);
                        }
                        outline.Add(cut);
                    }
                }

                // 평면 사이 띠마다 띠 안에 든 둘레 점이 볼록 다각형을 이루므로 부채꼴로 나눈다.
                float bottom = Mathf.Min(depths[triangles[t]], Mathf.Min(depths[triangles[t + 1]], depths[triangles[t + 2]]));
                float top = Mathf.Max(depths[triangles[t]], Mathf.Max(depths[triangles[t + 1]], depths[triangles[t + 2]]));
                int firstPlane = Mathf.FloorToInt(bottom / step) + 1;
                int lastPlane = Mathf.CeilToInt(top / step) - 1;
                float lower = bottom;
                for (int plane = firstPlane; plane <= lastPlane + 1; plane++)
                {
                    float upper = plane <= lastPlane ? plane * step : top;
                    int start = -1;
                    int previous = -1;
                    foreach (int point in outline)
                    {
                        if (depths[point] < lower - epsilon || depths[point] > upper + epsilon) continue;

                        if (start < 0)
                        {
                            start = point;
                        }
                        else if (previous != start)
                        {
                            submeshes[s].Add(start);
                            submeshes[s].Add(previous);
                            submeshes[s].Add(point);
                        }
                        previous = point;
                    }
                    lower = upper;
                }
            }
        }

        Mesh mesh = new Mesh { name = source.name + " Sliced" };
        mesh.SetVertices(positions);
        if (uvs.Count > 0) mesh.SetUVs(0, uvs);
        if (colors.Count > 0) mesh.SetColors(colors);
        mesh.subMeshCount = submeshes.Length;
        for (int s = 0; s < submeshes.Length; s++)
        {
            mesh.SetTriangles(submeshes[s], s);
        }
        mesh.RecalculateNormals();
        if (uvs.Count > 0) mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// 글자를 생성기의 파닥임과 같은 공식으로 휜다. 글자 사각형은 잘게 나눌 수 없어 받침 큐브 아래로 파고들지 않을 만큼 글자마다 들어 올린다.
    /// time과 꾸물거림 설정, 꾸물거림 기준, 기억한 원래 글자 정점을 사용하며, TMP 글자 정점을 변경한다.
    /// </summary>
    private void FlopTexts(float time)
    {
        // 생성기와 같은 시간과 공식을 써야 판과 같은 박자로 휜다.
        float phase = time * _flopFrequency * Mathf.PI * 2f;
        float strength = 0.35f + 0.65f * Mathf.PerlinNoise(time * 1.7f, 0.37f);
        float bend = Mathf.Sin(phase) * strength * _flopBend * _flopLength;
        float hop = Mathf.Abs(Mathf.Sin(phase * 0.5f)) * strength * _flopHop * _flopLength;
        // 잘게 나눈 큐브는 오목하게 휠 때 평면 사이에서 곡선보다 이만큼까지 위로 뜬다.
        float sliceGap = Mathf.Max(0f, bend) * 0.25f / (FLOP_SLICES * FLOP_SLICES);
        Vector3[] quad = new Vector3[4];
        for (int i = 0; i < _texts.Length; i++)
        {
            TMP_Text text = _texts[i];
            // 꺼진 글자는 TMP가 메시를 만들지 않아 휠 정점이 없다.
            if (!text.isActiveAndEnabled) continue;

            CacheTextBaseVertices(i);
            TMP_MeshInfo[] meshInfo = text.textInfo.meshInfo;
            Matrix4x4 toBody = _body.worldToLocalMatrix * text.transform.localToWorldMatrix;
            Matrix4x4 fromBody = toBody.inverse;
            for (int m = 0; m < _textBaseVertices[i].Length; m++)
            {
                Vector3[] baseVertices = _textBaseVertices[i][m];
                // TMP는 글자마다 꼭짓점 4개짜리 사각형을 만든다.
                for (int j = 0; j < baseVertices.Length; j += 4)
                {
                    float centerAlong = 0f;
                    float cornerBend = 0f;
                    for (int k = 0; k < 4; k++)
                    {
                        quad[k] = toBody.MultiplyPoint3x4(baseVertices[j + k]);
                        float along = (quad[k].z - _flopPivot) / _flopLength;
                        centerAlong += along * 0.25f;
                        cornerBend += along * along * bend * 0.25f;
                    }
                    // 사각형은 꼭짓점만 곡선을 따라 볼록하게 휠 때 가운데가 곡선 아래로 처지므로 처진 만큼 더한다.
                    float lift = hop + sliceGap + Mathf.Max(0f, centerAlong * centerAlong * bend - cornerBend);
                    for (int k = 0; k < 4; k++)
                    {
                        float along = (quad[k].z - _flopPivot) / _flopLength;
                        quad[k].y += along * along * bend + lift;
                        meshInfo[m].vertices[j + k] = fromBody.MultiplyPoint3x4(quad[k]);
                    }
                }
            }
            text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        }
    }

    /// <summary>
    /// 글자의 원래 정점을 처음 휠 때와 TMP가 글자 메시를 다시 만들어 정점 수가 바뀌었을 때 기억한다.
    /// index 위치의 _texts를 사용하며, _textBaseVertices[index]를 변경한다.
    /// </summary>
    private void CacheTextBaseVertices(int index)
    {
        TMP_Text text = _texts[index];
        Vector3[][] cached = _textBaseVertices[index];
        bool valid = cached != null && cached.Length == text.textInfo.materialCount;
        for (int m = 0; valid && m < cached.Length; m++)
        {
            valid = cached[m].Length == text.textInfo.meshInfo[m].vertexCount;
        }
        if (valid) return;

        // TMP는 렌더 직전에 글자 메시를 만들므로 처음에는 정점이 비어 있어 지금 만들게 한다.
        text.ForceMeshUpdate();
        cached = new Vector3[text.textInfo.materialCount][];
        for (int m = 0; m < cached.Length; m++)
        {
            cached[m] = new Vector3[text.textInfo.meshInfo[m].vertexCount];
            Array.Copy(text.textInfo.meshInfo[m].vertices, cached[m], cached[m].Length);
        }
        _textBaseVertices[index] = cached;
    }
}
