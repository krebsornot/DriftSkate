using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DriftSkate
{
    /// <summary>
    /// Hydranten am Gehweg lassen sich umfahren (Auto oder Board): der Deckel fliegt weg, eine Wasserfontaene schiesst hoch.
    /// Wer hineinfaehrt, wird hausdachhoch geschleudert und landet auf dem naechsten Dach (die Flugbahn zielt auf einen Punkt
    /// ein Stueck hinter der Dachkante); ohne Dach in der Naehe geht es einfach gerade hoch.
    /// Nach 10 s versiegt das Wasser, nach weiteren 25 s steht der Hydrant wieder da.
    /// Die Hydranten stehen schon in der City-Szene (CityProps); beim Laden der Stadt haengt Setup dieses Script an alle.
    /// Online: wer einen Hydranten umfaehrt, meldet es (Client -> Host -> alle), die Fontaene sprudelt bei allen.
    /// </summary>
    public class FireHydrant : MonoBehaviour
    {
        public static readonly List<FireHydrant> All = new List<FireHydrant>();

        const float SprayTime = 10f, RepairTime = 25f, NoRoofApex = 16f, RoofSearch = 26f, AboveRoof = 3.5f;
        const string Msg = "DS_Hydrant";
        static bool _registered;

        public int Index { get; private set; }
        public bool Spraying => _state == 1;
        public float Apex => _apex;

        int _state;              // 0 heil, 1 sprudelt, 2 kaputt (trocken)
        float _timer, _apex, _relaunch;
        PlayerAvatar _carPush; // Auto fliegt erst senkrecht hoch, ueber Dachhoehe kommt der Schub aufs Dach
        Vector3? _landing;
        readonly List<GameObject> _cap = new List<GameObject>();
        Collider _body;
        ParticleSystem _jet, _splash;
        Transform _core;

        /// <summary>Alle Hydranten der geladenen Stadt einrichten (Reihenfolge nach Position, auf allen Rechnern gleich).</summary>
        public static void Setup()
        {
            All.Clear();
            _registered = false;
            var list = new List<Transform>();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Hydrant") list.Add(t);
            list.Sort((a, b) =>
            {
                int c = Mathf.RoundToInt(a.position.x * 10f).CompareTo(Mathf.RoundToInt(b.position.x * 10f));
                return c != 0 ? c : Mathf.RoundToInt(a.position.z * 10f).CompareTo(Mathf.RoundToInt(b.position.z * 10f));
            });
            for (int i = 0; i < list.Count; i++)
            {
                var h = list[i].gameObject.GetComponent<FireHydrant>() ?? list[i].gameObject.AddComponent<FireHydrant>();
                h.Index = i;
                All.Add(h);
            }
        }

        void Awake()
        {
            foreach (Transform c in transform)
            {
                if (c.name == "HydrantBody") _body = c.GetComponent<Collider>();
                if (c.name == "HydrantTop" || c.name == "HydrantNut" || c.name == "HydrantRing" || c.name == "HydrantNozzle") _cap.Add(c.gameObject);
            }
            // Man faehrt einfach drueber bzw. hindurch: der Hydrant bremst niemanden, er geht kaputt
            if (_body != null) _body.isTrigger = true;
        }

        void OnDestroy() => All.Remove(this);

        void Update()
        {
            RegisterMessages();
            var p = PlayerAvatar.Local;
            float dt = Time.deltaTime;
            _relaunch -= dt;
            CarPush();
            switch (_state)
            {
                case 0:
                    if (p != null && Touches(p, 0.5f, 1.2f, 2.5f)) { Break(); Send(); p.combo?.AddAction("HYDRANT", 150f); }
                    break;
                case 1:
                    _timer -= dt;
                    UpdateCore(dt);
                    if (p != null && _relaunch <= 0f && Touches(p, 1.1f, _apex - transform.position.y, 0f)) Launch(p);
                    if (_timer <= 0f) StopSpray();
                    break;
                case 2:
                    _timer -= dt;
                    UpdateCore(dt);
                    // erst reparieren, wenn keiner direkt daneben steht
                    if (_timer <= 0f && (p == null || !Touches(p, 4f, 3f, 0f))) Repair();
                    break;
            }
        }

        /// <summary>Ist der Spieler (Auto oder Skater) bis 'radius' am Hydranten und unterhalb 'height'? minSpeed: so schnell (m/s).</summary>
        bool Touches(PlayerAvatar p, float radius, float height, float minSpeed)
        {
            Vector3 hp = transform.position;
            if (p.Mode == PlayerMode.Driving)
            {
                var rb = p.car.Body;
                if (rb == null || rb.linearVelocity.magnitude < minSpeed) return false;
                // Abstand zur Karosserie (Kiste ums Auto), nicht zur Mitte
                Vector3 local = p.car.transform.InverseTransformPoint(hp + Vector3.up * 0.4f);
                var b = CarBounds(p);
                Vector3 closest = new Vector3(Mathf.Clamp(local.x, b.min.x, b.max.x), Mathf.Clamp(local.y, b.min.y, b.max.y), Mathf.Clamp(local.z, b.min.z, b.max.z));
                float flat = new Vector2(local.x - closest.x, local.z - closest.z).magnitude;
                float dy = p.car.transform.position.y - hp.y;
                return flat < radius && dy < height;
            }
            var s = p.skater;
            if (s.State == SkaterState.Hidden || s.State == SkaterState.Hitched) return false;
            if (s.Speed < minSpeed && minSpeed > 0f) return false;
            Vector3 sp = s.transform.position;
            float d = new Vector2(sp.x - hp.x, sp.z - hp.z).magnitude;
            return d < radius + 0.3f && sp.y - hp.y < height && sp.y - hp.y > -1f;
        }

        static Bounds CarBounds(PlayerAvatar p)
        {
            // Karosserie in Auto-Koordinaten (aus den Collidern, einmal pro Auto genug genau)
            var b = new Bounds(Vector3.up * 0.6f, new Vector3(1.8f, 1.2f, 4.3f));
            foreach (var c in p.car.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger) continue;
                var lb = c.bounds;
                b.Encapsulate(p.car.transform.InverseTransformPoint(lb.min));
                b.Encapsulate(p.car.transform.InverseTransformPoint(lb.max));
            }
            return b;
        }

        // ------------------------------------------------------------------ Kaputt, Fontaene, Reparatur

        void Break()
        {
            if (_state == 1) return;
            _state = 1;
            _timer = SprayTime;
            FindLanding();
            foreach (var c in _cap) c.SetActive(false);
            FlyingCap();
            StartSpray();
            SpraySound.Play(0.9f, false);
        }

        void StopSpray()
        {
            _state = 2;
            _timer = RepairTime;
            if (_jet != null) _jet.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (_splash != null) _splash.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void Repair()
        {
            _state = 0;
            foreach (var c in _cap) c.SetActive(true);
            if (_core != null) _core.gameObject.SetActive(false);
        }

        /// <summary>Ziel: naechstes Dach (Punkt 4 m hinter der Kante); Fontaene reicht bis etwas ueber das Dach.</summary>
        void FindLanding()
        {
            _landing = null;
            _apex = transform.position.y + NoRoofApex;
            Vector3 hp = transform.position;
            float best = float.MaxValue;
            foreach (var c in Physics.OverlapSphere(hp, RoofSearch, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.name != "Roof") continue;
                var b = c.bounds;
                Vector3 edge = b.ClosestPoint(new Vector3(hp.x, b.max.y, hp.z));
                float d = new Vector2(edge.x - hp.x, edge.z - hp.z).magnitude;
                if (d >= best) continue;
                best = d;
                // ein Stueck hinter die Kante, Richtung Dachmitte (bei schmalen Daechern nicht ueber die Mitte hinaus)
                Vector3 inward = new Vector3(b.center.x - edge.x, 0f, b.center.z - edge.z);
                float room = inward.magnitude;
                Vector3 target = edge + (room > 0.01f ? inward / room * Mathf.Min(4.5f, room) : Vector3.zero);
                target.y = b.max.y;
                _landing = target;
                _apex = Mathf.Max(target.y + AboveRoof, hp.y + 6f);
            }
        }

        void Launch(PlayerAvatar p)
        {
            bool car = p.Mode == PlayerMode.Driving;
            float g = car ? Physics.gravity.magnitude : SkaterController.GravityAccel;
            Vector3 start = car ? p.car.transform.position : p.skater.transform.position;
            float up = Mathf.Max(2f, _apex - start.y);
            float vy = Mathf.Sqrt(2f * g * up);
            if (car)
            {
                // Das Auto hat Luftwiderstand (Kraft k * v²): Startgeschwindigkeit so, dass es trotzdem bis zur Spitze kommt
                float k = p.car.setup.drag, m = p.car.Body.mass;
                if (k > 0f && m > 0f) vy = Mathf.Sqrt(m * g / k * (Mathf.Exp(2f * k * up / m) - 1f));
            }
            Vector3 flat;
            if (_landing.HasValue)
            {
                // so weit seitlich, dass man im Fallen genau auf dem Dachpunkt ankommt
                float t = vy / g + Mathf.Sqrt(2f * Mathf.Max(0.5f, _apex - _landing.Value.y) / g);
                flat = new Vector3(_landing.Value.x - start.x, 0f, _landing.Value.z - start.z) / t;
            }
            else
            {
                var rb = car ? p.car.Body : p.skater.GetComponent<Rigidbody>();
                flat = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up) * 0.5f;
            }
            Vector3 v = flat + Vector3.up * vy;
            bool ok;
            if (car)
            {
                // Senkrecht hoch: mit Schub zum Dach wuerde das lange Auto mit der Schnauze an die Hauswand stossen
                if (_landing.HasValue) { v = Vector3.up * vy; _carPush = p; }
                p.car.Body.linearVelocity = v;
                p.car.Body.angularVelocity = Vector3.zero;
                p.combo?.AddAction("GEYSER", 300f);
                ok = true;
            }
            else ok = p.skater.Geyser(v);
            if (!ok) return;
            _relaunch = 1.5f;
            BurstPop.Show("GEYSER!", Palette.Cyan, new Vector2(0f, 220f), 200f, 1.4f);
        }

        /// <summary>Auto ist ueber Dachhoehe: seitlich so anschieben, dass es im Fallen auf dem Dachpunkt landet.</summary>
        void CarPush()
        {
            if (_carPush == null || !_landing.HasValue) return;
            var p = _carPush;
            if (p.Mode != PlayerMode.Driving) { _carPush = null; return; }
            var rb = p.car.Body;
            Vector3 pos = p.car.transform.position;
            Vector3 target = _landing.Value;
            if (pos.y < target.y + 1.5f)
            {
                if (rb.linearVelocity.y < 0f) _carPush = null; // kommt nicht hoch genug, Pech
                return;
            }
            float g = Physics.gravity.magnitude, vy = rb.linearVelocity.y;
            float t = (vy + Mathf.Sqrt(vy * vy + 2f * g * (pos.y - target.y - 0.6f))) / g;
            Vector3 flat = new Vector3(target.x - pos.x, 0f, target.z - pos.z) / Mathf.Max(0.3f, t);
            rb.linearVelocity = flat + Vector3.up * vy;
            _carPush = null;
        }

        // ------------------------------------------------------------------ Effekte

        void StartSpray()
        {
            float h = _apex - transform.position.y;
            if (_jet == null)
            {
                _jet = Water("HydrantJet", 120f, true);
                _splash = Water("HydrantSplash", 60f, false);
                var core = Shapes.Part(PrimitiveType.Cylinder, transform, Vector3.zero, Vector3.one, Palette.Hex("6FE0FF"), 0f, emission: 0.9f, name: "HydrantWaterCore");
                core.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _core = core.transform;
            }
            // Strahl: Startgeschwindigkeit so, dass die Tropfen bis zur Spitze kommen
            float v0 = Mathf.Sqrt(2f * 9.81f * h);
            var main = _jet.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(v0 * 0.92f, v0);
            main.startLifetime = 2f * v0 / 9.81f * 0.9f;
            main.maxParticles = 1200;
            _jet.Play();
            _splash.Play();
            _core.gameObject.SetActive(true);
            _core.localScale = new Vector3(0.45f, 0.01f, 0.45f);
            _core.localPosition = new Vector3(0f, 0.6f, 0f);
        }

        /// <summary>Wassersaeule waechst schnell hoch und sinkt am Ende wieder zusammen.</summary>
        void UpdateCore(float dt)
        {
            if (_core == null || !_core.gameObject.activeSelf) return;
            float h = _apex - transform.position.y - 1.5f;
            float target = _state == 1 ? h : 0f;
            float cur = _core.localScale.y * 2f;
            cur = Mathf.MoveTowards(cur, target, (_state == 1 ? 60f : 25f) * dt);
            float wob = 1f + Mathf.Sin(Time.time * 23f) * 0.06f;
            _core.localScale = new Vector3(0.32f * wob, Mathf.Max(0.005f, cur * 0.5f), 0.32f * wob);
            _core.localPosition = new Vector3(0f, 0.6f + cur * 0.5f, 0f);
            if (_state == 2 && cur <= 0.01f) _core.gameObject.SetActive(false);
        }

        ParticleSystem Water(string name, float rate, bool jet)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 0.65f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.gravityModifier = 1f;
            main.startColor = new Color(0.85f, 0.96f, 1f, 0.85f);
            main.startSize = jet ? new ParticleSystem.MinMaxCurve(0.35f, 0.75f) : new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
            main.startSpeed = jet ? 10f : new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startLifetime = jet ? 2f : new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = jet ? 1200 : 200;
            var em = ps.emission;
            em.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = jet ? 2.5f : 55f;
            shape.radius = jet ? 0.15f : 0.3f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, jet ? 0.7f : 1f, 1f, jet ? 1.6f : 0.5f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.8f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Resources.Load<Material>("SmokeMat");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        /// <summary>Der Deckel fliegt weg (lose Kopie, verschwindet nach ein paar Sekunden).</summary>
        void FlyingCap()
        {
            var top = _cap.Find(c => c.name == "HydrantTop");
            if (top == null) return;
            var cap = Instantiate(top, top.transform.position, top.transform.rotation);
            cap.SetActive(true);
            cap.layer = 0;
            var col = cap.AddComponent<SphereCollider>();
            col.radius = 0.5f;
            var rb = cap.AddComponent<Rigidbody>();
            rb.mass = 3f;
            rb.linearVelocity = new Vector3(Random.Range(-2f, 2f), Random.Range(9f, 12f), Random.Range(-2f, 2f));
            rb.angularVelocity = Random.insideUnitSphere * 12f;
            Destroy(cap, 6f);
        }

        // ------------------------------------------------------------------ Netzwerk

        static void RegisterMessages()
        {
            if (_registered) return;
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || nm.CustomMessagingManager == null) return;
            _registered = true;
            nm.CustomMessagingManager.RegisterNamedMessageHandler(Msg, OnMessage);
        }

        /// <summary>Selbst umgefahren: Client meldet es dem Host, der Host allen (Clients).</summary>
        void Send()
        {
            var nm = NetworkManager.Singleton;
            if (!_registered || nm == null || !nm.IsListening) return;
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe(Index);
            if (nm.IsServer) { if (nm.ConnectedClientsIds.Count > 1) nm.CustomMessagingManager.SendNamedMessageToAll(Msg, w, NetworkDelivery.Reliable); }
            else nm.CustomMessagingManager.SendNamedMessage(Msg, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }

        static void OnMessage(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out int index);
            if (index < 0 || index >= All.Count || All[index] == null) return;
            var h = All[index];
            if (h._state != 1) h.Break();
            // Host gibt die Meldung eines Clients an alle weiter
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsServer && sender != NetworkManager.ServerClientId)
            {
                using var w = new FastBufferWriter(8, Allocator.Temp);
                w.WriteValueSafe(index);
                nm.CustomMessagingManager.SendNamedMessageToAll(Msg, w, NetworkDelivery.Reliable);
            }
        }

        /// <summary>Fuer Tests: Hydrant sofort aufbrechen.</summary>
        public void TestBreak() => Break();

        /// <summary>Fuer Tests: Landepunkt auf dem Dach (null = keins in der Naehe), vorab berechnet.</summary>
        public Vector3? PreviewLanding()
        {
            if (_state == 0) FindLanding();
            return _landing;
        }
    }
}
