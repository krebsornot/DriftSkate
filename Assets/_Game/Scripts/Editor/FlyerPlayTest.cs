using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Kurzer Play-Mode-Test in der Stadt: bewegen sich Zeppelin, Schleppflugzeug und NPC-Verkehr, ohne Fehler?
    /// Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.FlyerPlayTest.Run
    /// </summary>
    public static class FlyerPlayTest
    {
        static double _started;
        static bool _failed, _oldEnabled;
        static EnterPlayModeOptions _oldOptions;
        static Vector3 _zep, _plane;
        static float _t0;
        static int _stage;

        public static void Run()
        {
            SaveSystem.DataFolder = System.IO.Path.GetFullPath("Logs/FlyerTestSave"); // Fynns Spielstand bleibt unberuehrt
            System.IO.Directory.CreateDirectory(SaveSystem.DataFolder);
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
            if (EditorApplication.timeSinceStartup - _started > 90) { Debug.LogError("Zeitlimit"); Quit(1); return; }
            if (!EditorApplication.isPlaying || Time.time < 2f) return;
            var zep = UnityEngine.Object.FindAnyObjectByType<Zeppelin>();
            var plane = UnityEngine.Object.FindAnyObjectByType<TowPlane>();
            if (_stage == 0)
            {
                _zep = zep != null ? zep.transform.position : Vector3.zero;
                _plane = plane != null ? plane.transform.position : Vector3.zero;
                _t0 = Time.time;
                _stage = 1;
                return;
            }
            if (Time.time - _t0 < 3f) return;
            float zm = zep != null ? Vector3.Distance(_zep, zep.transform.position) : -1f;
            float pm = plane != null ? Vector3.Distance(_plane, plane.transform.position) : -1f;
            int moving = 0;
            if (Traffic.Instance != null) foreach (var c in Traffic.Instance.cars) if (c.Speed > 2f) moving++;
            bool ok = !_failed && zm > 15f && pm > 40f && moving >= 6;
            Debug.Log($"[Flyer] Zeppelin {zm:0} m, Flugzeug {pm:0} m in 3 s, NPC-Autos in Fahrt {moving}, Fehler {_failed} -> {(ok ? "FLYER TEST PASS" : "FLYER TEST FAIL")}");
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
