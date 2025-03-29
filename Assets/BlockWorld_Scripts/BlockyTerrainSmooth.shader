Shader "Custom/BlockyTerrainSmooth"
{
    Properties
    {
        _BaseTex("Grass Texture (Albedo)", 2D) = "white" {}
        _SecondTex("Dirt Texture (Albedo)", 2D) = "white" {}
        _Tint("Tint Color", Color) = (1,1,1,1)
        _NormalSmooth("Normal Smoothing", Range(0,1)) = 0.5
        _MinHeight("Min Height (full grass)", Float) = 0.0
        _MaxHeight("Max Height (no grass)", Float) = 20.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend One Zero
            Cull Back

            HLSLPROGRAM
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 4.5

            // Enable URP lighting variants
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _MIXED_LIGHTING_SUBTRACTIVE

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            // Include URP libraries for transforms + lighting
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Material properties
            TEXTURE2D(_BaseTex);   SAMPLER(sampler_BaseTex);
            TEXTURE2D(_SecondTex); SAMPLER(sampler_SecondTex);

            float4 _Tint;         // RGBA tint color
            float  _NormalSmooth; // [0..1], how strongly we lerp the normal upward
            float  _MinHeight;
            float  _MaxHeight;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS   : SV_POSITION;
                float3 normalWS_smooth : TEXCOORD0; // for lighting
                float3 normalWS_raw    : TEXCOORD1; // for slope detection
                float3 worldPos        : TEXCOORD2;
                float2 uv             : TEXCOORD3;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                // Transform vertex to clip space
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS);

                // Compute world-space position
                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz);

                // Original world-space normal (for slope-based blending)
                float3 normalWS_raw = normalize(mul((float3x3)unity_ObjectToWorld, IN.normalOS));
                OUT.normalWS_raw = normalWS_raw;

                // Also create a 'smoothed' normal for lighting
                float3 upDir = float3(0,1,0);
                float slopeFactor = _NormalSmooth * (1.0 - abs(normalWS_raw.y));
                float3 normalWS_smooth = normalize(lerp(normalWS_raw, upDir, slopeFactor));
                OUT.normalWS_smooth = normalWS_smooth;

                // Pass UV
                OUT.uv = IN.uv;
                return OUT;
            }

            inline float3 DiffuseLambert(float3 lightColor, float3 lightDir, float3 normal)
            {
                normal = normalize(normal);
                float NdotL = saturate(dot(normal, lightDir));
                return lightColor * NdotL;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                // (1) For lighting, use the 'smoothed' normal
                float3 N_light = normalize(IN.normalWS_smooth);

                // (2) For slope detection, use the 'raw' normal
                float3 N_raw   = normalize(IN.normalWS_raw);

                // slopeFactor ~ 1 if horizontal (N.y=1), 0 if vertical (N.y=0)
                float rawSlope = saturate(N_raw.y);

                // Use smoothstep to reduce flicker near the boundary
                float slopeFactor = smoothstep(0.2, 0.8, rawSlope);

                // Height factor
                float heightRange = max(0.001, (_MaxHeight - _MinHeight));
                float heightFactor = saturate((_MaxHeight - IN.worldPos.y) / heightRange);

                // final grass weight
                float grassWeight = slopeFactor * heightFactor;

                // Sample textures
                float4 colGrass = SAMPLE_TEXTURE2D(_BaseTex, sampler_BaseTex, IN.uv);
                float4 colDirt  = SAMPLE_TEXTURE2D(_SecondTex, sampler_SecondTex, IN.uv);
                float4 baseColor = lerp(colDirt, colGrass, grassWeight);

                // Apply tint
                baseColor.rgb *= _Tint.rgb;
                baseColor.a   *= _Tint.a;

                // Lighting accumulation
                float3 lighting = 0.0;

                // Main directional light
                Light mainLight = GetMainLight();
                lighting += DiffuseLambert(mainLight.color * mainLight.shadowAttenuation,
                                           mainLight.direction, N_light);

                // Additional lights
                uint lightCount = GetAdditionalLightsCount();
                for (uint i = 0; i < lightCount; i++)
                {
                    Light curLight = GetAdditionalLight(i, IN.worldPos);
                    float3 lightCol = curLight.color
                                      * (curLight.distanceAttenuation * curLight.shadowAttenuation);
                    lighting += DiffuseLambert(lightCol, curLight.direction, N_light);
                }

                float3 finalRGB = baseColor.rgb * lighting;
                return float4(finalRGB, baseColor.a);
            }
            ENDHLSL
        }
    }
}
