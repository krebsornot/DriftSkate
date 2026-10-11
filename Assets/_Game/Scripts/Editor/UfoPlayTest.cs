using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Play-Mode-Test in der Stadt: das UFO ist bei Abend-Himmel weg, taucht bei Nacht auf, folgt seiner Tour nach
    /// der gemeinsamen Uhr und verschwindet wieder, wenn der Himmel zurueckgestellt wird. Ohne Fehler im Log.
    /// Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.UfoPlayTest.Run
    /// </summary>
    public static class UfoPlayTest
    {
        static double _started;
        static bool _failed, _oldEnabled;
        static EnterPlayModeOptions _oldOptions;
        static float _t0;
        static int _stage;
        static bool _hiddenAtDusk, _shownAtNight, _onRoute, _travels, _hiddenAgain;

        public static void Run()
        {
            SaveSystem.DataFolder = System.IO.Path.GetFullPath("Logs/UfoTestSave"); // Fynns Spielstand bleibt unberuehrt
            System.IO.Directory.CreateDirectory(SaveSystem.DataFolder);
            System.IO.Directory.CreateDirectory("Logs/ufo");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            _oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _oldOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            Application.logMessageReceived += (m, t, type) => { if (type == LogType.Exception || type == LogType.Error) _failed = true; };
            _started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup - _started > 120) { Debug.LogError("Zeitlimit"); Quit(1); return; }
            if (!EditorApplication.isPlaying || Time.time < 2f) return;
            var ufo = Object.FindAnyObjectByType<Ufo>();
            var weather = WeatherSystem.Instance;
            if (ufo == null || weather == null) { Debug.LogError("[UfoTest] UFO oder WeatherSystem fehlt"); Finish(); return; }
            float since = Time.time - _t0;

            switch (_stage)
            {
                case 0:
                    weather.SetSky(SkyLook.Default.id);
                    Next();
                    break;
                case 1:
                    if (since < 2.5f) return;
                    _hiddenAtDusk = !ufo.Visible && !ufo.visual.gameObject.activeSelf;
                    weather.SetSky(ufo.nightSky);
                    Next();
                    break;
                case 2:
                    if (since < 3f) return;
                    _shownAtNight = ufo.Visible && ufo.visual.gameObject.activeSelf;
                    double now = SharedClock.Time;
                    _onRoute = Vector3.Distance(ufo.transform.position, ufo.PositionAt(now, out _)) < 0.5f;
                    _travels = Vector3.Distance(ufo.PositionAt(now, out _), ufo.PositionAt(now + ufo.hoverTime + ufo.travelTime, out _)) > 50f;
                    Shot(ufo);
                    weather.SetSky(SkyLook.Default.id);
                    Next();
                    break;
                case 3:
                    if (since < 2.5f) return;
                    _hiddenAgain = !ufo.Visible && !ufo.visual.gameObject.activeSelf;
                    Finish();
                    break;
            }
        }

        static void Next() { _stage++; _t0 = Time.time; }

        /// <summary>Bild aus dem laufenden Spiel (Emission, Strahl, Bloom): Spielkamera kurz neben das UFO setzen.</summary>
        static void Shot(Ufo ufo)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var t = cam.transform;
            Vector3 pos = t.position;
            Quaternion rot = t.rotation;
            float fov = cam.fieldOfView;
            t.position = ufo.transform.position + new Vector3(-40f, -8f, -25f);
            t.LookAt(ufo.transform.position + Vector3.down * 10f);
            cam.fieldOfView = 55f;
            SimTests.Capture(cam, "Logs/ufo/ufo_play_night.png");
            t.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = fov;
        }

        static void Finish()
        {
            bool ok = !_failed && _hiddenAtDusk && _shownAtNight && _onRoute && _travels && _hiddenAgain;
            Debug.Log($"[UfoTest] Abend weg {_hiddenAtDusk}, Nacht da {_shownAtNight}, auf der Route {_onRoute}, fliegt weiter {_travels}, " +
                      $"wieder weg {_hiddenAgain}, Fehler {_failed} -> {(ok ? "UFO TEST PASS" : "UFO TEST FAIL")}");
            EditorApplication.ExitPlaymode();
            Quit(ok ? 0 : 1);
        }

        static void Quit(int code)
        {
            EditorApplication.update -= Tick;
            EditorSettings.enterPlayModeOptionsEnabled = _oldEnabled;
            EditorSettings.enterPlayModeOptions = _oldOptions;
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(code);
        }
    }
}
