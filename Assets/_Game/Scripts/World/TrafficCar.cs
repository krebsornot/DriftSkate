using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Ein NPC-Auto auf seiner Runde (siehe Traffic). Echter Rigidbody (1050 kg), der seiner Spur mit begrenzter
    /// Kraft folgt: rammt man ihn, wird er weggeschoben und dreht sich (statt wie eine Wand zu stehen), rutscht aus und
    /// faehrt danach in seine Spur zurueck. Hoehe und Kippen sind gesperrt, damit nichts umfaellt oder durch den Boden
    /// rutscht. Bremst vor Autos und Skatern; steht es zu lange, faehrt es kurz "durch" (ohne Collider).
    /// Raeder drehen und lenken, Bremslichter, Blinker vor dem Abbiegen. Man kann sich dranhaengen (Hitchable).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TrafficCar : MonoBehaviour
    {
        [Header("Teile (vom Editor verdrahtet)")]
        public Transform[] wheels = new Transform[4]; // vorn links, vorn rechts, hinten links, hinten rechts
        public float wheelRadius = 0.34f, wheelBase = 2.2f;
        public Renderer tailLights, indicatorLeft, indicatorRight;
        public Material tailOff, tailOn, indicatorOff, indicatorOn;
        public float cullDistance = 175f;

        [Header("Physik")]
        public float mass = 1050f;
        public float followAccel = 9f;     // so stark zieht es zurueck auf die Spur (m/s^2, begrenzt)
        public float knockDistance = 2.2f; // weiter weggeschoben = aus der Spur geworfen
        public float knockImpact = 3.5f;   // ab diesem Aufprall-Tempo (m/s) sofort ausgehebelt

        public enum Mode { Drive, Knocked, Recover }

        Traffic _traffic;
        Traffic.Route _route;
        float _phase, _s, _speed, _blocked, _ghost, _spin, _steer, _modeTime, _still;
        bool _braking, _init;
        int _blink; // -1 links, 1 rechts, 0 aus
        Mode _mode;
        Rigidbody _rb;
        BoxCollider _box;
        Renderer[] _renderers;
        bool _visible = true;
        static int _obstacleMask;
        static PhysicsMaterial _slide;

        public float Speed => _mode == Mode.Drive ? _speed : (_rb != null ? _rb.linearVelocity.magnitude : 0f);
        public float RoutePosition => _s;
        public bool Braking => _braking;
        public Mode State => _mode;

        public void Init(Traffic traffic, Traffic.Route route, float phase, int id)
        {
            _traffic = traffic;
            _route = route;
            _phase = phase;
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = false;
            _rb.mass = mass;
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            // Flach auf der Strasse: nur schieben und um die Hochachse drehen
            _rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0.5f;
            _rb.centerOfMass = new Vector3(0f, 0.5f, 0f);
            _box = GetComponent<BoxCollider>();
            if (_slide == null)
                _slide = new PhysicsMaterial("TrafficSlide") { dynamicFriction = 0.25f, staticFriction = 0.25f, bounciness = 0.1f, frictionCombine = PhysicsMaterialCombine.Minimum };
            if (_box != null) _box.sharedMaterial = _slide;
            _renderers = GetComponentsInChildren<Renderer>();
            if (_obstacleMask == 0) _obstacleMask = LayerMask.GetMask("Car", "Skater");

            var hitch = GetComponent<Hitchable>() ?? gameObject.AddComponent<Hitchable>();
            hitch.kind = Hitchable.KindNpc;
            hitch.id = id;

            _s = _route.DistanceAtTime(Traffic.Clock + _phase);
            _route.Sample(_s, out Vector3 p, out Vector3 d, out _speed);
            transform.SetPositionAndRotation(p, Quaternion.LookRotation(d));
            _rb.position = p;
            _rb.rotation = transform.rotation;
            _rb.linearVelocity = d * _speed;
            _mode = Mode.Drive;
            _init = true;
        }

        // ------------------------------------------------------------------ Fahren

        void FixedUpdate()
        {
            if (!_init) return;
            float dt = Time.fixedDeltaTime;
            _modeTime += dt;
            _ghost = Mathf.Max(0f, _ghost - dt);
            if (_box != null) _box.enabled = _ghost <= 0f;

            if (_mode == Mode.Knocked) { StepKnocked(dt); return; }

            // Wo das Auto laut Uhr sein sollte: aufholen bzw. zurueckfallen lassen, bei grossem Abstand springen
            float target = _route.DistanceAtTime(Traffic.Clock + _phase);
            float diff = _route.Delta(_s, target);
            _route.Sample(_s, out _, out _, out float routeSpeed);
            if (Mathf.Abs(diff) > 90f && !NearCamera(60f))
            {
                _s = target;
                diff = 0f;
                Snap();
            }
            float desired = routeSpeed * Mathf.Clamp(1f + diff / 25f, 0.4f, 1.5f);
            if (_mode == Mode.Recover) desired = Mathf.Min(desired, 6f + _modeTime * 2f); // sachte wieder anfahren

            // Hindernis auf der Spur (Autos, Skater): rechtzeitig anhalten
            bool obstacle = false;
            if (_ghost <= 0f)
            {
                float look = 4f + _speed * 1.6f;
                Vector3 half = _box != null ? _box.size * 0.5f : new Vector3(0.8f, 0.6f, 1.8f);
                Vector3 center = transform.TransformPoint(_box != null ? _box.center : Vector3.up * 0.8f);
                Vector3 start = center + transform.forward * (half.z + 0.3f);
                if (Physics.BoxCast(start, new Vector3(half.x * 0.85f, half.y * 0.8f, 0.2f), transform.forward, out RaycastHit hit,
                        transform.rotation, look, _obstacleMask, QueryTriggerInteraction.Ignore)
                    && hit.collider.attachedRigidbody != _rb)
                {
                    obstacle = true;
                    desired = Mathf.Min(desired, Mathf.Max(0f, hit.distance - 1.5f) * 1.1f);
                }
            }
            if (obstacle && desired < 0.6f)
            {
                _blocked += dt;
                if (_blocked > 6f) { _ghost = 3f; _blocked = 0f; }
            }
            else _blocked = Mathf.Max(0f, _blocked - dt * 2f);

            _braking = desired < _speed - 0.3f;
            _speed = Mathf.MoveTowards(_speed, desired, (_braking ? 7f : 2.5f) * dt);
            _s = Mathf.Repeat(_s + _speed * dt, _route.length);

            _route.Sample(_s, out Vector3 pos, out Vector3 dir, out _);
            Follow(pos, dir, dt);

            // Lenkwinkel aus der Kruemmung, Blinker vor einer Kurve
            _route.Sample(_s + 2.5f, out _, out Vector3 ahead, out _);
            float turn = Vector3.SignedAngle(dir, ahead, Vector3.up) * Mathf.Deg2Rad / 2.5f; // 1/m
            _steer = Mathf.Lerp(_steer, Mathf.Atan(wheelBase * turn) * Mathf.Rad2Deg, 1f - Mathf.Exp(-8f * dt));
            _route.Sample(_s + 26f, out _, out Vector3 far, out _);
            float upcoming = Vector3.SignedAngle(dir, far, Vector3.up);
            _blink = upcoming > 30f ? 1 : upcoming < -30f ? -1 : 0;
        }

        /// <summary>Mit begrenzter Beschleunigung auf den Spurpunkt ziehen (Feder + Daempfer), ebenso die Ausrichtung.</summary>
        void Follow(Vector3 pos, Vector3 dir, float dt)
        {
            Vector3 p = _rb.position;
            Vector3 err = new Vector3(pos.x - p.x, 0f, pos.z - p.z);
            Vector3 fwd = Vector3.ProjectOnPlane(_rb.rotation * Vector3.forward, Vector3.up).normalized;
            float yawErr = Vector3.SignedAngle(fwd, dir, Vector3.up);

            if (_mode == Mode.Drive && (err.magnitude > knockDistance || Mathf.Abs(yawErr) > 35f)) { Knock(); return; }
            if (_mode == Mode.Recover)
            {
                if (err.magnitude < 0.4f && Mathf.Abs(yawErr) < 6f) SetMode(Mode.Drive);
                else if (_modeTime > 10f) { if (NearCamera(45f)) _ghost = 2f; else Snap(); }
            }

            // Hoehe folgt der Strasse (die Physik haelt sie fest)
            if (Mathf.Abs(pos.y - p.y) > 0.005f) _rb.position = new Vector3(p.x, pos.y, p.z);

            float limit = _mode == Mode.Recover ? 4f : followAccel;
            Vector3 v = _rb.linearVelocity;
            v.y = 0f;
            Vector3 a = err * (_mode == Mode.Recover ? 2.5f : 7f) + (dir * _speed - v) * 4.5f;
            if (a.magnitude > limit) a = a.normalized * limit;
            _rb.linearVelocity = v + a * dt;

            float w = _rb.angularVelocity.y;
            float alpha = yawErr * Mathf.Deg2Rad * 9f - w * 5f;
            float maxAlpha = _mode == Mode.Recover ? 2.5f : 6f;
            _rb.angularVelocity = new Vector3(0f, w + Mathf.Clamp(alpha, -maxAlpha, maxAlpha) * dt, 0f);
        }

        // ------------------------------------------------------------------ Gerammt

        void OnCollisionEnter(Collision c)
        {
            if (_mode == Mode.Knocked || c.rigidbody == null) return;
            // Nur echte Stoesse von Autos (Spieler oder andere NPCs); Skater schubsen nicht
            if (c.rigidbody.mass < 300f) return;
            if (c.relativeVelocity.magnitude > knockImpact) Knock();
        }

        void Knock()
        {
            SetMode(Mode.Knocked);
            _rb.linearDamping = 1.1f;   // Reifen radieren quer: rutscht aus
            _rb.angularDamping = 1.6f;
            _braking = true;
            _blink = 0;
            _still = 0f;
        }

        void StepKnocked(float dt)
        {
            // Rollreibung in Laengsrichtung kaum, quer stark (Reifen): der Rutscher dreht sich und kommt zum Stehen
            Vector3 v = _rb.linearVelocity;
            Vector3 fwd = transform.forward;
            float along = Vector3.Dot(v, fwd);
            Vector3 lat = v - fwd * along;
            lat = Vector3.MoveTowards(lat, Vector3.zero, 7f * dt);
            along = Mathf.MoveTowards(along, 0f, 2.5f * dt);
            _rb.linearVelocity = fwd * along + lat;
            _speed = Mathf.Abs(along);
            _steer = Mathf.Lerp(_steer, 0f, 1f - Mathf.Exp(-3f * dt));

            bool still = _rb.linearVelocity.magnitude < 0.8f && Mathf.Abs(_rb.angularVelocity.y) < 0.5f;
            _still = still ? _still + dt : 0f;
            if (_still > 1.2f || _modeTime > 8f) StartRecover();
        }

        /// <summary>Zurueck auf die Runde: am naechsten Spurpunkt vor dem Auto weitermachen, langsam anfahren.</summary>
        void StartRecover()
        {
            Vector3 p = _rb.position;
            Vector3 fwd = transform.forward;
            float best = float.MaxValue;
            int bi = 0;
            var pts = _route.pos;
            for (int i = 0; i < pts.Length; i++)
            {
                float d = (pts[i] - p).sqrMagnitude;
                if (Vector3.Dot(_route.dir[i], fwd) < -0.3f) d += 400f; // lieber in Fahrtrichtung einfaedeln
                if (d < best) { best = d; bi = i; }
            }
            _s = _route.s[bi] + 4f;
            _speed = 0f;
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0.5f;
            SetMode(Mode.Recover);
        }

        void SetMode(Mode m)
        {
            _mode = m;
            _modeTime = 0f;
        }

        /// <summary>Unbeobachtet direkt auf die Spur setzen.</summary>
        void Snap()
        {
            _route.Sample(_s, out Vector3 p, out Vector3 d, out float v);
            _rb.position = p;
            _rb.rotation = Quaternion.LookRotation(d);
            transform.SetPositionAndRotation(p, _rb.rotation);
            _speed = v;
            _rb.linearVelocity = d * v;
            _rb.angularVelocity = Vector3.zero;
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0.5f;
            SetMode(Mode.Drive);
        }

        bool NearCamera(float range)
        {
            var cam = Camera.main;
            return cam != null && (cam.transform.position - transform.position).sqrMagnitude < range * range;
        }

        // ------------------------------------------------------------------ Optik

        void Update()
        {
            if (!_init) return;
            float dt = Time.deltaTime;

            // Weit weg: gar nicht zeichnen (die Stadt ist CPU-gebunden)
            var cam = Camera.main;
            bool visible = cam == null || (cam.transform.position - transform.position).sqrMagnitude < cullDistance * cullDistance;
            if (visible != _visible)
            {
                _visible = visible;
                foreach (var r in _renderers) if (r != null) r.enabled = visible;
            }
            if (!visible) return;

            float roll = _rb != null ? Vector3.Dot(_rb.linearVelocity, transform.forward) : _speed;
            _spin = Mathf.Repeat(_spin + roll / wheelRadius * Mathf.Rad2Deg * dt, 360f);
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] == null) continue;
                float steer = i < 2 ? _steer : 0f;
                wheels[i].localRotation = Quaternion.Euler(0f, steer, 0f) * Quaternion.Euler(_spin, 0f, 0f);
            }

            if (tailLights != null) tailLights.sharedMaterial = _braking || _speed < 0.3f ? tailOn : tailOff;
            bool on = Mathf.Repeat(Time.time * 1.6f, 1f) < 0.5f;
            bool hazard = _mode != Mode.Drive; // nach einem Crash: Warnblinker
            if (indicatorLeft != null) indicatorLeft.sharedMaterial = (_blink < 0 || hazard) && on ? indicatorOn : indicatorOff;
            if (indicatorRight != null) indicatorRight.sharedMaterial = (_blink > 0 || hazard) && on ? indicatorOn : indicatorOff;
        }
    }
}
