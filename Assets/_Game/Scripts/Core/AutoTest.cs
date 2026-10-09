using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DriftSkate
{
    /// <summary>
    /// Automatischer Spieltest, nur aktiv mit dem Startparameter -autotest.
    /// Klickt durch die Garage, startet die Stadt (Solo-Netzwerk), faehrt, driftet, macht einen Bail-Out,
    /// skatet, ruft das Auto und steigt wieder ein. Protokoll und Screenshots landen in Logs/.
    /// </summary>
    public class AutoTest : MonoBehaviour
    {
        public static bool Enabled => HasArg("-autotest") || HasArg("-nettest-host") || HasArg("-nettest-client");
        public static bool NetHost => HasArg("-nettest-host");
        public static bool NetClient => HasArg("-nettest-client");

        static bool HasArg(string a) => Array.IndexOf(Environment.GetCommandLineArgs(), a) >= 0;

        static string ArgValue(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        readonly StringBuilder _log = new StringBuilder();
        int _errors, _checksFailed;
        float _startTime;

        /// <summary>Start der Skate-Tests: freie Flaeche am Brunnenplatz, mit Abstand zur (festen) LONG RAIL und den Kickern.</summary>
        static readonly Vector3 SkateTestStart = new Vector3(-20f, 0.1f, -20f);

        public static void Create()
        {
            var go = new GameObject("AutoTest");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoTest>();
        }

        void Awake()
        {
            _startTime = Time.realtimeSinceStartup;
            Application.logMessageReceived += OnLog;
            if (NetHost) StartCoroutine(RunNetHost());
            else if (NetClient) StartCoroutine(RunNetClient());
            else StartCoroutine(Run());
        }

        void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                // Meldungen aus dem Editor selbst (Batch-Modus) sind keine Spielfehler.
                if (condition.Contains("should not be called in batch mode") || (stack != null && stack.Contains("UnityEditor.Search"))) return;
                if (condition.Contains("Index was out of range") && (stack == null || !stack.Contains("DriftSkate"))) return;
                _errors++;
                _log.AppendLine("  !! " + type + ": " + condition);
                if (!string.IsNullOrEmpty(stack)) _log.AppendLine("     " + stack.Split('\n')[0]);
            }
        }

        void Log(string s)
        {
            _log.AppendLine($"[{Time.realtimeSinceStartup - _startTime,6:0.0}s] {s}");
        }

        void Check(bool ok, string what)
        {
            if (!ok) _checksFailed++;
            Log((ok ? "OK    " : "FEHLT ") + what);
        }

        void Update()
        {
            if (Time.realtimeSinceStartup - _startTime > 240f) Finish("Zeitlimit erreicht");
        }

        IEnumerator WaitFor(Func<bool> cond, float timeout)
        {
            float t = 0f;
            while (!cond() && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        IEnumerator Drive(PlayerAvatar a, float duration, Func<float, VehicleInput> input, float shotAt = -1f, string shotPath = null)
        {
            float t = 0f;
            bool shot = false;
            while (t < duration)
            {
                a.car.input = input(t);
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                if (!shot && shotPath != null && t >= shotAt)
                {
                    shot = true;
                    Capture(shotPath);
                }
            }
            a.car.input = default;
        }

        /// <summary>Einsteigen, auf der Ringstrasse beschleunigen, rausspringen und die Landetaste wie ein Mensch druecken.</summary>
        IEnumerator BailOutVariant(PlayerAvatar a, string label, float reaction, bool afterTouchdown)
        {
            if (a.DistanceToCar > 3.5f) a.TestCallCar();
            yield return Frames(10);
            if (a.DistanceToCar > 3.5f) a.skater.Place(a.car.transform.TransformPoint(new Vector3(2f, 0.1f, 0f)), a.skater.Heading);
            yield return Frames(5);
            if (a.Mode != PlayerMode.Driving) a.TestInteract();
            yield return Frames(5);
            a.car.Teleport(new Vector3(-200f, 0.6f, CityBuilder.RoadCenter(0) + 3f), Quaternion.Euler(0f, 90f, 0f));
            yield return Frames(20);
            yield return Drive(a, 2.4f, t => new VehicleInput { throttle = 1f });
            a.TestInteract();
            a.skater.InjectInput(Vector2.zero, false);
            if (afterTouchdown)
            {
                yield return WaitFor(() => a.skater.State != SkaterState.Air, 4f);
                yield return new WaitForSeconds(0.1f);
            }
            else
            {
                yield return WaitFor(() => a.skater.LandPromptActive || a.skater.State != SkaterState.Air, 4f);
                yield return new WaitForSeconds(reaction);
            }
            a.skater.InjectInput(Vector2.zero, true);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            a.skater.InjectInput(Vector2.zero, false);
            yield return WaitFor(() => a.skater.State != SkaterState.Air, 4f);
            yield return new WaitForSeconds(0.5f);
            Check(a.skater.State == SkaterState.Riding, label);
        }

        IEnumerator Run()
        {
            // ---------------------------------------------------------- Garage
            yield return WaitFor(() => FindAnyObjectByType<GarageMenu>() != null, 10f);
            yield return Frames(20);
            var menu = FindAnyObjectByType<GarageMenu>();
            Check(menu != null, "Garage geladen");
            if (menu != null)
            {
                foreach (var tab in new[] { "AUTOS", "TUNING", "TEILE", "FOLIE", "BOARDS", "OUTFIT", "GARAGEN", "GRAFFITI", "OPTIONEN", "FAHREN" })
                {
                    menu.ShowTabForTest(tab);
                    yield return Frames(4);
                    if (tab == "AUTOS" || tab == "TUNING" || tab == "TEILE" || tab == "GRAFFITI" || tab == "FAHREN") Capture("Logs/play_garage_" + tab.ToLowerInvariant() + ".png");
                }
                Log("Alle Garagen-Tabs geoeffnet");

                // Folie: Vorlage anwenden, Ebene bearbeiten, Netzwerk-Kodierung pruefen
                var carSave = SaveSystem.Profile.GetCarSave(SaveSystem.Profile.selectedCar);
                var carDef = Catalog.Car(SaveSystem.Profile.selectedCar);
                carSave.design.wrap = LiveryPresets.Make("TOUGE", carDef, SaveSystem.Profile.crewColor, carSave.paint);
                menu.SelectLayerForTest(0);
                yield return WaitFor(() => false, 1.2f);
                Capture("Logs/play_garage_folie.png");
                Check(carSave.design.wrap.Count == 4 && FindAnyObjectByType<CarLivery>() != null, "Folien-Vorlage angewendet, Folie auf dem Auto");
                var decoded = CarDesign.Decode(carSave.design.Encode(), carDef.id, Color.white);
                bool same = decoded.wrap.Count == carSave.design.wrap.Count;
                for (int i = 0; same && i < decoded.wrap.Count; i++)
                    same = decoded.wrap[i].sticker == carSave.design.wrap[i].sticker && Mathf.Abs(decoded.wrap[i].x - carSave.design.wrap[i].x) < 0.01f
                        && Mathf.Abs(decoded.wrap[i].size - carSave.design.wrap[i].size) / carSave.design.wrap[i].size < 0.05f;
                Check(same && decoded.parts[CarDesign.Rims] == carSave.design.parts[CarDesign.Rims], $"Design fuers Netzwerk kodiert ({carSave.design.Encode().Length} Zeichen) und zurueckgelesen");

                // Unterboden-Neon: in der Garage einschalten (pink), kodieren, Vorschau-Auto leuchtet
                carSave.design.neon = 1;
                carSave.design.neonColor = Palette.Pink;
                menu.RebuildPreviewForTest();
                menu.ShowTabForTest("TEILE");
                yield return Frames(6);
                var dn = CarDesign.Decode(carSave.design.Encode(), carDef.id, Color.white);
                Check(dn.neon == 1 && Mathf.Abs(dn.neonColor.r - Palette.Pink.r) < 0.01f && FindAnyObjectByType<Underglow>() != null,
                      "Unterboden-Neon in der Garage an und fuers Netzwerk kodiert");
                Capture("Logs/play_garage_neon.png");

                // Mods: Beispiel-Charakter (Mixamo-Rig) und Beispiel-Longboard
                yield return WaitFor(() => ModLibrary.Characters.TrueForAll(m => !m.loading) && ModLibrary.Boards.TrueForAll(m => !m.loading), 20f);
                foreach (var m in ModLibrary.Characters) Log($"Mod-Charakter '{m.name}': {m.Status}");
                foreach (var m in ModLibrary.Boards) Log($"Mod-Board '{m.name}': {m.Status}");
                var modChar = ModLibrary.Characters.Find(m => m.loaded);
                var modBoard = ModLibrary.Boards.Find(m => m.loaded);
                Check(modChar != null, "Mod-Charakter geladen");
                Check(modBoard != null, "Mod-Board geladen");
                if (modChar != null) SaveSystem.Profile.selectedCharacter = modChar.id;
                if (modBoard != null) SaveSystem.Profile.selectedBoard = modBoard.id;
                SaveSystem.Save();
                menu.ShowTabForTest("MODS");
                yield return Frames(10);
                Capture("Logs/play_garage_mods.png");

                // Admin-Modus: alles gratis, freischalten
                Admin.SetMode(true);
                SaveSystem.Profile.adminFree = true;
                long before = SaveSystem.Profile.money;
                Check(SaveSystem.TrySpend(1500000) && SaveSystem.Profile.money == before, "Admin: teures Auto gratis");
                Admin.UnlockAll();
                Check(SaveSystem.Profile.ownedCars.Count == Catalog.Cars.Count, "Admin: alles freigeschaltet");
                menu.ShowTabForTest("OPTIONEN");
                yield return Frames(6);
                Capture("Logs/play_garage_admin.png");
                SaveSystem.Profile.adminFree = false;
                SaveSystem.Profile.selectedCar = "roku86";
                menu.ShowTabForTest("FAHREN");
            }

            // ---------------------------------------------------------- Stadt
            GameSession.Mode = SessionMode.Solo;
            // Bei Gewitter testen: Wetter, Blitze und Spiegel-Kamera laufen so die ganze Zeit mit
            SaveSystem.Profile.weather = "gewitter";
            SaveSystem.Profile.reflections = true;
            SceneManager.LoadScene("City");
            yield return WaitFor(() => PlayerAvatar.Local != null, 20f);
            var a = PlayerAvatar.Local;
            Check(a != null, "Stadt geladen, Spieler gespawnt (Solo-Host)");
            if (a == null) { Finish("kein Spieler"); yield break; }
            a.externalControl = true;
            a.skater.inputEnabled = false;
            a.combo.ActionAdded += (l, p) => Log($"   Aktion: {l} +{p} (x{a.combo.Multiplier})");
            a.combo.Ended += (m, f, r) => Log($"   Combo zu Ende: +{m} Geld" + (f ? $" (gescheitert: {r}, Auto bei {a.car.transform.position}, Skater bei {a.skater.transform.position})" : ""));
            yield return Frames(30);
            Check(WeatherSystem.Rain > 0.9f && WeatherSystem.Wetness > 0.9f && PlanarReflection.Instance != null && PlanarReflection.Instance.Active,
                "Regen aktiv, Boden nass, Spiegelungen an");
            Log($"Start: Pos {a.car.transform.position}, Modus {a.Mode}");
            Capture("Logs/play_city_start.png");
            var rig = a.skater.rig;
            Check(rig != null && rig.Ready, "Rig an der Figur aktiv (Mod-Charakter: " + SaveSystem.Profile.selectedCharacter + ")");
            Check(a.skater.boardPivot != null && a.skater.boardPivot.Find("ModBoard") != null, "Mod-Board am Skater");

            // Unterboden-Neon am eigenen Auto in der Stadt
            {
                var ug = a.car.GetComponentInChildren<Underglow>();
                var glow = ug != null ? ug.transform.Find("NeonGlow") : null;
                yield return WaitFor(() => glow != null && glow.gameObject.activeInHierarchy, 2f);
                float gap = glow != null ? a.car.transform.InverseTransformPoint(glow.position).y : 99f;
                Check(ug != null && glow != null && glow.gameObject.activeInHierarchy && Mathf.Abs(gap) < 0.6f, $"Neon unter dem Auto, Lichtschein auf dem Boden ({gap:0.00} m unter dem Auto)");
                var nc = Camera.main;
                if (nc != null)
                {
                    Vector3 cp = a.car.transform.position;
                    nc.transform.position = cp + a.car.transform.right * 4.2f + a.car.transform.forward * 2.5f + Vector3.up * 0.9f;
                    nc.transform.LookAt(cp + Vector3.up * 0.2f);
                }
                Capture("Logs/play_neon.png");
            }

            // Admin-Menue im Spiel
            SaveSystem.Profile.adminDebug = true;
            HUD.Instance.SetAdminPanel(true);
            yield return Frames(5);
            Capture("Logs/play_admin_panel.png");
            // Liste scrollt (in Spielgroesse 1600x900): ganz nach unten, dann muss der letzte Eintrag sichtbar sein
            var adminScroll = HUD.Instance.GetComponentInChildren<UnityEngine.UI.ScrollRect>(false);
            if (adminScroll != null && adminScroll.content.childCount > 0)
            {
                string scrollInfo = "";
                bool lastVisible = false;
                Capture("Logs/play_admin_panel_scrolled.png", () =>
                {
                    var list = adminScroll.content;
                    float overflow = list.rect.height - adminScroll.viewport.rect.height;
                    adminScroll.verticalNormalizedPosition = 0f;
                    var last = (RectTransform)list.GetChild(list.childCount - 1);
                    var c = new Vector3[4];
                    last.GetWorldCorners(c);
                    float bottom = adminScroll.viewport.InverseTransformPoint(c[0]).y;
                    lastVisible = bottom >= adminScroll.viewport.rect.yMin - 1f;
                    scrollInfo = $"{list.childCount} Eintraege, {overflow:0} px mehr als sichtbar, Position {list.anchoredPosition.y:0}, letzter '{last.GetComponentInChildren<UnityEngine.UI.Text>()?.text}'";
                });
                Check(lastVisible, "Admin-Menue scrollt bis zum letzten Eintrag (" + scrollInfo + ")");
                adminScroll.verticalNormalizedPosition = 1f;
            }
            else Check(false, "Admin-Menue hat eine Scroll-Liste");
            HUD.Instance.SetAdminPanel(false);
            SaveSystem.Profile.adminDebug = false;
            Admin.SetMode(false);

            // 1. Beschleunigen - auf der langen, freien Ringstrasse im Sueden, auf der suedlichen Spur: der Drift nach links
            // hat so Platz bis zum Gehweg mit den Laternen
            a.car.Teleport(new Vector3(-200f, 0.6f, CityBuilder.RoadCenter(0) - 4f), Quaternion.Euler(0f, 90f, 0f));
            yield return Frames(20);
            yield return Drive(a, 2.6f, t => new VehicleInput { throttle = 1f });
            Log($"Nach 2,6 s Vollgas: {a.car.SpeedKmh:0} km/h, Gang {a.car.Gear}");
            Check(a.car.SpeedKmh > 30f, "Auto beschleunigt");

            // 2. Drift einleiten und halten (mit Lenkhilfe)
            yield return Drive(a, 0.4f, t => new VehicleInput { steer = -1f, throttle = 0.6f, handbrake = t < 0.3f });
            float maxSlip = 0f;
            yield return Drive(a, 3f, t =>
            {
                float slip = a.car.BodySlip;
                maxSlip = Mathf.Max(maxSlip, Mathf.Abs(slip));
                float err = Mathf.Abs(slip) - 30f;
                return new VehicleInput { steer = -Mathf.Sign(slip) * 0.25f, throttle = Mathf.Clamp01(0.75f - err * 0.03f) };
            }, 1.6f, "Logs/play_drift.png");
            Log($"Drift: max Winkel {maxSlip:0}, Tempo {a.car.SpeedKmh:0} km/h, Combo aktiv {a.combo.Active}, Punkte {a.combo.Points:0} x{a.combo.Multiplier}");
            Check(maxSlip > 15f, "Drift erreicht");

            // 3. Gerade fahren, Combo auszahlen lassen
            yield return Drive(a, 3f, t => new VehicleInput { throttle = 0.3f, steer = Mathf.Clamp(-a.car.BodySlip / 40f, -1f, 1f) });
            Log($"Geld nach erster Combo: {SaveSystem.Profile.money}");

            // 3b. Kamera-Perspektiven beim Fahren durchschalten (C / rechten Stick druecken)
            int startView = CameraRig.Instance.View;
            for (int i = 0; i < CameraRig.Views.Length; i++)
            {
                yield return Drive(a, 1.2f, t => new VehicleInput { throttle = 0.3f, steer = Mathf.Clamp(-a.car.BodySlip / 40f, -1f, 1f) });
                Capture("Logs/play_cam_drive_" + CameraRig.Views[CameraRig.Instance.View].ToLower() + ".png");
                CameraRig.Instance.NextView();
            }
            Check(CameraRig.Instance.View == startView, "Kamera-Perspektiven durchgeschaltet: " + string.Join(", ", CameraRig.Views));


            // 4. Bail-Out bei Tempo
            yield return Drive(a, 1.2f, t => new VehicleInput { throttle = 0.8f });
            float bailSpeed = a.car.SpeedKmh;
            a.TestInteract();
            Log($"Bail-Out bei {bailSpeed:0} km/h, Modus {a.Mode}, Skater {a.skater.State}");
            Check(a.Mode == PlayerMode.Skating && a.skater.State == SkaterState.Air, "Bail-Out gestartet");
            // Wie beim echten Driften: Lenkung (D), Handbremse (Leertaste = Ollie) und Kupplung (Shift = Manual)
            // bleiben vom Auto aus die ganze Zeit gedrueckt
            a.skater.InjectInput(new Vector2(1f, 0f), true, manual: true);
            yield return WaitFor(() => a.skater.LandPromptActive || a.skater.State != SkaterState.Air, 4f);
            Capture("Logs/play_bailout.png");
            yield return WaitFor(() => a.skater.State != SkaterState.Air, 4f);
            Log($"Gelandet: Skater {a.skater.State}");
            Check(a.skater.State == SkaterState.Riding, "Bail-Out sauber gelandet (Tasten aus dem Auto noch gedrueckt)");
            float hold = Time.time;
            while (Time.time - hold < 0.8f) { a.skater.InjectInput(new Vector2(1f, 0f), true, manual: true); yield return null; }
            a.skater.InjectInput(Vector2.zero, false);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Check(a.skater.State == SkaterState.Riding && !a.skater.Manualing, "Nach dem Bail-Out kein Manual-Sturz und kein ungewollter Ollie");
            Capture("Logs/play_mod_skater.png");

            // Wie ein Mensch: erst auf "JETZT!" reagieren (0,3 s Reaktionszeit), bzw. knapp nach dem Aufsetzen druecken
            yield return BailOutVariant(a, "Bail-Out mit 0,3 s Reaktionszeit", 0.3f, false);
            yield return BailOutVariant(a, "Bail-Out, Taste knapp nach dem Aufsetzen", 0f, true);
            // Skate-Tests auf freiem Platz (nach dem Bail-Out kann man ueberall gelandet sein)
            a.skater.Place(SkateTestStart, 90f);
            yield return Frames(10);
            yield return WaitFor(() => a.skater.State == SkaterState.Riding, 3f);

            // Fahrtwind: kurz auf Grind-Tempo halten, Streifen und Rauschen muessen da sein
            var skRb = a.skater.GetComponent<Rigidbody>();
            float wt = Time.time;
            while (Time.time - wt < 1.4f)
            {
                a.skater.InjectInput(Vector2.zero, false);
                skRb.linearVelocity = new Vector3(23f, skRb.linearVelocity.y, 0f);
                yield return null;
            }
            float windI = CameraRig.Instance.Wind.Intensity;
            Log($"Fahrtwind bei {a.skater.Speed * 3.6f:0} km/h: Staerke {windI:0.00}");
            Check(windI > 0.5f, "Fahrtwind bei hohem Skate-Tempo");
            Capture("Logs/play_speedwind.png");
            a.skater.Place(SkateTestStart, 90f);
            yield return Frames(10);
            yield return WaitFor(() => a.skater.State == SkaterState.Riding, 3f);

            // 5. Skaten: anschieben, Ollie + Kickflip
            float t0 = Time.time;
            bool pushShot = false;
            while (Time.time - t0 < 2.2f)
            {
                a.skater.InjectInput(new Vector2(0, 1), false);
                if (!pushShot && a.skater.PushPhase > 0.4f && a.skater.PushPhase < 0.55f) { pushShot = true; Capture("Logs/play_push.png"); }
                yield return null;
            }
            Log($"Skate-Tempo nach Pushen: {a.skater.Speed * 3.6f:0} km/h");
            Check(a.skater.Speed > 4f, "Pushen bringt Tempo");
            t0 = Time.time;
            while (Time.time - t0 < 1.0f) { a.skater.InjectInput(new Vector2(1f, 0.3f), false); yield return null; }
            Capture("Logs/play_carve.png");
            Log($"Nach Carven: {a.skater.Speed * 3.6f:0} km/h, Zustand {a.skater.State}");
            t0 = Time.time;
            while (Time.time - t0 < 0.6f) { a.skater.InjectInput(new Vector2(0f, 1f), false); yield return null; }
            t0 = Time.time;
            while (Time.time - t0 < 0.3f) { a.skater.InjectInput(new Vector2(0, 0.5f), true); yield return null; }
            a.skater.InjectInput(Vector2.zero, false);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Check(a.skater.State == SkaterState.Air, "Ollie abgesprungen");
            a.skater.InjectInput(Vector2.zero, false, flip: true);
            yield return new WaitForSeconds(0.2f);
            Capture("Logs/play_kickflip.png");
            yield return WaitFor(() => a.skater.State != SkaterState.Air, 3f);
            Log($"Nach Kickflip: {a.skater.State}, Combo {a.combo.Points:0} x{a.combo.Multiplier}");

            // 6. Grab mit Spin
            yield return WaitFor(() => a.skater.State == SkaterState.Riding, 3f);
            t0 = Time.time;
            while (Time.time - t0 < 0.35f) { a.skater.InjectInput(new Vector2(0, 0.6f), true); yield return null; }
            a.skater.InjectInput(Vector2.zero, false);
            yield return new WaitForFixedUpdate();
            t0 = Time.time;
            while (a.skater.State == SkaterState.Air && Time.time - t0 < 0.45f) { a.skater.InjectInput(new Vector2(1f, 0), false, grab: true); yield return null; }
            a.skater.InjectInput(Vector2.zero, false);
            yield return WaitFor(() => a.skater.State != SkaterState.Air, 3f);
            Log($"Nach Grab/Spin: {a.skater.State}, Fakie {a.skater.Fakie}");

            // 6b. Ollie mit 180, rueckwaerts (Fakie) weiterfahren und lenken, dann Revert
            yield return WaitFor(() => a.skater.State == SkaterState.Riding, 3f);
            a.skater.Place(SkateTestStart, 90f);
            yield return Frames(5);
            t0 = Time.time;
            while (Time.time - t0 < 1.4f) { a.skater.InjectInput(new Vector2(0, 1), false); yield return null; }
            t0 = Time.time;
            while (Time.time - t0 < 0.3f) { a.skater.InjectInput(Vector2.zero, true); yield return null; }
            a.skater.InjectInput(Vector2.zero, false);
            yield return new WaitForFixedUpdate();
            t0 = Time.time;
            while (a.skater.State == SkaterState.Air && Mathf.Abs(a.skater.AirSpin) < 172f && Time.time - t0 < 1.5f)
            {
                a.skater.InjectInput(new Vector2(1f, 0), false);
                yield return null;
            }
            a.skater.InjectInput(Vector2.zero, false);
            yield return WaitFor(() => a.skater.State != SkaterState.Air, 3f);
            Log($"180: Drehung {a.skater.AirSpin:0} Grad, Zustand {a.skater.State}, Fakie {a.skater.Fakie}");
            Check(a.skater.State == SkaterState.Riding && a.skater.Fakie, "180 gelandet, faehrt Fakie weiter");
            t0 = Time.time;
            {
                SkaterState lastSt = a.skater.State;
                while (Time.time - t0 < 1.2f)
                {
                    a.skater.InjectInput(new Vector2(0.3f, 1f), false);
                    if (a.skater.State != lastSt) { Log($"   Fakie-Fahrt: {lastSt}->{a.skater.State} bei {a.skater.transform.position}, {a.skater.Speed * 3.6f:0} km/h"); lastSt = a.skater.State; }
                    yield return null;
                }
            }
            Capture("Logs/play_fakie.png");
            Check(a.skater.State == SkaterState.Riding && a.skater.Fakie && a.skater.Speed > 2f,
                $"Fakie anschieben und lenken ({a.skater.Speed * 3.6f:0} km/h)");
            yield return WaitFor(() => a.skater.State == SkaterState.Riding, 1f); // wie ein Mensch: erst, wenn er wieder rollt
            Log($"Vor dem Revert: {a.skater.State}, Fakie {a.skater.Fakie}, {a.skater.Speed * 3.6f:0} km/h");
            a.skater.InjectInput(Vector2.zero, false, grab: true);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            a.skater.InjectInput(Vector2.zero, false);
            yield return Frames(15);
            Log($"Nach dem Revert: {a.skater.State}, Fakie {a.skater.Fakie}");
            Check(!a.skater.Fakie && a.skater.State == SkaterState.Riding, "Revert aus Fakie");

            // 6c. Wie ein Mensch: kurzer Ollie, D die ganze Luftzeit und noch ueber die Landung hinaus halten,
            // dann anschieben. Das Board darf nach dem Aufsetzen weder nachdrehen noch weiterlenken.
            a.skater.Place(SkateTestStart, 90f);
            yield return Frames(5);
            t0 = Time.time;
            while (Time.time - t0 < 1.4f) { a.skater.InjectInput(new Vector2(0, 1), false); yield return null; }
            a.skater.InjectInput(Vector2.zero, true);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            a.skater.InjectInput(Vector2.zero, false);
            yield return new WaitForFixedUpdate();
            t0 = Time.time;
            while (a.skater.State == SkaterState.Air && Time.time - t0 < 2f) { a.skater.InjectInput(new Vector2(1f, 0), false); yield return null; }
            float landHeading = a.skater.Heading, landSpin = a.skater.AirSpin;
            bool landFakie = a.skater.Fakie;
            t0 = Time.time;
            while (Time.time - t0 < 0.25f) { a.skater.InjectInput(new Vector2(1f, 0), false); yield return null; }
            t0 = Time.time;
            while (Time.time - t0 < 0.5f) { a.skater.InjectInput(new Vector2(0, 1), false); yield return null; }
            a.skater.InjectInput(Vector2.zero, false);
            float turned = Mathf.Abs(Mathf.DeltaAngle(landHeading, a.skater.Heading));
            float boardYaw = a.skater.align.eulerAngles.y;
            float boardOff = Mathf.Abs(Mathf.DeltaAngle(boardYaw, a.skater.Heading + (a.skater.Fakie ? 180f : 0f)));
            Log($"180 mit gehaltenem D: Drehung {landSpin:0} Grad, Fakie {landFakie} -> {a.skater.Fakie}, danach gedreht {turned:0} Grad, Board-Abweichung {boardOff:0} Grad, {a.skater.State}");
            Check(a.skater.State == SkaterState.Riding && landFakie && a.skater.Fakie && turned < 10f && boardOff < 10f,
                "180 mit gehaltener Spin-Taste: bleibt Fakie, dreht nicht nach");

            // 7. Manual
            yield return WaitFor(() => a.skater.State == SkaterState.Riding, 3f);
            t0 = Time.time;
            while (Time.time - t0 < 1f)
            {
                a.skater.InjectInput(new Vector2(0, -Mathf.Clamp(a.skater.Balance * 3f, -1f, 1f)), false, manual: true);
                yield return null;
            }
            a.skater.InjectInput(Vector2.zero, false);
            Log($"Nach Manual: {a.skater.State}, Combo aktiv {a.combo.Active}");

            // 7b. Grind auf der langen Rail am Brunnenplatz
            GrindRail rail = null;
            foreach (var r in GrindRail.All) if (r.label == "LONG RAIL") rail = r;
            Check(rail != null, "Rail gefunden");
            if (rail != null)
            {
                Vector3 dir = rail.DirectionAt(0.5f);
                Vector3 start = rail.PointAt(0.5f) - dir * 4f;
                start.y = 0.3f;
                if (Physics.Raycast(start + Vector3.up * 2f, Vector3.down, out RaycastHit gh, 5f, ~LayerMask.GetMask("Rail", "Skater"))) start = gh.point;
                yield return WaitFor(() => a.skater.State == SkaterState.Riding, 3f);
                a.skater.Place(start, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);
                t0 = Time.time;
                while (a.skater.State != SkaterState.Grinding && Time.time - t0 < 3f) { a.skater.InjectInput(new Vector2(0, 1), false, grind: true); yield return null; }
                Check(a.skater.State == SkaterState.Grinding, "Grind gestartet (" + a.skater.CurrentTrick + ")");
                float grindStartSpeed = a.skater.Speed;
                Capture("Logs/play_grind.png");
                t0 = Time.time;
                while (a.skater.State == SkaterState.Grinding && Time.time - t0 < 1.2f)
                {
                    a.skater.InjectInput(new Vector2(Mathf.Clamp(a.skater.Balance * 3f, -1f, 1f), 0), false, grind: true);
                    yield return null;
                }
                float grindEndSpeed = a.skater.Speed;
                bool stillGrinding = a.skater.State == SkaterState.Grinding;
                a.skater.InjectInput(Vector2.zero, true);
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                a.skater.InjectInput(Vector2.zero, false);
                float jumpSpeed = a.skater.Speed;
                Log($"Grind-Tempo: Start {grindStartSpeed * 3.6f:0} km/h, Ende {grindEndSpeed * 3.6f:0} km/h, nach dem Absprung {jumpSpeed * 3.6f:0} km/h");
                if (stillGrinding)
                {
                    Check(grindEndSpeed > grindStartSpeed + 1f, "Beim Grinden schneller geworden");
                    Check(jumpSpeed > grindEndSpeed + 1f, "Absprung vom Grind gibt Schwung");
                }
                yield return WaitFor(() => a.skater.State != SkaterState.Air && a.skater.State != SkaterState.Grinding, 3f);
                Log($"Nach Grind: {a.skater.State}, Hoehe {a.skater.transform.position.y:0.00} m, Combo {a.combo.Points:0} x{a.combo.Multiplier}");
                // Die Rail ist fest: nach dem Ollie aus dem Grind nicht oben auf der Stange haengen bleiben
                Check(a.skater.State == SkaterState.Riding && a.skater.transform.position.y < 0.3f, "Nach dem Ollie aus dem Grind neben der Rail gelandet");

                // 7b2. Ohne Grind-Taste quer auf die Rail zu: man prallt ab statt hindurchzufahren
                yield return WaitFor(() => a.skater.State == SkaterState.Riding, 3f);
                Vector3 mid = rail.PointAt(rail.Length * 0.5f);
                Vector3 across = Vector3.Cross(Vector3.up, rail.DirectionAt(rail.Length * 0.5f)).normalized;
                if (Vector3.Dot(across, -mid) < 0f) across = -across; // von der Platzmitte her anfahren (am Rand stehen Laternen)
                Vector3 from = mid + across * 4f;
                if (Physics.Raycast(from + Vector3.up * 2f, Vector3.down, out RaycastHit fh, 5f, ~LayerMask.GetMask("Rail", "Skater"))) from = fh.point;
                a.skater.Place(from, Mathf.Atan2(-across.x, -across.z) * Mathf.Rad2Deg);
                t0 = Time.time;
                while (Time.time - t0 < 2.5f) { a.skater.InjectInput(new Vector2(0, 1), false); yield return null; }
                a.skater.InjectInput(Vector2.zero, false);
                float sideOfRail = Vector3.Dot(a.skater.transform.position - mid, across);
                Log($"Ohne Grind gegen die Rail: Abstand {sideOfRail:0.00} m, {a.skater.State}");
                Check(sideOfRail > 0.2f && a.skater.State == SkaterState.Riding, "Rail ist fest (Skater faehrt nicht hindurch)");
            }

            // 7d. Absteigen und zu Fuss: gehen, rennen, springen, wieder aufs Board
            yield return WaitFor(() => a.skater.State == SkaterState.Riding, 3f);
            a.skater.Place(SkateTestStart, 90f);
            yield return Frames(5);
            a.skater.InjectInput(Vector2.zero, false);
            a.skater.InjectBoardToggle();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Check(a.skater.State == SkaterState.Walking, "Abgestiegen (zu Fuss)");
            Vector3 walkStart = a.skater.transform.position;
            t0 = Time.time;
            while (Time.time - t0 < 2f) { a.skater.InjectInput(new Vector2(0, 1), false); yield return null; }
            float walked = Vector3.Distance(walkStart, a.skater.transform.position), walkSpeed = a.skater.Speed;
            t0 = Time.time;
            while (Time.time - t0 < 1.5f) { a.skater.InjectInput(new Vector2(0, 1), false, manual: true); yield return null; }
            float runSpeed = a.skater.Speed;
            Capture("Logs/play_walk.png");
            // Seitenansicht fuer die Laufanimation (Kamera nur fuer dieses Bild versetzen)
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 sk = a.skater.transform.position, side = a.skater.transform.right;
                cam.transform.position = sk + side * 3.2f + Vector3.up * 1.1f;
                cam.transform.LookAt(sk + Vector3.up * 0.9f);
                Capture("Logs/play_walk_side.png");
            }
            Log($"Zu Fuss: in 2 s {walked:0.0} m gegangen ({walkSpeed * 3.6f:0} km/h), rennend {runSpeed * 3.6f:0} km/h");
            Check(a.skater.State == SkaterState.Walking && walked > 3f && walkSpeed < 3f && runSpeed > 4.5f, "Gehen und Rennen");
            float groundY = a.skater.transform.position.y, peak = groundY;
            a.skater.InjectInput(new Vector2(0, 1), true, manual: true);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            t0 = Time.time;
            float jumpPose = 0f;
            bool jumpShot = false;
            while (Time.time - t0 < 0.6f)
            {
                a.skater.InjectInput(new Vector2(0, 1), false, manual: true);
                peak = Mathf.Max(peak, a.skater.transform.position.y);
                if (a.skater.rig != null) jumpPose = Mathf.Max(jumpPose, a.skater.rig.walkJump);
                if (!jumpShot && Time.time - t0 > 0.18f) { jumpShot = true; Capture("Logs/play_walk_jump.png"); }
                yield return null;
            }
            yield return WaitFor(() => a.skater.transform.position.y < groundY + 0.05f, 2f);
            yield return WaitFor(() => false, 0.4f);
            if (a.skater.rig != null)
                Check(jumpPose > 0.8f && a.skater.rig.walkJump < 0.2f, $"Sprung-Animation zu Fuss (Pose {jumpPose:0.00}, nach der Landung {a.skater.rig.walkJump:0.00})");
            Check(peak > groundY + 0.4f && a.skater.State == SkaterState.Walking, $"Zu Fuss gesprungen ({peak - groundY:0.00} m hoch)");
            // Shift (Rennen) noch gehalten beim Aufsteigen: darf kein Manual ausloesen
            a.skater.InjectInput(new Vector2(0, 1), false, manual: true);
            a.skater.InjectBoardToggle();
            for (int k = 0; k < 4; k++) yield return new WaitForFixedUpdate();
            Check(a.skater.State == SkaterState.Riding && !a.skater.Manualing, "Wieder aufs Board (gehaltenes Shift loest kein Manual aus)");
            a.skater.InjectInput(Vector2.zero, false);

            // 7g. Wand zu Wand in der Seitengasse: quer anfahren (A halten = Tempo), an die Wand, A halten (laden) und
            // loslassen, auf der anderen Seite wieder abfangen. Mit Reaktionszeit wie ein Mensch.
            if (AlleyCrew.Found)
            {
                var al = AlleyCrew.Current;
                float depth = -1f;
                for (float d = 15f; d < al.length - 3f && depth < 0f; d += 1.5f)
                {
                    Vector3 c = al.entrance + al.inward * d + Vector3.up * 0.4f;
                    Vector3 lo = c - al.across * (al.halfWidth - 0.45f), hi = c + al.across * (al.halfWidth - 0.45f);
                    if (!Physics.CheckCapsule(lo, hi, 0.3f, ~LayerMask.GetMask("Skater", "Car", "Ignore Raycast"), QueryTriggerInteraction.Ignore)
                        && !Physics.CheckCapsule(lo + Vector3.up * 1.4f, hi + Vector3.up * 1.4f, 0.3f, ~LayerMask.GetMask("Skater", "Car", "Ignore Raycast"), QueryTriggerInteraction.Ignore)) depth = d;
                }
                Check(depth > 0f, $"Freie Stelle in der Gasse fuer Wand-zu-Wand ({depth:0} m tief, {al.halfWidth * 2f:0.0} m breit)");
                if (depth > 0f)
                {
                    Vector3 start = al.entrance + al.inward * depth - al.across * (al.halfWidth - 0.5f);
                    if (Physics.Raycast(start + Vector3.up * 2f, Vector3.down, out RaycastHit sg, 5f, ~LayerMask.GetMask("Rail", "Skater"))) start = sg.point;
                    a.skater.Place(start + Vector3.up * 0.1f, Mathf.Atan2(al.across.x, al.across.z) * Mathf.Rad2Deg);
                    yield return Frames(5);
                    int maxChain = 0;
                    float t3 = Time.time;
                    // Anlauf: geduckt Tempo holen, 3 m vor der Wand loslassen (Ollie)
                    Vector3 farWall = al.entrance + al.inward * depth + al.across * al.halfWidth;
                    while (Time.time - t3 < 2.5f && Vector3.Dot(farWall - a.skater.transform.position, al.across) > 3f) { a.skater.InjectInput(Vector2.zero, true); yield return null; }
                    a.skater.InjectInput(Vector2.zero, false);
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                    string chainLog = $" Absprung bei {a.skater.Speed:0.0} m/s ({a.skater.State});";
                    for (int hop = 0; hop < 3; hop++)
                    {
                        yield return WaitFor(() => a.skater.State == SkaterState.WallPlant || a.skater.State == SkaterState.Riding || a.skater.State == SkaterState.Bailed, 2.5f);
                        if (a.skater.State != SkaterState.WallPlant) break;
                        maxChain = Mathf.Max(maxChain, a.skater.PlantChain);
                        if (hop == 1) Capture("Logs/play_wall2wall.png");
                        yield return WaitFor(() => false, 0.25f); // Reaktionszeit
                        // A halten (bei der zweiten Wand voll laden), dann loslassen
                        float holdFor = hop == 1 ? 1.05f : 0.4f, th = Time.time;
                        float charge = 0f;
                        while (Time.time - th < holdFor && a.skater.State == SkaterState.WallPlant) { a.skater.InjectInput(Vector2.zero, true); charge = Mathf.Max(charge, a.skater.PlantCharge); yield return null; }
                        bool stillThere = a.skater.State == SkaterState.WallPlant;
                        a.skater.InjectInput(Vector2.zero, false);
                        yield return new WaitForFixedUpdate();
                        yield return new WaitForFixedUpdate();
                        Vector3 v = a.skater.GetComponent<Rigidbody>().linearVelocity;
                        chainLog += $" #{hop + 1}: Ladung {charge:0.00}, haelt {stillThere}, weg {Vector3.ProjectOnPlane(v, Vector3.up).magnitude:0.0} m/s hoch {v.y:0.0} m/s;";
                        if (hop == 1) Check(stillThere && charge > 0.95f, $"An der Wand A gehalten: voll aufgeladen ({charge:0.00}) ohne abzurutschen");
                    }
                    Log("Wand zu Wand:" + chainLog);
                    yield return WaitFor(() => a.skater.State == SkaterState.Riding || a.skater.State == SkaterState.Bailed, 4f);
                    Check(maxChain >= 3 && a.skater.State == SkaterState.Riding, $"Wand zu Wand gesprungen ({maxChain} Wallplants hintereinander, danach {a.skater.State})");
                    a.skater.InjectInput(Vector2.zero, false);
                    yield return Frames(10);
                }
            }

            // 7i. Easter Egg: Engelsfluegel schweben in der Stadt, durchfahren = aufheben, danach am Ruecken
            {
                var wp = AngelWingsPickup.Instance;
                Check(wp != null, "Engelsfluegel-Pickup in der Stadt");
                if (wp != null)
                {
                    AngelWingsPickup.ResetForTesting();
                    yield return Frames(3);
                    var vis = wp.transform.Find("Visual");
                    Check(vis != null && vis.gameObject.activeInHierarchy && WingsOnBack.Find(a.skater) == null, "Fluegel schweben, Spieler hat noch keine");
                    if (vis != null)
                    {
                        Vector3 y0 = vis.position;
                        yield return WaitFor(() => false, 0.6f);
                        var fc = Camera.main;
                        if (fc != null)
                        {
                            fc.transform.position = wp.transform.position + new Vector3(2.6f, 0.4f, -2.6f);
                            fc.transform.LookAt(wp.transform.position);
                        }
                        Capture("Logs/play_wings_float.png");
                        Check(Vector3.Distance(y0, vis.position) > 0.01f, "Fluegel wippen/drehen sich");
                    }
                    // Anfahren und durchfahren
                    Vector3 target = wp.transform.position;
                    Vector3 from = target + Vector3.back * 7f;
                    if (Physics.Raycast(from + Vector3.up * 3f, Vector3.down, out RaycastHit fg, 8f, ~LayerMask.GetMask("Rail", "Skater", "Car"))) from = fg.point;
                    a.skater.Place(from + Vector3.up * 0.1f, 0f);
                    yield return Frames(5);
                    float tw = Time.time;
                    while (Time.time - tw < 3f && !wp.Taken) { a.skater.InjectInput(new Vector2(0, 1), false); yield return null; }
                    a.skater.InjectInput(Vector2.zero, false);
                    Check(wp.Taken && SaveSystem.Profile.angelWings, $"Engelsfluegel aufgehoben ({(wp.Taken ? "ja" : "nein")}, Abstand zuletzt {Vector3.Distance(a.skater.transform.position + Vector3.up * 0.9f, target):0.0} m)");
                    yield return WaitFor(() => WingsOnBack.Find(a.skater) != null, 2f);
                    yield return WaitFor(() => false, 0.4f);
                    var onBack = WingsOnBack.Find(a.skater);
                    var chest = a.skater.rig != null ? a.skater.rig.Chest : null;
                    float d = onBack != null && chest != null ? Vector3.Distance(onBack.transform.position, chest.position) : -1f;
                    Check(onBack != null && a.HasWings && d >= 0f && d < 0.4f && (vis == null || !vis.gameObject.activeInHierarchy),
                          $"Fluegel sitzen am Ruecken ({d:0.00} m vom Brustknochen), Pickup weg");
                    var bc = Camera.main;
                    if (bc != null && onBack != null)
                    {
                        Vector3 sp = a.skater.transform.position;
                        Vector3 face = a.skater.rig != null ? a.skater.rig.frame.forward : a.skater.transform.forward;
                        face = Vector3.ProjectOnPlane(face, Vector3.up).normalized;
                        bc.transform.position = sp - face * 2.6f + Vector3.Cross(Vector3.up, face) * 1.2f + Vector3.up * 1.7f;
                        bc.transform.LookAt(sp + Vector3.up * 1.2f);
                        Capture("Logs/play_wings_back.png");
                        bc.transform.position = sp + face * 2.4f + Vector3.up * 1.5f;
                        bc.transform.LookAt(sp + Vector3.up * 1.1f);
                        Capture("Logs/play_wings_front.png");
                    }
                    // Im Auto unsichtbar
                    if (a.TestInteract() || a.Mode == PlayerMode.Driving)
                    {
                        yield return Frames(5);
                        var rends = onBack != null ? onBack.GetComponentsInChildren<Renderer>() : new Renderer[0];
                        Check(a.Mode == PlayerMode.Driving && System.Array.TrueForAll(rends, r => !r.enabled), "Fluegel im Auto ausgeblendet");
                        a.TestInteract();
                        yield return Frames(5);
                    }
                }
            }

            // 7h. Wallride in der Seitengasse: schraeg auf die Wand zu, Ollie, Grind EINMAL antippen, an der Wand entlang.
            // Versuch 1 endet mit Wallie (Ollie), Versuch 2 laesst Grind los. Mit Reaktionszeit wie ein Mensch.
            if (AlleyCrew.Found)
            {
                var al = AlleyCrew.Current;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    bool wallie = attempt == 0;
                    bool scored = false, bailed = false;
                    System.Action<string, int> onAction = (name, _) => { if (name == "WALLRIDE") scored = true; };
                    System.Action<string> onBail = _ => bailed = true;
                    a.combo.ActionAdded += onAction;
                    a.skater.BailedEvent += onBail;
                    Vector3 wallN = al.across; // Wand auf der -across-Seite, Normale zeigt in die Gasse
                    Vector3 start = al.entrance + al.inward * 12.5f - al.across * (al.halfWidth - 1.7f);
                    if (Physics.Raycast(start + Vector3.up * 2f, Vector3.down, out RaycastHit sg, 5f, ~LayerMask.GetMask("Rail", "Skater"))) start = sg.point;
                    Vector3 runDir = (Quaternion.AngleAxis(Vector3.SignedAngle(al.inward, -al.across, Vector3.up) > 0 ? 13f : -13f, Vector3.up) * al.inward).normalized;
                    a.skater.Place(start + Vector3.up * 0.1f, Mathf.Atan2(runDir.x, runDir.z) * Mathf.Rad2Deg);
                    yield return Frames(5);
                    float t4 = Time.time;
                    while (Time.time - t4 < 1.0f) { a.skater.InjectInput(new Vector2(0, 1), false); yield return null; }
                    t4 = Time.time;
                    while (Time.time - t4 < 0.2f) { a.skater.InjectInput(new Vector2(0, 1), true); yield return null; }
                    a.skater.InjectInput(Vector2.zero, false);
                    yield return WaitFor(() => false, 0.12f); // Reaktion: Grind erst in der Luft antippen
                    a.skater.InjectInput(Vector2.zero, false, grind: true);
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                    a.skater.InjectInput(Vector2.zero, false); // losgelassen, nicht gehalten
                    yield return WaitFor(() => a.skater.State == SkaterState.WallRide || a.skater.State == SkaterState.Bailed || a.skater.State == SkaterState.Riding, 1.3f);
                    bool rode = a.skater.State == SkaterState.WallRide;
                    Check(rode && scored, $"Wallride {attempt + 1}: an der Wand ({a.skater.State}, {a.skater.Speed * 3.6f:0} km/h)");
                    if (rode)
                    {
                        float y0 = a.skater.transform.position.y, minWall = 99f, maxWall = 0f;
                        Vector3 p0 = a.skater.transform.position;
                        float tr = Time.time;
                        bool shot = false;
                        while (Time.time - tr < (wallie ? 0.65f : 0.5f) && a.skater.State == SkaterState.WallRide)
                        {
                            a.skater.InjectInput(Vector2.zero, false); // ohne Grind zu halten
                            if (Physics.Raycast(a.skater.transform.position + Vector3.up * 0.9f, -a.skater.RideNormal, out RaycastHit wh, 3f, ~LayerMask.GetMask("Skater", "Rail", "Car")))
                            { minWall = Mathf.Min(minWall, wh.distance); maxWall = Mathf.Max(maxWall, wh.distance); }
                            if (!shot && Time.time - tr > 0.3f && wallie)
                            {
                                shot = true;
                                var rc = Camera.main;
                                if (rc != null)
                                {
                                    Vector3 sp = a.skater.transform.position;
                                    rc.transform.position = sp + a.skater.RideNormal * 3.6f - a.skater.transform.forward * 1.5f + Vector3.up * 1.3f;
                                    rc.transform.LookAt(sp + Vector3.up * 0.8f);
                                }
                                Capture("Logs/play_wallride.png");
                                var bp = a.skater.boardPivot;
                                float boardAngle = Vector3.Angle(bp.up, a.skater.RideNormal), bodyAngle = Vector3.Angle(a.skater.align.up, Vector3.up);
                                Log($"Wallride-Pose: Board-Deck zur Wand {boardAngle:0} Grad (0 = flach an der Wand), Koerper gekippt {bodyAngle:0} Grad, Board-Kinder: {bp.childCount} ({(bp.childCount > 0 ? bp.GetChild(0).name : "-")})");
                                Check(boardAngle < 20f, $"Board liegt flach an der Wand ({boardAngle:0} Grad)");
                            }
                            yield return null;
                        }
                        float along = Vector3.Distance(Vector3.ProjectOnPlane(a.skater.transform.position - p0, Vector3.up), Vector3.zero);
                        Check(a.skater.State == SkaterState.WallRide && along > 1.5f && maxWall - minWall < 0.25f,
                              $"Faehrt an der Wand entlang ({along:0.0} m, Wandabstand {minWall:0.00}-{maxWall:0.00} m, Hoehe {a.skater.transform.position.y - y0:+0.00;-0.00} m)");
                        if (wallie)
                        {
                            a.skater.InjectInput(Vector2.zero, true);
                            yield return new WaitForFixedUpdate();
                            yield return new WaitForFixedUpdate();
                            a.skater.InjectInput(Vector2.zero, false);
                            Vector3 v = a.skater.GetComponent<Rigidbody>().linearVelocity;
                            Check(a.skater.State == SkaterState.Air && Vector3.Dot(v, wallN) > 2f && v.y > 3f, $"Wallie: von der Wand abgesprungen (weg {Vector3.Dot(v, wallN):0.0} m/s, hoch {v.y:0.0} m/s)");
                            yield return WaitFor(() => false, 0.2f);
                            Capture("Logs/play_wallie.png");
                        }
                        else
                        {
                            a.skater.InjectInput(Vector2.zero, false, grind: true); // nochmal antippen = loslassen
                            yield return new WaitForFixedUpdate();
                            yield return new WaitForFixedUpdate();
                            a.skater.InjectInput(Vector2.zero, false);
                            Check(a.skater.State == SkaterState.Air || a.skater.State == SkaterState.Riding, $"Grind nochmal angetippt: von der Wand ({a.skater.State})");
                        }
                        yield return WaitFor(() => a.skater.State == SkaterState.Riding || a.skater.State == SkaterState.Bailed, 4f);
                        Check(a.skater.State == SkaterState.Riding && !bailed, $"Nach dem Wallride sauber gelandet ({a.skater.State})");
                    }
                    a.combo.ActionAdded -= onAction;
                    a.skater.BailedEvent -= onBail;
                    a.skater.InjectInput(Vector2.zero, false);
                    yield return WaitFor(() => a.skater.State == SkaterState.Riding, 3f);
                    yield return Frames(10);
                }
            }

            // 7j. Doppelsprung mit den Engelsfluegeln: erst ein normaler Ollie (Hoehe merken), dann einer mit zweitem Druck
            // kurz vor dem hoechsten Punkt (Reaktionszeit), ein dritter Druck darf nichts mehr tun. Dazu zu Fuss.
            {
                a.SetWings(true);
                yield return Frames(3);
                var wings = WingsOnBack.Find(a.skater);
                Check(a.skater.canDoubleJump && wings != null, "Mit Fluegeln ist der Doppelsprung freigeschaltet");
                float[] peaks = new float[2];
                float tipMin = 99f, tipMax = -99f;
                bool scoredDj = false;
                System.Action<string, int> onDj = (name, _) => { if (name == "DOPPELSPRUNG") scoredDj = true; };
                a.combo.ActionAdded += onDj;
                for (int run = 0; run < 2; run++)
                {
                    a.skater.Place(SkateTestStart, 90f);
                    yield return Frames(5);
                    float t5 = Time.time;
                    while (Time.time - t5 < 0.8f) { a.skater.InjectInput(new Vector2(0, 1), false); yield return null; }
                    t5 = Time.time;
                    while (Time.time - t5 < 0.3f) { a.skater.InjectInput(Vector2.zero, true); yield return null; }
                    a.skater.InjectInput(Vector2.zero, false);
                    float djGround = a.skater.transform.position.y, djPeak = djGround, ta = Time.time;
                    bool pressed = false, third = false;
                    float vyAfter = 0f;
                    while (Time.time - ta < 3f && (a.skater.State == SkaterState.Air || Time.time - ta < 0.1f))
                    {
                        djPeak = Mathf.Max(djPeak, a.skater.transform.position.y);
                        float vy = a.skater.GetComponent<Rigidbody>().linearVelocity.y;
                        if (run == 1 && !pressed && vy < 1.2f && Time.time - ta > 0.25f)
                        {
                            pressed = true;
                            a.skater.InjectInput(Vector2.zero, true);
                            yield return new WaitForFixedUpdate();
                            yield return new WaitForFixedUpdate();
                            a.skater.InjectInput(Vector2.zero, false);
                            vyAfter = a.skater.GetComponent<Rigidbody>().linearVelocity.y;
                            yield return WaitFor(() => false, 0.12f);
                            Capture("Logs/play_doublejump.png");
                            continue;
                        }
                        if (run == 1 && pressed && !third && Time.time - ta > 0.9f && a.skater.State == SkaterState.Air)
                        {
                            third = true;
                            float before = a.skater.GetComponent<Rigidbody>().linearVelocity.y;
                            a.skater.InjectInput(Vector2.zero, true);
                            yield return new WaitForFixedUpdate();
                            yield return new WaitForFixedUpdate();
                            a.skater.InjectInput(Vector2.zero, false);
                            Check(a.skater.GetComponent<Rigidbody>().linearVelocity.y < before + 0.5f, "Nur ein Doppelsprung pro Sprung");
                        }
                        if (wings != null && wings.Flapper != null) { tipMin = Mathf.Min(tipMin, wings.Flapper.TipDepth); tipMax = Mathf.Max(tipMax, wings.Flapper.TipDepth); }
                        yield return null;
                    }
                    peaks[run] = djPeak - djGround;
                    if (run == 1) Check(pressed && vyAfter > 5.5f, $"Doppelsprung ausgeloest (hoch {vyAfter:0.0} m/s)");
                    yield return WaitFor(() => a.skater.State == SkaterState.Riding || a.skater.State == SkaterState.Bailed, 3f);
                    yield return Frames(10);
                }
                a.combo.ActionAdded -= onDj;
                Check(peaks[1] > peaks[0] + 1f && scoredDj && a.skater.State == SkaterState.Riding,
                      $"Doppelsprung: {peaks[0]:0.00} m normal, {peaks[1]:0.00} m mit Fluegeln, sauber gelandet ({a.skater.State})");
                Check(tipMax - tipMin > 0.15f, $"Fluegel schlagen vor und zurueck (Spitze {tipMin:0.00} bis {tipMax:0.00} m in Blickrichtung)");

                // Zu Fuss: Sprung, dann nochmal
                a.skater.InjectInput(Vector2.zero, false);
                a.skater.InjectBoardToggle();
                for (int k = 0; k < 4; k++) yield return new WaitForFixedUpdate();
                yield return Frames(5);
                if (a.skater.State == SkaterState.Walking)
                {
                    float gy = a.skater.transform.position.y, pk = gy;
                    a.skater.InjectInput(Vector2.zero, true);
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                    a.skater.InjectInput(Vector2.zero, false);
                    float tw2 = Time.time;
                    bool second = false;
                    while (Time.time - tw2 < 2f)
                    {
                        pk = Mathf.Max(pk, a.skater.transform.position.y);
                        if (!second && Time.time - tw2 > 0.3f)
                        {
                            second = true;
                            a.skater.InjectInput(Vector2.zero, true);
                            yield return new WaitForFixedUpdate();
                            yield return new WaitForFixedUpdate();
                            a.skater.InjectInput(Vector2.zero, false);
                        }
                        if (second && a.skater.transform.position.y < gy + 0.05f && Time.time - tw2 > 0.6f) break;
                        yield return null;
                    }
                    Check(pk - gy > 1.2f && a.skater.DoubleJumpUsed == false, $"Doppelsprung zu Fuss ({pk - gy:0.00} m hoch, normal ca. 0,8 m)");
                }
                else Check(false, "Absteigen fuer den Doppelsprung zu Fuss");
                a.skater.InjectBoardToggle();
                for (int k = 0; k < 4; k++) yield return new WaitForFixedUpdate();
                yield return Frames(5);
            }

            // 7k. Locker runterrollen: von einer 18 cm hohen Kante (wie ein Bordstein) fahren. Der Skater soll mit der
            // Schwerkraft einen kurzen Bogen fliegen, nicht wie von einem Magneten sofort nach unten gerissen werden.
            {
                var ledge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ledge.name = "TestKante";
                float gy = 0f;
                if (Physics.Raycast(SkateTestStart + Vector3.up * 3f, Vector3.down, out RaycastHit lg, 6f, ~LayerMask.GetMask("Rail", "Skater", "Car"))) gy = lg.point.y;
                ledge.transform.position = new Vector3(SkateTestStart.x - 4f, gy + 0.09f, SkateTestStart.z);
                ledge.transform.localScale = new Vector3(10f, 0.18f, 3f);
                Physics.SyncTransforms();
                float edgeX = SkateTestStart.x + 1f;
                a.skater.Place(new Vector3(SkateTestStart.x - 8f, gy + 0.3f, SkateTestStart.z), 90f);
                yield return Frames(5);
                float t6 = Time.time;
                while (Time.time - t6 < 3f && a.skater.transform.position.x < edgeX - 0.3f) { a.skater.InjectInput(new Vector2(0, 1), false); yield return new WaitForFixedUpdate(); }
                a.skater.InjectInput(Vector2.zero, false);
                float speedBefore = a.skater.Speed, tEdge = -1f, tDown = -1f, vyEarly = 0f;
                var rbk = a.skater.GetComponent<Rigidbody>();
                bool wasAir = false;
                for (int i = 0; i < 200; i++)
                {
                    yield return new WaitForFixedUpdate();
                    float x = a.skater.transform.position.x, y = a.skater.transform.position.y;
                    if (tEdge < 0f && x > edgeX) tEdge = Time.time;
                    if (tEdge >= 0f && Time.time - tEdge <= 0.05f) vyEarly = Mathf.Min(vyEarly, rbk.linearVelocity.y);
                    if (a.skater.State == SkaterState.Air) wasAir = true;
                    if (tEdge >= 0f && tDown < 0f && y < gy + 0.03f) tDown = Time.time;
                    if (tEdge >= 0f && (i % 3 == 0) && Time.time - tEdge < 0.6f)
                        Log($"   t+{Time.time - tEdge:0.00}: {a.skater.State}, {a.skater.Speed * 3.6f:0} km/h, v {rbk.linearVelocity}, y {y - gy:0.00}");
                    if (tDown >= 0f && Time.time - tDown > 0.5f) break;
                }
                float fall = tDown > 0f && tEdge > 0f ? tDown - tEdge : -1f;
                Log($"Kante runter: {speedBefore * 3.6f:0} km/h, Fall {fall:0.00} s (frei wie Schwerkraft ca. 0,16 s), Anfangs-Fallgeschwindigkeit {vyEarly:0.0} m/s, kurz in der Luft {wasAir}, danach {a.skater.Speed * 3.6f:0} km/h");
                Check(fall > 0.1f && vyEarly > -1.6f && a.skater.State == SkaterState.Riding && a.skater.Speed > speedBefore * 0.8f,
                      $"Locker von der Kante gerollt (Fall {fall:0.00} s, Anfang {vyEarly:0.0} m/s, Tempo gehalten)");
                Destroy(ledge);
                yield return Frames(5);
            }

            // 7d. Ollie (A / Leertaste) gehalten: geduckt Tempo aufbauen wie in Tony Hawk's, ohne zu pushen
            {
                a.skater.Place(SkateTestStart, 90f);
                yield return Frames(5);
                float t1 = Time.time;
                float crouchSpeed = 0f;
                string speeds = "";
                while (Time.time - t1 < 1.2f)
                {
                    a.skater.InjectInput(Vector2.zero, true);
                    if (crouchSpeed < 0.0001f || (int)((Time.time - t1) / 0.3f) != (int)((Time.time - t1 - Time.deltaTime) / 0.3f)) speeds += $" {Time.time - t1:0.0}s:{a.skater.Speed:0.0}";
                    crouchSpeed = a.skater.Speed;
                    yield return null;
                }
                Log($"Geduckt (Board-Tempo x{a.skater.board.speed:0.00}):{speeds}");
                Check(a.skater.State == SkaterState.Riding && crouchSpeed > 3f && a.skater.PushPhase < 0f,
                      $"Ollie gehalten: geduckt beschleunigt auf {crouchSpeed * 3.6f:0} km/h (ohne Pushen)");
                a.skater.InjectInput(Vector2.zero, false); // loslassen = Ollie
                yield return WaitFor(() => a.skater.State == SkaterState.Riding, 3f);
                yield return Frames(10);
            }

            // 7e. Zu Fuss an eine beliebige Wand spruehen: naechste senkrechte Wand vom Platz aus suchen
            {
                Vector3 from = SkateTestStart + Vector3.up * 1.35f;
                RaycastHit wall = default;
                bool found = false;
                for (int k = 0; k < 36 && !found; k++)
                {
                    Vector3 dir = Quaternion.Euler(0f, k * 10f, 0f) * Vector3.forward;
                    if (Physics.Raycast(from, dir, out wall, 120f, ~LayerMask.GetMask("Skater", "Car", "Rail", "Ignore Raycast"), QueryTriggerInteraction.Ignore)
                        && Mathf.Abs(wall.normal.y) < 0.2f && wall.rigidbody == null
                        && wall.collider.bounds.size.y > 3f && Mathf.Max(wall.collider.bounds.size.x, wall.collider.bounds.size.z) > 4f) found = true;
                }
                Check(found, "Wand zum Taggen gefunden");
                if (found)
                {
                    Vector3 n = Vector3.ProjectOnPlane(wall.normal, Vector3.up).normalized;
                    Vector3 stand = wall.point + n * 1.3f;
                    if (Physics.Raycast(stand + Vector3.up * 2f, Vector3.down, out RaycastHit g, 6f, ~LayerMask.GetMask("Rail", "Skater"))) stand = g.point;
                    a.skater.Place(stand + Vector3.up * 0.1f, Mathf.Atan2(-n.x, -n.z) * Mathf.Rad2Deg);
                    yield return Frames(5);
                    a.skater.InjectInput(Vector2.zero, false);
                    a.skater.InjectBoardToggle();
                    for (int k = 0; k < 4; k++) yield return new WaitForFixedUpdate();
                    yield return Frames(10);
                    bool sprayedRiding = false;
                    int before = GameObject.Find("WallTags") != null ? GameObject.Find("WallTags").transform.childCount : 0;
                    bool sprayed = a.skater.State == SkaterState.Walking && a.TrySpray();
                    int after = GameObject.Find("WallTags") != null ? GameObject.Find("WallTags").transform.childCount : 0;
                    Check(sprayed && after == before + 1, $"Zu Fuss an die Wand gesprueht ({after - before} Tag, Zustand {a.skater.State}, Wand {wall.collider.name} {wall.distance:0} m)" + (sprayed ? "" : " - " + WallTags.LastReason));
                    yield return WaitFor(() => false, 0.6f);
                    var wcam = Camera.main;
                    if (wcam != null)
                    {
                        wcam.transform.position = wall.point + n * 3.4f + Vector3.Cross(Vector3.up, n) * 2.2f + Vector3.up * 0.5f;
                        wcam.transform.LookAt(wall.point + Vector3.up * 0.2f);
                    }
                    Capture("Logs/play_walltag.png");
                    // Auf dem Board geht freies Taggen nicht (nur an Spots)
                    a.skater.InjectBoardToggle();
                    for (int k = 0; k < 4; k++) yield return new WaitForFixedUpdate();
                    if (a.skater.State == SkaterState.Riding) sprayedRiding = a.TrySpray();
                    Check(a.skater.State == SkaterState.Riding && !sprayedRiding, "Auf dem Board kein freies Taggen");

                    // 7f. Wallplant: Anlauf auf die Wand, Ollie, an der Wand abfangen, nach kurzer Reaktionszeit mit Ollie abspringen.
                    // Zweiter Versuch ohne Abspringen: man kippt von der Wand weg und landet normal.
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        bool jumpOff = attempt == 0;
                        bool planted = false, scored = false, bailed = false;
                        System.Action<string, int> onAction = (name, _) => { if (name == "WALLPLANT") scored = true; };
                        System.Action<string> onBail = _ => bailed = true;
                        a.combo.ActionAdded += onAction;
                        a.skater.BailedEvent += onBail;
                        Vector3 run = wall.point + n * 11f;
                        if (Physics.Raycast(run + Vector3.up * 2f, Vector3.down, out RaycastHit rg, 6f, ~LayerMask.GetMask("Rail", "Skater"))) run = rg.point;
                        a.skater.Place(run + Vector3.up * 0.1f, Mathf.Atan2(-n.x, -n.z) * Mathf.Rad2Deg);
                        yield return Frames(5);
                        float t2 = Time.time;
                        // anschieben bis 5.5 m (zweiter Versuch: 3.5 m, Plant knapp ueber dem Boden) vor der Wand, dann Ollie (kurz halten, loslassen), Stick weiter nach vorn
                        while (Time.time - t2 < 4f && a.skater.State == SkaterState.Riding && Vector3.Dot(a.skater.transform.position - wall.point, n) > (jumpOff ? 5.6f : 3.6f))
                        { a.skater.InjectInput(new Vector2(0, 1), false); yield return null; }
                        float approach = a.skater.Speed;
                        t2 = Time.time;
                        while (Time.time - t2 < 0.25f) { a.skater.InjectInput(new Vector2(0, 1), true); yield return null; }
                        a.skater.InjectInput(new Vector2(0, 1), false);
                        // Mitschreiben, was zwischen Absprung und Wand passiert
                        var seen = new System.Text.StringBuilder();
                        float minWall = 99f, tPlant = Time.time;
                        SkaterState lastState = a.skater.State;
                        while (Time.time - tPlant < 2.5f && a.skater.State != SkaterState.WallPlant)
                        {
                            float dWall = Vector3.Dot(a.skater.transform.position - wall.point, n);
                            minWall = Mathf.Min(minWall, dWall);
                            if (a.skater.State != lastState) { seen.Append($" {lastState}->{a.skater.State}@{Time.time - tPlant:0.00}s/{dWall:0.00}m/h{a.skater.transform.position.y:0.00}"); lastState = a.skater.State; }
                            yield return null;
                        }
                        Log($"Wallplant-Anflug: {seen}, naechster Wandabstand {minWall:0.00} m, Tempo jetzt {a.skater.Speed:0.0} m/s");
                        planted = a.skater.State == SkaterState.WallPlant;
                        Check(planted && scored, $"Wallplant {(jumpOff ? "1" : "2")}: an der Wand abgefangen (Anlauf {approach * 3.6f:0} km/h, Zustand {a.skater.State})");
                        if (planted)
                        {
                            yield return WaitFor(() => false, 0.1f);
                            if (jumpOff)
                            {
                                var pcam = Camera.main;
                                if (pcam != null)
                                {
                                    pcam.transform.position = a.skater.transform.position + n * 3.2f + Vector3.Cross(Vector3.up, n) * 2.6f + Vector3.up * 1.2f;
                                    pcam.transform.LookAt(a.skater.transform.position + Vector3.up * 0.9f);
                                }
                                Capture("Logs/play_wallplant.png");
                                yield return WaitFor(() => false, 0.2f); // Reaktionszeit
                                Check(a.skater.State == SkaterState.WallPlant, $"Haengt noch an der Wand nach {a.skater.PlantTime:0.00} s");
                                // Antippen: druecken, kurz halten, loslassen = Absprung
                                a.skater.InjectInput(Vector2.zero, true);
                                yield return WaitFor(() => false, 0.12f);
                                a.skater.InjectInput(Vector2.zero, false);
                                yield return new WaitForFixedUpdate();
                                yield return new WaitForFixedUpdate();
                                Vector3 v = a.skater.GetComponent<Rigidbody>().linearVelocity;
                                Check(a.skater.State == SkaterState.Air && Vector3.Dot(v, n) > 3f && v.y > 3f, $"Von der Wand abgesprungen (weg {Vector3.Dot(v, n):0.0} m/s, hoch {v.y:0.0} m/s)");
                                yield return WaitFor(() => false, 0.25f);
                                Capture("Logs/play_wallplant_jump.png");
                            }
                            else
                            {
                                yield return WaitFor(() => a.skater.State != SkaterState.WallPlant, 2f);
                                Check(a.skater.State == SkaterState.Air || a.skater.State == SkaterState.Riding, $"Ohne Abspringen von der Wand gekippt ({a.skater.State})");
                            }
                            yield return WaitFor(() => a.skater.State == SkaterState.Riding || a.skater.State == SkaterState.Bailed, 4f);
                            Check(a.skater.State == SkaterState.Riding && !bailed, $"Nach dem Wallplant sauber gelandet ({a.skater.State})");
                        }
                        a.combo.ActionAdded -= onAction;
                        a.skater.BailedEvent -= onBail;
                        a.skater.InjectInput(Vector2.zero, false);
                        yield return WaitFor(() => a.skater.State == SkaterState.Riding, 3f);
                        yield return Frames(10);
                    }
                }
            }

            // 7c. Graffiti spruehen
            yield return WaitFor(() => a.skater.State == SkaterState.Riding, 3f);
            TagSpot spot = TagSpot.ByIndex(0);
            if (spot != null)
            {
                Vector3 p = spot.transform.position + spot.transform.forward * 1.6f;
                if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit th, 8f, ~LayerMask.GetMask("Rail", "Skater"))) p = th.point;
                a.skater.Place(p, spot.transform.eulerAngles.y);
                yield return Frames(5);
                Check(a.TrySpray() && spot.TaggedByMe, "Graffiti gesprueht (" + spot.name + ")");
                Capture("Logs/play_tag.png");
            }

            // 7l. NPC-Verkehr und Skitchen: neben ein fahrendes NPC-Auto, F = dranhaengen, mitfahren,
            //     Ollie halten und loslassen = Slingshot, sauber landen
            var traffic = Traffic.Instance;
            Check(traffic != null && traffic.cars.Count >= 6, $"NPC-Verkehr faehrt ({(traffic != null ? traffic.cars.Count : 0)} Autos)");
            if (traffic != null && traffic.cars.Count > 0)
            {
                var npc = traffic.cars[0];
                var hitchable = npc.GetComponent<Hitchable>();
                Vector3 npcStart = npc.transform.position;
                yield return WaitFor(() => npc.Speed > 6f, 10f);
                yield return new WaitForSeconds(1f);
                Check(Vector3.Distance(npcStart, npc.transform.position) > 5f, $"NPC-Auto faehrt ({npc.Speed * 3.6f:0} km/h)");
                a.skater.InjectInput(Vector2.zero, false);
                a.skater.Place(hitchable.StandPoint(Hitchable.Left) + Vector3.up * 0.05f, npc.transform.eulerAngles.y);
                yield return Frames(2);
                bool hitched = a.TestHitch();
                Vector3 hitchStart = a.skater.transform.position;
                yield return new WaitForSeconds(2.5f);
                float off = a.skater.HitchTarget != null
                    ? Vector3.Distance(a.skater.transform.position, a.skater.HitchTarget.StandPoint(a.skater.HitchAnchor)) : 99f;
                float rode = Vector3.Distance(hitchStart, a.skater.transform.position);
                Check(hitched && a.skater.State == SkaterState.Hitched && off < 0.3f && rode > 12f,
                      $"Am NPC-Auto drangehaengt (Haltepunkt {a.skater.HitchAnchor}, {rode:0} m mitgefahren, Abweichung {off:0.00} m, Combo {a.combo.Total})");
                Capture("Logs/play_skitch.png");
                float carSpeed = npc.Speed;
                a.skater.InjectInput(Vector2.zero, true);
                yield return new WaitForSeconds(0.45f);
                a.skater.InjectInput(Vector2.zero, false);
                yield return Frames(3);
                Check(a.skater.State == SkaterState.Air && a.skater.Speed > carSpeed * 0.8f,
                      $"Slingshot vom Auto ({a.skater.Speed * 3.6f:0} km/h, Auto {carSpeed * 3.6f:0} km/h)");
                Capture("Logs/play_skitch_slingshot.png");
                yield return WaitFor(() => a.skater.State != SkaterState.Air, 4f);
                Check(a.skater.State == SkaterState.Riding, "Nach dem Slingshot gelandet (" + a.skater.State + ")");
                yield return Frames(10);
            }

            // 8. Auto rufen und einsteigen
            yield return new WaitForSeconds(2.5f);
            Log($"Abstand zum Auto: {a.DistanceToCar:0.0} m");
            if (a.DistanceToCar > 3.5f) a.TestCallCar();
            yield return Frames(10);
            Log($"Nach Auto rufen: Abstand {a.DistanceToCar:0.0} m");
            if (a.DistanceToCar > 3.5f)
            {
                // Zum Auto rollen lassen: Skater direkt daneben absetzen
                a.skater.Place(a.car.transform.TransformPoint(new Vector3(2f, 0.1f, 0f)), a.skater.Heading);
                yield return Frames(5);
            }
            bool entered = a.TestInteract();
            yield return Frames(5);
            Check(entered && a.Mode == PlayerMode.Driving, "Wieder eingestiegen");

            // 9. Im Stand normal aussteigen und wieder einsteigen
            yield return Drive(a, 2f, t => new VehicleInput { handbrake = true });
            Log($"Vor dem Aussteigen: {a.car.SpeedKmh:0.0} km/h");
            a.TestInteract();
            yield return Frames(5);
            Check(a.Mode == PlayerMode.Skating && a.skater.State == SkaterState.Riding, "Im Stand ausgestiegen");
            Capture("Logs/play_exit.png");
            for (int i = 0; i < CameraRig.Views.Length; i++)
            {
                yield return new WaitForSeconds(1.2f);
                Capture("Logs/play_cam_skate_" + CameraRig.Views[CameraRig.Instance.View].ToLower() + ".png");
                CameraRig.Instance.NextView();
            }

            yield return Frames(10);
            Check(a.TestInteract() && a.Mode == PlayerMode.Driving, "Wieder eingestiegen (2)");

            // 10. Stoner-NPCs am Platz: Animationen laufen, sie lachen ueber Tricks, Jojo raucht
            Check(StonerNpc.All.Count == 6, $"Stoner-NPCs, Gassen-Crew und Miru aufgestellt ({StonerNpc.All.Count})");
            var jojo = StonerNpc.All.Find(n => n.Id == "Jojo");
            if (jojo != null)
            {
                Vector3 fwd = StonerNpc.HangoutFacing;
                a.car.Teleport(StonerNpc.HangoutPosition + fwd * 7f + Vector3.up * 0.6f, Quaternion.LookRotation(-fwd));
                yield return Drive(a, 1.5f, t => new VehicleInput { handbrake = true });
                var hand = jojo.Animator.GetBoneTransform(HumanBodyBones.RightHand);
                Vector3 handBefore = hand.position;
                jojo.Play("Smoke");
                yield return Drive(a, 1.3f, t => new VehicleInput { handbrake = true });
                Check(Vector3.Distance(hand.position, handBefore) > 0.15f, $"Jojo zieht am Joint (Hand {Vector3.Distance(hand.position, handBefore):0.00} m bewegt)");
                var npcCam = Camera.main;
                Vector3 jojoHead = jojo.Head.position;
                npcCam.transform.position = jojoHead + jojo.transform.forward * 1.6f + jojo.transform.right * 0.4f;
                npcCam.transform.LookAt(jojoHead + Vector3.down * 0.2f);
                Capture("Logs/play_npc_smoke.png");
                yield return Drive(a, 1.6f, t => new VehicleInput { handbrake = true });
                Check(jojo.SmokeParticles > 0, $"Rauch sichtbar ({jojo.SmokeParticles} Partikel)");
                jojoHead = jojo.Head.position;
                npcCam.transform.position = jojoHead + jojo.transform.forward * 1.6f + jojo.transform.right * 0.4f;
                npcCam.transform.LookAt(jojoHead + Vector3.down * 0.2f);
                Capture("Logs/play_npc_exhale.png");
                // Trick in der Naehe -> alle lachen (mit kurzer Reaktionszeit)
                yield return Drive(a, 2.5f, t => new VehicleInput { handbrake = true });
                a.combo.AddAction("TEST-TRICK", 100f);
                yield return Drive(a, 1.0f, t => new VehicleInput { handbrake = true });
                int laughing = StonerNpc.All.FindAll(n => n.CurrentAction == "Laugh").Count;
                Check(laughing >= 2, $"NPCs lachen ueber den Trick ({laughing} von 3)");
                Vector3 c = StonerNpc.HangoutPosition;
                Vector3 side = Vector3.Cross(Vector3.up, fwd);
                npcCam.transform.position = c + fwd * 4.2f + side * 2.6f + Vector3.up * 1.7f;
                npcCam.transform.LookAt(c + Vector3.up * 0.9f);
                Capture("Logs/play_npc_group.png");
                yield return Drive(a, 3f, t => new VehicleInput { handbrake = true });
            }

            // 11. Jojos Auftraege: ansprechen, Sprechblasen, annehmen, erfuellen, Belohnung abholen
            var quests = JojoQuests.Instance;
            Check(quests != null, "Jojo vergibt Auftraege");
            if (quests != null && jojo != null)
            {
                SaveSystem.Profile.jojoQuest = 0;
                if (a.Mode == PlayerMode.Driving) a.TestInteract();
                yield return Frames(10);
                Vector3 jf = Vector3.ProjectOnPlane(jojo.transform.forward, Vector3.up).normalized;
                a.skater.Place(jojo.transform.position + jf * 2.2f + Vector3.up * 0.1f, Quaternion.LookRotation(-jf).eulerAngles.y);
                yield return Frames(15);
                Check(JojoQuests.Near(a.skater.transform.position) != null, "Nah genug, um Jojo anzusprechen");
                quests.Talk();
                yield return WaitFor(() => false, 1.8f);
                Capture("Logs/play_quest_bubble.png");
                for (int i = 0; i < 12 && quests.Talking && !quests.ChoiceOpen; i++) { quests.TestPress(true); yield return WaitFor(() => false, 0.3f); }
                yield return WaitFor(() => false, 3.5f);
                Capture("Logs/play_quest_choice.png");
                Check(quests.ChoiceOpen, "Auftrag wird angeboten (annehmen / ablehnen)");
                for (int i = 0; i < 4 && quests.Talking; i++) { quests.TestPress(true); yield return Frames(3); }
                yield return Frames(3);
                Check(quests.State == JojoQuests.Phase.Active && !quests.Talking && !GameInput.Blocked, "Auftrag angenommen, Steuerung wieder frei");
                a.combo.AddAction("TEST-TRICK", 3500f);
                yield return WaitFor(() => quests.State == JojoQuests.Phase.Done, 5f);
                Check(quests.State == JojoQuests.Phase.Done, "Auftrag erfuellt (Combo)");
                yield return WaitFor(() => false, 0.5f);
                Capture("Logs/play_quest_done.png");
                long before = SaveSystem.Profile.money;
                quests.Talk();
                for (int i = 0; i < 12 && quests.Talking; i++) { quests.TestPress(true); yield return WaitFor(() => false, 0.3f); }
                yield return WaitFor(() => false, 0.35f);
                Capture("Logs/play_quest_reward.png");
                Check(SaveSystem.Profile.money > before && SaveSystem.Profile.jojoQuest == 1, $"Belohnung abgeholt (+{SaveSystem.Profile.money - before})");
                // Naechster Auftrag (Snacks holen): Leuchtsaeule und Markierung
                quests.Talk();
                for (int i = 0; i < 12 && quests.Talking && !quests.ChoiceOpen; i++) { quests.TestPress(true); yield return WaitFor(() => false, 0.3f); }
                for (int i = 0; i < 4 && quests.Talking; i++) { quests.TestPress(true); yield return Frames(3); }
                yield return WaitFor(() => false, 1.5f);
                Check(quests.State == JojoQuests.Phase.Active && GameObject.Find("SnackBeacon") != null, "Snack-Auftrag mit Leuchtsaeule");
                Capture("Logs/play_quest_fetch.png");
            }

            // 12. Lunas Spielmodi: ansprechen, Drohnenflug zum Ziel, Countdown, Sprint ins Ziel
            var luna = LunaChallenges.Instance;
            var lunaNpc = StonerNpc.All.Find(n => n.Id == "Luna");
            Check(luna != null && lunaNpc != null, "Luna bietet Spielmodi an");
            if (luna != null && lunaNpc != null)
            {
                SaveSystem.Profile.lunaChallenge = 0;
                if (a.Mode == PlayerMode.Driving) a.TestInteract();
                yield return Frames(10);
                Vector3 lf = Vector3.ProjectOnPlane(lunaNpc.transform.forward, Vector3.up).normalized;
                a.skater.Place(lunaNpc.transform.position + lf * 2.2f + Vector3.up * 0.1f, Quaternion.LookRotation(-lf).eulerAngles.y);
                yield return Frames(15);
                Check(QuestNpc.Near(a.skater.transform.position) == luna, "Nah genug, um Luna anzusprechen");
                luna.Talk();
                for (int i = 0; i < 12 && luna.Talking && !luna.ChoiceOpen; i++) { luna.TestPress(true); yield return WaitFor(() => false, 0.3f); }
                yield return WaitFor(() => false, 3.5f);
                Capture("Logs/play_luna_dialog.png");
                for (int i = 0; i < 4 && luna.Talking; i++) { luna.TestPress(true); yield return Frames(3); }
                yield return WaitFor(() => false, 2.4f);
                Check(luna.State == LunaChallenges.Phase.Intro && a.Mode == PlayerMode.Driving, $"Drohnenflug laeuft, Spieler startklar im Auto ({luna.State}, {a.Mode})");
                Capture("Logs/play_luna_drone.png");
                yield return WaitFor(() => false, 2.8f);
                Capture("Logs/play_luna_drone_goal.png");
                yield return WaitFor(() => luna.State == LunaChallenges.Phase.Running, 10f);
                yield return Frames(3);
                Check(luna.State == LunaChallenges.Phase.Running && !GameInput.Blocked && luna.TimeLeft > 20f, $"Countdown vorbei, Sprint laeuft ({luna.TimeLeft:0.0} s)");
                Capture("Logs/play_luna_run.png");
                long beforeLuna = SaveSystem.Profile.money;
                Vector3 toGoal = Vector3.ProjectOnPlane(luna.Goal - a.car.transform.position, Vector3.up).normalized;
                a.car.Teleport(luna.Goal + Vector3.up * 0.6f, Quaternion.LookRotation(toGoal));
                yield return WaitFor(() => luna.State == LunaChallenges.Phase.Open, 3f);
                yield return WaitFor(() => false, 0.4f);
                Capture("Logs/play_luna_win.png");
                Check(luna.State == LunaChallenges.Phase.Open && SaveSystem.Profile.money > beforeLuna && SaveSystem.Profile.lunaChallenge == 1,
                      $"Sprint geschafft, Belohnung +{SaveSystem.Profile.money - beforeLuna}");
            }

            // 12b. Gassen-Crew: Nix verkauft eine Tuete (liegt beim Deal in seiner Hand), rauchen gibt Chill, Moe vergibt einen Liefer-Job
            var nix = NixDealer.Instance;
            var moe = MoeQuests.Instance;
            var nixNpc = StonerNpc.All.Find(n => n.Id == "Nix");
            var moeNpc = StonerNpc.All.Find(n => n.Id == "Moe");
            Check(AlleyCrew.Found && nix != null && moe != null && nixNpc != null && moeNpc != null, "Gassen-Crew in der Seitengasse (Nix, Moe)");
            if (nix != null && moe != null && nixNpc != null && moeNpc != null)
            {
                var prof = SaveSystem.Profile;
                prof.baggies = 0;
                prof.nixDeals = 0;
                prof.moeQuest = 0;
                prof.nixFriend = false;
                if (prof.money < 5000) prof.money = 5000;
                if (a.Mode == PlayerMode.Driving) a.TestInteract();
                yield return Frames(10);
                Vector3 nf = Vector3.ProjectOnPlane(nixNpc.transform.forward, Vector3.up).normalized;
                a.skater.Place(nixNpc.transform.position + nf * 2.2f + Vector3.up * 0.1f, Quaternion.LookRotation(-nf).eulerAngles.y);
                yield return Frames(15);
                Check(QuestNpc.Near(a.skater.transform.position) == nix, "Nah genug, um Nix anzusprechen");
                long m0 = prof.money;
                nix.Talk();
                for (int i = 0; i < 12 && nix.Talking && !nix.ChoiceOpen; i++) { nix.TestPress(true); yield return WaitFor(() => false, 0.3f); }
                yield return WaitFor(() => false, 3f);
                Capture("Logs/play_nix_dialog.png");
                Check(nix.ChoiceOpen, "Nix bietet eine Tuete an (kaufen / ablehnen)");
                for (int i = 0; i < 4 && nix.Talking; i++) { nix.TestPress(true); yield return Frames(3); }
                yield return Frames(3);
                Check(prof.baggies == 1 && prof.nixDeals == 1 && !GameInput.Blocked, $"Tuete gekauft (Geld {m0} -> {prof.money}, Steuerung frei)");
                yield return WaitFor(() => false, 1.3f);
                var bag = GameObject.Find("Baggie");
                var nixHand = nixNpc.Animator.GetBoneTransform(HumanBodyBones.RightHand);
                float bagDist = bag != null ? Vector3.Distance(bag.transform.position, nixHand.position) : -1f;
                Check(bag != null && bag.activeInHierarchy && bagDist < 0.2f && nixNpc.CurrentAction == "Deal", $"Tuete liegt beim Deal in Nix' Hand ({bagDist:0.00} m)");
                var dealCam = Camera.main;
                Vector3 nixHead = nixNpc.Head.position;
                dealCam.transform.position = nixHead + nixNpc.transform.forward * 1.7f - Vector3.Cross(Vector3.up, nixNpc.transform.forward) * 0.6f - Vector3.up * 0.2f;
                dealCam.transform.LookAt(nixHead + Vector3.down * 0.35f);
                Capture("Logs/play_nix_deal.png");
                // Rauchen: Chill an, Tuete weg, Combo-Fenster laeuft langsamer
                Check(Chill.Instance != null && Chill.Instance.Use() && Chill.Active && prof.baggies == 0 && Chill.ComboDrain < 1f && Chill.BalanceCalm < 1f,
                      $"Tuete geraucht: Chill {Chill.TimeLeft:0} s, Combo-Fenster x{1f / Chill.ComboDrain:0.0}");
                Check(!Chill.Instance.Use(), "Ohne Tuete kein Chill");
                yield return WaitFor(() => false, 3f);
                Capture("Logs/play_chill.png");

                // Moe: Job annehmen, Paket zum Hafen, zurueck, Belohnung + Tuete; danach Freundschaftspreis bei Nix
                Vector3 mf = Vector3.ProjectOnPlane(moeNpc.transform.forward, Vector3.up).normalized;
                a.skater.Place(moeNpc.transform.position + mf * 2.2f + Vector3.up * 0.1f, Quaternion.LookRotation(-mf).eulerAngles.y);
                yield return WaitFor(() => false, 3.5f); // Nix' Sprechblase ist weg, Deal fertig
                Check(QuestNpc.Near(a.skater.transform.position) == moe, "Nah genug, um Moe anzusprechen");
                moe.Talk();
                for (int i = 0; i < 12 && moe.Talking && !moe.ChoiceOpen; i++) { moe.TestPress(true); yield return WaitFor(() => false, 0.3f); }
                yield return WaitFor(() => false, 3f);
                Capture("Logs/play_moe_dialog.png");
                for (int i = 0; i < 4 && moe.Talking; i++) { moe.TestPress(true); yield return Frames(3); }
                yield return WaitFor(() => false, 1f);
                Check(moe.State == MoeQuests.Phase.Active && GameObject.Find("DropBeacon") != null && moe.TimeLeft > 60f, $"Liefer-Job angenommen ({moe.TimeLeft:0} s)");
                a.skater.Place(moe.DropPoint + Vector3.up * 0.3f, a.skater.Heading);
                yield return WaitFor(() => moe.State == MoeQuests.Phase.Done, 3f);
                Check(moe.State == MoeQuests.Phase.Done && GameObject.Find("DropBeacon") == null, "Paket abgeliefert");
                a.skater.Place(moeNpc.transform.position + mf * 2.2f + Vector3.up * 0.1f, Quaternion.LookRotation(-mf).eulerAngles.y);
                yield return Frames(15);
                long m1 = prof.money;
                moe.Talk();
                for (int i = 0; i < 12 && moe.Talking; i++) { moe.TestPress(true); yield return WaitFor(() => false, 0.3f); }
                yield return WaitFor(() => false, 0.4f);
                Capture("Logs/play_moe_reward.png");
                Check(prof.moeQuest == 1 && prof.money > m1 && prof.baggies == 1, $"Moe zahlt (+{prof.money - m1}) und gibt eine Tuete ({prof.baggies})");
                Check(NixDealer.CurrentPrice == NixDealer.FriendPrice, "Nix macht jetzt Freundschaftspreis");
            }

            // 12c. Miru im Park: sitzt auf der Bank, steht beim Ansprechen auf, setzt sich wieder (auf Wunsch oder wenn man wegfaehrt)
            var miru = MiruTalk.Instance;
            var miruNpc = StonerNpc.All.Find(n => n.Id == "Miru");
            Check(ParkMiru.Found && miru != null && miruNpc != null && miruNpc.Sitting && miruNpc.SitAmount > 0.99f, "Miru sitzt auf der Parkbank");
            if (miru != null && miruNpc != null)
            {
                if (a.Mode == PlayerMode.Driving) a.TestInteract();
                yield return Frames(10);
                Vector3 front = ParkMiru.StandPosition + ParkMiru.Facing * 2.2f;
                a.skater.Place(front + Vector3.up * 0.1f, Quaternion.LookRotation(-ParkMiru.Facing).eulerAngles.y);
                yield return WaitFor(() => false, 1.2f); // ankommen, kurz gucken
                Check(QuestNpc.Near(a.skater.transform.position) == miru, $"Miru ansprechbar (naechster: {QuestNpc.Near(a.skater.transform.position)?.Speaker}, {a.skater.State}, {a.skater.Speed:0.0} m/s, Abstand {Vector3.Distance(a.skater.transform.position, miru.transform.position):0.0} m)");
                miru.Talk();
                yield return WaitFor(() => false, 0.3f);
                Capture("Logs/play_miru_rising.png");
                Check(!miruNpc.Sitting && miruNpc.SitAmount > 0.05f && miruNpc.SitAmount < 0.95f, $"Miru steht auf ({miruNpc.SitAmount:0.00})");
                yield return WaitFor(() => miru.Talking, 3f);
                Check(miru.Talking && miruNpc.SitAmount < 0.05f && Vector3.Distance(miru.transform.position, ParkMiru.StandPosition) < 0.05f, "Miru steht vor der Bank und redet");
                yield return WaitFor(() => false, 1.5f); // Text lesen
                Capture("Logs/play_miru_dialog.png");
                for (int i = 0; i < 12 && miru.Talking && !miru.ChoiceOpen; i++) { miru.TestPress(true); yield return WaitFor(() => false, 0.6f); }
                yield return WaitFor(() => false, 2f);
                Check(miru.ChoiceOpen, "Miru: Antworten (bis dann / setz dich)");
                miru.TestPress(false); // "setz dich ruhig"
                yield return WaitFor(() => false, 1.5f);
                Check(!miru.Talking && miruNpc.Sitting && miruNpc.SitAmount > 0.99f && !GameInput.Blocked, "Miru setzt sich wieder, Steuerung frei");
                Capture("Logs/play_miru_sits.png");

                // Zweites Gespraech: "bis dann", dann wegfahren -> sie setzt sich von selbst
                yield return WaitFor(() => false, 1f);
                miru.Talk();
                yield return WaitFor(() => miru.Talking, 3f);
                Check(miru.Talking, $"Miru redet nochmal (sitzt {miruNpc.SitAmount:0.00})");
                yield return WaitFor(() => false, 1f);
                for (int i = 0; i < 12 && miru.Talking && !miru.ChoiceOpen; i++) { miru.TestPress(true); yield return WaitFor(() => false, 0.6f); }
                yield return WaitFor(() => false, 3f); // letzte Seite zu Ende lesen
                miru.TestPress(true);
                yield return WaitFor(() => false, 1.5f);
                Check(!miru.Talking && !miruNpc.Sitting && miruNpc.SitAmount < 0.01f, $"Miru bleibt stehen, solange man da ist (redet {miru.Talking}, sitzt {miruNpc.Sitting} {miruNpc.SitAmount:0.00})");
                a.skater.Place(front + ParkMiru.Facing * 15f + Vector3.up * 0.1f, a.skater.Heading);
                yield return WaitFor(() => miruNpc.SitAmount > 0.99f, 6f);
                Check(miruNpc.Sitting && miruNpc.SitAmount > 0.99f && Vector3.Distance(miru.transform.position, ParkMiru.SeatPosition) < 0.05f, $"Miru setzt sich wieder, als man weg ist (redet {miru.Talking}, sitzt {miruNpc.SitAmount:0.00})");
            }

            // 13. Auto zuruecksetzen
            a.car.ResetUpright();
            yield return Frames(20);
            Check(WeatherSystem.Strikes > 0, $"Gewitter: {WeatherSystem.Strikes} Blitze eingeschlagen");
            Log($"Ende: Geld {SaveSystem.Profile.money}, beste Combo {SaveSystem.Profile.bestCombo}");
            Finish("fertig");
        }

        // ---------------------------------------------------------- Online-Test (zwei Spielinstanzen)

        IEnumerator RunNetHost()
        {
            SaveSystem.Profile.playerName = "HostTester";
            SaveSystem.Profile.weather = "gewitter"; // der Client muss das Gewitter des Hosts bekommen
            yield return WaitFor(() => ModLibrary.Characters.TrueForAll(m => !m.loading) && ModLibrary.Boards.TrueForAll(m => !m.loading), 20f);
            var hostChar = ModLibrary.Characters.Find(m => m.loaded);
            var hostBoard = ModLibrary.Boards.Find(m => m.loaded);
            Check(hostChar != null && hostBoard != null, "Mods in der Exe geladen");
            SaveSystem.Profile.selectedCharacter = hostChar != null ? hostChar.id : "";
            SaveSystem.Profile.selectedBoard = hostBoard != null ? hostBoard.id : "standard";
            PaintTestGraffiti(SaveSystem.Graffiti);
            SaveSystem.Profile.angelWings = true;
            var hostDesign = SaveSystem.Profile.GetCarSave(SaveSystem.Profile.selectedCar).design;
            hostDesign.neon = 3; // Regenbogen
            GameSession.Mode = SessionMode.Host;
            GameSession.Port = ushort.TryParse(ArgValue("-netport", "7777"), out var netPort) ? netPort : (ushort)7777; // eigener Port, falls nebenher ein Spiel hostet
            SceneManager.LoadScene("City");
            yield return WaitFor(() => PlayerAvatar.Local != null, 20f);
            var a = PlayerAvatar.Local;
            Check(a != null, "Host gestartet, eigener Spieler da");
            if (a == null) { Finish("kein Spieler"); yield break; }
            a.externalControl = true;
            a.skater.inputEnabled = false;
            yield return WaitFor(() => FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None).Length >= 2, 60f);
            var all = FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None);
            Check(all.Length >= 2, "Client verbunden (Spieler: " + all.Length + ")");
            PlayerAvatar remote = null;
            foreach (var p in all) if (p != a) remote = p;
            if (remote != null)
            {
                yield return WaitFor(() => !string.IsNullOrEmpty(remote.PlayerName), 5f);
                Log($"Client-Spieler: Name '{remote.PlayerName}', Modus {remote.Mode}, Auto bei {remote.car.transform.position}");
                Check(remote.PlayerName == "ClientTester", "Name des Clients synchronisiert");
            }
            yield return Drive(a, 2f, t => new VehicleInput { throttle = 1f });
            a.TestInteract();
            Log($"Host macht Bail-Out, Modus {a.Mode}");
            float t0 = Time.time;
            Vector3 lastRemote = remote != null ? remote.car.transform.position : Vector3.zero;
            float moved = 0f;
            while (Time.time - t0 < 10f)
            {
                if (remote != null)
                {
                    moved += Vector3.Distance(lastRemote, remote.car.transform.position);
                    lastRemote = remote.car.transform.position;
                }
                yield return null;
            }
            Check(moved > 5f, $"Auto des Clients bewegt sich beim Host ({moved:0} m)");

            // Eigenes Graffiti auf einen Spot spruehen: der Client muss genau dieses Bild sehen
            var spot = TagSpot.ByIndex(0);
            bool sprayed = false;
            if (spot != null)
            {
                foreach (var dir in new[] { spot.transform.forward, -spot.transform.forward, spot.transform.right, -spot.transform.right })
                {
                    Vector3 probe = spot.transform.position + Vector3.ProjectOnPlane(dir, Vector3.up).normalized * 1.6f;
                    if (!Physics.Raycast(probe + Vector3.up * 0.5f, Vector3.down, out RaycastHit g, 4f, ~LayerMask.GetMask("Rail", "Skater", "Car"), QueryTriggerInteraction.Ignore)) continue;
                    if (Vector3.Distance(g.point, spot.transform.position) > 3.6f) continue;
                    a.PlaceForChallenge(false, g.point, 0f);
                    yield return Frames(10);
                    sprayed = a.TrySpray();
                    if (sprayed) break;
                }
            }
            Check(sprayed, "Host sprueht sein Graffiti auf einen Spot");

            // Skitchen online: Host setzt sich ins Auto, der Client haengt sich an, dann faehrt der Host los
            Vector3 road = new Vector3(CityBuilder.RoadCenter(0) + 25f, 0f, CityBuilder.RoadCenter(1));
            a.PlaceForChallenge(true, road, 90f);
            var myHitch = a.car.GetComponent<Hitchable>();
            yield return WaitFor(() => remote != null && remote.skater.RemoteHitch == myHitch, 25f);
            Check(remote != null && remote.skater.RemoteHitch == myHitch, "Client haengt beim Host am Auto (" + (remote != null ? remote.skater.RemoteState.ToString() : "-") + ")");
            float maxOff = 0f, t1 = Time.time;
            Vector3 carStart = a.car.transform.position;
            while (Time.time - t1 < 6f)
            {
                a.car.input = new VehicleInput { throttle = 0.45f };
                yield return new WaitForEndOfFrame(); // nach LateUpdate messen (dann klebt der Skater am Auto)
                if (remote != null && remote.skater.RemoteHitch == myHitch && remote.skater.RemoteState == SkaterState.Hitched)
                    maxOff = Mathf.Max(maxOff, Vector3.Distance(remote.skater.transform.position, myHitch.StandPoint(remote.skater.RemoteHitchAnchor)));
            }
            a.car.input = new VehicleInput { brake = 1f };
            float hostRode = Vector3.Distance(carStart, a.car.transform.position);
            Capture("Logs/net_host_skitch.png");
            Check(hostRode > 15f && maxOff < 0.3f, $"Skater des Clients klebt beim Host am fahrenden Auto ({hostRode:0} m, max. Abweichung {maxOff:0.00} m)");
            // Dem Client Zeit lassen, seinen Test zu beenden, bevor der Host schliesst
            yield return new WaitForSeconds(6f);
            Finish("Host fertig");
        }

        IEnumerator RunNetClient()
        {
            SaveSystem.Profile.playerName = "ClientTester";
            SaveSystem.Profile.weather = "klar"; // eigene Einstellung, online gilt aber das Wetter des Hosts
            GameSession.Mode = SessionMode.Client;
            GameSession.Address = ArgValue("-nettest-client", "127.0.0.1");
            if (GameSession.Address.StartsWith("-")) GameSession.Address = "127.0.0.1";
            GameSession.Port = ushort.TryParse(ArgValue("-netport", "7777"), out var netPort) ? netPort : (ushort)7777; // eigener Port, falls nebenher ein Spiel hostet
            SceneManager.LoadScene("City");
            yield return WaitFor(() => PlayerAvatar.Local != null, 30f);
            var a = PlayerAvatar.Local;
            Check(a != null, "Client verbunden, eigener Spieler da");
            if (a == null) { Finish("keine Verbindung"); yield break; }
            a.externalControl = true;
            a.skater.inputEnabled = false;
            yield return WaitFor(() => FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None).Length >= 2, 20f);
            PlayerAvatar host = null;
            foreach (var p in FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None)) if (p != a) host = p;
            Check(host != null, "Host-Spieler sichtbar");
            if (host != null) Log($"Host-Spieler: Name '{host.PlayerName}', Modus {host.Mode}");
            yield return WaitFor(() => ModLibrary.Characters.TrueForAll(m => !m.loading) && ModLibrary.Boards.TrueForAll(m => !m.loading), 20f);
            yield return Frames(10);
            if (host != null)
            {
                bool modBoard = host.skater.boardPivot != null && host.skater.boardPivot.Find("ModBoard") != null;
                Check(modBoard && host.skater.rig != null && host.skater.rig.Ready, "Mod-Figur und Mod-Board des Hosts beim Client sichtbar");
                yield return WaitFor(() => WingsOnBack.Find(host.skater) != null, 5f);
                Check(host.HasWings && WingsOnBack.Find(host.skater) != null, "Engelsfluegel des Hosts beim Client sichtbar");
                Check(host.car.GetComponentInChildren<Underglow>() != null, "Unterboden-Neon des Hosts beim Client sichtbar");
            }
            bool sawSkating = false;
            float t0 = Time.time;
            while (Time.time - t0 < 12f)
            {
                if (a == null) { Finish("Verbindung verloren"); yield break; }
                float el = Time.time - t0;
                a.car.input = new VehicleInput { throttle = el < 4f ? 0.8f : 0f, brake = el > 4f ? 0.5f : 0f };
                if (host != null && host.Mode == PlayerMode.Skating && !sawSkating)
                {
                    sawSkating = true;
                    Log($"Host-Modus gewechselt zu Skating, Skater bei {host.skater.transform.position}");
                }
                yield return null;
            }
            Check(sawSkating, "Bail-Out des Hosts beim Client angekommen");
            Log($"Wetter beim Client: Regen {WeatherSystem.Rain:0.00}, Gewitter {WeatherSystem.Storm}, Blitze {WeatherSystem.Strikes}, Nachrichten vom Host {WeatherSystem.SyncMessages}");
            Check(WeatherSystem.SyncMessages > 0 && WeatherSystem.Rain > 0.8f && WeatherSystem.Storm && WeatherSystem.Strikes > 0, "Wetter des Hosts beim Client (Gewitter mit Blitzen)");
            if (host != null)
            {
                // Eigenes Graffiti des Hosts: uebertragen, und sein Tag auf dem Spot zeigt genau dieses Bild
                yield return WaitFor(() => host.HasRemoteGraffiti, 10f);
                var g = host.RemoteGraffiti;
                Check(host.HasRemoteGraffiti && g != null && IsTestGraffiti(g), $"Eigenes Graffiti des Hosts beim Client angekommen ({(g != null ? g.width + "x" + g.height : "-")})");
                // Der Host sprueht auf den Spot, der ihm am naechsten ist: irgendeiner muss sein Bild zeigen
                TagSpot hostSpot = null;
                yield return WaitFor(() => (hostSpot = FindSpotWith(host.RemoteGraffiti)) != null, 25f);
                Check(hostSpot != null, "Tag des Hosts zeigt beim Client sein eigenes Graffiti" + (hostSpot != null ? $" ({hostSpot.name})" : ""));
            }
            if (host != null)
            {
                // Skitchen online: sobald der Host im Auto sitzt, seitlich ranrollen und dranhaengen
                var hostHitch = host.car.GetComponent<Hitchable>();
                yield return WaitFor(() => host.Mode == PlayerMode.Driving && host.car.transform.position.x < CityBuilder.RoadCenter(0) + 40f, 40f);
                // Der Host wurde dorthin teleportiert: warten, bis sein Auto beim Client auch angekommen ist und steht
                float still = 0f;
                yield return WaitFor(() => { still = hostHitch.Velocity.magnitude < 1f ? still + Time.deltaTime : 0f; return still > 1f; }, 10f);
                a.PlaceForChallenge(false, hostHitch.StandPoint(Hitchable.Left), host.car.transform.eulerAngles.y);
                yield return WaitFor(() => a.skater.State == SkaterState.Riding, 2f);
                yield return Frames(3);
                var near = Hitchable.FindNear(a.skater.transform.position, a, out int nearAnchor);
                Vector3 sp = hostHitch.StandPoint(Hitchable.Left);
                Log($"Vor dem Dranhaengen: Skater {a.skater.State} bei {a.skater.transform.position}, Haltepunkt {sp} ({Vector3.Distance(sp, a.skater.transform.position):0.00} m), " +
                    $"Host-Auto {host.car.transform.position} v {hostHitch.Velocity.magnitude:0.0}, frei {hostHitch.Available}, gefunden {(near != null ? near.name + "/" + nearAnchor : "nichts")}, Hitchables {Hitchable.All.Count}");
                bool hitched = a.TestHitch();
                Check(hitched && a.skater.HitchTarget == hostHitch, "Client haengt sich ans Auto des Hosts");
                Vector3 start = a.skater.transform.position;
                float t2 = Time.time;
                bool held = true;
                while (Time.time - t2 < 7f) { held &= a.skater.State == SkaterState.Hitched; yield return null; }
                float rode = Vector3.Distance(start, a.skater.transform.position);
                Capture("Logs/net_client_skitch.png");
                Check(held && rode > 15f, $"Am fahrenden Auto des Hosts mitgefahren ({rode:0} m, Combo {a.combo.Total})");
                a.skater.ReleaseHitch(false);
                yield return Frames(3);
                Check(a.skater.State == SkaterState.Riding, "Losgelassen, rollt weiter (" + a.skater.State + ")");
            }
            if (a != null) Log($"Eigenes Auto: {a.car.SpeedKmh:0} km/h bei {a.car.transform.position}");
            Finish("Client fertig");
        }

        /// <summary>Erkennbares Test-Graffiti: gruener Rahmen, magenta Mitte, sonst transparent.</summary>
        static void PaintTestGraffiti(Texture2D t)
        {
            var px = new Color32[t.width * t.height];
            for (int y = 0; y < t.height; y++)
                for (int x = 0; x < t.width; x++)
                {
                    bool border = x < 24 || y < 24 || x >= t.width - 24 || y >= t.height - 24;
                    bool center = Mathf.Abs(x - t.width / 2) < 50 && Mathf.Abs(y - t.height / 2) < 50;
                    px[y * t.width + x] = border ? new Color32(0, 255, 0, 255) : center ? new Color32(255, 0, 255, 255) : new Color32(0, 0, 0, 0);
                }
            t.SetPixels32(px);
            t.Apply();
        }

        static TagSpot FindSpotWith(Texture tex)
        {
            for (int i = 0; i < TagSpot.Count; i++)
            {
                var s = TagSpot.ByIndex(i);
                if (s != null && s.ShownGraffiti != null && s.ShownGraffiti == tex) return s;
            }
            return null;
        }

        static bool IsTestGraffiti(Texture2D t)
        {
            Color32 edge = t.GetPixel(5, 5), mid = t.GetPixel(t.width / 2, t.height / 2);
            return edge.g > 240 && edge.r < 15 && mid.r > 240 && mid.b > 240 && mid.g < 15;
        }

        void Capture(string path) => Capture(path, null);

        /// <summary>Screenshot in 1600x900; prepare laeuft nach dem Layout in dieser Groesse, direkt vor dem Rendern.</summary>
        void Capture(string path, Action prepare)
        {
            var cam = Camera.main;
            if (cam == null) return;
            try
            {
                const int w = 1600, h = 900;
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                var canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                var modes = new RenderMode[canvases.Length];
                cam.targetTexture = rt;
                for (int i = 0; i < canvases.Length; i++)
                {
                    modes[i] = canvases[i].renderMode;
                    if (canvases[i].renderMode == RenderMode.ScreenSpaceOverlay)
                    {
                        canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                        canvases[i].worldCamera = cam;
                        canvases[i].planeDistance = 1f;
                    }
                }
                Canvas.ForceUpdateCanvases();
                if (prepare != null)
                {
                    prepare();
                    Canvas.ForceUpdateCanvases();
                }
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;
                for (int i = 0; i < canvases.Length; i++) canvases[i].renderMode = modes[i];
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Destroy(tex);
                rt.Release();
            }
            catch (Exception e)
            {
                Log("Screenshot fehlgeschlagen: " + e.Message);
            }
        }

        bool _finished;

        void Finish(string reason)
        {
            if (_finished) return;
            _finished = true;
            Log($"ENDE ({reason}). Fehler im Log: {_errors}, fehlgeschlagene Pruefungen: {_checksFailed}");
            string path = ArgValue("-testlog", "Logs/autotest.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, _log.ToString());
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(_errors > 0 || _checksFailed > 0 ? 1 : 0);
#else
            Application.Quit(_errors > 0 || _checksFailed > 0 ? 1 : 0);
#endif
        }
    }
}
