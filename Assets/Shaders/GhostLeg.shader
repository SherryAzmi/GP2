// "Shadow" guide leg: a dark see-through copy of the patient's shin showing where to put it.
// A glowing rim (cyan while aiming, green once the real leg matches) pulses gently to draw the eye.
Shader "GP2/GhostLeg"
{
    Properties
    {
        _Color ("Body", Color) = (0.03, 0.05, 0.1, 0.5)
        _RimColor ("Rim", Color) = (0.3, 0.9, 1, 1)
        _Pulse ("Pulse", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color, _RimColor;
                half _Pulse;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            V vert(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0 - saturate(abs(dot(normalize(i.normalWS), viewDir))), 1.5);
                half pulse = 1.0 - _Pulse + _Pulse * (0.5 + 0.5 * sin(_Time.y * 4.0));
                // Dark shadow body with a bright glowing outline (over-bright so it reads in sunlight).
                half3 col = lerp(_Color.rgb, _RimColor.rgb * 1.6, saturate(rim * 1.3) * pulse);
                half alpha = saturate(_Color.a + rim * pulse * 0.8);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
