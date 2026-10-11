using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Stellt Bodo und Udo hinter die Imbisswagen in den Parks (HotdogCart_Park*, von PropAssets in die Stadt gesetzt):
    /// vor den Grill auf der Seite mit den Schranktueren, Blick ueber die Theke zur offenen Seite (dort steht der Kunde,
    /// im Park schaut sie zur Platzmitte). Die Stadt-Szene bleibt unveraendert. Nur lokal, ohne Netzwerk.
    /// </summary>
    public static class HotdogStand
    {
        /// <summary>Verkaeufer, der Reihe nach fuer die Park-Wagen (Park0 = Platz neben dem Brunnen, Park1 = Mirus Park).</summary>
        static readonly string[] Vendors = { "Bodo", "Udo" };

        // Wagen im Prefab-Raum (PropAssets.HotdogCart): die Vorwaerts-Achse des Prefabs zeigt zum Schild, das seitlich
        // versetzt ist, daher steht der Wagen darin gut 22 Grad schraeg. Gemessen am Modell (HotdogTests.MeasureCart).
        public static readonly Vector3 CartCenter = new Vector3(0.093f, 0f, 0.143f);   // Mitte der Arbeitsflaeche (Boden)
        public static readonly Vector3 CartAxis = new Vector3(0.382f, 0f, 0.924f);     // Laengsachse, zum Schild hin
        public static readonly Vector3 ServeSide = new Vector3(0.924f, 0f, -0.382f);   // offene Seite (Kunde), gegenueber Schirm und Tueren
        /// <summary>Verkaeufer: so weit hinter der Wagenmitte und so weit Richtung Schild (vor dem Grill). Passt zu build_hotdog_vendors.py.</summary>
        public const float VendorBack = 0.85f, VendorAlong = 0.44f;

        public static readonly List<Transform> Carts = new List<Transform>();
        public static readonly List<HotdogVendor> Spawned = new List<HotdogVendor>();

        /// <summary>Fuer das Admin-Menue: vor dem ersten Park-Wagen auf der Kundenseite.</summary>
        public static Vector3 TeleportPoint
        {
            get
            {
                Find();
                if (Carts.Count == 0) return new Vector3(CityBuilder.BlockCenter(1), 0.5f, CityBuilder.BlockCenter(2));
                return CustomerSpot(Carts[0], 3f) + Vector3.up * 0.5f;
            }
        }

        /// <summary>Kundenseite: so weit vor der Wagenmitte (Boden).</summary>
        public static Vector3 CustomerSpot(Transform cart, float distance) => cart.TransformPoint(CartCenter + ServeSide * distance);
        public static Vector3 CustomerFacing(Transform cart) => -cart.TransformDirection(ServeSide);

        static void Find()
        {
            Carts.RemoveAll(c => c == null);
            if (Carts.Count > 0) return;
            var group = GameObject.Find("HotdogCarts");
            if (group == null) return;
            foreach (Transform t in group.transform)
                if (t.name.StartsWith("HotdogCart_Park")) Carts.Add(t);
            Carts.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        public static void Spawn()
        {
            Carts.Clear();
            Spawned.Clear();
            Find();
            if (Carts.Count == 0)
            {
                Debug.LogWarning("HotdogStand: keine Imbisswagen in den Parks (DriftSkate → Imbisswagen → In die Stadt setzen)");
                return;
            }
            var group = new GameObject("HotdogVendors").transform;
            for (int i = 0; i < Carts.Count; i++)
            {
                var cart = Carts[i];
                string id = Vendors[i % Vendors.Length];
                // Hoehe vom Wagen nehmen (er steht auf dem Platz), nicht per Strahl: darueber liegen Schirm und Kollisionskiste
                Vector3 pos = cart.TransformPoint(CartCenter + CartAxis * VendorAlong - ServeSide * VendorBack);
                pos.y = cart.position.y;
                var npc = StonerNpc.Spawn(id, pos, cart.TransformDirection(ServeSide), group);
                if (npc == null) continue;
                Spawned.Add(npc.gameObject.AddComponent<HotdogVendor>());
            }
        }
    }
}
