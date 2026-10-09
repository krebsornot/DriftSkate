using UnityEngine;

namespace DriftSkate
{
    /// <summary>Baut den Garagenraum passend zur gewaehlten Garage (Groesse, Farben, Neon, Stellplaetze).</summary>
    public class GarageEnvironment : MonoBehaviour
    {
        public Transform turntable;
        public Transform carRoot;
        public Transform skaterRoot;
        Transform _room;

        public void Build(GarageDef g, PlayerProfile p)
        {
            if (_room != null)
            {
                _room.name = "_removed";
                _room.gameObject.SetActive(false);
                Shapes.DestroySafe(_room.gameObject);
            }
            _room = Shapes.Group(transform, "Room");
            float w = g.size, d = g.size * 0.8f, h = 6.5f;

            Shapes.Box(_room, new Vector3(0, -0.1f, 0), new Vector3(w * 2f, 0.2f, d * 2f), g.floor, 0f, name: "Floor");
            Shapes.Box(_room, new Vector3(0, h * 0.5f, d), new Vector3(w * 2f, h, 0.4f), g.wall, 0.5f, name: "BackWall");
            Shapes.Box(_room, new Vector3(-w, h * 0.5f, 0), new Vector3(0.4f, h, d * 2f), Palette.Shade(g.wall, 0.85f), 0.5f, name: "WallL");
            Shapes.Box(_room, new Vector3(w, h * 0.5f, 0), new Vector3(0.4f, h, d * 2f), Palette.Shade(g.wall, 0.85f), 0.5f, name: "WallR");
            for (int i = -2; i <= 2; i++)
                Shapes.Box(_room, new Vector3(0, h, i * d * 0.4f), new Vector3(w * 2f, 0.4f, 0.5f), Palette.Ink, 0.2f, name: "Beam");

            // Bodenmarkierungen und Neon
            Shapes.Box(_room, new Vector3(0, 0.01f, 0), new Vector3(7.4f, 0.02f, 7.4f), Palette.Yellow, 0f, name: "Marking");
            Shapes.Box(_room, new Vector3(0, 0.015f, 0), new Vector3(7f, 0.02f, 7f), g.floor, 0f, name: "MarkingInner");
            Shapes.Box(_room, new Vector3(0, h - 0.8f, d - 0.25f), new Vector3(w * 1.6f, 0.15f, 0.1f), g.neon, 0f, emission: 2f, name: "Neon");
            Shapes.Box(_room, new Vector3(-w + 0.25f, h - 1.2f, 0), new Vector3(0.1f, 0.15f, d * 1.4f), g.neon, 0f, emission: 2f, name: "Neon");
            Shapes.Box(_room, new Vector3(w - 0.25f, h - 1.2f, 0), new Vector3(0.1f, 0.15f, d * 1.4f), g.neon, 0f, emission: 2f, name: "Neon");

            // Eigenes Graffiti gross an der Rueckwand
            var decal = ToonMaterials.CreateDecal(SaveSystem.Graffiti);
            Shapes.Decal(_room, new Vector3(0, 3.2f, d - 0.22f), new Vector2(4.4f, 4.4f), Vector3.back, decal, "WallGraffiti");

            // Regal mit Reifen und Werkzeug
            Vector3 shelf = new Vector3(-w + 1.2f, 0, d - 1.5f);
            for (int i = 0; i < 3; i++)
                Shapes.Box(_room, shelf + new Vector3(0, 0.8f + i * 1.1f, 0), new Vector3(1.6f, 0.1f, 2.4f), Palette.Metal, 0.2f, name: "Shelf");
            for (int i = 0; i < 4; i++)
                Shapes.Part(PrimitiveType.Cylinder, _room, new Vector3(w - 1.4f, 0.17f + i * 0.34f, d - 1.6f), new Vector3(1.2f, 0.16f, 1.2f), Palette.Rubber, 0.2f, name: "Tire");
            Shapes.Box(_room, new Vector3(w - 1.6f, 0.5f, -d + 2.4f), new Vector3(1.4f, 1f, 0.8f), Palette.Red, 0.3f, name: "Toolbox");
            Color[] posters = { Palette.Pink, Palette.Cyan, Palette.Lime };
            for (int i = 0; i < 3; i++)
                Shapes.Box(_room, new Vector3(-w + 0.25f, 2.6f, -d * 0.5f + i * 2.4f), new Vector3(0.05f, 1.8f, 1.3f), posters[i], 0.2f, euler: new Vector3(0, 0, i * 3f - 3f), name: "Poster");

            // Geparkte eigene Autos (ausser dem gewaehlten) an den Stellplaetzen
            int slot = 0;
            foreach (var id in p.ownedCars)
            {
                if (id == p.selectedCar) continue;
                if (slot >= g.slots - 1) break;
                var def = Catalog.Car(id);
                var parked = Shapes.Group(_room, "Parked_" + id);
                bool leftSide = slot % 2 == 0;
                parked.localPosition = new Vector3((leftSide ? -1 : 1) * (w - 3f), 0, -d * 0.5f + (slot / 2) * 5.5f);
                parked.localRotation = Quaternion.Euler(0, leftSide ? 90f : -90f, 0);
                CarBuilder.Build(parked, def, p.GetCarSave(id).paint, p.crewColor, SaveSystem.Graffiti, null, p.GetCarSave(id).design);
                slot++;
            }

            if (turntable == null)
            {
                var tt = Shapes.Part(PrimitiveType.Cylinder, transform, new Vector3(0, 0.05f, 0), new Vector3(6.4f, 0.05f, 6.4f), Palette.Hex("3B3A48"), 0.3f, name: "Turntable");
                turntable = tt.transform;
                carRoot = Shapes.Group(transform, "CarRoot", new Vector3(0, 0.1f, 0));
                skaterRoot = Shapes.Group(transform, "SkaterRoot", new Vector3(3.9f, 0f, -1.2f));
                skaterRoot.localRotation = Quaternion.Euler(0, -150f, 0);
            }
        }

        /// <summary>Beim Folieren: Auto anhalten und so drehen, dass die bearbeitete Seite zur Kamera zeigt (null = Drehscheibe laeuft).</summary>
        public static float? HoldYaw;

        void Update()
        {
            if (carRoot != null)
            {
                if (HoldYaw.HasValue)
                    carRoot.rotation = Quaternion.Slerp(carRoot.rotation, Quaternion.Euler(0f, HoldYaw.Value, 0f), 1f - Mathf.Exp(-4f * Time.deltaTime));
                else carRoot.Rotate(0, 10f * Time.deltaTime, 0, Space.World);
            }
            if (turntable != null) turntable.rotation = Quaternion.Euler(0, carRoot != null ? carRoot.eulerAngles.y : 0f, 0);
        }
    }
}
