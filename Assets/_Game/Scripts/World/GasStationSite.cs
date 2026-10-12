using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Skate-Tankstelle (Prefab aus Resources, gebaut mit DriftSkate → Tankstelle) statt des grossen Hauses in Block_43
    /// im Norden (35 x 60 m, Front zur Strasse im Westen). Neben den Haeusern wirkte das Modell klein: im Prefab einmal um
    /// Scale vergroessert (45 x 34 m), dafuer der Platz mit genug Tiefe. Zapfsaeulen und Preis-Pylon zeigen zur Strasse,
    /// der Laden steht hinten, die Skate-Ecke (Quarter, Funbox, Rails, Treppe) an der Seite. Frueher stand sie im Eckhaus
    /// suedoestlich in Block_32, dort war kein Platz fuer mehr Groesse (CAN CLUB daneben, Hochhaus dahinter).
    /// Graffiti-Stellen des alten Hauses kommen an die Seitenwand des Ladens.
    /// Laeuft beim Neubau der Stadt (CityBuilder) und per Editor-Menue, mehrfach ausfuehren ist harmlos.
    /// </summary>
    public static class GasStationSite
    {
        public const string PrefabPath = "Buildings/Tankstelle";
        public const string BlockName = "Block_43_B";
        /// <summary>Vergroesserung des Modells (genau einmal, im Prefab unter "Scaled").</summary>
        public const float Scale = 1.4f;
        /// <summary>
        /// Hof im Modell (unskaliert, Front nach -Z der Station): halbe Breite 16 m, Vorderkante 11 m vor der Mitte,
        /// Hinterkante 13 m dahinter.
        /// </summary>
        const float ModelHalfX = 16f, ModelFront = 11f, ModelBack = 13f;
        /// <summary>Seitenwand des Ladens im Modell (Front nach -Z), Graffiti-Stellen dort.</summary>
        static readonly Vector3 ModelTagLocal = new Vector3(-14.06f, 2.6f, 9.9f);

        /// <summary>Wo die Graffiti-Stelle relativ zur Station sitzt (skaliert, in deren Ausrichtung).</summary>
        public static Vector3 TagLocal => ModelTagLocal * Scale;

        public static GameObject Apply(Transform cityRoot)
        {
            var prefab = Resources.Load<GameObject>(PrefabPath);
            if (prefab == null) return null;
            Transform block = null;
            foreach (Transform t in cityRoot) if (t.name == BlockName) block = t;
            if (block == null) { Debug.LogWarning("GasStationSite: " + BlockName + " fehlt"); return null; }

            // Das groesste Haus im Block (bzw. die schon gesetzte Station merkt sich Mitte und Richtung)
            Vector3 c, front;
            Bounds? houseArea = null; // Grundflaeche des alten Hauses (laenger als die Station: Fenster und Dach-Deko dort auch weg)
            var old = block.Find(prefab.name);
            if (old != null)
            {
                c = old.position;
                front = -old.forward;
            }
            else
            {
                Transform house = null;
                foreach (Transform t in block)
                    if (t.name == "Building" && (house == null || t.lossyScale.x * t.lossyScale.z > house.lossyScale.x * house.lossyScale.z)) house = t;
                if (house == null) { Debug.LogWarning("GasStationSite: kein Haus in " + BlockName); return null; }
                // Front zur Strasse: die Seite, zu der das Haus aus der Blockmitte verschoben ist
                // (die Block-Objekte liegen im Ursprung, die Mitte gibt die Gehweg-Platte des Blocks)
                var walk = block.Find("Sidewalk");
                Vector3 blockCenter = walk != null ? walk.position : block.position;
                Vector3 off = house.position - blockCenter;
                off.y = 0f;
                if (off.sqrMagnitude < 1f) off = blockCenter.x < 0f ? Vector3.right : Vector3.left;
                front = Mathf.Abs(off.x) > Mathf.Abs(off.z) ? new Vector3(Mathf.Sign(off.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(off.z));
                float halfDepth = Mathf.Abs(Vector3.Dot(house.lossyScale, front)) * 0.5f;
                Vector3 houseFront = house.position + front * halfDepth;
                houseFront.y = 0f;
                // Vorderkante der Station an der Hausfront, seitlich mittig auf dem Haus
                c = houseFront - front * (ModelFront * Scale);
                var hb = new Bounds(new Vector3(house.position.x, 50f, house.position.z), new Vector3(house.lossyScale.x + 2f, 100f, house.lossyScale.z + 2f));
                houseArea = hb;
            }
            c.y = 0f;
            // Station: Modell-Front zeigt nach -Z der Station, also Station-Vorwaerts = weg von der Strasse
            Quaternion rot = Quaternion.LookRotation(-front, Vector3.up);
            float halfX = ModelHalfX * Scale, frontZ = ModelFront * Scale, backZ = ModelBack * Scale;

            // Weg kommt alles, was im Hof, im alten Haus oder auf dem Gehweg davor steht (dort Palmen und Laternen der Station)
            bool Inside(Vector3 p)
            {
                Vector3 l = Quaternion.Inverse(rot) * (p - c);
                if (houseArea.HasValue && houseArea.Value.Contains(new Vector3(p.x, 50f, p.z))) return true;
                return Mathf.Abs(l.x) < halfX + 1f && l.z > -(frontZ + 3.5f) && l.z < backZ + 3f;
            }
            Vector3 a = rot * new Vector3(-halfX - 1f, 0f, -(frontZ + 3.5f)), b = rot * new Vector3(halfX + 1f, 0f, backZ + 3f);
            var area = new Bounds(c + (a + b) * 0.5f + Vector3.up * 50f, new Vector3(Mathf.Abs(a.x - b.x), 100f, Mathf.Abs(a.z - b.z)));
            if (houseArea.HasValue) area.Encapsulate(houseArea.Value);

            int tags = 0;
            var (removed, cut) = CityPlots.Clear(cityRoot, block, Inside, area, tag =>
            {
                Vector3 side = rot * Vector3.left;
                tag.transform.SetPositionAndRotation(c + rot * (TagLocal + Vector3.back * (tags++ * 5.5f)), Quaternion.LookRotation(side));
                tag.transform.localScale = Vector3.one * 0.75f;
            });
            Vector3 pa = rot * new Vector3(-halfX, 0f, -frontZ), pb = rot * new Vector3(halfX, 0f, backZ);
            CityPlots.SinkSidewalk(block, Rect.MinMaxRect(c.x + Mathf.Min(pa.x, pb.x), c.z + Mathf.Min(pa.z, pb.z), c.x + Mathf.Max(pa.x, pb.x), c.z + Mathf.Max(pa.z, pb.z)));

            var inst = Object.Instantiate(prefab, block);
            inst.name = prefab.name;
            inst.transform.SetPositionAndRotation(c, rot);
            Debug.Log($"[Tankstelle] in {BlockName} bei ({c.x:0.0}, {c.z:0.0}) gesetzt, Front nach {front} (x{Scale}), {removed} Objekte und {cut} Fenster-Dreiecke entfernt, {tags} Graffiti-Stellen an die Ladenwand");
            return inst;
        }
    }
}
