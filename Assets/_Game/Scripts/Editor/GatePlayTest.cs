using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Bruecke und Schranken im Play-Mode: mit dem Auto von der Strasse auf die Bruecke zufahren (Schranke muss rechtzeitig
    /// oben sein), die Auffahrt hoch (dabei die Steigung am Auto messen: hoechstens 8 Grad) und ein Stueck ueber das Deck,
    /// dann vor die Parkhaus-Einfahrt. Bilder nach Logs/gates/.
    /// Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.GatePlayTest.Run (vorher profile.json sichern)
    /// </summary>
    public static class GatePlayTest
    {
        static double _started;
        static int _stage, _failures;
        static float _t0, _maxY, _maxPitch;
        static bool _oldEnabled, _gateOpenSeen, _shot;
        static EnterPlayModeOptions _oldOptions;
        static StreamWriter _log;

        public static void Run()
        {
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            _oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _oldOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            _started = EditorApplication.timeSinceStartup;
            _stage = 0;
            _failures = 0;
            Directory.CreateDirectory("Logs/gates");
            _log = new StreamWriter("Logs/gates/result.txt", false);
            var p = SaveSystem.Profile;
            p.adminMode = false;
            p.sky = "abend";
            p.weather = "klar";
            SaveSystem.Save();
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Quit(int code)
        {
            EditorApplication.update -= Tick;
            EditorSettings.enterPlayModeOptionsEnabled = _oldEnabled;
            EditorSettings.enterPlayModeOptions = _oldOptions;
            Log(code == 0 && _failures == 0 ? "ERGEBNIS: alles OK" : $"ERGEBNIS: {_failures} Fehler");
            _log?.Close();
            _log = null;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            EditorApplication.Exit(code != 0 ? code : _failures > 0 ? 2 : 0);
        }

        static void Log(string line) { Debug.Log("[GateTest] " + line); _log?.WriteLine(line); _log?.Flush(); }
        static void Check(bool ok, string what) { Log((ok ? "OK: " : "FEHLT: ") + what); if (!ok) _failures++; }

        static void Shot(Vector3 from, Vector3 at, string file)
        {
            var cam = Camera.main;
            cam.transform.position = from;
            cam.transform.LookAt(at);
            SimTests.Capture(cam, "Logs/gates/" + file + ".png");
        }

        static AutoGate Gate(string name) => RoadGates.Gates.Find(g => g != null && g.name == name);

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 180) throw new Exception("Zeitlimit");
                if (!EditorApplication.isPlaying) return;
                var a = PlayerAvatar.Local;
                if (a == null || Time.time < 2f) return;
                float t = Time.time;
                var car = a.car;
                switch (_stage)
                {
                    case 0:
                    {
                        Check(RoadGates.BridgeOpen && RoadGates.Gates.Count == 6, $"Bruecke offen, {RoadGates.Gates.Count} Schranken-Einheiten");
                        var g = Gate("BridgeGate_A");
                        Check(g != null && g.Openness < 0.01f, "Bruecken-Schranke anfangs zu");
                        Shot(RoadGates.BridgeGate + new Vector3(-14f, 4f, 16f), RoadGates.BridgeGate + Vector3.up, "bridge_gate_closed");
                        Check(RoadGates.BridgeRampGrade < 0.12f, $"Auffahrt flach: {RoadGates.BridgeRampGrade * 100f:0.0} % Steigung, Deck ab x {RoadGates.BridgeDeckStart:0}");
                        // Auffahrt von der Seite (Profil) und von unten (Pfeiler, Gelaender, Seile)
                        float mid = (RoadGates.BridgeGate.x + RoadGates.BridgeDeckStart) * 0.5f;
                        Shot(new Vector3(mid, 9f, -95f), new Vector3(mid, 7f, 0f), "bridge_ramp_side");
                        Shot(RoadGates.BridgeGate + new Vector3(-30f, 8f, -26f), new Vector3(mid, 8f, 0f), "bridge_ramp_quay");
                        a.externalControl = true;
                        // 70 m vor der Schranke auf der Strasse, Blick zur Bruecke (Osten)
                        a.PlaceForChallenge(true, new Vector3(CityBuilder.RoadCenter(CityBuilder.Grid) - 11f, 0.3f, 0f), 90f); // aeussere Ringstrasse, Blick zur Bruecke
                        _stage = 1; _t0 = t;
                        break;
                    }
                    case 1: // Vollgas auf die Schranke zu
                    {
                        float steer = Mathf.Clamp(car.transform.position.z / 8f + Vector3.Dot(car.transform.forward, Vector3.forward) * 1.5f, -0.4f, 0.4f); // zur Mitte der Auffahrt
                        car.input = new VehicleInput { throttle = car.Speed < 16f ? 1f : 0.2f, steer = steer };
                        var g = Gate("BridgeGate_A");
                        float dx = RoadGates.BridgeGate.x - car.transform.position.x;
                        if (dx < 8f && dx > 0f && !_gateOpenSeen)
                        {
                            _gateOpenSeen = true;
                            Check(g.Openness > 0.9f, $"Schranke oben, bevor das Auto ankommt ({g.Openness:0.00} bei {dx:0.0} m, {car.SpeedKmh:0} km/h)");
                            Shot(car.transform.position + new Vector3(-9f, 3.5f, 5f), RoadGates.BridgeGate + Vector3.up, "bridge_gate_open");
                        }
                        _maxY = Mathf.Max(_maxY, car.transform.position.y);
                        if (car.transform.position.x > RoadGates.BridgeGate.x + 10f && car.Grounded)
                            _maxPitch = Mathf.Max(_maxPitch, Mathf.Asin(Mathf.Clamp(car.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg);
                        if (car.transform.position.x > RoadGates.BridgeDeckStart + 15f)
                        {
                            float y = car.transform.position.y;
                            Check(car.Grounded && y > 14.5f && y < 16.5f, $"Auf dem Bruecken-Deck: Hoehe {y:0.0} m, x {car.transform.position.x:0}, Bodenkontakt {car.Grounded}");
                            Check(_maxPitch < 8f, $"Auffahrt nicht zu steil: Auto hoechstens {_maxPitch:0.0} Grad geneigt");
                            Shot(car.transform.position + new Vector3(-16f, 6f, 9f), car.transform.position + Vector3.up, "bridge_deck");
                            car.input = new VehicleInput { brake = 1f };
                            _stage = 2; _t0 = t;
                        }
                        else if (t - _t0 > 40f) throw new Exception($"Nicht auf die Bruecke gekommen: {car.transform.position}, {car.SpeedKmh:0} km/h");
                        break;
                    }
                    case 2: // Schranke geht wieder zu, wenn keiner mehr da ist
                    {
                        car.input = new VehicleInput { brake = 1f };
                        if (t - _t0 < 5f) break;
                        var g = Gate("BridgeGate_A");
                        Check(g.Openness < 0.05f, $"Schranke wieder zu ({g.Openness:0.00})");
                        // Bruecken-Ende: Abschluss-Mauer
                        Shot(RoadGates.BridgeEnd + new Vector3(-25f, 6f, 12f), RoadGates.BridgeEnd + Vector3.up, "bridge_end");
                        // Parkhaus: vor die Ost-Einfahrt, langsam hinein
                        var pg = Gate("ParkhausGateEast_A");
                        Check(pg != null, "Parkhaus-Schranken da");
                        Vector3 lane = pg.lane;
                        a.PlaceForChallenge(true, lane + new Vector3(0f, 0.3f, 28f), 180f);
                        Shot(lane + new Vector3(9f, 3f, 10f), lane + Vector3.up, "parkhaus_gate_closed");
                        _stage = 3; _t0 = t;
                        break;
                    }
                    case 3:
                    {
                        car.input = new VehicleInput { throttle = car.Speed < 7f ? 0.6f : 0f };
                        var pg = Gate("ParkhausGateEast_A");
                        float dz = car.transform.position.z - pg.lane.z;
                        if (dz < 6f && !_shot)
                        {
                            _shot = true;
                            Check(pg.Openness > 0.9f, $"Parkhaus-Schranke oben ({pg.Openness:0.00} bei {dz:0.0} m)");
                            Shot(pg.lane + new Vector3(9f, 3f, 10f), pg.lane + Vector3.up, "parkhaus_gate_open");
                        }
                        if (dz < -6f || t - _t0 > 20f)
                        {
                            Check(dz < -6f, $"Ins Parkhaus gefahren ({dz:0.0} m hinter der Schranke)");
                            Quit(0);
                        }
                        break;
                    }
                }
            }
            catch (Exception e) { Debug.LogException(e); Log("FEHLER: " + e.Message); Quit(1); }
        }
    }
}
