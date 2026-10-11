using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Bootsrampe am West-Kai (gegenueber der Bruecke), beim Laden der Stadt gebaut, Szene bleibt unveraendert:
    /// In der Mitte der Hafenoeffnung bekommt das gelbe Gelaender eine 10 m breite Luecke (altes Gelaender aus, zwei
    /// kuerzere Stuecke mit Grind-Linie neu), die unsichtbare Kai-Wand wird geteilt, Poller in der Luecke weg, und eine
    /// Betonrampe mit gelben Bordkanten fuehrt von der Kaikante ins Wasser. Wer da runterfaehrt, schwimmt (Floater).
    /// </summary>
    public static class HarborSlipway
    {
        public const float HalfWidth = 5f, Length = 12f, Drop = 2.6f;
        /// <summary>Oberkante der Rampe an der Kaikante (Mitte), fuer Tests.</summary>
        public static Vector3 Top { get; private set; }
        public static bool Built { get; private set; }

        public static void Build()
        {
            Built = false;
            var perimeter = GameObject.Find("Perimeter");
            if (perimeter == null) { Debug.LogWarning("HarborSlipway: Kai fehlt"); return; }
            Vector3 outward = Vector3.left, along = Vector3.Cross(Vector3.up, outward); // along = +z
            float half = CityBuilder.Half;
            float edgeX = -(half + 4f);
            var group = new GameObject("HarborSlipway").transform;

            // Kai-Wand teilen, Poller in der Luecke aus
            foreach (Transform t in perimeter.transform)
            {
                Vector3 p = t.position;
                if (p.x > -half || Mathf.Abs(p.z) > 50f) continue;
                if (t.name == "Bollard" && Mathf.Abs(p.z) < HalfWidth + 0.8f) t.gameObject.SetActive(false);
                else if (t.name == "Boundary" && t.TryGetComponent(out BoxCollider wall) && wall.size.z > 60f)
                {
                    float piece = wall.size.z * 0.5f - (HalfWidth + 0.5f);
                    wall.enabled = false;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var go = new GameObject("BoundaryPart");
                        go.layer = t.gameObject.layer;
                        go.transform.SetParent(t.parent, false);
                        go.transform.position = p + Vector3.forward * (s * (HalfWidth + 0.5f + piece * 0.5f));
                        go.AddComponent<BoxCollider>().size = new Vector3(wall.size.x, wall.size.y, piece);
                    }
                }
            }

            // Gelaender: das alte Stueck ueber die ganze Oeffnung aus, zwei neue links und rechts der Luecke
            GrindRail old = null;
            foreach (var r in GrindRail.All)
            {
                if (r == null || r.label != "QUAY RAIL") continue;
                var bar = r.transform.Find("Bar");
                if (bar != null && bar.position.x < -half - 2f && Mathf.Abs(bar.position.z) < 10f) { old = r; break; }
            }
            if (old != null)
            {
                var bar = old.transform.Find("Bar");
                Vector3 c = bar.position;
                float len = bar.localScale.y * 2f;
                var barMat = bar.GetComponent<Renderer>().sharedMaterial;
                var post = old.transform.Find("Post");
                var postMat = post != null ? post.GetComponent<Renderer>().sharedMaterial : ToonMaterials.Get(Palette.Ink, 0.15f, false);
                old.gameObject.SetActive(false);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 a = c + along * (s * (HalfWidth + 0.6f));
                    Vector3 b = c + along * (s * len * 0.5f);
                    Rail(group, a, b, barMat, postMat, old.gameObject.layer);
                }
            }

            // Rampe: Betonplatte von der Kaikante (Hoehe 0) 2,6 m runter ins Wasser, gelbe Bordkanten an den Seiten
            var root = new GameObject("Slipway").transform;
            root.SetParent(group, false);
            root.SetPositionAndRotation(new Vector3(edgeX, 0f, 0f), Quaternion.LookRotation(outward, Vector3.up)); // lokal +Z = raus aufs Wasser, lokal +X = Welt +Z
            Slab(root, "Ramp", MeshFactory.RampSlab(HalfWidth * 2f, Length, -Drop, 3f, 1.5f, 0f), ToonMaterials.Get(Palette.Concrete, 0.4f, true, 0f, Surface.Concrete), 0f, true);
            var curb = MeshFactory.RampSlab(0.4f, Length - 1f, -Drop * (Length - 1f) / Length, 3f, 0.4f, 0.3f);
            var curbMat = ToonMaterials.Get(Palette.Yellow, 0.3f);
            Slab(root, "Curb", curb, curbMat, HalfWidth - 0.2f, true);
            Slab(root, "Curb", curb, curbMat, -HalfWidth + 0.2f, true);
            // Warnstreifen auf dem Kai vor der Rampe
            var stripes = new MeshFactory.BoxBatch();
            for (int i = 0; i < 8; i++)
                stripes.Add(new Vector3(edgeX + 1.2f, 0.02f, -HalfWidth + 0.7f + i * 1.25f), new Vector3(1.6f, 0.03f, 0.6f), 45f);
            stripes.Build(group, "Stripes", Palette.Yellow, "slipway_stripes", 0.2f);
            Top = new Vector3(edgeX, 0f, 0f);
            Built = true;
        }

        static void Rail(Transform parent, Vector3 a, Vector3 b, Material barMat, Material postMat, int layer)
        {
            var go = new GameObject("Grind_QUAY RAIL");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.layer = layer;
            var rail = go.AddComponent<GrindRail>();
            rail.label = "QUAY RAIL";
            rail.points = new[] { a + Vector3.up * 0.04f, b + Vector3.up * 0.04f };
            Vector3 d = b - a;
            var bar = Shapes.Part(PrimitiveType.Cylinder, go.transform, (a + b) * 0.5f, new Vector3(0.09f, d.magnitude * 0.5f, 0.09f), Color.white, 0.2f, keepCollider: true, name: "Bar");
            bar.transform.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            bar.GetComponent<Renderer>().sharedMaterial = barMat;
            bar.layer = layer;
            int count = Mathf.Max(2, Mathf.CeilToInt(d.magnitude / 3f) + 1);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)(count - 1));
                float h = Mathf.Max(0.1f, p.y);
                var post = Shapes.Part(PrimitiveType.Cylinder, go.transform, new Vector3(p.x, p.y - h * 0.5f, p.z), new Vector3(0.07f, h * 0.5f, 0.07f), Color.white, 0.15f, keepCollider: true, name: "Post");
                post.GetComponent<Renderer>().sharedMaterial = postMat;
                post.layer = layer;
            }
            go.SetActive(true);
        }

        static void Slab(Transform parent, string name, Mesh mesh, Material material, float x, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(x, 0f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }
    }
}
