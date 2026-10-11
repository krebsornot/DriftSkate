using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Menues mit Controller (virtuelles Gamepad im Play-Mode): in der Garage Steuerkreuz -> Auswahl mit Rahmen, runter,
    /// RB -> naechster Reiter, B -> zurueck in die Reiter-Spalte, rechts -> in den Inhalt, A auf einen Knopf.
    /// Ergebnis nach Logs/menupad/. Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.MenuGamepadTest.Run
    /// </summary>
    public static class MenuGamepadTest
    {
        static double _started;
        static int _stage, _failures;
        static float _t0;
        static Gamepad _pad;
        static StreamWriter _log;
        static bool _oldEnabled;
        static EnterPlayModeOptions _oldOptions;

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Garage.unity");
            _oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _oldOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload; // sonst geht der Ablauf verloren
            _started = EditorApplication.timeSinceStartup;
            _stage = 0;
            _failures = 0;
            Directory.CreateDirectory("Logs/menupad");
            _log = new StreamWriter("Logs/menupad/result.txt", false);
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Quit(int code)
        {
            EditorApplication.update -= Tick;
            EditorSettings.enterPlayModeOptionsEnabled = _oldEnabled;
            EditorSettings.enterPlayModeOptions = _oldOptions;
            if (_pad != null) InputSystem.RemoveDevice(_pad);
            Log(code == 0 && _failures == 0 ? "ERGEBNIS: alles OK" : $"ERGEBNIS: {_failures} Fehler");
            _log?.Close();
            _log = null;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            EditorApplication.Exit(code != 0 ? code : _failures > 0 ? 2 : 0);
        }

        static void Log(string line) { Debug.Log("[MenuPadTest] " + line); _log?.WriteLine(line); _log?.Flush(); }
        static void Check(bool ok, string what) { Log((ok ? "OK: " : "FEHLT: ") + what); if (!ok) _failures++; }

        static void Press(GamepadButton b) => InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(b));
        static void Release() => InputSystem.QueueStateEvent(_pad, new GamepadState());

        static string Selected()
        {
            var go = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (go == null) return "(nichts)";
            var t = go.GetComponentInChildren<UnityEngine.UI.Text>();
            return t != null ? t.text : go.name;
        }

        static string Tab()
        {
            var menu = UnityEngine.Object.FindAnyObjectByType<GarageMenu>();
            return menu != null ? (string)typeof(GarageMenu).GetField("_tab", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(menu) : "?";
        }

        // Taste druecken, nach kurzer Zeit loslassen, dann pruefen
        static float _pressAt = -1f;
        static bool Step(GamepadButton b, float t)
        {
            if (_pressAt < 0f) { Press(b); _pressAt = t; return false; }
            if (t - _pressAt < 0.15f) return false;
            Release();
            if (t - _pressAt < 0.45f) return false;
            _pressAt = -1f;
            return true;
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 90) throw new Exception("Zeitlimit");
                if (!EditorApplication.isPlaying || Time.unscaledTime < 1.5f) return;
                float t = Time.unscaledTime;
                switch (_stage)
                {
                    case 0:
                        _pad = InputSystem.AddDevice<Gamepad>();
                        Check(MenuGamepad.Instance != null, "Menue-Controller-Hilfe laeuft");
                        _stage = 1;
                        break;
                    case 1:
                        if (!Step(GamepadButton.DpadDown, t)) break;
                        Check(MenuGamepad.UsingNav && EventSystem.current.currentSelectedGameObject != null, $"Steuerkreuz: Auswahl da ({Selected()})");
                        _stage = 2;
                        break;
                    case 2:
                        if (!Step(GamepadButton.RightShoulder, t)) break;
                        Check(Tab() == "AUTOS", $"RB: Reiter {Tab()}, Auswahl {Selected()}");
                        _stage = 3;
                        break;
                    case 3:
                        if (!Step(GamepadButton.DpadDown, t)) break;
                        Log($"   runter: {Selected()}");
                        _stage = 4;
                        break;
                    case 4:
                        if (!Step(GamepadButton.East, t)) break;
                        Check(Selected() == "AUTOS", $"B: zurueck in die Reiter-Spalte ({Selected()})");
                        _stage = 5;
                        break;
                    case 5:
                        if (!Step(GamepadButton.DpadDown, t)) break;
                        Check(Selected() == "TUNING", $"Reiter-Spalte runter: {Selected()}");
                        _stage = 6;
                        break;
                    case 6:
                        if (!Step(GamepadButton.South, t)) break;
                        Check(Tab() == "TUNING", $"A auf den Reiter: {Tab()}");
                        _stage = 7;
                        break;
                    case 7:
                        if (!Step(GamepadButton.DpadRight, t)) break;
                        var sel = EventSystem.current.currentSelectedGameObject;
                        var menu = UnityEngine.Object.FindAnyObjectByType<GarageMenu>();
                        var content = (RectTransform)typeof(GarageMenu).GetField("_content", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(menu);
                        Check(sel != null && sel.transform.IsChildOf(content), $"Rechts: in den Inhalt ({Selected()}, {sel?.GetComponent<UnityEngine.UI.Selectable>()?.GetType().Name})");
                        _stage = 8;
                        break;
                    case 8:
                        if (!Step(GamepadButton.LeftShoulder, t)) break;
                        Check(Tab() == "AUTOS", $"LB: {Tab()}");
                        _stage = 9;
                        break;
                    case 9:
                        if (!Step(GamepadButton.LeftShoulder, t)) break;
                        Check(Tab() == "FAHREN", $"LB zurueck bis FAHREN: {Tab()}, Auswahl {Selected()}");
                        Check(Selected() == "SOLO FAHREN", "Auf FAHREN ist SOLO FAHREN ausgewaehlt");
                        Quit(0);
                        break;
                }
            }
            catch (Exception e) { Debug.LogException(e); Log("FEHLER: " + e.Message); Quit(1); }
        }
    }
}
