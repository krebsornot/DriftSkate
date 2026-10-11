using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Dreht ein Teil gleichmaessig um eine lokale Achse (Hubschrauber-Rotor), nach der gemeinsamen Uhr, damit es online
    /// bei allen gleich steht. Nichts wird uebertragen.
    /// </summary>
    public class RotorSpin : MonoBehaviour
    {
        public Vector3 axis = Vector3.up;
        public float period = 2f;   // Sekunden pro Umdrehung
        Quaternion _rest;

        void Awake() => _rest = transform.localRotation;

        void Update() => transform.localRotation = _rest * Quaternion.AngleAxis((float)(SharedClock.Time % period / period * 360.0), axis);
    }
}
