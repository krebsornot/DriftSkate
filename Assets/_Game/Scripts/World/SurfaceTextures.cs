using System;
using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>Oberflaechen-Arten der Stadt. Jede bekommt im Toon-Shader eine Struktur in Weltkoordinaten.</summary>
    public enum Surface { None, Asphalt, DriftAsphalt, Paving, PlazaTiles, Concrete, Stucco, Brick, Roof, Container, Water }

    /// <summary>
    /// Prozedurale, kachelbare Texturen: Graustufen-Strukturen (0.5 = neutral, siehe _DetailMap im Toon-Shader) fuer
    /// Asphalt, Gehwegplatten, Beton, Putz, Ziegel, Wellblech und Wasser sowie Farb-Texturen fuer Fenster, Schaufenster
    /// und Tueren. Alles im Code erzeugt (wie SprayArt); der Editor-Builder speichert sie als PNG unter Generated/Textures.
    /// </summary>
    public static class SurfaceTextures
    {
        public struct Look
        {
            public string texture;
            public float scale;     // Kacheln pro Meter
            public float strength;  // 0 = aus, 1 = volle Struktur
            public Vector2 scroll;  // UV pro Sekunde (Wasser)
        }

        public static Look For(Surface s)
        {
            switch (s)
            {
                case Surface.Asphalt:      return new Look { texture = "surf_asphalt", scale = 1f / 8f, strength = 0.55f };
                case Surface.DriftAsphalt: return new Look { texture = "surf_drift", scale = 1f / 32f, strength = 0.65f };
                case Surface.Paving:       return new Look { texture = "surf_paving", scale = 1f / 4f, strength = 0.6f };
                case Surface.PlazaTiles:   return new Look { texture = "surf_tiles", scale = 1f / 8f, strength = 0.55f };
                case Surface.Concrete:     return new Look { texture = "surf_concrete", scale = 1f / 8f, strength = 0.5f };
                case Surface.Stucco:       return new Look { texture = "surf_stucco", scale = 1f / 4f, strength = 0.45f };
                case Surface.Brick:        return new Look { texture = "surf_brick", scale = 1f / 4f, strength = 0.4f };
                case Surface.Roof:         return new Look { texture = "surf_roof", scale = 1f / 4f, strength = 0.5f };
                case Surface.Container:    return new Look { texture = "surf_container", scale = 1f / 2f, strength = 0.7f };
                case Surface.Water:        return new Look { texture = "surf_water", scale = 1f / 24f, strength = 0.75f, scroll = new Vector2(0.006f, 0.0025f) };
                default:                   return new Look { texture = null, scale = 1f, strength = 0f };
            }
        }

        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        /// <summary>Farb-Texturen (sRGB): Fenster ("win_*") und Schilder ("sign_*"); alle anderen sind lineare Strukturen.</summary>
        public static bool IsColor(string key) => key.StartsWith("win_") || key.StartsWith("sign_");

        /// <summary>Textur zum Schluessel (gecacht).</summary>
        public static Texture2D Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var tex = Make(key);
            if (tex == null) return null;
            tex = TextureStore.Persist(tex, key, !IsColor(key));
            Cache[key] = tex;
            return tex;
        }

        public static void ClearCache() => Cache.Clear();

        static Texture2D Make(string key)
        {
            switch (key)
            {
                case "surf_asphalt": return Asphalt(key, 512, 11, null, 0f);
                case "surf_drift": return DriftAsphalt(key, 23);
                case "surf_paving": return Paving(key, 31);
                case "surf_tiles": return Tiles(key, 41);
                case "surf_concrete": return Concrete(key, 53);
                case "surf_stucco": return Stucco(key, 61);
                case "surf_brick": return Brick(key, 71);
                case "surf_roof": return Roof(key, 83);
                case "surf_container": return Container(key, 97);
                case "surf_water": return Water(key, 101);
                case "win_facade": return Windows(key);
                case "win_shop": return Shop(key);
                case "win_door": return Door(key);
                case "vend_front": return VendFront(key);
                case "sign_driftzone": return SignLettering(key, "DRIFT ZONE");
                default:
                    Debug.LogWarning("[DriftSkate] Unbekannte Textur: " + key);
                    return null;
            }
        }

        // ------------------------------------------------------------------ Rauschen (kachelbar)

        static int Wrap(int v, int n) => ((v % n) + n) % n;

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777215f;
            }
        }

        static float ValueNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int x1 = Wrap(x0 + 1, period), y1 = Wrap(y0 + 1, period);
            x0 = Wrap(x0, period);
            y0 = Wrap(y0, period);
            float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Fraktales Rauschen ueber eine Kachel (u, v in 0..1); 'cells' = Zellen der groebsten Oktave.</summary>
        static float Fbm(float u, float v, int cells, int seed, int octaves = 4)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += ValueNoise(u * cells, v * cells, cells, seed + o * 101) * amp;
                norm += amp;
                amp *= 0.5f;
                cells *= 2;
            }
            return sum / norm;
        }

        /// <summary>Weicher Punkt in einen Puffer, mit Umbruch an den Raendern (die Textur bleibt kachelbar).</summary>
        static void Stamp(float[] buf, int w, int h, float cx, float cy, float radius, float value)
        {
            int r = Mathf.CeilToInt(radius + 1f);
            int ix = Mathf.FloorToInt(cx), iy = Mathf.FloorToInt(cy);
            for (int y = iy - r; y <= iy + r; y++)
            {
                for (int x = ix - r; x <= ix + r; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float a = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy)) * value;
                    if (a <= 0f) continue;
                    int i = Wrap(y, h) * w + Wrap(x, w);
                    if (a > buf[i]) buf[i] = a;
                }
            }
        }

        // ------------------------------------------------------------------ Texturen anlegen

        static Texture2D NewTexture(string key, int w, int h, bool linear)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, true, linear)
            {
                name = key, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8
            };
        }

        static Texture2D Gray(string key, int w, int h, Func<int, int, float> f)
        {
            var tex = NewTexture(key, w, h, true);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(f(x, y)) * 255f);
                    px[y * w + x] = new Color32(b, b, b, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        static Texture2D Colored(string key, int w, int h, Func<int, int, Color> f)
        {
            var tex = NewTexture(key, w, h, false);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = f(x, y);
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // ------------------------------------------------------------------ Boeden

        /// <summary>Asphalt: weiche Flecken, feine Koernung, helle Steinchen. Optional dunkle Spuren (Reifen).</summary>
        static Texture2D Asphalt(string key, int size, int seed, float[] marks, float markDark)
        {
            return Gray(key, size, size, (x, y) =>
            {
                float g = 0.5f + (Fbm(x / (float)size, y / (float)size, 4, seed) - 0.5f) * 0.14f;
                g += (Hash(x, y, seed + 7) - 0.5f) * 0.09f;
                float s = Hash(x, y, seed + 13);
                if (s > 0.976f) g += 0.09f;
                else if (s < 0.014f) g -= 0.07f;
                if (marks != null) g -= marks[y * size + x] * markDark;
                return g;
            });
        }

        /// <summary>Drift-Platz: Asphalt mit alten Reifenspuren (Kreise und Boegen, immer zwei Reifen nebeneinander).</summary>
        static Texture2D DriftAsphalt(string key, int seed)
        {
            const int size = 1024;   // 32 m pro Kachel, ca. 3 cm pro Pixel
            var marks = new float[size * size];
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            for (int k = 0; k < 16; k++)
            {
                float cx = R(0, size), cy = R(0, size), radius = R(80f, 300f), a0 = R(0f, Mathf.PI * 2f);
                float sweep = R(1.6f, 4.8f) * (rng.Next(2) == 0 ? 1f : -1f), strength = R(0.45f, 1f);
                for (int track = 0; track < 2; track++)
                {
                    float rr = radius + track * 46f; // Spurweite ~1,45 m
                    int steps = Mathf.CeilToInt(Mathf.Abs(sweep) * rr / 0.7f);
                    for (int i = 0; i <= steps; i++)
                    {
                        float t = i / (float)steps, a = a0 + sweep * t;
                        float fade = Mathf.Sqrt(Mathf.Sin(t * Mathf.PI)) * strength;
                        Stamp(marks, size, size, cx + Mathf.Cos(a) * rr, cy + Mathf.Sin(a) * rr, 3.2f, fade);
                    }
                }
            }
            return Asphalt(key, size, seed, marks, 0.2f);
        }

        /// <summary>Gehweg: 1 m grosse Platten (4 x 4 pro Kachel) mit dunklen Fugen und leicht verschiedenen Toenen.</summary>
        static Texture2D Paving(string key, int seed)
        {
            const int size = 512, slab = 128;
            return Gray(key, size, size, (x, y) =>
            {
                int lx = x % slab, ly = y % slab;
                float g = 0.5f + (Hash(x / slab, y / slab, seed) - 0.5f) * 0.08f;
                g += (Fbm(x / (float)size, y / (float)size, 8, seed + 3, 3) - 0.5f) * 0.06f;
                g += (Hash(x, y, seed + 5) - 0.5f) * 0.05f;
                int edge = Mathf.Min(Mathf.Min(lx, slab - 1 - lx), Mathf.Min(ly, slab - 1 - ly));
                if (edge < 2) g = 0.33f;
                else if (edge < 3) g -= 0.04f;
                return g;
            });
        }

        /// <summary>Platz: grosse Platten 2 x 1 m im Verband (jede zweite Reihe um eine halbe Platte versetzt).</summary>
        static Texture2D Tiles(string key, int seed)
        {
            const int size = 512, tw = 128, th = 64;   // 8 m pro Kachel
            return Gray(key, size, size, (x, y) =>
            {
                int row = y / th;
                int xs = Wrap(x + (row % 2) * (tw / 2), size);
                int lx = xs % tw, ly = y % th;
                float g = 0.5f + (Hash(xs / tw, row, seed) - 0.5f) * 0.1f;
                g += (Fbm(x / (float)size, y / (float)size, 8, seed + 3, 3) - 0.5f) * 0.05f;
                g += (Hash(x, y, seed + 5) - 0.5f) * 0.04f;
                int edge = Mathf.Min(Mathf.Min(lx, tw - 1 - lx), Mathf.Min(ly, th - 1 - ly));
                if (edge < 2) g = 0.35f;
                return g;
            });
        }

        /// <summary>Beton: wolkig, fein gekoernt, Dehnungsfugen alle 4 m.</summary>
        static Texture2D Concrete(string key, int seed)
        {
            const int size = 512, joint = 256;   // 8 m pro Kachel
            return Gray(key, size, size, (x, y) =>
            {
                float g = 0.5f + (Fbm(x / (float)size, y / (float)size, 4, seed, 5) - 0.5f) * 0.16f;
                g += (Hash(x, y, seed + 9) - 0.5f) * 0.04f;
                if (x % joint < 2 || y % joint < 2) g = Mathf.Min(g, 0.36f);
                return g;
            });
        }

        // ------------------------------------------------------------------ Gebaeude

        static Texture2D Stucco(string key, int seed)
        {
            const int size = 512;   // 4 m
            return Gray(key, size, size, (x, y) =>
                0.5f + (Fbm(x / (float)size, y / (float)size, 8, seed, 4) - 0.5f) * 0.12f + (Hash(x, y, seed + 3) - 0.5f) * 0.07f);
        }

        /// <summary>Ziegel 50 x 25 cm (stilisiert), helle Fugen, jede zweite Reihe versetzt.</summary>
        static Texture2D Brick(string key, int seed)
        {
            const int size = 512, bw = 64, bh = 32;   // 4 m pro Kachel
            return Gray(key, size, size, (x, y) =>
            {
                int row = y / bh;
                int xs = Wrap(x + (row % 2) * (bw / 2), size);
                int lx = xs % bw, ly = y % bh;
                if (lx < 3 || ly < 3) return 0.64f;
                float g = 0.5f + (Hash(xs / bw, row, seed) - 0.5f) * 0.14f;
                return g + (Hash(x, y, seed + 5) - 0.5f) * 0.05f;
            });
        }

        /// <summary>Flachdach: Kies.</summary>
        static Texture2D Roof(string key, int seed)
        {
            const int size = 256;   // 4 m
            return Gray(key, size, size, (x, y) =>
            {
                float g = 0.5f + (Fbm(x / (float)size, y / (float)size, 4, seed, 3) - 0.5f) * 0.1f;
                g += (Hash(x, y, seed + 1) - 0.5f) * 0.22f;
                return g;
            });
        }

        /// <summary>Wellblech (Container): senkrechte Sicken alle 25 cm, Licht- und Schattenkante wie im Toon-Look.</summary>
        static Texture2D Container(string key, int seed)
        {
            const int w = 256, h = 64, rib = 32;   // 2 m pro Kachel
            return Gray(key, w, h, (x, y) =>
            {
                int lx = x % rib;
                float g = lx < 12 ? 0.58f : lx < 15 ? 0.42f : lx < 28 ? 0.5f : 0.64f;
                g += (Fbm(x / (float)w, y / (float)h, 4, seed, 3) - 0.5f) * 0.06f;
                return g + (Hash(x, y, seed + 1) - 0.5f) * 0.03f;
            });
        }

        /// <summary>Wasser: helle, kurze Wellenlinien (wie gezeichnet) auf leicht wolkigem Grund.</summary>
        static Texture2D Water(string key, int seed)
        {
            const int size = 512;   // 24 m
            var lines = new float[size * size];
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            for (int k = 0; k < 70; k++)
            {
                float x0 = R(0, size), y0 = R(0, size), len = R(30f, 110f), amp = R(1.5f, 3.5f), period = R(28f, 50f);
                int steps = Mathf.CeilToInt(len / 0.6f);
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps, x = x0 + len * t;
                    float fade = Mathf.Clamp01(Mathf.Sin(t * Mathf.PI) * 3f);
                    Stamp(lines, size, size, x, y0 + Mathf.Sin(x / period * Mathf.PI * 2f) * amp, 1.6f, fade);
                }
            }
            return Gray(key, size, size, (x, y) =>
            {
                float g = 0.5f + (Fbm(x / (float)size, y / (float)size, 4, seed + 1, 3) - 0.5f) * 0.12f;
                return Mathf.Lerp(g, 0.95f, lines[y * size + x]);
            });
        }

        // ------------------------------------------------------------------ Fenster, Laeden, Tueren (Farbe)

        /// <summary>Reihenfolge der Fenster-Varianten im Atlas: 0/1 Glas mit Reflex, 2 Jalousie, 3 Vorhaenge, 4 erleuchtet.</summary>
        static readonly int[] WindowVariants = { 0, 4, 2, 0, 3, 1, 4, 2 };

        /// <summary>Alpha unter 1 = leuchtet am Abend (siehe _GlowMask im Toon-Shader).</summary>
        static Color Lit(Color c, float glow) => new Color(c.r, c.g, c.b, 1f - glow);

        /// <summary>
        /// Fenster-Atlas: 8 Fenster nebeneinander (je 128 x 64 Pixel = ein Fenster von ca. 2,6 x 1,3 m) mit hellem Rahmen,
        /// Mittelsprosse, zweifarbigem Glas und Lichtreflex. Die Fensterbaender verschieben ihn zufaellig.
        /// </summary>
        static Texture2D Windows(string key)
        {
            Color frame = Palette.Hex("E9E4F2"), sill = Palette.Hex("BDB5D1");
            Color glassHi = Palette.Hex("434C80"), glassLo = Palette.Hex("262B4A"), streak = Palette.Hex("5F6DA8");
            Color blind = Palette.Hex("F1E6C8"), slat = Palette.Hex("CDBF9C");
            Color[] curtains = { Palette.Hex("FF8FAB"), Palette.Hex("7FD8BE"), Palette.Hex("FFE066") };
            Color roomLight = Palette.Hex("FFDD9A"), roomLightLow = Palette.Hex("F2B463");
            return Colored(key, 1024, 64, (x, y) =>
            {
                int m = x / 128, variant = WindowVariants[m];
                float u = (x % 128 + 0.5f) / 128f, v = (y + 0.5f) / 64f;
                if (v < 0.11f) return sill;
                if (u < 0.05f || u > 0.95f || v > 0.89f || Mathf.Abs(u - 0.5f) < 0.022f) return frame;
                bool right = u > 0.5f;
                float pu = right ? (u - 0.522f) / 0.428f : (u - 0.05f) / 0.428f;   // 0..1 innerhalb der Scheibe
                float pv = (v - 0.11f) / 0.78f;
                if (variant == 2 && pv > 0.42f) return y % 5 == 0 ? slat : blind;
                if (variant == 3 && right && (pu < 0.22f || pu > 0.78f)) return Lit(curtains[m % curtains.Length], 0.35f);
                if (variant == 3 && right) return Lit(roomLight, 0.8f);
                if (variant == 4) return Lit(pv > 0.62f ? roomLight : roomLightLow, 1f);
                float d = pu * 0.9f + pv * 0.55f + (variant == 1 ? 0.35f : 0f) + (right ? 0.15f : 0f);
                d -= Mathf.Floor(d);
                if ((d > 0.18f && d < 0.3f) || (d > 0.34f && d < 0.38f)) return streak;
                return pv > 0.62f ? glassHi : glassLo;
            });
        }

        /// <summary>Schaufenster (ein Feld = 2,4 m breit): dunkler Rahmen, Sockel, Oberlicht, Auslage mit bunten Waren.</summary>
        /// <summary>Front eines Getraenkeautomaten: drei Reihen bunter Dosen hinter leuchtendem Glas.</summary>
        static Texture2D VendFront(string key)
        {
            Color bg = Palette.Hex("FFF1D0"), shelf = Palette.Hex("B9A8D6");
            Color[] cans = { Palette.Pink, Palette.Cyan, Palette.Lime, Palette.Orange, Palette.Purple, Palette.Yellow, Palette.Red };
            return Colored(key, 128, 192, (x, y) =>
            {
                float u = (x + 0.5f) / 128f, v = (y + 0.5f) / 192f;
                for (int row = 0; row < 3; row++)
                {
                    float b = 0.1f + row * 0.3f;
                    if (v >= b - 0.035f && v < b) return Lit(shelf, 0.3f);
                    if (v >= b && v < b + 0.19f)
                    {
                        float t = (u - 0.06f) / 0.22f;
                        int slot = Mathf.FloorToInt(t);
                        float su = t - slot;
                        if (slot >= 0 && slot < 4 && su > 0.18f && su < 0.82f)
                        {
                            Color c = cans[Mathf.FloorToInt(Hash(slot, row, 17) * cans.Length) % cans.Length];
                            if (su < 0.32f) c = Color.Lerp(c, Color.white, 0.45f); // Glanzstreifen
                            if (v > b + 0.16f) c = Palette.Hex("D8D8E0");           // Deckel
                            return Lit(c, 0.5f);
                        }
                    }
                }
                return Lit(bg, 0.65f);
            });
        }

        static Texture2D Shop(string key)
        {
            // Abends innen warm beleuchtet (Alpha < 1 leuchtet, siehe Lit)
            Color frame = Palette.Hex("3A3D50"), kick = Palette.Hex("4A4E63"), shelf = Palette.Hex("2C3150");
            Color glassHi = Palette.Hex("FFE3A8"), glassLo = Palette.Hex("F0BE78"), streak = Palette.Hex("FFF4DC");
            Color[] goods = { Palette.Pink, Palette.Yellow, Palette.Lime, Palette.Orange, Palette.Purple, Palette.Cyan, Palette.White };
            return Colored(key, 256, 256, (x, y) =>
            {
                float u = (x + 0.5f) / 256f, v = (y + 0.5f) / 256f;
                if (v < 0.13f) return kick;
                if (u < 0.035f || u > 0.965f || v > 0.95f || Mathf.Abs(v - 0.78f) < 0.018f) return frame;
                if (v > 0.78f) return Lit(glassHi, 0.9f);
                float d = u * 0.8f + v * 0.5f;
                d -= Mathf.Floor(d);
                if (d > 0.2f && d < 0.3f) return Lit(streak, 0.6f);
                // Regal mit Waren hinter der Scheibe
                if (v > 0.3f && v < 0.325f && u > 0.08f && u < 0.92f) return shelf;
                if (v >= 0.325f && v < 0.47f)
                {
                    int slot = Mathf.FloorToInt((u - 0.08f) / 0.105f);
                    float su = (u - 0.08f) / 0.105f - slot;
                    if (slot >= 0 && slot < 8 && su > 0.15f && su < 0.85f && v < 0.36f + 0.1f * Hash(slot, 3, 5))
                        return Lit(goods[Mathf.FloorToInt(Hash(slot, 7, 11) * goods.Length) % goods.Length], 0.5f);
                }
                return Lit(glassLo, 0.7f);
            });
        }

        // ------------------------------------------------------------------ Schild-Schrift (Graffiti-Blockbuchstaben)

        /// <summary>Buchstaben als Linienzuege in einer Einheitsbox (Breite ~0.9, Hoehe 1, Ursprung unten links).</summary>
        static readonly Dictionary<char, Vector2[][]> Glyphs = new Dictionary<char, Vector2[][]>
        {
            ['D'] = new[] { P(0, 0, 0, 1, 0.45f, 1, 0.75f, 0.88f, 0.88f, 0.62f, 0.88f, 0.38f, 0.75f, 0.12f, 0.45f, 0, 0, 0) },
            ['R'] = new[] { P(0, 0, 0, 1, 0.55f, 1, 0.8f, 0.9f, 0.86f, 0.75f, 0.8f, 0.6f, 0.55f, 0.5f, 0, 0.5f), P(0.5f, 0.5f, 0.88f, 0) },
            ['I'] = new[] { P(0.45f, 0, 0.45f, 1), P(0.15f, 1, 0.75f, 1), P(0.15f, 0, 0.75f, 0) },
            ['F'] = new[] { P(0, 0, 0, 1, 0.85f, 1), P(0, 0.55f, 0.65f, 0.55f) },
            ['T'] = new[] { P(0, 1, 0.9f, 1), P(0.45f, 1, 0.45f, 0) },
            ['Z'] = new[] { P(0.05f, 1, 0.88f, 1, 0.02f, 0, 0.9f, 0) },
            ['O'] = new[] { Ellipse(0.45f, 0.5f, 0.43f, 0.5f, 20) },
            ['N'] = new[] { P(0, 0, 0, 1, 0.85f, 0, 0.85f, 1) },
            ['E'] = new[] { P(0.85f, 1, 0, 1, 0, 0, 0.85f, 0), P(0, 0.52f, 0.65f, 0.52f) },
        };

        static Vector2[] P(params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            return pts;
        }

        static Vector2[] Ellipse(float cx, float cy, float rx, float ry, int n)
        {
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                pts[i] = new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry);
            }
            return pts;
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>
        /// Schriftzug im Look der Spray-Grafiken: dicke, leicht kursive Marker-Buchstaben in Gelb mit hellem Glanz,
        /// dunkler Kontur und lila 3D-Kante nach unten rechts. Transparent drumherum (Alpha-Cutout im Toon-Shader).
        /// </summary>
        static Texture2D SignLettering(string key, string text)
        {
            const int w = 1024, h = 256;
            const float slant = 0.22f, advance = 1.18f, space = 0.55f;
            // Laenge in Buchstabenbreiten, dann so skalieren, dass alles mit Rand hineinpasst
            float units = 0f;
            foreach (char ch in text) units += ch == ' ' ? space : advance;
            units -= advance - 0.9f;
            float glyphH = Mathf.Min(150f, (w - 70f) / (units * 0.62f + slant));
            float glyphW = glyphH * 0.62f, fill = glyphH * 0.09f, outline = fill + glyphH * 0.06f;
            Vector2 shadow = new Vector2(glyphH * 0.065f, -glyphH * 0.065f);
            float x0 = (w - (units * glyphW + slant * glyphH)) * 0.5f, y0 = (h - glyphH) * 0.5f;

            var segments = new List<(Vector2 a, Vector2 b)>();
            float cursor = 0f;
            foreach (char ch in text)
            {
                if (ch == ' ') { cursor += space; continue; }
                if (Glyphs.TryGetValue(ch, out var strokes))
                {
                    foreach (var stroke in strokes)
                    {
                        for (int i = 0; i < stroke.Length - 1; i++)
                        {
                            Vector2 Map(Vector2 q) => new Vector2(x0 + (cursor + q.x) * glyphW + slant * q.y * glyphH, y0 + q.y * glyphH);
                            segments.Add((Map(stroke[i]), Map(stroke[i + 1])));
                        }
                    }
                }
                cursor += advance;
            }

            float Dist(Vector2 p)
            {
                float best = float.MaxValue;
                foreach (var s in segments) best = Mathf.Min(best, SegmentDistance(p, s.a, s.b));
                return best;
            }

            Color fillCol = Palette.Yellow, shine = Palette.Hex("FFF6C9"), ink = Palette.Ink, depth = Palette.Hex("5A1E4A");
            return Colored(key, w, h, (x, y) =>
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Dist(p);
                if (d < fill) return Dist(p + new Vector2(0f, fill * 0.45f)) >= fill ? shine : fillCol; // Glanz an der Oberkante
                if (d < outline) return ink;
                float ds = Dist(p - shadow);
                if (ds < outline) return ds < fill ? depth : ink;
                return new Color(0f, 0f, 0f, 0f);
            });
        }

        /// <summary>Ladentuer (ca. 1,5 x 2,3 m): Rahmen, Glas oben, Paneel unten, gelber Griff.</summary>
        static Texture2D Door(string key)
        {
            Color frame = Palette.Hex("3A3D50"), panel = Palette.Hex("4A4E63");
            Color glass = Lit(Palette.Hex("F0BE78"), 0.7f), streak = Lit(Palette.Hex("FFF4DC"), 0.6f), handle = Palette.Yellow;
            return Colored(key, 128, 256, (x, y) =>
            {
                float u = (x + 0.5f) / 128f, v = (y + 0.5f) / 256f;
                if (u < 0.08f || u > 0.92f || v > 0.95f || (v > 0.86f && v < 0.88f)) return frame;
                if (u > 0.74f && u < 0.8f && v > 0.42f && v < 0.58f) return handle;
                if (v < 0.4f) return panel;
                float d = u * 0.7f + v * 0.5f;
                d -= Mathf.Floor(d);
                return d > 0.25f && d < 0.36f ? streak : glass;
            });
        }
    }
}
