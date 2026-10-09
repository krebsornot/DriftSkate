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
    /// Baut GLB-Deko aus Art/Props (Telefonzelle, Imbisswagen) und verteilt sie auf die Gehwege der Stadt.
    /// Wenige Draw Calls: Rumpf mit Paletten-Textur, leuchtendes Dachschild, Aufkleber als Atlas und Glas als
    /// Streifen-Cutout (beides auf der Ebene "Detail", wird nur in der Naehe gezeichnet).
    /// Menue: DriftSkate → Telefonzellen / Imbisswagen.
    /// </summary>
    public static class PropAssets
    {
        /// <summary>Beschreibt eine GLB-Deko, die auf die Gehwege verteilt wird.</summary>
        public class Config
        {
            public string name;                 // Prefab-/Asset-Name und Praefix
            public string folder;               // Assets/_Game/<folder>
            public string glbFile;              // Datei in Art/Props
            public string group;                // Szenen-Gruppe
            public string logFolder;
            public float scale = 1f;
            public string frontFrom, frontTo;   // Modell-Knoten: Vorwaerts = von -> nach (flach)
            public string glassMat, signMat;    // optionale Spezialmaterialien
            public float signEmission = 1.3f;
            public int count = 10;
            public float spacing = 50f;
            public float[] offsets = { -28f, -12f, 12f, 28f }; // entlang der Blockkante (Laternen stehen bei 0 und +-20)
            public float inset = 2.3f;          // Abstand der Mitte von der Blockkante
            public bool alongStreet;            // true: Vorwaerts laeuft entlang der Strasse statt zur Strasse
            public float alongFlip = 1f;
            public int seed = 1010;
            public string excludeTypes = "HD";  // Blockarten ohne Deko (Hafen, Driftplatz)
            public float probeForward = 0.4f;   // Platzpruefung: zusaetzlich zur Tuer-/Bedienseite
            public float probeSide = 0.5f;
            public float colliderHeight;        // > 0: Collider nur aus Teilen unter dieser Hoehe (m, fertig skaliert)
            public int perPark;                 // zusaetzlich so viele pro Park (P-Block), am Rand mit Blick zur Mitte

            public string Root => "Assets/_Game/" + folder;
            public string GlbPath => Root + "/Source/" + glbFile;
            public string PrefabPath => Root + "/" + name + ".prefab";
            public string SourceGlb => "Art/Props/" + glbFile;
            public string MatPath(string part) => Root + "/Materials/" + name + "_" + part + ".mat";
            public string MeshPath(string part) => Root + "/Meshes/" + name + "_" + part + ".asset";
            public string TexPath(string part) => Root + "/Textures/" + name + "_" + part + ".png";
        }

        public static readonly Config PhoneBooth = new Config
        {
            name = "PhoneBooth", folder = "PhoneBooth", glbFile = "phonebooth_teal.glb", group = "PhoneBooths", logFolder = "phonebooth",
            scale = 1.1f, frontFrom = "Fascia", frontTo = "Door_Header", glassMat = "Glass", signMat = "Sign_Light", signEmission = 1.3f,
            count = 14, spacing = 48f, seed = 1010,
        };

        public static readonly Config HotdogCart = new Config
        {
            name = "HotdogCart", folder = "HotdogCart", glbFile = "hotdogcart_orange.glb", group = "HotdogCarts", logFolder = "hotdogcart",
            scale = 1.15f, colliderHeight = 1.25f, frontFrom = "Cart_Body", frontTo = "Sign_Hotdog",
            count = 7, spacing = 62f, seed = 2020, alongStreet = true, inset = 3.2f, offsets = new[] { -26f, -8f, 8f, 26f },
            probeForward = 0.2f, probeSide = 0.6f, perPark = 1,
        };

        // ------------------------------------------------------------------ Menue

        [MenuItem("DriftSkate/Telefonzellen/Prefab bauen")] public static void BuildPhoneBooth() => Build(PhoneBooth);
        [MenuItem("DriftSkate/Telefonzellen/In die Stadt setzen")] public static void PlacePhoneBooths() => PlaceInCity(PhoneBooth);
        [MenuItem("DriftSkate/Telefonzellen/Kontrollbilder rendern")] public static void RenderPhoneBooths() => RenderCheck(PhoneBooth);
        [MenuItem("DriftSkate/Imbisswagen/Prefab bauen")] public static void BuildHotdogCart() => Build(HotdogCart);
        [MenuItem("DriftSkate/Imbisswagen/In die Stadt setzen")] public static void PlaceHotdogCarts() => PlaceInCity(HotdogCart);
        [MenuItem("DriftSkate/Imbisswagen/Kontrollbilder rendern")] public static void RenderHotdogCarts() => RenderCheck(HotdogCart);

        /// <summary>Batch: Prefab bauen, verteilen, Kontrollbilder.</summary>
        public static void BatchPhoneBooths() => BatchAll(PhoneBooth);
        public static void BatchHotdogCarts() => BatchAll(HotdogCart);

        static void BatchAll(Config c)
        {
            Build(c);
            PlaceInCity(c);
            RenderCheck(c);
        }

        // ------------------------------------------------------------------ Bauen

        public static void Build(Config c)
        {
            GlbBake.EnsureFolders(c.Root + "/Source", c.Root + "/Meshes", c.Root + "/Materials", c.Root + "/Textures");
            if (!File.Exists(c.GlbPath))
            {
                if (!File.Exists(c.SourceGlb)) throw new Exception("Modell fehlt: " + c.SourceGlb);
                File.Copy(c.SourceGlb, c.GlbPath, true);
            }
            AssetDatabase.ImportAsset(c.GlbPath, ImportAssetOptions.ForceUpdate);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(c.GlbPath);
            if (src == null) throw new Exception("GLB konnte nicht importiert werden: " + c.GlbPath);

            // Nur Materialien mit eigener Farbe; Aufkleber und Schild haben Texturen
            var colors = GlbBake.ReadColors(c.GlbPath, untintedAsWhite: false);
            if (c.glassMat != null) colors.Remove(c.glassMat);

            var inst = Object.Instantiate(src);
            var frame = new GameObject("Frame").transform;
            try
            {
                var all = inst.GetComponentsInChildren<Transform>(true);
                Transform Find(string n) => all.FirstOrDefault(t => GlbBake.CleanName(t.name) == n);
                var from = Find(c.frontFrom);
                var to = Find(c.frontTo);
                if (from == null || to == null) throw new Exception(c.frontFrom + " oder " + c.frontTo + " fehlt im Modell");

                // Vorwaerts (+Z) = von -> nach; Wurzel des Modells = Mitte unter dem Modell. Masse ohne Skalierung,
                // skaliert wird ueber das Visual-Objekt im Prefab
                Vector3 fwd = Vector3.ProjectOnPlane(to.position - from.position, Vector3.up).normalized;
                frame.SetPositionAndRotation(inst.transform.position, Quaternion.LookRotation(fwd, Vector3.up));
                Matrix4x4 toFrame = frame.worldToLocalMatrix;

                // Aufkleber-Texturen sammeln (Material-Name -> Textur) und als Atlas packen
                var decalTex = new Dictionary<string, Texture>();
                Texture signTex = null;
                foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (var um in mr.sharedMaterials)
                    {
                        string n = GlbBake.CleanName(um.name);
                        var tex = GlbBake.FirstTexture(um);
                        if (n == c.signMat) signTex = tex;
                        else if (n == c.glassMat) continue;
                        else if (tex != null && !decalTex.ContainsKey(n)) decalTex[n] = tex;       // alles mit Textur = Aufkleber
                        else if (tex == null && !colors.ContainsKey(n)) colors[n] = Color.white;   // ohne Faktor und Textur: weiss
                    }
                var names = colors.Keys.ToList();
                var decalNames = decalTex.Keys.OrderBy(k => k).ToList();
                Rect[] rects = new Rect[0];
                Texture2D atlas = null;
                if (decalNames.Count > 0)
                {
                    var readable = decalNames.Select(k => GlbBake.Readable(decalTex[k])).ToArray();
                    var atlasTex = new Texture2D(2048, 2048, TextureFormat.RGBA32, false);
                    rects = atlasTex.PackTextures(readable, 4, 2048, false);
                    foreach (var r in readable) Object.DestroyImmediate(r);
                    atlas = GlbBake.SaveTexture(atlasTex, c.TexPath("Decals"), FilterMode.Bilinear, true);
                }

                var body = new GlbBake.MeshBuilder();
                var decals = new GlbBake.MeshBuilder();
                var glass = new GlbBake.MeshBuilder();
                var sign = new GlbBake.MeshBuilder();
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    Matrix4x4 m = toFrame * mf.transform.localToWorldMatrix;
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        string mat = GlbBake.CleanName(mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)].name);
                        if (mat == c.glassMat) glass.Add(mf.sharedMesh, sub, Vector2.zero, m, true);
                        else if (mat == c.signMat) sign.Add(mf.sharedMesh, sub, Vector2.zero, m, true);
                        else if (decalTex.ContainsKey(mat)) decals.Add(mf.sharedMesh, sub, Vector2.zero, m, true, rects[decalNames.IndexOf(mat)]);
                        else if (colors.ContainsKey(mat)) body.Add(mf.sharedMesh, sub, GlbBake.PaletteUv(names.IndexOf(mat)), m);
                        else throw new Exception(c.name + ": unbekanntes Material " + mat);
                    }
                }

                var palette = GlbBake.Palette(c.TexPath("Palette"), names.Select(n => colors[n]).ToArray());
                var bodyMat = GlbBake.ToonMat(c.MatPath("Body"), Color.white, palette, 0.3f, rim: 0.15f);
                var bodyMesh = GlbBake.SaveMesh(body.ToMesh(c.name + "_Body"), c.MeshPath("Body"));

                var root = new GameObject(c.name);
                try
                {
                    int detail = LayerMask.NameToLayer("Detail");
                    var visual = new GameObject("Visual").transform;
                    visual.SetParent(root.transform, false);
                    visual.localScale = Vector3.one * c.scale;

                    void Part(string part, GlbBake.MeshBuilder b, Func<Material> mat, bool detailLayer)
                    {
                        if (b.Empty) return;
                        GlbBake.Part(visual, part, GlbBake.SaveMesh(b.ToMesh(c.name + "_" + part), c.MeshPath(part)), mat(), false,
                            detailLayer ? detail : -1);
                    }

                    GlbBake.Part(visual, "Body", bodyMesh, bodyMat, true);
                    Part("Sign", sign, () => GlbBake.ToonMat(c.MatPath("Sign"), Color.white, signTex, 0f, c.signEmission, rim: 0.15f), false);
                    Part("Decals", decals, () => GlbBake.ToonMat(c.MatPath("Decals"), Color.white, atlas, 0f, cutoff: 0.5f, rim: 0.15f), true);
                    Part("Glass", glass, () => GlbBake.ToonMat(c.MatPath("Glass"), Color.white, MakeGlassStreaks(c), 0f, 0.15f, true, 0.5f, 0.15f), true);

                    // Feste Huelle: Skater und Autos prallen ab. Bei hohen Aufbauten (Sonnenschirm, Schild) zaehlt nur
                    // der Teil unter colliderHeight
                    var bounds = bodyMesh.bounds;
                    if (c.colliderHeight > 0f)
                    {
                        var low = bodyMesh.vertices.Where(v => v.y * c.scale <= c.colliderHeight).ToList();
                        if (low.Count > 0)
                        {
                            bounds = new Bounds(low[0], Vector3.zero);
                            foreach (var v in low) bounds.Encapsulate(v);
                        }
                    }
                    var box = root.AddComponent<BoxCollider>();
                    box.center = bounds.center * c.scale;
                    box.size = bounds.size * c.scale;

                    PrefabUtility.SaveAsPrefabAsset(root, c.PrefabPath);
                    Debug.Log($"[{c.name}] Prefab gebaut: {c.PrefabPath} ({decalNames.Count} Aufkleber im Atlas, Groesse {box.size})");
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

        /// <summary>Glas als Toon-Highlights: zwei schraege helle Streifen, der Rest ist durchsichtig (Cutout).</summary>
        static Texture2D MakeGlassStreaks(Config c)
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float a = (x + y) / (float)(n * 2);
                    bool streak = (a > 0.30f && a < 0.40f) || (a > 0.47f && a < 0.51f);
                    tex.SetPixel(x, y, new Color(0.78f, 0.97f, 0.94f, streak ? 1f : 0f));
                }
            tex.Apply();
            return GlbBake.SaveTexture(tex, c.TexPath("GlassStreaks"), FilterMode.Bilinear, true);
        }

        // ------------------------------------------------------------------ Verteilen

        public static void PlaceInCity(Config c)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c.PrefabPath);
            if (prefab == null) { Build(c); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c.PrefabPath); }
            var scene = EditorSceneManager.OpenScene(ProjectBuilder.CityPath);

            var old = GameObject.Find(c.group);
            if (old != null) Object.DestroyImmediate(old);
            var group = new GameObject(c.group).transform;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(group.gameObject, scene);

            Physics.SyncTransforms();
            int solid = ~LayerMask.GetMask("Skater", "Car", "Ignore Raycast");
            int ground = solid & ~LayerMask.GetMask("Rail");
            var box = prefab.GetComponent<BoxCollider>();
            var half = box.size * 0.5f;

            // Kandidaten: Gehweg an den Blockseiten, Tuer zur Strasse (bzw. entlang der Strasse)
            var rng = new System.Random(c.seed);
            var cands = new List<(Vector3 pos, Vector3 facing)>();
            Vector3[] sides = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            for (int row = 0; row < CityBuilder.Grid; row++)
                for (int col = 0; col < CityBuilder.Grid; col++)
                {
                    if (c.excludeTypes.IndexOf(CityBuilder.BlockType(row, col)) >= 0) continue;
                    var center = new Vector3(CityBuilder.BlockCenter(col), 0f, CityBuilder.BlockCenter(row));
                    foreach (var side in sides)
                    {
                        Vector3 along = Vector3.Cross(Vector3.up, side);
                        foreach (float o in c.offsets)
                            cands.Add((center + side * (CityBuilder.Block * 0.5f - c.inset) + along * o, c.alongStreet ? along * c.alongFlip : side));
                    }
                }
            cands = cands.OrderBy(_ => rng.Next()).ToList();

            var placed = new List<Vector3>();
            foreach (var cd in cands)
            {
                if (placed.Count >= c.count) break;
                if (placed.Any(p => Vector3.Distance(p, cd.pos) < c.spacing)) continue;
                if (!Physics.Raycast(cd.pos + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 4f, ground, QueryTriggerInteraction.Ignore)) continue;
                if (hit.point.y < 0.07f || hit.point.y > 0.25f) continue; // nur auf dem Gehweg
                var rot = Quaternion.LookRotation(cd.facing);
                // Platz frei? (Box ueber dem Gehweg, Tuer-/Bedienseite ein Stueck weiter)
                var probeCenter = hit.point + rot * (box.center + new Vector3(0f, 0.15f, c.probeForward));
                var probeHalf = new Vector3(half.x + 0.1f, half.y - 0.2f, half.z + c.probeSide);
                if (Physics.CheckBox(probeCenter, probeHalf, rot, solid, QueryTriggerInteraction.Ignore)) continue;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.name = c.name + placed.Count;
                go.transform.SetParent(group, false);
                go.transform.SetPositionAndRotation(hit.point, rot);
                placed.Add(hit.point);
            }

            int parks = c.perPark > 0 ? PlaceInParks(c, prefab, group, scene, solid, ground) : 0;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[{c.name}] {placed.Count} am Gehweg, {parks} in Parks: " + string.Join(", ", placed.Select(p => $"({p.x:0}, {p.z:0})")));
        }

        /// <summary>
        /// In jeden Park (P-Block) perPark Stueck: am Rand des Platzes, Bedienseite zur Mitte. Mit Abstand zu Rails,
        /// Ledges, Treppen und Baenken, damit keine Skate-Linie und kein Sitzplatz zugestellt wird.
        /// </summary>
        static int PlaceInParks(Config c, GameObject prefab, Transform group, UnityEngine.SceneManagement.Scene scene, int solid, int ground)
        {
            var box = prefab.GetComponent<BoxCollider>();
            var half = box.size * 0.5f;
            var rng = new System.Random(c.seed + 7);
            int count = 0;
            Vector3[] sides = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            for (int row = 0; row < CityBuilder.Grid; row++)
                for (int col = 0; col < CityBuilder.Grid; col++)
                {
                    if (CityBuilder.BlockType(row, col) != 'P') continue;
                    var center = new Vector3(CityBuilder.BlockCenter(col), 0f, CityBuilder.BlockCenter(row));
                    var cands = new List<(Vector3 pos, Quaternion rot)>();
                    foreach (var side in sides)
                    {
                        Vector3 along = Vector3.Cross(Vector3.up, side);
                        for (float o = -18f; o <= 18f; o += 4.5f)
                            for (float d = 25f; d >= 19f; d -= 3f)
                            {
                                Vector3 pos = center + side * d + along * o;
                                // Seitlich zur Mitte: Laengsseite (Bedienseite) schaut zum Platz
                                cands.Add((pos, Quaternion.LookRotation(c.alongStreet ? along : -side)));
                            }
                    }
                    int placedHere = 0;
                    foreach (var cd in cands.OrderBy(_ => rng.Next()))
                    {
                        if (placedHere >= c.perPark) break;
                        if (!Physics.Raycast(cd.pos + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 4f, ground, QueryTriggerInteraction.Ignore)) continue;
                        if (hit.normal.y < 0.98f || hit.point.y > 0.4f) continue; // flacher Platzboden, nicht auf Treppe/Ledge
                        // grosszuegig frei: 2,5 m Luft rundum (Skate-Linien), Rails extra
                        var probeCenter = hit.point + cd.rot * (box.center + Vector3.up * 0.15f);
                        if (Physics.CheckBox(probeCenter, half + new Vector3(2.5f, -0.2f, 2.5f), cd.rot, solid, QueryTriggerInteraction.Ignore)) continue;
                        if (Physics.CheckSphere(hit.point, 6f, LayerMask.GetMask("Rail"), QueryTriggerInteraction.Ignore)) continue;

                        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                        go.name = c.name + "_Park" + count;
                        go.transform.SetParent(group, false);
                        go.transform.SetPositionAndRotation(hit.point, cd.rot);
                        Debug.Log($"[{c.name}] Park {row}{col}: ({hit.point.x:0}, {hit.point.z:0})");
                        placedHere++;
                        count++;
                    }
                }
            return count;
        }

        // ------------------------------------------------------------------ Kontrolle

        public static void RenderCheck(Config c)
        {
            Directory.CreateDirectory("Logs/" + c.logFolder);
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var group = GameObject.Find(c.group);
            if (group == null || group.transform.childCount == 0) throw new Exception("Keine Instanzen in der Stadt: " + c.name);
            var cam = Camera.main;

            void Shot(Transform t, Vector3 local, Vector3 look, float fov, string file)
            {
                cam.fieldOfView = fov;
                cam.transform.position = t.TransformPoint(local);
                cam.transform.LookAt(t.TransformPoint(look));
                SimTests.Capture(cam, "Logs/" + c.logFolder + "/prop_" + file + ".png");
            }

            var first = group.transform.GetChild(0);
            Shot(first, new Vector3(0f, 1.6f, 4.2f), new Vector3(0f, 1.3f, 0f), 55f, "front");
            Shot(first, new Vector3(-3.2f, 1.9f, 4f), new Vector3(0f, 1.3f, 0f), 55f, "front_34");
            Shot(first, new Vector3(3.2f, 2.2f, -4f), new Vector3(0f, 1.3f, 0f), 55f, "back_34");
            Shot(first, new Vector3(5f, 1.6f, 0f), new Vector3(0f, 1.3f, 0f), 50f, "side");
            Shot(first, new Vector3(-5f, 1.6f, 0f), new Vector3(0f, 1.3f, 0f), 50f, "side_left");
            Shot(first, new Vector3(0f, 1.8f, 22f), new Vector3(0f, 1.5f, 0f), 50f, "far");

            // Ein paar weitere in ihrer Umgebung, dazu alle in Parks
            for (int i = 1; i < Mathf.Min(group.transform.childCount, 5); i++)
                Shot(group.transform.GetChild(i), new Vector3(-4f, 2.4f, 9f), new Vector3(0f, 1.4f, 0f), 60f, "city_" + i);
            foreach (Transform t in group.transform)
                if (t.name.Contains("_Park"))
                {
                    Shot(t, new Vector3(-2.5f, 2f, 6f), new Vector3(0f, 1.2f, 0f), 55f, "park_" + t.name.Substring(t.name.Length - 1));
                    Shot(t, new Vector3(-3f, 5f, 15f), new Vector3(0f, 0.8f, 0f), 60f, "park_wide_" + t.name.Substring(t.name.Length - 1));
                }
        }
    }
}
