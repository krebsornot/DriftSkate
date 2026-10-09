// Toon-Abendhimmel: Farbverlauf mit Gluehen um die tiefstehende Sonne, gezeichnete Wolken mit harter
// Licht-/Schattenkante, Sonnenscheibe mit Ring und erste Sterne im Zenit. Wird als RenderSettings.skybox benutzt.
Shader "DriftSkate/Sky"
{
    Properties
    {
        _ZenithColor ("Zenit", Color) = (0.17, 0.16, 0.42, 1)
        _MidColor ("Mitte", Color) = (0.54, 0.36, 0.66, 1)
        _HorizonColor ("Horizont", Color) = (0.91, 0.63, 0.71, 1)
        _GroundColor ("Unter dem Horizont", Color) = (0.36, 0.29, 0.47, 1)
        _GlowColor ("Gluehen um die Sonne", Color) = (1, 0.69, 0.44, 1)
        _SunDir ("Richtung zur Sonne", Vector) = (-0.9, 0.3, -0.3, 0)
        _SunColor ("Sonne", Color) = (1, 0.88, 0.62, 1)
        _SunSize ("Sonnenradius (Bogenmass)", Range(0.01, 0.2)) = 0.055
        _CloudLit ("Wolken Licht", Color) = (1, 0.8, 0.66, 1)
        _CloudShade ("Wolken Schatten", Color) = (0.5, 0.36, 0.6, 1)
        _CloudCover ("Wolkenmenge", Range(0, 1)) = 0.45
        _CloudSpeed ("Wolken-Tempo", Float) = 0.006
        _StarStrength ("Sterne", Range(0, 2)) = 0.8
        _ShootingStars ("Sternschnuppen", Range(0, 2)) = 0
        _StarHeight ("Sterne ab Hoehe", Range(0, 1)) = 0.4
        _Overcast ("Regenwolken", Range(0, 1)) = 0
        _Flash ("Blitz-Aufhellung", Range(0, 2)) = 0
        _FlashDir ("Richtung des Blitzes", Vector) = (1, 0.2, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor, _MidColor, _HorizonColor, _GroundColor, _GlowColor;
                float4 _SunDir, _SunColor;
                float _SunSize;
                float4 _CloudLit, _CloudShade;
                float _CloudCover, _CloudSpeed, _StarStrength, _ShootingStars, _StarHeight, _Overcast, _Flash;
                float4 _FlashDir;
            CBUFFER_END

            float SegmentDist(float2 p, float2 a, float2 b, out float t)
            {
                float2 ab = b - a;
                t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-6));
                return length(p - a - ab * t);
            }

            // Seltene Sternschnuppen: alle paar Sekunden wuerfelt ein Zeitfenster, ob eine faellt
            float ShootingStars(float2 suv)
            {
                float total = 0.0;
                for (int k = 0; k < 2; k++)
                {
                    float period = 6.0 + k * 3.7;
                    float tt = _Time.y / period + k * 0.37;
                    float slot = floor(tt), phase = frac(tt);
                    float r = frac(sin(slot * 91.7 + k * 13.1) * 43758.5);
                    if (r > 0.45) continue;                       // meist faellt keine
                    float life = phase * period / 0.9;            // 0.9 s Flugzeit
                    if (life > 1.0) continue;
                    float2 start = float2(frac(r * 17.3) - 0.5, frac(r * 29.1) - 0.5) * 0.9;
                    float ang = frac(r * 53.7) * 6.2832;
                    float2 dir = float2(cos(ang), sin(ang));
                    float2 head = start + dir * life * 0.35;
                    float2 tail = head - dir * 0.12;
                    float t;
                    float d = SegmentDist(suv, tail, head, t);
                    float fade = sin(life * 3.1416);
                    total += smoothstep(0.0025, 0.0, d) * t * t * fade;
                }
                return total;
            }

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

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

            float Fbm(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++)
                {
                    s += Noise(p) * a;
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }
                return s;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 sun = normalize(_SunDir.xyz);
                float h = d.y;
                float toSun = dot(d, sun);
                // Wie stark eine Richtung zur Sonne hin liegt (nur waagerecht), fuer das Gluehen am Horizont
                float azimuth = saturate(dot(normalize(d.xz + 1e-4), normalize(sun.xz + 1e-4)) * 0.5 + 0.5);

                // Verlauf Horizont -> Mitte -> Zenit, darunter dunkler
                float3 sky = lerp(_HorizonColor.rgb, _MidColor.rgb, smoothstep(0.02, 0.32, h));
                sky = lerp(sky, _ZenithColor.rgb, smoothstep(0.25, 0.85, h));
                float glow = pow(azimuth, 3.0) * (1.0 - smoothstep(0.0, 0.45, h)) + pow(saturate(toSun), 12.0) * 0.6;
                sky = lerp(sky, _GlowColor.rgb, saturate(glow));
                sky += _GlowColor.rgb * exp(-abs(h) * 45.0) * 0.25;                       // heller Streifen am Horizont
                if (h < 0.0) sky = lerp(lerp(_HorizonColor.rgb, _GlowColor.rgb, pow(azimuth, 3.0) * 0.6), _GroundColor.rgb, saturate(-h * 5.0));

                // Wolken: auf eine Kuppel projiziert, in die Laenge gezogen, harte Kanten; zur Sonne hin beleuchtet
                float cloud = 0.0;
                float3 cloudCol = 0;
                if (h > 0.0)
                {
                    float2 uv = d.xz / (h + 0.12) * float2(0.45, 1.3) + _Time.y * _CloudSpeed * (1.0 + _Overcast * 3.0) * float2(1.0, 0.25);
                    float density = Fbm(uv * 1.6);
                    float threshold = lerp(0.72, 0.4, lerp(_CloudCover, 1.15, _Overcast));
                    float mask = smoothstep(0.03, 0.14, h) * (1.0 - smoothstep(0.5, 0.85, h));
                    cloud = smoothstep(threshold, threshold + 0.012, density) * mask;
                    cloud = lerp(cloud, smoothstep(0.0, 0.08, h), _Overcast * 0.85); // bei Regen fast geschlossene Decke
                    float2 towardSun = normalize(sun.xz + 1e-4) * float2(0.45, 1.3) * 0.18;
                    float lit = smoothstep(0.0, 0.008, density - Fbm((uv + towardSun) * 1.6));
                    cloudCol = lerp(_CloudShade.rgb, _CloudLit.rgb, lit);
                    cloudCol = lerp(cloudCol, _GlowColor.rgb * 1.3, pow(saturate(toSun), 10.0) * 0.7);
                    cloudCol = lerp(cloudCol, _HorizonColor.rgb, 1.0 - smoothstep(0.03, 0.2, h)); // im Dunst verschwinden
                    float cl = dot(cloudCol, float3(0.3, 0.59, 0.11));
                    cloudCol = lerp(cloudCol, float3(0.78, 0.82, 0.95) * cl * (0.55 + 0.25 * lit), _Overcast * 0.85); // Regenwolken grau
                }

                // Sonne: Scheibe, gezeichneter Ring, weicher Schein (hell genug fuer Bloom)
                float disc = smoothstep(cos(_SunSize * 1.04), cos(_SunSize), toSun);
                float ring = smoothstep(cos(_SunSize * 2.3), cos(_SunSize * 2.2), toSun) * (1.0 - smoothstep(cos(_SunSize * 2.0), cos(_SunSize * 1.9), toSun));
                float3 col = sky + (_GlowColor.rgb * ring * 0.35 + _SunColor.rgb * pow(saturate(toSun), 300.0) * 0.8) * (1.0 - _Overcast);
                col = lerp(col, _SunColor.rgb * 2.2, disc * (1.0 - _Overcast));

                // Sterne im Zenit, leicht funkelnd
                float2 suv = d.xz / (h + 1.0) * 70.0;
                float2 cell = floor(suv);
                float r = Hash(cell);
                float2 pos = float2(Hash(cell + 3.1), Hash(cell + 7.7)) * 0.8 + 0.1;
                float star = r > 0.94 ? smoothstep(0.09, 0.0, length(frac(suv) - pos)) : 0.0;
                star *= (0.6 + 0.4 * sin(_Time.y * (1.5 + r * 3.0) + r * 60.0)) * smoothstep(_StarHeight, _StarHeight + 0.5, h) * _StarStrength;
                col += star * (1.0 - _Overcast);
                if (_ShootingStars > 0.0 && h > 0.15 && _Overcast < 0.5)
                    col += float3(1.0, 0.95, 0.85) * ShootingStars(d.xz / (h + 1.0)) * _ShootingStars * 2.0 * smoothstep(0.15, 0.35, h);

                col = lerp(col, cloudCol, cloud);
                // Bei Regen: Himmel entsaettigt und dunkler
                float lum = dot(col, float3(0.3, 0.59, 0.11));
                col = lerp(col, float3(0.8, 0.85, 1.0) * lum * 0.75, _Overcast * 0.75);
                // Blitz: Wolken und Himmel leuchten auf, am staerksten in Richtung des Einschlags
                if (_Flash > 0.0)
                {
                    float toward = pow(saturate(dot(d, normalize(_FlashDir.xyz)) * 0.5 + 0.5), 4.0);
                    col += float3(0.75, 0.8, 1.0) * _Flash * (0.25 + 0.75 * toward) * (0.5 + 0.7 * cloud);
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
