// Open sea (URP, transparent, mobile friendly) for a flat sea plane.
//  - Moving swell and wind ripples, computed per pixel as normals (the plane itself stays flat).
//  - Colour by distance from the shore: clear turquoise over the sand, deep blue further out.
//  - Sky reflection at grazing angles (Fresnel), a sun glitter path, and lines of foam near the shore.
Shader "GP2/Sea"
{
    Properties
    {
        _Shallow ("Shallow (over sand)", Color) = (0.16, 0.76, 0.82, 0.5)
        _Mid ("Mid", Color) = (0.04, 0.56, 0.68, 0.85)
        _Deep ("Deep", Color) = (0.02, 0.24, 0.42, 0.97)
        _SkyColor ("Sky reflection", Color) = (0.66, 0.84, 0.98, 1)
        _ShoreZ ("Shoreline (world Z)", Float) = -1.75
        _DeepDistance ("Distance to deep water (m)", Float) = 40
        _WaveStrength ("Swell strength", Range(0, 2)) = 1
        _Glitter ("Sun glitter", Range(0, 5)) = 2.5
        _FoamAmount ("Shore foam", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "GP2Noise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Shallow, _Mid, _Deep, _SkyColor;
                float _ShoreZ, _DeepDistance, _WaveStrength, _Glitter, _FoamAmount;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
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
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // Slope (dh/dx, dh/dz) of one travelling sine wave.
            float2 wave(float2 p, float2 dir, float lambda, float amp, float speed, float t)
            {
                float k = 6.2832 / lambda;
                float ph = dot(p, dir) * k - t * speed * k;
                return dir * (amp * k * cos(ph));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 pos = i.positionWS;
                float2 p = pos.xz;
                float t = _Time.y;
                float dist = distance(pos, _WorldSpaceCameraPos);
                float fine = saturate(1.0 - dist / 60.0);      // ripples fade out with distance (no shimmer)
                float far = saturate(1.0 - dist / 400.0);

                // Swell rolling toward the shore (+Z), plus cross-chop and noise ripples.
                float2 s = 0;
                s += wave(p, normalize(float2(0.1, 1.0)), 9.0, 0.10, 1.6, t);
                s += wave(p, normalize(float2(-0.35, 1.0)), 5.3, 0.05, 1.3, t);
                s += wave(p, normalize(float2(0.6, 0.8)), 3.1, 0.025, 1.0, t);
                s *= _WaveStrength * far;
                float2 rp = p * 1.7 + float2(0.0, t * 0.35);
                float e = 0.15;
                float r0 = gp2Noise(rp), rx = gp2Noise(rp + float2(e, 0)), rz = gp2Noise(rp + float2(0, e));
                s += float2(rx - r0, rz - r0) / e * 0.06 * fine;
                float2 rp2 = p * 4.3 - float2(t * 0.25, t * 0.5);
                float q0 = gp2Noise(rp2), qx = gp2Noise(rp2 + float2(e, 0)), qz = gp2Noise(rp2 + float2(0, e));
                s += float2(qx - q0, qz - q0) / e * 0.025 * fine;
                float3 n = normalize(float3(-s.x, 1.0, -s.y));

                // Colour and opacity by how far out the water is.
                float offshore = max(0.0, _ShoreZ - pos.z);
                float k1 = saturate(offshore / 6.0), k2 = saturate((offshore - 6.0) / _DeepDistance);
                half4 body = lerp(_Shallow, _Mid, k1);
                body = lerp(body, _Deep, k2);

                Light sun = GetMainLight();
                float3 V = normalize(GetWorldSpaceViewDir(pos));
                half ndl = saturate(dot(n, sun.direction) * 0.5 + 0.5);
                half3 col = body.rgb * (SampleSH(n) * 0.7 + sun.color * ndl * 0.5);

                // Sky reflection, stronger at grazing angles (most of what you see toward the horizon).
                half fres = 0.02 + 0.98 * pow(1.0 - saturate(dot(n, V)), 5.0);
                col = lerp(col, _SkyColor.rgb, fres * 0.85);

                // Sun: a tight glitter path plus a broad sheen.
                float3 H = normalize(sun.direction + V);
                half nh = saturate(dot(n, H));
                col += sun.color * (pow(nh, 600.0) * _Glitter * 4.0 + pow(nh, 60.0) * 0.12);

                // Foam lines near the shore, drifting in, broken up by noise.
                float band = sin((pos.z - t * 0.8) * 1.9 + gp2Noise(p * 0.6) * 4.0) * 0.5 + 0.5;
                float lace = gp2Noise(p * float2(3.0, 5.0) + t * 0.3);
                float foam = smoothstep(0.82, 0.97, band) * smoothstep(0.35, 0.75, lace) * saturate(1.0 - offshore / 9.0) * _FoamAmount;
                foam += smoothstep(1.2, 0.0, offshore) * smoothstep(0.4, 0.8, lace) * _FoamAmount * 0.7;
                col = lerp(col, half3(0.95, 0.98, 1.0) * (0.8 + 0.2 * ndl), saturate(foam) * fine);

                half alpha = saturate(max(body.a, fres * 0.9) + foam * 0.6);
                col = MixFog(col, i.fogFactor);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
