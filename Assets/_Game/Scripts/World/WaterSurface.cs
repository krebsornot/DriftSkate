using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DriftSkate
{
    /// <summary>
    /// Das Wasser rund um die Stadt, beim Laden der Stadt anstelle des flachen Kastens aus der Szene:
    /// - Ein Gitter (400 x 400 m, Zelle 2,5 m), das der Kamera folgt, mit dem Wasser-Shader (Wellen im Vertex-Shader,
    ///   Toon-Stufen, Schaum, Sonnenglitzer); dahinter bis zum Horizont eine flache Platte ohne Wellen.
    /// - Drei langsame Sinuswellen, dieselbe Formel hier (HeightAt) und im Shader, damit Auftrieb und Bild zusammenpassen.
    /// - Eine kleine Wellen-Simulation auf der CPU (WaterRipples, 96 x 96 m um den Spieler): Platscher, Kielwasser und
    ///   Regentropfen breiten sich als Ringe aus. Die Hoehen gehen als Textur in den Shader.
    /// - Auftrieb (Floater) fuer das eigene Auto, Platscher mit Tropfen, Ring und Geraeusch, auch fuer andere Spieler.
    /// - Eine Bootsrampe am West-Kai (Luecke im Gelaender und in der unsichtbaren Kai-Wand), damit man ueberhaupt ins
    ///   Wasser kommt.
    /// </summary>
    public class WaterSurface : MonoBehaviour
    {
        public static WaterSurface Instance { get; private set; }

        /// <summary>Ruhiger Wasserspiegel (Oberkante des alten Wasser-Kastens).</summary>
        public const float Level = -1.7f;
        const float GridSize = 400f, Cell = 2.5f;

        // Wellen: Richtung x, z, Amplitude (m), Wellenlaenge (m). Tempo wie auf tiefem Wasser (siehe Wave im Shader).
        static readonly Vector4[] Waves =
        {
            new Vector4(1f, 0.35f, 0.22f, 26f),
            new Vector4(-0.4f, 1f, 0.12f, 15f),
            new Vector4(0.7f, -0.7f, 0.06f, 9f),
        };

        Material _mat, _farMat;
        Transform _grid, _far;
        WaterRipples _ripples;
        float _rainDrops;
        readonly List<PlayerAvatar> _avatars = new List<PlayerAvatar>();
        readonly Dictionary<PlayerAvatar, Vector2> _prevY = new Dictionary<PlayerAvatar, Vector2>(); // x = Auto, y = Skater
        float _scan;

        public WaterRipples Ripples => _ripples;

        public static void Spawn()
        {
            if (Instance != null) return;
            new GameObject("WaterSurface").AddComponent<WaterSurface>();
        }

        void Awake()
        {
            Instance = this;
            _mat = LoadMaterial();
            if (_mat == null) { Debug.LogWarning("WaterSurface: Material WaterMat fehlt (DriftSkate -> Wasser -> Materialien anlegen)"); enabled = false; return; }
            HideSceneWater();
            BuildMeshes();
            _ripples = new WaterRipples();
            PushGlobals();
            HarborSlipway.Build();
        }

        void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;

        void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

        void OnDestroy()
        {
            _ripples?.Dispose();
            if (Instance == this) Instance = null;
        }

        /// <summary>Gitter unter die Kamera legen, die gerade rendert (auch Screenshot- und Spiegel-Kameras), auf Zellen gerastert.</summary>
        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (_grid == null || cam.cameraType == CameraType.Preview) return;
            Vector3 focus = cam.transform.position;
            float gx = Mathf.Floor(focus.x / Cell) * Cell - GridSize * 0.5f;
            float gz = Mathf.Floor(focus.z / Cell) * Cell - GridSize * 0.5f;
            _grid.position = new Vector3(gx, Level, gz);
        }

        static Material LoadMaterial()
        {
            var mat = Resources.Load<Material>("WaterMat");
            if (mat != null) return new Material(mat);
            var shader = Shader.Find("DriftSkate/Water");
            return shader != null ? new Material(shader) : null;
        }

        /// <summary>Alter Wasser-Kasten und die festen Schaumstreifen am Kai aus der Szene ausblenden.</summary>
        static void HideSceneWater()
        {
            var perimeter = GameObject.Find("Perimeter");
            if (perimeter != null)
                foreach (Transform t in perimeter.transform)
                    if (t.name == "Water") t.gameObject.SetActive(false);
            var foam = GameObject.Find("Foam");
            if (foam != null && foam.GetComponent<MeshFilter>() != null && foam.transform.position.sqrMagnitude < 1f) foam.SetActive(false);
        }

        void BuildMeshes()
        {
            _grid = MakeRenderer("WaterGrid", GridMesh("WaterGrid", GridSize, Cell), _mat);

            // Weit draussen (bis 1,6 km): Platte ohne Vertex-Wellen (die Wellenform kommt im Shader pro Pixel), ein Stueck
            // tiefer, damit sie nicht mit dem Gitter flimmert. Grob unterteilt reicht.
            _farMat = new Material(_mat);
            _farMat.SetFloat("_WaveAmp", 0f);
            _farMat.SetFloat("_RippleOn", 0f);
            _far = MakeRenderer("WaterFar", GridMesh("WaterFar", 3200f, 80f), _farMat);
            _far.position = new Vector3(-1600f, Level - 0.06f, -1600f);
        }

        /// <summary>Ebenes Gitter von (0,0) bis (size,size), Normalen nach oben, Bounds mit Platz fuer die Wellen.</summary>
        static Mesh GridMesh(string name, float size, float cell)
        {
            int n = Mathf.RoundToInt(size / cell);
            var verts = new Vector3[(n + 1) * (n + 1)];
            var tris = new int[n * n * 6];
            for (int z = 0; z <= n; z++)
                for (int x = 0; x <= n; x++)
                    verts[z * (n + 1) + x] = new Vector3(x * cell, 0f, z * cell);
            int k = 0;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int a = z * (n + 1) + x, b = a + 1, c = a + n + 1, d = c + 1;
                    tris[k++] = a; tris[k++] = c; tris[k++] = b;
                    tris[k++] = b; tris[k++] = c; tris[k++] = d;
                }
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            var normals = new Vector3[verts.Length];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.SetNormals(normals);
            mesh.bounds = new Bounds(new Vector3(size * 0.5f, 0f, size * 0.5f), new Vector3(size, 4f, size));
            return mesh;
        }

        Transform MakeRenderer(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
            return go.transform;
        }

        void PushGlobals()
        {
            Shader.SetGlobalVector("_DS_Wave0", Waves[0]);
            Shader.SetGlobalVector("_DS_Wave1", Waves[1]);
            Shader.SetGlobalVector("_DS_Wave2", Waves[2]);
            Shader.SetGlobalFloat("_DS_WaterTime", WaterTime);
            Shader.SetGlobalFloat("_DS_RippleScale", 1f);
            _ripples?.PushGlobals();
        }

        /// <summary>Zeitbasis der Wellen (Shader und C# nehmen dieselbe).</summary>
        public static float WaterTime => Time.timeSinceLevelLoad;

        void Update()
        {
            // (Das Gitter wird in OnBeginCamera unter die jeweilige Kamera gelegt.)
            var cam = Camera.main;
            Vector3 focus = cam != null ? cam.transform.position : Vector3.zero;

            // Simulation um den Spieler (Auto oder Skater), nicht um die Kamera: dort passiert etwas
            var local = PlayerAvatar.Local;
            if (local != null && local.car != null) Floater.Attach(local.car);
            Vector3 center = local != null ? (local.Mode == PlayerMode.Driving ? local.car.transform.position : local.skater.transform.position) : focus;
            _ripples.Recenter(center);
            RainDrops();
            _ripples.Step(Time.deltaTime);
            PushGlobals();
            WatchAvatars();
        }

        /// <summary>Regen: zufaellige kleine Tropfen in der Simulation.</summary>
        void RainDrops()
        {
            float rain = WeatherSystem.Rain;
            if (rain <= 0.01f) return;
            _rainDrops += rain * 90f * Time.deltaTime;
            while (_rainDrops >= 1f)
            {
                _rainDrops -= 1f;
                _ripples.Disturb(_ripples.RandomPoint(), 0.5f, -0.035f);
            }
        }

        /// <summary>Andere Spieler (nicht selbst simuliert): Platscher, wenn Auto oder Skater unter den Wasserspiegel faellt.</summary>
        void WatchAvatars()
        {
            _scan -= Time.deltaTime;
            if (_scan <= 0f)
            {
                _scan = 1f;
                _avatars.Clear();
                _avatars.AddRange(FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None));
            }
            foreach (var a in _avatars)
            {
                if (a == null || a.IsOwner || a.car == null || a.skater == null) continue;
                float cy = a.car.transform.position.y, sy = a.skater.transform.position.y;
                if (_prevY.TryGetValue(a, out var prev))
                {
                    if (prev.x >= Level && cy < Level) Splash(a.car.transform.position, Mathf.Clamp01((prev.x - cy) / Time.deltaTime / 10f), true);
                    if (a.skater.State != SkaterState.Hidden && prev.y >= Level && sy < Level) Splash(a.skater.transform.position, 0.5f, false);
                    if (cy < Level + 0.5f && a.car.Speed > 1f) Wake(a.car.transform.position, a.car.Speed, Time.deltaTime);
                }
                _prevY[a] = new Vector2(cy, sy);
            }
        }

        // ------------------------------------------------------------------ Hoehe und Stoerungen

        /// <summary>Hoehe der Wellen (ohne Simulation) an x, z zur Zeit t, relativ zum Wasserspiegel.</summary>
        public static float WaveHeight(float x, float z, float t)
        {
            float h = 0f;
            foreach (var w in Waves)
            {
                float dx = w.x, dz = w.y, len = Mathf.Sqrt(dx * dx + dz * dz);
                dx /= len; dz /= len;
                float k = 2f * Mathf.PI / w.w;
                float c = Mathf.Sqrt(9.81f * w.w / (2f * Mathf.PI));
                h += w.z * Mathf.Sin(k * (dx * x + dz * z - c * t));
            }
            return h;
        }

        /// <summary>Wasseroberflaeche (Welt-y) an dieser Stelle, mit Wellen und Simulation.</summary>
        public static float HeightAt(Vector3 p)
        {
            float h = Level + WaveHeight(p.x, p.z, WaterTime);
            if (Instance != null && Instance._ripples != null) h += Instance._ripples.Sample(p);
            return h;
        }

        /// <summary>Platscher: Ring in der Simulation, Tropfen, Geraeusch. strength 0..1.</summary>
        public static void Splash(Vector3 pos, float strength, bool big)
        {
            if (Instance == null) return;
            strength = Mathf.Clamp01(strength);
            pos.y = HeightAt(pos);
            Instance._ripples.Disturb(pos, big ? 2.4f : 1.3f, -(0.18f + 0.3f * strength) * (big ? 1f : 0.6f));
            WaterFx.Splash(pos, strength, big);
            WaterSound.Splash(pos, big ? strength : strength * 0.6f);
        }

        /// <summary>Kielwasser: etwas, das sich mit 'speed' durchs Wasser bewegt (jeden Frame aufrufen).</summary>
        public static void Wake(Vector3 pos, float speed, float dt)
        {
            if (Instance == null || speed < 0.3f) return;
            Instance._ripples.Disturb(pos, 1.4f, -Mathf.Min(0.015f, speed * 0.001f) * dt * 30f);
        }
    }

    /// <summary>
    /// Kraeuselungen auf der CPU: Hoehenfeld N x N um einen Mittelpunkt, Wellengleichung mit Daempfung, am Rand weich
    /// abgefangen. Verschiebt sich der Mittelpunkt um ganze Zellen, wandert der Inhalt mit. Die Hoehen landen in einer
    /// Textur (R32) fuer den Wasser-Shader; HeightAt liest sie bilinear.
    /// </summary>
    public class WaterRipples
    {
        const int N = 128;
        const float CellSize = 0.75f, Extent = N * CellSize;
        const float Rate = 30f, Stiffness = 0.02f, Damping = 0.988f, MaxHeight = 0.35f;

        readonly float[] _h = new float[N * N], _v = new float[N * N], _tmp = new float[N * N];
        readonly Texture2D _tex;
        Vector2 _origin;
        float _acc;
        readonly System.Random _rng = new System.Random(99);

        public WaterRipples()
        {
            _tex = new Texture2D(N, N, TextureFormat.RFloat, false, true) { name = "WaterRipples", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _tex.SetPixelData(_h, 0);
            _tex.Apply(false, false);
        }

        public void Dispose() => Shapes.DestroySafe(_tex);

        public void PushGlobals()
        {
            Shader.SetGlobalTexture("_DS_RippleTex", _tex);
            Shader.SetGlobalVector("_DS_RippleRect", new Vector4(_origin.x, _origin.y, 1f / Extent, Extent));
        }

        /// <summary>Feld um 'center' legen; der Inhalt rueckt mit (ganze Zellen).</summary>
        public void Recenter(Vector3 center)
        {
            float ox = Mathf.Floor((center.x - Extent * 0.5f) / CellSize) * CellSize;
            float oz = Mathf.Floor((center.z - Extent * 0.5f) / CellSize) * CellSize;
            int dx = Mathf.RoundToInt((ox - _origin.x) / CellSize), dz = Mathf.RoundToInt((oz - _origin.y) / CellSize);
            if (dx == 0 && dz == 0) return;
            if (Mathf.Abs(dx) >= N || Mathf.Abs(dz) >= N) { System.Array.Clear(_h, 0, _h.Length); System.Array.Clear(_v, 0, _v.Length); }
            else { Shift(_h, dx, dz); Shift(_v, dx, dz); }
            _origin = new Vector2(ox, oz);
        }

        void Shift(float[] a, int dx, int dz)
        {
            System.Array.Clear(_tmp, 0, _tmp.Length);
            for (int z = 0; z < N; z++)
            {
                int sz = z + dz;
                if (sz < 0 || sz >= N) continue;
                int x0 = Mathf.Max(0, -dx), x1 = Mathf.Min(N, N - dx);
                if (x1 <= x0) continue;
                System.Array.Copy(a, sz * N + x0 + dx, _tmp, z * N + x0, x1 - x0);
            }
            System.Array.Copy(_tmp, a, a.Length);
        }

        public void Step(float dt)
        {
            _acc += dt;
            int steps = 0;
            while (_acc >= 1f / Rate && steps < 3) { _acc -= 1f / Rate; Simulate(); steps++; }
            if (_acc > 1f / Rate) _acc = 0f;
            if (steps > 0)
            {
                _tex.SetPixelData(_h, 0);
                _tex.Apply(false, false);
            }
        }

        void Simulate()
        {
            for (int z = 1; z < N - 1; z++)
            {
                int row = z * N;
                // Rand: schneller abklingen, damit nichts zurueckgeworfen wird
                float edgeZ = Mathf.Min(z, N - 1 - z);
                for (int x = 1; x < N - 1; x++)
                {
                    int i = row + x;
                    float avg = (_h[i - 1] + _h[i + 1] + _h[i - N] + _h[i + N]) * 0.25f;
                    float edge = Mathf.Min(edgeZ, Mathf.Min(x, N - 1 - x));
                    float damp = edge < 8f ? Damping - (8f - edge) * 0.012f : Damping;
                    _v[i] = (_v[i] + (avg - _h[i]) * Stiffness) * damp;
                }
            }
            for (int i = 0; i < _h.Length; i++) _h[i] = Mathf.Clamp(_h[i] + _v[i], -MaxHeight, MaxHeight);
        }

        /// <summary>Hoehe an der Stelle p einstellen: kreisfoermig (radius in m) mit weichem Rand; amount in m (negativ = Delle).</summary>
        public void Disturb(Vector3 p, float radius, float amount)
        {
            float fx = (p.x - _origin.x) / CellSize, fz = (p.z - _origin.y) / CellSize;
            int r = Mathf.CeilToInt(radius / CellSize);
            int cx = Mathf.RoundToInt(fx), cz = Mathf.RoundToInt(fz);
            if (cx < -r || cz < -r || cx >= N + r || cz >= N + r) return;
            for (int z = cz - r; z <= cz + r; z++)
            {
                if (z < 1 || z >= N - 1) continue;
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 1 || x >= N - 1) continue;
                    float d = Mathf.Sqrt((x - fx) * (x - fx) + (z - fz) * (z - fz)) * CellSize / radius;
                    if (d > 1f) continue;
                    float w = 1f - d * d;
                    _h[z * N + x] = Mathf.Clamp(_h[z * N + x] + amount * w, -MaxHeight, MaxHeight);
                }
            }
        }

        /// <summary>Bilinear aus dem Feld, 0 ausserhalb (und am Rand ausgeblendet wie im Shader).</summary>
        public float Sample(Vector3 p)
        {
            float fx = (p.x - _origin.x) / CellSize - 0.5f, fz = (p.z - _origin.y) / CellSize - 0.5f;
            if (fx < 0f || fz < 0f || fx >= N - 1 || fz >= N - 1) return 0f;
            int x = (int)fx, z = (int)fz;
            float tx = fx - x, tz = fz - z;
            float a = _h[z * N + x], b = _h[z * N + x + 1], c = _h[(z + 1) * N + x], d = _h[(z + 1) * N + x + 1];
            float h = Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
            float ux = (p.x - _origin.x) / Extent, uz = (p.z - _origin.y) / Extent;
            float edge = Mathf.Min(Mathf.Min(ux, 1f - ux), Mathf.Min(uz, 1f - uz));
            return h * Mathf.SmoothStep(0f, 1f, edge / 0.08f);
        }

        public Vector3 RandomPoint()
        {
            return new Vector3(_origin.x + (float)_rng.NextDouble() * Extent, WaterSurface.Level, _origin.y + (float)_rng.NextDouble() * Extent);
        }
    }

    /// <summary>Platscher-Optik: Tropfen (gestreckte Billboards) und ein wachsender, verblassender Ring auf dem Wasser.</summary>
    public static class WaterFx
    {
        static ParticleSystem _drops;
        static Material _dropMat, _ringMat;

        public static Material DropMaterial => _dropMat != null ? _dropMat : _dropMat = WaterArt.FxMaterial("Drop", WaterArt.Drop());
        public static Material RingMaterial => _ringMat != null ? _ringMat : _ringMat = WaterArt.FxMaterial("Ring", WaterArt.Ring());

        public static void Splash(Vector3 pos, float strength, bool big)
        {
            if (_drops == null) _drops = MakeDrops();
            int count = Mathf.RoundToInt((big ? 55f : 28f) * (0.5f + strength));
            var ep = new ParticleSystem.EmitParams { position = pos };
            for (int i = 0; i < count; i++)
            {
                Vector2 dir = Random.insideUnitCircle.normalized;
                float up = Random.Range(3f, 7.5f) * (0.6f + strength) * (big ? 1f : 0.7f);
                float side = Random.Range(0.8f, 4f) * (0.5f + strength);
                ep.velocity = new Vector3(dir.x * side, up, dir.y * side);
                ep.startSize = Random.Range(0.06f, 0.16f) * (big ? 1.2f : 0.9f);
                ep.startLifetime = Random.Range(0.7f, 1.3f);
                ep.startColor = new Color(0.82f, 0.94f, 1f, Random.Range(0.55f, 0.8f));
                _drops.Emit(ep, 1);
            }
            Ring(pos, big ? 1f : 0.6f, strength);
        }

        public static void Ring(Vector3 pos, float size, float strength)
        {
            var ring = RingAt(pos, (4f + 6f * strength) * size, 1.3f + 0.6f * strength, 0.55f);
            ring.followWater = true;
        }

        /// <summary>Ring an einer festen Stelle (z. B. Gehweg unter dem Hydranten), waagerecht.</summary>
        public static SplashRing RingAt(Vector3 pos, float maxSize, float life, float alpha)
        {
            var go = new GameObject("SplashRing");
            go.transform.position = pos + Vector3.up * 0.06f;
            go.transform.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
            go.AddComponent<MeshFilter>().sharedMesh = WaterArt.Quad;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = RingMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var ring = go.AddComponent<SplashRing>();
            ring.maxSize = maxSize;
            ring.life = life;
            ring.alpha = alpha;
            return ring;
        }

        static ParticleSystem MakeDrops()
        {
            var go = new GameObject("WaterDrops");
            Object.DontDestroyOnLoad(go);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.gravityModifier = 1f;
            main.startColor = new Color(0.9f, 0.98f, 1f, 0.9f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500;
            var em = ps.emission;
            em.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.75f, 0.92f, 1f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = DropMaterial;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 1.8f;
            r.velocityScale = 0.035f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }
    }

    /// <summary>Ring, der waechst und verblasst (Platscher, Hydranten-Pfuetze). Auf dem Wasser folgt er der Oberflaeche.</summary>
    public class SplashRing : MonoBehaviour
    {
        public float maxSize = 6f, life = 1.5f, startSize = 0.6f, alpha = 0.8f;
        public bool followWater;
        public Color color = Color.white;
        float _t;
        MaterialPropertyBlock _mpb;
        MeshRenderer _mr;

        void Awake()
        {
            _mr = GetComponent<MeshRenderer>();
            _mpb = new MaterialPropertyBlock();
        }

        void Update()
        {
            _t += Time.deltaTime;
            float u = Mathf.Clamp01(_t / life);
            float ease = 1f - (1f - u) * (1f - u);
            float s = Mathf.Lerp(startSize, maxSize, ease);
            transform.localScale = new Vector3(s, s, 1f);
            if (followWater)
            {
                var p = transform.position;
                p.y = WaterSurface.HeightAt(p) + 0.06f;
                transform.position = p;
            }
            var c = color;
            c.a = alpha * (1f - u) * (1f - u);
            _mpb.SetColor("_BaseColor", c);
            _mr.SetPropertyBlock(_mpb);
            if (u >= 1f) Destroy(gameObject);
        }
    }

    /// <summary>Kleine prozedurale Texturen und Meshes fuer Wasser-Effekte (keine Bilddateien).</summary>
    public static class WaterArt
    {
        static Mesh _quad;
        static readonly Dictionary<string, Texture2D> Tex = new Dictionary<string, Texture2D>();

        /// <summary>Einheits-Quad in der XY-Ebene (Vorderseite +Z und -Z, da der FX-Shader beidseitig zeichnet).</summary>
        public static Mesh Quad
        {
            get
            {
                if (_quad != null) return _quad;
                _quad = new Mesh { name = "WaterQuad" };
                _quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) });
                _quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
                _quad.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
                _quad.RecalculateNormals();
                _quad.RecalculateBounds();
                return _quad;
            }
        }

        public static Material FxMaterial(string name, Texture2D tex)
        {
            var baseMat = Resources.Load<Material>("SmokeMat");
            var mat = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("DriftSkate/FX"));
            mat.name = "Water_" + name;
            mat.SetTexture("_BaseMap", tex);
            return mat;
        }

        /// <summary>Tropfen: weicher Fleck, nach oben etwas spitzer.</summary>
        public static Texture2D Drop() => Make("drop", 32, (u, v) =>
        {
            float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
            float d = Mathf.Sqrt(dx * dx + dy * dy * 0.8f);
            float a = 1f - Mathf.SmoothStep(0.45f, 1f, d);
            return new Color(1f, 1f, 1f, a);
        });

        /// <summary>Ring: dunkle Innenkante, heller Kamm, weich auslaufend.</summary>
        public static Texture2D Ring() => Make("ring", 64, (u, v) =>
        {
            float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float band = 1f - Mathf.Abs(d - 0.82f) / 0.14f;
            float a = Mathf.Clamp01(band) * (1f - Mathf.SmoothStep(0.9f, 1f, d));
            return new Color(1f, 1f, 1f, a);
        });

        /// <summary>Weiche Scheibe (Pfuetze, Nebel).</summary>
        public static Texture2D Disc() => Make("disc", 64, (u, v) =>
        {
            float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            return new Color(1f, 1f, 1f, 1f - Mathf.SmoothStep(0.55f, 1f, d));
        });

        static Texture2D Make(string key, int size, System.Func<float, float, Color> f)
        {
            if (Tex.TryGetValue(key, out var t) && t != null) return t;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "water_" + key, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = f((x + 0.5f) / size, (y + 0.5f) / size);
            tex.SetPixels(px);
            tex.Apply();
            Tex[key] = tex;
            return tex;
        }
    }
}
