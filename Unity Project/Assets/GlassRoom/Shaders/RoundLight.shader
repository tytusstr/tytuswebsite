// A small round light (a ceiling downlight's glowing lens or its trim ring) that stays smooth
// at any distance. A light only a pixel or two across flickers as you move: some frames it
// lands on a pixel, some frames it falls between pixels. Here, once the disc would be drawn
// smaller than _MinPixels across, it is drawn at that size instead and dimmed to match, so it
// gives off the same total light and keeps a steady round shape. Works in every view that
// draws it (the main view and the floor's and walls' mirror images, each at its own size).
Shader "GlassRoom/Round Light"
{
    Properties
    {
        [NoScaleOffset] _BaseMap("Disc (white circle with soft edge)", 2D) = "white" {}
        [HDR] _BaseColor("Colour", Color) = (1, 1, 1, 1)
        _MinPixels("Smallest Size On Screen (pixels)", Float) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            // Added on top of what's behind: a light only ever brightens.
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _MinPixels;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fade : TEXCOORD1;
            };

            // How many pixels a world-space step from the disc's centre covers on screen.
            float PixelLength(float3 centreWS, float3 stepWS)
            {
                float4 a = TransformWorldToHClip(centreWS);
                float4 b = TransformWorldToHClip(centreWS + stepWS);
                float2 d = (b.xy / max(b.w, 1e-4) - a.xy / max(a.w, 1e-4)) * 0.5 * _ScreenParams.xy;
                return length(d);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 centre = TransformObjectToWorld(float3(0, 0, 0));
                // The disc's two radii in the world (the square is 1 x 1 in its own space).
                float3 axisX = TransformObjectToWorld(float3(0.5, 0, 0)) - centre;
                float3 axisZ = TransformObjectToWorld(float3(0, 0, 0.5)) - centre;
                // Each radius is stretched so the disc is at least _MinPixels across along it
                // (seen at a slant, the short way round still stays visible).
                float px = PixelLength(centre, axisX) * 2;
                float pz = PixelLength(centre, axisZ) * 2;
                float kx = max(1, _MinPixels / max(px, 1e-3));
                float kz = max(1, _MinPixels / max(pz, 1e-3));
                float3 world = centre + axisX * (input.positionOS.x * 2 * kx) + axisZ * (input.positionOS.z * 2 * kz);
                o.positionCS = TransformWorldToHClip(world);
                o.uv = input.uv;
                // Spread over a bigger area, each pixel gets proportionally less.
                o.fade = 1 / (kx * kz);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
                return half4(_BaseColor.rgb * (a * input.fade), 1);
            }
            ENDHLSL
        }
    }
}
