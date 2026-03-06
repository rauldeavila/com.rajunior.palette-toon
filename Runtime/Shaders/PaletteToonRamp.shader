Shader "Custom/PaletteToonRamp"
{
    Properties
    {
        [MainColor] _BaseColor("Base Tint", Color) = (1, 1, 1, 1)

        [HideInInspector] _ColorShadow("Shadow Color", Color)       = (0.15, 0.1, 0.2, 1)
        [HideInInspector] _ColorBase("Base Color", Color)            = (0.4, 0.3, 0.5, 1)
        [HideInInspector] _ColorHighlight("Highlight Color", Color)  = (0.9, 0.85, 1, 1)

        _Threshold1("Shadow Threshold", Range(0, 1)) = 0.35
        _Threshold2("Highlight Threshold", Range(0, 1)) = 0.75

        [Header(Lighting Behavior)]
        _UseRangePercentForLocalLights("Use Range Percent For Local Lights", Range(0, 1)) = 1
        _IntensityAffectsBands("Intensity Affects Bands", Range(0, 1)) = 0
        _BandAccumulation("Band Accumulation (0 Add / 1 Max)", Range(0, 1)) = 1
        _ApplyFog("Apply Fog", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        // ── FORWARD PASS — "ForwardOnly" forces per-pixel lighting in ALL
        //    rendering modes (Forward, Forward+, and Deferred).
        //    With plain "UniversalForward" in Deferred mode, additional
        //    lights are never applied to the object. ──
        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForwardOnly" }

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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float  fogCoord    : TEXCOORD2;
            };

            // NOTE: declared OUTSIDE UnityPerMaterial CBUFFER on purpose.
            // This disables SRP Batcher for this shader so that
            // MaterialPropertyBlock overrides actually reach the GPU.
            float4 _BaseColor;
            float4 _ColorShadow;
            float4 _ColorBase;
            float4 _ColorHighlight;
            float  _Threshold1;
            float  _Threshold2;
            float  _UseRangePercentForLocalLights;
            float  _IntensityAffectsBands;
            float  _BandAccumulation;
            float  _ApplyFog;

            // perceptual luminance — extracts brightness from a light's color
            // (light.color already includes intensity in URP)
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
                float geometric = saturate(NdotL) * distanceAttenuation * shadowAttenuation;
                float withIntensity = geometric * Luminance3(lightColor);
                return lerp(geometric, withIntensity, _IntensityAffectsBands);
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
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   n = GetVertexNormalInputs(input.normalOS);

                o.positionHCS = p.positionCS;
                o.positionWS  = p.positionWS;
                o.normalWS    = n.normalWS;
                o.fogCoord    = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float3 N = normalize(input.normalWS);
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

                // ── additional lights (point / spot / extra directional) ──
                // No #if guard — always runs. GetAdditionalLightsCount()
                // returns 0 when there are none, so the loop is a no-op.
                // LIGHT_LOOP_BEGIN/END handles both Forward+ (cluster) and
                // regular forward (simple for-loop) automatically.
                {
                    uint count = GetAdditionalLightsCount();
                    InputData inputData = (InputData)0;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionHCS);
                    inputData.positionWS = input.positionWS;

                    LIGHT_LOOP_BEGIN(count)
                        // Use overload with shadow mask so point/spot shadows are applied.
                        Light l = GetAdditionalLight(lightIndex, input.positionWS, half4(1.0, 1.0, 1.0, 1.0));
                        float addBand;
                        if (_UseRangePercentForLocalLights > 0.5)
                        {
                            float localRange = LocalLightRangeSignal(lightIndex, input.positionWS, l.distanceAttenuation);
                            if (localRange >= 0.0)
                            {
                                addBand = localRange * l.shadowAttenuation;
                            }
                            else
                            {
                                addBand = BandContribution(dot(N, l.direction), l.distanceAttenuation, l.shadowAttenuation, l.color);
                            }
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
                return float4(color, 1.0);
            }
            ENDHLSL
        }

        // ── SHADOW CASTER — makes this object cast shadows ──
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        // ── DEPTH ONLY ──
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthAttributes { float4 positionOS : POSITION; };
            struct DepthVaryings   { float4 positionHCS : SV_POSITION; };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings o;
                o.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            float4 DepthFrag(DepthVaryings input) : SV_Target { return 0; }
            ENDHLSL
        }

        // ── DEPTH NORMALS (for SSAO) ──
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DNAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct DNVaryings   { float4 positionHCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            DNVaryings DepthNormalsVert(DNAttributes input)
            {
                DNVaryings o;
                o.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS    = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            float4 DepthNormalsFrag(DNVaryings input) : SV_Target
            {
                return float4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
