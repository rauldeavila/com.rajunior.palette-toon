Shader "Custom/PaletteToonRamp_Terrain"
{
    Properties
    {
        [MainColor] _BaseColor("Base Tint", Color) = (1, 1, 1, 1)

        // Per-layer toon colors (4 layers × 3 bands = 12 colors)
        [HideInInspector] _ColorShadow_L0("Shadow L0", Color)       = (0.15, 0.1, 0.2, 1)
        [HideInInspector] _ColorBase_L0("Base L0", Color)            = (0.4, 0.3, 0.5, 1)
        [HideInInspector] _ColorHighlight_L0("Highlight L0", Color)  = (0.9, 0.85, 1, 1)

        [HideInInspector] _ColorShadow_L1("Shadow L1", Color)       = (0.15, 0.1, 0.2, 1)
        [HideInInspector] _ColorBase_L1("Base L1", Color)            = (0.4, 0.3, 0.5, 1)
        [HideInInspector] _ColorHighlight_L1("Highlight L1", Color)  = (0.9, 0.85, 1, 1)

        [HideInInspector] _ColorShadow_L2("Shadow L2", Color)       = (0.15, 0.1, 0.2, 1)
        [HideInInspector] _ColorBase_L2("Base L2", Color)            = (0.4, 0.3, 0.5, 1)
        [HideInInspector] _ColorHighlight_L2("Highlight L2", Color)  = (0.9, 0.85, 1, 1)

        [HideInInspector] _ColorShadow_L3("Shadow L3", Color)       = (0.15, 0.1, 0.2, 1)
        [HideInInspector] _ColorBase_L3("Base L3", Color)            = (0.4, 0.3, 0.5, 1)
        [HideInInspector] _ColorHighlight_L3("Highlight L3", Color)  = (0.9, 0.85, 1, 1)

        // Splatmap (assigned automatically by Unity terrain system)
        [HideInInspector] _Control("Splatmap", 2D) = "red" {}

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
            "RenderType" = "Opaque"
            "Queue" = "Geometry-100"
            "RenderPipeline" = "UniversalPipeline"
            "TerrainCompatible" = "True"
        }

        Pass
        {
            Name "TerrainToonForward"
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
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float  fogCoord    : TEXCOORD2;
                float2 controlUV   : TEXCOORD3;
            };

            TEXTURE2D(_Control);
            SAMPLER(sampler_Control);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;

                float4 _ColorShadow_L0;
                float4 _ColorBase_L0;
                float4 _ColorHighlight_L0;

                float4 _ColorShadow_L1;
                float4 _ColorBase_L1;
                float4 _ColorHighlight_L1;

                float4 _ColorShadow_L2;
                float4 _ColorBase_L2;
                float4 _ColorHighlight_L2;

                float4 _ColorShadow_L3;
                float4 _ColorBase_L3;
                float4 _ColorHighlight_L3;

                float4 _Control_ST;
                float  _Threshold1;
                float  _Threshold2;
                float  _IntensityAffectsBands;
                float  _BandAccumulation;
                float  _ApplyFog;
            CBUFFER_END

            float Luminance3(float3 c)
            {
                return dot(c, float3(0.2126, 0.7152, 0.0722));
            }

            float3 ToonRamp(float intensity, float3 shadow, float3 base, float3 highlight)
            {
                if (intensity < _Threshold1)  return shadow;
                if (intensity < _Threshold2)  return base;
                return highlight;
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
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   n = GetVertexNormalInputs(input.normalOS);

                o.positionHCS = p.positionCS;
                o.positionWS  = p.positionWS;
                o.normalWS    = n.normalWS;
                o.fogCoord    = ComputeFogFactor(p.positionCS.z);
                o.controlUV   = TRANSFORM_TEX(input.uv, _Control);
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

                // ── splatmap blending ──
                float4 splat = SAMPLE_TEXTURE2D(_Control, sampler_Control, input.controlUV);

                float3 c0 = ToonRamp(totalLight, _ColorShadow_L0.rgb, _ColorBase_L0.rgb, _ColorHighlight_L0.rgb);
                float3 c1 = ToonRamp(totalLight, _ColorShadow_L1.rgb, _ColorBase_L1.rgb, _ColorHighlight_L1.rgb);
                float3 c2 = ToonRamp(totalLight, _ColorShadow_L2.rgb, _ColorBase_L2.rgb, _ColorHighlight_L2.rgb);
                float3 c3 = ToonRamp(totalLight, _ColorShadow_L3.rgb, _ColorBase_L3.rgb, _ColorHighlight_L3.rgb);

                float3 color = c0 * splat.r + c1 * splat.g + c2 * splat.b + c3 * splat.a;
                color *= _BaseColor.rgb;

                float3 fogged = MixFog(color, input.fogCoord);
                color = lerp(color, fogged, _ApplyFog);
                return float4(color, 1.0);
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
