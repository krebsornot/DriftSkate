// Jet-Set-Radio-Look: harte Licht-/Schattenstufe, Rim-Licht und Inverted-Hull-Outline.
Shader "DriftSkate/Toon"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _BaseMap ("Base Map", 2D) = "white" {}
        _ShadowTint ("Shadow Tint", Color) = (0.58, 0.52, 0.78, 1)
        _Threshold ("Light Threshold", Range(-1, 1)) = 0.05
        _Softness ("Edge Softness", Range(0.001, 0.3)) = 0.015
        _RimColor ("Rim Color", Color) = (1,1,1,1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.2
        _OutlineColor ("Outline Color", Color) = (0.06, 0.05, 0.1, 1)
        _OutlineWidth ("Outline Width", Range(0, 3)) = 0.35
        _OutlineMode ("Outline Mode (0 Normals, 1 Box)", Range(0, 1)) = 1
        _Emission ("Emission", Range(0, 4)) = 0
        _Cutoff ("Alpha Cutoff (0 = aus)", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (0 = doppelseitig)", Float) = 2
        _DetailMap ("Oberflaeche (Welt-UV, grau = neutral)", 2D) = "gray" {}
        _DetailScale ("Oberflaeche: Kacheln pro Meter", Float) = 0.25
        _DetailStrength ("Oberflaeche: Staerke", Range(0, 1)) = 0
        _DetailScroll ("Oberflaeche: Verschiebung pro Sekunde (UV)", Vector) = (0, 0, 0, 0)
        _GlowMask ("Leuchten, wo die Textur-Alpha < 1 ist (erleuchtete Fenster)", Range(0, 4)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseMap_ST;
            float4 _ShadowTint;
            float _Threshold;
            float _Softness;
            float4 _RimColor;
            float _RimStrength;
            float4 _OutlineColor;
            float _OutlineWidth;
            float _OutlineMode;
            float _Emission;
            float _Cutoff;
            float _DetailScale;
            float _DetailStrength;
            float4 _DetailScroll;
            float _GlowMask;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_DetailMap);
        SAMPLER(sampler_DetailMap);

        // Global (Himmel-Variante): 0 = Tag/Abend, 1 = Schattenseite ganz dunkel (Nacht). Leuchten bleibt unberuehrt.
        float _DS_Darken;
        // Global (Wetter): Naesse 0..1 und das Bild der Spiegel-Kamera (PlanarReflection)
        float _DS_Wet;
        float _DS_ReflectionOn;
        TEXTURE2D(_DS_ReflectionTex);
        SAMPLER(sampler_DS_ReflectionTex);

        float WetHash(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float WetNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            float2 u = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(WetHash(i), WetHash(i + float2(1, 0)), u.x), lerp(WetHash(i + float2(0, 1)), WetHash(i + 1.0), u.x), u.y);
        }
        ENDHLSL

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float ndl = dot(n, mainLight.direction);
                float lightStep = smoothstep(_Threshold - _Softness, _Threshold + _Softness, ndl);
                float shadowStep = smoothstep(0.5 - _Softness, 0.5 + _Softness, mainLight.shadowAttenuation);
                float lit = lightStep * shadowStep;

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                if (_Cutoff > 0) clip(albedo.a - _Cutoff);
                half3 baseCol = albedo.rgb;

                // Oberflaechen-Struktur (Asphalt, Gehwegplatten, Beton ...) in Weltkoordinaten: auf die Ebene
                // der staerksten Normalen-Achse projiziert, damit sie auf beliebig skalierten Quadern gleich gross
                // bleibt und ueber benachbarte Objekte nahtlos weiterlaeuft. Grau (0.5) laesst die Farbe unveraendert.
                if (_DetailStrength > 0)
                {
                    float3 an = abs(n);
                    float2 duv = an.y >= max(an.x, an.z) ? i.positionWS.xz : (an.x >= an.z ? i.positionWS.zy : i.positionWS.xy);
                    duv = duv * _DetailScale + _DetailScroll.xy * _Time.y;
                    half3 detail = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, duv).rgb;
                    baseCol *= lerp(half3(1, 1, 1), detail * 2.0h, (half)_DetailStrength);
                }
                half3 col = lerp(baseCol * _ShadowTint.rgb * (1.0 - _DS_Darken), baseCol * mainLight.color, lit);
                col += baseCol * SampleSH(n) * 0.12;

                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float rim = smoothstep(0.62, 0.68, 1.0 - saturate(dot(n, viewDir))) * lit;
                col += _RimColor.rgb * mainLight.color * rim * _RimStrength;
                col += baseCol * _Emission;
                col += baseCol * _GlowMask * (1.0 - albedo.a);

                // Nasser Boden: dunkler, mit Spiegelbild der Stadt (Pfuetzen staerker) und Regentropfen-Kraeuseln.
                // Nur fast waagerechte Flaechen nahe dem Boden (keine Autodaecher, Ledges oder Dachflaechen).
                float wet = _DS_Wet * smoothstep(0.6, 0.95, n.y) * (1.0 - smoothstep(0.25, 0.45, i.positionWS.y));
                if (wet > 0.001)
                {
                    float2 wp = i.positionWS.xz;
                    float puddle = smoothstep(0.5, 0.62, WetNoise(wp * 0.09) * 0.7 + WetNoise(wp * 0.31) * 0.3);
                    col *= lerp(1.0, 0.7, wet);
                    float2 ripple = (float2(WetNoise(wp * 3.0 + _Time.y * 2.7), WetNoise(wp * 3.1 - _Time.y * 2.4)) - 0.5) * 0.014;
                    float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                    float2 ruv = float2(1.0 - suv.x, suv.y) + ripple * (0.5 + puddle);
                    half3 refl = _DS_ReflectionOn > 0.5 ? SAMPLE_TEXTURE2D(_DS_ReflectionTex, sampler_DS_ReflectionTex, ruv).rgb : unity_FogColor.rgb * 0.7;
                    float fres = 0.35 + 0.65 * pow(1.0 - saturate(dot(n, viewDir)), 3.0);
                    col = lerp(col, refl, wet * lerp(0.3, 0.85, puddle) * fres);
                }

                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertOutline
            #pragma fragment fragOutline
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct AttributesO
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsO
            {
                float4 positionCS : SV_POSITION;
                float fogFactor : TEXCOORD0;
            };

            VaryingsO vertOutline (AttributesO input)
            {
                VaryingsO o = (VaryingsO)0;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                // Box-Modus: jede Seite eines (skalierten) Quaders gleich breit nach aussen schieben.
                float3 s = sign(input.positionOS.xyz);
                float3 boxDir = s.x * TransformObjectToWorldDir(float3(1, 0, 0))
                              + s.y * TransformObjectToWorldDir(float3(0, 1, 0))
                              + s.z * TransformObjectToWorldDir(float3(0, 0, 1));
                float3 dir = lerp(normalWS, boxDir, _OutlineMode);

                float dist = distance(GetCameraPositionWS(), positionWS);
                positionWS += dir * (_OutlineWidth * 0.01 * clamp(dist, 2.0, 60.0));

                o.positionCS = TransformWorldToHClip(positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 fragOutline (VaryingsO i) : SV_Target
            {
                return half4(MixFog(_OutlineColor.rgb, i.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct AttributesS
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsS
            {
                float4 positionCS : SV_POSITION;
            };

            VaryingsS vertShadow (AttributesS input)
            {
                VaryingsS o = (VaryingsS)0;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 fragShadow (VaryingsS i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing

            struct AttributesD
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 vertDepth (AttributesD input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half fragDepth () : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma multi_compile_instancing

            struct AttributesDN
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsDN
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            VaryingsDN vertDN (AttributesDN input)
            {
                VaryingsDN o = (VaryingsDN)0;
                UNITY_SETUP_INSTANCE_ID(input);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 fragDN (VaryingsDN i) : SV_Target
            {
                return half4(normalize(i.normalWS), 0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
