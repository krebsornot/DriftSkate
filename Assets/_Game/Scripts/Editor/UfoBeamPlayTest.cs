using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Play-Mode-Test: bei Nacht geradeaus in den Traktorstrahl des UFOs fahren (erst Auto, dann Board). Man muss ca. 50 m
    /// ueber den Einstieg fliegen, am hoechsten Punkt noch in Fahrtrichtung unterwegs sein und darf beim Zurueckfallen in
    /// den Strahl nicht noch einmal hochgehen. Bilder nach Logs/ufo/.
    /// Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.UfoBeamPlayTest.Run
    /// </summary>
    public static class UfoBeamPlayTest
    {
        static double _started;
        static bool _oldEnabled;
        static EnterPlayModeOptions _oldOptions;
        static int _stage, _pass, _failures, _liftsBefore;
        static float _t0, _entrySpeed, _startY, _maxY;
        static Vector3 _dir, _startPos, _apexVel, _apexPos;
        static bool _launched, _shot;

        public static void Run()
        {
            SaveSystem.DataFolder = System.IO.Path.GetFullPath("Logs/UfoBeamTestSave"); // Fynns Spielstand bleibt unberuehrt
            System.IO.Directory.CreateDirectory(SaveSystem.DataFolder);
            System.IO.Directory.CreateDirectory("Logs/ufo");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            _oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _oldOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            _started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Check(bool ok, string what)
        {
            Debug.Log("[UfoBeam] " + (ok ? "OK    " : "FEHLT ") + what);
            if (!ok) _failures++;
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 180) throw new Exception("Zeitlimit");
                if (!EditorApplication.isPlaying) return;
                var a = PlayerAvatar.Local;
                var ufo = UnityEngine.Object.FindAnyObjectByType<Ufo>();
                if (a == null || ufo == null || WeatherSystem.Instance == null || Time.time < 2f) return;
                var sc = a.skater;
                float t = Time.time;
                bool car = _pass == 0;
                Vector3 Pos() => car ? a.car.transform.position : sc.transform.position;
                Vector3 Vel() => car ? a.car.Body.linearVelocity : sc.GetComponent<Rigidbody>().linearVelocity;

                switch (_stage)
                {
                    case 0: // Nacht, auf den naechsten Strahl warten (frisch angegangen, damit genug Zeit bleibt)
                        WeatherSystem.Instance.SetSky(ufo.nightSky);
                        a.externalControl = true;
                        sc.inputEnabled = false;
                        if (ufo.BeamActive) break; // laufender Strahl: auf den naechsten warten
                        _stage = 1;
                        break;
                    case 1:
                    {
                        if (!ufo.BeamActive) break;
                        Vector3 g = ufo.BeamGround;
                        _dir = Vector3.right;
                        float back = car ? 22f : 14f;
                        a.PlaceForChallenge(car, new Vector3(g.x, 0.1f, g.z) - _dir * back, 90f);
                        _liftsBefore = ufo.Lifts;
                        _launched = false; _shot = false; _maxY = float.MinValue;
                        Debug.Log($"[UfoBeam] Pass {(car ? "Auto" : "Board")}: Strahl bei {g}, Start {back} m davor");
                        _stage = 2; _t0 = t;
                        break;
                    }
                    case 2: // geradeaus anfahren
                    {
                        if (car) a.car.input = new VehicleInput { throttle = 1f };
                        else sc.InjectInput(new Vector2(0f, 1f), false);
                        Vector3 v = Vel();
                        if (ufo.Lifts > _liftsBefore)
                        {
                            _startPos = Pos();
                            _startY = _startPos.y;
                            _entrySpeed = Vector3.ProjectOnPlane(v, Vector3.up).magnitude;
                            _launched = true;
                            Debug.Log($"[UfoBeam] hochgeschleudert bei {_startPos}, Geschwindigkeit {v} (vy {v.y:0.0} m/s)");
                            _stage = 3; _t0 = t;
                        }
                        else _entrySpeed = Vector3.ProjectOnPlane(v, Vector3.up).magnitude;
                        if (t - _t0 > 8f) throw new Exception("Nicht in den Strahl gekommen, Spieler bei " + Pos());
                        break;
                    }
                    case 3: // fliegen: hoechsten Punkt und Tempo dort messen
                    {
                        if (car) a.car.input = default; else sc.InjectInput(Vector2.zero, false);
                        Vector3 p = Pos();
                        if (p.y > _maxY) { _maxY = p.y; _apexVel = Vel(); _apexPos = p; }
                        if (!_shot && t - _t0 > 1.2f) { Shot(a, car ? "beam_car" : "beam_board", p); _shot = true; }
                        bool down = t - _t0 > 2f && (car ? Mathf.Abs(Vel().y) < 0.5f && p.y < _startY + 2f : sc.State != SkaterState.Air);
                        if (down)
                        {
                            string who = car ? "Auto" : "Board";
                            float rise = _maxY - _startY;
                            Vector3 flat = Vector3.ProjectOnPlane(_apexVel, Vector3.up);
                            Vector3 moved = Vector3.ProjectOnPlane(_apexPos - _startPos, Vector3.up);
                            Check(rise > 44f && rise < 56f, $"{who} fliegt {rise:0.0} m hoch (Ziel 50 m)");
                            Check(flat.magnitude > _entrySpeed * (car ? 0.55f : 0.85f), $"{who} behaelt Tempo: rein mit {_entrySpeed * 3.6f:0} km/h, am hoechsten Punkt {flat.magnitude * 3.6f:0} km/h");
                            Check(Vector3.Angle(moved, _dir) < 15f && moved.magnitude > 10f, $"{who} fliegt in Fahrtrichtung weiter ({moved.magnitude:0} m bis zum hoechsten Punkt, Abweichung {Vector3.Angle(moved, _dir):0} Grad)");
                            _stage = 4; _t0 = t;
                        }
                        else if (t - _t0 > 14f) throw new Exception("Nicht wieder gelandet, Spieler bei " + p);
                        break;
                    }
                    case 4: // nach der Landung: kein zweites Hochgehen
                        if (t - _t0 < 1.5f) break;
                        Check(ufo.Lifts == _liftsBefore + 1, $"{(car ? "Auto" : "Board")} nur einmal hochgeschleudert ({ufo.Lifts - _liftsBefore}x)");
                        _pass++;
                        if (_pass >= 2) { Quit(_failures == 0 ? 0 : 1); return; }
                        _stage = 0;
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[UfoBeam] FEHLT: " + e.Message);
                Quit(1);
            }
        }

        static void Shot(PlayerAvatar a, string file, Vector3 target)
        {
            var cam = new GameObject("BeamCam").AddComponent<Camera>();
            cam.transform.position = target + new Vector3(-10f, -14f, -30f);
            cam.transform.LookAt(target + Vector3.down * 8f);
            cam.fieldOfView = 60f;
            SimTests.Capture(cam, "Logs/ufo/" + file + ".png");
            UnityEngine.Object.Destroy(cam.gameObject);
        }

        static void Quit(int code)
        {
            Debug.Log("[UfoBeam] " + (code == 0 ? "UFO BEAM TEST PASS" : "UFO BEAM TEST FAIL"));
            EditorApplication.update -= Tick;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            EditorSettings.enterPlayModeOptionsEnabled = _oldEnabled;
            EditorSettings.enterPlayModeOptions = _oldOptions;
            EditorApplication.Exit(code);
        }
    }
}
