using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// In der Stadt (mit Spielkamera): auf dem Board anschieben, Ollie, A kurz halten (kleine Drehung), landen und ohne
    /// Eingabe weiterrollen. Protokolliert Board-Richtung, Fahrtrichtung und Kamera-Blick ueber die Zeit.
    /// Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.CitySpinPlayTest.Run [-spinhold 0.12]
    /// </summary>
    public static class CitySpinPlayTest
    {
        static double _started;
        static int _stage;
        static float _t0, _hold = 0.12f;
        static bool _keepW, _holdThrough;
        static bool _oldEnabled;
        static EnterPlayModeOptions _oldOptions;

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int ai = Array.IndexOf(args, "-spinhold");
            if (ai >= 0 && ai + 1 < args.Length) float.TryParse(args[ai + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _hold);
            _keepW = Array.IndexOf(args, "-keepw") >= 0;           // W (anschieben) die ganze Zeit gehalten
            _holdThrough = Array.IndexOf(args, "-holdthrough") >= 0; // A bis ueber die Landung gehalten, dann los
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
            EditorApplication.update -= Tick;
            EditorSettings.enterPlayModeOptionsEnabled = _oldEnabled;
            EditorSettings.enterPlayModeOptions = _oldOptions;
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(code);
        }

        static float Yaw(Vector3 v) => Mathf.Repeat(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg, 360f);

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 120) throw new Exception("Zeitlimit");
                if (!EditorApplication.isPlaying) return;
                var a = PlayerAvatar.Local;
                if (a == null || Time.time < 2f) return;
                var sc = a.skater;
                var cam = Camera.main.transform;
                var rb = sc.GetComponent<Rigidbody>();
                float t = Time.time;
                switch (_stage)
                {
                    case 0:
                        a.externalControl = true;
                        sc.inputEnabled = false;
                        a.PlaceForChallenge(false, new Vector3(CityBuilder.RoadCenter(1) + 4f, 0.05f, CityBuilder.RoadCenter(0) + 20f), 0f);
                        _stage = 1; _t0 = t;
                        break;
                    case 1: // anschieben (Strasse nach Norden)
                        sc.InjectInput(new Vector2(0f, 1f), false);
                        if (t - _t0 > 4.5f) { _stage = 2; _t0 = t; }
                        break;
                    case 2: // Ollie laden
                        sc.InjectInput(Vector2.zero, true);
                        if (t - _t0 > 0.3f) { sc.InjectInput(Vector2.zero, false); _stage = 3; _t0 = t; }
                        break;
                    case 3: // A kurz halten, dann loslassen
                        float w = _keepW ? 1f : 0f;
                        bool spinKey = _holdThrough || t - _t0 < _hold;
                        sc.InjectInput(new Vector2(spinKey ? -1f : 0f, w), false);
                        if (sc.State == SkaterState.Air || t - _t0 < 0.1f)
                            Debug.Log($"[City] Luft t {t - _t0:0.00} Board {Mathf.Repeat(sc.Heading, 360f):0} Flug {Yaw(rb.linearVelocity):0} Kamera {cam.eulerAngles.y:0} Spin {sc.AirSpin:0}");
                        else { Debug.Log($"[City] gelandet nach {t - _t0:0.00} s, Drehung {sc.AirSpin:0}"); _stage = 4; _t0 = t; }
                        break;
                    case 4:
                        float w2 = _keepW ? 1f : 0f;
                        sc.InjectInput(new Vector2(_holdThrough && t - _t0 < 0.15f ? -1f : 0f, w2), false);
                        Debug.Log($"[City] Boden t {t - _t0:0.00} Board {Mathf.Repeat(sc.Heading, 360f):0} Align {sc.align.eulerAngles.y:0} Fahrt {Yaw(rb.linearVelocity):0} Kamera {cam.eulerAngles.y:0} {sc.State}");
                        if (t - _t0 > 1.5f) { EditorApplication.ExitPlaymode(); Quit(0); }
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Quit(1);
            }
        }
    }
}
