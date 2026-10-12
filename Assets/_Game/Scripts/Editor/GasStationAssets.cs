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
    /// Skate-Tankstelle aus Art/Environment/gas_station_skate.glb (Fynns Modell, three.js, echte Groesse 32 x 24 m Hof):
    /// Laden mit Flachdach, Tankdach auf vier Pfeilern, zwei Zapfinseln, Preis-Pylon, Skate-Ecke (Quarter, Funbox mit
    /// Down-Rail, Flatbar, Kicker, Manual-Pad, Curb, Treppe mit Handlauf und Hubba) und Deko (Reifen, Faesser, Container,
    /// Automat ...). Strasse, Gehweg und Hydrant aus dem Modell entfallen, die hat die Stadt selbst; die zwei Laternen bleiben.
    /// Gebacken wird gedreht (Front nach -Z) und auf Gehweg-Hoehe: Hof = 0,18 m.
    /// Teile: Rumpf (Paletten-Textur), Leuchtteile, Glas (leicht leuchtend), Kleinkram (Layer Detail, ohne Schatten).
    /// Hitboxen: "Roof" (Ladendach und Tankdach, Hydranten finden sie), "Solid" (Laden, Pfeiler, Zapfsaeulen, Rampen,
    /// Hindernisse). Grind-Linien aus den Kanten der Skate-Teile, Dachkanten, Parkstoppern und dem Tankdach-Rand.
    /// Menue: DriftSkate → Tankstelle.
    /// </summary>
    public static class GasStationAssets
    {
        const string Name = "Tankstelle";
        const string Root = "Assets/_Game/Tankstelle";
        const string SourceGlb = "Art/Environment/gas_station_skate.glb";
        const string GlbPath = Root + "/Source/gas_station_skate.glb";
        static string PrefabPath => "Assets/_Game/Resources/" + GasStationSite.PrefabPath + ".prefab";
        const float SidewalkTop = 0.18f;
        /// <summary>Modell-Front (+Z, Gehweg) zeigt in der Stadt nach Sueden zum Herz-Platz.</summary>
        const float Yaw = 180f;
        static string MatPath(string part) => Root + "/Materials/" + Name + "_" + part + ".mat";
        static string MeshPath(string part) => Root + "/Meshes/" + Name + "_" + part + ".asset";
        static string TexPath(string part) => Root + "/Textures/" + Name + "_" + part + ".png";

        /// <summary>Knotennamen ohne Nummer. Strasse und Gehweg hat die Stadt, ein Deko-Hydrant waere mit den echten verwechselbar.</summary>
        static readonly string[] Skip = { "Sidewalk", "Curb", "StreetDash", "Hydrant", "HydrantNozzle" };
        /// <summary>Standen im Modell auf dessen Gehweg (0,15 m), in der Stadt ist der Gehweg so hoch wie der Hof.</summary>
        static readonly string[] OnSidewalk = { "NewsBox", "NewsBoxWindow", "LampPole", "LampArm", "LampHead", "LampGlow" };
        static readonly string[] Roof = { "Roof", "CanopyTop" };
        static readonly string[] Solid =
        {
            "Plinth", "ShopBlock", "Parapet", "Fascia", "ACUnit", "VentPipe",
            "CanopyFascia", "CanopySoffit", "Pillar", "PillarBase",
            "Island", "PumpBase", "PumpBody", "PumpHead", "PumpTop", "Bollard", "TrashBin",
            "PylonBase", "PylonPole", "PylonBox", "PylonHeader", "PylonCap",
            "QuarterPipe", "QPDeckPlate", "Funbox", "FunboxLedge", "Kicker", "ManualPad", "GrindCurb", "StairPlatform", "Step", "Hubba",
            "Tire", "Drum", "Dumpster", "DumpsterLid", "Freezer", "VendingMachine", "Planter", "AirStation", "TrashCan", "NewsBox", "PalmTrunk", "LampPole",
        };
        /// <summary>Grindbare Kanten (Knoten ohne Nummer) und ihr Name in der Combo-Anzeige.</summary>
        static readonly Dictionary<string, string> Grind = new Dictionary<string, string>
        {
            { "QPCoping", "COPING" }, { "QPRail", "TANKE RAIL" }, { "FunboxCoping", "FUNBOX" }, { "LedgeAngle", "FUNBOX LEDGE" },
            { "DownRail", "DOWN RAIL" }, { "FlatBar", "FLATBAR" }, { "CurbAngle", "CURB" }, { "PadAngle", "MANUAL PAD" },
            { "HubbaCoping", "HUBBA" }, { "Handrail", "HANDRAIL" }, { "PlatformRail", "TANKE RAIL" },
            { "Parapet", "DACHKANTE" }, { "ParkStop", "PARKSTOPPER" },
        };
        /// <summary>Kleinteile bis zu dieser Groesse kommen auf den Detail-Layer (ab 120 m ausgeblendet, ohne Schatten).</summary>
        const float SmallSize = 0.9f;

        [MenuItem("DriftSkate/Tankstelle/Prefab bauen")] public static void Build() => BuildPrefab();
        [MenuItem("DriftSkate/Tankstelle/In die Stadt setzen (statt grossem Haus Block_43)")] public static void Place() => PlaceInCity();
        [MenuItem("DriftSkate/Tankstelle/Kontrollbilder rendern")] public static void Render() => RenderCheck();

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
                // Gehweghoehe kommt erst ausserhalb der Skalierung dazu (sonst laege der Hof hoeher als der Gehweg)
                Matrix4x4 frame = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, Yaw, 0f), Vector3.one) * inst.transform.worldToLocalMatrix;
                var palette = new List<Color>();
                int ColorIndex(Color c)
                {
                    int i = palette.FindIndex(x => Mathf.Abs(x.r - c.r) + Mathf.Abs(x.g - c.g) + Mathf.Abs(x.b - c.b) < 0.01f);
                    if (i >= 0) return i;
                    palette.Add(c);
                    return palette.Count - 1;
                }

                var parts = new Dictionary<string, GlbBake.MeshBuilder>();
                GlbBake.MeshBuilder Part(string key) => parts.TryGetValue(key, out var b) ? b : parts[key] = new GlbBake.MeshBuilder();
                var roof = new GlbBake.MeshBuilder();
                var solid = new GlbBake.MeshBuilder();
                var lines = new List<(string label, Vector3[] pts)>();
                int skipped = 0;

                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    string node = GlbBake.StripIndex(mf.name);
                    if (Skip.Contains(node)) { skipped++; continue; }
                    Matrix4x4 m = frame * mf.transform.localToWorldMatrix;
                    if (OnSidewalk.Contains(node)) m = Matrix4x4.Translate(Vector3.down * 0.15f) * m;

                    var verts = mf.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray();
                    var b = new Bounds(verts[0], Vector3.zero);
                    foreach (var v in verts) b.Encapsulate(v);
                    bool small = Mathf.Max(b.size.x, b.size.y, b.size.z) < SmallSize && !Solid.Contains(node);

                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        string mat = GlbBake.CleanName(mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)].name);
                        if (!colors.TryGetValue(mat, out var col)) throw new Exception("Unbekanntes Material " + mat);
                        string key = mat == "Glow" ? "Glow" : mat == "Glass" ? "Glass" : small ? "Details" : "Body";
                        Part(key).Add(mf.sharedMesh, sub, GlbBake.PaletteUv(ColorIndex(col), 8), m);
                        if (Roof.Contains(node)) roof.Add(mf.sharedMesh, sub, Vector2.zero, m);
                        else if (Solid.Contains(node)) solid.Add(mf.sharedMesh, sub, Vector2.zero, m);
                    }

                    if (Grind.TryGetValue(node, out var label) && !(node == "QPRail" && b.max.y < 2.6f))
                        lines.Add((label, TopLine(verts)));
                    if (node == "CanopyTop")
                    {
                        // Rand des Tankdachs: vier Kanten, an den Ecken verbunden
                        float y = b.max.y + 0.02f;
                        var c = new[] { new Vector3(b.min.x, y, b.min.z), new Vector3(b.max.x, y, b.min.z), new Vector3(b.max.x, y, b.max.z), new Vector3(b.min.x, y, b.max.z) };
                        for (int i = 0; i < 4; i++) lines.Add(("TANKDACH", new[] { c[i], c[(i + 1) % 4] }));
                    }
                }

                var paletteTex = GlbBake.Palette(TexPath("Palette"), palette.ToArray(), 8);
                var mats = new Dictionary<string, Material>
                {
                    { "Body", GlbBake.ToonMat(MatPath("Body"), Color.white, paletteTex, 0.25f, rim: 0.15f) },
                    { "Details", GlbBake.ToonMat(MatPath("Details"), Color.white, paletteTex, 0f, rim: 0.15f) },
                    { "Glow", GlbBake.ToonMat(MatPath("Glow"), Color.white, paletteTex, 0f, 1.6f, rim: 0f) },
                    { "Glass", GlbBake.ToonMat(MatPath("Glass"), Color.white, paletteTex, 0f, 0.6f, rim: 0.2f) },
                };

                var root = new GameObject(Name);
                try
                {
                    // Alles unter "Scaled": genau einmal vergroessert (GasStationSite.Scale), auf Gehweghoehe gesetzt
                    var scaled = new GameObject("Scaled").transform;
                    scaled.SetParent(root.transform, false);
                    scaled.localPosition = Vector3.up * SidewalkTop;
                    scaled.localScale = Vector3.one * GasStationSite.Scale;
                    var visual = new GameObject("Visual").transform;
                    visual.SetParent(scaled, false);
                    int detail = LayerMask.NameToLayer("Detail");
                    foreach (var kv in parts)
                    {
                        if (kv.Value.Empty) continue;
                        var mesh = GlbBake.SaveMesh(kv.Value.ToMesh(Name + "_" + kv.Key), MeshPath(kv.Key));
                        GlbBake.Part(visual, kv.Key, mesh, mats[kv.Key], kv.Key == "Body", kv.Key == "Details" ? detail : -1);
                    }
                    void Collider(string part, GlbBake.MeshBuilder b)
                    {
                        if (b.Empty) return;
                        var go = new GameObject(part);
                        go.transform.SetParent(scaled, false);
                        go.AddComponent<MeshCollider>().sharedMesh = GlbBake.SaveMesh(b.ToMesh(Name + "_" + part + "Collider"), MeshPath(part + "Collider"));
                    }
                    Collider("Roof", roof);
                    Collider("Solid", solid);

                    var rails = new GameObject("Rails").transform;
                    rails.SetParent(scaled, false);
                    int railLayer = LayerMask.NameToLayer("Rail");
                    for (int i = 0; i < lines.Count; i++)
                    {
                        var go = new GameObject("Grind_" + i + "_" + lines[i].label.Replace(' ', '_'));
                        go.transform.SetParent(rails, false);
                        if (railLayer >= 0) go.layer = railLayer;
                        var r = go.AddComponent<GrindRail>();
                        r.label = lines[i].label;
                        r.points = lines[i].pts;
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    Debug.Log($"[Tankstelle] Prefab gebaut: {PrefabPath} ({palette.Count} Farben, Teile {string.Join(", ", parts.Keys)}, {lines.Count} Grind-Linien, {skipped} Teile weggelassen)");
                }
                finally { Object.DestroyImmediate(root); }
            }
            finally { Object.DestroyImmediate(inst); }
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Grind-Linie eines laenglichen Teils (Rohr, Winkeleisen, Bordstein, auch schraeg wie Handlauf und Hubba):
        /// entlang der laengeren waagerechten Achse, an beiden Enden die Oberkante, quer mittig.
        /// </summary>
        static Vector3[] TopLine(Vector3[] verts)
        {
            var b = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) b.Encapsulate(v);
            int axis = b.size.x >= b.size.z ? 0 : 2, cross = 2 - axis;
            const float endBand = 0.12f;
            Vector3 End(bool max)
            {
                float edge = max ? b.max[axis] : b.min[axis];
                float top = float.MinValue;
                foreach (var v in verts) if (Mathf.Abs(v[axis] - edge) < endBand) top = Mathf.Max(top, v.y);
                var p = Vector3.zero;
                p[axis] = edge;
                p[cross] = b.center[cross];
                p.y = top + 0.02f;
                return p;
            }
            return new[] { End(false), End(true) };
        }

        // ------------------------------------------------------------------ In die Stadt

        public static void PlaceInCity()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) BuildPrefab();
            var scene = EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var city = scene.GetRootGameObjects().FirstOrDefault(g => g.transform.Find(GasStationSite.BlockName) != null);
            if (city == null) throw new Exception("Stadt mit " + GasStationSite.BlockName + " nicht gefunden");
            MeshStore.PersistHook = (mesh, key) => GlbBake.SaveMesh(mesh, "Assets/_Game/Generated/Meshes/" + key + ".asset");
            try
            {
                var inst = GasStationSite.Apply(city.transform);
                if (inst == null) throw new Exception("Tankstelle konnte nicht gesetzt werden");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                var linked = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                linked.transform.SetParent(inst.transform.parent, false);
                linked.transform.SetPositionAndRotation(inst.transform.position, inst.transform.rotation);
                linked.name = inst.name;
                Object.DestroyImmediate(inst);
                foreach (var t in linked.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
            }
            finally { MeshStore.PersistHook = null; }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ Kontrolle

        public static void RenderCheck()
        {
            Directory.CreateDirectory("Logs/tankstelle");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var t = GameObject.Find(Name);
            if (t == null) throw new Exception("Tankstelle nicht in der Stadt");
            Vector3 c = t.transform.position;
            Quaternion rot = t.transform.rotation;
            // Punkte unten in Modell-Metern relativ zur Station (Front nach -Z): mit Drehung und Vergroesserung umrechnen
            Vector3 W(Vector3 local) => c + rot * (local * GasStationSite.Scale);
            Physics.SyncTransforms();

            // Boden und Daecher pruefen (Hof auf Gehweg-Hoehe, Ladendach, Tankdach)
            void Probe(string what, Vector3 local)
            {
                Vector3 from = W(local) + Vector3.up * 30f;
                bool hit = Physics.Raycast(from, Vector3.down, out var h, 40f, ~LayerMask.GetMask("Rail"), QueryTriggerInteraction.Ignore);
                Debug.Log($"[Tankstelle] {what}: " + (hit ? $"{h.collider.name} bei y {h.point.y:0.00}" : "nichts getroffen"));
            }
            Probe("Hof Einfahrt", new Vector3(0f, 0f, -9f));
            Probe("Hof Skate-Ecke", new Vector3(7f, 0f, -3f));
            Probe("Ladendach", new Vector3(-7f, 0f, 10f));
            Probe("Tankdach", new Vector3(-5f, 0f, -2f));
            Probe("Quarterpipe-Deck", new Vector3(8f, 0f, 9.6f));
            Debug.Log($"[Tankstelle] {t.GetComponentsInChildren<GrindRail>().Length} Grind-Linien");

            var cam = Camera.main;
            void Shot(Vector3 local, Vector3 look, float fov, string file)
            {
                cam.fieldOfView = fov;
                cam.transform.position = W(local);
                cam.transform.LookAt(W(look));
                SimTests.Capture(cam, "Logs/tankstelle/" + file + ".png");
            }
            Shot(new Vector3(-8f, 10f, -24f), new Vector3(0f, 2f, 2f), 60f, "front");
            Shot(new Vector3(-22f, 3f, -20f), new Vector3(0f, 2.5f, 0f), 60f, "front_low");
            Shot(new Vector3(30f, 9f, -18f), new Vector3(6f, 1f, 2f), 55f, "skate_corner");
            Shot(new Vector3(9f, 2.2f, -9f), new Vector3(8f, 1f, 6f), 65f, "skate_inside");
            Shot(new Vector3(-6f, 1.8f, -6f), new Vector3(-6f, 1.8f, 10f), 65f, "under_canopy");
            Shot(new Vector3(-18.5f, 2.4f, 2.5f), GasStationSite.TagLocal / GasStationSite.Scale, 60f, "west_wall_tag");
            Shot(new Vector3(0f, 60f, -50f), new Vector3(0f, 0f, 0f), 55f, "overview");
            Shot(new Vector3(-30f, 10f, -70f), new Vector3(0f, 3f, 0f), 50f, "from_street");
        }
    }
}
