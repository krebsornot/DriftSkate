using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// NPC-Auto aus Fynns toon-city-car.glb (glTFast-Import): alle Teile zu wenigen Meshes zusammengefasst, Farben
    /// ueber eine kleine Paletten-Textur (ein Material fuer das ganze Auto), Raeder als drehbare Pivots, Lichter
    /// getrennt (Bremslicht, Blinker). Sechs Lackfarben als eigene Prefabs. Dazu der Verkehr in der Stadt.
    /// </summary>
    public static class TrafficAssets
    {
        const string Root = "Assets/_Game/Traffic";
        const string Glb = Root + "/Source/toon-city-car.glb";

        // Farben aus der GLB (baseColorFactor, linear) fuer die Paletten-Textur
        static readonly (string mat, Color linear)[] Source =
        {
            ("teal_paint", new Color(0.0844f, 0.2664f, 0.2918f)),
            ("dark_glass", new Color(0.0232f, 0.0176f, 0.0513f)),
            ("faded_roof", new Color(0.2705f, 0.4233f, 0.4342f)),
            ("glass_reflection", new Color(0.1529f, 0.1070f, 0.3185f)),
            ("dark_trim", new Color(0.0437f, 0.0423f, 0.0545f)),
            ("tire_rubber", new Color(0.0252f, 0.0242f, 0.0296f)),
            ("hubcap_silver", new Color(0.5841f, 0.5647f, 0.5271f)),
            ("headlight_glow", new Color(1f, 0.7605f, 0.4342f)),
            ("indicator_amber", new Color(0.6654f, 0.3231f, 0.0908f)),
            ("taillight_glow", new Color(0.5520f, 0.0953f, 0.0762f)),
            ("sticker", new Color(0.7454f, 0.4793f, 0.1170f)),
            ("sticker_ink", new Color(0.0467f, 0.0356f, 0.0844f)),
            ("dent_shade", new Color(0.0578f, 0.1812f, 0.2016f)),
            ("scuff_bare", new Color(0.4851f, 0.5520f, 0.5029f)),
        };

        /// <summary>Lackfarben (sRGB): das Original-Petrol und fuenf passend zur Stadt.</summary>
        static readonly (string name, string hex)[] Paints =
        {
            ("Teal", null), ("Coral", "E8706A"), ("Mustard", "E9B949"), ("Lilac", "A98BD6"), ("Cream", "EADCC0"), ("Sky", "6EA8D8"),
        };

        // Teile mit Outline (Silhouette); alles andere ohne, das spart Draw Calls und sieht ruhiger aus
        static readonly string[] Outlined = { "body", "cabin_glass", "roof", "bumper_front", "bumper_rear" };

        [MenuItem("DriftSkate/Verkehr/NPC-Autos bauen")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Glb);
            if (src == null) throw new Exception("NPC-Auto fehlt: " + Glb);
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Meshes");
            Directory.CreateDirectory(Root + "/Textures");

            var inst = Object.Instantiate(src);
            var carRoot = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "toon_city_car") ?? inst.transform;
            try
            {
                // Ausrichtung pruefen: Scheinwerfer muessen vorn (+Z) sein
                var head = carRoot.GetComponentsInChildren<Transform>(true).First(t => t.name.StartsWith("headlight_L") || t.name.StartsWith("headlight_R"));
                float headZ = carRoot.InverseTransformPoint(head.position).z;
                var frame = new GameObject("Frame").transform; // Bezugssystem des fertigen Autos
                frame.SetPositionAndRotation(carRoot.position, carRoot.rotation * (headZ < 0f ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity));

                var names = Source.Select(x => x.mat).ToList();
                var groups = new Dictionary<string, MeshBuilder>();
                MeshBuilder G(string key, Transform space)
                {
                    if (!groups.TryGetValue(key, out var g)) groups[key] = g = new MeshBuilder(space);
                    return g;
                }

                // Rad-Pivots: Mitte jedes Rads, Achsen wie das Auto
                var wheelPivots = new Dictionary<string, Transform>();
                foreach (var w in carRoot.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("wheel_")))
                {
                    var pivot = new GameObject(w.name).transform;
                    pivot.SetPositionAndRotation(w.position, frame.rotation);
                    wheelPivots[w.name] = pivot;
                }

                foreach (var mf in carRoot.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    string node = mf.name;
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        string mat = CleanName(mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)].name);
                        if (mat == "headlight_halo") continue; // durchsichtiger Schein: weglassen
                        if (mat == "indicator_amber_0") mat = "indicator_amber";
                        int idx = names.IndexOf(mat);
                        if (idx < 0) throw new Exception("Unbekanntes Material im NPC-Auto: " + mat);

                        var wheel = mf.transform.GetComponentsInParent<Transform>(true).FirstOrDefault(t => t.name.StartsWith("wheel_"));
                        string key;
                        Transform space = frame;
                        if (wheel != null) { key = "W:" + wheel.name; space = wheelPivots[wheel.name]; }
                        else if (mat == "headlight_glow") key = "HeadLights";
                        else if (mat == "taillight_glow") key = "TailLights";
                        else if (mat == "indicator_amber")
                            key = frame.InverseTransformPoint(mf.transform.position).x < 0f ? "IndicatorLeft" : "IndicatorRight";
                        else key = Outlined.Contains(StripIndex(node)) ? "Body" : "Details";
                        G(key, space).Add(mf, sub, PaletteUv(idx));
                    }
                }

                var palettes = Paints.Select(p => MakePalette(p.name, p.hex)).ToArray();
                var shared = new Dictionary<string, Material>
                {
                    ["HeadLights"] = Mat("Traffic_HeadLights", Source[7].linear.gamma, 1.4f, 0f),
                    ["TailOff"] = Mat("Traffic_TailOff", Source[9].linear.gamma, 0.5f, 0f),
                    ["TailOn"] = Mat("Traffic_TailOn", new Color(1f, 0.15f, 0.12f), 2.6f, 0f),
                    ["IndOff"] = Mat("Traffic_IndicatorOff", Source[8].linear.gamma, 0.15f, 0f),
                    ["IndOn"] = Mat("Traffic_IndicatorOn", new Color(1f, 0.62f, 0.15f), 3f, 0f),
                };

                // Meshes speichern (einmal, fuer alle Farben gleich)
                var meshes = new Dictionary<string, Mesh>();
                foreach (var kv in groups)
                {
                    string file = kv.Key.Replace("W:", "Wheel_");
                    meshes[kv.Key] = SaveMesh(kv.Value.ToMesh("TrafficCar_" + file), Root + "/Meshes/TrafficCar_" + file + ".asset");
                }

                for (int v = 0; v < Paints.Length; v++)
                {
                    var bodyMat = Mat("Traffic_" + Paints[v].name, Color.white, 0f, 0.3f, palettes[v]);
                    var detailMat = Mat("Traffic_" + Paints[v].name + "_Detail", Color.white, 0f, 0f, palettes[v]);
                    var car = new GameObject("TrafficCar_" + Paints[v].name);
                    try
                    {
                        car.layer = LayerMask.NameToLayer("Car");
                        var rb = car.AddComponent<Rigidbody>();
                        rb.isKinematic = true;
                        rb.interpolation = RigidbodyInterpolation.Interpolate;
                        var box = car.AddComponent<BoxCollider>();
                        box.center = new Vector3(0f, 0.9f, 0f);
                        box.size = new Vector3(1.64f, 1.3f, 3.62f);
                        var tc = car.AddComponent<TrafficCar>();
                        car.AddComponent<Hitchable>();

                        Renderer Part(string key, Material m, Transform parent, Vector3 localPos, bool shadows)
                        {
                            if (!meshes.TryGetValue(key, out var mesh)) return null;
                            var go = new GameObject(key.Replace("W:", ""));
                            go.layer = car.layer;
                            go.transform.SetParent(parent, false);
                            go.transform.localPosition = localPos;
                            go.AddComponent<MeshFilter>().sharedMesh = mesh;
                            var r = go.AddComponent<MeshRenderer>();
                            r.sharedMaterial = m;
                            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                            r.lightProbeUsage = LightProbeUsage.Off;
                            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                            return r;
                        }

                        Part("Body", bodyMat, car.transform, Vector3.zero, true);
                        Part("Details", detailMat, car.transform, Vector3.zero, false);
                        Part("HeadLights", shared["HeadLights"], car.transform, Vector3.zero, false);
                        tc.tailLights = Part("TailLights", shared["TailOff"], car.transform, Vector3.zero, false);
                        tc.indicatorLeft = Part("IndicatorLeft", shared["IndOff"], car.transform, Vector3.zero, false);
                        tc.indicatorRight = Part("IndicatorRight", shared["IndOff"], car.transform, Vector3.zero, false);
                        tc.tailOff = shared["TailOff"]; tc.tailOn = shared["TailOn"];
                        tc.indicatorOff = shared["IndOff"]; tc.indicatorOn = shared["IndOn"];

                        // Raeder: vorn links, vorn rechts, hinten links, hinten rechts (links = -X im fertigen Auto)
                        var order = wheelPivots.Values
                            .Select(p => (p, local: frame.InverseTransformPoint(p.position)))
                            .OrderBy(x => x.local.z > 0f ? 0 : 1).ThenBy(x => x.local.x).ToList();
                        for (int i = 0; i < order.Count && i < 4; i++)
                        {
                            var pivot = new GameObject("Wheel" + i).transform;
                            pivot.SetParent(car.transform, false);
                            pivot.localPosition = order[i].local;
                            Part("W:" + order[i].p.name, bodyMat, pivot, Vector3.zero, false);
                            tc.wheels[i] = pivot;
                        }
                        tc.wheelBase = order.Count == 4 ? Mathf.Abs(order[0].local.z - order[2].local.z) : 2.2f;
                        tc.wheelRadius = 0.34f;
                        PrefabUtility.SaveAsPrefabAsset(car, Root + "/TrafficCar_" + Paints[v].name + ".prefab");
                    }
                    finally { Object.DestroyImmediate(car); }
                }
                foreach (var p in wheelPivots.Values) Object.DestroyImmediate(p.gameObject);
                Object.DestroyImmediate(frame.gameObject);
            }
            finally { Object.DestroyImmediate(inst); }
            AssetDatabase.SaveAssets();
            Debug.Log("[Traffic] NPC-Autos gebaut: " + string.Join(", ", Paints.Select(p => p.name)));
        }

        static string CleanName(string n)
        {
            int cut = n.IndexOfAny(new[] { ' ', '(' });
            return (cut > 0 ? n.Substring(0, cut) : n).Trim();
        }

        static string StripIndex(string n)
        {
            // glTFast haengt bei doppelten Namen Nummern an ("tire_1")
            int u = n.LastIndexOf('_');
            return u > 0 && int.TryParse(n.Substring(u + 1), out _) ? n.Substring(0, u) : n;
        }

        // ------------------------------------------------------------------ Palette

        const int PalSize = 4; // 4x4 Felder, je Feld 4x4 Pixel
        const int Cell = 4;

        static Vector2 PaletteUv(int idx)
        {
            int x = idx % PalSize, y = idx / PalSize;
            return new Vector2((x + 0.5f) / PalSize, (y + 0.5f) / PalSize);
        }

        static Texture2D MakePalette(string name, string paintHex)
        {
            var cols = Source.Select(s => s.linear.gamma).ToArray();
            if (paintHex != null)
            {
                ColorUtility.TryParseHtmlString("#" + paintHex, out Color paint);
                cols[0] = paint;                                         // Lack
                cols[2] = Color.Lerp(paint, Color.white, 0.32f);         // ausgebleichtes Dach
                cols[12] = Color.Lerp(paint, Color.black, 0.3f);         // Dellen
            }
            int size = PalSize * Cell;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int idx = (y / Cell) * PalSize + x / Cell;
                    tex.SetPixel(x, y, idx < cols.Length ? cols[idx] : Color.magenta);
                }
            tex.Apply();
            string path = Root + "/Textures/Traffic_Palette_" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.filterMode = FilterMode.Point;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Mat(string name, Color color, float emission, float outline, Texture tex = null)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(ToonMaterials.Shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = ToonMaterials.Shader;
            mat.enableInstancing = true;
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", tex);
            mat.SetFloat("_Emission", emission);
            mat.SetFloat("_OutlineWidth", outline);
            mat.SetFloat("_OutlineMode", 0f);
            mat.SetFloat("_RimStrength", 0.25f);
            mat.SetShaderPassEnabled("SRPDefaultUnlit", outline > 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        /// <summary>Sammelt Dreiecke vieler Teile in einem Raum (Auto oder Rad), mit Paletten-UV statt Material.</summary>
        class MeshBuilder
        {
            readonly Transform _space;
            readonly List<Vector3> _pos = new List<Vector3>(), _nrm = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<int> _tri = new List<int>();

            public MeshBuilder(Transform space) { _space = space; }

            public void Add(MeshFilter mf, int sub, Vector2 uv)
            {
                var mesh = mf.sharedMesh;
                Matrix4x4 m = _space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Matrix4x4 n = m.inverse.transpose;
                bool flip = m.determinant < 0f;
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
                    Vector3 nn = norms != null && norms.Length > v ? n.MultiplyVector(norms[v]).normalized : Vector3.up;
                    if (float.IsNaN(nn.x) || nn.sqrMagnitude < 0.5f) nn = Vector3.up;
                    _nrm.Add(nn);
                    _uv.Add(uv);
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

        // ------------------------------------------------------------------ Stadt

        [MenuItem("DriftSkate/Verkehr/In die Stadt setzen")]
        public static void PlaceInCity()
        {
            var prefabs = Paints.Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/TrafficCar_" + p.name + ".prefab")).ToArray();
            if (prefabs.Any(p => p == null)) { Build(); prefabs = Paints.Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/TrafficCar_" + p.name + ".prefab")).ToArray(); }
            var scene = EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var traffic = Object.FindAnyObjectByType<Traffic>(FindObjectsInactive.Include);
            if (traffic == null) traffic = new GameObject("Traffic").AddComponent<Traffic>();
            traffic.carPrefabs = prefabs;
            traffic.gameObject.isStatic = false;
            EditorUtility.SetDirty(traffic);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            traffic.BuildRoutes();
            Debug.Log("[Traffic] In der Stadt: " + string.Join(", ", traffic.routes.Select(r => $"{r.length:0} m / {r.duration:0} s")));
        }

        /// <summary>Batch: Autos bauen, in die Stadt setzen, Kontrollbilder.</summary>
        public static void BatchAll()
        {
            Build();
            PlaceInCity();
            RenderCheck();
        }

        [MenuItem("DriftSkate/Verkehr/Kontrollbilder rendern")]
        public static void RenderCheck()
        {
            Directory.CreateDirectory("Logs/traffic");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var cam = Camera.main;
            var traffic = Object.FindAnyObjectByType<Traffic>();
            traffic.BuildRoutes();

            // Alle Farben nebeneinander auf einer Strasse
            float z = CityBuilder.RoadCenter(1) - traffic.laneOffset, x0 = CityBuilder.BlockCenter(1) - 14f;
            var shown = new List<GameObject>();
            for (int i = 0; i < traffic.carPrefabs.Length; i++)
            {
                var car = (GameObject)PrefabUtility.InstantiatePrefab(traffic.carPrefabs[i]);
                car.transform.SetPositionAndRotation(new Vector3(x0 + i * 5.5f, 0f, z), Quaternion.Euler(0f, 90f + (i % 2) * 20f, 0f));
                shown.Add(car);
            }
            var c0 = shown[0].transform;
            Shot(cam, new Vector3(x0 + 13f, 3.2f, z - 12f), new Vector3(x0 + 13f, 0.7f, z), 50f, "lineup");
            Shot(cam, c0.TransformPoint(new Vector3(-3.2f, 1.6f, 4.2f)), c0.TransformPoint(new Vector3(0, 0.7f, 0)), 45f, "front");
            Shot(cam, c0.TransformPoint(new Vector3(3.4f, 1.4f, -4.4f)), c0.TransformPoint(new Vector3(0, 0.7f, 0)), 45f, "rear");
            Shot(cam, c0.TransformPoint(new Vector3(4.6f, 0.9f, 0.2f)), c0.TransformPoint(new Vector3(0, 0.7f, 0)), 45f, "side");
            foreach (var s in shown) Object.DestroyImmediate(s);

            // Routen von oben: auf jeder Route ein Auto, Blick ueber die Stadt
            for (int r = 0; r < traffic.routes.Count; r++)
            {
                var route = traffic.routes[r];
                route.Sample(route.length * 0.12f, out Vector3 p, out Vector3 d, out _);
                var car = (GameObject)PrefabUtility.InstantiatePrefab(traffic.carPrefabs[r % traffic.carPrefabs.Length]);
                car.transform.SetPositionAndRotation(p, Quaternion.LookRotation(d));
                shown.Add(car);
                if (r == 0)
                    Shot(cam, p - d * 9f + Vector3.Cross(Vector3.up, d) * 4f + Vector3.up * 3.5f, p + d * 6f, 55f, "road");
            }
            Shot(cam, new Vector3(0f, 420f, -260f), Vector3.zero, 60f, "overview");
            foreach (var s in shown) Object.DestroyImmediate(s);
        }

        static void Shot(Camera cam, Vector3 pos, Vector3 look, float fov, string name)
        {
            cam.fieldOfView = fov;
            cam.transform.position = pos;
            cam.transform.LookAt(look);
            SimTests.Capture(cam, "Logs/traffic/traffic_" + name + ".png");
        }
    }
}
