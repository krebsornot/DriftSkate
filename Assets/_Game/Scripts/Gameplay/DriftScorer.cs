using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>Bewertet Drifts: Winkel x Tempo x Zonen- und Wandnaehe-Bonus. Speist die gemeinsame Combo.</summary>
    [RequireComponent(typeof(VehicleController))]
    public class DriftScorer : MonoBehaviour
    {
        public const float MinAngle = 15f, MinSpeedKmh = 30f;

        public ComboSystem combo;
        public bool IsDrifting { get; private set; }
        public float CurrentAngle { get; private set; }
        public float ProximityBonus { get; private set; }
        public DriftZone CurrentZone { get; private set; }

        VehicleController _vc;
        int _lastSign;
        float _driftTime, _noDriftTime;
        int _mask;

        void Awake()
        {
            _vc = GetComponent<VehicleController>();
            _vc.HardImpact += OnImpact;
            _mask = ~LayerMask.GetMask("Car", "Skater", "Ignore Raycast", "Rail");
        }

        void OnDestroy()
        {
            if (_vc != null) _vc.HardImpact -= OnImpact;
        }

        void OnImpact(float deltaV)
        {
            if (combo != null && _vc.hasDriver && combo.Active) combo.Fail("WAND!");
        }

        void Update()
        {
            if (combo == null || !_vc.isLocal || !_vc.hasDriver)
            {
                IsDrifting = false;
                return;
            }

            float slip = Mathf.Abs(_vc.BodySlip);
            float kmh = _vc.SpeedKmh;
            CurrentAngle = slip;
            CurrentZone = DriftZone.Find(transform.position);

            if (kmh > 15f && slip > 125f && _vc.Grounded)
            {
                if (combo.Active) combo.Fail("DREHER!");
                IsDrifting = false;
                return;
            }

            bool drifting = kmh > MinSpeedKmh && slip > MinAngle && slip < 110f && _vc.ForwardSpeed > 0f && _vc.Grounded;
            if (drifting)
            {
                _noDriftTime = 0f;
                int sign = _vc.BodySlip > 0 ? 1 : -1;
                if (!IsDrifting)
                {
                    combo.AddAction(CurrentZone != null ? "ZONE DRIFT" : "DRIFT", 50f);
                    _driftTime = 0f;
                }
                else if (sign != _lastSign && _driftTime > 0.4f)
                {
                    combo.AddAction("TRANSITION", 250f);
                    _driftTime = 0f;
                }
                _lastSign = sign;
                _driftTime += Time.deltaTime;

                float angleFactor = Mathf.Lerp(1f, 3f, Mathf.InverseLerp(MinAngle, 60f, slip));
                float speedFactor = kmh / 50f;
                ProximityBonus = WallProximity();
                float zone = CurrentZone != null ? CurrentZone.multiplier : 1f;
                combo.AddPoints(120f * angleFactor * speedFactor * zone * (1f + ProximityBonus) * Time.deltaTime);
                combo.Hold();
                IsDrifting = true;
            }
            else
            {
                // Kurze Unterbrechungen (z. B. im Uebergang) beenden den Drift nicht sofort.
                _noDriftTime += Time.deltaTime;
                if (_noDriftTime > 0.35f) IsDrifting = false;
                ProximityBonus = 0f;
            }
        }

        /// <summary>0..1, je naeher Heck oder Seite an einer Wand sind.</summary>
        float WallProximity()
        {
            const float range = 3f;
            float best = range;
            Vector3 rear = transform.TransformPoint(new Vector3(0, 0.5f, -_vc.setup.wheelbase * 0.5f - 0.8f));
            Vector3[] dirs = { -transform.forward, transform.right, -transform.right, (-transform.forward + transform.right).normalized, (-transform.forward - transform.right).normalized };
            foreach (var d in dirs)
            {
                if (Physics.Raycast(rear, d, out RaycastHit hit, range, _mask, QueryTriggerInteraction.Ignore) && Vector3.Dot(hit.normal, Vector3.up) < 0.5f)
                    best = Mathf.Min(best, hit.distance);
            }
            return 1f - best / range;
        }
    }
}
