using System;
using System.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Play-Mode-Test: Wallride in der Seitengasse (wie im AutoTest, mit Reaktionszeit). Misst ueber die ersten 1,2 s, wie
    /// hoch die Knoechel ueber dem Deck stehen (am Board ca. 0,23 m; frueher schwebten die Fuesse kurz 14 cm daneben), Bilder nach Logs/wallride/.
    /// Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.WallridePlayTest.Run
    /// </summary>
    public static class WallridePlayTest
    {
        static double _started;
        static bool _oldEnabled, _running;
        static EnterPlayModeOptions _oldOptions;

        public static void Run()
        {
            SaveSystem.DataFolder = System.IO.Path.GetFullPath("Logs/WallrideTestSave"); // Fynns Spielstand bleibt unberuehrt
            System.IO.Directory.CreateDirectory(SaveSystem.DataFolder);
            System.IO.Directory.CreateDirectory("Logs/wallride");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            _oldEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _oldOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            _started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup - _started > 120) { Debug.LogError("[Wallride] Zeitlimit"); Quit(1); return; }
            if (_running || !EditorApplication.isPlaying || Time.time < 3f || PlayerAvatar.Local == null) return;
            if (!AlleyCrew.Found) { Debug.LogError("[Wallride] FEHLT: Gasse nicht gefunden"); Quit(1); return; }
            _running = true;
            Debug.Log("[Wallride] Start in der Gasse");
            PlayerAvatar.Local.StartCoroutine(Drive(PlayerAvatar.Local));
        }

        static IEnumerator Drive(PlayerAvatar a)
        {
            a.externalControl = true;
            if (a.Mode == PlayerMode.Driving) { a.TestInteract(); yield return null; }
            var sc = a.skater;
            sc.inputEnabled = false;
            var al = AlleyCrew.Current;
            Vector3 start = al.entrance + al.inward * 12.5f - al.across * (al.halfWidth - 1.7f);
            if (Physics.Raycast(start + Vector3.up * 2f, Vector3.down, out RaycastHit sg, 5f, ~LayerMask.GetMask("Rail", "Skater"))) start = sg.point;
            Vector3 runDir = (Quaternion.AngleAxis(Vector3.SignedAngle(al.inward, -al.across, Vector3.up) > 0 ? 13f : -13f, Vector3.up) * al.inward).normalized;
            sc.Place(start + Vector3.up * 0.1f, Mathf.Atan2(runDir.x, runDir.z) * Mathf.Rad2Deg);
            for (int i = 0; i < 5; i++) yield return null;
            float t = Time.time;
            while (Time.time - t < 1.0f) { sc.InjectInput(new Vector2(0, 1), false); yield return null; }
            t = Time.time;
            while (Time.time - t < 0.2f) { sc.InjectInput(new Vector2(0, 1), true); yield return null; }
            sc.InjectInput(Vector2.zero, false);
            t = Time.time;
            while (Time.time - t < 0.12f) yield return null; // Reaktionszeit
            sc.InjectInput(Vector2.zero, false, grind: true);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            sc.InjectInput(Vector2.zero, false);
            t = Time.time;
            while (sc.State != SkaterState.WallRide && Time.time - t < 1.3f) yield return null;
            Debug.Log($"[Wallride] nach dem Ollie: {sc.State}");
            if (sc.State != SkaterState.WallRide) { Debug.LogError("[Wallride] FEHLT: kein Wallride (" + sc.State + ")"); Quit(1); yield break; }

            var map = SkeletonMap.Resolve(sc.rig.transform, null, out _);
            var board = sc.boardPivot;
            float t0 = Time.time, worst = 0f, worstAt = 0f;
            float[] shots = { 0.1f, 0.3f, 0.6f, 1.0f };
            int shot = 0;
            var log = new System.Text.StringBuilder();
            while (Time.time - t0 < 1.2f && sc.State == SkaterState.WallRide)
            {
                yield return null;
                float rt = Time.time - t0;
                // Knoechelhoehe ueber dem Deck (Deck-Ebene: Board-Pivot + deckTop entlang board.up)
                float h = 0f;
                for (int f = 0; f < 2; f++)
                    h = Mathf.Max(h, Vector3.Dot(map.foot[f].position - board.position, board.up) - sc.rig.deckTop);
                if (rt > 0.12f && h > worst) { worst = h; worstAt = rt; } // die ersten 0,12 s blendet das Board ein
                if (shot < shots.Length && rt >= shots[shot])
                {
                    log.Append($" {rt:0.0}s:{h:0.00}m");
                    var cam = new GameObject("WallCam").AddComponent<Camera>();
                    Vector3 p = sc.transform.position;
                    // vor dem Fahrer an der Wand entlang, leicht von der Wand weg: Abstand Fuss-Deck gut zu sehen
                    cam.transform.position = p + sc.transform.forward * 3.4f + sc.WallNormal * 1.4f + Vector3.up * 0.9f;
                    cam.transform.LookAt(p + Vector3.up * 0.6f - sc.WallNormal * 0.2f);
                    cam.fieldOfView = 50f;
                    SimTests.Capture(cam, $"Logs/wallride/wallride_{shots[shot]:0.0}s.png".Replace(',', '.'));
                    UnityEngine.Object.Destroy(cam.gameObject);
                    shot++;
                }
            }
            Debug.Log($"[Wallride] Knoechel ueber dem Deck:{log}");
            bool ok = worst < 0.28f; // Knoechel stehen am Board ca. 0,23 m ueber der Deck-Ebene; frei schwebend waren es 0,37 m
            Debug.Log($"[Wallride] {(ok ? "OK   " : "FEHLT")} Fuesse am Board (hoechstens {worst:0.00} m ueber dem Deck bei {worstAt:0.00} s) -> {(ok ? "WALLRIDE TEST PASS" : "WALLRIDE TEST FAIL")}");
            Quit(ok ? 0 : 1);
        }

        static void Quit(int code)
        {
            EditorApplication.update -= Tick;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            EditorSettings.enterPlayModeOptionsEnabled = _oldEnabled;
            EditorSettings.enterPlayModeOptions = _oldOptions;
            EditorApplication.Exit(code);
        }
    }
}
