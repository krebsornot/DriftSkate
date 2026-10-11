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
    /// Baut das UFO aus Art/Ufo/ufo_driftskate.glb: Rumpf mit Paletten-Textur, drehender Lichtring (pink und cyan
    /// getrennt zum Blinken), leuchtende Duesen, Kern und Kuppel-Glanz, Aufkleber, Traktorstrahl mit Verlauf
    /// (FX-Shader, Pivot oben, Laenge 1) und ein Leuchtfleck fuer den Boden. Prefab mit Ufo-Komponente.
    /// </summary>
    public static class UfoAssets
    {
        const string SourceGlb = "Art/Ufo/ufo_driftskate.glb";
        const string Root = "Assets/_Game/Ufo";
        const string GlbPath = Root + "/Source/ufo_driftskate.glb";
        const string PrefabPath = Root + "/Ufo.prefab";
        /// <summary>Groesse im Spiel: das Original hat knapp 16 m Durchmesser, im Spiel gut 23 m (fast so breit wie eine Strasse).</summary>
        const float Scale = 1.5f;

        static string MatPath(string n) => Root + "/Materials/" + n + ".mat";
        static string MeshPath(string n) => Root + "/Meshes/" + n + ".asset";

        [MenuItem("DriftSkate/UFO/Prefab bauen")]
        public static void Build()
        {
            GlbBake.EnsureFolders(Root + "/Source", Root + "/Meshes", Root + "/Materials", Root + "/Textures");
            if (!File.Exists(SourceGlb)) throw new Exception("UFO fehlt: " + SourceGlb);
            File.Copy(SourceGlb, GlbPath, true);
            AssetDatabase.ImportAsset(GlbPath, ImportAssetOptions.ForceUpdate);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
            if (src == null) throw new Exception("GLB konnte nicht importiert werden: " + GlbPath);

            var colors = GlbBake.ReadColors(GlbPath); // Kuppel-Glanz hat nur Leuchtfarbe -> weiss
            var names = colors.Keys.Where(n => !n.StartsWith("Decal") && n != "Beam").ToList();

            var inst = Object.Instantiate(src);
            try
            {
                var all = inst.GetComponentsInChildren<Transform>(true);
                var ringNode = all.FirstOrDefault(t => GlbBake.CleanName(t.name) == "Light_Ring");
                if (ringNode == null) throw new Exception("Light_Ring fehlt im Modell");
                Matrix4x4 toFrame = inst.transform.worldToLocalMatrix; // Wurzel = Mitte des UFOs

                var b = new Dictionary<string, GlbBake.MeshBuilder>();
                GlbBake.MeshBuilder B(string n) => b.TryGetValue(n, out var mb) ? mb : b[n] = new GlbBake.MeshBuilder();
                var decals = new Dictionary<string, (GlbBake.MeshBuilder b, Texture tex)>();
                var beam = new GlbBake.MeshBuilder();
                float beamTop = float.MinValue, beamBottom = float.MaxValue;
                var beamParts = new List<(Mesh mesh, int sub, Matrix4x4 m)>();

                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    Matrix4x4 m = toFrame * mf.transform.localToWorldMatrix;
                    bool inRing = mf.transform.IsChildOf(ringNode);
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        var um = mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)];
                        string mat = GlbBake.CleanName(um.name);
                        if (mat.StartsWith("Decal"))
                        {
                            if (!decals.TryGetValue(mat, out var d)) decals[mat] = d = (new GlbBake.MeshBuilder(), GlbBake.FirstTexture(um));
                            d.b.Add(mf.sharedMesh, sub, Vector2.zero, m, keepUv: true);
                            continue;
                        }
                        if (mat == "Beam")
                        {
                            foreach (var v in mf.sharedMesh.vertices)
                            {
                                float y = m.MultiplyPoint3x4(v).y;
                                beamTop = Mathf.Max(beamTop, y);
                                beamBottom = Mathf.Min(beamBottom, y);
                            }
                            beamParts.Add((mf.sharedMesh, sub, m));
                            continue;
                        }
                        if (!colors.ContainsKey(mat)) throw new Exception("Unbekanntes Material " + mat);
                        Vector2 uv = GlbBake.PaletteUv(names.IndexOf(mat));
                        string part;
                        switch (mat)
                        {
                            case "Neon_Pink": part = inRing ? "RingPink" : "Thruster"; break;
                            case "Neon_Cyan": part = inRing ? "RingCyan" : "Core"; break;
                            case "Core_Glow": part = "Core"; break;
                            case "Dome_Highlight": part = "Highlight"; break;
                            default: part = inRing ? "RingBody" : "Body"; break;
                        }
                        B(part).Add(mf.sharedMesh, sub, uv, m);
                    }
                }
                if (beamParts.Count == 0) throw new Exception("Traktorstrahl (Material Beam) fehlt");

                // Strahl: Pivot an der Oberkante, Laenge 1 nach unten (wird im Spiel bis zum Boden gestreckt)
                float beamLen = beamTop - beamBottom;
                Matrix4x4 beamNorm = Matrix4x4.Scale(new Vector3(1f, 1f / beamLen, 1f)) * Matrix4x4.Translate(new Vector3(0f, -beamTop, 0f));
                foreach (var p in beamParts) beam.Add(p.mesh, p.sub, Vector2.zero, beamNorm * p.m, keepUv: true);
                var beamMesh = beam.ToMesh("Ufo_Beam");
                // Verlauf: oben kraeftig, unten fast durchsichtig
                beamMesh.colors = beamMesh.vertices.Select(v => new Color(1f, 1f, 1f, Mathf.Lerp(1f, 0.18f, Mathf.Clamp01(-v.y)))).ToArray();

                var palette = GlbBake.Palette(Root + "/Textures/Ufo_Palette.png", names.Select(n => colors[n]).ToArray());
                var bodyMat = GlbBake.ToonMat(MatPath("Ufo_Body"), Color.white, palette, 0.25f, rim: 0.3f);
                var pinkMat = GlbBake.ToonMat(MatPath("Ufo_Pink"), Color.white, palette, 0f, emission: 1.4f, rim: 0f);
                var cyanMat = GlbBake.ToonMat(MatPath("Ufo_Cyan"), Color.white, palette, 0f, emission: 1.4f, rim: 0f);
                var thrusterMat = GlbBake.ToonMat(MatPath("Ufo_Thruster"), Color.white, palette, 0f, emission: 1.6f, rim: 0f);
                var coreMat = GlbBake.ToonMat(MatPath("Ufo_Core"), Color.white, palette, 0f, emission: 1.6f, rim: 0f);
                var highlightMat = GlbBake.ToonMat(MatPath("Ufo_Highlight"), Color.white, palette, 0f, emission: 0.8f, rim: 0f);

                Color beamCol = colors.TryGetValue("Beam", out var bc) ? bc : new Color(0.4f, 0.95f, 1f);
                var beamMat = FxMat(MatPath("Ufo_Beam"), new Color(beamCol.r, beamCol.g, beamCol.b, 0.3f), null);
                var glowTex = GlowTexture(Root + "/Textures/Ufo_Glow.png");
                var glowMat = FxMat(MatPath("Ufo_Glow"), new Color(beamCol.r, beamCol.g, beamCol.b, 0.75f), glowTex);

                var root = new GameObject("Ufo");
                try
                {
                    var ufo = root.AddComponent<Ufo>();
                    ufo.scale = Scale;

                    var visual = new GameObject("Visual").transform;
                    visual.SetParent(root.transform, false);
                    visual.localScale = Vector3.one * Scale;
                    ufo.visual = visual;

                    Renderer Part(string name, Material mat, Transform parent, bool shadows)
                    {
                        if (!b.TryGetValue(name, out var mb) || mb.Empty) return null;
                        return GlbBake.Part(parent, name, GlbBake.SaveMesh(mb.ToMesh("Ufo_" + name), MeshPath("Ufo_" + name)), mat, shadows);
                    }

                    Part("Body", bodyMat, visual, true);
                    Part("Thruster", thrusterMat, visual, false);
                    ufo.core = Part("Core", coreMat, visual, false);
                    Part("Highlight", highlightMat, visual, false);
                    foreach (var kv in decals)
                    {
                        var dm = GlbBake.ToonMat(MatPath("Ufo_" + kv.Key), Color.white, kv.Value.tex, 0f, cutoff: 0.5f, rim: 0.2f);
                        GlbBake.Part(visual, kv.Key, GlbBake.SaveMesh(kv.Value.b.ToMesh("Ufo_" + kv.Key), MeshPath("Ufo_" + kv.Key)), dm, false);
                    }

                    var ring = new GameObject("Ring").transform; // Lichtring dreht um die Hochachse (Mitte des UFOs)
                    ring.SetParent(visual, false);
                    ufo.ring = ring;
                    Part("RingBody", bodyMat, ring, false);
                    ufo.ringPink = Part("RingPink", pinkMat, ring, false);
                    ufo.ringCyan = Part("RingCyan", cyanMat, ring, false);

                    var beamT = new GameObject("Beam").transform;
                    beamT.SetParent(visual, false);
                    beamT.localPosition = new Vector3(0f, beamTop, 0f);
                    beamT.localScale = new Vector3(1f, beamLen, 1f);
                    GlbBake.Part(beamT, "BeamMesh", GlbBake.SaveMesh(beamMesh, MeshPath("Ufo_Beam")), beamMat, false);
                    ufo.beam = beamT;

                    var glow = new GameObject("GroundGlow").transform;
                    glow.SetParent(root.transform, false);
                    glow.localPosition = new Vector3(0f, -60f, 0f);
                    GlbBake.Part(glow, "Glow", GlbBake.SaveMesh(GlowQuad(), MeshPath("Ufo_Glow")), glowMat, false);
                    ufo.groundGlow = glow;

                    foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    Debug.Log($"[UFO] Prefab gebaut: {PrefabPath} (Teile: {string.Join(", ", b.Keys)}, {decals.Count} Aufkleber, Strahl {beamLen:0.0} m)");
                }
                finally { Object.DestroyImmediate(root); }
            }
            finally { Object.DestroyImmediate(inst); }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Unbeleuchtetes, transparentes Material (DriftSkate/FX, nutzt Vertex-Farben fuer den Verlauf).</summary>
        static Material FxMat(string path, Color color, Texture tex)
        {
            var shader = Shader.Find("DriftSkate/FX");
            if (shader == null) throw new Exception("Shader DriftSkate/FX fehlt");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", tex);
            mat.SetFloat("_FogAmount", 0.5f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Weicher runder Lichtfleck mit etwas hellerem Rand (wo der Strahl auf den Boden trifft).</summary>
        static Texture2D GlowTexture(string path)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float disc = Mathf.Pow(Mathf.Clamp01(1f - r), 1.4f) * 0.6f;
                    float rim = Mathf.Exp(-Mathf.Pow((r - 0.8f) / 0.07f, 2f)) * 0.55f;
                    float a = Mathf.Clamp01(disc + rim) * Mathf.Clamp01((1f - r) * 12f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return GlbBake.SaveTexture(tex, path, FilterMode.Bilinear, true);
        }

        /// <summary>Flaches Quadrat 1 x 1 (liegend), Normalen nach oben.</summary>
        static Mesh GlowQuad()
        {
            var mesh = new Mesh { name = "Ufo_Glow" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            mesh.normals = Enumerable.Repeat(Vector3.up, 4).ToArray();
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.colors = Enumerable.Repeat(Color.white, 4).ToArray();
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        [MenuItem("DriftSkate/UFO/In die Stadt setzen")]
        public static void PlaceInCity()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Build(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); }
            var scene = EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var ufo = Object.FindAnyObjectByType<Ufo>(FindObjectsInactive.Include);
            if (ufo == null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.name = "Ufo";
                ufo = go.GetComponent<Ufo>();
            }
            ufo.transform.position = ufo.PositionAt(0.0, out _);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[UFO] In der Stadt, Tour {ufo.tour.Length} Kreuzungen, Runde {ufo.TourLength:0} s");
        }

        /// <summary>Batch: Prefab bauen, in die Stadt setzen, Kontrollbilder rendern.</summary>
        public static void BatchAll()
        {
            Build();
            PlaceInCity();
            RenderCheck();
        }

        [MenuItem("DriftSkate/UFO/Kontrollbilder rendern (Nacht)")]
        public static void RenderCheck()
        {
            Directory.CreateDirectory("Logs/ufo");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var ufo = Object.FindAnyObjectByType<Ufo>();
            if (ufo == null) throw new Exception("Kein UFO in der Stadt");
            SkyLook.Apply(SkyLook.Find(ufo.nightSky)); // nur fuer die Bilder, die Szene wird nicht gespeichert
            var cam = Camera.main;

            // Schweben ueber der ersten Kreuzung, Strahl an
            ufo.ShowAt(4.0);
            var t = ufo.transform;
            var shots = new (string name, Vector3 local, Vector3 look, float fov)[]
            {
                ("side",      new Vector3(-34f, 2f, -10f),  new Vector3(0f, 0f, 0f),    50f),
                ("above_34",  new Vector3(-22f, 16f, 24f),  new Vector3(0f, 0f, 0f),    50f),
                ("below",     new Vector3(-14f, -16f, 10f), new Vector3(0f, 0f, 0f),    60f),
                ("beam",      new Vector3(-55f, -20f, -40f), new Vector3(0f, -28f, 0f), 55f),
            };
            foreach (var s in shots)
            {
                cam.fieldOfView = s.fov;
                cam.transform.position = t.position + s.local;
                cam.transform.LookAt(t.position + s.look);
                SimTests.Capture(cam, "Logs/ufo/ufo_" + s.name + ".png");
            }

            // Vom Boden: an der Kreuzung unter dem Strahl und von der naechsten Strasse aus
            Vector3 g = new Vector3(t.position.x, 0f, t.position.z);
            var ground = new (string name, double time, Vector3 pos)[]
            {
                ("street",   4.0, g + new Vector3(38f, 1.8f, 6f)),
                ("far",      4.0, g + new Vector3(0f, 1.8f, -150f)),
                ("travel",   ufo.hoverTime + ufo.travelTime * 0.3, g + new Vector3(30f, 1.8f, 20f)),
            };
            foreach (var s in ground)
            {
                ufo.ShowAt(s.time);
                cam.fieldOfView = 60f;
                cam.transform.position = s.pos;
                cam.transform.LookAt(t.position + Vector3.down * 10f);
                SimTests.Capture(cam, "Logs/ufo/ufo_ground_" + s.name + ".png");
            }
        }
    }
}
