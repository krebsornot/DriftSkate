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
    /// Herz-Denkmal (Art/Props/heart_monument_streetart.glb, Fynns Modell) statt des Brunnens in der Mitte des Brunnenplatzes.
    /// Drei Draw Calls: alles Einfarbige ueber eine Paletten-Textur, Herz und Rand mit Hoehenverlauf (im Modell Vertex-Farben,
    /// die der Toon-Shader nicht kann) und die Neon-Roehre leuchtend. Auf der Stahlkante des runden Sockels liegt ein
    /// geschlossener Rund-Grind (GrindRail.Closed: man kann endlos im Kreis grinden).
    /// Das Prefab liegt in Resources, damit auch CityBuilder es beim Neubau der Stadt einsetzt.
    /// Menue: DriftSkate → Herz-Denkmal.
    /// </summary>
    public static class MonumentAssets
    {
        const string Name = "HeartMonument";
        const string Root = "Assets/_Game/HeartMonument";
        const string SourceGlb = "Art/Props/heart_monument_streetart.glb";
        const string GlbPath = Root + "/Source/heart_monument_streetart.glb";
        const string PrefabPath = "Assets/_Game/Resources/" + CityBuilder.HeartMonumentPrefab + ".prefab";
        const float Scale = 1.5f;      // Sockel dann ca. 9,3 m breit, Grind-Kante 0,77 m hoch (Brunnenrand war 0,72 m)
        const int RingPoints = 96;
        static string MatPath(string part) => Root + "/Materials/" + Name + "_" + part + ".mat";
        static string MeshPath(string part) => Root + "/Meshes/" + Name + "_" + part + ".asset";
        static string TexPath(string part) => Root + "/Textures/" + Name + "_" + part + ".png";

        // Teile mit Vertex-Farbverlauf: Spalte in der Verlaufs-Textur (Herz und Rand teilen sich eine), Neon leuchtet
        static readonly Dictionary<string, int> GradientNodes = new Dictionary<string, int>
            { { "heart_sculpture", 0 }, { "rim_glow_front", 0 }, { "rim_glow_back", 0 }, { "neon_rim_tube", 1 } };
        const int GradientColumns = 2, GradientRows = 64, ColumnPx = 4;

        [MenuItem("DriftSkate/Herz-Denkmal/Prefab bauen")] public static void Build() => BuildPrefab();
        [MenuItem("DriftSkate/Herz-Denkmal/In die Stadt setzen (statt Brunnen)")] public static void Place() => PlaceInCity();
        [MenuItem("DriftSkate/Herz-Denkmal/Kontrollbilder rendern")] public static void Render() => RenderCheck();

        /// <summary>Batch: Prefab bauen, Brunnen ersetzen, Kontrollbilder (Logs/heart/*.png).</summary>
        public static void BatchAll()
        {
            BuildPrefab();
            PlaceInCity();
            RenderCheck();
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
            // Auch die Beton-Materialien haben einen Farbfaktor; ihre feine Schmutz-Textur lassen wir weg (Toon-Look)
            var colors = GlbBake.ReadColors(GlbPath);

            var inst = Object.Instantiate(src);
            try
            {
                var palette = new List<Color>();
                int ColorIndex(Color c)
                {
                    int i = palette.FindIndex(x => Mathf.Abs(x.r - c.r) + Mathf.Abs(x.g - c.g) + Mathf.Abs(x.b - c.b) < 0.01f);
                    if (i >= 0) return i;
                    palette.Add(c);
                    return palette.Count - 1;
                }

                var body = new GlbBake.MeshBuilder();
                var gradient = new GlbBake.MeshBuilder();
                var neon = new GlbBake.MeshBuilder();
                Bounds coping = default, heart = default;
                var samples = new List<(int column, float y, Color c)>();
                var gradientParts = new List<(Mesh mesh, Matrix4x4 m, int column)>();
                float minY = float.MaxValue, maxY = float.MinValue;

                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    string node = GlbBake.CleanName(mf.name);
                    Matrix4x4 m = inst.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    if (node == "steel_coping_ledge") coping = TransformBounds(mf.sharedMesh.bounds, m);
                    if (node == "heart_sculpture")
                    {
                        // genau aus den Ecken (die Mesh-Bounds wuerden durch die Drehung aufgeblaeht)
                        var hv = mf.sharedMesh.vertices;
                        heart = new Bounds(m.MultiplyPoint3x4(hv[0]), Vector3.zero);
                        foreach (var v in hv) heart.Encapsulate(m.MultiplyPoint3x4(v));
                    }
                    if (GradientNodes.TryGetValue(node, out int column))
                    {
                        // Vertex-Farben (linear) nach Hoehe sammeln
                        var verts = mf.sharedMesh.vertices;
                        var cols = mf.sharedMesh.colors;
                        if (cols == null || cols.Length != verts.Length) throw new Exception(node + " hat keine Vertex-Farben mehr");
                        for (int i = 0; i < verts.Length; i++)
                        {
                            float y = m.MultiplyPoint3x4(verts[i]).y;
                            samples.Add((column, y, cols[i].gamma));
                            minY = Mathf.Min(minY, y);
                            maxY = Mathf.Max(maxY, y);
                        }
                        gradientParts.Add((mf.sharedMesh, m, column));
                        continue;
                    }
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        string mat = GlbBake.CleanName(mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)].name);
                        if (!colors.TryGetValue(mat, out var c)) throw new Exception("Unbekanntes Material " + mat);
                        body.Add(mf.sharedMesh, sub, GlbBake.PaletteUv(ColorIndex(c), 8), m);
                    }
                }
                if (coping.size == Vector3.zero) throw new Exception("steel_coping_ledge fehlt im Modell");

                // Verlaufs-Textur: je Spalte der Mittelwert der Vertex-Farben pro Hoehenstufe, Luecken aufgefuellt
                var gradTex = new Texture2D(GradientColumns * ColumnPx, GradientRows, TextureFormat.RGBA32, false);
                float Row(float y) => Mathf.Clamp01((y - minY) / Mathf.Max(0.001f, maxY - minY)) * (GradientRows - 1);
                for (int col = 0; col < GradientColumns; col++)
                {
                    var sum = new Color[GradientRows];
                    var n = new int[GradientRows];
                    foreach (var s in samples.Where(s => s.column == col))
                    {
                        int r = Mathf.RoundToInt(Row(s.y));
                        sum[r] += s.c;
                        n[r]++;
                    }
                    var rows = new Color?[GradientRows];
                    for (int r = 0; r < GradientRows; r++) if (n[r] > 0) rows[r] = sum[r] / n[r];
                    for (int r = 0; r < GradientRows; r++)
                    {
                        Color c = rows[r] ?? Nearest(rows, r);
                        c.a = 1f;
                        for (int x = 0; x < ColumnPx; x++) gradTex.SetPixel(col * ColumnPx + x, r, c);
                    }
                }
                gradTex.Apply();
                var gradAsset = GlbBake.SaveTexture(gradTex, TexPath("Gradient"), FilterMode.Bilinear, false);

                foreach (var (mesh, m, column) in gradientParts)
                {
                    // UV = Spaltenmitte, Hoehe im Verlauf (eigene Kopie, das Import-Mesh bleibt unveraendert)
                    var copy = Object.Instantiate(mesh);
                    var verts = copy.vertices;
                    var uv = new Vector2[verts.Length];
                    float u = (column + 0.5f) / GradientColumns;
                    for (int i = 0; i < verts.Length; i++) uv[i] = new Vector2(u, (Row(m.MultiplyPoint3x4(verts[i]).y) + 0.5f) / GradientRows);
                    copy.uv = uv;
                    for (int sub = 0; sub < copy.subMeshCount; sub++) (column == 1 ? neon : gradient).Add(copy, sub, Vector2.zero, m, true);
                    Object.DestroyImmediate(copy);
                }

                var paletteTex = GlbBake.Palette(TexPath("Palette"), palette.ToArray(), 8);
                var root = new GameObject(Name);
                try
                {
                    var visual = new GameObject("Visual").transform;
                    visual.SetParent(root.transform, false);
                    visual.localScale = Vector3.one * Scale;
                    var bodyMesh = GlbBake.SaveMesh(body.ToMesh(Name + "_Body"), MeshPath("Body"));
                    GlbBake.Part(visual, "Body", bodyMesh, GlbBake.ToonMat(MatPath("Body"), Color.white, paletteTex, 0.35f, rim: 0.15f), true);
                    GlbBake.Part(visual, "Heart", GlbBake.SaveMesh(gradient.ToMesh(Name + "_Heart"), MeshPath("Heart")),
                                 GlbBake.ToonMat(MatPath("Heart"), Color.white, gradAsset, 0.35f, rim: 0.25f), true);
                    GlbBake.Part(visual, "Neon", GlbBake.SaveMesh(neon.ToMesh(Name + "_Neon"), MeshPath("Neon")),
                                 GlbBake.ToonMat(MatPath("Neon"), Color.white, gradAsset, 0f, 1.6f, rim: 0f), false);

                    // Fest: runder Sockel (Prisma, Skater landen oben drauf) und das Herz als Kasten
                    var bb = bodyMesh.bounds;
                    var plinth = new GameObject("PlinthCollider");
                    plinth.transform.SetParent(root.transform, false);
                    var mc = plinth.AddComponent<MeshCollider>();
                    mc.sharedMesh = GlbBake.SaveMesh(Prism(coping.extents.x * Scale, coping.max.y * Scale, 48), MeshPath("PlinthCollider"));
                    mc.convex = true;
                    var box = root.AddComponent<BoxCollider>();
                    box.center = heart.center * Scale;
                    box.size = heart.size * Scale;

                    // Rund-Grind oben auf der Stahlkante (geschlossener Ring, der letzte Punkt ist wieder der erste)
                    var ring = new GameObject("Grind_HEART GRIND");
                    ring.transform.SetParent(root.transform, false);
                    int railLayer = LayerMask.NameToLayer("Rail");
                    if (railLayer >= 0) ring.layer = railLayer;
                    var rail = ring.AddComponent<GrindRail>();
                    rail.label = "HEART GRIND";
                    float radius = (coping.extents.x - 0.05f) * Scale, height = coping.max.y * Scale + 0.02f;
                    rail.points = Enumerable.Range(0, RingPoints + 1).Select(i =>
                    {
                        float a = (i % RingPoints) / (float)RingPoints * Mathf.PI * 2f;
                        return new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius);
                    }).ToArray();

                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    Debug.Log($"[Heart] Prefab gebaut: {PrefabPath} ({palette.Count} Farben, Ring r={radius:0.00} m h={height:0.00} m, " +
                              $"Herz {box.size}, {bodyMesh.vertexCount} Ecken)");
                }
                finally { Object.DestroyImmediate(root); }
            }
            finally { Object.DestroyImmediate(inst); }
            AssetDatabase.SaveAssets();
        }

        static Color Nearest(Color?[] rows, int r)
        {
            for (int d = 1; d < rows.Length; d++)
            {
                if (r - d >= 0 && rows[r - d].HasValue) return rows[r - d].Value;
                if (r + d < rows.Length && rows[r + d].HasValue) return rows[r + d].Value;
            }
            return Color.magenta;
        }

        static Bounds TransformBounds(Bounds b, Matrix4x4 m)
        {
            var r = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int k = 0; k < 8; k++)
                r.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3(k & 1, (k >> 1) & 1, (k >> 2) & 1) * 2f - Vector3.one)));
            return r;
        }

        /// <summary>Senkrechtes Prisma (n Ecken) von y = 0 bis height, als Collider fuer den runden Sockel.</summary>
        static Mesh Prism(float radius, float height, int n)
        {
            var v = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                v.Add(p);
                v.Add(p + Vector3.up * height);
            }
            var t = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int a = i * 2, b = (i + 1) % n * 2;
                t.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                if (i > 0 && i < n - 1) { t.AddRange(new[] { 1, b + 1, a + 1 }); t.AddRange(new[] { 0, a, b }); }
            }
            var mesh = new Mesh { name = Name + "_PlinthCollider" };
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------ In die Stadt

        /// <summary>In der fertigen City-Szene den Brunnen (Becken, Wasser, Schalen, Wasser-Partikel, Rund-Grind) durch das Denkmal ersetzen.</summary>
        public static void PlaceInCity()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { BuildPrefab(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); }
            var scene = EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
            var block = all.FirstOrDefault(t => t.name.StartsWith("Block_") && t.name.EndsWith("_X"));
            if (block == null) throw new Exception("Brunnenplatz (Block_.._X) nicht gefunden");

            // Mitte: altes Becken bzw. schon gesetztes Denkmal
            var anchor = all.FirstOrDefault(t => t.name == "FountainBasin" || t.name == Name);
            if (anchor == null) throw new Exception("Weder Brunnen noch Denkmal auf dem Platz gefunden");
            var c = new Vector3(anchor.position.x, 0f, anchor.position.z);

            string[] fountainParts = { "FountainBasin", "FountainPillar", "FountainBowl", "FountainStem", "FountainBowlTop", "FountainBall",
                                       "FountainJet", "FountainCascade", Name };
            int removed = 0;
            foreach (var t in all)
            {
                if (t == null) continue;
                bool fountain = fountainParts.Contains(t.name) && t.parent == block;
                bool water = t.name == "Water" && t.parent == block && Flat(t.position - c) < 7f;
                bool ring = t.name == "Grind_FOUNTAIN GRIND";
                if (!fountain && !water && !ring) continue;
                Object.DestroyImmediate(t.gameObject);
                removed++;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.name = Name;
            go.transform.SetParent(block, false);
            go.transform.SetPositionAndRotation(c, Quaternion.identity);

            // Graffiti-Stelle vom Brunnen-Pfeiler auf die Rueckseite des Herzens (Nummer bleibt, damit gespeicherte Tags passen)
            var tag = block.GetComponentsInChildren<TagSpot>(true).OrderBy(t => Flat(t.transform.position - c)).FirstOrDefault();
            if (tag != null && Flat(tag.transform.position - c) < 3f) CityBuilder.MoveTagToMonument(tag.transform, go.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Heart] Denkmal bei {c} gesetzt, {removed} Brunnen-Teile entfernt, Graffiti-Stelle {(tag != null ? tag.name : "-")}");
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        // ------------------------------------------------------------------ Kontrolle

        public static void RenderCheck()
        {
            Directory.CreateDirectory("Logs/heart");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var m = GameObject.Find(Name);
            if (m == null) throw new Exception("Denkmal nicht in der Stadt");
            var cam = Camera.main;
            var t = m.transform;
            void Shot(Vector3 local, Vector3 look, float fov, string file)
            {
                cam.fieldOfView = fov;
                cam.transform.position = t.TransformPoint(local);
                cam.transform.LookAt(t.TransformPoint(look));
                SimTests.Capture(cam, "Logs/heart/" + file + ".png");
            }
            Shot(new Vector3(0f, 2.2f, 11f), new Vector3(0f, 1.8f, 0f), 55f, "front");
            Shot(new Vector3(0f, 2.2f, -11f), new Vector3(0f, 1.8f, 0f), 55f, "back");
            Shot(new Vector3(9f, 2.5f, 7f), new Vector3(0f, 1.5f, 0f), 55f, "front_34");
            Shot(new Vector3(-6f, 1.2f, 4f), new Vector3(0f, 0.8f, 0f), 55f, "ledge");
            Shot(new Vector3(0f, 1f, 0f) + new Vector3(18f, 14f, 18f), new Vector3(0f, 0.5f, 0f), 55f, "plaza");
            Shot(new Vector3(0f, 26f, 0.01f), Vector3.zero, 40f, "top");
        }
    }
}
