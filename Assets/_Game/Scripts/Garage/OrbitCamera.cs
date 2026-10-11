using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DriftSkate
{
    /// <summary>Garagen-Kamera: mit der Maus ziehen (oder rechter Stick) zum Drehen, Mausrad zum Zoomen.</summary>
    public class OrbitCamera : MonoBehaviour
    {
        public Vector3 target = new Vector3(1.2f, 0.9f, 0f);
        public float yaw = -35f, pitch = 14f, distance = 9f;
        public Vector2 screenOffset = new Vector2(0.15f, 0f);

        void LateUpdate()
        {
            var mouse = Mouse.current;
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (mouse != null && mouse.leftButton.isPressed && !overUi)
            {
                Vector2 d = mouse.delta.ReadValue();
                yaw += d.x * 0.25f;
                pitch = Mathf.Clamp(pitch - d.y * 0.2f, 2f, 45f);
            }
            if (mouse != null && !overUi) distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * 0.01f, 5f, 16f);
            var pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 s = pad.rightStick.ReadValue();
                yaw += s.x * 90f * Time.deltaTime;
                pitch = Mathf.Clamp(pitch - s.y * 60f * Time.deltaTime, 2f, 45f);
            }

            Apply();
        }

        /// <summary>Kamera aus yaw, pitch und distance setzen.</summary>
        public void Apply()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0);
            // Bild leicht nach links schieben, damit rechts Platz fuer das Menue ist
            Vector3 step = -(rot * Vector3.forward) + rot * Vector3.right * screenOffset.x;
            transform.SetPositionAndRotation(target + step * Distance(step), rot);
        }

        /// <summary>Zoom-Abstand, in Raeumen mit Grenze (GarageEnvironment.CameraBounds) hoechstens bis vor Wand oder Decke.</summary>
        float Distance(Vector3 step)
        {
            float d = distance;
            var b = GarageEnvironment.CameraBounds;
            if (b.size == Vector3.zero) return d;
            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(step[i]) < 1e-4f) continue;
                float face = step[i] > 0f ? b.max[i] : b.min[i];
                d = Mathf.Min(d, (face - target[i]) / step[i]);
            }
            return Mathf.Max(d, 2f);
        }
    }
}
