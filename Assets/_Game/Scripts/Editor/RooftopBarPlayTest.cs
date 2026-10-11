using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Play-Mode-Test: auf der Rooftop-Bar stehen Rocco (hinter dem Tresen, Haende auf Tresenhoehe) und Miru (am DJ-Pult,
    /// Animation DJ). Bilder aus mehreren Winkeln und zu zwei Zeitpunkten nach Logs/rooftopbar/play_*.png.
    /// Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.RooftopBarPlayTest.Run
    /// </summary>
    public static class RooftopBarPlayTest
    {
        static double _started;
        static bool _oldEnabled, _failed;
        static EnterPlayModeOptions _oldOptions;
        static int _stage, _failures;

        public static void Run()
        {
            SaveSystem.DataFolder = System.IO.Path.GetFullPath("Logs/RooftopBarTestSave"); // Fynns Spielstand bleibt unberuehrt
            System.IO.Directory.CreateDirectory(SaveSystem.DataFolder);
            System.IO.Directory.CreateDirectory("Logs/rooftopbar");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            _oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _oldOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            Application.logMessageReceived += (m, t, type) => { if (type == LogType.Exception) _failed = true; };
            _started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Check(bool ok, string what)
        {
            Debug.Log("[RooftopBar] " + (ok ? "OK    " : "FEHLT ") + what);
            if (!ok) _failures++;
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 120) throw new Exception("Zeitlimit");
                if (!EditorApplication.isPlaying || Time.time < 3f) return;
                var crew = RooftopBarCrew.Spawned;
                var rocco = crew.Find(n => n.Id == "Rocco");
                var miru = crew.Find(n => n.Id == "Miru");
                switch (_stage)
                {
                    case 0:
                    {
                        Check(crew.Count == 2 && rocco != null && miru != null, $"Bar-Crew aufgestellt ({crew.Count})");
                        if (rocco == null || miru == null) { Quit(1); return; }
                        Check(rocco.HasClip("Idle") && rocco.HasClip("Shake") && rocco.HasClip("Laugh"), "Rocco hat Idle, Shake und Laugh");
                        Check(miru.HasClip("DJ"), "Miru hat die DJ-Animation");
                        var top = GameObject.Find(RoofSets.Bar.name).transform;
                        Check(Mathf.Abs(rocco.transform.position.y - top.position.y) < 0.1f, $"Rocco steht auf dem Dach ({rocco.transform.position.y - top.position.y:0.00} m)");
                        Check(Mathf.Abs(miru.transform.position.y - top.position.y - 0.4f) < 0.1f, $"Miru steht auf der Buehne ({miru.transform.position.y - top.position.y:0.00} m)");
                        Shots(rocco, miru, "a");
                        _stage = 1;
                        break;
                    }
                    case 1:
                        if (Time.time < 4.4f) return;
                        Shots(rocco, miru, "b");
                        if (Time.time < 5f) return;
                        rocco.Play("Shake");
                        _stage = 2;
                        break;
                    case 2:
                        if (Time.time < 7.3f) return;
                        Shot(rocco.transform, new Vector3(1.6f, 1.7f, 2.6f), "play_rocco_shake");
                        Quit(_failures == 0 && !_failed ? 0 : 1);
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[RooftopBar] FEHLT: " + e.Message);
                Quit(1);
            }
        }

        static void Shots(StonerNpc rocco, StonerNpc miru, string tag)
        {
            Shot(rocco.transform, new Vector3(0.6f, 1.6f, 3.2f), "play_rocco_front_" + tag);
            Shot(rocco.transform, new Vector3(2.8f, 1.5f, 0.6f), "play_rocco_side_" + tag);
            Shot(miru.transform, new Vector3(0.4f, 1.6f, 3.0f), "play_miru_front_" + tag);
            Shot(miru.transform, new Vector3(2.4f, 1.6f, -0.6f), "play_miru_side_" + tag);
        }

        /// <summary>Kamera relativ zur Figur (lokal: +Z = vor ihr), schaut auf Brusthoehe.</summary>
        static void Shot(Transform who, Vector3 local, string file)
        {
            var cam = new GameObject("BarCam").AddComponent<Camera>();
            cam.transform.position = who.TransformPoint(local);
            cam.transform.LookAt(who.position + Vector3.up * 1.15f);
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.05f;
            SimTests.Capture(cam, "Logs/rooftopbar/" + file + ".png");
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
        }

        static void Quit(int code)
        {
            Debug.Log("[RooftopBar] " + (code == 0 ? "ROOFTOP BAR TEST PASS" : "ROOFTOP BAR TEST FAIL"));
            EditorApplication.update -= Tick;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            EditorSettings.enterPlayModeOptionsEnabled = _oldEnabled;
            EditorSettings.enterPlayModeOptions = _oldOptions;
            EditorApplication.Exit(code);
        }
    }
}
