using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Animation der Spielfigur pruefen:
    /// (1) Kontaktblaetter ohne Play-Modus (Menue, Batch: RenderPoseSheet): Figuren in Reihen mit festen Rig-Werten
    ///     (Stehen, Rollen, vier Push-Phasen, Ollie laden, Absprung, Luft, Spin, Indy, Landung, Manual, 50-50, Boardslide,
    ///     Nosegrind, Kurve, Bremsen, Gehen, Rennen, Sturz), je Reihe von der Zehenseite und schraeg von vorn,
    ///     Bilder nach Logs/skater/poses_*.png.
    /// (2) Play-Mode-Test (ohne -quit: RunMotion): anschieben, Ollie aufladen, abspringen, landen, Manual, dazu
    ///     Einzelbilder aus der echten Bewegung (Logs/skater/move_*.png) und Pruefungen (OK / FEHLT) in Logs/skater/result.txt.
    /// </summary>
    public static class SkaterAnimTests
    {
        // ------------------------------------------------------------------ Kontaktblatt

        struct PoseDef
        {
            public string name;
            public Action<SkaterRig> set;
            public float moveSpeed; // zu Fuss: so schnell laeuft die Figur beim Posieren (Schrittphase aus der Bewegung)
        }

        static readonly string[][] Outfits =
        {
            new[] { "head_cap_y", "jacket_orange", "pants_denim", "shoes_white", "back_rolltop" },
            new[] { "head_hair", "jacket_blue", "pants_cargo", "shoes_red" },
            new[] { "head_phones", "jacket_lime", "pants_purple", "shoes_yellow" },
            new[] { "head_beanie_p", "jacket_white", "pants_denim", "shoes_red", "back_dome" },
            new[] { "head_bucket", "jacket_blue", "pants_purple", "shoes_white" },
        };

        static PoseDef P(string name, Action<SkaterRig> set, float move = 0f) => new PoseDef { name = name, set = set, moveSpeed = move };

        static PoseDef[][] Sheets() => new[]
        {
            new[]
            {
                P("Stehen", r => { }),
                P("Rollen", r => r.speed = 10f),
                P("Push 10%", r => { r.pushPhase = 0.1f; r.speed = 6f; }),
                P("Push 30%", r => { r.pushPhase = 0.3f; r.speed = 6f; }),
                P("Push 50%", r => { r.pushPhase = 0.5f; r.speed = 6f; }),
                P("Push 80%", r => { r.pushPhase = 0.8f; r.speed = 6f; }),
                P("Bremsen", r => { r.brake = 1f; r.speed = 6f; }),
            },
            new[]
            {
                P("Ollie laden", r => { r.crouch = 0.95f; r.charge = 1f; }),
                P("Absprung", r => { r.pop = 1f; r.airborne = 1f; r.crouch = 0.3f; }),
                P("Luft", r => { r.airborne = 1f; r.crouch = 0.3f; }),
                P("Spin", r => { r.airborne = 1f; r.crouch = 0.3f; r.spin = 1f; }),
                P("Indy", r => { r.grab = "INDY"; r.airborne = 1f; r.crouch = 0.3f; }),
                P("Landung", r => { r.land = 0.9f; r.crouch = 0.7f; r.speed = 8f; }),
                P("Kurve", r => { r.lean = 0.9f; r.speed = 9f; }),
            },
            new[]
            {
                P("Manual", r => { r.manual = 1f; r.balance = 0.7f; r.board.localRotation = Quaternion.Euler(-12f, 0f, 0f); }),
                P("50-50", r => { r.grind = 1f; r.grindName = "50-50"; }),
                P("Boardslide", r => { r.grind = 1f; r.grindName = "BOARDSLIDE"; r.board.localRotation = Quaternion.Euler(0f, 90f, 0f); }),
                P("Nosegrind", r => { r.grind = 1f; r.grindName = "NOSEGRIND"; r.balance = -0.5f; }),
                P("Gehen", r => { r.walk = 1f; r.board.localRotation = Quaternion.Euler(0f, 0f, 90f); r.board.localPosition = new Vector3(0.3f, 0.66f, 0.04f); }, 1.6f),
                P("Rennen", r => { r.walk = 1f; r.board.localRotation = Quaternion.Euler(0f, 0f, 90f); r.board.localPosition = new Vector3(0.3f, 0.66f, 0.04f); }, 5.5f),
                P("Sturz", r => { r.bail = 1f; r.board.localRotation = Quaternion.Euler(0f, 40f, 170f); r.board.localPosition = new Vector3(0.6f, 0.08f, 0.5f); }),
            },
        };

        [MenuItem("DriftSkate/Tests/Skater-Posen rendern")]
        public static void RenderPoseSheet()
        {
            Directory.CreateDirectory("Logs/skater");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            // Mitten auf einer Strasse (frei von Props), Fahrtrichtung Osten, Zehenseite nach Sueden
            Vector3 travel = Vector3.right;
            Vector3 toeSide = Quaternion.Euler(0, 80f, 0) * travel;
            Vector3 basePos = new Vector3(CityBuilder.BlockCenter(2), 0f, CityBuilder.RoadCenter(3));
            var sheets = Sheets();
            var cam = Camera.main;
            var log = new System.Text.StringBuilder();
            GameObject Make(PoseDef pose, Vector3 at, int outfit)
            {
                var root = new GameObject("Pose_" + pose.name);
                root.transform.SetPositionAndRotation(at, Quaternion.LookRotation(travel));
                SkaterBuilder.Build(root.transform, SkaterBuilder.Outfit.FromIds(Outfits[outfit % Outfits.Length], Palette.Pink), Catalog.Boards[outfit % Catalog.Boards.Count], null, null);
                var rig = root.GetComponentInChildren<SkaterRig>();
                if (rig == null) { Debug.LogError("[DriftSkate] Kein Rig an der Figur"); return root; }
                pose.set(rig);
                // Gewichte einschwingen lassen; zu Fuss die Figur ein Stueck bewegen, damit die Schrittphase stimmt,
                // am Ende wieder an ihren Platz
                for (int k = 0; k < 30; k++)
                {
                    if (pose.moveSpeed > 0f) root.transform.position += travel * pose.moveSpeed * 0.05f;
                    rig.ApplyPose(0.05f);
                }
                if (pose.moveSpeed > 0f)
                {
                    root.transform.position = at;
                    rig.ApplyPose(0.0001f);
                }
                return root;
            }
            void Shoot(Vector3 pos, Vector3 look, float fov, string file)
            {
                cam.fieldOfView = fov;
                cam.transform.position = pos;
                cam.transform.LookAt(look);
                SimTests.Capture(cam, "Logs/skater/" + file + ".png");
            }
            int n = 0;
            for (int sheet = 0; sheet < sheets.Length; sheet++)
            {
                var poses = sheets[sheet];
                var roots = new List<GameObject>();
                for (int i = 0; i < poses.Length; i++)
                {
                    var root = Make(poses[i], basePos + travel * (i - (poses.Length - 1) * 0.5f) * 2.1f, i);
                    roots.Add(root);
                    var rig = root.GetComponentInChildren<SkaterRig>();
                    if (rig != null) log.AppendLine($"{poses[i].name}: " + Describe(root.transform, rig));
                }
                Vector3 center = basePos + Vector3.up * 0.9f;
                Shoot(basePos + toeSide * 9.5f + Vector3.up * 1.5f, center, 52f, $"poses_{sheet + 1}_seite");
                Shoot(basePos + toeSide * 6.5f + travel * 6.5f + Vector3.up * 2.2f, center, 50f, $"poses_{sheet + 1}_schraeg");
                Shoot(basePos - toeSide * 9f + travel * 4f + Vector3.up * 1.7f, center, 52f, $"poses_{sheet + 1}_ruecken");
                foreach (var r in roots) UnityEngine.Object.DestroyImmediate(r);

                // Nahaufnahmen jeder Pose: Zehenseite und schraeg von vorn
                for (int i = 0; i < poses.Length; i++, n++)
                {
                    var root = Make(poses[i], basePos, i);
                    Vector3 c = basePos + Vector3.up * 0.85f;
                    Shoot(c + toeSide * 3.4f + Vector3.up * 0.25f, c, 40f, $"close_{n:00}_{Safe(poses[i].name)}_seite");
                    Shoot(c + toeSide * 2.4f + travel * 2.4f + Vector3.up * 0.6f, c, 40f, $"close_{n:00}_{Safe(poses[i].name)}_schraeg");
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
            File.WriteAllText("Logs/skater/poses.txt", log.ToString());
            Debug.Log("[DriftSkate] Skater-Posen gerendert nach Logs/skater/\n" + log);
        }

        static string Safe(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in name.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            return sb.ToString();
        }

        /// <summary>Kennzahlen einer Pose (Huefthoehe, Kniewinkel, Fuesse, Handhoehen), lokal zur Figur.</summary>
        static string Describe(Transform root, SkaterRig rig)
        {
            var map = SkeletonMap.Resolve(rig.transform, null, out _);
            if (map == null) return "?";
            float Knee(int i)
            {
                Vector3 a = map.upperLeg[i].position - map.lowerLeg[i].position, b = map.foot[i].position - map.lowerLeg[i].position;
                return Vector3.Angle(a, b);
            }
            Vector3 hip = root.InverseTransformPoint(map.hips.position);
            Vector3 hl = root.InverseTransformPoint(map.hand[0].position), hr = root.InverseTransformPoint(map.hand[1].position);
            Vector3 fl = root.InverseTransformPoint(map.foot[0].position), fr = root.InverseTransformPoint(map.foot[1].position);
            bool nan = float.IsNaN(hip.x) || float.IsNaN(hl.x) || float.IsNaN(fl.x);
            return $"Huefte {hip.y:0.00} m, Knie L {Knee(0):0}/R {Knee(1):0} Grad, Fuss L ({fl.x:+0.00;-0.00},{fl.y:0.00},{fl.z:+0.00;-0.00}) R ({fr.x:+0.00;-0.00},{fr.y:0.00},{fr.z:+0.00;-0.00}), Hand L {hl.y:0.00} R {hr.y:0.00}{(nan ? "  NaN!" : "")}";
        }

        // ------------------------------------------------------------------ Bewegung im Play-Modus

        static double _started;
        static int _stage, _failures, _pushShots;
        static float _t0, _lastPush, _minY, _maxY;
        static bool _oldEnabled, _pushSeen, _airSeen, _manualSeen, _charged, _popShot, _airShot;
        static EnterPlayModeOptions _oldOptions;
        static StreamWriter _log;

        public static void RunMotion()
        {
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            _oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _oldOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            _started = EditorApplication.timeSinceStartup;
            _stage = 0; _failures = 0; _pushShots = 0; _lastPush = -1f;
            _pushSeen = _airSeen = _manualSeen = _charged = _popShot = _airShot = false;
            Directory.CreateDirectory("Logs/skater");
            _log = new StreamWriter("Logs/skater/result.txt", false);
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

        static void Log(string line) { Debug.Log("[SkaterAnim] " + line); _log?.WriteLine(line); _log?.Flush(); }
        static void Check(bool ok, string what) { Log((ok ? "OK: " : "FEHLT: ") + what); if (!ok) _failures++; }

        /// <summary>Bild von der Zehenseite (Kamera faehrt mit), Figur in der Mitte.</summary>
        static void Shot(SkaterController sc, string file, float side = 3.6f, float height = 1.1f, float ahead = 0f)
        {
            var cam = Camera.main;
            Vector3 fwd = Vector3.ProjectOnPlane(sc.transform.forward, Vector3.up).normalized;
            Vector3 toe = Quaternion.Euler(0, 80f, 0) * fwd;
            Vector3 focus = sc.transform.position + Vector3.up * 0.85f;
            cam.fieldOfView = 45f;
            cam.transform.position = focus + toe * side + fwd * ahead + Vector3.up * (height - 0.85f);
            cam.transform.LookAt(focus);
            SimTests.Capture(cam, "Logs/skater/" + file + ".png");
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 180) throw new Exception("Zeitlimit");
                if (!EditorApplication.isPlaying) return;
                var a = PlayerAvatar.Local;
                if (a == null || Time.time < 2.5f) return;
                float t = Time.time;
                var sc = a.skater;
                switch (_stage)
                {
                    case 0:
                    {
                        Check(sc.rig != null && sc.rig.Ready, "Figur mit Rig");
                        a.externalControl = true;
                        sc.inputEnabled = false;
                        // Auf dem Board auf der Strasse suedlich vom Herz-Platz, Blick nach Osten
                        a.PlaceForChallenge(false, new Vector3(CityBuilder.BlockCenter(1), 0.2f, CityBuilder.RoadCenter(3)), 90f);
                        _stage = 1; _t0 = t;
                        break;
                    }
                    case 1: // anschieben, vier Bilder aus einem Zyklus
                    {
                        sc.InjectInput(new Vector2(0f, 1f), false);
                        float ph = sc.PushPhase;
                        if (ph >= 0f) _pushSeen = true;
                        if (sc.Speed > 2.5f && ph >= 0f)
                        {
                            float[] marks = { 0.1f, 0.3f, 0.5f, 0.8f };
                            if (_pushShots < marks.Length && ph >= marks[_pushShots] && (_lastPush < marks[_pushShots] || _pushShots == 0))
                            {
                                Shot(sc, $"move_push_{_pushShots + 1}");
                                Log($"Push-Bild {_pushShots + 1} bei Phase {ph:0.00}, {sc.Speed:0.0} m/s");
                                _pushShots++;
                            }
                            _lastPush = ph;
                        }
                        if (_pushShots >= 4 || t - _t0 > 8f)
                        {
                            Check(_pushSeen && _pushShots >= 4, $"Anschieben: {_pushShots} Bilder aus dem Zyklus, Tempo {sc.Speed:0.0} m/s");
                            _stage = 2; _t0 = t;
                        }
                        break;
                    }
                    case 2: // Ollie aufladen (gehalten), dann loslassen
                    {
                        bool hold = t - _t0 < 0.4f;
                        sc.InjectInput(new Vector2(0f, 0.3f), hold);
                        if (hold && t - _t0 > 0.3f && !_charged)
                        {
                            _charged = true;
                            Check(sc.rig.charge > 0.5f && sc.rig.crouch > 0.35f, $"Ollie laden: charge {sc.rig.charge:0.00}, crouch {sc.rig.crouch:0.00}");
                            Shot(sc, "move_charge");
                        }
                        if (!hold)
                        {
                            if (!_popShot && sc.State == SkaterState.Air) { _popShot = true; Shot(sc, "move_pop", 3.6f, 1.4f); Log($"Absprung: pop {sc.rig.pop:0.00}"); _minY = _maxY = sc.transform.position.y; _stage = 3; _t0 = t; }
                            else if (t - _t0 > 1.5f) { Check(false, "Kein Ollie nach dem Loslassen"); _stage = 4; _t0 = t; }
                        }
                        break;
                    }
                    case 3: // in der Luft: Bild am hoechsten Punkt, dann Landung
                    {
                        sc.InjectInput(new Vector2(0f, 0.3f), false);
                        float y = sc.transform.position.y;
                        if (sc.State == SkaterState.Air)
                        {
                            _airSeen = true;
                            if (!_airShot && y < _maxY && y > _minY + 0.3f) { _airShot = true; Shot(sc, "move_air", 3.6f, 1.6f); Log($"Luft: airborne {sc.rig.airborne:0.00}, Hoehe {y:0.00}"); }
                            _maxY = Mathf.Max(_maxY, y);
                        }
                        else if (_airSeen && sc.State == SkaterState.Riding)
                        {
                            Shot(sc, "move_land");
                            Check(sc.rig.land > 0.3f, $"Landung: land {sc.rig.land:0.00}, crouch {sc.rig.crouch:0.00}");
                            Check(_maxY - _minY > 0.5f, $"Ollie-Hoehe {_maxY - _minY:0.00} m");
                            _stage = 4; _t0 = t;
                        }
                        else if (sc.State == SkaterState.Bailed) { Check(false, "Sturz bei der Landung"); _stage = 4; _t0 = t; }
                        if (t - _t0 > 4f) { Check(false, "Keine Landung"); _stage = 4; _t0 = t; }
                        break;
                    }
                    case 4: // Manual halten
                    {
                        bool manual = t - _t0 > 0.6f && t - _t0 < 2.2f;
                        sc.InjectInput(new Vector2(0f, manual ? 0.2f : 0.6f), false, manual: manual);
                        if (sc.Manualing && !_manualSeen && t - _t0 > 1.3f)
                        {
                            _manualSeen = true;
                            Shot(sc, "move_manual");
                            Check(sc.rig.manual > 0.8f, $"Manual: rig.manual {sc.rig.manual:0.00}, balance {sc.rig.balance:0.00}");
                        }
                        if (t - _t0 > 2.6f)
                        {
                            Check(_manualSeen, "Manual gefahren");
                            // Keine NaN im Skelett
                            bool nan = false;
                            foreach (var b in sc.rig.GetComponentsInChildren<Transform>()) { var p = b.position; if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)) nan = true; }
                            Check(!nan, "Skelett ohne NaN");
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
