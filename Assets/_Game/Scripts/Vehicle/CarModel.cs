using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Fertig modelliertes Spielerauto (aus einer GLB, gebaut im Editor: DriftSkate → Autos → Garagen-Modelle bauen).
    /// Liegt unter Resources/CarModels. Die Farben stecken in einer kleinen Paletten-Textur; der Lack wird pro Auto
    /// darin umgefaerbt, Decals (Aufkleber) bleiben. CarBuilder haengt die vier Raeder an die Federbeine der Physik.
    /// </summary>
    public class CarModel : MonoBehaviour
    {
        [Tooltip("Vorn links, vorn rechts, hinten links, hinten rechts (Radmitte = Ursprung)")]
        public Transform[] wheels = new Transform[4];
        public Renderer[] paletteRenderers;   // Karosserie, Anbauteile, Raeder: teilen sich die Paletten-Textur
        public Color[] palette;               // Farben je Feld (sRGB)
        public int[] paintSlots;              // Felder, die die Lackfarbe bekommen
        public int paletteSize = 4, cell = 4;
        public Vector3 boxCenter, boxSize;    // Collider (Karosserie ohne Raeder)
        public float wheelRadius = 0.31f;

        Texture2D _tex;

        /// <summary>Lackfarbe setzen: eigene Paletten-Textur fuer dieses Auto, Materialien als Instanz.</summary>
        public void ApplyPaint(Color paint)
        {
            if (palette == null || palette.Length == 0) return;
            var cols = (Color[])palette.Clone();
            if (paintSlots != null)
                foreach (int i in paintSlots)
                    if (i >= 0 && i < cols.Length) cols[i] = paint;
            int size = paletteSize * cell;
            if (_tex == null)
            {
                _tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "CarPalette" };
            }
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int idx = (y / cell) * paletteSize + x / cell;
                    px[y * size + x] = idx < cols.Length ? cols[idx] : Color.magenta;
                }
            _tex.SetPixels(px);
            _tex.Apply();
            if (_mats == null)
            {
                // Eigene Kopien der Materialien (geht auch im Editor ohne Material-Leck-Warnung)
                _mats = new System.Collections.Generic.Dictionary<Material, Material>();
                foreach (var r in paletteRenderers)
                {
                    if (r == null) continue;
                    var shared = r.sharedMaterials;
                    for (int i = 0; i < shared.Length; i++)
                    {
                        if (shared[i] == null) continue;
                        if (!_mats.TryGetValue(shared[i], out var copy)) _mats[shared[i]] = copy = new Material(shared[i]);
                        shared[i] = copy;
                    }
                    r.sharedMaterials = shared;
                }
            }
            foreach (var m in _mats.Values) m.SetTexture("_BaseMap", _tex);
        }

        System.Collections.Generic.Dictionary<Material, Material> _mats;

        void OnDestroy()
        {
            Kill(_tex);
            if (_mats != null) foreach (var m in _mats.Values) Kill(m);
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
