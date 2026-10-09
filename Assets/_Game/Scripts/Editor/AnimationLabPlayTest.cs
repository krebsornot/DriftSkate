using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    public static class AnimationLabPlayTest
    {
        static double _started;
        static int _stage;
        static bool _failed;
        static Vector3 _start;
        static bool _oldEnabled;
        static EnterPlayModeOptions _oldOptions;

        public static void Run()
        {
            EditorSceneManager.OpenScene(AnimationLabAssets.ScenePath);
            // Preserve the callback through entering Play Mode in this batch process only.
            _oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _oldOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            Application.logMessageReceived += OnLog;
            _started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) _failed = true;
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 60) throw new Exception("Play test timed out.");
                if (!EditorApplication.isPlaying || Time.time < 1f) return;
                var sc = UnityEngine.Object.FindFirstObjectByType<SkaterController>();
                if (sc == null || sc.rig == null || !sc.rig.Ready) throw new Exception("Player/rig missing.");
                sc.inputEnabled = false;
                if (_stage == 0) { _start = sc.transform.position; _stage = 1; }
                sc.InjectInput(new Vector2(.1f, Time.time < 4f ? 1f : 0f), false);
                if (_stage == 1 && Time.time > 2.5f)
                {
                    if (Vector3.Distance(_start, sc.transform.position) < .5f) throw new Exception("Skater did not move.");
                    sc.InjectBoardToggle();
                    _stage = 2;
                }
                if (_stage == 2 && Time.time > 3.5f)
                {
                    if (sc.State != SkaterState.Walking) throw new Exception("Walking transition failed.");
                    _stage = 3;
                }
                if (_stage == 3 && Time.time > 5f)
                {
                    if (sc.transform.position.y < -.5f) throw new Exception("Player fell through the floor.");
                    sc.InjectBoardToggle();
                    _stage = 4;
                }
                if (_stage == 4 && Time.time > 6f)
                {
                    if (sc.State != SkaterState.Riding) throw new Exception("Board transition failed.");
                    Debug.Log("ANIMATION LAB PLAY PASS: startup, movement, walking, stop, remount, ground collision.");
                    EditorApplication.update -= Tick;
                    Finish(_failed ? 1 : 0);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.update -= Tick;
                Finish(1);
            }
        }

        static void Finish(int code)
        {
            EditorSettings.enterPlayModeOptionsEnabled = _oldEnabled;
            EditorSettings.enterPlayModeOptions = _oldOptions;
            EditorApplication.Exit(code);
        }
    }
}
