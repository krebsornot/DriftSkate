using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// 180 in der Luft, landen, danach keine Eingabe: misst, wie weit sich Skater, Align (Board + Figur) und Board
    /// nach der Landung noch drehen (soll ~0 sein). Play-Mode im Animation Lab, Aufruf ohne -quit:
    /// -executeMethod DriftSkate.EditorTools.SpinPlayTest.Run
    /// </summary>
    public static class SpinPlayTest
    {
        static double _started;
        static int _stage;
        static float _t0;
        static bool _oldEnabled;
        static EnterPlayModeOptions _oldOptions;
        static float _lastAlign, _lastBoard, _lastRoot, _turnAlign, _turnBoard, _turnRoot;

        public static void Run()
        {
            EditorSceneManager.OpenScene(AnimationLabAssets.ScenePath);
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

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 60) throw new Exception("Spin-Test: Zeitlimit");
                if (!EditorApplication.isPlaying || Time.time < 1f) return;
                var sc = UnityEngine.Object.FindFirstObjectByType<SkaterController>();
                sc.inputEnabled = false;
                float t = Time.time;
                switch (_stage)
                {
                    case 0: // anschieben
                        sc.InjectInput(new Vector2(0f, 1f), false);
                        if (t > 2.5f) { _stage = 1; _t0 = t; }
                        break;
                    case 1: // Ollie laden
                        sc.InjectInput(Vector2.zero, true);
                        if (t - _t0 > 0.3f) { sc.InjectInput(Vector2.zero, false); _stage = 2; }
                        break;
                    case 2: // D halten bis ~180, dann loslassen (wie ein Mensch)
                        bool spinning = sc.State == SkaterState.Air && Mathf.Abs(sc.AirSpin) < 175f;
                        sc.InjectInput(spinning ? new Vector2(1f, 0f) : Vector2.zero, false);
                        if (sc.State != SkaterState.Air && t - _t0 > 0.6f)
                        {
                            Debug.Log($"[Spin] gelandet: Drehung {sc.AirSpin:0}, Fakie {sc.Fakie}, {sc.State}");
                            _stage = 3; _t0 = t;
                            _lastAlign = sc.align.eulerAngles.y; _lastBoard = sc.boardPivot.eulerAngles.y; _lastRoot = sc.transform.eulerAngles.y;
                        }
                        break;
                    case 3: // ohne Eingabe weiterrollen und Drehung messen
                        sc.InjectInput(Vector2.zero, false);
                        _turnAlign += Mathf.Abs(Mathf.DeltaAngle(_lastAlign, sc.align.eulerAngles.y)); _lastAlign = sc.align.eulerAngles.y;
                        _turnBoard += Mathf.Abs(Mathf.DeltaAngle(_lastBoard, sc.boardPivot.eulerAngles.y)); _lastBoard = sc.boardPivot.eulerAngles.y;
                        _turnRoot += Mathf.Abs(Mathf.DeltaAngle(_lastRoot, sc.transform.eulerAngles.y)); _lastRoot = sc.transform.eulerAngles.y;
                        if (t - _t0 > 1.5f)
                        {
                            bool ok = _turnAlign < 20f && _turnBoard < 20f;
                            Debug.Log($"[Spin] nach der Landung gedreht: Skater-Wurzel {_turnRoot:0}, Align {_turnAlign:0}, Board {_turnBoard:0} Grad, Fakie {sc.Fakie} -> {(ok ? "SPIN TEST PASS" : "SPIN TEST FAIL")}");
                            EditorApplication.ExitPlaymode();
                            Quit(ok ? 0 : 1);
                        }
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
