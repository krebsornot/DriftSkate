using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// NPC-Verkehr: Autos fahren feste Runden ueber das Strassenraster, immer auf der rechten Spur, bremsen vor
    /// Kurven und vor Hindernissen. Wo ein Auto gerade ist, folgt aus der Uhrzeit (online: Server-Zeit), so sehen
    /// alle Spieler den Verkehr gleich, ohne dass etwas uebertragen wird. Lokales Bremsen holt das Auto danach auf.
    /// Prefabs kommen aus dem Editor: DriftSkate → Verkehr.
    /// </summary>
    public class Traffic : MonoBehaviour
    {
        public static Traffic Instance { get; private set; }

        public GameObject[] carPrefabs;
        public float laneOffset = 4.2f;     // Spurmitte rechts neben der Mittellinie (m)
        public float cruise = 11f;          // Reisetempo (m/s, ~40 km/h)
        public float cornerCut = 11f;       // so weit vor der Kreuzung beginnt die Kurve (m)
        public float lateralAccel = 2.8f;   // Querbeschleunigung in Kurven (m/s^2)
        public float accel = 2.2f, decel = 3.5f;

        /// <summary>Runden als Kreuzungen (Spalte, Zeile) im 6x6-Strassenraster, Fahrtrichtung in Listenreihenfolge.</summary>
        static readonly Vector2Int[][] RouteDefs =
        {
            new[] { new Vector2Int(1, 1), new Vector2Int(4, 1), new Vector2Int(4, 4), new Vector2Int(1, 4) },
            new[] { new Vector2Int(1, 4), new Vector2Int(4, 4), new Vector2Int(4, 1), new Vector2Int(1, 1) },
            new[] { new Vector2Int(0, 0), new Vector2Int(0, 5), new Vector2Int(5, 5), new Vector2Int(5, 0) },
            new[] { new Vector2Int(2, 0), new Vector2Int(2, 5), new Vector2Int(3, 5), new Vector2Int(3, 0) },
            new[] { new Vector2Int(0, 3), new Vector2Int(5, 3), new Vector2Int(5, 2), new Vector2Int(0, 2) },
        };
        /// <summary>Autos pro Runde (in derselben Reihenfolge).</summary>
        static readonly int[] CarsPerRoute = { 3, 3, 2, 2, 1 };

        public readonly List<Route> routes = new List<Route>();
        public readonly List<TrafficCar> cars = new List<TrafficCar>();

        void Awake()
        {
            Instance = this;
            BuildRoutes();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (carPrefabs == null || carPrefabs.Length == 0) return;
            int id = 0;
            for (int r = 0; r < routes.Count; r++)
            {
                int n = CarsPerRoute[r];
                for (int k = 0; k < n; k++)
                {
                    var prefab = carPrefabs[(id * 7) % carPrefabs.Length]; // jedes Modell einmal, gut gemischt
                    var go = Instantiate(prefab, transform);
                    go.name = "TrafficCar_" + id;
                    var car = go.GetComponent<TrafficCar>();
                    // gleichmaessig verteilt, je Runde etwas versetzt
                    float phase = routes[r].duration * ((k + 0.37f * r) / n);
                    car.Init(this, routes[r], phase, id);
                    cars.Add(car);
                    id++;
                }
            }
        }

        /// <summary>Gemeinsame Uhr: online die Server-Zeit, solo die Spielzeit.</summary>
        public static double Clock => SharedClock.Time;

        public void BuildRoutes()
        {
            routes.Clear();
            foreach (var def in RouteDefs) routes.Add(new Route(this, def));
        }

        // ------------------------------------------------------------------ Route

        public class Route
        {
            public Vector3[] pos;
            public Vector3[] dir;
            public float[] s, speed, time;
            public float length, duration;

            public Route(Traffic t, Vector2Int[] corners)
            {
                var pts = new List<Vector3>();
                int n = corners.Length;
                Vector3 C(int i)
                {
                    var c = corners[(i % n + n) % n];
                    return new Vector3(CityBuilder.RoadCenter(c.x), 0f, CityBuilder.RoadCenter(c.y));
                }
                Vector3 Right(Vector3 d) => new Vector3(d.z, 0f, -d.x);

                for (int i = 0; i < n; i++)
                {
                    Vector3 dirIn = (C(i) - C(i - 1)).normalized, dirOut = (C(i + 1) - C(i)).normalized;
                    Vector3 corner = C(i) + Right(dirIn) * t.laneOffset + Right(dirOut) * t.laneOffset;
                    // Kurve (quadratische Bezier) um die Kreuzung
                    Vector3 a = corner - dirIn * t.cornerCut, b = corner + dirOut * t.cornerCut;
                    for (int k = 0; k < 16; k++)
                    {
                        float u = k / 16f;
                        pts.Add((1 - u) * (1 - u) * a + 2 * (1 - u) * u * corner + u * u * b);
                    }
                    // Gerade bis zur naechsten Kurve
                    Vector3 dirNext = (C(i + 2) - C(i + 1)).normalized;
                    Vector3 nextCorner = C(i + 1) + Right(dirOut) * t.laneOffset + Right(dirNext) * t.laneOffset;
                    Vector3 end = nextCorner - dirOut * t.cornerCut;
                    float len = Vector3.Distance(b, end);
                    int steps = Mathf.Max(1, Mathf.CeilToInt(len / 2f));
                    for (int k = 0; k < steps; k++) pts.Add(Vector3.Lerp(b, end, k / (float)steps));
                }

                // Bodenhoehe (Strasse) unter jedem Punkt
                int mask = ~LayerMask.GetMask("Car", "Skater", "Ignore Raycast", "Rail");
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector3 p = pts[i];
                    if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3.5f, mask, QueryTriggerInteraction.Ignore))
                        p.y = Mathf.Clamp(hit.point.y, -0.5f, 1f);
                    pts[i] = p;
                }

                int m = pts.Count;
                pos = pts.ToArray();
                dir = new Vector3[m];
                s = new float[m + 1];
                speed = new float[m];
                time = new float[m + 1];
                for (int i = 0; i < m; i++)
                {
                    dir[i] = (pos[(i + 1) % m] - pos[(i - 1 + m) % m]).normalized;
                    s[i + 1] = s[i] + Vector3.Distance(pos[i], pos[(i + 1) % m]);
                }
                length = s[m];

                // Tempo: in Kurven nach Kruemmung, davor bremsen, danach beschleunigen (zweimal rundherum)
                for (int i = 0; i < m; i++)
                {
                    Vector3 a = Flat(dir[(i - 1 + m) % m]), b = Flat(dir[(i + 1) % m]);
                    float ds = Seg(i - 1) + Seg(i);
                    float k = Vector3.Angle(a, b) * Mathf.Deg2Rad / Mathf.Max(0.01f, ds);
                    speed[i] = Mathf.Min(t.cruise, k > 1e-4f ? Mathf.Sqrt(t.lateralAccel / k) : t.cruise);
                }
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = m - 1; i >= 0; i--)
                    {
                        int j = (i + 1) % m;
                        speed[i] = Mathf.Min(speed[i], Mathf.Sqrt(speed[j] * speed[j] + 2f * t.decel * Seg(i)));
                    }
                    for (int i = 0; i < m; i++)
                    {
                        int j = (i + 1) % m;
                        speed[j] = Mathf.Min(speed[j], Mathf.Sqrt(speed[i] * speed[i] + 2f * t.accel * Seg(i)));
                    }
                }
                for (int i = 0; i < m; i++)
                    time[i + 1] = time[i] + Seg(i) / Mathf.Max(0.5f, (speed[i] + speed[(i + 1) % m]) * 0.5f);
                duration = time[m];
            }

            static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z).normalized;
            float Seg(int i)
            {
                int m = pos.Length;
                i = (i % m + m) % m;
                return s[i + 1] - s[i];
            }

            int Index(float[] table, float v)
            {
                int lo = 0, hi = table.Length - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (table[mid] <= v) lo = mid; else hi = mid;
                }
                return lo;
            }

            /// <summary>Strecke (m), an der ein Auto nach t Sekunden ist.</summary>
            public float DistanceAtTime(double t)
            {
                float tt = (float)(t % duration);
                if (tt < 0f) tt += duration;
                int i = Index(time, tt);
                float f = Mathf.InverseLerp(time[i], time[i + 1], tt);
                return Mathf.Lerp(s[i], s[i + 1], f);
            }

            public void Sample(float dist, out Vector3 p, out Vector3 d, out float v)
            {
                dist = Mathf.Repeat(dist, length);
                int i = Index(s, dist);
                int m = pos.Length;
                float f = Mathf.InverseLerp(s[i], s[i + 1], dist);
                int j = (i + 1) % m;
                p = Vector3.Lerp(pos[i], pos[j], f);
                d = Vector3.Slerp(dir[i], dir[j], f).normalized;
                v = Mathf.Lerp(speed[i], speed[j], f);
            }

            /// <summary>Kleinster vorzeichenbehafteter Abstand a -> b auf der Runde.</summary>
            public float Delta(float a, float b)
            {
                float d = Mathf.Repeat(b - a, length);
                return d > length * 0.5f ? d - length : d;
            }
        }

        void OnDrawGizmosSelected()
        {
            if (routes.Count == 0) BuildRoutes();
            Color[] cols = { Color.cyan, Color.magenta, Color.yellow, Color.green, Color.red };
            for (int r = 0; r < routes.Count; r++)
            {
                Gizmos.color = cols[r % cols.Length];
                var p = routes[r].pos;
                for (int i = 0; i < p.Length; i++) Gizmos.DrawLine(p[i] + Vector3.up * 0.3f, p[(i + 1) % p.Length] + Vector3.up * 0.3f);
            }
        }
    }
}
