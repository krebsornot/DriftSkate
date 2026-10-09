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
    /// Baut das Schleppflugzeug aus Art/Planes/towplane_ds420.glb: Teile nach Funktion zusammengefasst
    /// (Rumpf mit Paletten-Textur, Propeller, Aufkleber, Banner-Segmente als Kette), Toon-Materialien,
    /// Prefab mit TowPlane-Komponente. Vorwaerts wird aus Propeller und Banner abgeleitet.
    /// </summary>
    public static class TowPlaneAssets
    {
        const string SourceGlb = "Art/Planes/towplane_ds420.glb";
        const string Root = "Assets/_Game/TowPlane";
        const string GlbPath = Root + "/Source/towplane_ds420.glb";
        const string PrefabPath = Root + "/TowPlane.prefab";
        /// <summary>Groesse im Spiel: das Original ist gut 8 m lang, in 100 m Hoehe wirkt es sonst winzig.</summary>
        const float Scale = 2.4f;

        static string MatPath(string n) => Root + "/Materials/" + n + ".mat";
        static string MeshPath(string n) => Root + "/Meshes/" + n + ".asset";

        [MenuItem("DriftSkate/Schleppflugzeug/Prefab bauen")]
        public static void Build()
        {
            GlbBake.EnsureFolders(Root + "/Source", Root + "/Meshes", Root + "/Materials", Root + "/Textures");
            if (!File.Exists(GlbPath))
            {
                if (!File.Exists(SourceGlb)) throw new Exception("Flugzeug fehlt: " + SourceGlb);
                File.Copy(SourceGlb, GlbPath, true);
            }
            AssetDatabase.ImportAsset(GlbPath, ImportAssetOptions.ForceUpdate);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
            if (src == null) throw new Exception("GLB konnte nicht importiert werden: " + GlbPath);

            var colors = GlbBake.ReadColors(GlbPath, untintedAsWhite: false); // Aufkleber und Stoff haben Texturen
            var names = colors.Keys.ToList();

            var inst = Object.Instantiate(src);
            var frame = new GameObject("Frame").transform;
            try
            {
                var all = inst.GetComponentsInChildren<Transform>(true);
                Transform Find(string n) => all.FirstOrDefault(t => GlbBake.CleanName(t.name) == n);
                var propNode = Find("Propeller");
                var bannerNode = Find("Banner");
                var hook = Find("Tow_Hook");
                if (propNode == null || bannerNode == null || hook == null) throw new Exception("Propeller, Banner oder Tow_Hook fehlt im Modell");

                // Vorwaerts: vom Haken (hinten) zum Propeller (vorn); Wurzel des Modells = Mitte unter dem Rumpf
                Vector3 fwd = Vector3.ProjectOnPlane(propNode.position - hook.position, Vector3.up).normalized;
                // Masse ohne Skalierung, skaliert wird ueber das Visual-Objekt im Prefab
                frame.SetPositionAndRotation(inst.transform.position, Quaternion.LookRotation(fwd, Vector3.up));
                Matrix4x4 toFrame = frame.worldToLocalMatrix;

                var body = new GlbBake.MeshBuilder();
                var prop = new GlbBake.MeshBuilder();
                var decals = new Dictionary<string, (GlbBake.MeshBuilder b, Texture tex)>();
                var segments = new List<(Transform node, MeshFilter mf, Texture tex)>();
                Vector3 propPivot = toFrame.MultiplyPoint3x4(propNode.position);
                Matrix4x4 propToLocal = Matrix4x4.Translate(-propPivot);

                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    Matrix4x4 m = toFrame * mf.transform.localToWorldMatrix;
                    bool isBannerSeg = GlbBake.CleanName(mf.name).StartsWith("Banner_0");
                    bool inProp = mf.transform.IsChildOf(propNode);
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        var um = mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)];
                        string mat = GlbBake.CleanName(um.name);
                        if (isBannerSeg) { segments.Add((mf.transform, mf, GlbBake.FirstTexture(um))); break; }
                        if (mat.StartsWith("Decal"))
                        {
                            if (!decals.TryGetValue(mat, out var d)) decals[mat] = d = (new GlbBake.MeshBuilder(), GlbBake.FirstTexture(um));
                            d.b.Add(mf.sharedMesh, sub, Vector2.zero, m, keepUv: true);
                            continue;
                        }
                        if (!colors.ContainsKey(mat)) throw new Exception("Unbekanntes Material " + mat);
                        Vector2 uv = GlbBake.PaletteUv(names.IndexOf(mat));
                        if (inProp) prop.Add(mf.sharedMesh, sub, uv, propToLocal * m);
                        else body.Add(mf.sharedMesh, sub, uv, m);
                    }
                }
                if (segments.Count == 0) throw new Exception("Keine Banner-Segmente gefunden");

                // Banner-Segmente: von vorn nach hinten sortieren, Pivot an der Vorderkante, Kette
                var segData = segments.Select(s =>
                {
                    Matrix4x4 m = toFrame * s.mf.transform.localToWorldMatrix;
                    var b = new Bounds(m.MultiplyPoint3x4(s.mf.sharedMesh.vertices[0]), Vector3.zero);
                    foreach (var v in s.mf.sharedMesh.vertices) b.Encapsulate(m.MultiplyPoint3x4(v));
                    return (s.mf, s.tex, bounds: b, m);
                }).OrderByDescending(s => s.bounds.center.z).ToList();

                var palette = GlbBake.Palette(Root + "/Textures/TowPlane_Palette.png", names.Select(n => colors[n]).ToArray());
                var bodyMat = GlbBake.ToonMat(MatPath("TowPlane_Body"), Color.white, palette, 0.3f, rim: 0.15f);
                var propMat = GlbBake.ToonMat(MatPath("TowPlane_Prop"), Color.white, palette, 0f, rim: 0.15f);

                var root = new GameObject("TowPlane");
                try
                {
                    var plane = root.AddComponent<TowPlane>();

                    Renderer Part(string name, Mesh mesh, Material mat, Transform parent, bool shadows) =>
                        GlbBake.Part(parent, name, mesh, mat, shadows);

                    // Alles im fertigen Flugzeug um Scale groesser
                    var visual = new GameObject("Visual").transform;
                    visual.SetParent(root.transform, false);
                    visual.localScale = Vector3.one * Scale;

                    Part("Body", GlbBake.SaveMesh(body.ToMesh("TowPlane_Body"), MeshPath("TowPlane_Body")), bodyMat, visual, true);

                    var propPivotGo = new GameObject("Propeller").transform;
                    propPivotGo.SetParent(visual, false);
                    propPivotGo.localPosition = propPivot;
                    Part("PropMesh", GlbBake.SaveMesh(prop.ToMesh("TowPlane_Prop"), MeshPath("TowPlane_Prop")), propMat, propPivotGo, false);
                    plane.propeller = propPivotGo;

                    foreach (var kv in decals)
                    {
                        var dm = GlbBake.ToonMat(MatPath("TowPlane_" + kv.Key), Color.white, kv.Value.tex, 0f, cutoff: 0.5f, rim: 0.15f);
                        Part(kv.Key, GlbBake.SaveMesh(kv.Value.b.ToMesh("TowPlane_" + kv.Key), MeshPath("TowPlane_" + kv.Key)), dm, visual, false);
                    }

                    var clothTex = segData[0].tex;
                    var clothMat = GlbBake.ToonMat(MatPath("TowPlane_Cloth"), Color.white, clothTex, 0f, doubleSided: true, rim: 0.15f);
                    Transform prev = visual;
                    Vector3 prevPivot = Vector3.zero;
                    var chain = new List<Transform>();
                    for (int i = 0; i < segData.Count; i++)
                    {
                        var s = segData[i];
                        Vector3 pivot = new Vector3(s.bounds.center.x, s.bounds.center.y, s.bounds.max.z);
                        var go = new GameObject("Banner" + (i + 1)).transform;
                        go.SetParent(prev, false);
                        go.localPosition = pivot - prevPivot;
                        var mb = new GlbBake.MeshBuilder();
                        for (int sub = 0; sub < s.mf.sharedMesh.subMeshCount; sub++)
                            mb.Add(s.mf.sharedMesh, sub, Vector2.zero, Matrix4x4.Translate(-pivot) * s.m, keepUv: true);
                        Part("Cloth", GlbBake.SaveMesh(mb.ToMesh("TowPlane_Banner" + (i + 1)), MeshPath("TowPlane_Banner" + (i + 1))), clothMat, go, false);
                        chain.Add(go);
                        prev = go;
                        prevPivot = pivot;
                    }
                    plane.banner = chain.ToArray();

                    foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    Debug.Log($"[Schleppflugzeug] Prefab gebaut: {PrefabPath} ({segData.Count} Banner-Segmente, {decals.Count} Aufkleber)");
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

        [MenuItem("DriftSkate/Schleppflugzeug/In die Stadt setzen")]
        public static void PlaceInCity()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Build(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); }
            var scene = EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var plane = Object.FindAnyObjectByType<TowPlane>(FindObjectsInactive.Include);
            if (plane == null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.name = "TowPlane";
                plane = go.GetComponent<TowPlane>();
            }
            plane.transform.position = plane.PointAt(plane.startOffset);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Schleppflugzeug] In der Stadt, Route {plane.RouteLength:0} m, Runde {plane.RouteLength / plane.speed:0} s");
        }

        /// <summary>Batch: Prefab bauen, in die Stadt setzen, Kontrollbilder rendern.</summary>
        public static void BatchAll()
        {
            Build();
            PlaceInCity();
            RenderCheck();
        }

        [MenuItem("DriftSkate/Schleppflugzeug/Kontrollbilder rendern")]
        public static void RenderCheck()
        {
            Directory.CreateDirectory("Logs/towplane");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var plane = Object.FindAnyObjectByType<TowPlane>();
            if (plane == null) throw new Exception("Kein Schleppflugzeug in der Stadt");
            var cam = Camera.main;
            float total = plane.RouteLength;

            plane.SetRoutePosition(total * 0.12f);
            var t = plane.transform;
            var shots = new (string name, Vector3 local, Vector3 look, float fov)[]
            {
                ("side_left",  new Vector3(-30f, 3f, -8f),  new Vector3(0f, 0f, -12f), 55f),
                ("front_34",   new Vector3(-16f, 5f, 24f),  new Vector3(0f, 0f, 0f),   50f),
                ("rear_34",    new Vector3(14f, 9f, -26f),  new Vector3(0f, -1f, -22f), 55f),
                ("banner",     new Vector3(-42f, 0f, -55f), new Vector3(0f, -5f, -52f), 55f),
                ("banner_end", new Vector3(-30f, 2f, -105f), new Vector3(0f, -5f, -66f), 55f),
                ("top",        new Vector3(0f, 40f, -10f),  new Vector3(0f, 0f, -14f),  55f),
            };
            foreach (var s in shots)
            {
                cam.fieldOfView = s.fov;
                cam.transform.position = t.TransformPoint(s.local);
                cam.transform.LookAt(t.TransformPoint(s.look));
                SimTests.Capture(cam, "Logs/towplane/plane_" + s.name + ".png");
            }

            // Vom Boden aus: am Stadtrand und aus der Stadtmitte
            float B(int i) => CityBuilder.BlockCenter(i);
            float R(int k) => CityBuilder.RoadCenter(k);
            var ground = new (string name, float at, Vector3 pos)[]
            {
                ("center_a", 0.00f, new Vector3(R(2), 1.8f, B(2))),
                ("center_b", 0.25f, new Vector3(R(2), 1.8f, B(2))),
                ("center_c", 0.50f, new Vector3(R(2), 1.8f, B(2))),
                ("edge",     0.25f, new Vector3(R(4), 1.8f, B(2))),
            };
            foreach (var g in ground)
            {
                plane.SetRoutePosition(total * g.at);
                cam.fieldOfView = 60f;
                cam.transform.position = g.pos;
                cam.transform.LookAt(plane.transform.position + Vector3.down * 6f);
                SimTests.Capture(cam, "Logs/towplane/plane_ground_" + g.name + ".png");
            }
        }
    }
}
