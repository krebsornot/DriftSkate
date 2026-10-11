using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DriftSkate
{
    /// <summary>
    /// Grundstuecke der fertigen Stadt fuer eigene Modelle freiraeumen (Parkhaus, Baustelle): alles, was im neuen Bau
    /// stecken wuerde, kommt weg (Haeuser, Dach-Deko, Markisen, Strassen-Deko) und aus den zusammengefassten Meshes
    /// (Fenster, Ladenfenster, Tueren, Kanten) werden die Dreiecke im Grundstueck geschnitten. Graffiti-Stellen bleiben
    /// auf Wunsch erhalten (ihre Nummern haengen am Namen) und werden vom Aufrufer neu platziert.
    /// </summary>
    public static class CityPlots
    {
        static readonly string[] MergedMeshes = { "Windows", "ShopWindows", "Doors", "Trim" };

        /// <summary>inside: Weltpunkt liegt im Grundstueck; cutArea: Bereich fuer die Fenster-Meshes; keepTag: Graffiti-Stelle behalten.</summary>
        public static (int removed, int cut) Clear(Transform cityRoot, Transform block, Func<Vector3, bool> inside, Bounds cutArea, Action<TagSpot> keepTag = null)
        {
            var kill = new List<GameObject>();
            foreach (Transform t in block)
            {
                if (t.name == "Sidewalk") continue;
                var r = t.GetComponentInChildren<Renderer>();
                Vector3 p = r != null ? r.bounds.center : t.position;
                if (!inside(p)) continue;
                var tag = t.GetComponent<TagSpot>();
                if (tag != null && keepTag != null) { keepTag(tag); continue; }
                kill.Add(t.gameObject);
            }
            // Strassen-Deko, Laternen usw.: direkte Kinder der Gruppen unter der Stadt
            foreach (Transform group in cityRoot)
            {
                if (group.name.StartsWith("Block_") || group.GetComponent<Renderer>() != null) continue;
                foreach (Transform t in group)
                    if (inside(t.position) && t.position.y < 2f) kill.Add(t.gameObject);
            }
            foreach (var g in kill) { g.SetActive(false); Shapes.DestroySafe(g); }

            int cut = 0;
            foreach (var name in MergedMeshes)
            {
                var t = cityRoot.Find(name);
                var mf = t != null ? t.GetComponent<MeshFilter>() : null;
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = Cut(mf.sharedMesh, cutArea, out int n);
                if (n == 0) continue;
                cut += n;
                mf.sharedMesh = MeshStore.Persist(mesh, mf.sharedMesh.name);
            }
            return (kill.Count, cut);
        }

        const string SidewalkPiece = "SidewalkCut";

        /// <summary>
        /// Der Gehweg-Quader des Blocks (Oberkante 0,18 m) liegt genau auf Hoehe des Bodens der neuen Modelle, das flimmert
        /// (Z-Fighting). Darum wird sein Bild durch Streifen um das Grundstueck ersetzt, unter dem Grundstueck liegt ein
        /// um 'sink' abgesenktes Stueck (falls das Modell den Boden nicht ganz abdeckt, entsteht dort kein Loch).
        /// Der Collider des Gehwegs bleibt unveraendert. Mehrfach ausfuehren ist harmlos.
        /// </summary>
        public static int SinkSidewalk(Transform block, Rect plot, float sink = 0.06f)
        {
            var old = new List<GameObject>();
            foreach (Transform t in block) if (t.name == SidewalkPiece) old.Add(t.gameObject);
            foreach (var g in old) { g.SetActive(false); Shapes.DestroySafe(g); }

            int built = 0;
            foreach (Transform t in block)
            {
                if (t.name != "Sidewalk") continue;
                var r = t.GetComponent<MeshRenderer>();
                if (r == null) continue;
                var b = new Bounds(t.position, t.lossyScale); // Einheitswuerfel, Renderer kann schon aus sein
                var walk = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
                float x0 = Mathf.Max(plot.xMin, walk.xMin), x1 = Mathf.Min(plot.xMax, walk.xMax);
                float z0 = Mathf.Max(plot.yMin, walk.yMin), z1 = Mathf.Min(plot.yMax, walk.yMax);
                if (x0 >= x1 || z0 >= z1) continue;

                void Piece(float ax, float az, float bx, float bz, float top)
                {
                    if (bx - ax < 0.01f || bz - az < 0.01f) return;
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = SidewalkPiece;
                    Shapes.RemoveCollider(go);
                    go.transform.SetParent(block, false);
                    go.transform.position = new Vector3((ax + bx) * 0.5f, (b.min.y + top) * 0.5f, (az + bz) * 0.5f);
                    go.transform.localScale = new Vector3(bx - ax, top - b.min.y, bz - az);
                    var pr = go.GetComponent<MeshRenderer>();
                    pr.sharedMaterials = r.sharedMaterials;
                    pr.shadowCastingMode = r.shadowCastingMode;
                    pr.receiveShadows = r.receiveShadows;
                    go.layer = t.gameObject.layer;
                    go.isStatic = t.gameObject.isStatic;
                    built++;
                }
                float y = b.max.y;
                Piece(walk.xMin, walk.yMin, walk.xMax, z0, y);   // Sued
                Piece(walk.xMin, z1, walk.xMax, walk.yMax, y);   // Nord
                Piece(walk.xMin, z0, x0, z1, y);                 // West
                Piece(x1, z0, walk.xMax, z1, y);                 // Ost
                Piece(x0, z0, x1, z1, y - sink);                 // unter dem Modell, abgesenkt
                r.enabled = false;
            }
            return built;
        }

        /// <summary>Kopie ohne die Dreiecke, deren Mitte in 'area' liegt (Rest kompakt, Normalen/UVs bleiben).</summary>
        public static Mesh Cut(Mesh src, Bounds area, out int removed)
        {
            var verts = src.vertices;
            var normals = src.normals;
            var uvs = src.uv;
            var tris = src.triangles;
            var keep = new List<int>();
            removed = 0;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 m = (verts[tris[i]] + verts[tris[i + 1]] + verts[tris[i + 2]]) / 3f;
                if (area.Contains(m)) { removed++; continue; }
                keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]);
            }
            if (removed == 0) return src;
            var map = new Dictionary<int, int>();
            var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>();
            for (int i = 0; i < keep.Count; i++)
            {
                int v = keep[i];
                if (!map.TryGetValue(v, out int k))
                {
                    k = nv.Count;
                    map[v] = k;
                    nv.Add(verts[v]);
                    if (normals.Length == verts.Length) nn.Add(normals[v]);
                    if (uvs.Length == verts.Length) nu.Add(uvs[v]);
                }
                keep[i] = k;
            }
            var mesh = new Mesh { name = src.name };
            if (nv.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(nv);
            if (nn.Count == nv.Count) mesh.SetNormals(nn);
            if (nu.Count == nv.Count) mesh.SetUVs(0, nu);
            mesh.SetTriangles(keep, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
