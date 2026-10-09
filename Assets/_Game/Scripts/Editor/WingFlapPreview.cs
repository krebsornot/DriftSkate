using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Rendert die Engelsfluegel mit dem prozeduralen Fluegelschlag (WingFlapper) ueber einen ganzen Schlag:
    /// Zeilen = von hinten, von der Seite, von oben; Spalten = Zeitpunkte. Bild: Logs/wing_flap_sheet.png
    /// </summary>
    public static class WingFlapPreview
    {
        [MenuItem("DriftSkate/Angel Wings/Fluegelschlag rendern")]
        public static void Run()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Accessories/AngelWings/AngelWings.prefab");
            var scene = EditorSceneManager.NewPreviewScene();
            const int cols = 6, rows = 4, cw = 360, ch = 300;
            var sheet = new Texture2D(cols * cw, rows * ch, TextureFormat.RGB24, false);
            var rt = new RenderTexture(cw, ch, 24) { antiAliasing = 4 };
            var px = new Texture2D(cw, ch, TextureFormat.RGB24, false);
            try
            {
                var root = new GameObject("WingsRoot");
                SceneManager.MoveGameObjectToScene(root, scene);
                var wings = Object.Instantiate(prefab, root.transform);
                wings.transform.localScale = Vector3.one * WingsOnBack.Fit(wings, 1.45f);
                foreach (var smr in wings.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.forceMatrixRecalculationPerRender = true;
                var flap = wings.AddComponent<WingFlapper>();
                // Kleiner Koerper-Ersatz: zeigt, wo vorn ist (Kugel = Kopf vorn)
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                SceneManager.MoveGameObjectToScene(body, scene);
                body.transform.position = new Vector3(0f, -0.55f, 0.18f);
                body.transform.localScale = new Vector3(0.45f, 0.6f, 0.3f);
                var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                SceneManager.MoveGameObjectToScene(nose, scene);
                nose.transform.position = new Vector3(0f, 0.25f, 0.45f);
                nose.transform.localScale = Vector3.one * 0.18f;
                var lightGo = new GameObject("Light");
                SceneManager.MoveGameObjectToScene(lightGo, scene);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.3f;
                lightGo.transform.rotation = Quaternion.Euler(40f, 160f, 0f);
                var camGo = new GameObject("Cam");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.13f, 0.14f, 0.2f);
                cam.orthographic = true;
                cam.orthographicSize = 1.15f;
                cam.targetTexture = rt;

                // Zeile 0-2: Luft-Schlag (von hinten, Seite, oben); Zeile 3: Doppelsprung-Schlag von der Seite
                flap.frequency = 1.5f; flap.sweep = 20f; flap.lift = 26f; flap.tuck = 24f; // wie WingsOnBack in der Luft
                for (int i = 0; i < 40; i++) flap.Tick(0.05f); // eingeschwungen
                Vector3[] camPos = { new Vector3(0, 0.2f, -5), new Vector3(5, 0.2f, 0), new Vector3(0, 5, 0.01f), new Vector3(5, 0.2f, 0) };
                for (int c = 0; c < cols; c++)
                {
                    for (int r = 0; r < 3; r++) Shot(cam, camPos[r], rt, px, sheet, c * cw, (rows - 1 - r) * ch, cw, ch);
                    flap.Tick(1f / 1.5f / cols);
                }
                flap.Burst();
                for (int c = 0; c < cols; c++)
                {
                    flap.Tick(0.55f / cols);
                    Shot(cam, camPos[3], rt, px, sheet, c * cw, 0, cw, ch);
                }
                Directory.CreateDirectory("Logs");
                File.WriteAllBytes("Logs/wing_flap_sheet.png", sheet.EncodeToPNG());
                Debug.Log("[Wings] Logs/wing_flap_sheet.png");
            }
            finally
            {
                cameraCleanup(rt);
                Object.DestroyImmediate(sheet);
                Object.DestroyImmediate(px);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void cameraCleanup(RenderTexture rt) { if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); } }

        static void Shot(Camera cam, Vector3 pos, RenderTexture rt, Texture2D px, Texture2D sheet, int x, int y, int w, int h)
        {
            cam.transform.position = pos;
            cam.transform.LookAt(Vector3.zero, pos.y > 4f ? Vector3.forward : Vector3.up);
            cam.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            px.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            px.Apply();
            RenderTexture.active = old;
            sheet.SetPixels(x, y, w, h, px.GetPixels());
        }
    }
}
