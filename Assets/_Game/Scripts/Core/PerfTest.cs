using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DriftSkate
{
    /// <summary>
    /// Leistungs-Messung im fertigen Spiel (Startparameter -perftest): Stadt solo laden, VSync aus, feste Kamerapunkte
    /// abfahren und je 4 s FPS sowie CPU-/GPU-Zeit pro Frame messen. Ergebnis in -perflog (Standard Logs/perf_player.txt),
    /// danach beendet sich das Spiel.
    /// </summary>
    public class PerfTest : MonoBehaviour
    {
        public static bool Enabled => Array.IndexOf(Environment.GetCommandLineArgs(), "-perftest") >= 0;

        public static void Create()
        {
            var go = new GameObject("PerfTest");
            DontDestroyOnLoad(go);
            go.AddComponent<PerfTest>();
        }

        static string ArgValue(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        readonly FrameTiming[] _timings = new FrameTiming[1];

        IEnumerator Start()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            yield return null;
            GameSession.Mode = SessionMode.Solo;
            string weather = ArgValue("-perfweather", "klar");
            SaveSystem.Profile.weather = weather;
            SaveSystem.Profile.reflections = true;
            SceneManager.LoadScene("City");
            float t0 = Time.realtimeSinceStartup;
            while (PlayerAvatar.Local == null && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            var sb = new StringBuilder();
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            sb.AppendLine($"{SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), {SystemInfo.processorType}, {Screen.width}x{Screen.height}");
            sb.AppendLine($"Wetter: {weather}");
            sb.AppendLine($"GPU Resident Drawer: {(urp != null ? urp.gpuResidentDrawerMode.ToString() : "?")}, Occlusion: {(urp != null && urp.gpuResidentDrawerEnableOcclusionCullingInCameras)}");
            if (PlayerAvatar.Local == null)
            {
                Write(sb.AppendLine("Kein Spieler gespawnt"));
                yield break;
            }
            PlayerAvatar.Local.externalControl = true;
            if (HUD.Instance != null) HUD.Instance.enabled = true;
            yield return new WaitForSecondsRealtime(weather == "klar" ? 4f : 12f); // Aufwaermen (Shader, Streaming; Regen braucht, bis der Boden nass ist)

            float B(int i) => CityBuilder.BlockCenter(i);
            float R(int k) => CityBuilder.RoadCenter(k);
            var views = new (string name, Vector3 pos, Vector3 target)[]
            {
                ("strasse_haeuser", new Vector3(R(1), 1.8f, B(0)), new Vector3(R(1), 1.5f, B(1))),
                ("strasse_quer",    new Vector3(B(1), 1.8f, R(1)), new Vector3(B(3), 1.5f, R(1))),
                ("kreuzung",        new Vector3(R(2) + 6f, 1.8f, R(1) + 6f), new Vector3(R(3), 1.5f, R(0))),
                ("brunnenplatz",    new Vector3(B(2) - 24f, 1.8f, B(2) - 24f), new Vector3(B(2) + 20f, 1.5f, B(2) + 20f)),
                ("platz_sofa",      new Vector3(B(1) + 10f, 1.8f, B(2) - 10f), new Vector3(B(1) - 20f, 1.5f, B(2) + 28f)),
            };
            // Seitenansicht des eigenen Autos (Folie, Radlaeufe, Bloom)
            var carT = PlayerAvatar.Local.car.transform;
            var carRb = carT.GetComponent<Rigidbody>();
            if (carRb != null) { carRb.linearVelocity = Vector3.zero; carRb.isKinematic = true; } // sonst rollt es aus dem Bild
            var list = new System.Collections.Generic.List<(string name, Vector3 pos, Vector3 target)>(views)
            {
                ("auto_seite", carT.position + carT.right * 5.5f + Vector3.up * 1.1f, carT.position + Vector3.up * 0.6f),
                ("auto_seite_links", carT.position - carT.right * 5.5f + carT.forward * 1.5f + Vector3.up * 0.9f, carT.position + Vector3.up * 0.6f),
            };
            ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ArgValue("-perflog", "Logs/perf_player.txt"))), $"perf_{weather}_follow.png"));
            yield return null;
            yield return null;
            var rends = carT.GetComponentsInChildren<Renderer>(true);
            int enabledR = 0; Bounds cb = new Bounds(carT.position, Vector3.zero);
            foreach (var r in rends) if (r.enabled && r.gameObject.activeInHierarchy) { enabledR++; cb.Encapsulate(r.bounds); }
            sb.AppendLine($"Auto: Pos {carT.position}, Renderer {enabledR}/{rends.Length}, Bounds {cb.center} {cb.size}, Kamera {Camera.main?.transform.position}");
            float worst = float.MaxValue;
            foreach (var v in list)
            {
                CameraRig.Instance?.SetDialogShot(v.pos, v.target);
                if (v.name.StartsWith("auto_")) sb.AppendLine($"  {v.name}: Kamera-Ziel {v.pos} -> {v.target}");
                yield return new WaitForSecondsRealtime(1.5f);
                if (v.name.StartsWith("auto_")) sb.AppendLine($"  {v.name}: Kamera ist bei {Camera.main.transform.position}, Auto bei {carT.position}");
                int frames = 0;
                float time = 0f, maxDt = 0f, cpu = 0f, gpu = 0f;
                int timed = 0;
                while (time < 4f)
                {
                    yield return null;
                    float dt = Time.unscaledDeltaTime;
                    frames++;
                    time += dt;
                    maxDt = Mathf.Max(maxDt, dt);
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
                    {
                        cpu += (float)_timings[0].cpuFrameTime;
                        gpu += (float)_timings[0].gpuFrameTime;
                        timed++;
                    }
                }
                ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ArgValue("-perflog", "Logs/perf_player.txt"))), $"perf_{weather}_{v.name}.png"));
                yield return null;
                float fps = frames / time;
                worst = Mathf.Min(worst, fps);
                string ft = timed > 0 ? $"CPU {cpu / timed:0.0} ms, GPU {gpu / timed:0.0} ms" : "keine Frame-Zeiten";
                sb.AppendLine($"{v.name,-16} {fps,6:0} FPS  (langsamster Frame {maxDt * 1000f:0.0} ms, {ft})");
            }
            sb.AppendLine($"Schlechtester Punkt: {worst:0} FPS");
            Write(sb);
        }

        void Write(StringBuilder sb)
        {
            string path = ArgValue("-perflog", "Logs/perf_player.txt");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(path, sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogWarning("Perf-Log nicht geschrieben: " + e.Message);
            }
            Application.Quit();
        }
    }
}
