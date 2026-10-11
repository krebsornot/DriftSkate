using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Patrouillen-Bot ENFORCER 01 (Art/Characters/enforcer01_patrol.glb, Fynns Modell) fuer Mirus Auftrag.
    /// Rumpf ueber Paletten-Textur, Aufkleber als Atlas, Warnstreifen gekachelt, Display und Leuchtteile leuchtend.
    /// Die beiden Rotoren und das Blaulicht sind eigene Teile, die PatrolBot zur Laufzeit dreht bzw. blinken laesst.
    /// Das Prefab liegt in Resources (PatrolBot.PrefabPath). Menue: DriftSkate → Patrouillen-Bot.
    /// </summary>
    public static class PatrolBotAssets
    {
        const string Name = "PatrolBot";
        const string Root = "Assets/_Game/PatrolBot";
        const string SourceGlb = "Art/Characters/enforcer01_patrol.glb";
        const string GlbPath = Root + "/Source/enforcer01_patrol.glb";
        static string PrefabPath => "Assets/_Game/Resources/" + PatrolBot.PrefabPath + ".prefab";
        const float Scale = 1.4f; // Modell ist 1,3 m breit, so gut 1,8 m: gut zu sehen und zu treffen
        static string MatPath(string part) => Root + "/Materials/" + Name + "_" + part + ".mat";
        static string MeshPath(string part) => Root + "/Meshes/" + Name + "_" + part + ".asset";
        static string TexPath(string part) => Root + "/Textures/" + Name + "_" + part + ".png";

        static readonly string[] GlowMats = { "led_visor", "thrust_glow", "thrust_glow_0" };
        const string ScreenMat = "display";

        [MenuItem("DriftSkate/Patrouillen-Bot/Prefab bauen")] public static void Build() => BuildPrefab();
        [MenuItem("DriftSkate/Patrouillen-Bot/Kontrollbilder rendern")] public static void Render() => RenderCheck();

        public static void BatchAll()
        {
            BuildPrefab();
            RenderCheck();
        }

        // ------------------------------------------------------------------ GLB-Materialien (Kachelung der Warnstreifen)

        [Serializable] class GltfJson { public GltfMat[] materials; }
        [Serializable] class GltfMat { public string name; public GltfPbr pbrMetallicRoughness; }
        [Serializable] class GltfPbr { public GltfTexRef baseColorTexture; }
        [Serializable] class GltfTexRef { public int index = -1; public GltfTexExt extensions; }
        [Serializable] class GltfTexExt { public GltfTexTransform KHR_texture_transform; }
        [Serializable] class GltfTexTransform { public float[] scale, offset; }

        /// <summary>UV-Rechteck aus KHR_texture_transform (glTFast dreht V, daher y = 1 - scale - offset); null = nicht gekachelt.</summary>
        static Dictionary<string, Rect> ReadTiling(string path)
        {
            var bytes = File.ReadAllBytes(path);
            int len = BitConverter.ToInt32(bytes, 12);
            var json = JsonUtility.FromJson<GltfJson>(System.Text.Encoding.UTF8.GetString(bytes, 20, len));
            var result = new Dictionary<string, Rect>();
            foreach (var m in json.materials)
            {
                var tt = m.pbrMetallicRoughness?.baseColorTexture?.extensions?.KHR_texture_transform;
                if (tt?.scale == null || tt.scale.Length != 2) continue;
                Vector2 s = new Vector2(tt.scale[0], tt.scale[1]);
                Vector2 o = tt.offset != null && tt.offset.Length == 2 ? new Vector2(tt.offset[0], tt.offset[1]) : Vector2.zero;
                result[m.name] = new Rect(o.x, 1f - s.y - o.y, s.x, s.y);
            }
            return result;
        }

        // ------------------------------------------------------------------ Bauen

        public static void BuildPrefab()
        {
            foreach (var d in new[] { "/Meshes", "/Materials", "/Textures" }) AssetDatabase.DeleteAsset(Root + d);
            GlbBake.EnsureFolders(Root + "/Source", Root + "/Meshes", Root + "/Materials", Root + "/Textures",
                                  Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            if (!File.Exists(SourceGlb)) throw new Exception("Modell fehlt: " + SourceGlb);
            File.Copy(SourceGlb, GlbPath, true);
            AssetDatabase.ImportAsset(GlbPath, ImportAssetOptions.ForceUpdate);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
            if (src == null) throw new Exception("GLB konnte nicht importiert werden: " + GlbPath);
            var colors = GlbBake.ReadColors(GlbPath, untintedAsWhite: false);
            var tiling = ReadTiling(GlbPath);

            var inst = Object.Instantiate(src);
            var frame = new GameObject("Frame").transform;
            try
            {
                var all = inst.GetComponentsInChildren<Transform>(true);
                Transform Find(string n) => all.FirstOrDefault(t => GlbBake.CleanName(t.name) == n);
                bool Under(Transform t, Transform group)
                {
                    for (; t != null; t = t.parent) if (t == group) return true;
                    return false;
                }

                // Vorne = Richtung zum Display (das Gesicht), Ursprung = Mitte des Rumpfs
                var display = Find("display");
                var hull = Find("hull_upper");
                if (display == null || hull == null) throw new Exception("display oder hull_upper fehlt im Modell");
                var hullBounds = hull.GetComponent<Renderer>().bounds;
                Vector3 fwd = Vector3.ProjectOnPlane(display.position - hullBounds.center, Vector3.up).normalized;
                frame.SetPositionAndRotation(hullBounds.center, Quaternion.LookRotation(fwd, Vector3.up));
                Matrix4x4 toFrame = frame.worldToLocalMatrix;

                // Texturen: Display leuchtet, gekachelte Warnstreifen je eigenes Material, Rest (Fahne, Nummer) in einen Atlas
                var tex = new Dictionary<string, Texture>();
                foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (var um in mr.sharedMaterials)
                    {
                        string n = GlbBake.CleanName(um.name);
                        var t = GlbBake.FirstTexture(um);
                        if (t != null && !tex.ContainsKey(n)) tex[n] = t;
                        else if (t == null && !colors.ContainsKey(n)) colors[n] = Color.white; // reflex_edge: ohne Faktor = weiss
                    }
                var decalNames = tex.Keys.Where(n => n != ScreenMat && !tiling.ContainsKey(n)).OrderBy(n => n).ToList();
                var tiledNames = tex.Keys.Where(n => tiling.ContainsKey(n)).OrderBy(n => n).ToList();
                var readable = decalNames.Select(k => GlbBake.Readable(tex[k])).ToArray();
                var atlasTex = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
                var rects = atlasTex.PackTextures(readable, 4, 1024, false);
                foreach (var r in readable) Object.DestroyImmediate(r);
                var atlas = GlbBake.SaveTexture(atlasTex, TexPath("Decals"), FilterMode.Bilinear, true);

                var palette = new List<Color>();
                int ColorIndex(Color c)
                {
                    int i = palette.FindIndex(x => Mathf.Abs(x.r - c.r) + Mathf.Abs(x.g - c.g) + Mathf.Abs(x.b - c.b) < 0.01f);
                    if (i >= 0) return i;
                    palette.Add(c);
                    return palette.Count - 1;
                }
                int white = ColorIndex(Color.white);

                // Bewegliche Teile: eigener Drehpunkt (Rotor-Mitte, Drehachse = duennste Richtung der Blaetter), Blaulicht
                var rotorL = Find("rotor_L");
                var rotorR = Find("rotor_R");
                var siren = Find("siren_lens");
                if (rotorL == null || rotorR == null || siren == null) throw new Exception("rotor_L, rotor_R oder siren_lens fehlt");

                var parts = new Dictionary<string, (GlbBake.MeshBuilder b, Matrix4x4 toPart)>();
                (GlbBake.MeshBuilder b, Matrix4x4 toPart) PartFor(string key, Matrix4x4 toPart)
                {
                    if (!parts.TryGetValue(key, out var p)) parts[key] = p = (new GlbBake.MeshBuilder(), toPart);
                    return p;
                }
                var pivots = new Dictionary<string, (Vector3 pos, Quaternion rot)>();
                foreach (var (rotor, side) in new[] { (rotorL, "L"), (rotorR, "R") })
                {
                    var rb = new Bounds();
                    bool first = true;
                    foreach (var mf in rotor.GetComponentsInChildren<MeshFilter>())
                        foreach (var v in mf.sharedMesh.vertices)
                        {
                            var p = toFrame.MultiplyPoint3x4(mf.transform.TransformPoint(v));
                            if (first) { rb = new Bounds(p, Vector3.zero); first = false; } else rb.Encapsulate(p);
                        }
                    var e = rb.extents;
                    Vector3 axis = e.y <= e.x && e.y <= e.z ? Vector3.up : e.x <= e.z ? Vector3.right : Vector3.forward;
                    pivots["Rotor_" + side] = (rb.center, Quaternion.FromToRotation(Vector3.up, axis));
                }
                pivots["Siren"] = (toFrame.MultiplyPoint3x4(siren.position), Quaternion.identity);
                Matrix4x4 PivotInverse(string key) => Matrix4x4.TRS(pivots[key].pos, pivots[key].rot, Vector3.one).inverse;

                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    Matrix4x4 m = toFrame * mf.transform.localToWorldMatrix;
                    string group = Under(mf.transform, rotorL) ? "Rotor_L" : Under(mf.transform, rotorR) ? "Rotor_R" : mf.transform == siren ? "Siren" : null;
                    Matrix4x4 local = group != null ? PivotInverse(group) * m : m;
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        string mat = GlbBake.CleanName(mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)].name);
                        if (group == "Siren") { PartFor("Siren", local).b.Add(mf.sharedMesh, sub, GlbBake.PaletteUv(white, 8), local); continue; }
                        string key = mat == ScreenMat ? "Screen"
                                   : GlowMats.Contains(mat) ? "Glow"
                                   : tiledNames.Contains(mat) ? "Tiled_" + mat
                                   : decalNames.Contains(mat) ? "Decals"
                                   : "Body";
                        if (group != null && key == "Body") key = group;
                        else if (group != null) throw new Exception($"{group}: Material {mat} wird nicht unterstuetzt");
                        var part = PartFor(key, local).b;
                        if (key == "Screen") part.Add(mf.sharedMesh, sub, Vector2.zero, local, true);
                        else if (key.StartsWith("Tiled_")) part.Add(mf.sharedMesh, sub, Vector2.zero, local, true, tiling[mat]);
                        else if (key == "Decals") part.Add(mf.sharedMesh, sub, Vector2.zero, local, true, rects[decalNames.IndexOf(mat)]);
                        else if (colors.TryGetValue(mat, out var c)) part.Add(mf.sharedMesh, sub, GlbBake.PaletteUv(ColorIndex(c), 8), local);
                        else throw new Exception("Unbekanntes Material " + mat);
                    }
                }

                var paletteTex = GlbBake.Palette(TexPath("Palette"), palette.ToArray(), 8);
                var bodyMat = GlbBake.ToonMat(MatPath("Body"), Color.white, paletteTex, 0.25f, rim: 0.25f);
                var root = new GameObject(Name);
                try
                {
                    var visual = new GameObject("Visual").transform;
                    visual.SetParent(root.transform, false);
                    visual.localScale = Vector3.one * Scale;
                    foreach (var kv in parts)
                    {
                        string key = kv.Key;
                        Material mat;
                        if (key == "Screen") mat = GlbBake.ToonMat(MatPath("Screen"), Color.white, Copy(tex[ScreenMat], "Screen", TextureWrapMode.Clamp), 0f, 1.1f, rim: 0f);
                        else if (key == "Glow") mat = GlbBake.ToonMat(MatPath("Glow"), Color.white, paletteTex, 0f, 1.6f, rim: 0f);
                        else if (key == "Decals") mat = GlbBake.ToonMat(MatPath("Decals"), Color.white, atlas, 0f, cutoff: 0.5f, rim: 0.15f);
                        else if (key.StartsWith("Tiled_"))
                        {
                            string m = key.Substring(6);
                            mat = GlbBake.ToonMat(MatPath(key), Color.white, Copy(tex[m], key, TextureWrapMode.Repeat), 0f, cutoff: 0.5f, rim: 0.15f);
                        }
                        else if (key == "Siren") mat = GlbBake.ToonMat(MatPath("Siren"), Color.white, paletteTex, 0.25f, 1.8f, rim: 0f);
                        else mat = bodyMat;

                        Transform parent = visual;
                        if (pivots.TryGetValue(key, out var pv))
                        {
                            parent = new GameObject(key).transform;
                            parent.SetParent(visual, false);
                            parent.SetLocalPositionAndRotation(pv.pos, pv.rot);
                        }
                        GlbBake.Part(parent, key == parent.name ? "Mesh" : key, GlbBake.SaveMesh(kv.Value.b.ToMesh(Name + "_" + key), MeshPath(key)), mat,
                                     key == "Body");
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    Debug.Log($"[PatrolBot] Prefab gebaut: {PrefabPath} ({palette.Count} Farben, Teile: {string.Join(", ", parts.Keys)}, " +
                              $"Rotor-Achsen L {pivots["Rotor_L"].rot * Vector3.up} R {pivots["Rotor_R"].rot * Vector3.up})");
                }
                finally { Object.DestroyImmediate(root); }
            }
            finally
            {
                Object.DestroyImmediate(frame.gameObject);
                Object.DestroyImmediate(inst);
            }
            AssetDatabase.SaveAssets();
        }

        static Texture2D Copy(Texture t, string part, TextureWrapMode wrap)
        {
            var saved = GlbBake.SaveTexture(GlbBake.Readable(t), TexPath(part), FilterMode.Bilinear, true);
            var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(saved));
            imp.wrapMode = wrap;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(saved));
        }

        // ------------------------------------------------------------------ Kontrolle

        public static void RenderCheck()
        {
            Directory.CreateDirectory("Logs/patrolbot");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var cam = Camera.main;
            Vector3 c = new Vector3(CityBuilder.BlockCenter(3), 2.2f, CityBuilder.BlockCenter(2));
            var bot = Object.Instantiate(prefab, c, Quaternion.identity);
            var t = bot.transform;
            void Shot(Vector3 local, float fov, string file)
            {
                cam.fieldOfView = fov;
                cam.transform.position = t.TransformPoint(local);
                cam.transform.LookAt(t.position);
                SimTests.Capture(cam, "Logs/patrolbot/" + file + ".png");
            }
            Shot(new Vector3(0f, 0.3f, 4f), 45f, "front");
            Shot(new Vector3(3f, 1f, 2.5f), 45f, "front_34");
            Shot(new Vector3(-3.5f, 0.5f, -2f), 45f, "back_34");
            Shot(new Vector3(0f, 3.5f, 0.3f), 45f, "top");
            Shot(new Vector3(0f, -2.2f, 2f), 45f, "below");
            Shot(new Vector3(4f, -1.2f, 9f), 50f, "park");
            Object.DestroyImmediate(bot);
        }
    }
}
