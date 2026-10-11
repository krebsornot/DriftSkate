using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Flamme an der Duese eines Raketen-Boosters: nur sichtbar, solange das Auto boostet (auch bei anderen Spielern,
    /// VehicleController.Boosting kommt dann ueber das Netzwerk). Schiesst beim Zuenden heraus, flackert in der Laenge
    /// und dreht sich leicht; dazu ein Raketen-Rauschen (im Code erzeugt, wie die anderen Sounds).
    /// </summary>
    public class BoosterFlame : MonoBehaviour
    {
        VehicleController _car;
        Renderer[] _renderers;
        AudioSource _audio;
        float _on, _seed;
        static AudioClip _roar;

        void Awake()
        {
            _car = GetComponentInParent<VehicleController>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _seed = Random.value * 10f;
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.clip = _roar != null ? _roar : (_roar = MakeRoar());
            _audio.loop = true;
            _audio.spatialBlend = 1f;
            _audio.minDistance = 4f;
            _audio.maxDistance = 70f;
            _audio.volume = 0f;
            _audio.playOnAwake = false;
            Show(false);
        }

        void Show(bool on)
        {
            foreach (var r in _renderers) if (r.enabled != on) r.enabled = on;
        }

        void Update()
        {
            bool boosting = _car != null && _car.Boosting;
            _on = Mathf.MoveTowards(_on, boosting ? 1f : 0f, Time.deltaTime * (boosting ? 7f : 4f));
            Show(_on > 0.02f);
            if (_on > 0.02f)
            {
                float t = Time.time * 30f + _seed;
                float flicker = 0.85f + 0.15f * Mathf.Sin(t) * Mathf.Sin(t * 1.7f + 1.3f);
                transform.localScale = new Vector3(1f + 0.06f * Mathf.Sin(t * 0.9f), 1f + 0.06f * Mathf.Cos(t * 1.1f), _on * flicker * 1.15f);
                transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * 340f + _seed * 40f);
            }
            float vol = _on * 0.55f * SaveSystem.Profile.sfxVolume;
            _audio.volume = vol;
            if (vol > 0.001f && !_audio.isPlaying) _audio.Play();
            else if (vol <= 0.001f && _audio.isPlaying) _audio.Stop();
            _audio.pitch = 0.9f + 0.1f * _on;
        }

        /// <summary>Raketen-Rauschen: tiefes, gefiltertes Rauschen mit leichtem Knattern, nahtlos schleifbar.</summary>
        static AudioClip MakeRoar()
        {
            const int rate = 44100, len = rate * 2;
            var data = new float[len];
            var rng = new System.Random(5);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < len; i++)
            {
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (n - lp) * 0.08f;     // dumpfes Rauschen
                lp2 += (n - lp2) * 0.35f;   // helles Zischen
                float t = i / (float)rate;
                float crackle = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 23f * t) * Mathf.Sin(2f * Mathf.PI * 7f * t);
                data[i] = (lp * 1.6f + lp2 * 0.25f) * crackle;
            }
            // Anfang und Ende ueberblenden, damit die Schleife nicht knackt
            int fade = rate / 10;
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                data[i] = data[i] * w + data[len - fade + i] * (1f - w);
            }
            var clip = AudioClip.Create("RocketRoar", len - fade, 1, rate, false);
            var trimmed = new float[len - fade];
            System.Array.Copy(data, trimmed, len - fade);
            clip.SetData(trimmed, 0);
            return clip;
        }
    }
}
