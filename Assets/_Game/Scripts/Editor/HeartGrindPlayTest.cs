using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Rund-Grind am Herz-Denkmal wie ein Mensch: tangential anfahren, Ollie, nach 0,3 s Reaktionszeit Grind druecken und
    /// halten, Balance mit etwas Verzoegerung ausgleichen. Zweimal (beide Richtungen, Start direkt an der Naht des Rings).
    /// Prueft, dass man ohne Absteigen mindestens zwei volle Runden (720 Grad) grindet.
    /// Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.HeartGrindPlayTest.Run
    /// </summary>
    public static class HeartGrindPlayTest
    {
        const float Reaction = 0.3f, Goal = 720f;
        static double _started;
        static int _stage, _pass, _failures;
        static float _t0, _swept, _lastAngle, _ollieAt, _maxRadiusError;
        static Vector3 _center;
        static float _radius = 4.52f;
        static readonly System.Collections.Generic.Queue<(float t, float b)> _balance = new System.Collections.Generic.Queue<(float, float)>();
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
            _stage = 0;
            _pass = 0;
            _failures = 0;
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

        static float Angle(Vector3 p) => Mathf.Atan2(p.z - _center.z, p.x - _center.x) * Mathf.Rad2Deg;

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 150) throw new Exception("Zeitlimit");
                if (!EditorApplication.isPlaying) return;
                var a = PlayerAvatar.Local;
                if (a == null || Time.time < 2f) return;
                var sc = a.skater;
                float t = Time.time;
                // Richtung: Pass 0 im Ring-Sinn (Start bei Winkel 0 nach +Z), Pass 1 dagegen (nach -Z)
                float dirZ = _pass == 0 ? 1f : -1f;
                switch (_stage)
                {
                    case 0:
                    {
                        var m = GameObject.Find("HeartMonument");
                        if (m == null) throw new Exception("HeartMonument fehlt in der Stadt");
                        _center = m.transform.position;
                        foreach (var r in GrindRail.All)
                            if (r.label == "HEART GRIND") { r.Closest(_center, out var p, out _); _radius = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(_center.x, _center.z)); }
                        a.externalControl = true;
                        sc.inputEnabled = false;
                        // Anlauf auf einer Linie knapp ausserhalb des Rings, tangential zur Naht (Winkel 0)
                        a.PlaceForChallenge(false, _center + new Vector3(_radius + 0.6f, 0.05f, -dirZ * 14f), dirZ > 0 ? 0f : 180f);
                        _stage = 1; _t0 = t; _swept = 0f; _maxRadiusError = 0f; _balance.Clear();
                        Debug.Log($"[HeartGrind] Pass {_pass}: Mitte {_center}, Ring r={_radius:0.00} m");
                        break;
                    }
                    case 1: // anschieben, Ollie rund 2,5 m vor der Naht laden
                        sc.InjectInput(new Vector2(0f, 1f), false);
                        if ((sc.transform.position.z - _center.z) * dirZ > -2.8f - sc.Speed * 0.3f) { _stage = 2; _t0 = t; }
                        if (t - _t0 > 8f) throw new Exception("Anlauf zu lang, Position " + sc.transform.position);
                        break;
                    case 2: // Ollie laden und loslassen
                        sc.InjectInput(new Vector2(0f, 1f), t - _t0 < 0.25f);
                        if (t - _t0 >= 0.25f) { _stage = 3; _t0 = t; _ollieAt = t; }
                        break;
                    case 3: // Reaktionszeit, dann Grind halten (W noch gedrueckt vom Anlauf)
                        bool grind = t - _ollieAt > Reaction;
                        sc.InjectInput(new Vector2(0f, grind ? 0f : 1f), false, grind: grind);
                        if (sc.State == SkaterState.Grinding)
                        {
                            Debug.Log($"[HeartGrind] Grind gestartet nach {t - _ollieAt:0.00} s bei Winkel {Angle(sc.transform.position):0}, {sc.CurrentTrick}");
                            _lastAngle = Angle(sc.transform.position);
                            _stage = 4; _t0 = t;
                        }
                        else if (t - _t0 > 2f) throw new Exception("Kein Grind, Zustand " + sc.State + " bei " + sc.transform.position);
                        break;
                    case 4: // grinden: Balance mit 0,25 s Verzoegerung gegenhalten, Winkel aufsummieren
                    {
                        _balance.Enqueue((t, sc.Balance));
                        float seen = 0f;
                        while (_balance.Count > 0 && t - _balance.Peek().t > 0.25f) seen = _balance.Dequeue().b;
                        sc.InjectInput(new Vector2(Mathf.Clamp(seen * 3f, -1f, 1f), 0f), false, grind: true);
                        var pos = sc.transform.position;
                        float ang = Angle(pos);
                        _swept += Mathf.Abs(Mathf.DeltaAngle(_lastAngle, ang));
                        _lastAngle = ang;
                        float rErr = Mathf.Abs(Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(_center.x, _center.z)) - _radius);
                        _maxRadiusError = Mathf.Max(_maxRadiusError, rErr);
                        if (sc.State != SkaterState.Grinding)
                        {
                            Debug.LogError($"[HeartGrind] FEHLT: Pass {_pass} nach {_swept:0} Grad abgebrochen ({sc.State}) bei Winkel {ang:0}");
                            _failures++;
                            Next();
                        }
                        else if (_swept >= Goal)
                        {
                            Debug.Log($"[HeartGrind] OK: Pass {_pass} {_swept:0} Grad in {t - _t0:0.0} s ohne Absteigen, Tempo {sc.Speed * 3.6f:0} km/h, " +
                                      $"Abstand zum Ring max {_maxRadiusError:0.000} m, Hoehe {pos.y:0.00} m");
                            Next();
                        }
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[HeartGrind] FEHLT: " + e.Message);
                Quit(1);
            }
        }

        static void Next()
        {
            _pass++;
            _stage = 0;
            if (_pass >= 2) Quit(_failures == 0 ? 0 : 1);
        }
    }
}
