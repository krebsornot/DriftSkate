using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Vorschaubilder aller Autos in der Garage (Front schraeg, Seite, Heck schraeg) zum Beurteilen der Karosserien,
    /// dazu jede Folien-Vorlage und die Anbauteile am Beispielauto. Bilder in Logs/cars/.
    /// </summary>
    public static class CarRenderTests
    {
        [MenuItem("DriftSkate/Tests/Autos rendern")]
        public static void RenderAll()
        {
            Directory.CreateDirectory("Logs/cars");
            EditorSceneManager.OpenScene(ProjectBuilder.GaragePath);
            var env = Object.FindAnyObjectByType<GarageEnvironment>();
            var profile = new PlayerProfile();
            profile.EnsureDefaults();
            env.Build(Catalog.Garages[1], profile);
            if (env.skaterRoot != null) env.skaterRoot.gameObject.SetActive(false);
            var cam = Camera.main;
            var orbit = cam.GetComponent<OrbitCamera>();
            if (orbit != null) orbit.enabled = false;
            var tag = MakeTag();

            foreach (var def in Catalog.Cars)
            {
                var design = CarDesign.Default(def.id, Palette.Pink);
                CarBuilder.Build(env.carRoot, def, def.defaultColor, Palette.Pink, tag, null, design);
                Shoot(cam, env.carRoot, def.id + "_front", 38f, 12f, 7.2f);
                Shoot(cam, env.carRoot, def.id + "_side", 90f, 4f, 7.6f);
                Shoot(cam, env.carRoot, def.id + "_rear", 145f, 12f, 7.2f);
            }

            // Vorlagen am S15
            var demo = Catalog.Car("sylph15");
            foreach (var preset in LiveryPresets.Names)
            {
                var d = CarDesign.Default(demo.id, Palette.Pink);
                d.wrap = LiveryPresets.Make(preset, demo, Palette.Pink, demo.defaultColor);
                Color paint = preset == "PANDA" ? Palette.White : demo.defaultColor;
                CarBuilder.Build(env.carRoot, demo, paint, Palette.Pink, tag, null, d);
                Shoot(cam, env.carRoot, "preset_" + preset.ToLower(), 40f, 18f, 7.4f);
                if (preset == "TOUGE" || preset == "RENNSPORT") Shoot(cam, env.carRoot, "preset_" + preset.ToLower() + "_top", 180f, 58f, 7.4f);
            }

            // Alle Anbauteile am Raijin
            var r34 = Catalog.Car("muscle8");
            var full = CarDesign.Default(r34.id, Palette.Pink);
            full.parts[CarDesign.Lip] = 2;
            full.parts[CarDesign.Skirts] = 1;
            full.parts[CarDesign.Fenders] = 1;
            full.parts[CarDesign.Wing] = 2;
            full.parts[CarDesign.Hood] = 2;
            full.parts[CarDesign.Exhaust] = 1;
            full.parts[CarDesign.Rims] = 2;
            full.camber = 7f;
            full.wrap = LiveryPresets.Make("TOUGE", r34, Palette.Lime, r34.defaultColor);
            CarBuilder.Build(env.carRoot, r34, Palette.Hex("2B2B33"), Palette.Lime, tag, null, full);
            Shoot(cam, env.carRoot, "parts_front", 32f, 14f, 6.8f);
            Shoot(cam, env.carRoot, "parts_rear", 150f, 16f, 6.8f);
            foreach (int rim in new[] { 0, 1, 3, 4 })
            {
                full.parts[CarDesign.Rims] = rim;
                CarBuilder.Build(env.carRoot, r34, Palette.Hex("2B2B33"), Palette.Lime, tag, null, full);
                Shoot(cam, env.carRoot, "rim_" + rim, 70f, 4f, 3.2f, new Vector3(0f, 0.35f, 1.3f));
            }
            Debug.Log("[DriftSkate] Autos gerendert: Logs/cars");
        }

        static void Shoot(Camera cam, Transform car, string name, float yaw, float pitch, float dist, Vector3? lookOffset = null)
        {
            Vector3 target = car.position + (lookOffset ?? new Vector3(0f, 0.65f, 0f));
            Quaternion rot = Quaternion.Euler(pitch, yaw + 180f, 0f);
            cam.fieldOfView = 40f;
            cam.transform.SetPositionAndRotation(target - rot * Vector3.forward * dist, rot);
            SimTests.Capture(cam, "Logs/cars/" + name + ".png");
        }

        static Texture2D MakeTag()
        {
            var t = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            GraffitiPainter.DrawDefaultTag(t);
            return t;
        }
    }
}

namespace DriftSkate.EditorTools
{
    public static class CarMeshDebug
    {
        public static void Run()
        {
            var def = Catalog.Car("sylph15");
            var body = new CarBody(def, CarShape.For(def.id));
            var m = body.BuildGreenhouse();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"verts {m.vertexCount} bounds {m.bounds} subs {m.subMeshCount}");
            for (int s = 0; s < m.subMeshCount; s++) sb.AppendLine($"sub{s}: {m.GetTriangles(s).Length / 3} tris");
            var v = m.vertices; var n = m.normals; var t = m.GetTriangles(1);
            if (t.Length == 0) t = m.GetTriangles(0);
            for (int i = 0; i < Mathf.Min(t.Length, 30); i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 geo = Vector3.Cross(b - a, c - a).normalized;
                sb.AppendLine($"tri a{a} n{n[t[i]]} geo{geo}");
            }
            sb.AppendLine($"cowl {CarShape.For(def.id).CowlT} glassEnd {CarShape.For(def.id).GlassEndT} roofY0 {body.RoofY(0f)} topY0 {body.TopY(0f)}");
            System.IO.File.WriteAllText("Logs/carmesh_debug.txt", sb.ToString());
        }
    }
}

namespace DriftSkate.EditorTools
{
    public static class CarNormalCheck
    {
        public static void Run()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var def in Catalog.Cars)
            {
                for (int variant = 0; variant < 2; variant++)
                {
                    var go = new GameObject("Check");
                    var d = CarDesign.Default(def.id, Color.white);
                    if (variant == 1) { d.parts[CarDesign.Fenders] = 1; d.parts[CarDesign.Hood] = 2; d.parts[CarDesign.Wing] = 4; d.parts[CarDesign.Lip] = 2; d.parts[CarDesign.Skirts] = 1; d.parts[CarDesign.PopUps] = 1; }
                    CarBuilder.Build(go.transform, def, Color.red, Color.white, null, null, d);
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var m = mf.sharedMesh;
                        if (m == null) continue;
                        var n = m.normals; var v = m.vertices;
                        int bad = 0, nan = 0;
                        for (int i = 0; i < n.Length; i++)
                        {
                            if (float.IsNaN(n[i].x) || float.IsNaN(v[i].x) || float.IsInfinity(v[i].x)) nan++;
                            else if (n[i].sqrMagnitude < 0.5f) bad++;
                        }
                        if (bad > 0 || nan > 0) sb.AppendLine($"{def.id} v{variant} {mf.name}: {bad} kurze Normalen, {nan} NaN von {n.Length}");
                    }
                    Object.DestroyImmediate(go);
                }
            }
            System.IO.File.WriteAllText("Logs/car_normals.txt", sb.Length == 0 ? "alles ok" : sb.ToString());
        }
    }
}
