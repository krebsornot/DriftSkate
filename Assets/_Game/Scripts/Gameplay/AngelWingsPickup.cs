using System.Collections;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Easter Egg: Engelsfluegel schweben irgendwo in der Stadt (das Objekt "AngelWingsPickup" in City.unity, im Editor
    /// frei verschiebbar, z. B. auf ein Dach). Sie wippen, drehen sich langsam und glitzern. Faehrt oder springt man
    /// hindurch, fliegen sie einem auf den Ruecken und bleiben dort (gespeichert im Profil, online fuer alle sichtbar).
    /// </summary>
    public class AngelWingsPickup : MonoBehaviour
    {
        public static AngelWingsPickup Instance { get; private set; }

        [Tooltip("So nah muss man herankommen (m, gemessen ab Huefthoehe).")]
        public float radius = 1.7f;
        [Tooltip("Spannweite, solange sie schweben (m).")]
        public float floatSpan = 2.2f;

        Transform _visual;
        Vector3 _visualBase;
        ParticleSystem _sparkle;
        bool _taken;

        public bool Taken => _taken;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            _visual = transform.Find("Visual");
            if (_visual != null)
            {
                _visualBase = _visual.localPosition;
                _visual.localScale = Vector3.one * WingsOnBack.Fit(_visual.gameObject, floatSpan);
                foreach (var r in _visual.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // Ruhiger, weiter Fluegelschlag beim Schweben
                var flap = _visual.gameObject.AddComponent<WingFlapper>();
                flap.frequency = 0.6f;
                flap.sweep = 16f;
                flap.lift = 16f;
                flap.tuck = 14f;
            }
            _sparkle = MakeSparkle(transform);
            if (SaveSystem.Profile.angelWings) SetTaken(true);
        }

        void Update()
        {
            if (_taken || _visual == null) return;
            float t = Time.time;
            _visual.localPosition = _visualBase + Vector3.up * Mathf.Sin(t * 1.4f) * 0.14f;
            _visual.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.9f) * 4f, t * 28f, 0f);

            var p = PlayerAvatar.Local;
            if (p == null || p.Mode != PlayerMode.Skating) return;
            Vector3 body = p.skater.transform.position + Vector3.up * 0.9f;
            if ((body - transform.position).sqrMagnitude < radius * radius) Collect(p);
        }

        void Collect(PlayerAvatar p)
        {
            _taken = true;
            SaveSystem.Profile.angelWings = true;
            SaveSystem.Save();
            if (p.combo != null) p.combo.AddAction("ENGELSFLUEGEL!", 1000f);
            HUD.Instance?.Toast("ENGELSFLUEGEL GEFUNDEN!", 3.5f);
            if (_sparkle != null)
            {
                _sparkle.Emit(60);
                var em = _sparkle.emission;
                em.rateOverTime = 0f;
            }
            StartCoroutine(FlyToPlayer(p));
        }

        /// <summary>Die schwebenden Fluegel fliegen zum Spieler, werden kleiner, dann sitzen sie am Ruecken.</summary>
        IEnumerator FlyToPlayer(PlayerAvatar p)
        {
            Vector3 from = _visual.position, scale = _visual.localScale;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.5f)
            {
                if (p == null) break;
                float k = Mathf.SmoothStep(0f, 1f, t);
                Vector3 to = p.skater.transform.position + Vector3.up * 1.3f;
                _visual.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.6f;
                _visual.localScale = scale * Mathf.Lerp(1f, 0.6f, k);
                yield return null;
            }
            if (p != null) p.SetWings(true);
            SetTaken(true);
        }

        void SetTaken(bool taken)
        {
            _taken = taken;
            if (_visual != null)
            {
                _visual.gameObject.SetActive(!taken);
                if (!taken) _visual.localScale = Vector3.one * WingsOnBack.Fit(_visual.gameObject, floatSpan);
            }
            if (_sparkle != null)
            {
                var em = _sparkle.emission;
                em.rateOverTime = taken ? 0f : 9f;
            }
        }

        /// <summary>Admin / Tests: Fluegel wieder abnehmen und das Easter Egg zuruecksetzen.</summary>
        public static void ResetForTesting()
        {
            SaveSystem.Profile.angelWings = false;
            SaveSystem.Save();
            PlayerAvatar.Local?.SetWings(false);
            if (Instance != null) Instance.SetTaken(false);
        }

        /// <summary>Glitzern um die Fluegel: kleine helle Funken, die langsam aufsteigen.</summary>
        static ParticleSystem MakeSparkle(Transform parent)
        {
            var go = new GameObject("Sparkle");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.97f, 0.8f, 0.9f), new Color(0.85f, 0.8f, 1f, 0.9f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.03f;
            main.maxParticles = 120;
            var emission = ps.emission;
            emission.rateOverTime = 9f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.8f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Resources.Load<Material>("SmokeMat");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.95f, 0.6f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
