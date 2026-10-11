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
