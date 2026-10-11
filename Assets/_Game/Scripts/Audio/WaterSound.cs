using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Wassergeraeusche ohne Audiodatei: Platscher (Rauschen mit tiefem "Plumps" und abfallendem Zischen) und das
    /// Rauschen einer Fontaene (nahtlose Schleife aus gefiltertem Rauschen). Lautstaerke ueber den Effekt-Regler.
    /// </summary>
    public static class WaterSound
    {
        const int Rate = 44100;
        static AudioClip[] _splash;
        static AudioClip _rush;

        /// <summary>Platscher an einer Stelle in der Welt (3D). strength 0..1 = wie heftig.</summary>
        public static void Splash(Vector3 pos, float strength)
        {
            if (_splash == null)
            {
                _splash = new AudioClip[3];
                for (int i = 0; i < _splash.Length; i++) _splash[i] = MakeSplash(777 + i * 31);
            }
            var p = SaveSystem.Profile;
            float vol = Mathf.Clamp01((0.5f + 0.5f * strength) * (p != null ? p.sfxVolume : 1f));
            if (vol <= 0.01f) return;
            var go = new GameObject("Splash");
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 1f;
            src.minDistance = 6f;
            src.maxDistance = 120f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.pitch = Random.Range(0.85f, 1.1f) * Mathf.Lerp(1.15f, 0.85f, strength);
            src.PlayOneShot(_splash[Random.Range(0, _splash.Length)], vol);
            Object.Destroy(go, 2.5f);
        }

        /// <summary>Dauerhaftes Rauschen (Fontaene) an einem Objekt; Lautstaerke spaeter ueber source.volume.</summary>
        public static AudioSource Rush(Transform parent, float volume)
        {
            if (_rush == null) _rush = MakeRush(4242);
            var go = new GameObject("WaterRush");
            go.transform.SetParent(parent, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = _rush;
            src.loop = true;
            src.spatialBlend = 1f;
            src.minDistance = 4f;
            src.maxDistance = 70f;
            src.rolloffMode = AudioRolloffMode.Linear;
            var p = SaveSystem.Profile;
            src.volume = Mathf.Clamp01(volume * (p != null ? p.sfxVolume : 1f));
            src.Play();
            return src;
        }

        static AudioClip MakeSplash(int seed)
        {
            var rng = new System.Random(seed);
            float Rand() => (float)rng.NextDouble();
            float len = 1.1f;
            int n = Mathf.CeilToInt(len * Rate);
            var data = new float[n];
            float lp = 0f, bp = 0f, lp2 = 0f;
            for (int s = 0; s < n; s++)
            {
                float t = s / (float)Rate;
                float white = Rand() * 2f - 1f;
                // Plumps: tiefer, schnell abklingender Ton
                float thump = Mathf.Sin(6.2832f * (90f - 60f * t) * t) * Mathf.Exp(-t * 14f) * 0.9f;
                // Zischen: Rauschen, erst hell, dann dumpfer; Huellkurve mit kurzem Anstieg
                float cutoff = Mathf.Lerp(0.6f, 0.08f, Mathf.Clamp01(t / 0.9f));
                lp += (white - lp) * cutoff;
                bp += (lp - bp) * 0.35f;
                float env = Mathf.Clamp01(t / 0.03f) * Mathf.Exp(-t * 3.2f);
                // Nachtroepfeln: kleine Spritzer
                float drip = Rand() < 0.0015f * Mathf.Exp(-t * 2f) ? (Rand() - 0.5f) * 1.6f : 0f;
                lp2 += (drip - lp2) * 0.5f;
                data[s] = Mathf.Clamp(thump + (bp * 1.6f) * env + lp2 * 0.6f, -1f, 1f) * 0.85f;
            }
            var clip = AudioClip.Create("Splash_" + seed, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip MakeRush(int seed)
        {
            var rng = new System.Random(seed);
            float len = 2f;
            int n = Mathf.CeilToInt(len * Rate);
            var data = new float[n];
            float lp = 0f, bp = 0f;
            for (int s = 0; s < n; s++)
            {
                float t = s / (float)Rate;
                float white = (float)rng.NextDouble() * 2f - 1f;
                // leicht wanderndes Filter, damit es lebt
                float cutoff = 0.22f + 0.08f * Mathf.Sin(t * 2.3f) + 0.04f * Mathf.Sin(t * 7.1f);
                lp += (white - lp) * cutoff;
                bp += (lp - bp) * 0.5f;
                data[s] = bp * 1.4f;
            }
            // nahtlos: Anfang und Ende ueberblenden
            int fade = Rate / 10;
            for (int s = 0; s < fade; s++)
            {
                float a = s / (float)fade;
                data[s] = data[s] * a + data[n - fade + s] * (1f - a);
            }
            var clip = AudioClip.Create("WaterRush", n - fade, 1, Rate, false);
            var trimmed = new float[n - fade];
            System.Array.Copy(data, trimmed, n - fade);
            clip.SetData(trimmed, 0);
            return clip;
        }
    }
}
