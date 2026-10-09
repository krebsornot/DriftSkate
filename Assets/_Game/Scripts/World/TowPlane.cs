using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Das Schleppflugzeug mit Werbebanner: zieht in gut 100 m Hoehe einen grossen, leicht unrunden Bogen
    /// um den Stadtrand. Propeller dreht, das Flugzeug neigt sich in der Kurve, das Banner flattert in einer
    /// Kette aus Segmenten und schwingt in Kurven nach aussen. Online richtet sich die Position nach der
    /// Server-Zeit, so sehen alle Spieler das Flugzeug an derselben Stelle.
    /// Aufbau (Meshes, Materialien, Pivots) kommt aus dem Editor: DriftSkate → Schleppflugzeug.
    /// </summary>
    public class TowPlane : MonoBehaviour
    {
        [Header("Route (Bogen um die Stadt)")]
        public Vector3 center = Vector3.zero;
        public float radiusX = 395f;
        public float radiusZ = 355f;
        public float wobble = 0.07f;      // Unrundheit der Kurve (Anteil vom Radius)
        public float altitude = 108f;
        public float altitudeSwing = 7f;
        public float speed = 21f;         // m/s
        public float startOffset = 0f;    // Meter auf der Route (Solo)

        [Header("Teile (vom Editor verdrahtet)")]
        public Transform propeller;
        public Transform[] banner;        // Kette: Segment 0 haengt am Seil, jedes weitere am Ende des vorigen

        LoopRoute _route;
        float _s;
        float _bank, _swing, _yawRate;
        Vector3 _lastForward;
        AudioSource _audio;

        public float RouteLength => Route.Length;

        LoopRoute Route => _route ?? (_route = new LoopRoute(Shape));

        /// <summary>Flache Route ohne Hoehe: Ellipse mit leichter Beule, im Uhrzeigersinn von oben.</summary>
        Vector3 Shape(float theta)
        {
            float k = 1f + wobble * Mathf.Sin(theta * 3f + 1.1f) + wobble * 0.5f * Mathf.Sin(theta * 2f + 0.4f);
            return new Vector3(Mathf.Sin(theta) * radiusX * k, 0f, Mathf.Cos(theta) * radiusZ * k);
        }

        public Vector3 PointAt(float s) => center + Route.FlatPoint(s) + Vector3.up * Height(s);

        float Height(float s) => altitude + Mathf.Sin(s / Route.Length * Mathf.PI * 6f + 0.3f) * altitudeSwing;

        void Awake()
        {
            _s = startOffset;
            Place(0f, true);
        }

        void Start()
        {
            if (Application.isBatchMode) return;
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.clip = MakeEngineClip();
            _audio.loop = true;
            _audio.spatialBlend = 1f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.minDistance = 40f;
            _audio.maxDistance = 300f;
            _audio.dopplerLevel = 0f;
            _audio.volume = 0f;
            _audio.time = Random.value * 2f;
            _audio.Play();
        }

        /// <summary>Fuer Screenshots und Tests: Flugzeug an eine Stelle der Route setzen.</summary>
        public void SetRoutePosition(float s)
        {
            _s = s;
            Place(0f, true);
            Animate(0f, true);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _s = Route.Advance(_s, speed, startOffset, dt, 60f);
            Place(dt, false);
            Animate(dt, false);
        }

        void Place(float dt, bool snap)
        {
            Vector3 p = PointAt(_s);
            Vector3 fwd = (PointAt(_s + 8f) - p).normalized;
            Vector3 flat = Vector3.ProjectOnPlane(fwd, Vector3.up).normalized;

            float turn = 0f; // Gierrate in Grad/s
            if (!snap && dt > 0f && _lastForward != Vector3.zero)
                turn = Vector3.SignedAngle(_lastForward, flat, Vector3.up) / dt;
            _lastForward = flat;
            _yawRate = Mathf.Lerp(_yawRate, turn, snap ? 1f : 1f - Mathf.Exp(-dt * 1.2f));
            // Rechtskurve (positive Gierrate) -> rechte Flaeche nach unten
            _bank = Mathf.Lerp(_bank, Mathf.Clamp(-_yawRate * 7f, -26f, 26f), snap ? 1f : 1f - Mathf.Exp(-dt * 1.5f));
            transform.SetPositionAndRotation(p, Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 0f, _bank));
        }

        void Animate(float dt, bool snap)
        {
            float t = Time.time;
            if (propeller != null) propeller.Rotate(0f, 0f, 1100f * dt, Space.Self);

            if (banner != null)
            {
                // Das Banner bleibt in Kurven hinter dem Flugzeug zurueck: Schwung nach aussen, dazu ruhiges Flattern
                _swing = Mathf.Lerp(_swing, Mathf.Clamp(_yawRate * 2.2f, -14f, 14f), snap ? 1f : 1f - Mathf.Exp(-dt * 0.9f));
                for (int i = 0; i < banner.Length; i++)
                {
                    if (banner[i] == null) continue;
                    float amp = 2.2f + i * 0.9f;
                    float yaw = _swing / Mathf.Max(1, banner.Length) * 1.4f + Mathf.Sin(t * 2.1f - i * 0.8f) * amp;
                    float pitch = Mathf.Sin(t * 1.6f - i * 0.6f + 1.3f) * (1.4f + i * 0.35f);
                    banner[i].localRotation = Quaternion.Euler(pitch, yaw, 0f);
                }
            }

            if (_audio != null)
            {
                var cam = Camera.main;
                float d = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 999f;
                _audio.volume = 0.4f * SaveSystem.Profile.sfxVolume;
                _audio.pitch = 1f + Mathf.Sin(t * 0.17f) * 0.02f;
                _audio.mute = d > _audio.maxDistance;
            }
        }

        /// <summary>Knatternder Kolbenmotor: Grundton mit Zuendschlag, leicht verrauscht. Endlos schleifbar.</summary>
        static AudioClip MakeEngineClip()
        {
            const int rate = 44100, seconds = 2;
            int len = rate * seconds;
            var data = new float[len];
            var rng = new System.Random(321);
            float lp = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)rate;
                // Frequenzen ganzzahlig pro 2 s, damit die Schleife nahtlos ist
                float tone = Mathf.Sin(2f * Mathf.PI * 70f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 140f * t) * 0.3f
                           + Mathf.Sin(2f * Mathf.PI * 210f * t) * 0.12f;
                float pulse = 0.55f + 0.45f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 35f * t), 2f);
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.06f;
                data[i] = (tone * pulse + lp * 0.7f) * 0.45f;
            }
            var clip = AudioClip.Create("TowPlaneEngine", len, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void OnDrawGizmosSelected()
        {
            _route = null; // Werte im Inspector koennten sich geaendert haben
            Route.DrawGizmo(center, Height, Color.magenta);
        }
    }
}
