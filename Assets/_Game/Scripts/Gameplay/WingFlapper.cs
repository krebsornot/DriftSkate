using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Fluegelschlag fuer die Engelsfluegel, direkt an den Knochen berechnet (statt der Blender-Animation, die die
    /// Fluegel nur in ihrer eigenen Ebene drehte wie ein Scheibenwischer):
    /// Schwingen nach hinten/vorn um die Hochachse, Heben/Senken um die Blickachse, Ellbogen und Spitze ziehen verzoegert
    /// nach, die Federn schwingen leicht mit. Burst() ist ein einzelner kraeftiger Schlag (Doppelsprung).
    /// Raum: Wurzel des Fluegel-Objekts, x = Spannweite, y = oben, z = Blickrichtung der Figur.
    /// </summary>
    [DefaultExecutionOrder(210)]
    public class WingFlapper : MonoBehaviour
    {
        [Tooltip("Schlaege pro Sekunde")] public float frequency = 0.5f;
        [Tooltip("Schwingen nach hinten/vorn (Grad)")] public float sweep = 10f;
        [Tooltip("Heben/Senken (Grad)")] public float lift = 6f;
        [Tooltip("Grundhaltung: so weit nach hinten angelegt (Grad)")] public float tuck = 18f;

        class Side
        {
            public float sign;
            public Transform shoulder, elbow, tip;
            public Transform[] feathers;
        }

        Side[] _sides;
        Transform[] _bones;
        Quaternion[] _rest;
        float _phase, _burst = -1f;
        // weich nachgefuehrte Werte (Wechsel Boden/Luft ohne Ruck)
        float _f, _sw, _li, _tu;

        void Awake() => Init();

        /// <summary>Knochen suchen und Ruhepose merken (auch wenn Awake nicht laeuft, z. B. Editor-Vorschau).</summary>
        void Init()
        {
            if (_sides != null) return;
            var anim = GetComponentInChildren<Animator>();
            if (anim != null) anim.enabled = false; // die alte Scheibenwischer-Animation nicht mehr abspielen
            var all = GetComponentsInChildren<Transform>(true);
            Transform Find(string n) { foreach (var t in all) if (t.name == n) return t; return null; }
            _sides = new Side[2];
            int i = 0;
            foreach (var suffix in new[] { "L", "R" })
            {
                var s = new Side { shoulder = Find("Shoulder." + suffix), elbow = Find("Elbow." + suffix), tip = Find("Tip." + suffix), feathers = new Transform[7] };
                for (int k = 0; k < 7; k++) s.feathers[k] = Find($"Feather{k + 1:00}.{suffix}");
                Transform probe = s.tip != null ? s.tip : s.shoulder;
                s.sign = probe != null && transform.InverseTransformPoint(probe.position).x < 0f ? -1f : 1f;
                _sides[i++] = s;
            }
            var list = new System.Collections.Generic.List<Transform>();
            foreach (var s in _sides)
            {
                foreach (var t in new[] { s.shoulder, s.elbow, s.tip }) if (t != null) list.Add(t);
                foreach (var t in s.feathers) if (t != null) list.Add(t);
            }
            _bones = list.ToArray();
            _rest = new Quaternion[_bones.Length];
            for (int k = 0; k < _bones.Length; k++) _rest[k] = _bones[k].localRotation;
            _f = frequency; _sw = sweep; _li = lift; _tu = tuck;
        }

        /// <summary>Ein kraeftiger Schlag nach unten (z. B. Doppelsprung).</summary>
        public void Burst() => _burst = 0f;

        public bool Valid => _sides != null && _sides[0].shoulder != null && _sides[1].shoulder != null;

        /// <summary>Wie weit die Spitze gerade nach hinten zeigt (fuer Tests): lokales z der linken Spitze.</summary>
        public float TipDepth => Valid && _sides[0].tip != null ? transform.InverseTransformPoint(_sides[0].tip.position).z : 0f;

        void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>Einen Schritt weiterrechnen (auch fuer Vorschau-Bilder im Editor).</summary>
        public void Tick(float dt)
        {
            Init();
            if (!Valid) return;
            float k = 1f - Mathf.Exp(-3f * dt);
            _f = Mathf.Lerp(_f, frequency, k);
            _sw = Mathf.Lerp(_sw, sweep, k);
            _li = Mathf.Lerp(_li, lift, k);
            _tu = Mathf.Lerp(_tu, tuck, k);
            _phase = Mathf.Repeat(_phase + dt * _f * Mathf.PI * 2f, Mathf.PI * 200f);

            // Einzelner kraeftiger Schlag: erst weit hoch und zurueck ausholen, dann schnell nach unten und vorn
            float bSweep = 0f, bLift = 0f;
            if (_burst >= 0f)
            {
                _burst += dt / 0.55f;
                float t = Mathf.Clamp01(_burst);
                float up = Mathf.Sin(Mathf.Clamp01(t / 0.3f) * Mathf.PI * 0.5f);          // ausholen
                float down = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.3f) / 0.35f)); // Schlag
                float back = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.65f) / 0.35f));
                bLift = (up * 50f - down * 85f) * back;
                bSweep = (up * 25f - down * 12f) * back; // nach vorn nur wenig, sonst schlagen sie durch den Koerper
                if (_burst >= 1f) _burst = -1f;
            }

            for (int i = 0; i < _bones.Length; i++) _bones[i].localRotation = _rest[i];
            Vector3 upAxis = transform.up, fwdAxis = transform.forward;
            foreach (var s in _sides)
            {
                // Hochachse: positiver Winkel legt die Spitze nach hinten (-z); Blickachse: positiver Winkel hebt sie an
                float a0 = _tu + _sw * Mathf.Sin(_phase) + bSweep;
                float l0 = _li * Mathf.Cos(_phase) + bLift;
                Rotate(s.shoulder, upAxis, fwdAxis, s.sign, a0, l0);
                // Ellbogen und Spitze verzoegert, beim Ausholen etwas eingeklappt
                float fold = Mathf.Max(0f, Mathf.Cos(_phase)) * _li * 0.5f;
                Rotate(s.elbow, upAxis, fwdAxis, s.sign, _sw * 0.45f * Mathf.Sin(_phase - 0.7f) + fold + bSweep * 0.3f, _li * 0.4f * Mathf.Cos(_phase - 0.7f) + bLift * 0.25f);
                Rotate(s.tip, upAxis, fwdAxis, s.sign, _sw * 0.3f * Mathf.Sin(_phase - 1.3f) + fold * 0.6f, _li * 0.35f * Mathf.Cos(_phase - 1.3f) + bLift * 0.2f);
                // Federn: kleines Nachschwingen um die eigene Laengsachse
                for (int f = 0; f < s.feathers.Length; f++)
                {
                    var fe = s.feathers[f];
                    if (fe == null) continue;
                    float wobble = (_li * 0.6f + Mathf.Abs(bLift) * 0.15f) * Mathf.Sin(_phase - 1.6f - f * 0.15f);
                    fe.rotation = Quaternion.AngleAxis(wobble, fe.position - (s.elbow != null ? s.elbow.position : transform.position)) * fe.rotation;
                }
            }
        }

        static void Rotate(Transform bone, Vector3 upAxis, Vector3 fwdAxis, float sign, float sweepDeg, float liftDeg)
        {
            if (bone == null) return;
            bone.rotation = Quaternion.AngleAxis(sign * liftDeg, fwdAxis) * Quaternion.AngleAxis(sign * sweepDeg, upAxis) * bone.rotation;
        }
    }
}
