using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Baut den DRIFT x SKATE-Zeppelin aus Art/Zeppelin/Zeppelin_DriftXSkate.obj (+ .mtl):
    /// eigene OBJ-Einlese (rechtshaendig -> Unity: X gespiegelt, Dreiecke umgedreht), Teile nach Funktion
    /// zusammengefasst (wenige Draw Calls), Toon-Materialien aus den MTL-Farben, Graffiti- und Sticker-Texturen
    /// im Code erzeugt (Schrift: Sedgwick Ave / Permanent Marker), Prefab mit Zeppelin-Komponente.
    /// </summary>
    public static class ZeppelinAssets
    {
        const string Source = "Art/Zeppelin/Zeppelin_DriftXSkate.obj";
        const string Root = "Assets/_Game/Zeppelin";
        const string PrefabPath = Root + "/Zeppelin.prefab";
        const string FontDir = "Assets/_Game/Resources/Fonts/";

        class ObjPart
        {
            public string name, mat;
            public readonly List<int[]> faces = new List<int[]>(); // je Ecke: v, vt, vn (0-basiert)
        }

        // ---------------------------------------------------------------- Menue

        [MenuItem("DriftSkate/Zeppelin/Prefab bauen")]
        public static void Build()
        {
            if (!File.Exists(Source)) throw new Exception("Zeppelin-OBJ fehlt: " + Source);
            Directory.CreateDirectory(Root + "/Textures");
            Directory.CreateDirectory(Root + "/Materials");
            Directory.CreateDirectory(Root + "/Meshes");

            var colors = ReadMtl(Path.ChangeExtension(Source, ".mtl"));
            ReadObj(Source, out var v, out var vt, out var vn, out var parts);
            var textures = BuildTextures();
            var mats = new Dictionary<string, Material>();
            foreach (var kv in colors) mats[kv.Key] = MakeMaterial(kv.Key, kv.Value, textures);

            var root = new GameObject("Zeppelin");
            try
            {
                var zep = root.AddComponent<Zeppelin>();
                Vector3 Center(string prefix)
                {
                    var b = PartBounds(parts.Where(p => p.name.StartsWith(prefix)), v);
                    return b.center;
                }

                // Pivots (Unity-Raum): Propeller-Naben, Ruder-Scharnier, Wimpelmast
                Vector3 propL = Center("Propeller_L_Spinner"), propR = Center("Propeller_R_Spinner");
                var hinge = new Vector3(0f, 0f, -29.48f);
                var mast = new Vector3(0f, 12.8f, -27.4f);

                var groups = new (string name, Func<string, bool> match, Vector3 pivot, bool shadows)[]
                {
                    ("PropellerL", n => n.StartsWith("Propeller_L_"), propL, false),
                    ("PropellerR", n => n.StartsWith("Propeller_R_"), propR, false),
                    ("Rudder", n => n.StartsWith("Rudder_"), hinge, false),
                    ("Elevator", n => n.StartsWith("Elevator_"), hinge, false),
                    ("Pennant", n => n == "Pennant_Flag" || n == "Pennant_Stripe", mast, false),
                    ("Neon", n => n.StartsWith("Neon_"), Vector3.zero, false),
                    ("NavLights", n => n.StartsWith("Light_Position_"), Vector3.zero, false),
                    ("TailLight", n => n == "Light_Tail_White", Vector3.zero, false),
                    ("Windows", n => n.StartsWith("Window_"), Vector3.zero, false),
                    ("Decals", n => n.StartsWith("Decal_") || n.StartsWith("Sticker_"), Vector3.zero, false),
                    ("Body", n => true, Vector3.zero, true),
                };
                var used = new HashSet<ObjPart>();
                var made = new Dictionary<string, Renderer>();
                foreach (var g in groups)
                {
                    var members = parts.Where(p => !used.Contains(p) && g.match(p.name)).ToList();
                    if (members.Count == 0) throw new Exception("Zeppelin-Teil fehlt: " + g.name);
                    foreach (var m in members) used.Add(m);
                    var go = new GameObject(g.name);
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = g.pivot;
                    var order = members.Select(m => m.mat).Distinct().ToList();
                    var mesh = BuildMesh(g.name, members, order, v, vt, vn, g.pivot);
                    mesh = SaveMesh(mesh, Root + "/Meshes/Zeppelin_" + g.name + ".asset");
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterials = order.Select(n => mats[n]).ToArray();
                    r.shadowCastingMode = g.shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    r.receiveShadows = g.name == "Body";
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    made[g.name] = r;
                }

                zep.propLeft = made["PropellerL"].transform;
                zep.propRight = made["PropellerR"].transform;
                zep.rudder = made["Rudder"].transform;
                zep.elevator = made["Elevator"].transform;
                zep.pennant = made["Pennant"].transform;
                zep.neon = made["Neon"];
                zep.navLights = made["NavLights"];
                zep.tailLight = made["TailLight"];
                zep.windows = made["Windows"];
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("[Zeppelin] Prefab gebaut: " + PrefabPath + $" ({parts.Count} OBJ-Teile)");
        }

        [MenuItem("DriftSkate/Zeppelin/In die Stadt setzen")]
        public static void PlaceInCity()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Build(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); }
            var scene = EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var zep = Object.FindAnyObjectByType<Zeppelin>(FindObjectsInactive.Include);
            if (zep == null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.name = "Zeppelin";
                zep = go.GetComponent<Zeppelin>();
            }
            zep.transform.position = zep.PointAt(zep.startOffset);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Zeppelin] In der Stadt, Route {zep.RouteLength:0} m, Runde {zep.RouteLength / zep.speed:0} s");
        }

        /// <summary>Batch: Prefab bauen, in die Stadt setzen, Kontrollbilder rendern.</summary>
        public static void BatchAll()
        {
            Build();
            PlaceInCity();
            RenderCheck();
        }

        [MenuItem("DriftSkate/Zeppelin/Kontrollbilder rendern")]
        public static void RenderCheck()
        {
            Directory.CreateDirectory("Logs/zeppelin");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var zep = Object.FindAnyObjectByType<Zeppelin>();
            if (zep == null) throw new Exception("Kein Zeppelin in der Stadt");
            var cam = Camera.main;
            float total = zep.RouteLength;

            // Nahaufnahmen in der Luft (Himmel als Hintergrund)
            zep.SetRoutePosition(total * 0.1f);
            var t = zep.transform;
            var shots = new (string name, Vector3 local, Vector3 look, float fov)[]
            {
                ("side_left",   new Vector3(-62f, -4f, 2f),  new Vector3(0f, -2f, 0f), 50f),
                ("side_right",  new Vector3(62f, -4f, 2f),   new Vector3(0f, -2f, 0f), 50f),
                ("front_34",    new Vector3(-38f, -14f, 52f), new Vector3(0f, -3f, 0f), 50f),
                ("rear_34",     new Vector3(30f, 8f, -55f),  new Vector3(0f, 0f, -5f), 50f),
                ("gondola",     new Vector3(-14f, -16f, 22f), new Vector3(0f, -9f, 6f), 50f),
                ("tail",        new Vector3(-14f, 6f, -46f), new Vector3(0f, 2f, -28f), 50f),
            };
            foreach (var s in shots)
            {
                cam.fieldOfView = s.fov;
                cam.transform.position = t.TransformPoint(s.local);
                cam.transform.LookAt(t.TransformPoint(s.look));
                SimTests.Capture(cam, "Logs/zeppelin/zep_" + s.name + ".png");
            }

            // Vom Boden aus: aus der Stadt nach oben geschaut, an mehreren Stellen der Route
            float B(int i) => CityBuilder.BlockCenter(i);
            float R(int k) => CityBuilder.RoadCenter(k);
            var ground = new (string name, float at, Vector3 pos)[]
            {
                ("street_a", 0.05f, new Vector3(B(2) - 20f, 1.8f, R(2) + 4f)),
                ("street_b", 0.30f, new Vector3(B(1), 1.8f, R(1) - 4f)),
                ("plaza",    0.55f, new Vector3(B(2) + 20f, 2.2f, B(2) - 20f)),
                ("street_c", 0.80f, new Vector3(R(3), 1.8f, B(3))),
            };
            foreach (var g in ground)
            {
                zep.SetRoutePosition(total * g.at);
                cam.fieldOfView = 65f;
                cam.transform.position = g.pos;
                cam.transform.LookAt(zep.transform.position + Vector3.down * 4f);
                SimTests.Capture(cam, "Logs/zeppelin/zep_ground_" + g.name + ".png");
            }
        }

        // ---------------------------------------------------------------- OBJ / MTL

        static Dictionary<string, Color> ReadMtl(string path)
        {
            var result = new Dictionary<string, Color>();
            string cur = null;
            foreach (var raw in File.ReadAllLines(path))
            {
                var p = raw.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                if (p[0] == "newmtl") cur = p[1];
                else if (p[0] == "Kd" && cur != null)
                    result[cur] = new Color(F(p[1]), F(p[2]), F(p[3])).gamma; // MTL-Farben sind linear
            }
            return result;
        }

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        static void ReadObj(string path, out List<Vector3> v, out List<Vector2> vt, out List<Vector3> vn, out List<ObjPart> parts)
        {
            v = new List<Vector3>(); vt = new List<Vector2>(); vn = new List<Vector3>(); parts = new List<ObjPart>();
            ObjPart cur = null;
            foreach (var raw in File.ReadLines(path))
            {
                var p = raw.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                switch (p[0])
                {
                    // Rechtshaendig (three.js) -> Unity: X spiegeln
                    case "v": v.Add(new Vector3(-F(p[1]), F(p[2]), F(p[3]))); break;
                    case "vt": vt.Add(new Vector2(F(p[1]), F(p[2]))); break;
                    case "vn": vn.Add(new Vector3(-F(p[1]), F(p[2]), F(p[3]))); break;
                    case "o": cur = new ObjPart { name = p[1] }; parts.Add(cur); break;
                    case "usemtl": cur.mat = p[1]; break;
                    case "f":
                        var corners = p.Skip(1).Select(c =>
                        {
                            var a = c.Split('/');
                            return new[] { int.Parse(a[0]) - 1, a.Length > 1 && a[1] != "" ? int.Parse(a[1]) - 1 : -1, a.Length > 2 ? int.Parse(a[2]) - 1 : -1 };
                        }).ToArray();
                        for (int i = 1; i + 1 < corners.Length; i++)
                        {
                            // Gespiegelt -> Umlaufsinn umdrehen
                            cur.faces.Add(corners[0]); cur.faces.Add(corners[i + 1]); cur.faces.Add(corners[i]);
                        }
                        break;
                }
            }
        }

        static Bounds PartBounds(IEnumerable<ObjPart> parts, List<Vector3> v)
        {
            bool first = true;
            var b = new Bounds();
            foreach (var p in parts)
                foreach (var c in p.faces)
                {
                    if (first) { b = new Bounds(v[c[0]], Vector3.zero); first = false; }
                    else b.Encapsulate(v[c[0]]);
                }
            return b;
        }

        static Mesh BuildMesh(string name, List<ObjPart> members, List<string> order, List<Vector3> v, List<Vector2> vt, List<Vector3> vn, Vector3 pivot)
        {
            var pos = new List<Vector3>(); var uv = new List<Vector2>(); var nrm = new List<Vector3>();
            var subs = new List<List<int>>();
            var lookup = new Dictionary<(int, int, int), int>();
            foreach (var mat in order)
            {
                var tris = new List<int>();
                foreach (var part in members.Where(m => m.mat == mat))
                    foreach (var c in part.faces)
                    {
                        var key = (c[0], c[1], c[2]);
                        if (!lookup.TryGetValue(key, out int idx))
                        {
                            idx = pos.Count;
                            pos.Add(v[c[0]] - pivot);
                            uv.Add(c[1] >= 0 ? vt[c[1]] : Vector2.zero);
                            nrm.Add(c[2] >= 0 ? vn[c[2]].normalized : Vector3.up);
                            lookup[key] = idx;
                        }
                        tris.Add(idx);
                    }
                subs.Add(tris);
                lookup.Clear(); // jede Teilmenge eigene Vertices (saubere Normalen pro Material)
            }
            var mesh = new Mesh { name = "Zeppelin_" + name };
            if (pos.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(pos); mesh.SetUVs(0, uv); mesh.SetNormals(nrm);
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            foreach (var n in mesh.normals)
                if (float.IsNaN(n.x) || n.sqrMagnitude < 0.5f) throw new Exception("Zeppelin-Mesh " + name + " hat kaputte Normalen");
            return mesh;
        }

        static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        static Material MakeMaterial(string name, Color color, Dictionary<string, Texture2D> textures)
        {
            string path = Root + "/Materials/Zep_" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(ToonMaterials.Shader) { name = "Zep_" + name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = ToonMaterials.Shader;
            mat.enableInstancing = true;
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", null);
            mat.SetFloat("_Emission", 0f);
            mat.SetFloat("_Cutoff", 0f);
            mat.SetColor("_ShadowTint", new Color(0.6f, 0.54f, 0.8f));
            mat.SetFloat("_RimStrength", 0.25f);

            float outline = 0f;
            bool box = false;
            switch (name)
            {
                case "Hull_Sand": outline = 0.6f; mat.SetFloat("_RimStrength", 0.35f); break;
                case "Rib_VioletGrey": outline = 0.3f; break;
                case "Brass_Patina": case "Copper_Patina": outline = 0.25f; mat.SetFloat("_RimStrength", 0.45f); break;
                case "Window_Emissive": mat.SetFloat("_Emission", 1.6f); break;
                case "Neon_Cyan": case "Neon_Pink": case "Neon_Violet": mat.SetFloat("_Emission", 1.6f); break;
                case "Light_Red": case "Light_Green": case "Light_White": mat.SetFloat("_Emission", 2.4f); break;
                case "Pennant_Pink": mat.SetFloat("_Cull", 0f); break;
            }
            if (name == "Neon_Cyan") mat.SetFloat("_Cull", 0f);
            if (textures.TryGetValue(name, out var tex))
            {
                mat.SetColor("_BaseColor", Color.white);
                mat.SetTexture("_BaseMap", tex);
                mat.SetFloat("_Cutoff", 0.5f);
                mat.SetFloat("_RimStrength", 0.15f);
            }
            mat.SetFloat("_OutlineWidth", outline);
            mat.SetFloat("_OutlineMode", box ? 1f : 0f);
            mat.SetShaderPassEnabled("SRPDefaultUnlit", outline > 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------------------------------------------------------------- Texturen

        static readonly Color Ink = new Color(0.07f, 0.04f, 0.11f);
        static readonly Color Pink = new Color(1f, 0.25f, 0.6f);
        static readonly Color Violet = new Color(0.55f, 0.3f, 1f);
        static readonly Color Cyan = new Color(0.2f, 0.92f, 1f);
        static readonly Color Lemon = new Color(1f, 0.9f, 0.25f);

        static Dictionary<string, Texture2D> BuildTextures()
        {
            var sedgwick = AssetDatabase.LoadAssetAtPath<Font>(FontDir + "SedgwickAve.ttf");
            var marker = AssetDatabase.LoadAssetAtPath<Font>(FontDir + "PermanentMarker.ttf");
            if (sedgwick == null || marker == null) throw new Exception("Graffiti-Schriften fehlen in " + FontDir);
            return new Dictionary<string, Texture2D>
            {
                ["Decal_Graffiti"] = Save("Zep_Graffiti", GraffitiPiece(sedgwick)),
                ["Decal_Badge"] = Save("Zep_Badge", Badge(marker)),
                ["Decal_NoBrakes"] = Save("Zep_NoBrakes", TextSticker(marker, "NO BRAKES", 512, 256, Pink, Color.white, Ink)),
                ["Decal_Grind"] = Save("Zep_Grind", TextSticker(marker, "GRIND!", 512, 256, Cyan, Ink, Ink)),
                ["Decal_Star"] = Save("Zep_Star", StarSticker()),
                ["Decal_Checker"] = Save("Zep_Checker", Checker()),
            };
        }

        static Texture2D Save(string name, Canvas c)
        {
            string path = Root + "/Textures/" + name + ".png";
            var tex = new Texture2D(c.w, c.h, TextureFormat.RGBA32, false);
            tex.SetPixels(c.px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.mipMapsPreserveCoverage = true;
            imp.alphaTestReferenceValue = 0.5f;
            imp.anisoLevel = 4;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        class Canvas
        {
            public readonly int w, h;
            public readonly Color[] px;
            public Canvas(int w, int h) { this.w = w; this.h = h; px = new Color[w * h]; }
            /// <summary>Malt Farbe (oder Farbfunktion) ueberall, wo die Maske > 0.5 ist.</summary>
            public void Fill(float[] mask, Func<int, int, Color> color)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (mask[i] <= 0.5f) continue;
                        var c = color(x, y); c.a = 1f;
                        px[i] = c;
                    }
            }
            public void Fill(float[] mask, Color color) => Fill(mask, (x, y) => color);
        }

        /// <summary>Text als Graustufen-Maske (0..1), auf w x h eingepasst (Rand margin), optional schraeg.</summary>
        static float[] TextMask(Font font, string text, int w, int h, float margin, float slant = 0f, float yShift = 0f)
        {
            const int size = 200;
            font.RequestCharactersInTexture(text, size, FontStyle.Normal);
            var atlas = font.material.mainTexture;
            var rt = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(atlas, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, false, true);
            read.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            // Je nach Atlas-Format steckt die Deckung im Alpha- oder im Farbkanal: den Kanal mit Kontrast nehmen
            var all = read.GetPixels();
            float aMin = all.Min(p => p.a), aMax = all.Max(p => p.a);
            float rMin = all.Min(p => p.r), rMax = all.Max(p => p.r);
            bool useAlpha = aMax - aMin >= rMax - rMin;

            // Glyphen nebeneinander in eine Roh-Maske setzen
            var infos = text.Select(ch => { font.GetCharacterInfo(ch, out var ci, size); return ci; }).ToArray();
            int minY = infos.Min(ci => ci.minY), maxY = infos.Max(ci => ci.maxY);
            int rawW = infos.Sum(ci => ci.advance) + size / 2, rawH = maxY - minY + 8;
            var raw = new float[rawW * rawH];
            int pen = size / 4;
            foreach (var ci in infos)
            {
                int gw = ci.glyphWidth, gh = ci.glyphHeight;
                for (int gy = 0; gy < gh; gy++)
                    for (int gx = 0; gx < gw; gx++)
                    {
                        float fx = (gx + 0.5f) / gw, fy = (gy + 0.5f) / gh;
                        Vector2 uv = Vector2.Lerp(Vector2.Lerp(ci.uvBottomLeft, ci.uvBottomRight, fx), Vector2.Lerp(ci.uvTopLeft, ci.uvTopRight, fx), fy);
                        var c = read.GetPixelBilinear(uv.x, uv.y);
                        float a = useAlpha ? c.a : c.r;
                        int x = pen + ci.minX + gx, y = ci.minY - minY + 4 + gy;
                        if (x >= 0 && x < rawW && y >= 0 && y < rawH) raw[y * rawW + x] = Mathf.Max(raw[y * rawW + x], a);
                    }
                pen += ci.advance;
            }
            Object.DestroyImmediate(read);

            // Ausdehnung der Tinte ermitteln und in das Ziel einpassen
            int x0 = rawW, x1 = 0, y0 = rawH, y1 = 0;
            for (int y = 0; y < rawH; y++)
                for (int x = 0; x < rawW; x++)
                    if (raw[y * rawW + x] > 0.5f) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
            if (x1 <= x0) throw new Exception("Schrift lieferte keine Pixel fuer '" + text + "' (Font-Atlas leer?)");
            float inkW = x1 - x0 + 1, inkH = y1 - y0 + 1;
            float availW = w * (1f - 2f * margin) - slant * h, availH = h * (1f - 2f * margin);
            float scale = Mathf.Min(availW / inkW, availH / inkH);
            float offX = (w - inkW * scale - slant * h * 0.5f) * 0.5f, offY = (h - inkH * scale) * 0.5f + yShift * h;
            var mask = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sx = (x - offX - slant * (y - h * 0.5f)) / scale + x0, sy = (y - offY) / scale + y0;
                    mask[y * w + x] = SampleBilinear(raw, rawW, rawH, sx, sy);
                }
            return mask;
        }

        static float SampleBilinear(float[] m, int w, int h, float x, float y)
        {
            x -= 0.5f; y -= 0.5f;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            float At(int a, int b) => a < 0 || b < 0 || a >= w || b >= h ? 0f : m[b * w + a];
            return Mathf.Lerp(Mathf.Lerp(At(ix, iy), At(ix + 1, iy), fx), Mathf.Lerp(At(ix, iy + 1), At(ix + 1, iy + 1), fx), fy);
        }

        /// <summary>Abstand (Pixel) jedes Pixels zur Maske, Chamfer-Naeherung 3-4.</summary>
        static float[] Distance(float[] mask, int w, int h)
        {
            var d = new float[w * h];
            const float big = 1e6f, a = 1f, b = 1.4142f;
            for (int i = 0; i < d.Length; i++) d[i] = mask[i] > 0.5f ? 0f : big;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x; float v = d[i];
                    if (x > 0) v = Mathf.Min(v, d[i - 1] + a);
                    if (y > 0)
                    {
                        v = Mathf.Min(v, d[i - w] + a);
                        if (x > 0) v = Mathf.Min(v, d[i - w - 1] + b);
                        if (x < w - 1) v = Mathf.Min(v, d[i - w + 1] + b);
                    }
                    d[i] = v;
                }
            for (int y = h - 1; y >= 0; y--)
                for (int x = w - 1; x >= 0; x--)
                {
                    int i = y * w + x; float v = d[i];
                    if (x < w - 1) v = Mathf.Min(v, d[i + 1] + a);
                    if (y < h - 1)
                    {
                        v = Mathf.Min(v, d[i + w] + a);
                        if (x < w - 1) v = Mathf.Min(v, d[i + w + 1] + b);
                        if (x > 0) v = Mathf.Min(v, d[i + w - 1] + b);
                    }
                    d[i] = v;
                }
            return d;
        }

        static float[] Grow(float[] mask, int w, int h, float r)
        {
            var d = Distance(mask, w, h);
            return d.Select(x => x <= r ? 1f : 0f).ToArray();
        }

        static float[] Shift(float[] mask, int w, int h, int dx, int dy)
        {
            var o = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int sx = x - dx, sy = y - dy;
                    if (sx >= 0 && sy >= 0 && sx < w && sy < h) o[y * w + x] = mask[sy * w + sx];
                }
            return o;
        }

        static Color Rainbow(float t)
        {
            // Sandevistan-Verlauf: Pink -> Violett -> Cyan -> Zitrone
            t = Mathf.Clamp01(t) * 3f;
            if (t < 1f) return Color.Lerp(Pink, Violet, t);
            if (t < 2f) return Color.Lerp(Violet, Cyan, t - 1f);
            return Color.Lerp(Cyan, Lemon, t - 2f);
        }

        /// <summary>Grosses "DRIFT x SKATE"-Piece: 3D-Block, dicke Outline, Regenbogen-Fuellung, Glanzkante, Nasen.</summary>
        static Canvas GraffitiPiece(Font font)
        {
            const int w = 2048, h = 512;
            var c = new Canvas(w, h);
            var text = TextMask(font, "DRIFT x SKATE", w, h, 0.09f, slant: 0.12f, yShift: 0.03f);
            var outline = Grow(text, w, h, 13f);
            // 3D-Block nach rechts unten
            var block = new float[w * h];
            for (int k = 1; k <= 20; k++)
            {
                var s = Shift(outline, w, h, k, -k);
                for (int i = 0; i < block.Length; i++) block[i] = Mathf.Max(block[i], s[i]);
            }
            var blockOutline = Grow(block, w, h, 5f);

            // Nasen (Drips) unter der Fuellung
            var drips = new float[w * h];
            var rng = new System.Random(2042);
            for (int n = 0; n < 26; n++)
            {
                int x = rng.Next(120, w - 120);
                int top = -1;
                for (int y = h - 1; y >= 0; y--) if (text[y * w + x] > 0.5f) top = y; // tiefster Tintenpunkt
                if (top < 0) continue;
                int len = rng.Next(25, 95);
                float rad = 4.5f + (float)rng.NextDouble() * 3f;
                for (int y = top; y > top - len && y > 0; y--)
                    for (int dx = -6; dx <= 6; dx++)
                    {
                        float r = (y == top - len + 1) ? rad * 1.5f : rad;
                        if (Mathf.Abs(dx) <= r && x + dx >= 0 && x + dx < w) drips[y * w + x + dx] = 1f;
                    }
                // runde Tropfenspitze
                int cy = top - len;
                for (int yy = -9; yy <= 9; yy++)
                    for (int xx = -9; xx <= 9; xx++)
                        if (xx * xx + yy * yy <= rad * rad * 2.2f && cy + yy > 0 && cy + yy < h && x + xx >= 0 && x + xx < w)
                            drips[(cy + yy) * w + x + xx] = 1f;
            }
            var dripOutline = Grow(drips, w, h, 6f);

            c.Fill(blockOutline, Ink);
            c.Fill(block, (x, y) => Color.Lerp(new Color(0.24f, 0.1f, 0.4f), new Color(0.12f, 0.06f, 0.26f), y / (float)h));
            c.Fill(outline, Ink);
            c.Fill(dripOutline, Ink);
            c.Fill(drips, (x, y) => Rainbow(x / (float)w) * 0.92f);
            c.Fill(text, (x, y) => Color.Lerp(Rainbow(x / (float)w + (y / (float)h - 0.5f) * 0.12f), Color.white, y > h * 0.62f ? 0.18f : 0f));
            // Glanzkante: schmales Band innen an der oberen linken Kante jedes Buchstabens
            var shine = new float[w * h];
            var far = Shift(text, w, h, 8, -8);
            var near = Shift(text, w, h, 3, -3);
            for (int i = 0; i < shine.Length; i++) shine[i] = text[i] > 0.5f && near[i] > 0.5f && far[i] < 0.5f ? 1f : 0f;
            c.Fill(shine, new Color(1f, 1f, 1f));
            // Funkelsterne
            foreach (var p in new[] { new Vector2(0.06f, 0.8f), new Vector2(0.52f, 0.86f), new Vector2(0.95f, 0.22f) })
                Sparkle(c, (int)(p.x * w), (int)(p.y * h), 34);
            return c;
        }

        static void Sparkle(Canvas c, int cx, int cy, int r)
        {
            var m = new float[c.w * c.h];
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    float ax = Mathf.Abs(x) / (float)r, ay = Mathf.Abs(y) / (float)r;
                    if (Mathf.Sqrt(ax) + Mathf.Sqrt(ay) > 1f) continue;
                    int px = cx + x, py = cy + y;
                    if (px >= 0 && py >= 0 && px < c.w && py < c.h) m[py * c.w + px] = 1f;
                }
            c.Fill(Grow(m, c.w, c.h, 4f), Ink);
            c.Fill(m, Color.white);
        }

        static Canvas Badge(Font font)
        {
            const int s = 512;
            var c = new Canvas(s, s);
            float[] Disc(float r) => Enumerable.Range(0, s * s).Select(i =>
            {
                float x = i % s + 0.5f - s / 2f, y = i / s + 0.5f - s / 2f;
                return x * x + y * y <= r * r ? 1f : 0f;
            }).ToArray();
            c.Fill(Disc(250f), Ink);
            c.Fill(Disc(236f), (x, y) => Rainbow(1f - y / (float)s));
            c.Fill(Disc(206f), Ink);
            c.Fill(Disc(196f), new Color(0.16f, 0.09f, 0.3f));
            var text = TextMask(font, "DxS", s, s, 0.24f, slant: 0.08f);
            c.Fill(Grow(text, s, s, 9f), Ink);
            c.Fill(text, (x, y) => Color.Lerp(Pink, Lemon, y / (float)s));
            Sparkle(c, 360, 380, 26);
            return c;
        }

        static Canvas TextSticker(Font font, string label, int w, int h, Color bg, Color fg, Color ink)
        {
            var c = new Canvas(w, h);
            float[] Rounded(float inset, float rad) => Enumerable.Range(0, w * h).Select(i =>
            {
                float x = i % w + 0.5f, y = i / w + 0.5f;
                float dx = Mathf.Max(Mathf.Max(inset + rad - x, x - (w - inset - rad)), 0f);
                float dy = Mathf.Max(Mathf.Max(inset + rad - y, y - (h - inset - rad)), 0f);
                return dx * dx + dy * dy <= rad * rad ? 1f : 0f;
            }).ToArray();
            c.Fill(Rounded(4f, 46f), ink);
            c.Fill(Rounded(16f, 34f), bg);
            c.Fill(Rounded(16f, 34f), (x, y) => y > h * 0.55f ? Color.Lerp(bg, Color.white, 0.15f) : bg);
            var text = TextMask(font, label, w, h, 0.16f, slant: 0.06f);
            c.Fill(Grow(text, w, h, 6f), ink);
            c.Fill(text, fg);
            return c;
        }

        static Canvas StarSticker()
        {
            const int s = 512;
            var c = new Canvas(s, s);
            var star = new float[s * s];
            for (int i = 0; i < star.Length; i++)
            {
                float x = i % s + 0.5f - s / 2f, y = i / s + 0.5f - s / 2f;
                float ang = Mathf.Atan2(y, x) + Mathf.PI / 2f;
                float k = Mathf.Repeat(ang, Mathf.PI * 2f / 5f) / (Mathf.PI * 2f / 5f); // 0..1 je Zacke
                float rad = Mathf.Lerp(92f, 208f, 1f - Mathf.Abs(k - 0.5f) * 2f); // Spitze bei k = 0.5 zeigt nach oben
                star[i] = Mathf.Sqrt(x * x + y * y) <= rad ? 1f : 0f;
            }
            c.Fill(Grow(star, s, s, 16f), Ink);
            c.Fill(star, (x, y) => Color.Lerp(new Color(1f, 0.62f, 0.15f), Lemon, y / (float)s));
            var inner = new float[s * s];
            for (int i = 0; i < inner.Length; i++)
            {
                float x = i % s - s * 0.42f, y = i / s - s * 0.6f;
                inner[i] = star[i] > 0.5f && x * x * 0.5f + y * y < 900f ? 1f : 0f;
            }
            c.Fill(inner, Color.white);
            return c;
        }

        static Canvas Checker()
        {
            const int w = 512, h = 128;
            var c = new Canvas(w, h);
            var all = new float[w * h];
            for (int i = 0; i < all.Length; i++) all[i] = 1f;
            c.Fill(all, Ink);
            c.Fill(all, (x, y) =>
            {
                if (x < 10 || x >= w - 10 || y < 10 || y >= h - 10) return Ink;
                int cx = (x - 10) / 27, cy = (y - 10) / 27;
                return ((cx + cy) & 1) == 0 ? Color.white : Ink;
            });
            return c;
        }
    }
}
