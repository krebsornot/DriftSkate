using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Eine grindbare Kante als Linienzug (Gelaender, Bordstein, Ledge). Punkte sind lokal zum Objekt.
    /// Der Skater rastet ein, wenn er nah genug ist und Grind drueckt.
    /// Hat die Rail sichtbare Stangen ("Bar", vom CityBuilder, auch im Editor kopierte "Bar (1)" ...), folgt die Linie
    /// den Stangen: verschoben, gedreht oder gestreckt grindet man trotzdem genau darauf. Aneinandergesetzte Stangen
    /// werden zu einer durchgehenden Linie verbunden (z. B. oben waagerecht, dann die Treppe runter); Stangen, die nicht
    /// aneinander haengen, werden im Spiel zu eigenen Rails. Am Ende einer Rail geht es auf einer anschliessenden weiter
    /// (siehe <see cref="FindConnected"/>).
    /// </summary>
    public class GrindRail : MonoBehaviour
    {
        public static readonly List<GrindRail> All = new List<GrindRail>();

        public Vector3[] points = { new Vector3(0, 0, -2), new Vector3(0, 0, 2) };
        public string label = "RAIL";

        /// <summary>So weit ueber der Mittelachse der Stange laeuft die Grind-Linie (wie beim Bau im CityBuilder).</summary>
        const float BarTop = 0.04f;
        /// <summary>Stangen- bzw. Rail-Enden, die naeher beieinander liegen, gelten als verbunden.</summary>
        public const float JoinDistance = 0.6f;

        Vector3[] _world;
        float[] _cumulative;
        bool _splitDone;
        public float Length { get; private set; }

        void OnEnable()
        {
            All.Add(this);
            Rebuild();
        }

        void OnDisable() => All.Remove(this);

        public void Rebuild()
        {
            var chains = WorldChains();
            _world = chains.Count > 0 ? chains[0] : null;
            if (_world == null) return;
            _cumulative = new float[_world.Length];
            Length = 0f;
            for (int i = 1; i < _world.Length; i++)
            {
                Length += Vector3.Distance(_world[i - 1], _world[i]);
                _cumulative[i] = Length;
            }
            // Weitere, nicht verbundene Stangen: im Spiel als eigene Rails daneben (ohne Stangen, nur Linie)
            if (chains.Count > 1 && Application.isPlaying && !_splitDone)
            {
                _splitDone = true;
                for (int c = 1; c < chains.Count; c++)
                {
                    var go = new GameObject(name + "_Teil" + c);
                    go.layer = gameObject.layer;
                    go.transform.SetParent(transform, false);
                    var part = go.AddComponent<GrindRail>();
                    part.enabled = false;
                    part.label = label;
                    part.points = System.Array.ConvertAll(chains[c], p => go.transform.InverseTransformPoint(p));
                    part.enabled = true;
                }
            }
        }

        public Vector3 StartPoint => _world[0];
        public Vector3 EndPoint => _world[_world.Length - 1];

        public bool Closest(Vector3 position, out Vector3 point, out float distanceAlong)
        {
            point = Vector3.zero;
            distanceAlong = 0f;
            if (_world == null) Rebuild();
            if (_world == null) return false;
            float best = float.MaxValue;
            for (int i = 0; i < _world.Length - 1; i++)
            {
                Vector3 a = _world[i], b = _world[i + 1];
                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(position - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                Vector3 p = a + ab * t;
                float d = (p - position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    point = p;
                    distanceAlong = _cumulative[i] + ab.magnitude * t;
                }
            }
            return true;
        }

        public Vector3 PointAt(float s)
        {
            int i = Segment(s);
            float segLen = _cumulative[i + 1] - _cumulative[i];
            float t = segLen > 0 ? (s - _cumulative[i]) / segLen : 0f;
            return Vector3.Lerp(_world[i], _world[i + 1], Mathf.Clamp01(t));
        }

        public Vector3 DirectionAt(float s)
        {
            int i = Segment(s);
            return (_world[i + 1] - _world[i]).normalized;
        }

        int Segment(float s)
        {
            for (int i = 0; i < _cumulative.Length - 1; i++)
                if (s <= _cumulative[i + 1]) return i;
            return _cumulative.Length - 2;
        }

        /// <summary>
        /// Schliesst an diesem Ende (end, Fahrtrichtung dir) eine andere Rail an? Dann dort weitergrinden:
        /// s = Startposition auf der neuen Rail, sign = +1/-1 Richtung auf ihr. Knicke bis maxAngle Grad gehen.
        /// </summary>
        public static bool FindConnected(GrindRail from, Vector3 end, Vector3 dir, out GrindRail next, out float s, out float sign, float maxAngle = 80f)
        {
            next = null;
            s = 0f;
            sign = 1f;
            float best = JoinDistance * JoinDistance;
            foreach (var r in All)
            {
                if (r == from || r == null || r._world == null) continue;
                float dStart = (r.StartPoint - end).sqrMagnitude, dEnd = (r.EndPoint - end).sqrMagnitude;
                if (dStart < best && Vector3.Angle(dir, r.DirectionAt(0f)) < maxAngle) { best = dStart; next = r; s = 0f; sign = 1f; }
                if (dEnd < best && Vector3.Angle(dir, -r.DirectionAt(r.Length)) < maxAngle) { best = dEnd; next = r; s = r.Length; sign = -1f; }
            }
            return next != null;
        }

        /// <summary>Linien in Weltkoordinaten: aus den sichtbaren Stangen (verbundene zu je einem Linienzug), sonst aus points.</summary>
        List<Vector3[]> WorldChains()
        {
            var result = new List<Vector3[]>();
            var bars = BarSegments();
            if (bars.Count > 0)
            {
                var used = new bool[bars.Count];
                while (System.Array.IndexOf(used, false) >= 0) result.Add(Chain(bars, used));
                return result;
            }
            if (points == null || points.Length < 2) return result;
            var w = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++) w[i] = transform.TransformPoint(points[i]);
            result.Add(w);
            return result;
        }

        /// <summary>Alle sichtbaren Stangen (Zylinder "Bar ...") als Strecken; doppelte (uebereinander kopierte) nur einmal.</summary>
        List<(Vector3 a, Vector3 b)> BarSegments()
        {
            var list = new List<(Vector3 a, Vector3 b)>();
            foreach (Transform c in transform)
            {
                if (!c.name.StartsWith("Bar") || !c.gameObject.activeSelf || c.GetComponent<MeshFilter>() == null) continue;
                // Unitys Zylinder: Achse = lokales up, Enden bei y = -1 und +1
                Vector3 a = c.TransformPoint(Vector3.down) + Vector3.up * BarTop, b = c.TransformPoint(Vector3.up) + Vector3.up * BarTop;
                bool dup = list.Exists(s => (Near(s.a, a) && Near(s.b, b)) || (Near(s.a, b) && Near(s.b, a)));
                if (!dup) list.Add((a, b));
            }
            return list;
        }

        static bool Near(Vector3 p, Vector3 q) => (p - q).sqrMagnitude < 0.05f * 0.05f;

        /// <summary>
        /// Einen Linienzug aus noch freien Strecken bauen: an einem freien Ende anfangen und immer die Strecke anhaengen,
        /// deren Ende am naechsten liegt (bis JoinDistance). Benutzte Strecken werden in used markiert.
        /// </summary>
        static Vector3[] Chain(List<(Vector3 a, Vector3 b)> segs, bool[] used)
        {
            // Start: ein Ende, an dem keine andere freie Stange haengt (sonst irgendeine freie)
            int start = -1;
            bool startAtA = true;
            for (int i = 0; i < segs.Count && start < 0; i++)
            {
                if (used[i]) continue;
                if (!Joined(segs, used, i, segs[i].a)) { start = i; startAtA = true; }
                else if (!Joined(segs, used, i, segs[i].b)) { start = i; startAtA = false; }
            }
            if (start < 0) start = System.Array.IndexOf(used, false);
            used[start] = true;
            var line = new List<Vector3> { startAtA ? segs[start].a : segs[start].b, startAtA ? segs[start].b : segs[start].a };
            while (true)
            {
                Vector3 end = line[line.Count - 1];
                int best = -1;
                bool fromA = true;
                float bestD = JoinDistance * JoinDistance;
                for (int i = 0; i < segs.Count; i++)
                {
                    if (used[i]) continue;
                    float da = (segs[i].a - end).sqrMagnitude, db = (segs[i].b - end).sqrMagnitude;
                    if (da < bestD) { bestD = da; best = i; fromA = true; }
                    if (db < bestD) { bestD = db; best = i; fromA = false; }
                }
                if (best < 0) break;
                used[best] = true;
                // Gelenk in die Mitte der beiden Enden legen, dann zum anderen Ende der neuen Stange
                Vector3 near = fromA ? segs[best].a : segs[best].b, far = fromA ? segs[best].b : segs[best].a;
                line[line.Count - 1] = (end + near) * 0.5f;
                line.Add(far);
            }
            return line.ToArray();
        }

        static bool Joined(List<(Vector3 a, Vector3 b)> segs, bool[] used, int self, Vector3 end)
        {
            for (int i = 0; i < segs.Count; i++)
                if (i != self && !used[i] && ((segs[i].a - end).sqrMagnitude < JoinDistance * JoinDistance || (segs[i].b - end).sqrMagnitude < JoinDistance * JoinDistance)) return true;
            return false;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            foreach (var w in WorldChains())
                for (int i = 0; i < w.Length - 1; i++) Gizmos.DrawLine(w[i], w[i + 1]);
        }
    }
}
