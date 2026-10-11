using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Mirus Auftrag wie ein Mensch: annehmen, in den Park rollen, warten bis der Bot einen entdeckt und sich vor einen stellt,
    /// dann mit 0,3 s Reaktionszeit Ollie in ihn hinein (W bleibt gedrueckt), lenken, damit man im Park bleibt.
    /// Prueft drei Treffer, Absturz bis auf den Boden und dass Miru danach "erledigt" meldet. Bilder nach Logs/patrolbot/.
    /// Aufruf ohne -quit: -executeMethod DriftSkate.EditorTools.PatrolBotPlayTest.Run
    /// </summary>
    public static class PatrolBotPlayTest
    {
        const float Reaction = 0.3f;
        static double _started;
        static int _stage, _failures, _shots;
        static float _t0, _decideAt = -1f, _ollieUntil = -1f, _lastHitTime;
        static int _miruQuestBefore;
        static Vector3 _park;
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
            _stage = 0;
            _failures = 0;
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
            Debug.Log("[PatrolBotTest] " + (ok ? "OK: " : "FEHLT: ") + what);
            if (!ok) _failures++;
        }

        static void Shot(PatrolBot bot, string file)
        {
            var cam = Camera.main;
            System.IO.Directory.CreateDirectory("Logs/patrolbot");
            SimTests.Capture(cam, "Logs/patrolbot/" + file + ".png");
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 180) throw new Exception("Zeitlimit");
                if (!EditorApplication.isPlaying) return;
                var a = PlayerAvatar.Local;
                var miru = MiruTalk.Instance;
                if (a == null || miru == null || Time.time < 2f) return;
                var sc = a.skater;
                float t = Time.time;
                var bot = miru.Bot;
                switch (_stage)
                {
                    case 0:
                        _miruQuestBefore = SaveSystem.Profile.miruQuest;
                        SaveSystem.Profile.miruQuest = 0;
                        miru.Accept();
                        Check(miru.State == MiruTalk.Phase.Active && miru.Bot != null, "Auftrag angenommen, Bot gespawnt");
                        _park = new Vector3(CityBuilder.BlockCenter(3), 0f, CityBuilder.BlockCenter(2));
                        a.externalControl = true;
                        sc.inputEnabled = false;
                        a.PlaceForChallenge(false, _park + new Vector3(-24f, 0.05f, 0f), 90f);
                        _stage = 1; _t0 = t;
                        break;
                    case 1:
                    {
                        if (bot == null) throw new Exception("Bot weg");
                        // Fahren: im Park bleiben (zur Mitte lenken, wenn man zu weit draussen ist)
                        Vector3 pos = sc.transform.position;
                        Vector3 toCenter = _park - pos; toCenter.y = 0f;
                        float steer = 0f;
                        if (toCenter.magnitude > 14f)
                        {
                            float want = Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg;
                            steer = Mathf.Clamp(Mathf.DeltaAngle(sc.Heading, want) / 25f, -1f, 1f);
                        }
                        // Entscheidung wie ein Mensch: Bot schwebt vor einem und ist gleich da -> nach Reaktionszeit Ollie
                        Vector3 toBot = bot.Center - pos; float flat = new Vector2(toBot.x, toBot.z).magnitude;
                        Vector3 fwd = Quaternion.Euler(0f, sc.Heading, 0f) * Vector3.forward;
                        bool ahead = Vector3.Dot(new Vector3(toBot.x, 0f, toBot.z).normalized, fwd) > 0.75f;
                        if (_decideAt < 0f && _ollieUntil < 0f && sc.State == SkaterState.Riding && bot.Alerted && ahead &&
                            flat < 1.5f + sc.Speed * (Reaction + 0.45f) && bot.Center.y < 3.2f)
                            _decideAt = t + Reaction;
                        if (_decideAt > 0f && t >= _decideAt) { _decideAt = -1f; _ollieUntil = t + 0.2f; }
                        bool ollie = _ollieUntil > 0f && t < _ollieUntil;
                        if (_ollieUntil > 0f && t >= _ollieUntil) _ollieUntil = -1f;
                        sc.InjectInput(new Vector2(steer, sc.Speed < 6.5f ? 1f : 0f), ollie);

                        if (bot.Hits > 0 && t - _lastHitTime > 1f && _shots < bot.Hits)
                        {
                            _shots = bot.Hits;
                            _lastHitTime = t;
                            Debug.Log($"[PatrolBotTest] Treffer {bot.Hits} nach {t - _t0:0.0} s, Bot {bot.Center}, Skater {pos} {sc.State}");
                            Shot(bot, "hit_" + bot.Hits);
                        }
                        if (bot.IsDown) { _stage = 2; _t0 = t; }
                        else if (t - _t0 > 90f) throw new Exception($"Nach 90 s nur {bot.Hits} Treffer");
                        break;
                    }
                    case 2: // Absturz abwarten
                        sc.InjectInput(Vector2.zero, false);
                        if (miru.State == MiruTalk.Phase.Done)
                        {
                            Check(bot != null && bot.Center.y < 1.2f, $"Bot liegt am Boden ({bot?.Center})");
                            Check(true, "Drei Treffer, Bot abgestuerzt, Miru: zurueck zu ihr");
                            Shot(bot, "down");
                            _stage = 3; _t0 = t;
                        }
                        else if (t - _t0 > 8f) throw new Exception("Bot ist nicht unten angekommen: " + bot?.Center);
                        break;
                    case 3:
                        SaveSystem.Profile.miruQuest = _miruQuestBefore;
                        Quit(_failures == 0 ? 0 : 1);
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[PatrolBotTest] FEHLT: " + e.Message);
                SaveSystem.Profile.miruQuest = _miruQuestBefore;
                Quit(1);
            }
        }
    }
}
