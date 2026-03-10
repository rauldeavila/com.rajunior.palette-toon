Shader "Custom/PaletteToonGrass"
{
    Properties
    {
        _MainTex("Grass Texture", 2D) = "white" {}
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5

        [MainColor] _BaseColor("Base Tint", Color) = (1, 1, 1, 1)

        [HideInInspector] _ColorShadow("Shadow Color", Color)       = (0.15, 0.1, 0.2, 1)
        [HideInInspector] _ColorBase("Base Color", Color)            = (0.4, 0.3, 0.5, 1)
        [HideInInspector] _ColorHighlight("Highlight Color", Color)  = (0.9, 0.85, 1, 1)

        _Threshold1("Shadow Threshold", Range(0, 1)) = 0.35
        _Threshold2("Highlight Threshold", Range(0, 1)) = 0.75

        [Header(Lighting Behavior)]
        _IntensityAffectsBands("Intensity Affects Bands", Range(0, 1)) = 1
        _BandAccumulation("Band Accumulation (0 Add / 1 Max)", Range(0, 1)) = 1
        _ApplyFog("Apply Fog", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "DisableBatching" = "True"
        }

        // ── FORWARD PASS ──
        Pass
        {
            Name "ToonGrassForward"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Off
            AlphaToMask On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ColorShadow;
                float4 _ColorBase;
                float4 _ColorHighlight;
                float4 _MainTex_ST;
                float  _Cutoff;
                float  _Threshold1;
                float  _Threshold2;
                float  _IntensityAffectsBands;
                float  _BandAccumulation;
                float  _ApplyFog;
            CBUFFER_END

            // Unity terrain wind globals (set by terrain engine)
            float4 _WaveAndDistance;    // xyz = wind direction * strength, w = distance fade
            float4 _CameraPosition;    // xyz = camera world pos
            float  _ShakeWindSpeed;
            float  _ShakeBending;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 texcoord   : TEXCOORD0;
                float4 color      : COLOR; // alpha = wave scale (0=root, 1=tip)
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float  fogCoord    : TEXCOORD3;
            };

            // ── Fast sine/cosine approximation ──
            void FastSinCos(float4 val, out float4 s, out float4 c)
            {
                val = val * 6.408849 - 3.141593;
                float4 r5 = val * val;
                float4 r6 = r5 * r5;
                float4 r7 = r6 * r5;
                float4 r8 = r6 * r5 * val;
                s = val + val * r5 * (-0.16161616) + r8 * 0.0083333;
                c = 1.0 + r5 * (-0.4999999) + r6 * 0.0416666 + r7 * (-0.00138888);
            }

            // ── Wave animation ──
            float3 GrassWaveAnimation(float3 positionOS, float waveScale)
            {
                float3 worldPos = TransformObjectToWorld(positionOS);

                // Per-blade phase offset from world position
                float phaseOffset = (worldPos.x + worldPos.z) * 0.1;

                // Multiple wave frequencies for natural motion
                float4 wavePhase = frac(float4(
                    phaseOffset + _Time.y * _ShakeWindSpeed,
                    phaseOffset * 0.7 + _Time.y * _ShakeWindSpeed * 1.3,
                    phaseOffset * 1.3 + _Time.y * _ShakeWindSpeed * 0.8,
                    phaseOffset * 0.5 + _Time.y * _ShakeWindSpeed * 1.7
                ));

                float4 s, c;
                FastSinCos(wavePhase, s, c);

                // Combine waves with decreasing amplitude
                float combinedWave = s.x + s.y * 0.5 + s.z * 0.3 + s.w * 0.2;
                combinedWave *= 0.5; // normalize

                // Displacement along wind direction, scaled by vertex tip factor
                float3 displacement = _WaveAndDistance.xyz * combinedWave * waveScale * _ShakeBending;
                return positionOS + displacement;
            }

            // ── Toon lighting (same as PaletteToonRamp) ──

            float Luminance3(float3 c)
            {
                return dot(c, float3(0.2126, 0.7152, 0.0722));
            }

            float3 ToonRamp(float intensity)
            {
                if (intensity < _Threshold1)  return _ColorShadow.rgb;
                if (intensity < _Threshold2)  return _ColorBase.rgb;
                return _ColorHighlight.rgb;
            }

            float BandContribution(float NdotL, float distanceAttenuation, float shadowAttenuation, float3 lightColor)
            {
                float shadow = smoothstep(0.0, 0.05, shadowAttenuation);
                float unshadowed = saturate(NdotL) * distanceAttenuation;
                float withIntensity = unshadowed * Luminance3(lightColor);
                float lit = lerp(unshadowed, withIntensity, _IntensityAffectsBands);
                float shadowDrop = (1.0 - shadow) * (_Threshold2 - _Threshold1);
                return max(0.0, lit - shadowDrop);
            }

            int ResolveAdditionalLightIndex(uint loopIndex)
            {
            #if USE_CLUSTER_LIGHT_LOOP
                return (int)loopIndex;
            #else
                return GetPerObjectLightIndex(loopIndex);
            #endif
            }

            float LocalLightRangeSignal(uint loopIndex, float3 positionWS, float distanceAttenuation)
            {
                int lightIndex = ResolveAdditionalLightIndex(loopIndex);

            #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
                float4 lightPositionWS = _AdditionalLightsBuffer[lightIndex].position;
                half4 distanceAndSpotAttenuation = _AdditionalLightsBuffer[lightIndex].attenuation;
            #else
                float4 lightPositionWS = _AdditionalLightsPosition[lightIndex];
                half4 distanceAndSpotAttenuation = _AdditionalLightsAttenuation[lightIndex];
            #endif

                if (lightPositionWS.w == 0.0)
                {
                    return -1.0;
                }

                float3 lightVector = lightPositionWS.xyz - positionWS * lightPositionWS.w;
                float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);

                float invRangeSq = max((float)distanceAndSpotAttenuation.x, 1e-6);
                float normalizedDistance = saturate(sqrt(distanceSqr * invRangeSq));
                float rangeSignal = 1.0 - normalizedDistance;

                float distanceOnly = DistanceAttenuation(distanceSqr, distanceAndSpotAttenuation.xy);
                float spotSignal = saturate(distanceAttenuation / max(distanceOnly, 1e-5));

                return saturate(rangeSignal * spotSignal);
            }

            Varyings vert(Attributes input)
            {
                Varyings o;

                // Apply wave animation (scaled by vertex color alpha)
                float3 animatedPos = GrassWaveAnimation(input.positionOS.xyz, input.color.a);

                VertexPositionInputs p = GetVertexPositionInputs(animatedPos);
                VertexNormalInputs   n = GetVertexNormalInputs(input.normalOS);

                o.positionHCS = p.positionCS;
                o.positionWS  = p.positionWS;
                o.normalWS    = n.normalWS;
                o.uv          = TRANSFORM_TEX(input.texcoord, _MainTex);
                o.fogCoord    = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float4 frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                float4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(texColor.a - _Cutoff);

                // Flip normal for backface
                float3 N = normalize(input.normalWS);
                N = isFrontFace ? N : -N;

                float totalLight = 0.0;
                bool useAdditive = (_BandAccumulation < 0.5);

                // ── main light ──
            #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 sc = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(sc);
            #else
                Light mainLight = GetMainLight();
            #endif

                float mainBand = BandContribution(dot(N, mainLight.direction), mainLight.distanceAttenuation, mainLight.shadowAttenuation, mainLight.color);
                totalLight = useAdditive ? (totalLight + mainBand) : max(totalLight, mainBand);

                // ── additional lights ──
                {
                    uint count = GetAdditionalLightsCount();
                    InputData inputData = (InputData)0;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionHCS);
                    inputData.positionWS = input.positionWS;

                    LIGHT_LOOP_BEGIN(count)
                        Light l = GetAdditionalLight(lightIndex, input.positionWS, half4(1.0, 1.0, 1.0, 1.0));
                        float addBand;
                        float localRange = LocalLightRangeSignal(lightIndex, input.positionWS, l.distanceAttenuation);
                        if (localRange >= 0.0)
                        {
                            float NdotL = saturate(dot(N, l.direction));
                            float shadow = smoothstep(0.0, 0.05, l.shadowAttenuation);
                            float unshadowed = NdotL * localRange;
                            float withIntensity = unshadowed * Luminance3(l.color);
                            float lit = lerp(unshadowed, withIntensity, _IntensityAffectsBands);
                            float shadowDrop = (1.0 - shadow) * (_Threshold2 - _Threshold1);
                            addBand = max(0.0, lit - shadowDrop);
                        }
                        else
                        {
                            addBand = BandContribution(dot(N, l.direction), l.distanceAttenuation, l.shadowAttenuation, l.color);
                        }
                        totalLight = useAdditive ? (totalLight + addBand) : max(totalLight, addBand);
                    LIGHT_LOOP_END
                }

                totalLight = saturate(totalLight);

                float3 color = ToonRamp(totalLight) * _BaseColor.rgb;
                float3 fogged = MixFog(color, input.fogCoord);
                color = lerp(color, fogged, _ApplyFog);
                return float4(color, texColor.a);
            }
            ENDHLSL
        }

        // ── SHADOW CASTER ──
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ColorShadow;
                float4 _ColorBase;
                float4 _ColorHighlight;
                float4 _MainTex_ST;
                float  _Cutoff;
                float  _Threshold1;
                float  _Threshold2;
                float  _IntensityAffectsBands;
                float  _BandAccumulation;
                float  _ApplyFog;
            CBUFFER_END

            float4 _WaveAndDistance;
            float4 _CameraPosition;
            float  _ShakeWindSpeed;
            float  _ShakeBending;

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 texcoord   : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct ShadowVaryings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            void FastSinCos(float4 val, out float4 s, out float4 c)
            {
                val = val * 6.408849 - 3.141593;
                float4 r5 = val * val;
                float4 r6 = r5 * r5;
                float4 r7 = r6 * r5;
                float4 r8 = r6 * r5 * val;
                s = val + val * r5 * (-0.16161616) + r8 * 0.0083333;
                c = 1.0 + r5 * (-0.4999999) + r6 * 0.0416666 + r7 * (-0.00138888);
            }

            float3 GrassWaveAnimation(float3 positionOS, float waveScale)
            {
                float3 worldPos = TransformObjectToWorld(positionOS);
                float phaseOffset = (worldPos.x + worldPos.z) * 0.1;
                float4 wavePhase = frac(float4(
                    phaseOffset + _Time.y * _ShakeWindSpeed,
                    phaseOffset * 0.7 + _Time.y * _ShakeWindSpeed * 1.3,
                    phaseOffset * 1.3 + _Time.y * _ShakeWindSpeed * 0.8,
                    phaseOffset * 0.5 + _Time.y * _ShakeWindSpeed * 1.7
                ));
                float4 s, c;
                FastSinCos(wavePhase, s, c);
                float combinedWave = (s.x + s.y * 0.5 + s.z * 0.3 + s.w * 0.2) * 0.5;
                float3 displacement = _WaveAndDistance.xyz * combinedWave * waveScale * _ShakeBending;
                return positionOS + displacement;
            }

            float4 GetShadowPositionHClip(float3 positionWS, float3 normalWS)
            {
                float4 positionCS;
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #else
                positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
            #endif

            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif

                return positionCS;
            }

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings o;
                float3 animatedPos = GrassWaveAnimation(input.positionOS.xyz, input.color.a);
                float3 positionWS = TransformObjectToWorld(animatedPos);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.positionHCS = GetShadowPositionHClip(positionWS, normalWS);
                o.uv = TRANSFORM_TEX(input.texcoord, _MainTex);
                return o;
            }

            float4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                float alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // ── DEPTH ONLY ──
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ColorShadow;
                float4 _ColorBase;
                float4 _ColorHighlight;
                float4 _MainTex_ST;
                float  _Cutoff;
                float  _Threshold1;
                float  _Threshold2;
                float  _IntensityAffectsBands;
                float  _BandAccumulation;
                float  _ApplyFog;
            CBUFFER_END

            float4 _WaveAndDistance;
            float4 _CameraPosition;
            float  _ShakeWindSpeed;
            float  _ShakeBending;

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 texcoord   : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct DepthVaryings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            void FastSinCos(float4 val, out float4 s, out float4 c)
            {
                val = val * 6.408849 - 3.141593;
                float4 r5 = val * val;
                float4 r6 = r5 * r5;
                float4 r7 = r6 * r5;
                float4 r8 = r6 * r5 * val;
                s = val + val * r5 * (-0.16161616) + r8 * 0.0083333;
                c = 1.0 + r5 * (-0.4999999) + r6 * 0.0416666 + r7 * (-0.00138888);
            }

            float3 GrassWaveAnimation(float3 positionOS, float waveScale)
            {
                float3 worldPos = TransformObjectToWorld(positionOS);
                float phaseOffset = (worldPos.x + worldPos.z) * 0.1;
                float4 wavePhase = frac(float4(
                    phaseOffset + _Time.y * _ShakeWindSpeed,
                    phaseOffset * 0.7 + _Time.y * _ShakeWindSpeed * 1.3,
                    phaseOffset * 1.3 + _Time.y * _ShakeWindSpeed * 0.8,
                    phaseOffset * 0.5 + _Time.y * _ShakeWindSpeed * 1.7
                ));
                float4 s, c;
                FastSinCos(wavePhase, s, c);
                float combinedWave = (s.x + s.y * 0.5 + s.z * 0.3 + s.w * 0.2) * 0.5;
                float3 displacement = _WaveAndDistance.xyz * combinedWave * waveScale * _ShakeBending;
                return positionOS + displacement;
            }

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings o;
                float3 animatedPos = GrassWaveAnimation(input.positionOS.xyz, input.color.a);
                o.positionHCS = TransformObjectToHClip(animatedPos);
                o.uv = TRANSFORM_TEX(input.texcoord, _MainTex);
                return o;
            }

            float4 DepthFrag(DepthVaryings input) : SV_Target
            {
                float alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // ── DEPTH NORMALS (for SSAO) ──
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ColorShadow;
                float4 _ColorBase;
                float4 _ColorHighlight;
                float4 _MainTex_ST;
                float  _Cutoff;
                float  _Threshold1;
                float  _Threshold2;
                float  _IntensityAffectsBands;
                float  _BandAccumulation;
                float  _ApplyFog;
            CBUFFER_END

            float4 _WaveAndDistance;
            float4 _CameraPosition;
            float  _ShakeWindSpeed;
            float  _ShakeBending;

            struct DNAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 texcoord   : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct DNVaryings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float2 uv          : TEXCOORD1;
            };

            void FastSinCos(float4 val, out float4 s, out float4 c)
            {
                val = val * 6.408849 - 3.141593;
                float4 r5 = val * val;
                float4 r6 = r5 * r5;
                float4 r7 = r6 * r5;
                float4 r8 = r6 * r5 * val;
                s = val + val * r5 * (-0.16161616) + r8 * 0.0083333;
                c = 1.0 + r5 * (-0.4999999) + r6 * 0.0416666 + r7 * (-0.00138888);
            }

            float3 GrassWaveAnimation(float3 positionOS, float waveScale)
            {
                float3 worldPos = TransformObjectToWorld(positionOS);
                float phaseOffset = (worldPos.x + worldPos.z) * 0.1;
                float4 wavePhase = frac(float4(
                    phaseOffset + _Time.y * _ShakeWindSpeed,
                    phaseOffset * 0.7 + _Time.y * _ShakeWindSpeed * 1.3,
                    phaseOffset * 1.3 + _Time.y * _ShakeWindSpeed * 0.8,
                    phaseOffset * 0.5 + _Time.y * _ShakeWindSpeed * 1.7
                ));
                float4 s, c;
                FastSinCos(wavePhase, s, c);
                float combinedWave = (s.x + s.y * 0.5 + s.z * 0.3 + s.w * 0.2) * 0.5;
                float3 displacement = _WaveAndDistance.xyz * combinedWave * waveScale * _ShakeBending;
                return positionOS + displacement;
            }

            DNVaryings DepthNormalsVert(DNAttributes input)
            {
                DNVaryings o;
                float3 animatedPos = GrassWaveAnimation(input.positionOS.xyz, input.color.a);
                o.positionHCS = TransformObjectToHClip(animatedPos);
                o.normalWS    = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.texcoord, _MainTex);
                return o;
            }

            float4 DepthNormalsFrag(DNVaryings input) : SV_Target
            {
                float alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                clip(alpha - _Cutoff);
                return float4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
