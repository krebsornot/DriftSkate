using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Verdichterrad des Aufbau-Turbos: dreht mit der Motordrehzahl (mit Gas schneller, beim Gaswegnehmen laeuft es aus).
    /// Bewusst sichtbar langsam (echte Turbos drehen viel zu schnell fuers Auge). Geht auch fuer Autos anderer Spieler,
    /// deren Drehzahl kommt ueber das Netzwerk.
    /// </summary>
    public class TurboSpin : MonoBehaviour
    {
        VehicleController _car;
        Quaternion _rest;
        float _speed, _angle;

        void Awake()
        {
            _car = GetComponentInParent<VehicleController>();
            _rest = transform.localRotation;
        }

        void Update()
        {
            float target = 120f; // Leerlauf, Grad pro Sekunde
            if (_car != null)
            {
                float rpm = Mathf.InverseLerp(_car.setup.idleRpm, _car.setup.maxRpm, _car.EngineRpm);
                target = Mathf.Lerp(120f, 1100f, rpm) * Mathf.Lerp(0.55f, 1f, _car.Throttle);
            }
            // spoolt langsamer hoch, als es auslaeuft (Turboloch)
            _speed = Mathf.MoveTowards(_speed, target, (target > _speed ? 900f : 1400f) * Time.deltaTime);
            _angle = (_angle + _speed * Time.deltaTime) % 360f;
            transform.localRotation = _rest * Quaternion.AngleAxis(_angle, Vector3.forward);
        }
    }
}
