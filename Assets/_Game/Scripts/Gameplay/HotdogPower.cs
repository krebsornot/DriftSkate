using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace DriftSkate
{
    /// <summary>
    /// Hotdog von Bodo oder Udo essen (H / linken Stick druecken): die Figur fuehrt ihn zum Mund, zwei Bissen, dann
    /// "Hotdog-Power". Erst ein kurzer Schub nach vorn, danach 30 s mehr Tempo (Pushen, Ducken, Rennen, Motor), mehrere
    /// Hotdogs verlaengern bis 90 s. Dazu ein warmer Rand im Bild und eine Anzeige rechts oben (Hotdogs im Rucksack,
    /// Restzeit). Nur lokal: jeder simuliert sein Auto und seinen Skater selbst, das Tempo kommt so auch online an.
    /// </summary>
    [DefaultExecutionOrder(220)] // LateUpdate nach dem Skater-Rig (Hotdog an die fertig posierte Hand)
    public class HotdogPower : MonoBehaviour
    {
        public static HotdogPower Instance { get; private set; }
        public static readonly Color MustardColor = Palette.Hex("FFB627");
        public const int MaxHotdogs = 3;
        public const float Duration = 30f, MaxTime = 90f;
        /// <summary>Ablauf beim Essen (s): Hand hoch, zwei Bissen, Power, Hand runter.</summary>
        const float EatTime = 1.5f, BiteA = 0.55f, BiteB = 0.9f, PowerAt = 1.0f, RushTime = 1.2f;

        public static bool Active => Instance != null && Instance._left > 0f;
        public static float TimeLeft => Instance != null ? Instance._left : 0f;
        public static bool Eating => Instance != null && Instance._eatT >= 0f;
        /// <summary>Hoechsttempo und Anschub beim Skaten (1 = normal).</summary>
        public static float SkateFactor => Active ? 1.3f : 1f;
        /// <summary>Gehen und Rennen (1 = normal).</summary>
        public static float WalkFactor => Active ? 1.25f : 1f;
        /// <summary>Motor-Drehmoment (1 = normal).</summary>
        public static float TorqueFactor => Active ? 1.3f : 1f;
        /// <summary>Zusaetzliche Beschleunigung direkt nach dem Essen (m/s², klingt nach gut einer Sekunde ab).</summary>
        public static float SkateRush => Rush * 7f;
        public static float CarRush => Rush * 6f;
        static float Rush => Instance != null && Instance._rush > 0f ? Mathf.SmoothStep(0f, 1f, Instance._rush / RushTime) : 0f;

        /// <summary>Gilt das fuer dieses Auto (nur das eigene)?</summary>
        public static bool IsMine(VehicleController car) => car != null && PlayerAvatar.Local != null && PlayerAvatar.Local.car == car;

        float _left, _rush, _weight, _hintCooldown, _eatT = -1f;
        bool _powered, _bitA, _bitB;
        Volume _volume;
        Vignette _vignette;
        ColorAdjustments _color;
        RectTransform _box;
        Text _count, _timer;
        Transform _held;
        ParticleSystem _crumbs;
        AudioSource _audio;
        static AudioClip _bite;

        public static void Ensure()
        {
            if (Instance == null) new GameObject("HotdogPower").AddComponent<HotdogPower>();
        }

        void Awake()
        {
            Instance = this;
            BuildVolume();
            BuildUi();
            _held = BuildHotdog("HotdogInHand").transform;
            _held.SetParent(transform, false);
            _held.gameObject.SetActive(false);
            _crumbs = BuildCrumbs();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_volume != null && _volume.profile != null) Destroy(_volume.profile);
            if (_box != null) Destroy(_box.gameObject);
        }

        // ------------------------------------------------------------------ Essen

        /// <summary>Einen Hotdog essen (auch fuer den Autotest). false, wenn keiner da ist oder gerade gegessen wird.</summary>
        public bool Eat()
        {
            var p = SaveSystem.Profile;
            if (p.hotdogs <= 0 || _eatT >= 0f) return false;
            var a = PlayerAvatar.Local;
            if (a != null && a.Mode != PlayerMode.Driving && a.skater.State == SkaterState.Bailed) return false;
            p.hotdogs--;
            SaveSystem.Save();
            _eatT = 0f;
            _powered = _bitA = _bitB = false;
            return true;
        }

        void Update()
        {
            var a = PlayerAvatar.Local;
            bool paused = HUD.Instance != null && HUD.Instance.Paused;
            if (a != null && GameInput.Pressed(GameInput.Eat) && !QuestNpc.AnyTalking && !Eat() && SaveSystem.Profile.hotdogs <= 0 &&
                _eatT < 0f && Time.time > _hintCooldown)
            {
                _hintCooldown = Time.time + 3f;
                HUD.Instance?.Toast("Keine Hotdogs. Bodo und Udo verkaufen welche an den Imbisswagen in den Parks.", 3.5f);
            }

            if (_eatT >= 0f && !paused)
            {
                _eatT += Time.deltaTime;
                if (!_bitA && _eatT >= BiteA) { _bitA = true; Bite(a); }
                if (!_bitB && _eatT >= BiteB) { _bitB = true; Bite(a); }
                if (!_powered && _eatT >= PowerAt) { _powered = true; PowerUp(); }
                if (_eatT >= EatTime) _eatT = -1f;
            }
            // Haltung fuer das Rig (es posiert danach in seinem LateUpdate): Hand hoch, essen, Hand runter
            var rig = a != null && a.skater != null ? a.skater.rig : null;
            if (rig != null)
                rig.eat = _eatT >= 0f && a.Mode != PlayerMode.Driving ? Mathf.SmoothStep(0f, 1f, Mathf.Min(_eatT / 0.35f, (EatTime - _eatT) / 0.35f)) : 0f;
            if (!paused)
            {
                _left = Mathf.Max(0f, _left - Time.deltaTime);
                _rush = Mathf.Max(0f, _rush - Time.deltaTime);
            }

            // Bild: warmer Senf-Rand, langsam pulsierend, die letzten Sekunden klingt er aus
            float want = Mathf.Clamp01(_left / 4f) * 0.75f + (_rush > 0f ? 0.25f * _rush / RushTime : 0f);
            _weight = Mathf.MoveTowards(_weight, want, Time.deltaTime / 1.5f);
            if (_volume != null)
            {
                _volume.weight = _weight;
                _volume.enabled = _weight > 0.001f;
                _vignette.intensity.value = 0.26f + Mathf.Sin(Time.time * 1.1f) * 0.04f;
            }

            bool hud = a != null && !Admin.HideHud && !HUD.Cinematic;
            int dogs = SaveSystem.Profile.hotdogs;
            bool show = hud && (dogs > 0 || _left > 0f);
            _box.gameObject.SetActive(show);
            if (show)
            {
                // unter dem Chill-Kasten, wenn der auch da ist
                bool chillBox = SaveSystem.Profile.baggies > 0 || Chill.Active;
                _box.anchoredPosition = new Vector2(-30f, chillBox ? -244f : -160f);
                _count.text = dogs + (dogs == 1 ? " HOTDOG  [H]" : " HOTDOGS  [H]");
                _timer.text = _left > 0f ? "POWER " + Mathf.FloorToInt(_left / 60f) + ":" + (Mathf.CeilToInt(_left) % 60).ToString("00") : "";
            }
        }

        void PowerUp()
        {
            _left = Mathf.Min(_left + Duration, MaxTime);
            _rush = RushTime;
            BurstPop.Show("HOTDOG-POWER!", MustardColor, new Vector2(0f, 260f), 250f, 1.8f);
            HUD.Instance?.Toast("Mehr Tempo auf dem Board und im Auto  (" + Mathf.CeilToInt(_left) + " s)", 3f);
            var a = PlayerAvatar.Local;
            if (a != null && a.Mode != PlayerMode.Driving && a.combo != null && a.combo.Active) a.combo.AddAction("HOTDOG", 100f);
        }

        void Bite(PlayerAvatar a)
        {
            if (_bite == null) _bite = MakeBiteClip();
            _audio.pitch = Random.Range(0.9f, 1.1f);
            _audio.PlayOneShot(_bite, 0.55f * SaveSystem.Profile.sfxVolume);
            if (a == null || a.Mode == PlayerMode.Driving || !_held.gameObject.activeSelf) return;
            var e = new ParticleSystem.EmitParams();
            Vector3 mouth = _held.position + _held.forward * 0.08f;
            for (int i = 0; i < 6; i++)
            {
                e.position = mouth + Random.insideUnitSphere * 0.02f;
                e.velocity = Random.insideUnitSphere * 0.5f + Vector3.down * 0.3f;
                e.startSize = Random.Range(0.012f, 0.022f);
                e.startLifetime = Random.Range(0.5f, 0.8f);
                e.startColor = i % 3 == 0 ? Palette.Hex("F2B632") : Palette.Hex("E8B07A");
                _crumbs.Emit(e, 1);
            }
        }

        /// <summary>Hotdog in der Hand: vom Griff Richtung Mund, wird mit jedem Bissen kuerzer. Laeuft nach dem Rig.</summary>
        void LateUpdate()
        {
            var a = PlayerAvatar.Local;
            var rig = a != null && a.skater != null ? a.skater.rig : null;
            bool show = rig != null && rig.Ready && a.Mode != PlayerMode.Driving && a.skater.State != SkaterState.Hidden &&
                        _eatT >= 0f && _eatT < EatTime - 0.25f;
            if (_held.gameObject.activeSelf != show) _held.gameObject.SetActive(show);
            if (!show) return;
            Vector3 grip = rig.EatGrip;
            Vector3 toMouth = rig.Mouth - grip;
            float reach = toMouth.magnitude;
            Vector3 dir = Vector3.Slerp(rig.EatHandForward, toMouth / Mathf.Max(1e-4f, reach), rig.eat).normalized;
            // Vorne endet er am Mund (nicht im Kopf), mit jedem Bissen ein Stueck kuerzer
            float bitten = _bitB ? 0.55f : _bitA ? 0.78f : 1f;
            float len = 0.18f * bitten;
            if (rig.eat > 0.5f) len = Mathf.Min(len, Mathf.Max(0.05f, reach - 0.01f));
            _held.SetPositionAndRotation(grip + dir * len * 0.5f, Quaternion.LookRotation(dir, Vector3.up));
            _held.localScale = new Vector3(1f, 1f, len / 0.18f);
        }

        /// <summary>Fuer Tests: Power und laufendes Essen sofort beenden.</summary>
        public void ResetForTest()
        {
            _left = _rush = 0f;
            _eatT = -1f;
        }

        // ------------------------------------------------------------------ Hotdog-Modell (auch fuer Bodo und Udo)

        /// <summary>Hotdog aus Grundformen: Broetchen, Wuerstchen an beiden Enden sichtbar, Senf-Zickzack. Vorn = +Z, 18 cm lang.</summary>
        public static GameObject BuildHotdog(string name)
        {
            var root = new GameObject(name);
            var t = root.transform;
            Color bun = Palette.Hex("E8B07A"), wurst = Palette.Hex("B8483A"), senf = Palette.Hex("F2B632");
            foreach (float x in new[] { -0.017f, 0.017f })
                Part(PrimitiveType.Capsule, t, new Vector3(x, 0f, 0f), new Vector3(0.034f, 0.08f, 0.03f), bun, new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Capsule, t, new Vector3(0f, 0.012f, 0f), new Vector3(0.026f, 0.098f, 0.026f), wurst, new Vector3(90f, 0f, 0f));
            for (int i = 0; i < 5; i++)
            {
                float z = -0.056f + i * 0.028f;
                Part(PrimitiveType.Cube, t, new Vector3(0f, 0.026f, z), new Vector3(0.026f, 0.005f, 0.006f), senf,
                     new Vector3(0f, i % 2 == 0 ? 35f : -35f, 0f));
            }
            return root;
        }

        static void Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 size, Color color, Vector3 euler)
        {
            var go = Shapes.Part(type, parent, pos, size, color, 0f, euler);
            go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ Aufbau

        ParticleSystem BuildCrumbs()
        {
            var go = new GameObject("Crumbs");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1.2f;
            main.maxParticles = 40;
            main.startSpeed = 0f;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Resources.Load<Material>("SmokeMat"); // nimmt die Partikelfarbe an
            r.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        void BuildVolume()
        {
            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 49f;
            _volume.weight = 0f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _color = profile.Add<ColorAdjustments>(true);
            _color.saturation.Override(14f);
            _vignette = profile.Add<Vignette>(true);
            _vignette.color.Override(new Color(0.95f, 0.55f, 0.1f));
            _vignette.intensity.Override(0.26f);
            _vignette.smoothness.Override(0.75f);
            _volume.profile = profile;
            _volume.enabled = false;
        }

        void BuildUi()
        {
            var panel = UIFactory.Panel(SpeechBubble.Layer, "HotdogPower", new Color(0.09f, 0.07f, 0.12f, 0.88f), Vector2.one, Vector2.one, Vector2.one,
                                        new Vector2(-30f, -160f), new Vector2(330f, 74f), -1.5f);
            _box = panel.rectTransform;
            _count = UIFactory.LabelAt(_box, "", 26, MustardColor, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16f, -6f), new Vector2(300f, 34f));
            _timer = UIFactory.LabelAt(_box, "", 24, Palette.White, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16f, -38f), new Vector2(300f, 30f));
            _box.gameObject.SetActive(false);
        }

        /// <summary>Biss ohne Audiodatei: kurzes, dumpfes Knirschen aus gefiltertem Rauschen.</summary>
        static AudioClip MakeBiteClip()
        {
            const int rate = 44100;
            int n = Mathf.RoundToInt(0.16f * rate);
            var data = new float[n];
            var rng = new System.Random(4711);
            float lp = 0f, lp2 = 0f;
            float a = 2f * Mathf.PI * 1800f / rate / (1f + 2f * Mathf.PI * 1800f / rate);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float x = (float)rng.NextDouble() * 2f - 1f;
                // Knirschen: Rauschen in kleinen Koernern, die schnell leiser werden
                float grain = (rng.NextDouble() < 0.12 ? 1f : 0.25f);
                lp += a * (x * grain - lp);
                lp2 += a * (lp - lp2);
                float env = Mathf.Min(1f, t / 0.006f) * Mathf.Exp(-t / 0.05f);
                data[i] = lp2 * env * 2.2f;
            }
            var clip = AudioClip.Create("HotdogBite", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
