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
    /// Dach-Sets (World/RoofSets.cs) aus GLBs von Claude 3D Design backen: Helipad und Rooftop-Bar.
    /// Wenige Draw Calls: Bau ueber Paletten-Textur, kleine Deko auf Ebene "Detail", Leuchtteile (Material Glow_*) leuchtend,
    /// flache Aufkleber (Fugen, Markierungen) ohne Umriss und 1,5 cm angehoben (kein Flimmern auf der Flaeche darunter).
    /// Hitboxen aus den Knoten-Praefixen wie beim Parkhaus: Drive_ (befahrbar), Wall_ (fest), Rail_ (fest und grindbar),
    /// Deco_ ohne Hitbox. Rotor_* werden eigene Teile, die sich per RotorSpin drehen; Spot_* bleiben leere Punkte
    /// (Stellplaetze fuer NPCs). Menue: DriftSkate → Dach-Sets.
    /// </summary>
    public static class RoofSetAssets
    {
        [MenuItem("DriftSkate/Dach-Sets/Helipad bauen und setzen")] public static void BatchHelipad() => Batch(RoofSets.Helipad);
        [MenuItem("DriftSkate/Dach-Sets/Rooftop-Bar bauen und setzen")] public static void BatchBar() => Batch(RoofSets.Bar);

        public static void BatchAll()
        {
            foreach (var d in RoofSets.All) Batch(d);
        }

        static void Batch(RoofSets.Def def)
        {
            BuildPrefab(def);
            PlaceInCity(def);
            RenderCheck(def);
        }

        static string PrefabPath(RoofSets.Def d) => "Assets/_Game/Resources/" + d.prefabPath + ".prefab";
        static string MatPath(RoofSets.Def d, string part) => d.assetRoot + "/Materials/" + d.name + "_" + part + ".mat";
        static string MeshPath(RoofSets.Def d, string part) => d.assetRoot + "/Meshes/" + d.name + "_" + part + ".asset";
        static string TexPath(RoofSets.Def d, string part) => d.assetRoot + "/Textures/" + d.name + "_" + part + ".png";

        // ------------------------------------------------------------------ Bauen

        public static void BuildPrefab(RoofSets.Def def)
        {
            string root = def.assetRoot;
            string glbPath = root + "/Source/" + Path.GetFileName(def.glb);
            foreach (var d in new[] { "/Meshes", "/Materials", "/Textures" }) AssetDatabase.DeleteAsset(root + d);
            GlbBake.EnsureFolders(root + "/Source", root + "/Meshes", root + "/Materials", root + "/Textures",
                                  Path.GetDirectoryName(PrefabPath(def)).Replace('\\', '/'));
            if (!File.Exists(def.glb)) throw new Exception("Modell fehlt: " + def.glb);
            File.Copy(def.glb, glbPath, true);
            AssetDatabase.ImportAsset(glbPath, ImportAssetOptions.ForceUpdate);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (src == null) throw new Exception("GLB konnte nicht importiert werden: " + glbPath);
            var colors = GlbBake.ReadColors(glbPath);

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
                    for (; t != null && t != inst.transform; t = t.parent)
                        if (GlbBake.CleanName(t.name).StartsWith("Rotor_") && t.GetComponent<MeshFilter>() == null) return t;
                    return null;
                }

                Matrix4x4 toRoot = inst.transform.worldToLocalMatrix;
                var body = new GlbBake.MeshBuilder();
                var flat = new GlbBake.MeshBuilder();       // Aufkleber: Markierungen, Fugen (ohne Umriss, ohne Schatten)
                var detail = new GlbBake.MeshBuilder();     // kleine Deko, ab 120 m ausgeblendet
                var glow = new GlbBake.MeshBuilder();
                var drive = new GlbBake.MeshBuilder();
                var solid = new GlbBake.MeshBuilder();
                var rotors = new Dictionary<Transform, GlbBake.MeshBuilder>();
                var grind = new Dictionary<string, List<Vector3>>();
                Vector3? front = null;
                Matrix4x4 lift = Matrix4x4.Translate(Vector3.up * 0.015f);

                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    string node = GlbBake.CleanName(mf.name);
                    string prefix = Prefix(mf.transform);
                    var rotor = RotorOf(mf.transform);
                    Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                    if (node == def.frontNode) front = toRoot.MultiplyPoint3x4(mr.bounds.center);
                    var size = mr.bounds.size;
                    // flach (unter 1 cm hoch) und gross: Aufkleber auf einer Flaeche
                    bool decal = prefix == "Deco" && size.y < 0.012f && size.x * size.z > 1f;
                    bool small = size.magnitude < 4f && !node.StartsWith("Deco_Deck_") && !node.StartsWith("Deco_Heli_") && !node.StartsWith("Deco_Sign_") && !node.StartsWith("Deco_Neon_");
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
                        else if (decal) flat.Add(mf.sharedMesh, sub, uv, lift * m);
                        else if (node.StartsWith("Deco_Deck_")) flat.Add(mf.sharedMesh, sub, uv, m);
                        else if (prefix == "Deco" && small) detail.Add(mf.sharedMesh, sub, uv, m);
                        else body.Add(mf.sharedMesh, sub, uv, m);
                        if (prefix == "Drive") drive.Add(mf.sharedMesh, sub, Vector2.zero, m);
                        else if (prefix == "Wall" || prefix == "Rail") solid.Add(mf.sharedMesh, sub, Vector2.zero, m);
                    }
                    if (prefix == "Rail")
                    {
                        // jede Rail ein eigener Linienzug (z. B. Kanten links und rechts an einer Ledge)
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

                var paletteTex = GlbBake.Palette(TexPath(def, "Palette"), palette.ToArray(), 8);
                var prefabRoot = new GameObject(def.name);
                try
                {
                    // Modell drehen (frontNode nach +Z) und genau hier einmal skalieren
                    float yaw = front.HasValue ? Mathf.Atan2(front.Value.x, front.Value.z) * Mathf.Rad2Deg : 0f;
                    var model = new GameObject("Model").transform;
                    model.SetParent(prefabRoot.transform, false);
                    model.localRotation = Quaternion.Euler(0f, -yaw, 0f);
                    model.localScale = Vector3.one * def.scale;

                    var bodyMat = GlbBake.ToonMat(MatPath(def, "Body"), Color.white, paletteTex, 0.3f, rim: 0.15f);
                    int detailLayer = LayerMask.NameToLayer("Detail");
                    Transform Part(string part, GlbBake.MeshBuilder b, Material mat, bool shadows, int layer = -1, Transform parent = null)
                    {
                        if (b.Empty) return null;
                        return GlbBake.Part(parent ?? model, part, GlbBake.SaveMesh(b.ToMesh(def.name + "_" + part), MeshPath(def, part)), mat, shadows, layer).transform;
                    }
                    Part("Body", body, bodyMat, true);
                    Part("Markings", flat, GlbBake.ToonMat(MatPath(def, "Markings"), Color.white, paletteTex, 0f, rim: 0f), false);
                    Part("Detail", detail, GlbBake.ToonMat(MatPath(def, "Detail"), Color.white, paletteTex, 0.15f, rim: 0.15f), false, detailLayer);
                    Part("Glow", glow, GlbBake.ToonMat(MatPath(def, "Glow"), Color.white, paletteTex, 0f, 1.6f, rim: 0f), false);

                    // Rotoren: eigener Drehpunkt auf der Achse, drehen ruhig im Leerlauf
                    foreach (var kv in rotors)
                    {
                        string n = GlbBake.CleanName(kv.Key.name);
                        var pivot = new GameObject(n).transform;
                        pivot.SetParent(model, false);
                        pivot.localPosition = toRoot.MultiplyPoint3x4(kv.Key.position);
                        pivot.localRotation = Quaternion.Inverse(inst.transform.rotation) * kv.Key.rotation;
                        bool main = n == "Rotor_Main";
                        Part(n + "_Mesh", kv.Value, bodyMat, main, -1, pivot);
                        var spin = pivot.gameObject.AddComponent<RotorSpin>();
                        // Hauptrotor um die Hochachse, sonst um die duennste Ausdehnung (Heckrotor quer zum Heck)
                        var tmp = kv.Value.ToMesh("tmp");
                        var size = tmp.bounds.size;
                        Object.DestroyImmediate(tmp);
                        spin.axis = main ? Vector3.up : (size.x <= size.y && size.x <= size.z ? Vector3.right : size.z <= size.y ? Vector3.forward : Vector3.up);
                        spin.period = main ? 1.6f : 0.6f;
                    }

                    // Stellplaetze fuer NPCs (leere Punkte)
                    int spots = 0;
                    foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                    {
                        string n = GlbBake.CleanName(t.name);
                        if (!n.StartsWith("Spot_")) continue;
                        var s = new GameObject(n).transform;
                        s.SetParent(model, false);
                        s.localPosition = toRoot.MultiplyPoint3x4(t.position);
                        s.localRotation = Quaternion.Inverse(inst.transform.rotation) * t.rotation;
                        spots++;
                    }

                    void Collider(string part, GlbBake.MeshBuilder b)
                    {
                        if (b.Empty) return;
                        var go = new GameObject(part);
                        go.transform.SetParent(model, false);
                        go.AddComponent<MeshCollider>().sharedMesh = GlbBake.SaveMesh(b.ToMesh(def.name + "_" + part + "Collider"), MeshPath(def, part + "Collider"));
                    }
                    Collider("Drive", drive);
                    Collider("Solid", solid);

                    var rails = new GameObject("Rails").transform;
                    rails.SetParent(model, false);
                    int railLayer = LayerMask.NameToLayer("Rail");
                    int count = 0;
                    string label = def.name.ToUpperInvariant();
                    foreach (var kv in grind)
                    {
                        int i = 0;
                        foreach (var chain in ParkhausAssets.Chains(kv.Value))
                        {
                            var go = new GameObject("Grind_" + kv.Key + "_" + i++);
                            go.transform.SetParent(rails, false);
                            if (railLayer >= 0) go.layer = railLayer;
                            var r = go.AddComponent<GrindRail>();
                            r.label = label + (kv.Key.Contains("Ledge") || kv.Key.Contains("Edge") ? " LEDGE" : kv.Key.Contains("Coping") ? " COPING" : " RAIL");
                            r.points = chain.ToArray();
                            count++;
                        }
                    }

                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath(def));
                    Debug.Log($"[{def.name}] Prefab gebaut: {PrefabPath(def)} ({palette.Count} Farben, {count} Grind-Linien, {rotors.Count} Rotoren, {spots} Stellplaetze, gedreht um {-yaw:0} Grad, Skalierung {def.scale})");
                }
                finally { Object.DestroyImmediate(prefabRoot); }
            }
            finally { Object.DestroyImmediate(inst); }
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ In die Stadt

        public static void PlaceInCity(RoofSets.Def def)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(def)) == null) BuildPrefab(def);
            var scene = EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var city = scene.GetRootGameObjects().FirstOrDefault(g => g.transform.Find(def.blockName) != null);
            if (city == null) throw new Exception("Stadt mit " + def.blockName + " nicht gefunden");
            var inst = RoofSets.Apply(city.transform, def);
            if (inst == null) throw new Exception(def.name + " konnte nicht gesetzt werden");
            // als Prefab-Instanz, damit Aenderungen am Prefab automatisch ankommen
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(def));
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

        public static void RenderCheck(RoofSets.Def def)
        {
            string dir = "Logs/" + def.name.ToLowerInvariant();
            Directory.CreateDirectory(dir);
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var p = GameObject.Find(def.name);
            if (p == null) throw new Exception(def.name + " nicht in der Stadt");
            var t = p.transform;
            Physics.SyncTransforms();
            float h = def.half;

            // Hoehenprofil von vorn (+Z) quer durchs Set
            int mask = ~LayerMask.GetMask("Car", "Skater", "Ignore Raycast", "Rail");
            var s = new List<string>();
            for (int i = 0; i <= 20; i++)
            {
                Vector3 q = t.TransformPoint(new Vector3(0f, 0f, (h + 3f) - i * (2f * h + 3f) / 20f));
                s.Add(Physics.Raycast(q + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 14f, mask, QueryTriggerInteraction.Ignore)
                    ? (hit.point.y - t.position.y).ToString("0.00") : "-");
            }
            Debug.Log($"[{def.name}] Profil von vorn nach hinten: " + string.Join(" ", s));
            Debug.Log($"[{def.name}] Grind-Linien: {p.GetComponentsInChildren<GrindRail>().Length}, Rotoren: {p.GetComponentsInChildren<RotorSpin>().Length}");

            var cam = Camera.main;
            void Shot(Vector3 local, Vector3 look, float fov, string file)
            {
                cam.fieldOfView = fov;
                cam.transform.position = t.TransformPoint(local);
                cam.transform.LookAt(t.TransformPoint(look));
                SimTests.Capture(cam, dir + "/" + file + ".png");
            }
            Shot(new Vector3(h * 1.5f, h * 1.1f, h * 2f), new Vector3(0f, 2f, 0f), 55f, "overview");
            Shot(new Vector3(0f, 2f, h + 9f), new Vector3(0f, 3f, 0f), 60f, "front");
            Shot(new Vector3(-h * 0.5f, 4.5f, h * 0.5f), new Vector3(h * 0.2f, 3f, -h * 0.4f), 70f, "inside");
            Shot(new Vector3(-h * 1.4f, 7f, -h * 1.4f), new Vector3(0f, 3f, 0f), 60f, "back");
            Shot(new Vector3(0f, -t.position.y + 2f, h + 60f), new Vector3(0f, 4f, 0f), 45f, "from_street");
        }
    }
}
