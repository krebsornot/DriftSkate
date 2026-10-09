using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Baut die Optik eines Autos: Karosserie aus CarBody (JDM-Formen), Folierung als Textur, Lichter je nach Modell,
    /// Anbauteile (Lippe, Schweller, Verbreiterung, Fluegel, Haube, Auspuff), Felgen mit Sturz; dazu Raeder und Collider.
    /// </summary>
    public static class CarBuilder
    {
        public class Result
        {
            public Transform visual;
            public Transform seat;
            public Transform exitPoint;
            public Transform[] rearWheels = new Transform[2];
            public CarLivery livery;
        }

        static readonly Color Trim = Palette.Hex("1E1D25");
        static readonly Color Carbon = Palette.Hex("2A2932");
        static readonly Color LampLight = Palette.Hex("FFF4DA");
        static readonly Color TailRed = Palette.Hex("E8263E");
        static readonly Color Amber = Palette.Hex("FFA62B");

        public static Result Build(Transform root, CarDef def, Color paint, Color crew, Texture graffiti, VehicleController vc, CarDesign design = null)
        {
            foreach (var childName in new[] { "Visual", "Wheel0", "Wheel1", "Wheel2", "Wheel3", "Seat", "Exit" })
            {
                var oldChild = root.Find(childName);
                if (oldChild == null) continue;
                oldChild.name = "_removed";
                oldChild.gameObject.SetActive(false);
                Shapes.DestroySafe(oldChild.gameObject);
            }
            if (design == null) design = CarDesign.Default(def.id, crew);
            design.Sanitize();
            if (def.IsModel) return BuildModel(root, def, paint, design, vc);

            var res = new Result();
            var visual = Shapes.Group(root, "Visual");
            res.visual = visual;
            var body = new CarBody(def, CarShape.For(def.id));
            var s = body.s;
            float L = body.L, W = body.W, r = body.R;

            // Lack + Folie als eine Textur
            var livery = visual.gameObject.AddComponent<CarLivery>();
            livery.Init(body, paint, design, graffiti);
            res.livery = livery;
            var paintMat = livery.Material;
            var paintSolid = ToonMaterials.Get(paint, 0.35f, true);
            var paintRound = ToonMaterials.Get(paint, 0.35f, false);
            var trimMat = ToonMaterials.Get(Trim, 0.3f, true);
            var glassMat = ToonMaterials.Get(Palette.Hex("2A3048"), 0.4f, false);

            // Karosserie und Kabine
            MeshPart(visual, "Body", body.BuildLowerBody(), paintMat);
            MeshPart(visual, "Cabin", body.BuildGreenhouse(), paintMat, glassMat, ToonMaterials.Get(Trim, 0.4f, false));

            // Unterboden zwischen den Raedern (sonst sieht man unter den Kotfluegeln durch)
            float sill = CarShape.Eval(s.bottom, 0f);
            Shapes.Box(visual, new Vector3(0f, (0.14f + sill + 0.25f) * 0.5f, 0f), new Vector3(def.track - 0.34f, sill + 0.25f - 0.14f, L - 0.5f), Trim, 0f, name: "Chassis");
            for (int axle = 0; axle < 2; axle++) ArchLiner(visual, body, axle);

            BuildFront(visual, body, design, ToonMaterials.Get(paint, 0.12f, true), trimMat);
            BuildRear(visual, body, design, trimMat);
            BuildMirrors(visual, body, paintRound);
            BuildParts(visual, body, design, paint, paintSolid, trimMat);
            if (design.neon > 0) Underglow.Build(visual, def.track, def.wheelbase, L, r, sill, design.neon, design.neonColor);

            // Raeder
            float wheelWidth = 0.24f;
            bool wide = design.parts[CarDesign.Fenders] == 1;
            for (int i = 0; i < 4; i++)
            {
                bool left = i % 2 == 0;
                var pivot = Shapes.Group(root, "Wheel" + i);
                pivot.localPosition = vc != null
                    ? vc.wheels[i].localTop
                    : new Vector3((left ? -0.5f : 0.5f) * def.track, r, (i < 2 ? 0.5f : -0.5f) * def.wheelbase);
                float side = left ? -1f : 1f;
                var camber = Shapes.Group(pivot, "Camber");
                float deg = design.camber * (i < 2 ? 1f : 0.6f);
                camber.localRotation = Quaternion.Euler(0f, 0f, -side * deg);
                camber.localPosition = new Vector3(side * (wide ? 0.035f : 0f), 0f, 0f);
                var spin = Shapes.Group(camber, "Spin");
                BuildWheel(spin, r, wheelWidth, side, design.parts[CarDesign.Rims], design.rimColor);
                if (vc != null) vc.SetWheelVisual(i, pivot, spin);
                if (i >= 2) res.rearWheels[i - 2] = pivot;
            }

            // Collider
            var col = root.GetComponent<BoxCollider>();
            if (col == null) col = root.gameObject.AddComponent<BoxCollider>();
            float H = def.height;
            col.size = new Vector3(W, (H - sill) * 0.92f, L);
            col.center = new Vector3(0, sill + (H - sill) * 0.46f, 0);
            var pm = new PhysicsMaterial("CarBody") { dynamicFriction = 0.15f, staticFriction = 0.15f, bounciness = 0.05f, frictionCombine = PhysicsMaterialCombine.Minimum };
            col.sharedMaterial = pm;

            // Rechtslenker (JDM): Fahrer rechts
            float seatZ = (s.RoofFrontT + s.RoofRearT) * 0.5f * L;
            res.seat = Shapes.Group(root, "Seat", new Vector3(W * 0.22f, sill + 0.1f, seatZ));
            res.exitPoint = Shapes.Group(root, "Exit", new Vector3(W * 0.5f + 1.1f, 0.1f, seatZ));
            return res;
        }

        /// <summary>
        /// Fertig modelliertes Auto (CarModel unter Resources/CarModels): Modell einsetzen, Lack umfaerben, die vier
        /// Raeder an die Federbeine der Physik haengen. Folie und Anbauteile gibt es hier nicht, Neon und Sturz schon.
        /// </summary>
        static Result BuildModel(Transform root, CarDef def, Color paint, CarDesign design, VehicleController vc)
        {
            var res = new Result();
            var visual = Shapes.Group(root, "Visual");
            res.visual = visual;
            float r = def.wheelRadius;
            var prefab = Resources.Load<GameObject>("CarModels/" + def.model);
            CarModel model = null;
            if (prefab == null) Debug.LogError("[CarBuilder] Auto-Modell fehlt: Resources/CarModels/" + def.model);
            else
            {
                var inst = Object.Instantiate(prefab, visual, false);
                inst.name = "Model";
                model = inst.GetComponent<CarModel>();
                model.ApplyPaint(paint);
            }

            for (int i = 0; i < 4; i++)
            {
                bool left = i % 2 == 0;
                var pivot = Shapes.Group(root, "Wheel" + i);
                pivot.localPosition = vc != null
                    ? vc.wheels[i].localTop
                    : new Vector3((left ? -0.5f : 0.5f) * def.track, r, (i < 2 ? 0.5f : -0.5f) * def.wheelbase);
                float side = left ? -1f : 1f;
                var camber = Shapes.Group(pivot, "Camber");
                camber.localRotation = Quaternion.Euler(0f, 0f, -side * design.camber * (i < 2 ? 1f : 0.6f));
                var spin = Shapes.Group(camber, "Spin");
                if (model != null && model.wheels[i] != null)
                {
                    var w = model.wheels[i];
                    w.SetParent(spin, false);
                    w.localPosition = Vector3.zero;
                    w.localRotation = Quaternion.identity;
                }
                if (vc != null) vc.SetWheelVisual(i, pivot, spin);
                if (i >= 2) res.rearWheels[i - 2] = pivot;
            }
            if (design.neon > 0) Underglow.Build(visual, def.track, def.wheelbase, def.length, r, 0.25f, design.neon, design.neonColor);

            var col = root.GetComponent<BoxCollider>();
            if (col == null) col = root.gameObject.AddComponent<BoxCollider>();
            Vector3 size = model != null ? model.boxSize : new Vector3(def.width, def.height - 0.25f, def.length);
            col.size = size;
            col.center = model != null ? model.boxCenter : new Vector3(0f, 0.25f + size.y * 0.5f, 0f);
            col.sharedMaterial = new PhysicsMaterial("CarBody") { dynamicFriction = 0.15f, staticFriction = 0.15f, bounciness = 0.05f, frictionCombine = PhysicsMaterialCombine.Minimum };

            res.seat = Shapes.Group(root, "Seat", new Vector3(size.x * 0.22f, 0.35f, -0.1f));
            res.exitPoint = Shapes.Group(root, "Exit", new Vector3(size.x * 0.5f + 1.1f, 0.1f, -0.1f));
            return res;
        }

        /// <summary>Folie neu zeichnen, ohne das Auto neu zu bauen (Live-Vorschau im Garagen-Editor).</summary>
        public static void RefreshLivery(Transform root, Color paint, CarDesign design)
        {
            var livery = root.GetComponentInChildren<CarLivery>();
            if (livery != null) livery.Render(paint, design);
        }

        // ------------------------------------------------------------------ Bausteine

        static GameObject MeshPart(Transform parent, string name, Mesh mesh, params Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            return go;
        }

        /// <summary>Quader, dessen +z zur Normalen zeigt (Lichter, Gitter, Kennzeichen auf gewoelbten Flaechen).</summary>
        static GameObject OnSurface(Transform parent, Vector3 pos, Vector3 normal, Vector3 size, Color color, float outline = 0.15f, float emission = 0f, string name = "Part")
        {
            var go = Shapes.Box(parent, pos + normal * size.z * 0.35f, size, color, outline, emission: emission, name: name);
            go.transform.localRotation = Quaternion.LookRotation(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up);
            return go;
        }

        /// <summary>Runde Lampe (flacher Zylinder), Achse entlang der Normalen.</summary>
        static GameObject RoundOnSurface(Transform parent, Vector3 pos, Vector3 normal, float dx, float dy, float depth, Color color, float emission, string name)
        {
            var go = Shapes.Part(PrimitiveType.Cylinder, parent, pos + normal * depth * 0.3f, new Vector3(dx, depth * 0.5f, dy), color, 0.12f, emission: emission, name: name);
            go.transform.localRotation = Quaternion.LookRotation(normal, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            return go;
        }

        /// <summary>Lampe mit dunklem Gehaeuse und leuchtendem Glas, auf Front (sign 1) oder Heck (-1).</summary>
        static void Lamp(Transform parent, CarBody body, int sign, float x, float y, float w, float h, Color lens, float emission, bool round = false, string name = "Lamp")
        {
            body.EndPoint(x, y, sign, out Vector3 p, out Vector3 n);
            if (round)
            {
                RoundOnSurface(parent, p, n, w + 0.03f, h + 0.03f, 0.05f, Trim, 0f, name + "Housing");
                RoundOnSurface(parent, p + n * 0.012f, n, w, h, 0.05f, lens, emission, name);
            }
            else
            {
                OnSurface(parent, p, n, new Vector3(w + 0.03f, h + 0.03f, 0.05f), Trim, 0.12f, 0f, name + "Housing");
                OnSurface(parent, p + n * 0.012f, n, new Vector3(w, h, 0.05f), lens, 0.08f, emission, name);
            }
        }

        static void ArchLiner(Transform parent, CarBody body, int axle)
        {
            // Dunkle Halbroehre im Radlauf: wirkt wie Tiefe statt Lack von unten.
            // Innen- und Aussenseite mit eigenen Punkten, sonst heben sich die Normalen auf (Laenge 0 -> NaN im Shader -> Bloom macht alles weiss).
            float z = body.WheelT(axle) * body.L, rad = body.archR - 0.005f, len = body.def.track + 0.1f;
            const int seg = 12;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            for (int face = 0; face < 2; face++)
            {
                int start = verts.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float a = Mathf.PI * i / seg;
                    Vector3 radial = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));
                    Vector3 c = new Vector3(0f, body.R, z) + radial * rad;
                    Vector3 n = face == 0 ? -radial : radial; // 0: innen (zum Rad), 1: aussen
                    verts.Add(c + Vector3.left * len * 0.5f); normals.Add(n);
                    verts.Add(c + Vector3.right * len * 0.5f); normals.Add(n);
                    if (i < seg)
                    {
                        int k = start + i * 2;
                        // Unity: Cross(b-a, c-a) zeigt zur Vorderseite; (k, k+1, k+2) zeigt radial nach aussen
                        if (face == 1) tris.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 });
                        else tris.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
                    }
                }
            }
            var m = new Mesh { name = "ArchLiner" };
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            MeshPart(parent, "ArchLiner", m, ToonMaterials.Get(Palette.Hex("121118"), 0f, false));
        }

        // ------------------------------------------------------------------ Front

        static void BuildFront(Transform v, CarBody body, CarDesign d, Material paintSolid, Material trimMat)
        {
            var s = body.s;
            float W = body.W, L = body.L;
            float noseTop = body.TopY(0.49f), chin = body.BottomY(0.49f);
            float midY = (noseTop + chin) * 0.5f;

            switch (s.head)
            {
                case HeadStyle.PopUp:
                {
                    bool open = d.parts[CarDesign.PopUps] == 1;
                    float t = 0.5f - 0.3f / L;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float x = side * W * 0.3f;
                        float y = body.TopSurfaceY(t, x);
                        Vector3 p = new Vector3(x, y, t * L);
                        float slope = (body.TopY(t + 0.01f) - body.TopY(t - 0.01f)) / (0.02f * L);
                        var lid = Shapes.Box(v, p + Vector3.up * (open ? 0.1f : 0.004f), new Vector3(0.4f, open ? 0.2f : 0.02f, 0.28f), Color.white, 0.12f, name: "PopUp");
                        lid.GetComponent<Renderer>().sharedMaterial = paintSolid;
                        lid.transform.localRotation = Quaternion.Euler(open ? 0f : -Mathf.Atan(slope) * Mathf.Rad2Deg, 0f, 0f);
                        if (open)
                        {
                            Shapes.Box(v, p + new Vector3(0f, 0.11f, 0.145f), new Vector3(0.34f, 0.13f, 0.02f), LampLight, 0.08f, emission: 1.3f, name: "Headlight");
                        }
                        // kleine Lampen in der Stossstange (Blinker, Standlicht)
                        Lamp(v, body, 1, side * W * 0.36f, midY + 0.02f, 0.2f, 0.05f, Amber, 0.8f, false, "Blinker");
                        Lamp(v, body, 1, side * W * 0.2f, midY + 0.02f, 0.16f, 0.05f, LampLight, 0.9f, false, "Parklight");
                    }
                    Lamp(v, body, 1, 0f, midY + 0.07f, W * 0.18f, 0.04f, Trim, 0f, false, "Grille");
                    break;
                }
                case HeadStyle.Slim:
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Lamp(v, body, 1, side * W * 0.33f, noseTop - 0.07f, 0.4f, 0.085f, LampLight, 1.3f, false, "Headlight");
                        Lamp(v, body, 1, side * W * 0.38f, chin + 0.09f, 0.14f, 0.05f, Amber, 0.8f, false, "Blinker");
                    }
                    Lamp(v, body, 1, 0f, noseTop - 0.08f, W * 0.24f, 0.06f, Trim, 0f, false, "Grille");
                    Lamp(v, body, 1, 0f, chin + 0.1f, W * 0.5f, 0.1f, Trim, 0f, false, "Intake");
                    break;
                case HeadStyle.Oval:
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Lamp(v, body, 1, side * W * 0.33f, noseTop - 0.065f, 0.4f, 0.13f, LampLight, 1.3f, true, "Headlight");
                        Lamp(v, body, 1, side * W * 0.39f, chin + 0.08f, 0.13f, 0.05f, Amber, 0.8f, false, "Blinker");
                    }
                    Lamp(v, body, 1, 0f, chin + 0.12f, W * 0.56f, 0.13f, Trim, 0f, false, "Intake");
                    break;
                case HeadStyle.Square:
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Lamp(v, body, 1, side * W * 0.32f, noseTop - 0.09f, 0.42f, 0.12f, LampLight, 1.3f, false, "Headlight");
                        Lamp(v, body, 1, side * W * 0.4f, chin + 0.1f, 0.14f, 0.06f, Amber, 0.8f, false, "Blinker");
                    }
                    Lamp(v, body, 1, 0f, noseTop - 0.09f, W * 0.26f, 0.1f, Trim, 0f, false, "Grille");
                    Lamp(v, body, 1, 0f, chin + 0.09f, W * 0.48f, 0.08f, Trim, 0f, false, "Intake");
                    break;
                case HeadStyle.Angular:
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Lamp(v, body, 1, side * W * 0.33f, noseTop - 0.08f, 0.42f, 0.1f, LampLight, 1.3f, false, "Headlight");
                        Lamp(v, body, 1, side * W * 0.26f, noseTop - 0.08f, 0.09f, 0.08f, Palette.White, 1.6f, true, "Projector");
                    }
                    Lamp(v, body, 1, 0f, noseTop - 0.09f, W * 0.3f, 0.08f, Trim, 0f, false, "Grille");
                    Lamp(v, body, 1, 0f, chin + 0.11f, W * 0.62f, 0.12f, Trim, 0f, false, "Intake");
                    break;
            }

            // Kennzeichen vorn (japanisches Format)
            body.EndPoint(0f, chin + 0.2f, 1, out Vector3 pp, out Vector3 pn);
            var plate = OnSurface(v, pp + pn * 0.03f, pn, new Vector3(0.33f, 0.165f, 0.015f), Palette.White, 0.08f, 0f, "Plate");
            plate.GetComponent<Renderer>().sharedMaterial = PlateMaterial(body.def);
        }

        // ------------------------------------------------------------------ Heck

        static void BuildRear(Transform v, CarBody body, CarDesign d, Material trimMat)
        {
            var s = body.s;
            float W = body.W;
            float tailTop = body.TopY(-0.49f), tailBot = body.BottomY(-0.49f);
            float lampY = tailTop - 0.11f;
            switch (s.tail)
            {
                case TailStyle.Bar:
                    Lamp(v, body, -1, 0f, lampY, W * 0.84f, 0.12f, TailRed, 1f, false, "Tail");
                    Lamp(v, body, -1, 0f, lampY, W * 0.3f, 0.125f, Trim, 0f, false, "TailGarnish");
                    break;
                case TailStyle.Slim:
                    for (int side = -1; side <= 1; side += 2) Lamp(v, body, -1, side * W * 0.34f, lampY, 0.4f, 0.09f, TailRed, 1f, false, "Tail");
                    break;
                case TailStyle.Wide:
                    for (int side = -1; side <= 1; side += 2) Lamp(v, body, -1, side * W * 0.24f, lampY, W * 0.42f, 0.11f, TailRed, 1f, false, "Tail");
                    Lamp(v, body, -1, 0f, lampY, 0.14f, 0.11f, Trim, 0f, false, "TailGarnish");
                    break;
                case TailStyle.Block:
                    for (int side = -1; side <= 1; side += 2) Lamp(v, body, -1, side * W * 0.33f, lampY - 0.02f, 0.44f, 0.16f, TailRed, 1f, false, "Tail");
                    Lamp(v, body, -1, 0f, lampY, W * 0.3f, 0.05f, Palette.Hex("8A1828"), 0.4f, false, "TailGarnish");
                    break;
                case TailStyle.OvalPair:
                    Lamp(v, body, -1, 0f, lampY, W * 0.9f, 0.16f, Trim, 0f, false, "TailBand");
                    for (int side = -1; side <= 1; side += 2)
                        for (int k = 0; k < 2; k++)
                            Lamp(v, body, -1, side * W * (0.38f - k * 0.13f), lampY, 0.13f, 0.13f, TailRed, 1.1f, true, "Tail");
                    break;
                case TailStyle.QuadRound:
                    for (int side = -1; side <= 1; side += 2)
                        for (int k = 0; k < 2; k++)
                            Lamp(v, body, -1, side * W * (0.38f - k * 0.13f), lampY, 0.14f, 0.14f, TailRed, 1.1f, true, "Tail");
                    break;
            }
            body.EndPoint(0f, tailBot + 0.26f, -1, out Vector3 pp, out Vector3 pn);
            var plate = OnSurface(v, pp + pn * 0.02f, pn, new Vector3(0.33f, 0.165f, 0.015f), Palette.White, 0.08f, 0f, "Plate");
            plate.GetComponent<Renderer>().sharedMaterial = PlateMaterial(body.def);
        }

        static void BuildMirrors(Transform v, CarBody body, Material paintRound)
        {
            float t = body.s.CowlT - 0.05f;
            float y = body.TopY(t) + 0.08f;
            for (int side = -1; side <= 1; side += 2)
            {
                float x = body.SideX(t, body.TopY(t) - 0.04f) * body.s.glassInset + 0.08f;
                var m = Shapes.Part(PrimitiveType.Sphere, v, new Vector3(side * x, y, t * body.L), new Vector3(0.16f, 0.1f, 0.12f), Color.white, 0.25f, name: "Mirror");
                m.GetComponent<Renderer>().sharedMaterial = paintRound;
                Shapes.Box(v, new Vector3(side * (x - 0.06f), y - 0.04f, t * body.L), new Vector3(0.08f, 0.03f, 0.04f), Trim, 0.1f, name: "MirrorStalk");
            }
        }

        // ------------------------------------------------------------------ Anbauteile

        static void BuildParts(Transform v, CarBody body, CarDesign d, Color paint, Material paintSolid, Material trimMat)
        {
            var s = body.s;
            float W = body.W, L = body.L, r = body.R;
            float chin = body.BottomY(0.49f);

            // Frontlippe / Splitter
            int lip = d.parts[CarDesign.Lip];
            if (lip > 0)
            {
                float z = body.EndZ(0f, chin + 0.04f, 1);
                Shapes.Box(v, new Vector3(0f, chin - 0.012f, z - (lip == 2 ? 0.06f : 0.04f)), new Vector3(W * (lip == 2 ? 0.98f : 0.84f), 0.035f, lip == 2 ? 0.3f : 0.16f), Carbon, 0.2f, name: "FrontLip");
                if (lip == 2)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var cn = Shapes.Box(v, new Vector3(side * W * 0.44f, chin + 0.1f, z - 0.06f), new Vector3(0.14f, 0.02f, 0.12f), Carbon, 0.15f, name: "Canard");
                        cn.transform.localRotation = Quaternion.Euler(0f, side * 10f, side * -12f);
                    }
            }

            // Seitenschweller
            if (d.parts[CarDesign.Skirts] == 1)
            {
                float t0 = body.WheelT(1) + (body.archR + 0.04f) / L, t1 = body.WheelT(0) - (body.archR + 0.04f) / L;
                float y = CarShape.Eval(s.bottom, 0f) + 0.035f;
                float x = body.SideX(0f, y + 0.05f) + 0.02f;
                for (int side = -1; side <= 1; side += 2)
                    Shapes.Box(v, new Vector3(side * x, y, (t0 + t1) * 0.5f * L), new Vector3(0.07f, 0.1f, (t1 - t0) * L), Carbon, 0.2f, name: "SideSkirt");
            }

            // Genietete Verbreiterungen: glatter Bogen ueber dem Radlauf, Nieten drauf
            if (d.parts[CarDesign.Fenders] == 1)
            {
                var fenderMat = ToonMaterials.Get(Trim, 0.25f, false);
                for (int axle = 0; axle < 2; axle++)
                {
                    float zc = body.WheelT(axle) * L;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        MeshPart(v, "OverFender", OverFenderMesh(body, zc, side), fenderMat);
                        for (int i = 1; i < 8; i++)
                        {
                            float a = Mathf.Lerp(14f, 166f, i / 8f) * Mathf.Deg2Rad;
                            float rr = body.archR + 0.04f;
                            float z = zc + Mathf.Cos(a) * rr, y = r + Mathf.Sin(a) * rr;
                            float x = FenderX(body, z, y) + 0.072f;
                            Shapes.Part(PrimitiveType.Sphere, v, new Vector3(side * x, y, z), Vector3.one * 0.024f, Palette.Metal, 0f, emission: 0.3f, name: "Rivet");
                        }
                    }
                }
            }

            // Motorhaube
            int hood = d.parts[CarDesign.Hood];
            if (hood > 0)
            {
                var m = HoodPatch(body);
                MeshPart(v, "CarbonHood", m, ToonMaterials.Get(Carbon, 0.2f, false));
                if (hood == 2)
                {
                    float t = 0.5f - 0.55f / L;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float x = side * W * 0.16f;
                        var vent = Shapes.Box(v, new Vector3(x, body.TopSurfaceY(t, x) + 0.012f, t * L), new Vector3(0.26f, 0.025f, 0.2f), Palette.Hex("0E0D12"), 0.15f, name: "HoodVent");
                        float slope = (body.TopY(t + 0.01f) - body.TopY(t - 0.01f)) / (0.02f * L);
                        vent.transform.localRotation = Quaternion.Euler(-Mathf.Atan(slope) * Mathf.Rad2Deg, 0f, 0f);
                    }
                }
            }

            // Heckfluegel
            int wing = d.parts[CarDesign.Wing];
            if (wing > 0)
            {
                float tw = s.hatch ? s.GlassEndT + 0.03f : -0.46f;
                float deckY = body.TopSurfaceY(tw, 0f);
                float z = tw * L;
                switch (wing)
                {
                    case 1: // Entenbuerzel: Abrisskante am Heckende
                    {
                        var duck = new GameObject("Ducktail");
                        duck.transform.SetParent(v, false);
                        duck.transform.localPosition = new Vector3(0f, body.TopSurfaceY(-0.48f, 0f) + 0.03f, -0.475f * L);
                        duck.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                        duck.AddComponent<MeshFilter>().sharedMesh = MeshFactory.Wedge(W * 0.86f, 0.07f, 0.24f);
                        duck.AddComponent<MeshRenderer>().sharedMaterial = paintSolid;
                        break;
                    }
                    case 2: // GT-Fluegel aus Carbon
                        Wing(v, W * 0.96f, deckY, z, 0.3f, 0.3f, Carbon, false);
                        break;
                    case 3: // Buegel in Wagenfarbe
                        Wing(v, W * 0.86f, deckY, z, 0.22f, 0.28f, paint, true);
                        break;
                    case 4: // Schwanenhals: hoch, von oben gehalten
                        Wing(v, W * 1.0f, deckY, z, 0.48f, 0.32f, Carbon, false, true);
                        break;
                }
            }

            // Auspuff
            int ex = d.parts[CarDesign.Exhaust];
            float ey = body.BottomY(-0.49f) + 0.04f;
            float ez = body.EndZ(W * 0.3f, ey + 0.05f, -1) - 0.03f;
            if (ex == 0) Pipe(v, new Vector3(W * 0.3f, ey, ez), 0.07f);
            else if (ex == 1) Pipe(v, new Vector3(W * 0.3f, ey + 0.01f, ez - 0.03f), 0.13f);
            else { Pipe(v, new Vector3(W * 0.3f, ey, ez), 0.085f); Pipe(v, new Vector3(-W * 0.3f, ey, ez), 0.085f); }
        }

        static float FenderX(CarBody body, float z, float y)
        {
            float t = z / body.L;
            float x = body.SideX(t, Mathf.Clamp(y, body.BottomY(t) + 0.02f, body.TopY(t) - 0.02f));
            return x > 0f ? x : body.HalfW(t);
        }

        /// <summary>Kotfluegel-Verbreiterung: Bogenband (innen offen) auf der Karosserieseite.</summary>
        static Mesh OverFenderMesh(CarBody body, float zc, int side)
        {
            const int seg = 20;
            float rIn = body.archR - 0.005f, rOut = body.archR + 0.085f, depth = 0.075f;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(8f, 172f, i / (float)seg) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));
                Vector3 c = new Vector3(0f, body.R, zc);
                float yMid = body.R + Mathf.Sin(a) * (rIn + rOut) * 0.5f, zMid = zc + Mathf.Cos(a) * (rIn + rOut) * 0.5f;
                float x0 = FenderX(body, zMid, yMid) - 0.02f;
                Vector3 inner = c + dir * rIn, outer = c + dir * rOut;
                // 4 Punkte je Schnitt: innen/aussen, an der Karosserie und aussen vorstehend
                verts.Add(new Vector3(side * x0, inner.y, inner.z));
                verts.Add(new Vector3(side * (x0 + depth), inner.y, inner.z));
                verts.Add(new Vector3(side * (x0 + depth * 0.7f), outer.y, outer.z));
                verts.Add(new Vector3(side * x0, outer.y, outer.z));
                if (i < seg)
                {
                    int k = i * 4, n = k + 4;
                    // Unterseite (zum Rad), Aussenflaeche, Oberseite (zur Karosserie)
                    void Q(int a0, int a1, int b1, int b0)
                    {
                        if (side > 0) { tris.AddRange(new[] { a0, b0, b1, a0, b1, a1 }); }
                        else { tris.AddRange(new[] { a0, a1, b1, a0, b1, b0 }); }
                    }
                    Q(k + 0, k + 1, n + 1, n + 0);
                    Q(k + 1, k + 2, n + 2, n + 1);
                    Q(k + 2, k + 3, n + 3, n + 2);
                }
            }
            var m = new Mesh { name = "OverFender" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            return m;
        }

        static void Pipe(Transform v, Vector3 p, float dia)
        {
            Shapes.Part(PrimitiveType.Cylinder, v, p, new Vector3(dia, 0.09f, dia), Palette.Metal, 0.2f, new Vector3(90f, 0f, 0f), name: "Exhaust");
            Shapes.Part(PrimitiveType.Cylinder, v, p + Vector3.back * 0.085f, new Vector3(dia * 0.7f, 0.01f, dia * 0.7f), Palette.Hex("0E0D12"), 0f, new Vector3(90f, 0f, 0f), name: "ExhaustHole");
        }

        static void Wing(Transform v, float width, float deckY, float z, float height, float chord, Color color, bool hoop, bool swan = false)
        {
            float wy = deckY + height;
            var mat = ToonMaterials.Get(color, 0.3f, true);
            for (int side = -1; side <= 1; side += 2)
            {
                float sx = side * width * (hoop ? 0.46f : 0.3f);
                if (swan)
                {
                    Shapes.Box(v, new Vector3(sx, deckY + height * 0.5f, z + 0.02f), new Vector3(0.035f, height + 0.04f, 0.1f), Trim, 0.15f, name: "WingStrut");
                    Shapes.Box(v, new Vector3(sx, wy + 0.05f, z - chord * 0.15f), new Vector3(0.035f, 0.06f, chord * 0.5f), Trim, 0.15f, name: "WingStrut");
                }
                else
                {
                    var strut = Shapes.Box(v, new Vector3(sx, deckY + height * 0.5f, z), new Vector3(hoop ? 0.07f : 0.035f, height, hoop ? 0.16f : 0.12f), hoop ? color : Trim, 0.2f, name: "WingStrut");
                    if (hoop) strut.GetComponent<Renderer>().sharedMaterial = mat;
                }
                if (!hoop) Shapes.Box(v, new Vector3(side * width * 0.5f, wy + 0.02f, z - 0.02f), new Vector3(0.02f, 0.16f, chord * 1.1f), color, 0.2f, name: "WingPlate");
            }
            var foil = Shapes.Box(v, new Vector3(0f, wy, z - 0.02f), new Vector3(width, 0.04f, chord), color, 0.3f, name: "Wing");
            foil.transform.localRotation = Quaternion.Euler(-7f, 0f, 0f);
        }

        /// <summary>Carbon-Motorhaube: duenne Schale auf der Haubenflaeche.</summary>
        static Mesh HoodPatch(CarBody body)
        {
            const int nt = 14, nx = 9;
            float t0 = body.s.CowlT + 0.012f, t1 = 0.475f;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= nt; i++)
            {
                float t = Mathf.Lerp(t0, t1, i / (float)nt);
                float half = body.HalfW(t) * 0.72f;
                for (int j = 0; j <= nx; j++)
                {
                    float x = Mathf.Lerp(-half, half, j / (float)nx);
                    verts.Add(new Vector3(x, body.TopSurfaceY(t, x) + 0.006f, t * body.L));
                    if (i < nt && j < nx)
                    {
                        int k = i * (nx + 1) + j;
                        tris.AddRange(new[] { k, k + 1, k + nx + 2, k, k + nx + 2, k + nx + 1 });
                    }
                }
            }
            var m = new Mesh { name = "CarbonHood" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            // Normalen nach oben ausrichten (Wicklung)
            var n = m.normals;
            if (n.Length > 0 && n[n.Length / 2].y < 0f)
            {
                for (int i = 0; i < tris.Count; i += 3) { int t = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = t; }
                m.SetTriangles(tris, 0);
                m.RecalculateNormals();
            }
            return m;
        }

        // ------------------------------------------------------------------ Felgen

        static void BuildWheel(Transform spin, float r, float width, float side, int style, Color rimColor)
        {
            Shapes.Part(PrimitiveType.Cylinder, spin, Vector3.zero, new Vector3(2f * r, width * 0.5f, 2f * r), Palette.Rubber, 0.3f, new Vector3(0, 0, 90), name: "Tire");
            // Felge vor der Reifenflanke: alles liegt aussen auf (der Reifen ist ein voller Zylinder)
            float baseX = side * (width * 0.5f + 0.002f);
            float rimR = r * 0.74f;
            Color barrel = Palette.Hex("2E2D36");
            Color polished = Palette.Hex("E4E6EE");

            // Helle Felgenteile leuchten leicht, damit Silber auch im Schatten silbern bleibt
            float Em(Color c) => c.grayscale > 0.35f ? 0.22f : 0f;

            void Disc(float radius, float offset, float thick, Color c, string name) =>
                Shapes.Part(PrimitiveType.Cylinder, spin, new Vector3(baseX + side * (offset + thick * 0.5f), 0, 0), new Vector3(radius * 2f, thick * 0.5f, radius * 2f), c, 0.1f, new Vector3(0, 0, 90), emission: Em(c), name: name);

            void Lip(float radius, float width2, float offset, float depth, Color c)
            {
                // Felgenhorn als Ring aus Segmenten (innen offen, damit man die Speichen sieht)
                const int seg = 18;
                for (int i = 0; i < seg; i++)
                {
                    float a = (i + 0.5f) / seg * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));
                    var b = Shapes.Box(spin, new Vector3(baseX + side * (offset + depth * 0.5f), 0f, 0f) + dir * (radius - width2 * 0.5f),
                                       new Vector3(radius * Mathf.PI * 2f / seg * 1.08f, depth, width2), c, 0f, emission: Em(c), name: "RimLip");
                    b.transform.localRotation = Quaternion.LookRotation(dir, Vector3.right);
                }
            }

            void Spoke(float angle, float r0, float r1, float w, float offset, float thick, Color c)
            {
                float a = angle * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));
                var sp = Shapes.Box(spin, new Vector3(baseX + side * (offset + thick * 0.5f), 0f, 0f) + dir * (r0 + r1) * 0.5f, new Vector3(w, thick, r1 - r0), c, 0f, emission: Em(c), name: "Spoke");
                sp.transform.localRotation = Quaternion.LookRotation(dir, Vector3.right);
            }

            Disc(rimR, 0f, 0.004f, barrel, "Barrel");
            switch (style)
            {
                case 0: // Sechs-Speiche (TE-Stil)
                    Lip(rimR, r * 0.08f, 0.004f, 0.02f, rimColor);
                    for (int i = 0; i < 6; i++) Spoke(i * 60f, r * 0.14f, rimR * 0.95f, r * 0.17f, 0.004f, 0.022f, rimColor);
                    Disc(r * 0.19f, 0.004f, 0.03f, rimColor, "Hub");
                    Disc(r * 0.08f, 0.034f, 0.006f, Palette.Hex("15141A"), "Cap");
                    break;
                case 1: // Mesh: viele duenne Kreuzspeichen, polierter Rand
                    Lip(rimR, r * 0.07f, 0.004f, 0.022f, polished);
                    for (int i = 0; i < 10; i++)
                    {
                        Spoke(i * 36f + 8f, r * 0.16f, rimR * 0.94f, r * 0.05f, 0.004f, 0.012f, rimColor);
                        Spoke(i * 36f - 8f, r * 0.16f, rimR * 0.94f, r * 0.05f, 0.012f, 0.012f, rimColor);
                    }
                    Disc(r * 0.22f, 0.004f, 0.028f, rimColor, "Hub");
                    Disc(r * 0.09f, 0.032f, 0.006f, polished, "Cap");
                    break;
                case 2: // Deep Dish: breites poliertes Bett, Speichen tief innen
                    Lip(rimR, r * 0.2f, 0.004f, 0.05f, polished);
                    for (int i = 0; i < 5; i++) Spoke(i * 72f, r * 0.14f, rimR * 0.76f, r * 0.17f, 0.004f, 0.02f, rimColor);
                    Disc(r * 0.18f, 0.004f, 0.026f, rimColor, "Hub");
                    break;
                case 3: // Fuenf-Speiche
                    Lip(rimR, r * 0.07f, 0.004f, 0.02f, rimColor);
                    for (int i = 0; i < 5; i++) Spoke(i * 72f + 36f, r * 0.14f, rimR * 0.96f, r * 0.24f, 0.004f, 0.022f, rimColor);
                    Disc(r * 0.2f, 0.004f, 0.03f, rimColor, "Hub");
                    Disc(r * 0.08f, 0.034f, 0.006f, polished, "Cap");
                    break;
                default: // Stahlfelge: geschlossene Scheibe mit Loechern
                    Disc(rimR, 0.004f, 0.012f, rimColor, "SteelDisc");
                    for (int i = 0; i < 6; i++)
                    {
                        float a = (i * 60f + 30f) * Mathf.Deg2Rad;
                        Shapes.Part(PrimitiveType.Cylinder, spin, new Vector3(baseX + side * 0.018f, Mathf.Sin(a) * rimR * 0.6f, Mathf.Cos(a) * rimR * 0.6f),
                                    new Vector3(r * 0.17f, 0.002f, r * 0.17f), Palette.Hex("15141A"), 0f, new Vector3(0, 0, 90), name: "RimHole");
                    }
                    Disc(r * 0.24f, 0.016f, 0.02f, polished, "HubCap");
                    break;
            }
        }

        // ------------------------------------------------------------------ Kennzeichen

        static readonly Dictionary<string, Material> _plates = new Dictionary<string, Material>();

        static Material PlateMaterial(CarDef def)
        {
            if (_plates.TryGetValue(def.id, out var m) && m != null) return m;
            string num = def.id switch { "roku86" => "86-27", "sylph15" => "15-03", "kazefc" => "13-88", "mark2j" => "10-07", "toro2j" => "80-08", _ => "34-26" };
            m = ToonMaterials.CreateDecal(StickerLibrary.Plate(num));
            m.SetFloat("_Cutoff", 0f);
            _plates[def.id] = m;
            return m;
        }
    }

    /// <summary>Haelt die Folien-Textur eines Autos und gibt sie beim Abbau wieder frei.</summary>
    public class CarLivery : MonoBehaviour
    {
        CarBody _body;
        Texture _graffiti;
        RenderTexture _rt;
        public Material Material { get; private set; }

        public void Init(CarBody body, Color paint, CarDesign design, Texture graffiti)
        {
            _body = body;
            _graffiti = graffiti;
            _rt = LiveryRenderer.Create();
            Material = new Material(ToonMaterials.Get(Color.white, 0.4f, false)) { name = "CarPaint" };
            Material.SetTexture("_BaseMap", _rt);
            Render(paint, design);
        }

        public void Render(Color paint, CarDesign design)
        {
            if (_rt == null) return;
            if (!_rt.IsCreated()) _rt.Create();
            LiveryRenderer.Render(_rt, _body, paint, design, _graffiti);
        }

        void OnDestroy()
        {
            if (_rt != null) { _rt.Release(); Shapes.DestroySafe(_rt); }
            if (Material != null) Shapes.DestroySafe(Material);
        }
    }
}
