using System;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Raycast-Fahrzeugphysik fuer Drift im Stil von CarX:
    /// Federung pro Rad, Reifen mit Schlupfkurve und Grip-Kreis, Heckantrieb mit Raddrehzahl,
    /// Kupplung (Clutch-Kick), Handbremse, Gegenlenk-Hilfe.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour
    {
        public class Wheel
        {
            public Vector3 localTop;
            public bool front;
            public bool grounded;
            public float compression, load;
            public Vector3 point, normal = Vector3.up;
            public float longForce;
            public float slipAngleDeg, latSlipSpeed, spinSlip;
            public float spinAngle;
            public Transform pivot, spin;
            public float visualOffset;
        }

        const float RadToRpm = 60f / (2f * Mathf.PI);

        public VehicleSetup setup = new VehicleSetup();
        public VehicleInput input;
        public bool hasDriver;
        public bool isLocal = true;

        public readonly Wheel[] wheels = new Wheel[4]; // 0 VL, 1 VR, 2 HL, 3 HR

        public int Gear { get; private set; } = 1;
        public float EngineRpm { get; private set; } = 900f;
        public float SteerAngle { get; set; }
        public float Throttle { get; private set; }
        public float BodySlip { get; private set; }
        public float ForwardSpeed { get; private set; }
        public float Speed => _rb != null ? (isLocal ? _rb.linearVelocity.magnitude : _remoteVelocity.magnitude) : 0f;
        public float SpeedKmh => Speed * 3.6f;
        public bool Grounded { get; private set; }
        public bool AllWheelsGrounded { get; private set; }
        public Rigidbody Body => _rb;

        /// <summary>Wird bei hartem Wandkontakt ausgeloest (Geschwindigkeitsaenderung in m/s).</summary>
        public event Action<float> HardImpact;

        Rigidbody _rb;
        int _mask;
        float _rearOmega;
        bool _prevClutchIn;
        float _shiftTimer;
        int _queuedShift;
        Vector3 _remoteVelocity, _remoteLastPos;

        void Awake() => Init();

        /// <summary>Initialisiert Raeder und Rigidbody. Wird von Awake aufgerufen (und von Editor-Tests direkt).</summary>
        public void Init()
        {
            _rb = GetComponent<Rigidbody>();
            _mask = ~LayerMask.GetMask("Car", "Skater", "Ignore Raycast", "Rail");
            for (int i = 0; i < 4; i++)
                if (wheels[i] == null) wheels[i] = new Wheel { front = i < 2 };
            ApplySetup(setup);
        }

        public void ApplySetup(VehicleSetup s)
        {
            setup = s;
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            _rb.mass = s.mass;
            _rb.centerOfMass = new Vector3(0f, s.comHeight, s.comForward);
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0.05f;
            _rb.maxAngularVelocity = 25f;
            _rb.interpolation = isLocal ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            _rb.collisionDetectionMode = isLocal ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.Discrete;

            float topY = s.wheelRadius + s.restLength;
            float x = s.track * 0.5f, z = s.wheelbase * 0.5f;
            if (wheels[0] == null) for (int i = 0; i < 4; i++) wheels[i] = new Wheel { front = i < 2 };
            wheels[0].localTop = new Vector3(-x, topY, z);
            wheels[1].localTop = new Vector3(x, topY, z);
            wheels[2].localTop = new Vector3(-x, topY, -z);
            wheels[3].localTop = new Vector3(x, topY, -z);
            EngineRpm = s.idleRpm;
            if (Gear > s.gears.Length) Gear = 1;
        }

        public void SetLocal(bool local)
        {
            isLocal = local;
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = !local;
            _rb.interpolation = local ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            _rb.collisionDetectionMode = local ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.Discrete;
            _remoteLastPos = transform.position;
        }

        public void QueueShift(int direction) => _queuedShift = direction;

        public void SetWheelVisual(int index, Transform pivot, Transform spin)
        {
            wheels[index].pivot = pivot;
            wheels[index].spin = spin;
        }

        void FixedUpdate()
        {
            if (!isLocal || _rb.isKinematic) return;
            Step(Time.fixedDeltaTime);
        }

        /// <summary>Ein Physikschritt. Oeffentlich, damit Editor-Tests ohne Play-Mode simulieren koennen.</summary>
        public void Step(float dt)
        {
            Transform t = transform;
            Vector3 up = t.up;
            Vector3 vel = _rb.linearVelocity;
            Vector3 flatVel = Vector3.ProjectOnPlane(vel, up);
            float fwdSpeed = Vector3.Dot(vel, t.forward);
            ForwardSpeed = fwdSpeed;
            BodySlip = flatVel.magnitude > 2f ? Vector3.SignedAngle(t.forward, flatVel, up) : 0f;

            VehicleInput inp = input;
            if (!hasDriver)
            {
                inp = default;
                inp.brake = 0.25f;
                inp.handbrake = flatVel.magnitude < 2f;
            }

            HandleGears(inp, fwdSpeed, dt);

            float throttle = inp.throttle, brake = inp.brake;
            if (setup.autoGearbox && Gear == -1)
            {
                throttle = inp.brake;
                brake = inp.throttle;
            }
            Throttle = throttle;

            UpdateSteering(inp, flatVel.magnitude, fwdSpeed, dt);
            UpdateSuspension(up, dt);

            float rearVLong = 0.5f * (Vector3.Dot(_rb.GetPointVelocity(t.TransformPoint(wheels[2].localTop)), t.forward) +
                                      Vector3.Dot(_rb.GetPointVelocity(t.TransformPoint(wheels[3].localTop)), t.forward));
            ComputeDrivetrain(inp, throttle, brake, rearVLong, dt);
            ApplyTireForces(inp, up, dt);

            // Luftwiderstand, Rollwiderstand, Abtrieb
            _rb.AddForce(-vel * vel.magnitude * setup.drag);
            if (Grounded && flatVel.magnitude > 0.5f)
                _rb.AddForce(-flatVel.normalized * setup.rolling * setup.mass * 9.81f);
            _rb.AddForce(-up * fwdSpeed * fwdSpeed * setup.downforce);
        }

        void HandleGears(VehicleInput inp, float fwdSpeed, float dt)
        {
            if (_shiftTimer > 0f) _shiftTimer -= dt;
            int maxGear = setup.gears.Length;

            if (setup.autoGearbox)
            {
                if (Gear > 0 && _shiftTimer <= 0f)
                {
                    // Tempo statt Vorwaertsanteil: im Drift faellt der Vorwaertsanteil, das Auto soll aber den Gang halten.
                    float speed = _rb.linearVelocity.magnitude;
                    float groundRpm = speed / setup.wheelRadius * Ratio(Gear) * RadToRpm;
                    bool drifting = Mathf.Abs(BodySlip) > 20f && speed > 6f;
                    bool atLimiter = EngineRpm > setup.maxRpm * 0.97f && groundRpm > setup.maxRpm * 0.72f;
                    if (Gear < maxGear && (groundRpm > setup.maxRpm * 0.93f || atLimiter)) { Gear++; _shiftTimer = 0.18f; }
                    else if (!drifting && Gear > 1 && groundRpm < setup.maxRpm * 0.42f) { Gear--; _shiftTimer = 0.12f; }
                }
                if (Gear > 0 && fwdSpeed < 0.8f && inp.brake > 0.5f && inp.throttle < 0.1f) Gear = -1;
                else if (Gear == -1 && fwdSpeed > -0.8f && inp.throttle > 0.3f) Gear = 1;
                if (Gear == 0) Gear = 1;
            }
            else if (_queuedShift != 0)
            {
                Gear = Mathf.Clamp(Gear + _queuedShift, -1, maxGear);
                _shiftTimer = 0.15f;
            }
            _queuedShift = 0;
        }

        float Ratio(int gear)
        {
            if (gear > 0) return setup.gears[Mathf.Min(gear, setup.gears.Length) - 1] * setup.finalDrive;
            if (gear < 0) return -setup.reverseRatio * setup.finalDrive;
            return 0f;
        }

        void UpdateSteering(VehicleInput inp, float speed, float fwdSpeed, float dt)
        {
            float slipAbs = Mathf.Abs(BodySlip);
            // Bei hohem Tempo ohne Drift weniger Einschlag, im Drift voller Einschlag.
            float speedLimit = Mathf.Lerp(1f, 0.5f, Mathf.InverseLerp(12f, 45f, speed) * (1f - Mathf.Clamp01(slipAbs / 20f)));
            float target = inp.steer * setup.maxSteer * speedLimit;

            if (setup.assist > 0f && fwdSpeed > 3f && Gear >= 0)
            {
                // Gegenlenk-Hilfe wie ein Fahrer: Raeder in Fahrtrichtung drehen (= Driftwinkel)
                // und zusaetzlich gegen die Drehrate lenken, damit das Heck nicht ueberdreht.
                // Der Spieler regelt den Winkel mit Gas und etwas Lenkung in die Kurve.
                float yawRateDeg = Vector3.Dot(_rb.angularVelocity, transform.up) * Mathf.Rad2Deg;
                float counter = BodySlip - yawRateDeg * 0.07f;
                float slipWeight = Mathf.Clamp01((slipAbs - 4f) / 10f);
                float assisted = counter + inp.steer * setup.maxSteer * 0.4f;
                target = Mathf.Lerp(target, assisted, Mathf.Clamp01(setup.assist * 1.25f) * slipWeight);
            }
            target = Mathf.Clamp(target, -setup.maxSteer, setup.maxSteer);
            SteerAngle = Mathf.MoveTowards(SteerAngle, target, setup.steerSpeed * dt);
        }

        void UpdateSuspension(Vector3 up, float dt)
        {
            float maxLen = setup.restLength + setup.wheelRadius;
            int groundedCount = 0;
            for (int i = 0; i < 4; i++)
            {
                Wheel w = wheels[i];
                Vector3 top = transform.TransformPoint(w.localTop);
                if (Physics.Raycast(top, -up, out RaycastHit hit, maxLen, _mask, QueryTriggerInteraction.Ignore))
                {
                    float compression = maxLen - hit.distance;
                    float compVel = (compression - w.compression) / dt;
                    w.compression = compression;
                    float k = w.front ? setup.springFront : setup.springRear;
                    float c = w.front ? setup.damperFront : setup.damperRear;
                    float f = k * compression + c * compVel;
                    float bump = setup.restLength * 0.9f;
                    if (compression > bump) f += (compression - bump) * k * 8f;
                    w.load = Mathf.Max(0f, f);
                    w.grounded = true;
                    w.point = hit.point;
                    w.normal = hit.normal;
                    groundedCount++;
                }
                else
                {
                    w.grounded = false;
                    w.compression = 0f;
                    w.load = 0f;
                }
            }

            AntiRoll(0, 1, setup.antiRollFront);
            AntiRoll(2, 3, setup.antiRollRear);

            for (int i = 0; i < 4; i++)
            {
                Wheel w = wheels[i];
                if (w.grounded) _rb.AddForceAtPosition(up * w.load, transform.TransformPoint(w.localTop));
            }

            Grounded = groundedCount > 0;
            AllWheelsGrounded = groundedCount == 4;
        }

        void AntiRoll(int a, int b, float stiffness)
        {
            Wheel wa = wheels[a], wb = wheels[b];
            float force = (wa.compression - wb.compression) * stiffness;
            if (wa.grounded) wa.load = Mathf.Max(0f, wa.load + force);
            if (wb.grounded) wb.load = Mathf.Max(0f, wb.load - force);
        }

        void ComputeDrivetrain(VehicleInput inp, float throttle, float brake, float rearVLong, float dt)
        {
            float r = setup.wheelRadius;
            float ratio = Ratio(Gear);
            bool clutchIn = inp.clutch || inp.handbrake || _shiftTimer > 0f || Gear == 0;
            bool clutchReleased = _prevClutchIn && !clutchIn;
            _prevClutchIn = clutchIn;

            float driveForce = 0f;
            if (!clutchIn && ratio != 0f)
            {
                if (clutchReleased)
                {
                    // Clutch-Kick: Motor dreht hoeher als die Raeder -> Raeder werden schlagartig beschleunigt.
                    float engineOmegaAtWheel = EngineRpm / RadToRpm / ratio;
                    if (Mathf.Abs(engineOmegaAtWheel) > Mathf.Abs(_rearOmega)) _rearOmega = engineOmegaAtWheel;
                }
                EngineRpm = Mathf.Max(setup.idleRpm, Mathf.Abs(_rearOmega * ratio) * RadToRpm);
                float torque = EngineTorque(EngineRpm) * throttle;
                if (EngineRpm >= setup.maxRpm) torque = 0f;
                if (throttle < 0.05f && Mathf.Abs(rearVLong) > 1f)
                    torque = -setup.peakTorque * 0.12f * (EngineRpm / setup.maxRpm);
                driveForce = torque * ratio * 0.9f / r;
            }
            else
            {
                float targetRpm = Mathf.Lerp(setup.idleRpm, setup.maxRpm * 1.02f, throttle);
                float rate = targetRpm > EngineRpm ? 11000f : 6000f;
                EngineRpm = Mathf.Clamp(Mathf.MoveTowards(EngineRpm, targetRpm, rate * dt), setup.idleRpm, setup.maxRpm);
            }

            Wheel rl = wheels[2], rr = wheels[3];
            float capL = rl.grounded ? setup.gripRear * rl.load : 0f;
            float capR = rr.grounded ? setup.gripRear * rr.load : 0f;
            float tractionCap = Mathf.Lerp(2f * Mathf.Min(capL, capR), capL + capR, setup.diffLock);
            float slideCap = tractionCap * 0.85f;
            float rearBrake = brake * setup.brakeForce * (1f - setup.brakeBias);
            float groundOmega = rearVLong / r;

            float rearForce;
            if (inp.handbrake)
            {
                _rearOmega = 0f;
                rearForce = Mathf.Abs(rearVLong) > 0.3f ? -Mathf.Sign(rearVLong) * slideCap : -rearVLong * setup.mass;
            }
            else
            {
                float surface = _rearOmega * r;
                bool spinning = Mathf.Abs(surface - rearVLong) > 0.6f;
                float brakeDir = Mathf.Abs(rearVLong) > 0.3f ? -Mathf.Sign(rearVLong) : 0f;
                float wanted = driveForce + brakeDir * rearBrake;

                if (!spinning && Mathf.Abs(wanted) <= tractionCap)
                {
                    _rearOmega = groundOmega;
                    rearForce = wanted;
                }
                else
                {
                    float slipDir = spinning ? Mathf.Sign(surface - rearVLong) : Mathf.Sign(wanted);
                    float tireForce = slipDir * slideCap;
                    float inertia = 1.6f + 0.18f * ratio * ratio * (clutchIn ? 0f : 1f);
                    float brakeTorque = rearBrake * r * Mathf.Sign(_rearOmega);
                    _rearOmega += ((driveForce - tireForce) * r - brakeTorque) / inertia * dt;
                    if (Mathf.Sign(_rearOmega * r - rearVLong) != slipDir) _rearOmega = groundOmega;
                    rearForce = tireForce;
                }
            }

            float half = rearForce * 0.5f;
            rl.longForce = rl.grounded ? half : 0f;
            rr.longForce = rr.grounded ? half : 0f;
            float spinSlip = Mathf.Abs(_rearOmega * r - rearVLong);
            rl.spinSlip = rr.spinSlip = inp.handbrake ? Mathf.Abs(rearVLong) : spinSlip;

            float frontBrake = brake * setup.brakeForce * setup.brakeBias * 0.5f;
            for (int i = 0; i < 2; i++)
            {
                Wheel w = wheels[i];
                if (!w.grounded) { w.longForce = 0f; continue; }
                Vector3 fwd = Quaternion.AngleAxis(SteerAngle, transform.up) * transform.forward;
                float v = Vector3.Dot(_rb.GetPointVelocity(w.point), fwd);
                w.longForce = Mathf.Abs(v) > 0.3f ? -Mathf.Sign(v) * frontBrake : -v * setup.mass * 0.25f * Mathf.Min(1f, brake);
                w.spinSlip = 0f;
            }
        }

        float EngineTorque(float rpm)
        {
            float x = rpm / setup.maxRpm;
            float d = (x - 0.68f) / 0.6f;
            return setup.peakTorque * Mathf.Clamp(0.55f + 0.45f * (1f - d * d), 0.3f, 1f) * (Admin.Turbo ? 1.8f : 1f);
        }

        static float LateralCurve(float alpha, float slideRatio)
        {
            const float peak = 0.13f; // ca. 7.5 Grad
            if (alpha < peak) return Mathf.Sin(alpha / peak * Mathf.PI * 0.5f);
            return Mathf.Lerp(1f, slideRatio, Mathf.Clamp01((alpha - peak) / 0.4f));
        }

        void ApplyTireForces(VehicleInput inp, Vector3 up, float dt)
        {
            for (int i = 0; i < 4; i++)
            {
                Wheel w = wheels[i];
                if (!w.grounded) { w.latSlipSpeed = 0f; w.slipAngleDeg = 0f; continue; }

                Vector3 fwd = w.front ? Quaternion.AngleAxis(SteerAngle, up) * transform.forward : transform.forward;
                fwd = Vector3.ProjectOnPlane(fwd, w.normal).normalized;
                Vector3 side = Vector3.Cross(w.normal, fwd);

                Vector3 pv = _rb.GetPointVelocity(w.point);
                float vLong = Vector3.Dot(pv, fwd);
                float vLat = Vector3.Dot(pv, side);

                float mu = w.front ? setup.gripFront : setup.gripRear;
                float cap = mu * w.load;
                float fx = Mathf.Clamp(w.longForce, -cap, cap);
                float latCap = Mathf.Sqrt(Mathf.Max(0f, cap * cap - fx * fx));
                if (!w.front && inp.handbrake && hasDriver) latCap *= setup.handbrakeGrip;

                float alpha = Mathf.Atan2(Mathf.Abs(vLat), Mathf.Max(Mathf.Abs(vLong), 1f));
                float curve = LateralCurve(alpha, w.front ? setup.frontSlideRatio : setup.rearSlideRatio);
                float fyCurve = curve * latCap;
                float fyStatic = Mathf.Abs(vLat) * setup.mass * 0.25f / dt * 0.5f;
                float fy = -Mathf.Sign(vLat) * Mathf.Min(fyCurve, fyStatic);

                w.slipAngleDeg = alpha * Mathf.Rad2Deg;
                w.latSlipSpeed = Mathf.Abs(vLat);

                _rb.AddForceAtPosition(fwd * fx + side * fy, w.point + up * (setup.wheelRadius * 0.4f));
            }
        }

        /// <summary>0..1: wie stark ein Hinterrad qualmt (fuer Rauch, Spuren, Sound).</summary>
        public float SmokeAmount(int wheelIndex)
        {
            Wheel w = wheels[wheelIndex];
            if (!w.grounded) return 0f;
            float lat = Mathf.InverseLerp(2.5f, 9f, w.latSlipSpeed);
            float spin = Mathf.InverseLerp(2f, 10f, w.spinSlip);
            return Mathf.Clamp01(Mathf.Max(lat, spin));
        }

        void Update()
        {
            if (!isLocal)
            {
                float dt = Mathf.Max(Time.deltaTime, 0.0001f);
                _remoteVelocity = Vector3.Lerp(_remoteVelocity, (transform.position - _remoteLastPos) / dt, 0.3f);
                _remoteLastPos = transform.position;
                ForwardSpeed = Vector3.Dot(_remoteVelocity, transform.forward);
                var flat = Vector3.ProjectOnPlane(_remoteVelocity, transform.up);
                BodySlip = flat.magnitude > 2f ? Vector3.SignedAngle(transform.forward, flat, transform.up) : 0f;
            }
            UpdateVisuals(Time.deltaTime);
        }

        void UpdateVisuals(float dt)
        {
            float maxLen = setup.restLength + setup.wheelRadius;
            Vector3 up = transform.up;
            for (int i = 0; i < 4; i++)
            {
                Wheel w = wheels[i];
                if (w.pivot == null) continue;
                Vector3 top = transform.TransformPoint(w.localTop);
                float dist = maxLen;
                if (Physics.Raycast(top, -up, out RaycastHit hit, maxLen, _mask, QueryTriggerInteraction.Ignore))
                    dist = hit.distance;
                float target = -(dist - setup.wheelRadius);
                w.visualOffset = Mathf.Lerp(w.visualOffset, target, 1f - Mathf.Exp(-30f * dt));
                w.pivot.localPosition = w.localTop + Vector3.up * w.visualOffset;
                w.pivot.localRotation = Quaternion.Euler(0f, w.front ? SteerAngle : 0f, 0f);

                float omega = (!w.front && isLocal) ? _rearOmega : ForwardSpeed / setup.wheelRadius;
                if (!isLocal && !w.front && w.spinSlip > 0f) omega += 25f;
                w.spinAngle = (w.spinAngle + omega * Mathf.Rad2Deg * dt) % 360f;
                if (w.spin != null) w.spin.localRotation = Quaternion.Euler(w.spinAngle, 0f, 0f);
            }
        }

        void OnCollisionEnter(Collision c)
        {
            if (!isLocal || c.contactCount == 0) return;
            if (c.rigidbody != null && !c.rigidbody.isKinematic && c.rigidbody.mass < 100f) return; // Pylonen usw.
            Vector3 n = c.GetContact(0).normal;
            if (Vector3.Dot(n, Vector3.up) > 0.6f) return; // Boden
            float deltaV = c.impulse.magnitude / Mathf.Max(1f, _rb.mass);
            if (deltaV > 0.8f) HardImpact?.Invoke(deltaV);
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            _rb.position = position;
            _rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _rearOmega = 0f;
            Gear = 1;
            SteerAngle = 0f;
        }

        public void ResetUpright()
        {
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Teleport(transform.position + Vector3.up * 1.2f, Quaternion.LookRotation(fwd.normalized, Vector3.up));
        }

        /// <summary>Fuer entfernte Spieler: Zustandswerte aus dem Netzwerk.</summary>
        public void SetRemoteState(float steer, float rpm, float throttle, bool smoking)
        {
            SteerAngle = steer;
            EngineRpm = rpm;
            Throttle = throttle;
            wheels[2].spinSlip = wheels[3].spinSlip = smoking ? 6f : 0f;
            wheels[2].grounded = wheels[3].grounded = true;
            wheels[2].latSlipSpeed = wheels[3].latSlipSpeed = smoking ? 6f : 0f;
        }
    }
}
