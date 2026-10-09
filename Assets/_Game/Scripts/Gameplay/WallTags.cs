using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Freies Taggen: zu Fuss (vom Board abgestiegen) das eigene Graffiti an jede Wand spruehen, vor der man steht.
    /// Andere Spieler sehen dasselbe Graffiti (bis es uebertragen ist, ein Standard-Tag in der Crew-Farbe). Haelt nur fuer die Sitzung; die aeltesten verschwinden ab 60 Stueck.
    /// </summary>
    public static class WallTags
    {
        const float Reach = 2.4f, Size = 1.7f, SmallSize = 1.0f;
        const int MaxTags = 60;

        static readonly List<GameObject> Tags = new List<GameObject>();
        static Transform _root;
        static Material _own;
        static readonly Dictionary<Color, Material> Remote = new Dictionary<Color, Material>();
        static Mesh _quad;

        /// <summary>Warum zuletzt keine Stelle gefunden wurde (fuer Tests).</summary>
        public static string LastReason { get; private set; }

        static int Mask => ~LayerMask.GetMask("Skater", "Car", "Rail", "Ignore Raycast");

        /// <summary>Freie Wandstelle vor der Figur suchen (geradeaus, sonst leicht links/rechts).</summary>
        public static bool Find(SkaterController skater, out Vector3 point, out Vector3 normal, out float size)
        {
            point = normal = default;
            size = 0f;
            Vector3 origin = skater.transform.position + Vector3.up * 1.35f;
            LastReason = "keine Wand in Reichweite";
            foreach (float yaw in new[] { 0f, -22f, 22f })
            {
                Vector3 dir = Quaternion.Euler(0f, skater.Heading + yaw, 0f) * Vector3.forward;
                if (!Physics.Raycast(origin, dir, out RaycastHit hit, Reach, Mask, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(hit.normal.y) > 0.35f || hit.rigidbody != null) { LastReason = "nicht senkrecht/fest: " + hit.collider.name; continue; } // nur senkrechte, feste Waende
                Vector3 n = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
                foreach (float s in new[] { Size, SmallSize })
                {
                    // Tag nicht zu tief haengen lassen (Unterkante mind. 25 cm ueber dem Boden der Figur)
                    Vector3 c = hit.point;
                    c.y = Mathf.Max(c.y, skater.transform.position.y + 0.25f + s * 0.5f);
                    if (!Fits(c, n, s)) { LastReason = $"passt nicht ({hit.collider.name}, {s} m, Hoehe {c.y - skater.transform.position.y:0.00})"; continue; }
                    point = c;
                    normal = n;
                    size = s;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Alle vier Ecken muessen auf derselben flachen Wand liegen (nicht ueber Kanten oder ins Leere).</summary>
        static bool Fits(Vector3 center, Vector3 n, float size)
        {
            Vector3 right = Vector3.Cross(Vector3.up, n).normalized;
            float h = size * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = center + right * (i % 2 == 0 ? -h : h) + Vector3.up * (i < 2 ? -h : h);
                if (!Physics.Raycast(corner + n * 0.35f, -n, out RaycastHit hit, 0.6f, Mask, QueryTriggerInteraction.Ignore)) return false;
                if (Vector3.Dot(hit.normal, n) < 0.9f) return false;
            }
            return true;
        }

        /// <summary>Eigenes Tag spruehen. true = es gab Punkte (nicht direkt neben einem eigenen Tag).</summary>
        public static bool PlaceLocal(Vector3 point, Vector3 normal, float size, Color crew)
        {
            bool fresh = true;
            foreach (var t in Tags)
                if (t != null && (t.transform.position - point).sqrMagnitude < 2.5f * 2.5f) { fresh = false; break; }
            if (_own == null) _own = Decal(SaveSystem.Graffiti);
            Spawn(point, normal, size, _own, crew);
            return fresh;
        }

        /// <summary>Tag eines anderen Spielers: sein Graffiti (custom), sonst Standard-Tag in seiner Crew-Farbe.</summary>
        public static void PlaceRemote(Vector3 point, Vector3 normal, float size, Color crew, Material custom = null)
        {
            var mat = custom;
            if (mat == null && (!Remote.TryGetValue(crew, out mat) || mat == null))
            {
                var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                GraffitiPainter.DrawDefaultTag(tex);
                mat = Decal(tex);
                mat.SetColor("_BaseColor", Color.Lerp(Color.white, crew, 0.6f));
                Remote[crew] = mat;
            }
            Spawn(point, normal, Mathf.Clamp(size, 0.5f, 2.5f), mat, crew);
        }

        /// <summary>Doppelseitiges Graffiti-Material (auch fuer die Tags anderer Spieler).</summary>
        public static Material Decal(Texture tex)
        {
            var m = ToonMaterials.CreateDecal(tex);
            m.SetFloat("_Cull", 0f); // doppelseitig
            return m;
        }

        static void Spawn(Vector3 point, Vector3 normal, float size, Material mat, Color crew)
        {
            if (_root == null)
            {
                _root = new GameObject("WallTags").transform;
                Tags.Clear();
            }
            Tags.RemoveAll(t => t == null);
            while (Tags.Count >= MaxTags)
            {
                Object.Destroy(Tags[0]);
                Tags.RemoveAt(0);
            }

            var go = new GameObject("WallTag");
            go.transform.SetParent(_root, false);
            // Vor Schaufenstern und Tueren (stehen bis 12 cm aus der Wand, ohne Collider); neuere Tags minimal weiter vorn
            go.transform.position = point + normal * (0.135f + 0.002f * (Tags.Count % 8));
            go.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = Quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<TagPop>();
            Tags.Add(go);
            SprayPuff(point + normal * 0.25f, normal, crew);
        }

        /// <summary>Quad 1x1, Vorderseite zeigt nach -Z (wie Unitys Quad), UVs ungespiegelt.</summary>
        static Mesh Quad
        {
            get
            {
                if (_quad != null) return _quad;
                _quad = new Mesh { name = "WallTagQuad" };
                _quad.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) };
                _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                _quad.triangles = new[] { 0, 3, 1, 0, 2, 3 };
                _quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                _quad.RecalculateBounds();
                return _quad;
            }
        }

        /// <summary>Kurze Farbwolke aus der Dose.</summary>
        static void SprayPuff(Vector3 pos, Vector3 normal, Color crew)
        {
            var go = new GameObject("SprayPuff");
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = 0.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startColor = new Color(crew.r, crew.g, crew.b, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 2f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Resources.Load<Material>("SmokeMat");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Vector3 right = Vector3.Cross(Vector3.up, normal).normalized;
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < 16; i++)
            {
                p.position = pos + right * Random.Range(-0.5f, 0.5f) + Vector3.up * Random.Range(-0.5f, 0.5f);
                p.velocity = normal * Random.Range(0.1f, 0.5f) + Random.insideUnitSphere * 0.15f;
                ps.Emit(p, 1);
            }
            ps.Play();
        }

        /// <summary>Tag erscheint mit kurzem Aufploppen statt schlagartig.</summary>
        class TagPop : MonoBehaviour
        {
            Vector3 _scale;
            float _t;

            void Start()
            {
                _scale = transform.localScale;
                transform.localScale = _scale * 0.6f;
            }

            void Update()
            {
                _t += Time.deltaTime / 0.35f;
                float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(_t), 3f);
                transform.localScale = _scale * Mathf.Lerp(0.6f, 1f, k);
                if (_t >= 1f) Destroy(this);
            }
        }
    }
}
