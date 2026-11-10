// Skybox: deep indigo night with a purple-magenta nebula band, dark dust lanes,
// and procedurally placed stars that gently twinkle.
Shader "GlassRoom/Night Sky"
{
    Properties
    {
        _Zenith("Zenith", Color) = (0.006, 0.005, 0.022, 1)
        _Horizon("Horizon", Color) = (0.05, 0.025, 0.08, 1)
        _NebulaA("Nebula Purple", Color) = (0.32, 0.1, 0.55, 1)
        _NebulaB("Nebula Magenta", Color) = (0.75, 0.16, 0.45, 1)
        _NebulaC("Nebula Teal", Color) = (0.06, 0.32, 0.42, 1)
        _NebulaStrength("Nebula Strength", Range(0, 3)) = 1.1
        _StarDensity("Star Density", Range(0, 1)) = 0.22
        _StarBrightness("Star Brightness", Float) = 40
        _Twinkle("Twinkle", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith;
                half4 _Horizon;
                half4 _NebulaA;
                half4 _NebulaB;
                half4 _NebulaC;
                half _NebulaStrength;
                half _StarDensity;
                half _StarBrightness;
                half _Twinkle;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            float3 Hash3(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)),
                           dot(p, float3(269.5, 183.3, 246.1)),
                           dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453);
            }

            float Hash1(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }

            float Noise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3 - 2 * f);
                float n000 = Hash1(i), n100 = Hash1(i + float3(1, 0, 0));
                float n010 = Hash1(i + float3(0, 1, 0)), n110 = Hash1(i + float3(1, 1, 0));
                float n001 = Hash1(i + float3(0, 0, 1)), n101 = Hash1(i + float3(1, 0, 1));
                float n011 = Hash1(i + float3(0, 1, 1)), n111 = Hash1(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            float Fbm(float3 p)
            {
                float sum = 0, amp = 0.5;
                for (int o = 0; o < 4; o++)
                {
                    sum += Noise(p) * amp;
                    p = p * 2.03 + 11.7;
                    amp *= 0.5;
                }
                return sum;
            }

            // One layer of stars: each cell of a 3D grid may hold one star.
            half3 StarLayer(float3 dir, float scale, float density, float sizeMin, float sizeMax)
            {
                float3 p = dir * scale;
                float3 cell = floor(p);
                float3 h = Hash3(cell);
                if (h.x > density)
                    return 0;

                float3 starPos = cell + 0.25 + 0.5 * Hash3(cell + 17.0);
                float d = length(p - starPos);
                float size = lerp(sizeMin, sizeMax, h.y * h.y);
                // Keep every star at least ~1.5 pixels wide so it stays a crisp point instead of
                // vanishing between pixels, and dim it to keep its total brightness the same.
                float pixel = length(fwidth(p));
                float radius = max(size, pixel * 1.5);
                float core = saturate(1 - d / radius);
                core = core * core * (size * size) / (radius * radius);

                float twinkle = 1 - _Twinkle * 0.5 * (1 + sin(_Time.y * (1.5 + 3 * h.z) + h.y * 40));
                // Mostly white, with a few warm and blue stars.
                half3 tint = lerp(half3(0.75, 0.85, 1.0), half3(1.0, 0.88, 0.75), h.z);
                return tint * core * twinkle * (0.25 + 0.75 * h.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.direction);
                half up = saturate(dir.y);
                half3 sky = lerp(_Horizon.rgb, _Zenith.rgb, pow(up, 0.5));

                // A galaxy band crossing high overhead, so it shows through a skylight.
                float3 bandNormal = normalize(float3(0.8, 0.06, -0.57));
                float bandDist = dot(dir, bandNormal);
                float band = exp(-bandDist * bandDist / 0.12);

                float clouds = Fbm(dir * 3.2);
                float detail = Fbm(dir * 7.5 + 5.3);
                float dust = Fbm(dir * 11 + 21.1);

                half3 nebulaColor = lerp(_NebulaA.rgb, _NebulaB.rgb, smoothstep(0.35, 0.7, detail));
                nebulaColor = lerp(nebulaColor, _NebulaC.rgb, smoothstep(0.55, 0.8, clouds) * 0.5);
                float glow = band * smoothstep(0.3, 0.8, clouds) + 0.25 * smoothstep(0.55, 0.85, clouds);
                half3 nebula = nebulaColor * glow * 0.28 * _NebulaStrength;
                // A bright, soft core along the middle of the band.
                nebula += half3(0.55, 0.42, 0.7) * band * band * smoothstep(0.4, 0.75, detail) * 0.12 * _NebulaStrength;
                // Dark dust lanes cutting through it.
                nebula *= 1 - 0.75 * band * smoothstep(0.45, 0.7, dust);

                float density = _StarDensity * (1 + 2.5 * band);
                half3 stars = StarLayer(dir, 140, density * 0.3, 0.05, 0.12)
                            + StarLayer(dir, 320, density, 0.05, 0.09)
                            + StarLayer(dir, 700, density * 1.5, 0.06, 0.09) * 0.6;

                return half4(sky + nebula + stars * _StarBrightness, 1);
            }
            ENDHLSL
        }
    }
}
