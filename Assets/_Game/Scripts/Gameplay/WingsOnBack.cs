using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Engelsfluegel am Ruecken des Skaters. Sitzen zwischen Brust- und Halsknochen, schauen in die Blickrichtung der
    /// Figur und folgen jeder Pose (nach dem Rig, daher spaete Ausfuehrung). In der Luft schlagen sie schneller.
    /// Haengen am Skater-Objekt selbst, damit sie einen Neuaufbau der Figur (Outfit, Mod) ueberstehen.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class WingsOnBack : MonoBehaviour
    {
        /// <summary>Spannweite am Ruecken (m) und Abstand vom Brustknochen nach hinten.</summary>
        const float Span = 1.45f, BackOffset = 0.14f;
        /// <summary>Hoehe am Ruecken: 0 = Brustknochen, 1 = Halsansatz (Schulterblaetter liegen knapp darunter).</summary>
        const float ShoulderHeight = 0.95f;

        SkaterController _skater;
        WingFlapper _flap;
        Renderer[] _renderers;
        bool _visible = true;

        public static WingsOnBack Find(SkaterController skater) => skater != null ? skater.GetComponentInChildren<WingsOnBack>(true) : null;

        /// <summary>Fluegel anlegen (true) oder abnehmen (false).</summary>
        public static void Set(SkaterController skater, bool on)
        {
            if (skater == null) return;
            var existing = Find(skater);
            if (!on)
            {
                if (existing != null) Destroy(existing.gameObject);
                skater.canDoubleJump = false;
                return;
            }
            skater.canDoubleJump = true;
            if (existing != null) return;
            var prefab = ItemPrefabs.Instance != null ? ItemPrefabs.Instance.angelWings : null;
            if (prefab == null)
            {
                Debug.LogWarning("WingsOnBack: Prefab fehlt (DriftSkate → Angel Wings → Pickup in die Stadt setzen)");
                return;
            }
            var go = Instantiate(prefab, skater.transform, false);
            go.name = "AngelWingsOnBack";
            var w = go.AddComponent<WingsOnBack>();
            w._skater = skater;
            go.transform.localScale = Vector3.one * Fit(go);
        }

        /// <summary>Ein kraeftiger Fluegelschlag (Doppelsprung).</summary>
        public void Burst()
        {
            if (_flap != null) _flap.Burst();
            if (!_visible) return;
            if (_feathers == null) _feathers = MakeFeathers(transform);
            _feathers.transform.position = transform.position;
            _feathers.Emit(16);
        }

        ParticleSystem _feathers;

        /// <summary>Ein paar weisse Federn, die beim kraeftigen Schlag herausfallen und langsam trudeln.</summary>
        static ParticleSystem MakeFeathers(Transform parent)
        {
            var go = new GameObject("Feathers");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(1f, 0.98f, 0.92f, 0.95f);
            main.gravityModifier = 0.12f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.8f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Resources.Load<Material>("SmokeMat");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        public WingFlapper Flapper => _flap;

        /// <summary>Faktor, damit die Spannweite passt (Mesh-Groesse, unabhaengig von der Animation).</summary>
        public static float Fit(GameObject wings, float span = Span)
        {
            var smr = wings.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null || smr.sharedMesh == null) return 1f;
            float width = smr.sharedMesh.bounds.size.x * Mathf.Abs(smr.transform.lossyScale.x / Mathf.Max(1e-4f, wings.transform.lossyScale.x));
            return width > 1e-3f ? span / width : 1f;
        }

        void Awake()
        {
            _flap = gameObject.AddComponent<WingFlapper>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in _renderers) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void LateUpdate()
        {
            if (_skater == null) { Destroy(gameObject); return; }
            var state = _skater.isLocal ? _skater.State : _skater.RemoteState;
            bool visible = state != SkaterState.Hidden;
            if (visible != _visible)
            {
                _visible = visible;
                foreach (var r in _renderers) if (r != null) r.enabled = visible;
            }
            if (!visible) return;

            var rig = _skater.rig;
            Transform chest = rig != null ? rig.Chest : null, neck = rig != null ? rig.Neck : null;
            Vector3 up, pos;
            if (chest != null && neck != null)
            {
                up = (neck.position - chest.position).normalized;
                pos = Vector3.LerpUnclamped(chest.position, neck.position, ShoulderHeight);
            }
            else
            {
                up = _skater.align != null ? _skater.align.up : Vector3.up;
                pos = _skater.transform.position + up * 1.3f;
            }
            Vector3 face = rig != null && rig.frame != null ? rig.frame.forward : _skater.transform.forward;
            Vector3 fwd = Vector3.ProjectOnPlane(face, up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(_skater.transform.forward, up);
            fwd.Normalize();
            transform.SetPositionAndRotation(pos - fwd * BackOffset, Quaternion.LookRotation(fwd, up));

            if (_flap != null)
            {
                // Am Boden locker angelegt und langsam, in der Luft weit offen und kraeftig
                bool air = state == SkaterState.Air || state == SkaterState.WallRide || state == SkaterState.WallPlant;
                bool walking = state == SkaterState.Walking;
                float speed = _skater.Speed;
                _flap.frequency = air ? 1.5f : walking ? 0.55f : Mathf.Lerp(0.45f, 0.8f, Mathf.InverseLerp(4f, 14f, speed));
                // tuck > sweep: die Spitzen bleiben immer hinter den Schultern
                _flap.sweep = air ? 20f : 9f;
                _flap.lift = air ? 26f : 6f;
                _flap.tuck = air ? 24f : Mathf.Lerp(18f, 32f, Mathf.InverseLerp(4f, 16f, speed)); // bei Tempo nach hinten gelegt
            }
        }
    }
}
