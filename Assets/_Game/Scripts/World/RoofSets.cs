using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Fertige Sets fuer grosse Flachdaecher (Prefabs aus Resources, gebaut mit DriftSkate → Dach-Sets aus GLBs von
    /// Claude 3D Design). Apply raeumt die Dach-Deko im Weg weg und setzt das Prefab mitten aufs groesste Dach des Blocks,
    /// die Prefab-Vorderseite (+Z) zeigt zum naechsten Hydranten: von dort schleudert die Fontaene Board und Auto aufs Dach.
    /// Laeuft beim Neubau der Stadt (CityBuilder) und per Editor-Menue auf der fertigen Szene, mehrfach ausfuehren ist harmlos.
    /// Nur Daecher bis ca. 25 m Hoehe nehmen: hoeher findet die Hydranten-Fontaene sie nicht (Suchradius 26 m).
    /// </summary>
    public static class RoofSets
    {
        public class Def
        {
            public string name, prefabPath, blockName, glb, assetRoot;
            /// <summary>Halbe Grundflaeche des fertigen Sets (nach Skalierung), fuer die Deko-Raeumung.</summary>
            public float half;
            /// <summary>Skalierung des Modells, genau einmal im Prefab angewendet.</summary>
            public float scale = 1f;
            /// <summary>Knoten, der nach vorn (+Z) zeigen soll (z. B. die Rampe); ohne: Modell-Vorderseite bleibt.</summary>
            public string frontNode;
        }

        /// <summary>Hubschrauber-Landeplatz: 60-m-Dach suedlich vom Herz-Denkmal (14,5 m), Modell 24 m, 1,5-fach (wirkte sonst winzig).</summary>
        public static readonly Def Helipad = new Def
        {
            name = "Helipad", prefabPath = "Buildings/Helipad", blockName = "Block_12_B",
            glb = "Art/Props/rooftop_helipad.glb", assetRoot = "Assets/_Game/Helipad", scale = 1.5f, half = 19.5f, frontNode = "Drive_Ramp",
        };

        /// <summary>Rooftop-Bar "MOON BAR" mit Lounge, DJ-Ecke und Skate-Teilen: 60-m-Dach im Nordwesten (24,6 m), Modell 34,5 m,
        /// Tresen schaut nach +Z. Spot_-Marker (Barkeeper, DJ, Gaeste) bleiben als leere Punkte im Prefab.</summary>
        public static readonly Def Bar = new Def
        {
            name = "RooftopBar", prefabPath = "Buildings/RooftopBar", blockName = "Block_40_B",
            glb = "Art/Props/rooftop_bar.glb", assetRoot = "Assets/_Game/RooftopBar", half = 18.5f,
        };

        public static readonly Def[] All = { Helipad, Bar };

        static readonly string[] RoofDeco = { "AC", "WaterTank", "Billboard", "BillboardPole" };

        public static void ApplyAll(Transform cityRoot)
        {
            foreach (var d in All) Apply(cityRoot, d);
        }

        public static GameObject Apply(Transform cityRoot, Def def)
        {
            var prefab = Resources.Load<GameObject>(def.prefabPath);
            if (prefab == null) return null;
            Transform block = null;
            foreach (Transform t in cityRoot) if (t.name == def.blockName) block = t;
            if (block == null) { Debug.LogWarning($"RoofSets: {def.blockName} fehlt ({def.name})"); return null; }

            // groesstes Dach im Block
            Transform roof = null;
            float area = 0f;
            foreach (Transform t in block)
            {
                if (t.name != "Roof") continue;
                float a = t.lossyScale.x * t.lossyScale.z;
                if (a > area) { area = a; roof = t; }
            }
            if (roof == null) { Debug.LogWarning($"RoofSets: kein Dach in {def.blockName} ({def.name})"); return null; }
            Vector3 top = roof.position + Vector3.up * roof.lossyScale.y * 0.5f;

            // Dach-Deko im Weg (Klimageraete, Wassertanks, Werbetafeln) und ein schon gesetztes Set kommen weg
            var kill = new List<GameObject>();
            foreach (Transform t in block)
            {
                bool deco = System.Array.IndexOf(RoofDeco, t.name) >= 0 && t.position.y > top.y - 1f
                            && Mathf.Abs(t.position.x - top.x) < def.half + t.lossyScale.x * 0.5f
                            && Mathf.Abs(t.position.z - top.z) < def.half + t.lossyScale.z * 0.5f;
                if (deco || t.name == def.name) kill.Add(t.gameObject);
            }
            foreach (var g in kill) { g.SetActive(false); Shapes.DestroySafe(g); }

            // Vorderseite (+Z) zum naechsten Hydranten: dort landet man nach der Fontaene
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
            inst.name = def.name;
            inst.transform.SetPositionAndRotation(top, Quaternion.LookRotation(toward));
            Debug.Log($"[{def.name}] auf {def.blockName} bei ({top.x:0}, {top.y:0.0}, {top.z:0}) gesetzt, Vorderseite nach {toward}, {kill.Count} Objekte entfernt");
            return inst;
        }
    }
}
