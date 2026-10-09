using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Spruehdosen-Geraeusch ohne Audiodatei: gefiltertes Rauschen ("Psssht"), manchmal mit dem Klackern
    /// der Mischkugel davor. Mehrere Varianten, damit es nicht immer gleich klingt.
    /// </summary>
    public static class SpraySound
    {
        const int Rate = 44100;
        static AudioClip[] _clips;
        static AudioSource _source;
        static int _last = -1;

        /// <summary>Kurzer Spruehstoss (2D). menu = Menue-Regler, sonst Effekt-Regler (z. B. Taggen im Spiel).</summary>
        public static void Play(float volume = 1f, bool menu = true)
        {
            if (_clips == null) Build();
            if (_source == null)
            {
                var go = new GameObject("SpraySound");
                Object.DontDestroyOnLoad(go);
                _source = go.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.spatialBlend = 0f;
            }
            int i = Random.Range(0, _clips.Length);
            if (i == _last) i = (i + 1) % _clips.Length;
            _last = i;
            _source.pitch = Random.Range(0.93f, 1.07f);
            var p = SaveSystem.Profile;
            _source.PlayOneShot(_clips[i], Mathf.Clamp01(volume * (menu ? p.uiVolume : p.sfxVolume)));
        }

        static void Build()
        {
            _clips = new AudioClip[5];
            for (int i = 0; i < _clips.Length; i++) _clips[i] = Make(1234 + i * 77, i < 2);
        }

        static AudioClip Make(int seed, bool rattle)
        {
            var rng = new System.Random(seed);
            float Rand() => (float)rng.NextDouble();
            float sprayLen = 0.26f + 0.14f * Rand();
            float rattleLen = rattle ? 0.2f : 0f;
            int n = Mathf.CeilToInt((rattleLen + sprayLen) * Rate);
            var data = new float[n];
            float dt = 1f / Rate;

            // Klackern: zwei kurze metallische Klicks der Mischkugel
            if (rattle)
            {
                float[] hits = { 0f, 0.075f + 0.02f * Rand() };
                foreach (float h in hits)
                {
                    int start = Mathf.RoundToInt(h * Rate);
                    float f1 = 2500f + 400f * Rand(), f2 = 3900f + 500f * Rand(), f3 = 6100f + 600f * Rand();
                    for (int s = 0; s < (int)(0.05f * Rate) && start + s < n; s++)
                    {
                        float t = s * dt;
                        float ring = (Mathf.Sin(2f * Mathf.PI * f1 * t) + 0.7f * Mathf.Sin(2f * Mathf.PI * f2 * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * f3 * t)) * Mathf.Exp(-t / 0.011f);
                        float tick = (Rand() * 2f - 1f) * Mathf.Exp(-t / 0.0025f);
                        data[start + s] += ring * 0.16f + tick * 0.35f;
                    }
                }
            }

            // Spruehen: Rauschen durch Hoch- und Tiefpass, mit Anfangs-"Pff" und leichtem Flattern
            int o = Mathf.RoundToInt(rattleLen * Rate);
            float hpA = 1f / (1f + 2f * Mathf.PI * 1700f * dt);        // Hochpass ~1,7 kHz
            float lpB = 2f * Mathf.PI * 7800f * dt / (1f + 2f * Mathf.PI * 7800f * dt); // Tiefpass ~7,8 kHz
            float hp = 0f, prevX = 0f, lp = 0f, lp2 = 0f, slow = 0f;
            float flutter = 38f + 18f * Rand();
            for (int s = 0; o + s < n; s++)
            {
                float t = s * dt;
                float x = Rand() * 2f - 1f;
                hp = hpA * (hp + x - prevX);
                prevX = x;
                lp += (hp - lp) * lpB;
                lp2 += (lp - lp2) * lpB;
                slow += ((Rand() * 2f - 1f) - slow) * 0.002f;
                float env = Mathf.Min(1f, t / 0.012f) * (1f - 0.3f * t / sprayLen);
                float rest = sprayLen - t;
                if (rest < 0.08f) env *= 0.5f - 0.5f * Mathf.Cos(Mathf.PI * Mathf.Clamp01(rest / 0.08f));
                env *= 1f + 0.45f * Mathf.Exp(-t / 0.03f);                          // Pff am Anfang
                env *= 1f + 0.07f * Mathf.Sin(2f * Mathf.PI * flutter * t) + 0.6f * slow;
                data[o + s] += (lp2 * 0.85f + hp * 0.15f) * env;
            }

            float peak = 0f;
            foreach (float v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
            if (peak > 0f) for (int i = 0; i < n; i++) data[i] *= 0.55f / peak;

            var clip = AudioClip.Create("Spray_" + seed, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
