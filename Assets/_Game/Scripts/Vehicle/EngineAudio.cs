using UnityEngine;

namespace DriftSkate
{
    /// <summary>Synthetischer Motorsound (Saegezahn-Obertoene) plus Reifenquietschen. Keine Audiodateien noetig.</summary>
    [RequireComponent(typeof(AudioSource))]
    public class EngineAudio : MonoBehaviour
    {
        public volatile float rpm = 900f, maxRpm = 7500f, throttle, squeal, volume = 0.8f;

        AudioSource _src;
        int _sampleRate = 48000;
        double _phase, _squealPhase;
        float _lp, _noiseLp, _noiseBp;
        uint _seed = 2463534242;

        void Awake()
        {
            _sampleRate = AudioSettings.outputSampleRate;
            _src = GetComponent<AudioSource>();
            _src.clip = AudioClip.Create("silence", _sampleRate, 1, _sampleRate, false);
            _src.loop = true;
            _src.spatialBlend = 0.5f;
            _src.rolloffMode = AudioRolloffMode.Linear;
            _src.minDistance = 4f;
            _src.maxDistance = 90f;
            _src.dopplerLevel = 0.3f;
            _src.Play();
        }

        public void SetSpatial(float blend)
        {
            if (_src != null) _src.spatialBlend = blend;
        }

        float Noise()
        {
            _seed ^= _seed << 13; _seed ^= _seed >> 17; _seed ^= _seed << 5;
            return (_seed / (float)uint.MaxValue) * 2f - 1f;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float freq = Mathf.Max(20f, rpm / 60f * 2f);
            double inc = freq / _sampleRate;
            float thr = throttle;
            float amp = volume * (0.18f + 0.32f * thr) * (0.6f + 0.4f * rpm / Mathf.Max(1f, maxRpm));
            float cutoff = Mathf.Lerp(0.08f, 0.32f, thr);
            float sq = squeal * volume;
            double sqInc = (880f + 120f * Mathf.Sin(rpm * 0.001f)) / _sampleRate;

            for (int i = 0; i < data.Length; i += channels)
            {
                _phase += inc;
                if (_phase > 1000.0) _phase -= 1000.0;
                float p = (float)(_phase % 1.0);
                float p2 = (float)((_phase * 2.0) % 1.0);
                float saw = p * 2f - 1f;
                float saw2 = p2 * 2f - 1f;
                float sub = Mathf.Sin((float)(_phase * 0.5 * 2.0 * Mathf.PI));
                float raw = saw * 0.55f + saw2 * 0.25f + sub * 0.35f + Noise() * 0.08f * thr;
                _lp += cutoff * (raw - _lp);
                float engine = _lp * amp;

                float n = Noise();
                _noiseLp += 0.25f * (n - _noiseLp);
                _noiseBp += 0.08f * (_noiseLp - _noiseBp);
                _squealPhase += sqInc;
                float tone = Mathf.Sin((float)(_squealPhase * 2.0 * Mathf.PI) + _noiseBp * 3f);
                float tire = (tone * 0.5f + (_noiseLp - _noiseBp) * 0.8f) * sq * 0.35f;

                float s = Mathf.Clamp(engine + tire, -1f, 1f);
                for (int c = 0; c < channels; c++) data[i + c] = s;
            }
        }
    }
}
