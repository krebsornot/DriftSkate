using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Boden, der sich bewegt (z. B. das drehende Kran-Oberteil): merkt sich je Physikschritt, wie weit er sich seit dem
    /// letzten bewegt hat. Wer darauf steht, wird um genau das mitgenommen (SkaterController), sonst rutscht der Boden
    /// unter einem weg. Laeuft vor dem Skater.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public class MovingGround : MonoBehaviour
    {
        Matrix4x4 _prev;
        bool _has;

        /// <summary>Bewegung im letzten Physikschritt (Welt): neuer Punkt = Delta * alter Punkt.</summary>
        public Matrix4x4 Delta { get; private set; } = Matrix4x4.identity;
        /// <summary>Drehung um die Hochachse im letzten Physikschritt (Grad).</summary>
        public float DeltaYaw { get; private set; }

        void FixedUpdate()
        {
            var m = transform.localToWorldMatrix;
            Delta = _has ? m * _prev.inverse : Matrix4x4.identity;
            Vector3 f = Delta.MultiplyVector(Vector3.forward);
            DeltaYaw = _has ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0f;
            _prev = m;
            _has = true;
        }

        public static MovingGround Of(Collider c) => c != null ? c.GetComponentInParent<MovingGround>() : null;
    }
}
