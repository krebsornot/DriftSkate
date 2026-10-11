using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Hubschrauber-Landeplatz (Prefab aus Resources, gebaut mit DriftSkate → Helipad) auf dem grossen Dach von Block_12
    /// (60 x 60 m, 14,5 m hoch, gleich suedlich vom Herz-Denkmal). Der Hydrant daneben schleudert Board und Auto aufs Dach,
    /// die Rampe des Landeplatzes zeigt zu ihm. Apply raeumt die Dach-Deko im Weg weg und setzt das Prefab; laeuft beim
    /// Neubau der Stadt (CityBuilder) und per Editor-Menue auf der fertigen Szene, mehrfach ausfuehren ist harmlos.
    /// </summary>
    public static class HelipadSite
    {
        public const string PrefabPath = "Buildings/Helipad";
        public const string BlockName = "Block_12_B";
        public const string PrefabName = "Helipad";
        /// <summary>Das Modell (24 x 24 m) wirkte auf dem 60-m-Dach winzig: im Prefab einmal so gross skaliert (36 x 36 m).</summary>
        public const float Scale = 1.5f;
        /// <summary>Halbe Grundflaeche des Sets plus etwas Luft fuer die Deko-Raeumung.</summary>
        const float Half = 12f * Scale + 1.5f;
        static readonly string[] RoofDeco = { "AC", "WaterTank", "Billboard", "BillboardPole" };

        public static GameObject Apply(Transform cityRoot)
        {
            var prefab = Resources.Load<GameObject>(PrefabPath);
            if (prefab == null) return null;
            Transform block = null;
            foreach (Transform t in cityRoot) if (t.name == BlockName) block = t;
            if (block == null) { Debug.LogWarning("HelipadSite: " + BlockName + " fehlt"); return null; }

            // groesstes Dach im Block
            Transform roof = null;
            float area = 0f;
            foreach (Transform t in block)
            {
                if (t.name != "Roof") continue;
                float a = t.lossyScale.x * t.lossyScale.z;
                if (a > area) { area = a; roof = t; }
            }
            if (roof == null) { Debug.LogWarning("HelipadSite: kein Dach in " + BlockName); return null; }
            Vector3 top = roof.position + Vector3.up * roof.lossyScale.y * 0.5f;

            // Dach-Deko im Weg (Klimageraete, Wassertanks, Werbetafeln) und ein alter Landeplatz kommen weg
            var kill = new List<GameObject>();
            foreach (Transform t in block)
            {
                bool deco = System.Array.IndexOf(RoofDeco, t.name) >= 0 && t.position.y > top.y - 1f
                            && Mathf.Abs(t.position.x - top.x) < Half + t.lossyScale.x * 0.5f
                            && Mathf.Abs(t.position.z - top.z) < Half + t.lossyScale.z * 0.5f;
                if (deco || t.name == PrefabName) kill.Add(t.gameObject);
            }
            foreach (var g in kill) { g.SetActive(false); Shapes.DestroySafe(g); }

            // Rampe (Prefab +Z) zeigt zum naechsten Hydranten: dort landet man nach der Fontaene
            Vector3 toward = Vector3.forward;
            float best = float.MaxValue;
            foreach (var t in cityRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Hydrant") continue;
                Vector3 d = t.position - top;
                d.y = 0f;
                if (d.sqrMagnitude < best) { best = d.sqrMagnitude; toward = d; }
            }
            // auf die naechste Dachseite runden, damit das Set gerade auf dem Dach steht
            toward = Mathf.Abs(toward.x) > Mathf.Abs(toward.z) ? new Vector3(Mathf.Sign(toward.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(toward.z));

            var inst = Object.Instantiate(prefab, block);
            inst.name = PrefabName;
            inst.transform.SetPositionAndRotation(top, Quaternion.LookRotation(toward));
            Debug.Log($"[Helipad] auf {BlockName} bei ({top.x:0}, {top.y:0.0}, {top.z:0}) gesetzt, Rampe nach {toward}, {kill.Count} Objekte entfernt");
            return inst;
        }
    }
}
