Shader "Hidden/EmbodiedAI/LinearDepth"
{
    // Renders linear eye-space depth as grayscale.
    // 0 (black) = near plane, 1 (white) = far plane.
    // Designed for URP on macOS Metal.

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "LinearDepth"
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float  linearDepth : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;

                // Linear eye-space depth normalized to [0, 1] over [near, far]
                float eyeDepth = -posInputs.positionVS.z;
                output.linearDepth = (eyeDepth - _ProjectionParams.y) /
                                     (_ProjectionParams.z - _ProjectionParams.y);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float d = saturate(input.linearDepth);
                return half4(d, d, d, 1.0);
            }
            ENDHLSL
        }
    }
}
