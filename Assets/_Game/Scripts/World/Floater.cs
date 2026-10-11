using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Auftrieb fuers Auto (nur das selbst simulierte): an vier Punkten knapp ueber den Radaufstandsflaechen drueckt das
    /// Wasser nach oben, je tiefer desto staerker (Gleichgewicht: Wasserlinie etwa auf Hoehe der Radnaben), dazu
    /// Daempfung, so schaukelt das Auto auf den Wellen (WaterSurface.HeightAt). Im Wasser
    /// wirkt Gas als schwacher Schub und Lenken dreht das Auto, man kann also ein Stueck paddeln. Nach ein paar Sekunden
    /// saeuft es ab (Auftrieb laesst nach, Blubberblasen) und steht wieder auf dem letzten festen Boden.
    /// Platscher beim Eintauchen, Kielwasser beim Fahren.
    /// </summary>
    public class Floater : MonoBehaviour
    {
        public const float FloatSeconds = 7f, SinkSeconds = 3f;

        public bool InWater { get; private set; }
        public float Submerged { get; private set; }  // 0..1, wie tief das Auto durchschnittlich drin ist
        public Vector3 LastSolid => _lastSolid;

        VehicleController _car;
        Rigidbody _rb;
        Vector3 _lastSolid;
        Quaternion _lastSolidRot = Quaternion.identity;
        float _solidTimer, _waterTime, _sink, _bubbleTimer;
        bool _hasSolid;

        public static Floater Attach(VehicleController car)
        {
            if (car == null) return null;
            return car.GetComponent<Floater>() ?? car.gameObject.AddComponent<Floater>();
        }

        void Awake()
        {
            _car = GetComponent<VehicleController>();
            _rb = GetComponent<Rigidbody>();
        }

        void FixedUpdate()
        {
            if (_car == null || !_car.isLocal || _rb.isKinematic || WaterSurface.Instance == null) return;
            float dt = Time.fixedDeltaTime;
            Vector3 pos = transform.position;

            // Letzter fester Boden (alle Raeder unten, aufrecht, nicht ueber dem Wasser)
            if (_car.AllWheelsGrounded && transform.up.y > 0.8f && pos.y > WaterSurface.Level + 1f)
            {
                _solidTimer += dt;
                if (_solidTimer > 0.8f || !_hasSolid)
                {
                    _solidTimer = 0f;
                    _hasSolid = true;
                    _lastSolid = pos;
                    Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                    _lastSolidRot = Quaternion.LookRotation(fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward, Vector3.up);
                }
            }

            // Schnelltest: ueberhaupt in der Naehe des Wassers?
            if (pos.y > WaterSurface.Level + 2.5f) { Leave(); return; }

            float g = Physics.gravity.magnitude, m = _rb.mass;
            float sinkFactor = _sink > 0f ? Mathf.Clamp01(1f - _sink / SinkSeconds) : 1f;
            float sub = 0f;
            int under = 0;
            foreach (var w in _car.wheels)
            {
                Vector3 p = transform.TransformPoint(new Vector3(w.localTop.x, 0.15f, w.localTop.z));
                float depth = WaterSurface.HeightAt(p) - p.y;
                if (depth <= 0f) continue;
                under++;
                sub += Mathf.Clamp01(depth / 0.9f);
                float lift = m * g * 0.35f * Mathf.Clamp(depth / 0.45f, 0f, 2.2f) * sinkFactor;
                Vector3 vp = _rb.GetPointVelocity(p);
                float dampF = -vp.y * m * 0.16f;
                _rb.AddForceAtPosition(Vector3.up * (lift + dampF), p);
            }
            Submerged = sub / 4f;

            if (under == 0) { Leave(); return; }
            if (!InWater) Enter();
            _waterTime += dt;

            // Wasserwiderstand und Paddeln
            Vector3 v = _rb.linearVelocity;
            _rb.AddForce(-v * m * 0.9f * Submerged);
            _rb.AddTorque(-_rb.angularVelocity * m * 0.6f * Submerged);
            if (_car.hasDriver && _sink <= 0f)
            {
                var inp = _car.input;
                Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                _rb.AddForce(fwd * (m * 2.2f * inp.throttle * Submerged));
                _rb.AddTorque(Vector3.up * (m * 0.9f * inp.steer * Mathf.Sign(Vector3.Dot(v, fwd) + 0.2f) * Submerged));
            }
            // Kielwasser
            Vector3 flat = Vector3.ProjectOnPlane(v, Vector3.up);
            if (flat.magnitude > 0.5f) WaterSurface.Wake(pos, flat.magnitude, dt);

            // Absaufen
            if (_waterTime > FloatSeconds)
            {
                _sink += dt;
                _bubbleTimer -= dt;
                if (_bubbleTimer <= 0f) { _bubbleTimer = 0.12f; WaterSurface.Instance.Ripples.Disturb(pos + Random.insideUnitSphere * 1.2f, 0.6f, 0.05f); }
                if (_sink > SinkSeconds + 1.2f || pos.y < WaterSurface.Level - 4f) Respawn();
            }
        }

        void Enter()
        {
            InWater = true;
            _waterTime = 0f;
            _sink = 0f;
            float vy = -_rb.linearVelocity.y;
            WaterSurface.Splash(transform.position, Mathf.Clamp01(vy / 9f + _rb.linearVelocity.magnitude / 25f), true);
            if (_car.hasDriver) HUD.Instance?.Toast("PLATSCH! Das Auto schwimmt ... noch.", 3f);
        }

        void Leave()
        {
            if (!InWater) return;
            InWater = false;
            _waterTime = 0f;
            _sink = 0f;
            Submerged = 0f;
        }

        void Respawn()
        {
            Vector3 to = _hasSolid ? _lastSolid : new Vector3(0f, 0.3f, 0f);
            _car.Teleport(to + Vector3.up * 0.4f, _lastSolidRot);
            Leave();
            if (_car.hasDriver) HUD.Instance?.Toast("ABGESOFFEN! Zurueck auf festem Boden.", 3f);
        }
    }
}
