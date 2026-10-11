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
    /// Baut die Werkstatt-Garage aus Art/Environment/garage_mainmenu.glb (Fynns Modell, ca. 1000 Teile) zu wenigen
    /// Meshes: Huelle (Waende, Boden-Linien), Einrichtung (Paletten-Textur), Neon, Bildschirme, Aufkleber-Atlas,
    /// Boden und gekachelte Waende mit eigener Textur, dazu die Drehscheibe als eigenes Teil.
    /// Das Prefab liegt in Resources, GarageEnvironment laedt es zur Laufzeit (GarageDef.model).
    /// Menue: DriftSkate → Garagen.
    /// </summary>
    public static class GarageAssets
    {
        const string Name = "GarageWorkshop";
        const string Root = "Assets/_Game/Garages";
        const string SourceGlb = "Art/Environment/garage_mainmenu.glb";
        const string GlbPath = Root + "/Source/garage_mainmenu.glb";
        const string PrefabPath = "Assets/_Game/Resources/Garages/" + Name + ".prefab";
        static string MatPath(string part) => Root + "/Materials/" + Name + "_" + part + ".mat";
        static string MeshPath(string part) => Root + "/Meshes/" + Name + "_" + part + ".asset";
        static string TexPath(string part) => Root + "/Textures/" + Name + "_" + part + ".png";

        [MenuItem("DriftSkate/Garagen/Werkstatt bauen")] public static void Build() => BuildPrefab();
        [MenuItem("DriftSkate/Garagen/Kontrollbilder rendern")] public static void Render() => RenderCheck();

        /// <summary>Batch: Prefab bauen und Kontrollbilder (Logs/garage/*.png).</summary>
        public static void BatchAll()
        {
            BuildPrefab();
            RenderCheck();
        }

        // ------------------------------------------------------------------ GLB-Materialien

        [Serializable] class GltfJson { public GltfMat[] materials; }
        [Serializable] class GltfMat { public string name; public GltfPbr pbrMetallicRoughness; public float[] emissiveFactor; public GltfTexRef emissiveTexture; public string alphaMode; }
        [Serializable] class GltfPbr { public float[] baseColorFactor; public GltfTexRef baseColorTexture; }
        [Serializable] class GltfTexRef { public int index = -1; public GltfTexExt extensions; }
        [Serializable] class GltfTexExt { public GltfTexTransform KHR_texture_transform; }
        [Serializable] class GltfTexTransform { public float[] scale, offset; }

        class MatInfo
        {
            public Color color = Color.white, emissive = Color.black;
            public bool textured, glow, translucent;
            public Vector2 tiling = Vector2.one, offset;
            public bool Tiled => tiling != Vector2.one || offset != Vector2.zero;

            /// <summary>KHR_texture_transform als UV-Rechteck fuer die Mesh-UVs. glTFast dreht V (v' = 1 - v), daher y = 1 - scale - offset.</summary>
            public Rect UvRect => new Rect(offset.x, 1f - tiling.y - offset.y, tiling.x, tiling.y);
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
                if (f != null && f.Length >= 3) info.color = new Color(f[0], f[1], f[2]).gamma; // glTF-Farben sind linear
                info.translucent = f != null && f.Length >= 4 && f[3] < 0.9f;
                var tex = m.pbrMetallicRoughness?.baseColorTexture;
                info.textured = tex != null && tex.index >= 0;
                var tt = tex?.extensions?.KHR_texture_transform;
                if (tt?.scale != null && tt.scale.Length == 2) info.tiling = new Vector2(tt.scale[0], tt.scale[1]);
                if (tt?.offset != null && tt.offset.Length == 2) info.offset = new Vector2(tt.offset[0], tt.offset[1]);
                var e = m.emissiveFactor;
                if (e != null && e.Length >= 3 && e[0] + e[1] + e[2] > 0.05f)
                {
                    info.glow = true;
                    info.emissive = new Color(e[0], e[1], e[2]).gamma;
                }
                result[m.name] = info;
            }
            return result;
        }

        // ------------------------------------------------------------------ Bauen

        public static void BuildPrefab()
        {
            // Alte Teile (andere Modellversion) wegraeumen, alles wird neu erzeugt
            foreach (var d in new[] { "/Meshes", "/Materials", "/Textures" }) AssetDatabase.DeleteAsset(Root + d);
            GlbBake.EnsureFolders(Root + "/Source", Root + "/Meshes", Root + "/Materials", Root + "/Textures", "Assets/_Game/Resources/Garages");
            if (!File.Exists(SourceGlb)) throw new Exception("Modell fehlt: " + SourceGlb);
            File.Copy(SourceGlb, GlbPath, true);
            AssetDatabase.ImportAsset(GlbPath, ImportAssetOptions.ForceUpdate);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
            if (src == null) throw new Exception("GLB konnte nicht importiert werden: " + GlbPath);
            var mats = ReadMaterials(GlbPath);

            var inst = Object.Instantiate(src);
            var frame = new GameObject("Frame").transform;
            try
            {
                var all = inst.GetComponentsInChildren<Transform>(true);
                Transform Find(string n) => all.FirstOrDefault(t => GlbBake.CleanName(t.name) == n);
                bool Under(Transform t, string group)
                {
                    for (; t != null; t = t.parent) if (GlbBake.CleanName(t.name) == group) return true;
                    return false;
                }

                // Im Modell liegt die Rueckwand bei -Z, in der Garage bei +Z (Kamera steht vorn): um 180 Grad drehen
                frame.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
                Matrix4x4 toFrame = frame.worldToLocalMatrix;

                // Texturen einsammeln: leuchtende (Bildschirme, Fenster) und normale (Aufkleber, Graffiti, Poster) in je einen Atlas,
                // Boden und gekachelte Waende behalten eigene Texturen
                var tex = new Dictionary<string, Texture>();
                foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (var um in mr.sharedMaterials)
                    {
                        string n = GlbBake.CleanName(um.name);
                        var t = GlbBake.FirstTexture(um);
                        if (t != null && !tex.ContainsKey(n)) tex[n] = t;
                    }

                const string floorMat = "Floor_Concrete", slotMat = "Graffiti_Slot";
                bool Tiled(string n) => mats.TryGetValue(n, out var i) && i.Tiled;
                var screenNames = tex.Keys.Where(n => mats[n].glow && mats[n].textured).OrderBy(n => n).ToList();
                var decalNames = tex.Keys.Where(n => !mats[n].glow && mats[n].textured && n != floorMat && n != slotMat && !Tiled(n)).OrderBy(n => n).ToList();
                // Gekachelte Texturen (Waende, Decke, Rolltor): Kachelung steckt in den Mesh-UVs, ein Material je Bild
                var wallTex = tex.Keys.Where(Tiled).Select(n => tex[n]).Distinct().ToList();

                Texture2D Atlas(List<string> names, string part, int max, out Rect[] rects)
                {
                    var readable = names.Select(k => GlbBake.Readable(tex[k])).ToArray();
                    var atlasTex = new Texture2D(max, max, TextureFormat.RGBA32, false);
                    rects = atlasTex.PackTextures(readable, 4, max, false);
                    foreach (var r in readable) Object.DestroyImmediate(r);
                    return Compressed(GlbBake.SaveTexture(atlasTex, TexPath(part), FilterMode.Bilinear, true));
                }
                var decalAtlas = Atlas(decalNames, "Decals", 4096, out var decalRects);
                var screenAtlas = Atlas(screenNames, "Screens", 2048, out var screenRects);

                // Farben: Einrichtung und Huelle teilen sich eine Palette, Neon hat eine eigene (Leuchtfarbe)
                var colors = new List<Color>();
                var glowColors = new List<Color>();
                int ColorIndex(List<Color> list, Color c)
                {
                    int i = list.FindIndex(x => Mathf.Abs(x.r - c.r) + Mathf.Abs(x.g - c.g) + Mathf.Abs(x.b - c.b) < 0.01f);
                    if (i >= 0) return i;
                    list.Add(c);
                    return list.Count - 1;
                }

                var shell = new GlbBake.MeshBuilder();      // Waende, Pfeiler, Traeger, Bodenlinien: ohne Schatten
                var body = new GlbBake.MeshBuilder();       // Einrichtung
                var turntable = new GlbBake.MeshBuilder();
                var neon = new GlbBake.MeshBuilder();
                var screens = new GlbBake.MeshBuilder();
                var decals = new GlbBake.MeshBuilder();
                var floor = new GlbBake.MeshBuilder();
                var walls = wallTex.Select(_ => new GlbBake.MeshBuilder()).ToList();
                int skipped = 0;
                const int paletteSize = 16;

                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    // Platzhalter-Auto und -Skater kommen aus dem Spiel
                    if (Under(mf.transform, "Car_Placeholder") || Under(mf.transform, "Skater_Lean") || Under(mf.transform, "Skater_Spot")) { skipped++; continue; }
                    bool isShell = Under(mf.transform, "Room_Walls") || Under(mf.transform, "Room_Floor") || Under(mf.transform, "Room_Ceiling")
                                   || Under(mf.transform, "Street_Outside");
                    bool isTurntable = Under(mf.transform, "Turntable");
                    Matrix4x4 m = toFrame * mf.transform.localToWorldMatrix;
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        string n = GlbBake.CleanName(mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)].name);
                        if (!mats.TryGetValue(n, out var info)) throw new Exception("Unbekanntes Material " + n);
                        if (n == slotMat) { skipped++; continue; }                 // dort haengt das eigene Graffiti
                        if (info.translucent && !info.textured) { skipped++; continue; } // Automaten-Glas: Dosen sollen sichtbar sein
                        if (n == floorMat) floor.Add(mf.sharedMesh, sub, Vector2.zero, m, true);
                        else if (Tiled(n)) walls[wallTex.IndexOf(tex[n])].Add(mf.sharedMesh, sub, Vector2.zero, m, true, info.UvRect);
                        else if (screenNames.Contains(n)) screens.Add(mf.sharedMesh, sub, Vector2.zero, m, true, screenRects[screenNames.IndexOf(n)]);
                        else if (decalNames.Contains(n)) decals.Add(mf.sharedMesh, sub, Vector2.zero, m, true, decalRects[decalNames.IndexOf(n)]);
                        else if (info.glow) neon.Add(mf.sharedMesh, sub, GlbBake.PaletteUv(ColorIndex(glowColors, info.emissive), paletteSize), m);
                        else
                        {
                            var uv = GlbBake.PaletteUv(ColorIndex(colors, info.color), paletteSize);
                            (isTurntable ? turntable : isShell ? shell : body).Add(mf.sharedMesh, sub, uv, m);
                        }
                    }
                }

                var palette = GlbBake.Palette(TexPath("Palette"), colors.ToArray(), paletteSize);
                var glowPalette = GlbBake.Palette(TexPath("NeonPalette"), glowColors.ToArray(), paletteSize);
                var bodyMat = GlbBake.ToonMat(MatPath("Body"), Color.white, palette, 0.3f, rim: 0.15f);
                var shellMat = GlbBake.ToonMat(MatPath("Shell"), Color.white, palette, 0.25f, rim: 0.1f);

                var root = new GameObject(Name);
                try
                {
                    int detail = LayerMask.NameToLayer("Detail");
                    Transform Part(string part, GlbBake.MeshBuilder b, Material mat, bool shadows, int layer = -1)
                    {
                        if (b.Empty) return null;
                        return GlbBake.Part(root.transform, part, GlbBake.SaveMesh(b.ToMesh(Name + "_" + part), MeshPath(part)), mat, shadows, layer).transform;
                    }

                    Part("Shell", shell, shellMat, false);
                    Part("Body", body, bodyMat, true);
                    Part("Floor", floor, GlbBake.ToonMat(MatPath("Floor"), Color.white, Compressed(CopyTexture(tex[floorMat], "Floor")), 0f, rim: 0f), false);
                    for (int i = 0; i < wallTex.Count; i++)
                    {
                        string w = "Tiled" + i;
                        var wm = GlbBake.ToonMat(MatPath(w), Color.white, Compressed(CopyTexture(wallTex[i], w), TextureWrapMode.Repeat), 0f, rim: 0.1f);
                        Part(w, walls[i], wm, false);
                    }
                    Part("Neon", neon, GlbBake.ToonMat(MatPath("Neon"), Color.white, glowPalette, 0f, 1.4f, rim: 0f), false);
                    Part("Screens", screens, GlbBake.ToonMat(MatPath("Screens"), Color.white, screenAtlas, 0f, 0.9f, rim: 0f), false);
                    Part("Decals", decals, GlbBake.ToonMat(MatPath("Decals"), Color.white, decalAtlas, 0f, cutoff: 0.5f, rim: 0.1f), false, detail);
                    // Deck etwas absenken: die Autos stehen wie bei der eingebauten Scheibe auf 0,1 m
                    var tt = Part("Turntable", turntable, bodyMat, false);
                    if (tt != null) tt.localPosition = new Vector3(0f, -0.035f, 0f);

                    // Ankerpunkte fuer GarageEnvironment: Stellplaetze und eigenes Graffiti (der Skater steht wie immer neben der Drehscheibe)
                    Vector3 Point(string node) => toFrame.MultiplyPoint3x4(Find(node).position);
                    var anchors = new GameObject("Anchors").transform;
                    anchors.SetParent(root.transform, false);
                    // Stellplaetze an den Nummern am Boden, Auto 0,4 m weiter zur Mitte, Heck zur Wand. Erst die Seite gegenueber
                    // der Kamera (x < 0), jeweils von der Drehscheibe nach hinten
                    var bays = all.Where(t => GlbBake.StripIndex(t.name) == "Bay_Number").Select(t => toFrame.MultiplyPoint3x4(t.position))
                                  .OrderBy(p => p.x > 0f).ThenByDescending(p => p.z).ToList();
                    for (int i = 0; i < bays.Count; i++)
                    {
                        var slot = new GameObject("Slot_" + i).transform;
                        slot.SetParent(anchors, false);
                        slot.localPosition = new Vector3(bays[i].x - Mathf.Sign(bays[i].x) * 0.4f, 0f, bays[i].z);
                        slot.localRotation = Quaternion.LookRotation(new Vector3(-Mathf.Sign(bays[i].x), 0f, 0f));
                        Debug.Log($"[Garage] Stellplatz {i}: {slot.localPosition}");
                    }
                    // Raum fuer die Kamera: innerhalb der Waende und unter der Decke
                    Bounds Box(string group)
                    {
                        var b = new Bounds();
                        bool any = false;
                        foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true).Where(r => Under(r.transform, group)))
                            for (int k = 0; k < 8; k++)
                            {
                                var e = r.bounds.extents;
                                var c = toFrame.MultiplyPoint3x4(r.bounds.center + Vector3.Scale(e, new Vector3(k & 1, (k >> 1) & 1, (k >> 2) & 1) * 2f - Vector3.one));
                                if (!any) { b = new Bounds(c, Vector3.zero); any = true; } else b.Encapsulate(c);
                            }
                        return b;
                    }
                    var walls3 = Box("Room_Walls");
                    var ceiling = Box("Room_Ceiling");
                    var camMin = new GameObject("CameraMin").transform;
                    camMin.SetParent(anchors, false);
                    camMin.localPosition = walls3.min + new Vector3(0.6f, 0.5f, 0.6f);
                    var camMax = new GameObject("CameraMax").transform;
                    camMax.SetParent(anchors, false);
                    camMax.localPosition = new Vector3(walls3.max.x - 0.6f, (ceiling.size != Vector3.zero ? ceiling.min.y : walls3.max.y) - 0.5f, walls3.max.z - 0.6f);
                    Debug.Log($"[Garage] Kamera-Raum {camMin.localPosition} .. {camMax.localPosition}");

                    var graffiti = new GameObject("GraffitiSlot").transform;
                    graffiti.SetParent(anchors, false);
                    graffiti.localPosition = Point("Graffiti_Slot_Surface") + Vector3.back * 0.01f;

                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    Debug.Log($"[Garage] Prefab gebaut: {PrefabPath} ({colors.Count} Farben, {glowColors.Count} Neon, {decalNames.Count} Aufkleber, " +
                              $"{screenNames.Count} Bildschirme, {wallTex.Count} Kacheltexturen, {skipped} Teile weggelassen)");
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

        /// <summary>Import-Textur als eigenes PNG-Asset speichern (sonst haengt sie unkomprimiert an der GLB).</summary>
        static Texture2D CopyTexture(Texture t, string part) => GlbBake.SaveTexture(GlbBake.Readable(t), TexPath(part), FilterMode.Bilinear, true);

        /// <summary>Grosse Texturen komprimieren (GlbBake speichert unkomprimiert, das ist fuer kleine Paletten gedacht).</summary>
        static Texture2D Compressed(Texture2D t, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            string path = AssetDatabase.GetAssetPath(t);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.wrapMode = wrap;
            imp.anisoLevel = 4;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ Kontrolle

        public static void RenderCheck()
        {
            Directory.CreateDirectory("Logs/garage");
            EditorSceneManager.OpenScene(ProjectBuilder.GaragePath);
            var env = Object.FindAnyObjectByType<GarageEnvironment>();
            var profile = new PlayerProfile();
            profile.EnsureDefaults();
            foreach (var c in Catalog.Cars.Take(4)) if (!profile.ownedCars.Contains(c.id)) profile.ownedCars.Add(c.id);
            var garage = Catalog.Garage("workshop");
            env.Build(garage, profile);
            CarBuilder.Build(env.carRoot, Catalog.Car(profile.selectedCar), Palette.White, Palette.Pink, SaveSystem.Graffiti, null);
            SkaterBuilder.Build(env.skaterRoot, SkaterBuilder.Outfit.FromProfile(profile), Catalog.Boards[0], SaveSystem.Graffiti, null);
            var cam = Camera.main;
            var orbit = cam.GetComponent<OrbitCamera>();

            float yaw0 = orbit.yaw, pitch0 = orbit.pitch, dist0 = orbit.distance;
            void Shot(float yaw, float pitch, float dist, string file)
            {
                orbit.yaw = yaw;
                orbit.pitch = pitch;
                orbit.distance = dist;
                orbit.Apply(); // mit Wand-Grenze wie im Spiel
                SimTests.Capture(cam, "Logs/garage/" + file + ".png");
            }

            Shot(yaw0, pitch0, dist0, "menu");
            Shot(yaw0, pitch0, 16f, "menu_far");
            Shot(yaw0, 45f, 16f, "menu_top");
            Shot(yaw0, 2f, 5f, "menu_near");
            Shot(30f, 14f, 12f, "right");
            Shot(-80f, 14f, 12f, "left");
            Shot(180f, 20f, 9f, "back");
        }
    }
}
