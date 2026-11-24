// Realistic beach sand (URP, mobile friendly, world-space so it needs no UVs or textures).
//  - Dry sand: warm colour patches, fine grain, wind ripples, tiny sparkling quartz glints.
//  - Wet sand: darker, smoother and glossy toward the water; it also darkens where the swash
//    has just run (the wave script sets _GP2_SwashZ / _GP2_SwashWet) and dries out again.
//  - Receives the sun's shadows.
Shader "GP2/Sand"
{
    Properties
    {
        _DryColor ("Dry sand", Color) = (0.93, 0.84, 0.67, 1)
        _DryColor2 ("Dry sand (patches)", Color) = (0.84, 0.72, 0.53, 1)
        _WetColor ("Wet sand", Color) = (0.56, 0.46, 0.34, 1)
        _WetLineZ ("Wet line (world Z, sea is lower)", Float) = -1.2
        _WetBlend ("Wet line softness", Float) = 0.6
        _GrainScale ("Grain scale", Float) = 90
        _RippleScale ("Wind ripples per metre", Float) = 2.4
        _RippleStrength ("Ripple strength", Range(0, 1)) = 0.35
        _Sparkle ("Sparkle", Range(0, 4)) = 1.6
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "GP2Noise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DryColor, _DryColor2, _WetColor;
                float _WetLineZ, _WetBlend, _GrainScale, _RippleScale, _RippleStrength, _Sparkle;
            CBUFFER_END
            float _GP2_SwashZ;    // furthest point the swash reached recently (world Z)
            float _GP2_SwashWet;  // 1 just after the water was there, drying to 0

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 pos = i.positionWS;
                float2 p = pos.xz;
                float near = gp2DetailFade(pos, 14.0);

                // --- Wetness: a ragged wet band along the water, plus the trail of the last swash.
                float edgeNoise = gp2Fbm(p * float2(0.7, 1.6)) - 0.5;
                float wetStatic = smoothstep(_WetLineZ + _WetBlend * 0.5, _WetLineZ - _WetBlend * 0.5, pos.z + edgeNoise * 0.5);
                float wetSwash = _GP2_SwashWet * smoothstep(_GP2_SwashZ + 0.12, _GP2_SwashZ - 0.25, pos.z + edgeNoise * 0.12);
                // Under the sea the sand is seen through clear water: wet, but lighter than the dark wet band.
                float under = step(pos.y, 0.0);
                float wet = saturate(max(max(wetStatic, wetSwash), under * 0.45));

                // --- Colour: broad patches, medium mottling and individual grains.
                float patches = gp2Fbm(p * 0.25);
                float mottle = gp2Noise(p * 3.1);
                float grain = gp2Hash(floor(p * _GrainScale));
                half3 dry = lerp(_DryColor.rgb, _DryColor2.rgb, saturate(patches * 1.3 - 0.2) * 0.8 + mottle * 0.2);
                dry *= 1.0 + (grain - 0.5) * 0.18 * near;
                half3 wetCol = _WetColor.rgb * (0.9 + 0.2 * mottle);
                half3 albedo = lerp(dry, wetCol, wet);

                // --- Surface relief: wind ripples on dry sand (bent by noise), grain bumps up close.
                float warp = gp2Fbm(p * 0.45) * 6.0;
                float phase = dot(p, normalize(float2(0.35, 1.0))) * _RippleScale * 6.2832 + warp;
                float ripple = cos(phase) * (0.6 + 0.4 * gp2Noise(p * 0.8));
                float3 n = normalize(i.normalWS);
                float2 slope = normalize(float2(0.35, 1.0)) * ripple * _RippleStrength * 0.35 * (1.0 - wet);
                slope += (gp2Hash2(floor(p * _GrainScale)) - 0.5) * 0.25 * near * (1.0 - wet * 0.7);
                n = normalize(n + float3(slope.x, 0.0, slope.y));

                // --- Lighting
                Light sun = GetMainLight(TransformWorldToShadowCoord(pos));
                float3 V = normalize(GetWorldSpaceViewDir(pos));
                half ndl = saturate(dot(n, sun.direction));
                half shadow = sun.shadowAttenuation * sun.distanceAttenuation;
                half3 col = albedo * (SampleSH(n) * 0.9 + sun.color * ndl * shadow);

                // Wet sand is glossy: sun highlight and a sky sheen at grazing angles.
                float3 H = normalize(sun.direction + V);
                half gloss = pow(saturate(dot(normalize(i.normalWS), H)), 140.0) * 2.2 * shadow;
                half fres = pow(1.0 - saturate(dot(normalize(i.normalWS), V)), 5.0);
                col += wet * (sun.color * gloss + SampleSH(reflect(-V, normalize(i.normalWS))) * fres * 0.35);

                // Quartz sparkles on dry sand: a few grains with their own tilted facet.
                float2 cell = floor(p * 160.0);
                float pick = step(0.986, gp2Hash(cell + 7.3));
                float3 facet = normalize(float3(gp2Hash(cell) - 0.5, 0.9, gp2Hash(cell + 3.1) - 0.5));
                half glint = pow(saturate(dot(facet, H)), 220.0) * pick * _Sparkle * (1.0 - wet) * shadow * near;
                col += sun.color * glint;

                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // Depth pass so the sand works with depth priming / SSAO.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half frag(V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
