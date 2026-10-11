// 물 블록(물 fish) 전용 물 셰이더. WaterV3를 복사해 구멍 스텐실 기본값과 그리는 순서만 바꿨다.
// 물수제비용 사실적인 물 셰이더(HDRP 물 스타일).
// 굴절과 깊이 기반 흡수/산란, 화면 공간 반사, GGX 햇빛 반사, 파도 마루 산란, 거품, 착수 물결을 그린다.
// 물결 위치는 SkipEffect가 전역 배열 _SkipRippleData(xy 위치, z 시작 시간, w 세기)로 넘긴다.
// 빗물결 세기는 EnvironmentController가 전역 값 _RainIntensity로 넘긴다.
// URP 에셋의 Depth Texture와 Opaque Texture가 켜져 있어야 한다.
Shader "Custom/WaterBlock"
{
    Properties
    {
        [Header(Water Body)]
        _ScatteringColor ("Scattering Color", Color) = (0.02, 0.12, 0.17, 1)
        _RefractionColor ("Refraction Color", Color) = (0.1, 0.5, 0.55, 1)
        _AbsorptionDistance ("Absorption Distance", Range(0.5, 50)) = 5
        _RefractionStrength ("Refraction Strength", Range(0, 0.2)) = 0.04

        [Header(Tip Scattering)]
        _TipScatteringColor ("Tip Scattering Color", Color) = (0.06, 0.5, 0.45, 1)
        _TipScatteringStrength ("Tip Scattering Strength", Range(0, 4)) = 1.2

        [Header(Waves)]
        _WindDirection ("Wind Direction", Range(0, 360)) = 30
        _WaveStrength ("Wave Strength", Range(0, 2)) = 1
        _WaveScale ("Wave Scale", Range(0.2, 5)) = 1
        _WaveSpeed ("Wave Speed", Range(0, 3)) = 1
        _Choppiness ("Choppiness", Range(0, 0.9)) = 0.5
        _DirectionalSpread ("Directional Spread", Range(0, 1)) = 0.6
        _GustStrength ("Gust Variation", Range(0, 1)) = 0.5

        [Header(Reflection)]
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 1
        _Roughness ("Roughness", Range(0.01, 1)) = 0.12
        _SSRStrength ("Screen Space Reflection", Range(0, 1)) = 1
        _SpecularStrength ("Sun Specular", Range(0, 4)) = 1
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.6

        [Header(Foam)]
        _FoamColor ("Foam Color", Color) = (0.95, 0.98, 1, 1)
        _FoamBubbleScale ("Foam Bubble Scale", Range(0.5, 10)) = 3
        _EdgeFoamDistance ("Edge Foam Distance", Range(0.01, 3)) = 0.6
        _EdgeFoamStrength ("Edge Foam Strength", Range(0, 1)) = 0.7

        [Header(Ripples)]
        _RippleSpeed ("Ripple Speed", Float) = 7
        _RippleWavelength ("Ripple Wavelength", Float) = 1.6
        _RippleLifetime ("Ripple Lifetime", Float) = 1.8
        _RippleNormalStrength ("Ripple Normal Strength", Range(0, 2)) = 0.8
        _RippleFoam ("Ripple Foam", Range(0, 1)) = 0.8

        [Header(Rain Ripples)]
        _RainRippleStrength ("Rain Ripple Strength", Range(0, 2)) = 0.6
        _RainRippleCellSize ("Rain Ripple Cell Size", Range(0.2, 3)) = 0.8
        _RainRipplePeriod ("Rain Ripple Period", Range(0.2, 3)) = 0.9

        [Header(Water Hole)]
        // 물 블록은 물 구멍 위를 지나가도 가려지지 않아야 하므로 기본값을 Always로 둔다.
        [Enum(UnityEngine.Rendering.CompareFunction)] _HoleStencilComp ("Hole Stencil Comp", Float) = 8
        // 켜면 구멍 벽용으로 그린다. 뒤를 비추지 않고 아주 깊은 물처럼 채우며, 파도는 면을 따라 아래로 흘러내린다.
        [Toggle(_WATER_HOLE_WALL)] _WaterHoleWall ("Water Hole Wall", Float) = 0
        _WallFlowSpeed ("Wall Flow Speed", Float) = 1.5
    }

    SubShader
    {
        // Opaque Texture 복사 뒤이면서 흐르는 물(Geometry)과 바다(Transparent-100)보다 늦게 그려 물 구멍 위에서도 덮이지 않게 한다.
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-90" "RenderPipeline" = "UniversalPipeline" }

        // 파낸 물 구멍(WaterHoleMask)이 표시한 곳에는 수면을 그리지 않는다. 비교 방식은 머티리얼의 _HoleStencilComp를 따른다.
        Stencil
        {
            Ref 128
            ReadMask 128
            Comp [_HoleStencilComp]
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma shader_feature_local _WATER_HOLE_WALL

            // 물은 깊이 텍스처에 없으므로 화면 공간 그림자 대신 그림자 맵을 직접 샘플링한다.
            #define _SURFACE_TYPE_TRANSPARENT 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            // 도감 미리보기에서 빛이 지나간다고 보는 물 블록 두께(m).
            #define PREVIEW_PATH_LENGTH 4.0
            // 도감 미리보기 최종 색에 곱하는 밝기 배율.
            #define PREVIEW_BRIGHTNESS 0.65
            #define WAVE_COUNT 20
            #define RIPPLE_COUNT 16
            #define GRAVITY 9.81
            #define LONGEST_WAVELENGTH 24.0
            #define WAVELENGTH_FALLOFF 0.78
            #define WAVE_STEEPNESS 0.055
            #define SSR_STEPS 32
            #define SSR_REFINE_STEPS 5
            #define SSR_FIRST_STEP 0.25
            #define SSR_STEP_GROWTH 1.2
            #define SPECULAR_MAX 64.0
            // 빗물결 값은 격자 한 칸을 1로 본 단위다.
            #define RAIN_RIPPLE_LAYERS 3
            #define RAIN_RIPPLE_MAX_RADIUS 0.28
            #define RAIN_RIPPLE_WAVELENGTH 0.09
            #define RAIN_RIPPLE_WIDTH2 0.004
            #define RAIN_RIPPLE_FOOTPRINT_LIMIT 0.06

            CBUFFER_START(UnityPerMaterial)
                half4 _ScatteringColor;
                half4 _RefractionColor;
                float _AbsorptionDistance;
                float _RefractionStrength;
                half4 _TipScatteringColor;
                half _TipScatteringStrength;
                float _WindDirection;
                float _WaveStrength;
                float _WaveScale;
                float _WaveSpeed;
                float _Choppiness;
                float _DirectionalSpread;
                float _GustStrength;
                half _ReflectionStrength;
                half _Roughness;
                half _SSRStrength;
                half _SpecularStrength;
                half _ShadowStrength;
                half4 _FoamColor;
                float _FoamBubbleScale;
                float _EdgeFoamDistance;
                half _EdgeFoamStrength;
                float _RippleSpeed;
                float _RippleWavelength;
                float _RippleLifetime;
                half _RippleNormalStrength;
                half _RippleFoam;
                half _RainRippleStrength;
                float _RainRippleCellSize;
                float _RainRipplePeriod;
                float _WallFlowSpeed;
            CBUFFER_END

            float4 _SkipRippleData[RIPPLE_COUNT];
            // EnvironmentController가 넘기는 비 세기(0~1).
            float _RainIntensity;

            struct Attributes
            {
                float4 positionOS : POSITION;
            #if defined(_WATER_HOLE_WALL)
                float3 normalOS : NORMAL;
            #endif
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            #if defined(_WATER_HOLE_WALL)
                float3 normalWS : TEXCOORD2;
            #endif
            };

            // slope: 수면 기울기, pinch: 마루를 뾰족하게 만드는 법선 y 감소량, height: 파고.
            // amplitude: 파고 정규화용 진폭 합, variance: 픽셀보다 짧아 지운 파도의 기울기 분산.
            struct WaveSample
            {
                float2 slope;
                float pinch;
                float height;
                float amplitude;
                float variance;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
            #if defined(_WATER_HOLE_WALL)
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            #endif
                return output;
            }

            // 격자 좌표 해시. sin을 쓰지 않아 큰 월드 좌표에서도 패턴이 깨지지 않는다.
            float Hash12(float2 p)
            {
                float3 p3 = frac(p.xyx * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // 0~1 범위의 부드러운 값 노이즈.
            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash12(cell);
                float b = Hash12(cell + float2(1.0, 0.0));
                float c = Hash12(cell + float2(0.0, 1.0));
                float d = Hash12(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 바람 방향 주변으로 퍼진 파도들을 깊은 물 분산 관계(속도 = sqrt(g/k))로 합친다.
            // 한 픽셀보다 짧은 파도는 지우고, 지운 기울기는 분산으로 모아 거칠기에 더한다.
            WaveSample SampleWaves(float2 xz, float time, float footprint)
            {
                WaveSample result = (WaveSample)0;
                float windAngle = radians(_WindDirection);
                float2 windDir = float2(cos(windAngle), sin(windAngle));

                // 큰 규모의 노이즈로 짧은 파도 세기를 바꿔 바람이 지나간 자국을 만든다.
                float2 gustUV = xz * 0.015 - windDir * time * 0.04;
                float gustNoise = ValueNoise(gustUV) * 0.65 + ValueNoise(gustUV * 2.7 + 13.0) * 0.35;
                float gust = lerp(1.0, 0.3 + gustNoise * 1.4, _GustStrength);

                float wavelength = LONGEST_WAVELENGTH * _WaveScale;
                [unroll]
                for (int i = 0; i < WAVE_COUNT; i++)
                {
                    // 짧은 파도일수록 바람 방향에서 더 넓게 퍼진다.
                    float order = i / (float)(WAVE_COUNT - 1);
                    float spread = (frac(i * 0.618034 + 0.17) * 2.0 - 1.0) * _DirectionalSpread * lerp(0.6, 1.6, order) * HALF_PI;
                    float dirSin, dirCos;
                    sincos(windAngle + spread, dirSin, dirCos);
                    float2 dir = float2(dirCos, dirSin);

                    float k = TWO_PI / wavelength;
                    float omega = sqrt(GRAVITY * k) * _WaveSpeed;
                    float phase = k * dot(dir, xz) - omega * time + frac(i * 0.754877) * TWO_PI;
                    float steepness = WAVE_STEEPNESS * _WaveStrength * lerp(1.0, gust, saturate(order * 2.0 - 0.5));
                    float fade = saturate(wavelength / max(footprint, 1e-4) * 0.25 - 0.5);

                    float phaseSin, phaseCos;
                    sincos(phase, phaseSin, phaseCos);
                    result.slope += dir * (steepness * fade * phaseCos);
                    result.pinch += steepness * fade * phaseSin;
                    result.height += steepness / k * fade * phaseSin;
                    result.amplitude += steepness / k;
                    result.variance += 0.5 * steepness * steepness * (1.0 - fade);

                    wavelength *= WAVELENGTH_FALLOFF;
                }
                return result;
            }

            // 착수 지점에서 바깥으로 퍼지는 고리 물결과 거품.
            void AddRipples(float2 xz, float time, inout float2 slope, inout float foam)
            {
                float k = TWO_PI / max(_RippleWavelength, 0.01);
                float sigma2 = _RippleWavelength * _RippleWavelength * 2.0;
                [unroll]
                for (int i = 0; i < RIPPLE_COUNT; i++)
                {
                    float4 ripple = _SkipRippleData[i];
                    float age = time - ripple.z;
                    float life = 1.0 - age / max(_RippleLifetime, 0.01);
                    if (age < 0.0 || life <= 0.0)
                    {
                        continue;
                    }

                    float2 offset = xz - ripple.xy;
                    float dist = length(offset);
                    float2 dir = offset / max(dist, 1e-3);
                    float x = dist - age * _RippleSpeed;
                    float envelope = exp(-x * x / sigma2);
                    float amplitude = ripple.w * life * life;

                    slope += dir * (cos(k * x) * envelope * amplitude * _RippleNormalStrength);
                    foam += envelope * amplitude * (0.5 + 0.5 * sin(k * x)) * _RippleFoam;
                    // 착수 직후 가운데에 잠깐 남는 거품.
                    foam += saturate(1.0 - dist / (1.0 + age * 2.0)) * amplitude * saturate(1.0 - age / 0.6) * _RippleFoam;
                }
            }

            // 빗방울이 떨어진 자리에서 작게 퍼지는 고리 물결. 격자 칸마다 해시로 위치와 시작 시간을 정해 반복한다.
            // 비가 약하면 일부 칸만 물결을 만들고, 멀어서 고리가 픽셀보다 작아지면 끈다.
            void AddRainRipples(float2 xz, float time, float footprint, inout float2 slope)
            {
                float cellSize = max(_RainRippleCellSize, 0.05);
                float fade = saturate(2.0 * (1.0 - footprint / (cellSize * RAIN_RIPPLE_FOOTPRINT_LIMIT)));
                if (fade <= 0.0)
                {
                    return;
                }

                float k = TWO_PI / RAIN_RIPPLE_WAVELENGTH;
                float strength = _RainRippleStrength * fade;
                [unroll]
                for (int layer = 0; layer < RAIN_RIPPLE_LAYERS; layer++)
                {
                    float2 uv = xz / cellSize + layer * float2(0.37, 0.61);
                    float2 cell = floor(uv);
                    float cycle = time / max(_RainRipplePeriod, 0.05) + Hash12(cell + layer * 17.0);
                    float t = frac(cycle);
                    float2 seed = cell + floor(cycle) * 7.31 + layer * 31.7;
                    if (Hash12(seed + 5.17) > _RainIntensity)
                    {
                        continue;
                    }

                    // 고리가 칸 밖으로 나가지 않도록 중심을 칸 가운데 쪽에 둔다.
                    float2 center = 0.35 + 0.3 * float2(Hash12(seed), Hash12(seed + 1.93));
                    float2 offset = frac(uv) - center;
                    float dist = length(offset);
                    float2 dir = offset / max(dist, 1e-3);
                    float x = dist - t * RAIN_RIPPLE_MAX_RADIUS;
                    float life = 1.0 - t;
                    float envelope = exp(-x * x / RAIN_RIPPLE_WIDTH2) * life * life;
                    slope += dir * (sin(k * x) * envelope * strength);
                }
            }

            // 월드 위치를 카메라 텍스처 UV(xy)와 눈 깊이(z)로 바꾼다.
            float3 WorldToScreen(float3 positionWS)
            {
                float4 positionCS = TransformWorldToHClip(positionWS);
                float2 ndc = positionCS.xy / positionCS.w;
                ndc.y *= _ProjectionParams.x;
                return float3(ndc * 0.5 + 0.5, positionCS.w);
            }

            // 반복문 안에서도 쓸 수 있도록 LOD 0으로 장면 깊이를 읽어 눈 깊이로 반환한다.
            float SampleEyeDepth(float2 uv)
            {
                uv = ClampAndScaleUVForBilinear(UnityStereoTransformScreenSpaceTex(uv), _CameraDepthTexture_TexelSize.xy);
                float rawDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, uv, 0).r;
                return LinearEyeDepth(rawDepth, _ZBufferParams);
            }

            // 반복문 안에서도 쓸 수 있도록 LOD 0으로 물을 그리기 전 장면 색을 읽는다.
            float3 SampleOpaqueColor(float2 uv)
            {
                uv = ClampAndScaleUVForBilinear(UnityStereoTransformScreenSpaceTex(uv), _CameraOpaqueTexture_TexelSize.xy);
                return SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv, 0).rgb;
            }

            // 반사 광선을 화면 깊이와 비교하며 전진시켜 물 위 물체의 반사색을 찾는다.
            // rgb에 반사색, a에 신뢰도를 반환하며 맞은 곳이 없으면 0을 반환한다.
            float4 TraceScreenSpaceReflection(float3 originWS, float3 directionWS)
            {
                float stepLength = SSR_FIRST_STEP;
                float previous = 0.0;
                float travelled = 0.0;
                [loop]
                for (int i = 0; i < SSR_STEPS; i++)
                {
                    previous = travelled;
                    travelled += stepLength;
                    float3 screen = WorldToScreen(originWS + directionWS * travelled);
                    if (screen.z <= 0.0 || any(screen.xy < 0.0) || any(screen.xy > 1.0))
                    {
                        break;
                    }

                    // 광선이 물체 뒤로 들어갔지만 너무 깊지 않을 때만 맞은 것으로 본다.
                    float depthDelta = screen.z - SampleEyeDepth(screen.xy);
                    if (depthDelta > 0.0 && depthDelta < stepLength * 2.0 + 0.3)
                    {
                        float low = previous;
                        float high = travelled;
                        [unroll]
                        for (int j = 0; j < SSR_REFINE_STEPS; j++)
                        {
                            float middle = (low + high) * 0.5;
                            float3 refine = WorldToScreen(originWS + directionWS * middle);
                            if (refine.z > SampleEyeDepth(refine.xy))
                            {
                                high = middle;
                            }
                            else
                            {
                                low = middle;
                            }
                        }

                        float3 hit = WorldToScreen(originWS + directionWS * high);
                        float2 edge = saturate(min(hit.xy, 1.0 - hit.xy) * 8.0);
                        float confidence = edge.x * edge.y * saturate((SSR_STEPS - i) / 4.0);
                        return float4(SampleOpaqueColor(hit.xy), confidence);
                    }
                    stepLength *= SSR_STEP_GROWTH;
                }
                return 0;
            }

            // 거칠기 alpha를 쓰는 GGX 햇빛 반사. 빛 색에 곱할 세기를 반환한다.
            float SunSpecular(float3 normalWS, float3 viewDirWS, float3 lightDirWS, float alpha)
            {
                float3 halfDir = normalize(lightDirWS + viewDirWS);
                float NdotH = saturate(dot(normalWS, halfDir));
                float NdotL = saturate(dot(normalWS, lightDirWS));
                float NdotV = max(dot(normalWS, viewDirWS), 1e-4);
                float LdotH = saturate(dot(lightDirWS, halfDir));

                float alpha2 = alpha * alpha;
                float d = (NdotH * alpha2 - NdotH) * NdotH + 1.0;
                float distribution = alpha2 / (PI * d * d);
                float visibility = 0.5 / (NdotL * (NdotV * (1.0 - alpha) + alpha) + NdotV * (NdotL * (1.0 - alpha) + alpha) + 1e-5);
                float fresnel = 0.02 + 0.98 * pow(1.0 - LdotH, 5.0);
                return min(distribution * visibility * fresnel * NdotL, SPECULAR_MAX);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 positionWS = input.positionWS;
                float time = _Time.y;
            #if defined(_WATER_HOLE_WALL)
                // 면을 기준으로 한 좌표로 파도를 샘플한다. 옆면은 세로축을 시간만큼 밀어 물이 아래로 흘러내리게 한다.
                float3 faceNormal = normalize(input.normalWS);
                float3 faceTangent = abs(faceNormal.y) > 0.5 ? float3(1.0, 0.0, 0.0) : normalize(cross(float3(0.0, 1.0, 0.0), faceNormal));
                float3 faceBitangent = cross(faceNormal, faceTangent);
                float2 waveCoord = float2(dot(positionWS, faceTangent), dot(positionWS, faceBitangent) + time * _WallFlowSpeed);
            #else
                float2 waveCoord = positionWS.xz;
            #endif
                float footprint = max(length(ddx(waveCoord)), length(ddy(waveCoord)));

                WaveSample waves = SampleWaves(waveCoord, time, footprint);
                float2 slope = waves.slope;
                float foam = 0.0;
            #if defined(_WATER_HOLE_WALL)
                float3 normalWS = normalize(faceNormal * max(1.0 - _Choppiness * waves.pinch, 0.2) - faceTangent * slope.x - faceBitangent * slope.y);
            #else
                AddRipples(positionWS.xz, time, slope, foam);
                // 굴절은 빗물결을 뺀 법선으로 계산해 비가 와도 물속이 흐트러지지 않게 한다.
                float3 refractNormalWS = normalize(float3(-slope.x, max(1.0 - _Choppiness * waves.pinch, 0.2), -slope.y));
                if (_RainIntensity > 0.0)
                {
                    AddRainRipples(positionWS.xz, time, footprint, slope);
                }
                float3 normalWS = normalize(float3(-slope.x, max(1.0 - _Choppiness * waves.pinch, 0.2), -slope.y));
            #endif

                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);
                float viewDistance = distance(GetCameraPositionWS(), positionWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
                half shadow = lerp(1.0, mainLight.shadowAttenuation, _ShadowStrength);
                half3 ambient = SampleSH(float3(0.0, 1.0, 0.0));

                // 지운 잔물결의 기울기 분산을 거칠기에 더해 먼 수면이 넓게 반짝이도록 한다.
                float baseAlpha = _Roughness * _Roughness;
                float alpha = sqrt(baseAlpha * baseAlpha + 2.0 * waves.variance);
                float perceptualRoughness = sqrt(alpha);

                half3 scatterLight = ambient + mainLight.color * (saturate(mainLight.direction.y) * shadow * 0.5);
                // 도감 미리보기 카메라는 직교 투영이고 뒤에 장면이 없어 깊이로 구한 거품이 화면을 하얗게 덮는다.
                // 직교 카메라에서는 장면 깊이와 화면 색을 쓰는 굴절, 화면 반사, 가장자리 거품을 끈다.
                bool orthographic = unity_OrthoParams.w > 0.5;
            #if defined(_WATER_HOLE_WALL)
                // 벽 뒤 장면을 비추지 않고, 빛이 모두 흡수된 아주 깊은 물처럼 산란색으로만 채운다.
                half3 underwater = _ScatteringColor.rgb * scatterLight;
            #else
                // 물결 법선만큼 화면 UV를 밀고, 그 자리에 물 위 물체가 있으면 원래 UV를 쓴다.
                float3 screen = WorldToScreen(positionWS);
                float surfaceDepth = screen.z;
                float sceneDepth = SampleEyeDepth(screen.xy);
                float2 distortion = mul((float3x3)GetWorldToViewMatrix(), float3(refractNormalWS.x, 0.0, refractNormalWS.z)).xy;
                float2 refractUV = screen.xy + distortion * (_RefractionStrength * saturate((sceneDepth - surfaceDepth) * 0.5));
                float refractDepth = SampleEyeDepth(refractUV);
                if (refractDepth < surfaceDepth)
                {
                    refractUV = screen.xy;
                    refractDepth = sceneDepth;
                }

                // 빛이 물속을 지나간 거리로 흡수(Beer-Lambert)하고, 흡수된 만큼 산란색으로 채운다.
                float depthToPath = viewDistance / max(surfaceDepth, 1e-4);
                float pathLength = max(refractDepth - surfaceDepth, 0.0) * depthToPath;
                float3 absorption = -log(max(_RefractionColor.rgb, 1e-3)) / _AbsorptionDistance;
                float3 transmittance = exp(-absorption * pathLength);
                half3 underwater = SampleOpaqueColor(refractUV) * transmittance + _ScatteringColor.rgb * scatterLight * (1.0 - transmittance);
                if (orthographic)
                {
                    // 뒤에서 들어오는 빛을 주변광으로 보고 블록 두께만큼 흡수한 색으로 채운다.
                    float3 previewTransmittance = exp(-absorption * PREVIEW_PATH_LENGTH);
                    underwater = scatterLight * (previewTransmittance + _ScatteringColor.rgb * (1.0 - previewTransmittance));
                }
            #endif

                // 햇빛을 등지고 본 파도 마루는 빛이 얇은 물을 통과해 밝은 청록색으로 보인다.
                float crest = saturate(waves.height / max(waves.amplitude * 0.5, 1e-4));
                float3 scatterDir = normalize(mainLight.direction + normalWS * 0.6);
                half backLight = pow(saturate(dot(viewDirWS, -scatterDir)), 4.0);
                underwater += _TipScatteringColor.rgb * mainLight.color * (shadow * _TipScatteringStrength * crest * crest * (backLight + 0.15));

                half NdotV = saturate(dot(normalWS, viewDirWS));
                half fresnel = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);

                // 파도가 가팔라도 물속 방향을 비추지 않게 반사 방향을 위로 접는다.
                float3 reflectDir = reflect(-viewDirWS, normalWS);
                reflectDir.y = abs(reflectDir.y);
                half3 reflection = GlossyEnvironmentReflection(reflectDir, positionWS, perceptualRoughness, 1.0);

                // 거친 먼 수면은 화면 반사가 흐려야 하므로 환경 반사 쪽으로 넘긴다.
                float ssrWeight = _SSRStrength * saturate(1.5 - perceptualRoughness * 2.0);
                UNITY_BRANCH
                if (ssrWeight > 0.0 && !orthographic)
                {
                    float4 ssr = TraceScreenSpaceReflection(positionWS + reflectDir * 0.05, reflectDir);
                    reflection = lerp(reflection, ssr.rgb, ssr.a * ssrWeight);
                }

                half3 color = lerp(underwater, reflection, fresnel * _ReflectionStrength);
                color += SunSpecular(normalWS, viewDirWS, mainLight.direction, alpha) * _SpecularStrength * shadow * mainLight.color;

            #if !defined(_WATER_HOLE_WALL)
                // 물체와 맞닿은 얕은 곳에 생기는 거품.
                float verticalDepth = max(sceneDepth - surfaceDepth, 0.0) * depthToPath * viewDirWS.y;
                if (!orthographic)
                {
                    foam += saturate(1.0 - verticalDepth / _EdgeFoamDistance) * _EdgeFoamStrength;
                }
            #endif

                // 거품 양이 적을수록 기포 노이즈의 밝은 부분만 남긴다. 먼 곳은 노이즈를 평균값으로 바꿔 깜빡임을 막는다.
                float2 foamUV = positionWS.xz * _FoamBubbleScale;
                float bubbles = ValueNoise(foamUV + time * 0.3) * 0.6 + ValueNoise(foamUV * 2.3 - time * 0.2) * 0.4;
                bubbles = lerp(bubbles, 0.5, saturate(footprint * _FoamBubbleScale - 0.5));
                float foamMask = saturate((saturate(foam) - (1.0 - bubbles) * 0.9) * 2.5);
                half3 foamLit = _FoamColor.rgb * (ambient + mainLight.color * saturate(dot(normalWS, mainLight.direction)) * shadow);
                color = lerp(color, foamLit, foamMask);
                if (orthographic)
                {
                    color *= PREVIEW_BRIGHTNESS;
                }

                // 바다는 꼭짓점이 몇 개뿐인 큰 상자라서 안개를 꼭짓점에서 구해 보간하면 카메라 뒤 꼭짓점 때문에 가까운 수면까지 안개로 덮인다.
                // 그래서 픽셀마다 안개 값을 구한다.
                color = MixFog(max(color, 0.0), ComputeFogFactor(TransformWorldToHClip(positionWS).z));
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
