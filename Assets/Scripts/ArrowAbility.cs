using System;

using UnityEngine;

public sealed class ArrowAbility : FishAbility
{
    [Header("Perfect Jump")]
    [SerializeField, Min(0f)] private float _perfectBoostSpeed = 15f;
    [SerializeField, Min(0f)] private float _visualRotationLockDuration = 3f;
    private BlackHoleEnding _blackHoleTarget;
    private Transform _body;
    private Quaternion _lockedWorldRotation;
    private MeshFilter[] _filters = Array.Empty<MeshFilter>();
    private MeshRenderer[] _renderers = Array.Empty<MeshRenderer>();
    private bool[] _originalForceRenderingOff = Array.Empty<bool>();
    private MaterialPropertyBlock _properties;
    private float _lockUntil = float.NegativeInfinity;

    /// <summary>
    /// 새 투척 전에 기본 Z 90도 자세를 구성하고 블랙홀과 렌더링 고정 상태를 준비한다.
    /// 입력값 없이 충돌 형상 생성 전에 초기 방향만 설정하며 비행 중 시각 고정은 물리 상태를 변경하지 않는다.
    /// </summary>
    public override void OnPrepareThrow()
    {
        RestoreRendering();
        transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        _blackHoleTarget = FindFirstObjectByType<BlackHoleEnding>(FindObjectsInactive.Include);
    }

    /// <summary>
    /// Perfect 판정이면 블랙홀 방향의 추가 속도를 주고 외형의 현재 월드 회전을 잠시 고정한다.
    /// context와 judge를 사용하며 플레이어 속도와 외형 고정 시간만 변경하고 물리 각속도는 유지한다.
    /// </summary>
    public override void OnJudgeSuccess(ThrowContext context, SkipJudge judge)
    {
        if (judge != SkipJudge.Perfect) return;
        _body = context.Body;
        if (Time.time >= _lockUntil)
        {
            RestoreRendering();
            _lockedWorldRotation = _body.rotation;
            _filters = _body.GetComponentsInChildren<MeshFilter>(true);
            _renderers = new MeshRenderer[_filters.Length];
            _originalForceRenderingOff = new bool[_filters.Length];
            _properties = new MaterialPropertyBlock();
            for (int i = 0; i < _filters.Length; i++)
            {
                _renderers[i] = _filters[i].GetComponent<MeshRenderer>();
                _originalForceRenderingOff[i] = _renderers[i].forceRenderingOff;
                _renderers[i].forceRenderingOff = true;
            }
        }
        _lockUntil = Time.time + _visualRotationLockDuration;
        Vector3 direction = _blackHoleTarget.transform.position - context.Player.transform.position;
        context.Player.AddVelocity(direction.normalized * _perfectBoostSpeed);
    }

    void LateUpdate()
    {
        if (Time.time >= _lockUntil)
        {
            RestoreRendering();
            return;
        }
        Quaternion delta = _lockedWorldRotation * Quaternion.Inverse(_body.rotation);
        Matrix4x4 correction = Matrix4x4.Translate(_body.position) * Matrix4x4.Rotate(delta)
            * Matrix4x4.Translate(-_body.position);
        for (int i = 0; i < _filters.Length; i++)
        {
            MeshRenderer renderer = _renderers[i];
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || _originalForceRenderingOff[i]) continue;
            Matrix4x4 matrix = correction * _filters[i].transform.localToWorldMatrix;
            Mesh mesh = _filters[i].sharedMesh;
            Bounds bounds = mesh.bounds;
            Vector3 e = bounds.extents;
            Vector3 extents = new Vector3(
                Mathf.Abs(matrix.m00) * e.x + Mathf.Abs(matrix.m01) * e.y + Mathf.Abs(matrix.m02) * e.z,
                Mathf.Abs(matrix.m10) * e.x + Mathf.Abs(matrix.m11) * e.y + Mathf.Abs(matrix.m12) * e.z,
                Mathf.Abs(matrix.m20) * e.x + Mathf.Abs(matrix.m21) * e.y + Mathf.Abs(matrix.m22) * e.z);
            renderer.GetPropertyBlock(_properties);
            Material[] materials = renderer.sharedMaterials;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                var parameters = new RenderParams(materials[Mathf.Min(subMesh, materials.Length - 1)])
                {
                    layer = renderer.gameObject.layer,
                    matProps = _properties,
                    shadowCastingMode = renderer.shadowCastingMode,
                    receiveShadows = renderer.receiveShadows,
                    lightProbeUsage = renderer.lightProbeUsage,
                    reflectionProbeUsage = renderer.reflectionProbeUsage,
                    renderingLayerMask = renderer.renderingLayerMask,
                    rendererPriority = renderer.rendererPriority,
                    motionVectorMode = renderer.motionVectorGenerationMode,
                    worldBounds = new Bounds(matrix.MultiplyPoint3x4(bounds.center), extents * 2f),
                };
                Graphics.RenderMesh(parameters, mesh, subMesh, matrix);
            }
        }
    }

    void OnDisable()
    {
        RestoreRendering();
    }

    /// <summary>
    /// 입력값 없이 원본 Renderer의 렌더링 제외 상태를 복원하고 고정 그림의 캐시를 비운다.
    /// 선택 변경과 파괴 시에도 Transform, 각속도, 충돌체 및 게임플레이 상태는 변경하지 않는다.
    /// </summary>
    private void RestoreRendering()
    {
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null) _renderers[i].forceRenderingOff = _originalForceRenderingOff[i];
        }
        _filters = Array.Empty<MeshFilter>();
        _renderers = Array.Empty<MeshRenderer>();
        _originalForceRenderingOff = Array.Empty<bool>();
        _lockUntil = float.NegativeInfinity;
    }
}
