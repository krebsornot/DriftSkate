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
    /// Baustelle mit Turmdrehkran aus Art/Environment/baukran_baustelle.glb (Fynns Modell) gegenueber vom Parkhaus.
    /// Drei Teile: Baustelle (Zaun, Rohbau mit Bohlen-Rampen, Erdhuegel, Bagger, Container ...), Kranmast mit Treppenturm
    /// (fest) und Kran-Oberteil (dreht sich, CraneSlew). Der Kran ist 1,4-fach skaliert (Ausleger rund 60 m, Hoehe rund 61 m),
    /// damit er von der Baustelle aus ueber das Parkhaus schwenkt. Strasse und Gehweg aus dem Modell entfallen.
    /// Je Teil: Paletten-Mesh, Aufkleber-Atlas, gekachelte Texturen (Warnstreifen, Bohlen), Leuchtteile.
    /// Hitboxen fuer Zaun, Rohbau, Rampen und Hindernisse, Grind-Linien aus den Grind_-Teilen (nicht auf dem drehenden Ausleger).
    /// Menue: DriftSkate → Baustelle.
    /// </summary>
    public static class BaustelleAssets
    {
        const string Name = "Baustelle";
        const string Root = "Assets/_Game/Baustelle";
        const string SourceGlb = "Art/Environment/baukran_baustelle.glb";
        const string GlbPath = Root + "/Source/baukran_baustelle.glb";
        static string PrefabPath => "Assets/_Game/Resources/" + BaustelleSite.PrefabPath + ".prefab";
        const float CraneScale = 1.4f;
        /// <summary>Modell-Ursprung (Kranmast) relativ zur Mitte des alten Hauses; Tor (Modell +Z) zeigt nach Westen.</summary>
        static readonly Vector3 SiteOffset = new Vector3(-3f, 0.185f, 8f);
        const float SiteYaw = -90f;
        static string MatPath(string part) => Root + "/Materials/" + Name + "_" + part + ".mat";
        static string MeshPath(string part) => Root + "/Meshes/" + Name + "_" + part + ".asset";
        static string TexPath(string part) => Root + "/Textures/" + Name + "_" + part + ".png";

        /// <summary>Strasse und Gehweg im Modell: die hat die Stadt selbst.</summary>
        static readonly string[] Skip = { "Sidewalk", "Street", "Street_Lines", "Sidewalk_Curb" };
        /// <summary>Gehoeren zum festen Kranmast (der Rest des Krans steckt in Crane_Mast, Ladders_Walkways).</summary>
        static readonly string[] MastExtras = { "Grind_Stair_Handrails", "Stair_Tower_Neon", "Mast_Warning_Light" };
        /// <summary>Mit Hitbox (Rest ist Deko: Aufkleber, Licht, Pylonen, Schubkarren ...).</summary>
        static readonly string[] Solid =
        {
            "Fence_Panels", "Fence_Feet", "Gate_Posts", "Gate_Leaf_Open",
            "Building_Slab_0", "Building_Deck_1", "Building_Deck_2", "Building_Deck_3", "Building_Columns",
            "Plank_Ramp_1", "Plank_Ramp_2", "Plank_Ramp_3",
            "Dirt_Mound", "Dirt_Kicker", "Gravel_Pile", "Concrete_Tubes", "Pipe_Stack", "Beam_Stack", "Containers",
            "Site_Trailer", "Excavator", "Mixer_Truck", "Floodlight_Masts",
            "Mast_Foundation", "Mast_Chords", "Mast_Braces", "Mast_Platforms", "Stair_Tower_Frame", "Stair_Tower_Base",
            "Stair_Tower_Flights", "Grind_Stair_Handrails", "Building_Formwork",
        };
        /// <summary>Verschoben (Modell-Meter), damit der 1,4-fach groessere Treppenturm am Mast Platz hat.</summary>
        static readonly Dictionary<string, Vector3> Nudge = new Dictionary<string, Vector3> { { "Beam_Stack", new Vector3(-0.6f, 0f, 0f) } };

        [MenuItem("DriftSkate/Baustelle/Prefab bauen")] public static void Build() => BuildPrefab();
        [MenuItem("DriftSkate/Baustelle/In die Stadt setzen (statt orangem Haus)")] public static void Place() => PlaceInCity();
        [MenuItem("DriftSkate/Baustelle/Kontrollbilder rendern")] public static void Render() => RenderCheck();

        public static void BatchAll()
        {
            BuildPrefab();
            PlaceInCity();
            RenderCheck();
        }

        // ------------------------------------------------------------------ GLB-Materialien

        [Serializable] class GltfJson { public GltfMat[] materials; }
        [Serializable] class GltfMat { public string name; public GltfPbr pbrMetallicRoughness; public float[] emissiveFactor; }
        [Serializable] class GltfPbr { public float[] baseColorFactor; public GltfTexRef baseColorTexture; }
        [Serializable] class GltfTexRef { public int index = -1; public GltfTexExt extensions; }
        [Serializable] class GltfTexExt { public GltfTexTransform KHR_texture_transform; }
        [Serializable] class GltfTexTransform { public float[] scale, offset; }

        class MatInfo
        {
            public Color color = Color.white, glow;
            public bool textured, emissive;
            public Rect? tiling; // KHR_texture_transform als UV-Rechteck (glTFast dreht V)
        }

        static Dictionary<string, MatInfo> ReadMaterials(string path)
        {
            var bytes = File.ReadAllBytes(path);
            int len = BitConverter.ToInt32(bytes, 12);
            var json = JsonUtility.FromJson<GltfJson>(System.Text.Encoding.UTF8.GetString(bytes, 20, len));
            var result = new Dictionary<string, MatInfo>();
            foreach (var m in json.materials)
            {
                var info = new MatInfo();
                var f = m.pbrMetallicRoughness?.baseColorFactor;
                if (f != null && f.Length >= 3) info.color = new Color(f[0], f[1], f[2]).gamma;
                var tex = m.pbrMetallicRoughness?.baseColorTexture;
                info.textured = tex != null && tex.index >= 0;
                var tt = tex?.extensions?.KHR_texture_transform;
                if (tt?.scale != null && tt.scale.Length == 2)
                {
                    Vector2 s = new Vector2(tt.scale[0], tt.scale[1]);
                    Vector2 o = tt.offset != null && tt.offset.Length == 2 ? new Vector2(tt.offset[0], tt.offset[1]) : Vector2.zero;
                    info.tiling = new Rect(o.x, 1f - s.y - o.y, s.x, s.y);
                }
                var e = m.emissiveFactor;
                if (e != null && e.Length >= 3 && e[0] + e[1] + e[2] > 0.05f) { info.emissive = true; info.glow = new Color(e[0], e[1], e[2]).gamma; }
                result[m.name] = info;
            }
            return result;
        }

        // ------------------------------------------------------------------ Bauen

        class Group
        {
            public readonly Dictionary<string, GlbBake.MeshBuilder> parts = new Dictionary<string, GlbBake.MeshBuilder>();
            public readonly GlbBake.MeshBuilder solid = new GlbBake.MeshBuilder();
            public readonly Dictionary<string, List<Vector3>> grind = new Dictionary<string, List<Vector3>>();
            public GlbBake.MeshBuilder Part(string key) => parts.TryGetValue(key, out var b) ? b : parts[key] = new GlbBake.MeshBuilder();
        }

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
            var mats = ReadMaterials(GlbPath);

            var inst = Object.Instantiate(src);
            try
            {
                bool Under(Transform t, string group)
                {
                    for (; t != null; t = t.parent) if (GlbBake.CleanName(t.name) == group) return true;
                    return false;
                }
                Matrix4x4 toRoot = inst.transform.worldToLocalMatrix;

                // Texturen sammeln; gekachelte (Transform oder UVs ausserhalb 0..1) bekommen ein eigenes Material
                var tex = new Dictionary<string, Texture>();
                var tiled = new HashSet<string>();
                foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mesh = mr.GetComponent<MeshFilter>().sharedMesh;
                    for (int sub = 0; sub < mr.sharedMaterials.Length; sub++)
                    {
                        string n = GlbBake.CleanName(mr.sharedMaterials[sub].name);
                        var t = GlbBake.FirstTexture(mr.sharedMaterials[sub]);
                        if (t == null) continue;
                        tex[n] = t;
                        if (mats.TryGetValue(n, out var mi) && mi.tiling.HasValue) tiled.Add(n);
                        if (mesh != null && mesh.uv.Any(u => u.x < -0.01f || u.x > 1.01f || u.y < -0.01f || u.y > 1.01f)) tiled.Add(n);
                    }
                }
                var decalNames = tex.Keys.Where(n => !tiled.Contains(n)).OrderBy(n => n).ToList();
                var readable = decalNames.Select(k => GlbBake.Readable(tex[k])).ToArray();
                var atlasTex = new Texture2D(4096, 4096, TextureFormat.RGBA32, false);
                var rects = atlasTex.PackTextures(readable, 4, 4096, false);
                foreach (var r in readable) Object.DestroyImmediate(r);
                var atlas = GlbBake.SaveTexture(atlasTex, TexPath("Decals"), FilterMode.Bilinear, true);
                var aimp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(atlas));
                aimp.textureCompression = TextureImporterCompression.CompressedHQ;
                aimp.SaveAndReimport();

                var palette = new List<Color>();
                int ColorIndex(Color c)
                {
                    int i = palette.FindIndex(x => Mathf.Abs(x.r - c.r) + Mathf.Abs(x.g - c.g) + Mathf.Abs(x.b - c.b) < 0.01f);
                    if (i >= 0) return i;
                    palette.Add(c);
                    return palette.Count - 1;
                }

                var site = new Group();
                var mast = new Group();
                var top = new Group();
                int skipped = 0;
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    string node = GlbBake.CleanName(mf.name);
                    if (Skip.Contains(node)) { skipped++; continue; }
                    var g = Under(mf.transform, "Crane_Top") ? top
                          : Under(mf.transform, "Crane_Mast") || Under(mf.transform, "Ladders_Walkways") || MastExtras.Contains(node) ? mast
                          : site;
                    Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                    if (Nudge.TryGetValue(node, out var nudge)) m = Matrix4x4.Translate(nudge) * m;
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        string mat = GlbBake.CleanName(mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)].name);
                        if (!mats.TryGetValue(mat, out var info)) throw new Exception("Unbekanntes Material " + mat);
                        if (tiled.Contains(mat)) g.Part("Tiled_" + mat).Add(mf.sharedMesh, sub, Vector2.zero, m, true, info.tiling ?? new Rect(0f, 0f, 1f, 1f));
                        else if (tex.ContainsKey(mat)) g.Part("Decals").Add(mf.sharedMesh, sub, Vector2.zero, m, true, rects[decalNames.IndexOf(mat)]);
                        else if (info.emissive) g.Part("Glow").Add(mf.sharedMesh, sub, GlbBake.PaletteUv(ColorIndex(info.glow), 8), m);
                        else g.Part("Body").Add(mf.sharedMesh, sub, GlbBake.PaletteUv(ColorIndex(info.color), 8), m);
                        if (Solid.Contains(node) && g != top) g.solid.Add(mf.sharedMesh, sub, Vector2.zero, m);
                    }
                    // Grind-Linien (Kanten am Rohbau, Rohrstapel, Treppengelaender) - nicht auf dem drehenden Ausleger
                    if (node.StartsWith("Grind_") && g != top)
                    {
                        if (!g.grind.TryGetValue(node, out var pts)) g.grind[node] = pts = new List<Vector3>();
                        var mv = mf.sharedMesh.vertices;
                        var tri = mf.sharedMesh.triangles;
                        for (int i = 0; i < tri.Length; i += 3)
                            for (int e = 0; e < 3; e++)
                            {
                                Vector3 a = m.MultiplyPoint3x4(mv[tri[i + e]]), b = m.MultiplyPoint3x4(mv[tri[i + (e + 1) % 3]]);
                                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.4f));
                                for (int k = 0; k <= steps; k++) pts.Add(Vector3.Lerp(a, b, k / (float)steps));
                            }
                    }
                }

                var paletteTex = GlbBake.Palette(TexPath("Palette"), palette.ToArray(), 8);
                var bodyMat = GlbBake.ToonMat(MatPath("Body"), Color.white, paletteTex, 0.25f, rim: 0.15f);
                var glowMat = GlbBake.ToonMat(MatPath("Glow"), Color.white, paletteTex, 0f, 1.6f, rim: 0f);
                var decalMat = GlbBake.ToonMat(MatPath("Decals"), Color.white, atlas, 0f, cutoff: 0.5f, rim: 0.1f);
                var tiledMats = new Dictionary<string, Material>();
                Material MatFor(string key)
                {
                    if (key == "Body") return bodyMat;
                    if (key == "Glow") return glowMat;
                    if (key == "Decals") return decalMat;
                    if (tiledMats.TryGetValue(key, out var tm)) return tm;
                    string mat = key.Substring(6);
                    var t = GlbBake.SaveTexture(GlbBake.Readable(tex[mat]), TexPath(key), FilterMode.Bilinear, true);
                    var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t));
                    imp.wrapMode = TextureWrapMode.Repeat;
                    imp.SaveAndReimport();
                    t = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(t));
                    return tiledMats[key] = GlbBake.ToonMat(MatPath(key), Color.white, t, 0.2f, rim: 0.1f);
                }

                var root = new GameObject(Name);
                try
                {
                    var siteT = new GameObject("Site").transform;
                    siteT.SetParent(root.transform, false);
                    siteT.SetLocalPositionAndRotation(SiteOffset, Quaternion.Euler(0f, SiteYaw, 0f));
                    var craneT = new GameObject("Crane").transform;
                    craneT.SetParent(siteT, false);
                    craneT.localScale = Vector3.one * CraneScale;
                    var mastT = new GameObject("Mast").transform;
                    mastT.SetParent(craneT, false);
                    var topT = new GameObject("Top").transform;
                    topT.SetParent(craneT, false);
                    topT.gameObject.AddComponent<CraneSlew>();

                    int railLayer = LayerMask.NameToLayer("Rail");
                    int rails = 0;
                    void Emit(Group g, Transform parent, string prefix)
                    {
                        foreach (var kv in g.parts)
                        {
                            if (kv.Value.Empty) continue;
                            bool shadows = kv.Key == "Body";
                            var r = GlbBake.Part(parent, kv.Key, GlbBake.SaveMesh(kv.Value.ToMesh(Name + "_" + prefix + "_" + kv.Key), MeshPath(prefix + "_" + kv.Key)), MatFor(kv.Key), shadows);
                            if (kv.Key == "Decals") r.gameObject.layer = LayerMask.NameToLayer("Detail");
                        }
                        if (!g.solid.Empty)
                        {
                            var col = new GameObject("Solid");
                            col.transform.SetParent(parent, false);
                            col.AddComponent<MeshCollider>().sharedMesh = GlbBake.SaveMesh(g.solid.ToMesh(Name + "_" + prefix + "_Collider"), MeshPath(prefix + "_Collider"));
                        }
                        foreach (var kv in g.grind)
                        {
                            int i = 0;
                            foreach (var chain in ParkhausAssets.Chains(kv.Value))
                            {
                                var go = new GameObject("Grind_" + kv.Key + "_" + i++);
                                go.transform.SetParent(parent, false);
                                if (railLayer >= 0) go.layer = railLayer;
                                var rail = go.AddComponent<GrindRail>();
                                rail.label = "BAUSTELLE";
                                rail.points = chain.ToArray();
                                rails++;
                            }
                        }
                    }
                    Emit(site, siteT, "Site");
                    Emit(mast, mastT, "Mast");
                    Emit(top, topT, "Top");

                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    Debug.Log($"[Baustelle] Prefab gebaut: {PrefabPath} ({palette.Count} Farben, {decalNames.Count} Texturen im Atlas, gekachelt: {string.Join(", ", tiled)}, " +
                              $"Teile Site {site.parts.Count} / Mast {mast.parts.Count} / Top {top.parts.Count}, {rails} Grind-Linien, {skipped} Teile weggelassen)");
                }
                finally { Object.DestroyImmediate(root); }
            }
            finally { Object.DestroyImmediate(inst); }
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ In die Stadt

        public static void PlaceInCity()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) BuildPrefab();
            var scene = EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var city = scene.GetRootGameObjects().FirstOrDefault(g => g.transform.Find(BaustelleSite.BlockName) != null);
            if (city == null) throw new Exception("Stadt mit " + BaustelleSite.BlockName + " nicht gefunden");
            MeshStore.PersistHook = (mesh, key) => GlbBake.SaveMesh(mesh, "Assets/_Game/Generated/Meshes/" + key + ".asset");
            try
            {
                var inst = BaustelleSite.Apply(city.transform);
                if (inst == null) throw new Exception("Baustelle konnte nicht gesetzt werden");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                var linked = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                linked.transform.SetParent(inst.transform.parent, false);
                linked.transform.SetPositionAndRotation(inst.transform.position, inst.transform.rotation);
                linked.name = inst.name;
                Object.DestroyImmediate(inst);
            }
            finally { MeshStore.PersistHook = null; }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ Kontrolle

        public static void RenderCheck()
        {
            Directory.CreateDirectory("Logs/baustelle");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var b = GameObject.Find(Name);
            if (b == null) throw new Exception("Baustelle nicht in der Stadt");
            var top = b.GetComponentInChildren<CraneSlew>();
            var ph = GameObject.Find(ParkhausSite.PrefabName);
            var cam = Camera.main;
            Vector3 mastPos = top.transform.position;
            Debug.Log($"[Baustelle] Mast bei {mastPos}, Abstand zur Parkhaus-Mitte {(ph != null ? Vector3.Distance(new Vector3(mastPos.x, 0, mastPos.z), ph.transform.position) : -1):0} m");

            // Ausleger so drehen, dass er zum Parkhaus zeigt (Spitze liegt im Modell bei (-31.5, -27))
            void Aim(Vector3 worldDir)
            {
                Vector3 tipLocal = new Vector3(-31.5f, 0f, -27f).normalized;
                Vector3 parentDir = top.transform.parent.InverseTransformDirection(worldDir);
                parentDir.y = 0f;
                float yaw = Vector3.SignedAngle(tipLocal, parentDir.normalized, Vector3.up);
                top.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
            void Shot(Vector3 pos, Vector3 look, float fov, string file)
            {
                cam.fieldOfView = fov;
                cam.transform.position = pos;
                cam.transform.LookAt(look);
                SimTests.Capture(cam, "Logs/baustelle/" + file + ".png");
            }
            Vector3 c = b.transform.position;
            Vector3 toPark = ph != null ? (ph.transform.position - mastPos) : Vector3.left;
            toPark.y = 0f;
            Aim(toPark);
            Shot(c + new Vector3(-40f, 70f, 120f), c + new Vector3(-50f, 25f, 0f), 55f, "over_parkhaus");
            Shot(ph != null ? ph.transform.position + new Vector3(0f, 32f, 40f) : c, mastPos + Vector3.up * 50f, 60f, "from_parkhaus_roof");
            Aim(Vector3.forward);
            Shot(c + new Vector3(-60f, 25f, 60f), c + new Vector3(0f, 20f, 0f), 55f, "site_overview");
            Shot(c + new Vector3(-30f, 2.5f, 8f), c + new Vector3(0f, 2f, 8f), 60f, "gate");
            Shot(c + new Vector3(-10f, 14f, -4f), c + new Vector3(8f, 5f, 4f), 65f, "inside");
            Shot(c + new Vector3(60f, 30f, -70f), c + new Vector3(-10f, 25f, 0f), 55f, "back");
        }
    }
}
