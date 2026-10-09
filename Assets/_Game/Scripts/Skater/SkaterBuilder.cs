using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Baut die Spielfigur und das Board aus Grundformen. Feste Figur, aber jede Kleidung austauschbar.
    /// Hierarchie: Skater > Align > BoardPivot / BodyPivot. BoardPivot wird fuer Flip-Tricks gedreht.
    /// </summary>
    public static class SkaterBuilder
    {
        public struct Outfit
        {
            public Color head, jacket, pants, shoes, crew;
            public int headVariant;

            public static Outfit FromProfile(PlayerProfile p)
            {
                var o = FromIds(p.equipped);
                o.crew = p.crewColor;
                return o;
            }

            public static Outfit FromIds(string[] ids, Color? crew = null)
            {
                var o = new Outfit { head = Palette.Hex("2A1E1A"), jacket = Palette.Blue, pants = Palette.Hex("2F3E6E"), shoes = Palette.White, crew = crew ?? Palette.Pink };
                if (ids == null) return o;
                foreach (var id in ids)
                {
                    var def = Catalog.Outfit(id);
                    if (def == null) continue;
                    switch (def.slot)
                    {
                        case OutfitSlot.Head: o.head = def.color; o.headVariant = def.variant; break;
                        case OutfitSlot.Jacket: o.jacket = def.color; break;
                        case OutfitSlot.Pants: o.pants = def.color; break;
                        case OutfitSlot.Shoes: o.shoes = def.color; break;
                    }
                }
                return o;
            }
        }

        /// <summary>Baut Board und Figur unter 'root'. Gibt die Pivots an den Controller (falls vorhanden).</summary>
        public static void Build(Transform root, Outfit outfit, BoardDef board, Texture graffiti, SkaterController sc, string characterId = null)
        {
            // Align und BoardPivot bleiben bestehen (BoardPivot traegt im Prefab eine NetworkTransform
            // und darf nicht umgehaengt werden). Nur ihr Inhalt wird neu gebaut.
            var align = root.Find("Align");
            if (align == null) align = Shapes.Group(root, "Align");
            var boardPivot = align.Find("BoardPivot");
            if (boardPivot == null) boardPivot = Shapes.Group(align, "BoardPivot", new Vector3(0, 0.1f, 0));

            ClearChildren(boardPivot);
            var oldBody = align.Find("BodyPivot");
            if (oldBody != null)
            {
                oldBody.name = "_removed";
                oldBody.gameObject.SetActive(false);
                Shapes.DestroySafe(oldBody.gameObject);
            }
            var metrics = BuildBoard(boardPivot, board, graffiti);

            var body = Shapes.Group(align, "BodyPivot", new Vector3(0, 0.1f, 0));
            body.localRotation = Quaternion.Euler(0, 80f, 0);
            SkaterRig rig = BuildModel(body, outfit, characterId);
            if (rig == null)
            {
                body.localPosition = new Vector3(0, 0.13f, 0);
                BuildBody(body, outfit); // Ersatzfigur, falls das Modell fehlt
            }
            else
            {
                rig.board = boardPivot;
                rig.frame = body;
                rig.travel = root;
                rig.deckTop = metrics.deckTop;
            }

            if (sc != null)
            {
                sc.align = align;
                sc.boardPivot = boardPivot;
                sc.bodyPivot = body;
                sc.board = board;
                sc.rig = rig;
                // Beim Grinden liegt der Hanger auf der Rail, beim Boardslide die Deck-Unterseite (Rail 2 cm unter der Wurzel)
                sc.grindBoardY = -0.02f - metrics.hangerBottom;
                sc.slideBoardY = -0.02f - metrics.deckBottom;
            }
        }

        static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var c = t.GetChild(i).gameObject;
                c.SetActive(false);
                Shapes.DestroySafe(c);
            }
        }

        static BoardMetrics BuildBoard(Transform pivot, BoardDef board, Texture graffiti)
        {
            if (board.isMod && BuildModBoard(pivot, board, out var modMetrics)) return modMetrics;
            return BoardBuilder.Build(pivot, board, graffiti);
        }

        /// <summary>
        /// Laedt die Figur aus Resources/Characters/Skater (aus Blender, siehe Tools/Blender/build_skater.py),
        /// faerbt sie nach Outfit ein und haengt das prozedurale Rig an.
        /// </summary>
        static SkaterRig BuildModel(Transform body, Outfit o, string characterId)
        {
            var mod = ModLibrary.Character(characterId);
            if (mod != null && mod.loaded && mod.template != null)
            {
                var modModel = Object.Instantiate(mod.template, body, false);
                modModel.name = "Model";
                modModel.SetActive(true);
                var modRig = PrepareModel(modModel, body, mod.height, mod.bones, false);
                if (modRig != null) return modRig;
                modModel.SetActive(false);
                Shapes.DestroySafe(modModel);
            }

            var prefab = Resources.Load<GameObject>("Characters/Skater");
            if (prefab == null) return null;
            var model = Object.Instantiate(prefab, body, false);
            model.name = "Model";

            string[] variants = { "Head_Hair", "Head_Cap", "Head_Beanie", "Head_Phones" };
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int v = System.Array.IndexOf(variants, smr.name);
                if (v >= 0) smr.gameObject.SetActive(v == o.headVariant);
                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = CharacterMaterial(mats[i] != null ? mats[i].name : "", o);
                smr.sharedMaterials = mats;
            }
            return PrepareModel(model, body, 0f, null, true);
        }

        /// <summary>
        /// Richtet ein Modell aus (Blick nach vorn ueber die Huefte: links/rechts), skaliert es auf Spielgroesse
        /// und haengt das Rig an. Gibt null zurueck, wenn das Skelett nicht passt.
        /// </summary>
        static SkaterRig PrepareModel(GameObject model, Transform body, float heightMeters, string[] bones, bool builtIn)
        {
            foreach (var anim in model.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(anim);
            foreach (var anim in model.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(anim);
            foreach (var col in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;

            var map = SkeletonMap.Resolve(model.transform, bones, out string missing);
            if (missing != null)
            {
                Debug.LogWarning("Figur passt nicht: Knochen fehlen (" + missing + ")");
                return null;
            }

            // Blickrichtung: senkrecht zur Linie rechte -> linke Huefte
            Vector3 up = body.up;
            Vector3 left = Vector3.ProjectOnPlane(map.upperLeg[0].position - map.upperLeg[1].position, up);
            if (left.sqrMagnitude > 1e-6f)
            {
                Vector3 facing = Vector3.Cross(up, left.normalized);
                float yaw = Vector3.SignedAngle(facing, body.forward, up);
                model.transform.rotation = Quaternion.AngleAxis(yaw, up) * model.transform.rotation;
            }

            if (!builtIn)
            {
                // Groesse: Kopfknochen ueber der Sohle wie bei der Standardfigur (1,56 m bei 1,80 m Koerpergroesse)
                float sole = float.MaxValue;
                foreach (var r in model.GetComponentsInChildren<Renderer>()) sole = Mathf.Min(sole, r.bounds.min.y);
                float headAboveSole = map.head.position.y - sole;
                if (sole < float.MaxValue && headAboveSole > 0.01f)
                {
                    float target = 1.56f * (heightMeters > 0.5f ? heightMeters : 1.8f) / 1.8f;
                    model.transform.localScale *= target / headAboveSole;
                }
            }

            var rig = model.AddComponent<SkaterRig>();
            rig.Init(model.transform, bones);
            return rig.Ready ? rig : null;
        }

        static Material CharacterMaterial(string name, Outfit o)
        {
            const float line = 0.28f;
            if (name.StartsWith("Skin")) return ToonMaterials.Get(Palette.Skin, line, false);
            if (name.StartsWith("JacketTrim")) return ToonMaterials.Get(Palette.Shade(o.jacket, 0.72f), line, false);
            if (name.StartsWith("Jacket")) return ToonMaterials.Get(o.jacket, line, false);
            if (name.StartsWith("Accent")) return ToonMaterials.Get(o.crew, 0.15f, false);
            if (name.StartsWith("Pants")) return ToonMaterials.Get(o.pants, line, false);
            if (name.StartsWith("Shoes")) return ToonMaterials.Get(o.shoes, line, false);
            if (name.StartsWith("Sole")) return ToonMaterials.Get(Palette.White, line, false);
            if (name.StartsWith("HeadWear")) return ToonMaterials.Get(o.head, line, false);
            if (name.StartsWith("HairDark")) return ToonMaterials.Get(Palette.Hex("2A1E1A"), line, false);
            if (name.StartsWith("EyeWhite")) return ToonMaterials.Get(Palette.White, 0f, false, 0.5f);
            if (name.StartsWith("Eyes")) return ToonMaterials.Get(Palette.Ink, 0f, false);
            return ToonMaterials.Get(Palette.Concrete, line, false);
        }

        /// <summary>
        /// Eigenes Board: auf 0,82 m Laenge skalieren und laengs ausrichten. Mit Rollen steht es auf dem Boden;
        /// ein reines Deck (flach im Verhaeltnis zur Laenge) bekommt die Standard-Trucks und -Rollen darunter.
        /// </summary>
        static bool BuildModBoard(Transform pivot, BoardDef board, out BoardMetrics metrics)
        {
            metrics = BoardBuilder.Measure(board.shape);
            var mod = ModLibrary.Board(board.id);
            if (mod == null || !mod.loaded || mod.template == null) return false;
            var model = Object.Instantiate(mod.template, pivot, false);
            model.name = "ModBoard";
            model.SetActive(true);
            foreach (var col in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);

            Quaternion savedRot = pivot.rotation;
            pivot.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            Bounds b = WorldBounds(model);
            if (b.size.x > b.size.z) model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * model.transform.localRotation;
            b = WorldBounds(model);
            float length = Mathf.Max(b.size.x, b.size.z);
            if (length > 1e-4f) model.transform.localScale *= 0.82f / length;
            b = WorldBounds(model);
            bool hasWheels = b.size.y > 0.11f;
            float bottom = hasWheels ? BoardBuilder.Ground : metrics.deckBottom;
            Vector3 p = pivot.position;
            model.transform.position += new Vector3(p.x - b.center.x, p.y + bottom - b.min.y, p.z - b.center.z);
            pivot.rotation = savedRot;
            if (hasWheels)
            {
                // Deck-Hoehe schaetzen: Standardhoehe, solange das Board nicht deutlich hoeher gebaut ist
                metrics.deckTop = Mathf.Max(metrics.deckTop, BoardBuilder.Ground + b.size.y - 0.05f);
                metrics.deckBottom = metrics.deckTop - 0.012f;
            }
            else
            {
                metrics.deckTop = metrics.deckBottom + Mathf.Min(0.015f, b.size.y * 0.4f);
                BoardBuilder.BuildTrucksOnly(pivot, board);
            }
            return true;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        static void BuildBody(Transform body, Outfit o)
        {
            Color skin = Palette.Skin;
            // Beine und Schuhe (Fuesse stehen in X-Richtung = entlang des Boards)
            for (int s = -1; s <= 1; s += 2)
            {
                Shapes.Box(body, new Vector3(s * 0.2f, 0.06f, 0.03f), new Vector3(0.13f, 0.11f, 0.3f), o.shoes, name: "Shoe");
                Shapes.Box(body, new Vector3(s * 0.17f, 0.5f, 0f), new Vector3(0.17f, 0.8f, 0.19f), o.pants, euler: new Vector3(0, 0, -s * 4f), name: "Leg");
            }
            Shapes.Box(body, new Vector3(0, 0.95f, 0), new Vector3(0.44f, 0.18f, 0.24f), o.pants, name: "Hips");
            Shapes.Box(body, new Vector3(0, 1.2f, 0), new Vector3(0.5f, 0.42f, 0.28f), o.jacket, name: "Torso");
            Shapes.Box(body, new Vector3(0, 1.43f, 0), new Vector3(0.36f, 0.06f, 0.22f), Palette.Shade(o.jacket, 0.8f), 0.15f, name: "Collar");
            for (int s = -1; s <= 1; s += 2)
            {
                var arm = Shapes.Group(body, "Arm", new Vector3(s * 0.31f, 1.36f, 0));
                arm.localRotation = Quaternion.Euler(0, 0, s * 18f);
                Shapes.Box(arm, new Vector3(0, -0.25f, 0), new Vector3(0.14f, 0.5f, 0.15f), o.jacket, name: "Sleeve");
                Shapes.Part(PrimitiveType.Sphere, arm, new Vector3(0, -0.54f, 0), Vector3.one * 0.12f, skin, 0.2f, name: "Hand");
            }

            // Kopf schaut in Fahrtrichtung (Koerper steht seitlich)
            var head = Shapes.Group(body, "Head", new Vector3(0, 1.62f, 0));
            head.localRotation = Quaternion.Euler(0, -70f, 0);
            Shapes.Box(head, Vector3.zero, new Vector3(0.27f, 0.3f, 0.27f), skin, name: "Face");
            for (int s = -1; s <= 1; s += 2)
                Shapes.Box(head, new Vector3(s * 0.065f, 0.03f, 0.137f), new Vector3(0.04f, 0.07f, 0.01f), Palette.Ink, 0f, name: "Eye");

            switch (o.headVariant)
            {
                case 1: // Cap
                    Shapes.Box(head, new Vector3(0, 0.17f, -0.01f), new Vector3(0.3f, 0.09f, 0.3f), o.head, name: "Cap");
                    Shapes.Box(head, new Vector3(0, 0.135f, 0.2f), new Vector3(0.26f, 0.025f, 0.16f), o.head, name: "Visor");
                    break;
                case 2: // Beanie
                    Shapes.Box(head, new Vector3(0, 0.16f, 0), new Vector3(0.3f, 0.16f, 0.3f), o.head, name: "Beanie");
                    Shapes.Part(PrimitiveType.Sphere, head, new Vector3(0, 0.27f, 0), Vector3.one * 0.08f, Palette.White, 0.15f, name: "Bobble");
                    break;
                case 3: // Kopfhoerer
                    Shapes.Box(head, new Vector3(0, 0.15f, -0.02f), new Vector3(0.29f, 0.07f, 0.29f), Palette.Hex("2A1E1A"), name: "Hair");
                    Shapes.Box(head, new Vector3(0, 0.2f, 0), new Vector3(0.33f, 0.035f, 0.06f), o.head, 0.15f, name: "Band");
                    for (int s = -1; s <= 1; s += 2)
                        Shapes.Box(head, new Vector3(s * 0.155f, 0.02f, 0), new Vector3(0.05f, 0.13f, 0.13f), o.head, 0.2f, name: "Cup");
                    break;
                default: // Struwwelhaare
                    Shapes.Box(head, new Vector3(0, 0.17f, -0.02f), new Vector3(0.3f, 0.1f, 0.3f), o.head, name: "Hair");
                    for (int i = 0; i < 4; i++)
                        Shapes.Box(head, new Vector3(-0.1f + i * 0.07f, 0.24f, -0.03f + (i % 2) * 0.06f), new Vector3(0.06f, 0.1f, 0.06f), o.head, 0.2f,
                            new Vector3(0, 0, -20f + i * 13f), name: "Spike");
                    break;
            }
        }

        public static void SetVisible(Transform root, bool visible)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
        }
    }
}
