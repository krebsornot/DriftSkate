// Toon-Wasser: Wellen im Vertex-Shader (drei Sinuswellen, gleiche Formel wie WaterSurface.HeightAt in C#), dazu die
// Kraeuselungen aus der CPU-Simulation (WaterRipples, globale Textur _DS_RippleTex). Farbe in harten Stufen (tief, flach,
// Kamm), Sonnenglitzer als gestufte Spiegelung, Schaum an Kaimauer/Pfeilern (ueber die Tiefentextur), auf Wellenkaemmen
// und wo die Simulation stark ausschlaegt. Nebelfarbe (= Horizont) spiegelt sich flach gesehen.
Shader "DriftSkate/Water"
{
    Properties
    {
        _DeepColor ("Tief", Color) = (0.08, 0.42, 0.62, 1)
        _ShallowColor ("Flach", Color) = (0.18, 0.71, 0.84, 1)
        _CrestColor ("Kamm", Color) = (0.55, 0.9, 0.96, 1)
        _FoamColor ("Schaum", Color) = (0.97, 0.96, 0.93, 1)
        _ShadowTint ("Schatten", Color) = (0.58, 0.52, 0.78, 1)
        _WaveAmp ("Wellenhoehe (Faktor)", Range(0, 2)) = 1
        _RippleOn ("Simulation mitnehmen", Range(0, 1)) = 1
        _Opacity ("Deckung", Range(0, 1)) = 0.82
        _FoamDepth ("Schaum-Tiefe am Ufer (m)", Float) = 1.4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        Cull Back

        Pass
        {
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor, _ShallowColor, _CrestColor, _FoamColor, _ShadowTint;
                float _WaveAmp, _RippleOn, _Opacity, _FoamDepth;
            CBUFFER_END

            // Global aus WaterSurface: drei Wellen (dir.x, dir.z, Amplitude, Wellenlaenge), Zeitbasis, Simulation
            float4 _DS_Wave0, _DS_Wave1, _DS_Wave2;
            float _DS_WaterTime;
            float4 _DS_RippleRect;   // Ursprung x, z, 1/Kantenlaenge, Kantenlaenge
            float _DS_RippleScale;
            float _DS_Darken, _DS_Wet;
            TEXTURE2D(_DS_RippleTex);
            SAMPLER(sampler_DS_RippleTex);

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
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

            // Eine Welle: Hoehe und Steigung. Tempo wie auf tiefem Wasser: c = sqrt(g * L / 2pi).
            void Wave(float4 w, float2 p, float t, inout float h, inout float2 slope)
            {
                float2 d = normalize(w.xy);
                float k = 6.28318 / w.w;
                float c = sqrt(9.81 * w.w / 6.28318);
                float phase = k * (dot(d, p) - c * t);
                float a = w.z * _WaveAmp;
                h += a * sin(phase);
                slope += a * k * cos(phase) * d;
            }

            float2 RippleUV(float2 p) { return (p - _DS_RippleRect.xy) * _DS_RippleRect.z; }

            float RippleMask(float2 uv)
            {
                float2 e = min(uv, 1.0 - uv);
                return smoothstep(0.0, 0.08, min(e.x, e.y)) * _RippleOn;
            }

            Varyings vert (Attributes input)
            {
                Varyings o = (Varyings)0;
                float3 p = TransformObjectToWorld(input.positionOS.xyz);
                float h = 0; float2 slope = 0;
                Wave(_DS_Wave0, p.xz, _DS_WaterTime, h, slope);
                Wave(_DS_Wave1, p.xz, _DS_WaterTime, h, slope);
                Wave(_DS_Wave2, p.xz, _DS_WaterTime, h, slope);
                float2 ruv = RippleUV(p.xz);
                float r = SAMPLE_TEXTURE2D_LOD(_DS_RippleTex, sampler_DS_RippleTex, ruv, 0).r * _DS_RippleScale * RippleMask(ruv);
                p.y += h + r;
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 wp = i.positionWS.xz;
                float t = _DS_WaterTime;
                // Wellenform pro Pixel (nicht aus den Vertices): so sieht auch die grobe ferne Platte Wellen
                float wh = 0; float2 wslope = 0;
                Wave(_DS_Wave0, wp, t, wh, wslope);
                Wave(_DS_Wave1, wp, t, wh, wslope);
                Wave(_DS_Wave2, wp, t, wh, wslope);
                // Nebel pro Pixel (die ferne Platte hat nur wenige Vertices)
                float fogFactor = ComputeFogFactor(TransformWorldToHClip(i.positionWS).z);

                // Normale: Wellen-Steigung plus Gefaelle der Simulation (zwei Nachbartexel)
                float2 ruv = RippleUV(wp);
                float rmask = RippleMask(ruv);
                float texel = _DS_RippleRect.z * 1.0;
                float rC = SAMPLE_TEXTURE2D(_DS_RippleTex, sampler_DS_RippleTex, ruv).r;
                float rX = SAMPLE_TEXTURE2D(_DS_RippleTex, sampler_DS_RippleTex, ruv + float2(texel, 0)).r;
                float rZ = SAMPLE_TEXTURE2D(_DS_RippleTex, sampler_DS_RippleTex, ruv + float2(0, texel)).r;
                float cell = _DS_RippleRect.w * texel;
                float2 rslope = float2(rX - rC, rZ - rC) / max(cell, 0.01) * _DS_RippleScale * rmask;
                float2 slope = wslope + rslope * 1.5;
                // Regen: feines Gekraeusel
                if (_DS_Wet > 0.01)
                    slope += (float2(Noise(wp * 2.2 + t * 2.1), Noise(wp * 2.3 - t * 1.9)) - 0.5) * 0.5 * _DS_Wet;
                float3 n = normalize(float3(-slope.x, 1.0, -slope.y));

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float3 L = mainLight.direction;
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));

                // Farbstufen nach Wellenform: Taeler tief, Berge flach, Kaemme hell. Die Neigung zur Sonne zaehlt relativ
                // zur ebenen Flaeche mit, damit es auch bei tief stehender Sonne Stufen gibt.
                float ndl = dot(n, L);
                float height = wh + rC * _DS_RippleScale * rmask;
                float shade = height * 2.5 + (ndl - L.y) * 5.0;
                float lightStep = smoothstep(0.0, 0.08, shade);
                float crestStep = smoothstep(0.4, 0.48, shade);
                half3 col = lerp(_DeepColor.rgb, _ShallowColor.rgb, lightStep);
                col = lerp(col, _CrestColor.rgb, crestStep);

                // Schatten (Bruecke, Pfeiler, Kai) als harte Stufe wie beim Toon-Shader
                float shadowStep = smoothstep(0.45, 0.55, mainLight.shadowAttenuation);
                col = lerp(col * _ShadowTint.rgb * (1.0 - _DS_Darken * 0.8), col * mainLight.color, shadowStep);
                col += col * SampleSH(n) * 0.1;

                // Sonnenglitzer: in der Spiegelung der Sonne einzelne Funken (Rauschen als Maske, wandert langsam),
                // dazu eine schwache Aufhellung der ganzen Glitzerstrasse. Keine geschlossene weisse Flaeche.
                float3 R = reflect(-L, n);
                float spec = pow(saturate(dot(R, V)), 90.0);
                float twinkle = Noise(wp * 1.7 + float2(t * 0.35, -t * 0.27));
                float sparkle = step(0.64, twinkle) * step(0.3, spec);
                float glint = (sparkle * 0.85 + step(0.45, spec) * 0.12) * shadowStep;
                col += mainLight.color * glint * (1.0 - _DS_Wet * 0.6);

                // Flach gesehen spiegelt sich etwas Himmel (Horizontfarbe = Nebelfarbe), das Wasser bleibt aber Wasser
                float fres = pow(1.0 - saturate(dot(n, V)), 4.0);
                half3 sky = lerp(unity_FogColor.rgb, _ShallowColor.rgb, 0.35);
                col = lerp(col, sky, fres * 0.3);

                // Schaum: am Ufer (Tiefenunterschied zur Szene), auf Kaemmen und bei starker Kraeuselung
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float rawDepth = SampleSceneDepth(suv);
                float sceneEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                float waterEye = i.screenPos.w;
                float diff = sceneEye - waterEye;
                bool hasDepth = sceneEye < 5000.0;
                float foamNoise = Noise(wp * 1.6 + float2(t * 0.6, t * 0.4)) * 0.6 + Noise(wp * 4.1 - float2(t * 0.9, 0)) * 0.4;
                // Flachwasser (Rampe, Kaimauer-Fuss) heller, Schaum nur als schmale Kante an der Wasserlinie
                float shallow = hasDepth ? (1.0 - smoothstep(0.0, _FoamDepth, diff)) : 0.0;
                col = lerp(col, _CrestColor.rgb * lerp(_ShadowTint.rgb, 1.0, shadowStep), shallow * 0.5);
                float shore = hasDepth ? (1.0 - smoothstep(0.0, 0.55, diff)) : 0.0;
                float shoreFoam = step(0.4, shore * (0.7 + 0.6 * foamNoise));
                float crestFoam = step(0.86, crestStep * foamNoise * 1.15);
                // Kraeuselungen: Schaum nur auf den Kaemmen der Ringe, wo es steil ist (Linien, keine Flaechen)
                float rippleFoam = step(0.08, rC * _DS_RippleScale * rmask) * step(0.35, length(rslope) * (0.7 + foamNoise));
                float foam = saturate(shoreFoam + crestFoam + rippleFoam);
                col = lerp(col, _FoamColor.rgb * lerp(_ShadowTint.rgb, 1.0, shadowStep), foam);

                // Deckung: am Ufer durchsichtiger (weicher Rand), sonst wie eingestellt
                float alpha = hasDepth ? lerp(0.45, _Opacity, smoothstep(0.0, 1.2, diff)) : _Opacity;
                alpha = max(alpha, foam * 0.95);

                col = MixFog(col, fogFactor);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
