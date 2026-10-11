using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Anbauteile fuer die Autos aus GLBs (Claude 3D Design): der Aufbau-Turbo fuer die Motorhaube
    /// (Art/CarParts/turbo_hood.glb -> Resources/CarParts/TurboHood). Ein Mesh ueber die Paletten-Textur, dazu das
    /// Verdichterrad (Schaufeln, Nabe, Mutter) als eigenes Teil mit Drehpunkt auf der Achse, damit es sich mit der
    /// Drehzahl drehen kann (TurboSpin). Menue: DriftSkate → Autos → Turbo bauen.
    /// </summary>
    public static class CarPartAssets
    {
        const string Name = "TurboHood";
        const string Root = "Assets/_Game/CarParts";
        const string SourceGlb = "Art/CarParts/turbo_hood.glb";
        const string GlbPath = Root + "/Source/turbo_hood.glb";
        const string PrefabPath = "Assets/_Game/Resources/" + CarBuilder.TurboPrefab + ".prefab";
        static readonly string[] Wheel = { "Blade", "CompressorHub", "HubNut" };

        [MenuItem("DriftSkate/Autos/Turbo bauen")]
        public static void BuildTurbo()
        {
            GlbBake.EnsureFolders(Root + "/Source", Root + "/Meshes", Root + "/Materials", Root + "/Textures", "Assets/_Game/Resources/CarParts");
            if (!File.Exists(SourceGlb)) throw new Exception("Modell fehlt: " + SourceGlb);
            File.Copy(SourceGlb, GlbPath, true);
            AssetDatabase.ImportAsset(GlbPath, ImportAssetOptions.ForceUpdate);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
            if (src == null) throw new Exception("GLB konnte nicht importiert werden: " + GlbPath);
            var colors = GlbBake.ReadColors(GlbPath);

            var inst = Object.Instantiate(src);
            var root = new GameObject(Name);
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
                Matrix4x4 toRoot = inst.transform.worldToLocalMatrix;
                var body = new GlbBake.MeshBuilder();
                var wheelParts = new List<(Mesh mesh, int sub, Vector2 uv, Matrix4x4 m)>();
                Bounds wheelBounds = default;
                bool any = false;
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null) continue;
                    string node = GlbBake.CleanName(mf.name);
                    bool spin = Array.Exists(Wheel, w => node.StartsWith(w));
                    Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        string mat = GlbBake.CleanName(mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)].name);
                        if (!colors.TryGetValue(mat, out var col)) col = Color.white;
                        var uv = GlbBake.PaletteUv(ColorIndex(col), 4);
                        if (spin) wheelParts.Add((mf.sharedMesh, sub, uv, m));
                        else body.Add(mf.sharedMesh, sub, uv, m);
                    }
                    if (spin)
                    {
                        var b = new Bounds(toRoot.MultiplyPoint3x4(mr.bounds.center), toRoot.MultiplyVector(mr.bounds.size));
                        if (!any) { wheelBounds = b; any = true; } else wheelBounds.Encapsulate(b);
                    }
                }
                var tex = GlbBake.Palette(Root + "/Textures/" + Name + "_Palette.png", palette.ToArray(), 4);
                var mat0 = GlbBake.ToonMat(Root + "/Materials/" + Name + ".mat", Color.white, tex, 0.15f, rim: 0.3f);
                GlbBake.Part(root.transform, "Body", GlbBake.SaveMesh(body.ToMesh(Name + "_Body"), Root + "/Meshes/" + Name + "_Body.asset"), mat0, true);

                // Verdichterrad: Drehpunkt in der Mitte der Nabe, Achse laengs (Z, Ansaugrichtung nach vorn)
                var pivot = new GameObject("Compressor").transform;
                pivot.SetParent(root.transform, false);
                Vector3 hub = wheelBounds.center;
                pivot.localPosition = hub;
                var wheel = new GlbBake.MeshBuilder();
                foreach (var w in wheelParts) wheel.Add(w.mesh, w.sub, w.uv, Matrix4x4.Translate(-hub) * w.m);
                GlbBake.Part(pivot, "Wheel", GlbBake.SaveMesh(wheel.ToMesh(Name + "_Wheel"), Root + "/Meshes/" + Name + "_Wheel.asset"), mat0, false);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[Turbo] Prefab gebaut: {PrefabPath} ({palette.Count} Farben, Verdichterrad um {hub})");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(inst);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
