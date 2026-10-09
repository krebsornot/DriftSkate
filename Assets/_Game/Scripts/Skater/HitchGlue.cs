using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Skitchen: setzt den Skater nach allen Updates (Auto-Physik, Netzwerk-Interpolation) an den Haltepunkt des
    /// Autos, bevor Kamera und Figuren-Pose in LateUpdate laufen. Sonst zittert er gegen das Auto.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class HitchGlue : MonoBehaviour
    {
        public SkaterController skater;

        void LateUpdate()
        {
            if (skater != null) skater.GlueHitch();
        }
    }
}
