// The cloud video high above the skylight (SkyVideo): shown as it is (it's a photo of the sky,
// no lighting), fading out softly toward its edges into the sky photo around it.
Shader "GlassRoom/Sky Video"
{
    Properties
    {
        _MainTex("Video", 2D) = "black" {}
        _Exposure("Brightness", Float) = 1.1
        _EdgeFade("Edge Fade (fraction)", Range(0.01, 0.5)) = 0.18
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _Exposure;
                half _EdgeFade;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb;
                // (The video's texture is an sRGB one, so this already reads as linear colour.)
                c *= _Exposure;
                float2 e = min(input.uv, 1 - input.uv);
                half a = smoothstep(0, _EdgeFade, e.x) * smoothstep(0, _EdgeFade, e.y);
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
