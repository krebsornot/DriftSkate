using UnityEngine;

namespace DriftSkate
{
    public enum HeadStyle { PopUp, Slim, Oval, Square, Angular }
    public enum TailStyle { Bar, Slim, Wide, Block, OvalPair, QuadRound }

    /// <summary>
    /// Karosserie-Form eines Autos (JDM-Vorbilder, ohne Marken). Laengspositionen t laufen von -0.5 (Heck) bis 0.5 (Front),
    /// Hoehen in Metern ueber dem Boden. Die Linien werden weich interpoliert (CarBodyMesh baut daraus das Mesh).
    /// </summary>
    public class CarShape
    {
        /// <summary>Oberkante des Unterbaus: Heck, Kofferraum, Guertellinie unter den Fenstern, Motorhaube, Nase.</summary>
        public Vector2[] top;
        /// <summary>Dachlinie der Kabine vom Ende der Heckscheibe bis zum Fuss der Frontscheibe.</summary>
        public Vector2[] roof;
        /// <summary>Unterkante (Schweller, Front- und Heckschuerze); Radlaeufe werden darueber ausgeschnitten.</summary>
        public Vector2[] bottom;

        public float noseTaper = 0.1f, noseRound = 0.08f;   // Breite an der Spitze (Anteil) und Laenge der Rundung (Anteil von L)
        public float tailTaper = 0.06f, tailRound = 0.06f;
        public float squareness = 4f;                       // Superellipse: 2 = rund, 6 = kantig
        public float flare = 0.015f;                        // Kotfluegel-Ausbeulung ueber den Raedern (m, je Seite)
        public float tumble = 0.07f;                        // Seiten oben eingezogen
        public float glassInset = 0.9f, glassTaper = 0.8f;   // Kabine: Breite unten (Anteil) und oben (Anteil von unten)
        public float sideGlassFront = 0f, sideGlassRear = 0f; // Seitenfenster von/bis (t); 0 = automatisch
        public float bPillar = 0f;                          // B-Saeule (t); 0 = keine
        public float[] doorLines;                           // Tuerfugen (t)
        public HeadStyle head;
        public TailStyle tail;
        public bool hatch;                                  // Heckklappe statt Kofferraum (Fuge oben)

        public float CowlT => roof[roof.Length - 1].x;
        public float GlassEndT => roof[0].x;
        public float RoofFrontT
        {
            get
            {
                float best = roof[0].x, maxY = float.MinValue;
                foreach (var p in roof) if (p.y >= maxY - 0.001f) { maxY = Mathf.Max(maxY, p.y); best = p.x; }
                return best;
            }
        }
        public float RoofRearT
        {
            get
            {
                float maxY = float.MinValue;
                foreach (var p in roof) maxY = Mathf.Max(maxY, p.y);
                foreach (var p in roof) if (p.y >= maxY - 0.001f) return p.x;
                return roof[0].x;
            }
        }

        /// <summary>Formen je Auto-ID (fuer unbekannte IDs das Coupe).</summary>
        public static CarShape For(string id)
        {
            switch (id)
            {
                case "roku86": // AE86-Schraegheck: Keil, Pop-ups, duenne Saeulen, steile Heckklappe
                    return new CarShape
                    {
                        top = V(-0.5f, 0.84f, -0.475f, 0.92f, -0.44f, 0.93f, -0.1f, 0.9f, 0.2f, 0.86f, 0.42f, 0.71f, 0.5f, 0.6f),
                        roof = V(-0.44f, 0.93f, -0.27f, 1.29f, -0.21f, 1.33f, 0.04f, 1.33f, 0.2f, 0.86f),
                        bottom = V(-0.5f, 0.3f, -0.44f, 0.22f, 0.44f, 0.22f, 0.5f, 0.27f),
                        noseTaper = 0.1f, noseRound = 0.07f, tailTaper = 0.04f, tailRound = 0.04f, squareness = 5f, flare = 0.012f,
                        glassTaper = 0.84f, bPillar = -0.06f, doorLines = new[] { 0.13f, -0.08f },
                        head = HeadStyle.PopUp, tail = TailStyle.Bar, hatch = true,
                    };
                case "sylph15": // S15-Coupe: weich, schmale Scheinwerfer, kurzes Heck mit Kofferraum
                    return new CarShape
                    {
                        top = V(-0.5f, 0.82f, -0.475f, 0.94f, -0.33f, 0.96f, -0.1f, 0.92f, 0.16f, 0.86f, 0.4f, 0.74f, 0.5f, 0.62f),
                        roof = V(-0.33f, 0.96f, -0.17f, 1.26f, -0.11f, 1.29f, 0.02f, 1.29f, 0.16f, 0.86f),
                        bottom = V(-0.5f, 0.32f, -0.44f, 0.22f, 0.44f, 0.21f, 0.5f, 0.27f),
                        noseTaper = 0.13f, noseRound = 0.09f, tailTaper = 0.07f, tailRound = 0.06f, squareness = 3.6f, flare = 0.018f,
                        glassTaper = 0.76f, doorLines = new[] { 0.1f, -0.13f },
                        head = HeadStyle.Slim, tail = TailStyle.Slim,
                    };
                case "kazefc": // FC: flache Keilnase mit Pop-ups, lange Haube, grosse gewoelbte Heckscheibe
                    return new CarShape
                    {
                        top = V(-0.5f, 0.84f, -0.47f, 0.9f, -0.4f, 0.91f, -0.1f, 0.88f, 0.12f, 0.84f, 0.38f, 0.69f, 0.5f, 0.55f),
                        roof = V(-0.4f, 0.91f, -0.17f, 1.24f, -0.12f, 1.27f, 0f, 1.27f, 0.12f, 0.84f),
                        bottom = V(-0.5f, 0.3f, -0.44f, 0.21f, 0.44f, 0.2f, 0.5f, 0.25f),
                        noseTaper = 0.15f, noseRound = 0.1f, tailTaper = 0.07f, tailRound = 0.06f, squareness = 3.2f, flare = 0.02f,
                        glassTaper = 0.74f, doorLines = new[] { 0.07f, -0.14f },
                        head = HeadStyle.PopUp, tail = TailStyle.Wide,
                    };
                case "mark2j": // JZX100-Limousine: kastig, vier Tueren, langer Kofferraum
                    return new CarShape
                    {
                        top = V(-0.5f, 0.9f, -0.48f, 0.99f, -0.3f, 1f, -0.05f, 0.97f, 0.19f, 0.93f, 0.44f, 0.84f, 0.5f, 0.72f),
                        roof = V(-0.3f, 1f, -0.19f, 1.36f, -0.15f, 1.4f, 0.07f, 1.4f, 0.19f, 0.93f),
                        bottom = V(-0.5f, 0.32f, -0.45f, 0.23f, 0.45f, 0.23f, 0.5f, 0.3f),
                        noseTaper = 0.06f, noseRound = 0.05f, tailTaper = 0.04f, tailRound = 0.04f, squareness = 5.5f, flare = 0.01f,
                        glassTaper = 0.84f, bPillar = -0.04f, doorLines = new[] { 0.15f, -0.04f, -0.22f },
                        head = HeadStyle.Square, tail = TailStyle.Block,
                    };
                case "toro2j": // A80: rund, bauchige Kotfluegel, ovale Lampen, Buegel-Fluegel
                    return new CarShape
                    {
                        top = V(-0.5f, 0.84f, -0.47f, 0.93f, -0.32f, 0.95f, -0.1f, 0.91f, 0.14f, 0.84f, 0.4f, 0.7f, 0.5f, 0.58f),
                        roof = V(-0.32f, 0.95f, -0.15f, 1.24f, -0.1f, 1.27f, 0.02f, 1.27f, 0.14f, 0.84f),
                        bottom = V(-0.5f, 0.3f, -0.44f, 0.21f, 0.44f, 0.2f, 0.5f, 0.26f),
                        noseTaper = 0.17f, noseRound = 0.11f, tailTaper = 0.1f, tailRound = 0.08f, squareness = 3f, flare = 0.032f,
                        glassTaper = 0.74f, doorLines = new[] { 0.09f, -0.13f },
                        head = HeadStyle.Oval, tail = TailStyle.OvalPair,
                    };
                case "muscle8": // R34-artig: kantig, breit, hohe Guertellinie, vier runde Rueckleuchten
                    return new CarShape
                    {
                        top = V(-0.5f, 0.92f, -0.47f, 1.01f, -0.32f, 1.02f, -0.1f, 0.98f, 0.17f, 0.92f, 0.43f, 0.82f, 0.5f, 0.7f),
                        roof = V(-0.32f, 1.02f, -0.18f, 1.33f, -0.14f, 1.36f, 0.05f, 1.36f, 0.17f, 0.92f),
                        bottom = V(-0.5f, 0.33f, -0.45f, 0.23f, 0.45f, 0.22f, 0.5f, 0.28f),
                        noseTaper = 0.07f, noseRound = 0.06f, tailTaper = 0.05f, tailRound = 0.05f, squareness = 4.8f, flare = 0.035f,
                        glassTaper = 0.8f, doorLines = new[] { 0.12f, -0.14f },
                        head = HeadStyle.Angular, tail = TailStyle.QuadRound,
                    };
                default:
                    return For("sylph15");
            }
        }

        static Vector2[] V(params float[] xy)
        {
            var p = new Vector2[xy.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            return p;
        }

        /// <summary>Monotone kubische Interpolation (kein Ueberschwingen) durch Punkte mit aufsteigendem x.</summary>
        public static float Eval(Vector2[] p, float x)
        {
            int n = p.Length;
            if (x <= p[0].x) return p[0].y;
            if (x >= p[n - 1].x) return p[n - 1].y;
            int i = 0;
            while (i < n - 2 && x > p[i + 1].x) i++;
            float Slope(int k) => (p[k + 1].y - p[k].y) / (p[k + 1].x - p[k].x);
            float Tangent(int k)
            {
                if (k == 0) return Slope(0);
                if (k == n - 1) return Slope(n - 2);
                float a = Slope(k - 1), b = Slope(k);
                if (a * b <= 0f) return 0f;
                return 2f / (1f / a + 1f / b);
            }
            float h = p[i + 1].x - p[i].x, t = (x - p[i].x) / h;
            float t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * p[i].y + (t3 - 2 * t2 + t) * h * Tangent(i)
                 + (-2 * t3 + 3 * t2) * p[i + 1].y + (t3 - t2) * h * Tangent(i + 1);
        }
    }
}
