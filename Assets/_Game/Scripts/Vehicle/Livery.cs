using System;
using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>Eine Folien-Ebene: Sticker mit Farbe, Lage, Groesse und Drehung auf einem Bereich des Autos.</summary>
    [Serializable]
    public class WrapLayer
    {
        public const int BothSides = 0, Left = 1, Right = 2, Top = 3;
        public static readonly string[] AreaNames = { "BEIDE SEITEN", "LINKS", "RECHTS", "OBEN" };

        public int sticker;
        public Color color = Color.white;
        public float x = 0.5f;       // entlang des Autos: 0 Heck .. 1 Front
        public float y = 0.5f;       // Seiten: Hoehe 0..1 der Autohoehe; oben: 0 links .. 1 rechts
        public float size = 0.5f;    // Hoehe in Metern
        public float stretch = 1f;   // Breite relativ zur normalen Form
        public float rot;            // Grad, gegen den Uhrzeigersinn (so wie man draufschaut)
        public int area;
        public bool mirror;

        public WrapLayer Clone() => (WrapLayer)MemberwiseClone();
    }

    /// <summary>Optik eines Autos: Folien-Ebenen, Anbauteile, Felgenfarbe, Sturz (wird mit dem Spielstand gespeichert und online verteilt).</summary>
    [Serializable]
    public class CarDesign
    {
        public const int Rims = 0, Lip = 1, Skirts = 2, Fenders = 3, Wing = 4, Hood = 5, Exhaust = 6, PopUps = 7, PartCount = 8;
        public const int MaxLayers = 24;

        public static readonly string[] PartNames = { "FELGEN", "FRONTLIPPE", "SCHWELLER", "VERBREITERUNG", "HECKFLUEGEL", "MOTORHAUBE", "AUSPUFF", "POP-UPS" };
        public static readonly string[][] PartOptions =
        {
            new[] { "SECHS-SPEICHE", "MESH", "DEEP DISH", "FUENF-SPEICHE", "STAHLFELGE" },
            new[] { "SERIE", "LIPPE", "SPLITTER" },
            new[] { "SERIE", "KIT" },
            new[] { "NEIN", "GENIETET" },
            new[] { "KEINER", "ENTENBUERZEL", "GT-FLUEGEL", "BUEGEL", "SCHWANENHALS" },
            new[] { "LACK", "CARBON", "CARBON + LUFT" },
            new[] { "SERIE", "DOSE", "DOPPELROHR" },
            new[] { "ZU", "AUF" },
        };

        public List<WrapLayer> wrap = new List<WrapLayer>();
        public int[] parts = new int[PartCount];
        public Color rimColor = Palette.Hex("C9CCD6");
        public float camber;
        public int neon;                                // Unterboden-Neon: 0 aus, 1 an, 2 pulsierend, 3 Regenbogen (Underglow.Modes)
        public Color neonColor = Palette.Hex("2EE6FF");
        public bool initialized;     // false = noch nie eingerichtet (JsonUtility legt leere Objekte an)

        public CarDesign Clone()
        {
            var d = (CarDesign)MemberwiseClone();
            d.parts = (int[])parts.Clone();
            d.wrap = new List<WrapLayer>();
            foreach (var l in wrap) d.wrap.Add(l.Clone());
            return d;
        }

        /// <summary>Werksoptik je Auto (Felgen und Fluegel wie das Vorbild, Crew-Streifen als Folie).</summary>
        public static CarDesign Default(string carId, Color crew)
        {
            var d = new CarDesign { initialized = true };
            switch (carId)
            {
                case "roku86": d.parts[Rims] = 4; break;
                case "sylph15": d.parts[Rims] = 0; d.parts[Wing] = 1; break;
                case "kazefc": d.parts[Rims] = 1; break;
                case "mark2j": d.parts[Rims] = 2; break;
                case "toro2j": d.parts[Rims] = 3; d.parts[Wing] = 3; break;
                case "muscle8": d.parts[Rims] = 0; d.parts[Wing] = 1; break;
            }
            d.wrap = LiveryPresets.Make("CREW-STREIFEN", Catalog.Car(carId), crew, Color.white);
            return d;
        }

        public void Sanitize()
        {
            if (parts == null || parts.Length != PartCount)
            {
                var p = new int[PartCount];
                if (parts != null) Array.Copy(parts, p, Mathf.Min(parts.Length, PartCount));
                parts = p;
            }
            for (int i = 0; i < PartCount; i++) parts[i] = Mathf.Clamp(parts[i], 0, PartOptions[i].Length - 1);
            if (wrap == null) wrap = new List<WrapLayer>();
            if (wrap.Count > MaxLayers) wrap.RemoveRange(MaxLayers, wrap.Count - MaxLayers);
            foreach (var l in wrap) l.sticker = Mathf.Clamp(l.sticker, 0, StickerLibrary.Count - 1);
            neon = Mathf.Clamp(neon, 0, Underglow.Modes.Length - 1);
        }

        // ------------------------------------------------------------------ Kompakt fuer das Netzwerk

        static byte B(float v01) => (byte)Mathf.Clamp(Mathf.RoundToInt(v01 * 255f), 0, 255);
        static byte LogB(float v, float min, float max) => B(Mathf.InverseLerp(Mathf.Log(min), Mathf.Log(max), Mathf.Log(Mathf.Clamp(v, min, max))));
        static float UnLog(byte b, float min, float max) => Mathf.Exp(Mathf.Lerp(Mathf.Log(min), Mathf.Log(max), b / 255f));

        public string Encode()
        {
            var bytes = new List<byte> { 1 };
            for (int i = 0; i < PartCount; i++) bytes.Add((byte)parts[i]);
            bytes.Add(B(rimColor.r)); bytes.Add(B(rimColor.g)); bytes.Add(B(rimColor.b));
            bytes.Add(B(Mathf.InverseLerp(0f, 12f, camber)));
            int n = Mathf.Min(wrap.Count, MaxLayers);
            bytes.Add((byte)n);
            for (int i = 0; i < n; i++)
            {
                var l = wrap[i];
                bytes.Add((byte)l.sticker);
                bytes.Add((byte)((l.area & 3) | (l.mirror ? 4 : 0)));
                bytes.Add(B(l.x)); bytes.Add(B(l.y));
                bytes.Add(LogB(l.size, 0.04f, 4f));
                bytes.Add(LogB(l.stretch, 0.1f, 12f));
                bytes.Add(B(Mathf.Repeat(l.rot + 180f, 360f) / 360f));
                bytes.Add(B(l.color.r)); bytes.Add(B(l.color.g)); bytes.Add(B(l.color.b));
            }
            // Neon hinten angehaengt (aeltere Stande ohne diese Bytes bleiben lesbar)
            bytes.Add((byte)neon);
            bytes.Add(B(neonColor.r)); bytes.Add(B(neonColor.g)); bytes.Add(B(neonColor.b));
            return Convert.ToBase64String(bytes.ToArray());
        }

        public static CarDesign Decode(string s, string carId, Color crew)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return Default(carId, crew);
                var b = Convert.FromBase64String(s);
                int k = 0;
                if (b[k++] != 1) return Default(carId, crew);
                var d = new CarDesign { initialized = true };
                for (int i = 0; i < PartCount; i++) d.parts[i] = b[k++];
                d.rimColor = new Color(b[k] / 255f, b[k + 1] / 255f, b[k + 2] / 255f);
                k += 3;
                d.camber = b[k++] / 255f * 12f;
                int n = b[k++];
                for (int i = 0; i < n; i++)
                {
                    var l = new WrapLayer
                    {
                        sticker = b[k++],
                        area = b[k] & 3,
                        mirror = (b[k++] & 4) != 0,
                        x = b[k++] / 255f,
                        y = b[k++] / 255f,
                        size = UnLog(b[k++], 0.04f, 4f),
                        stretch = UnLog(b[k++], 0.1f, 12f),
                        rot = b[k++] / 255f * 360f - 180f,
                    };
                    l.color = new Color(b[k] / 255f, b[k + 1] / 255f, b[k + 2] / 255f);
                    k += 3;
                    d.wrap.Add(l);
                }
                if (k + 4 <= b.Length)
                {
                    d.neon = b[k++];
                    d.neonColor = new Color(b[k] / 255f, b[k + 1] / 255f, b[k + 2] / 255f);
                    k += 3;
                }
                d.Sanitize();
                return d;
            }
            catch (Exception)
            {
                return Default(carId, crew);
            }
        }
    }

    // ====================================================================== Sticker

    /// <summary>Sticker-Motive, im Code gezeichnet (weisse Maske, wird beim Aufbringen eingefaerbt).</summary>
    public static class StickerLibrary
    {
        public class Def
        {
            public string name;
            public float aspect = 1f;        // Breite / Hoehe
            public bool directional;         // "Kopf" links im Bild (zeigt nach vorn); auf der rechten Seite gespiegelt
            public Func<float, float, float> mask;
            public bool graffiti;            // eigenes Graffiti (wird nicht eingefaerbt)
            internal Texture2D tex;
        }

        public const int GraffitiIndex = 0;

        static readonly List<Def> All = new List<Def>
        {
            new Def { name = "DEIN GRAFFITI", aspect = 1f, graffiti = true },
            new Def { name = "STREIFEN", aspect = 8f, mask = (u, v) => Box(u, v, 0.005f, 0.06f, 0.995f, 0.94f) },
            new Def { name = "DOPPELSTREIFEN", aspect = 8f, mask = (u, v) => Mathf.Max(Box(u, v, 0.005f, 0.04f, 0.995f, 0.4f), Box(u, v, 0.005f, 0.6f, 0.995f, 0.96f)) },
            new Def { name = "FLAECHE", aspect = 1f, mask = (u, v) => Box(u, v, 0.004f, 0.004f, 0.996f, 0.996f) },
            new Def { name = "VERLAUF", aspect = 4f, directional = true, mask = (u, v) => Box(u, v, 0.003f, 0.02f, 0.997f, 0.98f) * Mathf.Clamp01(1.15f - u * 1.15f) },
            new Def { name = "SPEED-LINES", aspect = 5f, directional = true, mask = SpeedLines },
            new Def { name = "HALFTONE", aspect = 3f, directional = true, mask = Halftone },
            new Def { name = "FLAMMEN", aspect = 2.6f, directional = true, mask = Flames },
            new Def { name = "SWOOSH", aspect = 4f, directional = true, mask = Swoosh },
            new Def { name = "PFEIL", aspect = 2f, directional = true, mask = Arrow },
            new Def { name = "HINOMARU", aspect = 1f, mask = (u, v) => Circle(u, v, 0.5f, 0.5f, 0.47f) },
            new Def { name = "RING", aspect = 1f, mask = (u, v) => Circle(u, v, 0.5f, 0.5f, 0.47f) * (1f - Circle(u, v, 0.5f, 0.5f, 0.35f)) },
            new Def { name = "SONNE", aspect = 2f, mask = RisingSun },
            new Def { name = "STERN", aspect = 1f, mask = Star },
            new Def { name = "BLITZ", aspect = 0.55f, mask = Bolt },
            new Def { name = "ZIELFLAGGE", aspect = 2f, mask = (u, v) => ((Mathf.FloorToInt(u * 8f) + Mathf.FloorToInt(v * 4f)) % 2 == 0 ? 1f : 0f) * Box(u, v, 0.004f, 0.008f, 0.996f, 0.992f) },
            new Def { name = "SAKURA", aspect = 1f, mask = Sakura },
            new Def { name = "KLECKS", aspect = 1f, mask = Splat },
            new Def { name = "CAMO", aspect = 2f, mask = Camo },
            new Def { name = "TOUGE 峠", aspect = 1f, mask = (u, v) => Strokes(u, v, Kanji.Touge, 0.052f, 0f) },
            new Def { name = "HASHIRI 走", aspect = 1f, mask = (u, v) => Strokes(u, v, Kanji.Hashiri, 0.058f, 0f) },
            new Def { name = "DRIFT", aspect = 3.4f, mask = (u, v) => Text(u, v, "DRIFT", 3.4f) },
            new Def { name = "JDM", aspect = 2.2f, mask = (u, v) => Text(u, v, "JDM", 2.2f) },
            new Def { name = "#86", aspect = 1f, mask = (u, v) => NumberPlate(u, v, "86") },
            new Def { name = "#13", aspect = 1f, mask = (u, v) => NumberPlate(u, v, "13") },
            new Def { name = "#07", aspect = 1f, mask = (u, v) => NumberPlate(u, v, "07") },
        };

        public static int Count => All.Count;
        public static Def Get(int i) => All[Mathf.Clamp(i, 0, All.Count - 1)];
        public static int Find(string name) => Mathf.Max(0, All.FindIndex(d => d.name == name));

        /// <summary>Maske als Textur (einmal erzeugt). Das Graffiti liefert der Aufrufer selbst.</summary>
        public static Texture2D Texture(int i)
        {
            var d = Get(i);
            if (d.graffiti) return null;
            if (d.tex != null) return d.tex;
            int h = 256, w = Mathf.Clamp(Mathf.RoundToInt(h * d.aspect), 64, 1024);
            if (d.aspect < 1f) { w = 256; h = Mathf.Clamp(Mathf.RoundToInt(w / d.aspect), 64, 1024); }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Sticker_" + d.name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // 2x2 Supersampling fuer glatte Kanten
                    float a = 0f;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                            a += d.mask((x + 0.25f + sx * 0.5f) / w, (y + 0.25f + sy * 0.5f) / h);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a * 0.25f) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            d.tex = tex;
            return tex;
        }

        // ------------------------------------------------------------------ Formen

        static float Box(float u, float v, float x0, float y0, float x1, float y1) => u >= x0 && u <= x1 && v >= y0 && v <= y1 ? 1f : 0f;

        static float Circle(float u, float v, float cx, float cy, float r) => (u - cx) * (u - cx) + (v - cy) * (v - cy) <= r * r ? 1f : 0f;

        static float SpeedLines(float u, float v)
        {
            float[] start = { 0f, 0.12f, 0.04f };
            float[] len = { 1f, 0.7f, 0.85f };
            for (int i = 0; i < 3; i++)
            {
                float v0 = 0.04f + i * 0.33f, v1 = v0 + 0.24f;
                if (v < v0 || v > v1) continue;
                float t = (u - start[i]) / len[i];
                if (t < 0f || t > 1f) continue;
                // Kopf vorn abgeschraegt, hinten auslaufend
                float slant = (v - v0) / (v1 - v0) * 0.04f;
                if (t < slant) continue;
                return Mathf.Clamp01((1f - t) * 3f);
            }
            return 0f;
        }

        static float Halftone(float u, float v)
        {
            const float cols = 18f, rows = 6f;
            float cu = (Mathf.Floor(u * cols) + 0.5f) / cols, cv = (Mathf.Floor(v * rows) + 0.5f) / rows;
            float r = 0.55f * (1f - cu) / rows;
            float du = (u - cu) * 3f, dv = v - cv; // Kreise trotz breiter Textur rund
            return du * du + dv * dv <= r * r ? 1f : 0f;
        }

        static float Flames(float u, float v)
        {
            if (u < 0.1f && v > 0.08f && v < 0.92f) return 1f;
            float[] center = { 0.16f, 0.36f, 0.55f, 0.72f, 0.88f };
            float[] length = { 0.75f, 1f, 0.82f, 0.95f, 0.6f };
            for (int i = 0; i < center.Length; i++)
            {
                float t = u / length[i];
                if (t > 1f) continue;
                float c = center[i] + 0.06f * Mathf.Sin(u * 9f + i * 1.7f) * t;
                float half = 0.13f * Mathf.Pow(1f - t, 0.75f);
                if (Mathf.Abs(v - c) < half) return 1f;
            }
            return 0f;
        }

        static float Swoosh(float u, float v)
        {
            // Klinge zwischen zwei Kreisboegen, vorn dick, hinten spitz
            float x = u * 4f, y = v;
            float outer = (x - 2.6f) * (x - 2.6f) + (y + 2.4f) * (y + 2.4f);
            float inner = (x - 3.0f) * (x - 3.0f) + (y + 2.75f) * (y + 2.75f);
            return outer < 3.3f * 3.3f && inner > 3.35f * 3.35f && u < 0.98f ? 1f : 0f;
        }

        static float Arrow(float u, float v)
        {
            // Spitze links (vorn)
            if (u < 0.4f) return Mathf.Abs(v - 0.5f) < (u / 0.4f) * 0.48f ? 1f : 0f;
            return v > 0.3f && v < 0.7f && u < 0.98f ? 1f : 0f;
        }

        static float RisingSun(float u, float v)
        {
            float x = (u - 0.5f) * 2f, y = v; // Mittelpunkt unten Mitte, Halbkreis
            float r = Mathf.Sqrt(x * x + y * y);
            if (r > 0.98f) return 0f;
            if (r < 0.28f) return 1f;
            float a = Mathf.Atan2(y, x) / Mathf.PI * 16f;
            return Mathf.Repeat(a, 2f) < 1f ? 1f : 0f;
        }

        static float Star(float u, float v)
        {
            float x = u - 0.5f, y = v - 0.47f;
            float a = Mathf.Atan2(y, x) + Mathf.PI * 0.5f, r = Mathf.Sqrt(x * x + y * y);
            float k = Mathf.Repeat(a, Mathf.PI * 2f / 5f) / (Mathf.PI * 2f / 5f);
            float edge = Mathf.Lerp(0.2f, 0.48f, 1f - Mathf.Abs(k - 0.5f) * 2f);
            return r < edge ? 1f : 0f;
        }

        static float Bolt(float u, float v)
        {
            Vector2[] poly = { new Vector2(0.62f, 1f), new Vector2(0.1f, 0.42f), new Vector2(0.46f, 0.46f), new Vector2(0.3f, 0f), new Vector2(0.92f, 0.6f), new Vector2(0.55f, 0.56f) };
            return InPoly(new Vector2(u, v), poly) ? 1f : 0f;
        }

        static float Sakura(float u, float v)
        {
            float x = u - 0.5f, y = v - 0.5f;
            float a = Mathf.Atan2(y, x), r = Mathf.Sqrt(x * x + y * y);
            float petal = Mathf.Abs(Mathf.Cos(a * 2.5f + 0.3f));
            float notch = Mathf.Pow(Mathf.Abs(Mathf.Sin(a * 5f + 0.6f)), 12f) * 0.08f; // Kerbe an der Blattspitze
            bool inPetal = r < 0.2f + 0.28f * Mathf.Pow(petal, 0.5f) - notch;
            bool hole = r < 0.06f;
            return inPetal && !hole ? 1f : 0f;
        }

        static float Splat(float u, float v)
        {
            float x = u - 0.5f, y = v - 0.5f;
            float a = Mathf.Atan2(y, x), r = Mathf.Sqrt(x * x + y * y);
            float edge = 0.3f + 0.06f * Mathf.Sin(a * 5f) + 0.04f * Mathf.Sin(a * 11f + 1f) + 0.12f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(a * 7f + 2f)), 8f);
            if (r < edge) return 1f;
            for (int i = 0; i < 7; i++)
            {
                float da = i * 0.9f + 0.4f, dr = 0.4f + (i % 3) * 0.03f;
                if (Circle(u, v, 0.5f + Mathf.Cos(da) * dr, 0.5f + Mathf.Sin(da) * dr, 0.025f + (i % 2) * 0.015f) > 0f) return 1f;
            }
            return 0f;
        }

        static float Camo(float u, float v)
        {
            float n = Noise(u * 6f, v * 3f) * 0.65f + Noise(u * 13f + 5f, v * 6.5f + 3f) * 0.35f;
            return n > 0.55f ? Box(u, v, 0.004f, 0.008f, 0.996f, 0.992f) : 0f;
        }

        static float Noise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float H(int a, int b) { unchecked { uint h = (uint)a * 374761393u + (uint)b * 668265263u; h = (h ^ (h >> 13)) * 1274126177u; return (h & 0xFFFF) / 65535f; } }
            return Mathf.Lerp(Mathf.Lerp(H(ix, iy), H(ix + 1, iy), fx), Mathf.Lerp(H(ix, iy + 1), H(ix + 1, iy + 1), fx), fy);
        }

        static float NumberPlate(float u, float v, string digits)
        {
            // Runde Startnummer: Kreis mit ausgesparten Ziffern
            if (Circle(u, v, 0.5f, 0.5f, 0.48f) == 0f) return 0f;
            float cut = Text((u - 0.17f) / 0.66f, (v - 0.27f) / 0.46f, digits, 1.35f);
            return 1f - cut;
        }

        /// <summary>Japanisches Kennzeichen (weiss, gruene Schrift und Rand).</summary>
        public static Texture2D Plate(string number)
        {
            const int w = 256, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Plate_" + number, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            Color32 bg = new Color32(246, 244, 236, 255), ink = new Color32(30, 92, 58, 255);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    bool border = u < 0.03f || u > 0.97f || v < 0.06f || v > 0.94f;
                    bool small = Text((u - 0.3f) / 0.4f, (v - 0.68f) / 0.2f, "DS 86", 4f) > 0f; // kleine obere Zeile
                    bool big = Text((u - 0.08f) / 0.84f, (v - 0.1f) / 0.52f, number, 3.2f) > 0f;
                    px[y * w + x] = border && !(u > 0.035f && u < 0.965f && v > 0.065f && v < 0.935f) || small || big ? ink : bg;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static bool InPoly(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        // ------------------------------------------------------------------ Strich-Schrift

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        static float Strokes(float u, float v, Vector2[][] strokes, float thick, float slant)
        {
            var p = new Vector2(u - slant * v, v);
            foreach (var s in strokes)
                for (int i = 0; i < s.Length - 1; i++)
                    if (SegDist(p, s[i], s[i + 1]) < thick) return 1f;
            return 0f;
        }

        /// <summary>Text in Blockbuchstaben, auf die Boxbreite (aspect = Breite/Hoehe) verteilt, leicht kursiv.</summary>
        static float Text(float u, float v, string text, float aspect)
        {
            if (u < 0f || u > 1f || v < 0f || v > 1f) return 0f;
            float x = u * aspect, y = v;
            float slant = 0.12f, glyphW = 0.62f, gap = 0.16f;
            float total = text.Length * glyphW + (text.Length - 1) * gap + slant;
            float scale = Mathf.Min(1f, (aspect - 0.1f) / total);
            float x0 = (aspect - total * scale) * 0.5f, y0 = (1f - scale) * 0.5f;
            x = (x - x0) / scale;
            y = (y - y0) / scale;
            x -= slant * y;
            int idx = Mathf.FloorToInt(x / (glyphW + gap));
            if (idx < 0 || idx >= text.Length) return 0f;
            float gx = (x - idx * (glyphW + gap)) / glyphW;
            if (!Font.TryGetValue(text[idx], out var strokes)) return 0f;
            var p = new Vector2(gx * 0.6f, y);
            foreach (var s in strokes)
                for (int i = 0; i < s.Length - 1; i++)
                    if (SegDist(p, s[i], s[i + 1]) < 0.085f) return 1f;
            return 0f;
        }

        static Vector2[] P(params float[] xy)
        {
            var p = new Vector2[xy.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            return p;
        }

        // Glyphen in einer Box 0.6 breit, 1 hoch (Linienmitten, Strichstaerke kommt dazu)
        static readonly Dictionary<char, Vector2[][]> Font = new Dictionary<char, Vector2[][]>
        {
            ['D'] = new[] { P(0.08f, 0.1f, 0.08f, 0.9f, 0.3f, 0.9f, 0.5f, 0.75f, 0.52f, 0.25f, 0.3f, 0.1f, 0.08f, 0.1f) },
            ['R'] = new[] { P(0.08f, 0.1f, 0.08f, 0.9f, 0.38f, 0.9f, 0.52f, 0.78f, 0.5f, 0.6f, 0.36f, 0.5f, 0.08f, 0.5f), P(0.3f, 0.5f, 0.52f, 0.1f) },
            ['I'] = new[] { P(0.3f, 0.1f, 0.3f, 0.9f), P(0.12f, 0.9f, 0.48f, 0.9f), P(0.12f, 0.1f, 0.48f, 0.1f) },
            ['F'] = new[] { P(0.08f, 0.1f, 0.08f, 0.9f, 0.52f, 0.9f), P(0.08f, 0.52f, 0.42f, 0.52f) },
            ['T'] = new[] { P(0.04f, 0.9f, 0.56f, 0.9f), P(0.3f, 0.9f, 0.3f, 0.1f) },
            ['J'] = new[] { P(0.16f, 0.9f, 0.52f, 0.9f), P(0.4f, 0.9f, 0.4f, 0.28f, 0.28f, 0.1f, 0.14f, 0.12f, 0.06f, 0.26f) },
            ['M'] = new[] { P(0.06f, 0.1f, 0.06f, 0.9f, 0.3f, 0.45f, 0.54f, 0.9f, 0.54f, 0.1f) },
            ['0'] = new[] { P(0.3f, 0.9f, 0.5f, 0.78f, 0.52f, 0.22f, 0.3f, 0.1f, 0.1f, 0.22f, 0.08f, 0.78f, 0.3f, 0.9f) },
            ['1'] = new[] { P(0.14f, 0.72f, 0.34f, 0.9f, 0.34f, 0.1f), P(0.14f, 0.1f, 0.52f, 0.1f) },
            ['3'] = new[] { P(0.08f, 0.82f, 0.28f, 0.9f, 0.48f, 0.82f, 0.5f, 0.62f, 0.28f, 0.52f, 0.5f, 0.42f, 0.52f, 0.2f, 0.3f, 0.1f, 0.08f, 0.18f) },
            ['6'] = new[] { P(0.48f, 0.84f, 0.3f, 0.9f, 0.12f, 0.75f, 0.08f, 0.3f, 0.2f, 0.1f, 0.42f, 0.1f, 0.52f, 0.28f, 0.42f, 0.48f, 0.2f, 0.5f, 0.08f, 0.38f) },
            ['7'] = new[] { P(0.06f, 0.9f, 0.54f, 0.9f, 0.24f, 0.1f) },
            ['2'] = new[] { P(0.08f, 0.78f, 0.24f, 0.9f, 0.44f, 0.88f, 0.52f, 0.7f, 0.44f, 0.52f, 0.08f, 0.1f, 0.54f, 0.1f) },
            ['4'] = new[] { P(0.4f, 0.1f, 0.4f, 0.9f, 0.06f, 0.36f, 0.56f, 0.36f) },
            ['5'] = new[] { P(0.5f, 0.9f, 0.12f, 0.9f, 0.1f, 0.54f, 0.34f, 0.58f, 0.52f, 0.44f, 0.5f, 0.2f, 0.3f, 0.1f, 0.08f, 0.16f) },
            ['9'] = new[] { P(0.5f, 0.6f, 0.38f, 0.5f, 0.18f, 0.52f, 0.08f, 0.7f, 0.18f, 0.88f, 0.38f, 0.9f, 0.52f, 0.72f, 0.48f, 0.3f, 0.3f, 0.1f, 0.12f, 0.16f) },
            ['-'] = new[] { P(0.12f, 0.5f, 0.48f, 0.5f) },
            ['8'] = new[] { P(0.3f, 0.52f, 0.12f, 0.64f, 0.14f, 0.84f, 0.3f, 0.9f, 0.46f, 0.84f, 0.48f, 0.64f, 0.3f, 0.52f, 0.08f, 0.38f, 0.1f, 0.16f, 0.3f, 0.1f, 0.5f, 0.16f, 0.52f, 0.38f, 0.3f, 0.52f) },
        };

        static class Kanji
        {
            // 峠 (Touge, Bergpass): links 山, rechts oben 上, rechts unten 下
            public static readonly Vector2[][] Touge =
            {
                P(0.2f, 0.86f, 0.2f, 0.14f), P(0.07f, 0.6f, 0.07f, 0.14f, 0.33f, 0.14f, 0.33f, 0.6f),
                P(0.66f, 0.95f, 0.66f, 0.57f), P(0.66f, 0.77f, 0.86f, 0.77f), P(0.44f, 0.57f, 0.95f, 0.57f),
                P(0.44f, 0.44f, 0.95f, 0.44f), P(0.66f, 0.44f, 0.66f, 0.03f), P(0.72f, 0.32f, 0.85f, 0.2f),
            };

            // 走 (Hashiri, fahren/rennen)
            public static readonly Vector2[][] Hashiri =
            {
                P(0.26f, 0.83f, 0.74f, 0.83f), P(0.5f, 0.97f, 0.5f, 0.64f), P(0.1f, 0.64f, 0.9f, 0.64f),
                P(0.5f, 0.64f, 0.5f, 0.2f), P(0.5f, 0.42f, 0.8f, 0.42f), P(0.28f, 0.5f, 0.28f, 0.2f),
                P(0.28f, 0.2f, 0.12f, 0.04f), P(0.28f, 0.24f, 0.58f, 0.09f, 0.97f, 0.06f),
            };
        }
    }

    // ====================================================================== Vorlagen

    public static class LiveryPresets
    {
        public static readonly string[] Names = { "LEER", "CREW-STREIFEN", "PANDA", "TOUGE", "RENNSPORT", "FLAMMEN", "SAKURA", "HALFTONE" };

        static WrapLayer L(string sticker, Color c, float x, float y, float size, float stretch = 1f, float rot = 0f, int area = WrapLayer.BothSides, bool mirror = false) =>
            new WrapLayer { sticker = StickerLibrary.Find(sticker), color = c, x = x, y = y, size = size, stretch = stretch, rot = rot, area = area, mirror = mirror };

        public static List<WrapLayer> Make(string name, CarDef def, Color crew, Color paint)
        {
            float len = def.length, h = def.height;
            Color dark = Palette.Hex("1C1A22"), white = Palette.White;
            Color contrast = paint.grayscale > 0.55f ? dark : white;
            var list = new List<WrapLayer>();
            switch (name)
            {
                case "CREW-STREIFEN":
                    list.Add(L("STREIFEN", crew, 0.5f, 0.6f / h, 0.07f, len * 0.86f / (0.07f * 8f)));
                    break;
                case "PANDA": // zweifarbig: unten schwarz, Linie dazwischen
                    list.Add(L("FLAECHE", dark, 0.5f, 0.31f / h, 0.46f, len * 1.04f / 0.46f));
                    list.Add(L("STREIFEN", Palette.Red, 0.5f, 0.555f / h, 0.03f, len * 1.02f / (0.03f * 8f)));
                    list.Add(L("DRIFT", white, 0.64f, 0.36f / h, 0.11f));
                    break;
                case "TOUGE":
                    list.Add(L("TOUGE 峠", contrast, 0.82f, 0.5f, 0.6f, 1f, 0f, WrapLayer.Top));
                    list.Add(L("SPEED-LINES", crew, 0.62f, 0.5f / h, 0.22f, 2.3f));
                    list.Add(L("HASHIRI 走", contrast, 0.2f, 0.62f / h, 0.24f));
                    list.Add(L("JDM", crew, 0.08f, 0.5f, 0.16f, 1f, 0f, WrapLayer.Top));
                    break;
                case "RENNSPORT":
                    list.Add(L("DOPPELSTREIFEN", crew, 0.5f, 0.5f, 0.55f, len * 1.04f / (0.55f * 8f), 0f, WrapLayer.Top));
                    list.Add(L("#86", white, 0.45f, 0.5f / h, 0.42f));
                    list.Add(L("STREIFEN", crew, 0.5f, 0.3f / h, 0.05f, len * 0.9f / 0.4f));
                    list.Add(L("ZIELFLAGGE", contrast, 0.1f, 0.62f / h, 0.14f));
                    break;
                case "FLAMMEN":
                    list.Add(L("FLAMMEN", Palette.Orange, 0.86f, 0.52f / h, 0.4f, 1.1f));
                    list.Add(L("FLAMMEN", Palette.Yellow, 0.87f, 0.52f / h, 0.26f, 1f));
                    list.Add(L("FLAMMEN", Palette.Orange, 0.93f, 0.5f, 0.9f, 0.8f, -90f, WrapLayer.Top));
                    break;
                case "SAKURA":
                    list.Add(L("SAKURA", Palette.Pink, 0.28f, 0.55f / h, 0.34f, 1f, 12f));
                    list.Add(L("SAKURA", Palette.Hex("FFC6DD"), 0.18f, 0.72f / h, 0.2f, 1f, -20f));
                    list.Add(L("SAKURA", Palette.Pink, 0.38f, 0.36f / h, 0.16f, 1f, 40f));
                    list.Add(L("SAKURA", Palette.Hex("FFC6DD"), 0.78f, 0.4f, 0.3f, 1f, 0f, WrapLayer.Top));
                    break;
                case "HALFTONE":
                    list.Add(L("HALFTONE", crew, 0.7f, 0.48f / h, 0.4f, 1.3f));
                    list.Add(L("VERLAUF", crew, 0.85f, 0.5f, 0.6f, 0.9f, 0f, WrapLayer.Top));
                    break;
            }
            return list;
        }
    }

    // ====================================================================== Rendern

    /// <summary>
    /// Zeichnet Lack, Folien-Ebenen und Fugen in eine RenderTexture (GPU, schnell genug fuer Live-Vorschau im Editor).
    /// Aufteilung wie in CarBody: drei Baender (oben/Draufsicht, rechte Seite, linke Seite).
    /// </summary>
    public static class LiveryRenderer
    {
        public const int Size = 1024;
        static Material _mat;

        public static RenderTexture Create()
        {
            var rt = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Livery", useMipMap = true, autoGenerateMips = true, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4
            };
            rt.Create();
            return rt;
        }

        static Material Mat
        {
            get
            {
                if (_mat == null) _mat = new Material(Canvas.GetDefaultCanvasMaterial()) { name = "LiveryBrush" };
                return _mat;
            }
        }

        public static void Render(RenderTexture rt, CarBody body, Color paint, CarDesign design, Texture graffiti)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.PushMatrix();
            GL.LoadOrtho();
            GL.Viewport(new Rect(0, 0, Size, Size));
            GL.Clear(false, true, Lin(paint));

            // Ebenen von unten nach oben, je Band beschnitten (Viewport)
            foreach (var layer in design.wrap)
            {
                var def = StickerLibrary.Get(layer.sticker);
                Texture tex = def.graffiti ? graffiti : StickerLibrary.Texture(layer.sticker);
                if (tex == null) continue;
                if (layer.area == WrapLayer.BothSides || layer.area == WrapLayer.Left) DrawLayer(body, layer, def, tex, WrapLayer.Left);
                if (layer.area == WrapLayer.BothSides || layer.area == WrapLayer.Right) DrawLayer(body, layer, def, tex, WrapLayer.Right);
                if (layer.area == WrapLayer.Top) DrawLayer(body, layer, def, tex, WrapLayer.Top);
            }

            DrawPanelLines(body);
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        /// <summary>Farben fuer GL im aktuellen Farbraum (Linear-Projekt: Umrechnung, die Textur ist sRGB).</summary>
        static Color Lin(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? new Color(c.linear.r, c.linear.g, c.linear.b, c.a) : c;

        static void SetBand(int area)
        {
            float b = area == WrapLayer.Left ? CarBody.BandLeft : area == WrapLayer.Right ? CarBody.BandRight : CarBody.BandTop;
            GL.Viewport(new Rect(0, (b + CarBody.Gutter) * Size, Size, (CarBody.BandH - 2f * CarBody.Gutter) * Size));
        }

        /// <summary>Viewer-Koordinaten (Meter: rechts/hoch, so wie man draufschaut) in Band-Koordinaten (0..1).</summary>
        static Vector2 ToBand(CarBody body, int area, float along, float cross, float dRight, float dUp)
        {
            switch (area)
            {
                case WrapLayer.Left: return new Vector2(1f - (along - dRight / body.L), cross + dUp / body.H);
                case WrapLayer.Right: return new Vector2(along + dRight / body.L, cross + dUp / body.H);
                default: return new Vector2(along + dUp / body.L, cross + dRight / body.W);
            }
        }

        static void DrawLayer(CarBody body, WrapLayer layer, StickerLibrary.Def def, Texture tex, int area)
        {
            SetBand(area);
            var mat = Mat;
            mat.mainTexture = tex;
            mat.SetPass(0);
            bool flipX = layer.mirror ^ (area == WrapLayer.Right && def.directional);
            float rot = (area == WrapLayer.Right && def.directional ? -layer.rot : layer.rot) * Mathf.Deg2Rad;
            float aspect = def.graffiti && tex != null ? (float)tex.width / tex.height : def.aspect;
            float hw = layer.size * layer.stretch * aspect * 0.5f, hh = layer.size * 0.5f;
            float c = Mathf.Cos(rot), s = Mathf.Sin(rot);
            Color col = Lin(def.graffiti ? Color.white : layer.color);
            GL.Begin(GL.QUADS);
            GL.Color(col);
            Vector2[] local = { new Vector2(-hw, -hh), new Vector2(-hw, hh), new Vector2(hw, hh), new Vector2(hw, -hh) };
            Vector2[] uv = { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            for (int i = 0; i < 4; i++)
            {
                Vector2 q = local[i];
                if (flipX) q.x = -q.x;
                float dr = q.x * c - q.y * s, du = q.x * s + q.y * c;
                Vector2 p = ToBand(body, area, layer.x, layer.y, dr, du);
                GL.TexCoord2(uv[i].x, uv[i].y);
                GL.Vertex3(p.x, p.y, 0f);
            }
            GL.End();
        }

        /// <summary>Feine dunkle Fugen: Tueren, Motorhaube, Kofferraum/Heckklappe, Tankdeckel.</summary>
        static void DrawPanelLines(CarBody body)
        {
            var mat = Mat;
            mat.mainTexture = Texture2D.whiteTexture;
            Color line = Lin(new Color(0.05f, 0.04f, 0.08f, 0.45f));
            var s = body.s;
            float wPx = 2.2f / Size; // Strichbreite in Band-u
            foreach (int area in new[] { WrapLayer.Left, WrapLayer.Right })
            {
                SetBand(area);
                mat.SetPass(0);
                GL.Begin(GL.QUADS);
                GL.Color(line);
                if (s.doorLines != null)
                {
                    foreach (float t in s.doorLines)
                    {
                        float along = t + 0.5f;
                        float y0 = (body.BottomY(t) + 0.03f) / body.H, y1 = (body.TopY(t) - 0.01f) / body.H;
                        float u = area == WrapLayer.Left ? 1f - along : along;
                        Quad(u - wPx, y0, u + wPx, y1);
                    }
                }
                GL.End();
            }

            SetBand(WrapLayer.Top);
            mat.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(line);
            float hWide = 2.2f / (Size * CarBody.BandH);
            // Motorhaube: Querfuge vor der Frontscheibe, Laengsfugen zu den Kotfluegeln
            float cowl = s.CowlT + 0.5f - 0.012f, nose = 0.985f;
            float side = 0.5f - 0.36f;
            Quad(cowl - wPx, side, cowl + wPx, 1f - side);
            Quad(cowl, side - hWide, nose, side + hWide);
            Quad(cowl, 1f - side - hWide, nose, 1f - side + hWide);
            // Kofferraum bzw. Heckklappe
            float trunk = s.GlassEndT + 0.5f - (s.hatch ? -0.01f : 0.01f);
            float tail = 0.015f;
            Quad(trunk - wPx, side, trunk + wPx, 1f - side);
            Quad(tail, side - hWide, trunk, side + hWide);
            Quad(tail, 1f - side - hWide, trunk, 1f - side + hWide);
            GL.End();
        }

        static void Quad(float x0, float y0, float x1, float y1)
        {
            GL.TexCoord2(0, 0); GL.Vertex3(x0, y0, 0);
            GL.TexCoord2(0, 1); GL.Vertex3(x0, y1, 0);
            GL.TexCoord2(1, 1); GL.Vertex3(x1, y1, 0);
            GL.TexCoord2(1, 0); GL.Vertex3(x1, y0, 0);
        }
    }
}
