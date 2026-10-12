using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DriftSkate
{
    /// <summary>
    /// Schaltet alle Ampeln der Stadt nach der gemeinsamen Uhr (online bei allen gleich, nichts wird uebertragen).
    /// Die Lichter der Ampeln sind Meshes, deren UVs in eine kleine Signal-Textur zeigen (Spalte = Lampe, Zeile = Richtung):
    /// beim Umschalten werden nur ihre Pixel neu gesetzt, alle Ampeln teilen sich ein Material.
    /// Richtung X = Ampeln, die entlang der X-Achse schauen (fuer Autos, die in X-Richtung fahren), Z entsprechend.
    /// Fussgaengerampeln schauen ueber die Strasse und haben Gruen, wenn die Autos in ihrer Blickrichtung Gruen haben.
    /// Beim Laden werden alle Ampeln einer Kreuzung zu wenigen Meshes zusammengefasst (Draw Calls).
    /// Aufgestellt von DriftSkate → Ampeln (TrafficLightAssets).
    /// </summary>
    public class TrafficSignals : MonoBehaviour
    {
        public const string LampsX = "Lamps_X", LampsZ = "Lamps_Z";
        /// <summary>Spalten der Signal-Textur.</summary>
        public const int CarRed = 0, CarAmber = 1, CarGreen = 2, PedRed = 3, PedGreen = 4, Columns = 8;
        public const float Cycle = 30f, Green = 11f, Amber = 3f, PedWalk = 9f;

        static readonly Color[] On =
        {
            new Color(1f, 0.12f, 0.1f), new Color(1f, 0.62f, 0.08f), new Color(0.2f, 1f, 0.45f),
            new Color(1f, 0.16f, 0.12f), new Color(0.35f, 1f, 0.6f),
        };
        const float OffLevel = 0.16f;

        /// <summary>Material der Lichter (Signal-Textur aus dem Bake, im Editor ohne Play-Modus die Grundstellung).</summary>
        public Material lampMaterial;

        Texture2D _tex;
        Material _mat;

        /// <summary>Laufende Signal-Textur (nur im Spiel).</summary>
        public Texture2D StateTexture => _tex;
        int _stateX = -1, _stateZ = -1;

        /// <summary>UV fuer Lampe 'lamp' in Richtung 'group' (0 = X, 1 = Z).</summary>
        public static Vector2 LampUv(int lamp, int group) => new Vector2((lamp + 0.5f) / Columns, (group + 0.5f) / 2f);

        /// <summary>Farben der Signal-Textur fuer beide Richtungen (Zustand 0..2 = Gruen, Gelb, Rot; Fussgaenger gehen bei walk).</summary>
        public static Color[] Pixels(int stateX, bool walkX, int stateZ, bool walkZ)
        {
            var px = new Color[Columns * 2];
            for (int g = 0; g < 2; g++)
            {
                int state = g == 0 ? stateX : stateZ;
                bool walk = g == 0 ? walkX : walkZ;
                for (int lamp = 0; lamp < On.Length; lamp++)
                {
                    bool lit = lamp == CarGreen ? state == 0 : lamp == CarAmber ? state == 1 : lamp == CarRed ? state == 2
                             : lamp == PedGreen ? walk : !walk;
                    px[g * Columns + lamp] = lit ? On[lamp] : On[lamp] * OffLevel;
                }
            }
            return px;
        }

        /// <summary>Lichter nach Blickrichtung waehlen: jede Ampel hat beide Licht-Meshes, eins bleibt aus.</summary>
        public static void ApplyGroups(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != LampsX && t.name != LampsZ) continue;
                Vector3 f = t.parent.forward;
                bool x = Mathf.Abs(f.x) > Mathf.Abs(f.z);
                t.gameObject.SetActive(t.name == (x ? LampsX : LampsZ));
            }
        }

        void Awake()
        {
            ApplyGroups(transform);
            if (lampMaterial != null)
            {
                _tex = new Texture2D(Columns, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "TrafficSignalState" };
                _mat = new Material(lampMaterial) { name = lampMaterial.name + " (Schaltung)" };
                _mat.SetTexture("_BaseMap", _tex);
            }
            foreach (Transform crossing in transform) Merge(crossing);
            Update();
        }

        /// <summary>Alle Ampeln einer Kreuzung je Material/Layer zu einem Mesh zusammenfassen.</summary>
        void Merge(Transform crossing)
        {
            var groups = new Dictionary<(Material, int, ShadowCastingMode), List<CombineInstance>>();
            Matrix4x4 toLocal = crossing.worldToLocalMatrix;
            foreach (var r in crossing.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var key = (r.sharedMaterial, r.gameObject.layer, r.shadowCastingMode);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mf.sharedMesh, transform = toLocal * r.localToWorldMatrix });
                r.enabled = false;
            }
            int n = 0;
            foreach (var kv in groups)
            {
                var mesh = new Mesh { name = crossing.name + "_" + n };
                int verts = 0;
                foreach (var ci in kv.Value) verts += ci.mesh.vertexCount;
                if (verts > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                var go = new GameObject("Merged_" + n++);
                go.layer = kv.Key.Item2;
                go.transform.SetParent(crossing, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                bool lamps = kv.Key.Item1 == lampMaterial;
                mr.sharedMaterial = lamps && _mat != null ? _mat : kv.Key.Item1;
                mr.shadowCastingMode = kv.Key.Item3;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        /// <summary>Ampelphase fuer Richtung X (Z ist um eine halbe Runde versetzt): 0 Gruen, 1 Gelb, 2 Rot.</summary>
        public static int State(double time, bool z, out bool walk)
        {
            float t = (float)((time + (z ? Cycle * 0.5 : 0.0)) % Cycle);
            if (t < 0f) t += Cycle;
            walk = t < PedWalk;
            return t < Green ? 0 : t < Green + Amber ? 1 : 2;
        }

        /// <summary>Sekunden, bis die Richtung Rot bekommt (0 bei Rot): bei Gelb entscheidet das, ob ein Auto noch durchfaehrt.</summary>
        public static float UntilRed(double time, bool z)
        {
            float t = (float)((time + (z ? Cycle * 0.5 : 0.0)) % Cycle);
            if (t < 0f) t += Cycle;
            return Mathf.Max(0f, Green + Amber - t);
        }

        void Update()
        {
            if (_tex == null) return;
            double time = SharedClock.Time;
            int sx = State(time, false, out bool wx), sz = State(time, true, out bool wz);
            int kx = sx * 2 + (wx ? 1 : 0), kz = sz * 2 + (wz ? 1 : 0);
            if (kx == _stateX && kz == _stateZ) return;
            _stateX = kx;
            _stateZ = kz;
            _tex.SetPixels(Pixels(sx, wx, sz, wz));
            _tex.Apply(false);
        }

        void OnDestroy()
        {
            if (_tex != null) Destroy(_tex);
            if (_mat != null) Destroy(_mat);
        }
    }
}
