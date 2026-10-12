using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Stopp-Schild am Schulbus: klappt an der Haltestelle aus (wie beim amerikanischen Schulbus) und beim Losfahren
    /// wieder an die Seite. Die beiden Stellungen kommen aus dem Editor (DriftSkate → Verkehr → Schulbus bauen).
    /// </summary>
    public class BusStopArm : MonoBehaviour
    {
        public Quaternion folded = Quaternion.identity, deployed = Quaternion.identity;
        TrafficCar _bus;
        float _open;

        void Awake() => _bus = GetComponentInParent<TrafficCar>();

        void Update()
        {
            bool atStop = _bus != null && _bus.AtStop;
            _open = Mathf.MoveTowards(_open, atStop ? 1f : 0f, Time.deltaTime * 1.6f);
            transform.localRotation = Quaternion.Slerp(folded, deployed, Mathf.SmoothStep(0f, 1f, _open));
        }
    }
}
