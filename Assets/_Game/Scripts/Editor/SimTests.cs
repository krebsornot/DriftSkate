using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Pruefungen ohne Play-Mode: simuliert die Drift-Physik mit festen Eingaben und rendert Screenshots.
    /// Ergebnisse landen in Logs/ (Textprotokoll und PNG).
    /// </summary>
    public static class SimTests
    {
        const float Dt = 0.01f;

        struct Phase
        {
            public string name;
            public float duration;
            public System.Func<VehicleController, float, VehicleInput> input;
        }

        /// <summary>Fuer die Kommandozeile: Physik-Test und Screenshots in einem Lauf.</summary>
        public static void RunAllBatch()
        {
            RunDriftTest();
            RenderScreenshots();
        }

        [MenuItem("DriftSkate/Tests/Drift-Physik simulieren")]
        public static void RunDriftTest()
        {
            var report = new StringBuilder();
            foreach (var carId in new[] { "roku86", "sylph15", "toro2j" })
                foreach (int assist in new[] { 2, 0 })
                    SimulateCar(carId, assist, report);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/drift_test.txt", report.ToString());
            Debug.Log(report.ToString());
        }

        static void SimulateCar(string carId, int assist, StringBuilder report)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var oldMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.transform.position = new Vector3(0, -1, 0);
                ground.transform.localScale = new Vector3(2000, 2, 2000);

                var def = Catalog.Car(carId);
                var go = new GameObject("TestCar") { layer = LayerMask.NameToLayer("Car") };
                var rb = go.AddComponent<Rigidbody>();
                var vc = go.AddComponent<VehicleController>();
                vc.Init();
                vc.ApplySetup(VehicleSetup.From(def, Tuning.Defaults(), assist, true));
                CarBuilder.Build(go.transform, def, def.defaultColor, Palette.Pink, null, vc);
                vc.SetLocal(true);
                vc.hasDriver = true;
                go.transform.position = new Vector3(0, 0.4f, 0);
                Physics.SyncTransforms();

                // Fahrer-Logik fuer den Dauer-Drift: haelt die Richtung grob mit Gegenlenken + Gas
                float targetSlip = 35f;
                var phases = new[]
                {
                    new Phase { name = "Anfahren", duration = 4.5f, input = (c, t) => new VehicleInput { throttle = 1f } },
                    new Phase { name = "Einlenken + Handbremse", duration = 0.45f, input = (c, t) => new VehicleInput { steer = -1f, throttle = 0.6f, handbrake = t < 0.3f } },
                    new Phase { name = "Drift halten", duration = 6f, input = (c, t) =>
                        {
                            float slip = c.BodySlip;
                            float err = Mathf.Abs(slip) - targetSlip;
                            float throttle = Mathf.Clamp01(0.75f - err * 0.03f);
                            float steer = assist > 0 ? Mathf.Clamp(-Mathf.Sign(slip) * 0.25f, -1f, 1f)
                                                     : Mathf.Clamp(slip / c.setup.maxSteer + (err > 0 ? 0.1f : -0.1f) * Mathf.Sign(slip), -1f, 1f);
                            return new VehicleInput { steer = steer, throttle = throttle };
                        } },
                    new Phase { name = "Ausrollen", duration = 3f, input = (c, t) => new VehicleInput { throttle = 0f } },
                };

                report.AppendLine($"=== {def.name} | Lenkhilfe {assist} | {def.horsepower} PS, {def.mass} kg ===");
                float total = 0f;
                foreach (var phase in phases)
                {
                    float t = 0f, maxSlip = 0f, slipSum = 0f, minSpeed = 999f, maxSpeed = 0f;
                    int samples = 0, driftSamples = 0;
                    bool spun = false;
                    float nextLog = 0f;
                    while (t < phase.duration)
                    {
                        vc.input = phase.input(vc, t);
                        vc.Step(Dt);
                        Physics.Simulate(Dt);
                        t += Dt;
                        total += Dt;
                        float slip = Mathf.Abs(vc.BodySlip);
                        maxSlip = Mathf.Max(maxSlip, slip);
                        slipSum += slip;
                        samples++;
                        if (slip > 15f && vc.SpeedKmh > 30f) driftSamples++;
                        if (slip > 120f && vc.SpeedKmh > 15f) spun = true;
                        minSpeed = Mathf.Min(minSpeed, vc.SpeedKmh);
                        maxSpeed = Mathf.Max(maxSpeed, vc.SpeedKmh);
                        if (t >= nextLog)
                        {
                            report.AppendLine($"  t={total,5:0.00}s  {phase.name,-24} v={vc.SpeedKmh,6:0.0} km/h  Gang={vc.Gear}  U/min={vc.EngineRpm,5:0}  Winkel={vc.BodySlip,6:0.0}  Lenkung={vc.SteerAngle,5:0.0}  Hoehe={go.transform.position.y:0.00}");
                            nextLog += 0.5f;
                        }
                    }
                    report.AppendLine($"  -> {phase.name}: max Winkel {maxSlip:0.0}, mittel {slipSum / samples:0.0}, Drift-Anteil {100f * driftSamples / samples:0}%, Tempo {minSpeed:0}-{maxSpeed:0} km/h{(spun ? ", DREHER!" : "")}");
                }
                report.AppendLine($"  Endlage: Pos {go.transform.position}, aufrecht {Vector3.Dot(go.transform.up, Vector3.up):0.00}");
                report.AppendLine();
            }
            finally
            {
                Physics.simulationMode = oldMode;
            }
        }

        [MenuItem("DriftSkate/Tests/Screenshots rendern")]
        public static void RenderScreenshots()
        {
            Directory.CreateDirectory("Logs");
            RenderBoards();
            RenderCharacterPoses();
            RenderCity();
            RenderGarage();
        }

        /// <summary>Spray-Geraeusche als WAV nach Logs/ schreiben (zum Anhoeren und Pruefen).</summary>
        [MenuItem("DriftSkate/Tests/Spray-Sounds exportieren")]
        public static void ExportSpraySounds()
        {
            Directory.CreateDirectory("Logs");
            var make = typeof(SpraySound).GetMethod("Make", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            for (int i = 0; i < 5; i++)
            {
                var clip = (AudioClip)make.Invoke(null, new object[] { 1234 + i * 77, i < 2 });
                var data = new float[clip.samples];
                clip.GetData(data, 0);
                using (var w = new BinaryWriter(File.Create($"Logs/spray_{i}.wav")))
                {
                    w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data.Length * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(clip.frequency); w.Write(clip.frequency * 2); w.Write((short)2); w.Write((short)16);
                    w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data.Length * 2);
                    foreach (float v in data) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * 32767f));
                }
            }
            Debug.Log("[DriftSkate] Spray-Sounds exportiert");
        }

        /// <summary>Alle Katalog-Boards nebeneinander: schraeg von oben, von unten und eine Nahaufnahme der Trucks.</summary>
        [MenuItem("DriftSkate/Tests/Boards rendern")]
        public static void RenderBoards()
        {
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var spawn = Object.FindAnyObjectByType<SpawnPoints>().transform.GetChild(0);
            Vector3 basePos = new Vector3(spawn.position.x, 0f, spawn.position.z);
            Quaternion rot = Quaternion.Euler(0f, spawn.eulerAngles.y, 0f);
            Vector3 right = rot * Vector3.right, fwd = rot * Vector3.forward;
            int n = Catalog.Boards.Count;
            for (int i = 0; i < n; i++)
            {
                var pivot = new GameObject("BoardPreview_" + Catalog.Boards[i].id).transform;
                pivot.SetPositionAndRotation(basePos + right * (i - (n - 1) * 0.5f) * 0.42f + Vector3.up * 0.1f, rot);
                BoardBuilder.Build(pivot, Catalog.Boards[i], MakeTag());
            }
            var cam = Camera.main;
            cam.fieldOfView = 30f;
            Vector3 c = basePos + Vector3.up * 0.05f;
            cam.transform.position = c - fwd * 0.9f + right * 0.6f + Vector3.up * 1.1f;
            cam.transform.LookAt(c);
            Capture(cam, "Logs/shot_boards.png");

            cam.transform.position = c + right * 2.6f + Vector3.up * 0.12f;
            cam.transform.LookAt(c);
            cam.fieldOfView = 26f;
            Capture(cam, "Logs/shot_boards_side.png");

            // Von unten: Boards umdrehen
            foreach (var p in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (p.name.StartsWith("BoardPreview_")) p.rotation = Quaternion.AngleAxis(180f, fwd) * rot;
            cam.fieldOfView = 30f;
            cam.transform.position = c - fwd * 0.9f - right * 0.4f + Vector3.up * 1.1f;
            cam.transform.LookAt(c);
            Capture(cam, "Logs/shot_boards_bottom.png");

            // Nahaufnahme Truck des Standard-Boards (wieder richtig herum)
            var first = GameObject.Find("BoardPreview_standard").transform;
            first.rotation = rot;
            Vector3 truck = first.position + fwd * 0.18f - Vector3.up * 0.06f;
            cam.fieldOfView = 30f;
            cam.transform.position = truck + fwd * 0.35f + right * 0.22f + Vector3.up * 0.05f;
            cam.transform.LookAt(truck);
            Capture(cam, "Logs/shot_board_truck.png");
        }

        /// <summary>Fuenf Figuren nebeneinander in verschiedenen Posen (prueft Modell + IK-Rig).</summary>
        static void RenderCharacterPoses()
        {
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var spawn = Object.FindAnyObjectByType<SpawnPoints>().transform.GetChild(0);
            Vector3 travel = spawn.forward;
            Vector3 toeSide = Quaternion.Euler(0, 80f, 0) * travel;
            Vector3 basePos = new Vector3(spawn.position.x, 0f, spawn.position.z);
            var poses = new (string name, System.Action<SkaterRig> set)[]
            {
                ("Stehen", r => { }),
                ("Ollie laden", r => r.crouch = 0.95f),
                ("Push", r => r.pushPhase = 0.45f),
                ("Push Fakie", r => { r.pushPhase = 0.45f; r.board.parent.localRotation = Quaternion.Euler(0, 180f, 0); }),
                ("Indy", r => { r.grab = "INDY"; r.airborne = 1f; r.crouch = 0.3f; }),
                ("Kurve", r => { r.lean = 0.9f; r.crouch = 0.2f; }),
                ("Kickflip", r => { r.airborne = 1f; r.crouch = 0.3f; r.board.localRotation = Quaternion.Euler(0, 0, 160f); r.board.localPosition = new Vector3(0, 0.3f, 0); }),
            };
            var outfits = new[]
            {
                new[] { "head_cap_y", "jacket_orange", "pants_denim", "shoes_white" },
                new[] { "head_hair", "jacket_blue", "pants_cargo", "shoes_red" },
                new[] { "head_phones", "jacket_lime", "pants_purple", "shoes_yellow" },
                new[] { "head_beanie_p", "jacket_white", "pants_denim", "shoes_red" },
                new[] { "head_cap_y", "jacket_blue", "pants_purple", "shoes_white" },
            };
            for (int i = 0; i < poses.Length; i++)
            {
                var root = new GameObject("Pose_" + poses[i].name);
                root.transform.SetPositionAndRotation(basePos + travel * (i - (poses.Length - 1) * 0.5f) * 2.0f, Quaternion.LookRotation(travel));
                SkaterBuilder.Build(root.transform, SkaterBuilder.Outfit.FromIds(outfits[i % outfits.Length], Palette.Pink), Catalog.Boards[i % Catalog.Boards.Count], MakeTag(), null);
                var rig = root.GetComponentInChildren<SkaterRig>();
                if (rig == null) { Debug.LogError("[DriftSkate] Kein Rig an der Figur"); continue; }
                poses[i].set(rig);
                for (int k = 0; k < 20; k++) rig.ApplyPose(0.1f);
                if (poses[i].name.StartsWith("Push") || poses[i].name == "Stehen")
                {
                    // Pruefen, welcher Fuss anschiebt: der in Fahrtrichtung hintere muss am Boden sein
                    var map = SkeletonMap.Resolve(rig.transform, null, out _);
                    var sb = new StringBuilder($"[DriftSkate] {poses[i].name}: Huefte {root.transform.InverseTransformPoint(map.hips.position).y:0.00} m");
                    for (int f = 0; f < 2; f++)
                    {
                        Vector3 p = root.transform.InverseTransformPoint(map.foot[f].position);
                        Vector3 h = root.transform.InverseTransformPoint(map.upperLeg[f].position);
                        sb.Append($" | Fuss {(f == 0 ? "L" : "R")} Hoehe {p.y:0.00} laengs {p.z:+0.00;-0.00} quer {p.x:+0.00;-0.00} (Hueftgelenk laengs {h.z:+0.00;-0.00})");
                    }
                    Debug.Log(sb.ToString());
                }
            }
            var cam = Camera.main;
            cam.fieldOfView = 40f;
            cam.transform.position = basePos + toeSide * 11.5f + Vector3.up * 1.5f;
            cam.transform.LookAt(basePos + Vector3.up * 0.9f);
            Capture(cam, "Logs/shot_character_poses.png");

            // Nahaufnahme der mittleren Figur (Push) von schraeg vorn
            cam.fieldOfView = 35f;
            Vector3 c = basePos + Vector3.up * 0.9f;
            cam.transform.position = c + toeSide * 3.2f + travel * 1.6f + Vector3.up * 0.3f;
            cam.transform.LookAt(c);
            Capture(cam, "Logs/shot_character_close.png");
        }

        static void RenderCity()
        {
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var spawn = Object.FindAnyObjectByType<SpawnPoints>().transform.GetChild(0);

            var carGo = new GameObject("PreviewCar");
            var def = Catalog.Car("sylph15");
            carGo.transform.SetPositionAndRotation(spawn.position + Vector3.up * 0.05f, Quaternion.Euler(0, spawn.eulerAngles.y - 25f, 0));
            CarBuilder.Build(carGo.transform, def, def.defaultColor, Palette.Pink, MakeTag(), null);

            var skGo = new GameObject("PreviewSkater");
            skGo.transform.position = spawn.position + spawn.right * -4f + spawn.forward * 2f;
            skGo.transform.rotation = Quaternion.Euler(0, spawn.eulerAngles.y + 30f, 0);
            SkaterBuilder.Build(skGo.transform, SkaterBuilder.Outfit.FromIds(new[] { "head_cap_y", "jacket_orange", "pants_denim", "shoes_white" }), Catalog.Boards[0], MakeTag(), null);

            var cam = Camera.main;
            Vector3 focus = carGo.transform.position + Vector3.up * 1f;
            cam.transform.position = focus - spawn.forward * 7.5f + spawn.right * 3.5f + Vector3.up * 2.2f;
            cam.transform.LookAt(focus + spawn.right * -1.5f);
            cam.fieldOfView = 60f;
            Capture(cam, "Logs/shot_city_street.png");

            cam.transform.position = new Vector3(-60, 70, -150);
            cam.transform.LookAt(new Vector3(0, 0, -20));
            cam.fieldOfView = 55f;
            Capture(cam, "Logs/shot_city_overview.png");

            // Hafenrand: Kaimauer, Gelaender, Wasser und Skyline
            cam.fieldOfView = 60f;
            cam.transform.position = new Vector3(CityBuilder.Half - 26f, 3.0f, -20f);
            cam.transform.LookAt(new Vector3(CityBuilder.Half + 80f, 12f, 10f));
            Capture(cam, "Logs/shot_harbor_edge.png");
            cam.transform.position = new Vector3(-150f, 2.0f, -CityBuilder.Half + 14f);
            cam.transform.LookAt(new Vector3(-110f, 6f, -CityBuilder.Half - 10f));
            Capture(cam, "Logs/shot_edge_street.png");

            var skatepark = GameObject.Find("Block_31_S");
            if (skatepark != null)
            {
                Vector3 c = skatepark.GetComponentInChildren<Renderer>().bounds.center;
                cam.transform.position = c + new Vector3(30, 18, -30);
                cam.transform.LookAt(c);
                Capture(cam, "Logs/shot_skatepark.png");
            }
        }

        /// <summary>Nahaufnahmen typischer Problemstellen der Stadt (Laternen, Treppen, Rails, Hafen) zum Pruefen nach Aenderungen am Stadtbau.</summary>
        [MenuItem("DriftSkate/Tests/Stadt-Details rendern")]
        public static void RenderCityDetails()
        {
            Directory.CreateDirectory("Logs");
            EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
            var cam = Camera.main;
            float B(int i) => CityBuilder.BlockCenter(i);
            float R(int k) => CityBuilder.RoadCenter(k);
            float h = CityBuilder.Half;
            var views = new (string name, Vector3 pos, Vector3 target, float fov)[]
            {
                ("street",        new Vector3(B(1) - 20f, 1.8f, R(1) - 7f),     new Vector3(B(2) + 10f, 2.5f, R(1) + 6f), 60f),
                ("crossing",      new Vector3(R(2) + 22f, 26f, R(2) - 30f),     new Vector3(R(2), 0f, R(2)), 55f),
                ("ringroad",      new Vector3(B(1), 1.8f, R(0) - 4f),           new Vector3(B(2), 2f, R(0) + 6f), 60f),
                ("plaza_stairs",  new Vector3(B(1) + 20f, 3f, B(2) - 36f),      new Vector3(B(1) + 14f, 1.2f, B(2) - 18f), 55f),
                ("fountain",      new Vector3(B(2) - 22f, 2.2f, B(2) - 42f),    new Vector3(B(2), 1f, B(2) - 20f), 60f),
                ("skatepark",     new Vector3(B(3) + 10f, 5f, B(1) - 18f),      new Vector3(B(3) - 6f, 0.5f, B(1)), 55f),
                ("pyramid",       new Vector3(B(3) - 10f, 4.5f, B(1) - 6f),     new Vector3(B(3) - 20f, 0.5f, B(1) + 6f), 55f),
                ("driftlot",      new Vector3(B(1), 3f, B(1) - 20f),            new Vector3(B(1), 5f, B(1) + 34f), 60f),
                ("harbor_bridge", new Vector3(h - 30f, 3f, -30f),               new Vector3(h + 6f, 3f, 0f), 60f),
                ("harbor_block",  new Vector3(B(2) - 34f, 7f, B(0) + 40f),      new Vector3(B(2), 0f, B(0)), 55f),
                ("corner",        new Vector3(-h + 22f, 3f, -h + 22f),          new Vector3(-h - 6f, 4f, -h - 6f), 60f),
                ("plaza2",        new Vector3(B(3) + 4f, 3f, B(2) - 32f),       new Vector3(B(3) + 22f, 2f, B(2) - 12f), 55f),
                ("plaza_overview", new Vector3(B(1) + 30f, 26f, B(2) - 40f),    new Vector3(B(1), 0f, B(2)), 60f),
                ("plaza_hangout", new Vector3(B(1) - 14f, 2.2f, B(2) + 14f),    new Vector3(B(1) - 24f, 2f, B(2) + 29f), 60f),
                ("fountain_overview", new Vector3(B(2) + 34f, 22f, B(2) + 34f), new Vector3(B(2), 1f, B(2)), 60f),
                ("fountain_close", new Vector3(B(2) + 9f, 2.5f, B(2) - 11f),    new Vector3(B(2), 2.5f, B(2)), 60f),
                ("tree_close",    new Vector3(B(2) - 20f, 2.5f, B(2) - 20f),    new Vector3(B(2) - 28f, 3.5f, B(2) - 28f), 60f),
                ("driftsign_back", new Vector3(B(1), 2.5f, R(2) + 2f),          new Vector3(B(1), 7f, B(1) + 34f), 60f),
                ("water",         new Vector3(-30f, 4f, -h + 4f),               new Vector3(-10f, -2f, -h - 40f), 60f),
                // Blick in die Abendsonne (Westen) die Strasse entlang
                ("sunset",        new Vector3(B(3), 1.8f, R(2) - 4f),           new Vector3(B(3), 1.8f, R(2) - 4f) + Vector3.ProjectOnPlane(SkyLook.SunDirection, Vector3.up).normalized * 100f + Vector3.up * 18f, 65f),
            };
            foreach (var v in views)
            {
                cam.fieldOfView = v.fov;
                cam.transform.position = v.pos;
                cam.transform.LookAt(v.target);
                Capture(cam, "Logs/detail_" + v.name + ".png");
            }

            // Strassen-Deko: je ein Automat und ein Container von schraeg vorn (mit dem Nachbar-Kram drumherum)
            foreach (var name in new[] { "VendingMachine", "Dumpster" })
            {
                var prop = GameObject.Find(name);
                if (prop == null) continue;
                Transform t = prop.transform;
                cam.fieldOfView = 55f;
                cam.transform.position = t.position + t.forward * 5.5f + t.right * 3.5f + Vector3.up * 2.2f;
                cam.transform.LookAt(t.position + Vector3.up * 0.8f);
                Capture(cam, "Logs/detail_prop_" + name.ToLower() + ".png");
            }

            // Ladenfront eines Hauses von der Strasse aus (erste Markise in der Szene, Blick auf die Fassade)
            var awning = GameObject.Find("Awning");
            if (awning != null)
            {
                Transform building = null;
                float best = float.MaxValue;
                foreach (var r in awning.transform.parent.GetComponentsInChildren<Renderer>())
                {
                    if (r.name != "Building") continue;
                    float d = (r.bounds.center - awning.transform.position).sqrMagnitude;
                    if (d < best) { best = d; building = r.transform; }
                }
                if (building != null)
                {
                    Vector3 street = awning.transform.position - building.position;
                    street.y = 0f;
                    street = Mathf.Abs(street.x) > Mathf.Abs(street.z) ? new Vector3(Mathf.Sign(street.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(street.z));
                    Vector3 look = new Vector3(awning.transform.position.x, 2f, awning.transform.position.z);
                    cam.fieldOfView = 60f;
                    cam.transform.position = look + street * 12f + Vector3.Cross(Vector3.up, street) * 5f + Vector3.up * 0.2f;
                    cam.transform.LookAt(look + Vector3.up * 1.5f);
                    Capture(cam, "Logs/detail_facade.png");
                }
            }
        }

        /// <summary>Jede Himmel-Variante aus zwei Blickwinkeln: in die Sonne und die Startstrasse entlang.</summary>
        [MenuItem("DriftSkate/Tests/Himmel-Varianten rendern")]
        public static void RenderSkyVariants()
        {
            Directory.CreateDirectory("Logs");
            foreach (var preset in SkyLook.Presets)
            {
                EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
                SkyLook.Apply(preset);
                var cam = Camera.main;
                Vector3 pos = new Vector3(CityBuilder.BlockCenter(3), 1.8f, CityBuilder.RoadCenter(2) - 4f);
                cam.fieldOfView = 65f;
                cam.transform.position = pos;
                cam.transform.LookAt(pos + Vector3.ProjectOnPlane(preset.SunDirection, Vector3.up).normalized * 100f + Vector3.up * 18f);
                Capture(cam, $"Logs/sky_{preset.id}_sunset.png");
                cam.transform.position = new Vector3(-60, 70, -150);
                cam.transform.LookAt(new Vector3(0, 0, -20));
                cam.fieldOfView = 55f;
                Capture(cam, $"Logs/sky_{preset.id}_overview.png");
            }
        }

        /// <summary>Regen mit nassem, spiegelndem Boden bei Abend und Nacht.</summary>
        [MenuItem("DriftSkate/Tests/Regen rendern")]
        public static void RenderRain()
        {
            Directory.CreateDirectory("Logs");
            foreach (var id in new[] { "abend", "nacht" })
            {
                EditorSceneManager.OpenScene(ProjectBuilder.CityPath);
                var preset = SkyLook.Find(id);
                SkyLook.Apply(preset);
                var go = new GameObject("Weather");
                var weather = go.AddComponent<WeatherSystem>();
                var refl = go.AddComponent<PlanarReflection>();
                weather.SetInstant(1f, 1f);
                var cam = Camera.main;
                var views = new (string name, Vector3 pos, Vector3 target)[]
                {
                    ("street", new Vector3(CityBuilder.BlockCenter(3), 1.6f, CityBuilder.RoadCenter(2) - 4f), new Vector3(CityBuilder.BlockCenter(1), 6f, CityBuilder.RoadCenter(2) + 2f)),
                    ("plaza",  new Vector3(CityBuilder.BlockCenter(2) + 8f, 1.4f, -40f), new Vector3(CityBuilder.BlockCenter(2) - 10f, 3f, 0f)),
                };
                foreach (var v in views)
                {
                    cam.fieldOfView = 62f;
                    cam.transform.position = v.pos;
                    cam.transform.LookAt(v.target);
                    weather.Follow(cam.transform);
                    weather.SetInstant(1f, 1f);
                    refl.RenderNow();
                    Capture(cam, $"Logs/rain_{id}_{v.name}.png");
                }
                // Gewitter: Blitz vor der Kamera am Horizont, im hellsten Moment
                cam.transform.position = views[0].pos;
                cam.transform.LookAt(views[0].target);
                float bearing = Mathf.Atan2(cam.transform.forward.x, cam.transform.forward.z) * Mathf.Rad2Deg + 12f;
                weather.Follow(cam.transform);
                weather.StrikeForScreenshot(cam.transform, bearing, 330f, 1234);
                refl.RenderNow();
                Capture(cam, $"Logs/storm_{id}.png");
                Object.DestroyImmediate(go);
                Shader.SetGlobalFloat("_DS_Wet", 0f);
            }
        }

        static void RenderGarage()
        {
            EditorSceneManager.OpenScene(ProjectBuilder.GaragePath);
            var env = Object.FindAnyObjectByType<GarageEnvironment>();
            var profile = new PlayerProfile();
            profile.EnsureDefaults();
            env.Build(Catalog.Garages[1], profile);
            var def = Catalog.Car("roku86");
            CarBuilder.Build(env.carRoot, def, Palette.White, Palette.Pink, MakeTag(), null);
            SkaterBuilder.Build(env.skaterRoot, SkaterBuilder.Outfit.FromProfile(profile), Catalog.Boards[0], MakeTag(), null);
            var cam = Camera.main;
            var orbit = cam.GetComponent<OrbitCamera>();
            Quaternion rot = Quaternion.Euler(orbit.pitch, orbit.yaw, 0);
            cam.transform.SetPositionAndRotation(orbit.target - rot * Vector3.forward * orbit.distance, rot);
            cam.transform.position += cam.transform.right * orbit.screenOffset.x * orbit.distance;
            Capture(cam, "Logs/shot_garage.png");
        }

        static Texture2D MakeTag()
        {
            var t = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            GraffitiPainter.DrawDefaultTag(t);
            return t;
        }

        internal static void Capture(Camera cam, string path)
        {
            const int w = 1600, h = 900;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = old;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Debug.Log("[DriftSkate] Screenshot: " + path);
        }
    }
}
