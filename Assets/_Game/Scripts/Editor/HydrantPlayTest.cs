using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Hydranten wie ein Mensch: (1) auf dem Board vom Gehweg aus ueber einen Hydranten rollen (nur anschieben),
    /// (2) mit dem Auto von der Strasse aus drueberfahren (Vollgas). Jeweils: Hydrant bricht, Fontaene schleudert hoch,
    /// Landung auf dem Dach, man steht/faehrt oben und steckt nicht im Dach (Hitbox). Bilder nach Logs/hydrant/.
    /// Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.HydrantPlayTest.Run
    /// </summary>
    public static class HydrantPlayTest
    {
        static double _started;
        static int _stage, _failures, _pass;
        static float _t0, _maxY;
        static FireHydrant _h;
        static Vector3 _landing, _toRoof;
        static bool _launched;
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
            _stage = 0; _pass = 0; _failures = 0;
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
            Debug.Log("[Hydrant] " + (ok ? "OK: " : "FEHLT: ") + what);
            if (!ok) _failures++;
        }

        static void Shot(string file)
        {
            System.IO.Directory.CreateDirectory("Logs/hydrant");
            SimTests.Capture(Camera.main, "Logs/hydrant/" + file + ".png");
        }

        /// <summary>Hydrant mit Dach in der Naehe, dessen Weg (Gehweg bzw. Strasse) frei ist.</summary>
        static FireHydrant Pick(bool car)
        {
            int skip = car ? 1 : 0;
            foreach (var h in FireHydrant.All)
            {
                var l = h.PreviewLanding();
                if (!l.HasValue) continue;
                Vector3 hp = h.transform.position;
                Vector3 dir = new Vector3(l.Value.x - hp.x, 0f, l.Value.z - hp.z).normalized;
                // Weg pruefen: Skater kommt von der Hauswand her (3,5 m), Auto von der Strasse (14 m)
                Vector3 from = car ? hp - dir * 14f : hp + dir * 3.5f;
                Vector3 a = from + Vector3.up * 0.9f, b = hp + Vector3.up * 0.9f;
                bool blocked = Physics.SphereCast(a, 0.45f, (b - a).normalized, out RaycastHit hit, Mathf.Max(0.1f, (b - a).magnitude - 0.6f), ~LayerMask.GetMask("Car", "Skater", "Ignore Raycast"), QueryTriggerInteraction.Ignore);
                if (blocked) continue;
                if (skip-- > 0) continue;
                return h;
            }
            return null;
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 150) throw new Exception("Zeitlimit");
                if (!EditorApplication.isPlaying) return;
                var a = PlayerAvatar.Local;
                if (a == null || Time.time < 2f || FireHydrant.All.Count == 0) return;
                var sc = a.skater;
                float t = Time.time;
                bool car = _pass == 1;
                switch (_stage)
                {
                    case 0:
                    {
                        Check(FireHydrant.All.Count > 20, $"{FireHydrant.All.Count} Hydranten eingerichtet");
                        _h = Pick(car);
                        if (_h == null) throw new Exception("Kein passender Hydrant mit Dach");
                        _landing = _h.PreviewLanding().Value;
                        Vector3 hp = _h.transform.position;
                        _toRoof = new Vector3(_landing.x - hp.x, 0f, _landing.z - hp.z).normalized;
                        a.externalControl = true;
                        sc.inputEnabled = false;
                        float yaw = Mathf.Atan2(_toRoof.x, _toRoof.z) * Mathf.Rad2Deg;
                        if (car) a.PlaceForChallenge(true, hp - _toRoof * 14f + Vector3.up * 0.1f, yaw);
                        else a.PlaceForChallenge(false, hp + _toRoof * 3.5f, yaw + 180f);
                        Debug.Log($"[Hydrant] Pass {_pass} ({(car ? "Auto" : "Board")}): Hydrant {_h.Index} bei {hp}, Dach-Ziel {_landing}");
                        _stage = 1; _t0 = t; _maxY = 0f; _launched = false;
                        break;
                    }
                    case 1: // anfahren: nur Gas bzw. anschieben
                        if (car) a.car.input = new VehicleInput { throttle = 1f };
                        else sc.InjectInput(new Vector2(0f, 1f), false);
                        if (_h.Spraying) { Debug.Log($"[Hydrant] Hydrant umgefahren nach {t - _t0:0.0} s, Fontaene bis {_h.Apex:0.0} m"); _stage = 2; _t0 = t; }
                        else if (t - _t0 > 6f) throw new Exception("Hydrant nicht umgefahren, Spieler bei " + (car ? a.car.transform.position : sc.transform.position));
                        break;
                    case 2: // fliegen
                    {
                        if (car) a.car.input = default;
                        else sc.InjectInput(Vector2.zero, false);
                        Vector3 pos = car ? a.car.transform.position : sc.transform.position;
                        _maxY = Mathf.Max(_maxY, pos.y);
                        if (!_launched && pos.y > _h.transform.position.y + 3f) { _launched = true; Debug.Log("[Hydrant] hochgeschleudert"); }
                        if (Mathf.Abs(t - _t0 - 0.9f) < 0.02f)
                        {
                            Shot((car ? "car" : "board") + "_jet");
                            // dazu von der Seite (Strassenseite), damit man die ganze Fontaene sieht
                            var side = new GameObject("SideCam").AddComponent<Camera>();
                            Vector3 hp = _h.transform.position;
                            side.transform.position = hp - _toRoof * 18f + Vector3.Cross(Vector3.up, _toRoof) * 10f + Vector3.up * 6f;
                            side.transform.LookAt(hp + Vector3.up * 12f);
                            side.fieldOfView = 70f;
                            System.IO.Directory.CreateDirectory("Logs/hydrant");
                            SimTests.Capture(side, "Logs/hydrant/" + (car ? "car" : "board") + "_jet_side.png");
                            UnityEngine.Object.Destroy(side.gameObject);
                        }
                        bool landed = car ? t - _t0 > 2f && Mathf.Abs(a.car.Body.linearVelocity.y) < 0.5f && pos.y > _landing.y - 1f && _maxY > pos.y + 1f
                                          : t - _t0 > 1f && sc.State == SkaterState.Riding;
                        if (landed && t - _t0 > 1.2f) { _stage = 3; _t0 = t; }
                        else if (t - _t0 > 8f) throw new Exception($"Keine Landung, Zustand {(car ? "Auto" : sc.State.ToString())} bei {pos}");
                        break;
                    }
                    case 3: // oben kurz stehen und pruefen
                    {
                        if (car) a.car.input = default; else sc.InjectInput(Vector2.zero, false);
                        if (t - _t0 < 1.5f) break;
                        Vector3 pos = car ? a.car.transform.position : sc.transform.position;
                        string who = car ? "Auto" : "Skater";
                        Check(_launched && _maxY > _landing.y + 1.5f, $"{who} hochgeschleudert bis {_maxY:0.0} m (Dach {_landing.y:0.0} m)");
                        // Hoehe ueber dem Dach: Skater-Ursprung = Rollen auf der Flaeche, Auto-Ursprung liegt 6 cm unter der Fahrbahn (wie auf der Strasse)
                        float above = pos.y - _landing.y;
                        bool onRoof = car ? above > -0.15f && above < 1.2f : above > -0.06f && above < 0.25f;
                        Check(onRoof, $"{who} steht auf dem Dach, nicht drin (Hoehe ueber Dachflaeche {above:0.00} m, {(car ? "" : sc.State.ToString())})");
                        Check(car || sc.State == SkaterState.Riding, $"Sauber gelandet ({(car ? "Auto" : sc.State.ToString())})");
                        Shot((car ? "car" : "board") + "_roof");
                        _pass++;
                        if (_pass >= 2) Quit(_failures == 0 ? 0 : 1);
                        else _stage = 0;
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Hydrant] FEHLT: " + e.Message);
                Quit(1);
            }
        }
    }
}
