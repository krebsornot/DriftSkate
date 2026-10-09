using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Fahrtwind fuer die Kamera beim schnellen Skaten: feine Windstreifen fliegen am Bildrand vorbei,
    /// dazu leises Rauschen und ein kaum spuerbares Zittern. Wird von <see cref="CameraRig"/> gesteuert.
    /// </summary>
    public class SpeedWind : MonoBehaviour
    {
        public float startSpeed = 11f;  // ab hier setzt der Wind ein (m/s)
        public float fullSpeed = 22f;   // hier ist er voll da (m/s)

        ParticleSystem _ps;
        AudioSource _audio;
        float _intensity, _emitCarry;

        /// <summary>0..1, weich nachgezogen. Die Kamera nutzt das fuers Zittern.</summary>
        public float Intensity => _intensity;

        void Awake()
        {
            BuildParticles();
            BuildAudio();
        }

        /// <summary>Jeden Frame von der Kamera: aktuelles Tempo und Fahrtrichtung (Welt).</summary>
        public void Tick(float speed, Vector3 flatVel, bool active, float dt)
        {
            float target = active ? Mathf.Clamp01((speed - startSpeed) / (fullSpeed - startSpeed)) : 0f;
            target = target * target * (3f - 2f * target);
            // Langsam rein, etwas schneller raus
            float rate = target > _intensity ? 1.6f : 3f;
            _intensity = Mathf.Lerp(_intensity, target, 1f - Mathf.Exp(-rate * dt));
            if (_intensity < 0.002f) _intensity = 0f;

            if (_audio != null)
            {
                _audio.volume = _intensity * 0.22f * SaveSystem.Profile.sfxVolume;
                _audio.pitch = 0.75f + 0.45f * _intensity;
                if (_intensity > 0f && !_audio.isPlaying) _audio.Play();
                else if (_intensity <= 0f && _audio.isPlaying) _audio.Stop();
            }

            if (_ps == null || _intensity <= 0f || speed < 0.5f) return;

            // Streifen kommen aus der Fahrtrichtung (im Kamera-Raum) auf die Kamera zu
            Vector3 dir = transform.InverseTransformDirection(flatVel / speed);
            if (dir.z < 0.3f) dir = Vector3.forward; // rueckwaerts / quer schauen: einfach von vorn
            dir.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            if (right.sqrMagnitude < 0.01f) right = Vector3.right;
            Vector3 up = Vector3.Cross(dir, right);

            float streakSpeed = 28f + speed * 1.4f;
            _emitCarry += (18f + 70f * _intensity) * _intensity * dt;
            var p = new ParticleSystem.EmitParams();
            while (_emitCarry >= 1f)
            {
                _emitCarry -= 1f;
                // Ring um die Bildmitte, damit der Skater frei bleibt
                float ang = Random.value * Mathf.PI * 2f;
                float rad = Random.Range(1.6f, 4.2f);
                float depth = Random.Range(7f, 14f);
                Vector3 pos = dir * depth + (right * Mathf.Cos(ang) * 1.6f + up * Mathf.Sin(ang)) * rad * 0.8f;
                p.position = pos;
                p.velocity = -dir * streakSpeed * Random.Range(0.85f, 1.15f);
                p.startLifetime = (depth - 0.4f) / streakSpeed;
                p.startSize = Random.Range(0.012f, 0.026f);
                p.startColor = new Color(1f, 1f, 1f, Random.Range(0.12f, 0.32f) * Mathf.Sqrt(_intensity));
                _ps.Emit(p, 1);
            }
        }

        void BuildParticles()
        {
            var go = new GameObject("SpeedWind");
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; // klebt an der Kamera
            main.maxParticles = 200;
            main.startSpeed = 0f;
            var emission = _ps.emission;
            emission.rateOverTime = 0f;
            var shape = _ps.shape;
            shape.enabled = false;
            var color = _ps.colorOverLifetime;
            color.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            color.color = grad;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.035f;
            r.lengthScale = 2f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var baseMat = Resources.Load<Material>("TrailMat");
            if (baseMat != null)
            {
                var m = new Material(baseMat) { name = "SpeedWind" };
                m.SetFloat("_FogAmount", 0f);
                r.sharedMaterial = m;
            }
            _ps.Play();
        }

        void BuildAudio()
        {
            // Braunes Rauschen als Schleife: tiefes, weiches Windrauschen statt Zischen
            const int rate = 22050, len = rate * 2;
            var data = new float[len];
            float b = 0f, lp = 0f;
            var rng = new System.Random(77);
            for (int i = 0; i < len; i++)
            {
                b = Mathf.Clamp(b + ((float)rng.NextDouble() * 2f - 1f) * 0.06f, -1f, 1f) * 0.998f;
                lp += (b - lp) * 0.35f;
                data[i] = lp;
            }
            // Naht der Schleife weich ueberblenden
            const int fade = 2048;
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                data[i] = Mathf.Lerp(data[len - fade + i], data[i], t);
            }
            float peak = 0.0001f;
            for (int i = 0; i < len - fade; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            for (int i = 0; i < len; i++) data[i] = data[i] / peak * 0.8f;
            var clip = AudioClip.Create("Wind", len - fade, 1, rate, false);
            clip.SetData(data, 0);

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.clip = clip;
            _audio.loop = true;
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.volume = 0f;
        }
    }
}
