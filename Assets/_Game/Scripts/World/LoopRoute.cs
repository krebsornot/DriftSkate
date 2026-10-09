using System;
using Unity.Netcode;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Gemeinsame Uhr fuer alles, was ohne Netzwerk-Sync bei allen Spielern gleich laufen soll
    /// (Zeppelin, Schleppflugzeug, NPC-Verkehr): online die Server-Zeit, solo die Spielzeit.
    /// </summary>
    public static class SharedClock
    {
        public static bool Online
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening;
            }
        }

        public static double Time => Online ? NetworkManager.Singleton.ServerTime.Time : UnityEngine.Time.timeAsDouble;
    }

    /// <summary>
    /// Geschlossene Flugroute aus einer Form-Funktion (Winkel 0..2pi -> flacher Punkt), gleichmaessig nach
    /// Bogenlaenge abgetastet, damit das Tempo ueberall gleich ist. Fuer Zeppelin und Schleppflugzeug.
    /// </summary>
    public class LoopRoute
    {
        const int Samples = 512;
        readonly Func<float, Vector3> _shape;
        readonly float[] _arc = new float[Samples + 1];

        public float Length { get; }

        public LoopRoute(Func<float, Vector3> shape)
        {
            _shape = shape;
            Vector3 prev = shape(0f);
            for (int i = 1; i <= Samples; i++)
            {
                Vector3 p = shape(i / (float)Samples * Mathf.PI * 2f);
                _arc[i] = _arc[i - 1] + Vector3.Distance(prev, p);
                prev = p;
            }
            Length = _arc[Samples];
        }

        /// <summary>Flacher Punkt (ohne Hoehe) nach s Metern auf der Route.</summary>
        public Vector3 FlatPoint(float s)
        {
            s = Mathf.Repeat(s, Length);
            int lo = 0, hi = Samples;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_arc[mid] <= s) lo = mid; else hi = mid;
            }
            float f = Mathf.InverseLerp(_arc[lo], _arc[hi], s);
            return _shape((lo + f) / Samples * Mathf.PI * 2f);
        }

        /// <summary>
        /// Naechste Position auf der Route: mit Tempo weiterfliegen und online sanft auf die Server-Zeit
        /// einschwenken; ab snapDistance (z. B. beim Beitritt) direkt hinspringen.
        /// </summary>
        public float Advance(float s, float speed, float startOffset, float dt, float snapDistance)
        {
            s += speed * dt;
            if (SharedClock.Online)
            {
                float target = (float)(SharedClock.Time * speed) + startOffset;
                float diff = Mathf.Repeat(target - s, Length);
                if (diff > Length * 0.5f) diff -= Length;
                if (Mathf.Abs(diff) > snapDistance) s += diff;
                else s += diff * Mathf.Min(1f, dt * 0.5f);
            }
            return Mathf.Repeat(s, Length);
        }

        /// <summary>Route als Gizmo zeichnen (height: Hoehe ueber dem flachen Punkt nach s).</summary>
        public void DrawGizmo(Vector3 center, Func<float, float> height, Color color)
        {
            Gizmos.color = color;
            Vector3 prev = center + FlatPoint(0f) + Vector3.up * height(0f);
            for (int i = 1; i <= 128; i++)
            {
                float s = i / 128f * Length;
                Vector3 p = center + FlatPoint(s) + Vector3.up * height(s);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
