using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DriftSkate
{
    /// <summary>Anzeige in der Stadt: Combo, Geld, Tacho, Balance, Prompts, Hilfe und Pausemenue.</summary>
    public class HUD : MonoBehaviour
    {
        public static HUD Instance { get; private set; }

        PlayerAvatar _avatar;
        Canvas _canvas;

        Text _money, _netInfo, _tags;
        RectTransform _comboBox;
        Text _comboPoints, _comboMult;
        Image _comboWindow;
        readonly List<Text> _actionLines = new List<Text>();
        readonly List<(string text, float time)> _actions = new List<(string, float)>();
        Text _result;
        float _resultTime;

        RectTransform _speedo;
        Text _speed, _gear, _angle;
        Image _rpmFill;

        RectTransform _balance;
        RectTransform _balanceMarker;
        Image _ollieFill;
        RectTransform _ollieBar;

        Text _prompt, _landPrompt, _toast, _help;
        float _toastTime;
        bool _helpVisible = true;

        RectTransform _pause;
        RectTransform _mainRoot, _admin;
        Text _debug;
        readonly System.Collections.Generic.List<(Button button, int index)> _adminToggles = new System.Collections.Generic.List<(Button, int)>();
        float _fps;

        void Awake()
        {
            Instance = this;
            Build();
        }

        public void Bind(PlayerAvatar avatar)
        {
            if (_avatar != null && _avatar.combo != null)
            {
                _avatar.combo.ActionAdded -= OnAction;
                _avatar.combo.Ended -= OnComboEnded;
            }
            _avatar = avatar;
            avatar.combo.ActionAdded += OnAction;
            avatar.combo.Ended += OnComboEnded;
        }

        /// <summary>Waehrend Kamerafahrten (z. B. Drohnenflug zum Ziel) ist das normale HUD aus.</summary>
        public static bool Cinematic;

        public bool Paused => _pause != null && _pause.gameObject.activeSelf;

        public void Toast(string text, float seconds = 2.5f)
        {
            _toast.text = text;
            _toastTime = seconds;
        }

        /// <summary>Neuer Song: die Now-Playing-Anzeige merkt das selbst und gleitet neu herein.</summary>
        public void ShowSong(string name) { }

        void OnAction(string label, int points)
        {
            _actions.Insert(0, (points > 0 ? label + "  +" + points : label, Time.time));
            if (_actions.Count > 4) _actions.RemoveAt(_actions.Count - 1);
        }

        void OnComboEnded(long amount, bool failed, string reason)
        {
            _actions.Clear();
            if (failed)
            {
                _result.text = (reason ?? "COMBO VERLOREN") + "\n+" + UIFactory.Money(amount) + " (halbe Punkte)";
                _result.color = Palette.Red;
            }
            else
            {
                _result.text = "COMBO!\n+" + UIFactory.Money(amount);
                _result.color = Palette.Lime;
            }
            _resultTime = 2.6f;
        }

        // ------------------------------------------------------------------ Aufbau

        void Build()
        {
            _canvas = UIFactory.Canvas("HUD Canvas", 10);
            _canvas.transform.SetParent(transform, false);
            _mainRoot = UIFactory.Stretch(_canvas.transform, "Main");
            var root = _mainRoot;

            // Geld oben rechts
            var moneyPanel = UIFactory.Panel(root, "Money", Palette.Yellow, Vector2.one, Vector2.one, Vector2.one, new Vector2(-30, -26), new Vector2(380, 74), -2f);
            _money = UIFactory.Label(moneyPanel.transform, "$0", 44, Palette.Ink, TextAnchor.MiddleCenter, true, false);
            _tags = UIFactory.LabelAt(root, "", 26, Palette.White, Vector2.one, Vector2.one, new Vector2(-34, -112), new Vector2(380, 40), TextAnchor.MiddleRight);

            _netInfo = UIFactory.LabelAt(root, "", 24, Palette.White, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -24), new Vector2(900, 36));
            NowPlaying.Create(root, new Vector2(30, -58));

            // Combo oben mittig
            _comboBox = UIFactory.Rect(root, "Combo", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(760, 300));
            var pointsPanel = UIFactory.Panel(_comboBox, "Points", Palette.Pink, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(520, 96), -3f);
            _comboPoints = UIFactory.Label(pointsPanel.transform, "0", 64, Palette.White, TextAnchor.MiddleCenter);
            var multPanel = UIFactory.Panel(_comboBox, "Mult", Palette.Yellow, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0f, 1), new Vector2(240, 14), new Vector2(150, 76), 6f);
            _comboMult = UIFactory.Label(multPanel.transform, "x1", 52, Palette.Ink, TextAnchor.MiddleCenter, true, false);
            var windowBg = UIFactory.Panel(_comboBox, "WindowBg", Palette.Ink, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -104), new Vector2(480, 12), -3f, false);
            _comboWindow = UIFactory.Panel(windowBg.transform, "Window", Palette.Lime, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero, 0f, false);
            _comboWindow.rectTransform.offsetMin = Vector2.zero;
            _comboWindow.rectTransform.offsetMax = Vector2.zero;
            for (int i = 0; i < 4; i++)
            {
                var line = UIFactory.LabelAt(_comboBox, "", 34 - i * 4, Palette.White, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                    new Vector2(0, -130 - i * 38), new Vector2(760, 40), TextAnchor.MiddleCenter);
                _actionLines.Add(line);
            }
            _result = UIFactory.LabelAt(root, "", 54, Palette.Lime, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(1000, 140), TextAnchor.MiddleCenter, true, -2f);

            // Tacho unten rechts
            _speedo = UIFactory.Rect(root, "Speedo", Vector2.right, Vector2.right, Vector2.right, new Vector2(-40, 40), new Vector2(380, 200));
            var speedPanel = UIFactory.Panel(_speedo, "SpeedPanel", Palette.Cyan, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(0, 40), new Vector2(380, 130), 3f);
            _speed = UIFactory.LabelAt(speedPanel.transform, "0", 86, Palette.Ink, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 6), new Vector2(230, 110), TextAnchor.MiddleRight, true);
            _speed.GetComponent<Outline>().effectColor = Palette.White;
            UIFactory.LabelAt(speedPanel.transform, "KM/H", 26, Palette.Ink, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(256, -24), new Vector2(100, 40));
            _gear = UIFactory.LabelAt(speedPanel.transform, "1", 60, Palette.Pink, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-18, 18), new Vector2(80, 80), TextAnchor.MiddleCenter);
            var rpmBg = UIFactory.Panel(_speedo, "RpmBg", Palette.Ink, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(10, 4), new Vector2(360, 24), 3f, false);
            _rpmFill = UIFactory.Panel(rpmBg.transform, "Rpm", Palette.Yellow, Vector2.zero, new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero, 0f, false);
            _rpmFill.rectTransform.offsetMin = new Vector2(3, 3);
            _rpmFill.rectTransform.offsetMax = new Vector2(0, -3);
            _angle = UIFactory.LabelAt(_speedo, "", 30, Palette.Yellow, Vector2.zero, Vector2.zero, new Vector2(0, 176), new Vector2(380, 40), TextAnchor.MiddleRight);

            // Balance und Ollie unten mittig
            _balance = UIFactory.Rect(root, "Balance", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 200), new Vector2(420, 34));
            var balBg = _balance.gameObject.AddComponent<Image>();
            balBg.color = Palette.Ink;
            var safe = UIFactory.Panel(_balance, "Safe", Palette.Lime, new Vector2(0.3f, 0.15f), new Vector2(0.7f, 0.85f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 0f, false);
            safe.rectTransform.offsetMin = safe.rectTransform.offsetMax = Vector2.zero;
            var marker = UIFactory.Panel(_balance, "Marker", Palette.Pink, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14, 20), 0f);
            _balanceMarker = marker.rectTransform;

            _ollieBar = UIFactory.Rect(root, "Ollie", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 160), new Vector2(240, 16));
            _ollieBar.gameObject.AddComponent<Image>().color = Palette.Ink;
            _ollieFill = UIFactory.Panel(_ollieBar, "Fill", Palette.Yellow, Vector2.zero, new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero, 0f, false);
            _ollieFill.rectTransform.offsetMin = new Vector2(2, 2);
            _ollieFill.rectTransform.offsetMax = new Vector2(0, -2);

            _prompt = UIFactory.LabelAt(root, "", 36, Palette.Yellow, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(900, 50), TextAnchor.MiddleCenter);
            _landPrompt = UIFactory.LabelAt(root, "", 110, Palette.Yellow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(1200, 160), TextAnchor.MiddleCenter, true, -4f);
            _toast = UIFactory.LabelAt(root, "", 38, Palette.White, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -160), new Vector2(1200, 60), TextAnchor.MiddleCenter);

            _help = UIFactory.LabelAt(root, "", 22, Palette.White, Vector2.zero, Vector2.zero, new Vector2(30, 30), new Vector2(760, 330), TextAnchor.LowerLeft, false);

            _debug = UIFactory.LabelAt(root, "", 22, Palette.Lime, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -100), new Vector2(900, 260), TextAnchor.UpperLeft, false);

            BuildPause(_canvas.transform);
            BuildAdmin(_canvas.transform);
        }

        void BuildAdmin(Transform root)
        {
            // Rechts, ueber fast die ganze Bildschirmhoehe; die Liste darin scrollt (Mausrad, Ziehen, Scrollbalken, Pad folgt der Auswahl)
            var panel = UIFactory.Panel(root, "Admin", Palette.Ink, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), Vector2.zero, Vector2.zero, 0f);
            panel.color = new Color(0.08f, 0.06f, 0.14f, 0.92f);
            _admin = panel.rectTransform;
            _admin.offsetMin = new Vector2(-24f - 470f, 40f);
            _admin.offsetMax = new Vector2(-24f, -40f);
            UIFactory.LabelAt(_admin, "ADMIN  (F1)", 40, Palette.Yellow, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(440, 56), TextAnchor.MiddleCenter);

            var view = UIFactory.Rect(_admin, "Scroll", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            view.offsetMin = new Vector2(8f, 10f);
            view.offsetMax = new Vector2(-24f, -74f);
            view.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f); // fuer Mausrad und Ziehen
            view.gameObject.AddComponent<RectMask2D>();
            var list = UIFactory.Rect(view, "List", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
            var v = list.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6;
            v.padding = new RectOffset(0, 0, 2, 12);
            v.childControlHeight = false;
            v.childControlWidth = false;
            v.childAlignment = TextAnchor.UpperCenter;
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var bar = UIFactory.Rect(_admin, "Scrollbar", new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), Vector2.zero, Vector2.zero);
            bar.offsetMin = new Vector2(-18f, 10f);
            bar.offsetMax = new Vector2(-8f, -74f);
            bar.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
            var handle = UIFactory.Rect(bar, "Handle", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.color = Palette.Yellow;
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImg;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = list;
            scroll.viewport = view;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 45f;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            view.gameObject.AddComponent<ScrollToSelected>();
            for (int i = 0; i < Admin.Toggles.Length; i++)
            {
                int idx = i;
                var b = UIFactory.Button(list, "", Palette.White, () => { Admin.Toggle(idx); RefreshAdmin(); }, new Vector2(420, 44), 22);
                _adminToggles.Add((b, idx));
            }
            UIFactory.Button(list, "+100.000 $", Palette.Lime, () => Admin.AddMoney(100000), new Vector2(420, 44), 22);
            UIFactory.Button(list, "AUTO ZURUECKSETZEN", Palette.Cyan, () => PlayerAvatar.Local?.car.ResetUpright(), new Vector2(420, 44), 22);
            UIFactory.Button(list, "ENGELSFLUEGEL ZURUECKSETZEN", Palette.Cyan, AngelWingsPickup.ResetForTesting, new Vector2(420, 44), 22);
            var tpLabel = UIFactory.FlowLabel(list, "TELEPORT", 26, Palette.Yellow, 420, TextAnchor.MiddleCenter);
            tpLabel.rectTransform.sizeDelta = new Vector2(420, 40);
            var spots = Admin.TeleportSpots;
            for (int i = 0; i < spots.Length; i++)
            {
                int idx = i; // Ziel erst beim Klick ausrechnen (Gasse, Park, Fluegel werden erst im Spiel gefunden)
                UIFactory.Button(list, spots[i].name.ToUpperInvariant(), Palette.Pink, () => Teleport(Admin.TeleportSpots[idx].pos), new Vector2(420, 38), 20);
            }
            _admin.gameObject.SetActive(false);
        }

        /// <summary>Admin-Menue oeffnen/schliessen (auch fuer Tests).</summary>
        public void SetAdminPanel(bool open)
        {
            _admin.gameObject.SetActive(open && Admin.On);
            RefreshAdmin();
        }

        void RefreshAdmin()
        {
            foreach (var (button, index) in _adminToggles)
            {
                bool on = Admin.Toggles[index].get();
                UIFactory.SetButtonLabel(button, Admin.Toggles[index].label + (on ? ":  AN" : ":  AUS"));
                button.GetComponent<Image>().color = on ? Palette.Yellow : Palette.White;
            }
        }

        static void Teleport(Vector3 pos)
        {
            var a = PlayerAvatar.Local;
            if (a == null) return;
            if (a.Mode == PlayerMode.Driving) a.car.Teleport(pos + Vector3.up * 0.4f, Quaternion.Euler(0, a.car.transform.eulerAngles.y, 0));
            else a.skater.Place(pos, a.skater.Heading);
        }

        void UpdateDebug()
        {
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.05f);
            if (!Admin.Debug || _avatar == null) { _debug.text = ""; return; }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"FPS {_fps:0}   Zeit x{Time.timeScale:0.0}   Modus {_avatar.Mode}");
            if (_avatar.Mode == PlayerMode.Driving)
            {
                var c = _avatar.car;
                sb.AppendLine($"Auto {c.SpeedKmh:0} km/h  Gang {c.Gear}  {c.EngineRpm:0} U/min  Winkel {c.BodySlip:0}  Lenkung {c.SteerAngle:0}");
                sb.AppendLine($"Pos {c.transform.position}");
            }
            else
            {
                var s = _avatar.skater;
                sb.AppendLine($"Skater {s.State}  {s.Speed * 3.6f:0} km/h  Push {s.PushPhase:0.00}  Balance {s.Balance:0.00}  Ollie {s.OllieCharge:0.00}");
                sb.AppendLine($"Pos {s.transform.position}  Abstand Auto {_avatar.DistanceToCar:0.0} m");
            }
            sb.AppendLine($"Combo {(_avatar.combo.Active ? _avatar.combo.Points.ToString("0") + " x" + _avatar.combo.Multiplier : "-")}   Mods {ModLibrary.Characters.Count} Figuren / {ModLibrary.Boards.Count} Boards");
            _debug.text = sb.ToString();
        }

        void BuildPause(Transform root)
        {
            _pause = UIFactory.Stretch(root, "Pause");
            var dim = _pause.gameObject.AddComponent<Image>();
            dim.color = new Color(0.05f, 0.03f, 0.1f, 0.7f);
            var panel = UIFactory.Panel(_pause, "Panel", Palette.Pink, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 560), -2f);
            UIFactory.LabelAt(panel.transform, "PAUSE", 80, Palette.Yellow, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(560, 110), TextAnchor.MiddleCenter);
            var list = UIFactory.Rect(panel.transform, "Buttons", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -50), new Vector2(460, 360));
            var v = list.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 22;
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlHeight = false;
            v.childControlWidth = false;
            UIFactory.Button(list, "WEITER", Palette.Lime, () => SetPaused(false), new Vector2(420, 80), 36);
            UIFactory.Button(list, "HILFE AN/AUS", Palette.Cyan, () => _helpVisible = !_helpVisible, new Vector2(420, 80), 36);
            UIFactory.Button(list, "ZUR GARAGE", Palette.Yellow, LeaveToGarage, new Vector2(420, 80), 36);
            UIFactory.Button(list, "ADMIN-MENUE", Palette.White, () =>
            {
                if (!Admin.On) { Toast("Admin-Modus ist aus (Garage > OPTIONEN)"); return; }
                SetPaused(false);
                _admin.gameObject.SetActive(true);
                RefreshAdmin();
            }, new Vector2(420, 70), 30);
            _pause.gameObject.SetActive(false);
        }

        void SetPaused(bool paused)
        {
            _pause.gameObject.SetActive(paused);
            GameInput.Blocked = paused;
        }

        public static void LeaveToGarage()
        {
            GameInput.Blocked = false;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.01f;
            if (PlayerAvatar.Local != null) PlayerAvatar.Local.combo.BankNow();
            SaveSystem.Save();
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.Shutdown();
                Destroy(nm.gameObject);
            }
            SceneManager.LoadScene("Garage");
        }

        // ------------------------------------------------------------------ Laufend

        void Update()
        {
            if (!SprayShop.BlocksPause && GameInput.Pause.WasPressedThisFrame()) SetPaused(!_pause.gameObject.activeSelf);
            if (GameInput.AdminPanel.WasPressedThisFrame())
            {
                if (Admin.On)
                {
                    _admin.gameObject.SetActive(!_admin.gameObject.activeSelf);
                    RefreshAdmin();
                }
                else Toast("Admin-Modus ist aus (Garage > OPTIONEN)");
            }
            if (!Admin.On && _admin.gameObject.activeSelf) _admin.gameObject.SetActive(false);
            _mainRoot.gameObject.SetActive(!Admin.HideHud && !Cinematic);
            UpdateDebug();

            _money.text = UIFactory.Money(SaveSystem.Profile.money);
            _tags.text = (TagSpot.Count > 0 ? "TAGS " + TagSpot.TaggedCount() + "/" + TagSpot.Count + "  |  " : "") + "DOSEN " + SaveSystem.Profile.sprayCans;
            UpdateNetInfo();

            _toastTime -= Time.deltaTime;
            _toast.color = new Color(1, 1, 1, Mathf.Clamp01(_toastTime));

            _resultTime -= Time.deltaTime;
            _result.color = new Color(_result.color.r, _result.color.g, _result.color.b, Mathf.Clamp01(_resultTime));
            _result.transform.parent.localScale = Vector3.one * (1f + Mathf.Max(0f, _resultTime - 2.3f) * 1.5f);

            if (_avatar == null)
            {
                _comboBox.gameObject.SetActive(false);
                _speedo.gameObject.SetActive(false);
                _balance.gameObject.SetActive(false);
                _ollieBar.gameObject.SetActive(false);
                _prompt.text = "";
                _help.text = "";
                return;
            }

            var combo = _avatar.combo;
            _comboBox.gameObject.SetActive(combo.Active);
            if (combo.Active)
            {
                _comboPoints.text = Mathf.RoundToInt(combo.Points).ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
                _comboMult.text = "x" + combo.Multiplier;
                _comboWindow.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(combo.WindowLeft / ComboSystem.Window), 1f);
                for (int i = 0; i < _actionLines.Count; i++)
                {
                    if (i < _actions.Count)
                    {
                        _actionLines[i].text = _actions[i].text;
                        float age = Time.time - _actions[i].time;
                        _actionLines[i].color = new Color(1f, 1f, 1f, Mathf.Clamp01(3f - age) * (1f - i * 0.2f));
                    }
                    else _actionLines[i].text = "";
                }
            }

            bool driving = _avatar.Mode == PlayerMode.Driving;
            _speedo.gameObject.SetActive(driving);
            if (driving)
            {
                var car = _avatar.car;
                _speed.text = Mathf.RoundToInt(car.SpeedKmh).ToString();
                _gear.text = car.Gear < 0 ? "R" : car.Gear == 0 ? "N" : car.Gear.ToString();
                float rpm = Mathf.InverseLerp(0f, car.setup.maxRpm, car.EngineRpm);
                _rpmFill.rectTransform.anchorMax = new Vector2(rpm, 1f);
                _rpmFill.color = rpm > 0.92f ? Palette.Red : Palette.Yellow;
                var scorer = _avatar.driftScorer;
                _angle.text = scorer.IsDrifting
                    ? Mathf.RoundToInt(scorer.CurrentAngle) + "°" + (scorer.CurrentZone != null ? "  ZONE x" + scorer.CurrentZone.multiplier.ToString("0.0") : "") +
                      (scorer.ProximityBonus > 0.05f ? "  NAEHE +" + Mathf.RoundToInt(scorer.ProximityBonus * 100) + "%" : "")
                    : "";
            }

            var sk = _avatar.skater;
            bool showBal = !driving && sk.ShowBalance;
            _balance.gameObject.SetActive(showBal);
            if (showBal) _balanceMarker.anchoredPosition = new Vector2(Mathf.Clamp(sk.Balance, -1f, 1f) * 200f, 0f);
            bool showOllie = !driving && sk.OllieCharge > 0.01f;
            _ollieBar.gameObject.SetActive(showOllie);
            if (showOllie) _ollieFill.rectTransform.anchorMax = new Vector2(sk.OllieCharge, 1f);

            if (!driving && sk.AwaitingBailOutLanding)
            {
                _landPrompt.text = sk.LandPromptActive ? "JETZT! [LEER / A]" : "GLEICH LANDEN ...";
                _landPrompt.color = sk.LandPromptActive ? Palette.Lime : Palette.Yellow;
                _landPrompt.transform.parent.localScale = Vector3.one * (sk.LandPromptActive ? 1.1f + Mathf.Sin(Time.time * 30f) * 0.05f : 0.8f);
            }
            else _landPrompt.text = sk.CurrentTrick != null && !driving ? sk.CurrentTrick : "";
            if (!sk.AwaitingBailOutLanding) _landPrompt.color = Palette.White;

            string prompt = _avatar.Prompt;
            if (prompt == "SPRUEHEN") _prompt.text = "[T / Steuerkreuz hoch]  GRAFFITI SPRUEHEN";
            else if (prompt == "AUTO RUFEN") _prompt.text = "[R / Select]  AUTO RUFEN";
            else if (prompt != null && prompt.StartsWith("MIT ") && prompt.EndsWith(" REDEN")) _prompt.text = "[E / X]  " + prompt;
            else if (prompt != null) _prompt.text = "[F / Y]  " + prompt;
            else _prompt.text = "";

            _help.text = _helpVisible ? (driving ? HelpDriving : _avatar.skater.State == SkaterState.Walking ? HelpWalking : HelpSkating) : "";
        }

        const string HelpDriving =
            "AUTO   (Tastatur / Gamepad)\n" +
            "Gas W / RT    Bremse S / LT    Lenken A D / Stick\n" +
            "Handbremse LEER / A    Kupplung SHIFT / X\n" +
            "Schalten E Q / RB LB (bei Handschaltung)\n" +
            "Aussteigen / Bail-Out F / Y    Zuruecksetzen R / Select\n" +
            "Umsehen rechte Maustaste / rechter Stick    Musik M\n" +
            "Kamera wechseln C / rechten Stick druecken    Pause ESC / Start";

        const string HelpSkating =
            "BOARD   (Tastatur / Gamepad)\n" +
            "Fahren W A S D / Stick    Ollie LEER halten + loslassen / A (gehalten: Tempo)\n" +
            "Wallplant: in der Luft gegen eine Wand, Ollie halten = aufladen\n" +
            "Wallride: schraeg an eine Wand springen + Grind antippen, Ollie = Wallie\n" +
            "Engelsfluegel: Ollie in der Luft = Doppelsprung\n" +
            "Flip J / X (+Richtung)    Grab K / RB (+Richtung, halten)\n" +
            "Grind L / B (an Kanten halten)    Manual SHIFT / LB (halten)\n" +
            "In der Luft lenken = Spins (180 = Fakie)    Revert K / RB am Boden\n" +
            "Einsteigen F / Y    Absteigen (zu Fuss) B / Steuerkreuz runter\n" +
            "Graffiti T / Steuerkreuz hoch    Auto rufen R / Select    Tuete rauchen G\n" +
            "Kamera wechseln C / rechten Stick druecken    Pause ESC / Start";

        const string HelpWalking =
            "ZU FUSS   (Tastatur / Gamepad)\n" +
            "Gehen W S / Stick    Drehen A D    Rennen SHIFT / LB (halten)\n" +
            "Springen LEER / A    Aufs Board B / Steuerkreuz runter\n" +
            "Einsteigen F / Y    Graffiti T / Steuerkreuz hoch    Auto rufen R / Select\n" +
            "Tuete rauchen G / Steuerkreuz links\n" +
            "Kamera wechseln C / rechten Stick druecken    Pause ESC / Start";

        void UpdateNetInfo()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) { _netInfo.text = ""; return; }
            if (GameSession.Mode == SessionMode.Solo) { _netInfo.text = "SOLO"; return; }
            int players = nm.IsServer ? nm.ConnectedClientsIds.Count : FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None).Length;
            _netInfo.text = nm.IsServer
                ? "ONLINE HOST  " + NetUtil.LocalIPv4() + ":" + GameSession.Port + "   SPIELER " + players
                : "ONLINE  verbunden mit " + GameSession.Address + "   SPIELER " + players;
        }
    }
}
