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
    
        // ------------------------------------------------------------------ Raketen-Booster

        /// <summary>
        /// Raketen-Booster (Art/CarParts/rocket_booster.glb fuers Dach, rocket_booster_hinten.glb fuers Heck):
        /// Rumpf ueber die Paletten-Textur, die Flammen (aus dem Dach-Modell, auch fuers Heck) als eigenes leuchtendes Teil
        /// "Flame" mit Drehpunkt an der Duese; BoosterFlame blendet sie beim Boosten ein und laesst sie flackern.
        /// </summary>
        [MenuItem("DriftSkate/Autos/Raketen-Booster bauen")]
        public static void BuildBoosters()
        {
            GlbBake.EnsureFolders(Root + "/Source", Root + "/Meshes", Root + "/Materials", Root + "/Textures", "Assets/_Game/Resources/CarParts");
            var roof = Import("Art/CarParts/rocket_booster.glb");
            var rear = Import("Art/CarParts/rocket_booster_hinten.glb");
            var flameColors = GlbBake.ReadColors(Root + "/Source/rocket_booster.glb");

            var roofInst = Object.Instantiate(roof);
            var rearInst = Object.Instantiate(rear);
            try
            {
                // Flammen aus dem Dach-Modell: Raum relativ zum Duesenende (Ursprung = Flammenansatz, Flamme zeigt nach -Z)
                var palette = new List<Color>();
                var flame = new GlbBake.MeshBuilder();
                Matrix4x4 roofRoot = roofInst.transform.worldToLocalMatrix;
                Vector3 roofAnchor = Vector3.zero;
                foreach (var mf in roofInst.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!GlbBake.CleanName(mf.name).StartsWith("Flame_")) continue;
                    roofAnchor = roofRoot.MultiplyPoint3x4(mf.transform.position);
                }
                foreach (var mf in roofInst.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!GlbBake.CleanName(mf.name).StartsWith("Flame_")) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                    {
                        string mat = GlbBake.CleanName(mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)].name);
                        if (!flameColors.TryGetValue(mat, out var col)) col = Color.cyan;
                        flame.Add(mf.sharedMesh, sub, GlbBake.PaletteUv(Index(palette, col), 4),
                                  Matrix4x4.Translate(-roofAnchor) * roofRoot * mf.transform.localToWorldMatrix);
                    }
                }
                var flameMesh = GlbBake.SaveMesh(flame.ToMesh("RocketFlame"), Root + "/Meshes/RocketFlame.asset");
                var flameTex = GlbBake.Palette(Root + "/Textures/RocketFlame_Palette.png", palette.ToArray(), 4);
                var flameMat = GlbBake.ToonMat(Root + "/Materials/RocketFlame.mat", Color.white, flameTex, 0f, 2.6f, rim: 0f);

                BakeRocket(roofInst, "RocketRoof", CarBuilder.RocketRoofPrefab, roofAnchor, flameMesh, flameMat);
                // Heck: Flamme am Ende der Duese (Mitte der Duesenoeffnung)
                Vector3 rearAnchor = Vector3.zero;
                Matrix4x4 rearRoot = rearInst.transform.worldToLocalMatrix;
                foreach (var mr in rearInst.GetComponentsInChildren<MeshRenderer>(true))
                    if (GlbBake.CleanName(mr.name) == "NozzleLip")
                    {
                        var c = rearRoot.MultiplyPoint3x4(mr.bounds.center);
                        float minZ = rearRoot.MultiplyPoint3x4(mr.bounds.min).z, maxZ = rearRoot.MultiplyPoint3x4(mr.bounds.max).z;
                        rearAnchor = new Vector3(c.x, c.y, Mathf.Min(minZ, maxZ) + 0.01f);
                    }
                BakeRocket(rearInst, "RocketRear", CarBuilder.RocketRearPrefab, rearAnchor, flameMesh, flameMat);
            }
            finally
            {
                Object.DestroyImmediate(roofInst);
                Object.DestroyImmediate(rearInst);
            }
            AssetDatabase.SaveAssets();
        }

        static GameObject Import(string source)
        {
            if (!File.Exists(source)) throw new Exception("Modell fehlt: " + source);
            string path = Root + "/Source/" + Path.GetFileName(source);
            File.Copy(source, path, true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) throw new Exception("GLB konnte nicht importiert werden: " + path);
            return go;
        }

        static int Index(List<Color> palette, Color c)
        {
            int i = palette.FindIndex(x => Mathf.Abs(x.r - c.r) + Mathf.Abs(x.g - c.g) + Mathf.Abs(x.b - c.b) < 0.01f);
            if (i >= 0) return i;
            palette.Add(c);
            return palette.Count - 1;
        }

        static void BakeRocket(GameObject inst, string name, string resourcePath, Vector3 flameAnchor, Mesh flameMesh, Material flameMat)
        {
            string glb = Root + "/Source/" + (name == "RocketRoof" ? "rocket_booster.glb" : "rocket_booster_hinten.glb");
            var colors = GlbBake.ReadColors(glb);
            var palette = new List<Color>();
            var body = new GlbBake.MeshBuilder();
            Matrix4x4 toRoot = inst.transform.worldToLocalMatrix;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null || GlbBake.CleanName(mf.name).StartsWith("Flame_")) continue;
                for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++)
                {
                    string mat = GlbBake.CleanName(mr.sharedMaterials[Mathf.Min(sub, mr.sharedMaterials.Length - 1)].name);
                    if (!colors.TryGetValue(mat, out var col)) col = Color.white;
                    body.Add(mf.sharedMesh, sub, GlbBake.PaletteUv(Index(palette, col), 4), toRoot * mf.transform.localToWorldMatrix);
                }
            }
            var root = new GameObject(name);
            try
            {
                var tex = GlbBake.Palette(Root + "/Textures/" + name + "_Palette.png", palette.ToArray(), 4);
                var mat = GlbBake.ToonMat(Root + "/Materials/" + name + ".mat", Color.white, tex, 0.15f, rim: 0.3f);
                GlbBake.Part(root.transform, "Body", GlbBake.SaveMesh(body.ToMesh(name + "_Body"), Root + "/Meshes/" + name + "_Body.asset"), mat, true);
                var pivot = new GameObject("Flame").transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = flameAnchor;
                GlbBake.Part(pivot, "FlameMesh", flameMesh, flameMat, false);
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/_Game/Resources/" + resourcePath + ".prefab");
                Debug.Log($"[Booster] {name} gebaut ({palette.Count} Farben, Flamme bei {flameAnchor})");
            }
            finally { Object.DestroyImmediate(root); }
        }
}
}
