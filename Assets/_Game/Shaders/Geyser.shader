// Wassersaeule der Hydranten-Fontaene: schwankt leicht, helle Streifen rasen nach oben, Raender leuchten (Fresnel),
// oben loest sie sich auf. Mesh-UV: x = Winkel, y = 0 unten .. 1 oben.
Shader "DriftSkate/Geyser"
{
    Properties
    {
        _Color ("Farbe", Color) = (0.55, 0.9, 1.0, 0.8)
        _StreakColor ("Streifen", Color) = (1, 1, 1, 1)
        _Sway ("Schwanken (m)", Float) = 0.12
        _Brightness ("Helligkeit", Range(0, 3)) = 1.3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "GeyserUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _StreakColor;
                float _Sway, _Brightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1.0), u.x), u.y);
            }

            Varyings vert (Attributes input)
            {
                Varyings o = (Varyings)0;
                float3 p = TransformObjectToWorld(input.positionOS.xyz);
                float t = _Time.y;
                // Schwanken nimmt nach oben zu (unten sitzt die Saeule fest im Hydranten)
                float v = input.uv.y;
                p.x += sin(t * 3.1 + v * 4.0) * _Sway * v * v;
                p.z += cos(t * 2.6 + v * 3.3) * _Sway * v * v;
                o.positionWS = p;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                o.positionCS = TransformWorldToHClip(p);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float t = _Time.y;
                float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float fres = pow(1.0 - saturate(dot(n, V)), 2.0);

                // Streifen rasen nach oben (Weltkoordinate y, damit die Laenge der Saeule egal ist)
                float streak = Noise(float2(i.uv.x * 7.0, i.positionWS.y * 1.4 - t * 7.0));
                float streak2 = Noise(float2(i.uv.x * 13.0 + 3.0, i.positionWS.y * 2.6 - t * 11.0));
                float bright = step(0.62, streak) * 0.6 + step(0.7, streak2) * 0.4;

                half3 col = _Color.rgb * _Brightness;
                col = lerp(col, _StreakColor.rgb * _Brightness, bright);
                col += _StreakColor.rgb * fres * 0.6;

                // Oben loest sich die Saeule in Troepfchen auf, ganz unten ist sie satt
                float top = 1.0 - smoothstep(0.72, 1.0, i.uv.y);
                float alpha = _Color.a * (0.55 + 0.45 * top) * top;
                alpha = saturate(alpha + bright * 0.25 + fres * 0.3);

                col = MixFog(col, i.fogFactor);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
