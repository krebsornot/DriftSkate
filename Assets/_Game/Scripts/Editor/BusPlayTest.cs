using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Play-Mode-Test: der Schulbus faehrt seine Runde (Zeit x5) und haelt an jeder Bushaltestelle mit der Tuer an der
    /// Haltestelle (Stopp-Schild ausgeklappt), ohne aus der Spur zu fliegen; dazu wird der NPC-Verkehr gezaehlt.
    /// Bilder nach Logs/bus/. Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.BusPlayTest.Run
    /// </summary>
    public static class BusPlayTest
    {
        static double _started;
        static bool _oldEnabled, _wasStopped, _begun;
        static EnterPlayModeOptions _oldOptions;
        static int _failures, _knocks, _shots;
        static float _tBegin, _nextLog;
        static readonly HashSet<int> _served = new HashSet<int>();
        static TrafficCar.Mode _lastMode;

        public static void Run()
        {
            SaveSystem.DataFolder = System.IO.Path.GetFullPath("Logs/BusTestSave");
            System.IO.Directory.CreateDirectory(SaveSystem.DataFolder);
            System.IO.Directory.CreateDirectory("Logs/bus");
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
            Debug.Log("[Bus] " + (ok ? "OK    " : "FEHLT ") + what);
            if (!ok) _failures++;
        }

        static void Quit(int code)
        {
            Debug.Log("[Bus] " + (code == 0 ? "BUS TEST PASS" : "BUS TEST FAIL"));
            Time.timeScale = 1f;
            EditorApplication.update -= Tick;
            EditorSettings.enterPlayModeOptionsEnabled = _oldEnabled;
            EditorSettings.enterPlayModeOptions = _oldOptions;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            EditorApplication.Exit(code);
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 260) throw new Exception("Zeitlimit");
                if (!EditorApplication.isPlaying || Time.time < 2f) return;
                var traffic = Traffic.Instance;
                if (traffic == null) return;
                var bus = traffic.bus;
                if (!_begun)
                {
                    _begun = true;
                    Check(traffic.cars.Count >= 20, $"NPC-Verkehr: {traffic.cars.Count} Autos");
                    if (bus == null) throw new Exception("Kein Schulbus (Prefab fehlt oder keine Haltestellen)");
                    Check(traffic.busRoute.stopS.Length == 6, $"Bus-Runde mit {traffic.busRoute.stopS.Length} Haltestellen, {traffic.busRoute.length:0} m, {traffic.busRoute.duration:0} s pro Runde");
                    // Alle Halte an der Route pruefen: Tuer (2,7 m vor der Mitte) nah an der Haltestelle, Bus am Bordstein
                    var stopsT = GameObject.Find("BusStops").transform;
                    foreach (float st in traffic.busRoute.stopS)
                    {
                        traffic.busRoute.Sample(st, out Vector3 bp, out Vector3 bd, out _);
                        Vector3 door = Flat(bp + bd * 2.7f);
                        float best = 999f; string name = "";
                        foreach (Transform h in stopsT) { float dd = Vector3.Distance(door, Flat(h.position)); if (dd < best) { best = dd; name = h.name; } }
                        Check(best < 4.5f, $"Halt bei {name}: Tuer {best:0.0} m von der Haltestelle");
                    }
                    // Die gemeinsame Uhr laeuft auch solo in Echtzeit: keine Zeitraffer, sonst wartet der Bus auf den Fahrplan
                    _tBegin = Time.time;
                    _lastMode = bus.State;
                    return;
                }
                if (bus.State != _lastMode && bus.State == TrafficCar.Mode.Knocked)
                {
                    _knocks++;
                    traffic.busRoute.Sample(bus.RoutePosition, out Vector3 rp, out Vector3 rd, out _);
                    Debug.Log($"[Bus] aus der Spur bei s={bus.RoutePosition:0} pos={bus.transform.position} Spur={rp} Abstand {Vector3.Distance(Flat(rp), Flat(bus.transform.position)):0.0} m, Winkel {Vector3.SignedAngle(Flat(bus.transform.forward), Flat(rd), Vector3.up):0} Grad, v={bus.Speed:0.0}");
                }
                _lastMode = bus.State;

                if (Time.time - _tBegin > _nextLog)
                {
                    _nextLog += 10f;
                    float target = traffic.busRoute.DistanceAtTime(Traffic.Clock);
                    Debug.Log($"[Bus] t={Time.time - _tBegin:0} s={bus.RoutePosition:0} Soll={target:0} v={bus.Speed:0.0} {bus.State} pos={bus.transform.position}");
                }
                bool stopped = bus.AtStop;
                if (stopped && !_wasStopped)
                {
                    // Tuer (vorn rechts) an der naechsten Haltestelle?
                    var stops = GameObject.Find("BusStops").transform;
                    int bestI = -1; float best = 999f;
                    for (int i = 0; i < stops.childCount; i++)
                    {
                        float d = Vector3.Distance(Flat(stops.GetChild(i).position), Flat(bus.transform.position + bus.transform.forward * 2.7f));
                        if (d < best) { best = d; bestI = i; }
                    }
                    _served.Add(bestI);
                    Debug.Log($"[Bus] Halt an {stops.GetChild(bestI).name}: Tuer {best:0.0} m von der Haltestelle (nach {(Time.time - _tBegin):0} s Spielzeit)");
                    if (best > 4.5f) { _failures++; Debug.Log("[Bus] FEHLT Halt zu weit von der Haltestelle"); }
                    if (_shots < 3)
                    {
                        _shots++;
                        var cam = new GameObject("BusCam").AddComponent<Camera>();
                        Vector3 side = Vector3.Cross(Vector3.up, bus.transform.forward);
                        Vector3 toStop = Flat(stops.GetChild(bestI).position) - Flat(bus.transform.position);
                        if (Vector3.Dot(side, toStop) > 0f) side = -side; // von der Strassenseite, schraeg von vorn
                        cam.transform.position = bus.transform.position + side * 10f + bus.transform.forward * 8f + Vector3.up * 2.6f;
                        cam.transform.LookAt(bus.transform.position + Vector3.up * 1.6f);
                        cam.fieldOfView = 55f;
                        SimTests.Capture(cam, $"Logs/bus/stop_{_shots}.png");
                        UnityEngine.Object.Destroy(cam.gameObject);
                                            }
                }
                _wasStopped = stopped;
                if (Time.time - _tBegin > 150f)
                {
                    Check(_served.Count >= 2, $"In 150 s an {_served.Count} Haltestellen wirklich gehalten");
                    Check(_knocks == 0, $"Bus nie aus der Spur geflogen ({_knocks}x)");
                    int knocked = traffic.cars.FindAll(c => c.State != TrafficCar.Mode.Drive).Count;
                    Debug.Log($"[Bus] NPC-Autos gerade aus der Spur/zurueck: {knocked} von {traffic.cars.Count}");
                    Quit(_failures == 0 ? 0 : 1);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Bus] FEHLT: " + e.Message);
                Quit(1);
            }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
