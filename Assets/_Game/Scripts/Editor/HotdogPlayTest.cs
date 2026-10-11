using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Imbiss im Park wie ein Mensch: zum Wagen rollen, Bodo ansprechen, Hotdog kaufen (liegt beim Reichen in seiner Hand),
    /// dann auf dem Drift-Platz Tempo messen: 3 s pushen ohne Hotdog, dann mit (essen mit 0,3 s Reaktionszeit, W bleibt
    /// gedrueckt), dasselbe im Auto mit Vollgas. Prueft, dass der Hotdog im Rucksack landet, die Power anspringt und Board
    /// und Auto spuerbar schneller werden. Bilder nach Logs/hotdog/.
    /// Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.HotdogPlayTest.Run
    /// Vorher profile.json sichern (der Test setzt Geld und Hotdogs).
    /// </summary>
    public static class HotdogPlayTest
    {
        const float Reaction = 0.3f, RunTime = 3f;
        static double _started;
        static int _stage, _failures, _run;
        static float _nextLog, _t0, _eatAt, _grillAt, _skateBase, _skateBoost, _carBase, _carBoost, _distBase, _distBoost;
        static long _money0;
        static Vector3 _start;
        static bool _oldEnabled, _ate, _eatShot;
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
            Directory.CreateDirectory("Logs/hotdog");
            _log = new StreamWriter("Logs/hotdog/result.txt", false);
            // Gleiche Bedingungen wie im normalen Spiel: kein Admin-Turbo, Abendhimmel ohne Regen (profile.json vorher sichern)
            var p = SaveSystem.Profile;
            p.adminMode = false;
            if (Environment.GetCommandLineArgs().Contains("-standardchar")) p.selectedCharacter = "";
            p.weather = "klar";
            p.sky = "abend";
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

        static void Log(string line)
        {
            Debug.Log("[HotdogTest] " + line);
            _log?.WriteLine(line);
            _log?.Flush();
        }

        static void Check(bool ok, string what)
        {
            Log((ok ? "OK: " : "FEHLT: ") + what);
            if (!ok) _failures++;
        }

        static void Shot(string file) => SimTests.Capture(Camera.main, "Logs/hotdog/" + file + ".png");

        static void ShotAt(Vector3 camPos, Vector3 look, float fov, string file)
        {
            var cam = Camera.main;
            float oldFov = cam.fieldOfView;
            cam.fieldOfView = fov;
            cam.transform.position = camPos;
            cam.transform.LookAt(look);
            Shot(file);
            cam.fieldOfView = oldFov;
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 200) throw new Exception("Zeitlimit");
                if (!EditorApplication.isPlaying) return;
                var a = PlayerAvatar.Local;
                if (a == null || Time.time < 2f || HotdogPower.Instance == null) return;
                var sc = a.skater;
                var prof = SaveSystem.Profile;
                float t = Time.time;
                var vendor = HotdogStand.Spawned.Count > 0 ? HotdogStand.Spawned[0] : null;
                var npc = vendor != null ? vendor.GetComponent<StonerNpc>() : null;
                switch (_stage)
                {
                    case 0:
                    {
                        Check(HotdogStand.Carts.Count == 2 && HotdogStand.Spawned.Count == 2, $"Zwei Park-Wagen mit Verkaeufer ({HotdogStand.Carts.Count} Wagen, {HotdogStand.Spawned.Count} Verkaeufer)");
                        foreach (var v in HotdogStand.Spawned)
                        {
                            var n = v.GetComponent<StonerNpc>();
                            bool rig = n != null && n.Animator != null && n.Animator.isHuman && n.Animator.avatar != null && n.Animator.avatar.isValid;
                            bool clips = n != null && n.HasClip("Idle") && n.HasClip("Grill") && n.HasClip("Serve") && n.HasClip("Laugh");
                            var cart = HotdogStand.Carts[HotdogStand.Spawned.IndexOf(v)];
                            float behind = Vector3.Dot(v.transform.position - cart.TransformPoint(HotdogStand.CartCenter), cart.TransformDirection(HotdogStand.ServeSide));
                            Check(rig && clips && behind < -0.6f, $"{n?.Id}: Humanoid {rig}, Idle/Grill/Serve/Laugh {clips}, steht {-behind:0.00} m hinter der Wagenmitte");
                        }
                        if (vendor == null) throw new Exception("kein Verkaeufer");
                        _money0 = prof.money = Math.Max(prof.money, 5000);
                        prof.hotdogs = 0;
                        prof.hotdogsBought = 0;
                        a.externalControl = true;
                        sc.inputEnabled = false;
                        var c0 = HotdogStand.Carts[0];
                        a.PlaceForChallenge(false, HotdogStand.CustomerSpot(c0, 1.7f) + Vector3.up * 0.05f,
                                            Quaternion.LookRotation(HotdogStand.CustomerFacing(c0)).eulerAngles.y);
                        _stage = 1; _t0 = t;
                        break;
                    }
                    case 1:
                        sc.InjectInput(Vector2.zero, false);
                        if (t - _t0 < 1.5f) break;
                        // Ueberblick: Wagen mit Verkaeufer von der Kundenseite und schraeg von hinten
                        var cart0 = HotdogStand.Carts[0];
                        Vector3 cc = cart0.TransformPoint(HotdogStand.CartCenter);
                        Vector3 side = cart0.TransformDirection(HotdogStand.ServeSide), axis = cart0.TransformDirection(HotdogStand.CartAxis);
                        ShotAt(cc + side * 4.2f + axis * 1.6f + Vector3.up * 1.7f, cc + Vector3.up * 1.15f, 50f, "stand_customer");
                        ShotAt(cc - side * 3.5f - axis * 2.5f + Vector3.up * 2.2f, cc + Vector3.up * 1f, 55f, "stand_back");
                        ShotAt(cc + axis * 4.5f + Vector3.up * 1.6f, cc + Vector3.up * 1.1f, 50f, "stand_side");
                        var c1 = HotdogStand.Carts[1];
                        Vector3 cc1 = c1.TransformPoint(HotdogStand.CartCenter);
                        Vector3 side1 = c1.TransformDirection(HotdogStand.ServeSide), axis1 = c1.TransformDirection(HotdogStand.CartAxis);
                        ShotAt(cc1 + side1 * 4.2f + axis1 * 1.6f + Vector3.up * 1.7f, cc1 + Vector3.up * 1.15f, 50f, "stand_customer_udo");
                        ShotAt(cc1 + side1 * 9f + Vector3.up * 3f, cc1 + Vector3.up * 1f, 55f, "stand_wide_udo");
                        Check(QuestNpc.Near(sc.transform.position) == vendor, $"Nah genug, um {vendor.Speaker} anzusprechen (Abstand {Vector3.Distance(sc.transform.position, vendor.transform.position):0.0} m)");
                        vendor.Talk();
                        _stage = 2; _t0 = t;
                        break;
                    case 2:
                        if (!vendor.Talking) throw new Exception("Dialog nicht offen");
                        if (!vendor.ChoiceOpen)
                        {
                            if (t - _t0 > 0.6f) { vendor.TestPress(true); _t0 = t; }
                            break;
                        }
                        if (t - _t0 < 3f) break; // letzte Seite zu Ende tippen lassen
                        Shot("dialog");
                        vendor.TestPress(true);
                        _stage = 3; _t0 = t;
                        break;
                    case 3:
                    {
                        if (t - _t0 < 1.3f) break;
                        Check(prof.hotdogs == 1 && prof.money == _money0 - (Admin.Free ? 0 : HotdogVendor.Price) && !GameInput.Blocked,
                              $"Hotdog gekauft (Geld {_money0} -> {prof.money}, Rucksack {prof.hotdogs}, Steuerung frei {!GameInput.Blocked})");
                        var served = GameObject.Find("ServedHotdog");
                        var hand = npc.Animator.GetBoneTransform(HumanBodyBones.LeftHand);
                        float d = served != null ? Vector3.Distance(served.transform.position, hand.position) : -1f;
                        Check(served != null && served.activeInHierarchy && d < 0.25f && npc.CurrentAction == "Serve",
                              $"Hotdog liegt beim Reichen in {npc.Id}s Hand ({d:0.00} m, {npc.CurrentAction})");
                        // von der Kundenseite, etwas erhoeht und zur linken Hand des Verkaeufers versetzt
                        Vector3 f = npc.transform.forward, look = served != null ? served.transform.position : npc.Head.position;
                        ShotAt(look + f * 1.4f - Vector3.Cross(Vector3.up, f) * 0.6f + Vector3.up * 0.55f, look, 45f, "serve");
                        _stage = 4; _t0 = t; _grillAt = -1f;
                        break;
                    }
                    case 4:
                        // Grillen abwarten (Spezial-Animation kommt alle paar Sekunden) und fotografieren
                        if (npc.CurrentAction == "Grill" && _grillAt < 0f) _grillAt = t;
                        if (npc.CurrentAction != "Grill") _grillAt = -1f;
                        if (_grillAt > 0f && t - _grillAt > 1.1f) // Zange auf dem Grill
                        {
                            Vector3 h = npc.Head.position, f = npc.transform.forward;
                            ShotAt(h + f * 2.4f - Vector3.Cross(Vector3.up, f) * 1.2f + Vector3.up * 0.2f, h + f * 0.6f - Vector3.up * 0.4f, 45f, "grill");
                            Check(true, "Grill-Animation laeuft");
                            StartRun(a, false, 0);
                            _stage = 5;
                        }
                        else if (t - _t0 > 30f) { Check(false, "Grill-Animation nicht gesehen"); StartRun(a, false, 0); _stage = 5; }
                        break;
                    case 5: // Board-Laeufe: 0 = ohne, 1 = mit Hotdog
                        if (Drive(a, t)) break;
                        if (_run == 0) { StartRun(a, false, 1); break; }
                        StartRun(a, true, 2);
                        _stage = 6;
                        break;
                    case 6: // Auto-Laeufe: 2 = ohne, 3 = mit Hotdog
                        if (Drive(a, t)) break;
                        if (_run == 2) { StartRun(a, true, 3); break; }
                        Log($"Board: ohne {_skateBase * 3.6f:0.0} km/h ({_distBase:0.0} m), mit Hotdog {_skateBoost * 3.6f:0.0} km/h ({_distBoost:0.0} m)");
                        Check(_skateBoost > _skateBase * 1.12f, $"Board mit Hotdog schneller (+{(_skateBoost / Mathf.Max(0.1f, _skateBase) - 1f) * 100f:0} %)");
                        Log($"Auto: ohne {_carBase * 3.6f:0.0} km/h, mit Hotdog {_carBoost * 3.6f:0.0} km/h");
                        Check(_carBoost > _carBase * 1.08f, $"Auto mit Hotdog schneller (+{(_carBoost / Mathf.Max(0.1f, _carBase) - 1f) * 100f:0} %)");
                        Check(prof.hotdogs == 0 && !HotdogPower.Instance.Eat(), "Beide Hotdogs gegessen, ohne Hotdog kein weiterer");
                        Quit(0);
                        break;
                }
            }
            catch (Exception e) { Debug.LogException(e); Log("FEHLER: " + e.Message); Quit(1); }
        }

        /// <summary>Lauf vorbereiten: auf dem Drift-Platz an den Westrand, Blick nach Osten; mit Hotdog vorher einen kaufen.</summary>
        static void StartRun(PlayerAvatar a, bool car, int run)
        {
            _run = run;
            bool boost = run % 2 == 1;
            if (boost) SaveSystem.Profile.hotdogs = Mathf.Max(SaveSystem.Profile.hotdogs, 1);
            // Power vom letzten Lauf abklingen lassen: Zeit auf 0 (der Test misst jeden Lauf fuer sich)
            HotdogPower.Instance.ResetForTest();
            _start = new Vector3(CityBuilder.BlockCenter(1) - 28f, 0.05f, CityBuilder.BlockCenter(1));
            a.PlaceForChallenge(car, _start, 90f);
            if (!car) a.skater.inputEnabled = false;
            _t0 = Time.time + 0.6f; // kurz ankommen
            _eatAt = boost ? _t0 + Reaction : -1f;
            _ate = _eatShot = false;
        }

        /// <summary>Einen Lauf fahren. true, solange er noch laeuft.</summary>
        static bool Drive(PlayerAvatar a, float t)
        {
            bool car = _run >= 2;
            bool going = t >= _t0;
            if (car) a.car.input = going ? new VehicleInput { throttle = 1f } : new VehicleInput { brake = 1f };
            else a.skater.InjectInput(going ? new Vector2(0f, 1f) : Vector2.zero, false);
            if (_eatAt > 0f && t >= _eatAt && !_ate)
            {
                _ate = true;
                Check(HotdogPower.Instance.Eat(), $"Hotdog essen ({(car ? "im Auto" : "auf dem Board")}, W gedrueckt)");
            }
            if (_ate && !_eatShot && !car && HotdogPower.Eating && a.skater.rig != null && a.skater.rig.eat > 0.95f)
            {
                _eatShot = true;
                var rig = a.skater.rig;
                Vector3 m = rig.Mouth;
                Vector3 fwd = Quaternion.Euler(0f, a.skater.Heading, 0f) * Vector3.forward;
                Vector3 toe = Vector3.ProjectOnPlane(a.skater.bodyPivot.forward, Vector3.up).normalized; // Blickrichtung der Figur
                ShotAt(m + fwd * 1.6f + toe * 0.7f + Vector3.up * 0.1f, m - Vector3.up * 0.2f, 45f, "eat_front");
                ShotAt(m + toe * 1.6f + fwd * 0.3f, m - Vector3.up * 0.15f, 40f, "eat_side");
                var held = GameObject.Find("HotdogInHand");
                float d = held != null ? Vector3.Distance(held.transform.position, m) : -1f;
                Log($"   Figur {(string.IsNullOrEmpty(SaveSystem.Profile.selectedCharacter) ? "Standard" : SaveSystem.Profile.selectedCharacter)}: Groesse {rig.Scale:0.00}, Gesicht {rig.FaceDepth:0.00} m vor dem Kopfknochen, Hand-Mund {Vector3.Distance(rig.EatGrip, m):0.00} m, Hotdog-Laenge {(held != null ? held.transform.localScale.z * 0.18f : 0f):0.00} m");
                Check(held != null && held.activeInHierarchy && d < 0.3f, $"Hotdog in der Hand vor dem Mund ({d:0.00} m)");
            }
            if (going && t - _t0 >= _nextLog)
            {
                _nextLog += 0.5f;
                Log($"   Lauf {_run} t={t - _t0:0.0} s: {(car ? a.car.Speed : a.skater.Speed) * 3.6f:0.0} km/h, {(car ? "" : a.skater.State.ToString())} Power {HotdogPower.Active} Schub {HotdogPower.SkateRush:0.0}");
            }
            if (!going || t - _t0 < RunTime) return true;
            _nextLog = 0f;
            float speed = car ? a.car.Speed : a.skater.Speed;
            float dist = Vector3.Distance(car ? a.car.transform.position : a.skater.transform.position, _start);
            if (_run == 0) { _skateBase = speed; _distBase = dist; }
            else if (_run == 1)
            {
                _skateBoost = speed; _distBoost = dist;
                Check(HotdogPower.Active && HotdogPower.TimeLeft > 20f, $"Hotdog-Power aktiv ({HotdogPower.TimeLeft:0} s)");
                Shot("boost_skate");
            }
            else if (_run == 2) _carBase = speed;
            else { _carBoost = speed; Shot("boost_car"); }
            if (car) a.car.input = new VehicleInput { brake = 1f };
            else a.skater.InjectInput(Vector2.zero, false);
            return false;
        }
    }
}
