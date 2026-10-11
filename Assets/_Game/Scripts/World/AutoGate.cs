using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Automatische Schranke: Gehaeuse am Strassenrand, rot-weisser Baum quer ueber die Fahrbahn. Kommt ein Spieler (Auto
    /// oder Skater, auch online) in die Naehe oder rast darauf zu, geht der Baum hoch, gut zwei Sekunden nachdem keiner
    /// mehr da ist wieder runter. Der Baum selbst hat keine Hitbox (eine verspaetete Schranke soll keinen Unfall bauen),
    /// das Gehaeuse schon. Laeuft auf jedem Rechner fuer sich nach den Positionen der Spieler, es wird nichts uebertragen.
    /// </summary>
    public class AutoGate : MonoBehaviour
    {
        public Transform boom;
        /// <summary>Mitte der Durchfahrt (dort wird gemessen) und wie weit vorher geoeffnet wird.</summary>
        public Vector3 lane;
        public float detect = 22f, lookAhead = 1.3f, openSeconds = 1.1f;

        float _open, _closeAt;
        Renderer _lamp;
        Material _lampOn, _lampOff;

        static readonly List<PlayerAvatar> Players = new List<PlayerAvatar>();
        static float _refresh;

        public bool IsOpen => _open > 0.99f;
        public float Openness => _open;

        /// <summary>
        /// Eine Schranken-Einheit bauen: steht bei 'post', der Baum zeigt nach 'across' (quer ueber die Fahrbahn) und ist
        /// 'length' lang, 'lane' ist die Mitte der Durchfahrt.
        /// </summary>
        public static AutoGate Build(Transform parent, string name, Vector3 post, Vector3 across, float length, Vector3 lane, bool housing = true, bool parkhausColors = false)
        {
            across = Vector3.ProjectOnPlane(across, Vector3.up).normalized;
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(post, Quaternion.LookRotation(Vector3.Cross(across, Vector3.up), Vector3.up)); // lokal +X = quer
            Color yellow = Palette.Hex("F6C945"), ink = Palette.Ink;
            if (housing)
            {
                var box = Shapes.Box(root, new Vector3(0f, 0.6f, 0f), new Vector3(0.6f, 1.2f, 0.6f), yellow, 0.3f, collider: true, name: "Housing");
                box.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var band = new MeshFactory.BoxBatch();
                band.Add(new Vector3(0f, 0.95f, 0f), new Vector3(0.62f, 0.12f, 0.62f));
                band.Add(new Vector3(0f, 0.2f, 0f), new Vector3(0.62f, 0.12f, 0.62f));
                band.Build(root, "HousingBands", ink, "gate_bands").isStatic = false;
            }

            // Ohne Gehaeuse (am Ticket-Automaten des Parkhauses): Baum direkt an der Saeule, Hoehe wie im Modell
            var pivot = new GameObject("Boom").transform;
            pivot.SetParent(root, false);
            pivot.localPosition = housing ? new Vector3(0.25f, 1.02f, 0f) : new Vector3(0f, 0.95f, 0f);
            Color armColor = parkhausColors ? Palette.Hex("F2B632") : Palette.White, stripeColor = parkhausColors ? Palette.Ink : Palette.Red;
            var arm = Shapes.Box(pivot, new Vector3(length * 0.5f, 0f, 0f), new Vector3(length, 0.14f, 0.12f), armColor, 0.25f, name: "Arm");
            arm.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var stripes = new MeshFactory.BoxBatch();
            for (float x = 0.5f; x < length - 0.3f; x += 1.2f)
                stripes.Add(new Vector3(x + 0.3f, 0f, 0f), new Vector3(0.6f, 0.15f, 0.13f));
            stripes.Build(pivot, "Stripes", stripeColor, "gate_stripes_" + Mathf.RoundToInt(length * 10f)).isStatic = false;

            var gate = root.gameObject.AddComponent<AutoGate>();
            gate.boom = pivot;
            gate.lane = lane;
            gate._lampOff = ToonMaterials.Get(Palette.Hex("8A3A2A"), 0.2f, false, 0f);
            gate._lampOn = ToonMaterials.Get(Palette.Hex("FF8A2A"), 0.2f, false, 2.5f);
            var lamp = Shapes.Part(PrimitiveType.Sphere, root, new Vector3(housing ? 0f : -0.18f, housing ? 1.3f : 1.66f, 0f), new Vector3(0.22f, 0.22f, 0.22f), Palette.White, 0.2f, name: "Lamp");
            gate._lamp = lamp.GetComponent<Renderer>();
            gate._lamp.sharedMaterial = gate._lampOff;
            gate._lamp.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return gate;
        }

        void Update()
        {
            if (Time.time > _refresh)
            {
                _refresh = Time.time + 1f;
                Players.Clear();
                Players.AddRange(FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None));
            }
            bool someone = false;
            foreach (var p in Players)
            {
                if (p == null) continue;
                someone |= Near(p.car != null ? p.car.transform : null, p.car != null ? p.car.GetComponent<Rigidbody>() : null);
                someone |= Near(p.skater != null ? p.skater.transform : null, p.skater != null ? p.skater.GetComponent<Rigidbody>() : null);
            }
            if (someone) _closeAt = Time.time + 2.2f;
            float target = Time.time < _closeAt ? 1f : 0f;
            _open = Mathf.MoveTowards(_open, target, Time.deltaTime / openSeconds);
            float t = _open * _open * (3f - 2f * _open);
            if (boom != null) boom.localRotation = Quaternion.Euler(0f, 0f, 84f * t);
            // Warnlicht blinkt, solange sich der Baum bewegt oder offen ist
            bool blink = _open > 0.01f && Mathf.Repeat(Time.time * 2.4f, 1f) < 0.5f;
            if (_lamp != null) _lamp.sharedMaterial = blink ? _lampOn : _lampOff;
        }

        bool Near(Transform t, Rigidbody rb)
        {
            if (t == null || !t.gameObject.activeInHierarchy) return false;
            Vector3 d = t.position - lane;
            if (Mathf.Abs(d.y) > 8f) return false;
            if (new Vector2(d.x, d.z).magnitude < detect) return true;
            // Schnell darauf zu: dorthin, wo er gleich ist
            if (rb == null || rb.isKinematic) return false;
            Vector3 ahead = d + rb.linearVelocity * lookAhead;
            return new Vector2(ahead.x, ahead.z).magnitude < detect;
        }
    }
}
