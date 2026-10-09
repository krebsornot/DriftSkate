using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Unterboden-Neon: leuchtende Roehren unter den Schwellern und vorn/hinten, dazu ein weicher Lichtschein auf dem
    /// Boden (eine flache, transparente Flaeche, die jedes Bild auf den Boden unter dem Auto gelegt wird; echte Lichter
    /// wuerden die Stadt zu viel Rechenzeit kosten). Modi: an, pulsierend, Regenbogen. Hebt das Auto ab, verblasst der Schein.
    /// </summary>
    public class Underglow : MonoBehaviour
    {
        public static readonly string[] Modes = { "AUS", "AN", "PULS", "REGENBOGEN" };

        int _mode;
        Color _color;
        Material _tubeMat, _glowMat;
        Transform _glow;
        Vector2 _glowSize;
        float _alpha;
        bool _first = true, _bodyLooked;
        Rigidbody _body;
        static Texture2D _falloff;

        /// <summary>Unter das Auto bauen (Aufruf aus CarBuilder, nur wenn Neon an ist).</summary>
        public static void Build(Transform visual, float track, float wheelbase, float length, float wheelRadius, float sill, int mode, Color color)
        {
            var ug = visual.gameObject.AddComponent<Underglow>();
            ug._mode = mode;
            ug._color = color;
            ug._tubeMat = new Material(ToonMaterials.Get(color, 0f, true, 1.3f)) { name = "NeonTube" };

            // Roehren knapp unter der Schwelle zwischen den Raedern, und quer vorn und hinten
            float y = Mathf.Max(0.12f, Mathf.Min(sill, 0.32f) - 0.06f);
            float sideLen = Mathf.Max(0.6f, wheelbase - wheelRadius * 2f - 0.25f);
            float sideX = track * 0.5f - 0.12f;
            for (int s = -1; s <= 1; s += 2)
                Tube(visual, ug._tubeMat, new Vector3(s * sideX, y, 0f), new Vector3(0.045f, 0.045f, sideLen));
            float endZ = wheelbase * 0.5f + wheelRadius + 0.12f;
            for (int s = -1; s <= 1; s += 2)
                Tube(visual, ug._tubeMat, new Vector3(0f, y, s * Mathf.Min(endZ, length * 0.5f - 0.25f)), new Vector3(track - 0.55f, 0.045f, 0.045f));

            // Lichtschein auf dem Boden
            var baseMat = Resources.Load<Material>("TrailMat");
            if (baseMat != null)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(quad.GetComponent<Collider>()); // sofort, sonst meldet der Rigidbody des Autos einen Mesh-Collider
                quad.name = "NeonGlow";
                quad.transform.SetParent(visual, false);
                ug._glowMat = new Material(baseMat) { name = "NeonGlow" };
                ug._glowMat.SetTexture("_BaseMap", Falloff);
                ug._glowMat.SetFloat("_FogAmount", 0.6f);
                var r = quad.GetComponent<MeshRenderer>();
                r.sharedMaterial = ug._glowMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                ug._glow = quad.transform;
                ug._glowSize = new Vector2(track + 1.9f, length + 2f);
            }
            ug.Apply(0f);
        }

        static void Tube(Transform parent, Material mat, Vector3 pos, Vector3 size)
        {
            var go = Shapes.Box(parent, pos, size, Color.white, 0f, name: "NeonTube");
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Weicher, abgerundeter Verlauf: in der Mitte unter dem Auto am hellsten, zum Rand hin aus.</summary>
        static Texture2D Falloff
        {
            get
            {
                if (_falloff != null) return _falloff;
                const int w = 64, h = 128;
                _falloff = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "NeonFalloff", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        // Abstand zu einem kleinen abgerundeten Kern (Auto-Grundriss), sehr weich auslaufend (wie gestreutes Licht)
                        float u = Mathf.Abs((x + 0.5f) / w * 2f - 1f), v = Mathf.Abs((y + 0.5f) / h * 2f - 1f);
                        float dx = Mathf.Max(0f, u - 0.15f) / 0.85f, dy = Mathf.Max(0f, v - 0.45f) / 0.55f;
                        float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                        float a = Mathf.Exp(-d * d * 4.5f) * (1f - d * d);
                        px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                _falloff.SetPixels32(px);
                _falloff.Apply();
                return _falloff;
            }
        }

        void LateUpdate() => Apply(Time.time);

        Color Current(float t)
        {
            switch (_mode)
            {
                case 2: return _color * Mathf.Lerp(0.5f, 1f, Mathf.SmoothStep(0f, 1f, 0.5f + 0.5f * Mathf.Sin(t * 1.8f)));
                case 3: return Color.HSVToRGB(Mathf.Repeat(t * 0.12f, 1f), 0.75f, 1f);
                default: return _color;
            }
        }

        void Apply(float t)
        {
            Color c = Current(t);
            if (_tubeMat != null) _tubeMat.SetColor("_BaseColor", c);
            if (_glow == null) return;

            // Boden unter der Wagenmitte suchen; je hoeher das Auto, desto schwaecher der Schein
            Vector3 center = transform.position + transform.up * 0.5f;
            int mask = ~LayerMask.GetMask("Car", "Skater", "Rail", "Ignore Raycast");
            float target = 0f;
            bool found = Physics.Raycast(center, Vector3.down, out RaycastHit hit, 50f, mask, QueryTriggerInteraction.Ignore);
            // Kein Boden mit Collider oder nur weit darunter, obwohl das Auto steht (Vorschau in der Garage): direkt unter das Auto legen
            if (!_bodyLooked) { _body = GetComponentInParent<Rigidbody>(); _bodyLooked = true; }
            var body = _body;
            if (!found || (hit.distance > 2.5f && (body == null || body.isKinematic)))
            {
                hit.point = transform.position + transform.up * 0.06f; // Raeder stehen leicht im Drehteller
                hit.normal = transform.up;
                hit.distance = 0.5f;
            }
            {
                float height = hit.distance - 0.5f;
                target = 1f - Mathf.InverseLerp(0.4f, 1.8f, height);
                Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, hit.normal);
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(transform.up, hit.normal);
                // Quad: sichtbare Seite zeigt nach -z -> -z nach oben (Bodennormale), y entlang der Wagenlaenge
                _glow.SetPositionAndRotation(hit.point + hit.normal * 0.025f, Quaternion.LookRotation(-hit.normal, fwd.normalized));
                _glow.localScale = new Vector3(_glowSize.x / Mathf.Max(1e-3f, transform.lossyScale.x), _glowSize.y / Mathf.Max(1e-3f, transform.lossyScale.z), 1f);
            }
            _alpha = _first ? target : Mathf.MoveTowards(_alpha, target, Time.deltaTime * 2.5f);
            _first = false;
            _glow.gameObject.SetActive(_alpha > 0.01f);
            // Sanft: nicht ueberhellt, halb durchsichtig, der weiche Verlauf macht den Rest
            _glowMat.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 0.6f * _alpha));
        }

        void OnDestroy()
        {
            if (_tubeMat != null) Shapes.DestroySafe(_tubeMat);
            if (_glowMat != null) Shapes.DestroySafe(_glowMat);
        }
    }
}
