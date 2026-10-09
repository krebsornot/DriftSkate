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
    /// NPC-Autos aus GLB-Modellen (glTFast-Import): alle Teile zu wenigen Meshes zusammengefasst, Farben ueber eine
    /// kleine Paletten-Textur (ein Material pro Auto), Raeder als drehbare Pivots (im Modell eingelenkte Vorderraeder
    /// werden gerade gestellt), Lichter getrennt (Bremslicht, Blinker). Fahrtrichtung und links/rechts kommen aus den
    /// Radpositionen, die Farben direkt aus den GLB-Materialien. Dazu der Verkehr in der Stadt.
    /// </summary>
    public static class TrafficAssets
    {
        const string Root = "Assets/_Game/Traffic";

        /// <summary>Ein Auto-Modell, optional in mehreren Lackfarben (Material-Name -> neue Farbe).</summary>
        class CarSpec
        {
            public string glb, name;
            public Dictionary<string, Color> overrides;
            public bool traffic = true; // faehrt im NPC-Verkehr mit
        }

        /// <summary>NPC-Autos etwas groesser als echt: auf den 24 m breiten Strassen wirkten sie sonst wie Spielzeug.</summary>
        const float NpcScale = 1.4f;

        static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out Color c); return c; }

        /// <summary>Toon-Kleinwagen in sechs Farben (Lack, ausgebleichtes Dach, Dellen) plus die Serienautos aus Art/Cars.</summary>
        static List<CarSpec> Specs()
        {
            var list = new List<CarSpec>();
            foreach (var (name, hex) in new[] { ("Teal", (string)null), ("Coral", "E8706A"), ("Mustard", "E9B949"), ("Lilac", "A98BD6"), ("Cream", "EADCC0"), ("Sky", "6EA8D8") })
            {
                var o = new Dictionary<string, Color>();
                if (hex != null)
                {
                    Color paint = Hex(hex);
                    o["teal_paint"] = paint;
                    o["faded_roof"] = Color.Lerp(paint, Color.white, 0.32f);
                    o["dent_shade"] = Color.Lerp(paint, Color.black, 0.3f);
                }
                list.Add(new CarSpec { glb = "toon-city-car", name = name, overrides = o, traffic = false });
            }
            // Serienautos (die getunten Varianten in Art/Cars kommen spaeter als Spielerautos)
            list.Add(new CarSpec { glb = "kaze_fc_sunny", name = "KazeFC" });
            list.Add(new CarSpec { glb = "raijin_34r_night", name = "Raijin34R" });
            list.Add(new CarSpec { glb = "roku_ae_panda", name = "RokuAE" });
            list.Add(new CarSpec { glb = "sylph_s15_pool", name = "SylphS15" });
            list.Add(new CarSpec { glb = "toro_2j_blaze", name = "Toro2J" });
            list.Add(new CarSpec { glb = "mark_ii_j_night", name = "MarkIIJ" });
            return list;
        }

        static string PrefabPath(CarSpec s) => Root + "/TrafficCar_" + s.name + ".prefab";

        // Teile mit Outline (Silhouette); alles andere ohne, das spart Draw Calls und sieht ruhiger aus
        static readonly string[] OutlinedPrefixes = { "body", "cabin", "roof", "bumper", "flare" };

        [MenuItem("DriftSkate/Verkehr/NPC-Autos bauen")]
        public static void Build()
        {
            AssetDatabase.Refresh();
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Meshes");
            Directory.CreateDirectory(Root + "/Textures");
            var lights = new Dictionary<string, Material>
            {
                ["HeadLights"] = Mat("Traffic_HeadLights", new Color(1f, 0.9f, 0.7f), 1.4f, 0f),
                ["TailOff"] = Mat("Traffic_TailOff", new Color(0.77f, 0.34f, 0.31f), 0.5f, 0f),
                ["TailOn"] = Mat("Traffic_TailOn", new Color(1f, 0.15f, 0.12f), 2.6f, 0f),
                ["IndOff"] = Mat("Traffic_IndicatorOff", new Color(0.85f, 0.6f, 0.33f), 0.15f, 0f),
                ["IndOn"] = Mat("Traffic_IndicatorOn", new Color(1f, 0.62f, 0.15f), 3f, 0f),
            };
            foreach (var group in Specs().GroupBy(s => s.glb))
                BuildModel(group.Key, group.ToList(), lights);
            AssetDatabase.SaveAssets();
            Debug.Log("[Traffic] NPC-Autos gebaut: " + string.Join(", ", Specs().Select(s => s.name)));
        }

        // ------------------------------------------------------------------ GLB-Farben

        [Serializable] class GltfJson { public GltfMat[] materials; }
        [Serializable] class GltfMat { public string name; public GltfPbr pbrMetallicRoughness; }
        [Serializable] class GltfPbr { public float[] baseColorFactor; }

        /// <summary>Materialfarben (sRGB) direkt aus dem JSON-Teil der GLB.</summary>
        static Dictionary<string, Color> ReadGlbColors(string path)
        {
            var bytes = File.ReadAllBytes(path);
            int len = BitConverter.ToInt32(bytes, 12);
            var json = JsonUtility.FromJson<GltfJson>(System.Text.Encoding.UTF8.GetString(bytes, 20, len));
            var result = new Dictionary<string, Color>();
            foreach (var m in json.materials ?? new GltfMat[0])
            {
                var f = m.pbrMetallicRoughness?.baseColorFactor;
                var linear = f != null && f.Length >= 3 ? new Color(f[0], f[1], f[2]) : Color.white;
                result[m.name] = linear.gamma;
            }
            return result;
        }

        // ------------------------------------------------------------------ Modell -> Prefabs

        static bool IsWheelRoot(Transform t) =>
            t.name.StartsWith("wheel_") && (t.parent == null || !t.parent.name.StartsWith("wheel_"));

        static bool IsFrontWheel(Transform t) => t.name.Contains("front") || t.name.StartsWith("wheel_F");

        static void BuildModel(string glbName, List<CarSpec> specs, Dictionary<string, Material> lights)
        {
            string glbPath = Root + "/Source/" + glbName + ".glb";
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (src == null) throw new Exception("NPC-Auto fehlt: " + glbPath);
            var colors = ReadGlbColors(glbPath);
            var names = colors.Keys.ToList();
            if (names.Count > PalSize * PalSize) throw new Exception(glbName + ": zu viele Materialien fuer die Palette");

            var inst = Object.Instantiate(src);
            var all = inst.GetComponentsInChildren<Transform>(true);
            var wheelRoots = all.Where(IsWheelRoot).ToList();
            if (wheelRoots.Count != 4) throw new Exception(glbName + ": 4 Raeder erwartet, gefunden " + wheelRoots.Count);
            // Auto-Wurzel = gemeinsamer Elternknoten der Raeder
            Transform carRoot = wheelRoots[0].parent;
            var frame = new GameObject("Frame").transform;
            try
            {
                // Fahrtrichtung aus den Radpositionen (vorn minus hinten), Boden = Wurzel des Modells
                Vector3 front = Vector3.zero, rear = Vector3.zero;
                foreach (var w in wheelRoots) { if (IsFrontWheel(w)) front += w.position; else rear += w.position; }
                Vector3 fwd = Vector3.ProjectOnPlane(front - rear, carRoot.up).normalized;
                frame.SetPositionAndRotation(carRoot.position, Quaternion.LookRotation(fwd, carRoot.up));
                frame.localScale = Vector3.one / NpcScale; // alles im fertigen Auto um NpcScale groesser
                // Ungelenkte Radachsen: die der Auto-Wurzel, im fertigen Auto ausgedrueckt (inkl. Vergroesserung)
                Matrix4x4 unsteer = Matrix4x4.Scale(Vector3.one * NpcScale) * Matrix4x4.Rotate(Quaternion.Inverse(frame.rotation) * carRoot.rotation);

                var groups = new Dictionary<string, MeshBuilder>();
                MeshBuilder G(string key) { if (!groups.TryGetValue(key, out var g)) groups[key] = g = new MeshBuilder(); return g; }
                float tireRadius = 0f;

                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    var wheel = mf.transform.GetComponentsInParent<Transform>(true).FirstOrDefault(IsWheelRoot);
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        string mat = CleanName(mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)].name);
                        if (!colors.ContainsKey(mat)) throw new Exception(glbName + ": unbekanntes Material " + mat);
                        if (mat.Contains("halo")) continue; // durchsichtiger Schein: weglassen
                        Vector2 uv = PaletteUv(names.IndexOf(mat));
                        if (wheel != null)
                        {
                            Matrix4x4 m = unsteer * (wheel.worldToLocalMatrix * mf.transform.localToWorldMatrix);
                            G("W:" + wheel.name).Add(mf.sharedMesh, sub, uv, m);
                            if (mat.Contains("tire")) tireRadius = Mathf.Max(tireRadius, mf.sharedMesh.bounds.extents.y * mf.transform.lossyScale.y * NpcScale);
                            continue;
                        }
                        string key;
                        if (mat.Contains("headlight") || mat.Contains("headlamp")) key = "HeadLights";
                        else if (mat.Contains("taillight") || mat.Contains("taillamp")) key = "TailLights";
                        else if (mat.Contains("indicator"))
                            key = frame.InverseTransformPoint(mf.transform.position).x < 0f ? "IndicatorLeft" : "IndicatorRight";
                        else
                        {
                            string node = StripIndex(mf.name);
                            key = OutlinedPrefixes.Any(p => node.StartsWith(p)) ? "Body" : "Details";
                        }
                        G(key).Add(mf.sharedMesh, sub, uv, frame.worldToLocalMatrix * mf.transform.localToWorldMatrix);
                    }
                }

                // Meshes einmal speichern (fuer alle Farben gleich)
                var meshes = new Dictionary<string, Mesh>();
                foreach (var kv in groups)
                {
                    string file = glbName + "_" + kv.Key.Replace("W:", "Wheel_");
                    meshes[kv.Key] = SaveMesh(kv.Value.ToMesh(file), Root + "/Meshes/" + file + ".asset");
                }

                // Collider aus Karosserie und Anbauteilen (ohne Raeder)
                var b = meshes["Body"].bounds;
                if (meshes.TryGetValue("Details", out var det)) b.Encapsulate(det.bounds);
                float bottom = Mathf.Max(b.min.y, 0.25f);
                var boxCenter = new Vector3(b.center.x, (bottom + b.max.y) * 0.5f, b.center.z);
                var boxSize = new Vector3(b.size.x, b.max.y - bottom, b.size.z);

                var wheelInfo = wheelRoots
                    .Select(w => (w, local: frame.InverseTransformPoint(w.position)))
                    .OrderBy(x => x.local.z > 0f ? 0 : 1).ThenBy(x => x.local.x).ToList();

                foreach (var spec in specs)
                {
                    var palColors = names.Select(n => spec.overrides != null && spec.overrides.TryGetValue(n, out var c) ? c : colors[n]).ToArray();
                    var palette = MakePalette(spec.name, palColors);
                    var bodyMat = Mat("Traffic_" + spec.name, Color.white, 0f, 0.3f, palette);
                    var detailMat = Mat("Traffic_" + spec.name + "_Detail", Color.white, 0f, 0f, palette);
                    var car = new GameObject("TrafficCar_" + spec.name);
                    try
                    {
                        car.layer = LayerMask.NameToLayer("Car");
                        var rb = car.AddComponent<Rigidbody>();
                        rb.isKinematic = true;
                        rb.interpolation = RigidbodyInterpolation.Interpolate;
                        var box = car.AddComponent<BoxCollider>();
                        box.center = boxCenter;
                        box.size = boxSize;
                        var tc = car.AddComponent<TrafficCar>();
                        car.AddComponent<Hitchable>();

                        Renderer Part(string key, Material m, Transform parent, bool shadows)
                        {
                            if (!meshes.TryGetValue(key, out var mesh)) return null;
                            var go = new GameObject(key.Replace("W:", ""));
                            go.layer = car.layer;
                            go.transform.SetParent(parent, false);
                            go.AddComponent<MeshFilter>().sharedMesh = mesh;
                            var r = go.AddComponent<MeshRenderer>();
                            r.sharedMaterial = m;
                            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                            r.lightProbeUsage = LightProbeUsage.Off;
                            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                            return r;
                        }

                        Part("Body", bodyMat, car.transform, true);
                        Part("Details", detailMat, car.transform, false);
                        Part("HeadLights", lights["HeadLights"], car.transform, false);
                        tc.tailLights = Part("TailLights", lights["TailOff"], car.transform, false);
                        tc.indicatorLeft = Part("IndicatorLeft", lights["IndOff"], car.transform, false);
                        tc.indicatorRight = Part("IndicatorRight", lights["IndOff"], car.transform, false);
                        tc.tailOff = lights["TailOff"]; tc.tailOn = lights["TailOn"];
                        tc.indicatorOff = lights["IndOff"]; tc.indicatorOn = lights["IndOn"];

                        // Raeder: vorn links, vorn rechts, hinten links, hinten rechts (links = -X)
                        for (int i = 0; i < 4; i++)
                        {
                            var pivot = new GameObject("Wheel" + i).transform;
                            pivot.SetParent(car.transform, false);
                            pivot.localPosition = wheelInfo[i].local;
                            Part("W:" + wheelInfo[i].w.name, bodyMat, pivot, false);
                            tc.wheels[i] = pivot;
                        }
                        tc.wheelBase = Mathf.Abs(wheelInfo[0].local.z - wheelInfo[2].local.z);
                        tc.wheelRadius = tireRadius > 0.1f ? tireRadius : 0.33f;
                        PrefabUtility.SaveAsPrefabAsset(car, PrefabPath(spec));
                    }
                    finally { Object.DestroyImmediate(car); }
                }
            }
            finally
            {
                Object.DestroyImmediate(frame.gameObject);
                Object.DestroyImmediate(inst);
            }
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

        static Texture2D MakePalette(string name, Color[] cols)
        {
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
            readonly List<Vector3> _pos = new List<Vector3>(), _nrm = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<int> _tri = new List<int>();

            /// <summary>Teilmesh mit Matrix m (Mesh-Raum -> Zielraum) und Paletten-UV anhaengen.</summary>
            public void Add(Mesh mesh, int sub, Vector2 uv, Matrix4x4 m, bool keepUv = false)
            {
                var meshUv = keepUv ? mesh.uv : null;
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
                    _uv.Add(keepUv && meshUv != null && meshUv.Length > v ? meshUv[v] : uv);
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

        // ------------------------------------------------------------------ Garagen-Modelle (Spielerautos)

        const string ModelRoot = "Assets/_Game/CarModels";
        const string ModelPrefabs = "Assets/_Game/Resources/CarModels";

        /// <summary>
        /// Getunte Autos aus Art/Cars als fahrbare Spielerautos: CarModel-Prefab unter Resources/CarModels/&lt;glb&gt;.
        /// Karosserie, Anbauteile und Raeder ueber eine Paletten-Textur (Lack wird zur Laufzeit umgefaerbt),
        /// Aufkleber (Decals) mit ihren Texturen ausgestanzt, Lichter und Neon leuchtend, Rauch-Effekte weggelassen.
        /// </summary>
        [MenuItem("DriftSkate/Autos/Garagen-Modelle bauen")]
        public static void BuildPlayerModels()
        {
            AssetDatabase.Refresh();
            Directory.CreateDirectory(ModelRoot + "/Meshes");
            Directory.CreateDirectory(ModelRoot + "/Materials");
            Directory.CreateDirectory(ModelRoot + "/Textures");
            Directory.CreateDirectory(ModelPrefabs);
            foreach (var def in Catalog.Cars.Where(c => c.IsModel))
                BuildPlayerModel(def.model);
            AssetDatabase.SaveAssets();
            Debug.Log("[CarModels] Gebaut: " + string.Join(", ", Catalog.Cars.Where(c => c.IsModel).Select(c => c.model)));
        }

        static void BuildPlayerModel(string glbName)
        {
            string glbPath = ModelRoot + "/Source/" + glbName + ".glb";
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (src == null) throw new Exception("Auto-Modell fehlt: " + glbPath);
            var colors = ReadGlbColors(glbPath);
            var names = colors.Keys.Where(n => !n.StartsWith("decal_") && n != "smoke").ToList();
            if (names.Count > PalSize * PalSize) throw new Exception(glbName + ": zu viele Materialien fuer die Palette");

            var inst = Object.Instantiate(src);
            var wheelRoots = inst.GetComponentsInChildren<Transform>(true).Where(IsWheelRoot).ToList();
            if (wheelRoots.Count != 4) throw new Exception(glbName + ": 4 Raeder erwartet, gefunden " + wheelRoots.Count);
            Transform carRoot = wheelRoots[0].parent;
            var frame = new GameObject("Frame").transform;
            var root = new GameObject(glbName);
            try
            {
                Vector3 front = Vector3.zero, rear = Vector3.zero, mid = Vector3.zero;
                foreach (var w in wheelRoots) { if (IsFrontWheel(w)) front += w.position; else rear += w.position; mid += w.position * 0.25f; }
                Vector3 fwd = Vector3.ProjectOnPlane(front - rear, carRoot.up).normalized;
                // Achsmitte = Ursprung (wie die Federbeine der Physik), Boden = Hoehe der Modell-Wurzel
                frame.SetPositionAndRotation(new Vector3(mid.x, carRoot.position.y, mid.z), Quaternion.LookRotation(fwd, carRoot.up));
                Matrix4x4 unsteer = Matrix4x4.Rotate(Quaternion.Inverse(frame.rotation) * carRoot.rotation);

                var groups = new Dictionary<string, MeshBuilder>();
                MeshBuilder G(string key) { if (!groups.TryGetValue(key, out var g)) groups[key] = g = new MeshBuilder(); return g; }
                var decalMats = new Dictionary<string, Material>();
                float tireRadius = 0f;

                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    if (mf.transform.GetComponentsInParent<Transform>(true).Any(t => t.name.StartsWith("smoke") || t.name.StartsWith("tire_smoke"))) continue; // Rauch macht das Spiel selbst
                    var wheel = mf.transform.GetComponentsInParent<Transform>(true).FirstOrDefault(IsWheelRoot);
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        var srcMat = mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)];
                        string mat = CleanName(srcMat.name);
                        if (mat == "smoke" || mat.Contains("halo")) continue;
                        Matrix4x4 toFrame = frame.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                        if (mat.StartsWith("decal_"))
                        {
                            // Aufkleber: eigenes Material mit der Textur aus der GLB, ausgestanzt
                            if (!decalMats.ContainsKey(mat)) decalMats[mat] = DecalMaterial(glbName, mat, srcMat, colors.TryGetValue(mat, out var dc) ? dc : Color.white);
                            G("Decal:" + mat).Add(mf.sharedMesh, sub, Vector2.zero, toFrame, keepUv: true);
                            continue;
                        }
                        if (!colors.ContainsKey(mat)) throw new Exception(glbName + ": unbekanntes Material " + mat);
                        Vector2 uv = PaletteUv(names.IndexOf(mat));
                        if (wheel != null)
                        {
                            Matrix4x4 m = unsteer * (wheel.worldToLocalMatrix * mf.transform.localToWorldMatrix);
                            G("W:" + wheel.name).Add(mf.sharedMesh, sub, uv, m);
                            if (mat.Contains("tire")) tireRadius = Mathf.Max(tireRadius, mf.sharedMesh.bounds.extents.y * mf.transform.lossyScale.y);
                            continue;
                        }
                        string key;
                        if (mat.Contains("headl") || mat.Contains("taill") || mat.Contains("indicator")) key = "Lights";
                        else if (mat.Contains("underglow") || mat.Contains("neon")) key = "Glow";
                        else key = OutlinedPrefixes.Any(p => StripIndex(mf.name).StartsWith(p)) ? "Body" : "Details";
                        G(key).Add(mf.sharedMesh, sub, uv, toFrame);
                    }
                }

                var meshes = new Dictionary<string, Mesh>();
                foreach (var kv in groups)
                {
                    string file = glbName + "_" + kv.Key.Replace("W:", "Wheel_").Replace("Decal:", "Decal_");
                    meshes[kv.Key] = SaveMesh(kv.Value.ToMesh(file), ModelRoot + "/Meshes/" + file + ".asset");
                }

                var palCols = names.Select(n => colors[n]).ToArray();
                var palette = MakePalette("Car_" + glbName, palCols);
                var bodyMat = Mat("Car_" + glbName, Color.white, 0f, 0.3f, palette);
                var detailMat = Mat("Car_" + glbName + "_Detail", Color.white, 0f, 0f, palette);
                var lightMat = Mat("Car_" + glbName + "_Lights", Color.white, 1.2f, 0f, palette);
                var glowMat = Mat("Car_" + glbName + "_Glow", Color.white, 2.5f, 0f, palette);

                var model = root.AddComponent<CarModel>();
                var paletteRenderers = new List<Renderer>();
                Renderer Part(string key, Material m, Transform parent, bool shadows)
                {
                    if (!meshes.TryGetValue(key, out var mesh)) return null;
                    var go = new GameObject(key.Replace("W:", "").Replace("Decal:", ""));
                    go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = m;
                    r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    return r;
                }
                foreach (var (key, mat, shadows) in new[] { ("Body", bodyMat, true), ("Details", detailMat, false), ("Lights", lightMat, false), ("Glow", glowMat, false) })
                {
                    var r = Part(key, mat, root.transform, shadows);
                    if (r != null) paletteRenderers.Add(r);
                }
                foreach (var kv in decalMats) Part("Decal:" + kv.Key, kv.Value, root.transform, false);

                var wheelInfo = wheelRoots.Select(w => (w, local: frame.InverseTransformPoint(w.position)))
                    .OrderBy(x => x.local.z > 0f ? 0 : 1).ThenBy(x => x.local.x).ToList();
                for (int i = 0; i < 4; i++)
                {
                    var r = Part("W:" + wheelInfo[i].w.name, bodyMat, root.transform, false);
                    r.name = "Wheel" + i;
                    r.transform.localPosition = wheelInfo[i].local;
                    model.wheels[i] = r.transform;
                    paletteRenderers.Add(r);
                }

                var b = meshes["Body"].bounds;
                if (meshes.TryGetValue("Details", out var det)) b.Encapsulate(det.bounds);
                float bottom = Mathf.Max(b.min.y, 0.25f);
                model.boxCenter = new Vector3(b.center.x, (bottom + b.max.y) * 0.5f, b.center.z);
                model.boxSize = new Vector3(b.size.x, b.max.y - bottom, b.size.z);
                model.wheelRadius = tireRadius > 0.1f ? tireRadius : 0.31f;
                model.palette = palCols;
                model.paintSlots = names.Select((n, i) => (n, i)).Where(x => x.n == "paint" || x.n == "paint_upper").Select(x => x.i).ToArray();
                model.paletteSize = PalSize;
                model.cell = Cell;
                model.paletteRenderers = paletteRenderers.ToArray();
                PrefabUtility.SaveAsPrefabAsset(root, ModelPrefabs + "/" + glbName + ".prefab");
                Debug.Log($"[CarModels] {glbName}: Radius {model.wheelRadius:0.000}, Box {model.boxSize}, Lack-Felder {model.paintSlots.Length}, Decals {decalMats.Count}");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(frame.gameObject);
                Object.DestroyImmediate(inst);
            }
        }

        /// <summary>Aufkleber-Material: Toon, Textur aus dem glTFast-Material, Alpha ausgestanzt, ohne Outline.</summary>
        static Material DecalMaterial(string glbName, string matName, Material src, Color tint)
        {
            Texture tex = null;
            foreach (var prop in src.GetTexturePropertyNames())
            {
                var t = src.GetTexture(prop);
                if (t != null) { tex = t; break; }
            }
            if (tex == null) Debug.LogWarning($"[CarModels] {glbName}/{matName}: keine Textur gefunden");
            var mat = Mat("Car_" + glbName + "_" + matName, tint, 0f, 0f, tex);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.SetFloat("_RimStrength", 0.1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ Stadt

        [MenuItem("DriftSkate/Verkehr/In die Stadt setzen")]
        public static void PlaceInCity()
        {
            var specs = Specs().Where(s => s.traffic).ToList();
            var prefabs = specs.Select(s => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(s))).ToArray();
            if (prefabs.Any(p => p == null)) { Build(); prefabs = specs.Select(s => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(s))).ToArray(); }
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

        /// <summary>Batch: Garagen-Modelle + NPC-Autos bauen, Verkehr setzen, Kontrollbilder (Logs/cars, Logs/traffic).</summary>
        public static void BatchCars()
        {
            BuildPlayerModels();
            Build();
            PlaceInCity();
            RenderCheck();
            RenderPlayerModels();
        }

        [MenuItem("DriftSkate/Autos/Garagen-Modelle rendern")]
        public static void RenderPlayerModels()
        {
            Directory.CreateDirectory("Logs/cars");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var cam = Camera.main;
            float z = CityBuilder.RoadCenter(1) - 4f, x0 = CityBuilder.BlockCenter(1) - 14f;
            foreach (var def in Catalog.Cars.Where(c => c.IsModel))
            {
                var car = new GameObject("Preview_" + def.id);
                try
                {
                    car.transform.SetPositionAndRotation(new Vector3(x0, 0f, z), Quaternion.Euler(0f, 90f, 0f));
                    CarBuilder.Build(car.transform, def, def.defaultColor, Palette.Pink, null, null, null);
                    var t = car.transform;
                    Shot(cam, t.TransformPoint(new Vector3(-3.4f, 1.5f, 4.6f)), t.TransformPoint(new Vector3(0, 0.6f, 0)), 45f, "car_" + def.id, "Logs/cars/");
                    Shot(cam, t.TransformPoint(new Vector3(3.6f, 1.6f, -4.8f)), t.TransformPoint(new Vector3(0, 0.6f, 0)), 45f, "car_" + def.id + "_rear", "Logs/cars/");
                    var col = car.GetComponent<BoxCollider>();
                    Debug.Log($"[CarModels] {def.name}: Collider {col.size} um {col.center}, Raeder {car.GetComponentsInChildren<Transform>().Count(x => x.name.StartsWith("Wheel"))}");
                }
                finally { Object.DestroyImmediate(car); }
            }
        }

        [MenuItem("DriftSkate/Verkehr/Kontrollbilder rendern")]
        public static void RenderCheck()
        {
            Directory.CreateDirectory("Logs/traffic");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var cam = Camera.main;
            var traffic = Object.FindAnyObjectByType<Traffic>();
            traffic.BuildRoutes();

            // Jedes Auto einzeln schraeg von vorn und von hinten, dazu eine Reihe der neuen Modelle
            float z = CityBuilder.RoadCenter(1) - traffic.laneOffset, x0 = CityBuilder.BlockCenter(1) - 14f;
            var shown = new List<GameObject>();
            foreach (var prefab in traffic.carPrefabs)
            {
                var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                car.transform.SetPositionAndRotation(new Vector3(x0, 0f, z), Quaternion.Euler(0f, 90f, 0f));
                var c0 = car.transform;
                string n = prefab.name.Replace("TrafficCar_", "").ToLowerInvariant();
                Shot(cam, c0.TransformPoint(new Vector3(-3.6f, 1.7f, 5f)), c0.TransformPoint(new Vector3(0, 0.7f, 0)), 45f, "car_" + n);
                Shot(cam, c0.TransformPoint(new Vector3(3.6f, 1.5f, -5.2f)), c0.TransformPoint(new Vector3(0, 0.7f, 0)), 45f, "car_" + n + "_rear");
                Object.DestroyImmediate(car);
            }
            var models = traffic.carPrefabs.Where(p => !new[] { "Teal", "Coral", "Mustard", "Lilac", "Cream", "Sky" }.Any(c => p.name.EndsWith(c))).Prepend(traffic.carPrefabs[0]).ToArray();
            for (int i = 0; i < models.Length; i++)
            {
                var car = (GameObject)PrefabUtility.InstantiatePrefab(models[i]);
                car.transform.SetPositionAndRotation(new Vector3(x0 + i * 6f, 0f, z), Quaternion.Euler(0f, 90f, 0f));
                shown.Add(car);
            }
            float mid = x0 + (models.Length - 1) * 3f;
            Shot(cam, new Vector3(mid, 4f, z - 17f), new Vector3(mid, 0.7f, z), 55f, "lineup");
            foreach (var s in shown) Object.DestroyImmediate(s);
            shown.Clear();

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

        static void Shot(Camera cam, Vector3 pos, Vector3 look, float fov, string name, string folder = null)
        {
            cam.fieldOfView = fov;
            cam.transform.position = pos;
            cam.transform.LookAt(look);
            SimTests.Capture(cam, folder != null ? folder + name + ".png" : "Logs/traffic/traffic_" + name + ".png");
        }
    }
}
