using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Verfolgerkamera: schwingt beim Driften mit der Fahrtrichtung mit (wie in CarX),
    /// geht beim Skaten naeher ran, Sichtfeld waechst mit dem Tempo. Rechter Stick / rechte Maustaste = umsehen,
    /// rechten Stick druecken / C = Perspektive wechseln (Standard, Nah, Weit).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig Instance { get; private set; }

        /// <summary>Werte einer Perspektive, getrennt fuer Auto und Board.</summary>
        struct ViewSettings
        {
            public float dist, distPerSpeed, height, pitch, pivot, fov;
            public ViewSettings(float dist, float distPerSpeed, float height, float pitch, float pivot, float fov)
            {
                this.dist = dist; this.distPerSpeed = distPerSpeed; this.height = height;
                this.pitch = pitch; this.pivot = pivot; this.fov = fov;
            }
        }

        public static readonly string[] Views = { "STANDARD", "NAH", "WEIT" };

        static readonly ViewSettings[] DriveViews =
        {
            new ViewSettings(6.2f, 0.025f, 2.0f,  8f, 1.0f, 60f), // Standard
            new ViewSettings(3.8f, 0.015f, 1.2f,  3f, 0.8f, 66f), // Nah: tief hinterm Heck, Tempo wirkt staerker
            new ViewSettings(9.5f, 0.030f, 3.4f, 17f, 1.0f, 56f), // Weit: hoch und weit hinten, guter Ueberblick beim Driften
        };

        static readonly ViewSettings[] SkateViews =
        {
            new ViewSettings(4.4f, 0.040f, 1.7f,  8f, 1.2f, 64f), // Standard
            new ViewSettings(2.7f, 0.020f, 0.6f,  2f, 0.9f, 80f), // Nah: tiefe Fisheye-Filmer-Kamera wie im Skatevideo
            new ViewSettings(7.0f, 0.040f, 3.0f, 17f, 1.2f, 58f), // Weit
        };

        PlayerAvatar _target;
        Camera _cam;
        float _roll;
        float _yaw, _yawOffset, _pitchOffset, _lookReturn;
        float _dist = 6f, _height = 2f, _pitch = 8f, _pivot = 1f;
        int _view;
        Vector3 _pos;
        bool _snap = true;
        int _mask;
        SpeedWind _wind;
        float _fov = 60f;
        bool _shot;
        Vector3 _shotPos, _shotLook;
        float _shotW;

        public int View => _view;
        public SpeedWind Wind => _wind;

        void Awake()
        {
            Instance = this;
            _cam = GetComponent<Camera>();
            _mask = ~LayerMask.GetMask("Car", "Skater", "Ignore Raycast", "Rail");
            _view = Mathf.Clamp(SaveSystem.Profile.cameraView, 0, Views.Length - 1);
            _wind = gameObject.AddComponent<SpeedWind>();
            _fov = _cam.fieldOfView;
            // Kleine Deko (Muelleimer, Blumen, Baenke ...) nur in der Naehe zeichnen
            int detail = LayerMask.NameToLayer("Detail");
            if (detail >= 0)
            {
                var dist = new float[32];
                dist[detail] = 85f;
                _cam.layerCullDistances = dist;
                _cam.layerCullSpherical = true;
            }
        }

        public void Follow(PlayerAvatar avatar)
        {
            _target = avatar;
            _snap = true;
        }

        /// <summary>Naechste Perspektive; wird im Profil gemerkt.</summary>
        public void NextView()
        {
            _view = (_view + 1) % Views.Length;
            SaveSystem.Profile.cameraView = _view;
            SaveSystem.Save();
            if (HUD.Instance != null) HUD.Instance.Toast("KAMERA: " + Views[_view], 1.4f);
        }

        /// <summary>Feste Einstellung fuer Gespraeche (z. B. mit Jojo); die Kamera gleitet langsam hin.</summary>
        public void SetDialogShot(Vector3 position, Vector3 lookAt)
        {
            _shot = true;
            _shotPos = position;
            _shotLook = lookAt;
        }

        public void ClearDialogShot() => _shot = false;

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (_target == null)
            {
                _wind.Tick(0f, Vector3.zero, false, dt);
                transform.RotateAround(Vector3.zero, Vector3.up, 4f * dt);
                return;
            }

            if (GameInput.Pressed(GameInput.CameraView)) NextView();

            Vector2 look = GameInput.LookValue;
            if (look.sqrMagnitude > 0.0001f)
            {
                _yawOffset += look.x;
                _pitchOffset = Mathf.Clamp(_pitchOffset - look.y * 0.5f, -15f, 40f);
                _lookReturn = 1.5f;
            }
            else
            {
                _lookReturn -= dt;
                if (_lookReturn <= 0f)
                {
                    _yawOffset = Mathf.MoveTowardsAngle(_yawOffset, 0f, 140f * dt);
                    _pitchOffset = Mathf.MoveTowards(_pitchOffset, 0f, 40f * dt);
                }
            }

            bool driving = _target.Mode == PlayerMode.Driving;
            ViewSettings v = driving ? DriveViews[_view] : SkateViews[_view];
            Transform focus = driving ? _target.car.transform : _target.skater.transform;
            Vector3 vel = driving ? (_target.car.Body != null && !_target.car.Body.isKinematic ? _target.car.Body.linearVelocity : Vector3.zero)
                                  : (_target.skater.GetComponent<Rigidbody>().isKinematic ? Vector3.zero : _target.skater.GetComponent<Rigidbody>().linearVelocity);
            Vector3 flatVel = Vector3.ProjectOnPlane(vel, Vector3.up);
            float speed = flatVel.magnitude;

            Vector3 fwd = driving ? Vector3.ProjectOnPlane(focus.forward, Vector3.up).normalized
                                  : Quaternion.Euler(0, _target.skater.Heading, 0) * Vector3.forward;
            // In der Luft der Flugrichtung folgen, nicht der Drehung (sonst schwenkt die Kamera bei Spins mit)
            if (!driving && (_target.skater.State == SkaterState.Air || _target.skater.State == SkaterState.WallRide) && speed > 1f) fwd = flatVel / speed;
            Vector3 dir = fwd;
            if (speed > 3f)
            {
                float follow = driving ? 0.55f : 0.35f;
                dir = Vector3.Slerp(fwd, flatVel.normalized, follow).normalized;
            }
            float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            if (_snap) _yaw = targetYaw;
            _yaw = Mathf.LerpAngle(_yaw, targetYaw, 1f - Mathf.Exp(-(driving ? 4.5f : 6f) * dt));

            // Beim Perspektivwechsel gleitet die Kamera weich an die neue Position
            float blend = _snap ? 1f : 1f - Mathf.Exp(-3f * dt);
            _dist = Mathf.Lerp(_dist, v.dist + speed * v.distPerSpeed, blend);
            _height = Mathf.Lerp(_height, v.height, blend);
            _pitch = Mathf.Lerp(_pitch, v.pitch, blend);
            _pivot = Mathf.Lerp(_pivot, v.pivot, blend);

            // Nicht unter -7 Grad, sonst taucht die tiefe Nah-Kamera beim Umsehen in den Boden
            Quaternion rot = Quaternion.Euler(Mathf.Max(-7f, _pitch + _pitchOffset), _yaw + _yawOffset, 0f);
            Vector3 pivot = focus.position + Vector3.up * _pivot;
            Vector3 desired = pivot - rot * Vector3.forward * _dist + Vector3.up * (_height - 1f);

            if (Physics.SphereCast(pivot, 0.25f, desired - pivot, out RaycastHit hit, Vector3.Distance(pivot, desired), _mask, QueryTriggerInteraction.Ignore))
                desired = pivot + (desired - pivot).normalized * Mathf.Max(0.8f, hit.distance - 0.1f);

            if (_snap) _pos = desired;
            _pos = Vector3.Lerp(_pos, desired, 1f - Mathf.Exp(-10f * dt));
            transform.position = _pos;
            Vector3 lookAt = pivot + flatVel * 0.08f;
            transform.rotation = Quaternion.LookRotation(lookAt - _pos, Vector3.up);

            // Fahrtwind beim schnellen Skaten (Auto hat schon genug Effekte): Streifen + feines Zittern
            _wind.Tick(speed, flatVel, !driving, dt);
            float w = _wind.Intensity;
            if (w > 0f)
            {
                float t = Time.time * 9f;
                transform.rotation *= Quaternion.Euler((Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 0.5f * w,
                                                       (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 0.35f * w,
                                                       (Mathf.PerlinNoise(t, 5.1f) - 0.5f) * 0.6f * w);
            }

            // Wallride: Kamera neigt sich weich mit (zur Wand hin), wie ein laessiger Schwenk
            float rollTarget = 0f;
            if (!driving && _target.skater.RideBlend > 0f)
            {
                float side = Vector3.Dot(_target.skater.RideTiltNormal, transform.right);
                rollTarget = side * 9f * Mathf.SmoothStep(0f, 1f, _target.skater.RideBlend);
            }
            _roll = Mathf.Lerp(_roll, rollTarget, 1f - Mathf.Exp(-3f * dt));
            if (Mathf.Abs(_roll) > 0.01f) transform.rotation *= Quaternion.Euler(0f, 0f, _roll);

            float targetFov = v.fov + Mathf.Min(speed * 0.6f, 32f);
            _fov = Mathf.Lerp(_fov, targetFov, 1f - Mathf.Exp(-3f * dt));
            _cam.fieldOfView = _fov;

            // Gespraechs-Einstellung weich ueberblenden
            _shotW = Mathf.MoveTowards(_shotW, _shot ? 1f : 0f, dt * 1.4f);
            if (_shotW > 0f)
            {
                float k = Mathf.SmoothStep(0f, 1f, _shotW);
                transform.position = Vector3.Lerp(transform.position, _shotPos, k);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(_shotLook - _shotPos, Vector3.up), k);
                _cam.fieldOfView = Mathf.Lerp(_fov, 50f, k);
            }
            _snap = false;
        }
    }
}
