using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 생성 메시와 물고기 프리팹의 메시와 머티리얼로 선택 미리보기 텍스처를 보관한다.
/// 게임 동작, 충돌체와 애니메이션은 생성하지 않는다. 물처럼 셰이더가 움직이는 항목만 모델을 남겨 다시 그린다.
/// </summary>
public sealed class FishSelectionPreview : IDisposable
{
    private const int PREVIEW_LAYER = 2;
    private const int TEXTURE_WIDTH = 192;
    private const int TEXTURE_HEIGHT = 128;

    private readonly Dictionary<Fish, RenderTexture> _textures = new Dictionary<Fish, RenderTexture>();
    private readonly Dictionary<Fish, AnimatedModel> _animatedModels = new Dictionary<Fish, AnimatedModel>();
    private readonly GameObject _root;
    private readonly Camera _camera;
    private readonly Material _material;
    private readonly bool _useOriginalMaterials;

    public RenderTexture DefaultFishTexture { get; private set; }

    /// <summary>
    /// useOriginalMaterials로 원본 색 사용 여부를 정하고 게임 공간에서 떨어진 미리보기 카메라를 만든다.
    /// 생성한 카메라와 미획득 항목용 회색 머티리얼을 렌더링 상태에 저장한다.
    /// </summary>
    public FishSelectionPreview(bool useOriginalMaterials = false)
    {
        _useOriginalMaterials = useOriginalMaterials;
        _root = new GameObject("Fish Selection Preview") { hideFlags = HideFlags.HideAndDontSave };
        _root.transform.position = new Vector3(10000f, 10000f, 10000f);
        GameObject cameraObject = new GameObject("Preview Camera");
        cameraObject.transform.SetParent(_root.transform, false);
        _camera = cameraObject.AddComponent<Camera>();
        _camera.enabled = false;
        _camera.orthographic = true;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = Color.clear;
        _camera.cullingMask = 1 << PREVIEW_LAYER;
        _camera.nearClipPlane = 0.01f;
        _camera.farClipPlane = 100f;
        _camera.aspect = (float)TEXTURE_WIDTH / TEXTURE_HEIGHT;
        _material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        _material.SetColor("_BaseColor", new Color(0.6f, 0.6f, 0.6f, 0.55f));
        _material.SetFloat("_Surface", 1f);
        _material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        _material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        _material.SetFloat("_ZWrite", 0f);
        _material.SetFloat("_Cull", (float)CullMode.Off);
        _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _material.SetOverrideTag("RenderType", "Transparent");
        _material.renderQueue = (int)RenderQueue.Transparent;
    }

    /// <summary>
    /// prefab을 키로 준비된 미리보기 텍스처를 조회한다.
    /// 보관된 RenderTexture를 반환하며 아직 준비하지 않았다면 null을 반환한다.
    /// </summary>
    public RenderTexture GetTexture(Fish prefab)
    {
        return _textures.TryGetValue(prefab, out RenderTexture texture) ? texture : null;
    }

    /// <summary>
    /// prefab을 사용해 이전 메쉬의 미리보기 텍스처를 해제한다.
    /// 캐시에서 제거하여 다음 준비 시 마지막 접촉한 Wall 메쉬를 다시 렌더링한다.
    /// </summary>
    public void InvalidateTexture(Fish prefab)
    {
        if (_animatedModels.Remove(prefab, out AnimatedModel animated)) UnityEngine.Object.Destroy(animated.Model);
        if (!_textures.Remove(prefab, out RenderTexture texture)) return;
        texture.Release();
        UnityEngine.Object.Destroy(texture);
    }

    /// <summary>
    /// prefab의 메시, 원본 머티리얼과 변환 계층을 사용해 투척 자세의 미리보기를 렌더링한다.
    /// OnGUI 밖에서 호출하며, prefab별 투명 배경 RenderTexture를 캐시에 저장한다.
    /// animated이고 원본 머티리얼을 쓰면 모델과 카메라 구도를 _animatedModels에 남겨 RefreshAnimatedTextures로 다시 그린다.
    /// </summary>
    public void PrepareTexture(Fish prefab, bool animated = false)
    {
        if (_textures.ContainsKey(prefab)) return;

        GameObject model = new GameObject("Preview Model");
        Transform visualRoot = new GameObject("Preview Visual Root").transform;
        visualRoot.SetParent(model.transform, false);
        visualRoot.localScale = prefab.transform.localScale;
        visualRoot.localRotation = prefab.Body == prefab.transform
            ? prefab.transform.localRotation
            : Quaternion.Inverse(prefab.Body.localRotation);
        Dictionary<Transform, Transform> transforms = new Dictionary<Transform, Transform>
        {
            { prefab.transform, visualRoot },
        };
        foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>())
        {
            if (!filter.TryGetComponent<MeshRenderer>(out MeshRenderer renderer)) continue;
            AddMesh(CopyTransform(filter.transform, transforms), filter.sharedMesh,
                renderer.sharedMaterials);
        }
        foreach (SkinnedMeshRenderer renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            AddMesh(CopyTransform(renderer.transform, transforms), renderer.sharedMesh, renderer.sharedMaterials);
        }

        // 회색 미획득 미리보기는 움직일 것이 없으므로 원본 머티리얼일 때만 모델을 남긴다.
        bool keepModel = animated && _useOriginalMaterials;
        _textures.Add(prefab, RenderModel(model, keepModel));
        if (keepModel)
        {
            _animatedModels.Add(prefab, new AnimatedModel(model, _camera.transform.position, _camera.orthographicSize));
        }
    }

    /// <summary>
    /// 남겨 둔 움직이는 모델을 저장한 카메라 구도로 각자의 RenderTexture에 다시 렌더링한다.
    /// _animatedModels와 _textures를 사용하며, 미리보기 카메라 위치와 크기, 해당 RenderTexture 내용을 변경한다.
    /// </summary>
    public void RefreshAnimatedTextures()
    {
        foreach (KeyValuePair<Fish, AnimatedModel> pair in _animatedModels)
        {
            AnimatedModel animated = pair.Value;
            animated.Model.SetActive(true);
            _camera.transform.position = animated.CameraPosition;
            _camera.orthographicSize = animated.OrthographicSize;
            _camera.targetTexture = _textures[pair.Key];
            _camera.Render();
            _camera.targetTexture = null;
            animated.Model.SetActive(false);
        }
    }

    /// <summary>
    /// fishMesh와 originalMaterials를 사용해 기본 투척물의 미리보기를 렌더링한다.
    /// 이전 기본 텍스처를 해제하고 새 결과를 DefaultFishTexture에 저장한다.
    /// </summary>
    public void PrepareDefaultFishTexture(Mesh fishMesh, Material[] originalMaterials = null)
    {
        if (DefaultFishTexture != null)
        {
            DefaultFishTexture.Release();
            UnityEngine.Object.Destroy(DefaultFishTexture);
        }
        GameObject model = new GameObject("Default Fish Preview Model");
        AddMesh(model.transform, fishMesh, originalMaterials);
        DefaultFishTexture = RenderModel(model);
    }

    /// <summary>
    /// source와 transforms 캐시를 사용해 부모 계층과 로컬 변환을 그대로 복사한다.
    /// 음수·비균일 스케일을 보존한 미리보기 Transform을 반환하고 캐시에 저장한다.
    /// </summary>
    private static Transform CopyTransform(Transform source, Dictionary<Transform, Transform> transforms)
    {
        if (transforms.TryGetValue(source, out Transform copy)) return copy;

        Transform parent = CopyTransform(source.parent, transforms);
        copy = new GameObject(source.name).transform;
        copy.SetParent(parent, false);
        copy.localPosition = source.localPosition;
        copy.localRotation = source.localRotation;
        copy.localScale = source.localScale;
        transforms.Add(source, copy);
        return copy;
    }

    /// <summary>
    /// model의 메시 경계로 크기와 카메라를 맞춰 미리보기를 렌더링한다.
    /// keepModel이면 렌더링 전용 모델을 끄기만 하고, 아니면 제거한 뒤 투명 배경 RenderTexture를 반환한다.
    /// </summary>
    private RenderTexture RenderModel(GameObject model, bool keepModel = false)
    {
        model.transform.localRotation = Quaternion.Euler(12f, 65f, 0f);
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        Bounds bounds = new Bounds(model.transform.position, Vector3.zero);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (i == 0) bounds = renderers[i].bounds;
            else bounds.Encapsulate(renderers[i].bounds);
        }
        float scale = 2f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 0.01f);
        model.transform.localScale = Vector3.one * scale;
        model.transform.localPosition = -bounds.center * scale;
        model.transform.SetParent(_root.transform, false);
        bounds = new Bounds(_root.transform.position, bounds.size * scale);
        _camera.transform.position = bounds.center - Vector3.forward * (bounds.extents.z + 10f);
        _camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x / _camera.aspect, 0.01f) * 1.2f;
        RenderTexture texture = new RenderTexture(TEXTURE_WIDTH, TEXTURE_HEIGHT, 24, RenderTextureFormat.ARGB32);
        texture.Create();
        _camera.targetTexture = texture;
        _camera.Render();
        _camera.targetTexture = null;
        model.SetActive(false);
        if (!keepModel) UnityEngine.Object.Destroy(model);
        return texture;
    }

    /// <summary>
    /// mesh와 originalMaterials로 parent의 로컬 원점에 렌더링 전용 자식 메시를 만든다.
    /// 보유용은 원본 머티리얼, 미획득용은 회색 머티리얼을 사용하는 렌더러를 추가한다.
    /// </summary>
    private void AddMesh(Transform parent, Mesh mesh, Material[] originalMaterials)
    {
        GameObject part = new GameObject("Preview Mesh") { layer = PREVIEW_LAYER };
        part.transform.SetParent(parent, false);
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = part.AddComponent<MeshRenderer>();
        Material[] materials = new Material[mesh.subMeshCount];
        for (int i = 0; i < materials.Length; i++) materials[i] = _material;
        renderer.sharedMaterials = _useOriginalMaterials ? originalMaterials : materials;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    /// <summary>
    /// 입력값 없이 캐시된 텍스처와 카메라, 머티리얼을 해제한다.
    /// 미리보기에서 생성한 Unity 오브젝트를 제거하고 텍스처 캐시를 비운다.
    /// </summary>
    public void Dispose()
    {
        foreach (RenderTexture texture in _textures.Values)
        {
            texture.Release();
            UnityEngine.Object.Destroy(texture);
        }
        _textures.Clear();
        // 남겨 둔 모델은 _root 아래에 있어 _root와 함께 제거된다.
        _animatedModels.Clear();
        if (DefaultFishTexture != null)
        {
            DefaultFishTexture.Release();
            UnityEngine.Object.Destroy(DefaultFishTexture);
            DefaultFishTexture = null;
        }
        UnityEngine.Object.Destroy(_material);
        UnityEngine.Object.Destroy(_root);
    }

    // 다시 그릴 미리보기 모델과 처음 렌더링할 때 맞춘 카메라 구도.
    private sealed class AnimatedModel
    {
        public GameObject Model { get; }
        public Vector3 CameraPosition { get; }
        public float OrthographicSize { get; }

        /// <summary>
        /// model과 cameraPosition, orthographicSize를 받아 다시 그릴 때 쓸 모델과 카메라 구도를 저장한다.
        /// 저장한 값은 바꾸지 않는다.
        /// </summary>
        public AnimatedModel(GameObject model, Vector3 cameraPosition, float orthographicSize)
        {
            Model = model;
            CameraPosition = cameraPosition;
            OrthographicSize = orthographicSize;
        }
    }
}
