using UnityEngine;

namespace DriftSkate
{
    /// <summary>Drift-Rauch (Cartoon-Wolken), farbige Reifenspuren und Motor-/Reifensound.</summary>
    [RequireComponent(typeof(VehicleController))]
    public class CarEffects : MonoBehaviour
    {
        VehicleController _vc;
        readonly ParticleSystem[] _smoke = new ParticleSystem[2];
        readonly TrailRenderer[] _trails = new TrailRenderer[2];
        EngineAudio _audio;
        Color _crew = Palette.Pink;
        bool _built;

        void Awake()
        {
            _vc = GetComponent<VehicleController>();
        }

        public void Build(Color crew)
        {
            _crew = crew;
            for (int i = 0; i < 2; i++)
            {
                if (_smoke[i] != null) Destroy(_smoke[i].gameObject);
                if (_trails[i] != null) Destroy(_trails[i].gameObject);
                _smoke[i] = CreateSmoke(i);
                _trails[i] = CreateTrail(i);
            }
            if (_audio == null)
            {
                var go = new GameObject("EngineAudio");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0, 0.5f, 1.2f);
                _audio = go.AddComponent<EngineAudio>();
            }
            _built = true;
        }

        ParticleSystem CreateSmoke(int index)
        {
            var go = new GameObject("Smoke" + index);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 220;
            main.gravityModifier = -0.04f;
            main.startColor = Color.Lerp(Color.white, _crew, 0.25f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 2.2f));

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            color.color = grad;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("SmokeMat");
            renderer.sortMode = ParticleSystemSortMode.Distance;
            ps.Play();
            return ps;
        }

        TrailRenderer CreateTrail(int index)
        {
            var go = new GameObject("Trail" + index);
            go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // Z zeigt nach unten -> Spur liegt flach
            var tr = go.AddComponent<TrailRenderer>();
            tr.alignment = LineAlignment.TransformZ;
            tr.time = 6f;
            tr.minVertexDistance = 0.35f;
            tr.widthMultiplier = 0.24f;
            tr.numCapVertices = 2;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.sharedMaterial = Resources.Load<Material>("TrailMat");
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(_crew, 0f), new GradientColorKey(_crew, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.7f, 0.7f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = grad;
            tr.emitting = false;
            return tr;
        }

        void LateUpdate()
        {
            if (!_built) return;
            float total = 0f;
            for (int i = 0; i < 2; i++)
            {
                var wheel = _vc.wheels[i + 2];
                float amount = _vc.SmokeAmount(i + 2);
                total += amount;
                Vector3 local = wheel.pivot != null ? wheel.pivot.localPosition : wheel.localTop;
                Vector3 ground = local - Vector3.up * (_vc.setup.wheelRadius - 0.03f);

                var smoke = _smoke[i];
                if (smoke != null)
                {
                    smoke.transform.localPosition = ground + new Vector3(0, 0.25f, -0.15f);
                    var emission = smoke.emission;
                    emission.rateOverTime = amount > 0.15f ? Mathf.Lerp(6f, 38f, amount) : 0f;
                }

                var trail = _trails[i];
                if (trail != null)
                {
                    trail.transform.localPosition = ground;
                    trail.emitting = amount > 0.35f && wheel.grounded;
                }
            }

            if (_audio != null)
            {
                _audio.rpm = _vc.EngineRpm;
                _audio.maxRpm = _vc.setup.maxRpm;
                _audio.throttle = _vc.hasDriver ? _vc.Throttle : 0f;
                _audio.squeal = Mathf.Clamp01(total * 0.6f);
                _audio.volume = SaveSystem.Profile.sfxVolume * (_vc.hasDriver || _vc.Speed > 1f ? 1f : 0.35f);
            }
        }

        public void SetSpatial(bool local)
        {
            if (_audio != null) _audio.SetSpatial(local ? 0.5f : 1f);
        }
    }
}
