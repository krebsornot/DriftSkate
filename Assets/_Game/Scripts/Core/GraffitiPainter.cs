using UnityEngine;

namespace DriftSkate
{
    /// <summary>Einfache Mal-Funktionen auf einer Texture2D (Pinsel, Spraydose, Radierer).</summary>
    public static class GraffitiPainter
    {
        public static void Clear(Texture2D tex)
        {
            var pixels = new Color32[tex.width * tex.height];
            tex.SetPixels32(pixels);
            tex.Apply();
        }

        /// <summary>Malt einen runden, harten Punkt. erase = transparent machen.</summary>
        public static void Dab(Texture2D tex, Vector2 p, float radius, Color color, bool erase = false)
        {
            int r = Mathf.CeilToInt(radius);
            int cx = Mathf.RoundToInt(p.x), cy = Mathf.RoundToInt(p.y);
            float r2 = radius * radius;
            for (int y = cy - r; y <= cy + r; y++)
            {
                if (y < 0 || y >= tex.height) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || x >= tex.width) continue;
                    float d2 = (x - p.x) * (x - p.x) + (y - p.y) * (y - p.y);
                    if (d2 > r2) continue;
                    tex.SetPixel(x, y, erase ? Color.clear : new Color(color.r, color.g, color.b, 1f));
                }
            }
        }

        /// <summary>Spraydose: verstreute kleine Punkte.</summary>
        public static void Spray(Texture2D tex, Vector2 p, float radius, Color color, int dots = 24)
        {
            for (int i = 0; i < dots; i++)
            {
                Vector2 o = Random.insideUnitCircle * radius;
                Dab(tex, p + o, Mathf.Max(1f, radius * 0.08f), color);
            }
        }

        public static void Line(Texture2D tex, Vector2 a, Vector2 b, float radius, Color color, bool erase = false)
        {
            float dist = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist / Mathf.Max(1f, radius * 0.35f)));
            for (int i = 0; i <= steps; i++) Dab(tex, Vector2.Lerp(a, b, i / (float)steps), radius, color, erase);
        }

        static void Poly(Texture2D tex, Vector2[] pts, float radius, Color color)
        {
            for (int i = 0; i < pts.Length - 1; i++) Line(tex, pts[i], pts[i + 1], radius, color);
        }

        /// <summary>Start-Tag, solange der Spieler noch kein eigenes Graffiti gemalt hat.</summary>
        public static void DrawDefaultTag(Texture2D tex)
        {
            var clear = new Color32[tex.width * tex.height];
            tex.SetPixels32(clear);

            var wave = new[]
            {
                new Vector2(34, 92), new Vector2(70, 168), new Vector2(104, 112), new Vector2(136, 178),
                new Vector2(170, 104), new Vector2(204, 170), new Vector2(226, 120)
            };
            Poly(tex, wave, 20, Palette.Ink);
            Poly(tex, wave, 13, Palette.Pink);
            for (int i = 0; i < wave.Length - 1; i++)
                Line(tex, wave[i] + new Vector2(-3, 5), Vector2.Lerp(wave[i], wave[i + 1], 0.45f) + new Vector2(-3, 5), 3, Palette.Yellow);

            // Tropfen
            foreach (var x in new[] { 70, 136, 204 })
            {
                Line(tex, new Vector2(x, 150), new Vector2(x, 60 + (x % 3) * 12), 6, Palette.Ink);
                Line(tex, new Vector2(x, 150), new Vector2(x, 62 + (x % 3) * 12), 3, Palette.Pink);
            }

            // Stern
            var c = new Vector2(214, 214);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                Vector2 tip = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? 26 : 14);
                Line(tex, c, tip, 6, Palette.Ink);
            }
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                Vector2 tip = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? 22 : 11);
                Line(tex, c, tip, 3, Palette.Yellow);
            }
            tex.Apply();
        }
    }
}
