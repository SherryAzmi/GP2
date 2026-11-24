// Natural football turf (URP, mobile friendly, world-space; no textures).
//  - Muted, natural greens with patchy variation and a few worn, yellowish areas.
//  - Mowing stripes that look lighter or darker depending on which way you look along them, like a real pitch.
//  - Fine blade detail up close that fades with distance (no shimmering), and a soft sheen at grazing angles.
//  - Receives the sun's shadows.
Shader "GP2/Grass"
{
    Properties
    {
        _Green ("Grass", Color) = (0.19, 0.33, 0.12, 1)
        _Green2 ("Grass (lush patches)", Color) = (0.15, 0.30, 0.10, 1)
        _Dry ("Worn / dry", Color) = (0.42, 0.42, 0.22, 1)
        _StripeWidth ("Mowing stripe width (m)", Float) = 5
        _StripeContrast ("Stripe contrast", Range(0, 0.4)) = 0.13
        _Wear ("Wear amount", Range(0, 1)) = 0.35
        _WearCenter ("Most worn spot (world XZ)", Vector) = (0, 11, 0, 0)
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
                half4 _Green, _Green2, _Dry;
                float _StripeWidth, _StripeContrast, _Wear;
                float4 _WearCenter;
            CBUFFER_END

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
                float3 V = normalize(GetWorldSpaceViewDir(pos));
                float near = gp2DetailFade(pos, 10.0);
                float mid = gp2DetailFade(pos, 35.0);

                // Base colour: lush and less lush patches, then wear (penalty spot, goalmouth) and dry spots.
                float patches = gp2Fbm(p * 0.18);
                half3 col = lerp(_Green.rgb, _Green2.rgb, saturate(patches * 1.4 - 0.2));
                float spots = gp2Fbm(p * 0.9 + 11.0);
                float wornArea = saturate(1.0 - distance(p, _WearCenter.xy) / 4.5) * 0.8
                               + saturate(1.0 - abs(p.y - 0.5) / 2.0) * saturate(1.0 - abs(p.x) / 5.0) * 0.6;
                float wear = saturate(wornArea * _Wear * (0.6 + spots) + smoothstep(0.72, 0.9, spots) * 0.25);
                col = lerp(col, _Dry.rgb, wear);

                // Mowing stripes (across the pitch, along X). Grass laid one way looks lighter when you look
                // with the grain and darker against it, so the stripe brightness flips with the view direction.
                float stripe = frac(p.y / (_StripeWidth * 2.0)) < 0.5 ? 1.0 : -1.0;
                float facing = clamp(-V.z * 2.5, -1.0, 1.0);
                float soft = smoothstep(0.0, 0.08, abs(frac(p.y / _StripeWidth) - 0.5) * 2.0);
                col *= 1.0 + stripe * facing * _StripeContrast * (0.4 + 0.6 * soft) + stripe * _StripeContrast * 0.35;

                // Blades: streaky fine noise and small dark gaps between blades, only up close.
                float blades = gp2Noise(p * float2(70.0, 22.0)) * 0.6 + gp2Noise(p * float2(23.0, 140.0)) * 0.4;
                col *= lerp(1.0, 0.82 + 0.36 * blades, near);
                col *= lerp(1.0, 0.92 + 0.16 * gp2Noise(p * 6.0), mid);

                // Lighting with a tilted, noisy normal (clumps of grass), shadows and ambient.
                float2 tilt = (float2(gp2Noise(p * 9.0), gp2Noise(p * 9.0 + 5.0)) - 0.5) * 0.5 * mid;
                float3 n = normalize(normalize(i.normalWS) + float3(tilt.x, 0, tilt.y));
                Light sun = GetMainLight(TransformWorldToShadowCoord(pos));
                half shadow = sun.shadowAttenuation * sun.distanceAttenuation;
                half ndl = saturate(dot(n, sun.direction) * 0.8 + 0.2);
                half3 lit = col * (SampleSH(n) * 0.85 + sun.color * ndl * shadow * 0.95);

                // Soft silvery sheen of blades at grazing angles, and a little light passing through them.
                half fres = pow(1.0 - saturate(dot(normalize(i.normalWS), V)), 4.0);
                lit += col * fres * 0.35 + col * saturate(dot(-V, sun.direction)) * 0.15 * shadow;

                lit = MixFog(lit, i.fogFactor);
                return half4(lit, 1);
            }
            ENDHLSL
        }

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
