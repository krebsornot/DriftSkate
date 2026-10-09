using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Erzeugt die fiktive Stadt als 5x5-Raster aus Bloecken, getrennt durch Strassen.
    /// B = Haeuser, P = Platz, X = Brunnenplatz (Mitte), S = Skatepark, D = Drift-Platz, H = Hafen.
    /// Wird vom Editor-Builder einmal ausgefuehrt und als Szene gespeichert; danach frei editierbar.
    /// </summary>
    public partial class CityBuilder
    {
        public const float Block = 70f, Road = 24f;
        public const int Grid = 5;
        public static float Total => Grid * Block + (Grid + 1) * Road;
        public static float Half => Total * 0.5f;

        /// <summary>Gehweg-Rand um Plaetze, Skateparks, Drift-Platz und Hafen: Breite und Bordsteinhoehe.</summary>
        const float Ring = 3f, RingHeight = 0.1f;
        /// <summary>So weit hinter der Bordsteinkante stehen die Laternen auf dem Gehweg.</summary>
        const float LampInset = 1.3f;
        /// <summary>Halbe Breite der Hafenoeffnung (Kaimauer mit Gelaender) an jeder Inselseite.</summary>
        const float QuayOpening = 44f;
        /// <summary>Halbe Breite der Brueckenauffahrt auf der Ostseite.</summary>
        const float BridgeHalfWidth = 11f;

        static readonly string[] Map =
        {
            "BBHBB",
            "BDBSB",
            "HPXPH",
            "BSBDB",
            "BBHBB",
        };

        static readonly Color[] BuildingColors =
        {
            Palette.Hex("F2A65A"), Palette.Hex("6EC6CA"), Palette.Hex("F67280"), Palette.Hex("C06C84"),
            Palette.Hex("A8E6CF"), Palette.Hex("FFB38A"), Palette.Hex("8FB9FF"), Palette.Hex("D4A5FF"),
            Palette.Hex("FFE066"), Palette.Hex("7FD8BE"), Palette.Hex("FF8FAB"), Palette.Hex("9DB4FF"),
        };

        static readonly Color[] ContainerColors =
        {
            Palette.Red, Palette.Blue, Palette.Orange, Palette.Teal, Palette.Yellow, Palette.Purple, Palette.Lime
        };

        readonly System.Random _rng = new System.Random(4242);
        // Eigener Zufall fuer Details (Fassaden, Fenster): so bleibt die Aufteilung der Stadt aus _rng unveraendert
        readonly System.Random _detail = new System.Random(977);
        Transform _root, _rails, _props;
        readonly MeshFactory.BoxBatch _windows = new MeshFactory.BoxBatch();
        readonly MeshFactory.BoxBatch _shops = new MeshFactory.BoxBatch();
        readonly MeshFactory.BoxBatch _doors = new MeshFactory.BoxBatch();
        readonly MeshFactory.BoxBatch _trim = new MeshFactory.BoxBatch();
        readonly MeshFactory.BoxBatch _roadWhite = new MeshFactory.BoxBatch();
        readonly MeshFactory.BoxBatch _roadYellow = new MeshFactory.BoxBatch();
        readonly MeshFactory.BoxBatch _foam = new MeshFactory.BoxBatch();
        int _tagCount;
        int _railLayer;

        float R(float min, float max) => min + (float)_rng.NextDouble() * (max - min);
        int RI(int min, int maxExclusive) => _rng.Next(min, maxExclusive);
        T Pick<T>(T[] arr) => arr[_rng.Next(arr.Length)];

        public static float BlockCenter(int i) => -Half + Road + i * (Block + Road) + Block * 0.5f;
        public static float RoadCenter(int k) => -Half + Road * 0.5f + k * (Block + Road);

        public void Build(Transform root)
        {
            _root = root;
            _railLayer = LayerMask.NameToLayer("Rail");
            if (_railLayer < 0) _railLayer = 0;
            _rails = Shapes.Group(root, "Rails");
            _props = Shapes.Group(root, "Props");

            // Boden (Fahrbahnen)
            var ground = Shapes.Box(root, new Vector3(0, -1f, 0), new Vector3(Total + 1f, 2f, Total + 1f), Palette.Asphalt, 0f, collider: true, name: "Ground", surface: Surface.Asphalt);
            ground.isStatic = true;

            BuildRoadMarkings();

            for (int row = 0; row < Grid; row++)
            {
                for (int col = 0; col < Grid; col++)
                {
                    char type = Map[row][col];
                    var center = new Vector3(BlockCenter(col), 0, BlockCenter(row));
                    var block = Shapes.Group(root, $"Block_{row}{col}_{type}", Vector3.zero);
                    switch (type)
                    {
                        case 'B': BuildBuildings(block, center); break;
                        case 'P': BuildPlaza(block, center, row * Grid + col); break;
                        case 'X': BuildFountainPlaza(block, center); break;
                        case 'S': BuildSkatepark(block, center, row > 2); break;
                        case 'D': BuildDriftLot(block, center, row > 2); break;
                        case 'H': BuildHarbor(block, center); break;
                    }
                }
            }

            BuildPerimeter();
            BuildLamps();
            BuildStreetProps();
            BuildSpawnPoints();
            OptimizeDetails();

            _windows.Build(root, "Windows", ToonMaterials.GetTextured("win_facade", Color.white, 0f, 1.5f), "city_windows");
            _shops.Build(root, "ShopWindows", ToonMaterials.GetTextured("win_shop", Color.white, 0f, 1.3f), "city_shops");
            _doors.Build(root, "Doors", ToonMaterials.GetTextured("win_door", Color.white, 0f, 1.1f), "city_doors");
            _trim.Build(root, "Trim", Palette.Hex("2C3150"), "city_trim");
            _roadWhite.Build(root, "RoadMarkingsWhite", Palette.White, "city_road_white");
            _roadYellow.Build(root, "RoadMarkingsYellow", Palette.Yellow, "city_road_yellow", 0.3f);
            _foam.Build(root, "Foam", Palette.White, "city_foam", 0.4f);

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponentInParent<Rigidbody>() == null && t.GetComponentInParent<TagSpot>() == null)
                    t.gameObject.isStatic = true;
            }
        }

        // ------------------------------------------------------------------ Strassen

        void BuildRoadMarkings()
        {
            float half = Half;
            for (int k = 0; k <= Grid; k++)
            {
                float c = RoadCenter(k);
                for (float p = -half + 4f; p < half - 4f; p += 10f)
                {
                    if (IsIntersection(p)) continue;
                    _roadWhite.Add(new Vector3(p, 0.012f, c), new Vector3(4.5f, 0.02f, 0.3f));
                    _roadWhite.Add(new Vector3(c, 0.012f, p), new Vector3(0.3f, 0.02f, 4.5f));
                }
                // Haltelinien / Zebrastreifen an Kreuzungen
                for (int j = 0; j <= Grid; j++)
                {
                    float other = RoadCenter(j);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        for (int z = -4; z <= 4; z++)
                        {
                            _roadWhite.Add(new Vector3(other + s * (Road * 0.5f + 2.2f), 0.012f, c + z * 2.4f), new Vector3(2.6f, 0.02f, 1.1f));
                            _roadWhite.Add(new Vector3(c + z * 2.4f, 0.012f, other + s * (Road * 0.5f + 2.2f)), new Vector3(1.1f, 0.02f, 2.6f));
                        }
                    }
                }
            }
        }

        bool IsIntersection(float p)
        {
            for (int k = 0; k <= Grid; k++)
                if (Mathf.Abs(p - RoadCenter(k)) < Road * 0.5f + 5f) return true;
            return false;
        }

        // ------------------------------------------------------------------ Haeuserblock

        void BuildBuildings(Transform block, Vector3 c)
        {
            Shapes.Box(block, c + new Vector3(0, 0.09f, 0), new Vector3(Block, 0.18f, Block), Palette.Sidewalk, 0.15f, collider: true, name: "Sidewalk", surface: Surface.Paving);
            float inner = Block * 0.5f - 5f;
            var rects = new List<Rect>();
            int layout = RI(0, 3);
            if (layout == 0) rects.Add(new Rect(-inner, -inner, inner * 2, inner * 2));
            else if (layout == 1)
            {
                float split = R(-8f, 8f);
                rects.Add(new Rect(-inner, -inner, inner + split - 3f, inner * 2));
                rects.Add(new Rect(split + 3f, -inner, inner - split - 3f, inner * 2));
            }
            else
            {
                rects.Add(new Rect(-inner, -inner, inner - 3f, inner - 3f));
                rects.Add(new Rect(3f, -inner, inner - 3f, inner - 3f));
                rects.Add(new Rect(-inner, 3f, inner - 3f, inner - 3f));
                rects.Add(new Rect(3f, 3f, inner - 3f, inner - 3f));
            }

            foreach (var r in rects)
            {
                Vector3 groundCenter = c + new Vector3(r.center.x, 0f, r.center.y);
                Building(block, groundCenter, r.width, r.height, R(12f, 42f), OutwardAxis(r), 0.18f, 0.6f);
            }
        }

        /// <summary>Ein Haus mit Fensterreihen, Laden mit Markise zur Strasse, Dach-Deko und evtl. Graffiti-Spot.</summary>
        void Building(Transform parent, Vector3 groundCenter, float sx, float sz, float h, Vector3 street, float baseY, float tagChance)
        {
            Color col = Pick(BuildingColors);
            Vector3 center = groundCenter + Vector3.up * (baseY + h * 0.5f);
            Surface wall = _detail.NextDouble() < 0.35 ? Surface.Brick : Surface.Stucco;
            Shapes.Box(parent, center, new Vector3(sx, h, sz), col, 0.5f, collider: true, name: "Building", surface: wall);
            Shapes.Box(parent, center + Vector3.up * (h * 0.5f + 0.25f), new Vector3(sx + 0.6f, 0.5f, sz + 0.6f), Palette.Shade(col, 0.75f), 0.4f, name: "Roof", surface: Surface.Roof);

            for (float y = 4.5f; y < h - 2f; y += 3.6f)
            {
                float wy = baseY + y;
                WindowBand(new Vector3(center.x, wy, center.z + sz * 0.5f + 0.06f), new Vector3(sx * 0.84f, 1.3f, 0.12f));
                WindowBand(new Vector3(center.x, wy, center.z - sz * 0.5f - 0.06f), new Vector3(sx * 0.84f, 1.3f, 0.12f));
                WindowBand(new Vector3(center.x + sx * 0.5f + 0.06f, wy, center.z), new Vector3(0.12f, 1.3f, sz * 0.84f));
                WindowBand(new Vector3(center.x - sx * 0.5f - 0.06f, wy, center.z), new Vector3(0.12f, 1.3f, sz * 0.84f));
            }

            Vector3 face = new Vector3(center.x, 0, center.z) + Vector3.Scale(street, new Vector3(sx * 0.5f, 0, sz * 0.5f));
            Vector3 along = Vector3.Cross(Vector3.up, street);
            float faceLen = Mathf.Abs(Vector3.Dot(along, new Vector3(sx, 0, sz)));
            Color awning = Pick(new[] { Palette.Pink, Palette.Yellow, Palette.Cyan, Palette.Lime, Palette.Orange });
            // Laden im Erdgeschoss auf der linken Seite (rechts sitzt ggf. der Graffiti-Spot), Markise genau darueber
            float shopA = -0.45f * faceLen, shopB = 0.12f * faceLen;
            Shapes.Box(parent, face + street * 0.8f + along * ((shopA + shopB) * 0.5f) + Vector3.up * (baseY + 3.2f),
                Abs(along * (shopB - shopA + 0.6f) + street * 1.6f + Vector3.up * 0.3f), awning, 0.3f, name: "Awning");
            Shopfront(face, street, along, shopA, shopB, baseY);
            if (_rng.NextDouble() < tagChance && faceLen > 12f)
                CreateTagSpot(parent, face + street * 0.05f + along * Mathf.Min(faceLen * 0.38f, faceLen * 0.5f - 2.4f) + Vector3.up * (baseY + 1.85f), street);

            Vector3 roofTop = center + Vector3.up * (h * 0.5f + 0.5f);
            if (_rng.NextDouble() < 0.5)
                Shapes.Part(PrimitiveType.Cylinder, parent, roofTop + new Vector3(R(-3, 3), 2.2f, R(-3, 3)), new Vector3(3f, 1.7f, 3f), Palette.Hex("A9744F"), 0.4f, name: "WaterTank");
            for (int i = 0; i < RI(1, 4); i++)
                Shapes.Box(parent, roofTop + new Vector3(R(-sx * 0.35f, sx * 0.35f), 0.6f, R(-sz * 0.35f, sz * 0.35f)), new Vector3(2f, 1.2f, 1.6f), Palette.Concrete, 0.3f, name: "AC");
            if (_rng.NextDouble() < 0.35)
            {
                Color b = Pick(new[] { Palette.Pink, Palette.Cyan, Palette.Yellow, Palette.Lime });
                Vector3 bp = roofTop + Vector3.up * 5f;
                float bw = Mathf.Min(14f, faceLen * 0.8f);
                Shapes.Box(parent, bp + street * 0.1f, Abs(along * bw + street * 0.4f + Vector3.up * 6f), b, 0.6f, emission: 0.7f, name: "Billboard");
                for (int s = -1; s <= 1; s += 2)
                    Shapes.Box(parent, bp - Vector3.up * 3.75f + along * (s * bw * 0.3f), new Vector3(0.4f, 3.5f, 0.4f), Palette.Metal, 0.2f, name: "BillboardPole");
            }
        }

        /// <summary>Eine Fensterreihe: einzelne Fenster mit Rahmen (Textur-Atlas), je Reihe zufaellig verschoben.</summary>
        void WindowBand(Vector3 center, Vector3 size)
        {
            float len = Mathf.Max(size.x, size.z);
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 2.6f));
            int u0 = _detail.Next(8);
            _windows.Add(center, size, 0f, new Vector4(u0 / 8f, (u0 + n) / 8f, 0f, 1f));
        }

        /// <summary>Ladenfront entlang der Fassade von a bis b: zwei Schaufenster mit Tuer in der Mitte.</summary>
        void Shopfront(Vector3 face, Vector3 street, Vector3 along, float a, float b, float baseY)
        {
            if (b - a < 4f) return;
            float door = (a + b) * 0.5f, y0 = baseY + 0.3f, y1 = baseY + 2.8f;
            ShopPane(face, street, along, a, door - 0.85f, y0, y1);
            ShopPane(face, street, along, door + 0.85f, b, y0, y1);
            _doors.Add(face + street * 0.06f + along * door + Vector3.up * (baseY + 1.2f), Abs(along * 1.5f + street * 0.12f + Vector3.up * 2.4f), 0f, new Vector4(0f, 1f, 0f, 1f));
        }

        void ShopPane(Vector3 face, Vector3 street, Vector3 along, float a, float b, float y0, float y1)
        {
            float len = b - a;
            if (len < 1.2f) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 2.4f));
            Vector3 c = face + street * 0.06f + along * ((a + b) * 0.5f) + Vector3.up * ((y0 + y1) * 0.5f);
            _shops.Add(c, Abs(along * len + street * 0.12f + Vector3.up * (y1 - y0)), 0f, new Vector4(0f, n, 0f, 1f));
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>Die Achse, in der ein Haus am weitesten zum Blockrand zeigt.</summary>
        static Vector3 OutwardAxis(Rect r)
        {
            Vector2 c = r.center;
            if (Mathf.Abs(c.x) > Mathf.Abs(c.y)) return new Vector3(Mathf.Sign(c.x), 0, 0);
            if (Mathf.Abs(c.y) > 0.01f) return new Vector3(0, 0, Mathf.Sign(c.y));
            return Vector3.forward;
        }

        // ------------------------------------------------------------------ Plaetze

        /// <summary>Boden eines offenen Blocks (Platz, Skatepark, Hafen) innerhalb des Gehweg-Rands.</summary>
        void Floor(Transform block, Vector3 c, Color color, Surface surface, float height = 0.06f)
        {
            float inner = Block - 2f * Ring;
            Shapes.Box(block, c + new Vector3(0, height * 0.5f, 0), new Vector3(inner, height, inner), color, 0f, collider: true, name: "Floor", surface: surface);
            SidewalkRing(block, c);
        }

        /// <summary>Gehweg rundum (3 m, Bordstein 10 cm): trennt den Block von der Strasse, darauf stehen die Laternen.</summary>
        void SidewalkRing(Transform block, Vector3 c)
        {
            float half = Block * 0.5f, y = RingHeight * 0.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                Shapes.Box(block, c + new Vector3(0, y, s * (half - Ring * 0.5f)), new Vector3(Block, RingHeight, Ring), Palette.Sidewalk, 0.15f, collider: true, name: "Sidewalk", surface: Surface.Paving);
                Shapes.Box(block, c + new Vector3(s * (half - Ring * 0.5f), y, 0), new Vector3(Ring, RingHeight, Block - 2f * Ring), Palette.Sidewalk, 0.15f, collider: true, name: "Sidewalk", surface: Surface.Paving);
            }
        }

        void BuildPlaza(Transform block, Vector3 c, int seed)
        {
            Floor(block, c, Palette.Concrete, Surface.PlazaTiles);
            float flip = seed % 2 == 0 ? 1f : -1f;

            // Treppe mit Handlaeufen und Plattform
            Vector3 stairBase = c + new Vector3(-14f * flip, 0, -10f);
            float stepH = 0.25f, stepD = 0.7f, width = 8f;
            for (int i = 0; i < 4; i++)
                Shapes.Box(block, stairBase + new Vector3(0, stepH * (i + 1) * 0.5f, -i * stepD), new Vector3(width, stepH * (i + 1), stepD), Palette.Hex("B7AFC8"), 0.25f, collider: true, name: "Step", surface: Surface.Concrete);
            Vector3 platform = stairBase + new Vector3(0, 0, -4 * stepD - 4f + stepD * 0.5f);
            Shapes.Box(block, platform + new Vector3(0, 0.5f, 0), new Vector3(width, 1f, 8f), Palette.Hex("B7AFC8"), 0.3f, collider: true, name: "Platform", surface: Surface.Concrete);
            var ramp = Shapes.Part(PrimitiveType.Cube, block, platform + new Vector3(0, 0.5f, -4f - 2.5f), Vector3.one, Palette.Hex("C9C3D6"), 0.3f, keepCollider: false, name: "PlatformRamp", surface: Surface.Concrete);
            MakeMesh(ramp, MeshFactory.Wedge(width, 1f, 5f), true);
            ramp.transform.localRotation = Quaternion.Euler(0, 180f, 0);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 top = platform + new Vector3(s * (width * 0.5f - 0.3f), 1f + 0.85f, 4f);
                Vector3 bottom = stairBase + new Vector3(s * (width * 0.5f - 0.3f), 0.85f, stepD * 0.5f + 0.6f);
                CreateRail(top, bottom, true, Palette.Yellow, "HANDRAIL");
            }

            // Ledges und Baenke
            CreateLedge(block, c + new Vector3(12f * flip, 0, 14f), new Vector3(10f, 0.55f, 1.2f), Palette.Hex("8E86A8"));
            CreateLedge(block, c + new Vector3(12f * flip, 0, 4f), new Vector3(10f, 0.45f, 1.2f), Palette.Hex("8E86A8"));
            CreateLedge(block, c + new Vector3(-4f * flip, 0, 22f), new Vector3(1.2f, 0.5f, 12f), Palette.Pink);
            CreateLedge(block, c + new Vector3(22f * flip, 0, -16f), new Vector3(1.2f, 0.4f, 14f), Palette.Cyan);
            // Manual-Pad
            Shapes.Box(block, c + new Vector3(2f * flip, 0.12f, -22f), new Vector3(8f, 0.24f, 3f), Palette.Lime, 0.3f, collider: true, name: "ManualPad", surface: Surface.Concrete);
            // Flachrail
            CreateRail(c + new Vector3(-24f * flip, 0.55f, 6f), c + new Vector3(-24f * flip, 0.55f, 20f), true, Palette.Pink, "RAIL");

            // Baeume in Pflanzkuebeln
            for (int i = 0; i < 4; i++)
            {
                var p = c + new Vector3((i % 2 == 0 ? -1 : 1) * 27f, 0, (i < 2 ? -1 : 1) * 27f);
                Planter(block, p);
            }
            // Graffiti-Wand neben der Treppe (zeigt zur Platzmitte, frei von Rampe und Rails)
            if (_rng.NextDouble() < 0.8) CreateTagSpot(block, c + new Vector3(-26f * flip, 2.6f, -14f), new Vector3(flip, 0, 0), standalone: true);

            // Hecken, Baeume und Baenke am Rand (nur wo nichts steht)
            PlazaEdges(block, c);
        }

        void Planter(Transform block, Vector3 p)
        {
            CreateLedge(block, p, new Vector3(4f, 0.6f, 4f), Palette.Hex("9E8FB8"), false);
            Shapes.Box(block, p + new Vector3(0, 0.61f, 0), new Vector3(3.4f, 0.02f, 3.4f), Palette.Hex("6B4A3A"), 0f, name: "Soil");
            Tree(block, p + new Vector3(0, 0.6f, 0), G(1.0f, 1.15f));
            for (int i = 0; i < 4; i++)
                Bush(block, p + new Vector3((i % 2 == 0 ? -1f : 1f) * 1.15f, 0.6f, (i < 2 ? -1f : 1f) * 1.15f), G(0.7f, 0.95f), i % 2 == 0);
        }

        void BuildFountainPlaza(Transform block, Vector3 c)
        {
            Floor(block, c, Palette.Concrete, Surface.PlazaTiles);
            Shapes.Part(PrimitiveType.Cylinder, block, c + new Vector3(0, 0.07f, 0), new Vector3(30f, 0.01f, 30f), Palette.Pink, 0f, name: "PlazaCircle");
            Shapes.Part(PrimitiveType.Cylinder, block, c + new Vector3(0, 0.08f, 0), new Vector3(26f, 0.01f, 26f), Palette.Concrete, 0f, name: "PlazaCircleInner", surface: Surface.PlazaTiles);
            // Brunnen
            var basin = Shapes.Part(PrimitiveType.Cylinder, block, c + new Vector3(0, 0.35f, 0), new Vector3(12f, 0.35f, 12f), Palette.White, 0.5f, keepCollider: false, name: "FountainBasin", surface: Surface.Concrete);
            var mc = basin.AddComponent<MeshCollider>();
            mc.sharedMesh = basin.GetComponent<MeshFilter>().sharedMesh;
            Shapes.Part(PrimitiveType.Cylinder, block, c + new Vector3(0, 0.66f, 0), new Vector3(10.6f, 0.02f, 10.6f), Palette.Cyan, 0f, emission: 0.4f, name: "Water", surface: Surface.Water);
            Shapes.Part(PrimitiveType.Cylinder, block, c + new Vector3(0, 2f, 0), new Vector3(1.4f, 2f, 1.4f), Palette.Yellow, 0.5f, keepCollider: true, name: "FountainPillar");
            FountainTiers(block, c);

            // Rund-Grind auf dem Brunnenrand
            var pts = new List<Vector3>();
            for (int i = 0; i <= 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                pts.Add(c + new Vector3(Mathf.Cos(a) * 5.85f, 0.72f, Mathf.Sin(a) * 5.85f));
            }
            CreateRailPath(pts, "FOUNTAIN GRIND");

            // Kicker zum Brunnen und Rails
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + 45f;
                Vector3 dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                var k = new GameObject("Kicker");
                k.transform.SetParent(block, false);
                k.transform.position = c + dir * 22f + Vector3.up * 0.35f;
                k.transform.rotation = Quaternion.LookRotation(dir);
                MakeMesh(k, MeshFactory.Wedge(3f, 0.7f, 2.4f), true, Palette.Yellow, surface: Surface.Concrete);
            }
            CreateRail(c + new Vector3(-16f, 0.5f, -30f), c + new Vector3(16f, 0.5f, -30f), true, Palette.Cyan, "LONG RAIL");

            // Baum-Inseln in den Ecken, Hecken und Baenke am Rand (Mitte bleibt frei zum Driften)
            for (int i = 0; i < 4; i++)
                TreeIsland(block, c + new Vector3((i % 2 == 0 ? -1f : 1f) * 26f, 0f, (i < 2 ? -1f : 1f) * 26f));
            PlazaEdges(block, c);

            var zone = new GameObject("DriftZone_Fountain");
            zone.transform.SetParent(block, false);
            zone.transform.position = c;
            var dz = zone.AddComponent<DriftZone>();
            dz.size = new Vector3(56f, 5f, 56f);
            dz.multiplier = 1.3f;

            CreateTagSpot(block, c + new Vector3(0, 3.4f, 0.75f), Vector3.forward, standalone: false, size: new Vector2(1.6f, 1.6f));
        }

        // ------------------------------------------------------------------ Skatepark

        void BuildSkatepark(Transform block, Vector3 c, bool flipped)
        {
            Floor(block, c, Palette.Hex("D8D2E6"), Surface.Concrete);
            float f = flipped ? -1f : 1f;

            // Quarterpipes an den Enden (Fahrflaeche zeigt zur Mitte), Rueckseite direkt am Gehweg-Rand
            for (int s = -1; s <= 1; s += 2)
            {
                var qp = new GameObject("QuarterPipe");
                qp.transform.SetParent(block, false);
                float radius = 3.2f, deck = 2.5f;
                qp.transform.position = c + new Vector3(0, radius * 0.5f, s * (Block * 0.5f - Ring - (radius + deck) * 0.5f - 0.2f));
                qp.transform.rotation = Quaternion.Euler(0, s > 0 ? 180f : 0f, 0);
                MakeMesh(qp, MeshFactory.QuarterPipe(22f, radius, deck), true, Palette.Hex("A4C8FF"), boxOutline: false, surface: Surface.Concrete);
                // Coping (Kante oben) zum Grinden; fest ist die Quarterpipe selbst
                Vector3 lip = qp.transform.TransformPoint(new Vector3(0, radius * 0.5f, (radius + deck) * 0.5f - radius));
                CreateRail(lip + new Vector3(-10.5f, 0.05f, 0), lip + new Vector3(10.5f, 0.05f, 0), false, Palette.Metal, "COPING", solid: false);
            }

            // Funbox: Auffahrten vorne und hinten, Rail laengs auf einer Seite, Seitenkanten grindbar
            Vector3 fb = c;
            CreateLedge(block, fb, new Vector3(8f, 1f, 6f), Palette.Hex("FFB8D9"), false);
            for (int s = -1; s <= 1; s += 2)
            {
                var w = new GameObject("FunboxRamp");
                w.transform.SetParent(block, false);
                w.transform.position = fb + new Vector3(0, 0.5f, s * (3f + 1.75f));
                w.transform.rotation = Quaternion.Euler(0, s > 0 ? 0f : 180f, 0);
                MakeMesh(w, MeshFactory.Wedge(8f, 1f, 3.5f), true, Palette.Hex("FFB8D9"), surface: Surface.Concrete);
            }
            CreateRail(fb + new Vector3(2f * f, 1.45f, -2.7f), fb + new Vector3(2f * f, 1.45f, 2.7f), true, Palette.Yellow, "FUNBOX RAIL");
            for (int s = -1; s <= 1; s += 2)
                CreateRailPath(new List<Vector3> { fb + new Vector3(s * 3.85f, 1.02f, -2.9f), fb + new Vector3(s * 3.85f, 1.02f, 2.9f) }, "FUNBOX LEDGE");

            // Pyramide: Plateau 5 x 5 m, ringsum 3,5 m Schraege (auch ueber die Ecken)
            Vector3 py = c + new Vector3(-20f * f, 0, 6f);
            var pyramid = new GameObject("Pyramid");
            pyramid.transform.SetParent(block, false);
            pyramid.transform.position = py + Vector3.up * 0.6f;
            MakeMesh(pyramid, MeshFactory.Frustum(12f, 12f, 5f, 5f, 1.2f), true, Palette.Lime, surface: Surface.Concrete);

            // Kicker und Flachrails
            for (int i = 0; i < 2; i++)
            {
                var k = new GameObject("Kicker");
                k.transform.SetParent(block, false);
                k.transform.position = c + new Vector3((14f + i * 8f) * f, 0.4f, -8f + i * 4f);
                k.transform.rotation = Quaternion.Euler(0, i == 0 ? 0f : 180f, 0);
                MakeMesh(k, MeshFactory.Wedge(3f, 0.8f, 2.6f), true, Palette.Yellow, surface: Surface.Concrete);
            }
            CreateRail(c + new Vector3(24f * f, 0.5f, -14f), c + new Vector3(24f * f, 0.5f, 6f), true, Palette.Pink, "RAIL");
            CreateRail(c + new Vector3(-26f * f, 0.65f, -16f), c + new Vector3(-14f * f, 0.65f, -16f), true, Palette.Cyan, "RAIL");
            CreateLedge(block, c + new Vector3(-24f * f, 0, -6f), new Vector3(1.2f, 0.5f, 10f), Palette.Orange);

            CreateTagSpot(block, c + new Vector3(31f * f, 2.2f, 12f), new Vector3(-f, 0, 0), standalone: true);
        }

        // ------------------------------------------------------------------ Drift-Platz

        void BuildDriftLot(Transform block, Vector3 c, bool flipped)
        {
            float inner = Block - 2f * Ring;
            Shapes.Box(block, c + new Vector3(0, 0.02f, 0), new Vector3(inner, 0.04f, inner), Palette.AsphaltDark, 0f, collider: true, name: "DriftLot", surface: Surface.DriftAsphalt);
            SidewalkRing(block, c);
            float f = flipped ? -1f : 1f;

            // Parkplatz-Linien am Rand
            for (int i = -6; i <= 6; i++)
                _roadWhite.Add(c + new Vector3(i * 5f, 0.045f, 27.5f), new Vector3(0.2f, 0.02f, 5f));

            // Drei Zonen mit gelben Rahmen
            var zones = new[]
            {
                (pos: new Vector3(-18f * f, 0, -14f), size: new Vector2(16f, 16f), mult: 1.5f),
                (pos: new Vector3(18f * f, 0, -14f), size: new Vector2(16f, 16f), mult: 1.8f),
                (pos: new Vector3(0, 0, 12f), size: new Vector2(24f, 14f), mult: 2.0f),
            };
            int idx = 0;
            foreach (var z in zones)
            {
                Vector3 p = c + z.pos;
                var go = new GameObject("DriftZone_" + (++idx));
                go.transform.SetParent(block, false);
                go.transform.position = p;
                var dz = go.AddComponent<DriftZone>();
                dz.size = new Vector3(z.size.x, 5f, z.size.y);
                dz.multiplier = z.mult;
                float hx = z.size.x * 0.5f, hz = z.size.y * 0.5f;
                _roadYellow.Add(p + new Vector3(0, 0.05f, hz), new Vector3(z.size.x, 0.02f, 0.5f));
                _roadYellow.Add(p + new Vector3(0, 0.05f, -hz), new Vector3(z.size.x, 0.02f, 0.5f));
                _roadYellow.Add(p + new Vector3(hx, 0.05f, 0), new Vector3(0.5f, 0.02f, z.size.y));
                _roadYellow.Add(p + new Vector3(-hx, 0.05f, 0), new Vector3(0.5f, 0.02f, z.size.y));
                // Clipping-Pfosten
                Shapes.Part(PrimitiveType.Cylinder, block, p + new Vector3(0, 0.9f, 0), new Vector3(0.5f, 0.9f, 0.5f), Palette.Yellow, 0.4f, keepCollider: true, emission: 0.2f, name: "ClipPost");
            }

            // Reifenstapel als Wand (gibt Naehe-Bonus)
            for (int i = 0; i < 14; i++)
            {
                for (int h = 0; h < 2; h++)
                {
                    Vector3 p = c + new Vector3(-30f + i * 4.6f, 0.3f + h * 0.55f, -30f);
                    Shapes.Part(PrimitiveType.Cylinder, block, p, new Vector3(1.1f, 0.27f, 1.1f), i % 2 == 0 ? Palette.Rubber : Palette.Pink, 0.2f, keepCollider: true, name: "TireWall");
                }
            }

            // Pylonen (leicht, umfahrbar)
            for (int i = 0; i < 10; i++)
            {
                Vector3 p = c + new Vector3(R(-26f, 26f), 0.4f, R(-4f, 4f) - 2f);
                var cone = Shapes.Part(PrimitiveType.Cylinder, _props, p, new Vector3(0.35f, 0.4f, 0.35f), Palette.Orange, 0.3f, keepCollider: true, name: "Cone");
                var rb = cone.AddComponent<Rigidbody>();
                rb.mass = 4f;
            }

            // Schild am Rand, Schrift auf beiden Seiten (zum Platz und zur Strasse)
            var sign = new GameObject("DriftSign");
            sign.transform.SetParent(block, false);
            sign.transform.position = c + new Vector3(0, 7f, 34f);
            Shapes.Box(sign.transform, Vector3.zero, new Vector3(22f, 5f, 0.5f), Palette.Pink, 0.6f, emission: 0.55f, name: "SignBoard");
            Shapes.Box(sign.transform, new Vector3(-8f, -4.5f, 0), new Vector3(0.5f, 5f, 0.5f), Palette.Metal, 0.3f, collider: true, name: "Pole");
            Shapes.Box(sign.transform, new Vector3(8f, -4.5f, 0), new Vector3(0.5f, 5f, 0.5f), Palette.Metal, 0.3f, collider: true, name: "Pole");
            // Schriftzug als Aufkleber auf beiden Seiten (zum Platz und zur Strasse); jede Seite nur von vorn sichtbar
            var lettering = ToonMaterials.GetDecal("sign_driftzone", 0.6f);
            Shapes.Decal(sign.transform, new Vector3(0, 0, -0.27f), new Vector2(20f, 5f), Vector3.back, lettering, "SignText");
            Shapes.Decal(sign.transform, new Vector3(0, 0, 0.27f), new Vector2(20f, 5f), Vector3.forward, lettering, "SignText");
        }

        // ------------------------------------------------------------------ Hafen

        void BuildHarbor(Transform block, Vector3 c)
        {
            Floor(block, c, Palette.Hex("B1B9C9"), Surface.Concrete);
            // Containerreihen mit Gassen dazwischen
            for (int row = 0; row < 3; row++)
            {
                float z = c.z - 22f + row * 22f;
                float x = c.x - 28f;
                while (x < c.x + 26f)
                {
                    bool longOne = _rng.NextDouble() < 0.5;
                    float len = longOne ? 12f : 6f;
                    if (x + len > c.x + 30f) break;
                    int stack = _rng.NextDouble() < 0.4 ? 2 : 1;
                    for (int s = 0; s < stack; s++)
                    {
                        Color col = Pick(ContainerColors);
                        Shapes.Box(block, new Vector3(x + len * 0.5f, 1.3f + s * 2.6f, z), new Vector3(len, 2.6f, 2.5f), col, 0.4f, collider: true, name: "Container", surface: Surface.Container);
                        _trim.Add(new Vector3(x + len * 0.5f, 1.3f + s * 2.6f, z + 1.27f), new Vector3(len * 0.9f, 0.15f, 0.04f));
                    }
                    if (stack == 1 && _rng.NextDouble() < 0.5)
                    {
                        var w = new GameObject("ContainerRamp");
                        w.transform.SetParent(block, false);
                        w.transform.position = new Vector3(x + len * 0.5f, 1.3f, z + 1.25f + 3f);
                        w.transform.rotation = Quaternion.identity; // hohes Ende zeigt zum Container
                        MakeMesh(w, MeshFactory.Wedge(Mathf.Min(len, 6f), 2.6f, 6f), true, Palette.Concrete, surface: Surface.Concrete);
                        CreateRailPath(new List<Vector3> { new Vector3(x + 0.2f, 2.62f, z + 1.2f), new Vector3(x + len - 0.2f, 2.62f, z + 1.2f) }, "CONTAINER EDGE");
                    }
                    x += len + R(4f, 9f);
                }
            }
            // Kran
            Vector3 k = c + new Vector3(24f, 0, 30f);
            Shapes.Box(block, k + new Vector3(-5f, 9f, 0), new Vector3(1f, 18f, 1f), Palette.Yellow, 0.4f, collider: true, name: "CraneLeg");
            Shapes.Box(block, k + new Vector3(5f, 9f, 0), new Vector3(1f, 18f, 1f), Palette.Yellow, 0.4f, collider: true, name: "CraneLeg");
            Shapes.Box(block, k + new Vector3(0, 18.5f, -6f), new Vector3(12f, 1.2f, 22f), Palette.Yellow, 0.5f, name: "CraneBeam");
            Shapes.Box(block, k + new Vector3(0, 20f, 2f), new Vector3(4f, 2f, 3f), Palette.Red, 0.4f, name: "CraneCab");
        }

        // ------------------------------------------------------------------ Rand, Laternen, Startplaetze

        /// <summary>
        /// Die Stadt liegt auf einer Insel: rundum Haeuserzeilen mit Gehweg, an den vier Hafen-Seiten
        /// eine Kaimauer mit Gelaender (grindbar) und Blick aufs Wasser, dahinter eine Skyline im Dunst.
        /// </summary>
        void BuildPerimeter()
        {
            var edge = Shapes.Group(_root, "Perimeter");
            float h = Half;
            const float land = 34f, walk = 6f, opening = QuayOpening;

            // Wasser rundum
            Shapes.Box(edge, new Vector3(0, -1.9f, 0), new Vector3(3200f, 0.4f, 3200f), Palette.Hex("2DB4D6"), 0f, emission: 0.12f, name: "Water", surface: Surface.Water);

            Vector3[] outwards = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            int boundaryLayer = LayerMask.NameToLayer("Ignore Raycast");
            foreach (var outward in outwards)
            {
                Vector3 along = Vector3.Cross(Vector3.up, outward);
                bool bridgeSide = outward == Vector3.right;
                float end = h + land;
                // Landstreifen links und rechts der Hafenoeffnung
                foreach (var seg in new[] { (-end, -opening), (opening, end) })
                {
                    float p0 = seg.Item1, p1 = seg.Item2, len = p1 - p0, mid = (p0 + p1) * 0.5f;
                    Shapes.Box(edge, outward * (h + land * 0.5f) + along * mid + Vector3.up * -1.2f,
                        Abs(along * len + outward * land + Vector3.up * 2.4f), Palette.Hex("8F88A6"), 0.3f, collider: true, name: "Land", surface: Surface.Concrete);
                    Shapes.Box(edge, outward * (h + walk * 0.5f) + along * mid + Vector3.up * 0.09f,
                        Abs(along * len + outward * walk + Vector3.up * 0.18f), Palette.Sidewalk, 0.15f, collider: true, name: "EdgeSidewalk", surface: Surface.Paving);

                    // Haeuserzeile, Fassaden zur Stadt
                    // Ecken frei lassen (dort ueberschneiden sich die Zeilen), Blick aufs Wasser
                    float pEnd = Mathf.Min(p1, h + walk);
                    float p = Mathf.Max(p0, -(h + walk)) + 1f;
                    while (p < pEnd - 10f)
                    {
                        float w = Mathf.Min(R(14f, 28f), pEnd - p);
                        float d = R(18f, 26f);
                        Vector3 ground = outward * (h + walk + d * 0.5f) + along * (p + w * 0.5f);
                        Building(edge, ground, Mathf.Abs(along.x) > 0.5f ? w : d, Mathf.Abs(along.x) > 0.5f ? d : w, R(16f, 58f), -outward, 0.18f, 0.3f);
                        p += w + R(0.5f, 2.5f);
                    }
                    // Unsichtbare Grenze hinter dem Gehweg (schliesst Luecken zwischen den Haeusern)
                    var wall = new GameObject("Boundary");
                    wall.transform.SetParent(edge, false);
                    wall.transform.position = outward * (h + walk + 0.6f) + along * mid + Vector3.up * 5f;
                    wall.layer = boundaryLayer;
                    var bc = wall.AddComponent<BoxCollider>();
                    bc.size = Abs(along * len + outward * 1f + Vector3.up * 10f);
                }

                // Hafenoeffnung: Kaimauer, Poller, Gelaender zum Grinden, Blick aufs Wasser
                Shapes.Box(edge, outward * (h + 2f) + Vector3.up * -0.9f, Abs(along * opening * 2f + outward * 4f + Vector3.up * 1.8f), Palette.Hex("B7AFC8"), 0.3f, collider: true, name: "Quay", surface: Surface.Concrete);
                float gap = BridgeHalfWidth + 1.5f; // vor der Brueckenauffahrt: Absperrung statt Gelaender
                for (float q = -opening + 3f; q < opening - 2f; q += 6f)
                {
                    if (bridgeSide && Mathf.Abs(q) < gap) continue;
                    Shapes.Part(PrimitiveType.Cylinder, edge, outward * (h + 0.8f) + along * q + Vector3.up * 0.3f, new Vector3(0.45f, 0.3f, 0.45f), Palette.Ink, 0.2f, keepCollider: true, name: "Bollard");
                }
                Vector3 railLine = outward * (h + 3.4f) + Vector3.up * 0.95f;
                if (bridgeSide)
                {
                    CreateRail(railLine + along * (-opening + 1f), railLine + along * -gap, true, Palette.Yellow, "QUAY RAIL");
                    CreateRail(railLine + along * gap, railLine + along * (opening - 1f), true, Palette.Yellow, "QUAY RAIL");
                    Roadblock(edge, outward, along, gap);
                }
                else CreateRail(railLine + along * (-opening + 1f), railLine + along * (opening - 1f), true, Palette.Yellow, "QUAY RAIL");
                var quayWall = new GameObject("Boundary");
                quayWall.transform.SetParent(edge, false);
                quayWall.transform.position = outward * (h + 3.9f) + Vector3.up * 3f;
                quayWall.layer = boundaryLayer;
                var qc = quayWall.AddComponent<BoxCollider>();
                qc.size = Abs(along * opening * 2f + outward * 0.6f + Vector3.up * 6f);
                for (float q = -opening; q < opening; q += 3.5f)
                    _foam.Add(outward * (h + 4.2f + R(0f, 0.6f)) + along * (q + R(0f, 1.5f)) + Vector3.up * -1.68f, Abs(along * R(1.2f, 2.6f) + outward * 0.25f + Vector3.up * 0.05f));
            }

            BuildSkyline();
            BuildBridge(Vector3.right);
        }

        /// <summary>Absperrung vor der gesperrten Brueckenauffahrt: rot-weisse Schranke und zwei Verbotsschilder.</summary>
        void Roadblock(Transform parent, Vector3 outward, Vector3 along, float halfWidth)
        {
            Vector3 line = outward * (Half + 3.4f);
            for (int s = -1; s <= 1; s += 2)
                Shapes.Box(parent, line + along * (s * halfWidth) + Vector3.up * 0.65f, new Vector3(0.35f, 1.3f, 0.35f), Palette.White, 0.25f, collider: true, name: "BarrierPost");
            const int pieces = 12;
            float seg = 2f * halfWidth / pieces;
            for (int i = 0; i < pieces; i++)
            {
                float p = -halfWidth + seg * (i + 0.5f);
                Shapes.Box(parent, line + along * p + Vector3.up * 1.05f, Abs(along * seg + outward * 0.16f + Vector3.up * 0.24f), i % 2 == 0 ? Palette.Red : Palette.White, 0.2f, collider: true, name: "Barrier");
            }
            // Verbotsschilder (weiss mit rotem Rand), zur Stadt gedreht
            Vector3 euler = Quaternion.FromToRotation(Vector3.up, -outward).eulerAngles;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 p = line + along * (s * (halfWidth - 1.6f)) - outward * 0.35f;
                Shapes.Box(parent, p + Vector3.up * 1.2f, new Vector3(0.12f, 2.4f, 0.12f), Palette.Metal, 0.15f, name: "SignPole");
                Shapes.Part(PrimitiveType.Cylinder, parent, p + Vector3.up * 2.45f - outward * 0.1f, new Vector3(1f, 0.03f, 1f), Palette.Red, 0.2f, euler, name: "SignRing");
                Shapes.Part(PrimitiveType.Cylinder, parent, p + Vector3.up * 2.45f - outward * 0.14f, new Vector3(0.74f, 0.03f, 0.74f), Palette.White, 0f, euler, name: "SignFace");
            }
        }

        /// <summary>Festland-Skyline gegenueber: gestufte Tuerme mit Fensterbaendern, im Dunst aufgehellt.</summary>
        void BuildSkyline()
        {
            var skyline = Shapes.Group(_root, "Skyline");
            var windows = new MeshFactory.BoxBatch();
            Color haze = SkyLook.Haze;
            for (int i = 0; i < 80; i++)
            {
                float angle = R(0f, Mathf.PI * 2f);
                // nicht genau vor die Bruecke stellen
                if (Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, 0f)) < 7f) continue;
                float dist = R(480f, 820f);
                Vector3 basePos = new Vector3(Mathf.Cos(angle) * dist, -2f, Mathf.Sin(angle) * dist);
                Vector3 toCity = -basePos.normalized;
                float fade = Mathf.InverseLerp(480f, 820f, dist);
                Color col = Color.Lerp(Pick(BuildingColors), haze, Mathf.Lerp(0.2f, 0.45f, fade));
                Color roofCol = Palette.Shade(col, 0.85f);

                // bis zu drei Stufen, jede schmaler als die darunter
                float w = R(24f, 52f), d = R(24f, 52f), y = basePos.y;
                int sections = RI(1, 4);
                for (int sct = 0; sct < sections; sct++)
                {
                    float hh = sct == 0 ? R(30f, 75f) : R(14f, 38f);
                    Vector3 c = new Vector3(basePos.x, y + hh * 0.5f, basePos.z);
                    Shapes.Box(skyline, c, new Vector3(w, hh, d), col, 0.35f, name: "Tower");
                    Shapes.Box(skyline, c + Vector3.up * (hh * 0.5f + 0.6f), new Vector3(w + 1.2f, 1.2f, d + 1.2f), roofCol, 0.3f, name: "TowerRoof");
                    // Fensterbaender nur auf den Seiten, die zur Insel zeigen
                    for (float fy = y + 5f; fy < y + hh - 3f; fy += 5f)
                    {
                        if (toCity.z > 0.2f) windows.Add(new Vector3(c.x, fy, c.z + d * 0.5f + 0.1f), new Vector3(w * 0.82f, 1.8f, 0.2f));
                        if (toCity.z < -0.2f) windows.Add(new Vector3(c.x, fy, c.z - d * 0.5f - 0.1f), new Vector3(w * 0.82f, 1.8f, 0.2f));
                        if (toCity.x > 0.2f) windows.Add(new Vector3(c.x + w * 0.5f + 0.1f, fy, c.z), new Vector3(0.2f, 1.8f, d * 0.82f));
                        if (toCity.x < -0.2f) windows.Add(new Vector3(c.x - w * 0.5f - 0.1f, fy, c.z), new Vector3(0.2f, 1.8f, d * 0.82f));
                    }
                    y += hh + 1.2f;
                    w *= R(0.6f, 0.8f);
                    d *= R(0.6f, 0.8f);
                }
                if (_rng.NextDouble() < 0.35)
                    Shapes.Box(skyline, new Vector3(basePos.x, y + 9f, basePos.z), new Vector3(1.4f, 18f, 1.4f), Color.Lerp(Palette.Metal, haze, 0.4f), 0.2f, name: "Antenna");
                else if (_rng.NextDouble() < 0.4)
                {
                    Color sign = Color.Lerp(Pick(new[] { Palette.Pink, Palette.Yellow, Palette.Cyan, Palette.Lime }), haze, 0.25f);
                    Shapes.Box(skyline, new Vector3(basePos.x, y + 5f, basePos.z) + toCity * (d * 0.3f), Abs(Vector3.Cross(Vector3.up, toCity) * w * 0.9f + toCity * 0.6f + Vector3.up * 8f), sign, 0.3f, emission: 0.6f, name: "RoofSign");
                }
            }
            // Abends brennt in der Skyline Licht: warme Fensterbaender im Dunst
            windows.Build(skyline, "SkylineWindows", Color.Lerp(Palette.Hex("FFC97A"), haze, 0.3f), "city_skyline_windows", 0.6f);
        }

        /// <summary>Haengebruecke vom Hafen zum Festland (Kulisse, gesperrt).</summary>
        void BuildBridge(Vector3 outward)
        {
            var bridge = Shapes.Group(_root, "Bridge");
            Vector3 along = Vector3.Cross(Vector3.up, outward);
            float start = Half + 62f, length = 520f, deckY = 14f;
            Color steel = Palette.Hex("E2475B"), deck = Palette.Hex("B9B3C9");
            Vector3 mid = outward * (start + length * 0.5f);
            Shapes.Box(bridge, mid + Vector3.up * deckY, Abs(outward * length + along * 22f + Vector3.up * 2.2f), deck, 0.4f, name: "Deck", surface: Surface.Concrete);
            // Auffahrt: beginnt buendig an der Kaikante (hinter der Absperrung) und steigt bis zur Deck-Oberkante
            float quayEdge = Half + 4f, deckTop = deckY + 1.1f;
            Vector3 topFrom = outward * quayEdge, topTo = outward * start + Vector3.up * deckTop;
            Quaternion slope = Quaternion.LookRotation(topTo - topFrom);
            var ramp = Shapes.Box(bridge, (topFrom + topTo) * 0.5f - slope * Vector3.up * 1.1f, new Vector3(22f, 2.2f, Vector3.Distance(topFrom, topTo)), deck, 0.4f, name: "Approach", surface: Surface.Concrete);
            ramp.transform.rotation = slope;
            for (float x = Half + 20f; x < start; x += 14f)
            {
                float top = Mathf.Lerp(0f, deckTop, (x - quayEdge) / (start - quayEdge)) - 2.4f; // Unterkante der Auffahrt
                if (top < -1.2f) continue;
                Shapes.Box(bridge, outward * x + Vector3.up * ((top - 2.5f) * 0.5f), Abs(along * 12f + outward * 3f + Vector3.up * (top + 2.5f)), Palette.Hex("8F88A6"), 0.4f, name: "Pier");
            }
            Shapes.Box(bridge, mid + Vector3.up * (deckY + 1.6f) + along * 10.5f, Abs(outward * length + along * 0.6f + Vector3.up * 1.2f), steel, 0.3f, name: "Barrier");
            Shapes.Box(bridge, mid + Vector3.up * (deckY + 1.6f) - along * 10.5f, Abs(outward * length + along * 0.6f + Vector3.up * 1.2f), steel, 0.3f, name: "Barrier");
            float towerH = 78f;
            float[] towers = { start + 130f, start + length - 130f };
            foreach (float t in towers)
            {
                foreach (int sgn in new[] { -1, 1 })
                    Shapes.Box(bridge, outward * t + along * (12f * sgn) + Vector3.up * (towerH * 0.5f - 2f), new Vector3(4f, towerH, 4f), steel, 0.5f, name: "Pylon");
                Shapes.Box(bridge, outward * t + Vector3.up * (towerH - 6f), Abs(along * 28f + outward * 4f + Vector3.up * 4f), steel, 0.5f, name: "PylonBeam");
                Shapes.Box(bridge, outward * t + Vector3.up * (deckY - 4f), Abs(along * 28f + outward * 4f + Vector3.up * 3f), steel, 0.5f, name: "PylonBeam");
            }
            // Tragseile als Kettenlinie aus kurzen Stuecken
            foreach (int sgn in new[] { -1, 1 })
            {
                Vector3 side = along * (12f * sgn);
                Vector3 prev = Vector3.zero;
                for (int k = 0; k <= 40; k++)
                {
                    float u = k / 40f;
                    float x = Mathf.Lerp(start, start + length, u);
                    float y;
                    if (x < towers[0]) y = Mathf.Lerp(deckY + 2f, towerH - 4f, Mathf.Pow((x - start) / (towers[0] - start), 1.6f));
                    else if (x > towers[1]) y = Mathf.Lerp(towerH - 4f, deckY + 2f, Mathf.Pow((x - towers[1]) / (start + length - towers[1]), 0.6f));
                    else
                    {
                        float m = (x - towers[0]) / (towers[1] - towers[0]) * 2f - 1f;
                        y = Mathf.Lerp(deckY + 4f, towerH - 4f, m * m);
                    }
                    Vector3 pnt = outward * x + side + Vector3.up * y;
                    if (k > 0)
                    {
                        Vector3 segMid = (pnt + prev) * 0.5f;
                        var seg = Shapes.Box(bridge, segMid, new Vector3(0.7f, 0.7f, Vector3.Distance(pnt, prev) + 0.4f), steel, 0f, name: "Cable");
                        seg.transform.rotation = Quaternion.LookRotation(pnt - prev);
                        if (k % 2 == 0 && y > deckY + 3f)
                            Shapes.Box(bridge, new Vector3(pnt.x, (y + deckY) * 0.5f, pnt.z), new Vector3(0.25f, y - deckY, 0.25f), steel, 0f, name: "Hanger");
                    }
                    prev = pnt;
                }
            }
            // Pfeiler im Wasser
            for (float x = start + 45f; x < start + length; x += 65f)
                Shapes.Box(bridge, outward * x + Vector3.up * (deckY * 0.5f - 1.5f), Abs(along * 14f + outward * 5f + Vector3.up * (deckY - 1f)), Palette.Hex("8F88A6"), 0.4f, name: "Pier");
        }

        /// <summary>
        /// Laternen auf allen Gehwegen, 1,3 m hinter der Bordsteinkante, Kopf ueber der Fahrbahn. Gegenueberliegende
        /// Strassenseiten sind versetzt (eine Seite bei -20 und +20 m, die andere in der Mitte). Steht dort etwas
        /// (Rampe, Wand, Container), rueckt die Laterne ein Stueck zur Seite oder entfaellt.
        /// </summary>
        void BuildLamps()
        {
            var lamps = Shapes.Group(_root, "Lamps");
            Physics.SyncTransforms();
            Vector3[] sides = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            for (int row = 0; row < Grid; row++)
            {
                for (int col = 0; col < Grid; col++)
                {
                    var c = new Vector3(BlockCenter(col), 0, BlockCenter(row));
                    foreach (var side in sides)
                    {
                        Vector3 along = Vector3.Cross(Vector3.up, side);
                        foreach (float o in LampOffsets(side))
                            TryLamp(lamps, c + side * (Block * 0.5f - LampInset) + along * o, side, along);
                    }
                }
            }
            // Aussenseite der Ringstrasse (Gehweg vor der Haeuserzeile); an den Kaimauern keine
            foreach (var outward in sides)
            {
                Vector3 along = Vector3.Cross(Vector3.up, outward);
                for (int i = 0; i < Grid; i++)
                {
                    foreach (float o in LampOffsets(-outward))
                    {
                        float p = BlockCenter(i) + o;
                        if (Mathf.Abs(p) < QuayOpening + 4f) continue;
                        TryLamp(lamps, outward * (Half + LampInset) + along * p, -outward, along);
                    }
                }
            }
        }

        /// <summary>Versatz entlang der Blockseite: Seiten nach +X/+Z bei -20/+20 m, nach -X/-Z in der Mitte.</summary>
        static float[] LampOffsets(Vector3 facing) => facing.x + facing.z > 0f ? new[] { -20f, 20f } : new[] { 0f };

        void TryLamp(Transform parent, Vector3 pos, Vector3 facing, Vector3 along)
        {
            int solid = ~LayerMask.GetMask("Skater", "Car", "Ignore Raycast");
            int ground = solid & ~LayerMask.GetMask("Rail");
            foreach (float shift in new[] { 0f, 3f, -3f, 6f, -6f })
            {
                Vector3 p = pos + along * shift;
                // Nur auf dem Gehweg (Bordstein-Hoehe), nicht auf Fahrbahn, Rampen oder Containern
                if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 4f, ground, QueryTriggerInteraction.Ignore)) continue;
                if (hit.point.y < RingHeight - 0.03f || hit.point.y > 0.25f) continue;
                if (Physics.CheckBox(hit.point + Vector3.up * 3.75f, new Vector3(0.7f, 3.35f, 0.7f), Quaternion.identity, solid, QueryTriggerInteraction.Ignore)) continue;
                Lamp(parent, hit.point, facing);
                return;
            }
        }

        /// <summary>Strassenlaterne: Sockel, Mast (mit Collider), Ausleger und leuchtender Kopf ueber der Fahrbahn.</summary>
        void Lamp(Transform parent, Vector3 basePos, Vector3 facing)
        {
            var lamp = Shapes.Group(parent, "Lamp");
            lamp.position = basePos;
            lamp.rotation = Quaternion.LookRotation(facing);
            Shapes.Box(lamp, new Vector3(0, 0.2f, 0), new Vector3(0.5f, 0.4f, 0.5f), Palette.Ink, 0.15f, name: "LampBase");
            Shapes.Part(PrimitiveType.Cylinder, lamp, new Vector3(0, 3.3f, 0), new Vector3(0.22f, 3.3f, 0.22f), Palette.Ink, 0.15f, keepCollider: true, name: "LampPole");
            Shapes.Box(lamp, new Vector3(0, 6.45f, 0.95f), new Vector3(0.16f, 0.16f, 1.9f), Palette.Ink, 0.15f, name: "LampArm");
            Shapes.Box(lamp, new Vector3(0, 6.3f, 1.75f), new Vector3(0.55f, 0.24f, 0.9f), Palette.Cream, 0.2f, emission: 2.2f, name: "LampHead");
        }

        void BuildSpawnPoints()
        {
            var spawns = Shapes.Group(_root, "SpawnPoints");
            spawns.gameObject.AddComponent<SpawnPoints>();
            float road = RoadCenter(2);
            for (int i = 0; i < 8; i++)
            {
                var sp = Shapes.Group(spawns, "Spawn" + i);
                float x = -18f + (i / 2) * 12f;
                float z = road + (i % 2 == 0 ? -5f : 5f);
                sp.position = new Vector3(x, 0.35f, z);
                sp.rotation = Quaternion.Euler(0, i % 2 == 0 ? 90f : -90f, 0);
            }
        }

        // ------------------------------------------------------------------ Bausteine

        void MakeMesh(GameObject go, Mesh mesh, bool collider, Color? color = null, bool boxOutline = true, Surface surface = Surface.None)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) mr = go.AddComponent<MeshRenderer>();
            if (color.HasValue) mr.sharedMaterial = ToonMaterials.Get(color.Value, 0.35f, boxOutline, 0f, surface);
            go.transform.localScale = Vector3.one;
            if (collider)
            {
                var old = go.GetComponent<Collider>();
                if (old != null) Object.DestroyImmediate(old);
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
            }
        }

        /// <summary>Ledge: Block mit Grind-Kanten oben an beiden Laengsseiten.</summary>
        void CreateLedge(Transform parent, Vector3 bottomCenter, Vector3 size, Color color, bool rails = true)
        {
            Shapes.Box(parent, bottomCenter + Vector3.up * size.y * 0.5f, size, color, 0.35f, collider: true, name: "Ledge", surface: Surface.Concrete);
            if (!rails) return;
            float y = bottomCenter.y + size.y + 0.02f;
            if (size.x >= size.z)
            {
                for (int s = -1; s <= 1; s += 2)
                    CreateRailPath(new List<Vector3>
                    {
                        new Vector3(bottomCenter.x - size.x * 0.5f + 0.1f, y, bottomCenter.z + s * (size.z * 0.5f - 0.15f)),
                        new Vector3(bottomCenter.x + size.x * 0.5f - 0.1f, y, bottomCenter.z + s * (size.z * 0.5f - 0.15f))
                    }, "LEDGE");
            }
            else
            {
                for (int s = -1; s <= 1; s += 2)
                    CreateRailPath(new List<Vector3>
                    {
                        new Vector3(bottomCenter.x + s * (size.x * 0.5f - 0.15f), y, bottomCenter.z - size.z * 0.5f + 0.1f),
                        new Vector3(bottomCenter.x + s * (size.x * 0.5f - 0.15f), y, bottomCenter.z + size.z * 0.5f - 0.1f)
                    }, "LEDGE");
            }
        }

        /// <summary>Unsichtbare Grind-Linie (Weltkoordinaten).</summary>
        GrindRail CreateRailPath(List<Vector3> worldPoints, string label)
        {
            var go = new GameObject("Grind_" + label);
            go.transform.SetParent(_rails, false);
            go.layer = _railLayer;
            var rail = go.AddComponent<GrindRail>();
            rail.label = label;
            rail.points = worldPoints.ToArray();
            return rail;
        }

        /// <summary>
        /// Sichtbares Gelaender mit Pfosten bis zum Boden. Stange und Pfosten sind fest (Autos und Skater prallen ab),
        /// gegrindet wird per Knopf. solid = false fuer Kanten, die schon Teil eines festen Objekts sind (Coping).
        /// </summary>
        void CreateRail(Vector3 a, Vector3 b, bool posts, Color color, string label, bool solid = true)
        {
            var rail = CreateRailPath(new List<Vector3> { a + Vector3.up * 0.04f, b + Vector3.up * 0.04f }, label);
            Vector3 d = b - a;
            var bar = Shapes.Part(PrimitiveType.Cylinder, rail.transform, (a + b) * 0.5f, new Vector3(0.09f, d.magnitude * 0.5f, 0.09f), color, 0.2f, keepCollider: solid, name: "Bar");
            bar.transform.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            bar.layer = _railLayer;
            if (!posts) return;
            int count = Mathf.Max(2, Mathf.CeilToInt(d.magnitude / 3f) + 1);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)(count - 1));
                float h = Mathf.Max(0.1f, p.y - GroundHeightGuess(p));
                var post = Shapes.Part(PrimitiveType.Cylinder, rail.transform, new Vector3(p.x, p.y - h * 0.5f, p.z), new Vector3(0.07f, h * 0.5f, 0.07f), Palette.Ink, 0.15f, keepCollider: true, name: "Post");
                post.layer = _railLayer;
            }
        }

        /// <summary>Hoehe des Bodens unter p (Rails, Spieler und unsichtbare Grenzen zaehlen nicht).</summary>
        static float GroundHeightGuess(Vector3 p)
        {
            Physics.SyncTransforms();
            int mask = ~LayerMask.GetMask("Rail", "Skater", "Car", "Ignore Raycast");
            if (Physics.Raycast(p + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, 8f, mask, QueryTriggerInteraction.Ignore)) return hit.point.y;
            return 0f;
        }

        void CreateTagSpot(Transform parent, Vector3 center, Vector3 normal, bool standalone = false, Vector2? size = null)
        {
            Vector2 s = size ?? new Vector2(4f, 3f);
            var go = new GameObject($"TagSpot_{_tagCount++:00}");
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.rotation = Quaternion.LookRotation(normal);
            _tagSpots.Add(center);
            if (standalone)
            {
                // Freistehende Wand, die bis zum Boden reicht
                go.transform.position += Vector3.up * 0.3f;
                float bottom = GroundHeightGuess(center) - go.transform.position.y, top = s.y * 0.5f + 0.5f;
                Shapes.Box(go.transform, new Vector3(0, (top + bottom) * 0.5f, -0.2f), new Vector3(s.x + 1f, top - bottom, 0.3f), Palette.Hex("6C5A8C"), 0.4f, collider: true, name: "TagWall", surface: Surface.Concrete);
            }
            var spot = go.AddComponent<TagSpot>();
            var frameMat = ToonMaterials.Get(Palette.Yellow, 0f, true, 0.35f);
            var frame = Shapes.Decal(go.transform, Vector3.zero, s + new Vector2(0.3f, 0.3f), Vector3.forward, frameMat, "Frame");
            frame.transform.localPosition = new Vector3(0, 0, 0.02f);
            var inner = Shapes.Decal(go.transform, new Vector3(0, 0, 0.025f), s - new Vector2(0.3f, 0.3f), Vector3.forward, ToonMaterials.Get(Palette.Hex("FFF6C9"), 0f, true, 0.2f), "Inner");
            inner.transform.SetParent(frame.transform, true);
            var surface = Shapes.Decal(go.transform, new Vector3(0, 0, 0.05f), s, Vector3.forward, ToonMaterials.Get(Palette.White, 0f), "Surface");
            surface.GetComponent<Renderer>().enabled = false;
            spot.frame = frame.GetComponent<Renderer>();
            spot.surface = surface.GetComponent<Renderer>();
        }
    }
}
