using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Pausemenue in der Stadt: WETTER & HIMMEL oeffnen, NACHT und GEWITTER druecken, pruefen, dass Himmel und Regen
    /// umspringen (Regen zieht schnell auf), Bilder nach Logs/weather/. Spielstand wird danach zurueckgesetzt.
    /// Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.WeatherMenuPlayTest.Run
    /// </summary>
    public static class WeatherMenuPlayTest
    {
        static double _started;
        static int _stage, _failures;
        static float _t0;
        static string _oldSky, _oldWeather;
        static bool _oldEnabled;
        static EnterPlayModeOptions _oldOptions;

        public static void Run()
        {
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            _oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _oldOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            _started = EditorApplication.timeSinceStartup;
            _stage = 0;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Quit(int code)
        {
            EditorApplication.update -= Tick;
            EditorSettings.enterPlayModeOptionsEnabled = _oldEnabled;
            EditorSettings.enterPlayModeOptions = _oldOptions;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            EditorApplication.Exit(code);
        }

        static void Check(bool ok, string what)
        {
            Debug.Log("[WeatherMenu] " + (ok ? "OK: " : "FEHLT: ") + what);
            if (!ok) _failures++;
        }

        static void Click(string label)
        {
            var b = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == "Button_" + label && x.gameObject.activeInHierarchy);
            if (b == null) throw new Exception("Knopf fehlt: " + label);
            b.onClick.Invoke();
        }

        /// <summary>Bild mit HUD: Overlay-Canvas kurz an die Kamera haengen (Overlay wird von cam.Render nicht erfasst).</summary>
        static void Shot(string file)
        {
            var cam = Camera.main;
            var canvas = HUD.Instance.GetComponentInChildren<Canvas>();
            var mode = canvas.renderMode;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
            System.IO.Directory.CreateDirectory("Logs/weather");
            SimTests.Capture(cam, "Logs/weather/" + file + ".png");
            canvas.renderMode = mode;
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 120) throw new Exception("Zeitlimit");
                if (!EditorApplication.isPlaying) return;
                if (PlayerAvatar.Local == null || HUD.Instance == null || WeatherSystem.Instance == null || Time.time < 2f) return;
                float t = Time.time;
                switch (_stage)
                {
                    case 0:
                        _oldSky = SaveSystem.Profile.sky;
                        _oldWeather = SaveSystem.Profile.weather;
                        WeatherSystem.Instance.SetMode("klar");
                        WeatherSystem.Instance.SetInstant(0f, 0f);
                        WeatherSystem.Instance.SetSky("abend");
                        Check(WeatherSystem.CanControl, "Solo darf Wetter und Himmel bestimmen");
                        typeof(HUD).GetMethod("SetPaused", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                   .Invoke(HUD.Instance, new object[] { true });
                        Click("WETTER & HIMMEL");
                        _stage = 1; _t0 = t;
                        break;
                    case 1:
                        if (t - _t0 < 0.3f) break;
                        Shot("menu_abend");
                        Click("NACHT");
                        Click("GEWITTER");
                        _stage = 2; _t0 = t;
                        break;
                    case 2:
                        if (t - _t0 < 0.3f) break;
                        Check(RenderSettings.skybox != null && RenderSettings.skybox.name == "Sky_nacht", "Himmel ist NACHT (" + RenderSettings.skybox?.name + ")");
                        Check(WeatherSystem.Sky.id == "nacht" && SaveSystem.Profile.sky == "nacht", "Himmel im Wettersystem und Spielstand");
                        Check(SaveSystem.Profile.weather == "gewitter" && WeatherSystem.Storm, "Gewitter eingestellt");
                        Shot("menu_nacht");
                        Click("ZURUECK");
                        Click("WEITER");
                        _stage = 3; _t0 = t;
                        break;
                    case 3:
                        if (t - _t0 < 7f) break;
                        Check(WeatherSystem.Rain > 0.95f, $"Regen nach 7 s voll da ({WeatherSystem.Rain:0.00})");
                        Check(!HUD.Instance.Paused, "Pausemenue zu");
                        Shot("city_nacht_gewitter");
                        WeatherSystem.Instance.SetSky("pink");
                        WeatherSystem.Instance.SetMode("klar");
                        _stage = 4; _t0 = t;
                        break;
                    case 4:
                        if (t - _t0 < 7f) break;
                        Check(WeatherSystem.Rain < 0.05f && !WeatherSystem.Storm, $"Aufgeklart ({WeatherSystem.Rain:0.00})");
                        Shot("city_pink_klar");
                        SaveSystem.Profile.sky = _oldSky;
                        SaveSystem.Profile.weather = _oldWeather;
                        SaveSystem.Save();
                        Quit(_failures == 0 ? 0 : 1);
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[WeatherMenu] FEHLT: " + e);
                Quit(1);
            }
        }
    }
}
