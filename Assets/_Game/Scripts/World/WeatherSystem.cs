using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Dynamischer Regen und Gewitter in der Stadt: Regenwolken ziehen auf (Himmel grau, Licht schwaecher, Nebel dichter),
    /// dann Regen mit Tropfen-Streifen, Spritzern und Rauschen; der Boden wird nass und spiegelt (PlanarReflection).
    /// Bei Gewitter schlagen Blitze am Horizont ein (Strahl, Himmel und Stadt flackern hell, Donner je nach Entfernung
    /// verzoegert). Modus aus dem Spielstand: "klar", "dynamisch", "regen" oder "gewitter".
    /// Online bestimmt der Host das Wetter und schickt es an alle Clients (Zustand alle 2 s, jeder Blitz sofort).
    /// </summary>
    public class WeatherSystem : MonoBehaviour
    {
        public static WeatherSystem Instance { get; private set; }

        /// <summary>Regenstaerke 0..1 (Wolken, Tropfen, Licht).</summary>
        public static float Rain => Instance != null ? Instance._rain : 0f;
        /// <summary>Naesse des Bodens 0..1 (kommt etwas nach dem Regen, trocknet langsam).</summary>
        public static float Wetness => Instance != null ? Instance._wet : 0f;
        /// <summary>Gewitter aktiv (Blitze, solange es kraeftig regnet).</summary>
        public static bool Storm => Instance != null && Instance._storm;
        /// <summary>Anzahl der Blitze seit dem Laden der Stadt (fuer Tests, auch vom Host empfangene).</summary>
        public static int Strikes { get; private set; }
        /// <summary>Zahl der vom Host empfangenen Wetter-Nachrichten (fuer Tests).</summary>
        public static int SyncMessages { get; private set; }

        const float BuildUp = 25f, WetUp = 15f, DryDown = 70f;
        const string MsgState = "DS_Weather", MsgStrike = "DS_Lightning";

        float _rain, _wet, _target, _timer, _nextStrike, _flash, _sendTimer, _boltTime;
        bool _storm, _registered;
        SkyPreset _preset;
        Material _sky;
        Light _sun;
        ParticleSystem _drops, _splashes;
        AudioSource _audio, _thunderSource;
        AudioClip[] _thunder;
        LineRenderer _bolt;
        Vector3 _flashDir = Vector3.right;
        Transform _follow;

        void Awake()
        {
            Instance = this;
            Strikes = 0;
            SyncMessages = 0;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Shader.SetGlobalFloat("_DS_Wet", 0f);
            var nm = NetworkManager.Singleton;
            if (_registered && nm != null)
            {
                if (nm.CustomMessagingManager != null)
                {
                    nm.CustomMessagingManager.UnregisterNamedMessageHandler(MsgState);
                    nm.CustomMessagingManager.UnregisterNamedMessageHandler(MsgStrike);
                }
                nm.OnClientConnectedCallback -= OnClientConnected;
            }
        }

        void Start()
        {
            Init();
            string mode = SaveSystem.Profile.weather;
            if (mode == "regen" || mode == "gewitter") _rain = _wet = _target = 1f;
            _storm = mode == "gewitter";
            // Dynamisch: erst trocken, der erste Schauer kommt nach gut einer Minute
            _timer = mode == "dynamisch" ? Random.Range(50f, 90f) : float.MaxValue;
            _nextStrike = Random.Range(3f, 6f);
            Apply();
        }

        void Init()
        {
            _preset = SkyLook.Find(SaveSystem.Profile != null ? SaveSystem.Profile.sky : null);
            _sky = RenderSettings.skybox;
            _sun = RenderSettings.sun;
            if (_sun == null)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { _sun = l; break; }
            if (_drops == null) BuildParticles();
            if (_bolt == null) BuildBolt();
            if (_audio == null && !Application.isBatchMode)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.clip = MakeRainClip();
                _audio.loop = true;
                _audio.spatialBlend = 0f;
                _audio.volume = 0f;
                _audio.Play();
                _thunderSource = gameObject.AddComponent<AudioSource>();
                _thunderSource.spatialBlend = 0f;
                _thunder = new[] { MakeThunderClip(11), MakeThunderClip(29), MakeThunderClip(47) };
            }
        }

        /// <summary>Fuer Screenshots und Tests: Regen und Naesse direkt setzen.</summary>
        public void SetInstant(float rain, float wet)
        {
            Init();
            _rain = _target = rain;
            _wet = wet;
            _timer = float.MaxValue;
            Apply();
            if (_drops != null) { _drops.Simulate(1.5f, true, true); _splashes.Simulate(0.4f, true, true); }
        }

        /// <summary>Fuer Screenshots: Blitz in Richtung (Grad) und Entfernung sofort, in voller Helligkeit.</summary>
        public void StrikeForScreenshot(Transform cam, float bearing, float distance, int seed)
        {
            Init();
            _follow = cam;
            Strike(bearing, distance, seed);
            _flash = 1f;
            Apply();
        }

        /// <summary>Online folgt nur ein reiner Client dem Host; Host und Solo bestimmen das Wetter selbst.</summary>
        static bool FollowsHost
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening && nm.IsClient && !nm.IsServer;
            }
        }

        static bool HostWithClients
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening && nm.IsServer && nm.ConnectedClientsIds.Count > 1;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            RegisterMessages();
            if (!FollowsHost)
            {
                if (SaveSystem.Profile.weather == "dynamisch")
                {
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        bool starting = _target < 0.5f;
                        _target = starting ? Random.Range(0.65f, 1f) : 0f;
                        _storm = starting && Random.value < 0.35f; // etwa jeder dritte Schauer wird ein Gewitter
                        _timer = starting ? Random.Range(70f, 140f) : Random.Range(110f, 220f);
                        SendState();
                    }
                }
                // Blitze bestimmt der Host bzw. das Solo-Spiel
                if (_storm && _rain > 0.7f)
                {
                    _nextStrike -= dt;
                    if (_nextStrike <= 0f)
                    {
                        _nextStrike = AutoTest.Enabled ? Random.Range(3f, 5f) : Random.Range(6f, 16f);
                        float bearing = Random.Range(0f, 360f), distance = Random.Range(250f, 520f);
                        int seed = Random.Range(0, int.MaxValue);
                        Strike(bearing, distance, seed);
                        SendStrike(bearing, distance, seed);
                    }
                }
                _sendTimer -= dt;
                if (_sendTimer <= 0f) { _sendTimer = 2f; SendState(); }
            }
            _rain = Mathf.MoveTowards(_rain, _target, dt / BuildUp);
            float wetTarget = _rain > 0.35f ? 1f : 0f;
            _wet = Mathf.MoveTowards(_wet, wetTarget, dt / (wetTarget > _wet ? WetUp : DryDown));
            UpdateLightning(dt);
            Apply();
        }

        void LateUpdate()
        {
            if (_follow == null && Camera.main != null) _follow = Camera.main.transform;
            if (_follow != null) Follow(_follow);
        }

        /// <summary>Regen und Spritzer um die Kamera herum halten (vor ihr etwas mehr).</summary>
        public void Follow(Transform cam)
        {
            Vector3 p = cam.position;
            _drops.transform.position = new Vector3(p.x, p.y + 16f, p.z) + Vector3.ProjectOnPlane(cam.forward, Vector3.up) * 10f;
            _splashes.transform.position = new Vector3(p.x, 0.14f, p.z) + Vector3.ProjectOnPlane(cam.forward, Vector3.up) * 8f;
        }

        void Apply()
        {
            float r = _rain, f = _flash;
            if (_sky != null && _sky.HasProperty("_Overcast"))
            {
                _sky.SetFloat("_Overcast", r);
                _sky.SetFloat("_Flash", f * 0.55f);
                _sky.SetVector("_FlashDir", _flashDir);
            }
            if (_sun != null)
            {
                _sun.intensity = _preset.sunIntensity * Mathf.Lerp(1f, 0.4f, r) + f * 0.7f;
                _sun.color = Color.Lerp(_preset.sunLight, new Color(0.8f, 0.85f, 1f), Mathf.Clamp01(f * 1.5f));
            }
            Color grey = Color.Lerp(_preset.horizon, new Color(0.45f, 0.48f, 0.58f) * Mathf.Lerp(1f, 0.45f, _preset.darken), 0.65f);
            RenderSettings.fogColor = Color.Lerp(Color.Lerp(_preset.horizon, grey, r), new Color(0.62f, 0.66f, 0.85f), f * 0.22f);
            RenderSettings.fogStartDistance = Mathf.Lerp(_preset.fogStart, 40f, r);
            RenderSettings.fogEndDistance = Mathf.Lerp(_preset.fogEnd, 380f, r);
            float darken = Mathf.Lerp(_preset.darken, Mathf.Max(_preset.darken, 0.3f), r);
            Shader.SetGlobalFloat("_DS_Darken", Mathf.Lerp(darken, 0f, Mathf.Clamp01(f) * 0.6f));
            Shader.SetGlobalFloat("_DS_Wet", _wet);

            if (_drops != null)
            {
                var e = _drops.emission;
                e.rateOverTime = r * (_storm ? 5500f : 4000f);
                var s = _splashes.emission;
                s.rateOverTime = r * 700f;
            }
            if (_audio != null) _audio.volume = r * 0.55f * SaveSystem.Profile.sfxVolume;
        }

        // ------------------------------------------------------------------ Blitz und Donner

        /// <summary>Ein Einschlag: Strahl am Horizont (Richtung in Grad, Entfernung in m), Flackern, Donner verzoegert.</summary>
        void Strike(float bearing, float distance, int seed)
        {
            Strikes++;
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            Vector3 center = _follow != null ? _follow.position : Vector3.zero;
            Vector3 dir = Quaternion.Euler(0f, bearing, 0f) * Vector3.forward;
            Vector3 ground = new Vector3(center.x, 0f, center.z) + dir * distance;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            _flashDir = (dir + Vector3.up * 0.35f).normalized;

            // Gezackter Hauptstrahl von den Wolken bis zum Boden
            const int segments = 16;
            var main = new Vector3[segments + 1];
            Vector3 top = ground + Vector3.up * R(140f, 190f) + side * R(-25f, 25f);
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                main[i] = Vector3.Lerp(top, ground, t) + side * R(-9f, 9f) * Mathf.Sin(t * Mathf.PI) + dir * R(-6f, 6f) * t;
            }
            // Verzweigung bei 40 %: zur Seite hinaus und wieder zurueck (eine durchgehende Linie)
            int fork = segments * 2 / 5;
            float sign = R(0f, 1f) < 0.5f ? -1f : 1f;
            var points = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i <= fork; i++) points.Add(main[i]);
            Vector3 b = main[fork];
            var branch = new Vector3[3];
            for (int k = 0; k < 3; k++) branch[k] = b + side * sign * ((k + 1) * R(8f, 14f)) + Vector3.down * ((k + 1) * R(12f, 20f));
            points.AddRange(branch);
            points.Add(branch[1]);
            points.Add(branch[0]);
            points.Add(b);
            for (int i = fork + 1; i <= segments; i++) points.Add(main[i]);
            _bolt.positionCount = points.Count;
            _bolt.SetPositions(points.ToArray());
            _bolt.widthMultiplier = R(1.6f, 2.6f) * (distance / 350f);
            _bolt.enabled = true;
            _boltTime = 0f;

            if (_thunderSource != null && _thunder != null)
            {
                _thunderSource.clip = _thunder[rng.Next(_thunder.Length)];
                _thunderSource.volume = Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(250f, 520f, distance)) * SaveSystem.Profile.sfxVolume;
                _thunderSource.PlayDelayed(distance / 343f); // Schall braucht ~1 s pro 343 m
            }
        }

        /// <summary>Flackern: drei helle Spitzen in den ersten 0,4 s, solange ist auch der Strahl zu sehen.</summary>
        void UpdateLightning(float dt)
        {
            if (_bolt == null || !_bolt.enabled) { _flash = Mathf.MoveTowards(_flash, 0f, dt * 6f); return; }
            _boltTime += dt;
            float t = _boltTime;
            float pulse = t < 0.08f ? 1f : t < 0.14f ? 0.25f : t < 0.24f ? 0.85f : t < 0.3f ? 0.2f : t < 0.4f ? 0.55f : 0f;
            _flash = pulse;
            _bolt.startColor = _bolt.endColor = new Color(0.9f, 0.92f, 1f, Mathf.Clamp01(pulse * 1.2f));
            if (t > 0.45f) _bolt.enabled = false;
        }

        void BuildBolt()
        {
            var go = new GameObject("Lightning");
            go.transform.SetParent(transform, false);
            _bolt = go.AddComponent<LineRenderer>();
            _bolt.useWorldSpace = true;
            _bolt.numCornerVertices = 2;
            _bolt.alignment = LineAlignment.View;
            _bolt.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _bolt.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.55f));
            var baseMat = Resources.Load<Material>("TrailMat");
            if (baseMat != null)
            {
                // Ueberhell (HDR), damit der Bloom einen Schein drumherum legt
                var m = new Material(baseMat) { name = "LightningBolt" };
                m.SetColor("_BaseColor", new Color(3.2f, 3.3f, 4.2f, 1f));
                m.SetFloat("_FogAmount", 0.15f); // durch den Regennebel hindurch sichtbar
                _bolt.sharedMaterial = m;
            }
            _bolt.enabled = false;
        }

        // ------------------------------------------------------------------ Netzwerk

        void RegisterMessages()
        {
            if (_registered) return;
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || nm.CustomMessagingManager == null) return;
            _registered = true;
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgState, OnState);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgStrike, OnStrike);
            nm.OnClientConnectedCallback += OnClientConnected;
        }

        /// <summary>Neuer Spieler: sofort den aktuellen Stand schicken (Regen, Naesse, Gewitter).</summary>
        void OnClientConnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsServer && clientId != NetworkManager.ServerClientId) SendState(clientId);
        }

        void SendState(ulong? to = null)
        {
            var nm = NetworkManager.Singleton;
            if (!_registered || nm == null || !nm.IsServer || (to == null && !HostWithClients)) return;
            using var w = new FastBufferWriter(32, Allocator.Temp);
            w.WriteValueSafe(_target);
            w.WriteValueSafe(_rain);
            w.WriteValueSafe(_wet);
            w.WriteValueSafe(_storm);
            if (to.HasValue) nm.CustomMessagingManager.SendNamedMessage(MsgState, to.Value, w, NetworkDelivery.Reliable);
            else nm.CustomMessagingManager.SendNamedMessageToAll(MsgState, w, NetworkDelivery.Reliable);
        }

        void SendStrike(float bearing, float distance, int seed)
        {
            var nm = NetworkManager.Singleton;
            if (!_registered || nm == null || !HostWithClients) return;
            using var w = new FastBufferWriter(16, Allocator.Temp);
            w.WriteValueSafe(bearing);
            w.WriteValueSafe(distance);
            w.WriteValueSafe(seed);
            nm.CustomMessagingManager.SendNamedMessageToAll(MsgStrike, w, NetworkDelivery.Reliable);
        }

        void OnState(ulong sender, FastBufferReader r)
        {
            if (!FollowsHost) return;
            r.ReadValueSafe(out float target);
            r.ReadValueSafe(out float rain);
            r.ReadValueSafe(out float wet);
            r.ReadValueSafe(out bool storm);
            SyncMessages++;
            _target = target;
            _storm = storm;
            // Beim Beitreten (oder nach Aussetzern) direkt auf den Stand des Hosts springen, sonst weich folgen
            if (Mathf.Abs(_rain - rain) > 0.25f) _rain = rain;
            if (Mathf.Abs(_wet - wet) > 0.25f) _wet = wet;
        }

        void OnStrike(ulong sender, FastBufferReader r)
        {
            if (!FollowsHost) return;
            r.ReadValueSafe(out float bearing);
            r.ReadValueSafe(out float distance);
            r.ReadValueSafe(out int seed);
            Strike(bearing, distance, seed);
        }

        // ------------------------------------------------------------------ Partikel

        void BuildParticles()
        {
            var baseMat = Resources.Load<Material>("TrailMat");

            // Tropfen: lange, duenne Streifen, fallen schraeg mit etwas Wind
            _drops = new GameObject("RainDrops").AddComponent<ParticleSystem>();
            _drops.transform.SetParent(transform, false);
            _drops.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _drops.main;
            main.loop = true;
            main.startLifetime = 1.3f;
            main.startSpeed = 0f;
            main.startSize = 0.035f;
            main.startColor = new Color(0.8f, 0.86f, 1f, 0.45f);
            main.maxParticles = 9000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shape = _drops.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(60f, 1f, 60f);
            var vel = _drops.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            vel.y = new ParticleSystem.MinMaxCurve(-26f, -22f);
            vel.z = new ParticleSystem.MinMaxCurve(0.5f, 1f);
            var em = _drops.emission;
            em.rateOverTime = 0f;
            var rend = _drops.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.045f;
            rend.lengthScale = 1f;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (baseMat != null) rend.sharedMaterial = new Material(baseMat) { name = "RainDrop" };
            _drops.Play();

            // Spritzer: kleine, flach liegende Ringe auf dem Boden rund um die Kamera
            _splashes = new GameObject("RainSplashes").AddComponent<ParticleSystem>();
            _splashes.transform.SetParent(transform, false);
            _splashes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var sm = _splashes.main;
            sm.loop = true;
            sm.startLifetime = 0.3f;
            sm.startSpeed = 0f;
            sm.startSize = 0.3f;
            sm.startColor = new Color(0.85f, 0.9f, 1f, 0.55f);
            sm.maxParticles = 2000;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            var ss = _splashes.shape;
            ss.shapeType = ParticleSystemShapeType.Box;
            ss.scale = new Vector3(40f, 0f, 40f);
            var size = _splashes.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f));
            var col = _splashes.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var sem = _splashes.emission;
            sem.rateOverTime = 0f;
            var srend = _splashes.GetComponent<ParticleSystemRenderer>();
            srend.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            srend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (baseMat != null)
            {
                var m = new Material(baseMat) { name = "RainSplash" };
                m.SetTexture("_BaseMap", MakeRingTexture());
                srend.sharedMaterial = m;
            }
            _splashes.Play();
        }

        static Texture2D MakeRingTexture()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "RainRing" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) * 9f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        // ------------------------------------------------------------------ Geraeusche

        /// <summary>Regenrauschen aus gefiltertem Rauschen und einzelnen Tropfen-Klicks (keine Audiodatei noetig).</summary>
        static AudioClip MakeRainClip()
        {
            const int rate = 44100, seconds = 4;
            int len = rate * seconds;
            var data = new float[len];
            var rng = new System.Random(77);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < len; i++)
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                lp += (white - lp) * 0.25f;
                lp2 += (lp - lp2) * 0.08f;
                data[i] = lp * 0.35f + lp2 * 0.6f;
            }
            // Tropfen: kurze, abklingende Klicks
            for (int k = 0; k < 900; k++)
            {
                int start = rng.Next(len);
                float amp = 0.15f + (float)rng.NextDouble() * 0.35f, freq = 1800f + (float)rng.NextDouble() * 2600f;
                for (int j = 0; j < 400; j++)
                {
                    int idx = (start + j) % len;
                    data[idx] += Mathf.Sin(j * freq * 2f * Mathf.PI / rate) * amp * Mathf.Exp(-j / 60f);
                }
            }
            // Anfang und Ende angleichen, damit die Schleife nicht knackt
            const int fade = 2000;
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                data[i] = Mathf.Lerp(data[len - fade + i], data[i], t);
            }
            for (int i = 0; i < len; i++) data[i] = Mathf.Clamp(data[i] * 0.6f, -1f, 1f);
            var clip = AudioClip.Create("Rain", len, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Donner: harter Knall am Anfang, dann tiefes, an- und abschwellendes Grollen (ohne Audiodatei).</summary>
        static AudioClip MakeThunderClip(int seed)
        {
            const int rate = 22050;
            int len = rate * 5;
            var data = new float[len];
            var rng = new System.Random(seed);
            float brown = 0f, lp = 0f, peak = 0f;
            float swell1 = 0.6f + (float)rng.NextDouble(), swell2 = 1.6f + (float)rng.NextDouble() * 1.2f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)rate;
                float white = (float)rng.NextDouble() * 2f - 1f;
                brown = Mathf.Clamp(brown + white * 0.06f, -1f, 1f) * 0.997f;
                lp += (white - lp) * 0.3f;
                float crack = t < 0.18f ? lp * Mathf.Exp(-t / 0.05f) * 1.6f : 0f;
                float swell = 0.6f + 0.5f * Mathf.Exp(-Mathf.Pow((t - swell1) * 2.2f, 2f)) + 0.4f * Mathf.Exp(-Mathf.Pow((t - swell2) * 1.6f, 2f));
                float rumble = brown * Mathf.Exp(-t / 1.7f) * swell;
                data[i] = (crack + rumble * 2.4f) * Mathf.Clamp01(t / 0.02f);
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            float norm = peak > 0f ? 0.9f / peak : 1f;
            for (int i = 0; i < len; i++) data[i] *= norm;
            var clip = AudioClip.Create("Thunder" + seed, len, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
