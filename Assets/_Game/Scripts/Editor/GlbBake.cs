using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Gemeinsames Werkzeug fuer GLB-Modelle (glTFast-Import), die zu wenigen, guenstigen Meshes "gebacken" werden:
    /// Teile zusammenfassen, Farben in eine kleine Paletten-Textur legen (ein Material fuer viele Farben),
    /// Toon-Materialien und Assets anlegen. Benutzt von TrafficAssets (NPC- und Garagen-Autos), PropAssets
    /// (Telefonzelle, Imbisswagen) und TowPlaneAssets (Schleppflugzeug).
    /// </summary>
    public static class GlbBake
    {
        /// <summary>Sammelt Dreiecke vieler Teilmeshes in einem Zielraum.</summary>
        public class MeshBuilder
        {
            readonly List<Vector3> _pos = new List<Vector3>(), _nrm = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<int> _tri = new List<int>();

            public bool Empty => _pos.Count == 0;

            /// <summary>
            /// Teilmesh mit Matrix m (Mesh-Raum -> Zielraum) anhaengen. uv = fester Punkt (Palettenfeld); mit keepUv
            /// bleiben die Mesh-UVs, optional in uvRect verschoben (Textur-Atlas).
            /// </summary>
            public void Add(Mesh mesh, int sub, Vector2 uv, Matrix4x4 m, bool keepUv = false, Rect uvRect = default)
            {
                var meshUv = keepUv ? mesh.uv : null;
                Matrix4x4 n = m.inverse.transpose;
                bool flip = m.determinant < 0f; // gespiegelt: Umlaufsinn drehen
                var verts = mesh.vertices;
                var norms = mesh.normals;
                var tris = mesh.GetTriangles(sub);
                var map = new Dictionary<int, int>();
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int a = Map(tris[i]), b = Map(tris[i + 1]), c = Map(tris[i + 2]);
                    if (flip) { _tri.Add(a); _tri.Add(c); _tri.Add(b); }
                    else { _tri.Add(a); _tri.Add(b); _tri.Add(c); }
                }

                int Map(int v)
                {
                    if (map.TryGetValue(v, out int k)) return k;
                    k = _pos.Count;
                    _pos.Add(m.MultiplyPoint3x4(verts[v]));
                    // Nie Null-Normalen: die werden im Toon-Shader zu NaN und Bloom faerbt dann alles weiss
                    Vector3 nn = norms != null && norms.Length > v ? n.MultiplyVector(norms[v]).normalized : Vector3.up;
                    if (float.IsNaN(nn.x) || nn.sqrMagnitude < 0.5f) nn = Vector3.up;
                    _nrm.Add(nn);
                    if (keepUv && meshUv != null && meshUv.Length > v)
                    {
                        var u = meshUv[v];
                        _uv.Add(uvRect.width > 0f ? new Vector2(uvRect.x + u.x * uvRect.width, uvRect.y + u.y * uvRect.height) : u);
                    }
                    else _uv.Add(uv);
                    map[v] = k;
                    return k;
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                if (_pos.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(_pos);
                mesh.SetNormals(_nrm);
                mesh.SetUVs(0, _uv);
                mesh.SetTriangles(_tri, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        // ------------------------------------------------------------------ Namen

        /// <summary>Material-/Objektname ohne Unity-Zusatz wie " (Instance)".</summary>
        public static string CleanName(string n)
        {
            int i = n.IndexOf(" (", StringComparison.Ordinal);
            return (i > 0 ? n.Substring(0, i) : n).Trim();
        }

        /// <summary>Knotenname ohne angehaengte Nummer, die glTFast bei doppelten Namen vergibt ("tire_1" -> "tire").</summary>
        public static string StripIndex(string n)
        {
            n = CleanName(n);
            int u = n.LastIndexOf('_');
            return u > 0 && int.TryParse(n.Substring(u + 1), out _) ? n.Substring(0, u) : n;
        }

        // ------------------------------------------------------------------ GLB lesen

        [Serializable] class GltfJson { public GltfMat[] materials; }
        [Serializable] class GltfMat { public string name; public GltfPbr pbrMetallicRoughness; }
        [Serializable] class GltfPbr { public float[] baseColorFactor; }

        /// <summary>
        /// Materialfarben (sRGB) direkt aus dem JSON-Teil der GLB. Materialien ohne Farbfaktor (meist mit Textur):
        /// weiss, oder mit untintedAsWhite = false weglassen.
        /// </summary>
        public static Dictionary<string, Color> ReadColors(string glbPath, bool untintedAsWhite = true)
        {
            var bytes = File.ReadAllBytes(glbPath);
            int len = BitConverter.ToInt32(bytes, 12);
            var json = JsonUtility.FromJson<GltfJson>(System.Text.Encoding.UTF8.GetString(bytes, 20, len));
            var result = new Dictionary<string, Color>();
            foreach (var m in json.materials ?? new GltfMat[0])
            {
                var f = m.pbrMetallicRoughness?.baseColorFactor;
                if (f != null && f.Length >= 3) result[m.name] = new Color(f[0], f[1], f[2]).gamma; // glTF-Farben sind linear
                else if (untintedAsWhite) result[m.name] = Color.white;
            }
            return result;
        }

        public static Texture FirstTexture(Material m)
        {
            foreach (var p in m.GetTexturePropertyNames())
            {
                var t = m.GetTexture(p);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>Lesbare Kopie einer (nicht lesbaren) Import-Textur, z. B. fuer einen Atlas.</summary>
        public static Texture2D Readable(Texture t)
        {
            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(t, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        // ------------------------------------------------------------------ Assets schreiben

        /// <summary>Textur als PNG-Asset speichern (die uebergebene Textur wird freigegeben).</summary>
        public static Texture2D SaveTexture(Texture2D tex, string path, FilterMode filter, bool mips)
        {
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.filterMode = filter;
            imp.mipmapEnabled = mips;
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Paletten-Textur: size x size Felder zu je cell x cell Pixeln (Point-Filter, keine Mips).</summary>
        public static Texture2D Palette(string path, Color[] cols, int size = 4, int cell = 1)
        {
            if (cols.Length > size * size) throw new Exception("Zu viele Farben fuer die Palette: " + path);
            int px = size * cell;
            var tex = new Texture2D(px, px, TextureFormat.RGBA32, false);
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                {
                    int idx = (y / cell) * size + x / cell;
                    tex.SetPixel(x, y, idx < cols.Length ? cols[idx] : Color.magenta);
                }
            tex.Apply();
            return SaveTexture(tex, path, FilterMode.Point, false);
        }

        /// <summary>UV in der Mitte von Palettenfeld idx (unabhaengig von der Feldgroesse in Pixeln).</summary>
        public static Vector2 PaletteUv(int idx, int size = 4) => new Vector2((idx % size + 0.5f) / size, (idx / size + 0.5f) / size);

        /// <summary>Toon-Material anlegen oder aktualisieren. cutoff > 0: ausgestanzt (Aufkleber), dann ohne Schatten.</summary>
        public static Material ToonMat(string path, Color color, Texture tex, float outline, float emission = 0f,
                                       bool doubleSided = false, float cutoff = 0f, float rim = 0.2f)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(ToonMaterials.Shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = ToonMaterials.Shader;
            mat.enableInstancing = true;
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", tex);
            mat.SetFloat("_Emission", emission);
            mat.SetFloat("_OutlineWidth", outline);
            mat.SetFloat("_OutlineMode", 0f);
            mat.SetFloat("_RimStrength", rim);
            mat.SetFloat("_Cull", doubleSided ? 0f : 2f);
            mat.SetFloat("_Cutoff", cutoff);
            mat.SetShaderPassEnabled("SRPDefaultUnlit", outline > 0f);
            mat.SetShaderPassEnabled("ShadowCaster", cutoff <= 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Mesh als Asset speichern; existiert es schon, wird es ersetzt (GUID und Verweise bleiben).</summary>
        public static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        /// <summary>Asset-Ordner anlegen (auch verschachtelt), z. B. "Assets/_Game/Foo/Meshes".</summary>
        public static void EnsureFolders(params string[] folders)
        {
            foreach (var d in folders)
            {
                if (AssetDatabase.IsValidFolder(d)) continue;
                string parent = Path.GetDirectoryName(d).Replace('\\', '/');
                EnsureFolders(parent);
                AssetDatabase.CreateFolder(parent, Path.GetFileName(d));
            }
        }

        /// <summary>Renderer-Kind ohne Licht-/Reflexionsproben anlegen (alles Toon, die brauchen wir nicht).</summary>
        public static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material mat, bool shadows, int layer = -1)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (layer >= 0) go.layer = layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }
    }
}
