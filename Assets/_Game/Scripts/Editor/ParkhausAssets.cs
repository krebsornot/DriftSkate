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
    /// Drift-Parkhaus aus Art/Environment/parkhaus_drift.glb (Fynns Modell, 64 x 64 m, Spirale + Ringstrassen, 4 Etagen + Dach).
    /// Wenige Draw Calls: Bau ueber Paletten-Textur (mit Schatten), Bodenmarkierungen, Neon (leuchtend).
    /// Hitboxen aus den Knoten-Praefixen: Drive_ (befahrbar), Wall_ (fest), Curb_ und Rail_ (fest und grindbar).
    /// Das Dachdeck bekommt einen eigenen Collider "Roof", damit Hydranten-Fontaenen aufs Parkhausdach zielen.
    /// Grind-Linien werden aus den Curb_/Rail_-Meshes gewonnen (Mittellinie, oben auf der Kante).
    /// Der Boden wird so abgesenkt, dass die Einfahrt genau auf Gehweghoehe liegt.
    /// Menue: DriftSkate → Parkhaus.
    /// </summary>
    public static class ParkhausAssets
    {
        const string Name = "Parkhaus";
        const string Root = "Assets/_Game/Parkhaus";
        const string SourceGlb = "Art/Environment/parkhaus_drift.glb";
        const string GlbPath = Root + "/Source/parkhaus_drift.glb";
        static string PrefabPath => "Assets/_Game/Resources/" + ParkhausSite.PrefabPath + ".prefab";
        const float SidewalkTop = 0.18f;
        static string MatPath(string part) => Root + "/Materials/" + Name + "_" + part + ".mat";
        static string MeshPath(string part) => Root + "/Meshes/" + Name + "_" + part + ".asset";
        static string TexPath(string part) => Root + "/Textures/" + Name + "_" + part + ".png";

        [MenuItem("DriftSkate/Parkhaus/Prefab bauen")] public static void Build() => BuildPrefab();
        [MenuItem("DriftSkate/Parkhaus/In die Stadt setzen (Block_03)")] public static void Place() => PlaceInCity();
        [MenuItem("DriftSkate/Parkhaus/Kontrollbilder rendern")] public static void Render() => RenderCheck();

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
                string Prefix(Transform t)
                {
                    for (; t != null; t = t.parent)
                    {
                        string n = GlbBake.CleanName(t.name);
                        foreach (var p in new[] { "Drive", "Wall", "Curb", "Rail", "Neon", "Deco" })
                            if (n == p || n.StartsWith(p + "_")) return p;
                    }
                    return "Deco";
                }

                Matrix4x4 toRoot = inst.transform.worldToLocalMatrix;
                var body = new GlbBake.MeshBuilder();
                var markings = new GlbBake.MeshBuilder();
                var neon = new GlbBake.MeshBuilder();
                var drive = new GlbBake.MeshBuilder();   // Collider: befahrbar (ohne Dach)
                var roof = new GlbBake.MeshBuilder();    // Collider: Dachdeck ("Roof")
                var solid = new GlbBake.MeshBuilder();   // Collider: Waende, Kanten, Gelaender, geparkte Autos
                var grind = new Dictionary<string, List<Vector3>>();
                var glowColors = new List<Color>();

                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    string node = GlbBake.CleanName(mf.name);
                    string prefix = Prefix(mf.transform);
                    Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        string mat = GlbBake.CleanName(mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)].name);
                        if (!colors.TryGetValue(mat, out var col)) col = Color.white;
                        var uv = GlbBake.PaletteUv(ColorIndex(col), 8);
                        if (prefix == "Neon") { neon.Add(mf.sharedMesh, sub, uv, m); continue; }
                        if (node == "Deco_Floor_Markings") markings.Add(mf.sharedMesh, sub, uv, m);
                        else body.Add(mf.sharedMesh, sub, uv, m);
                        if (prefix == "Drive") (node == "Drive_Roof" ? roof : drive).Add(mf.sharedMesh, sub, Vector2.zero, m);
                        else if (prefix == "Wall" || prefix == "Curb" || prefix == "Rail") solid.Add(mf.sharedMesh, sub, Vector2.zero, m);
                    }
                    // Grind-Linien: gleichfarbige Streifen-Teile (_Yellow/_Black) gehoeren zu einer Kante
                    if (prefix == "Curb" || prefix == "Rail")
                    {
                        string key = node.Replace("_Yellow", "").Replace("_Black", "");
                        if (!grind.TryGetValue(key, out var pts)) grind[key] = pts = new List<Vector3>();
                        // Punkte entlang aller Dreieckskanten (lange Kaesten haben nur Eckpunkte an den Enden)
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
                var root = new GameObject(Name);
                try
                {
                    // Absenken: Boden der Ringstrasse auf Gehweghoehe (Einfahrt ohne Stufe)
                    var offsetRoot = new GameObject("Model").transform;
                    offsetRoot.SetParent(root.transform, false);

                    Transform Part(string part, GlbBake.MeshBuilder b, Material mat, bool shadows, int layer = -1)
                    {
                        if (b.Empty) return null;
                        return GlbBake.Part(offsetRoot, part, GlbBake.SaveMesh(b.ToMesh(Name + "_" + part), MeshPath(part)), mat, shadows, layer).transform;
                    }
                    Part("Body", body, GlbBake.ToonMat(MatPath("Body"), Color.white, paletteTex, 0.3f, rim: 0.15f), true);
                    Part("Markings", markings, GlbBake.ToonMat(MatPath("Markings"), Color.white, paletteTex, 0f, rim: 0f), false);
                    Part("Neon", neon, GlbBake.ToonMat(MatPath("Neon"), Color.white, paletteTex, 0f, 1.6f, rim: 0f), false);

                    void Collider(string part, GlbBake.MeshBuilder b)
                    {
                        if (b.Empty) return;
                        var go = new GameObject(part);
                        go.transform.SetParent(offsetRoot, false);
                        go.AddComponent<MeshCollider>().sharedMesh = GlbBake.SaveMesh(b.ToMesh(Name + "_" + part + "Collider"), MeshPath(part + "Collider"));
                    }
                    Collider("Drive", drive);
                    Collider("Roof", roof);
                    Collider("Solid", solid);

                    // Hoehe der Ringstrasse an der Einfahrt messen (Strahl von oben auf den Fahr-Collider)
                    Physics.SyncTransforms();
                    float floor = 0f;
                    var driveCol = offsetRoot.Find("Drive").GetComponent<MeshCollider>();
                    if (driveCol.Raycast(new Ray(new Vector3(18f, 4f, 26f), Vector3.down), out RaycastHit hit, 10f)) floor = hit.point.y;
                    offsetRoot.localPosition = Vector3.up * (SidewalkTop - floor);

                    // Grind-Linien
                    var rails = new GameObject("Rails").transform;
                    rails.SetParent(offsetRoot, false);
                    int railLayer = LayerMask.NameToLayer("Rail");
                    int count = 0;
                    foreach (var kv in grind)
                    {
                        int i = 0;
                        foreach (var chain in Chains(kv.Value))
                        {
                            var go = new GameObject("Grind_" + kv.Key + "_" + i++);
                            go.transform.SetParent(rails, false);
                            if (railLayer >= 0) go.layer = railLayer;
                            var r = go.AddComponent<GrindRail>();
                            r.label = kv.Key.StartsWith("Rail") ? "PARKHAUS RAIL" : "PARKHAUS CURB";
                            r.points = chain.ToArray();
                            count++;
                        }
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    Debug.Log($"[Parkhaus] Prefab gebaut: {PrefabPath} ({palette.Count} Farben, {count} Grind-Linien, Boden an der Einfahrt {floor:0.00} m -> abgesenkt um {floor - SidewalkTop:0.00} m)");
                }
                finally { Object.DestroyImmediate(root); }
            }
            finally { Object.DestroyImmediate(inst); }
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Mittellinien einer Kante aus ihren Eckpunkten: auf ein 0,7-m-Raster zusammenfassen (Mitte, Oberkante),
        /// dann zu Linienzuegen verbinden (naechster Nachbar bis 1,6 m). Knicke ueber 50 Grad trennen (eckige Gelaender),
        /// fast geschlossene lange Linien werden zum Ring.
        /// </summary>
        internal static List<List<Vector3>> Chains(List<Vector3> verts)
        {
            const float cell = 0.7f, maxGap = 1.6f, breakAngle = 50f;
            var cells = new Dictionary<Vector3Int, (Vector3 sum, int n, float top)>();
            foreach (var v in verts)
            {
                var k = new Vector3Int(Mathf.FloorToInt(v.x / cell), Mathf.FloorToInt(v.y / cell), Mathf.FloorToInt(v.z / cell));
                cells.TryGetValue(k, out var c);
                cells[k] = (c.sum + v, c.n + 1, c.n == 0 ? v.y : Mathf.Max(c.top, v.y));
            }
            var pts = cells.Values.Select(c => { var p = c.sum / c.n; p.y = c.top + 0.02f; return p; }).ToList();
            var used = new bool[pts.Count];
            var result = new List<List<Vector3>>();

            int Nearest(Vector3 from, Vector3? dir)
            {
                int best = -1;
                float bd = maxGap * maxGap;
                for (int i = 0; i < pts.Count; i++)
                {
                    if (used[i]) continue;
                    Vector3 d = pts[i] - from;
                    float sq = d.sqrMagnitude;
                    if (sq < 0.04f) { used[i] = true; continue; } // Doppelpunkte schlucken
                    if (dir.HasValue && Vector3.Dot(d, dir.Value) < 0f) continue;
                    if (sq < bd) { bd = sq; best = i; }
                }
                return best;
            }

            List<Vector3> Grow(int start)
            {
                var line = new List<Vector3> { pts[start] };
                used[start] = true;
                for (int pass = 0; pass < 2; pass++)
                {
                    while (true)
                    {
                        Vector3 last = line[line.Count - 1];
                        Vector3? dir = line.Count > 1 ? last - line[line.Count - 2] : (Vector3?)null;
                        int n = Nearest(last, dir);
                        if (n < 0) break;
                        used[n] = true;
                        line.Add(pts[n]);
                    }
                    line.Reverse(); // zweiter Durchgang: vom Anfang aus in die andere Richtung
                }
                return line;
            }

            for (int i = 0; i < pts.Count; i++)
            {
                if (used[i]) continue;
                var raw = Grow(i);
                // ausduennen (mind. 1 m Abstand), damit das Zittern der Rastermitten ueber die Kantenbreite keine Knicke macht
                var line = new List<Vector3> { raw[0] };
                for (int k = 1; k < raw.Count; k++)
                    if (Vector3.Distance(line[line.Count - 1], raw[k]) >= 1f || k == raw.Count - 1) line.Add(raw[k]);
                // an scharfen Knicken teilen
                var part = new List<Vector3> { line[0] };
                for (int k = 1; k < line.Count; k++)
                {
                    if (part.Count >= 2 && k < line.Count)
                    {
                        Vector3 a = part[part.Count - 1] - part[part.Count - 2], b = line[k] - part[part.Count - 1];
                        if (Vector3.Angle(a, b) > breakAngle) { Add(part); part = new List<Vector3> { part[part.Count - 1] }; }
                    }
                    part.Add(line[k]);
                }
                Add(part);
            }
            return result;

            void Add(List<Vector3> line)
            {
                float len = 0f;
                for (int k = 1; k < line.Count; k++) len += Vector3.Distance(line[k - 1], line[k]);
                if (len < 2f) return;
                if (len > 12f && Vector3.Distance(line[0], line[line.Count - 1]) < maxGap) line.Add(line[0]);
                result.Add(line);
            }
        }

        // ------------------------------------------------------------------ In die Stadt

        public static void PlaceInCity()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) BuildPrefab();
            var scene = EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var city = scene.GetRootGameObjects().FirstOrDefault(g => g.transform.Find(ParkhausSite.BlockName) != null);
            if (city == null) throw new Exception("Stadt mit " + ParkhausSite.BlockName + " nicht gefunden");
            // Beschnittene Fenster-Meshes wie beim Stadtbau unter Generated/Meshes speichern
            MeshStore.PersistHook = (mesh, key) =>
            {
                string path = "Assets/_Game/Generated/Meshes/" + key + ".asset";
                return GlbBake.SaveMesh(mesh, path);
            };
            try
            {
                var inst = ParkhausSite.Apply(city.transform);
                if (inst == null) throw new Exception("Parkhaus konnte nicht gesetzt werden");
                // als Prefab-Instanz, damit Aenderungen am Prefab automatisch ankommen
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
            Directory.CreateDirectory("Logs/parkhaus");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var p = GameObject.Find(Name);
            if (p == null) throw new Exception("Parkhaus nicht in der Stadt");
            var t = p.transform;
            Physics.SyncTransforms();

            // Hoehenprofil: Strasse -> Einfahrt -> Ringstrasse, Ringstrasse -> Tor in die Spirale (Westseite, P1 und P2)
            int mask = ~LayerMask.GetMask("Car", "Skater", "Ignore Raycast", "Rail");
            string Profile(Vector3 a, Vector3 b, float above, int steps)
            {
                var s = new List<string>();
                for (int i = 0; i <= steps; i++)
                {
                    Vector3 q = t.TransformPoint(Vector3.Lerp(a, b, i / (float)steps));
                    s.Add(Physics.Raycast(q + Vector3.up * above, Vector3.down, out RaycastHit h, above + 2f, mask, QueryTriggerInteraction.Ignore)
                        ? h.point.y.ToString("0.00") : "-");
                }
                return string.Join(" ", s);
            }
            Debug.Log("[Parkhaus] Einfahrt (x=18, z 40 -> 22): " + Profile(new Vector3(18f, 0f, 40f), new Vector3(18f, 0f, 22f), 3f, 18));
            Debug.Log("[Parkhaus] Ausfahrt (x=-18, z 40 -> 22): " + Profile(new Vector3(-18f, 0f, 40f), new Vector3(-18f, 0f, 22f), 3f, 18));
            Debug.Log("[Parkhaus] P1 Tor West (x -30 -> -8, z 0): " + Profile(new Vector3(-30f, 0f, 0f), new Vector3(-8f, 0f, 0f), 3f, 22));
            Debug.Log("[Parkhaus] P2 Tor West (x -30 -> -8, z 0): " + Profile(new Vector3(-30f, 6.5f, 0f), new Vector3(-8f, 6.5f, 0f), 3f, 22));
            Debug.Log("[Parkhaus] Ring P1 (z 25, x -25 -> 25): " + Profile(new Vector3(-25f, 0f, 25f), new Vector3(25f, 0f, 25f), 3f, 10));

            var cam = Camera.main;
            void Shot(Vector3 local, Vector3 look, float fov, string file)
            {
                cam.fieldOfView = fov;
                cam.transform.position = t.TransformPoint(local);
                cam.transform.LookAt(t.TransformPoint(look));
                SimTests.Capture(cam, "Logs/parkhaus/city_" + file + ".png");
            }
            Shot(new Vector3(60f, 45f, 95f), new Vector3(0f, 10f, 0f), 55f, "overview");
            Shot(new Vector3(18f, 2.2f, 48f), new Vector3(18f, 1.5f, 20f), 60f, "entrance");
            Shot(new Vector3(24f, 2.2f, 24f), new Vector3(-10f, 1.5f, 24f), 70f, "ring_p1");
            Shot(new Vector3(-26f, 8.7f, 14f), new Vector3(-12f, 7.5f, 0f), 70f, "gate_p2");
            Shot(new Vector3(-12f, 9f, -2f), new Vector3(12f, 6f, -10f), 70f, "helix");
            Shot(new Vector3(28f, 30f, 28f), new Vector3(0f, 26f, 0f), 60f, "roof");
            Shot(new Vector3(-70f, 25f, -40f), new Vector3(0f, 12f, 0f), 50f, "back");
        }
    }
}
