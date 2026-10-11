using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Play-Mode-Test: Pausemenue → OPTIONEN. Lautstaerke-Regler, Gegenlenken, Getriebe und Kamera umstellen; die Werte
    /// muessen sofort im Spiel ankommen (Musik-Lautstaerke, Fahrwerte des Autos, Kamera) und gespeichert sein.
    /// Eigener Spielstand unter Logs/PauseOptionsTestSave, Bild nach Logs/options/.
    /// Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.PauseOptionsPlayTest.Run
    /// </summary>
    public static class PauseOptionsPlayTest
    {
        static double _started;
        static bool _oldEnabled;
        static EnterPlayModeOptions _oldOptions;
        static int _stage, _failures;
        static float _t0;

        public static void Run()
        {
            SaveSystem.DataFolder = System.IO.Path.GetFullPath("Logs/PauseOptionsTestSave"); // Fynns Spielstand bleibt unberuehrt
            System.IO.Directory.CreateDirectory(SaveSystem.DataFolder);
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            _oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _oldOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            _started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Quit(int code)
        {
            Debug.Log("[PauseOptions] " + (code == 0 ? "PAUSE OPTIONS TEST PASS" : "PAUSE OPTIONS TEST FAIL"));
            EditorApplication.update -= Tick;
            EditorSettings.enterPlayModeOptionsEnabled = _oldEnabled;
            EditorSettings.enterPlayModeOptions = _oldOptions;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            EditorApplication.Exit(code);
        }

        static void Check(bool ok, string what)
        {
            Debug.Log("[PauseOptions] " + (ok ? "OK    " : "FEHLT ") + what);
            if (!ok) _failures++;
        }

        static void Click(string label)
        {
            var b = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == "Button_" + label && x.gameObject.activeInHierarchy);
            if (b == null) throw new Exception("Knopf fehlt: " + label);
            b.onClick.Invoke();
        }

        static void Shot(string file)
        {
            var cam = Camera.main;
            var canvas = HUD.Instance.GetComponentInChildren<Canvas>();
            var mode = canvas.renderMode;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
            System.IO.Directory.CreateDirectory("Logs/options");
            SimTests.Capture(cam, "Logs/options/" + file + ".png");
            canvas.renderMode = mode;
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 120) throw new Exception("Zeitlimit");
                if (!EditorApplication.isPlaying || PlayerAvatar.Local == null || HUD.Instance == null || Time.time < 2.5f) return;
                var p = SaveSystem.Profile;
                var car = PlayerAvatar.Local.car;
                float t = Time.time;
                switch (_stage)
                {
                    case 0:
                        p.assistLevel = 2;
                        p.autoGearbox = true;
                        PlayerAvatar.Local.ReapplyCarSetup();
                        typeof(HUD).GetMethod("SetPaused", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                   .Invoke(HUD.Instance, new object[] { true });
                        Click("OPTIONEN");
                        _stage = 1; _t0 = t;
                        break;
                    case 1:
                        if (t - _t0 < 0.3f) break;
                        Shot("pause_options");
                        float assistBefore = car.setup.assist;
                        Click("MANUELL");
                        Check(!p.autoGearbox && !car.setup.autoGearbox, "Getriebe auf MANUELL, sofort am Auto");
                        Click("STARK");
                        Check(p.assistLevel == 3 && car.setup.assist > assistBefore, $"Gegenlenken STARK, sofort am Auto ({assistBefore:0.00} -> {car.setup.assist:0.00})");
                        Click("NAH");
                        Check(CameraRig.Instance != null && CameraRig.Views[CameraRig.Instance.View] == "NAH", "Kamera NAH");
                        var music = UnityEngine.Object.FindObjectsByType<Slider>(FindObjectsSortMode.None).Where(s => s.gameObject.activeInHierarchy)
                                         .OrderByDescending(s => s.transform.position.y).FirstOrDefault();
                        if (music == null) throw new Exception("Regler fehlen");
                        music.value = 0.3f;
                        Check(Mathf.Abs(p.musicVolume - 0.3f) < 0.01f, $"Musik-Regler setzt Lautstaerke ({p.musicVolume:0.00})");
                        Shot("pause_options_changed");
                        Click("ZURUECK");
                        Click("WEITER");
                        Check(!HUD.Instance.Paused, "Pausemenue wieder zu");
                        Quit(_failures == 0 ? 0 : 1);
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[PauseOptions] FEHLT: " + e.Message);
                Quit(1);
            }
        }
    }
}
