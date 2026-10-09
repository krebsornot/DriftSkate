using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Der DRIFT x SKATE-Zeppelin: zieht in gut 75 m Hoehe eine liegende Acht ueber die Stadt.
    /// Propeller drehen, Seiten- und Hoehenruder folgen der Kurve, Wimpel flattert, Positionslichter leuchten,
    /// das Hecklicht blitzt doppelt und die Neonstreifen laufen langsam durch. Online richtet sich die Position
    /// nach der Server-Zeit, so sehen alle Spieler den Zeppelin an derselben Stelle.
    /// Aufbau (Meshes, Materialien, Pivots) kommt aus dem Editor: DriftSkate → Zeppelin.
    /// </summary>
    public class Zeppelin : MonoBehaviour
    {
        [Header("Route (liegende Acht um die Stadtmitte)")]
        public Vector3 center = Vector3.zero;
        public float length = 190f;       // halbe Breite der Acht
        public float width = 105f;        // halbe Hoehe der Acht
        public float yaw = 35f;           // Drehung der Acht (Grad)
        public float altitude = 76f;
        public float altitudeSwing = 6f;
        public float speed = 9f;          // m/s
        public float startOffset = 0f;    // Meter auf der Route (Solo)

        [Header("Teile (vom Editor verdrahtet)")]
        public Transform propLeft, propRight, rudder, elevator, pennant;
        public Renderer neon, navLights, tailLight, windows;

        LoopRoute _route;
        float _s;            // aktuelle Position auf der Route (m)
        float _rudder, _elevator, _bank;
        Vector3 _lastForward;
        Material[] _neonMats;
        Material _tailMat;
        Material[] _navMats;
        AudioSource _audio;

        public float RouteLength => Route.Length;

        LoopRoute Route => _route ?? (_route = new LoopRoute(Shape));

        /// <summary>Flache Acht (Lemniskate von Gerono) ohne Hoehe, im lokalen Routen-Raum.</summary>
        Vector3 Shape(float theta) =>
            Quaternion.Euler(0f, yaw, 0f) * new Vector3(Mathf.Sin(theta) * length, 0f, Mathf.Sin(theta) * Mathf.Cos(theta) * width * 2f);

        /// <summary>Weltposition auf der Route nach s Metern (inkl. sanftem Auf und Ab).</summary>
        public Vector3 PointAt(float s) => center + Route.FlatPoint(s) + Vector3.up * Height(s);

        float Height(float s) => altitude + Mathf.Sin(s / Route.Length * Mathf.PI * 4f + 0.7f) * altitudeSwing;

        void Awake()
        {
            _s = startOffset;
            if (neon != null) _neonMats = neon.materials;
            if (navLights != null) _navMats = navLights.materials;
            if (tailLight != null) _tailMat = tailLight.material;
            Place(0f, true);
        }

        void Start()
        {
            if (Application.isBatchMode) return;
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.clip = MakeHumClip();
            _audio.loop = true;
            _audio.spatialBlend = 1f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.minDistance = 45f;
            _audio.maxDistance = 260f;
            _audio.dopplerLevel = 0f;
            _audio.volume = 0f;
            _audio.time = Random.value * 3f;
            _audio.Play();
        }

        void OnDestroy()
        {
            if (_neonMats != null) foreach (var m in _neonMats) Destroy(m);
            if (_navMats != null) foreach (var m in _navMats) Destroy(m);
            if (_tailMat != null) Destroy(_tailMat);
        }

        /// <summary>Fuer Screenshots und Tests: Zeppelin an eine Stelle der Route setzen.</summary>
        public void SetRoutePosition(float s)
        {
            _s = s;
            Place(0f, true);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _s = Route.Advance(_s, speed, startOffset, dt, 40f);
            Place(dt, false);
            Animate(dt);
        }

        void Place(float dt, bool snap)
        {
            Vector3 p = PointAt(_s);
            Vector3 ahead = PointAt(_s + 6f);
            Vector3 fwd = (ahead - p).normalized;
            Vector3 flat = Vector3.ProjectOnPlane(fwd, Vector3.up).normalized;

            // Gierrate -> leichte Schraeglage und Seitenruder
            float turn = 0f;
            if (!snap && dt > 0f && _lastForward != Vector3.zero)
                turn = Vector3.SignedAngle(_lastForward, flat, Vector3.up) / dt; // Grad/s
            _lastForward = flat;
            float k = snap ? 1f : 1f - Mathf.Exp(-dt * 1.5f);
            _bank = Mathf.Lerp(_bank, Mathf.Clamp(-turn * 0.6f, -4f, 4f), k);
            _rudder = Mathf.Lerp(_rudder, Mathf.Clamp(turn * 4f, -18f, 18f), k);
            float pitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
            _elevator = Mathf.Lerp(_elevator, Mathf.Clamp(pitch * 4f, -14f, 14f), k);

            transform.SetPositionAndRotation(p, Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, 0f, _bank));
        }

        void Animate(float dt)
        {
            float t = Time.time;
            if (propLeft != null) propLeft.Rotate(0f, 0f, -320f * dt, Space.Self);
            if (propRight != null) propRight.Rotate(0f, 0f, 320f * dt, Space.Self);
            // Seitenruder (Hinterkante zeigt nach -Z): positiv gieren -> Ruder nach rechts
            if (rudder != null) rudder.localRotation = Quaternion.Euler(0f, -_rudder, 0f);
            if (elevator != null) elevator.localRotation = Quaternion.Euler(_elevator, 0f, 0f);
            if (pennant != null)
                pennant.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 2.3f) * 9f + Mathf.Sin(t * 5.1f) * 3f, 0f);

            // Neon: die drei Farben laufen langsam nacheinander auf
            if (_neonMats != null)
                for (int i = 0; i < _neonMats.Length; i++)
                    _neonMats[i].SetFloat("_Emission", 0.9f + 1.4f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * 1.3f - i * 2.094f), 2f));

            // Positionslichter atmen leicht, Hecklicht blitzt doppelt
            if (_navMats != null)
                foreach (var m in _navMats) m.SetFloat("_Emission", 2.2f + 0.4f * Mathf.Sin(t * 2f));
            if (_tailMat != null)
            {
                float c = Mathf.Repeat(t, 1.6f);
                bool flash = c < 0.07f || (c > 0.22f && c < 0.29f);
                _tailMat.SetFloat("_Emission", flash ? 4f : 0.3f);
            }

            if (_audio != null)
            {
                var cam = Camera.main;
                float d = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 999f;
                _audio.volume = 0.45f * SaveSystem.Profile.sfxVolume;
                _audio.pitch = 1f + Mathf.Sin(t * 0.21f) * 0.02f;
                _audio.mute = d > _audio.maxDistance;
            }
        }

        /// <summary>Tiefes Propeller-Brummen: Grundton mit Blattschlag, leicht verrauscht. Endlos schleifbar.</summary>
        static AudioClip MakeHumClip()
        {
            const int rate = 44100, seconds = 3;
            int len = rate * seconds;
            var data = new float[len];
            var rng = new System.Random(123);
            float lp = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)rate;
                // Frequenzen ganzzahlig pro 3 s, damit die Schleife nahtlos ist
                float baseTone = Mathf.Sin(2f * Mathf.PI * 46f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 92f * t) * 0.25f
                               + Mathf.Sin(2f * Mathf.PI * 139f * t) * 0.1f;
                float beat = 0.6f + 0.4f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 16f * t), 3f);
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.04f;
                data[i] = (baseTone * beat + lp * 0.9f) * 0.5f;
            }
            var clip = AudioClip.Create("ZeppelinHum", len, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void OnDrawGizmosSelected()
        {
            _route = null; // Werte im Inspector koennten sich geaendert haben
            Route.DrawGizmo(center, Height, Color.cyan);
        }
    }
}
