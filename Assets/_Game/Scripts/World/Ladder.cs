using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Kletterbare Leiter: Linie von unten nach oben (lokal zum eigenen Transform, dreht sich also mit, z. B. auf dem
    /// Kran-Oberteil), Seite, auf der man haengt, und optionale Absteige-Punkte oben und unten. Der Skater klettert daran
    /// (SkaterController.StartClimb) ohne Physik entlang der Linie und steigt an den Enden ab.
    /// </summary>
    public class Ladder : MonoBehaviour
    {
        public static readonly List<Ladder> All = new List<Ladder>();

        /// <summary>Unterster und oberster Kletterpunkt der Fuesse (lokal).</summary>
        public Vector3 bottom, top;
        /// <summary>Von der Leiter zum Kletternden (lokal, waagerecht).</summary>
        public Vector3 side = Vector3.back;
        /// <summary>Wo man oben bzw. unten wieder steht (lokal). Ohne: oben bleibt man haengen, unten einen Schritt zurueck.</summary>
        public bool hasTopExit, hasBottomExit;
        public Vector3 topExit, bottomExit;
        /// <summary>Optional: Bezug fuer den oberen Absteige-Punkt (z. B. drehender Ausleger ueber einer festen Mast-Leiter).</summary>
        public Transform exitFrame;
        /// <summary>Sprossenabstand (Welt-Meter, fuer die Kletter-Pose) und Abstand der Figur zur Leiter.</summary>
        public float rungSpacing = 0.3f, standoff = 0.36f;
        public string label = "LEITER";

        public Vector3 Bottom => transform.TransformPoint(bottom);
        public Vector3 Top => transform.TransformPoint(top);
        public float Length => Vector3.Distance(Bottom, Top);
        public Vector3 Side
        {
            get
            {
                Vector3 s = Vector3.ProjectOnPlane(transform.TransformDirection(side), Vector3.up);
                return s.sqrMagnitude > 1e-6f ? s.normalized : Vector3.back;
            }
        }
        public Vector3 TopExit => (exitFrame != null ? exitFrame : transform).TransformPoint(topExit);
        public Vector3 BottomExit => hasBottomExit ? transform.TransformPoint(bottomExit) : Bottom + Side * 0.7f;

        /// <summary>Punkt auf der Sprossen-Ebene in Hoehe s (Meter ab unten).</summary>
        public Vector3 PointAt(float s)
        {
            Vector3 b = Bottom, t = Top;
            float len = Vector3.Distance(b, t);
            return len > 1e-4f ? Vector3.Lerp(b, t, Mathf.Clamp01(s / len)) : b;
        }

        /// <summary>Wo die Figur (Fuss-Mitte) beim Klettern in Hoehe s steht.</summary>
        public Vector3 ClimbPosition(float s) => PointAt(s) + Side * standoff;

        /// <summary>Hoehe s des naechsten Punkts auf der Leiter zu pos und waagerechter Abstand dorthin.</summary>
        public float Project(Vector3 pos, out float dist)
        {
            Vector3 b = Bottom, t = Top, d = t - b;
            float len = d.magnitude;
            float s = len > 1e-4f ? Mathf.Clamp(Vector3.Dot(pos - b, d / len), 0f, len) : 0f;
            Vector3 p = PointAt(s);
            dist = new Vector2(pos.x - p.x, pos.z - p.z).magnitude;
            // Fuesse muessen grob auf Hoehe der Leiter sein (nicht 10 m drunter)
            if (pos.y < b.y - 1.2f || pos.y > t.y + 1.5f) dist = float.MaxValue;
            return s;
        }

        /// <summary>Naechste Leiter, die man von pos aus greifen kann (waagerecht naeher als radius).</summary>
        public static Ladder FindNear(Vector3 pos, float radius, out float s)
        {
            Ladder best = null;
            s = 0f;
            float bestD = radius;
            foreach (var l in All)
            {
                if (l == null || !l.isActiveAndEnabled) continue;
                float ls = l.Project(pos, out float d);
                if (d < bestD) { bestD = d; best = l; s = ls; }
            }
            return best;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(Bottom, Top);
            Gizmos.DrawLine(Top, Top + Side * standoff);
            if (hasTopExit) Gizmos.DrawWireSphere(TopExit, 0.3f);
        }
    }
}
