using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

using UnityEngine.Rendering.Universal;

[ExecuteAlways]
[DisallowMultipleComponent]
public class BlackHoleLensing : MonoBehaviour
{
    private static int _activeInstances;
    private static readonly (int Source, int Target)[] _diskFloatProperties = Array.ConvertAll(
        new[] { "_Intensity", "_Opacity", "_InnerRadius", "_OuterRadius", "_EdgeSoftness", "_RotationSpeed", "_NoiseScale", "_SpiralStrength" },
        property => (Shader.PropertyToID(property), Shader.PropertyToID("_Disk" + property.Substring(1))));

    [Header("렌더링 참조")]
    [SerializeField] private Renderer _eventHorizon;
    [SerializeField] private Renderer _accretionDisk;

    [Header("런타임 상태")]
    private MaterialPropertyBlock _properties;

    public static bool IsActive => _activeInstances > 0;
    public Renderer EventHorizon => _eventHorizon;
    public Renderer AccretionDisk => _accretionDisk;

    void OnEnable()
    {
        _activeInstances++;
        _properties = new MaterialPropertyBlock();
        RenderPipelineManager.beginContextRendering += BeginContextRendering;
        RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
    }

    void OnDisable()
    {
        _activeInstances--;
        RenderPipelineManager.beginContextRendering -= BeginContextRendering;
        RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
        if (_eventHorizon == null)
            return;
        _eventHorizon.GetPropertyBlock(_properties);
        _properties.SetFloat("_DiskEnabled", 0f);
        _eventHorizon.SetPropertyBlock(_properties);
    }

    /// <summary>
    /// 지정한 사건의 지평선과 강착원반 렌더러 및 카메라를 바탕으로 프로퍼티 블록을 갱신한다.
    /// eventHorizon, accretionDisk, diskMaterial, camera, properties를 사용하며, eventHorizon의 MaterialPropertyBlock을 변경한다.
    /// </summary>
    public static void ApplyLensingProperties(Renderer eventHorizon, Renderer accretionDisk, Material diskMaterial, Camera camera, MaterialPropertyBlock properties = null)
    {
        if (eventHorizon == null || accretionDisk == null)
            return;

        if (properties == null)
            properties = new MaterialPropertyBlock();

        eventHorizon.GetPropertyBlock(properties);
        properties.SetColor("_LensBackgroundColor", camera != null ? camera.backgroundColor : Color.clear);
        properties.SetFloat("_DiskEnabled", accretionDisk.enabled && accretionDisk.gameObject.activeInHierarchy ? 1f : 0f);
        properties.SetMatrix("_DiskWorldToLocal", accretionDisk.transform.worldToLocalMatrix);
        if (diskMaterial != null)
        {
            properties.SetVector("_DiskInnerColor", diskMaterial.GetColor("_InnerColor"));
            properties.SetVector("_DiskOuterColor", diskMaterial.GetColor("_OuterColor"));
            foreach (var property in _diskFloatProperties)
                properties.SetFloat(property.Target, diskMaterial.GetFloat(property.Source));
        }
        eventHorizon.SetPropertyBlock(properties);
    }

    /// <summary>
    /// 렌더 컨텍스트의 카메라 목록을 사용해 URP 카메라 설정 생성 전에 깊이와 불투명 색상 텍스처를 요청한다.
    /// 각 카메라의 requiresDepthTexture 및 requiresColorTexture를 활성화한다.
    /// </summary>
    private void BeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
    {
        foreach (Camera camera in cameras)
        {
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.requiresDepthTexture = true;
            cameraData.requiresColorTexture = true;
        }
    }

    /// <summary>
    /// 렌더링할 카메라 이벤트를 받아 형제 원반의 현재 Transform과 외형을 EventHorizon에 전달한다.
    /// Inspector 참조가 지정되지 않은 편집 중 상태에서는 머티리얼 상태를 변경하지 않는다.
    /// </summary>
    private void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (_eventHorizon == null || _accretionDisk == null)
            return;

        ApplyLensingProperties(_eventHorizon, _accretionDisk, _accretionDisk.sharedMaterial, camera, _properties);
    }
}
