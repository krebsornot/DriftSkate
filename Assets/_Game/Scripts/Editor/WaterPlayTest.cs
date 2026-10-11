using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Wasser im Play-Mode: (1) Wellen unter der Bruecke und am Kai fotografieren, (2) mit dem Auto die Bootsrampe am
    /// West-Kai runter: Platscher, das Auto schwimmt und schaukelt (Hoehe folgt dem Wasser), paddelt ein Stueck, saeuft
    /// nach ein paar Sekunden ab und steht wieder auf festem Boden; (3) Skater springt vom Kai: Platscher, treibt,
    /// kommt zurueck. Bilder und Ergebnis nach Logs/water/.
    /// Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.WaterPlayTest.Run (vorher profile.json sichern)
    /// </summary>
    public static class WaterPlayTest
    {
        static double _started;
        static int _stage, _failures;
        static float _t0, _minY, _maxY, _shots;
        static bool _oldEnabled, _splashSeen;
        static int _ringsSeen;
        static Vector3 _entry;
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
            _stage = 0; _failures = 0; _shots = 0f; _ringsSeen = 0; _splashSeen = false;
            Directory.CreateDirectory("Logs/water");
            _log = new StreamWriter("Logs/water/result.txt", false);
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

        static void Log(string line) { Debug.Log("[WaterTest] " + line); _log?.WriteLine(line); _log?.Flush(); }
        static void Check(bool ok, string what) { Log((ok ? "OK: " : "FEHLT: ") + what); if (!ok) _failures++; }

        static void Shot(Vector3 from, Vector3 at, string file, float fov = 60f)
        {
            var cam = Camera.main;
            float old = cam.fieldOfView;
            cam.fieldOfView = fov;
            cam.transform.position = from;
            cam.transform.LookAt(at);
            SimTests.Capture(cam, "Logs/water/" + file + ".png");
            cam.fieldOfView = old;
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 240) throw new Exception("Zeitlimit");
                if (!EditorApplication.isPlaying) return;
                var a = PlayerAvatar.Local;
                if (a == null || Time.time < 2.5f) return;
                float t = Time.time;
                var car = a.car;
                var water = WaterSurface.Instance;
                float half = CityBuilder.Half;
                switch (_stage)
                {
                    case 0:
                    {
                        Check(water != null && water.enabled, "Wasser da (WaterSurface mit Material)");
                        Check(HarborSlipway.Built, "Bootsrampe am West-Kai gebaut");
                        float h0 = WaterSurface.HeightAt(new Vector3(300f, 0f, 20f)), h1 = WaterSurface.HeightAt(new Vector3(300f, 0f, 20f) + Vector3.right * 9f);
                        Check(Mathf.Abs(h0 - WaterSurface.Level) < 0.6f && Mathf.Abs(h0 - h1) > 0.001f, $"Wellen: Hoehe {h0:0.00} / {h1:0.00} m");
                        // Bilder: unter der Bruecke, Kai mit Pfeilern, Bootsrampe
                        Shot(new Vector3(RoadGates.BridgeDeckStart + 60f, 4f, -34f), new Vector3(RoadGates.BridgeDeckStart + 90f, 8f, 10f), "bridge_water", 62f);
                        Shot(new Vector3(half + 1f, 2.2f, 24f), new Vector3(half + 60f, 2f, -10f), "quay_water", 62f);
                        Shot(new Vector3(-half + 12f, 3.5f, 14f), new Vector3(-half - 8f, -1f, 0f), "slipway", 62f);
                        a.externalControl = true;
                        // Auto 30 m vor der Rampe auf dem Kai/Ringstrasse, Blick nach Westen
                        a.PlaceForChallenge(true, new Vector3(-half + 26f, 0.3f, 0f), -90f);
                        _stage = 1; _t0 = t; _minY = 99f; _maxY = -99f;
                        break;
                    }
                    case 1: // Gas, die Rampe runter
                    {
                        float steer = Mathf.Clamp(-car.transform.position.z / 6f, -0.3f, 0.3f);
                        car.input = new VehicleInput { throttle = car.Speed < 11f ? 1f : 0.1f, steer = steer };
                        var fl = car.GetComponent<Floater>();
                        if (fl != null && fl.InWater)
                        {
                            _entry = car.transform.position;
                            Log($"Auto im Wasser nach {t - _t0:0.0} s bei {_entry}, {car.SpeedKmh:0} km/h");
                            Check(GameObject.Find("SplashRing") != null, "Platscher-Ring auf dem Wasser");
                            _stage = 2; _t0 = t;
                        }
                        else if (t - _t0 > 15f) throw new Exception($"Auto nicht ins Wasser gekommen: {car.transform.position}");
                        break;
                    }
                    case 2: // schwimmen, schaukeln, paddeln
                    {
                        car.input = new VehicleInput { throttle = 0.6f, steer = 0.2f };
                        Vector3 p = car.transform.position;
                        _minY = Mathf.Min(_minY, p.y); _maxY = Mathf.Max(_maxY, p.y);
                        if (t - _t0 > 0.6f && _shots < 1f) { _shots = 1f; Shot(p + new Vector3(10f, 3f, -9f), p + Vector3.up * 0.5f, "car_splash", 62f); }
                        if (t - _t0 > 3.5f && _shots < 2f) { _shots = 2f; Shot(p + new Vector3(9f, 2.5f, 8f), p + Vector3.up * 0.3f, "car_floating", 62f); }
                        if (t - _t0 > 4.5f)
                        {
                            float wl = WaterSurface.HeightAt(p);
                            Check(p.y > wl - 1.2f && p.y < wl + 1.2f, $"Auto schwimmt nahe der Oberflaeche (Auto {p.y:0.00} m, Wasser {wl:0.00} m)");
                            Check(_maxY - _minY > 0.08f, $"Auto schaukelt ({(_maxY - _minY):0.00} m Hub)");
                            float drift = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(_entry.x, 0f, _entry.z));
                            Check(drift > 2f, $"Auto bewegt sich im Wasser ({drift:0.0} m seit dem Eintauchen)");
                            Check(car.transform.up.y > 0.7f, $"Auto liegt aufrecht im Wasser (up.y {car.transform.up.y:0.00})");
                            _stage = 3; _t0 = t;
                        }
                        break;
                    }
                    case 3: // absaufen und zurueck auf festen Boden
                    {
                        car.input = default;
                        Vector3 p = car.transform.position;
                        if (p.y > WaterSurface.Level + 1f && t - _t0 > 2f)
                        {
                            Check(true, $"Auto wieder an Land bei {p} nach {t - _t0:0.0} s");
                            Shot(p + new Vector3(6f, 3f, -8f), p + Vector3.up * 0.5f, "car_back", 62f);
                            // Skater: auf dem Board die Bootsrampe runter
                            a.PlaceForChallenge(false, new Vector3(-half + 9f, 0.2f, 0f), -90f);
                            _stage = 4; _t0 = t;
                        }
                        else if (t - _t0 > Floater.FloatSeconds + Floater.SinkSeconds + 6f) throw new Exception($"Auto kommt nicht zurueck: {p}");
                        break;
                    }
                    case 4: // Skater rollt zum Kai und ueber die Kante der Rampe
                    {
                        var sc = a.skater;
                        sc.InjectInput(new Vector2(0f, 1f), false);
                        Vector3 p = sc.transform.position;
                        if (sc.State == SkaterState.Bailed && p.y < WaterSurface.Level + 0.5f)
                        {
                            Log($"Skater im Wasser nach {t - _t0:0.0} s bei {p}");
                            _stage = 5; _t0 = t;
                        }
                        else if (t - _t0 > 12f)
                        {
                            // Zur Not direkt ueber dem Wasser absetzen (dann zaehlt nur das Treiben)
                            Log($"Skater kam nicht von selbst ins Wasser ({p}), wird ueber dem Wasser abgesetzt");
                            sc.Place(new Vector3(-half - 24f, 1.5f, 3f), -90f);
                            _stage = 5; _t0 = t;
                        }
                        break;
                    }
                    case 5: // treiben und zurueck
                    {
                        var sc = a.skater;
                        sc.InjectInput(Vector2.zero, false);
                        Vector3 p = sc.transform.position;
                        if (sc.State == SkaterState.Bailed && p.y < WaterSurface.Level + 0.6f && _shots < 3f)
                        {
                            _shots = 3f;
                            Shot(p + new Vector3(5f, 2f, -5f), p, "skater_floating", 60f);
                            float wl = WaterSurface.HeightAt(p);
                            Check(p.y > wl - 0.8f && p.y < wl + 0.5f && p.x < -half - 10f, $"Skater treibt auf dem Wasser ({p}, Wasser {wl:0.00} m)");
                        }
                        if (_shots >= 3f && sc.State == SkaterState.Riding && p.y > WaterSurface.Level + 1f)
                        {
                            Check(true, $"Skater wieder auf festem Boden bei {p}");
                            Quit(0);
                        }
                        else if (t - _t0 > 14f) throw new Exception($"Skater kommt nicht zurueck: {sc.State} bei {p}");
                        break;
                    }
                }
            }
            catch (Exception e) { Debug.LogException(e); Log("FEHLER: " + e.Message); Quit(1); }
        }
    }
}
