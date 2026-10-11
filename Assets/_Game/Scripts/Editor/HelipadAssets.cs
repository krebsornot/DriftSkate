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
    /// Hubschrauber-Landeplatz aus Art/Props/rooftop_helipad.glb (Claude 3D Design, Set 24 x 24 m: Plattform mit Rampe,
    /// Hubschrauber, Schuppen mit Neon "SKY PORT", Klimageraete, Windsack, Ledge und Rail zum Skaten).
    /// Wenige Draw Calls: Bau ueber Paletten-Textur, kleine Deko auf Ebene "Detail", Leuchtteile (Glow_*) leuchtend.
    /// Hitboxen aus den Knoten-Praefixen wie beim Parkhaus: Drive_ (befahrbar), Wall_ (fest), Rail_ (fest und grindbar),
    /// Deco_ ohne Hitbox. Rotor_Main und Rotor_Tail werden eigene Teile, die sich per RotorSpin drehen.
    /// Das Modell wird so gedreht, dass die Rampe nach +Z zeigt. Menue: DriftSkate → Helipad.
    /// </summary>
    public static class HelipadAssets
    {
        const string Name = "Helipad";
        const string Root = "Assets/_Game/Helipad";
        const string SourceGlb = "Art/Props/rooftop_helipad.glb";
        const string GlbPath = Root + "/Source/rooftop_helipad.glb";
        static string PrefabPath => "Assets/_Game/Resources/" + HelipadSite.PrefabPath + ".prefab";
        static string MatPath(string part) => Root + "/Materials/" + Name + "_" + part + ".mat";
        static string MeshPath(string part) => Root + "/Meshes/" + Name + "_" + part + ".asset";
        static string TexPath(string part) => Root + "/Textures/" + Name + "_" + part + ".png";

        [MenuItem("DriftSkate/Helipad/Prefab bauen")] public static void Build() => BuildPrefab();
        [MenuItem("DriftSkate/Helipad/Aufs Dach setzen (Block_12)")] public static void Place() => PlaceInCity();
        [MenuItem("DriftSkate/Helipad/Kontrollbilder rendern")] public static void Render() => RenderCheck();

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
                        foreach (var p in new[] { "Drive", "Wall", "Rail", "Deco", "Rotor" })
                            if (n == p || n.StartsWith(p + "_")) return p;
                    }
                    return "Deco";
                }
                Transform RotorOf(Transform t)
                {
                    for (; t != null; t = t.parent)
                    {
                        string n = GlbBake.CleanName(t.name);
                        if (n == "Rotor_Main" || n == "Rotor_Tail") return t;
                    }
                    return null;
                }

                Matrix4x4 toRoot = inst.transform.worldToLocalMatrix;
                var body = new GlbBake.MeshBuilder();
                var markings = new GlbBake.MeshBuilder();   // Deck-Bemalung: ohne Umriss, ohne Schatten
                var detail = new GlbBake.MeshBuilder();     // kleine Deko, ab 120 m ausgeblendet
                var glow = new GlbBake.MeshBuilder();
                var drive = new GlbBake.MeshBuilder();
                var solid = new GlbBake.MeshBuilder();
                var rotors = new Dictionary<Transform, GlbBake.MeshBuilder>();
                var grind = new Dictionary<string, List<Vector3>>();
                Vector3 ramp = Vector3.zero;

                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    string node = GlbBake.CleanName(mf.name);
                    string prefix = Prefix(mf.transform);
                    var rotor = RotorOf(mf.transform);
                    Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                    if (node == "Drive_Ramp") ramp = toRoot.MultiplyPoint3x4(mr.bounds.center);
                    bool small = mr.bounds.size.magnitude < 4f && !node.StartsWith("Deco_Deck_") && !node.StartsWith("Deco_Heli_") && !node.StartsWith("Deco_Neon_");
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        string mat = GlbBake.CleanName(mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)].name);
                        if (!colors.TryGetValue(mat, out var col)) col = Color.white;
                        var uv = GlbBake.PaletteUv(ColorIndex(col), 8);
                        if (rotor != null)
                        {
                            if (!rotors.TryGetValue(rotor, out var rb)) rotors[rotor] = rb = new GlbBake.MeshBuilder();
                            rb.Add(mf.sharedMesh, sub, uv, rotor.worldToLocalMatrix * mf.transform.localToWorldMatrix);
                            continue;
                        }
                        if (mat.StartsWith("Glow")) glow.Add(mf.sharedMesh, sub, uv, m);
                        else if (node.StartsWith("Deco_Deck_")) markings.Add(mf.sharedMesh, sub, uv, m);
                        else if (prefix == "Deco" && small) detail.Add(mf.sharedMesh, sub, uv, m);
                        else body.Add(mf.sharedMesh, sub, uv, m);
                        if (prefix == "Drive") drive.Add(mf.sharedMesh, sub, Vector2.zero, m);
                        else if (prefix == "Wall" || prefix == "Rail") solid.Add(mf.sharedMesh, sub, Vector2.zero, m);
                    }
                    if (prefix == "Rail")
                    {
                        // gleiche Kante (z. B. Ledge vorn/hinten) bleibt getrennt, jede Rail ein eigener Linienzug
                        if (!grind.TryGetValue(node, out var pts)) grind[node] = pts = new List<Vector3>();
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
                    // Modell drehen: Rampe nach +Z
                    float yaw = Mathf.Atan2(ramp.x, ramp.z) * Mathf.Rad2Deg;
                    var model = new GameObject("Model").transform;
                    model.SetParent(root.transform, false);
                    model.localRotation = Quaternion.Euler(0f, -yaw, 0f);
                    model.localScale = Vector3.one * HelipadSite.Scale; // Skalierung genau hier, einmal

                    var bodyMat = GlbBake.ToonMat(MatPath("Body"), Color.white, paletteTex, 0.3f, rim: 0.15f);
                    int detailLayer = LayerMask.NameToLayer("Detail");
                    Transform Part(string part, GlbBake.MeshBuilder b, Material mat, bool shadows, int layer = -1, Transform parent = null)
                    {
                        if (b.Empty) return null;
                        return GlbBake.Part(parent ?? model, part, GlbBake.SaveMesh(b.ToMesh(Name + "_" + part), MeshPath(part)), mat, shadows, layer).transform;
                    }
                    Part("Body", body, bodyMat, true);
                    Part("Markings", markings, GlbBake.ToonMat(MatPath("Markings"), Color.white, paletteTex, 0f, rim: 0f), false);
                    Part("Detail", detail, GlbBake.ToonMat(MatPath("Detail"), Color.white, paletteTex, 0.15f, rim: 0.15f), false, detailLayer);
                    Part("Glow", glow, GlbBake.ToonMat(MatPath("Glow"), Color.white, paletteTex, 0f, 1.6f, rim: 0f), false);

                    // Rotoren: eigener Drehpunkt auf der Achse, drehen ruhig im Leerlauf
                    foreach (var kv in rotors)
                    {
                        string n = GlbBake.CleanName(kv.Key.name);
                        var pivot = new GameObject(n).transform;
                        pivot.SetParent(model, false);
                        pivot.localPosition = toRoot.MultiplyPoint3x4(kv.Key.position);
                        pivot.localRotation = Quaternion.Inverse(inst.transform.rotation) * kv.Key.rotation;
                        Part(n + "_Mesh", kv.Value, bodyMat, n == "Rotor_Main", -1, pivot);
                        var spin = pivot.gameObject.AddComponent<RotorSpin>();
                        bool main = n == "Rotor_Main";
                        // Hauptrotor dreht um seine Hochachse, Heckrotor um die Achse quer zum Heck (duennste Ausdehnung)
                        var tmp = kv.Value.ToMesh("tmp");
                        var size = tmp.bounds.size;
                        Object.DestroyImmediate(tmp);
                        spin.axis = main ? Vector3.up : (size.x <= size.y && size.x <= size.z ? Vector3.right : size.z <= size.y ? Vector3.forward : Vector3.up);
                        spin.period = main ? 1.6f : 0.6f;
                    }

                    void Collider(string part, GlbBake.MeshBuilder b)
                    {
                        if (b.Empty) return;
                        var go = new GameObject(part);
                        go.transform.SetParent(model, false);
                        go.AddComponent<MeshCollider>().sharedMesh = GlbBake.SaveMesh(b.ToMesh(Name + "_" + part + "Collider"), MeshPath(part + "Collider"));
                    }
                    Collider("Drive", drive);
                    Collider("Solid", solid);

                    var rails = new GameObject("Rails").transform;
                    rails.SetParent(model, false);
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
                            r.label = kv.Key.Contains("Ledge") ? "HELIPAD LEDGE" : "HELIPAD RAIL";
                            r.points = chain.ToArray();
                            count++;
                        }
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    Debug.Log($"[Helipad] Prefab gebaut: {PrefabPath} ({palette.Count} Farben, {count} Grind-Linien, {rotors.Count} Rotoren, Rampe bei {ramp} -> gedreht um {-yaw:0} Grad)");
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
            var city = scene.GetRootGameObjects().FirstOrDefault(g => g.transform.Find(HelipadSite.BlockName) != null);
            if (city == null) throw new Exception("Stadt mit " + HelipadSite.BlockName + " nicht gefunden");
            var inst = HelipadSite.Apply(city.transform);
            if (inst == null) throw new Exception("Helipad konnte nicht gesetzt werden");
            // als Prefab-Instanz, damit Aenderungen am Prefab automatisch ankommen
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var linked = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            linked.transform.SetParent(inst.transform.parent, false);
            linked.transform.SetPositionAndRotation(inst.transform.position, inst.transform.rotation);
            linked.name = inst.name;
            Object.DestroyImmediate(inst);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ Kontrolle

        public static void RenderCheck()
        {
            Directory.CreateDirectory("Logs/helipad");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var p = GameObject.Find(Name);
            if (p == null) throw new Exception("Helipad nicht in der Stadt");
            var t = p.transform;
            Physics.SyncTransforms();

            // Hoehenprofil: Dach -> Rampe -> Deck (Rampe zeigt nach +Z)
            int mask = ~LayerMask.GetMask("Car", "Skater", "Ignore Raycast", "Rail");
            var s = new List<string>();
            for (int i = 0; i <= 20; i++)
            {
                Vector3 q = t.TransformPoint(new Vector3(0f, 0f, 22f - i * 1.6f));
                s.Add(Physics.Raycast(q + Vector3.up * 6f, Vector3.down, out RaycastHit h, 10f, mask, QueryTriggerInteraction.Ignore)
                    ? (h.point.y - t.position.y).ToString("0.00") : "-");
            }
            Debug.Log("[Helipad] Profil Dach -> Rampe -> Deck (z 22 -> -10): " + string.Join(" ", s));
            Debug.Log("[Helipad] Grind-Linien: " + p.GetComponentsInChildren<GrindRail>().Length + ", Rotoren: " + p.GetComponentsInChildren<RotorSpin>().Length);

            var cam = Camera.main;
            void Shot(Vector3 local, Vector3 look, float fov, string file)
            {
                cam.fieldOfView = fov;
                cam.transform.position = t.TransformPoint(local);
                cam.transform.LookAt(t.TransformPoint(look));
                SimTests.Capture(cam, "Logs/helipad/" + file + ".png");
            }
            Shot(new Vector3(30f, 22f, 40f), new Vector3(0f, 2f, 0f), 55f, "overview");
            Shot(new Vector3(0f, 2f, 28f), new Vector3(0f, 3f, 0f), 60f, "ramp");
            Shot(new Vector3(-9f, 4.5f, 9f), new Vector3(3f, 4.5f, -6f), 70f, "deck");
            Shot(new Vector3(-26f, 7f, -26f), new Vector3(0f, 3f, 0f), 60f, "back");
            Shot(new Vector3(0f, -12f, 90f), new Vector3(0f, 4f, 0f), 45f, "from_street");
        }
    }
}
