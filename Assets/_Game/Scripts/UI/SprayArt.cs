using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DriftSkate
{
    /// <summary>
    /// Spruehdosen-Optik fuer das Menue, komplett im Code erzeugt (keine Bilddateien):
    /// Farbstriche mit Spruehnebel und Nasen, Kleckse, Sticker-Icons, Spraydose und Stern.
    /// Alle Formen sind weiss bzw. in festen Farben und werden ueber Image.color eingefaerbt.
    /// </summary>
    public static class SprayArt
    {
        public const int StrokeVariants = 6, SplatVariants = 4;

        /// <summary>Lage des eigentlichen Strichs in der Stroke-Textur (Anteil von oben); darunter haengen die Nasen.</summary>
        public const float StrokeTop = 0.08f, StrokeBottom = 0.66f;

        public enum Icon { Wheel, Wrench, Paint, Board, Shirt, Gear, Garage, Can, Steering }

        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite Stroke(int variant) => Get("stroke" + Mod(variant, StrokeVariants), () => MakeStroke(Mod(variant, StrokeVariants)));
        public static Sprite Splat(int variant) => Get("splat" + Mod(variant, SplatVariants), () => MakeSplat(Mod(variant, SplatVariants)));
        public static Sprite IconSprite(Icon icon) => Get("icon" + icon, () => MakeIcon(icon));
        public static Sprite BigCan => Get("bigcan", MakeBigCan);
        public static Sprite Star => Get("star", MakeStar);

        static int Mod(int v, int n) => ((v % n) + n) % n;

        static Sprite Get(string key, Func<Texture2D> make)
        {
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            var tex = make();
            tex.name = "Spray_" + key;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            s.name = key;
            Cache[key] = s;
            return s;
        }

        // ------------------------------------------------------------------------------------------ Rauschen

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        static float Noise(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Fraktales Rauschen, etwa -1..1.</summary>
        static float Fbm(float x, float y, int seed, int octaves = 4)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += (Noise(x, y, seed + i * 31) * 2f - 1f) * amp;
                norm += amp;
                x *= 2.03f; y *= 2.03f; amp *= 0.5f;
            }
            return sum / norm;
        }

        static float SMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        static float Capsule(float px, float py, float ax, float ay, float bx, float by, float r)
        {
            float pax = px - ax, pay = py - ay, bax = bx - ax, bay = by - ay;
            float h = Mathf.Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay + 1e-6f));
            float dx = pax - bax * h, dy = pay - bay * h;
            return Mathf.Sqrt(dx * dx + dy * dy) - r;
        }

        // ------------------------------------------------------------------------------------------ Spruehfarbe

        struct Drip { public float x, y0, y1, w; }

        /// <summary>Spruehfarbe aus einer Distanzfunktion (Pixel, negativ = innen): weiche Kante, Nebel, Koernung.</summary>
        static Texture2D SprayTexture(int w, int h, int seed, Func<float, float, float> sdf)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float fy = h - 1 - y; // Texturzeile 0 ist unten, gerechnet wird von oben
                for (int x = 0; x < w; x++)
                {
                    float d = sdf(x, fy);
                    d += Fbm(x / 16f, fy / 16f, seed) * 3.2f;            // ausgefranste Kante
                    float core = Mathf.Clamp01(0.5f - d / 2.4f);
                    float grain = Hash(x, y, seed + 7);
                    float halo = d > 0f ? Mathf.Exp(-d / 7f) * 0.55f * (grain > 0.5f ? 1f : 0.25f) : 0f; // Spruehnebel
                    float a = Mathf.Max(core, halo);
                    if (core > 0.99f) a *= 0.94f + 0.06f * Fbm(x / 5f, fy / 5f, seed + 3, 2);            // Koernung im Strich
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static float Drips(List<Drip> drips, float px, float py, float d)
        {
            foreach (var dr in drips)
            {
                float body = Capsule(px, py, dr.x, dr.y0, dr.x, dr.y1, dr.w);
                float drop = Mathf.Sqrt((px - dr.x) * (px - dr.x) + (py - dr.y1) * (py - dr.y1)) - dr.w * 1.45f;
                d = SMin(d, Mathf.Min(body, drop), 5f);
            }
            return d;
        }

        static Texture2D MakeStroke(int v)
        {
            const int W = 512, H = 256;
            int seed = 1000 + v * 97;
            var rng = new System.Random(seed);
            float top = H * StrokeTop, bottom = H * StrokeBottom;
            float mid = (top + bottom) * 0.5f, half = (bottom - top) * 0.5f;
            float xl = W * (0.03f + 0.03f * (float)rng.NextDouble());
            float xr = W * (0.97f - 0.03f * (float)rng.NextDouble());
            float slant = ((float)rng.NextDouble() - 0.5f) * 0.05f;

            Func<float, float> centre = x => mid + Fbm(x / 140f, 0.5f, seed + 11, 3) * H * 0.035f + (x - W * 0.5f) * slant;
            Func<float, float> radius = x => half * (0.97f + 0.07f * Fbm(x / 80f, 3.7f, seed + 13, 3));

            var drips = new List<Drip>();
            int count = 2 + rng.Next(4);
            for (int i = 0; i < count; i++)
            {
                float x = Mathf.Lerp(xl + half, xr - half, (float)rng.NextDouble());
                float y0 = centre(x) + radius(x) * 0.8f;
                float len = H * (0.06f + 0.24f * (float)rng.NextDouble());
                float wid = 3.5f + 5f * (float)rng.NextDouble();
                drips.Add(new Drip { x = x, y0 = y0, y1 = Mathf.Min(H - wid * 1.6f - 4f, y0 + len), w = wid });
            }
            var specks = new List<Vector3>();
            for (int i = 0; i < 26; i++)
            {
                float x = Mathf.Lerp(xl, xr, (float)rng.NextDouble());
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                float y = centre(x) + side * (radius(x) + 4f + 16f * (float)rng.NextDouble());
                specks.Add(new Vector3(x, y, 0.8f + 2f * (float)rng.NextDouble()));
            }

            return SprayTexture(W, H, seed, (x, y) =>
            {
                float r = radius(x), c = centre(x);
                float dx = Mathf.Max(xl + r - x, 0f, x - (xr - r));
                float dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy) - r;
                d = Drips(drips, x, y, d);
                foreach (var s in specks) d = Mathf.Min(d, Mathf.Sqrt((x - s.x) * (x - s.x) + (y - s.y) * (y - s.y)) - s.z);
                return d;
            });
        }

        static Texture2D MakeSplat(int v)
        {
            const int S = 256;
            int seed = 5000 + v * 131;
            var rng = new System.Random(seed);
            float cx = S * 0.5f, cy = S * 0.42f, rad = S * 0.27f;
            var drips = new List<Drip>();
            int count = 2 + rng.Next(3);
            for (int i = 0; i < count; i++)
            {
                float x = cx + (((float)rng.NextDouble()) - 0.5f) * rad * 1.3f;
                float y0 = cy + rad * 0.7f;
                float wid = 4f + 5f * (float)rng.NextDouble();
                drips.Add(new Drip { x = x, y0 = y0, y1 = Mathf.Min(S - wid * 1.6f - 4f, y0 + S * (0.08f + 0.25f * (float)rng.NextDouble())), w = wid });
            }
            var specks = new List<Vector3>();
            for (int i = 0; i < 30; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = rad * (1.15f + 0.55f * (float)rng.NextDouble());
                specks.Add(new Vector3(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r * 0.9f, 1f + 3.5f * (float)rng.NextDouble()));
            }
            return SprayTexture(S, S, seed, (x, y) =>
            {
                float dx = x - cx, dy = y - cy;
                float ang = Mathf.Atan2(dy, dx);
                float rr = rad * (1f + 0.2f * Fbm(Mathf.Cos(ang) * 1.6f + 7f, Mathf.Sin(ang) * 1.6f + 3f, seed + 5, 3));
                float d = Mathf.Sqrt(dx * dx + dy * dy) - rr;
                d = Drips(drips, x, y, d);
                foreach (var s in specks) d = Mathf.Min(d, Mathf.Sqrt((x - s.x) * (x - s.x) + (y - s.y) * (y - s.y)) - s.z);
                return d;
            });
        }

        // ------------------------------------------------------------------------------------------ Sticker-Icons

        /// <summary>Kleine Leinwand mit Distanzfunktionen: Formen mit dicker Kontur uebereinander malen.</summary>
        class Canvas
        {
            readonly int _w, _h;
            readonly Color[] _px;
            readonly float _aspect, _pix;
            public Canvas(int w, int h)
            {
                _w = w; _h = h;
                _px = new Color[w * h];
                _aspect = w / (float)h;
                _pix = 2f / h;
            }

            /// <summary>Koordinaten: y von -1 (unten) bis 1 (oben), x von -Seitenverhaeltnis bis +Seitenverhaeltnis.</summary>
            public void Fill(Func<float, float, float> sdf, Color color, float outline = 0f, Color? outlineColor = null)
            {
                Color oc = outlineColor ?? Palette.Ink;
                for (int j = 0; j < _h; j++)
                {
                    float y = (j + 0.5f) / _h * 2f - 1f;
                    for (int i = 0; i < _w; i++)
                    {
                        float x = ((i + 0.5f) / _w * 2f - 1f) * _aspect;
                        float d = sdf(x, y);
                        if (d - outline > _pix) continue;
                        int k = j * _w + i;
                        if (outline > 0f) Blend(k, oc, Mathf.Clamp01(0.5f - (d - outline) / _pix));
                        Blend(k, color, Mathf.Clamp01(0.5f - d / _pix));
                    }
                }
            }

            void Blend(int k, Color c, float cov)
            {
                cov *= c.a;
                if (cov <= 0f) return;
                Color dst = _px[k];
                float a = cov + dst.a * (1f - cov);
                if (a <= 1e-5f) return;
                _px[k] = new Color((c.r * cov + dst.r * dst.a * (1f - cov)) / a, (c.g * cov + dst.g * dst.a * (1f - cov)) / a,
                                   (c.b * cov + dst.b * dst.a * (1f - cov)) / a, a);
            }

            public Texture2D ToTexture()
            {
                var tex = new Texture2D(_w, _h, TextureFormat.RGBA32, false);
                tex.SetPixels(_px);
                tex.Apply(false, true);
                return tex;
            }
        }

        static float Circle(float x, float y, float cx, float cy, float r) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;

        static float Box(float x, float y, float cx, float cy, float hw, float hh, float rotDeg = 0f, float round = 0f)
        {
            float a = -rotDeg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            float px = (x - cx) * c - (y - cy) * s, py = (x - cx) * s + (y - cy) * c;
            float qx = Mathf.Abs(px) - hw + round, qy = Mathf.Abs(py) - hh + round;
            return Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f)) + Mathf.Min(Mathf.Max(qx, qy), 0f) - round;
        }

        static float Polygon(float x, float y, Vector2[] v)
        {
            float d = float.MaxValue, s = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                float ex = v[j].x - v[i].x, ey = v[j].y - v[i].y, wx = x - v[i].x, wy = y - v[i].y;
                float h = Mathf.Clamp01((wx * ex + wy * ey) / (ex * ex + ey * ey));
                float bx = wx - ex * h, by = wy - ey * h;
                d = Mathf.Min(d, bx * bx + by * by);
                bool c1 = y >= v[i].y, c2 = y < v[j].y, c3 = ex * wy > ey * wx;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * Mathf.Sqrt(d);
        }

        /// <summary>Koordinaten fuer eine gedrehte Form.</summary>
        static void Rot(ref float x, ref float y, float deg)
        {
            float a = -deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            float nx = x * c - y * s, ny = x * s + y * c;
            x = nx; y = ny;
        }

        const float Line = 0.085f; // Kontur der Icons
        static readonly Color Silver = Palette.Hex("D9DCE6"), DarkMetal = Palette.Hex("3A3D4E");

        static Texture2D MakeIcon(Icon icon)
        {
            var c = new Canvas(128, 128);
            switch (icon)
            {
                case Icon.Wheel:
                    c.Fill((x, y) => Circle(x, y, 0f, 0f, 0.8f), Palette.Rubber, Line, Palette.White);
                    c.Fill((x, y) => Circle(x, y, 0f, 0f, 0.5f), Silver, 0.04f);
                    c.Fill((x, y) => Circle(x, y, 0f, 0f, 0.38f), DarkMetal);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 72f * Mathf.Deg2Rad + 0.3f;
                        float ex = Mathf.Cos(a) * 0.45f, ey = Mathf.Sin(a) * 0.45f;
                        c.Fill((x, y) => Capsule(x, y, 0f, 0f, ex, ey, 0.075f), Silver);
                    }
                    c.Fill((x, y) => Circle(x, y, 0f, 0f, 0.13f), Silver, 0.03f);
                    break;
                case Icon.Wrench:
                    c.Fill((x, y) =>
                    {
                        float handle = Capsule(x, y, -0.6f, -0.6f, 0.2f, 0.2f, 0.15f);
                        float head = Circle(x, y, 0.42f, 0.42f, 0.36f);
                        float notch = Box(x, y, 0.62f, 0.62f, 0.15f, 0.4f, -45f);
                        float hole = Circle(x, y, -0.55f, -0.55f, 0.065f);
                        return Mathf.Max(Mathf.Min(handle, Mathf.Max(head, -notch)), -hole);
                    }, Silver, Line);
                    break;
                case Icon.Paint:
                    c.Fill((x, y) =>
                    {
                        float blob = Circle(x, y, 0f, 0.22f, 0.55f) + 0.06f * Mathf.Sin(Mathf.Atan2(y - 0.22f, x) * 5f);
                        float d1 = Mathf.Min(Capsule(x, y, -0.22f, 0f, -0.22f, -0.55f, 0.1f), Circle(x, y, -0.22f, -0.6f, 0.15f));
                        float d2 = Mathf.Min(Capsule(x, y, 0.2f, 0f, 0.2f, -0.35f, 0.08f), Circle(x, y, 0.2f, -0.4f, 0.12f));
                        return SMin(blob, Mathf.Min(d1, d2), 0.08f);
                    }, Palette.Pink, Line);
                    c.Fill((x, y) => Circle(x, y, -0.2f, 0.42f, 0.11f), new Color(1f, 1f, 1f, 0.85f));
                    break;
                case Icon.Board:
                    for (int i = -1; i <= 1; i += 2)
                        for (int k = -1; k <= 1; k += 2)
                        {
                            float wx = i * 0.5f, wy = k * 0.2f;
                            Rot(ref wx, ref wy, -35f);
                            c.Fill((x, y) => Circle(x, y, wx, wy, 0.15f), Palette.Pink, 0.05f);
                        }
                    c.Fill((x, y) => Box(x, y, 0f, 0f, 0.92f, 0.25f, 35f, 0.24f), Palette.Yellow, Line);
                    c.Fill((x, y) => Box(x, y, 0f, 0f, 0.62f, 0.05f, 35f, 0.05f), Palette.Pink);
                    break;
                case Icon.Shirt:
                    var shirt = new[]
                    {
                        new Vector2(-0.9f, 0.42f), new Vector2(-0.5f, 0.78f), new Vector2(0.5f, 0.78f), new Vector2(0.9f, 0.42f),
                        new Vector2(0.66f, 0.12f), new Vector2(0.45f, 0.28f), new Vector2(0.45f, -0.82f), new Vector2(-0.45f, -0.82f),
                        new Vector2(-0.45f, 0.28f), new Vector2(-0.66f, 0.12f)
                    };
                    c.Fill((x, y) => Mathf.Max(Polygon(x, y, shirt) - 0.04f, -Circle(x, y, 0f, 0.86f, 0.22f)), Palette.Blue, Line);
                    c.Fill((x, y) => Box(x, y, 0f, -0.1f, 0.3f, 0.06f, 0f, 0.06f), Palette.Cyan);
                    break;
                case Icon.Gear:
                    c.Fill((x, y) =>
                    {
                        float a = Mathf.Atan2(y, x);
                        float teeth = Mathf.Clamp(Mathf.Sin(a * 8f) * 3f, -1f, 1f);
                        float r = Mathf.Sqrt(x * x + y * y);
                        return Mathf.Max(r - (0.66f + 0.13f * teeth), -(r - 0.25f));
                    }, Palette.Hex("B8BCC9"), Line);
                    c.Fill((x, y) => Mathf.Abs(Circle(x, y, 0f, 0f, 0.42f)) - 0.035f, DarkMetal);
                    break;
                case Icon.Garage:
                    var house = new[]
                    {
                        new Vector2(-0.9f, 0.08f), new Vector2(0f, 0.82f), new Vector2(0.9f, 0.08f), new Vector2(0.72f, 0.08f),
                        new Vector2(0.72f, -0.82f), new Vector2(-0.72f, -0.82f), new Vector2(-0.72f, 0.08f)
                    };
                    c.Fill((x, y) => Polygon(x, y, house), Palette.Orange, Line);
                    c.Fill((x, y) => Box(x, y, 0f, -0.36f, 0.46f, 0.44f, 0f, 0.04f), DarkMetal);
                    for (int i = 0; i < 4; i++)
                    {
                        float yy = -0.1f - i * 0.19f;
                        c.Fill((x, y) => Box(x, y, 0f, yy, 0.42f, 0.035f), Silver);
                    }
                    break;
                case Icon.Steering:
                    c.Fill((x, y) => Mathf.Abs(Circle(x, y, 0f, 0f, 0.66f)) - 0.12f, Palette.Rubber, Line, Palette.White);
                    c.Fill((x, y) => Mathf.Min(Box(x, y, 0f, 0.0f, 0.62f, 0.09f, 0f, 0.05f), Box(x, y, 0f, -0.32f, 0.09f, 0.34f, 0f, 0.05f)), Silver, 0.04f);
                    c.Fill((x, y) => Circle(x, y, 0f, 0f, 0.2f), Palette.Red, 0.04f);
                    break;
                default: // Spraydose
                    PaintCan(c, 1f, -20f);
                    break;
            }
            return c.ToTexture();
        }

        /// <summary>Spraydose (Hoehe ~1.8 in Leinwand-Einheiten mal scale), gedreht um rot Grad.</summary>
        static void PaintCan(Canvas c, float scale, float rot)
        {
            Func<Func<float, float, float>, Func<float, float, float>> T = f => (x, y) =>
            {
                Rot(ref x, ref y, -rot);
                return f(x / scale, y / scale) * scale;
            };
            c.Fill(T((x, y) => Box(x, y, 0f, -0.12f, 0.4f, 0.72f, 0f, 0.14f)), Palette.Orange, Line);
            c.Fill(T((x, y) => Box(x, y, 0f, -0.18f, 0.4f, 0.28f)), Palette.Pink);
            c.Fill(T((x, y) => Box(x, y, 0f, -0.18f, 0.4f, 0.06f)), Palette.Cyan);
            c.Fill(T((x, y) => Box(x, y, 0f, 0.62f, 0.3f, 0.12f, 0f, 0.1f)), Silver, Line);
            c.Fill(T((x, y) => Box(x, y, 0f, 0.8f, 0.16f, 0.1f, 0f, 0.05f)), Palette.Pink, Line);
            c.Fill(T((x, y) => Box(x, y, 0.13f, 0.86f, 0.07f, 0.04f)), DarkMetal);
            c.Fill(T((x, y) => Box(x, y, -0.24f, -0.12f, 0.05f, 0.52f, 0f, 0.05f)), new Color(1f, 1f, 1f, 0.55f));
        }

        static Texture2D MakeBigCan()
        {
            var c = new Canvas(256, 384);
            PaintCan(c, 1.05f, -14f);
            return c.ToTexture();
        }

        static Texture2D MakeStar()
        {
            var c = new Canvas(128, 128);
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                float r = i % 2 == 0 ? 0.88f : 0.38f;
                pts[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r - 0.05f);
            }
            c.Fill((x, y) => Polygon(x, y, pts) - 0.03f, Palette.Yellow, Line);
            c.Fill((x, y) => Circle(x, y, -0.12f, 0.2f, 0.09f), new Color(1f, 1f, 1f, 0.8f));
            return c.ToTexture();
        }
    }

    /// <summary>Laesst einen Spruehstrich von links nach rechts erscheinen (Image im Modus Filled).</summary>
    public class SprayReveal : MonoBehaviour
    {
        Image _img;
        float _t, _delay, _duration = 0.2f;

        public static void Play(Image img, float delay = 0f, float duration = 0.2f)
        {
            if (img == null) return;
            var r = img.GetComponent<SprayReveal>();
            if (r == null) r = img.gameObject.AddComponent<SprayReveal>();
            r._img = img;
            r._t = 0f;
            r._delay = delay;
            r._duration = duration;
            r.enabled = true;
            img.fillAmount = 0f;
        }

        void Update()
        {
            if (_img == null) { enabled = false; return; }
            _t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01((_t - _delay) / _duration);
            _img.fillAmount = 1f - (1f - k) * (1f - k);
            if (k >= 1f) enabled = false;
        }
    }

    /// <summary>Senkrechter Farbverlauf fuer UI-Text (z. B. das Logo).</summary>
    public class UIGradient : BaseMeshEffect
    {
        public Color top = Color.white, bottom = Color.gray;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var v = new UIVertex();
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                min = Mathf.Min(min, v.position.y);
                max = Mathf.Max(max, v.position.y);
            }
            float span = Mathf.Max(1e-3f, max - min);
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                Color c = Color.Lerp(bottom, top, (v.position.y - min) / span);
                v.color = c * (Color)v.color;
                vh.SetUIVertex(v, i);
            }
        }
    }
}
