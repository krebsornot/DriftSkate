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
    /// Ampeln aus Art/Props/ampel_mast.glb (Mast mit Ausleger und zwei Signalkoepfen) und Art/Props/ampel_fussgaenger.glb
    /// (Fussgaengerampel mit Taster), beide von Fynn (three.js, echte Groesse). Je Modell ein Prefab: Body (Paletten-Textur)
    /// und zwei Licht-Meshes Lamps_X / Lamps_Z, deren UVs in die Signal-Textur von TrafficSignals zeigen; welches aktiv ist,
    /// haengt an der Blickrichtung. Der Mast wird gespiegelt gebacken, damit der Ausleger von der rechten Ecke ueber die
    /// Fahrbahn ragt (Rechtsverkehr).
    /// Verteilt an den 16 inneren Kreuzungen: je Zufahrt eine Mast-Ampel an der rechten Ecke, je Zebrastreifen zwei
    /// Fussgaengerampeln, Gruppe "TrafficLights" in City.unity. Menue: DriftSkate → Ampeln.
    /// </summary>
    public static class TrafficLightAssets
    {
        const string Root = "Assets/_Game/TrafficLights";
        const string Group = "TrafficLights";
        const string Mast = "Ampel_Mast", Ped = "Ampel_Fussgaenger";
        static string Source(string v) => "Art/Props/" + (v == Mast ? "ampel_mast" : "ampel_fussgaenger") + ".glb";
        static string GlbPath(string v) => Root + "/Source/" + Path.GetFileName(Source(v));
        static string PrefabPath(string v) => Root + "/" + v + ".prefab";
        static string MeshPath(string part) => Root + "/Meshes/" + part + ".asset";
        static string MatPath(string part) => Root + "/Materials/TrafficLight_" + part + ".mat";
        static string TexPath(string part) => Root + "/Textures/TrafficLight_" + part + ".png";

        /// <summary>Lampen nach Material; Taster-Leuchte bleibt fest im Body.</summary>
        static readonly Dictionary<string, int> LampOf = new Dictionary<string, int>
        {
            { "Lamp_Red", TrafficSignals.CarRed }, { "Lamp_Amber", TrafficSignals.CarAmber }, { "Lamp_Green", TrafficSignals.CarGreen },
            { "Ped_Red", TrafficSignals.PedRed }, { "Ped_Green", TrafficSignals.PedGreen },
        };

        /// <summary>Lage an der Kreuzung, gemessen von der Kreuzungsmitte (Fahrbahn 24 m, Bordstein bei 12 m).</summary>
        const float MastSide = 13.1f, MastBack = 13.1f, PedSide = 12.75f, PedBack = 16.4f;

        [MenuItem("DriftSkate/Ampeln/Prefabs bauen")] public static void Build() => BuildPrefabs();
        [MenuItem("DriftSkate/Ampeln/In die Stadt setzen")] public static void Place() => PlaceInCity();
        [MenuItem("DriftSkate/Ampeln/Kontrollbilder rendern")] public static void Render() => RenderCheck();

        public static void BatchAll()
        {
            BuildPrefabs();
            PlaceInCity();
        }

        // ------------------------------------------------------------------ Bauen

        public static void BuildPrefabs()
        {
            GlbBake.EnsureFolders(Root + "/Source", Root + "/Meshes", Root + "/Materials", Root + "/Textures");
            var palette = new List<Color>();
            int ColorIndex(Color c)
            {
                int i = palette.FindIndex(x => Mathf.Abs(x.r - c.r) + Mathf.Abs(x.g - c.g) + Mathf.Abs(x.b - c.b) < 0.01f);
                if (i >= 0) return i;
                palette.Add(c);
                return palette.Count - 1;
            }

            var built = new Dictionary<string, (GlbBake.MeshBuilder body, GlbBake.MeshBuilder lx, GlbBake.MeshBuilder lz, Bounds pole)>();
            foreach (var v in new[] { Mast, Ped })
            {
                if (!File.Exists(Source(v))) throw new Exception("Modell fehlt: " + Source(v));
                File.Copy(Source(v), GlbPath(v), true);
                AssetDatabase.ImportAsset(GlbPath(v), ImportAssetOptions.ForceUpdate);
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath(v));
                if (src == null) throw new Exception("GLB konnte nicht importiert werden: " + GlbPath(v));
                var colors = GlbBake.ReadColors(GlbPath(v));
                var inst = Object.Instantiate(src);
                try
                {
                    // glTFast dreht das three.js-Modell so, dass der Ausleger nach -X zeigt: fuer Rechtsverkehr spiegeln
                    Matrix4x4 frame = (v == Mast ? Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)) : Matrix4x4.identity) * inst.transform.worldToLocalMatrix;
                    var body = new GlbBake.MeshBuilder();
                    var lx = new GlbBake.MeshBuilder();
                    var lz = new GlbBake.MeshBuilder();
                    Bounds? pole = null;
                    foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var mr = mf.GetComponent<MeshRenderer>();
                        if (mr == null || mf.sharedMesh == null) continue;
                        string node = GlbBake.CleanName(mf.name);
                        Matrix4x4 m = frame * mf.transform.localToWorldMatrix;
                        if (node.StartsWith("Pole") || node == "PoleBase")
                            foreach (var p in mf.sharedMesh.vertices)
                            {
                                var w = m.MultiplyPoint3x4(p);
                                if (pole.HasValue) { var b = pole.Value; b.Encapsulate(w); pole = b; }
                                else pole = new Bounds(w, Vector3.zero);
                            }
                        for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                        {
                            string mat = GlbBake.CleanName(mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)].name);
                            if (LampOf.TryGetValue(mat, out int lamp) && !node.StartsWith("ButtonPanel"))
                            {
                                lx.Add(mf.sharedMesh, sub, TrafficSignals.LampUv(lamp, 0), m);
                                lz.Add(mf.sharedMesh, sub, TrafficSignals.LampUv(lamp, 1), m);
                                continue;
                            }
                            if (!colors.TryGetValue(mat, out var col)) throw new Exception("Unbekanntes Material " + mat);
                            if (mat == "Ped_Red") col = new Color(1f, 0.16f, 0.12f); // Taster-Leuchte
                            body.Add(mf.sharedMesh, sub, GlbBake.PaletteUv(ColorIndex(col)), m);
                        }
                    }
                    if (!pole.HasValue) throw new Exception("Kein Mast in " + v);
                    built[v] = (body, lx, lz, pole.Value);
                }
                finally { Object.DestroyImmediate(inst); }
            }

            var paletteTex = GlbBake.Palette(TexPath("Palette"), palette.ToArray());
            var bodyMat = GlbBake.ToonMat(MatPath("Body"), Color.white, paletteTex, 0.2f, rim: 0.15f);
            // Signal-Textur in Grundstellung (X Gruen, Z Rot); im Spiel ersetzt TrafficSignals sie durch die laufende Schaltung
            var sig = new Texture2D(TrafficSignals.Columns, 2, TextureFormat.RGBA32, false);
            sig.SetPixels(TrafficSignals.Pixels(0, true, 2, false));
            sig.Apply();
            var sigTex = GlbBake.SaveTexture(sig, TexPath("Signals"), FilterMode.Point, false);
            var lampMat = GlbBake.ToonMat(MatPath("Lamps"), Color.white, sigTex, 0f, 1.8f, rim: 0f);

            int detail = LayerMask.NameToLayer("Detail");
            foreach (var kv in built)
            {
                string v = kv.Key;
                var root = new GameObject(v);
                try
                {
                    // Fussgaengerampeln sind klein: Detail-Layer (ab 120 m aus), ohne Schatten
                    int layer = v == Ped ? detail : -1;
                    GlbBake.Part(root.transform, "Body", GlbBake.SaveMesh(kv.Value.body.ToMesh(v + "_Body"), MeshPath(v + "_Body")), bodyMat, v == Mast, layer);
                    GlbBake.Part(root.transform, TrafficSignals.LampsX, GlbBake.SaveMesh(kv.Value.lx.ToMesh(v + "_LampsX"), MeshPath(v + "_LampsX")), lampMat, false, layer);
                    var z = GlbBake.Part(root.transform, TrafficSignals.LampsZ, GlbBake.SaveMesh(kv.Value.lz.ToMesh(v + "_LampsZ"), MeshPath(v + "_LampsZ")), lampMat, false, layer);
                    z.gameObject.SetActive(false);
                    var pb = kv.Value.pole;
                    var cap = root.AddComponent<CapsuleCollider>();
                    cap.direction = 1;
                    cap.radius = Mathf.Max(pb.extents.x, pb.extents.z) * 0.8f;
                    cap.height = pb.size.y;
                    cap.center = new Vector3(0f, pb.center.y, 0f);
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(v));
                }
                finally { Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Ampeln] Prefabs gebaut ({palette.Count} Farben)");
        }

        // ------------------------------------------------------------------ Verteilen

        public static void PlaceInCity()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(Mast)) == null) BuildPrefabs();
            var mast = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(Mast));
            var ped = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(Ped));
            var scene = EditorSceneManager.OpenScene(ProjectBuilder.CityPath);

            var old = GameObject.Find(Group);
            if (old != null) Object.DestroyImmediate(old);
            Physics.SyncTransforms();
            var group = new GameObject(Group);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(group, scene);
            group.AddComponent<TrafficSignals>().lampMaterial = AssetDatabase.LoadAssetAtPath<Material>(MatPath("Lamps"));

            int solid = ~LayerMask.GetMask("Skater", "Car", "Ignore Raycast");
            int ground = solid & ~LayerMask.GetMask("Rail");
            int masts = 0, peds = 0;
            var skipped = new Dictionary<string, int>();
            void Skip(string why) => skipped[why] = skipped.TryGetValue(why, out int n) ? n + 1 : 1;

            // Freien Platz auf dem Gehweg suchen, notfalls etwas weiter weg von der Kreuzung
            bool Spot(Vector3 pos, Vector3 away, Quaternion rot, Vector3 probeCenter, Vector3 probeHalf, out Vector3 at)
            {
                foreach (float shift in new[] { 0f, 1.2f, 2.4f, 3.6f })
                {
                    Vector3 p = pos + away * shift;
                    at = p;
                    if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 4f, ground, QueryTriggerInteraction.Ignore)) { Skip("kein Boden"); continue; }
                    if (hit.point.y < 0.07f || hit.point.y > 0.25f) { Skip("kein Gehweg"); continue; }
                    at = hit.point;
                    if (Physics.CheckBox(at + rot * probeCenter, probeHalf, rot, solid, QueryTriggerInteraction.Ignore)) { Skip("Platz belegt"); continue; }
                    return true;
                }
                at = pos;
                return false;
            }

            Vector3[] dirs = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            for (int i = 1; i < CityBuilder.Grid; i++)
                for (int j = 1; j < CityBuilder.Grid; j++)
                {
                    var c = new Vector3(CityBuilder.RoadCenter(j), 0f, CityBuilder.RoadCenter(i));
                    var crossing = new GameObject($"Crossing_{i}{j}").transform;
                    crossing.SetParent(group.transform, false);
                    crossing.position = c;
                    foreach (var d in dirs)
                    {
                        // d = Fahrtrichtung der ankommenden Autos, rechts davon die Ecke mit dem Mast
                        Vector3 right = Vector3.Cross(Vector3.up, d);
                        var rot = Quaternion.LookRotation(-d);
                        // Mast (0,3 m) und Ausleger (4,3 m nach links ueber die Fahrbahn, 4,8 bis 6,6 m hoch) frei?
                        if (Spot(c + right * MastSide - d * MastBack, -d, rot, new Vector3(0f, 1.6f, 0f), new Vector3(0.35f, 1.3f, 0.35f), out var mp)
                            && !Physics.CheckBox(mp + rot * new Vector3(2.3f, 5.7f, 0f), new Vector3(2f, 0.9f, 0.4f), rot, solid, QueryTriggerInteraction.Ignore))
                        {
                            var m = (GameObject)PrefabUtility.InstantiatePrefab(mast, scene);
                            m.transform.SetParent(crossing, false);
                            m.transform.SetPositionAndRotation(mp, rot);
                            Physics.SyncTransforms();
                            masts++;
                        }
                        // Fussgaengerampeln an beiden Enden des Zebrastreifens vor der Zufahrt, Blick ueber die Strasse
                        foreach (int s in new[] { -1, 1 })
                        {
                            var prot = Quaternion.LookRotation(-right * s);
                            if (!Spot(c + right * (s * PedSide) - d * PedBack, -d, prot, new Vector3(0f, 1.5f, 0.1f), new Vector3(0.25f, 1.2f, 0.25f), out var pp)) continue;
                            var p = (GameObject)PrefabUtility.InstantiatePrefab(ped, scene);
                            p.transform.SetParent(crossing, false);
                            p.transform.SetPositionAndRotation(pp, prot);
                            Physics.SyncTransforms();
                            peds++;
                        }
                    }
                }
            TrafficSignals.ApplyGroups(group.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Ampeln] {masts} Mast-Ampeln und {peds} Fussgaengerampeln an 16 Kreuzungen | Pruefungen: " +
                      string.Join(", ", skipped.Select(kv => kv.Key + " " + kv.Value)));
        }

        // ------------------------------------------------------------------ Kontrolle

        public static void RenderCheck()
        {
            Directory.CreateDirectory("Logs/ampeln");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var group = GameObject.Find(Group);
            if (group == null) throw new Exception("Keine Ampeln in der Stadt");
            var cam = Camera.main;
            void Shot(Vector3 pos, Vector3 look, float fov, string file)
            {
                cam.fieldOfView = fov;
                cam.transform.position = pos;
                cam.transform.LookAt(look);
                SimTests.Capture(cam, "Logs/ampeln/" + file + ".png");
            }
            foreach (var name in new[] { "Crossing_22", "Crossing_13" })
            {
                var t = group.transform.Find(name);
                if (t == null) continue;
                Vector3 c = t.position;
                Debug.Log($"[Ampeln] {name}: {t.childCount} Ampeln");
                // aus Fahrersicht auf der rechten Spur (Richtung +Z), dann schraeg von oben
                Shot(c + new Vector3(4.2f, 1.4f, -45f), c + new Vector3(0f, 4f, 0f), 50f, name + "_driver");
                Shot(c + new Vector3(-30f, 22f, -30f), c, 55f, name + "_overview");
                Shot(c + new Vector3(16f, 1.7f, -21f), c + new Vector3(12.75f, 2.2f, -16.4f), 55f, name + "_ped");
            }
        }
    }
}
