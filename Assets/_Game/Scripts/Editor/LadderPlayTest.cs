using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Kran-Leiter im Play-Mode: vor den Mast stellen, greifen (wie F), mit W (und Shift) bis oben klettern, auf den
    /// drehenden Laufsteg absteigen, dort stehen bleiben (muss mitdrehen) und ein Stueck den Ausleger entlang laufen,
    /// dann wieder an die Leiter und ein Stueck runter. Prueft auch, dass die Hitbox des Laufstegs am sichtbaren Steg liegt.
    /// Bilder und Ergebnis nach Logs/ladder/. Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.LadderPlayTest.Run
    /// Vorher profile.json sichern.
    /// </summary>
    public static class LadderPlayTest
    {
        static double _started;
        static int _stage, _failures;
        static float _t0, _nextShot;
        static Vector3 _localOnTop;
        static bool _oldEnabled;
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
            Directory.CreateDirectory("Logs/ladder");
            _log = new StreamWriter("Logs/ladder/result.txt", false);
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

        static void Log(string line) { Debug.Log("[LadderTest] " + line); _log?.WriteLine(line); _log?.Flush(); }
        static void Check(bool ok, string what) { Log((ok ? "OK: " : "FEHLT: ") + what); if (!ok) _failures++; }

        static void Shot(Vector3 from, Vector3 at, float fov, string file)
        {
            var cam = Camera.main;
            float old = cam.fieldOfView;
            cam.fieldOfView = fov;
            cam.transform.position = from;
            cam.transform.LookAt(at);
            SimTests.Capture(cam, "Logs/ladder/" + file + ".png");
            cam.fieldOfView = old;
        }

        static Ladder Find(string name) => Ladder.All.Find(l => l != null && l.name == name);

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 200) throw new Exception("Zeitlimit");
                if (!EditorApplication.isPlaying) return;
                var a = PlayerAvatar.Local;
                if (a == null || Time.time < 2f) return;
                var sc = a.skater;
                float t = Time.time;
                var mast = Find("MastLadder");
                var top = mast != null ? mast.exitFrame : null;
                switch (_stage)
                {
                    case 0:
                    {
                        Check(mast != null && Find("CounterweightLadder") != null && top != null && top.GetComponent<MovingGround>() != null,
                              $"Kran eingerichtet: {Ladder.All.Count} Leitern, Oberteil dreht mit");
                        if (mast == null) throw new Exception("keine Mast-Leiter");
                        // Liegt die Laufsteg-Hitbox am sichtbaren Steg? (naechster Punkt des Oberteil-Meshes)
                        var deck = top.Find("ClimbSetup/Jib_Walkway").GetComponent<BoxCollider>();
                        var body = top.Find("Body").GetComponent<MeshFilter>().sharedMesh;
                        Vector3 c = top.InverseTransformPoint(deck.transform.TransformPoint(deck.center));
                        float best = float.MaxValue;
                        foreach (var v in body.vertices) best = Mathf.Min(best, (v - c).sqrMagnitude);
                        Check(Mathf.Sqrt(best) < 1.5f, $"Laufsteg-Hitbox am sichtbaren Ausleger ({Mathf.Sqrt(best):0.00} m bis zum naechsten Mesh-Punkt)");
                        a.externalControl = true;
                        sc.inputEnabled = false;
                        Vector3 start = mast.BottomExit + Vector3.up * 0.05f;
                        Vector3 face = Vector3.ProjectOnPlane(mast.Bottom - start, Vector3.up);
                        a.PlaceForChallenge(false, start, Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg);
                        _stage = 1; _t0 = t;
                        break;
                    }
                    case 1:
                    {
                        sc.InjectInput(Vector2.zero, false);
                        if (t - _t0 < 1f) break;
                        var near = Ladder.FindNear(sc.transform.position, 1.3f, out float s);
                        Check(near == mast, $"Leiter vom Fundament aus greifbar (Hoehe {s:0.0} m)");
                        Check(sc.StartClimb(mast, s) && sc.State == SkaterState.Climbing, "An die Leiter gegriffen");
                        _stage = 2; _t0 = t; _nextShot = t + 2f;
                        break;
                    }
                    case 2: // hochklettern, W und Shift gehalten
                    {
                        sc.InjectInput(new Vector2(0f, 1f), false, manual: true);
                        if (sc.State == SkaterState.Climbing && sc.ClimbHeight > 12f && _nextShot < float.MaxValue)
                        {
                            _nextShot = float.MaxValue;
                            // Kamera innen im Mast, schraeg hinter der Figur (aussen versperren Streben die Sicht)
                            Vector3 p = sc.transform.position;
                            Vector3 side = mast.Side, right = Vector3.Cross(Vector3.up, side);
                            Shot(p + side * 0.9f + right * 1.2f + Vector3.up * 0.3f, p + Vector3.up * 1.0f, 70f, "climb_close");
                            Shot(p + side * 1.0f - right * 1.1f - Vector3.up * 0.6f, p + Vector3.up * 1.2f, 70f, "climb_inside");
                            Log($"   Klettern: Hoehe {sc.ClimbHeight:0.0} m, Pose {sc.rig?.climb:0.00}");
                        }
                        if (sc.State == SkaterState.Walking)
                        {
                            Log($"   oben angekommen nach {t - _t0:0.0} s bei {sc.transform.position}");
                            _stage = 3; _t0 = t;
                        }
                        else if (t - _t0 > 60f) throw new Exception($"Nicht oben angekommen (Hoehe {sc.ClimbHeight:0.0} von {mast.Length:0.0}, {sc.State})");
                        break;
                    }
                    case 3: // stehen bleiben: muss mit dem Ausleger mitdrehen
                    {
                        sc.InjectInput(Vector2.zero, false);
                        if (t - _t0 < 0.8f) { _localOnTop = top.InverseTransformPoint(sc.transform.position); break; }
                        if (t - _t0 < 4f) break;
                        Vector3 nowLocal = top.InverseTransformPoint(sc.transform.position);
                        float drift = Vector3.Distance(nowLocal, _localOnTop) * top.lossyScale.x;
                        float h = sc.transform.position.y;
                        Check(sc.State == SkaterState.Walking && drift < 0.3f, $"Steht auf dem drehenden Laufsteg und dreht mit (rutscht {drift:0.00} m in 3 s, Hoehe {h:0.0} m)");
                        Vector3 p = sc.transform.position;
                        Vector3 toAxis = Vector3.ProjectOnPlane(top.position - p, Vector3.up).normalized;
                        Shot(p - toAxis * 6f + Vector3.up * 2.5f + Vector3.Cross(Vector3.up, toAxis) * 3f, p + Vector3.up * 0.8f, 55f, "on_jib");
                        _stage = 4; _t0 = t;
                        break;
                    }
                    case 4: // den Ausleger entlang laufen
                    {
                        Vector3 p = sc.transform.position;
                        Vector3 outward = Vector3.ProjectOnPlane(p - top.position, Vector3.up).normalized;
                        float want = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg;
                        float steer = Mathf.Clamp(Mathf.DeltaAngle(sc.Heading, want) / 30f, -1f, 1f);
                        sc.InjectInput(new Vector2(steer, 1f), false);
                        if (t - _t0 < 4f) break;
                        float r = Vector3.ProjectOnPlane(p - top.position, Vector3.up).magnitude;
                        Check(sc.State == SkaterState.Walking && p.y > top.position.y + 3f, $"Auf dem Ausleger gelaufen: {r:0.0} m vom Mast, Hoehe {p.y:0.0} m");
                        Shot(p + Vector3.up * 14f - outward * 18f, p, 55f, "jib_walk_far");
                        sc.InjectInput(Vector2.zero, false);
                        // zurueck an die Leiter (wie F oben am Ausstieg) und ein Stueck runter
                        Check(sc.StartClimb(mast, mast.Length) && sc.State == SkaterState.Climbing, "Oben wieder an die Leiter");
                        _stage = 5; _t0 = t;
                        break;
                    }
                    case 5:
                    {
                        sc.InjectInput(new Vector2(0f, -1f), false);
                        if (t - _t0 < 2f) break;
                        Check(sc.State == SkaterState.Climbing && sc.ClimbHeight < mast.Length - 3f, $"Runtergeklettert bis {sc.ClimbHeight:0.0} m");
                        sc.InjectInput(Vector2.zero, true); // loslassen
                        _stage = 6; _t0 = t;
                        break;
                    }
                    case 6:
                        sc.InjectInput(Vector2.zero, false);
                        if (t - _t0 < 0.3f) break;
                        Check(sc.State == SkaterState.Walking, $"Losgelassen: {sc.State}");
                        Quit(0);
                        break;
                }
            }
            catch (Exception e) { Debug.LogException(e); Log("FEHLER: " + e.Message); Quit(1); }
        }
    }
}
