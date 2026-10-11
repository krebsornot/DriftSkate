using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Das Drift-Parkhaus (Prefab aus Resources, gebaut mit DriftSkate → Parkhaus) steht statt des grossen Hauses in
    /// Block_03 (Stadtrand Sued-Ost, 60 x 60 m). Apply raeumt dort alles weg, was im 64 x 64 m grossen Parkhaus stecken
    /// wuerde (Haus, Dach-Deko, Markise, Strassen-Deko, die passenden Stuecke der zusammengefassten Fenster-Meshes) und
    /// setzt das Parkhaus, Einfahrt nach +Z (Stadtmitte). Laeuft beim Neubau der Stadt (CityBuilder) und per Editor-Menue
    /// auf der fertigen Szene; mehrfach ausfuehren ist harmlos.
    /// </summary>
    public static class ParkhausSite
    {
        public const string PrefabPath = "Buildings/Parkhaus";
        public const string BlockName = "Block_03_B";
        public const string PrefabName = "Parkhaus";
        const float Half = 32f, Margin = 0.6f, Gate = 18f, GateHalf = 8.5f;

        public static GameObject Apply(Transform cityRoot)
        {
            var prefab = Resources.Load<GameObject>(PrefabPath);
            if (prefab == null) return null;
            Transform block = null;
            foreach (Transform t in cityRoot) if (t.name == BlockName) block = t;
            if (block == null) { Debug.LogWarning("ParkhausSite: " + BlockName + " fehlt"); return null; }

            // Mitte: altes Haus bzw. schon gesetztes Parkhaus
            var old = block.Find(prefab.name);
            var house = block.Find("Building");
            Vector3 c = old != null ? old.position : house != null ? house.position : block.position;
            c.y = 0f;
            var area = new Bounds(c + Vector3.up * 50f, new Vector3(2f * (Half + Margin), 100f, 2f * (Half + Margin)));
            // Vor Ein- und Ausfahrt (x = +-18 m, Seite +Z) bis zur Bordsteinkante frei: keine Laterne, kein Hydrant im Weg
            bool Inside(Vector3 p) => (Mathf.Abs(p.x - c.x) < Half + Margin && Mathf.Abs(p.z - c.z) < Half + Margin)
                || (Mathf.Abs(Mathf.Abs(p.x - c.x) - Gate) < GateHalf && p.z - c.z > 0f && p.z - c.z < Half + 5f);

            var (removed, cut) = CityPlots.Clear(cityRoot, block, Inside, area);
            CityPlots.SinkSidewalk(block, new Rect(c.x - Half - Margin, c.z - Half - Margin, 2f * (Half + Margin), 2f * (Half + Margin)));

            var inst = Object.Instantiate(prefab, block);
            inst.name = prefab.name;
            inst.transform.SetPositionAndRotation(c, Quaternion.identity);
            Debug.Log($"[Parkhaus] in {BlockName} bei ({c.x:0}, {c.z:0}) gesetzt, {removed} Objekte und {cut} Fenster-Dreiecke entfernt");
            return inst;
        }
    }
}
