using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace DriftSkate
{
    /// <summary>
    /// Stoner-NPCs mit Sofa an einem Platz (Modelle und Animationen aus Tools/Blender/build_stoners.py).
    /// Jede Figur spielt ihr Idle in Schleife und ab und zu ihre eigene Animation (Jojo raucht, Kalle isst Chips,
    /// Luna tanzt), schaut dem Spieler langsam nach und lacht, wenn er in der Naehe Tricks macht oder stuerzt.
    /// Nur Deko: laeuft auf jedem Rechner gleich, ohne Netzwerk.
    /// </summary>
    public class StonerNpc : MonoBehaviour
    {
        public static readonly List<StonerNpc> All = new List<StonerNpc>();

        public string Id { get; private set; }
        public string CurrentAction { get; private set; }
        public Transform Head => _head;
        public Animator Animator => _anim;

        /// <summary>Wo die Gruppe in der Stadt sitzt: Platz neben dem Brunnenplatz (Block 2/1), Ecke beim Baum, Blick zur Platzmitte.</summary>
        public static Vector3 HangoutPosition => new Vector3(CityBuilder.BlockCenter(1) - 20f, 0f, CityBuilder.BlockCenter(2) + 28.6f);
        public static Vector3 HangoutFacing => Vector3.back;

        static readonly Dictionary<string, string> Specials = new Dictionary<string, string>
        {
            { "Jojo", "Smoke" }, { "Kalle", "Snack" }, { "Luna", "Vibe" }, { "Nix", "Lookout" }, { "Moe", "Count" },
        };

        static readonly Dictionary<string, string[]> Cheers = new Dictionary<string, string[]>
        {
            { "Jojo", new[] { "Duuude... krass.", "Alter, mach das nochmal.", "Wie hast du das gemacht, Mann?" } },
            { "Kalle", new[] { "Hahaha! Nochmal!", "Boah, vom Zugucken krieg ich Hunger.", "Willst du Chips? Nein? Okay." } },
            { "Luna", new[] { "Uuuh, so smooth!", "Das sah aus wie ein Song.", "Okay, das war schoen." } },
            { "Nix", new[] { "Heh. Nicht schlecht.", "Mach nicht so laut hier, Mann.", "Okay, okay. Du kannst was." } },
            { "Moe", new[] { "So will ich das sehen!", "Ey, der Junge hat Style.", "Sauber. Merk ich mir." } },
            { "Miru", new[] { "Hm. Gar nicht mal schlecht.", "Okay, das war cool.", "Mach weiter, ich guck zu." } },
        };

        static readonly Dictionary<string, string[]> Fails = new Dictionary<string, string[]>
        {
            { "Jojo", new[] { "Autsch... alles chillig?" } },
            { "Kalle", new[] { "Hahaha, der Boden!" } },
            { "Luna", new[] { "Ups. Atmen nicht vergessen." } },
            { "Nix", new[] { "Pfff. Steh auf, Mann. Leise." } },
            { "Moe", new[] { "Hahaha! Das kostet extra." } },
            { "Miru", new[] { "Autsch. Lebst du noch?" } },
        };

        Animator _anim;
        PlayableGraph _graph;
        AnimationMixerPlayable _mixer;
        AnimationClipPlayable _idle, _action, _sit;
        float _sitWeight, _sitTarget;
        readonly Dictionary<string, AnimationClip> _clips = new Dictionary<string, AnimationClip>();
        string _special;
        float _actionTime, _actionEnd, _actionWeight, _nextSpecial, _laughCooldown, _pendingLaugh = -1f;
        static float _lineCooldown;
        SpeechBubble _bark;
        Transform _head, _jointTip;
        Vector3 _headFaceLocal, _mouthLocal, _lookDir;
        float _look;
        ParticleSystem _wisp, _puff;
        bool _puffed;
        ComboSystem _combo;

        // ------------------------------------------------------------------ Aufbau

        /// <summary>Sofa und die drei Figuren an den Platz stellen.</summary>
        public static void SpawnHangout()
        {
            if (Resources.Load<GameObject>("Characters/NPC_Kalle") == null) return;
            var group = new GameObject("Stoners").transform;
            Vector3 pos = Ground(HangoutPosition);
            Vector3 fwd = HangoutFacing;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            var couch = SpawnCouch(pos, fwd, group);
            Spawn("Kalle", pos - right * 0.37f, fwd, group);
            Spawn("Jojo", Ground(pos + right * 1.45f + fwd * 0.45f), Quaternion.Euler(0, -32f, 0) * fwd, group);
            Spawn("Luna", Ground(pos - right * 1.5f + fwd * 0.55f), Quaternion.Euler(0, 28f, 0) * fwd, group);
            if (couch == null) Debug.LogWarning("StonerNpc: Sofa fehlt");
        }

        internal static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, ~LayerMask.GetMask("Rail", "Skater", "Car"), QueryTriggerInteraction.Ignore))
                p.y = hit.point.y;
            return p;
        }

        public static StonerNpc Spawn(string id, Vector3 position, Vector3 facing, Transform parent = null)
        {
            var prefab = Resources.Load<GameObject>("Characters/NPC_" + id);
            if (prefab == null)
            {
                Debug.LogWarning("StonerNpc: Modell fehlt: Characters/NPC_" + id);
                return null;
            }
            var root = new GameObject("NPC_" + id);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing, Vector3.up));
            var model = Instantiate(prefab, root.transform, false);
            model.name = "Model";
            ApplyToon(model);
            var npc = root.AddComponent<StonerNpc>();
            npc.Init(id, model.transform);
            return npc;
        }

        /// <summary>Fertiges Prefab (eigene Materialien, Animator im Kind-Objekt) als NPC aufstellen.</summary>
        public static StonerNpc SpawnPrefab(string id, GameObject prefab, Vector3 position, Vector3 facing, Transform parent = null)
        {
            var root = new GameObject("NPC_" + id);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing, Vector3.up));
            var instance = Instantiate(prefab, root.transform, false);
            instance.name = "Model";
            var anim = instance.GetComponentInChildren<Animator>();
            var npc = root.AddComponent<StonerNpc>();
            npc.Init(id, anim != null ? anim.transform : instance.transform);
            return npc;
        }

        static GameObject SpawnCouch(Vector3 position, Vector3 facing, Transform parent)
        {
            var prefab = Resources.Load<GameObject>("Characters/NPC_Couch");
            if (prefab == null) return null;
            var root = new GameObject("Couch");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing, Vector3.up));
            var model = Instantiate(prefab, root.transform, false);
            model.name = "Model";
            ApplyToon(model);

            // Lehne = hoechster Teil; die Sitzflaeche soll nach vorn (facing) zeigen
            var mf = model.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable)
            {
                Vector3 all = Vector3.zero, top = Vector3.zero;
                int na = 0, nt = 0;
                var verts = mf.sharedMesh.vertices;
                float maxY = float.MinValue;
                foreach (var v in verts) maxY = Mathf.Max(maxY, mf.transform.TransformPoint(v).y);
                foreach (var v in verts)
                {
                    Vector3 w = mf.transform.TransformPoint(v);
                    all += w; na++;
                    if (w.y > maxY - 0.15f) { top += w; nt++; }
                }
                Vector3 back = Vector3.ProjectOnPlane(top / nt - all / na, Vector3.up);
                if (back.sqrMagnitude > 1e-4f)
                {
                    float yaw = Vector3.SignedAngle(-back.normalized, root.transform.forward, Vector3.up);
                    model.transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * model.transform.rotation;
                }
            }
            // Kollision: Sitz-Block und Lehne
            var seat = root.AddComponent<BoxCollider>();
            seat.center = new Vector3(0f, 0.24f, 0f);
            seat.size = new Vector3(1.9f, 0.48f, 0.85f);
            var backrest = root.AddComponent<BoxCollider>();
            backrest.center = new Vector3(0f, 0.55f, -0.31f);
            backrest.size = new Vector3(1.9f, 0.55f, 0.23f);
            return root;
        }

        /// <summary>Toon-Materialien: Farbe und Art stehen im Materialnamen (z. B. "Jojo_Skin__E9B996_flat").</summary>
        internal static void ApplyToon(GameObject model)
        {
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = Toon(mats[i]);
                r.sharedMaterials = mats;
                if (r is SkinnedMeshRenderer smr)
                {
                    // Bounds fuer alle Posen (Sitzen, Arme hoch)
                    var b = smr.localBounds;
                    b.Expand(0.8f);
                    smr.localBounds = b;
                    smr.updateWhenOffscreen = false;
                }
            }
        }

        static readonly Regex ColorTag = new Regex("__([0-9A-Fa-f]{6})");

        static Material Toon(Material src)
        {
            string n = src != null ? src.name : "";
            var m = ColorTag.Match(n);
            Color c = src != null ? src.color : Color.magenta;
            if (m.Success && ColorUtility.TryParseHtmlString("#" + m.Groups[1].Value, out var parsed)) c = parsed;
            bool flat = n.Contains("_flat"), glow = n.Contains("_glow");
            return ToonMaterials.Get(c, flat ? 0f : 0.22f, false, glow ? 2f : 0f);
        }

        void Init(string id, Transform model)
        {
            Id = id;
            _anim = model.GetComponent<Animator>();
            if (_anim == null) _anim = model.gameObject.AddComponent<Animator>();
            _anim.applyRootMotion = false;
            _anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var clip in Resources.LoadAll<AnimationClip>("Characters/NPC_" + id))
                if (!clip.name.StartsWith("__preview__")) _clips[clip.name] = clip;

            // Blickrichtung aus der Hueftlinie (wie bei der Spielfigur), Modell nach vorn drehen
            var legL = FindBone(model, "UpperLeg_L");
            var legR = FindBone(model, "UpperLeg_R");
            if (legL != null && legR != null)
            {
                Vector3 left = Vector3.ProjectOnPlane(legL.position - legR.position, Vector3.up);
                if (left.sqrMagnitude > 1e-6f)
                {
                    Vector3 facing = Vector3.Cross(Vector3.up, left.normalized);
                    float yaw = Vector3.SignedAngle(facing, transform.forward, Vector3.up);
                    model.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * model.rotation;
                }
            }
            _head = FindBone(model, "Head");
            _jointTip = FindBone(model, "JointTip");
            if (_head != null)
            {
                _headFaceLocal = _head.InverseTransformDirection(transform.forward);
                // Mund: 10 cm vor und 5 cm ueber dem Kopfknochen (der sitzt am Hals)
                float k = Mathf.Max(0.5f, Vector3.Distance(_head.position, model.position) / 1.6f);
                _mouthLocal = _head.InverseTransformPoint(_head.position + transform.forward * 0.1f * k + Vector3.up * 0.048f * k);
            }
            Specials.TryGetValue(id, out _special);

            _graph = PlayableGraph.Create("Stoner_" + id);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "Out", _anim);
            _mixer = AnimationMixerPlayable.Create(_graph, 3);
            output.SetSourcePlayable(_mixer);
            if (_clips.TryGetValue("Idle", out var idle))
            {
                _idle = AnimationClipPlayable.Create(_graph, idle);
                _graph.Connect(_idle, 0, _mixer, 0);
                _mixer.SetInputWeight(0, 1f);
                _idle.SetTime(Random.Range(0f, idle.length));
            }
            else Debug.LogWarning($"StonerNpc {id}: keine Idle-Animation ({_clips.Count} Clips)");
            // Sitzen als zweite Grundhaltung (Eingang 2), wird mit dem Idle ueberblendet
            if (_clips.TryGetValue("Sitting", out var sit))
            {
                _sit = AnimationClipPlayable.Create(_graph, sit);
                _graph.Connect(_sit, 0, _mixer, 2);
                _sit.SetTime(Random.Range(0f, sit.length));
            }
            _graph.Play();

            if (_jointTip != null) BuildSmoke();
            if (id == "Jojo") gameObject.AddComponent<JojoQuests>();
            if (id == "Luna") gameObject.AddComponent<LunaChallenges>();
            if (id == "Nix") gameObject.AddComponent<NixDealer>();
            if (id == "Moe") gameObject.AddComponent<MoeQuests>();
            _nextSpecial = Time.time + Random.Range(3f, 9f);
            All.Add(this);
        }

        static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        // ------------------------------------------------------------------ Animation

        public bool HasClip(string name) => _clips.ContainsKey(name);

        /// <summary>Soll sitzen (true) oder stehen; das Ueberblenden dauert gut eine halbe Sekunde (sofort: ohne Blende).</summary>
        public void SetSitting(bool sit, bool instant = false)
        {
            if (!_sit.IsValid()) sit = false;
            _sitTarget = sit ? 1f : 0f;
            if (instant) _sitWeight = _sitTarget;
            ApplyWeights();
        }

        public bool Sitting => _sitTarget > 0.5f;
        /// <summary>0 = steht, 1 = sitzt (waehrend des Aufstehens dazwischen, geglaettet).</summary>
        public float SitAmount => Mathf.SmoothStep(0f, 1f, _sitWeight);

        void ApplyWeights()
        {
            float s = _sit.IsValid() ? SitAmount : 0f;
            _mixer.SetInputWeight(0, (1f - _actionWeight) * (1f - s));
            _mixer.SetInputWeight(1, _actionWeight);
            _mixer.SetInputWeight(2, (1f - _actionWeight) * s);
        }
        public int SmokeParticles => (_wisp != null ? _wisp.particleCount : 0) + (_puff != null ? _puff.particleCount : 0);
        public IEnumerable<string> ClipNames => _clips.Keys;

        /// <summary>Eine Animation einblenden (danach geht es zurueck ins Idle).</summary>
        public bool Play(string name)
        {
            if (!_clips.TryGetValue(name, out var clip) || !_graph.IsValid()) return false;
            if (_action.IsValid())
            {
                _graph.Disconnect(_mixer, 1);
                _action.Destroy();
            }
            _action = AnimationClipPlayable.Create(_graph, clip);
            _graph.Connect(_action, 0, _mixer, 1);
            _action.SetTime(0);
            CurrentAction = name;
            _actionTime = 0f;
            _actionEnd = clip.isLooping ? clip.length * 2f : clip.length;
            _puffed = false;
            return true;
        }

        /// <summary>Fuer Vorschau-Bilder im Editor: Pose einer Animation zu einem Zeitpunkt sofort anwenden.</summary>
        public void Preview(string clip, float time)
        {
            if (!_graph.IsValid()) return;
            if (clip != null && clip != "Idle" && Play(clip))
            {
                _action.SetTime(time);
                _actionWeight = 1f;
            }
            else
            {
                if (_idle.IsValid()) _idle.SetTime(time);
                if (_sit.IsValid()) _sit.SetTime(time);
                _actionWeight = 0f;
                CurrentAction = null;
            }
            ApplyWeights();
            _graph.Evaluate(0f);
        }

        void Update()
        {
            if (!_graph.IsValid()) return;
            float dt = Time.deltaTime;
            // In der Naehe immer animieren, weiter weg nur, wenn sichtbar (spart Rechenzeit in der grossen Stadt)
            var culling = PlayerNear(60f) ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullUpdateTransforms;
            if (_anim.cullingMode != culling) _anim.cullingMode = culling;

            if (CurrentAction != null)
            {
                _actionTime += dt;
                float fadeIn = Mathf.Clamp01(_actionTime / 0.35f);
                float fadeOut = Mathf.Clamp01((_actionEnd - _actionTime) / 0.4f);
                _actionWeight = Mathf.SmoothStep(0f, 1f, Mathf.Min(fadeIn, fadeOut));
                if (_action.IsValid() && _action.GetAnimationClip().isLooping)
                {
                    float len = _action.GetAnimationClip().length;
                    if (_action.GetTime() > len) _action.SetTime(_action.GetTime() % len);
                }
                if (CurrentAction == "Smoke" && !_puffed && _actionTime > 2.55f)
                {
                    _puffed = true;
                    Exhale();
                }
                if (_actionTime >= _actionEnd)
                {
                    CurrentAction = null;
                    _actionWeight = 0f;
                }
            }
            foreach (var loop in new[] { _idle, _sit })
            {
                if (!loop.IsValid()) continue;
                float len = loop.GetAnimationClip().length;
                if (loop.GetTime() > len) loop.SetTime(loop.GetTime() % len);
            }
            _sitWeight = Mathf.MoveTowards(_sitWeight, _sitTarget, dt / 0.7f);
            ApplyWeights();

            if (CurrentAction == null && Time.time > _nextSpecial && _special != null)
            {
                Play(_special);
                _nextSpecial = Time.time + _actionEnd + Random.Range(8f, 16f);
            }
            if (_pendingLaugh > 0f && Time.time > _pendingLaugh)
            {
                _pendingLaugh = -1f;
                if (CurrentAction != "Smoke") Play("Laugh");
            }

            var local = PlayerAvatar.Local;
            var combo = local != null ? local.combo : null;
            if (combo != _combo)
            {
                Unsubscribe();
                _combo = combo;
                if (_combo != null)
                {
                    _combo.ActionAdded += OnPlayerAction;
                    _combo.Ended += OnComboEnded;
                }
            }
        }

        void LateUpdate()
        {
            if (_head == null) return;
            // Kopf langsam zum Spieler drehen, wenn er nah und vor der Figur ist
            float want = 0f;
            if (PlayerHead(out Vector3 target))
            {
                Vector3 dir = target - _head.position;
                float dist = dir.magnitude;
                float ang = Vector3.Angle(transform.forward, Vector3.ProjectOnPlane(dir, Vector3.up));
                if (dist > 0.4f && dist < 10f && ang < 105f)
                {
                    want = 1f;
                    _lookDir = dir / dist;
                }
            }
            if (CurrentAction == "Smoke" || CurrentAction == "Snack") want *= 0.35f;
            _look = Mathf.MoveTowards(_look, want, Time.deltaTime * 0.9f);
            if (_look <= 0.001f || _lookDir == Vector3.zero) return;
            Vector3 cur = _head.TransformDirection(_headFaceLocal);
            Quaternion turn = Quaternion.FromToRotation(cur, _lookDir);
            turn.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            angle = Mathf.Clamp(angle, -60f, 60f) * Mathf.SmoothStep(0f, 1f, _look);
            _head.rotation = Quaternion.AngleAxis(angle, axis) * _head.rotation;
        }

        static bool PlayerHead(out Vector3 pos)
        {
            pos = default;
            var p = PlayerAvatar.Local;
            if (p == null) return false;
            Transform t = p.Mode == PlayerMode.Driving ? (p.car != null ? p.car.transform : null) : (p.skater != null ? p.skater.transform : null);
            if (t == null) return false;
            pos = t.position + Vector3.up * (p.Mode == PlayerMode.Driving ? 1.1f : 1.55f);
            return true;
        }

        bool PlayerNear(float radius) => PlayerHead(out Vector3 p) && (p - transform.position).sqrMagnitude < radius * radius;

        void OnPlayerAction(string label, int points)
        {
            if (!PlayerNear(18f) || Time.time < _laughCooldown) return;
            _pendingLaugh = Time.time + Random.Range(0.25f, 0.8f);
            _laughCooldown = Time.time + Random.Range(5f, 9f);
            Say(Cheers);
        }

        void OnComboEnded(long amount, bool failed, string reason)
        {
            if (!failed || !PlayerNear(18f) || Time.time < _laughCooldown) return;
            _pendingLaugh = Time.time + Random.Range(0.3f, 0.7f);
            _laughCooldown = Time.time + 6f;
            Say(Fails);
        }

        void Say(Dictionary<string, string[]> lines)
        {
            if (Time.time < _lineCooldown || !PlayerNear(10f) || HUD.Instance == null) return;
            if (!lines.TryGetValue(Id, out var options)) return;
            if (_head == null || QuestNpc.AnyTalking) return;
            _lineCooldown = Time.time + 30f;
            if (_bark != null && !_bark.Closed) _bark.Close();
            _bark = SpeechBubble.Show(_head, Vector3.up * 0.3f, Id.ToUpperInvariant(), NameColor(Id), options[Random.Range(0, options.Length)], false, 2.6f);
        }

        static Color NameColor(string id)
        {
            switch (id)
            {
                case "Jojo": return JojoQuests.BeanieColor;
                case "Kalle": return Palette.Cyan;
                case "Nix": return NixDealer.NameColor;
                case "Moe": return MoeQuests.NameColor;
                case "Miru": return MiruTalk.NameColor;
                default: return Palette.Pink;
            }
        }

        // ------------------------------------------------------------------ Rauch

        void BuildSmoke()
        {
            _wisp = MakeSmoke("Wisp", _jointTip, 0.012f, 0.028f, 1.8f, 0.3f);
            var em = _wisp.emission;
            em.rateOverTime = 5f;
            _puff = MakeSmoke("Puff", transform, 0.04f, 0.08f, 2.2f, 0.42f);
        }

        internal static ParticleSystem MakeSmoke(string name, Transform parent, float sizeMin, float sizeMax, float life, float alpha)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.012f;
            main.maxParticles = 60;
            main.startColor = new Color(0.82f, 0.82f, 0.88f, alpha);
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.005f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 2.6f));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.4f, 0.55f), new GradientAlphaKey(0f, 1f) });
            color.color = grad;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.08f;
            noise.frequency = 0.6f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("SmokeMat");
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        /// <summary>Ausatmen: kleine Wolke vor dem Mund, leicht nach vorn und oben.</summary>
        void Exhale()
        {
            if (_puff == null || _head == null) return;
            Vector3 mouth = _head.TransformPoint(_mouthLocal);
            Vector3 dir = (_head.TransformDirection(_headFaceLocal) + Vector3.up * 0.6f).normalized;
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < 14; i++)
            {
                p.position = mouth + Random.insideUnitSphere * 0.02f;
                p.velocity = dir * Random.Range(0.25f, 0.55f) + Random.insideUnitSphere * 0.06f;
                p.startSize = Random.Range(0.035f, 0.07f);
                p.startLifetime = Random.Range(1.6f, 2.6f);
                _puff.Emit(p, 1);
            }
        }

        // ------------------------------------------------------------------ Aufraeumen

        void Unsubscribe()
        {
            if (_combo == null) return;
            _combo.ActionAdded -= OnPlayerAction;
            _combo.Ended -= OnComboEnded;
            _combo = null;
        }

        void OnDestroy()
        {
            Unsubscribe();
            if (_graph.IsValid()) _graph.Destroy();
            All.Remove(this);
        }
    }
}
