using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DriftSkate
{
    public enum PlayerMode : byte { Driving, Skating }

    /// <summary>
    /// Ein Spieler = Auto + Skater. Steuert den Wechsel (Aussteigen, Bail-Out, Einsteigen, Auto rufen)
    /// und synchronisiert Aussehen und Zustand fuer andere Spieler. Der Besitzer simuliert alles selbst.
    /// </summary>
    public class PlayerAvatar : NetworkBehaviour
    {
        public static PlayerAvatar Local { get; private set; }

        [Header("Verweise (vom Builder gesetzt)")]
        public VehicleController car;
        public CarEffects carEffects;
        public DriftScorer driftScorer;
        public SkaterController skater;
        public ComboSystem combo;

        public PlayerMode Mode => (PlayerMode)_mode.Value;
        public string PlayerName => _name.Value.ToString();
        public string Prompt { get; private set; }

        /// <summary>Fuer automatische Tests: Eingaben kommen von aussen statt vom Controller.</summary>
        public bool externalControl;
        public float DistanceToCar => Vector3.Distance(skater.transform.position, car.transform.position);

        const NetworkVariableReadPermission R = NetworkVariableReadPermission.Everyone;
        const NetworkVariableWritePermission W = NetworkVariableWritePermission.Owner;

        readonly NetworkVariable<byte> _mode = new NetworkVariable<byte>(0, R, W);
        readonly NetworkVariable<int> _carIndex = new NetworkVariable<int>(0, R, W);
        readonly NetworkVariable<Color> _paint = new NetworkVariable<Color>(Color.white, R, W);
        readonly NetworkVariable<Color> _crew = new NetworkVariable<Color>(Color.magenta, R, W);
        readonly NetworkVariable<FixedString64Bytes> _boardId = new NetworkVariable<FixedString64Bytes>(default, R, W);
        readonly NetworkVariable<FixedString64Bytes> _characterId = new NetworkVariable<FixedString64Bytes>(default, R, W);
        readonly NetworkVariable<int> _outfit = new NetworkVariable<int>(0, R, W);
        readonly NetworkVariable<FixedString32Bytes> _name = new NetworkVariable<FixedString32Bytes>(default, R, W);
        readonly NetworkVariable<FixedString512Bytes> _design = new NetworkVariable<FixedString512Bytes>(default, R, W); // Folie, Teile, Felgen (CarDesign.Encode)
        readonly NetworkVariable<float> _steer = new NetworkVariable<float>(0f, R, W);
        readonly NetworkVariable<float> _rpm = new NetworkVariable<float>(900f, R, W);
        readonly NetworkVariable<float> _throttle = new NetworkVariable<float>(0f, R, W);
        readonly NetworkVariable<bool> _smoke = new NetworkVariable<bool>(false, R, W);
        readonly NetworkVariable<byte> _skaterState = new NetworkVariable<byte>(0, R, W);
        readonly NetworkVariable<bool> _wings = new NetworkVariable<bool>(false, R, W); // Engelsfluegel am Ruecken (Easter Egg)
        readonly NetworkVariable<int> _hitch = new NetworkVariable<int>(-1, R, W); // Skitchen: Auto + Haltepunkt (Hitchable.Encode), -1 = frei

        CarBuilder.Result _carParts;
        float _steerSmoothed;
        float _lastTrickTime;
        Texture2D _remoteGraffiti;
        Material _remoteTagMat;
        bool _remoteGraffitiCustom;
        // Graffiti-Uebertragung: PNG in Stuecken (passen sicher in ein Netzwerk-Paket)
        const int GraffitiChunk = 1000, GraffitiMaxBytes = 512 * 1024;
        byte[] _graffitiIn;
        int _graffitiInVersion = -1, _graffitiInCount, _graffitiVersion;
        NameTag _nameTag;

        // ------------------------------------------------------------------ Spawn

        public override void OnNetworkSpawn()
        {
            // An jedes Spieler-Auto kann man sich dranhaengen (ausser ans eigene), solange jemand faehrt
            var hitchable = car.GetComponent<Hitchable>() ?? car.gameObject.AddComponent<Hitchable>();
            hitchable.kind = Hitchable.KindPlayer;
            hitchable.id = (int)NetworkObjectId;
            hitchable.owner = this;
            hitchable.available = () => Mode == PlayerMode.Driving;

            if (IsOwner)
            {
                Local = this;
                var p = SaveSystem.Profile;
                _carIndex.Value = Catalog.CarIndex(p.selectedCar);
                _paint.Value = p.GetCarSave(p.selectedCar).paint;
                _design.Value = new FixedString512Bytes(p.GetCarSave(p.selectedCar).design.Encode());
                _crew.Value = p.crewColor;
                _boardId.Value = new FixedString64Bytes(Trim(p.selectedBoard, 60));
                _characterId.Value = new FixedString64Bytes(Trim(p.selectedCharacter ?? "", 60));
                _outfit.Value = PackOutfit(p.equipped);
                _name.Value = new FixedString32Bytes(Trim(p.playerName, 28));
                _mode.Value = (byte)PlayerMode.Driving;
                _wings.Value = p.angelWings;

                BuildLocal();
                car.SetLocal(true);
                skater.SetLocal(true);
                skater.combo = combo;
                driftScorer.combo = combo;
                carEffects.SetSpatial(true);
                skater.BailedEvent += _ => _lastTrickTime = 0f;
                skater.DoubleJumped += () => { WingsOnBack.Find(skater)?.Burst(); WingBurstRpc(); };
                combo.ActionAdded += (_, __) => _lastTrickTime = Time.time;

                var spawn = SpawnPoints.Get((int)OwnerClientId);
                car.Teleport(spawn.position, spawn.rotation);
                EnterCar(false);
                WingsOnBack.Set(skater, _wings.Value);

                if (CameraRig.Instance != null) CameraRig.Instance.Follow(this);
                if (HUD.Instance != null) HUD.Instance.Bind(this);
                ModLibrary.Changed += OnModsChanged;
                // Neues Graffiti gemalt: allen anderen schicken (wer spaeter dazukommt, fragt selbst nach)
                SaveSystem.GraffitiChanged += SendGraffitiToAll;
            }
            else
            {
                car.SetLocal(false);
                skater.SetLocal(false);
                combo.enabled = false;
                BuildRemote();
                _carIndex.OnValueChanged += (_, __) => BuildRemote();
                _paint.OnValueChanged += (_, __) => BuildRemote();
                _design.OnValueChanged += (_, __) => BuildRemote();
                _boardId.OnValueChanged += (_, __) => BuildRemote();
                _characterId.OnValueChanged += (_, __) => BuildRemote();
                ModLibrary.Changed += OnModsChanged;
                _outfit.OnValueChanged += (_, __) => BuildRemote();
                _crew.OnValueChanged += (_, __) => BuildRemote();
                _nameTag = NameTag.Create(transform, PlayerName);
                _name.OnValueChanged += (_, v) => { if (_nameTag != null) _nameTag.SetText(v.ToString()); };
                RequestGraffitiRpc();
                WingsOnBack.Set(skater, _wings.Value);
                _wings.OnValueChanged += (_, on) => WingsOnBack.Set(skater, on);
            }
        }

        /// <summary>Ein Mod wurde fertig geladen: Figur/Board neu aufbauen, falls er gebraucht wird.</summary>
        void OnModsChanged()
        {
            if (this == null || !IsSpawned) return;
            if (IsOwner) RebuildSkaterLocal();
            else BuildRemote();
        }

        public override void OnNetworkDespawn()
        {
            ModLibrary.Changed -= OnModsChanged;
            SaveSystem.GraffitiChanged -= SendGraffitiToAll;
            if (Local == this)
            {
                combo.BankNow();
                Local = null;
            }
        }

        static string Trim(string s, int max) => string.IsNullOrEmpty(s) ? "Player" : (s.Length > max ? s.Substring(0, max) : s);

        static int PackOutfit(string[] ids)
        {
            int packed = 0;
            for (int i = 0; i < 4; i++)
            {
                int idx = ids != null && i < ids.Length ? Catalog.OutfitIndex(ids[i]) : -1;
                packed |= ((idx + 1) & 0xFF) << (i * 8);
            }
            return packed;
        }

        static string[] UnpackOutfit(int packed)
        {
            var ids = new string[4];
            for (int i = 0; i < 4; i++)
            {
                int idx = ((packed >> (i * 8)) & 0xFF) - 1;
                ids[i] = idx >= 0 && idx < Catalog.Outfits.Count ? Catalog.Outfits[idx].id : null;
            }
            return ids;
        }

        void BuildLocal()
        {
            var p = SaveSystem.Profile;
            var def = Catalog.Car(p.selectedCar);
            var save = p.GetCarSave(def.id);
            car.ApplySetup(VehicleSetup.From(def, save.tuning, p.assistLevel, p.autoGearbox));
            _carParts = CarBuilder.Build(car.transform, def, save.paint, p.crewColor, SaveSystem.Graffiti, car, save.design);
            carEffects.Build(p.crewColor);
            RebuildSkaterLocal();
        }

        void RebuildSkaterLocal()
        {
            var p = SaveSystem.Profile;
            bool visible = Mode == PlayerMode.Skating;
            SkaterBuilder.Build(skater.transform, SkaterBuilder.Outfit.FromProfile(p), Catalog.Board(p.selectedBoard), SaveSystem.Graffiti, skater, p.selectedCharacter);
            SkaterBuilder.SetVisible(skater.transform, visible || !IsSpawned);
        }

        void BuildRemote()
        {
            if (_remoteGraffiti == null)
            {
                _remoteGraffiti = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                GraffitiPainter.DrawDefaultTag(_remoteGraffiti);
            }
            var def = Catalog.Cars[Mathf.Clamp(_carIndex.Value, 0, Catalog.Cars.Count - 1)];
            car.ApplySetup(VehicleSetup.From(def, null, 0, true));
            car.SetLocal(false);
            var design = CarDesign.Decode(_design.Value.ToString(), def.id, _crew.Value);
            _carParts = CarBuilder.Build(car.transform, def, _paint.Value, _crew.Value, _remoteGraffiti, car, design);
            carEffects.Build(_crew.Value);
            carEffects.SetSpatial(false);
            var board = Catalog.Board(_boardId.Value.ToString());
            SkaterBuilder.Build(skater.transform, SkaterBuilder.Outfit.FromIds(UnpackOutfit(_outfit.Value), _crew.Value), board, _remoteGraffiti, skater,
                _characterId.Value.ToString());
            _remoteSkaterVisible = !_remoteSkaterVisible; // Sichtbarkeit nach dem Neuaufbau neu anwenden
            ApplyRemoteVisibility();
        }

        // ------------------------------------------------------------------ Update

        void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner) UpdateOwner();
            else UpdateRemote();
        }

        void UpdateOwner()
        {
            if (GameInput.Pressed(GameInput.NextSong) && MusicPlayer.Instance != null) MusicPlayer.Instance.Next();

            if (Mode == PlayerMode.Driving) UpdateDriving();
            else UpdateSkating();

            _steer.Value = car.SteerAngle;
            _rpm.Value = car.EngineRpm;
            _throttle.Value = car.Throttle;
            _smoke.Value = car.SmokeAmount(2) > 0.35f || car.SmokeAmount(3) > 0.35f;
            _skaterState.Value = (byte)((byte)skater.State | (skater.Fakie ? FakieBit : 0));
            var hitched = skater.HitchTarget;
            _hitch.Value = hitched != null ? hitched.Encode(skater.HitchAnchor) : -1;
        }

        const byte FakieBit = 0x80; // im Zustands-Byte: faehrt rueckwaerts

        void UpdateDriving()
        {
            if (externalControl)
            {
                if (_carParts != null && _carParts.seat != null)
                    skater.transform.SetPositionAndRotation(_carParts.seat.position, car.transform.rotation);
                Prompt = car.Speed < 1.5f ? "AUSSTEIGEN" : "BAIL OUT!";
                return;
            }
            Vector2 move = GameInput.MoveValue;
            float dt = Time.deltaTime;
            bool analog = UnityEngine.InputSystem.Gamepad.current != null && Mathf.Abs(move.x) > 0.01f && Mathf.Abs(move.x) < 0.99f;
            if (analog) _steerSmoothed = move.x;
            else
            {
                float rate = Mathf.Abs(move.x) > Mathf.Abs(_steerSmoothed) || Mathf.Sign(move.x) != Mathf.Sign(_steerSmoothed) ? 4f : 6f;
                _steerSmoothed = Mathf.MoveTowards(_steerSmoothed, move.x, rate * dt);
            }

            car.input = new VehicleInput
            {
                steer = _steerSmoothed,
                throttle = GameInput.ThrottleValue,
                brake = GameInput.BrakeValue,
                handbrake = GameInput.Held(GameInput.Handbrake),
                clutch = GameInput.Held(GameInput.Clutch)
            };
            if (GameInput.Pressed(GameInput.ShiftUp)) car.QueueShift(1);
            if (GameInput.Pressed(GameInput.ShiftDown)) car.QueueShift(-1);

            // Skater sitzt unsichtbar im Auto
            if (_carParts != null && _carParts.seat != null)
                skater.transform.SetPositionAndRotation(_carParts.seat.position, car.transform.rotation);

            if (GameInput.Pressed(GameInput.ResetCar))
            {
                combo.Fail("RESET");
                car.ResetUpright();
            }

            Prompt = car.Speed < 1.5f ? "AUSSTEIGEN" : "BAIL OUT!";
            if (GameInput.Pressed(GameInput.Interact))
            {
                if (car.Speed < 1.5f) ExitCar();
                else BailOut();
            }
        }

        void UpdateSkating()
        {
            car.input = default;
            if (externalControl) { Prompt = null; return; }

            // Am Auto: F / Y laesst los (Ollie schleudert, das macht der Skater selbst)
            if (skater.State == SkaterState.Hitched)
            {
                Prompt = "LOSLASSEN   (OLLIE = SLINGSHOT)";
                if (GameInput.Pressed(GameInput.Interact)) skater.ReleaseHitch(false);
                return;
            }
            if (SprayShop.TryInteract(this)) { Prompt = "SPRAYDOSEN KAUFEN [E]"; return; }
            float dist = DistanceToCar;
            bool canEnter = dist < 3.5f && car.Speed < 4f && skater.State != SkaterState.Bailed;

            // NPC ansprechen (Jojo, Luna) hat Vorrang vor Einsteigen (sie stehen nah am Platz)
            var npc = QuestNpc.Near(skater.transform.position);
            if (npc != null && skater.Speed < 5f && (skater.State == SkaterState.Riding || skater.State == SkaterState.Walking))
            {
                Prompt = "MIT " + npc.Speaker + " REDEN";
                if (GameInput.Pressed(GameInput.Talk) || GameInput.Pressed(GameInput.Interact)) npc.Talk();
                return;
            }

            // Auto zum Dranhaengen in Reichweite (nur auf dem Board, nicht am eigenen Auto)
            Hitchable hitch = null;
            int hitchAnchor = -1;
            if (skater.State == SkaterState.Riding && !canEnter)
                hitch = Hitchable.FindNear(skater.transform.position, this, out hitchAnchor);

            var spot = TagSpot.FindNear(skater.transform.position, 4f);
            if (spot != null && !spot.TaggedByMe) Prompt = "SPRUEHEN";
            else if (canEnter) Prompt = "EINSTEIGEN";
            else if (hitch != null) Prompt = hitchAnchor == Hitchable.Rear ? "HINTEN DRANHAENGEN" : "SEITLICH DRANHAENGEN";
            else if (skater.State == SkaterState.Walking && WallTags.Find(skater, out _, out _, out _)) Prompt = "SPRUEHEN";
            else if (dist > 12f) Prompt = "AUTO RUFEN";
            else Prompt = null;

            if (GameInput.Pressed(GameInput.Interact) && canEnter) EnterCar(true);
            else if (GameInput.Pressed(GameInput.Interact) && hitch != null) skater.StartHitch(hitch, hitchAnchor);

            if (GameInput.Pressed(GameInput.Spray)) TrySpray();

            if (GameInput.Pressed(GameInput.ResetCar))
            {
                if (dist > 12f) CallCar();
                else if (skater.State == SkaterState.Bailed || skater.transform.position.y < -5f)
                    skater.Place(car.transform.TransformPoint(new Vector3(2.5f, 0.2f, 0f)), skater.Heading);
            }
        }

        /// <summary>
        /// Graffiti spruehen: auf den naechsten freien Spot (auf dem Board oder zu Fuss, nicht in der Luft),
        /// zu Fuss sonst an die Wand, vor der man steht.
        /// </summary>
        public bool TrySpray()
        {
            if (SaveSystem.Profile.sprayCans <= 0)
            {
                HUD.Instance?.Toast("Keine Spraydosen mehr! Nachschub bei CAN CLUB.");
                return false;
            }
            var spot = TagSpot.FindNear(skater.transform.position, 4f);
            if (spot == null || spot.TaggedByMe || (skater.State != SkaterState.Riding && skater.State != SkaterState.Walking))
                return skater.State == SkaterState.Walking && TryWallTag();
            spot.ApplyLocal(SaveSystem.Graffiti);
            SaveSystem.Profile.sprayCans--;
            SaveSystem.Save();
            SpraySound.Play(1f, false);
            combo.AddAction("TAG!", 500f);
            TagRpc(spot.Index, SaveSystem.Profile.crewColor);
            return true;
        }

        void UpdateRemote()
        {
            car.SetRemoteState(_steer.Value, _rpm.Value, _throttle.Value, _smoke.Value);
            car.hasDriver = Mode == PlayerMode.Driving;
            skater.RemoteState = (SkaterState)(_skaterState.Value & ~FakieBit);
            skater.RemoteFakie = (_skaterState.Value & FakieBit) != 0;
            skater.RemoteHitch = Hitchable.Decode(_hitch.Value, out int remoteAnchor);
            skater.RemoteHitchAnchor = remoteAnchor;
            ApplyRemoteVisibility();
            if (_nameTag != null)
            {
                _nameTag.target = Mode == PlayerMode.Driving ? car.transform : skater.transform;
                _nameTag.height = Mode == PlayerMode.Driving ? 2.4f : 2.3f;
            }
        }

        bool _remoteSkaterVisible = true;

        void ApplyRemoteVisibility()
        {
            bool visible = Mode == PlayerMode.Skating;
            if (visible == _remoteSkaterVisible) return;
            _remoteSkaterVisible = visible;
            SkaterBuilder.SetVisible(skater.transform, visible);
            var col = skater.GetComponent<Collider>();
            if (col != null) col.enabled = visible;
        }

        // ------------------------------------------------------------------ Wechsel

        void EnterCar(bool fromSkating)
        {
            if (fromSkating && combo.Active)
            {
                bool stylish = skater.State == SkaterState.Air || Time.time - _lastTrickTime < 0.6f;
                combo.AddAction(stylish ? "HOP IN!" : "EINSTEIGEN", stylish ? 400f : 100f);
            }
            skater.Hide();
            SkaterBuilder.SetVisible(skater.transform, false);
            car.hasDriver = true;
            _mode.Value = (byte)PlayerMode.Driving;
        }

        void ExitCar()
        {
            Vector3 pos = FindExitPosition();
            if (combo.Active) combo.AddAction("AUSSTEIGEN", 100f);
            skater.Place(pos, car.transform.eulerAngles.y);
            SkaterBuilder.SetVisible(skater.transform, true);
            car.hasDriver = false;
            car.input = default;
            _mode.Value = (byte)PlayerMode.Skating;
        }

        void BailOut()
        {
            Transform t = car.transform;
            Vector3 v = car.Body.linearVelocity;
            Vector3 pos = t.TransformPoint(new Vector3(car.setup.track * 0.5f + 0.9f, 1.1f, 0f));
            // Gelaender sind fest: nicht in einer Stange starten
            if (Physics.CheckSphere(pos, 0.4f, ~LayerMask.GetMask("Car", "Skater", "Ignore Raycast"), QueryTriggerInteraction.Ignore))
                pos = t.position + Vector3.up * 2.4f;
            Vector3 launch = v * 0.85f + t.right * 2.5f + Vector3.up * 4.2f;
            // Blick in die tatsaechliche Flugrichtung (inkl. Seitwaerts-Schwung), sonst landet man schraeg
            Vector3 flat = Vector3.ProjectOnPlane(launch, Vector3.up);
            float heading = flat.sqrMagnitude > 1f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : t.eulerAngles.y;
            skater.LaunchFromCar(pos, launch, heading);
            SkaterBuilder.SetVisible(skater.transform, true);
            car.hasDriver = false;
            car.input = default;
            _mode.Value = (byte)PlayerMode.Skating;
            if (combo.Active) combo.Hold();
        }

        Vector3 FindExitPosition()
        {
            // Boden ohne Gelaender suchen, aber auch nicht in einem Gelaender absetzen (die sind fest)
            int ground = ~LayerMask.GetMask("Car", "Skater", "Ignore Raycast", "Rail");
            int solid = ~LayerMask.GetMask("Car", "Skater", "Ignore Raycast");
            Vector3[] candidates =
            {
                _carParts != null && _carParts.exitPoint != null ? _carParts.exitPoint.position : car.transform.TransformPoint(new Vector3(2f, 0.1f, 0f)),
                car.transform.TransformPoint(new Vector3(-2f, 0.1f, 0f)),
                car.transform.TransformPoint(new Vector3(0f, 0.1f, -3.4f)),
                car.transform.TransformPoint(new Vector3(0f, 0.1f, 3.4f)),
            };
            foreach (var c in candidates)
            {
                if (Physics.CheckCapsule(c + Vector3.up * 0.5f, c + Vector3.up * 1.6f, 0.3f, solid, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.Raycast(c + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 4f, ground, QueryTriggerInteraction.Ignore))
                    return hit.point;
            }
            return car.transform.position + Vector3.up * 2.2f;
        }

        void CallCar()
        {
            int mask = ~LayerMask.GetMask("Car", "Skater", "Ignore Raycast", "Rail");
            int solid = ~LayerMask.GetMask("Car", "Skater", "Ignore Raycast");
            Transform s = skater.transform;
            Vector3[] offsets = { s.right * 5f, -s.right * 5f, -s.forward * 6f, s.forward * 6f, s.right * 9f, -s.right * 9f };
            foreach (var o in offsets)
            {
                Vector3 c = s.position + o;
                if (!Physics.Raycast(c + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 12f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.9f) continue;
                Vector3 pos = hit.point + Vector3.up * 0.3f;
                if (Physics.CheckBox(pos + Vector3.up * 0.9f, new Vector3(1.1f, 0.6f, 2.5f), Quaternion.Euler(0, skater.Heading, 0), solid, QueryTriggerInteraction.Ignore)) continue;
                car.Teleport(pos, Quaternion.Euler(0, skater.Heading, 0));
                return;
            }
            HUD.Instance?.Toast("Kein Platz fuer das Auto");
        }

        // ------------------------------------------------------------------ Test-Schnittstelle

        /// <summary>Wie die Taste F / Y: aussteigen, Bail-Out oder einsteigen.</summary>
        public bool TestInteract()
        {
            if (Mode == PlayerMode.Driving)
            {
                if (car.Speed < 1.5f) ExitCar(); else BailOut();
                return true;
            }
            if (DistanceToCar < 3.5f && car.Speed < 4f && skater.State != SkaterState.Bailed)
            {
                EnterCar(true);
                return true;
            }
            return false;
        }

        bool TryWallTag()
        {
            if (!WallTags.Find(skater, out Vector3 point, out Vector3 normal, out float size)) return false;
            SaveSystem.Profile.sprayCans--;
            SaveSystem.Save();
            Color crew = SaveSystem.Profile.crewColor;
            if (WallTags.PlaceLocal(point, normal, size, crew)) combo.AddAction("WALL TAG", 150f);
            SpraySound.Play(1f, false);
            WallTagRpc(point, normal, size, crew);
            return true;
        }

        public void TestCallCar() => CallCar();

        /// <summary>Fuer Tests: wie F / Y neben einem fremden Auto (dranhaengen).</summary>
        public bool TestHitch()
        {
            if (skater.State != SkaterState.Riding) return false;
            var h = Hitchable.FindNear(skater.transform.position, this, out int anchor);
            return h != null && skater.StartHitch(h, anchor);
        }

        /// <summary>Startklar fuer einen Spielmodus: im Auto bzw. auf dem Board an den Start stellen (laufende Combo wird gutgeschrieben).</summary>
        public void PlaceForChallenge(bool driving, Vector3 position, float heading)
        {
            combo.BankNow();
            if (driving)
            {
                car.Teleport(position + Vector3.up * 0.3f, Quaternion.Euler(0f, heading, 0f));
                if (Mode != PlayerMode.Driving) EnterCar(false);
                return;
            }
            if (Mode == PlayerMode.Driving)
            {
                car.hasDriver = false;
                car.input = default;
                SkaterBuilder.SetVisible(skater.transform, true);
                _mode.Value = (byte)PlayerMode.Skating;
            }
            skater.Place(position + Vector3.up * 0.1f, heading);
        }

        // ------------------------------------------------------------------ Netzwerk-Aktionen

        [Rpc(SendTo.NotMe)]
        void WallTagRpc(Vector3 point, Vector3 normal, float size, Color color) => WallTags.PlaceRemote(point, normal, size, color, RemoteTagMaterial);

        [Rpc(SendTo.NotMe)]
        void TagRpc(int spotIndex, Color color)
        {
            var spot = TagSpot.ByIndex(spotIndex);
            if (spot != null) spot.ApplyRemote(color, RemoteTagMaterial, _remoteGraffiti);
        }

        /// <summary>Engelsfluegel anlegen oder abnehmen (auch fuer die anderen Spieler).</summary>
        public void SetWings(bool on)
        {
            if (!IsOwner) return;
            _wings.Value = on;
            WingsOnBack.Set(skater, on);
        }

        public bool HasWings => _wings.Value;

        /// <summary>Doppelsprung eines anderen Spielers: sein Fluegelschlag auch hier.</summary>
        [Rpc(SendTo.NotMe)]
        void WingBurstRpc() => WingsOnBack.Find(skater)?.Burst();

        // ------------------------------------------------------------------ Eigenes Graffiti fuer die anderen

        /// <summary>Hat dieser (fremde) Spieler sein eigenes Graffiti schon geschickt?</summary>
        public bool HasRemoteGraffiti => _remoteGraffitiCustom;
        public Texture2D RemoteGraffiti => _remoteGraffiti;

        /// <summary>Material fuer die Tags dieses Spielers, sobald sein Graffiti da ist (vorher null = Standard-Tag).</summary>
        Material RemoteTagMaterial
        {
            get
            {
                if (!_remoteGraffitiCustom || _remoteGraffiti == null) return null;
                if (_remoteTagMat == null) _remoteTagMat = WallTags.Decal(_remoteGraffiti);
                return _remoteTagMat;
            }
        }

        void SendGraffitiToAll()
        {
            if (this == null || !IsSpawned || !IsOwner) return;
            _graffitiVersion++;
            SendGraffiti(RpcTarget.NotMe);
        }

        void SendGraffiti(BaseRpcTarget target)
        {
            byte[] png = SaveSystem.Graffiti.EncodeToPNG();
            if (png == null || png.Length == 0 || png.Length > GraffitiMaxBytes) return;
            for (int offset = 0; offset < png.Length; offset += GraffitiChunk)
            {
                int n = Mathf.Min(GraffitiChunk, png.Length - offset);
                var part = new byte[n];
                System.Array.Copy(png, offset, part, 0, n);
                GraffitiChunkRpc(_graffitiVersion, png.Length, offset, part, target);
            }
        }

        /// <summary>Ein anderer Spieler (gerade beigetreten) fragt beim Besitzer nach dessen Graffiti.</summary>
        [Rpc(SendTo.Owner)]
        void RequestGraffitiRpc(RpcParams rpcParams = default)
        {
            SendGraffiti(RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Persistent));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void GraffitiChunkRpc(int version, int total, int offset, byte[] data, RpcParams rpcParams)
        {
            if (IsOwner || total <= 0 || total > GraffitiMaxBytes || data == null || offset < 0 || offset + data.Length > total) return;
            if (version != _graffitiInVersion || _graffitiIn == null || _graffitiIn.Length != total)
            {
                _graffitiIn = new byte[total];
                _graffitiInVersion = version;
                _graffitiInCount = 0;
            }
            System.Array.Copy(data, 0, _graffitiIn, offset, data.Length);
            _graffitiInCount += data.Length;
            if (_graffitiInCount < total) return;

            if (_remoteGraffiti == null) _remoteGraffiti = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            // In dieselbe Textur laden: Auto-Folie, Board und schon gespruehte Tags zeigen sofort das neue Bild
            if (_remoteGraffiti.LoadImage(_graffitiIn) && _remoteGraffiti.width <= 1024 && _remoteGraffiti.height <= 1024)
            {
                _remoteGraffiti.wrapMode = TextureWrapMode.Clamp;
                _remoteGraffitiCustom = true;
            }
            else GraffitiPainter.DrawDefaultTag(_remoteGraffiti);
            _graffitiIn = null;
        }
    }
}
