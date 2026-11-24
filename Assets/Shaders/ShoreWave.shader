// Breaking shore wave for the beach game (URP, mobile friendly).
// Colour and opacity come from the mesh's vertex colours (deep teal body, light turquoise thin water);
// TEXCOORD1.x is the foam amount, turned into lacy, moving white foam by procedural noise here.
// Lighting: wrapped sun diffuse + ambient, a sun glint, and a sky-tinted reflection at grazing angles.
Shader "GP2/ShoreWave"
{
    Properties
    {
        _FoamColor ("Foam colour", Color) = (0.97, 0.99, 1, 1)
        _SkyColor ("Sky reflection", Color) = (0.62, 0.82, 0.97, 1)
        _Glint ("Sun glint", Range(0, 3)) = 1.2
        _FoamScale ("Foam pattern scale", Float) = 3.2
        _FoamSpeed ("Foam drift speed", Float) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FoamColor;
                half4 _SkyColor;
                half _Glint;
                float _FoamScale;
                float _FoamSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;     // wave mesh: (0.5, 0.5); spray particles: quad corners -> round droplets
                float2 data : TEXCOORD1;   // x = foam 0..1, y = flow speed (streaks foam along the run)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                float2 data : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                float2 uv : TEXCOORD4;
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
                o.color = v.color;
                o.data = v.data;
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x),
                            lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }

            // Cellular-looking foam: two octaves, folded so bubbles form rings with gaps (lace).
            float foamPattern(float2 p, float t)
            {
                float n = noise(p + float2(0, t)) * 0.6 + noise(p * 2.3 - float2(t * 0.7, t * 0.4)) * 0.4;
                return 1.0 - abs(n * 2.0 - 1.0);
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light sun = GetMainLight();

                // Water body
                half wrap = saturate(dot(n, sun.direction) * 0.5 + 0.5);
                half3 ambient = SampleSH(n);
                half3 water = i.color.rgb * (ambient * 0.75 + sun.color * wrap * 0.55);
                half fresnel = pow(1.0 - saturate(dot(n, viewDir)), 4.0);
                water = lerp(water, _SkyColor.rgb, fresnel * 0.45);
                half spec = pow(saturate(dot(n, normalize(sun.direction + viewDir))), 96.0) * _Glint;
                water += sun.color * spec;

                // Foam: denser where data.x is high; streaks stretch along the run direction (z) when flowing.
                float foam = saturate(i.data.x);
                float t = _Time.y * _FoamSpeed;
                // Height folded into the second axis, so steep faces (the crest) get foam too instead of streaks.
                float2 uv = float2(i.positionWS.x, i.positionWS.z + i.positionWS.y * 1.6) * _FoamScale
                          * float2(1.0, 1.0 / (1.0 + i.data.y * 0.6));
                float pattern = foamPattern(uv, t * 3.0);
                float mask = saturate((pattern - (1.0 - foam)) * 4.0 + foam * foam * 0.6);
                // Foam scatters light in every direction, so it stays bright white even on faces turned from the sun.
                half3 foamLit = _FoamColor.rgb * (0.82 + 0.22 * wrap) * lerp(half3(1, 1, 1), saturate(sun.color), 0.25);

                half3 col = lerp(water, foamLit, mask);
                half alpha = saturate(max(i.color.a, mask * 0.97 * step(0.001, i.color.a + foam)));
                alpha *= saturate((0.5 - length(i.uv - 0.5)) * 6.0); // round droplets; 1 on the wave mesh
                col = MixFog(col, i.fogFactor);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
