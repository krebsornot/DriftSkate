using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    public enum BodyStyle { Coupe, Hatch, Sedan, Muscle }

    public class CarDef
    {
        public string id, name, blurb;
        public long price;
        public int horsepower;          // Anzeige
        public float peakTorque;        // Nm
        public float maxRpm;
        public float mass;              // kg
        public float length, width, height, wheelbase, track, wheelRadius;
        public BodyStyle style;
        public Color defaultColor;
        public bool popUpLights, spoiler;
        public float[] gears;
        public float finalDrive;
    }

    public class BoardDef
    {
        public string id, name, blurb;
        public long price;
        public Color deck, wheels;
        public Color trucks = Palette.Metal, bushings = Palette.Orange;
        public BoardShape shape = BoardShape.Popsicle();
        public float speed = 1f, pop = 1f, balance = 1f;
        public bool isMod; // eigenes Board aus dem Mods-Ordner
    }

    public enum OutfitSlot { Head, Jacket, Pants, Shoes }

    public class OutfitDef
    {
        public string id, name;
        public OutfitSlot slot;
        public long price;
        public Color color;
        public int variant; // Kopf: 0 Haare, 1 Cap, 2 Beanie, 3 Kopfhoerer
    }

    public class GarageDef
    {
        public string id, name, blurb;
        public long price;
        public int slots;
        public Color wall, floor, neon;
        public float size;
    }

    /// <summary>Alle kaufbaren Inhalte. Autos sind von japanischen Drift-Legenden inspiriert (Formen in CarShape), ohne Marken.</summary>
    public static class Catalog
    {
        public static readonly List<CarDef> Cars = new List<CarDef>
        {
            new CarDef {
                id = "roku86", name = "Roku AE", blurb = "Leichter Klassiker. Wenig Leistung, viel Gefuehl.",
                price = 0, horsepower = 170, peakTorque = 270, maxRpm = 7800, mass = 1050,
                length = 4.2f, width = 1.66f, height = 1.33f, wheelbase = 2.4f, track = 1.42f, wheelRadius = 0.30f,
                style = BodyStyle.Hatch, defaultColor = Palette.White, popUpLights = true,
                gears = new[] { 3.6f, 2.2f, 1.55f, 1.18f, 0.95f }, finalDrive = 4.3f },
            new CarDef {
                id = "sylph15", name = "Sylph S15", blurb = "Ausgewogenes Drift-Coupe, perfekt zum Lernen.",
                price = 150000, horsepower = 250, peakTorque = 360, maxRpm = 7500, mass = 1240,
                length = 4.45f, width = 1.70f, height = 1.29f, wheelbase = 2.52f, track = 1.48f, wheelRadius = 0.32f,
                style = BodyStyle.Coupe, defaultColor = Palette.Yellow, spoiler = true,
                gears = new[] { 3.3f, 2.1f, 1.5f, 1.15f, 0.92f, 0.78f }, finalDrive = 4.1f },
            new CarDef {
                id = "kazefc", name = "Kaze FC", blurb = "Drehfreudiger Wankel, sehr agil.",
                price = 300000, horsepower = 280, peakTorque = 330, maxRpm = 8800, mass = 1220,
                length = 4.3f, width = 1.69f, height = 1.27f, wheelbase = 2.43f, track = 1.45f, wheelRadius = 0.31f,
                style = BodyStyle.Coupe, defaultColor = Palette.Red, popUpLights = true,
                gears = new[] { 3.5f, 2.2f, 1.6f, 1.2f, 0.95f, 0.8f }, finalDrive = 4.3f },
            new CarDef {
                id = "mark2j", name = "Mark II J", blurb = "Vier Tueren, langer Radstand, stabile Winkel.",
                price = 500000, horsepower = 330, peakTorque = 450, maxRpm = 7200, mass = 1450,
                length = 4.8f, width = 1.75f, height = 1.4f, wheelbase = 2.73f, track = 1.5f, wheelRadius = 0.33f,
                style = BodyStyle.Sedan, defaultColor = Palette.Purple,
                gears = new[] { 3.2f, 2.0f, 1.45f, 1.1f, 0.88f, 0.76f }, finalDrive = 3.9f },
            new CarDef {
                id = "toro2j", name = "Toro 2J", blurb = "Reihensechser mit Turbo. Viel Druck, viel Rauch.",
                price = 900000, horsepower = 420, peakTorque = 560, maxRpm = 7400, mass = 1480,
                length = 4.52f, width = 1.81f, height = 1.27f, wheelbase = 2.55f, track = 1.52f, wheelRadius = 0.33f,
                style = BodyStyle.Coupe, defaultColor = Palette.Orange, spoiler = true,
                gears = new[] { 3.25f, 2.05f, 1.48f, 1.12f, 0.9f, 0.76f }, finalDrive = 3.8f },
            new CarDef {
                id = "muscle8", name = "Raijin 34R", blurb = "Reihensechser Twin-Turbo. Kantig, breit, eine Legende.",
                price = 1500000, horsepower = 500, peakTorque = 620, maxRpm = 7600, mass = 1560,
                length = 4.6f, width = 1.79f, height = 1.36f, wheelbase = 2.67f, track = 1.52f, wheelRadius = 0.34f,
                style = BodyStyle.Coupe, defaultColor = Palette.Hex("3D5FD8"),
                gears = new[] { 2.9f, 1.95f, 1.4f, 1.08f, 0.86f, 0.7f }, finalDrive = 3.6f },
        };

        public static readonly List<BoardDef> Boards = new List<BoardDef>
        {
            new BoardDef { id = "standard", name = "Standard", blurb = "Solides 8\"-Popsicle fuer den Anfang.", price = 0,
                deck = Palette.Pink, wheels = Palette.White, shape = BoardShape.Popsicle(8f) },
            new BoardDef { id = "streetpro", name = "Street Pro", blurb = "Steile Kicks und kleine harte Rollen: mehr Pop.", price = 40000,
                deck = Palette.Lime, wheels = Palette.Yellow, bushings = Palette.Red, pop = 1.18f, shape = BoardShape.Street },
            new BoardDef { id = "cruiser", name = "Cruiser", blurb = "Breiter Cruiser mit grossen weichen Rollen: mehr Tempo.", price = 60000,
                deck = Palette.Cyan, wheels = Palette.Orange, bushings = Palette.Yellow, speed = 1.2f, pop = 0.95f, shape = BoardShape.Cruiser },
            new BoardDef { id = "grindking", name = "Grind King", blurb = "8,5\" breit mit hohen Trucks: ruhiger auf Rails und im Manual.", price = 90000,
                deck = Palette.Purple, wheels = Palette.Cyan, bushings = Palette.Lime, balance = 1.45f, shape = BoardShape.Wide },
            new BoardDef { id = "goldie", name = "Goldie", blurb = "Alles ein bisschen besser. Und es glaenzt.", price = 250000,
                deck = Palette.Hex("F5C542"), wheels = Palette.Ink, trucks = Palette.Hex("DCDFE8"), bushings = Palette.Yellow,
                speed = 1.12f, pop = 1.12f, balance = 1.2f, shape = BoardShape.Popsicle(8.25f) },
        };

        public static readonly List<OutfitDef> Outfits = new List<OutfitDef>
        {
            new OutfitDef { id = "head_hair", name = "Struwwelhaare", slot = OutfitSlot.Head, price = 0, color = Palette.Hex("2A1E1A"), variant = 0 },
            new OutfitDef { id = "head_cap_y", name = "Cap Gelb", slot = OutfitSlot.Head, price = 8000, color = Palette.Yellow, variant = 1 },
            new OutfitDef { id = "head_beanie_p", name = "Beanie Pink", slot = OutfitSlot.Head, price = 12000, color = Palette.Pink, variant = 2 },
            new OutfitDef { id = "head_phones", name = "Kopfhoerer", slot = OutfitSlot.Head, price = 30000, color = Palette.Cyan, variant = 3 },

            new OutfitDef { id = "jacket_blue", name = "Jacke Blau", slot = OutfitSlot.Jacket, price = 0, color = Palette.Blue },
            new OutfitDef { id = "jacket_orange", name = "Hoodie Orange", slot = OutfitSlot.Jacket, price = 10000, color = Palette.Orange },
            new OutfitDef { id = "jacket_lime", name = "Track Jacket Lime", slot = OutfitSlot.Jacket, price = 18000, color = Palette.Lime },
            new OutfitDef { id = "jacket_white", name = "Bomber Weiss", slot = OutfitSlot.Jacket, price = 25000, color = Palette.White },

            new OutfitDef { id = "pants_denim", name = "Jeans", slot = OutfitSlot.Pants, price = 0, color = Palette.Hex("2F3E6E") },
            new OutfitDef { id = "pants_cargo", name = "Cargo Oliv", slot = OutfitSlot.Pants, price = 9000, color = Palette.Hex("5B6B3A") },
            new OutfitDef { id = "pants_purple", name = "Baggy Lila", slot = OutfitSlot.Pants, price = 15000, color = Palette.Purple },

            new OutfitDef { id = "shoes_white", name = "Sneaker Weiss", slot = OutfitSlot.Shoes, price = 0, color = Palette.White },
            new OutfitDef { id = "shoes_red", name = "Sneaker Rot", slot = OutfitSlot.Shoes, price = 7000, color = Palette.Red },
            new OutfitDef { id = "shoes_yellow", name = "Skate-Schuh Gelb", slot = OutfitSlot.Shoes, price = 11000, color = Palette.Yellow },
        };

        public static readonly List<GarageDef> Garages = new List<GarageDef>
        {
            new GarageDef { id = "backyard", name = "Hinterhof", blurb = "Klein, aber deins. Platz fuer 2 Autos.", price = 0,
                slots = 2, wall = Palette.Hex("8C7A9E"), floor = Palette.Hex("6E6A78"), neon = Palette.Pink, size = 10f },
            new GarageDef { id = "workshop", name = "Werkstatt", blurb = "Mit Hebebuehne. Platz fuer 4 Autos.", price = 120000,
                slots = 4, wall = Palette.Hex("3F6E8C"), floor = Palette.Hex("5E6470"), neon = Palette.Cyan, size = 14f },
            new GarageDef { id = "hall", name = "Halle", blurb = "Grosse Halle fuer die Crew. 8 Autos.", price = 450000,
                slots = 8, wall = Palette.Hex("7A3F5E"), floor = Palette.Hex("4E4A5A"), neon = Palette.Yellow, size = 18f },
            new GarageDef { id = "loft", name = "Rooftop-Loft", blurb = "Ganz oben ueber der Stadt. 12 Autos.", price = 1200000,
                slots = 12, wall = Palette.Hex("2E2A4A"), floor = Palette.Hex("3A3650"), neon = Palette.Lime, size = 22f },
        };

        public static CarDef Car(string id) => Cars.Find(c => c.id == id) ?? Cars[0];
        public static BoardDef Board(string id) => Boards.Find(b => b.id == id) ?? (ModLibrary.IsMod(id) ? ModLibrary.BoardDef(id) : null) ?? Boards[0];
        public static OutfitDef Outfit(string id) => Outfits.Find(o => o.id == id);
        public static GarageDef Garage(string id) => Garages.Find(g => g.id == id) ?? Garages[0];

        public static int CarIndex(string id) => Mathf.Max(0, Cars.FindIndex(c => c.id == id));
        public static int BoardIndex(string id) => Mathf.Max(0, Boards.FindIndex(b => b.id == id));
        public static int OutfitIndex(string id) => Outfits.FindIndex(o => o.id == id);
    }
}
