using System;
using UnityEngine;

namespace DriftSkate
{
    public struct VehicleInput
    {
        public float steer, throttle, brake;
        public bool handbrake, clutch;
    }

    /// <summary>Alle physikalischen Werte eines Autos, berechnet aus Auto-Daten + Tuning.</summary>
    [Serializable]
    public class VehicleSetup
    {
        public float mass = 1250f, wheelbase = 2.5f, track = 1.48f, wheelRadius = 0.32f;
        public float comHeight = 0.42f, comForward = 0.06f;

        public float restLength = 0.3f;
        public float springFront = 48000f, springRear = 46000f;
        public float damperFront = 3200f, damperRear = 3000f;
        public float antiRollFront = 16000f, antiRollRear = 9000f;

        public float maxSteer = 48f, steerSpeed = 340f;

        public float peakTorque = 360f, idleRpm = 900f, maxRpm = 7500f;
        public float[] gears = { 3.3f, 2.1f, 1.5f, 1.15f, 0.92f, 0.78f };
        public float reverseRatio = 3.4f, finalDrive = 4.1f;

        public float gripFront = 1.06f, gripRear = 0.98f;
        public float frontSlideRatio = 0.86f, rearSlideRatio = 0.8f;
        public float diffLock = 1f;
        public float brakeForce = 12000f, brakeBias = 0.62f, handbrakeGrip = 0.45f;

        public float drag = 0.42f, rolling = 0.012f, downforce = 1.2f;

        public float assist = 0.6f;
        public bool autoGearbox = true;

        public static float AssistFromLevel(int level)
        {
            switch (level)
            {
                case 0: return 0f;
                case 1: return 0.55f;
                case 2: return 0.8f;
                default: return 1f;
            }
        }

        public static VehicleSetup From(CarDef def, float[] t, int assistLevel, bool autoGearbox)
        {
            if (t == null || t.Length != Tuning.Count) t = Tuning.Defaults();
            var s = new VehicleSetup
            {
                mass = def.mass,
                wheelbase = def.wheelbase,
                track = def.track,
                wheelRadius = def.wheelRadius,
                maxRpm = def.maxRpm,
                gears = def.gears,
                finalDrive = def.finalDrive,
                assist = AssistFromLevel(assistLevel),
                autoGearbox = autoGearbox
            };

            float massScale = def.mass / 1250f;
            s.springFront = 48000f * massScale * Mathf.Lerp(0.6f, 1.5f, t[Tuning.SpringFront]);
            s.springRear = 46000f * massScale * Mathf.Lerp(0.6f, 1.5f, t[Tuning.SpringRear]);
            float corner = def.mass * 0.25f;
            s.damperFront = 2f * 0.4f * Mathf.Sqrt(s.springFront * corner);
            s.damperRear = 2f * 0.4f * Mathf.Sqrt(s.springRear * corner);
            s.antiRollFront = 16000f * massScale;
            s.antiRollRear = 9000f * massScale;

            s.restLength = Mathf.Lerp(0.34f, 0.2f, t[Tuning.RideHeight]);
            s.comHeight = Mathf.Lerp(0.5f, 0.38f, t[Tuning.RideHeight]);

            s.maxSteer = Mathf.Lerp(32f, 64f, t[Tuning.Steering]);
            s.peakTorque = def.peakTorque * Mathf.Lerp(0.75f, 1.3f, t[Tuning.Power]);

            s.gripFront = 1.06f * Mathf.Lerp(0.96f, 1.1f, t[Tuning.Camber]) * Mathf.Lerp(1.03f, 0.97f, t[Tuning.SpringFront]);
            s.gripRear = 0.98f * Mathf.Lerp(1.08f, 0.88f, t[Tuning.RearPressure]) * Mathf.Lerp(1.04f, 0.96f, t[Tuning.SpringRear]);

            s.diffLock = t[Tuning.Differential];
            s.brakeBias = Mathf.Lerp(0.45f, 0.8f, t[Tuning.BrakeBias]);
            s.brakeForce = def.mass * 9.81f * 1.05f;
            return s;
        }
    }
}
