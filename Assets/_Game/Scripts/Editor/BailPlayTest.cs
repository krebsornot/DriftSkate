using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Sturz-Test im Animation Lab (Play-Mode im Batch): einmal in voller Fahrt hinfallen, einmal aus der Luft.
    /// Prueft, dass weder der Skater noch irgendein Knochen der Figur unter dem Boden landet, und rendert die
    /// Sturz-Pose (Logs/bail/*.png). Aufruf: -executeMethod DriftSkate.EditorTools.BailPlayTest.Run
    /// </summary>
    public static class BailPlayTest
    {
        static double _started;
        static int _stage;
        static float _t0, _minBone = 99f, _minRoot = 99f;
        static int _shots;
        static bool _failed, _oldEnabled;
        static EnterPlayModeOptions _oldOptions;

        public static void Run()
        {
            Directory.CreateDirectory("Logs/bail");
            EditorSceneManager.OpenScene(AnimationLabAssets.ScenePath);
            _oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _oldOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            Application.logMessageReceived += (m, t, type) => { if (type == LogType.Exception || type == LogType.Error) _failed = true; };
            _started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static string _lowName;

        /// <summary>Editor-Einstellungen wie vorher, dann beenden.</summary>
        static void Quit(int code)
        {
            EditorSettings.enterPlayModeOptionsEnabled = _oldEnabled;
            EditorSettings.enterPlayModeOptions = _oldOptions;
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(code);
        }

        static float LowestBone(SkaterController sc)
        {
            float min = 99f;
            foreach (var t in sc.rig.GetComponentsInChildren<Transform>())
                if (t.GetComponent<Renderer>() == null && t.position.y < min) { min = t.position.y; if (min < _minBone) _lowName = t.name; }
            return min;
        }

        static void Shot(SkaterController sc, string name)
        {
            var cam = Camera.main;
            Vector3 p = sc.transform.position;
            cam.fieldOfView = 45f;
            cam.transform.position = p + new Vector3(2.6f, 1.3f, -2.6f);
            cam.transform.LookAt(p + Vector3.up * 0.4f);
            SimTests.Capture(cam, "Logs/bail/bail_" + name + ".png");
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 90) throw new Exception("Bail-Test: Zeitlimit");
                if (!EditorApplication.isPlaying || Time.time < 1f) return;
                var sc = UnityEngine.Object.FindFirstObjectByType<SkaterController>();
                if (sc == null || sc.rig == null || !sc.rig.Ready) throw new Exception("Skater/Figur fehlt");
                sc.inputEnabled = false;
                float t = Time.time;
                if (_stage == 0)
                {
                    sc.InjectInput(new Vector2(0f, 1f), false);
                    if (t > 3.5f) { sc.Bail("TEST"); _t0 = t; _stage = 1; _shots = 0; Debug.Log($"[Bail] Sturz in Fahrt bei {sc.Speed:0.0} m/s"); }
                    return;
                }
                if (_stage == 1 || _stage == 3)
                {
                    sc.InjectInput(Vector2.zero, false);
                    _minBone = Mathf.Min(_minBone, LowestBone(sc));
                    _minRoot = Mathf.Min(_minRoot, sc.transform.position.y);
                    float[] at = { 0.35f, 0.8f, 1.4f };
                    if (_shots < at.Length && t - _t0 > at[_shots]) { Shot(sc, (_stage == 1 ? "ride_" : "air_") + _shots); _shots++; }
                    if (t - _t0 > 2.4f)
                    {
                        Debug.Log($"[Bail] {(_stage == 1 ? "Fahrt" : "Luft")}: tiefster Knochen {_minBone:0.00} m ({_lowName}), Skater {_minRoot:0.00} m, jetzt {sc.State}");
                        if (_minBone < -0.05f || _minRoot < -0.05f) { Debug.LogError("[Bail] Figur unter dem Boden"); _failed = true; }
                        _minBone = _minRoot = 99f;
                        if (_stage == 1)
                        {
                            sc.LaunchFromCar(new Vector3(0f, 3.5f, -4f), new Vector3(0f, 2f, 7f), 0f);
                            _stage = 2;
                            _t0 = t;
                        }
                        else
                        {
                            EditorApplication.update -= Tick;
                            Debug.Log(_failed ? "BAIL TEST FAIL" : "BAIL TEST PASS");
                            EditorApplication.ExitPlaymode();
                            Quit(_failed ? 1 : 0);
                        }
                    }
                    return;
                }
                if (_stage == 2 && t - _t0 > 0.35f)
                {
                    sc.Bail("TEST LUFT");
                    _t0 = t;
                    _shots = 0;
                    _stage = 3;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.update -= Tick;
                Quit(1);
            }
        }
    }
}
