using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Drehender Oberteil eines Baukrans (Ausleger, Kabine, Gegengewicht, Laufkatze): eine Runde in 'period' Sekunden,
    /// nach der gemeinsamen Uhr (online Server-Zeit), damit der Arm bei allen Spielern gleich steht. Nichts wird uebertragen.
    /// </summary>
    public class CraneSlew : MonoBehaviour
    {
        public float period = 150f;
        public float offset;

        void Update() => transform.localRotation = Quaternion.Euler(0f, Angle(SharedClock.Time), 0f);

        public float Angle(double time) => offset + (float)(time % period / period * 360.0);
    }
}
