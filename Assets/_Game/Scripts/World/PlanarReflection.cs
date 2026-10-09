using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DriftSkate
{
    /// <summary>
    /// Spiegelungen auf nassem Boden ("Raytracing-Look" ohne RTX): eine zweite Kamera steht unter dem Boden, schaut
    /// gespiegelt nach oben und rendert die Stadt in eine Textur. Der Toon-Shader zeigt sie auf nassen Flaechen
    /// (_DS_ReflectionTex). Die Kamera ist eine echte Drehung (keine Spiegelmatrix), daher bleibt das Culling normal;
    /// das Bild ist dadurch waagerecht gespiegelt und wird im Shader mit u = 1 - u gelesen.
    /// Gerendert wird nur, solange der Boden nass ist und die Option an ist.
    /// </summary>
    public class PlanarReflection : MonoBehaviour
    {
        public static PlanarReflection Instance { get; private set; }

        public float planeHeight = 0.05f;
        public float resolutionScale = 0.5f;
        public float farClip = 380f;

        Camera _main, _refl;
        RenderTexture _rt;
        float _savedWet;

        public bool Active => _refl != null && _refl.enabled;

        void OnEnable()
        {
            Instance = this;
            RenderPipelineManager.beginContextRendering += OnBeginContext;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContext;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            Shader.SetGlobalFloat("_DS_ReflectionOn", 0f);
            if (_refl != null) _refl.enabled = false;
        }

        void OnDestroy()
        {
            if (_rt != null) { _rt.Release(); Shapes.DestroySafe(_rt); }
            if (_refl != null) Shapes.DestroySafe(_refl.gameObject);
            if (Instance == this) Instance = null;
        }

        bool Setup()
        {
            if (_main == null) _main = Camera.main;
            if (_main == null) return false;
            if (_refl == null)
            {
                var go = new GameObject("ReflectionCamera");
                go.transform.SetParent(transform, false);
                _refl = go.AddComponent<Camera>();
                _refl.enabled = false;
                var data = _refl.GetUniversalAdditionalCameraData();
                data.renderShadows = false;
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                data.requiresDepthTexture = false;
                data.requiresColorTexture = false;
            }
            int w = Mathf.Max(64, Mathf.RoundToInt(_main.pixelWidth * resolutionScale));
            int h = Mathf.Max(64, Mathf.RoundToInt(_main.pixelHeight * resolutionScale));
            if (_rt == null || _rt.width != w || _rt.height != h)
            {
                if (_rt != null) { _rt.Release(); Shapes.DestroySafe(_rt); }
                _rt = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR) { name = "PlanarReflection", useMipMap = false };
                _refl.targetTexture = _rt;
                Shader.SetGlobalTexture("_DS_ReflectionTex", _rt);
            }
            return true;
        }

        /// <summary>Kamera an die gespiegelte Position bringen (vor dem Rendern jedes Frames).</summary>
        public void UpdateCamera()
        {
            if (!Setup()) return;
            Transform m = _main.transform;
            Vector3 p = m.position, f = m.forward, u = m.up;
            _refl.CopyFrom(_main); // uebernimmt auch die Position, daher danach spiegeln
            _refl.transform.SetPositionAndRotation(new Vector3(p.x, 2f * planeHeight - p.y, p.z), Quaternion.LookRotation(new Vector3(f.x, -f.y, f.z), new Vector3(u.x, -u.y, u.z)));
            _refl.targetTexture = _rt;
            _refl.depth = _main.depth - 1f;
            _refl.farClipPlane = Mathf.Min(_main.farClipPlane, farClip);
            _refl.clearFlags = CameraClearFlags.Skybox;
            _refl.cullingMask = _main.cullingMask & ~LayerMask.GetMask("UI", "Detail");
            _refl.ResetProjectionMatrix();
            // Alles unter der Bodenebene wegschneiden (schraege Near-Plane)
            Matrix4x4 w2c = _refl.worldToCameraMatrix;
            Vector3 cpos = w2c.MultiplyPoint(new Vector3(0f, planeHeight, 0f));
            Vector3 cnorm = w2c.MultiplyVector(Vector3.up).normalized;
            _refl.projectionMatrix = _refl.CalculateObliqueMatrix(new Vector4(cnorm.x, cnorm.y, cnorm.z, -Vector3.Dot(cpos, cnorm)));
        }

        /// <summary>Fuer Screenshots im Editor: sofort einmal spiegeln.</summary>
        public void RenderNow()
        {
            UpdateCamera();
            if (_refl == null) return;
            Shader.SetGlobalFloat("_DS_ReflectionOn", 1f);
            _refl.Render();
        }

        void LateUpdate()
        {
            bool want = SaveSystem.Profile.reflections && WeatherSystem.Wetness > 0.01f && Setup();
            if (_refl != null) _refl.enabled = want;
            Shader.SetGlobalFloat("_DS_ReflectionOn", want ? 1f : 0f);
        }

        void OnBeginContext(ScriptableRenderContext ctx, System.Collections.Generic.List<Camera> cams)
        {
            if (_refl != null && _refl.enabled) UpdateCamera();
        }

        // Die Spiegel-Kamera selbst sieht trockenen Boden (sonst liest sie ihre eigene Textur)
        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _refl) return;
            _savedWet = Shader.GetGlobalFloat("_DS_Wet");
            Shader.SetGlobalFloat("_DS_Wet", 0f);
        }

        void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _refl) return;
            Shader.SetGlobalFloat("_DS_Wet", _savedWet);
        }
    }
}
