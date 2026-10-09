using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>Die neun Tuning-Regler. Werte sind 0..1, 0.5 ist Serie (Differential: 1 = gesperrt).</summary>
    public static class Tuning
    {
        public const int Power = 0, Steering = 1, SpringFront = 2, SpringRear = 3, RideHeight = 4,
                         Camber = 5, RearPressure = 6, Differential = 7, BrakeBias = 8, Count = 9;

        public static readonly string[] Labels =
        {
            "Motorleistung", "Lenkwinkel", "Federn vorne", "Federn hinten", "Tieferlegung",
            "Sturz vorne", "Reifendruck hinten", "Differential", "Bremsbalance"
        };

        public static readonly string[] Hints =
        {
            "Mehr Leistung = Winkel leichter halten",
            "Mehr Einschlag = groessere Driftwinkel",
            "Hart = direkt, weich = mehr Grip vorne",
            "Weich = mehr Grip hinten, hart = bricht leichter aus",
            "Tiefer = stabiler, aber weniger Federweg",
            "Mehr Sturz = mehr Grip vorne im Drift",
            "Mehr Druck = weniger Grip hinten",
            "Offen bis gesperrt (Drift: gesperrt)",
            "Links = hinten, rechts = vorne"
        };

        /// <summary>Sturz-Optik der Raeder in Grad (aus dem Regler "Sturz vorne").</summary>
        public static float VisualCamber(float[] t) => t == null || t.Length != Count ? 2f : Mathf.Max(0f, t[Camber] - 0.25f) / 0.75f * 9f;

        public static float[] Defaults()
        {
            var t = new float[Count];
            for (int i = 0; i < Count; i++) t[i] = 0.5f;
            t[Differential] = 1f;
            return t;
        }
    }

    [Serializable]
    public class CarSave
    {
        public string id;
        public float[] tuning = Tuning.Defaults();
        public Color paint;
        public CarDesign design;      // Folierung, Anbauteile, Felgen (null = Werksoptik)
    }

    [Serializable]
    public class PlayerProfile
    {
        public long money = 5000;
        public int sprayCans = 12; // Startvorrat; Nachschub im Spray-Shop.
        public long bestCombo;
        public string playerName = "Rookie";

        public List<string> ownedCars = new List<string>();
        public string selectedCar = "roku86";
        public List<CarSave> carSaves = new List<CarSave>();

        public List<string> ownedBoards = new List<string>();
        public string selectedBoard = "standard";

        /// <summary>Leer = Standardfigur mit Outfit, sonst ein Mod-Charakter ("mod:skater/...").</summary>
        public string selectedCharacter = "";

        // Admin-Modus zum Testen
        public bool adminMode;
        public bool adminFree, adminNoBail, adminNoComboFail, adminMoon, adminTurbo, adminSlowMo, adminDebug, adminHideHud;

        public List<string> ownedOutfits = new List<string>();
        public string[] equipped = { "head_hair", "jacket_blue", "pants_denim", "shoes_white" };

        public List<string> ownedGarages = new List<string>();
        public string selectedGarage = "backyard";

        public Color crewColor = new Color(1f, 0.24f, 0.55f);
        public int assistLevel = 2;          // 0 aus, 1 schwach, 2 mittel, 3 stark
        public bool autoGearbox = true;
        public float musicVolume = 0.6f, sfxVolume = 0.8f;
        public float uiVolume = 0.8f; // Menue-Geraeusche (Spray beim Reiterwechsel)
        public string lastHostAddress = "127.0.0.1";
        public string sky = "abend";        // Himmel-Variante in der Stadt (SkyLook.Presets)
        public string weather = "dynamisch"; // Wetter: "klar", "dynamisch" (Regen kommt und geht), "regen", "gewitter"; online gilt der Host
        public bool reflections = true;     // Spiegelungen auf nassem Boden (kostet etwas Leistung)
        public int lunaChallenge;           // wie viele Spielmodi von Luna schon geschafft sind (LunaChallenges)
        public int jojoQuest;               // wie viele Auftraege von Jojo schon erledigt sind (JojoQuests)
        public int moeQuest;                // wie viele Jobs von Moe in der Gasse schon erledigt sind (MoeQuests)
        public int nixDeals;                // wie oft man bei Nix schon gekauft hat (NixDealer)
        public bool nixFriend;              // Nix hat den Freundschaftspreis schon angesagt
        public int baggies;                 // Tueten im Rucksack (Chill: G / Steuerkreuz links)
        public bool angelWings;             // Easter Egg: Engelsfluegel gefunden und am Ruecken (AngelWingsPickup)
        public int cameraView;              // Kamera-Perspektive in der Stadt (CameraRig.Views): 0 Standard, 1 Nah, 2 Weit

        public CarSave GetCarSave(string carId)
        {
            var save = carSaves.Find(c => c.id == carId);
            if (save == null)
            {
                save = new CarSave { id = carId, paint = Catalog.Car(carId).defaultColor };
                carSaves.Add(save);
            }
            if (save.tuning == null || save.tuning.Length != Tuning.Count) save.tuning = Tuning.Defaults();
            if (save.design == null || !save.design.initialized) save.design = CarDesign.Default(carId, crewColor);
            save.design.Sanitize();
            save.design.camber = Tuning.VisualCamber(save.tuning);
            return save;
        }

        public string Equipped(OutfitSlot slot) => equipped[(int)slot];

        /// <summary>Stellplaetze = die groesste Garage, die man besitzt.</summary>
        public int GarageSlots
        {
            get
            {
                int max = 2;
                foreach (var g in Catalog.Garages)
                    if (ownedGarages.Contains(g.id)) max = Mathf.Max(max, g.slots);
                return max;
            }
        }

        /// <summary>Stellt sicher, dass Startinhalte vorhanden sind (auch nach Updates).</summary>
        public void EnsureDefaults()
        {
            void Own(List<string> list, string id) { if (!list.Contains(id)) list.Add(id); }
            Own(ownedCars, "roku86");
            Own(ownedBoards, "standard");
            Own(ownedGarages, "backyard");
            foreach (var o in Catalog.Outfits) if (o.price == 0) Own(ownedOutfits, o.id);
            if (equipped == null || equipped.Length != 4)
                equipped = new[] { "head_hair", "jacket_blue", "pants_denim", "shoes_white" };
            if (!ownedCars.Contains(selectedCar)) selectedCar = "roku86";
            if (!ownedBoards.Contains(selectedBoard) && !ModLibrary.IsMod(selectedBoard)) selectedBoard = "standard";
            if (selectedCharacter == null) selectedCharacter = "";
            if (!ownedGarages.Contains(selectedGarage)) selectedGarage = "backyard";
            if (string.IsNullOrWhiteSpace(playerName)) playerName = "Rookie";
            foreach (var id in ownedCars) GetCarSave(id);
        }
    }

    /// <summary>Speichert das Profil als JSON und das Graffiti als PNG im persistenten Datenordner.</summary>
    public static class SaveSystem
    {
        static PlayerProfile _profile;

        /// <summary>Ordner fuer Spielstand und Graffiti (Tests nutzen einen eigenen Ordner).</summary>
        public static string DataFolder = null;
        static string Folder => string.IsNullOrEmpty(DataFolder) ? Application.persistentDataPath : DataFolder;

        public static string ProfilePath => Path.Combine(Folder, "profile.json");
        public static string GraffitiPath => Path.Combine(Folder, "graffiti.png");
        public static string MusicFolder => Path.Combine(Application.persistentDataPath, "Music");

        public static event Action GraffitiChanged;

        public static PlayerProfile Profile
        {
            get
            {
                if (_profile == null) Load();
                return _profile;
            }
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(ProfilePath))
                {
                    string json = File.ReadAllText(ProfilePath);
                    _profile = JsonUtility.FromJson<PlayerProfile>(json);
                    // Older saves receive the starting supply once; an explicitly saved zero stays empty.
                    if (_profile != null && json.IndexOf("\"sprayCans\"", StringComparison.Ordinal) < 0)
                        _profile.sprayCans = 12;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Profil konnte nicht geladen werden, starte neu: " + e.Message);
            }
            if (_profile == null) _profile = new PlayerProfile();
            _profile.EnsureDefaults();
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(ProfilePath, JsonUtility.ToJson(Profile, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Profil konnte nicht gespeichert werden: " + e.Message);
            }
        }

        public static void AddMoney(long amount)
        {
            Profile.money = Math.Max(0, Profile.money + amount);
            Save();
        }

        public static bool TrySpend(long amount)
        {
            if (Admin.Free) return true;
            if (Profile.money < amount) return false;
            Profile.money -= amount;
            Save();
            return true;
        }

        static Texture2D _graffiti;

        /// <summary>Das eigene Graffiti (256x256, transparent). Wird bei Bedarf erzeugt.</summary>
        public static Texture2D Graffiti
        {
            get
            {
                if (_graffiti != null) return _graffiti;
                _graffiti = new Texture2D(256, 256, TextureFormat.RGBA32, false) { name = "Graffiti", wrapMode = TextureWrapMode.Clamp };
                bool loaded = false;
                try
                {
                    if (File.Exists(GraffitiPath)) loaded = _graffiti.LoadImage(File.ReadAllBytes(GraffitiPath));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Graffiti konnte nicht geladen werden: " + e.Message);
                }
                if (!loaded || _graffiti.width != 256) GraffitiPainter.DrawDefaultTag(_graffiti);
                return _graffiti;
            }
        }

        public static void SaveGraffiti()
        {
            try
            {
                File.WriteAllBytes(GraffitiPath, Graffiti.EncodeToPNG());
            }
            catch (Exception e)
            {
                Debug.LogWarning("Graffiti konnte nicht gespeichert werden: " + e.Message);
            }
            GraffitiChanged?.Invoke();
        }
    }

    public enum SessionMode { Solo, Host, Client }

    /// <summary>Wie die Stadt gestartet werden soll (wird in der Garage gesetzt).</summary>
    public static class GameSession
    {
        public static SessionMode Mode = SessionMode.Solo;
        public static string Address = "127.0.0.1";
        public static ushort Port = 7777;
        public static string LastError;
    }
}
