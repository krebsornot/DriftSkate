using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// NPC-Verkehr: Autos fahren feste Runden ueber das Strassenraster, immer auf der rechten Spur, bremsen vor
    /// Kurven und vor Hindernissen. Wo ein Auto gerade ist, folgt aus der Uhrzeit (online: Server-Zeit), so sehen
    /// alle Spieler den Verkehr gleich, ohne dass etwas uebertragen wird. Lokales Bremsen holt das Auto danach auf.
    /// Der Fahrplan jedes Autos (Schedule) ist eine deterministische Simulation mit festem Schritt ab Uhrzeit 0: er haelt
    /// an roten Ampeln (TrafficSignals, Haltelinie 18 m vor der Kreuzungsmitte), an Bushaltestellen und hinter dem
    /// Vordermann derselben Runde. Jeder Rechner rechnet denselben Fahrplan nach, nichts wird uebertragen.
    /// Prefabs kommen aus dem Editor: DriftSkate → Verkehr. Dazu faehrt ein Schulbus (Resources/Vehicles/SchoolBus,
    /// DriftSkate → Verkehr → Schulbus bauen) eine eigene Runde ueber alle sechs Bushaltestellen und haelt dort kurz.
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
            // mehr Verkehr: Aussenring andersherum, dazu eine Schleife durch den Westen und eine durch den Sueden
            new[] { new Vector2Int(0, 0), new Vector2Int(5, 0), new Vector2Int(5, 5), new Vector2Int(0, 5) },
            new[] { new Vector2Int(1, 0), new Vector2Int(1, 3), new Vector2Int(4, 3), new Vector2Int(4, 0) },
            new[] { new Vector2Int(0, 1), new Vector2Int(3, 1), new Vector2Int(3, 4), new Vector2Int(0, 4) },
        };
        /// <summary>Autos pro Runde (in derselben Reihenfolge). Frueher 11 insgesamt, jetzt 25.</summary>
        static readonly int[] CarsPerRoute = { 4, 4, 4, 3, 2, 3, 3, 2 };

        /// <summary>
        /// Schulbus: Runde ueber alle sechs Bushaltestellen (jede liegt rechts der Fahrspur in Fahrtrichtung), ca. 2,5 km.
        /// Nur echte Abbiegungen als Ecken (geradeaus ueber eine Kreuzung braucht keine Ecke).
        /// </summary>
        static readonly Vector2Int[] BusRouteDef =
        {
            new Vector2Int(0, 5), new Vector2Int(5, 5), new Vector2Int(5, 3), new Vector2Int(4, 3), new Vector2Int(4, 0),
            new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(2, 1), new Vector2Int(2, 3), new Vector2Int(3, 3),
            new Vector2Int(3, 4), new Vector2Int(0, 4),
        };
        public const string BusPrefabPath = "Vehicles/SchoolBus";
        public float busCruise = 9f;        // Bus faehrt gemuetlicher (m/s, ~32 km/h)
        public float busStopTime = 7f;      // so lange haelt er an jeder Haltestelle (s)
        /// <summary>Wo die Tuer beim Halten steht: so weit vor der Busmitte (m).</summary>
        const float BusDoorAhead = 2.7f;

        /// <summary>Ranfahren an die Haltestelle (Punkte sind ca. 2 m auseinander): Rampe rein/raus, gehalten davor/danach.</summary>
        const int PullRampIn = 9, PullHoldBefore = 4, PullHoldAfter = 3, PullRampOut = 7;
        /// <summary>Abstand Busmitte zum Bordstein beim Halten (halbe Busbreite plus Luft; naeher bleibt die Busecke am Haltestellenhaeuschen haengen).</summary>
        const float CurbGap = 2.6f;

        public Route busRoute;
        public TrafficCar bus;

        public readonly List<Route> routes = new List<Route>();
        public readonly List<TrafficCar> cars = new List<TrafficCar>();
        readonly List<Schedule> _schedules = new List<Schedule>();

        /// <summary>Gibt es Ampeln in der Stadt? Dann halten die Fahrplaene an Rot.</summary>
        public static bool LightsActive { get; private set; }

        void Awake()
        {
            Instance = this;
            LightsActive = FindAnyObjectByType<TrafficSignals>() != null;
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
                var ring = new List<Schedule>();
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
                    ring.Add(car.Sched);
                    id++;
                }
                // Autos derselben Runde fahren hintereinander: jeder haelt Abstand zum naechsten vor ihm (Ring)
                for (int k = 0; k < ring.Count; k++) ring[k].leader = ring[(k + 1) % ring.Count];
            }
            SpawnBus(id);
        }

        // ------------------------------------------------------------------ Fahrplan

        /// <summary>Fahrplan fuer ein Fahrzeug anlegen (Start bei Strecke startS zur Uhrzeit 0).</summary>
        public Schedule AddSchedule(Route route, float startS)
        {
            var s = new Schedule(route, startS, accel, decel);
            _schedules.Add(s);
            return s;
        }

        /// <summary>
        /// Alle Fahrplaene bis zur Uhrzeit nachrechnen (feste Schritte, in Reihenfolge der Autos: deterministisch). Ein neu
        /// angelegter Fahrplan holt beim ersten Mal von Uhrzeit 0 auf (bei spaetem Beitritt ein paar hunderttausend Schritte).
        /// </summary>
        public void AdvanceSchedules(double clock)
        {
            long k = (long)System.Math.Floor(clock / Schedule.Step);
            foreach (var s in _schedules)
            {
                int guard = 0;
                while (s.tick < k && guard++ < 400000)
                {
                    s.Simulate(s.tick * Schedule.Step);
                    s.tick++;
                }
                if (s.tick < k) s.tick = k; // Uhr unglaubwuerdig weit vorn: nicht ewig rechnen
            }
        }

        /// <summary>Soll-Position eines Fahrplans zur Uhrzeit (zwischen zwei Schritten linear).</summary>
        public static float ScheduledS(Schedule s, double clock)
        {
            float rest = Mathf.Clamp((float)(clock - s.tick * Schedule.Step), 0f, Schedule.Step);
            return Mathf.Repeat(s.S + s.V * rest, s.route.length);
        }

        /// <summary>
        /// Fahrplan eines Fahrzeugs: Strecke S und Tempo V auf seiner Runde, in Schritten von 0,1 s ab Uhrzeit 0 nachgerechnet.
        /// Haelt an roten Ampeln (bei Gelb nur, wenn es bequem reicht), wartet an Haltestellen (Bus) und faehrt nicht in den
        /// Vordermann derselben Runde. Dieselbe Rechnung auf jedem Rechner ergibt denselben Fahrplan.
        /// </summary>
        public class Schedule
        {
            public const float Step = 0.1f;
            /// <summary>So weit vor der Haltelinie bleibt das Auto stehen (m).</summary>
            const float LineGap = 0.7f;

            public readonly Route route;
            public Schedule leader;
            public float S, V;
            /// <summary>Bis zu diesem Schritt (0,1 s ab Uhrzeit 0) ist der Fahrplan gerechnet.</summary>
            public long tick;
            /// <summary>Wartet gerade an einer roten Ampel bzw. haelt an einer Haltestelle.</summary>
            public bool AtRedLight { get; private set; }
            public bool Dwelling => _dwell > 0f;

            readonly float _accel, _decel;
            float _dwell, _leaveS = -1f;
            int _line;

            public Schedule(Route r, float startS, float accel, float decel)
            {
                route = r;
                S = Mathf.Repeat(startS, r.length);
                V = r.SpeedAt(S);
                _accel = accel;
                _decel = decel;
                _line = 0;
                while (_line < r.lineS.Length && r.lineS[_line] < S) _line++;
                if (_line >= r.lineS.Length) _line = 0;
            }

            public void Simulate(double time)
            {
                float desired = route.SpeedAt(S);
                AtRedLight = false;

                // Haltestelle: ankommen, Tuer offen (dwell), dann anfahren, auch wenn das Tempoprofil dort noch 0 sagt
                if (_dwell > 0f)
                {
                    _dwell -= Step;
                    desired = 0f;
                    if (_dwell <= 0f) _leaveS = S;
                }
                else if (_leaveS >= 0f)
                {
                    if (Mathf.Repeat(S - _leaveS, route.length) < 3f) desired = Mathf.Max(desired, 2.5f);
                    else _leaveS = -1f;
                }
                else if (route.stopS.Length > 0)
                {
                    // Naechster Haltepunkt voraus: genau dort zum Stehen kommen, dann warten
                    float dist = route.length;
                    foreach (float st in route.stopS) dist = Mathf.Min(dist, Mathf.Repeat(st - S, route.length));
                    desired = Mathf.Min(desired, Mathf.Sqrt(2f * _decel * Mathf.Max(0f, dist - 0.05f)));
                    if (dist < 0.35f && V < 0.25f) { _dwell = route.dwell; desired = 0f; }
                }

                // Ampel: naechste Haltelinie voraus (Linien sind entlang der Runde sortiert)
                if (LightsActive && route.lineS.Length > 0)
                {
                    float dist = Mathf.Repeat(route.lineS[_line] - S, route.length);
                    int guard = 0;
                    while (dist > route.length - 3f && guard++ < route.lineS.Length)
                    {
                        _line = (_line + 1) % route.lineS.Length;
                        dist = Mathf.Repeat(route.lineS[_line] - S, route.length);
                    }
                    int state = TrafficSignals.State(time, route.lineZ[_line], out _);
                    if (state != 0)
                    {
                        float brake = V * V / (2f * _decel);
                        // Rot: halten, ausser es geht gar nicht mehr. Gelb: durchfahren nur, wenn man die Linie noch vor
                        // Rot erreicht, sonst halten (wenn es noch geht)
                        bool canStop = dist > brake * 0.6f - 0.5f;
                        bool stop = state == 2 ? canStop : canStop && dist > Mathf.Max(0.5f, V) * TrafficSignals.UntilRed(time, route.lineZ[_line]);
                        if (stop)
                        {
                            desired = Mathf.Min(desired, Mathf.Sqrt(2f * _decel * Mathf.Max(0f, dist - LineGap)));
                            AtRedLight = dist < LineGap + 1.5f;
                        }
                    }
                }

                // Vordermann derselben Runde: nicht auffahren, hinter ihm anhalten
                if (leader != null && leader != this)
                {
                    float gap = Mathf.Repeat(leader.S - S, route.length);
                    if (gap < 14f) desired = Mathf.Min(desired, leader.V + Mathf.Max(0f, gap - 6.5f) * 0.7f);
                }

                V = Mathf.MoveTowards(V, desired, (desired < V ? _decel * 1.3f : _accel) * Step);
                S = Mathf.Repeat(S + V * Step, route.length);
            }
        }

        void SpawnBus(int id)
        {
            var prefab = Resources.Load<GameObject>(BusPrefabPath);
            if (prefab == null || busRoute == null || busRoute.stopS.Length == 0) return;
            var go = Instantiate(prefab, transform);
            go.name = "SchoolBus";
            bus = go.GetComponent<TrafficCar>();
            bus.Init(this, busRoute, 0f, id);
        }

        /// <summary>Bushaltestellen der Stadt (Gruppe "BusStops" in City.unity).</summary>
        static List<Vector3> BusStops()
        {
            var list = new List<Vector3>();
            var g = GameObject.Find("BusStops");
            if (g != null) foreach (Transform t in g.transform) list.Add(t.position);
            return list;
        }

        /// <summary>Gemeinsame Uhr: online die Server-Zeit, solo die Spielzeit.</summary>
        public static double Clock => SharedClock.Time;

        public void BuildRoutes()
        {
            routes.Clear();
            foreach (var def in RouteDefs) routes.Add(new Route(this, def));
            var stops = BusStops();
            busRoute = stops.Count > 0 ? new Route(this, BusRouteDef, busCruise, stops, busStopTime, BusDoorAhead) : null;
        }

        // ------------------------------------------------------------------ Route

        public class Route
        {
            public Vector3[] pos;
            public Vector3[] dir;
            public float[] s, speed, time;
            public float length, duration;
            /// <summary>Haltepunkte (Strecke in m), leer bei normalen Runden, und wie lange dort gewartet wird (s).</summary>
            public float[] stopS = new float[0];
            public float dwell;
            /// <summary>Haltelinien vor Ampel-Kreuzungen: Strecke (m) aufsteigend, und ob die Zufahrt entlang Z laeuft (Ampelgruppe Z).</summary>
            public float[] lineS = new float[0];
            public bool[] lineZ = new bool[0];
            /// <summary>Haltelinie: so weit vor der Kreuzungsmitte (Bordstein bei 12 m, davor der Zebrastreifen).</summary>
            public const float StopLine = 18f;

            public Route(Traffic t, Vector2Int[] corners) : this(t, corners, t.cruise, null, 0f, 0f) { }

            /// <summary>
            /// Runde ueber die Kreuzungen; mit stops haelt das Fahrzeug an jedem Punkt dwell Sekunden, und zwar so, dass
            /// die Stelle doorAhead Meter vor seiner Mitte liegt (Bustuer an der Haltestelle). Stops neben anderen Strassen
            /// (weiter als 16 m von der Runde) werden ignoriert.
            /// </summary>
            public Route(Traffic t, Vector2Int[] corners, float cruise, List<Vector3> stops, float dwell, float doorAhead)
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

                // Haltestellen: naechster Spurpunkt, dahinter ein Punkt 5 cm weiter (auf diesem Mini-Stueck wird gewartet)
                var stopIdx = new List<int>();
                if (stops != null)
                {
                    foreach (var stop in stops)
                    {
                        int best = -1;
                        float bd = 16f * 16f;
                        for (int i = 0; i < pts.Count; i++)
                        {
                            float d = new Vector2(pts[i].x - stop.x, pts[i].z - stop.z).sqrMagnitude;
                            if (d < bd) { bd = d; best = i; }
                        }
                        if (best < 0) continue;
                        // Busmitte doorAhead hinter der Haltestelle: um so viele Punkte zurueck (Punkte ca. 2 m auseinander)
                        int back = Mathf.RoundToInt(doorAhead / 2f);
                        int i0 = (best - back + pts.Count) % pts.Count;
                        Vector3 dirAt = (pts[(i0 + 1) % pts.Count] - pts[i0]).normalized;
                        pts.Insert(i0 + 1, pts[i0] + dirAt * 0.05f);
                        for (int k = 0; k < stopIdx.Count; k++) if (stopIdx[k] > i0) stopIdx[k]++;
                        stopIdx.Add(i0);
                    }
                    // An den Bordstein ranfahren: vor der Haltestelle sanft nach rechts, danach wieder einscheren
                    var shift = new float[pts.Count];
                    foreach (int i0 in stopIdx)
                        for (int k = -PullRampIn - PullHoldBefore; k <= PullHoldAfter + PullRampOut; k++)
                        {
                            float w = k < -PullHoldBefore ? Mathf.SmoothStep(0f, 1f, (k + PullHoldBefore + PullRampIn) / (float)PullRampIn)
                                    : k > PullHoldAfter ? Mathf.SmoothStep(0f, 1f, (PullHoldAfter + PullRampOut - k) / (float)PullRampOut) : 1f;
                            int j = ((i0 + k) % pts.Count + pts.Count) % pts.Count;
                            shift[j] = Mathf.Max(shift[j], w);
                        }
                    float pull = CityBuilder.Road * 0.5f - CurbGap - t.laneOffset;
                    var moved = new List<Vector3>(pts);
                    for (int i = 0; i < pts.Count; i++)
                    {
                        if (shift[i] <= 0f) continue;
                        Vector3 d = (pts[(i + 1) % pts.Count] - pts[(i - 1 + pts.Count) % pts.Count]).normalized;
                        moved[i] = pts[i] + Right(d) * pull * shift[i];
                    }
                    pts = moved;
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
                    speed[i] = Mathf.Min(cruise, k > 1e-4f ? Mathf.Sqrt(t.lateralAccel / k) : cruise);
                }
                foreach (int i in stopIdx) { speed[i] = 0f; speed[(i + 1) % m] = 0f; }
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
                    time[i + 1] = time[i] + Seg(i) / Mathf.Max(0.5f, (speed[i] + speed[(i + 1) % m]) * 0.5f)
                                  + (stopIdx.Contains(i) ? dwell : 0f);
                duration = time[m];
                stopS = stopIdx.ConvertAll(i => s[i]).ToArray();
                this.dwell = dwell;
                FindStopLines();
            }

            /// <summary>
            /// Haltelinien: wo die Spur die Linie 18 m vor der Mitte einer Ampel-Kreuzung (innere 4x4) in Fahrtrichtung
            /// ueberquert. Nur Zufahrten auf derselben Strasse (Querabstand unter 11 m), damit Nachbarstrassen nicht zaehlen.
            /// </summary>
            void FindStopLines()
            {
                var lines = new List<(float s, bool z)>();
                int m = pos.Length;
                for (int i = 1; i < CityBuilder.Grid; i++)
                    for (int j = 1; j < CityBuilder.Grid; j++)
                    {
                        Vector3 c = new Vector3(CityBuilder.RoadCenter(j), 0f, CityBuilder.RoadCenter(i));
                        for (int k = 0; k < m; k++)
                        {
                            Vector3 p0 = pos[k], p1 = pos[(k + 1) % m];
                            Vector3 d = p1 - p0;
                            d.y = 0f;
                            if (d.sqrMagnitude < 1e-6f) continue;
                            bool z = Mathf.Abs(d.z) > Mathf.Abs(d.x);
                            Vector3 axis = z ? new Vector3(0f, 0f, Mathf.Sign(d.z)) : new Vector3(Mathf.Sign(d.x), 0f, 0f);
                            float u0 = Vector3.Dot(p0 - c, axis), u1 = Vector3.Dot(p1 - c, axis);
                            if (!(u0 < -StopLine && u1 >= -StopLine)) continue;
                            Vector3 lat = (p0 - c) - axis * u0;
                            if (Mathf.Abs(z ? lat.x : lat.z) > 11f) continue;
                            float f = (-StopLine - u0) / (u1 - u0);
                            lines.Add((s[k] + f * Seg(k), z));
                        }
                    }
                lines.Sort((a, b) => a.s.CompareTo(b.s));
                lineS = lines.ConvertAll(l => l.s).ToArray();
                lineZ = lines.ConvertAll(l => l.z).ToArray();
            }

            /// <summary>Tempo laut Profil (Kurven, Haltestellen) an der Strecke dist.</summary>
            public float SpeedAt(float dist)
            {
                dist = Mathf.Repeat(dist, length);
                int i = Index(s, dist);
                float f = Mathf.InverseLerp(s[i], s[i + 1], dist);
                return Mathf.Lerp(speed[i], speed[(i + 1) % pos.Length], f);
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

            /// <summary>Steht dist (m) an einem Haltepunkt (bis 'range' davor oder dahinter)?</summary>
            public bool NearStop(float dist, float range)
            {
                foreach (float st in stopS) if (Mathf.Abs(Delta(dist, st)) < range) return true;
                return false;
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
            Color[] cols = { Color.cyan, Color.magenta, Color.yellow, Color.green, Color.red, Color.blue, Color.white, Color.gray };
            for (int r = 0; r < routes.Count; r++)
            {
                Gizmos.color = cols[r % cols.Length];
                var p = routes[r].pos;
                for (int i = 0; i < p.Length; i++) Gizmos.DrawLine(p[i] + Vector3.up * 0.3f, p[(i + 1) % p.Length] + Vector3.up * 0.3f);
            }
        }
    }
}
