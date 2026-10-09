using UnityEngine;

namespace DriftSkate
{
    /// <summary>Globale Einstellungen beim Spielstart (vor der ersten Szene).</summary>
    public static class Boot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            Time.fixedDeltaTime = 0.01f; // 100 Hz Physik fuer stabiles Drift-Verhalten
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 1;
            // Gelaender und Rails sind fest, auch fuer Skater (gegrindet wird per Knopf). Aeltere Projekteinstellungen
            // hatten diese Kollision abgeschaltet, deshalb hier ausdruecklich einschalten.
            int skater = LayerMask.NameToLayer("Skater"), rail = LayerMask.NameToLayer("Rail");
            if (skater >= 0 && rail >= 0) Physics.IgnoreLayerCollision(skater, rail, false);
            GameInput.Ensure();
            if (AutoTest.Enabled || PerfTest.Enabled)
            {
                // Eigener, frischer Spielstand fuer den automatischen Test
                string sub = PerfTest.Enabled ? "perftest" : AutoTest.NetHost ? "autotest_host" : AutoTest.NetClient ? "autotest_client" : "autotest";
                SaveSystem.DataFolder = System.IO.Path.Combine(Application.persistentDataPath, sub);
                System.IO.Directory.CreateDirectory(SaveSystem.DataFolder);
                if (System.IO.File.Exists(SaveSystem.ProfilePath)) System.IO.File.Delete(SaveSystem.ProfilePath);
            }
            SaveSystem.Load();
            MusicPlayer.Ensure();
            ModLibrary.EnsureScanned();
            if (AutoTest.Enabled) AutoTest.Create();
            if (PerfTest.Enabled) PerfTest.Create();
        }
    }
}
