using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Parkhaus mit dem Auto: (1) von der Strasse durch die Einfahrt auf die Ringstrasse (gerade, halbes Gas),
    /// (2) vom Tor auf P1 in die Spirale und mit Lenkung entlang der Mittellinie bis auf P3 hoch. Prueft, dass das Auto
    /// nirgends haengen bleibt und auf der richtigen Hoehe faehrt. Bilder nach Logs/parkhaus/.
    /// Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.ParkhausPlayTest.Run
    /// </summary>
    public static class ParkhausPlayTest
    {
        static double _started;
        static int _stage, _failures;
        static float _t0, _maxY, _slowTime, _dir;
        static Transform _p;
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
            _stage = 0; _failures = 0;
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
            Debug.Log("[ParkhausTest] " + (ok ? "OK: " : "FEHLT: ") + what);
            if (!ok) _failures++;
        }

        static void Shot(string file)
        {
            System.IO.Directory.CreateDirectory("Logs/parkhaus");
            SimTests.Capture(Camera.main, "Logs/parkhaus/drive_" + file + ".png");
        }

        static float Floor(Vector3 local)
        {
            Vector3 w = _p.TransformPoint(local);
            return Physics.Raycast(w + Vector3.up * 2.5f, Vector3.down, out RaycastHit h, 6f, ~LayerMask.GetMask("Car", "Skater", "Ignore Raycast", "Rail"), QueryTriggerInteraction.Ignore)
                ? _p.InverseTransformPoint(h.point).y : -99f;
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 150) throw new Exception("Zeitlimit");
                if (!EditorApplication.isPlaying) return;
                var a = PlayerAvatar.Local;
                if (a == null || Time.time < 2f) return;
                float t = Time.time;
                if (_p == null) _p = GameObject.Find("Parkhaus")?.transform;
                if (_p == null) throw new Exception("Parkhaus fehlt in der Stadt");
                var car = a.car;
                Vector3 local = _p.InverseTransformPoint(car.transform.position);
                float speed = car.Body.linearVelocity.magnitude;
                switch (_stage)
                {
                    case 0:
                        a.externalControl = true;
                        a.skater.inputEnabled = false;
                        a.PlaceForChallenge(true, _p.TransformPoint(new Vector3(18f, 0.1f, 52f)), 180f);
                        _stage = 1; _t0 = t;
                        break;
                    case 1: // Einfahrt: geradeaus, halbes Gas
                        car.input = new VehicleInput { throttle = t - _t0 < 3.5f ? 0.6f : 0f, brake = t - _t0 > 3.5f ? 0.6f : 0f };
                        if (Mathf.Abs(t - _t0 - 2.6f) < 0.02f) Shot("entrance");
                        if (local.z < 12f || t - _t0 > 7f)
                        {
                            Check(local.z < 24f, $"Durch die Einfahrt bis auf die Ringstrasse (z {local.z:0.0}, Tempo bei der Einfahrt ok)");
                            Check(Mathf.Abs(local.y - 0.12f) < 0.2f, $"Auto faehrt auf Bodenhoehe (y {local.y:0.00})");
                            // ins Tor auf P1 (Westseite) setzen, Richtung Spirale
                            float up = Floor(new Vector3(-12.5f, 0f, 4f)), down = Floor(new Vector3(-12.5f, 0f, -4f));
                            _dir = down > up ? 1f : -1f; // Winkel waechst Richtung z-: dort steigt die Spirale an -> +1
                            Debug.Log($"[ParkhausTest] Spirale am Tor: Boden z+4 {up:0.00}, z-4 {down:0.00} -> Richtung {_dir}");
                            a.PlaceForChallenge(true, _p.TransformPoint(new Vector3(-26f, 0.3f, 0f)), 90f);
                            _stage = 2; _t0 = t; _maxY = 0f; _slowTime = 0f;
                        }
                        break;
                    case 2: // Spirale hoch: Ziel 7 m voraus auf der Mittellinie (Radius 12,5 m)
                    {
                        Vector2 flat = new Vector2(local.x, local.z);
                        float r = flat.magnitude;
                        Vector3 target;
                        if (r > 17f) target = new Vector3(-12.5f, 0f, -_dir * 2f); // erst ins Tor, leicht zur ansteigenden Seite
                        else
                        {
                            float ang = Mathf.Atan2(local.z, local.x) + _dir * 7f / 12.5f;
                            target = new Vector3(Mathf.Cos(ang) * 12.5f, 0f, Mathf.Sin(ang) * 12.5f);
                        }
                        Vector3 to = _p.TransformDirection(target - new Vector3(local.x, 0f, local.z));
                        float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                        float steer = Mathf.Clamp(Mathf.DeltaAngle(car.transform.eulerAngles.y, want) / 30f, -1f, 1f);
                        car.input = new VehicleInput { throttle = speed < 9f ? 0.55f : 0f, brake = speed > 12f ? 0.3f : 0f, steer = steer };
                        _maxY = Mathf.Max(_maxY, local.y);
                        _slowTime = t - _t0 > 3f && speed < 1.5f ? _slowTime + Time.deltaTime : 0f;
                        if (Mathf.Abs(t - _t0 - 6f) < 0.02f) Shot("helix_p1");
                        if (_slowTime > 2.5f) throw new Exception($"Auto haengt in der Spirale fest bei {local} (Tempo {speed:0.0})");
                        if (local.y > 13f && r < 17.5f)
                        {
                            Check(true, $"Spirale bis P3 hochgefahren in {t - _t0:0.0} s (Hoehe {local.y:0.0} m)");
                            Shot("helix_p3");
                            Quit(_failures == 0 ? 0 : 1);
                        }
                        else if (t - _t0 > 60f) throw new Exception($"Nach 60 s nur bis {_maxY:0.0} m gekommen, jetzt bei {local}");
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[ParkhausTest] FEHLT: " + e.Message);
                Quit(1);
            }
        }
    }
}
