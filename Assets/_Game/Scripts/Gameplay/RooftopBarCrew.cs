using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DriftSkate
{
    /// <summary>
    /// Leute auf der Rooftop-Bar (RoofSets.Bar): Barkeeper Rocco hinter dem Tresen (Modell aus Tools/Blender/build_bartender.py,
    /// wischt den Tresen, schuettelt ab und zu einen Drink) und Miru am DJ-Pult (eigene Animation "DJ" in build_miru.py).
    /// Die Park-Miru mit ihrem Auftrag bleibt, wo sie ist. Stellplaetze sind die Spot_-Punkte im Bar-Prefab.
    /// Nur Deko, auf jedem Rechner gleich, ohne Netzwerk.
    /// </summary>
    public static class RooftopBarCrew
    {
        /// <summary>Miru steht etwas vor dem Spot, sonst reicht sie nicht bequem an die Plattenteller.</summary>
        const float DjForward = 0.12f;

        public static readonly List<StonerNpc> Spawned = new List<StonerNpc>();

        public static void Spawn()
        {
            Spawned.Clear();
            Transform bar = null;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == RoofSets.Bar.name && t.Find("Model") != null) bar = t;
            if (bar == null)
            {
                Debug.LogWarning("RooftopBarCrew: keine Rooftop-Bar in der Stadt (DriftSkate → Dach-Sets → Rooftop-Bar bauen und setzen)");
                return;
            }
            var bartender = Find(bar, "Spot_Bartender");
            var dj = Find(bar, "Spot_DJ");
            var group = new GameObject("RooftopBarCrew").transform;

            if (bartender != null)
            {
                var rocco = StonerNpc.Spawn("Rocco", bartender.position, Flat(bartender.forward), group);
                if (rocco != null) Spawned.Add(rocco);
            }
            var miruPrefab = NpcPrefabs.Instance != null ? NpcPrefabs.Instance.miru : null;
            if (dj != null && miruPrefab != null)
            {
                Vector3 f = Flat(dj.forward);
                var miru = StonerNpc.SpawnPrefab("Miru", miruPrefab, dj.position + f * DjForward, f, group);
                if (miru != null)
                {
                    if (!miru.SetIdle("DJ")) Debug.LogWarning("RooftopBarCrew: Miru hat keine DJ-Animation (Tools/Blender/build_miru.py neu exportieren)");
                    Spawned.Add(miru);
                }
            }
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            Debug.LogWarning("RooftopBarCrew: " + name + " fehlt im Bar-Prefab");
            return null;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }
    }
}
