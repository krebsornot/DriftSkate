using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Schulbus "Swagger Bus" aus Art/Props/bus_swagger.glb (Claude 3D Design) als NPC-Fahrzeug fuer Traffic:
    /// Karosserie ueber die Paletten-Textur, Scheiben ohne Umriss, Scheinwerfer leuchtend, Ruecklichter umschaltbar
    /// (Bremslicht), vier Raeder als eigene Drehpunkte, das Stopp-Schild als Klappteil (BusStopArm).
    /// Echter Rigidbody wie die NPC-Autos, aber schwer (9 t): rammt man ihn, schiebt man sich eher selbst weg.
    /// Ergebnis: Resources/Vehicles/SchoolBus. Menue: DriftSkate → Verkehr → Schulbus bauen.
    /// </summary>
    public static class SchoolBusAssets
    {
        const string Root = "Assets/_Game/SchoolBus";
        const string SourceGlb = "Art/Props/bus_swagger.glb";
        const string GlbPath = Root + "/Source/bus_swagger.glb";
        static string PrefabPath => "Assets/_Game/Resources/" + Traffic.BusPrefabPath + ".prefab";
        static readonly string[] WheelNodes = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };

        [MenuItem("DriftSkate/Verkehr/Schulbus bauen")]
        public static void Build()
        {
            foreach (var d in new[] { "/Meshes", "/Materials", "/Textures" }) AssetDatabase.DeleteAsset(Root + d);
            GlbBake.EnsureFolders(Root + "/Source", Root + "/Meshes", Root + "/Materials", Root + "/Textures", "Assets/_Game/Resources/Vehicles");
            if (!File.Exists(SourceGlb)) throw new Exception("Modell fehlt: " + SourceGlb);
            File.Copy(SourceGlb, GlbPath, true);
            AssetDatabase.ImportAsset(GlbPath, ImportAssetOptions.ForceUpdate);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
            if (src == null) throw new Exception("GLB konnte nicht importiert werden: " + GlbPath);
            var colors = GlbBake.ReadColors(GlbPath);

            var inst = Object.Instantiate(src);
            var bus = new GameObject("SchoolBus");
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
                Transform Ancestor(Transform t, Func<string, bool> match)
                {
                    for (; t != null && t != inst.transform; t = t.parent)
                        if (match(GlbBake.CleanName(t.name))) return t;
                    return null;
                }

                Matrix4x4 toRoot = inst.transform.worldToLocalMatrix;
                var body = new GlbBake.MeshBuilder();
                var glass = new GlbBake.MeshBuilder();
                var heads = new GlbBake.MeshBuilder();
                var tails = new GlbBake.MeshBuilder();
                var parts = new Dictionary<Transform, GlbBake.MeshBuilder>(); // Raeder und Stopp-Schild
                Bounds box = default;
                bool any = false;

                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    string node = GlbBake.CleanName(mf.name);
                    var pivot = Ancestor(mf.transform, n => Array.IndexOf(WheelNodes, n) >= 0 || n == "StopArm");
                    Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        string mat = GlbBake.CleanName(mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)].name);
                        if (!colors.TryGetValue(mat, out var col)) col = Color.white;
                        var uv = GlbBake.PaletteUv(ColorIndex(col), 4);
                        if (pivot != null)
                        {
                            if (!parts.TryGetValue(pivot, out var pb)) parts[pivot] = pb = new GlbBake.MeshBuilder();
                            pb.Add(mf.sharedMesh, sub, uv, pivot.worldToLocalMatrix * mf.transform.localToWorldMatrix);
                            continue;
                        }
                        if (node.StartsWith("Headlight_")) heads.Add(mf.sharedMesh, sub, uv, m);
                        else if (node.StartsWith("TailLight_")) tails.Add(mf.sharedMesh, sub, uv, m);
                        else if (mat == "Glass") glass.Add(mf.sharedMesh, sub, uv, m);
                        else body.Add(mf.sharedMesh, sub, uv, m);
                    }
                    // Hitbox aus dem Aufbau (ohne Spiegel, Auspuff, Spoiler, Raeder)
                    if (pivot == null && (node == "Cabin" || node == "Hood" || node == "Chassis" || node.StartsWith("Bumper")))
                    {
                        var b = new Bounds(toRoot.MultiplyPoint3x4(mr.bounds.center), toRoot.MultiplyVector(mr.bounds.size));
                        b.size = new Vector3(Mathf.Abs(b.size.x), Mathf.Abs(b.size.y), Mathf.Abs(b.size.z));
                        if (!any) { box = b; any = true; } else box.Encapsulate(b);
                    }
                }

                var tex = GlbBake.Palette(Root + "/Textures/SchoolBus_Palette.png", palette.ToArray(), 4);
                var bodyMat = GlbBake.ToonMat(Root + "/Materials/SchoolBus.mat", Color.white, tex, 0.3f, rim: 0.25f);
                var glassMat = GlbBake.ToonMat(Root + "/Materials/SchoolBus_Glass.mat", Color.white, tex, 0f, rim: 0.4f);
                var headMat = GlbBake.ToonMat(Root + "/Materials/SchoolBus_Head.mat", Palette.Hex("FFF4DA"), null, 0f, 1.6f, rim: 0f);
                var tailOff = GlbBake.ToonMat(Root + "/Materials/SchoolBus_TailOff.mat", Palette.Hex("7A1420"), null, 0f, 0.3f, rim: 0f);
                var tailOn = GlbBake.ToonMat(Root + "/Materials/SchoolBus_TailOn.mat", Palette.Hex("FF2A3E"), null, 0f, 2.2f, rim: 0f);

                int carLayer = LayerMask.NameToLayer("Car");
                bus.layer = carLayer;
                Renderer Part(Transform parent, string name, GlbBake.MeshBuilder b, Material mat, bool shadows) =>
                    b.Empty ? null : GlbBake.Part(parent, name, GlbBake.SaveMesh(b.ToMesh("SchoolBus_" + name), Root + "/Meshes/SchoolBus_" + name + ".asset"), mat, shadows, carLayer);

                Part(bus.transform, "Body", body, bodyMat, true);
                Part(bus.transform, "Glass", glass, glassMat, false);
                Part(bus.transform, "HeadLights", heads, headMat, false);
                var tailR = Part(bus.transform, "TailLights", tails, tailOff, false);

                var rb = bus.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                var hitbox = bus.AddComponent<BoxCollider>();
                hitbox.center = box.center;
                hitbox.size = box.size;
                var tc = bus.AddComponent<TrafficCar>();
                bus.AddComponent<Hitchable>();
                tc.mass = 9000f;
                tc.followAccel = 10f;  // mindestens so stark wie das Bremsen (7 m/s²), sonst faellt er beim Halten zurueck
                tc.knockDistance = 5f;
                tc.knockImpact = 14f;   // erst ein richtig harter Einschlag bringt ihn aus der Spur
                tc.cullDistance = 260f; // gross, sieht man von weit
                tc.tailLights = tailR;
                tc.tailOff = tailOff;
                tc.tailOn = tailOn;

                float frontZ = 0f, rearZ = 0f, radius = 0f;
                foreach (var kv in parts)
                {
                    string n = GlbBake.CleanName(kv.Key.name);
                    var pivot = new GameObject(n).transform;
                    pivot.SetParent(bus.transform, false);
                    pivot.localPosition = toRoot.MultiplyPoint3x4(kv.Key.position);
                    pivot.localRotation = Quaternion.Inverse(inst.transform.rotation) * kv.Key.rotation;
                    Part(pivot, n + "_Mesh", kv.Value, bodyMat, n != "StopArm");
                    int wi = Array.IndexOf(WheelNodes, n);
                    if (wi >= 0)
                    {
                        // TrafficCar dreht die Raeder per localRotation: Ruhestellung muss Null sein
                        pivot.localRotation = Quaternion.identity;
                        tc.wheels[wi] = pivot;
                        var tmp = kv.Value.ToMesh("tmp");
                        radius = Mathf.Max(radius, tmp.bounds.extents.y);
                        Object.DestroyImmediate(tmp);
                        if (wi == 0) frontZ = pivot.localPosition.z;
                        if (wi == 2) rearZ = pivot.localPosition.z;
                    }
                    else
                    {
                        // Stopp-Schild: so wie im Modell = ausgeklappt; eingeklappt liegt es nach hinten an der Seite
                        var arm = pivot.gameObject.AddComponent<BusStopArm>();
                        arm.deployed = pivot.localRotation;
                        arm.folded = Quaternion.AngleAxis(pivot.localPosition.x > 0f ? 90f : -90f, Vector3.up) * pivot.localRotation;
                    }
                }
                tc.wheelBase = Mathf.Abs(frontZ - rearZ);
                tc.wheelRadius = radius > 0.1f ? radius : 0.6f;

                PrefabUtility.SaveAsPrefabAsset(bus, PrefabPath);
                Debug.Log($"[SchoolBus] Prefab gebaut: {PrefabPath} ({palette.Count} Farben, Box {box.size}, Radstand {tc.wheelBase:0.00}, Radius {tc.wheelRadius:0.00})");
            }
            finally
            {
                Object.DestroyImmediate(bus);
                Object.DestroyImmediate(inst);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
