using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Strassen-Deko auf allen Gehwegen: an der Bordsteinkante Muelleimer, Hydranten, Zeitungskaesten und Poller,
    /// an Hauswaenden Getraenkeautomaten (leuchten), Muellcontainer mit Saecken, Recycling-Tonnen und Fahrradstaender.
    /// Muelleimer und Tonnen sind lose (Rigidbody) und lassen sich umfahren. Eigener Zufall (_deco); vor jedem Teil
    /// wird geprueft, ob Platz ist (Laternen, Tag-Spots und das Sofa der Stoner bleiben frei).
    /// </summary>
    public partial class CityBuilder
    {
        readonly System.Random _deco = new System.Random(8080);
        readonly List<Vector3> _tagSpots = new List<Vector3>();
        float D(float min, float max) => min + (float)_deco.NextDouble() * (max - min);
        T DPick<T>(T[] arr) => arr[_deco.Next(arr.Length)];

        static readonly Color TrashBag = Palette.Hex("2B2440"), TrashBag2 = Palette.Hex("3D3358");

        void BuildStreetProps()
        {
            var props = Shapes.Group(_root, "StreetProps");
            Physics.SyncTransforms();
            Vector3[] sides = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            for (int row = 0; row < Grid; row++)
            {
                for (int col = 0; col < Grid; col++)
                {
                    bool houses = Map[row][col] == 'B';
                    var c = new Vector3(BlockCenter(col), 0, BlockCenter(row));
                    foreach (var side in sides)
                    {
                        Vector3 along = Vector3.Cross(Vector3.up, side);
                        for (float o = -29f; o <= 29f; o += 6.5f)
                        {
                            float p = o + D(-1.2f, 1.2f);
                            if (_deco.NextDouble() < 0.3) TryCurbProp(props, c + side * (Block * 0.5f - 0.75f) + along * p, side, along);
                            if (houses && _deco.NextDouble() < 0.3) TryWallProp(props, c + side * (Block * 0.5f - 2.5f) + along * p, side, along);
                        }
                    }
                }
            }
        }

        /// <summary>Gehweg unter p (nicht Fahrbahn, Rampe oder Platz-Boden)? Liefert die Hoehe.</summary>
        static bool OnSidewalk(Vector3 p, out float y)
        {
            y = 0f;
            int ground = ~LayerMask.GetMask("Skater", "Car", "Ignore Raycast", "Rail");
            if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 4f, ground, QueryTriggerInteraction.Ignore)) return false;
            if (hit.point.y < RingHeight - 0.03f || hit.point.y > 0.25f || hit.collider.name != "Sidewalk") return false;
            y = hit.point.y;
            return true;
        }

        bool NearTagSpot(Vector3 p, float r)
        {
            foreach (var t in _tagSpots)
                if (Vector3.ProjectOnPlane(t - p, Vector3.up).sqrMagnitude < r * r) return true;
            return false;
        }

        void TryCurbProp(Transform parent, Vector3 pos, Vector3 side, Vector3 along)
        {
            if (!OnSidewalk(pos, out float y) || NearHangout(pos) || NearTagSpot(pos, 3f)) return;
            pos.y = y;
            if (!Free(pos, new Vector3(0.6f, 0.6f, 0.6f), Quaternion.identity)) return;
            double r = _deco.NextDouble();
            if (r < 0.55) StreetBin(parent, pos);
            else if (r < 0.75) Hydrant(parent, pos);
            else if (r < 0.9) NewsBox(parent, pos, side);
            else Bollards(parent, pos, along);
        }

        /// <summary>Vor einer Hauswand: von der Gehwegmitte zur Hausseite suchen und das Teil davor stellen.</summary>
        void TryWallProp(Transform parent, Vector3 probe, Vector3 side, Vector3 along)
        {
            if (!OnSidewalk(probe, out float y) || NearHangout(probe)) return;
            int mask = ~LayerMask.GetMask("Skater", "Car", "Ignore Raycast", "Rail");
            if (!Physics.Raycast(probe + Vector3.up * 1f, -side, out RaycastHit hit, 3.5f, mask, QueryTriggerInteraction.Ignore)) return;
            if (hit.collider.name != "Building" || NearTagSpot(hit.point, 4f)) return;
            double r = _deco.NextDouble();
            float depth = r < 0.35 ? 0.8f : r < 0.65 ? 1.15f : 0.65f;
            float width = r < 0.35 ? 1.0f : r < 0.65 ? 3.2f : r < 0.9 ? 2.0f : 2.2f;
            Vector3 pos = new Vector3(hit.point.x, y, hit.point.z) + side * (depth * 0.5f + 0.06f);
            if (!OnSidewalk(pos, out _)) return;
            Quaternion rot = Quaternion.LookRotation(side);
            if (!Free(pos, new Vector3(width * 0.5f, 0.8f, depth * 0.5f), rot)) return;
            if (r < 0.35) VendingMachine(parent, pos, side);
            else if (r < 0.65) Dumpster(parent, pos, side, along);
            else if (r < 0.9) RecyclingBins(parent, pos, side, along);
            else BikeRack(parent, pos, side);
        }

        /// <summary>Kleinteile ohne eigenen Schatten (die grossen Formen werfen ihn schon).</summary>
        static readonly HashSet<string> NoShadow = new HashSet<string>
        {
            "CrownPuff", "CrownTop", "Branch", "TrunkBase", "Bush", "Flower", "HedgeTop", "Soil", "Grass",
            "BenchSlat", "BenchLeg", "BenchBackPost", "BenchBack", "BinBody", "BinLid", "Trash",
            "FountainBowl", "FountainStem", "FountainBowlTop", "FountainBall"
        };

        /// <summary>Kleine Deko auf die Ebene "Detail" (wird nur in der Naehe gezeichnet).</summary>
        static readonly HashSet<string> DetailNames = new HashSet<string>
        {
            "Bush", "Flower", "BenchSlat", "BenchLeg", "BenchBackPost", "BenchBack", "BinBody", "BinLid", "Trash"
        };

        /// <summary>
        /// Leistung: Kleinteile werfen keinen Schatten (sonst wird jedes Teil pro Schatten-Kaskade nochmal gezeichnet),
        /// Strassen-Deko und Kleinkram kommen auf die Ebene "Detail", die die Kamera nur bis 85 m zeichnet.
        /// Ausnahmen: Automat und Container werfen weiter Schatten.
        /// </summary>
        void OptimizeDetails()
        {
            int detail = LayerMask.NameToLayer("Detail");
            var street = _root.Find("StreetProps");
            foreach (var r in _root.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name;
                bool inStreet = street != null && r.transform.IsChildOf(street);
                bool keepShadow = n == "VendingBody" || n == "DumpsterBody";
                if (!keepShadow && (inStreet || NoShadow.Contains(n))) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (detail >= 0 && (inStreet || DetailNames.Contains(n))) r.gameObject.layer = detail;
            }
            if (detail < 0) Debug.LogWarning("[DriftSkate] Layer 'Detail' fehlt (ProjectBuilder.SetupLayers).");
        }

        // ------------------------------------------------------------------ Teile

        /// <summary>Runder Strassen-Muelleimer, lose (laesst sich umfahren). Ab und zu quillt Muell oben raus.</summary>
        void StreetBin(Transform parent, Vector3 pos)
        {
            var b = Shapes.Group(parent, "StreetBin");
            b.position = pos;
            b.rotation = Quaternion.Euler(0f, D(0f, 360f), 0f);
            Color body = DPick(new[] { Palette.Teal, Palette.Teal, Palette.Hex("4A6FD8"), Palette.Hex("8C5CFF") });
            Shapes.Part(PrimitiveType.Cylinder, b, new Vector3(0f, 0.45f, 0f), new Vector3(0.55f, 0.45f, 0.55f), body, 0.3f, name: "BinBody");
            Shapes.Part(PrimitiveType.Cylinder, b, new Vector3(0f, 0.93f, 0f), new Vector3(0.64f, 0.04f, 0.64f), Palette.Ink, 0.2f, name: "BinLid");
            if (_deco.NextDouble() < 0.35)
            {
                Shapes.Box(b, new Vector3(0.08f, 1.0f, 0.05f), new Vector3(0.28f, 0.12f, 0.2f), Palette.White, 0f, new Vector3(8f, 25f, 12f), name: "Trash");
                Shapes.Box(b, new Vector3(-0.1f, 1.02f, -0.06f), new Vector3(0.18f, 0.2f, 0.12f), DPick(new[] { Palette.Red, Palette.Yellow, Palette.Pink }), 0f, new Vector3(-10f, 60f, 5f), name: "Trash");
            }
            Loose(b, new Vector3(0f, 0.47f, 0f), new Vector3(0.6f, 0.94f, 0.6f), 18f);
        }

        /// <summary>Lose machen: Box-Collider am Ganzen (kein Kullern wie bei Kapseln) und Rigidbody.</summary>
        static void Loose(Transform t, Vector3 center, Vector3 size, float mass)
        {
            var col = t.gameObject.AddComponent<BoxCollider>();
            col.center = center;
            col.size = size;
            var rb = t.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.3f;
            rb.angularDamping = 0.6f;
            rb.Sleep();
        }

        void Hydrant(Transform parent, Vector3 pos)
        {
            var h = Shapes.Group(parent, "Hydrant");
            h.position = pos;
            h.rotation = Quaternion.Euler(0f, D(0f, 360f), 0f);
            Shapes.Part(PrimitiveType.Cylinder, h, new Vector3(0f, 0.04f, 0f), new Vector3(0.42f, 0.04f, 0.42f), Palette.Shade(Palette.Red, 0.7f), 0.2f, name: "HydrantFoot");
            Shapes.Part(PrimitiveType.Cylinder, h, new Vector3(0f, 0.32f, 0f), new Vector3(0.3f, 0.28f, 0.3f), Palette.Red, 0.3f, keepCollider: true, name: "HydrantBody");
            Shapes.Part(PrimitiveType.Cylinder, h, new Vector3(0f, 0.6f, 0f), new Vector3(0.36f, 0.04f, 0.36f), Palette.Shade(Palette.Red, 0.7f), 0.2f, name: "HydrantRing");
            Shapes.Part(PrimitiveType.Sphere, h, new Vector3(0f, 0.64f, 0f), new Vector3(0.3f, 0.24f, 0.3f), Palette.Red, 0.3f, name: "HydrantTop");
            Shapes.Part(PrimitiveType.Cylinder, h, new Vector3(0f, 0.79f, 0f), new Vector3(0.08f, 0.04f, 0.08f), Palette.Yellow, 0f, name: "HydrantNut");
            for (int s = -1; s <= 1; s += 2)
                Shapes.Part(PrimitiveType.Cylinder, h, new Vector3(s * 0.19f, 0.38f, 0f), new Vector3(0.12f, 0.06f, 0.12f), Palette.Yellow, 0.2f, new Vector3(0f, 0f, 90f), name: "HydrantNozzle");
        }

        void NewsBox(Transform parent, Vector3 pos, Vector3 facing)
        {
            var n = Shapes.Group(parent, "NewsBox");
            n.position = pos;
            n.rotation = Quaternion.LookRotation(facing);
            Color col = DPick(new[] { Palette.Blue, Palette.Yellow, Palette.Red, Palette.Lime });
            Shapes.Box(n, new Vector3(0f, 0.08f, 0f), new Vector3(0.44f, 0.16f, 0.4f), Palette.Ink, 0.15f, name: "NewsLeg");
            Shapes.Box(n, new Vector3(0f, 0.6f, 0f), new Vector3(0.52f, 0.88f, 0.46f), col, 0.3f, collider: true, name: "NewsBody");
            Shapes.Box(n, new Vector3(0f, 0.78f, 0.235f), new Vector3(0.4f, 0.3f, 0.02f), Palette.Cream, 0f, emission: 0.35f, name: "NewsWindow");
            Shapes.Box(n, new Vector3(0f, 1.06f, 0f), new Vector3(0.56f, 0.05f, 0.5f), Palette.Shade(col, 0.7f), 0.2f, name: "NewsTop");
        }

        void Bollards(Transform parent, Vector3 pos, Vector3 along)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 p = pos + along * (s * 0.7f);
                Shapes.Part(PrimitiveType.Cylinder, parent, p + Vector3.up * 0.45f, new Vector3(0.22f, 0.45f, 0.22f), Palette.Ink, 0.2f, keepCollider: true, name: "Bollard");
                Shapes.Part(PrimitiveType.Cylinder, parent, p + Vector3.up * 0.72f, new Vector3(0.24f, 0.05f, 0.24f), Palette.Yellow, 0f, emission: 0.3f, name: "BollardBand");
            }
        }

        /// <summary>Getraenkeautomat mit leuchtender Front und bunten Dosen dahinter.</summary>
        void VendingMachine(Transform parent, Vector3 pos, Vector3 facing)
        {
            var v = Shapes.Group(parent, "VendingMachine");
            v.position = pos;
            v.rotation = Quaternion.LookRotation(facing);
            Color col = DPick(new[] { Palette.Red, Palette.Blue, Palette.White, Palette.Pink });
            Shapes.Box(v, new Vector3(0f, 0.95f, 0f), new Vector3(1.0f, 1.9f, 0.8f), col, 0.4f, name: "VendingBody");
            var glow = Shapes.Box(v, new Vector3(-0.08f, 1.2f, 0.405f), new Vector3(0.66f, 1.0f, 0.03f), Palette.Cream, 0f, name: "VendingGlow");
            glow.GetComponent<Renderer>().sharedMaterial = ToonMaterials.GetTextured("vend_front", Color.white, 0.25f, 1.3f);
            Shapes.Box(v, new Vector3(0.36f, 1.25f, 0.41f), new Vector3(0.16f, 0.5f, 0.03f), Palette.Ink, 0f, name: "VendingPanel");
            Shapes.Box(v, new Vector3(0f, 0.35f, 0.41f), new Vector3(0.62f, 0.18f, 0.03f), Palette.Ink, 0f, name: "VendingSlot");
            Shapes.Box(v, new Vector3(0f, 1.82f, 0.41f), new Vector3(0.9f, 0.12f, 0.03f), Palette.Shade(col, 0.6f), 0f, emission: 0.8f, name: "VendingSign");
            var col2 = v.gameObject.AddComponent<BoxCollider>();
            col2.center = new Vector3(0f, 0.95f, 0f);
            col2.size = new Vector3(1.0f, 1.9f, 0.8f);
        }

        /// <summary>Muellcontainer auf Rollen, Deckel leicht offen, Muellsaecke daneben.</summary>
        void Dumpster(Transform parent, Vector3 pos, Vector3 facing, Vector3 along)
        {
            var d = Shapes.Group(parent, "Dumpster");
            d.position = pos;
            d.rotation = Quaternion.LookRotation(facing);
            Color col = DPick(new[] { Palette.Hex("2F8F5B"), Palette.Hex("3D6FB5"), Palette.Hex("C2552E") });
            Shapes.Box(d, new Vector3(0f, 0.7f, 0f), new Vector3(1.9f, 1.1f, 1.05f), col, 0.4f, name: "DumpsterBody");
            Shapes.Box(d, new Vector3(0f, 1.3f, -0.04f), new Vector3(1.98f, 0.08f, 1.15f), Palette.Shade(col, 0.65f), 0.3f, new Vector3(-7f, 0f, 0f), name: "DumpsterLid");
            Shapes.Box(d, new Vector3(0f, 0.95f, 0.54f), new Vector3(1.94f, 0.1f, 0.04f), Palette.Shade(col, 0.65f), 0.2f, name: "DumpsterRim");
            for (int i = 0; i < 4; i++)
                Shapes.Part(PrimitiveType.Cylinder, d, new Vector3(i % 2 == 0 ? -0.8f : 0.8f, 0.09f, i < 2 ? -0.38f : 0.38f), new Vector3(0.18f, 0.04f, 0.18f), Palette.Ink, 0.1f, new Vector3(0f, 0f, 90f), name: "Wheel");
            var col2 = d.gameObject.AddComponent<BoxCollider>();
            col2.center = new Vector3(0f, 0.7f, 0f);
            col2.size = new Vector3(1.95f, 1.35f, 1.1f);

            // Saecke links oder rechts daneben
            float s = _deco.NextDouble() < 0.5 ? -1f : 1f;
            int bags = _deco.Next(1, 4);
            for (int i = 0; i < bags; i++)
            {
                Vector3 bp = pos + along * (s * (1.35f + i * 0.45f)) + facing * D(-0.25f, 0.25f);
                float sz = D(0.5f, 0.68f);
                Shapes.Part(PrimitiveType.Sphere, parent, bp + Vector3.up * sz * 0.4f, new Vector3(sz, sz * 0.8f, sz * 0.9f), i % 2 == 0 ? TrashBag : TrashBag2, 0.35f, name: "TrashBag");
                Shapes.Part(PrimitiveType.Sphere, parent, bp + Vector3.up * sz * 0.82f, Vector3.one * 0.14f, i % 2 == 0 ? TrashBag : TrashBag2, 0f, name: "TrashBagKnot");
            }
        }

        /// <summary>Drei Recycling-Tonnen (gelb, blau, gruen), jede lose.</summary>
        void RecyclingBins(Transform parent, Vector3 pos, Vector3 facing, Vector3 along)
        {
            Color[] cols = { Palette.Yellow, Palette.Blue, Palette.Lime };
            for (int i = 0; i < 3; i++)
            {
                var r = Shapes.Group(parent, "RecyclingBin");
                r.position = pos + along * ((i - 1) * 0.68f);
                r.rotation = Quaternion.LookRotation(facing) * Quaternion.Euler(0f, D(-4f, 4f), 0f);
                Shapes.Box(r, new Vector3(0f, 0.5f, 0f), new Vector3(0.6f, 1.0f, 0.6f), cols[i], 0.3f, name: "RecyclingBody");
                Shapes.Box(r, new Vector3(0f, 1.03f, 0f), new Vector3(0.64f, 0.07f, 0.64f), Palette.Shade(cols[i], 0.65f), 0.2f, name: "RecyclingLid");
                Shapes.Part(PrimitiveType.Cylinder, r, new Vector3(0f, 0.62f, 0.305f), new Vector3(0.3f, 0.01f, 0.3f), Palette.White, 0f, new Vector3(90f, 0f, 0f), name: "RecyclingLabel");
                Loose(r, new Vector3(0f, 0.53f, 0f), new Vector3(0.6f, 1.06f, 0.6f), 14f);
            }
        }

        void BikeRack(Transform parent, Vector3 pos, Vector3 facing)
        {
            var b = Shapes.Group(parent, "BikeRack");
            b.position = pos;
            b.rotation = Quaternion.LookRotation(facing);
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 0.75f;
                for (int s = -1; s <= 1; s += 2)
                    Shapes.Part(PrimitiveType.Cylinder, b, new Vector3(x, 0.4f, s * 0.3f), new Vector3(0.06f, 0.4f, 0.06f), Palette.Metal, 0.15f, name: "RackPost");
                Shapes.Part(PrimitiveType.Cylinder, b, new Vector3(x, 0.8f, 0f), new Vector3(0.06f, 0.3f, 0.06f), Palette.Metal, 0.15f, new Vector3(90f, 0f, 0f), name: "RackTop");
            }
            var col = b.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.42f, 0f);
            col.size = new Vector3(1.7f, 0.84f, 0.7f);
        }
    }
}
