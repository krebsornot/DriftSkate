using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Masse eines Boards in Metern (Winkel in Grad). Vorlagen orientieren sich an echten Street-Decks:
    /// 8" breit, 31,8" lang, 14,25" Achsabstand, ca. 20 Grad Kicktails, 54-mm-Rollen.
    /// </summary>
    public struct BoardShape
    {
        public float length, width, wheelbase, thickness, concave;
        public float noseKick, tailKick;      // Winkel der Kicktails
        public float noseRound, tailRound;    // Laenge der Rundung an Nose und Tail
        public float nosePower, tailPower;    // Superellipse: 2 = rund, groesser = eckiger, kleiner = spitzer
        public float noseWidth, tailWidth;    // Breitenfaktor zur Nose bzw. zum Tail hin (Cruiser-Formen)
        public float wheelDiameter, wheelWidth, wheelLip, axleWidth, truckHeight, riser;

        public static BoardShape Popsicle(float widthInch = 8f) => new BoardShape
        {
            length = 0.807f, width = widthInch * 0.0254f, wheelbase = 0.362f, thickness = 0.012f, concave = 0.007f,
            noseKick = 20f, tailKick = 21f, noseRound = widthInch * 0.0254f * 0.56f, tailRound = widthInch * 0.0254f * 0.52f,
            nosePower = 2.3f, tailPower = 2.6f, noseWidth = 1f, tailWidth = 1f,
            wheelDiameter = 0.054f, wheelWidth = 0.032f, wheelLip = 0.006f, axleWidth = widthInch * 0.0254f + 0.004f,
            truckHeight = 0.053f, riser = 0f
        };

        /// <summary>Street-Deck: steilere Kicks, tieferes Concave, kleine harte Rollen.</summary>
        public static BoardShape Street
        {
            get
            {
                var s = Popsicle(8.125f);
                s.noseKick = 22f; s.tailKick = 23f; s.concave = 0.009f;
                s.wheelDiameter = 0.052f; s.wheelLip = 0.005f;
                return s;
            }
        }

        /// <summary>Breites Deck (8,5") mit flacheren Kicks, hoeheren Trucks und etwas groesseren Rollen.</summary>
        public static BoardShape Wide
        {
            get
            {
                var s = Popsicle(8.5f);
                s.wheelbase = 0.368f; s.noseKick = 18f; s.tailKick = 19f; s.concave = 0.006f;
                s.wheelDiameter = 0.056f; s.wheelWidth = 0.034f; s.truckHeight = 0.055f;
                return s;
            }
        }

        /// <summary>Breiter Cruiser: spitz zulaufende, flache Nose, eckiger Tail, grosse weiche Rollen, Riser.</summary>
        public static BoardShape Cruiser => new BoardShape
        {
            length = 0.83f, width = 0.24f, wheelbase = 0.40f, thickness = 0.012f, concave = 0.004f,
            noseKick = 6f, tailKick = 17f, noseRound = 0.24f, tailRound = 0.07f, nosePower = 1.7f, tailPower = 4f,
            noseWidth = 1f, tailWidth = 0.86f,
            wheelDiameter = 0.06f, wheelWidth = 0.043f, wheelLip = 0.012f, axleWidth = 0.235f, truckHeight = 0.053f, riser = 0.006f
        };

        public string Key => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0:F3}_{1:F3}_{2:F3}_{3:F3}_{4:F3}_{5:F1}_{6:F1}_{7:F3}_{8:F3}_{9:F2}_{10:F2}_{11:F2}_{12:F2}",
            length, width, wheelbase, thickness, concave, noseKick, tailKick, noseRound, tailRound, nosePower, tailPower, noseWidth, tailWidth);
    }

    /// <summary>Hoehen eines gebauten Boards relativ zum BoardPivot (fuer Fuesse und Grinds).</summary>
    public struct BoardMetrics
    {
        public float deckTop, deckBottom, hangerBottom;
    }

    /// <summary>
    /// Baut ein Skateboard als echte Meshes: Deck mit Concave, Kicktails und gerundeter Kante (Grip oben,
    /// Ply an der Kante, Lack mit Graffiti unten), zwei Trucks (Baseplate, Kingpin, Bushings, Hanger, Achse)
    /// und vier Rollen mit Lagern. Der BoardPivot liegt 10 cm ueber dem Boden, +Z ist die Nose.
    /// </summary>
    public static class BoardBuilder
    {
        public const float Ground = -0.1f;
        const float EdgeRadius = 0.003f;
        const int Rows = 81, Cols = 11, WheelSegments = 28;

        static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        static readonly Color Grip = Palette.Hex("2B2833");
        static readonly Color Ply = Palette.Hex("D9BC92");
        static readonly Color Bearing = Palette.Hex("3B3E4A");
        static readonly Color Bolt = Palette.Hex("4A4C58");

        public static BoardMetrics Measure(BoardShape s)
        {
            float bottom = Ground + s.wheelDiameter * 0.5f + s.truckHeight + s.riser;
            return new BoardMetrics
            {
                deckBottom = bottom,
                deckTop = bottom + s.thickness,
                hangerBottom = Ground + s.wheelDiameter * 0.5f - 0.009f
            };
        }

        /// <summary>Komplettes Board unter 'pivot' bauen.</summary>
        public static BoardMetrics Build(Transform pivot, BoardDef def, Texture graffiti)
        {
            var s = def.shape;
            var m = Measure(s);

            var deck = MeshObject(pivot, "Deck", DeckMesh(s, m.deckTop), Vector3.zero,
                ToonMaterials.Get(Grip, 0f, false),
                ToonMaterials.Get(Ply, 0.2f, false),
                ToonMaterials.Get(def.deck, 0.2f, false));
            deck.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            if (graffiti != null)
            {
                var decal = MeshObject(pivot, "BoardGraffiti", GraphicMesh(s, m.deckTop), Vector3.zero, ToonMaterials.CreateDecal(graffiti));
                decal.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            for (int i = 0; i < 2; i++)
            {
                bool front = i == 0;
                float z = (front ? 1f : -1f) * s.wheelbase * 0.5f;
                BuildTruck(pivot, s, def, m, z, front);
                Bolts(pivot, s, m.deckTop, z);
            }
            return m;
        }

        /// <summary>Nur Trucks und Rollen (fuer eigene Decks ohne Achsen, z. B. THAW-Board-Mods).</summary>
        public static BoardMetrics BuildTrucksOnly(Transform pivot, BoardDef def)
        {
            var s = def.shape;
            var m = Measure(s);
            for (int i = 0; i < 2; i++)
            {
                bool front = i == 0;
                BuildTruck(pivot, s, def, m, (front ? 1f : -1f) * s.wheelbase * 0.5f, front);
            }
            return m;
        }

        // ------------------------------------------------------------------------------------------ Deck

        /// <summary>Mittellinie der Deck-Oberseite bei Bogenlaenge u (Concave nicht eingerechnet).</summary>
        static void Centre(in BoardShape s, float u, float topY, out Vector3 p, out Vector3 n)
        {
            bool nose = u >= 0f;
            float kick = (nose ? s.noseKick : s.tailKick) * Mathf.Deg2Rad;
            float start = s.wheelbase * 0.5f + 0.025f; // der Kick beginnt knapp hinter den Schrauben
            const float r = 0.07f;
            float d = Mathf.Abs(u) - start;
            float z, y, a;
            if (d <= 0f) { z = Mathf.Abs(u); y = 0f; a = 0f; }
            else if (d <= r * kick) { a = d / r; z = start + r * Mathf.Sin(a); y = r * (1f - Mathf.Cos(a)); }
            else
            {
                a = kick;
                float rest = d - r * kick;
                z = start + r * Mathf.Sin(kick) + rest * Mathf.Cos(kick);
                y = r * (1f - Mathf.Cos(kick)) + rest * Mathf.Sin(kick);
            }
            float sign = nose ? 1f : -1f;
            p = new Vector3(0f, topY + y, sign * z);
            n = new Vector3(0f, Mathf.Cos(a), -sign * Mathf.Sin(a));
        }

        static float HalfWidth(in BoardShape s, float u)
        {
            float half = s.length * 0.5f;
            float w = s.width * 0.5f * Mathf.Lerp(s.tailWidth, s.noseWidth, u / s.length + 0.5f);
            bool nose = u >= 0f;
            float round = nose ? s.noseRound : s.tailRound, pw = nose ? s.nosePower : s.tailPower;
            float into = Mathf.Abs(u) - (half - round);
            if (into > 0f)
            {
                float t = Mathf.Clamp01(into / round);
                w *= Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(t, pw)), 1f / pw);
            }
            return w;
        }

        /// <summary>Punkt auf dem Deck: x quer, u entlang (Bogenlaenge), depth = Abstand unter der Oberseite.</summary>
        static Vector3 Surface(in BoardShape s, float topY, float x, float u, float depth)
        {
            Centre(s, u, topY, out var c, out var n);
            float k = x / (s.width * 0.5f);
            return c + Vector3.right * x + n * (s.concave * k * k - depth);
        }

        static float RowU(in BoardShape s, int i) => s.length * 0.5f * Mathf.Sin((i / (float)(Rows - 1) * 2f - 1f) * Mathf.PI * 0.5f);

        /// <summary>Umriss (flach, x/u) eines Rows auf einer Seite, dazu die Normale des Umrisses.</summary>
        static Vector2 Outline(in BoardShape s, int i, float side) => new Vector2(side * HalfWidth(s, RowU(s, i)), RowU(s, i));

        static Vector2 OutlineNormal(in BoardShape s, int i, float side)
        {
            Vector2 a = Outline(s, Mathf.Max(0, i - 1), side), b = Outline(s, Mathf.Min(Rows - 1, i + 1), side);
            Vector2 t = b - a;
            Vector2 m = side * new Vector2(t.y, -t.x);
            return m.sqrMagnitude > 1e-12f ? m.normalized : new Vector2(side, 0f);
        }

        static Vector2 Inset(in BoardShape s, int i, float side)
        {
            Vector2 p = Outline(s, i, side) - OutlineNormal(s, i, side) * EdgeRadius;
            if (p.x * side < 0f) p.x = 0f;
            return p;
        }

        static Vector2 GridPoint(in BoardShape s, int i, int j)
        {
            float v = j / (float)(Cols - 1) * 2f - 1f;
            float side = v < 0f ? -1f : 1f;
            Vector2 e = Inset(s, i, side);
            float t = Mathf.Abs(v);
            float u = RowU(s, i);
            return new Vector2(e.x * t, u + (e.y - u) * t);
        }

        static Mesh DeckMesh(BoardShape s, float topY)
        {
            string key = "BoardDeck_" + s.Key + "_" + topY.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            if (Meshes.TryGetValue(key, out var cached) && cached != null) return cached;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var top = new List<int>();
            var ply = new List<int>();
            var bottom = new List<int>();

            // Ober- und Unterseite als Gitter
            for (int pass = 0; pass < 2; pass++)
            {
                int start = verts.Count;
                float depth = pass == 0 ? 0f : s.thickness;
                for (int i = 0; i < Rows; i++)
                    for (int j = 0; j < Cols; j++)
                    {
                        Vector2 g = GridPoint(s, i, j);
                        verts.Add(Surface(s, topY, g.x, g.y, depth));
                        // Unterseite: Lack/Grafik von unten lesbar (x gespiegelt)
                        uvs.Add(pass == 0 ? new Vector2(g.x / s.width + 0.5f, g.y / s.length + 0.5f)
                                          : new Vector2(0.5f - g.x / s.width, g.y / s.length + 0.5f));
                    }
                for (int i = 0; i < Rows - 1; i++)
                {
                    Centre(s, RowU(s, i), topY, out _, out var n);
                    for (int j = 0; j < Cols - 1; j++)
                    {
                        int a = start + i * Cols + j, b = a + 1, c = a + Cols + 1, d = a + Cols;
                        Quad(pass == 0 ? top : bottom, verts, a, b, c, d, pass == 0 ? n : -n);
                    }
                }
            }

            // Kante: vier Ringe pro Umrisspunkt (oben eingerueckt, aussen oben, aussen unten, unten eingerueckt)
            var loop = new List<(int i, float side)>();
            for (int i = 0; i < Rows; i++) loop.Add((i, 1f));
            for (int i = Rows - 1; i >= 0; i--) loop.Add((i, -1f));
            int ringStart = verts.Count;
            foreach (var (i, side) in loop)
            {
                Vector2 e = Outline(s, i, side), inset = Inset(s, i, side);
                verts.Add(Surface(s, topY, inset.x, inset.y, 0f));
                verts.Add(Surface(s, topY, e.x, e.y, EdgeRadius));
                verts.Add(Surface(s, topY, e.x, e.y, s.thickness - EdgeRadius));
                verts.Add(Surface(s, topY, inset.x, inset.y, s.thickness));
                for (int k = 0; k < 4; k++) uvs.Add(Vector2.zero);
            }
            for (int l = 0; l < loop.Count - 1; l++)
            {
                Vector2 m = OutlineNormal(s, loop[l].i, loop[l].side);
                var outward = new Vector3(m.x, 0f, m.y);
                for (int k = 0; k < 3; k++)
                {
                    int a = ringStart + l * 4 + k, b = a + 1, c = a + 5, d = a + 4;
                    Quad(ply, verts, a, b, c, d, outward);
                }
            }

            var mesh = new Mesh { name = key };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(top, 0);
            mesh.SetTriangles(ply, 1);
            mesh.SetTriangles(bottom, 2);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return Store(key, mesh);
        }

        /// <summary>Graffiti auf der Unterseite, folgt der Woelbung (Mitte des Decks, 55 cm lang).</summary>
        static Mesh GraphicMesh(BoardShape s, float topY)
        {
            string key = "BoardGraphic_" + s.Key + "_" + topY.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            if (Meshes.TryGetValue(key, out var cached) && cached != null) return cached;
            const int rows = 28, cols = 9;
            const float halfLen = 0.275f;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i < rows; i++)
            {
                float u = Mathf.Lerp(-halfLen, halfLen, i / (float)(rows - 1));
                float hw = HalfWidth(s, u) - EdgeRadius * 2f;
                for (int j = 0; j < cols; j++)
                {
                    float x = Mathf.Lerp(-hw, hw, j / (float)(cols - 1));
                    verts.Add(Surface(s, topY, x, u, s.thickness + 0.0008f));
                    uvs.Add(new Vector2(0.5f + x / s.width, 0.5f - u / (halfLen * 2f)));
                }
            }
            for (int i = 0; i < rows - 1; i++)
            {
                Centre(s, Mathf.Lerp(-halfLen, halfLen, i / (float)(rows - 1)), topY, out _, out var n);
                for (int j = 0; j < cols - 1; j++)
                {
                    int a = i * cols + j;
                    Quad(tris, verts, a, a + 1, a + cols + 1, a + cols, -n);
                }
            }
            var mesh = new Mesh { name = key };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return Store(key, mesh);
        }

        // ------------------------------------------------------------------------------------------ Trucks und Rollen

        static void BuildTruck(Transform pivot, in BoardShape s, BoardDef def, BoardMetrics m, float z, bool front)
        {
            var truck = Shapes.Group(pivot, front ? "TruckFront" : "TruckBack", new Vector3(0f, m.deckBottom, z));
            float inner = front ? -1f : 1f; // Richtung Deck-Mitte (dort sitzt der Kingpin)
            Color metal = def.trucks, dark = Palette.Shade(def.trucks, 0.78f);
            float axleY = -(s.riser + s.truckHeight);
            float hz = inner * 0.003f;

            if (s.riser > 0f)
                Shapes.Box(truck, new Vector3(0f, -s.riser * 0.5f, 0f), new Vector3(0.06f, s.riser, 0.07f), Palette.Rubber, 0.08f, name: "Riser");
            Shapes.Box(truck, new Vector3(0f, -s.riser - 0.003f, 0f), new Vector3(0.056f, 0.006f, 0.066f), dark, 0.08f, name: "Baseplate");
            Shapes.Box(truck, new Vector3(0f, -s.riser - 0.012f, inner * 0.008f), new Vector3(0.03f, 0.013f, 0.036f), dark, 0.08f, name: "Body");

            // Kingpin mit zwei Bushings
            float kz = inner * 0.013f;
            float kTop = -s.riser - 0.006f, kBottom = axleY + 0.003f;
            Shapes.Part(PrimitiveType.Cylinder, truck, new Vector3(0f, (kTop + kBottom) * 0.5f, kz), new Vector3(0.0064f, (kTop - kBottom) * 0.5f, 0.0064f),
                Palette.Shade(metal, 1.1f), 0f, name: "Kingpin");
            Shapes.Part(PrimitiveType.Cylinder, truck, new Vector3(0f, -s.riser - 0.0225f, kz), new Vector3(0.022f, 0.0045f, 0.022f),
                def.bushings, 0.06f, name: "Bushing");
            Shapes.Part(PrimitiveType.Cylinder, truck, new Vector3(0f, axleY + 0.011f, kz), new Vector3(0.024f, 0.0045f, 0.024f),
                def.bushings, 0.06f, name: "Bushing");
            Shapes.Part(PrimitiveType.Cylinder, truck, new Vector3(0f, axleY + 0.005f, kz), new Vector3(0.016f, 0.002f, 0.016f),
                dark, 0f, name: "KingpinNut");

            // Hanger und Achse
            float hangerHalf = s.axleWidth * 0.5f - s.wheelWidth - 0.006f;
            MeshObject(truck, "Hanger", HangerMesh(hangerHalf), new Vector3(0f, axleY, hz), ToonMaterials.Get(metal, 0.12f, false));
            Shapes.Part(PrimitiveType.Cylinder, truck, new Vector3(0f, axleY, hz), new Vector3(0.008f, s.axleWidth * 0.5f, 0.008f),
                Palette.Shade(metal, 1.15f), 0f, new Vector3(0f, 0f, 90f), name: "Axle");

            var wheel = WheelMesh(s.wheelDiameter, s.wheelWidth, s.wheelLip);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (s.axleWidth * 0.5f - 0.0065f - s.wheelWidth * 0.5f);
                var w = MeshObject(truck, "Wheel", wheel, new Vector3(x, axleY, hz),
                    ToonMaterials.Get(def.wheels, 0.15f, false), ToonMaterials.Get(Bearing, 0f, false));
                if (side < 0) w.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // Aussenseite nach links
                Shapes.Part(PrimitiveType.Cylinder, truck, new Vector3(side * (s.axleWidth * 0.5f - 0.003f), axleY, hz),
                    new Vector3(0.011f, 0.003f, 0.011f), dark, 0f, new Vector3(0f, 0f, 90f), name: "AxleNut");
            }
        }

        static void Bolts(Transform pivot, in BoardShape s, float topY, float z)
        {
            Centre(s, z, topY, out _, out var n);
            for (int a = -1; a <= 1; a += 2)
                for (int b = -1; b <= 1; b += 2)
                {
                    Vector3 p = Surface(s, topY, a * 0.0206f, z + b * 0.027f, 0f) + n * 0.0004f;
                    Shapes.Part(PrimitiveType.Cylinder, pivot, p, new Vector3(0.0085f, 0.0007f, 0.0085f), Bolt, 0f, name: "Bolt");
                }
        }

        /// <summary>Hanger: abgerundetes Trapez (breit an der Achse, schmal oben am Kingpin), 2,8 cm tief.</summary>
        static Mesh HangerMesh(float halfWidth)
        {
            string key = "BoardHanger_" + halfWidth.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            if (Meshes.TryGetValue(key, out var cached) && cached != null) return cached;
            var outline = new List<Vector2>
            {
                new Vector2(-halfWidth + 0.004f, -0.009f), new Vector2(halfWidth - 0.004f, -0.009f),
                new Vector2(halfWidth, -0.005f), new Vector2(halfWidth, 0.006f), new Vector2(halfWidth - 0.006f, 0.0095f),
                new Vector2(0.022f, 0.022f), new Vector2(0.012f, 0.027f), new Vector2(-0.012f, 0.027f), new Vector2(-0.022f, 0.022f),
                new Vector2(-halfWidth + 0.006f, 0.0095f), new Vector2(-halfWidth, 0.006f), new Vector2(-halfWidth, -0.005f),
            };
            const float depth = 0.028f;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            int n = outline.Count;
            foreach (float z in new[] { depth * 0.5f, -depth * 0.5f })
                foreach (var p in outline) verts.Add(new Vector3(p.x, p.y, z));
            verts.Add(new Vector3(0f, 0.006f, depth * 0.5f));
            verts.Add(new Vector3(0f, 0.006f, -depth * 0.5f));
            int cf = 2 * n, cb = 2 * n + 1;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Tri(tris, verts, cf, i, j, Vector3.forward);
                Tri(tris, verts, cb, n + j, n + i, Vector3.back);
                Vector2 e = outline[j] - outline[i];
                Quad(tris, verts, i, j, n + j, n + i, new Vector3(e.y, -e.x, 0f));
            }
            var mesh = new Mesh { name = key };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return Store(key, mesh);
        }

        /// <summary>Rolle als Drehkoerper um die X-Achse (Aussenseite +X), Lagerschild als zweites Material.</summary>
        static Mesh WheelMesh(float diameter, float width, float lip)
        {
            string key = string.Format(System.Globalization.CultureInfo.InvariantCulture, "BoardWheel_{0:F3}_{1:F3}_{2:F3}", diameter, width, lip);
            if (Meshes.TryGetValue(key, out var cached) && cached != null) return cached;
            float R = diameter * 0.5f, h = width * 0.5f, core = 0.011f;
            var prof = new List<Vector2> { new Vector2(h - 0.0025f, core), new Vector2(h, core + 0.003f) };
            for (int k = 0; k <= 5; k++)
            {
                float a = k / 5f * Mathf.PI * 0.5f;
                prof.Add(new Vector2(h - lip + Mathf.Cos(a) * lip, R - lip + Mathf.Sin(a) * lip));
            }
            for (int k = 0; k <= 5; k++)
            {
                float a = Mathf.PI * 0.5f + k / 5f * Mathf.PI * 0.5f;
                prof.Add(new Vector2(-h + lip + Mathf.Cos(a) * lip, R - lip + Mathf.Sin(a) * lip));
            }
            prof.Add(new Vector2(-h + 0.002f, R * 0.62f));
            prof.Add(new Vector2(-h + 0.0035f, core + 0.002f));
            prof.Add(new Vector2(-h + 0.0025f, core));

            var verts = new List<Vector3>();
            var urethane = new List<int>();
            var bearing = new List<int>();
            for (int k = 0; k < prof.Count; k++)
                for (int a = 0; a < WheelSegments; a++)
                {
                    float ang = a / (float)WheelSegments * Mathf.PI * 2f;
                    verts.Add(new Vector3(prof[k].x, prof[k].y * Mathf.Cos(ang), prof[k].y * Mathf.Sin(ang)));
                }
            for (int k = 0; k < prof.Count - 1; k++)
            {
                Vector2 d = prof[k + 1] - prof[k];
                var n2 = new Vector2(d.y, -d.x);
                for (int a = 0; a < WheelSegments; a++)
                {
                    int b = (a + 1) % WheelSegments;
                    float ang = a / (float)WheelSegments * Mathf.PI * 2f;
                    var expected = new Vector3(n2.x, n2.y * Mathf.Cos(ang), n2.y * Mathf.Sin(ang));
                    Quad(urethane, verts, k * WheelSegments + a, k * WheelSegments + b, (k + 1) * WheelSegments + b, (k + 1) * WheelSegments + a, expected);
                }
            }
            // Lagerschilde auf beiden Seiten
            foreach (float side in new[] { 1f, -1f })
            {
                int start = verts.Count;
                float x = side * (h - 0.0025f);
                for (int a = 0; a < WheelSegments; a++)
                {
                    float ang = a / (float)WheelSegments * Mathf.PI * 2f;
                    verts.Add(new Vector3(x, core * Mathf.Cos(ang), core * Mathf.Sin(ang)));
                    verts.Add(new Vector3(x, 0.0045f * Mathf.Cos(ang), 0.0045f * Mathf.Sin(ang)));
                }
                for (int a = 0; a < WheelSegments; a++)
                {
                    int b = (a + 1) % WheelSegments;
                    Quad(bearing, verts, start + a * 2, start + b * 2, start + b * 2 + 1, start + a * 2 + 1, new Vector3(side, 0f, 0f));
                }
            }
            var mesh = new Mesh { name = key };
            mesh.SetVertices(verts);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(urethane, 0);
            mesh.SetTriangles(bearing, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return Store(key, mesh);
        }

        // ------------------------------------------------------------------------------------------ Helfer

        static Mesh Store(string key, Mesh mesh)
        {
            mesh = MeshStore.Persist(mesh, key);
            Meshes[key] = mesh;
            return mesh;
        }

        static GameObject MeshObject(Transform parent, string name, Mesh mesh, Vector3 localPos, params Material[] mats)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = mats;
            return go;
        }

        /// <summary>Dreieck so anlegen, dass seine Vorderseite in Richtung 'expected' zeigt (Unity: im Uhrzeigersinn).</summary>
        static void Tri(List<int> tris, List<Vector3> v, int a, int b, int c, Vector3 expected)
        {
            Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (n.sqrMagnitude < 1e-14f) return;
            if (Vector3.Dot(n, expected) >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
            else { tris.Add(a); tris.Add(c); tris.Add(b); }
        }

        static void Quad(List<int> tris, List<Vector3> v, int a, int b, int c, int d, Vector3 expected)
        {
            Tri(tris, v, a, b, c, expected);
            Tri(tris, v, a, c, d, expected);
        }
    }
}
