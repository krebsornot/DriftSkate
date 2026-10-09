using System;
using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Ein Auto, an das man sich auf dem Board dranhaengen kann (Skitchen): Autos der anderen Spieler und NPC-Autos.
    /// Drei Haltepunkte: links und rechts an der Tuer, hinten an der Stossstange. Die Masse kommen aus dem BoxCollider
    /// des Autos. Online wird nur (Art, Nummer, Haltepunkt) uebertragen; jeder Client klebt den Skater selbst ans Auto.
    /// </summary>
    public class Hitchable : MonoBehaviour
    {
        public const int KindPlayer = 1, KindNpc = 2;
        public const int Left = 0, Right = 1, Rear = 2;

        public static readonly List<Hitchable> All = new List<Hitchable>();

        public int kind, id;
        /// <summary>Spieler-Auto: wem es gehoert (das eigene Auto ist tabu).</summary>
        public PlayerAvatar owner;
        /// <summary>Darf man sich gerade dranhaengen (z. B. nur mit Fahrer)? null = immer.</summary>
        public Func<bool> available;

        BoxCollider _box;
        Vector3 _lastPos, _velocity;
        bool _hasLast;

        public Vector3 Velocity => _velocity;
        public bool Available => isActiveAndEnabled && (available == null || available());

        /// <summary>Code fuers Netzwerk: Art, Nummer und Haltepunkt in einer Zahl (-1 = nirgends).</summary>
        public int Encode(int anchor) => (kind << 28) | ((id & 0x3FFFFFF) << 2) | (anchor & 3);

        public static Hitchable Decode(int code, out int anchor)
        {
            anchor = code & 3;
            if (code < 0) return null;
            int k = (code >> 28) & 0xF, i = (code >> 2) & 0x3FFFFFF;
            foreach (var h in All)
                if (h != null && h.kind == k && (h.id & 0x3FFFFFF) == i) return h;
            return null;
        }

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            _hasLast = false;
        }

        void OnDisable() => All.Remove(this);

        void LateUpdate()
        {
            // Tempo aus der tatsaechlichen Bewegung (gilt fuer eigene, fremde und NPC-Autos gleich)
            float dt = Time.deltaTime;
            Vector3 p = transform.position;
            if (_hasLast && dt > 1e-4f)
            {
                Vector3 v = (p - _lastPos) / dt;
                if (v.sqrMagnitude > 80f * 80f) v = _velocity; // Teleport
                _velocity = Vector3.Lerp(_velocity, v, 1f - Mathf.Exp(-12f * dt));
            }
            _lastPos = p;
            _hasLast = true;
        }

        BoxCollider Box
        {
            get
            {
                if (_box == null) _box = GetComponent<BoxCollider>();
                return _box;
            }
        }

        Vector3 Center => Box != null ? Box.center : new Vector3(0f, 0.7f, 0f);
        Vector3 Half => Box != null ? Box.size * 0.5f : new Vector3(0.9f, 0.6f, 2.1f);

        /// <summary>Wo der Skater steht (lokal im Auto, Hoehe 0; die Bodenhoehe wird gesucht).</summary>
        public Vector3 StandLocal(int anchor, float sway = 0f)
        {
            Vector3 c = Center, h = Half;
            switch (anchor)
            {
                case Left: return new Vector3(c.x - h.x - 0.62f - sway, 0f, c.z - h.z * 0.18f);
                case Right: return new Vector3(c.x + h.x + 0.62f + sway, 0f, c.z - h.z * 0.18f);
                default: return new Vector3(c.x + sway, 0f, c.z - h.z - 0.95f);
            }
        }

        /// <summary>Wo die Hand greift (Welt): Tuerkante bzw. Stossstange.</summary>
        public Vector3 HoldPoint(int anchor)
        {
            Vector3 c = Center, h = Half;
            float bottom = c.y - h.y;
            switch (anchor)
            {
                case Left: return transform.TransformPoint(new Vector3(c.x - h.x - 0.02f, bottom + h.y * 1.05f, c.z - h.z * 0.12f));
                case Right: return transform.TransformPoint(new Vector3(c.x + h.x + 0.02f, bottom + h.y * 1.05f, c.z - h.z * 0.12f));
                default: return transform.TransformPoint(new Vector3(c.x - h.x * 0.25f, bottom + 0.3f, c.z - h.z - 0.03f));
            }
        }

        static int _groundMask;

        /// <summary>Standpunkt in der Welt, auf den Boden gesetzt.</summary>
        public Vector3 StandPoint(int anchor, float sway = 0f)
        {
            if (_groundMask == 0) _groundMask = ~LayerMask.GetMask("Car", "Skater", "Ignore Raycast", "Rail");
            Vector3 p = transform.TransformPoint(StandLocal(anchor, sway));
            float carY = transform.position.y;
            if (Physics.Raycast(new Vector3(p.x, carY + 1.5f, p.z), Vector3.down, out RaycastHit hit, 4f, _groundMask, QueryTriggerInteraction.Ignore))
                p.y = hit.point.y;
            else p.y = carY;
            return p;
        }

        /// <summary>Naechstes Auto mit freiem Haltepunkt in Reichweite (eigenes Auto ausgenommen).</summary>
        public static Hitchable FindNear(Vector3 pos, PlayerAvatar self, out int anchor, float reach = 2.6f)
        {
            anchor = -1;
            Hitchable best = null;
            float bestD = reach;
            foreach (var h in All)
            {
                if (h == null || !h.Available || (self != null && h.owner == self)) continue;
                if ((h.transform.position - pos).sqrMagnitude > 64f) continue;
                if (h.transform.up.y < 0.7f) continue; // umgekippt
                for (int a = 0; a < 3; a++)
                {
                    Vector3 s = h.transform.TransformPoint(h.StandLocal(a));
                    float d = Vector2.Distance(new Vector2(s.x, s.z), new Vector2(pos.x, pos.z));
                    if (d < bestD && Mathf.Abs(pos.y - h.transform.position.y) < 1.5f)
                    {
                        bestD = d;
                        best = h;
                        anchor = a;
                    }
                }
            }
            return best;
        }
    }
}
