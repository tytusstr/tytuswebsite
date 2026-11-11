// Glossy white wall with a true mirror reflection rendered by MirrorSurface (one per wall).
// Without a live mirror image (Low graphics, wall out of view, or inside another mirror) it
// shows the soft pre-captured reflection of the hall instead.
Shader "GlassRoom/Mirror Wall"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.93, 0.94, 0.95, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.93
        _Metallic("Metallic", Range(0, 1)) = 0.05
        _ReflectionStrength("Reflection Strength", Range(0, 1)) = 0.5
        _MinReflectivity("Reflectivity Facing Straight On", Range(0, 1)) = 0.1
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
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fog

            // The reflection is added below (mirror or captured), not by the lighting itself.
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

            TEXTURE2D(_MirrorTex);
            SAMPLER(sampler_MirrorTex);
            float _MirrorWeight;     // per wall: 1 while its mirror image is live
            float _MirrorsDisabled;  // 1 while another mirror is being drawn

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

                half3 reflection;
                if (_MirrorWeight * (1 - _MirrorsDisabled) > 0.5)
                {
                    // The mirror camera's image is flipped left-to-right; flip it back.
                    float2 uv = float2(1 - inputData.normalizedScreenSpaceUV.x, inputData.normalizedScreenSpaceUV.y);
                    reflection = SAMPLE_TEXTURE2D(_MirrorTex, sampler_MirrorTex, uv).rgb;
                }
                else
                {
                    half3 reflectVector = reflect(-inputData.viewDirectionWS, inputData.normalWS);
                    half perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(_Smoothness);
                    reflection = GlossyEnvironmentReflection(reflectVector, inputData.positionWS, perceptualRoughness, 1, inputData.normalizedScreenSpaceUV);
                }
                half facing = saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
                half fresnel = _MinReflectivity + (1 - _MinReflectivity) * pow(1 - facing, _FresnelPower);
                half amount = fresnel * _ReflectionStrength;
                color.rgb = color.rgb * (1 - amount * 0.6) + reflection * amount;

                color.rgb = MixFog(color.rgb, input.fogFactor);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }

    FallBack "Universal Render Pipeline/Lit"
}
