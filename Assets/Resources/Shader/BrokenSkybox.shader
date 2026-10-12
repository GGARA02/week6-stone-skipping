// 스카이박스 물고기가 바꾸는 깨진 하늘. 유니티가 깨진 머티리얼을 그릴 때와 같은 마젠타(1, 0, 1)로 하늘 전체를 칠한다.
// 색이 바뀌면 깨진 머티리얼처럼 보이지 않으므로 값을 노출하지 않는다.
Shader "Skybox/Broken"
{
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 Vert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 Frag() : SV_Target
            {
                return half4(1.0, 0.0, 1.0, 1.0);
            }
            ENDHLSL
        }
    }
}
