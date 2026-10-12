using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Play-Mode-Test der Ampeln: je Kreuzung wenige zusammengefasste Meshes (Einzelteile aus), alle Lichter mit dem
    /// laufenden Schalt-Material, die Signal-Textur folgt der gemeinsamen Uhr und schaltet um (Bilder in zwei Phasen
    /// nach Logs/ampeln/). Danach 45 s Verkehr beobachten: NPC-Autos halten an roten Ampeln vor der Haltelinie, keins
    /// faehrt bei Rot ueber die Linie, und nach Gruen fahren sie weiter (Bild eines wartenden Autos). Ohne Fehler im Log.
    /// Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.TrafficLightPlayTest.Run
    /// </summary>
    public static class TrafficLightPlayTest
    {
        static double _started;
        static bool _failed, _oldEnabled, _merged, _material, _matches = true, _switched;
        static EnterPlayModeOptions _oldOptions;
        static int _stage, _firstState = -1, _crossings, _renderers;
        static float _t0, _watch;
        static int _waited, _crossedRed, _movedAfterGreen;
        static bool _carShot;
        static readonly System.Collections.Generic.Dictionary<TrafficCar, (int line, float s, bool wasRed, bool waited)> _track = new System.Collections.Generic.Dictionary<TrafficCar, (int, float, bool, bool)>();

        public static void Run()
        {
            SaveSystem.DataFolder = System.IO.Path.GetFullPath("Logs/AmpelTestSave"); // Fynns Spielstand bleibt unberuehrt
            System.IO.Directory.CreateDirectory(SaveSystem.DataFolder);
            System.IO.Directory.CreateDirectory("Logs/ampeln");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            _oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _oldOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            // Unitys Such-Indexer wirft in frischen Projektkopien eine ArgumentOutOfRangeException: nicht unser Fehler
            Application.logMessageReceived += (m, t, type) => { if ((type == LogType.Exception || type == LogType.Error) && (t == null || !t.Contains("UnityEditor.Search"))) _failed = true; };
            _started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup - _started > 120) { Debug.LogError("Zeitlimit"); Finish(); return; }
            if (!EditorApplication.isPlaying || Time.time < 2f) return;
            var signals = Object.FindAnyObjectByType<TrafficSignals>();
            if (signals == null) { Debug.LogError("[AmpelTest] TrafficSignals fehlt"); Finish(); return; }

            if (_stage == 0)
            {
                _merged = true;
                _material = true;
                foreach (Transform crossing in signals.transform)
                {
                    _crossings++;
                    int active = 0;
                    foreach (var r in crossing.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (!r.enabled) continue;
                        active++;
                        if (!r.name.StartsWith("Merged_")) _merged = false;
                        if (r.sharedMaterial.name.Contains("Lamps") && r.sharedMaterial.GetTexture("_BaseMap") != signals.StateTexture) _material = false;
                    }
                    _renderers += active;
                    if (active == 0 || active > 4) _merged = false;
                }
                _stage = 1;
                _t0 = Time.time;
                Shot("play_phase_a");
                return;
            }

            // Textur gegen die Uhr pruefen, bis einmal umgeschaltet wurde (spaetestens nach einer halben Runde)
            double time = SharedClock.Time;
            int sx = TrafficSignals.State(time, false, out bool wx), sz = TrafficSignals.State(time, true, out bool wz);
            var expect = TrafficSignals.Pixels(sx, wx, sz, wz);
            var px = signals.StateTexture.GetPixels();
            for (int i = 0; i < px.Length; i++)
                if (Mathf.Abs(px[i].r - expect[i].r) + Mathf.Abs(px[i].g - expect[i].g) > 0.05f) _matches = false;
            int key = sx * 10 + sz;
            if (_firstState < 0) _firstState = key;
            else if (key != _firstState && !_switched)
            {
                _switched = true;
                _t0 = Time.time;
            }
            if (_stage == 1)
            {
                if (_switched && Time.time - _t0 > 0.5f) { Shot("play_phase_b"); _stage = 2; _watch = Time.time; return; }
                if (Time.time - _t0 > TrafficSignals.Cycle * 0.6f) { Debug.LogError("[AmpelTest] Ampeln haben nicht umgeschaltet"); Finish(); }
                return;
            }
            WatchTraffic(time);
            if (Time.time - _watch > 45f) Finish();
        }

        /// <summary>NPC-Autos an den Haltelinien beobachten (Fahrplan-Soll und echtes Auto).</summary>
        static void WatchTraffic(double time)
        {
            var traffic = Traffic.Instance;
            if (traffic == null) return;
            foreach (var car in traffic.cars)
            {
                if (car == null || car.State != TrafficCar.Mode.Drive) continue;
                var sched = car.Sched;
                var route = sched.route;
                if (route.lineS.Length == 0) continue;
                // naechste Haltelinie vor dem Auto
                float s = car.RoutePosition, best = float.MaxValue;
                int li = -1;
                for (int i = 0; i < route.lineS.Length; i++)
                {
                    float d = Mathf.Repeat(route.lineS[i] - s, route.length);
                    if (d < best) { best = d; li = i; }
                }
                bool red = TrafficSignals.State(time, route.lineZ[li], out _) == 2;
                bool waited = false;
                if (_track.TryGetValue(car, out var prev) && prev.line == li)
                {
                    waited = prev.waited;
                    // Linie bei Rot ueberfahren? (vorher davor, jetzt dahinter, und schon vorher war Rot)
                    float before = Mathf.Repeat(route.lineS[li] - prev.s, route.length);
                    if (prev.wasRed && red && before < 6f && before > 0.3f && best > route.length - 6f) _crossedRed++;
                    // Hat an dieser Linie gewartet und faehrt jetzt bei Gruen wieder
                    if (waited && !red && car.Speed > 2f) { _movedAfterGreen++; waited = false; }
                }
                if (red && car.Speed < 0.4f && best < 4f)
                {
                    _waited++;
                    waited = true;
                    if (!_carShot)
                    {
                        _carShot = true;
                        var cam = Camera.main;
                        if (cam != null)
                        {
                            var t = cam.transform;
                            Vector3 pos = t.position; Quaternion rot = t.rotation;
                            Vector3 side = Vector3.Cross(Vector3.up, car.transform.forward);
                            t.position = car.transform.position - car.transform.forward * 9f + side * 7f + Vector3.up * 2.6f;
                            t.LookAt(car.transform.position + car.transform.forward * 12f + Vector3.up * 2.5f);
                            SimTests.Capture(cam, "Logs/ampeln/play_car_waiting.png");
                            t.SetPositionAndRotation(pos, rot);
                        }
                    }
                }
                _track[car] = (li, s, red, waited);
            }
        }

        /// <summary>Fahrersicht auf die Kreuzung am Herz-Platz (Richtung +Z, rechte Spur).</summary>
        static void Shot(string file)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var crossing = GameObject.Find("TrafficLights/Crossing_22");
            if (crossing == null) return;
            Vector3 c = crossing.transform.position;
            var t = cam.transform;
            Vector3 pos = t.position;
            Quaternion rot = t.rotation;
            t.position = c + new Vector3(4.2f, 1.6f, -32f);
            t.LookAt(c + new Vector3(4f, 4.5f, 0f));
            SimTests.Capture(cam, "Logs/ampeln/" + file + ".png");
            t.SetPositionAndRotation(pos, rot);
        }

        static void Finish()
        {
            bool cars = _waited > 30 && _crossedRed == 0 && _movedAfterGreen > 0;
            bool ok = !_failed && _crossings == 16 && _merged && _material && _matches && _switched && cars;
            Debug.Log($"[AmpelTest] Kreuzungen {_crossings}, Renderer {_renderers}, zusammengefasst {_merged}, Schalt-Material {_material}, " +
                      $"Textur passt zur Uhr {_matches}, umgeschaltet {_switched}, Fehler {_failed}");
            Debug.Log($"[AmpelTest] Verkehr: {_waited} Frames mit wartendem Auto an Rot, {_crossedRed}x bei Rot ueber die Linie, {_movedAfterGreen} Frames Weiterfahrt nach Gruen -> {(cars ? "OK" : "FEHLT")}");
            Debug.Log(ok ? "AMPEL TEST PASS" : "AMPEL TEST FAIL");
            EditorApplication.update -= Tick;
            EditorApplication.ExitPlaymode();
            EditorSettings.enterPlayModeOptionsEnabled = _oldEnabled;
            EditorSettings.enterPlayModeOptions = _oldOptions;
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
