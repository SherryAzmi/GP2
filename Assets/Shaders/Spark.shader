// Glowing spark for fireworks (URP, additive, unlit). Colour from the particle; a soft round dot with a hot white core.
Shader "GP2/Spark"
{
    Properties
    {
        _Intensity ("Brightness", Range(0.5, 3)) = 1.4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        // Normal blending (not additive): in daylight an additive glow washes out to white against the bright sky.
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Spark"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Intensity;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            V vert(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                // Trails stretch the uv along their length; the dot shape then becomes a soft streak.
                float d = length(i.uv - 0.5) * 2.0;
                half glow = saturate(1.0 - d);
                half core = saturate(1.0 - d * 2.2);
                // Saturated colour with a small white-hot centre; solid in the middle, soft at the edge.
                half3 col = lerp(i.color.rgb, half3(1, 1, 0.9), core * 0.55) * _Intensity;
                half alpha = saturate(glow * 1.6) * i.color.a;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
