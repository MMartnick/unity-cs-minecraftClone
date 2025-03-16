Shader "Custom/ToonOutlineURP6"
{
    Properties
    {
        _OutlineColor("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth("Outline Width", Range(0,0.1)) = 0.03

        _Color("Tint Color", Color) = (1,1,1,1)
        _MainTex("Base Map (Albedo)", 2D) = "white" {}
        _NormalMap("Normal Map (Tangent)", 2D) = "bump" {}
        _BumpScale("Normal Intensity", Range(0.0,4.0)) = 1.0

        [HDR]_AmbientColor("Ambient Color", Color) = (0.4,0.4,0.4,1)
        [HDR]_SpecularColor("Specular Color", Color) = (0.9,0.9,0.9,1)
        _Glossiness("Glossiness", Range(1,128)) = 32

        [HDR]_RimColor("Rim Color", Color) = (1,1,1,1)
        _RimAmount("Rim Amount", Range(0,1)) = 0.75
        _RimThreshold("Rim Threshold", Range(0,1)) = 0.1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalRenderPipeline" }

        Pass
        {
            Name "OutlinePass"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            Offset 1,1

            HLSLPROGRAM
            #pragma vertex VertOutline
            #pragma fragment FragOutline
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            Varyings VertOutline(Attributes IN)
            {
                Varyings OUT;
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float3 posWS = TransformObjectToWorld(IN.positionOS);
                posWS += normalWS * _OutlineWidth;
                OUT.positionHCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            half4 FragOutline(Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ToonForwardPass"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD1;
                float3 tangentWS   : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                float2 uv          : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_ST;
                float4 _SpecularColor;
                float4 _RimColor;
                float4 _AmbientColor;
                float _BumpScale;
                float _Glossiness;
                float _RimAmount;
                float _RimThreshold;
            CBUFFER_END

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS);
                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                float3 finalColor = texColor.rgb * _Color.rgb;
                return half4(finalColor, texColor.a);
            }
            ENDHLSL
        }
    }
}