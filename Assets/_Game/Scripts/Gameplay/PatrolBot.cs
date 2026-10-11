using System;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Patrouillen-Bot ENFORCER 01 aus Mirus Auftrag: kreist ueber dem Park, entdeckt den Spieler, schaltet das Blaulicht ein
    /// und stellt sich vor ihn, um ihn zu scannen. Wer ihn mit dem Board anspringt (in der Luft hineinfliegen), trifft ihn;
    /// nach drei Treffern stuerzt er ab. Fliegt ueber Baeume und Haeuser hinweg, hat keinen Collider (blockt niemanden).
    /// Nur lokal (wie Miru), andere Spieler sehen ihn nicht.
    /// </summary>
    public class PatrolBot : MonoBehaviour
    {
        public const string PrefabPath = "Npcs/PatrolBot";
        public const int HitsNeeded = 3;

        enum Mode { Patrol, Chase, Scan, Stunned, Crashing, Down }

        /// <summary>Treffer (1..HitsNeeded) und Absturz.</summary>
        public event Action<int> Hit;
        public event Action Downed;

        public int Hits { get; private set; }
        public bool IsDown => _mode == Mode.Crashing || _mode == Mode.Down;
        public bool Alerted => _mode == Mode.Chase || _mode == Mode.Scan;
        /// <summary>Mitte des Rumpfs (Trefferpunkt, Ziel der Markierung).</summary>
        public Vector3 Center => transform.position;

        const float PatrolHeight = 4.5f, ScanHeight = 2.1f, SpotDistance = 18f, LoseDistance = 55f;
        const float PatrolSpeed = 5f, ChaseSpeed = 12f, HitRadius = 1.25f, ScanTime = 3f;

        Mode _mode;
        Vector3 _home, _vel, _scanPoint;
        float _radius, _angle, _timer, _yaw, _spin, _roll, _rotorSpeed = 1f;
        Transform _visual, _rotorL, _rotorR;
        Quaternion _rotorBaseL, _rotorBaseR;
        Renderer _siren;
        MaterialPropertyBlock _mpb;
        ParticleSystem _smoke;
        int _groundMask;

        public static PatrolBot Spawn(Vector3 home, float radius)
        {
            var prefab = Resources.Load<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("PatrolBot: Prefab fehlt (DriftSkate → Patrouillen-Bot → Prefab bauen)");
                return null;
            }
            var go = Instantiate(prefab, home + Vector3.up * 14f, Quaternion.identity);
            go.name = "PatrolBot";
            var bot = go.AddComponent<PatrolBot>();
            bot._home = home;
            bot._radius = radius;
            return bot;
        }

        void Awake()
        {
            _visual = transform.Find("Visual");
            _rotorL = _visual != null ? _visual.Find("Rotor_L") : null;
            _rotorR = _visual != null ? _visual.Find("Rotor_R") : null;
            if (_rotorL != null) _rotorBaseL = _rotorL.localRotation;
            if (_rotorR != null) _rotorBaseR = _rotorR.localRotation;
            var siren = _visual != null ? _visual.Find("Siren") : null;
            _siren = siren != null ? siren.GetComponentInChildren<Renderer>() : null;
            _mpb = new MaterialPropertyBlock();
            _groundMask = ~LayerMask.GetMask("Car", "Skater", "Ignore Raycast", "Rail", "Detail");
            _angle = UnityEngine.Random.Range(0f, 360f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var player = PlayerAvatar.Local;
            Vector3 pos = transform.position;
            Vector3 playerPos = player == null ? pos : player.Mode == PlayerMode.Driving ? player.car.transform.position : player.skater.transform.position;
            float playerDist = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(playerPos.x, playerPos.z));
            Vector3 target = pos;
            float speed = PatrolSpeed;

            switch (_mode)
            {
                case Mode.Patrol:
                    _angle += PatrolSpeed / _radius * Mathf.Rad2Deg * dt;
                    float a = _angle * Mathf.Deg2Rad;
                    target = _home + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * _radius + Vector3.up * PatrolHeight;
                    if (player != null && playerDist < SpotDistance) Alert();
                    break;
                case Mode.Chase:
                    // Vor den Spieler setzen (in Fahrtrichtung), dann dort schweben und scannen
                    target = InFront(player, playerPos) + Vector3.up * ScanHeight;
                    speed = ChaseSpeed;
                    if (player == null || playerDist > LoseDistance) { _mode = Mode.Patrol; break; }
                    if (Vector3.Distance(pos, target) < 1.2f) { _mode = Mode.Scan; _scanPoint = target; _timer = ScanTime; }
                    break;
                case Mode.Scan:
                    target = _scanPoint;
                    speed = 3f;
                    _timer -= dt;
                    // Weitergefahren oder lange genug gescannt: neu vor den Spieler setzen
                    if (_timer <= 0f || playerDist > 9f) _mode = Mode.Chase;
                    break;
                case Mode.Stunned:
                    _timer -= dt;
                    _vel = Vector3.MoveTowards(_vel, Vector3.zero, 9f * dt);
                    target = pos + _vel * 0.2f;
                    if (_timer <= 0f) _mode = Mode.Chase;
                    break;
                case Mode.Crashing:
                    _vel += Physics.gravity * dt;
                    _spin += 540f * dt;
                    _rotorSpeed = Mathf.MoveTowards(_rotorSpeed, 0.15f, dt * 0.6f);
                    if (Physics.Raycast(pos, Vector3.down, out RaycastHit hit, 0.6f + Mathf.Max(0f, -_vel.y) * dt, _groundMask, QueryTriggerInteraction.Ignore))
                    {
                        transform.position = hit.point + Vector3.up * 0.55f;
                        _mode = Mode.Down;
                        _timer = 0f;
                        _vel = Vector3.zero;
                        Smoke(true, 1f);
                        Downed?.Invoke();
                    }
                    else transform.position = pos + _vel * dt;
                    break;
                case Mode.Down:
                    _rotorSpeed = Mathf.MoveTowards(_rotorSpeed, 0f, dt * 0.3f);
                    break;
            }

            if (_mode != Mode.Crashing && _mode != Mode.Down)
            {
                // Mindesthoehe ueber Baeumen, Daechern und Baenken unter dem Ziel und auf dem Weg dorthin
                target.y = Mathf.Max(target.y, ObstacleTop(target) + 1.4f, ObstacleTop(Vector3.Lerp(pos, target, 0.5f)) + 1.4f);
                Vector3 desired = Vector3.ClampMagnitude((target - pos) * 2.2f, speed);
                if (_mode != Mode.Stunned) _vel = Vector3.MoveTowards(_vel, desired, 14f * dt);
                transform.position = pos + _vel * dt;
                if (_mode != Mode.Stunned) TryHit(player);
            }

            UpdateLook(dt, player, playerPos);
        }

        void Alert()
        {
            _mode = Mode.Chase;
            HUD.Instance?.Toast("ENFORCER 01 HAT DICH IM VISIER", 2.5f);
        }

        /// <summary>Punkt 3,5 m vor dem Spieler (Fahrtrichtung, sonst Blickrichtung).</summary>
        static Vector3 InFront(PlayerAvatar p, Vector3 playerPos)
        {
            if (p == null) return playerPos;
            Vector3 v = p.Mode == PlayerMode.Driving ? p.car.GetComponent<Rigidbody>().linearVelocity : p.skater.GetComponent<Rigidbody>().linearVelocity;
            v.y = 0f;
            Vector3 dir = v.sqrMagnitude > 1f ? v.normalized : Quaternion.Euler(0f, p.Mode == PlayerMode.Driving ? p.car.transform.eulerAngles.y : p.skater.Heading, 0f) * Vector3.forward;
            return new Vector3(playerPos.x, Ground(playerPos), playerPos.z) + dir * 3.5f;
        }

        static float Ground(Vector3 p) => p.y < 0f ? 0f : p.y;

        float ObstacleTop(Vector3 p)
        {
            if (Physics.SphereCast(new Vector3(p.x, 40f, p.z), 0.8f, Vector3.down, out RaycastHit hit, 60f, _groundMask, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return 0f;
        }

        /// <summary>Treffer: Skater in der Luft fliegt in den Rumpf.</summary>
        void TryHit(PlayerAvatar player)
        {
            if (player == null || player.Mode != PlayerMode.Skating) return;
            var s = player.skater;
            if (s.State != SkaterState.Air) return;
            Vector3 chest = s.transform.position + Vector3.up * 1.1f;
            if ((chest - transform.position).sqrMagnitude > HitRadius * HitRadius) return;

            Hits++;
            Vector3 away = transform.position - s.transform.position;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            _vel = away * 7f + Vector3.up * 4f;
            _roll = 1f;
            Sparks();
            player.combo?.AddAction("ROBO SMACK", 750f);
            SpraySound.Play(0.7f, false);
            BurstPop.Show(Hits >= HitsNeeded ? "BOT DOWN!" : "TREFFER " + Hits + " / " + HitsNeeded, MiruTalk.NameColor, new Vector2(0f, 260f), 220f, 1.6f);
            Hit?.Invoke(Hits);
            if (Hits >= HitsNeeded)
            {
                _mode = Mode.Crashing;
                Smoke(false, 0.6f);
            }
            else { _mode = Mode.Stunned; _timer = 1.6f; }
        }

        void UpdateLook(float dt, PlayerAvatar player, Vector3 playerPos)
        {
            // Rotoren, Blaulicht (rot / blau im Wechsel, nur wenn er dich verfolgt), Neigung in Flugrichtung, leichtes Schweben
            float rotor = 1800f * _rotorSpeed * Time.time;
            if (_rotorL != null) _rotorL.localRotation = _rotorBaseL * Quaternion.Euler(0f, rotor, 0f);
            if (_rotorR != null) _rotorR.localRotation = _rotorBaseR * Quaternion.Euler(0f, -rotor, 0f);
            if (_siren != null)
            {
                bool on = Alerted || _mode == Mode.Stunned;
                bool red = Mathf.Repeat(Time.time * 3f, 1f) < 0.5f;
                _mpb.SetColor("_BaseColor", !on ? new Color(0.35f, 0.35f, 0.45f) : red ? new Color(1f, 0.15f, 0.25f) : new Color(0.2f, 0.45f, 1f));
                _mpb.SetFloat("_Emission", on ? 2.2f : 0.3f);
                _siren.SetPropertyBlock(_mpb);
            }

            Vector3 flat = new Vector3(_vel.x, 0f, _vel.z);
            Vector3 look = (Alerted && player != null) ? playerPos - transform.position : flat;
            look.y = 0f;
            if (look.sqrMagnitude > 0.05f && _mode != Mode.Down)
                _yaw = Mathf.MoveTowardsAngle(_yaw, Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, 220f * dt);
            Vector3 local = Quaternion.Euler(0f, -_yaw, 0f) * flat;
            float pitch = Mathf.Clamp(local.z * 2.2f, -18f, 18f), bank = Mathf.Clamp(-local.x * 2.2f, -18f, 18f);
            _roll = Mathf.MoveTowards(_roll, 0f, dt * 0.8f);
            float wobble = Mathf.Sin(Time.time * 18f) * 25f * _roll;
            if (_mode == Mode.Down) { pitch = 12f; bank = 28f; }
            transform.rotation = Quaternion.Euler(pitch, _yaw + _spin, bank + wobble);
            if (_visual != null) _visual.localPosition = _mode == Mode.Down ? Vector3.zero : Vector3.up * Mathf.Sin(Time.time * 2.1f) * 0.07f;
        }

        // ------------------------------------------------------------------ Effekte

        void Sparks()
        {
            var ps = Particles("Sparks", transform.position, false);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startColor = new Color(1f, 0.85f, 0.35f, 1f);
            main.gravityModifier = 1.2f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;
            ps.Emit(40);
            Destroy(ps.gameObject, 1.5f);
        }

        /// <summary>Rauch: beim Absturz dunkel hinterher, am Boden noch eine Weile.</summary>
        void Smoke(bool ground, float amount)
        {
            if (_smoke == null)
            {
                _smoke = Particles("Smoke", transform.position, true);
                _smoke.transform.SetParent(transform, true);
                var main = _smoke.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.32f);
                main.startColor = new Color(0.55f, 0.52f, 0.62f, 0.55f);
                var shape = _smoke.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.25f;
                main.gravityModifier = -0.15f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var size = _smoke.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.6f));
                _smoke.Play();
            }
            var em = _smoke.emission;
            em.rateOverTime = 12f * amount;
            if (ground) Destroy(_smoke.gameObject, 12f); // qualmt noch kurz, dann ist Ruhe
        }

        static ParticleSystem Particles(string name, Vector3 pos, bool loop)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = loop;
            main.playOnAwake = false;
            main.maxParticles = 200;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Resources.Load<Material>("SmokeMat");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        void OnDestroy()
        {
            if (_smoke != null) Destroy(_smoke.gameObject);
        }
    }
}
