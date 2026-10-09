using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Admin-Modus zum Testen. Alle Schalter wirken nur, wenn der Admin-Modus an ist,
    /// und nur fuer den eigenen Spieler (online sehen andere davon nichts ausser den Folgen).
    /// </summary>
    public static class Admin
    {
        static PlayerProfile P => SaveSystem.Profile;

        public static bool On => P.adminMode;
        public static bool Free => On && P.adminFree;
        public static bool NoBail => On && P.adminNoBail;
        public static bool NoComboFail => On && P.adminNoComboFail;
        public static bool Moon => On && P.adminMoon;
        public static bool Turbo => On && P.adminTurbo;
        public static bool SlowMo => On && P.adminSlowMo && GameSession.Mode == SessionMode.Solo;
        public static bool Debug => On && P.adminDebug;
        public static bool HideHud => On && P.adminHideHud;

        /// <summary>Alle Schalter mit Anzeigename, Getter und Setter (fuer Garage und Admin-Menue).</summary>
        public static readonly (string label, System.Func<bool> get, System.Action<bool> set)[] Toggles =
        {
            ("Alles gratis", () => P.adminFree, v => P.adminFree = v),
            ("Keine Stuerze", () => P.adminNoBail, v => P.adminNoBail = v),
            ("Combo bricht nie ab", () => P.adminNoComboFail, v => P.adminNoComboFail = v),
            ("Mond-Schwerkraft", () => P.adminMoon, v => P.adminMoon = v),
            ("Turbo (Auto + Board)", () => P.adminTurbo, v => P.adminTurbo = v),
            ("Zeitlupe (nur Solo)", () => P.adminSlowMo, v => P.adminSlowMo = v),
            ("Debug-Anzeige", () => P.adminDebug, v => P.adminDebug = v),
            ("HUD ausblenden", () => P.adminHideHud, v => P.adminHideHud = v),
        };

        public static void SetMode(bool on)
        {
            P.adminMode = on;
            SaveSystem.Save();
            ApplyTimeScale();
        }

        public static void Toggle(int index)
        {
            var t = Toggles[index];
            t.set(!t.get());
            SaveSystem.Save();
            ApplyTimeScale();
        }

        public static void ApplyTimeScale()
        {
            Time.timeScale = SlowMo ? 0.4f : 1f;
            Time.fixedDeltaTime = 0.01f * Time.timeScale;
        }

        /// <summary>Alle Autos, Boards, Outfits und Garagen besitzen.</summary>
        public static void UnlockAll()
        {
            foreach (var c in Catalog.Cars) if (!P.ownedCars.Contains(c.id)) P.ownedCars.Add(c.id);
            foreach (var b in Catalog.Boards) if (!P.ownedBoards.Contains(b.id)) P.ownedBoards.Add(b.id);
            foreach (var o in Catalog.Outfits) if (!P.ownedOutfits.Contains(o.id)) P.ownedOutfits.Add(o.id);
            foreach (var g in Catalog.Garages) if (!P.ownedGarages.Contains(g.id)) P.ownedGarages.Add(g.id);
            P.EnsureDefaults();
            SaveSystem.Save();
        }

        public static void AddMoney(long amount) => SaveSystem.AddMoney(amount);

        /// <summary>Teleport-Ziele in der Stadt (Name, Position).</summary>
        public static (string name, Vector3 pos)[] TeleportSpots => new[]
        {
            ("Start", new Vector3(-18f, 0.5f, CityBuilder.RoadCenter(2))),
            ("Brunnenplatz", new Vector3(0f, 0.5f, -26f)),
            ("Skatepark", new Vector3(CityBuilder.BlockCenter(1), 0.5f, CityBuilder.BlockCenter(3) - 12f)),
            ("Drift-Platz", new Vector3(CityBuilder.BlockCenter(1), 0.5f, CityBuilder.BlockCenter(1))),
            ("Hafen Ost", new Vector3(CityBuilder.Half - 14f, 0.5f, 10f)),
            ("Plaza", new Vector3(CityBuilder.BlockCenter(1), 0.5f, CityBuilder.BlockCenter(2))),
            ("Gasse (Nix & Moe)", AlleyCrew.TeleportPoint),
            ("Engelsfluegel", AngelWingsPickup.Instance != null ? AngelWingsPickup.Instance.transform.position + Vector3.back * 3f : new Vector3(0f, 0.5f, -26f)),
            ("Park (Miru)", ParkMiru.TeleportPoint),
        };
    }
}
