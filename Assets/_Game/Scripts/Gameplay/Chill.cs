using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace DriftSkate
{
    /// <summary>
    /// Tuete von Nix rauchen (G / Steuerkreuz links): 60 s "gechillt". Das Combo-Zeitfenster laeuft langsamer ab und die Balance
    /// bei Grinds und Manuals wackelt weniger. Dazu ein weicher Bild-Effekt (satter, leicht wabernde Farben, gruene Vignette)
    /// und eine Anzeige rechts oben (Tueten im Rucksack, Restzeit). Nur lokal.
    /// </summary>
    public class Chill : MonoBehaviour
    {
        public static Chill Instance { get; private set; }
        public const float Duration = 60f, MaxTime = 180f;

        public static bool Active => Instance != null && Instance._left > 0f;
        public static float TimeLeft => Instance != null ? Instance._left : 0f;
        /// <summary>So schnell laeuft das Combo-Zeitfenster ab (1 = normal).</summary>
        public static float ComboDrain => Active ? 0.6f : 1f;
        /// <summary>So stark wackelt die Balance bei Grind und Manual (1 = normal).</summary>
        public static float BalanceCalm => Active ? 0.55f : 1f;

        float _left, _weight, _hintCooldown;
        Volume _volume;
        ColorAdjustments _color;
        Vignette _vignette;
        LensDistortion _lens;
        RectTransform _box;
        Text _count, _timer;
        ParticleSystem _smoke;

        public static void Ensure()
        {
            if (Instance == null) new GameObject("Chill").AddComponent<Chill>();
        }

        void Awake()
        {
            Instance = this;
            BuildVolume();
            BuildUi();
            _smoke = StonerNpc.MakeSmoke("ChillSmoke", transform, 0.05f, 0.1f, 2.4f, 0.45f);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_volume != null && _volume.profile != null) Destroy(_volume.profile);
            if (_box != null) Destroy(_box.gameObject);
        }

        /// <summary>Eine Tuete rauchen. false, wenn keine da ist.</summary>
        public bool Use()
        {
            var p = SaveSystem.Profile;
            if (p.baggies <= 0) return false;
            p.baggies--;
            SaveSystem.Save();
            _left = Mathf.Min(_left + Duration, MaxTime);
            Puff();
            BurstPop.Show("GECHILLT", NixDealer.NameColor, new Vector2(0f, 260f), 230f, 1.8f);
            HUD.Instance?.Toast("Combo-Fenster laenger, Balance ruhiger  (" + Mathf.CeilToInt(_left) + " s)", 3f);
            return true;
        }

        void Update()
        {
            var p = PlayerAvatar.Local;
            bool hud = p != null && !Admin.HideHud && !HUD.Cinematic;
            if (p != null && GameInput.Pressed(GameInput.UseItem) && !QuestNpc.AnyTalking)
            {
                if (!Use() && Time.time > _hintCooldown)
                {
                    _hintCooldown = Time.time + 3f;
                    HUD.Instance?.Toast("Keine Tuete. Frag mal Nix in der Gasse am Brunnenplatz.", 3.5f);
                }
            }
            bool paused = HUD.Instance != null && HUD.Instance.Paused;
            if (!paused) _left = Mathf.Max(0f, _left - Time.deltaTime);

            // Effekt weich ein- und ausblenden (die letzten Sekunden klingt er ab)
            float want = Mathf.Clamp01(_left / 4f);
            _weight = Mathf.MoveTowards(_weight, want, Time.deltaTime / 2.5f);
            if (_volume != null)
            {
                _volume.weight = _weight;
                _volume.enabled = _weight > 0.001f;
                float t = Time.time;
                _color.hueShift.value = Mathf.Sin(t * 0.35f) * 10f;
                _lens.intensity.value = Mathf.Sin(t * 0.6f) * 0.12f;
                _vignette.intensity.value = 0.3f + Mathf.Sin(t * 0.9f) * 0.05f;
            }

            int bags = SaveSystem.Profile.baggies;
            bool show = hud && (bags > 0 || _left > 0f);
            _box.gameObject.SetActive(show);
            if (show)
            {
                _count.text = bags + (bags == 1 ? " TUETE  [G]" : " TUETEN  [G]");
                _timer.text = _left > 0f ? "CHILL " + Mathf.FloorToInt(_left / 60f) + ":" + (Mathf.CeilToInt(_left) % 60).ToString("00") : "";
            }
        }

        void Puff()
        {
            var p = PlayerAvatar.Local;
            if (p == null || _smoke == null) return;
            Transform t = p.Mode == PlayerMode.Driving ? p.car.transform : p.skater.transform;
            Vector3 mouth = t.position + Vector3.up * (p.Mode == PlayerMode.Driving ? 1.3f : 1.6f) + t.forward * 0.25f;
            var e = new ParticleSystem.EmitParams();
            for (int i = 0; i < 18; i++)
            {
                e.position = mouth + Random.insideUnitSphere * 0.04f;
                e.velocity = (t.forward + Vector3.up * 0.7f).normalized * Random.Range(0.3f, 0.7f) + Random.insideUnitSphere * 0.1f;
                e.startSize = Random.Range(0.06f, 0.14f);
                e.startLifetime = Random.Range(1.8f, 2.8f);
                _smoke.Emit(e, 1);
            }
        }

        void BuildVolume()
        {
            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 50f;
            _volume.weight = 0f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _color = profile.Add<ColorAdjustments>(true);
            _color.saturation.Override(28f);
            _color.hueShift.Override(0f);
            _vignette = profile.Add<Vignette>(true);
            _vignette.color.Override(new Color(0.25f, 0.55f, 0.2f));
            _vignette.intensity.Override(0.3f);
            _vignette.smoothness.Override(0.8f);
            _lens = profile.Add<LensDistortion>(true);
            _lens.intensity.Override(0f);
            _volume.profile = profile;
            _volume.enabled = false;
        }

        void BuildUi()
        {
            var layer = SpeechBubble.Layer;
            var panel = UIFactory.Panel(layer, "Chill", new Color(0.09f, 0.07f, 0.12f, 0.88f), Vector2.one, Vector2.one, Vector2.one,
                                        new Vector2(-30f, -160f), new Vector2(330f, 74f), 1.5f);
            _box = panel.rectTransform;
            _count = UIFactory.LabelAt(_box, "", 26, NixDealer.NameColor, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16f, -6f), new Vector2(300f, 34f));
            _timer = UIFactory.LabelAt(_box, "", 24, Palette.White, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16f, -38f), new Vector2(300f, 30f));
            _box.gameObject.SetActive(false);
        }
    }
}
