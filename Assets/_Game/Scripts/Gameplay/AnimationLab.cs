using UnityEngine;
using UnityEngine.InputSystem;

namespace DriftSkate
{
    /// <summary>Isolated, offline playground using the real player controller and skeleton.</summary>
    public class AnimationLab : MonoBehaviour
    {
        SkaterController _skater;
        Camera _camera;
        float _orbit = 145f;
        bool _enhanced = true;
        GUIStyle _title, _text;

        void Start()
        {
            GameInput.Ensure();
            var go = new GameObject("Animation Lab Player");
            int layer = LayerMask.NameToLayer("Skater");
            if (layer >= 0) go.layer = layer;
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0, .95f, 0);
            capsule.height = 1.5f;
            capsule.radius = .28f;
            go.AddComponent<Rigidbody>().mass = 75f;
            _skater = go.AddComponent<SkaterController>();
            SkaterBuilder.Build(go.transform, SkaterBuilder.Outfit.FromIds(null), Catalog.Boards[0], null, _skater);
            _skater.Place(new Vector3(0, .1f, -8), 0);
            if (_skater.rig != null) _skater.rig.proceduralLife = 1f;
            else Debug.LogError("Animation Lab: character skeleton unavailable.");
            _camera = Camera.main;
        }

        void Update()
        {
            if (_skater == null) return;
            var kb = Keyboard.current;
            if (kb != null && kb.vKey.wasPressedThisFrame)
            {
                _enhanced = !_enhanced;
                if (_skater.rig != null) _skater.rig.proceduralLife = _enhanced ? 1f : 0f;
            }
            if (GameInput.Pressed(GameInput.ResetCar) || _skater.transform.position.y < -5f)
                _skater.Place(new Vector3(0, .1f, -8), 0);
            if (GameInput.Pressed(GameInput.CameraView)) _orbit = Mathf.Repeat(_orbit + 90f, 360f);
        }

        void LateUpdate()
        {
            if (_skater == null || _camera == null) return;
            if (Mouse.current != null && Mouse.current.rightButton.isPressed)
                _orbit += Mouse.current.delta.ReadValue().x * .2f;
            Vector3 focus = _skater.transform.position + Vector3.up * .95f;
            Vector3 offset = Quaternion.Euler(0, _orbit, 0) * new Vector3(0, 1.1f, -4.3f);
            _camera.transform.position = Vector3.Lerp(_camera.transform.position, focus + offset, 1f - Mathf.Exp(-10f * Time.deltaTime));
            _camera.transform.LookAt(focus);
        }

        void OnGUI()
        {
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { fontSize = 23, fontStyle = FontStyle.Bold };
                _text = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            }
            GUI.Box(new Rect(16, 16, 530, 162), GUIContent.none);
            GUI.Label(new Rect(32, 26, 500, 34), "DRIFTSKATE / ANIMATION LAB", _title);
            GUI.Label(new Rect(32, 65, 500, 100),
                "WASD: Bewegen   B: Laufen / Board   Shift: Rennen\n" +
                "Leertaste: Springen / Ollie   R: Zurueck zum Start\n" +
                "C / rechte Maustaste: Kamera   V: Animation vergleichen\n" +
                (_enhanced ? "Neue Bewegung: AN" : "Bisherige Bewegung") +
                (_skater != null ? "   |   " + _skater.State + "   " + _skater.Speed.ToString("F1") + " m/s" : ""), _text);
        }
    }
}
