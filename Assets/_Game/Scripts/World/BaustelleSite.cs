using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Baustelle mit drehendem Baukran (Prefab aus Resources, gebaut mit DriftSkate → Baustelle) statt des orangen Hauses
    /// in Block_04, gegenueber vom Parkhaus. Der Kran steht so, dass sein Ausleger beim Drehen ueber das Parkhaus schwenkt.
    /// Die Graffiti-Stelle der Hauswand kommt an den Bauzaun. Laeuft beim Neubau der Stadt und per Editor-Menue,
    /// mehrfach ausfuehren ist harmlos.
    /// </summary>
    public static class BaustelleSite
    {
        public const string PrefabPath = "Buildings/Baustelle";
        public const string BlockName = "Block_04_B";
        /// <summary>Grundstueck relativ zur Mitte des alten Hauses: altes Haus 30 x 60 m, die Baustelle ragt 3 m weiter nach Westen (auf den Gehweg).</summary>
        const float West = 18.6f, East = 15.6f, HalfZ = 30.6f;
        /// <summary>Bauzaun Westseite (zur Strasse und zum Parkhaus) relativ zur Mitte, Graffiti-Stelle dort.</summary>
        public static readonly Vector3 TagLocal = new Vector3(-18.08f, 1.3f, 14f);

        public static GameObject Apply(Transform cityRoot)
        {
            var prefab = Resources.Load<GameObject>(PrefabPath);
            if (prefab == null) return null;
            Transform block = null, parkhaus = null;
            foreach (Transform t in cityRoot)
            {
                if (t.name == BlockName) block = t;
                if (t.name == ParkhausSite.BlockName) parkhaus = t;
            }
            if (block == null) { Debug.LogWarning("BaustelleSite: " + BlockName + " fehlt"); return null; }

            // Mitte: schon gesetzte Baustelle, sonst das Haus, das dem Parkhaus am naechsten steht (das orange)
            Vector3 c;
            var old = block.Find(prefab.name);
            if (old != null) c = old.position;
            else
            {
                var ph = parkhaus != null ? parkhaus.Find(ParkhausSite.PrefabName) : null;
                Vector3 toward = ph != null ? ph.position : parkhaus != null ? parkhaus.position : Vector3.zero;
                Transform best = null;
                foreach (Transform t in block)
                    if (t.name == "Building" && (best == null || (t.position - toward).sqrMagnitude < (best.position - toward).sqrMagnitude)) best = t;
                if (best == null) { Debug.LogWarning("BaustelleSite: kein Haus in " + BlockName); return null; }
                c = best.position;
            }
            c.y = 0f;
            bool Inside(Vector3 p) => p.x - c.x > -West && p.x - c.x < East && Mathf.Abs(p.z - c.z) < HalfZ;
            var area = new Bounds(c + new Vector3((East - West) * 0.5f, 50f, 0f), new Vector3(West + East, 100f, 2f * HalfZ));

            var (removed, cut) = CityPlots.Clear(cityRoot, block, Inside, area, tag =>
            {
                // an den Bauzaun (Westseite), etwas kleiner, damit sie nicht ueber den Zaun ragt
                tag.transform.SetPositionAndRotation(c + TagLocal, Quaternion.LookRotation(Vector3.left));
                tag.transform.localScale = Vector3.one * 0.75f;
            });

            var inst = Object.Instantiate(prefab, block);
            inst.name = prefab.name;
            inst.transform.SetPositionAndRotation(c, Quaternion.identity);
            Debug.Log($"[Baustelle] in {BlockName} bei ({c.x:0}, {c.z:0}) gesetzt, {removed} Objekte und {cut} Fenster-Dreiecke entfernt");
            return inst;
        }
    }
}
