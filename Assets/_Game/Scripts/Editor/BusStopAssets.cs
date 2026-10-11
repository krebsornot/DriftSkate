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
    /// Baut die zwei Bushaltestellen aus Art/Props/bus_stops.glb (BusStop_A pink/violett, BusStop_B graphit/cyan)
    /// und verteilt ein paar davon auf die Gehwege, offene Seite zur Strasse.
    /// Je Haltestelle wenige Draw Calls: Rumpf mit Paletten-Textur, Neon (leuchtend), Plakate/Anzeige (leuchtend)
    /// und Aufkleber/Fahrplan/Schilder als Atlas. Hitboxen aus den Knoten-Praefixen wie beim Parkhaus:
    /// Drive_ (befahrbar: Dach heisst "Roof", Bank), Wall_ (fest), Curb_ (fest und grindbar: Dachkante, Bankkante).
    /// Menue: DriftSkate → Haltestellen.
    /// </summary>
    public static class BusStopAssets
    {
        const string SourceGlb = "Art/Props/bus_stops.glb";
        const string Root = "Assets/_Game/BusStops";
        const string GlbPath = Root + "/Source/bus_stops.glb";
        const string Group = "BusStops";
        static readonly string[] Variants = { "BusStop_A", "BusStop_B" };
        const int Count = 6;            // insgesamt, abwechselnd A und B
        const float Spacing = 85f;      // Mindestabstand untereinander
        const float Inset = 1.15f;      // Mitte von der Blockkante (Dachkante endet kurz vor dem Bordstein)
        const float OldStopClearance = 60f; // Abstand zur Baustellen-Haltestelle (BusStopResidents)

        static string PrefabPath(string v) => Root + "/" + v + ".prefab";
        static string MatPath(string v, string part) => Root + "/Materials/" + v + "_" + part + ".mat";
        static string MeshPath(string v, string part) => Root + "/Meshes/" + v + "_" + part + ".asset";
        static string TexPath(string v, string part) => Root + "/Textures/" + v + "_" + part + ".png";

        [MenuItem("DriftSkate/Haltestellen/Prefabs bauen")] public static void Build() => BuildPrefabs();
        [MenuItem("DriftSkate/Haltestellen/In die Stadt setzen")] public static void Place() => PlaceInCity();
        [MenuItem("DriftSkate/Haltestellen/Kontrollbilder rendern")] public static void Render() => RenderCheck();

        /// <summary>Batch: Prefabs bauen, verteilen, Kontrollbilder.</summary>
        public static void BatchAll()
        {
            BuildPrefabs();
            PlaceInCity();
            RenderCheck();
        }

        // ------------------------------------------------------------------ Bauen

        static string Prefix(Transform t)
        {
            string n = GlbBake.CleanName(t.name);
            foreach (var p in new[] { "Drive", "Wall", "Curb", "Rail", "Neon", "Deco" })
                if (n == p || n.StartsWith(p + "_")) return p;
            return "Deco";
        }

        /// <summary>Leuchtende Texturen (Plakate, Anzeige) haben in der GLB eine Leucht-Textur.</summary>
        static bool Glowing(string mat) => mat.StartsWith("Poster") || mat.StartsWith("Display");

        public static void BuildPrefabs()
        {
            GlbBake.EnsureFolders(Root + "/Source", Root + "/Meshes", Root + "/Materials", Root + "/Textures");
            if (!File.Exists(SourceGlb)) throw new Exception("Modell fehlt: " + SourceGlb);
            File.Copy(SourceGlb, GlbPath, true);
            AssetDatabase.ImportAsset(GlbPath, ImportAssetOptions.ForceUpdate);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
            if (src == null) throw new Exception("GLB konnte nicht importiert werden: " + GlbPath);
            var colors = GlbBake.ReadColors(GlbPath, untintedAsWhite: false);

            var inst = Object.Instantiate(src);
            try
            {
                var all = inst.GetComponentsInChildren<Transform>(true);
                foreach (var v in Variants)
                {
                    var node = all.FirstOrDefault(t => GlbBake.StripIndex(t.name) == v);
                    if (node == null) throw new Exception(v + " fehlt im Modell");
                    BuildVariant(v, node, colors);
                }
            }
            finally { Object.DestroyImmediate(inst); }
            AssetDatabase.SaveAssets();
        }

        static void BuildVariant(string v, Transform node, Dictionary<string, Color> colors)
        {
            // Wurzel der Haltestelle = Mitte unten, +Z = offene Seite (Dachkante vorn, Rueckwand hinten)
            Matrix4x4 toRoot = node.worldToLocalMatrix;
            var palette = new List<Color>();
            int ColorIndex(Color c)
            {
                int i = palette.FindIndex(x => Mathf.Abs(x.r - c.r) + Mathf.Abs(x.g - c.g) + Mathf.Abs(x.b - c.b) < 0.01f);
                if (i >= 0) return i;
                palette.Add(c);
                return palette.Count - 1;
            }

            // Texturierte Materialien (Plakate, Fahrplan, Schilder, Graffiti) kommen in einen Atlas
            var texByMat = new Dictionary<string, Texture>();
            foreach (var mr in node.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var um in mr.sharedMaterials)
                {
                    string n = GlbBake.CleanName(um.name);
                    var tex = GlbBake.FirstTexture(um);
                    if (tex != null && !texByMat.ContainsKey(n)) texByMat[n] = tex;
                }
            var texNames = texByMat.Keys.OrderBy(k => k).ToList();
            Rect[] rects = new Rect[0];
            Texture2D atlas = null;
            if (texNames.Count > 0)
            {
                var readable = texNames.Select(k => GlbBake.Readable(texByMat[k])).ToArray();
                var atlasTex = new Texture2D(2048, 2048, TextureFormat.RGBA32, false);
                rects = atlasTex.PackTextures(readable, 4, 2048, false);
                foreach (var r in readable) Object.DestroyImmediate(r);
                atlas = GlbBake.SaveTexture(atlasTex, TexPath(v, "Atlas"), FilterMode.Bilinear, true);
            }

            var body = new GlbBake.MeshBuilder();
            var neon = new GlbBake.MeshBuilder();
            var decals = new GlbBake.MeshBuilder();
            var screens = new GlbBake.MeshBuilder();
            var roof = new GlbBake.MeshBuilder();    // Collider "Roof": Dach (befahrbar, Hydranten finden es)
            var drive = new GlbBake.MeshBuilder();   // Collider: Bank
            var solid = new GlbBake.MeshBuilder();   // Collider: Rahmen, Waende, Pfosten, Muelleimer, Mast
            var grind = new Dictionary<string, List<Vector3>>();

            foreach (var mf in node.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null) continue;
                string name = GlbBake.StripIndex(mf.name);
                string prefix = Prefix(mf.transform);
                Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                {
                    string mat = GlbBake.CleanName(mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)].name);
                    if (texByMat.ContainsKey(mat))
                    {
                        var rect = rects[texNames.IndexOf(mat)];
                        (Glowing(mat) ? screens : decals).Add(mf.sharedMesh, sub, Vector2.zero, m, true, rect);
                    }
                    else
                    {
                        if (!colors.TryGetValue(mat, out var col)) col = Color.white;
                        var uv = GlbBake.PaletteUv(ColorIndex(col));
                        (prefix == "Neon" || mat.StartsWith("Neon") ? neon : body).Add(mf.sharedMesh, sub, uv, m);
                    }
                    if (prefix == "Drive") (name == "Drive_Roof" ? roof : drive).Add(mf.sharedMesh, sub, Vector2.zero, m);
                    else if (prefix == "Wall" || prefix == "Curb" || prefix == "Rail") solid.Add(mf.sharedMesh, sub, Vector2.zero, m);
                }
                if (prefix == "Curb" || prefix == "Rail")
                {
                    if (!grind.TryGetValue(name, out var pts)) grind[name] = pts = new List<Vector3>();
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

            var paletteTex = GlbBake.Palette(TexPath(v, "Palette"), palette.ToArray());
            var root = new GameObject(v);
            try
            {
                int detail = LayerMask.NameToLayer("Detail");
                var visual = new GameObject("Visual").transform;
                visual.SetParent(root.transform, false);

                void Part(string part, GlbBake.MeshBuilder b, Func<Material> mat, bool shadows, int layer = -1)
                {
                    if (b.Empty) return;
                    GlbBake.Part(visual, part, GlbBake.SaveMesh(b.ToMesh(v + "_" + part), MeshPath(v, part)), mat(), shadows, layer);
                }
                Part("Body", body, () => GlbBake.ToonMat(MatPath(v, "Body"), Color.white, paletteTex, 0.25f, rim: 0.15f), true);
                Part("Neon", neon, () => GlbBake.ToonMat(MatPath(v, "Neon"), Color.white, paletteTex, 0f, 1.6f, rim: 0f), false);
                Part("Screens", screens, () => GlbBake.ToonMat(MatPath(v, "Screens"), Color.white, atlas, 0f, 0.9f, rim: 0f), false);
                Part("Decals", decals, () => GlbBake.ToonMat(MatPath(v, "Decals"), Color.white, atlas, 0f, cutoff: 0.5f, rim: 0.15f), false, detail);

                void Collider(string part, GlbBake.MeshBuilder b)
                {
                    if (b.Empty) return;
                    var go = new GameObject(part);
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshCollider>().sharedMesh = GlbBake.SaveMesh(b.ToMesh(v + "_" + part + "Collider"), MeshPath(v, part + "Collider"));
                }
                Collider("Roof", roof);
                Collider("Drive", drive);
                Collider("Solid", solid);

                var rails = new GameObject("Rails").transform;
                rails.SetParent(root.transform, false);
                int railLayer = LayerMask.NameToLayer("Rail");
                int count = 0;
                foreach (var kv in grind)
                {
                    int i = 0;
                    foreach (var chain in ParkhausAssets.Chains(kv.Value))
                    {
                        var go = new GameObject("Grind_" + kv.Key + "_" + i++);
                        go.transform.SetParent(rails, false);
                        if (railLayer >= 0) go.layer = railLayer;
                        var r = go.AddComponent<GrindRail>();
                        r.label = kv.Key.Contains("Roof") ? "HALTESTELLEN-DACH" : "HALTESTELLEN-BANK";
                        r.points = chain.ToArray();
                        count++;
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(v));
                Debug.Log($"[Haltestellen] {v}: {palette.Count} Farben, {texNames.Count} Texturen im Atlas, {count} Grind-Linien ({string.Join(", ", grind.Keys)})");
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ------------------------------------------------------------------ Verteilen

        public static void PlaceInCity()
        {
            var prefabs = Variants.Select(v => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(v))).ToArray();
            if (prefabs.Any(p => p == null)) { BuildPrefabs(); prefabs = Variants.Select(v => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(v))).ToArray(); }
            var scene = EditorSceneManager.OpenScene(ProjectBuilder.CityPath);

            var old = GameObject.Find(Group);
            if (old != null) Object.DestroyImmediate(old);
            var group = new GameObject(Group).transform;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(group.gameObject, scene);

            Physics.SyncTransforms();
            int solid = ~LayerMask.GetMask("Skater", "Car", "Ignore Raycast");
            int ground = solid & ~LayerMask.GetMask("Rail");

            // Die Haltestelle an der Baustelle (BusStopResidents) entsteht erst zur Laufzeit: Abstand halten
            var avoid = new List<Vector3>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name == "Baustelle" && t.parent != null && t.parent.name == "Block_04_B")
                    avoid.Add(t.position + new Vector3(6f, 0f, 33.1f));

            // Kandidaten: Gehweg an den Blockseiten, mittig zwischen den Laternen (die stehen bei 0 und +-20)
            var rng = new System.Random(4242);
            var cands = new List<(Vector3 pos, Vector3 facing)>();
            Vector3[] sides = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            for (int row = 0; row < CityBuilder.Grid; row++)
                for (int col = 0; col < CityBuilder.Grid; col++)
                {
                    if ("HD".IndexOf(CityBuilder.BlockType(row, col)) >= 0) continue; // Hafen, Driftplatz
                    var center = new Vector3(CityBuilder.BlockCenter(col), 0f, CityBuilder.BlockCenter(row));
                    foreach (var side in sides)
                    {
                        Vector3 along = Vector3.Cross(Vector3.up, side);
                        foreach (float o in new[] { -10f, 10f, -27f, 27f })
                            cands.Add((center + side * (CityBuilder.Block * 0.5f - Inset) + along * o, side));
                    }
                }
            cands = cands.OrderBy(_ => rng.Next()).ToList();

            var placed = new List<Vector3>();
            var skipped = new Dictionary<string, int>();
            void Skip(string why) => skipped[why] = skipped.TryGetValue(why, out int n) ? n + 1 : 1;
            foreach (var cd in cands)
            {
                if (placed.Count >= Count) break;
                if (placed.Any(p => Vector3.Distance(p, cd.pos) < Spacing)) { Skip("Abstand"); continue; }
                if (avoid.Any(p => Vector3.Distance(p, cd.pos) < OldStopClearance)) { Skip("Baustellen-Haltestelle"); continue; }
                if (!Physics.Raycast(cd.pos + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 4f, ground, QueryTriggerInteraction.Ignore)) { Skip("kein Boden"); continue; }
                if (hit.point.y < 0.07f || hit.point.y > 0.25f) { Skip("kein Gehweg"); continue; }
                var prefab = prefabs[placed.Count % prefabs.Length];
                var rot = Quaternion.LookRotation(cd.facing);
                // Platz frei? Grundflaeche der Haltestelle (5,8 x 1,9 m) bis 2,6 m hoch, etwas Luft an den Seiten
                var probeCenter = hit.point + rot * new Vector3(0f, 1.45f, 0f);
                if (Physics.CheckBox(probeCenter, new Vector3(3.3f, 1.25f, 0.95f), rot, solid, QueryTriggerInteraction.Ignore)) { Skip("Platz belegt"); continue; }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.name = prefab.name + "_" + placed.Count;
                go.transform.SetParent(group, false);
                go.transform.SetPositionAndRotation(hit.point, rot);
                Physics.SyncTransforms(); // naechste Platzpruefung sieht diese Haltestelle
                placed.Add(hit.point);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Haltestellen] {placed.Count} verteilt: " + string.Join(", ", placed.Select(p => $"({p.x:0}, {p.z:0})")) +
                      " | uebersprungen: " + string.Join(", ", skipped.Select(kv => kv.Key + " " + kv.Value)));
        }

        // ------------------------------------------------------------------ Kontrolle

        public static void RenderCheck()
        {
            Directory.CreateDirectory("Logs/busstops");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var group = GameObject.Find(Group);
            if (group == null || group.transform.childCount == 0) throw new Exception("Keine Haltestellen in der Stadt");
            var cam = Camera.main;

            void Shot(Transform t, Vector3 local, Vector3 look, float fov, string file)
            {
                cam.fieldOfView = fov;
                cam.transform.position = t.TransformPoint(local);
                cam.transform.LookAt(t.TransformPoint(look));
                SimTests.Capture(cam, "Logs/busstops/" + file + ".png");
            }

            // Je Variante die erste Instanz
            foreach (var v in Variants)
            {
                Transform first = null;
                foreach (Transform t in group.transform) if (t.name.StartsWith(v)) { first = t; break; }
                if (first == null) continue;
                Shot(first, new Vector3(0f, 1.6f, 6.5f), new Vector3(0f, 1.3f, 0f), 55f, v + "_front");
                Shot(first, new Vector3(-5f, 2f, 5.5f), new Vector3(0f, 1.3f, 0f), 55f, v + "_front_34");
                Shot(first, new Vector3(4.5f, 2.4f, -4f), new Vector3(0f, 1.3f, 0f), 55f, v + "_back_34");
                Shot(first, new Vector3(0f, 6f, 9f), new Vector3(0f, 1f, 0f), 55f, v + "_top");
                Shot(first, new Vector3(-6f, 1.8f, 22f), new Vector3(0f, 1.5f, 0f), 50f, v + "_street");
            }
            // Ueberblick: alle Standorte von oben
            cam.fieldOfView = 60f;
            cam.transform.position = new Vector3(0f, 420f, -260f);
            cam.transform.LookAt(Vector3.zero);
            SimTests.Capture(cam, "Logs/busstops/overview.png");
        }
    }
}
