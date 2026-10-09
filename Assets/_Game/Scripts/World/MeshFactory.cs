using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>Prozedurale Meshes fuer Skate-Elemente. Alle sind um ihren Bounding-Box-Mittelpunkt zentriert.</summary>
    public static class MeshFactory
    {
        static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        /// <summary>Sammelt Dreiecke mit flachen Normalen und dreht sie automatisch nach aussen.</summary>
        class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();
            /// <summary>Nur dann UVs ins Mesh schreiben (alle Dreiecke muessen ueber Tri/Quad gekommen sein).</summary>
            public bool hasUV;

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward) => Tri(a, b, c, outward, Vector2.zero, Vector2.zero, Vector2.zero);

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Vector2 ua, Vector2 ub, Vector2 uc)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(normal, outward) < 0f) { (b, c) = (c, b); (ub, uc) = (uc, ub); normal = -normal; }
                normal.Normalize();
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                n.Add(normal); n.Add(normal); n.Add(normal);
                uv.Add(ua); uv.Add(ub); uv.Add(uc);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
            {
                Tri(a, b, c, outward);
                Tri(a, c, d, outward);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                Tri(a, b, c, outward, ua, ub, uc);
                Tri(a, c, d, outward, ua, uc, ud);
            }

            public Mesh Build(string key)
            {
                var mesh = new Mesh { name = key };
                if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(v);
                mesh.SetNormals(n);
                if (hasUV && uv.Count == v.Count) mesh.SetUVs(0, uv);
                mesh.SetTriangles(t, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        /// <summary>Keil: steigt von vorne (+Z, Hoehe 0) nach hinten (-Z, Hoehe h) an.</summary>
        public static Mesh Wedge(float w, float h, float l)
        {
            string key = $"wedge_{w:0.##}_{h:0.##}_{l:0.##}";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            float x = w * 0.5f, y = h * 0.5f, z = l * 0.5f;
            Vector3 fbl = new Vector3(-x, -y, z), fbr = new Vector3(x, -y, z);
            Vector3 bbl = new Vector3(-x, -y, -z), bbr = new Vector3(x, -y, -z);
            Vector3 btl = new Vector3(-x, y, -z), btr = new Vector3(x, y, -z);

            var b = new Builder();
            b.Quad(fbl, fbr, btr, btl, new Vector3(0, l, h));   // Schraege (nach oben/vorne)
            b.Quad(bbl, bbr, btr, btl, Vector3.back);            // Rueckwand
            b.Quad(fbl, fbr, bbr, bbl, Vector3.down);            // Boden
            b.Tri(fbl, bbl, btl, Vector3.left);
            b.Tri(fbr, bbr, btr, Vector3.right);
            return Store(key, b.Build(key));
        }

        /// <summary>Quarterpipe: Rundung (Radius r) mit Plattform (Tiefe deck) oben. Die Fahrflaeche zeigt nach +Z.</summary>
        public static Mesh QuarterPipe(float width, float radius, float deck, int segments = 12)
        {
            string key = $"qp_{width:0.##}_{radius:0.##}_{deck:0.##}";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            // Profil in (z, y): Kurve von (0,0) bis (-r, r)
            var profile = new List<Vector2>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 0.5f;
                profile.Add(new Vector2(-Mathf.Sin(a) * radius, radius - Mathf.Cos(a) * radius));
            }
            float totalDepth = radius + deck;
            Vector2 center = new Vector2(-totalDepth * 0.5f, radius * 0.5f);
            float hx = width * 0.5f;
            Vector3 P(Vector2 p, float x) => new Vector3(x, p.y - center.y, p.x - center.x);

            var b = new Builder();

            // Gekruemmte Fahrflaeche mit weichen Normalen (fuer schoenes Toon-Licht)
            int start = b.v.Count;
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 0.5f;
                Vector3 normal = new Vector3(0, Mathf.Cos(a), Mathf.Sin(a)).normalized;
                b.v.Add(P(profile[i], -hx)); b.n.Add(normal);
                b.v.Add(P(profile[i], hx)); b.n.Add(normal);
            }
            for (int i = 0; i < segments; i++)
            {
                int a = start + i * 2;
                b.t.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
            }

            Vector2 top = new Vector2(-radius, radius), back = new Vector2(-totalDepth, radius);
            Vector2 backBottom = new Vector2(-totalDepth, 0), front = new Vector2(0, 0);
            b.Quad(P(top, -hx), P(back, -hx), P(back, hx), P(top, hx), Vector3.up);
            b.Quad(P(back, -hx), P(backBottom, -hx), P(backBottom, hx), P(back, hx), Vector3.back);
            b.Quad(P(front, -hx), P(front, hx), P(backBottom, hx), P(backBottom, -hx), Vector3.down);

            var side = new List<Vector2>(profile) { back };
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * hx;
                for (int i = 0; i < side.Count - 1; i++)
                    b.Tri(P(backBottom, x), P(side[i], x), P(side[i + 1], x), new Vector3(s, 0, 0));
            }
            return Store(key, b.Build(key));
        }

        /// <summary>
        /// Pyramidenstumpf: Grundflaeche w x d, Plateau tw x td, Hoehe h. Die Schraegen laufen auch ueber die Ecken
        /// (Hips), damit man von jeder Seite und schraeg hochfahren kann.
        /// </summary>
        public static Mesh Frustum(float w, float d, float tw, float td, float h)
        {
            string key = $"frustum_{w:0.##}_{d:0.##}_{tw:0.##}_{td:0.##}_{h:0.##}";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            float x = w * 0.5f, z = d * 0.5f, tx = tw * 0.5f, tz = td * 0.5f, y = h * 0.5f;
            Vector3[] bottom = { new Vector3(-x, -y, -z), new Vector3(x, -y, -z), new Vector3(x, -y, z), new Vector3(-x, -y, z) };
            Vector3[] top = { new Vector3(-tx, y, -tz), new Vector3(tx, y, -tz), new Vector3(tx, y, tz), new Vector3(-tx, y, tz) };
            var b = new Builder();
            b.Quad(top[0], top[1], top[2], top[3], Vector3.up);
            b.Quad(bottom[0], bottom[1], bottom[2], bottom[3], Vector3.down);
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Vector3 outward = (bottom[i] + bottom[j] + top[i] + top[j]) * 0.25f; // konvex und zentriert: Mitte zeigt nach aussen
                b.Quad(bottom[i], bottom[j], top[j], top[i], outward);
            }
            return Store(key, b.Build(key));
        }

        static Mesh Store(string key, Mesh mesh)
        {
            mesh = MeshStore.Persist(mesh, key);
            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>Viele Quader in ein Mesh zusammenfassen (fuer Details ohne Outline, z. B. Fenster, Markierungen).</summary>
        public class BoxBatch
        {
            readonly Builder _b = new Builder();

            public int Count { get; private set; }

            /// <summary>
            /// Quader hinzufuegen. uvRange (u0, u1, v0, v1) legt eine Textur auf die beiden grossen senkrechten Seiten
            /// (quer zur duenneren waagerechten Achse, u von links nach rechts von aussen gesehen); die uebrigen Seiten
            /// bekommen den Punkt (u0, v0), also den Rand der Textur.
            /// </summary>
            public void Add(Vector3 center, Vector3 size, float yaw = 0f, Vector4? uvRange = null)
            {
                Quaternion r = Quaternion.Euler(0, yaw, 0);
                Vector3 h = size * 0.5f;
                Vector3 C(float x, float y, float z) => center + r * new Vector3(x * h.x, y * h.y, z * h.z);
                Vector4 q = uvRange ?? Vector4.zero;
                if (uvRange.HasValue) _b.hasUV = true;
                Vector2 U(float s, float t) => new Vector2(Mathf.Lerp(q.x, q.y, s), Mathf.Lerp(q.z, q.w, t));
                Vector2 e = U(0, 0);
                bool faceZ = size.x >= size.z;
                _b.Quad(C(-1, 1, -1), C(-1, 1, 1), C(1, 1, 1), C(1, 1, -1), r * Vector3.up, e, e, e, e);
                _b.Quad(C(-1, -1, -1), C(1, -1, -1), C(1, -1, 1), C(-1, -1, 1), r * Vector3.down, e, e, e, e);
                if (faceZ)
                {
                    _b.Quad(C(-1, -1, 1), C(1, -1, 1), C(1, 1, 1), C(-1, 1, 1), r * Vector3.forward, U(1, 0), U(0, 0), U(0, 1), U(1, 1));
                    _b.Quad(C(-1, -1, -1), C(-1, 1, -1), C(1, 1, -1), C(1, -1, -1), r * Vector3.back, U(0, 0), U(0, 1), U(1, 1), U(1, 0));
                    _b.Quad(C(1, -1, -1), C(1, 1, -1), C(1, 1, 1), C(1, -1, 1), r * Vector3.right, e, e, e, e);
                    _b.Quad(C(-1, -1, -1), C(-1, -1, 1), C(-1, 1, 1), C(-1, 1, -1), r * Vector3.left, e, e, e, e);
                }
                else
                {
                    _b.Quad(C(-1, -1, 1), C(1, -1, 1), C(1, 1, 1), C(-1, 1, 1), r * Vector3.forward, e, e, e, e);
                    _b.Quad(C(-1, -1, -1), C(-1, 1, -1), C(1, 1, -1), C(1, -1, -1), r * Vector3.back, e, e, e, e);
                    _b.Quad(C(1, -1, -1), C(1, 1, -1), C(1, 1, 1), C(1, -1, 1), r * Vector3.right, U(0, 0), U(0, 1), U(1, 1), U(1, 0));
                    _b.Quad(C(-1, -1, -1), C(-1, -1, 1), C(-1, 1, 1), C(-1, 1, -1), r * Vector3.left, U(1, 0), U(0, 0), U(0, 1), U(1, 1));
                }
                Count++;
            }

            public GameObject Build(Transform parent, string name, Color color, string key, float emission = 0f)
            {
                return Build(parent, name, ToonMaterials.Get(color, 0f, true, emission), key);
            }

            public GameObject Build(Transform parent, string name, Material material, string key)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                if (Count == 0) return go;
                var mesh = MeshStore.Persist(_b.Build(key), key);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.isStatic = true;
                return go;
            }
        }
    }
}
