// Polished glass floor: URP lighting plus a sharp mirror reflection rendered by PlanarReflection.
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
            CBUFFER_END

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
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = _BaseColor.rgb;
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.occlusion = 1;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);

                // The reflection camera's image is mirrored left-to-right; flip it back.
                float2 reflectionUV = float2(1 - inputData.normalizedScreenSpaceUV.x, inputData.normalizedScreenSpaceUV.y);
                half3 reflection = SAMPLE_TEXTURE2D(_PlanarReflectionTex, sampler_PlanarReflectionTex, reflectionUV).rgb;
                half facing = saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
                half fresnel = _MinReflectivity + (1 - _MinReflectivity) * pow(1 - facing, _FresnelPower);
                half amount = fresnel * _ReflectionStrength * _PlanarReflectionWeight;
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
