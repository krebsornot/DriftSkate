using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Das UFO: taucht nur bei Nacht-Himmel auf (Warp mit Aufblaehen), schwebt ueber Kreuzungen und leuchtet mit dem
    /// Traktorstrahl die Strasse ab, dann huscht es mit Schwung zur naechsten Kreuzung (neigt sich beim Beschleunigen
    /// und Bremsen). Der Lichtring dreht sich, pink und cyan blinken im Wechsel, der Kern pulsiert.
    /// Position und Ablauf sind eine reine Funktion der gemeinsamen Uhr (online Server-Zeit), es wird nichts
    /// uebertragen; den Nacht-Himmel gibt online der Host vor (WeatherSystem).
    /// Wer in den Traktorstrahl faehrt (Auto oder Board), wird 50 m hochgeschleudert und behaelt dabei sein Tempo:
    /// geradeaus rein heisst im hohen Bogen geradeaus weiter. Das prueft jeder Rechner fuer seinen eigenen Spieler.
    /// Aufbau (Meshes, Materialien, Pivots) kommt aus dem Editor: DriftSkate → UFO.
    /// </summary>
    public class Ufo : MonoBehaviour
    {
        [Header("Wann")]
        public string nightSky = "nacht";

        [Header("Route (Kreuzungen, Index 0..5 je Achse, siehe CityBuilder.RoadCenter)")]
        public Vector2Int[] tour =
        {
            new Vector2Int(2, 2), new Vector2Int(4, 3), new Vector2Int(3, 5), new Vector2Int(1, 4), new Vector2Int(0, 2),
            new Vector2Int(2, 0), new Vector2Int(4, 1), new Vector2Int(5, 3), new Vector2Int(3, 3), new Vector2Int(1, 1),
        };
        public float hoverTime = 10f;     // s ueber einer Kreuzung (mit Strahl)
        public float travelTime = 8f;     // s bis zur naechsten
        public float hoverHeight = 48f;   // ueber allen Haeusern (bis 42 m), unter dem Zeppelin (~76 m)
        public float travelArc = 5f;      // zusaetzliche Hoehe mitten im Flug
        public float craneClearance = 17f; // am Baukran hoeher (Ausleger ~61 m)

        [Header("Teile (vom Editor verdrahtet)")]
        public Transform visual, ring, beam, groundGlow;
        public Renderer ringPink, ringCyan, core;
        public float scale = 1.5f;

        [Header("Traktorstrahl")]
        public float liftHeight = 50f;    // so hoch ueber den Einstiegspunkt fliegt man
        public float beamRadius = 5.5f;   // Radius am Boden, in dem der Strahl packt

        float _warp;            // 0 = weg, 1 = da
        Vector3 _tilt;          // geglaettete Beschleunigung (fuer die Neigung)
        float _beamGlow;
        Material _pinkMat, _cyanMat, _coreMat, _beamMat, _glowMat;
        Color _beamColor, _glowColor;
        Vector3 _crane;
        bool _hasCrane;
        AudioSource _audio;
        float _beamOn;
        Vector3 _beamGround;
        bool _liftedThisPass;   // pro Einfahrt nur einmal (faellt man zurueck in den Strahl, geht es nicht wieder hoch)

        float Period => hoverTime + travelTime;
        public float TourLength => Period * Mathf.Max(1, tour.Length);

        public bool Visible => _warp > 0f;
        /// <summary>Strahl an (packt Spieler) und wo er am Boden ankommt; Lifts zaehlt das Hochschleudern (fuer Tests).</summary>
        public bool BeamActive => _beamOn > 0.5f;
        public Vector3 BeamGround => _beamGround;
        public int Lifts { get; private set; }

        bool Night
        {
            get
            {
                var sky = WeatherSystem.Sky;
                return sky != null && sky.id == nightSky;
            }
        }

        void Awake()
        {
            if (ringPink != null) _pinkMat = ringPink.material;
            if (ringCyan != null) _cyanMat = ringCyan.material;
            if (core != null) _coreMat = core.material;
            if (beam != null)
            {
                var r = beam.GetComponentInChildren<Renderer>();
                if (r != null) { _beamMat = r.material; _beamColor = _beamMat.GetColor("_BaseColor"); }
            }
            if (groundGlow != null)
            {
                var r = groundGlow.GetComponentInChildren<Renderer>();
                if (r != null) { _glowMat = r.material; _glowColor = _glowMat.GetColor("_BaseColor"); }
            }
            FindCrane();
            _warp = Night ? 1f : 0f;
            Pose(SharedClock.Time, 0f, true);
        }

        void Start()
        {
            if (Application.isBatchMode) return;
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.clip = MakeHumClip();
            _audio.loop = true;
            _audio.spatialBlend = 1f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.minDistance = 35f;
            _audio.maxDistance = 240f;
            _audio.dopplerLevel = 0f;
            _audio.volume = 0f;
            _audio.Play();
        }

        void OnDestroy()
        {
            foreach (var m in new[] { _pinkMat, _cyanMat, _coreMat, _beamMat, _glowMat })
                if (m != null) Destroy(m);
        }

        void FindCrane()
        {
            var crane = FindAnyObjectByType<CraneSlew>();
            _hasCrane = crane != null;
            if (_hasCrane) _crane = crane.transform.position;
        }

        /// <summary>Fuer Screenshots und Tests (auch im Edit-Modus): Zeitpunkt der Tour anzeigen, sichtbar.</summary>
        public void ShowAt(double time)
        {
            if (!Application.isPlaying) FindCrane(); // im Edit-Modus keine Material-Kopien (die wuerden in die Szene lecken)
            _warp = 1f;
            _tilt = Vector3.zero;
            Pose(time, 0f, true);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float target = Night ? 1f : 0f;
            _warp = Mathf.MoveTowards(_warp, target, dt / 1.6f);
            Pose(SharedClock.Time, dt, false);
            CheckLift();

            if (_audio != null)
            {
                var cam = Camera.main;
                float d = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 999f;
                _audio.volume = 0.35f * SaveSystem.Profile.sfxVolume * Mathf.Clamp01(_warp * 2f);
                _audio.pitch = 1f + _beamGlow * 0.06f + Mathf.Sin(Time.time * 0.21f) * 0.015f;
                _audio.mute = _warp <= 0f || d > _audio.maxDistance;
            }
        }

        // ------------------------------------------------------------------ Route

        Vector3 Waypoint(int k)
        {
            var c = tour[((k % tour.Length) + tour.Length) % tour.Length];
            return new Vector3(CityBuilder.RoadCenter(c.x), 0f, CityBuilder.RoadCenter(c.y));
        }

        /// <summary>Position nach der Uhr: Schweben (mit leichtem Taumeln) und weiches Huschen zur naechsten Kreuzung.</summary>
        public Vector3 PositionAt(double time, out float beam)
        {
            beam = 0f;
            if (tour == null || tour.Length == 0) return transform.position;
            long seg = (long)System.Math.Floor(time / Period);
            float u = (float)(time - seg * (double)Period);
            int k = (int)(seg % tour.Length);
            Vector3 a = Waypoint(k), p;
            float arc = 0f;
            if (u < hoverTime)
            {
                p = a;
                // Strahl erst nach dem Ankommen an, vor dem Abflug wieder aus
                beam = Mathf.Clamp01((u - 0.8f) / 0.7f) * Mathf.Clamp01((hoverTime - 0.6f - u) / 0.6f);
            }
            else
            {
                float f = (u - hoverTime) / travelTime;
                float e = f * f * f * (f * (f * 6f - 15f) + 10f); // smootherstep
                p = Vector3.Lerp(a, Waypoint(k + 1), e);
                arc = Mathf.Sin(f * Mathf.PI) * travelArc;
            }
            float t = (float)(time % 3600.0);
            Vector3 wobble = new Vector3(Mathf.Sin(t * 0.7f) * 1.2f, Mathf.Sin(t * 1.1f + 0.5f) * 0.7f, Mathf.Cos(t * 0.9f) * 1.2f);
            p.y = Height(p) + arc;
            return p + wobble;
        }

        float Height(Vector3 flat)
        {
            float h = hoverHeight;
            if (_hasCrane)
            {
                float d = Vector2.Distance(new Vector2(flat.x, flat.z), new Vector2(_crane.x, _crane.z));
                h += craneClearance * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(50f, 95f, d)));
            }
            return h;
        }

        // ------------------------------------------------------------------ Darstellung

        void Pose(double time, float dt, bool snap)
        {
            Vector3 p = PositionAt(time, out float beamTarget);
            transform.SetPositionAndRotation(p, Quaternion.identity);

            bool show = _warp > 0f;
            if (visual != null && visual.gameObject.activeSelf != show) visual.gameObject.SetActive(show);
            if (groundGlow != null && !show) groundGlow.gameObject.SetActive(false);
            if (!show) { _beamGlow = 0f; return; }

            // Neigung aus der Beschleunigung: beim Losfliegen nach vorn, beim Bremsen nach hinten
            const float h = 0.25f;
            Vector3 acc = (PositionAt(time + h, out _) - 2f * p + PositionAt(time - h, out _)) / (h * h);
            acc.y = 0f;
            _tilt = snap ? acc : Vector3.Lerp(_tilt, acc, 1f - Mathf.Exp(-dt * 3f));
            Vector3 axis = Vector3.Cross(Vector3.up, _tilt);
            float angle = Mathf.Min(_tilt.magnitude * 0.9f, 20f);
            float t = (float)(time % 3600.0);
            Quaternion tilt = axis.sqrMagnitude > 1e-6f ? Quaternion.AngleAxis(angle, axis.normalized) : Quaternion.identity;
            // Rumpf dreht sich langsam, beim Warp schneller
            float spin = t * 9f + (1f - _warp) * 540f;

            if (visual != null)
            {
                float w = EaseOutBack(_warp);
                visual.localScale = new Vector3(scale * w, scale * Mathf.Lerp(w, 1f, 0.35f), scale * w);
                visual.localRotation = tilt * Quaternion.Euler(0f, spin, 0f);
            }
            if (ring != null) ring.localRotation = Quaternion.Euler(0f, t * 26f, 0f);

            // Lichter: pink und cyan im Wechsel, der Kern pulsiert ruhig
            float blink = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f / 2.4f);
            if (_pinkMat != null) _pinkMat.SetFloat("_Emission", Mathf.Lerp(0.35f, 2.2f, blink));
            if (_cyanMat != null) _cyanMat.SetFloat("_Emission", Mathf.Lerp(2.2f, 0.35f, blink));
            if (_coreMat != null) _coreMat.SetFloat("_Emission", 1.4f + 0.6f * Mathf.Sin(t * 2.1f) + _beamGlow * 0.8f);

            _beamGlow = snap ? beamTarget : Mathf.MoveTowards(_beamGlow, beamTarget, dt * 2.5f);
            float beamOn = _beamGlow * Mathf.Clamp01(_warp * 3f - 2f);
            UpdateBeam(p, beamOn, t);
        }

        // ------------------------------------------------------------------ Traktorstrahl: hochschleudern

        /// <summary>Ist der eigene Spieler im Strahl (am Boden darunter)? Dann einmal hochschleudern.</summary>
        void CheckLift()
        {
            var p = PlayerAvatar.Local;
            if (p == null) return;
            bool car = p.Mode == PlayerMode.Driving;
            if (!car && (p.skater.State == SkaterState.Hidden || p.skater.State == SkaterState.Hitched)) return;
            Vector3 pos = car ? p.car.transform.position : p.skater.transform.position;
            float flat = new Vector2(pos.x - _beamGround.x, pos.z - _beamGround.z).magnitude;
            bool inside = _beamOn > 0.5f && flat < beamRadius && pos.y < transform.position.y - 6f && pos.y < _beamGround.y + 4f;
            if (!inside)
            {
                if (flat > beamRadius + 1f || _beamOn <= 0.5f) _liftedThisPass = false;
                return;
            }
            if (_liftedThisPass) return;
            if (Lift(p, car)) { _liftedThisPass = true; Lifts++; }
        }

        /// <summary>Senkrecht so schnell, dass der hoechste Punkt liftHeight ueber dem Start liegt; waagerecht bleibt das Tempo.</summary>
        bool Lift(PlayerAvatar p, bool car)
        {
            if (car)
            {
                var rb = p.car.Body;
                if (rb == null) return false;
                Vector3 flatV = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
                float g = Physics.gravity.magnitude, vy = Mathf.Sqrt(2f * g * liftHeight);
                // Luftwiderstand (Kraft k * v²) wie beim Hydranten ausgleichen, sonst kommt das Auto nicht ganz hoch
                float k = p.car.setup.drag, m = rb.mass;
                if (k > 0f && m > 0f) vy = Mathf.Sqrt(m * g / k * (Mathf.Exp(2f * k * liftHeight / m) - 1f));
                rb.linearVelocity = flatV + Vector3.up * vy;
                rb.angularVelocity = Vector3.zero;
                p.combo?.AddAction("ENTFUEHRT", 500f);
            }
            else
            {
                var s = p.skater;
                var rb = s.GetComponent<Rigidbody>();
                // im Grind ist der Koerper kinematisch: dann Tempo aus Fahrtrichtung und Grind-Tempo
                Vector3 flatV = rb != null && !rb.isKinematic ? Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up)
                                                               : Quaternion.Euler(0f, s.Heading, 0f) * Vector3.forward * s.Speed;
                float vy = Mathf.Sqrt(2f * SkaterController.GravityAccel * liftHeight);
                if (!s.Geyser(flatV + Vector3.up * vy, "ENTFUEHRT", 500f)) return false;
            }
            BurstPop.Show("ENTFUEHRT!", Palette.Lime, new Vector2(0f, 220f), 200f, 1.4f);
            return true;
        }

        void UpdateBeam(Vector3 p, float on, float t)
        {
            _beamOn = on;
            _beamGround = new Vector3(p.x, 0f, p.z);
            if (beam == null) return;
            bool active = on > 0.01f;
            if (beam.gameObject.activeSelf != active) beam.gameObject.SetActive(active);
            if (groundGlow != null && groundGlow.gameObject.activeSelf != active) groundGlow.gameObject.SetActive(active);
            if (!active) return;

            // Strahl reicht bis zum Boden (oder Dach) unter dem UFO
            Vector3 top = beam.position;
            float dist = 60f;
            Vector3 ground = top + Vector3.down * dist;
            if (Physics.Raycast(top, Vector3.down, out var hit, 150f, ~0, QueryTriggerInteraction.Ignore))
            {
                dist = hit.distance;
                ground = hit.point;
            }
            _beamGround = ground;
            // Strahl-Mesh ist im Visual-Raum 1 lang (nach unten), das Visual ist skaliert
            float s = Mathf.Max(0.01f, beam.parent.lossyScale.x);
            beam.localScale = new Vector3(1f, dist / Mathf.Max(0.01f, beam.parent.lossyScale.y), 1f);
            float flicker = 0.9f + 0.1f * Mathf.Sin(t * 13f) * Mathf.Sin(t * 7.3f);
            if (_beamMat != null)
            {
                var c = _beamColor;
                c.a *= on * flicker;
                _beamMat.SetColor("_BaseColor", c);
            }
            if (groundGlow != null)
            {
                groundGlow.position = ground + Vector3.up * 0.06f;
                groundGlow.rotation = Quaternion.identity;
                float r = 6.5f * s * (1f + 0.04f * Mathf.Sin(t * 1.7f));
                groundGlow.localScale = new Vector3(r * 2.4f, 1f, r * 2.4f);
                if (_glowMat != null)
                {
                    var c = _glowColor;
                    c.a *= on * flicker;
                    _glowMat.SetColor("_BaseColor", c);
                }
            }
        }

        static float EaseOutBack(float x)
        {
            if (x <= 0f) return 0f;
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float y = x - 1f;
            return 1f + c3 * y * y * y + c1 * y * y;
        }

        /// <summary>Unheimliches Brummen mit leichtem Jaulen (Theremin), endlos schleifbar.</summary>
        static AudioClip MakeHumClip()
        {
            const int rate = 44100, seconds = 4;
            int len = rate * seconds;
            var data = new float[len];
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)rate;
                // Alle Frequenzen ganzzahlig pro 4 s, damit die Schleife nahtlos ist
                float hum = Mathf.Sin(2f * Mathf.PI * 62f * t + 1.5f * Mathf.Sin(2f * Mathf.PI * 0.5f * t)) * 0.55f
                          + Mathf.Sin(2f * Mathf.PI * 124f * t) * 0.2f;
                float whine = Mathf.Sin(2f * Mathf.PI * 660f * t + 18f * Mathf.Sin(2f * Mathf.PI * 1.5f * t)) * 0.07f;
                float swell = 0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * 0.25f * t);
                data[i] = (hum + whine) * swell * 0.5f;
            }
            var clip = AudioClip.Create("UfoHum", len, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void OnDrawGizmosSelected()
        {
            if (tour == null || tour.Length == 0) return;
            if (!_hasCrane) FindCrane();
            Gizmos.color = Color.cyan;
            for (int k = 0; k < tour.Length; k++)
            {
                Vector3 a = Waypoint(k), b = Waypoint(k + 1);
                a.y = Height(a);
                b.y = Height(b);
                Gizmos.DrawWireSphere(a, 6f);
                Gizmos.DrawLine(a, b);
            }
        }
    }
}
