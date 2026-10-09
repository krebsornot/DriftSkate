using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Ein NPC-Auto auf seiner Runde (siehe Traffic). Kinematischer Rigidbody: schiebt, laesst sich aber nicht
    /// schieben. Bremst vor Autos und Skatern auf der Spur; steht es zu lange, faehrt es kurz "durch" (ohne Collider),
    /// damit nichts dauerhaft blockiert. Raeder drehen und lenken, Bremslichter, Blinker vor dem Abbiegen.
    /// Man kann sich dranhaengen (Hitchable).
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

        Traffic _traffic;
        Traffic.Route _route;
        float _phase, _s, _speed, _blocked, _ghost, _spin, _steer;
        bool _braking, _init;
        int _blink; // -1 links, 1 rechts, 0 aus
        Rigidbody _rb;
        BoxCollider _box;
        Renderer[] _renderers;
        bool _visible = true;
        static int _obstacleMask;

        public float Speed => _speed;
        public float RoutePosition => _s;
        public bool Braking => _braking;

        public void Init(Traffic traffic, Traffic.Route route, float phase, int id)
        {
            _traffic = traffic;
            _route = route;
            _phase = phase;
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _box = GetComponent<BoxCollider>();
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
            _init = true;
        }

        void FixedUpdate()
        {
            if (!_init) return;
            float dt = Time.fixedDeltaTime;

            // Wo das Auto laut Uhr sein sollte: aufholen bzw. zurueckfallen lassen, bei grossem Abstand springen
            float target = _route.DistanceAtTime(Traffic.Clock + _phase);
            float diff = _route.Delta(_s, target);
            _route.Sample(_s, out _, out _, out float routeSpeed);
            if (Mathf.Abs(diff) > 90f && !NearCamera(60f))
            {
                _s = target;
                diff = 0f;
            }
            float desired = routeSpeed * Mathf.Clamp(1f + diff / 25f, 0.4f, 1.5f);

            // Hindernis auf der Spur (Autos, Skater): rechtzeitig anhalten
            _ghost = Mathf.Max(0f, _ghost - dt);
            if (_box != null) _box.enabled = _ghost <= 0f;
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
            _rb.MovePosition(pos);
            _rb.MoveRotation(Quaternion.LookRotation(dir));

            // Lenkwinkel aus der Kruemmung, Blinker vor einer Kurve
            _route.Sample(_s + 2.5f, out _, out Vector3 ahead, out _);
            float turn = Vector3.SignedAngle(dir, ahead, Vector3.up) * Mathf.Deg2Rad / 2.5f; // 1/m
            _steer = Mathf.Lerp(_steer, Mathf.Atan(wheelBase * turn) * Mathf.Rad2Deg, 1f - Mathf.Exp(-8f * dt));
            _route.Sample(_s + 26f, out _, out Vector3 far, out _);
            float upcoming = Vector3.SignedAngle(dir, far, Vector3.up);
            _blink = upcoming > 30f ? 1 : upcoming < -30f ? -1 : 0;
        }

        bool NearCamera(float range)
        {
            var cam = Camera.main;
            return cam != null && (cam.transform.position - transform.position).sqrMagnitude < range * range;
        }

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

            _spin = Mathf.Repeat(_spin + _speed / wheelRadius * Mathf.Rad2Deg * dt, 360f);
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] == null) continue;
                float steer = i < 2 ? _steer : 0f;
                wheels[i].localRotation = Quaternion.Euler(0f, steer, 0f) * Quaternion.Euler(_spin, 0f, 0f);
            }

            if (tailLights != null) tailLights.sharedMaterial = _braking || _speed < 0.3f ? tailOn : tailOff;
            bool on = Mathf.Repeat(Time.time * 1.6f, 1f) < 0.5f;
            if (indicatorLeft != null) indicatorLeft.sharedMaterial = _blink < 0 && on ? indicatorOn : indicatorOff;
            if (indicatorRight != null) indicatorRight.sharedMaterial = _blink > 0 && on ? indicatorOn : indicatorOff;
        }
    }
}
