using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DriftSkate
{
    /// <summary>
    /// Hauptmenue in der Garage: Fahren (Solo/Online), Autos kaufen, Tuning, Lack, Boards, Outfit,
    /// Garagen, Graffiti-Editor und Optionen. Alles im Code gebaut, damit es leicht anzupassen ist.
    /// Look: Spruehdosen-Graffiti (Farbstriche mit Nasen, Sticker-Icons, Spray-Sound beim Reiterwechsel).
    /// </summary>
    public class GarageMenu : MonoBehaviour
    {
        static readonly string[] Tabs = { "FAHREN", "AUTOS", "TUNING", "TEILE", "FOLIE", "BOARDS", "OUTFIT", "MODS", "GARAGEN", "GRAFFITI", "OPTIONEN" };
        static readonly Color[] TabColors =
        {
            Palette.Lime, Palette.Pink, Palette.Cyan, Palette.Orange, Palette.Pink, Palette.Yellow, Palette.Cyan, Palette.Orange, Palette.Lime, Palette.Pink, Palette.Cyan
        };
        static readonly SprayArt.Icon?[] TabIcons =
        {
            SprayArt.Icon.Steering, SprayArt.Icon.Wheel, SprayArt.Icon.Wrench, SprayArt.Icon.Gear, SprayArt.Icon.Paint, SprayArt.Icon.Board,
            SprayArt.Icon.Shirt, SprayArt.Icon.Gear, SprayArt.Icon.Garage, SprayArt.Icon.Can, SprayArt.Icon.Gear
        };

        // Schrift auf dem dunklen Inhaltsfeld
        static readonly Color TextColor = Palette.Hex("ECE7F7");
        static readonly Color HintColor = Palette.Hex("A39DBB");
        static readonly Color PanelColor = Palette.Hex("23252F");

        GarageEnvironment _env;
        Canvas _canvas;
        Text _money, _title, _toast, _footer, _tag;
        Image _titlePaint;
        bool _shownOnce;
        RectTransform _content;
        readonly Button[] _tabButtons = new Button[Tabs.Length];
        string _tab = "FAHREN";
        string _previewCar;
        string _previewBoard;
        float _toastTime;
        GraffitiEditor _graffiti;
        int _layerSel = -1;          // ausgewaehlte Folien-Ebene
        bool _pickSticker;           // Sticker-Auswahl offen
        bool _designDirty;
        float _saveTimer;

        PlayerProfile P => SaveSystem.Profile;

        void Start()
        {
            GameInput.Blocked = false;
            Time.timeScale = 1f;
            Shader.SetGlobalFloat("_DS_Darken", 0f); // Nacht-Himmel aus der Stadt gilt nicht in der Garage
            Time.fixedDeltaTime = 0.01f;
            ModLibrary.EnsureScanned();
            ModLibrary.Changed += OnModsChanged;
            _env = FindAnyObjectByType<GarageEnvironment>();
            if (_env == null) _env = new GameObject("GarageEnvironment").AddComponent<GarageEnvironment>();
            _previewCar = P.selectedCar;
            _previewBoard = P.selectedBoard;
            RebuildEnvironment();
            BuildUI();
            ShowTab("FAHREN");
            if (!string.IsNullOrEmpty(GameSession.LastError))
            {
                Toast(GameSession.LastError, 5f);
                GameSession.LastError = null;
            }
        }

        void OnDestroy()
        {
            ModLibrary.Changed -= OnModsChanged;
            GarageEnvironment.HoldYaw = null;
            if (_designDirty) SaveSystem.Save();
        }

        void OnModsChanged()
        {
            if (this == null) return;
            RebuildPreview();
            if (_tab == "MODS") ShowTab("MODS");
        }

        /// <summary>Beschriftung fuer Kaufknoepfe (im Admin-Modus "gratis").</summary>
        static string BuyLabel(long price) => Admin.Free ? "GRATIS HOLEN" : "KAUFEN " + UIFactory.Money(price);

        // ------------------------------------------------------------------ 3D-Vorschau

        void RebuildEnvironment()
        {
            _env.Build(Catalog.Garage(P.selectedGarage), P);
            RebuildPreview();
        }

        void RebuildPreview()
        {
            var def = Catalog.Car(_previewCar);
            bool owned = P.ownedCars.Contains(def.id);
            Color paint = owned ? P.GetCarSave(def.id).paint : def.defaultColor;
            var design = owned ? P.GetCarSave(def.id).design : CarDesign.Default(def.id, P.crewColor);
            CarBuilder.Build(_env.carRoot, def, paint, P.crewColor, SaveSystem.Graffiti, null, design);
            float drop = VehicleSetup.From(def, P.ownedCars.Contains(def.id) ? P.GetCarSave(def.id).tuning : null, 0, true).restLength;
            _env.carRoot.localPosition = new Vector3(0, 0.1f - (0.34f - drop) * 0.4f, 0);
            SkaterBuilder.Build(_env.skaterRoot, SkaterBuilder.Outfit.FromProfile(P), Catalog.Board(_previewBoard), SaveSystem.Graffiti, null, P.selectedCharacter);
        }

        // ------------------------------------------------------------------ Grundgeruest

        void BuildUI()
        {
            _canvas = UIFactory.Canvas("Garage Canvas", 5);
            var root = _canvas.transform;

            BuildLogo(root);

            var moneyPanel = UIFactory.Panel(root, "Money", Palette.Yellow, Vector2.one, Vector2.one, Vector2.one, new Vector2(-36, -30), new Vector2(420, 80), 2f);
            _money = UIFactory.Label(moneyPanel.transform, "", 50, Palette.Ink, TextAnchor.MiddleCenter, false, false);
            _money.font = UIFactory.GraffitiFont;

            // Reiter: Farbstriche mit Nasen, rechts daneben ein Sticker-Icon
            var tabs = UIFactory.Rect(root, "Tabs", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -178), new Vector2(330, 860));
            var v = tabs.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 14;
            v.childControlHeight = false;
            v.childControlWidth = false;
            var rng = new System.Random(4);
            for (int i = 0; i < Tabs.Length; i++)
            {
                string name = Tabs[i];
                var b = UIFactory.SprayButton(tabs, name, TabColors[i], () => ShowTab(name), new Vector2(264, 62), 34, Palette.Ink, i);
                b.transform.localRotation = Quaternion.Euler(0, 0, (float)rng.NextDouble() * 4f - 2f);
                if (TabIcons[i].HasValue)
                {
                    var icon = UIFactory.Rect(b.transform, "Icon", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(34, 2), new Vector2(76, 76));
                    icon.localRotation = Quaternion.Euler(0, 0, (float)rng.NextDouble() * 20f - 10f);
                    var img = icon.gameObject.AddComponent<Image>();
                    img.sprite = SprayArt.IconSprite(TabIcons[i].Value);
                    img.raycastTarget = false;
                }
                _tabButtons[i] = b;
            }

            // Inhaltsfeld: dunkle Tafel, Farbkleckse an den Ecken, Spraydose und Tag mit dem Spielernamen
            var frame = UIFactory.Rect(root, "ContentFrame", new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-36, -40), new Vector2(840, 900));
            Splat(frame, 0, Palette.Lime, new Vector2(40, 860), new Vector2(300, 300), -10f);
            Splat(frame, 1, Palette.Pink, new Vector2(800, 880), new Vector2(280, 280), 14f);
            Splat(frame, 2, Palette.Cyan, new Vector2(30, 40), new Vector2(240, 240), 6f);
            Splat(frame, 3, Palette.Purple, new Vector2(820, 60), new Vector2(220, 220), -20f);
            var panel = UIFactory.Panel(frame, "Content", PanelColor, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 0f);
            panel.color = new Color(PanelColor.r, PanelColor.g, PanelColor.b, 0.95f);

            _tag = UIFactory.LabelAt(frame, "", 54, Palette.Lime, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-34, -4), new Vector2(360, 76), TextAnchor.MiddleRight, false, -7f);
            _tag.font = UIFactory.MarkerFont;
            _tag.fontStyle = FontStyle.Bold;
            _tag.horizontalOverflow = HorizontalWrapMode.Overflow;

            var titleRt = UIFactory.Rect(frame, "Title", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(-22, 40), new Vector2(430, 80));
            titleRt.localRotation = Quaternion.Euler(0, 0, -3f);
            _titlePaint = UIFactory.SprayPaint(titleRt, Palette.Lime, 2, 80);
            _title = UIFactory.Label(titleRt, "", 58, Palette.Ink, TextAnchor.MiddleCenter, false, false);
            _title.font = UIFactory.GraffitiFont;

            _content = UIFactory.Rect(panel.transform, "Body", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _content.offsetMin = new Vector2(28, 28);
            _content.offsetMax = new Vector2(-28, -80);

            var can = UIFactory.Rect(frame, "SprayCan", new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-6, 120), new Vector2(150, 225));
            var canImg = can.gameObject.AddComponent<Image>();
            canImg.sprite = SprayArt.BigCan;
            canImg.raycastTarget = false;

            _footer = UIFactory.LabelAt(root, "Maus ziehen = drehen   Mausrad = Zoom", 24, Palette.White, new Vector2(0.35f, 0), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(900, 40), TextAnchor.MiddleCenter);
            _toast = UIFactory.LabelAt(root, "", 44, Palette.Yellow, new Vector2(0.35f, 0), new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(1100, 60), TextAnchor.MiddleCenter);
            _toast.font = UIFactory.GraffitiFont;
        }

        /// <summary>Farbklecks mit Nasen (Deko), Position in Pixeln von unten links des Elternelements.</summary>
        static Image Splat(RectTransform parent, int variant, Color color, Vector2 pos, Vector2 size, float rotation, float alpha = 0.9f)
        {
            var rt = UIFactory.Rect(parent, "Splat", Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), pos, size);
            rt.localRotation = Quaternion.Euler(0, 0, rotation);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = SprayArt.Splat(variant);
            img.color = new Color(color.r, color.g, color.b, alpha);
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Graffiti-Schriftzug: einzelne, leicht verdrehte Buchstaben mit Verlauf, Kontur und pinker 3D-Kante.</summary>
        void BuildLogo(Transform root)
        {
            var logo = UIFactory.Rect(root, "Logo", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -12), new Vector2(660, 160));
            logo.localRotation = Quaternion.Euler(0, 0, 2.5f);
            Splat(logo, 0, Palette.Pink, new Vector2(170, 70), new Vector2(360, 300), -6f, 0.95f);
            Splat(logo, 1, Palette.Cyan, new Vector2(470, 90), new Vector2(320, 280), 10f, 0.85f);
            Splat(logo, 2, Palette.Purple, new Vector2(330, 40), new Vector2(220, 200), 25f, 0.7f);

            var rng = new System.Random(11);
            float x = 14f;
            foreach (char ch in "DRIFT*SKATE")
            {
                if (ch == '*')
                {
                    var star = UIFactory.Rect(logo, "Star", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(x + 30f, -80f), new Vector2(68, 68));
                    star.localRotation = Quaternion.Euler(0, 0, 12f);
                    var img = star.gameObject.AddComponent<Image>();
                    img.sprite = SprayArt.Star;
                    img.raycastTarget = false;
                    x += 62f;
                    continue;
                }
                int size = 100 + rng.Next(16);
                var holder = UIFactory.Rect(logo, "Letter_" + ch, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120, 150));
                var t = holder.gameObject.AddComponent<Text>();
                t.font = UIFactory.GraffitiFont;
                t.fontSize = size;
                t.text = ch.ToString();
                t.alignment = TextAnchor.MiddleCenter;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.raycastTarget = false;
                var grad = holder.gameObject.AddComponent<UIGradient>();
                grad.top = Palette.Hex("FFF27A");
                grad.bottom = Palette.Hex("FF9A1F");
                var outline = holder.gameObject.AddComponent<Outline>();
                outline.effectColor = Palette.Ink;
                outline.effectDistance = new Vector2(5, -5);
                var shadow = holder.gameObject.AddComponent<Shadow>();
                shadow.effectColor = Palette.Hex("C2185B");
                shadow.effectDistance = new Vector2(7, -8);
                float w = t.preferredWidth;
                holder.anchoredPosition = new Vector2(x + w * 0.5f, -78f + (float)rng.NextDouble() * 14f - 7f);
                holder.localRotation = Quaternion.Euler(0, 0, (float)rng.NextDouble() * 16f - 8f);
                x += w * 0.86f;
            }

            var sub = UIFactory.LabelAt(logo, "Arbeitstitel", 32, Palette.White, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-30, -6), new Vector2(260, 44), TextAnchor.LowerRight, false, -4f);
            sub.font = UIFactory.MarkerFont;
            sub.fontStyle = FontStyle.Bold;
        }

        void Toast(string text, float seconds = 2.5f)
        {
            _toast.text = text;
            _toastTime = seconds;
        }

        void Update()
        {
            _money.text = UIFactory.Money(P.money);
            _toastTime -= Time.deltaTime;
            _toast.color = new Color(_toast.color.r, _toast.color.g, _toast.color.b, Mathf.Clamp01(_toastTime));
            // Folien-Aenderungen gesammelt speichern (Regler feuern sehr oft)
            _saveTimer -= Time.deltaTime;
            if (_designDirty && _saveTimer <= 0f)
            {
                _designDirty = false;
                _saveTimer = 1f;
                SaveSystem.Save();
            }
        }

        /// <summary>Fuer automatische Tests: Folien-Ebene auswaehlen (zeigt den Ebenen-Editor).</summary>
        public void SelectLayerForTest(int index)
        {
            _layerSel = index;
            ShowTab("FOLIE");
        }

        /// <summary>Fuer automatische Tests.</summary>
        public void ShowTabForTest(string tab)
        {
            _previewBoard = P.selectedBoard;
            RebuildPreview();
            ShowTab(tab);
        }

        /// <summary>Fuer Tests: Vorschau-Auto neu aufbauen (z. B. nach einer Design-Aenderung aus dem Test).</summary>
        public void RebuildPreviewForTest() => RebuildPreview();

        void ShowTab(string tab)
        {
            if (_graffiti != null && _graffiti.Dirty) _graffiti.Save();
            bool changed = tab != _tab || !_shownOnce;
            if (changed && _shownOnce) SpraySound.Play();
            _shownOnce = true;
            _tab = tab;
            _title.text = tab;
            _tag.text = P.playerName;
            int index = Array.IndexOf(Tabs, tab);
            _titlePaint.color = TabColors[index];
            for (int i = 0; i < Tabs.Length; i++)
            {
                bool on = i == index;
                UIFactory.SetButtonColor(_tabButtons[i], on ? TabColors[i] : Color.Lerp(TabColors[i], Palette.Hex("3B3D4E"), 0.55f));
                _tabButtons[i].transform.localScale = Vector3.one * (on ? 1.08f : 1f);
            }
            if (changed)
            {
                // Neuer Reiter wird "aufgesprueht"
                SprayReveal.Play(_titlePaint, 0f, 0.22f);
                SprayReveal.Play(UIFactory.PaintOf(_tabButtons[index]), 0f, 0.18f);
            }

            // Gleicher Reiter neu aufgebaut (z. B. Ebene gewaehlt): Scrollposition behalten
            var oldScroll = _content.GetComponentInChildren<ScrollRect>();
            float keepScroll = oldScroll != null && !changed ? oldScroll.verticalNormalizedPosition : 1f;
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
            _graffiti = null;
            if (tab != "FOLIE") { GarageEnvironment.HoldYaw = null; SetCameraPitch(14f); }
            if (_designDirty && changed) { _designDirty = false; SaveSystem.Save(); }

            switch (tab)
            {
                case "FAHREN": BuildDrive(); break;
                case "AUTOS": BuildCars(); break;
                case "TUNING": BuildTuning(); break;
                case "TEILE": BuildParts(); break;
                case "FOLIE": BuildWrap(); break;
                case "BOARDS": BuildBoards(); break;
                case "OUTFIT": BuildOutfits(); break;
                case "MODS": BuildMods(); break;
                case "GARAGEN": BuildGarages(); break;
                case "GRAFFITI": BuildGraffiti(); break;
                case "OPTIONEN": BuildOptions(); break;
            }

            if (!changed)
            {
                var newScroll = _content.GetComponentInChildren<ScrollRect>();
                if (newScroll != null)
                {
                    Canvas.ForceUpdateCanvases();
                    LayoutRebuilder.ForceRebuildLayoutImmediate(newScroll.content);
                    newScroll.verticalNormalizedPosition = keepScroll;
                }
            }

            if (changed)
            {
                int k = 0;
                foreach (var img in _content.GetComponentsInChildren<Image>())
                    if (img.name == "Paint") SprayReveal.Play(img, Mathf.Min(0.08f + k++ * 0.035f, 0.45f), 0.16f);
            }
        }

        RectTransform List() => UIFactory.ScrollList(_content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        static void Header(Transform list, string text, Color? color = null)
        {
            var row = UIFactory.Row(list, 52);
            UIFactory.FlowLabel(row, text, 36, color ?? Palette.Pink, 0).font = UIFactory.GraffitiFont;
        }

        static Text Info(Transform list, string text, int size = 24, float height = 0f)
        {
            var row = UIFactory.Row(list, height > 0 ? height : size * 1.5f);
            var t = UIFactory.FlowLabel(row, text, size, TextColor, 0, TextAnchor.UpperLeft, false);
            t.GetComponent<Outline>().enabled = false;
            return t;
        }

        // ------------------------------------------------------------------ FAHREN

        void BuildDrive()
        {
            var list = List();
            Header(list, "SPIELERNAME");
            var nameRow = UIFactory.Row(list, 70);
            UIFactory.InputField(nameRow, P.playerName, v =>
            {
                P.playerName = v.Length > 16 ? v.Substring(0, 16) : v;
                SaveSystem.Save();
            }, new Vector2(500, 64), 32, true);

            Header(list, "LOSFAHREN");
            var soloRow = UIFactory.Row(list, 90);
            UIFactory.SprayButton(soloRow, "SOLO FAHREN", Palette.Lime, () => StartCity(SessionMode.Solo), new Vector2(560, 84), 42);

            Header(list, "ONLINE");
            var hostRow = UIFactory.Row(list, 80);
            UIFactory.SprayButton(hostRow, "ONLINE HOSTEN", Palette.Cyan, () => StartCity(SessionMode.Host), new Vector2(420, 74), 34);
            Info(list, "Freunde im selben Netzwerk verbinden sich mit deiner IP: " + NetUtil.LocalIPv4() +
                       "\nUebers Internet: Port 7777 (UDP) im Router freigeben und deine oeffentliche IP weitergeben.", 22, 70);

            var joinRow = UIFactory.Row(list, 80);
            var ip = UIFactory.InputField(joinRow, P.lastHostAddress, null, new Vector2(360, 70), 34, true);
            UIFactory.SprayButton(joinRow, "BEITRETEN", Palette.Yellow, () =>
            {
                P.lastHostAddress = ip.text.Trim();
                SaveSystem.Save();
                GameSession.Address = P.lastHostAddress;
                StartCity(SessionMode.Client);
            }, new Vector2(300, 70), 34);

            Header(list, "STATISTIK");
            Info(list, "Beste Combo: " + UIFactory.Money(P.bestCombo) + "\nAuto: " + Catalog.Car(P.selectedCar).name +
                       "   Board: " + Catalog.Board(P.selectedBoard).name + "   Garage: " + Catalog.Garage(P.selectedGarage).name, 24, 70);
        }

        void StartCity(SessionMode mode)
        {
            GameSession.Mode = mode;
            GameSession.Port = 7777;
            SaveSystem.Save();
            SceneManager.LoadScene("City");
        }

        // ------------------------------------------------------------------ AUTOS

        void BuildCars()
        {
            var list = List();
            Info(list, $"Stellplaetze: {P.ownedCars.Count} / {P.GarageSlots}  (mehr Platz: groessere Garage kaufen)", 24);
            foreach (var car in Catalog.Cars)
            {
                var c = car;
                var row = UIFactory.Row(list, 78);
                var nameBtn = UIFactory.SprayButton(row, c.name, c.id == _previewCar ? Palette.Yellow : Palette.White, () => { _previewCar = c.id; RebuildPreview(); ShowTab("AUTOS"); }, new Vector2(270, 72), 30);
                UIFactory.FlowLabel(row, $"{c.horsepower} PS\n{c.mass:0} kg", 22, TextColor, 120, TextAnchor.MiddleLeft, false).GetComponent<Outline>().enabled = false;
                bool owned = P.ownedCars.Contains(c.id);
                string label = owned ? (P.selectedCar == c.id ? "AKTIV" : "WAEHLEN") : BuyLabel(c.price);
                Color col = owned ? (P.selectedCar == c.id ? Palette.Lime : Palette.Cyan) : Palette.Pink;
                UIFactory.SprayButton(row, label, col, () => BuyOrSelectCar(c), new Vector2(330, 72), 26);
            }
            var preview = Catalog.Car(_previewCar);
            Header(list, preview.name);
            Info(list, preview.blurb + $"\n{preview.horsepower} PS, {preview.peakTorque:0} Nm, {preview.mass:0} kg, {preview.gears.Length} Gaenge, Drehzahl bis {preview.maxRpm:0}", 24, 80);
        }

        void BuyOrSelectCar(CarDef c)
        {
            _previewCar = c.id;
            if (P.ownedCars.Contains(c.id))
            {
                P.selectedCar = c.id;
                SaveSystem.Save();
            }
            else if (P.ownedCars.Count >= P.GarageSlots && !Admin.On) Toast("Kein Stellplatz frei. Kauf eine groessere Garage!");
            else if (!SaveSystem.TrySpend(c.price)) Toast("Zu wenig Geld. Combos fahren!");
            else
            {
                P.ownedCars.Add(c.id);
                P.selectedCar = c.id;
                P.GetCarSave(c.id);
                SaveSystem.Save();
                Toast(c.name + " gekauft!");
            }
            RebuildEnvironment();
            ShowTab("AUTOS");
        }

        // ------------------------------------------------------------------ TUNING

        void BuildTuning()
        {
            var def = Catalog.Car(P.selectedCar);
            var save = P.GetCarSave(def.id);
            var list = List();
            Header(list, def.name.ToUpperInvariant());
            for (int i = 0; i < Tuning.Count; i++)
            {
                int idx = i;
                var row = UIFactory.Row(list, 52);
                UIFactory.FlowLabel(row, Tuning.Labels[i], 26, TextColor, 270, TextAnchor.MiddleLeft, true).GetComponent<Outline>().enabled = false;
                var value = UIFactory.FlowLabel(row, "", 24, Palette.Purple, 80, TextAnchor.MiddleRight, true);
                value.GetComponent<Outline>().enabled = false;
                value.text = Mathf.RoundToInt(save.tuning[i] * 100) + "%";
                UIFactory.Slider(row, save.tuning[i], v =>
                {
                    save.tuning[idx] = v;
                    value.text = Mathf.RoundToInt(v * 100) + "%";
                    SaveSystem.Save();
                    if (idx == Tuning.Camber) save.design.camber = Tuning.VisualCamber(save.tuning);
                    if (idx == Tuning.RideHeight || idx == Tuning.Camber) RebuildPreview();
                }, new Vector2(330, 40), Palette.Pink);
                var hint = Info(list, Tuning.Hints[i], 21, 28);
                hint.color = HintColor;
            }
            var resetRow = UIFactory.Row(list, 64);
            UIFactory.SprayButton(resetRow, "SERIE", Palette.White, () =>
            {
                save.tuning = Tuning.Defaults();
                SaveSystem.Save();
                RebuildPreview();
                ShowTab("TUNING");
            }, new Vector2(220, 58), 28);

            Header(list, "FAHRHILFEN");
            var assistRow = UIFactory.Row(list, 64);
            string[] levels = { "AUS", "SCHWACH", "MITTEL", "STARK" };
            UIFactory.FlowLabel(assistRow, "Gegenlenken", 24, TextColor, 170, TextAnchor.MiddleLeft, true).GetComponent<Outline>().enabled = false;
            for (int i = 0; i < levels.Length; i++)
            {
                int lvl = i;
                UIFactory.SprayButton(assistRow, levels[i], P.assistLevel == i ? Palette.Yellow : Palette.White, () =>
                {
                    P.assistLevel = lvl;
                    SaveSystem.Save();
                    ShowTab("TUNING");
                }, new Vector2(140, 56), 22);
            }
            var gearRow = UIFactory.Row(list, 64);
            UIFactory.FlowLabel(gearRow, "Getriebe", 24, TextColor, 170, TextAnchor.MiddleLeft, true).GetComponent<Outline>().enabled = false;
            UIFactory.SprayButton(gearRow, "AUTOMATIK", P.autoGearbox ? Palette.Yellow : Palette.White, () => { P.autoGearbox = true; SaveSystem.Save(); ShowTab("TUNING"); }, new Vector2(220, 56), 24);
            UIFactory.SprayButton(gearRow, "MANUELL", !P.autoGearbox ? Palette.Yellow : Palette.White, () => { P.autoGearbox = false; SaveSystem.Save(); ShowTab("TUNING"); }, new Vector2(220, 56), 24);
        }

        // ------------------------------------------------------------------ TEILE

        void BuildParts()
        {
            var def = Catalog.Car(P.selectedCar);
            var save = P.GetCarSave(def.id);
            var d = save.design;
            if (_previewCar != def.id) { _previewCar = def.id; RebuildPreview(); }
            var list = List();
            Header(list, "TEILE  " + def.name.ToUpperInvariant());
            if (def.IsModel)
            {
                var fixedHint = Info(list, "Dieses Auto hat ein festes Design: Anbauteile und Felgen gehoeren zum Modell. Unterboden-Neon geht trotzdem.", 22, 60);
                fixedHint.color = HintColor;
            }
            bool popUps = !def.IsModel && CarShape.For(def.id).head == HeadStyle.PopUp;
            for (int p = 0; p < CarDesign.PartCount && !def.IsModel; p++)
            {
                if (p == CarDesign.PopUps && !popUps) continue;
                int part = p;
                var options = CarDesign.PartOptions[p];
                var row = UIFactory.Row(list, 62);
                UIFactory.FlowLabel(row, CarDesign.PartNames[p], 26, TextColor, 230, TextAnchor.MiddleLeft, true).GetComponent<Outline>().enabled = false;
                UIFactory.SprayButton(row, "<", Palette.Cyan, () => CyclePart(d, part, -1), new Vector2(70, 56), 34, Palette.Ink);
                var val = UIFactory.FlowLabel(row, options[d.parts[p]], 24, Palette.Yellow, 250, TextAnchor.MiddleCenter, false);
                val.GetComponent<Outline>().enabled = false;
                UIFactory.SprayButton(row, ">", Palette.Cyan, () => CyclePart(d, part, 1), new Vector2(70, 56), 34, Palette.Ink);
            }
            if (!def.IsModel)
            {
                Header(list, "FELGENFARBE");
                SwatchGrid(list, c =>
                {
                    d.rimColor = c;
                    MarkDesign();
                    RebuildPreview();
                });
            }
            Header(list, "UNTERBODEN-NEON");
            var neonRow = UIFactory.Row(list, 62);
            UIFactory.FlowLabel(neonRow, "LICHT", 26, TextColor, 230, TextAnchor.MiddleLeft, true).GetComponent<Outline>().enabled = false;
            UIFactory.SprayButton(neonRow, "<", Palette.Cyan, () => CycleNeon(d, -1), new Vector2(70, 56), 34, Palette.Ink);
            var neonVal = UIFactory.FlowLabel(neonRow, Underglow.Modes[d.neon], 24, d.neon > 0 ? d.neonColor : Palette.Yellow, 250, TextAnchor.MiddleCenter, false);
            neonVal.GetComponent<Outline>().enabled = false;
            UIFactory.SprayButton(neonRow, ">", Palette.Cyan, () => CycleNeon(d, 1), new Vector2(70, 56), 34, Palette.Ink);
            if (d.neon > 0 && d.neon != 3)
            {
                SwatchGrid(list, c =>
                {
                    d.neonColor = c;
                    MarkDesign();
                    RebuildPreview();
                    ShowTab("TEILE");
                });
            }
            var hint = Info(list, $"Sturz-Optik der Raeder kommt vom Regler \"Sturz vorne\" im TUNING (gerade {d.camber:0.0} Grad).", 22, 60);
            hint.color = HintColor;
            var resetRow = UIFactory.Row(list, 64);
            UIFactory.SprayButton(resetRow, "WERKSZUSTAND", Palette.White, () =>
            {
                var factory = CarDesign.Default(def.id, P.crewColor);
                d.parts = factory.parts;
                d.rimColor = factory.rimColor;
                d.neon = 0;
                MarkDesign();
                RebuildPreview();
                ShowTab("TEILE");
            }, new Vector2(320, 58), 26);
        }

        void CycleNeon(CarDesign d, int dir)
        {
            int n = Underglow.Modes.Length;
            d.neon = (d.neon + dir + n) % n;
            MarkDesign();
            RebuildPreview();
            ShowTab("TEILE");
        }

        void CyclePart(CarDesign d, int part, int dir)
        {
            int n = CarDesign.PartOptions[part].Length;
            d.parts[part] = (d.parts[part] + dir + n) % n;
            MarkDesign();
            RebuildPreview();
            ShowTab("TEILE");
        }

        void MarkDesign()
        {
            _designDirty = true;
            _saveTimer = Mathf.Max(_saveTimer, 0.6f);
        }

        // ------------------------------------------------------------------ FOLIE (Lack + Folierung)

        void BuildWrap()
        {
            var def = Catalog.Car(P.selectedCar);
            var save = P.GetCarSave(def.id);
            var d = save.design;
            if (_previewCar != def.id) { _previewCar = def.id; RebuildPreview(); }
            if (_layerSel >= d.wrap.Count) _layerSel = d.wrap.Count - 1;
            FocusLayer(_layerSel >= 0 ? d.wrap[_layerSel] : null);
            var list = List();

            Header(list, "LACK  " + def.name.ToUpperInvariant());
            SwatchGrid(list, c =>
            {
                save.paint = c;
                MarkDesign();
                RebuildPreview();
            });
            if (def.IsModel)
            {
                var modelHint = Info(list, "Festes Design: die Aufkleber gehoeren zum Modell, eine eigene Folie gibt es hier nicht.", 22, 60);
                modelHint.color = HintColor;
                var factoryRow = UIFactory.Row(list, 64);
                UIFactory.SprayButton(factoryRow, "ORIGINAL-LACK", Palette.White, () => { save.paint = def.defaultColor; MarkDesign(); RebuildPreview(); }, new Vector2(320, 58), 26);
                return;
            }

            Header(list, "VORLAGEN");
            var presetRow = UIFactory.Row(list, 60);
            int perRow = 0;
            foreach (var name in LiveryPresets.Names)
            {
                if (perRow == 4) { presetRow = UIFactory.Row(list, 60); perRow = 0; }
                string preset = name;
                UIFactory.SprayButton(presetRow, preset, Palette.Cyan, () =>
                {
                    d.wrap = LiveryPresets.Make(preset, def, P.crewColor, save.paint);
                    _layerSel = d.wrap.Count > 0 ? 0 : -1;
                    _pickSticker = false;
                    MarkDesign();
                    RefreshWrap();
                    ShowTab("FOLIE");
                }, new Vector2(176, 54), 20);
                perRow++;
            }

            Header(list, $"EBENEN  {d.wrap.Count} / {CarDesign.MaxLayers}");
            var addRow = UIFactory.Row(list, 62);
            UIFactory.SprayButton(addRow, _pickSticker ? "ABBRECHEN" : "+ STICKER", _pickSticker ? Palette.White : Palette.Lime, () =>
            {
                _pickSticker = !_pickSticker;
                ShowTab("FOLIE");
            }, new Vector2(260, 56), 26);
            if (_pickSticker) StickerPicker(list, idx =>
            {
                if (d.wrap.Count >= CarDesign.MaxLayers) { Toast("Maximal " + CarDesign.MaxLayers + " Ebenen"); return; }
                var layer = new WrapLayer { sticker = idx, color = StickerLibrary.Get(idx).graffiti ? Color.white : P.crewColor, x = 0.5f, y = 0.5f, size = 0.4f };
                d.wrap.Add(layer);
                _layerSel = d.wrap.Count - 1;
                _pickSticker = false;
                MarkDesign();
                RefreshWrap();
                ShowTab("FOLIE");
            });

            // Oberste Ebene zuerst (so wie sie auf dem Auto liegt)
            for (int i = d.wrap.Count - 1; i >= 0; i--)
            {
                int idx = i;
                var l = d.wrap[i];
                var row = UIFactory.Row(list, 56, 8);
                string label = $"{i + 1}. {StickerLibrary.Get(l.sticker).name}";
                UIFactory.SprayButton(row, label, idx == _layerSel ? Palette.Yellow : Palette.White, () =>
                {
                    _layerSel = idx;
                    _pickSticker = false;
                    ShowTab("FOLIE");
                }, new Vector2(300, 52), 20);
                UIFactory.Swatch(row, l.color, () => { _layerSel = idx; ShowTab("FOLIE"); }, 40f);
                UIFactory.SprayButton(row, "AUF", Palette.Cyan, () => MoveLayer(d, idx, 1), new Vector2(76, 52), 18);
                UIFactory.SprayButton(row, "AB", Palette.Cyan, () => MoveLayer(d, idx, -1), new Vector2(66, 52), 18);
                UIFactory.SprayButton(row, "KOPIE", Palette.Purple, () =>
                {
                    if (d.wrap.Count >= CarDesign.MaxLayers) { Toast("Maximal " + CarDesign.MaxLayers + " Ebenen"); return; }
                    var copy = d.wrap[idx].Clone();
                    copy.x = Mathf.Clamp01(copy.x - 0.06f);
                    d.wrap.Insert(idx + 1, copy);
                    _layerSel = idx + 1;
                    MarkDesign();
                    RefreshWrap();
                    ShowTab("FOLIE");
                }, new Vector2(96, 52), 18);
                UIFactory.SprayButton(row, "X", Palette.Pink, () =>
                {
                    d.wrap.RemoveAt(idx);
                    _layerSel = Mathf.Min(_layerSel, d.wrap.Count - 1);
                    MarkDesign();
                    RefreshWrap();
                    ShowTab("FOLIE");
                }, new Vector2(52, 52), 22);
            }

            if (_layerSel >= 0 && _layerSel < d.wrap.Count) LayerEditor(list, d, d.wrap[_layerSel]);

            Header(list, "CREW-FARBE");
            Info(list, "Fuer Reifenspuren, Rauch und Sticker mit Crew-Farbe.", 22);
            SwatchGrid(list, c =>
            {
                P.crewColor = c;
                SaveSystem.Save();
                RebuildPreview();
            });
        }

        void LayerEditor(Transform list, CarDesign d, WrapLayer l)
        {
            var sticker = StickerLibrary.Get(l.sticker);
            Header(list, "EBENE " + (_layerSel + 1) + ": " + sticker.name, Palette.Yellow);

            var sRow = UIFactory.Row(list, 60);
            UIFactory.FlowLabel(sRow, "Sticker", 24, TextColor, 150, TextAnchor.MiddleLeft, true).GetComponent<Outline>().enabled = false;
            UIFactory.SprayButton(sRow, "<", Palette.Cyan, () => { l.sticker = (l.sticker - 1 + StickerLibrary.Count) % StickerLibrary.Count; MarkDesign(); RefreshWrap(); ShowTab("FOLIE"); }, new Vector2(70, 54), 34, Palette.Ink);
            var sName = UIFactory.FlowLabel(sRow, sticker.name, 24, Palette.Yellow, 260, TextAnchor.MiddleCenter, false);
            sName.GetComponent<Outline>().enabled = false;
            UIFactory.SprayButton(sRow, ">", Palette.Cyan, () => { l.sticker = (l.sticker + 1) % StickerLibrary.Count; MarkDesign(); RefreshWrap(); ShowTab("FOLIE"); }, new Vector2(70, 54), 34, Palette.Ink);

            var aRow = UIFactory.Row(list, 56, 8);
            for (int a = 0; a < WrapLayer.AreaNames.Length; a++)
            {
                int area = a;
                UIFactory.SprayButton(aRow, WrapLayer.AreaNames[a], l.area == a ? Palette.Yellow : Palette.White, () =>
                {
                    l.area = area;
                    MarkDesign();
                    RefreshWrap();
                    ShowTab("FOLIE");
                }, new Vector2(a == 0 ? 220 : 150, 52), 20);
            }

            bool top = l.area == WrapLayer.Top;
            SliderRow(list, "Position", l.x, v => l.x = v, () => top ? (l.x > 0.5f ? "vorn" : "hinten") : (l.x > 0.5f ? "vorn" : "hinten"));
            SliderRow(list, top ? "Quer" : "Hoehe", l.y, v => l.y = v, () => Mathf.RoundToInt(l.y * 100) + "%");
            SliderRow(list, "Groesse", Mathf.InverseLerp(Mathf.Log(0.05f), Mathf.Log(3f), Mathf.Log(l.size)),
                      v => l.size = Mathf.Exp(Mathf.Lerp(Mathf.Log(0.05f), Mathf.Log(3f), v)), () => l.size.ToString("0.00") + " m");
            SliderRow(list, "Breite", Mathf.InverseLerp(Mathf.Log(0.15f), Mathf.Log(10f), Mathf.Log(l.stretch)),
                      v => l.stretch = Mathf.Exp(Mathf.Lerp(Mathf.Log(0.15f), Mathf.Log(10f), v)), () => "x" + l.stretch.ToString("0.0"));
            SliderRow(list, "Drehung", Mathf.InverseLerp(-180f, 180f, l.rot), v => l.rot = Mathf.Round(Mathf.Lerp(-180f, 180f, v) / 5f) * 5f, () => l.rot.ToString("0") + " Grad");

            var mRow = UIFactory.Row(list, 60, 10);
            UIFactory.SprayButton(mRow, l.mirror ? "GESPIEGELT" : "SPIEGELN", l.mirror ? Palette.Yellow : Palette.White, () => { l.mirror = !l.mirror; MarkDesign(); RefreshWrap(); ShowTab("FOLIE"); }, new Vector2(230, 54), 22);
            UIFactory.SprayButton(mRow, "MITTIG", Palette.White, () => { if (top) l.y = 0.5f; else l.x = 0.5f; MarkDesign(); RefreshWrap(); ShowTab("FOLIE"); }, new Vector2(170, 54), 22);
            UIFactory.SprayButton(mRow, "GERADE", Palette.White, () => { l.rot = 0f; MarkDesign(); RefreshWrap(); ShowTab("FOLIE"); }, new Vector2(170, 54), 22);

            if (!sticker.graffiti)
            {
                var cRow = UIFactory.Row(list, 40);
                UIFactory.FlowLabel(cRow, "Farbe", 24, TextColor, 150, TextAnchor.MiddleLeft, true).GetComponent<Outline>().enabled = false;
                SwatchGrid(list, c => { l.color = c; MarkDesign(); RefreshWrap(); ShowTab("FOLIE"); });
                var crewRow = UIFactory.Row(list, 58, 10);
                UIFactory.SprayButton(crewRow, "CREW-FARBE", P.crewColor, () => { l.color = P.crewColor; MarkDesign(); RefreshWrap(); ShowTab("FOLIE"); }, new Vector2(240, 54), 22, Palette.Ink);
                UIFactory.SprayButton(crewRow, "LACKFARBE", P.GetCarSave(P.selectedCar).paint, () => { l.color = P.GetCarSave(P.selectedCar).paint; MarkDesign(); RefreshWrap(); ShowTab("FOLIE"); }, new Vector2(240, 54), 22, Palette.Ink);
            }
        }

        /// <summary>Regler mit Beschriftung und Wert; aendert die Folie live (ohne das Menue neu zu bauen).</summary>
        void SliderRow(Transform list, string label, float value01, Action<float> set, Func<string> text)
        {
            var row = UIFactory.Row(list, 50);
            UIFactory.FlowLabel(row, label, 24, TextColor, 150, TextAnchor.MiddleLeft, true).GetComponent<Outline>().enabled = false;
            var val = UIFactory.FlowLabel(row, text(), 22, Palette.Purple, 120, TextAnchor.MiddleRight, true);
            val.GetComponent<Outline>().enabled = false;
            UIFactory.Slider(row, Mathf.Clamp01(value01), v =>
            {
                set(v);
                val.text = text();
                MarkDesign();
                RefreshWrap();
            }, new Vector2(380, 40), Palette.Pink);
        }

        void MoveLayer(CarDesign d, int idx, int dir)
        {
            int to = idx + dir;
            if (to < 0 || to >= d.wrap.Count) return;
            (d.wrap[idx], d.wrap[to]) = (d.wrap[to], d.wrap[idx]);
            _layerSel = to;
            MarkDesign();
            RefreshWrap();
            ShowTab("FOLIE");
        }

        void RefreshWrap()
        {
            var save = P.GetCarSave(P.selectedCar);
            CarBuilder.RefreshLivery(_env.carRoot, save.paint, save.design);
            if (_layerSel >= 0 && _layerSel < save.design.wrap.Count) FocusLayer(save.design.wrap[_layerSel]);
        }

        /// <summary>Auto so hinstellen, dass die bearbeitete Seite zur Kamera zeigt.</summary>
        void FocusLayer(WrapLayer l)
        {
            if (l == null) { GarageEnvironment.HoldYaw = null; SetCameraPitch(14f); return; }
            switch (l.area)
            {
                case WrapLayer.Right: GarageEnvironment.HoldYaw = 55f; SetCameraPitch(10f); break;
                case WrapLayer.Top: GarageEnvironment.HoldYaw = l.x >= 0.5f ? 145f : -35f; SetCameraPitch(42f); break; // Haube vorn bzw. Heck zur Kamera
                default: GarageEnvironment.HoldYaw = -125f; SetCameraPitch(10f); break;
            }
        }

        static void SetCameraPitch(float pitch)
        {
            var orbit = FindAnyObjectByType<OrbitCamera>();
            if (orbit != null) orbit.pitch = pitch;
        }

        /// <summary>Raster mit allen Stickern (Vorschau-Bild + Name).</summary>
        void StickerPicker(Transform list, Action<int> pick)
        {
            int rows = Mathf.CeilToInt(StickerLibrary.Count / 5f);
            var holder = UIFactory.Rect(list, "Stickers", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, rows * 128));
            var le = holder.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = rows * 128;
            var grid = holder.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(132, 116);
            grid.spacing = new Vector2(10, 12);
            for (int i = 0; i < StickerLibrary.Count; i++)
            {
                int idx = i;
                var def = StickerLibrary.Get(i);
                var btn = UIFactory.Button(holder, "", Palette.Hex("3B3D4E"), () => pick(idx), new Vector2(132, 116));
                var picRt = UIFactory.Rect(btn.transform, "Pic", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(112, 70));
                var pic = picRt.gameObject.AddComponent<RawImage>();
                pic.texture = def.graffiti ? (Texture)SaveSystem.Graffiti : StickerLibrary.Texture(i);
                pic.color = def.graffiti ? Color.white : Palette.Yellow;
                pic.raycastTarget = false;
                // Seitenverhaeltnis beibehalten
                float aspect = def.aspect;
                picRt.sizeDelta = aspect >= 112f / 70f ? new Vector2(112, 112 / aspect) : new Vector2(70 * aspect, 70);
                var name = UIFactory.LabelAt(btn.transform, def.name, 16, Palette.White, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 4), new Vector2(128, 28), TextAnchor.MiddleCenter, true);
                name.GetComponent<Outline>().enabled = false;
            }
        }

        static void SwatchGrid(Transform list, Action<Color> onPick)
        {
            var holder = UIFactory.Rect(list, "Swatches", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 150));
            var le = holder.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 150;
            var grid = holder.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(76, 62);
            grid.spacing = new Vector2(14, 14);
            foreach (var c in Palette.Swatches)
            {
                var col = c;
                UIFactory.Swatch(holder, col, () => onPick(col));
            }
        }

        // ------------------------------------------------------------------ BOARDS

        void BuildBoards()
        {
            var list = List();
            foreach (var b in Catalog.Boards)
            {
                var board = b;
                var row = UIFactory.Row(list, 78);
                UIFactory.SprayButton(row, b.name, b.id == _previewBoard ? Palette.Yellow : Palette.White, () => { _previewBoard = board.id; RebuildPreview(); ShowTab("BOARDS"); }, new Vector2(250, 72), 30);
                UIFactory.FlowLabel(row, $"Tempo x{b.speed:0.00}\nPop x{b.pop:0.00}  Balance x{b.balance:0.00}", 20, TextColor, 220, TextAnchor.MiddleLeft, false).GetComponent<Outline>().enabled = false;
                bool owned = P.ownedBoards.Contains(b.id);
                string label = owned ? (P.selectedBoard == b.id ? "AKTIV" : "WAEHLEN") : BuyLabel(b.price);
                Color col = owned ? (P.selectedBoard == b.id ? Palette.Lime : Palette.Cyan) : Palette.Pink;
                UIFactory.SprayButton(row, label, col, () =>
                {
                    _previewBoard = board.id;
                    if (P.ownedBoards.Contains(board.id)) P.selectedBoard = board.id;
                    else if (SaveSystem.TrySpend(board.price))
                    {
                        P.ownedBoards.Add(board.id);
                        P.selectedBoard = board.id;
                        Toast(board.name + " gekauft!");
                    }
                    else Toast("Zu wenig Geld. Combos fahren!");
                    SaveSystem.Save();
                    RebuildPreview();
                    ShowTab("BOARDS");
                }, new Vector2(300, 72), 26);
            }
            Header(list, Catalog.Board(_previewBoard).name);
            Info(list, Catalog.Board(_previewBoard).blurb, 24);
        }

        // ------------------------------------------------------------------ OUTFIT

        void BuildOutfits()
        {
            var list = List();
            string[] slotNames = { "KOPF", "JACKE", "HOSE", "SCHUHE" };
            foreach (OutfitSlot slot in Enum.GetValues(typeof(OutfitSlot)))
            {
                Header(list, slotNames[(int)slot]);
                foreach (var o in Catalog.Outfits)
                {
                    if (o.slot != slot) continue;
                    var item = o;
                    var row = UIFactory.Row(list, 64);
                    var sw = UIFactory.Swatch(row, o.color, null, 52);
                    sw.GetComponent<Button>().interactable = false;
                    UIFactory.FlowLabel(row, o.name, 26, TextColor, 300, TextAnchor.MiddleLeft, true).GetComponent<Outline>().enabled = false;
                    bool owned = P.ownedOutfits.Contains(o.id);
                    bool equipped = P.Equipped(slot) == o.id;
                    string label = owned ? (equipped ? "AN" : "ANZIEHEN") : (Admin.Free ? "GRATIS" : UIFactory.Money(o.price));
                    Color col = owned ? (equipped ? Palette.Lime : Palette.Cyan) : Palette.Pink;
                    UIFactory.SprayButton(row, label, col, () =>
                    {
                        if (!P.ownedOutfits.Contains(item.id))
                        {
                            if (!SaveSystem.TrySpend(item.price)) { Toast("Zu wenig Geld. Combos fahren!"); return; }
                            P.ownedOutfits.Add(item.id);
                            Toast(item.name + " gekauft!");
                        }
                        P.equipped[(int)item.slot] = item.id;
                        SaveSystem.Save();
                        RebuildPreview();
                        ShowTab("OUTFIT");
                    }, new Vector2(240, 58), 24);
                }
            }
        }

        // ------------------------------------------------------------------ GARAGEN

        void BuildGarages()
        {
            var list = List();
            foreach (var g in Catalog.Garages)
            {
                var garage = g;
                var row = UIFactory.Row(list, 78);
                UIFactory.FlowLabel(row, g.name, 30, TextColor, 260, TextAnchor.MiddleLeft, true).GetComponent<Outline>().enabled = false;
                UIFactory.FlowLabel(row, g.slots + " Plaetze", 22, TextColor, 140, TextAnchor.MiddleLeft, false).GetComponent<Outline>().enabled = false;
                bool owned = P.ownedGarages.Contains(g.id);
                string label = owned ? (P.selectedGarage == g.id ? "HIER" : "WECHSELN") : BuyLabel(g.price);
                Color col = owned ? (P.selectedGarage == g.id ? Palette.Lime : Palette.Cyan) : Palette.Pink;
                UIFactory.SprayButton(row, label, col, () =>
                {
                    if (!P.ownedGarages.Contains(garage.id))
                    {
                        if (!SaveSystem.TrySpend(garage.price)) { Toast("Zu wenig Geld. Combos fahren!"); return; }
                        P.ownedGarages.Add(garage.id);
                        Toast(garage.name + " gekauft!");
                    }
                    P.selectedGarage = garage.id;
                    SaveSystem.Save();
                    RebuildEnvironment();
                    ShowTab("GARAGEN");
                }, new Vector2(330, 72), 26);
                Info(list, g.blurb, 20);
            }
        }

        // ------------------------------------------------------------------ GRAFFITI

        void BuildGraffiti()
        {
            var bg = UIFactory.Panel(_content, "CanvasBg", Palette.Hex("B9B2C9"), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -6), new Vector2(520, 520), 0f);
            var canvasRt = UIFactory.Rect(bg.transform, "Canvas", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-8, -8));
            canvasRt.gameObject.AddComponent<RawImage>();
            _graffiti = canvasRt.gameObject.AddComponent<GraffitiEditor>();

            var tools = UIFactory.Rect(_content, "Tools", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, -6), new Vector2(220, 520));
            var v = tools.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 12;
            v.childControlHeight = false;
            v.childControlWidth = false;
            Button brush = null, spray = null, eraser = null;
            void Select(GraffitiTool t)
            {
                _graffiti.tool = t;
                UIFactory.SetButtonColor(brush, t == GraffitiTool.Brush ? Palette.Yellow : Palette.White);
                UIFactory.SetButtonColor(spray, t == GraffitiTool.Spray ? Palette.Yellow : Palette.White);
                UIFactory.SetButtonColor(eraser, t == GraffitiTool.Eraser ? Palette.Yellow : Palette.White);
            }
            brush = UIFactory.SprayButton(tools, "PINSEL", Palette.Yellow, () => Select(GraffitiTool.Brush), new Vector2(210, 60), 26);
            spray = UIFactory.SprayButton(tools, "SPRAY", Palette.White, () => Select(GraffitiTool.Spray), new Vector2(210, 60), 26);
            eraser = UIFactory.SprayButton(tools, "RADIERER", Palette.White, () => Select(GraffitiTool.Eraser), new Vector2(210, 60), 26);
            UIFactory.SprayButton(tools, "LEEREN", Palette.White, () => _graffiti.Clear(), new Vector2(210, 60), 26);
            UIFactory.SprayButton(tools, "STANDARD", Palette.White, () => _graffiti.ResetToDefault(), new Vector2(210, 60), 26);
            UIFactory.SprayButton(tools, "SPEICHERN", Palette.Lime, () => { _graffiti.Save(); Toast("Graffiti gespeichert!"); }, new Vector2(210, 70), 30);

            var bottom = UIFactory.Rect(_content, "Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(0, 250));
            var bv = bottom.gameObject.AddComponent<VerticalLayoutGroup>();
            bv.spacing = 10;
            bv.childControlWidth = true;
            bv.childControlHeight = true;
            bv.childForceExpandHeight = false;
            var sizeRow = UIFactory.Row(bottom, 50);
            UIFactory.FlowLabel(sizeRow, "Pinselgroesse", 24, TextColor, 200, TextAnchor.MiddleLeft, true).GetComponent<Outline>().enabled = false;
            UIFactory.Slider(sizeRow, 0.3f, val => _graffiti.size = Mathf.Lerp(2f, 26f, val), new Vector2(420, 40), Palette.Pink);
            _graffiti.size = Mathf.Lerp(2f, 26f, 0.3f);
            var swatches = UIFactory.Rect(bottom, "Swatches", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(0, 150));
            swatches.gameObject.AddComponent<LayoutElement>().preferredHeight = 150;
            var grid = swatches.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(76, 62);
            grid.spacing = new Vector2(14, 14);
            foreach (var c in Palette.Swatches)
            {
                var col = c;
                UIFactory.Swatch(swatches, col, () => { _graffiti.color = col; if (_graffiti.tool == GraffitiTool.Eraser) Select(GraffitiTool.Brush); });
            }
        }

        // ------------------------------------------------------------------ MODS

        void BuildMods()
        {
            var list = List();
            Header(list, "EIGENE CHARAKTERE");
            var stdRow = UIFactory.Row(list, 64);
            UIFactory.FlowLabel(stdRow, "Standard (mit Outfit)", 26, TextColor, 430, TextAnchor.MiddleLeft, true).GetComponent<Outline>().enabled = false;
            bool std = string.IsNullOrEmpty(P.selectedCharacter);
            UIFactory.SprayButton(stdRow, std ? "AKTIV" : "WAEHLEN", std ? Palette.Lime : Palette.Cyan, () => SelectCharacter(""), new Vector2(200, 58), 24);
            if (ModLibrary.Characters.Count == 0) Info(list, "Noch keine eigenen Charaktere gefunden.", 22);
            foreach (var mod in ModLibrary.Characters) ModRow(list, mod, P.selectedCharacter == mod.id, () => SelectCharacter(mod.id));

            Header(list, "EIGENE BOARDS");
            if (ModLibrary.Boards.Count == 0) Info(list, "Noch keine eigenen Boards gefunden.", 22);
            foreach (var mod in ModLibrary.Boards) ModRow(list, mod, P.selectedBoard == mod.id, () =>
            {
                P.selectedBoard = mod.id;
                _previewBoard = mod.id;
                SaveSystem.Save();
                RebuildPreview();
                ShowTab("MODS");
            });

            Header(list, "SO GEHT'S");
            Info(list,
                "Format: GLB oder glTF (z. B. aus Blender: Datei > Exportieren > glTF 2.0).\n" +
                "Charaktere: Mods/Skaters/<Name>/figur.glb  -  Boards: Mods/Boards/<Name>/board.glb\n" +
                "Charaktere brauchen ein Skelett mit Huefte, Wirbelsaeule, Kopf, Armen und Beinen.\n" +
                "Erkannt werden u. a. Mixamo-, Blender/Rigify- und VRM-Knochennamen.\n" +
                "Optional mod.json: name, author, height (m), speed/pop/balance (Boards), bones.\n" +
                "Groesse und Ausrichtung werden automatisch angepasst. Online sehen andere\n" +
                "Spieler deinen Mod nur, wenn sie ihn auch haben.", 20, 200);
            Info(list, "Ordner: " + ModLibrary.GameModsFolder, 18, 40);
            var buttons = UIFactory.Row(list, 70);
            UIFactory.SprayButton(buttons, "ORDNER OEFFNEN", Palette.Yellow, () =>
            {
                System.IO.Directory.CreateDirectory(ModLibrary.GameModsFolder);
                Application.OpenURL(new Uri(ModLibrary.GameModsFolder).AbsoluteUri);
            }, new Vector2(300, 64), 26);
            UIFactory.SprayButton(buttons, "NEU LADEN", Palette.White, () =>
            {
                ModLibrary.Rescan();
                Toast("Mods werden neu geladen ...");
            }, new Vector2(240, 64), 26);
        }

        void ModRow(Transform list, ModInfo mod, bool active, Action select)
        {
            var row = UIFactory.Row(list, 64);
            UIFactory.FlowLabel(row, mod.name + (mod.author != null ? "  (" + mod.author + ")" : ""), 26, TextColor, 430, TextAnchor.MiddleLeft, true)
                .GetComponent<Outline>().enabled = false;
            if (mod.loaded)
                UIFactory.SprayButton(row, active ? "AKTIV" : "WAEHLEN", active ? Palette.Lime : Palette.Cyan, select, new Vector2(200, 58), 24);
            var status = Info(list, mod.Status, 18, 26);
            status.color = mod.error != null ? Palette.Red : HintColor;
        }

        void SelectCharacter(string id)
        {
            P.selectedCharacter = id;
            SaveSystem.Save();
            RebuildPreview();
            ShowTab("MODS");
        }

        // ------------------------------------------------------------------ OPTIONEN

        void BuildOptions()
        {
            var list = List();
            Header(list, "LAUTSTAERKE");
            var musicRow = UIFactory.Row(list, 56);
            UIFactory.FlowLabel(musicRow, "Musik", 26, TextColor, 200, TextAnchor.MiddleLeft, true).GetComponent<Outline>().enabled = false;
            UIFactory.Slider(musicRow, P.musicVolume, v => { P.musicVolume = v; SaveSystem.Save(); }, new Vector2(420, 40), Palette.Cyan);
            var sfxRow = UIFactory.Row(list, 56);
            UIFactory.FlowLabel(sfxRow, "Effekte", 26, TextColor, 200, TextAnchor.MiddleLeft, true).GetComponent<Outline>().enabled = false;
            UIFactory.Slider(sfxRow, P.sfxVolume, v => { P.sfxVolume = v; SaveSystem.Save(); }, new Vector2(420, 40), Palette.Cyan);
            var uiRow = UIFactory.Row(list, 56);
            UIFactory.FlowLabel(uiRow, "Menue", 26, TextColor, 200, TextAnchor.MiddleLeft, true).GetComponent<Outline>().enabled = false;
            UIFactory.Slider(uiRow, P.uiVolume, v => { P.uiVolume = v; SaveSystem.Save(); }, new Vector2(420, 40), Palette.Cyan);

            Header(list, "HIMMEL");
            var skyRow = UIFactory.Row(list, 70);
            foreach (var preset in SkyLook.Presets)
            {
                var p = preset;
                bool active = SkyLook.Find(P.sky) == p;
                UIFactory.SprayButton(skyRow, p.name, active ? Palette.Yellow : Palette.White, () => { P.sky = p.id; SaveSystem.Save(); ShowTab("OPTIONEN"); }, new Vector2(220, 64), 28);
            }

            Header(list, "WETTER");
            var weatherRow = UIFactory.Row(list, 70);
            foreach (var (id, label) in new[] { ("klar", "KLAR"), ("dynamisch", "DYNAMISCH"), ("regen", "REGEN"), ("gewitter", "GEWITTER") })
            {
                string mode = id;
                UIFactory.SprayButton(weatherRow, label, P.weather == mode ? Palette.Yellow : Palette.White, () => { P.weather = mode; SaveSystem.Save(); ShowTab("OPTIONEN"); }, new Vector2(200, 64), 26);
            }
            var reflRow = UIFactory.Row(list, 70);
            UIFactory.SprayButton(reflRow, P.reflections ? "SPIEGELUNGEN: AN" : "SPIEGELUNGEN: AUS", P.reflections ? Palette.Yellow : Palette.White,
                () => { P.reflections = !P.reflections; SaveSystem.Save(); ShowTab("OPTIONEN"); }, new Vector2(420, 64), 28);
            Info(list, "Dynamisch: ab und zu zieht ein Schauer auf, manchmal mit Gewitter. Online gilt das Wetter des Hosts. Spiegelungen auf nassem Boden kosten etwas Leistung.", 22, 80);

            Header(list, "EIGENE MUSIK");
            Info(list, "Lege MP3-, OGG- oder WAV-Dateien in diesen Ordner:\n" + SaveSystem.MusicFolder, 22, 70);
            var mrow = UIFactory.Row(list, 70);
            UIFactory.SprayButton(mrow, "ORDNER OEFFNEN", Palette.Yellow, () =>
            {
                System.IO.Directory.CreateDirectory(SaveSystem.MusicFolder);
                Application.OpenURL(new Uri(SaveSystem.MusicFolder).AbsoluteUri);
            }, new Vector2(300, 64), 26);
            UIFactory.SprayButton(mrow, "NEU EINLESEN", Palette.White, () =>
            {
                if (MusicPlayer.Instance == null) return;
                MusicPlayer.Instance.Rescan();
                Toast(MusicPlayer.Instance.SongCount + " Songs gefunden");
                if (MusicPlayer.Instance.SongCount > 0) MusicPlayer.Instance.Next();
            }, new Vector2(260, 64), 26);
            UIFactory.SprayButton(mrow, "NAECHSTER", Palette.White, () => MusicPlayer.Instance?.Next(), new Vector2(200, 64), 26);

            Header(list, "ADMIN-MODUS (ZUM TESTEN)");
            var adminRow = UIFactory.Row(list, 70);
            UIFactory.SprayButton(adminRow, Admin.On ? "ADMIN: AN" : "ADMIN: AUS", Admin.On ? Palette.Yellow : Palette.White, () =>
            {
                Admin.SetMode(!Admin.On);
                ShowTab("OPTIONEN");
            }, new Vector2(300, 64), 28);
            if (Admin.On)
            {
                Info(list, "Im Spiel: F1 oder Pause > ADMIN-MENUE (Schalter, Geld, Teleport).", 20);
                for (int i = 0; i < Admin.Toggles.Length; i++)
                {
                    int idx = i;
                    var row = UIFactory.Row(list, 56);
                    UIFactory.FlowLabel(row, Admin.Toggles[i].label, 24, TextColor, 330, TextAnchor.MiddleLeft, true).GetComponent<Outline>().enabled = false;
                    bool on = Admin.Toggles[i].get();
                    UIFactory.SprayButton(row, on ? "AN" : "AUS", on ? Palette.Yellow : Palette.White, () => { Admin.Toggle(idx); ShowTab("OPTIONEN"); }, new Vector2(140, 50), 24);
                }
                var actions = UIFactory.Row(list, 70);
                UIFactory.SprayButton(actions, "+1.000.000 $", Palette.Lime, () => { Admin.AddMoney(1000000); Toast("+1.000.000 $"); }, new Vector2(260, 64), 26);
                UIFactory.SprayButton(actions, "ALLES FREISCHALTEN", Palette.Cyan, () =>
                {
                    Admin.UnlockAll();
                    RebuildEnvironment();
                    Toast("Alles freigeschaltet");
                }, new Vector2(320, 64), 26);
            }

            Header(list, "STEUERUNG");
            Info(list,
                "Auto: Gas W/RT, Bremse S/LT, Lenken A D/Stick, Handbremse Leertaste/A, Kupplung Shift/X,\n" +
                "Schalten E Q/RB LB, Aussteigen oder Bail-Out F/Y, Zuruecksetzen R/Select.\n" +
                "Board: Ollie Leertaste halten und loslassen/A, Flip J/X, Grab K/RB, Grind L/B, Manual Shift/LB,\n" +
                "Spins in der Luft mit A D (nach 180 faehrt man Fakie), Revert K/RB am Boden, Einsteigen F/Y,\n" +
                "Graffiti T/Steuerkreuz hoch, Auto rufen R/Select.", 20, 155);
            Info(list, "Spielstand: " + SaveSystem.ProfilePath, 18, 40);
        }
    }
}
