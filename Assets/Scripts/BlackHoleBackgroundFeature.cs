using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

using UnityEngine.Rendering.Universal;

public class BlackHoleBackgroundFeature : ScriptableRendererFeature
{
    private BackgroundPass _pass;
    private InsidePass _insidePass;

    /// <summary>
    /// 렌더러 초기화 시 원반 앞의 배경을 캡처할 패스를 생성하여 보관한다.
    /// </summary>
    public override void Create()
    {
        _pass = new BackgroundPass
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents,
            requiresIntermediateTexture = true
        };
        _insidePass = new InsidePass
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents,
            requiresIntermediateTexture = true
        };
    }

    /// <summary>
    /// renderer와 renderingData를 받아 활성 블랙홀이 있을 때만 배경 캡처 패스를 추가한다.
    /// 블랙홀이 없는 씬에는 추가 배경 렌더링을 수행하지 않는다.
    /// </summary>
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (BlackHoleLensing.IsActive)
        {
            renderer.EnqueuePass(_pass);
            renderer.EnqueuePass(_insidePass);
        }
    }

    private class InsidePass : ScriptableRenderPass
    {
        private static readonly List<ShaderTagId> _shaderTags = new List<ShaderTagId>
        {
            new ShaderTagId("BlackHoleInside")
        };

        private class InsideData
        {
            public RendererListHandle Renderers { get; set; }
        }

        /// <summary>
        /// 투명 렌더링이 끝난 화면에 EventHorizon의 내부 면만 그린다.
        /// renderGraph와 frameData의 현재 색상 및 캡처 텍스처를 사용하며 내부 카메라 화면을 갱신한다.
        /// </summary>
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            UniversalRenderingData rendering = frameData.Get<UniversalRenderingData>();
            UniversalCameraData camera = frameData.Get<UniversalCameraData>();
            UniversalLightData lights = frameData.Get<UniversalLightData>();
            var filtering = new FilteringSettings(new RenderQueueRange { lowerBound = 3020, upperBound = 3020 });
            DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
                _shaderTags, rendering, camera, lights, SortingCriteria.CommonTransparent);
            var parameters = new RendererListParams(rendering.cullResults, drawing, filtering);
            RendererListHandle renderers = renderGraph.CreateRendererList(parameters);

            using (var builder = renderGraph.AddRasterRenderPass<InsideData>("Draw Black Hole Interior", out var data))
            {
                data.Renderers = renderers;
                builder.UseRendererList(renderers);
                builder.UseAllGlobalTextures(true);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (InsideData pass, RasterGraphContext context) =>
                    context.cmd.DrawRendererList(pass.Renderers));
            }
        }
    }

    private class BackgroundPass : ScriptableRenderPass
    {
        private static readonly int _backgroundTextureId = Shader.PropertyToID("_BlackHoleBackgroundTexture");
        private static readonly int _backgroundTexelSizeId = Shader.PropertyToID("_BlackHoleBackgroundTexture_TexelSize");
        private static readonly List<ShaderTagId> _shaderTags = new List<ShaderTagId>
        {
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly")
        };

        private class CopyData
        {
            public TextureHandle Source { get; set; }
        }

        private class BackgroundData
        {
            public RendererListHandle Renderers { get; set; }
            public Vector4 TexelSize { get; set; }
        }

        /// <summary>
        /// renderGraph와 frameData로 전체 해상도 배경을 복사하고 원반보다 먼저 그리는 투명 오브젝트를 합성한다.
        /// 생성한 배경 텍스처를 같은 카메라의 EventHorizon 셰이더에 전역 입력으로 연결한다.
        /// </summary>
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            UniversalRenderingData rendering = frameData.Get<UniversalRenderingData>();
            UniversalCameraData camera = frameData.Get<UniversalCameraData>();
            UniversalLightData lights = frameData.Get<UniversalLightData>();

            TextureHandle source = resources.activeColorTexture;
            TextureDesc description = renderGraph.GetTextureDesc(source);
            description.name = "Black Hole Background";
            description.clearBuffer = false;
            description.depthBufferBits = DepthBits.None;
            TextureHandle background = renderGraph.CreateTexture(description);

            using (var builder = renderGraph.AddRasterRenderPass<CopyData>("Copy Black Hole Background", out var data))
            {
                data.Source = source;
                builder.UseTexture(source, AccessFlags.Read);
                builder.SetRenderAttachment(background, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (CopyData pass, RasterGraphContext context) =>
                    Blitter.BlitTexture(context.cmd, pass.Source, new Vector4(1f, 1f, 0f, 0f), 0f, false));
            }

            var filtering = new FilteringSettings(new RenderQueueRange { lowerBound = 2501, upperBound = 3000 });
            DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
                _shaderTags, rendering, camera, lights, SortingCriteria.CommonTransparent);
            var parameters = new RendererListParams(rendering.cullResults, drawing, filtering);
            RendererListHandle renderers = renderGraph.CreateRendererList(parameters);

            using (var builder = renderGraph.AddRasterRenderPass<BackgroundData>("Capture Black Hole Background Layers", out var data))
            {
                data.Renderers = renderers;
                data.TexelSize = new Vector4(1f / camera.cameraTargetDescriptor.width,
                    1f / camera.cameraTargetDescriptor.height, camera.cameraTargetDescriptor.width, camera.cameraTargetDescriptor.height);
                builder.UseRendererList(renderers);
                builder.UseAllGlobalTextures(true);
                builder.SetRenderAttachment(background, 0, AccessFlags.ReadWrite);
                builder.SetGlobalTextureAfterPass(background, _backgroundTextureId);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (BackgroundData pass, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(pass.Renderers);
                    context.cmd.SetGlobalVector(_backgroundTexelSizeId, pass.TexelSize);
                });
            }
        }
    }
}
