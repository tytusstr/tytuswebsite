// Polished floor (black marble, Textures/Black Marble *): URP lighting plus a sharp mirror reflection rendered by PlanarReflection.
Shader "GlassRoom/Planar Glass Floor"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.86, 0.87, 0.88, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.97
        _Metallic("Metallic", Range(0, 1)) = 0
        _ReflectionStrength("Reflection Strength", Range(0, 1)) = 0.9
        _MinReflectivity("Reflectivity Facing Down", Range(0, 1)) = 0.45
        _FresnelPower("Fresnel Power", Range(1, 8)) = 4
        [NoScaleOffset] _BaseMap("Surface (e.g. marble)", 2D) = "white" {}
        [NoScaleOffset][Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        [NoScaleOffset] _RoughnessMap("Roughness Map", 2D) = "black" {}
        _TileSize("Texture Tile Size (m)", Float) = 3
        _BumpScale("Normal Strength", Range(0, 2)) = 0.4
        _RippleStrength("Reflection Ripple", Range(0, 0.05)) = 0.006
        _ReflectionBlur("Reflection Blur", Range(0, 0.05)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            // The mirror image replaces the blurry probe reflection, and the hidden room lights
            // shouldn't show up as glints: the only reflected light should be the real ring.
            #define _ENVIRONMENTREFLECTIONS_OFF 1
            #define _SPECULARHIGHLIGHTS_OFF 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Smoothness;
                half _Metallic;
                half _ReflectionStrength;
                half _MinReflectivity;
                half _FresnelPower;
                float _TileSize;
                half _BumpScale;
                half _RippleStrength;
                half _ReflectionBlur;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_RoughnessMap); SAMPLER(sampler_RoughnessMap);

            TEXTURE2D(_PlanarReflectionTex);
            SAMPLER(sampler_PlanarReflectionTex);
            float _PlanarReflectionWeight;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                // The surface texture is laid across the floor in world space, one tile every
                // _TileSize metres, so it never stretches however big the floor is.
                float2 uv = input.positionWS.xz / max(_TileSize, 0.01);
                half3 marble = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                half3 bump = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);
                half roughness = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, uv).r;
                // The floor faces straight up: texture x runs along world x, texture y along world z.
                half3 n = normalize(input.normalWS);
                half3 bumped = normalize(half3(bump.x, bump.z, bump.y));
                inputData.normalWS = n.y > 0.5 ? bumped : n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = _BaseColor.rgb * marble;
                surface.metallic = _Metallic;
                surface.smoothness = saturate(_Smoothness - roughness * 0.5);
                surface.occlusion = 1;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);

                // The reflection camera's image is mirrored left-to-right; flip it back.
                // Tiny unevenness in the polished stone ripples the reflection a little.
                float2 reflectionUV = float2(1 - inputData.normalizedScreenSpaceUV.x, inputData.normalizedScreenSpaceUV.y)
                                    + bump.xy * _RippleStrength;
                // A satin-polished floor blurs what it reflects, most of all up and down (lights
                // stretch into soft streaks toward you), more where the surface is rougher. The
                // reflection has mipmaps (see PlanarReflection): a few taps of a small, smooth
                // level spread along the streak give an even, soft blur.
                half3 reflection = 0;
                float blur = _ReflectionBlur * (0.6 + roughness);
                float lod = blur > 0 ? 2.2 + roughness * 1.5 : 0;
                const int taps = 7;
                UNITY_UNROLL
                for (int k = 0; k < taps; k++)
                {
                    float t = (k / (float)(taps - 1)) * 2 - 1;
                    float2 o = float2(0, t * blur * 2.0);
                    reflection += SAMPLE_TEXTURE2D_LOD(_PlanarReflectionTex, sampler_PlanarReflectionTex, reflectionUV + o, lod).rgb;
                }
                reflection /= taps;
                half facing = saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
                half fresnel = _MinReflectivity + (1 - _MinReflectivity) * pow(1 - facing, _FresnelPower);
                // Rougher patches of the stone reflect a little less.
                half amount = fresnel * _ReflectionStrength * _PlanarReflectionWeight * (1 - roughness * 0.6);
                color.rgb = color.rgb * (1 - amount * 0.7) + reflection * amount;

                color.rgb = MixFog(color.rgb, input.fogFactor);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        // Depth passes so SSAO and the depth texture include the floor.
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }

    FallBack "Universal Render Pipeline/Lit"
}
