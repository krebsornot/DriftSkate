using System;
using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    public enum SkaterState : byte { Riding, Air, Grinding, Bailed, Hidden, Walking, WallPlant, WallRide, Hitched }

    /// <summary>
    /// Arcade-Skaten per Knopf: Pushen, Ollie (halten und loslassen, geduckt nimmt man Tempo auf), Flip- und Grab-Tricks,
    /// Spins, Grinds, Manuals, Wallplants, Wallrides, Stuerze. Tricks werden erst bei sauberer Landung gutgeschrieben.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SkaterController : MonoBehaviour
    {
        enum FlipKind { None, Kickflip, Heelflip, Shuvit, Impossible, Hardflip }

        const float GroundProbeHeight = 0.6f, ProbeRadius = 0.22f, StandDistance = GroundProbeHeight - ProbeRadius;
        const float FlipDuration = 0.42f;
        static float Gravity => 9.81f * 1.35f * (Admin.Moon ? 0.4f : 1f);

        public bool isLocal = true;
        public bool inputEnabled = true;
        public ComboSystem combo;
        public BoardDef board = Catalog.Boards[0];

        [Header("Visuals (vom SkaterBuilder gesetzt)")]
        public Transform align, boardPivot, bodyPivot;
        /// <summary>Hoehe des BoardPivots beim Grinden (Hanger auf der Rail) und beim Boardslide (Deck auf der Rail).</summary>
        public float grindBoardY = 0.1f, slideBoardY = 0.1f;
        public SkaterRig rig;

        [Header("Fahrgefuehl")]
        public float pushDuration = 0.62f;      // Dauer eines Abstossens (s)
        public float pushAccel = 15f;           // Beschleunigung waehrend der Fuss am Boden ist (m/s^2)
        public float maxPushSpeed = 19f;        // so schnell wird man durch Pushen allein (m/s)
        public float rollingFriction = 0.12f;   // Ausrollen (m/s^2)
        public float airDrag = 0.00175f;        // Luftwiderstand
        public float crouchAccel = 4.5f;          // geduckt (Ollie gehalten) Tempo aufbauen wie in Tony Hawk's (m/s^2)

        [Header("Wallplant")]
        public float plantWindow = 0.9f;        // so lange haengt man an der Wand und kann abspringen (s)
        public float plantMinTime = 0.12f;      // kuerzeste Zeit an der Wand (Optik)
        public float plantPop = 6.2f;           // Absprung nach oben (m/s)
        public float plantPush = 0.75f;         // Anteil des Anlauftempos, mit dem man von der Wand weg fliegt
        public float plantBoost = 8f;           // mindestens so schnell weg von der Wand (reicht ueber eine Gasse zur naechsten Wand)
        public float plantChargeTime = 1f;      // Ollie an der Wand so lange halten = voll aufgeladen (s)
        public float plantChargeBoost = 4.5f;   // voll aufgeladen: so viel schneller weg (m/s), dazu hoeher
        public float plantChainBoost = 0.8f;    // jeder weitere Wallplant ohne Landung gibt etwas mehr Schwung (m/s)

        [Header("Wallride (Grind halten, schraeg an eine Wand)")]
        public float rideMinSpeed = 4f;         // so schnell muss man an der Wand entlang sein (m/s)
        public float rideLift = 2.2f;           // beim Ansetzen etwas nach oben (m/s), dann ein weicher Bogen
        public float rideGravity = 0.4f;        // Anteil der Schwerkraft an der Wand (spaeter mehr)
        public float rideMaxTime = 2.6f;        // laenger haelt man sich nicht
        public float rideJumpOut = 4.2f;        // Absprung (Wallie): weg von der Wand (m/s)
        public float rideJumpUp = 6f;           // und nach oben (m/s)

        [Header("Doppelsprung (nur mit Engelsfluegeln)")]
        public bool canDoubleJump;              // wird von WingsOnBack gesetzt
        public float doubleJumpUp = 6.8f;       // zweiter Sprung in der Luft (m/s nach oben)

        [Header("Grind")]
        public float grindAccel = 3.5f;         // auf der Rail wird man schneller (m/s^2)
        public float grindMaxSpeed = 26f;       // Obergrenze beim Grinden (m/s)
        public float grindJumpBoost = 1.25f;    // Tempo-Faktor beim Abspringen per Ollie aus dem Grind
        public float sideGrip = 9f;             // wie schnell seitliches Rutschen abgebaut wird

        [Header("Zu Fuss (Taste B)")]
        public float walkSpeed = 2.4f;          // Gehen (m/s)
        public float runSpeed = 6f;             // Rennen mit Shift / LB (m/s)
        public float walkAccel = 14f;           // Anlaufen und Abbremsen (m/s^2)
        public float walkTurn = 210f;           // Drehen im Stand bzw. beim Gehen (Grad/s)
        public float walkJump = 4.6f;           // Sprung zu Fuss (m/s nach oben)

        public SkaterState State { get; private set; } = SkaterState.Riding;
        public float Heading { get; private set; }
        public float Speed => _rb != null && !_rb.isKinematic ? Vector3.ProjectOnPlane(_rb.linearVelocity, Vector3.up).magnitude : _grindSpeed;
        public bool Manualing { get; private set; }
        public bool Grabbing => _grabbing;
        public float Balance { get; private set; }
        public bool ShowBalance => State == SkaterState.Grinding || Manualing;
        public float OllieCharge { get; private set; }
        public bool LandPromptActive { get; private set; }
        public bool AwaitingBailOutLanding => _bailOutLanding || _bailOutGrace > 0f;
        public string CurrentTrick { get; private set; }
        public Vector3 GroundNormal { get; private set; } = Vector3.up;

        /// <summary>
        /// Rueckwaerts fahren (Fakie): Board und Figur sind gegen die Fahrtrichtung gedreht. Heading bleibt die
        /// Fahrtrichtung, nur die Optik (Align) wird um 180 Grad gedreht.
        /// </summary>
        public bool Fakie { get; private set; }

        /// <summary>Drehung in der Luft seit dem Absprung (Grad, fuer Tests).</summary>
        public float AirSpin => _airSpin;

        /// <summary>Fuer Netzwerk-Optik: aktueller Zustand.</summary>
        public SkaterState RemoteState { get; set; }
        public bool RemoteFakie { get; set; }
        /// <summary>Entfernter Spieler haengt an diesem Auto (vom PlayerAvatar aus dem Netzwerk gesetzt).</summary>
        public Hitchable RemoteHitch { get; set; }
        public int RemoteHitchAnchor { get; set; }

        /// <summary>Auto, an dem man gerade haengt (Skitchen), sonst null.</summary>
        public Hitchable HitchTarget => State == SkaterState.Hitched ? _hitch : null;
        public int HitchAnchor => _hitchAnchor;

        public event Action<string> BailedEvent;
        /// <summary>Doppelsprung ausgeloest (Fluegelschlag, auch fuer die anderen Spieler).</summary>
        public event Action DoubleJumped;
        public bool DoubleJumpUsed => _doubleJumpUsed;

        Rigidbody _rb;
        CapsuleCollider _col;
        int _groundMask, _railLayer;

        // Gelaender sind fest: oben auf einer Stange gelandet, ohne zu grinden (aus OnCollisionStay)
        bool _onRailTop;
        Vector3 _railTopPoint;

        // Eingabe (in Update gesammelt, in FixedUpdate verbraucht)
        Vector2 _move;
        bool _ollieHeld, _olliePressed, _ollieReleased, _flipPressed, _grindHeld, _grindPressed, _grabHeld, _grabPressed, _manualHeld, _boardPressed;
        bool _doubleJumpUsed;
        float _grindBuffer; // Grind kurz vor der Wand angetippt: zaehlt noch fuer den Wallride
        float _walkAirTime;

        // Luft
        FlipKind _flip;
        float _flipT, _airSpin, _airTime;
        bool _grabbing;
        string _grabName;
        float _grabTime;
        readonly List<(string name, float points)> _pending = new List<(string, float)>();

        // Grind
        GrindRail _rail;
        float _railS, _railDir, _grindSpeed, _balanceVel;
        string _grindName;

        // Wallplant
        Vector3 _wallNormal, _prevAirFlat;
        float _plantTime, _plantSpeed, _plantCooldown, _lastAirOllie = -1f, _plantCharge;
        bool _plantCharging;
        int _plantChain;

        // Wallride
        Vector3 _rideNormal, _rideDir, _lastRideNormal = Vector3.up;
        float _rideTime, _rideSpeed, _rideVy, _rideCooldown, _rideBlend;
        ParticleSystem _rideDust;
        const float RideGap = 0.42f; // Abstand Skater-Mitte zur Wand

        /// <summary>Wand-Normale beim Wallride (von der Wand weg).</summary>
        public Vector3 RideNormal => _rideNormal;
        public float RideTime => _rideTime;
        /// <summary>0..1, wie weit Koerper und Board gerade an die Wand gekippt sind (fuer Kamera-Neigung).</summary>
        public float RideBlend => _rideBlend;
        public Vector3 RideTiltNormal => _lastRideNormal;

        /// <summary>Wallplants hintereinander ohne Landung (Wand zu Wand).</summary>
        public int PlantChain => _plantChain;
        /// <summary>Ollie an der Wand gehalten: Ladung 0..1 (-1 = laedt nicht).</summary>
        public float PlantCharge => _plantCharging ? _plantCharge : -1f;

        /// <summary>Wandrichtung (von der Wand weg) waehrend eines Wallplants.</summary>
        public Vector3 WallNormal => _wallNormal;
        /// <summary>Zeit an der Wand (fuer Tests und Optik).</summary>
        public float PlantTime => _plantTime;

        // Skitchen: am Auto festhalten
        Hitchable _hitch;
        int _hitchAnchor;
        float _hitchSway, _hitchTime;
        public const float HitchMaxSpeed = 30f; // schneller rollt man nach dem Loslassen nicht weiter (m/s)

        // Boden folgen ohne Magnet (StepRiding): letzte Fallgeschwindigkeit entlang des Bodens, Hoehen-Annaeherung
        float _lastSlopeVy, _fallVy;

        // Fahrgefuehl
        // Landung: 0 = gerade aufgesetzt, 1 = voller Grip (siehe LandSettleTime)
        float _landSettle = 1f, _landLossRate;
        const float LandSettleTime = 0.25f;
        float _turnVel, _spinVel, _pushPhase = -1f, _brake, _landAbsorb, _popTimer, _lean;

        /// <summary>-1 = kein Push, sonst Phase 0..1 eines Abstossens.</summary>
        public float PushPhase => _pushPhase;

        // Sturz / Bail-Out
        float _bailTimer;
        bool _bailOutLanding, _landPromptHit, _ollieLock, _manualLock, _steerLock, _revertLock;
        /// <summary>
        /// Grab (K / RB) ist in der Luft Grab, am Boden Revert. Ein Grab-Druck kurz vor der Landung kommt oft erst nach
        /// dem Aufsetzen an und drehte dann das Board ungewollt aus Fakie zurueck: Revert erst so lange nach der Landung.
        /// </summary>
        const float RevertDelay = 0.3f;
        int _earlyPresses;
        float _lastEarlyPress = -1f, _bailOutGrace, _landTime;

        // Bail-Out-Timing: Prompt so lange vor dem Aufsetzen, dass man mit normaler Reaktionszeit trifft,
        // dazu etwas Nachsicht davor und danach
        const float PromptTime = 0.65f, EarlyGrace = 0.25f, LateGrace = 0.2f;
        Vector3 _lastSafePos;
        float _safeTimer;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _col = GetComponent<CapsuleCollider>();
            _rb.useGravity = false;
            _rb.freezeRotation = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _groundMask = ~LayerMask.GetMask("Skater", "Ignore Raycast", "Rail");
            _railLayer = LayerMask.NameToLayer("Rail");
            Heading = transform.eulerAngles.y;
            _lastSafePos = transform.position;
            if (GetComponent<HitchGlue>() == null) gameObject.AddComponent<HitchGlue>().skater = this;
        }

        public void SetLocal(bool local)
        {
            isLocal = local;
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = !local || State == SkaterState.Hidden || State == SkaterState.Grinding;
            _rb.interpolation = local ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
        }

        // ---------------------------------------------------------------- Steuerung von aussen

        /// <summary>Im Auto: unsichtbar, ohne Physik.</summary>
        public void Hide()
        {
            _hitch = null;
            State = SkaterState.Hidden;
            _rb.isKinematic = true;
            if (_col != null) _col.enabled = false;
            Manualing = false;
            _grabbing = false;
            _bailOutLanding = false;
            _bailOutGrace = 0f;
            LandPromptActive = false;
        }

        /// <summary>Normal aussteigen: steht neben dem Auto, ohne Tempo.</summary>
        public void Place(Vector3 position, float heading)
        {
            ResetPhysics(position, heading);
            State = SkaterState.Riding;
            _rb.linearVelocity = Vector3.zero;
        }

        /// <summary>Bail-Out waehrend der Fahrt: rausfallen, rechtzeitig Ollie druecken, um sauber zu landen.</summary>
        public void LaunchFromCar(Vector3 position, Vector3 velocity, float heading)
        {
            ResetPhysics(position, heading);
            State = SkaterState.Air;
            _rb.linearVelocity = velocity;
            _airTime = 0f;
            _airSpin = 0f;
            _pending.Clear();
            _spinVel = 0f;
            _bailOutLanding = true;
            _bailOutGrace = 0f;
            _landPromptHit = false;
            _earlyPresses = 0;
            _lastEarlyPress = -1f;
        }

        void ResetPhysics(Vector3 position, float heading)
        {
            _hitch = null;
            if (_col != null) _col.enabled = true;
            _rb.interpolation = isLocal ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            _rb.isKinematic = !isLocal;
            Heading = heading;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0, heading, 0));
            _rb.position = position;
            _rb.rotation = Quaternion.Euler(0, heading, 0);
            _flip = FlipKind.None;
            _grabbing = false;
            Manualing = false;
            _rail = null;
            OllieCharge = 0f;
            CurrentTrick = null;
            Fakie = false;
            _bailOutGrace = 0f;
            _steerLock = false;
            _revertLock = false;
            _landSettle = 1f;
            _alignWorld = Quaternion.Euler(0, heading, 0); // nach dem Absetzen sofort richtig ausgerichtet
            _alignInit = true;
            _lastSlopeVy = _fallVy = 0f;
            _plantChain = 0;
            _plantCharging = false;
            _doubleJumpUsed = false;
            RideDust(false);
        }

        // ---------------------------------------------------------------- Eingabe

        bool _injected;

        /// <summary>Fuer automatische Tests: Eingaben wie vom Controller setzen (Druck/Loslassen wird erkannt).</summary>
        /// <summary>Fuer Tests: wie die Taste B (absteigen bzw. wieder aufs Board).</summary>
        public void InjectBoardToggle() => _boardPressed = true;

        public void InjectInput(Vector2 move, bool ollie, bool flip = false, bool grab = false, bool grind = false, bool manual = false)
        {
            _injected = true;
            _move = move;
            if (ollie && !_ollieHeld) _olliePressed = true;
            if (!ollie && _ollieHeld) _ollieReleased = true;
            _ollieHeld = ollie;
            _flipPressed |= flip;
            if (grab && !_grabHeld) _grabPressed = true;
            _grabHeld = grab;
            if (grind && !_grindHeld) _grindPressed = true;
            _grindHeld = grind;
            _manualHeld = manual;
        }

        void Update()
        {
            if (isLocal && inputEnabled && State != SkaterState.Hidden)
            {
                _move = GameInput.MoveValue;
                _ollieHeld = GameInput.Held(GameInput.Ollie);
                _olliePressed |= GameInput.Pressed(GameInput.Ollie);
                _ollieReleased |= GameInput.Released(GameInput.Ollie);
                _flipPressed |= GameInput.Pressed(GameInput.Flip);
                _grindHeld = GameInput.Held(GameInput.Grind);
                _grindPressed |= GameInput.Pressed(GameInput.Grind);
                _boardPressed |= GameInput.Pressed(GameInput.Board);
                _grabHeld = GameInput.Held(GameInput.Grab);
                _grabPressed |= GameInput.Pressed(GameInput.Grab);
                _manualHeld = GameInput.Held(GameInput.Manual);
            }
            else if (!_injected)
            {
                _move = Vector2.zero;
                _ollieHeld = _grindHeld = _grabHeld = _manualHeld = false;
            }
            Animate(Time.deltaTime);
        }

        void FixedUpdate()
        {
            if (!isLocal) return;
            float dt = Time.fixedDeltaTime;
            switch (State)
            {
                case SkaterState.Riding: StepRiding(dt); break;
                case SkaterState.Air: StepAir(dt); break;
                case SkaterState.Grinding: StepGrind(dt); break;
                case SkaterState.Bailed: StepBailed(dt); break;
                case SkaterState.Walking: StepWalking(dt); break;
                case SkaterState.WallPlant: StepWallPlant(dt); break;
                case SkaterState.WallRide: StepWallRide(dt); break;
                case SkaterState.Hitched: StepHitched(dt); break;
            }
            _plantCooldown = Mathf.Max(0f, _plantCooldown - dt);
            _rideCooldown = Mathf.Max(0f, _rideCooldown - dt);
            _olliePressed = _ollieReleased = _flipPressed = _grabPressed = _grindPressed = false;
            _onRailTop = false;
            _boardPressed = false;

            if (State != SkaterState.Hidden && transform.position.y < -25f)
            {
                Place(_lastSafePos + Vector3.up * 0.5f, Heading);
            }
        }

        bool ProbeGround(out RaycastHit hit, float extra)
        {
            Vector3 origin = _rb.position + Vector3.up * GroundProbeHeight;
            bool found = Physics.SphereCast(origin, ProbeRadius, Vector3.down, out hit, StandDistance + extra, _groundMask, QueryTriggerInteraction.Ignore);
            return found && hit.normal.y > 0.45f;
        }

        // ---------------------------------------------------------------- Fahren

        void StepRiding(float dt)
        {
            if (!ProbeGround(out RaycastHit hit, 0.25f))
            {
                EnterAir();
                return;
            }
            if (_boardPressed && _bailOutGrace <= 0f)
            {
                StartWalking();
                return;
            }

            // Bail-Out: Landetaste kurz nach dem Aufsetzen zaehlt noch
            if (_bailOutGrace > 0f)
            {
                if (_olliePressed || _ollieHeld)
                {
                    _bailOutGrace = 0f;
                    LandPromptActive = false;
                    _ollieLock = true; // dieser Druck ist die Landung, kein Ollie
                    combo?.AddAction("BAIL OUT!", 800f);
                }
                else
                {
                    _bailOutGrace -= dt;
                    if (_bailOutGrace <= 0f) { Bail("BAIL-OUT VERPATZT"); return; }
                }
            }

            Vector3 n = hit.normal;
            GroundNormal = Vector3.Slerp(GroundNormal, n, 1f - Mathf.Exp(-15f * dt));
            // Nach der Landung: Grip und Stabilisierung weich hochfahren statt sofort voll anzuheften
            _landSettle = Mathf.MoveTowards(_landSettle, 1f, dt / LandSettleTime);
            float settle = Smooth01(_landSettle);
            Vector3 fwd = Vector3.ProjectOnPlane(Quaternion.Euler(0, Heading, 0) * Vector3.forward, n).normalized;
            Vector3 right = Vector3.Cross(n, fwd);

            Vector3 vel = Vector3.ProjectOnPlane(_rb.linearVelocity, n);
            float along = Vector3.Dot(vel, fwd);
            float lat = Vector3.Dot(vel, right);

            // Hangabtrieb: bergab wird man schneller, quer zum Hang rutscht man leicht
            Vector3 slope = Vector3.ProjectOnPlane(Vector3.down * Gravity, n);
            along += Vector3.Dot(slope, fwd) * dt;
            if (_landSettle < 1f) along = Mathf.MoveTowards(along, 0f, _landLossRate * dt); // Tempoverlust einer schraegen Landung, verteilt
            lat += Vector3.Dot(slope, right) * dt * 0.5f;

            float turbo = Admin.Turbo ? 1.8f : 1f;
            float maxPush = maxPushSpeed * board.speed * turbo;
            bool crouching = _ollieHeld;
            // Geduckt (Ollie gehalten) nimmt man gleichmaessig Tempo auf; eine noch vom Auto gehaltene Leertaste zaehlt nicht
            bool crouchBoost = crouching && !_ollieLock && !Manualing && _move.y > -0.3f && _bailOutGrace <= 0f;

            // Abstossen in einzelnen Zuegen: Fuss runter, schieben, Fuss zurueck aufs Board
            bool wantPush = _move.y > 0.25f && !Manualing && !crouching && along < maxPush;
            if (_pushPhase >= 0f)
            {
                _pushPhase += dt / pushDuration;
                bool footOnGround = _pushPhase > 0.2f && _pushPhase < 0.65f;
                if (footOnGround && along < maxPush)
                {
                    float room = Mathf.Clamp01(1f - along / maxPush);
                    along += pushAccel * turbo * board.speed * Mathf.Max(0.4f, _move.y) * (0.35f + 0.65f * room) * dt;
                }
                if (_pushPhase >= 1f) _pushPhase = wantPush ? 0f : -1f;
            }
            else if (wantPush) _pushPhase = 0f;
            if (crouching || Manualing) _pushPhase = -1f;
            if (crouchBoost && along < maxPush)
            {
                float room = Mathf.Clamp01(1f - along / maxPush);
                along += crouchAccel * turbo * board.speed * (0.35f + 0.65f * room) * dt;
            }

            // Fussbremse
            float brakeTarget = _move.y < -0.3f && !Manualing && along > 0.5f ? -_move.y : 0f;
            _brake = Mathf.MoveTowards(_brake, brakeTarget, dt * 5f);
            if (_brake > 0.3f) along = Mathf.MoveTowards(along, 0f, 5f * _brake * dt);

            // Ausrollen und Luftwiderstand (wenig, damit man lange Schwung hat)
            along = Mathf.MoveTowards(along, 0f, (rollingFriction + airDrag * along * along) * dt);

            // Lenken mit Traegheit: einlenken braucht einen Moment, man carvt statt abzuknicken
            float speed = Mathf.Abs(along);
            float maxTurn = Mathf.Lerp(165f, 72f, Mathf.Clamp01(speed / 12f)) * (Manualing ? 0.45f : 1f) * Mathf.Lerp(0.4f, 1f, settle);
            if (speed < 0.6f) maxTurn *= 0.7f; // im Stand umdrehen (Kickturn)
            // Nach der Landung lenkt eine noch vom Spin gehaltene Taste nicht weiter (erst loslassen oder kurz warten)
            if (_steerLock && (Mathf.Abs(_move.x) < 0.2f || Time.time - _landTime > 0.35f)) _steerLock = false;
            float targetTurn = (_steerLock ? 0f : _move.x) * maxTurn;
            float response = Mathf.Abs(targetTurn) > Mathf.Abs(_turnVel) ? 6f : 9f;
            _turnVel = Mathf.Lerp(_turnVel, targetTurn, 1f - Mathf.Exp(-response * dt));
            Heading += _turnVel * dt;

            if (along < -0.8f)
            {
                // Rueckwaerts gerollt (z. B. Rampe runter): Fahrtrichtung dreht, das Board nicht -> Fakie wechselt
                Heading += 180f;
                along = -along;
                _turnVel = 0f;
                Fakie = !Fakie;
            }

            // Revert: aus Fakie am Boden zurueckdrehen (Grab-Taste)
            if (_revertLock && !_grabHeld) _revertLock = false;
            if (_grabPressed && Fakie && !Manualing && !_revertLock && Time.time - _landTime > RevertDelay)
            {
                Fakie = false;
                if (combo != null && combo.Active) combo.AddAction("REVERT", 120f);
            }

            Vector3 fwd2 = Vector3.ProjectOnPlane(Quaternion.Euler(0, Heading, 0) * Vector3.forward, n).normalized;
            Vector3 right2 = Vector3.Cross(n, fwd2);
            // Rollen halten die Spur, seitliches Rutschen klingt schnell ab (in harten Kurven bleibt ein Rest)
            lat *= Mathf.Exp(-sideGrip * Mathf.Lerp(0.15f, 1f, settle) * dt);

            // Am Boden bleiben, ohne festgesaugt zu werden. Zwei getrennte Teile:
            // 1) Bewegung entlang des Bodens: kippt der Boden nach unten weg (Kante, Kuppe, Bordstein), darf die
            //    Fallgeschwindigkeit nur so schnell wachsen wie mit der Schwerkraft; das Tempo in Fahrtrichtung bleibt.
            // 2) Hoehenkorrektur: eingesunken -> sofort hoch (ohne dass daraus ein Sprung wird); ueber dem Boden ->
            //    mit der Schwerkraft hin, nicht wie von einem Magneten gezogen.
            float error = hit.distance - StandDistance;
            Vector3 rideVel = fwd2 * along + right2 * lat;
            float slopeVy = rideVel.y, minSlopeVy = _lastSlopeVy - Gravity * dt;
            if (slopeVy < minSlopeVy)
            {
                Vector3 prevFlat = Vector3.ProjectOnPlane(_rb.linearVelocity, Vector3.up);
                Vector3 flat = Vector3.ProjectOnPlane(rideVel, Vector3.up);
                if (flat.sqrMagnitude > 1e-4f && flat.magnitude < prevFlat.magnitude) flat = flat.normalized * prevFlat.magnitude;
                rideVel = flat + Vector3.up * minSlopeVy;
                slopeVy = minSlopeVy;
            }
            _lastSlopeVy = slopeVy;
            float correction;
            if (error < 0.005f) { correction = Mathf.Clamp(-error / dt * 0.6f, 0f, 6f); _fallVy = 0f; }
            else
            {
                _fallVy = Mathf.Max(_fallVy - Gravity * dt, -error / dt * 0.6f);
                correction = _fallVy;
            }
            rideVel.y += correction;
            _rb.linearVelocity = rideVel;
            _rb.MoveRotation(Quaternion.Euler(0, Heading, 0));
            if (error > 0.12f && _bailOutGrace <= 0f)
            {
                // Boden ist weggefallen: frei weiterfliegen (ohne Ollie), Landung wie nach einem Sprung
                _pushPhase = -1f;
                EnterAir();
                return;
            }

            // Kurvenlage fuer die Optik
            _lean = Mathf.Clamp(_turnVel / 110f * Mathf.Clamp01(speed / 5f), -1f, 1f);

            // Ollie aufladen (nach dem Bail-Out erst, wenn die Landetaste einmal losgelassen wurde)
            if (_ollieLock && !_ollieHeld) _ollieLock = false;
            if (_ollieLock) _ollieReleased = false;
            else if (_ollieHeld) OllieCharge = Mathf.Min(1f, OllieCharge + dt / 0.35f);
            if (_ollieReleased && OllieCharge > 0f)
            {
                Pop(n, Mathf.Lerp(4.4f, 6.4f, OllieCharge));
                return;
            }
            if (!_ollieHeld) OllieCharge = 0f;

            // Manual
            if (_manualLock && !_manualHeld) _manualLock = false;
            if (_manualHeld && !_manualLock && !Manualing && along > 1.5f)
            {
                Manualing = true;
                Balance = UnityEngine.Random.Range(-0.15f, 0.15f);
                _balanceVel = 0f;
                combo?.AddAction("MANUAL", 60f);
                CurrentTrick = "MANUAL";
            }
            else if (!_manualHeld && Manualing)
            {
                Manualing = false;
                CurrentTrick = null;
            }
            if (Manualing)
            {
                UpdateBalance(_move.y, dt);
                combo?.AddPoints(70f * dt);
                combo?.Hold();
                if (Mathf.Abs(Balance) > 1f) { Bail("MANUAL VERLOREN"); return; }
            }

            if (_grindHeld && TryStartGrind()) return;

            _safeTimer += dt;
            if (_safeTimer > 1f) { _safeTimer = 0f; _lastSafePos = _rb.position; }
        }

        void Pop(Vector3 normal, float popSpeed)
        {
            Vector3 v = _rb.linearVelocity;
            v += Vector3.Lerp(Vector3.up, normal, 0.3f).normalized * popSpeed * board.pop;
            _rb.linearVelocity = v;
            OllieCharge = 0f;
            _popTimer = 0.22f;
            _pushPhase = -1f;
            bool fromManual = Manualing;
            Manualing = false;
            EnterAir();
            _pending.Add((fromManual ? "MANUAL OLLIE" : "OLLIE", 40f));
        }

        void EnterAir()
        {
            State = SkaterState.Air;
            _airTime = 0f;
            _airSpin = 0f;
            _spinVel = _turnVel * 0.5f;
            _pushPhase = -1f;
            _brake = 0f;
            _flip = FlipKind.None;
            _grabbing = false;
            Manualing = false;
            CurrentTrick = null;
            _lastAirOllie = -1f;
            _prevAirFlat = Vector3.ProjectOnPlane(_rb.linearVelocity, Vector3.up);
        }

        // ---------------------------------------------------------------- Zu Fuss

        /// <summary>Absteigen: Board unter den Arm, ab jetzt laufen (W/S, A/D drehen, Shift rennen, Leertaste springen).</summary>
        void StartWalking()
        {
            State = SkaterState.Walking;
            Manualing = false;
            CurrentTrick = null;
            _pushPhase = -1f;
            _brake = 0f;
            _turnVel = 0f;
            OllieCharge = 0f;
            Fakie = false; // zu Fuss schaut man immer in Laufrichtung
            _ollieLock = _ollieHeld;
            _walkAirTime = 0f;
        }

        /// <summary>Wieder aufs Board: weiterrollen mit dem Lauftempo (Tasten, die gerade gehalten werden, loesen nichts aus).</summary>
        void StopWalking()
        {
            State = SkaterState.Riding;
            _ollieLock = _ollieHeld;
            _manualLock = _manualHeld;
            OllieCharge = 0f;
        }

        void StepWalking(float dt)
        {
            if (_boardPressed && _walkAirTime <= 0f) { StopWalking(); return; }
            Vector3 v = _rb.linearVelocity;
            bool grounded = ProbeGround(out RaycastHit hit, 0.3f) && v.y <= 0.5f;

            // Drehen mit A/D (auch im Stand), beim Rennen etwas traeger
            bool run = _manualHeld;
            float turn = walkTurn * (run && _move.y > 0.1f ? 0.7f : 1f);
            _turnVel = Mathf.Lerp(_turnVel, _move.x * turn, 1f - Mathf.Exp(-12f * dt));
            Heading += _turnVel * dt;
            _rb.MoveRotation(Quaternion.Euler(0, Heading, 0));
            Vector3 fwd = Quaternion.Euler(0, Heading, 0) * Vector3.forward;

            float along = Vector3.Dot(Vector3.ProjectOnPlane(v, Vector3.up), fwd);
            float target = _move.y > 0.1f ? _move.y * (run ? runSpeed : walkSpeed) * (Admin.Turbo ? 1.6f : 1f)
                         : _move.y < -0.1f ? _move.y * walkSpeed * 0.6f : 0f;

            if (!grounded)
            {
                // Sprung oder Kante: in der Luft nur wenig steuern
                _walkAirTime += dt;
                if (_olliePressed && canDoubleJump && !_doubleJumpUsed && _walkAirTime > 0.1f)
                {
                    _doubleJumpUsed = true;
                    v.y = walkJump * 1.25f * (Admin.Moon ? 1.6f : 1f);
                    DoubleJumped?.Invoke();
                }
                along = Mathf.MoveTowards(along, target, walkAccel * 0.25f * dt);
                v = fwd * along + Vector3.up * (v.y - Gravity * dt);
                _rb.linearVelocity = v;
                if (_walkAirTime > 8f) Place(_lastSafePos + Vector3.up * 0.5f, Heading);
                return;
            }

            if (_walkAirTime > 0.25f) _landAbsorb = Mathf.Clamp01(-v.y / 7f) * 0.6f; // weich landen
            _walkAirTime = 0f;
            _doubleJumpUsed = false;
            GroundNormal = hit.normal;
            along = Mathf.MoveTowards(along, target, walkAccel * dt);
            float error = hit.distance - StandDistance;
            float snap = Mathf.Clamp(-error / dt * 0.6f, -6f, 6f);
            Vector3 groundFwd = Vector3.ProjectOnPlane(fwd, hit.normal).normalized;
            _rb.linearVelocity = groundFwd * along + Vector3.up * snap;

            if (_ollieLock && !_ollieHeld) _ollieLock = false;
            if (_olliePressed && !_ollieLock)
            {
                _rb.linearVelocity = groundFwd * along + Vector3.up * walkJump * (Admin.Moon ? 1.6f : 1f);
                _walkAirTime = 0.01f;
            }

            _safeTimer += dt;
            if (_safeTimer > 1f) { _safeTimer = 0f; _lastSafePos = _rb.position; }
        }

        // ---------------------------------------------------------------- Luft

        void StepAir(float dt)
        {
            _airTime += dt;
            // Hang Time: rund um den hoechsten Punkt wirkt die Schwerkraft schwaecher, der Bogen wird runder statt
            // gleich wieder nach unten zu ziehen (nicht beim Bail-Out, dort zaehlt das Landetiming)
            float vy = _rb.linearVelocity.y;
            float hang = _bailOutLanding ? 1f : Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(Mathf.Abs(vy) / 2.2f));
            Vector3 v = _rb.linearVelocity + Vector3.down * Gravity * hang * dt;
            _rb.linearVelocity = v;
            _rb.MoveRotation(Quaternion.Euler(0, Heading, 0));
            if (combo != null && combo.Active) combo.Hold();

            float spinRate = 380f * (_grabbing ? 0.85f : 1f);
            // Beim Bail-Out keine Drehung: man lenkt oft noch vom Auto aus und wuerde sonst quer landen
            float targetSpin = _bailOutLanding ? 0f : _move.x * spinRate;
            _spinVel = Mathf.Lerp(_spinVel, targetSpin, 1f - Mathf.Exp(-9f * dt));
            float spin = _spinVel * dt;
            Heading += spin;
            _airSpin += spin;
            _lean = Mathf.Lerp(_lean, 0f, 1f - Mathf.Exp(-4f * dt));

            // Flip-Tricks
            if (_flipPressed && _flip == FlipKind.None && !_bailOutLanding)
            {
                _flip = FlipFromStick(_move);
                _flipT = 0f;
                CurrentTrick = FlipName(_flip);
            }
            if (_flip != FlipKind.None)
            {
                _flipT += dt / FlipDuration;
                if (_flipT >= 1f)
                {
                    _pending.Add((FlipName(_flip), FlipPoints(_flip)));
                    _flip = FlipKind.None;
                    CurrentTrick = null;
                }
            }

            // Grabs
            if (_grabHeld && _flip == FlipKind.None && !_grabbing && !_bailOutLanding)
            {
                _grabbing = true;
                _grabName = GrabFromStick(_move);
                _grabTime = 0f;
                CurrentTrick = _grabName;
            }
            if (_grabbing)
            {
                _grabTime += dt;
                if (!_grabHeld)
                {
                    _pending.Add((_grabName, 100f + _grabTime * 450f));
                    _grabbing = false;
                    CurrentTrick = null;
                }
            }

            // Bail-Out-Landung: Prompt kurz vor dem Boden
            if (_bailOutLanding)
            {
                bool nearGround = v.y < 0f && Physics.Raycast(_rb.position + Vector3.up * 0.3f, Vector3.down, out RaycastHit h, 12f, _groundMask, QueryTriggerInteraction.Ignore)
                                  && TimeToGround(h.distance - 0.3f, -v.y) < PromptTime;
                // Wildes Druecken vor dem Prompt zaehlt als verpatzt, ein einzelner frueher Druck nicht
                LandPromptActive = nearGround && _earlyPresses < 3;
                if (_olliePressed)
                {
                    if (LandPromptActive) _landPromptHit = true;
                    else if (!_landPromptHit) { _earlyPresses++; _lastEarlyPress = _airTime; }
                }
                // Gehalten zaehlt auch (Leertaste ist im Auto die Handbremse und bleibt beim Rausspringen oft gedrueckt)
                if (LandPromptActive && _ollieHeld) _landPromptHit = true;
                // Knapp zu frueh (kurz vor dem Prompt) zaehlt noch als getroffen
                if (LandPromptActive && !_landPromptHit && _lastEarlyPress >= 0f && _airTime - _lastEarlyPress < EarlyGrace)
                    _landPromptHit = true;
            }

            if (_olliePressed && !_bailOutLanding) _lastAirOllie = _airTime;
            _grindBuffer = _grindPressed ? 0.35f : _grindBuffer - dt;
            if (TryWallPlant(v)) return;
            // Mit Fluegeln: Ollie in der Luft = Doppelsprung (nicht direkt vor einer Wand, da ist es der Wallplant)
            if (_olliePressed && canDoubleJump && !_doubleJumpUsed && !_bailOutLanding && _airTime > 0.1f && !WallAhead(v))
            {
                DoubleJump();
                v = _rb.linearVelocity;
            }

            if (_grindHeld && TryStartGrind()) return;
            if ((_grindHeld || _grindBuffer > 0f) && TryWallRide(v)) return;
            SlideOffRail();

            if (v.y <= 0.5f && ProbeGround(out RaycastHit hit, 0.05f))
            {
                Land(hit);
            }
            else if (_airTime > 8f)
            {
                Place(_lastSafePos + Vector3.up * 0.5f, Heading);
            }
        }

        /// <summary>Fallzeit bis zum Boden mit Schwerkraft (h = Hoehe, vDown = Fallgeschwindigkeit nach unten).</summary>
        static float TimeToGround(float h, float vDown)
        {
            h = Mathf.Max(0f, h);
            return (-vDown + Mathf.Sqrt(vDown * vDown + 2f * Gravity * h)) / Gravity;
        }

        void Land(RaycastHit hit)
        {
            _plantChain = 0;
            _doubleJumpUsed = false;
            GroundNormal = hit.normal;
            LandPromptActive = false;
            _landAbsorb = Mathf.Clamp01(-_rb.linearVelocity.y / 7f) * 0.9f + 0.15f;
            // Keine Drehung aus der Luft mitnehmen, und eine noch gehaltene Spin-Taste lenkt nicht gleich weiter
            _turnVel = 0f;
            _spinVel = 0f;
            _landTime = Time.time;
            _steerLock = Mathf.Abs(_move.x) > 0.2f;
            _revertLock = _grabHeld; // ein in der Luft gehaltener Grab wird beim Loslassen nicht zum Revert

            bool fromBailOut = _bailOutLanding;
            if (_bailOutLanding)
            {
                _bailOutLanding = false;
                // Tasten aus dem Auto (Leertaste = Handbremse, Shift = Kupplung) noch gedrueckt:
                // erst nach dem Loslassen wieder als Ollie bzw. Manual werten
                _ollieLock = _ollieHeld;
                _manualLock = _manualHeld;
                if (_landPromptHit || Admin.NoBail)
                {
                    LandPromptActive = false;
                    combo?.AddAction("BAIL OUT!", 800f);
                }
                else
                {
                    // Noch nicht gedrueckt: kurz nach dem Aufsetzen zaehlt es auch noch (siehe StepRiding)
                    _bailOutGrace = LateGrace;
                    LandPromptActive = true;
                }
            }
            else LandPromptActive = false;

            if (_flip != FlipKind.None && _flipT < 0.78f && !Admin.NoBail) { Bail("ZU FRUEH GELANDET"); return; }

            // Richtung pruefen: Board laengs zur Bewegung = sauber (vorwaerts oder Fakie), quer = Sturz.
            // Das Board bleibt genau so stehen, wie es aufsetzt; stattdessen wird der Schwung auf die
            // Board-Achse gelenkt. So ruckt nach einem 180 nichts nach.
            Vector3 flatVel = Vector3.ProjectOnPlane(_rb.linearVelocity, Vector3.up);
            float keep = 1f;
            if (flatVel.magnitude > 1.5f)
            {
                float velHeading = Mathf.Atan2(flatVel.x, flatVel.z) * Mathf.Rad2Deg;
                float facing = Heading + (Fakie ? 180f : 0f);
                float diff = Mathf.Abs(Mathf.DeltaAngle(facing, velHeading));
                if (diff > 65f && diff < 115f && !Admin.NoBail && !fromBailOut) { Bail("QUER GELANDET"); return; }
                Fakie = diff >= 90f;
                Heading = Fakie ? facing + 180f : facing;
                float off = Mathf.Abs(Mathf.DeltaAngle(Heading, velHeading));
                keep = Mathf.Lerp(1f, Mathf.Cos(off * Mathf.Deg2Rad), 0.5f); // schraeg gelandet kostet etwas Tempo
            }

            if (_flip != FlipKind.None) _pending.Add((FlipName(_flip), FlipPoints(_flip)));
            if (_grabbing) _pending.Add((_grabName, 100f + _grabTime * 450f));

            int halfSpins = Mathf.RoundToInt(Mathf.Abs(_airSpin) / 180f);
            if (halfSpins > 0) _pending.Add(((halfSpins * 180).ToString(), halfSpins * 160f));

            bool anyTrick = false;
            foreach (var p in _pending)
            {
                if (p.name == "OLLIE" && _pending.Count > 1) continue;
                combo?.AddAction(p.name, p.points);
                anyTrick = true;
            }
            if (!anyTrick && _airTime > 0.6f) combo?.AddPoints(30f);
            _pending.Clear();

            _flip = FlipKind.None;
            _grabbing = false;
            CurrentTrick = null;
            State = SkaterState.Riding;

            Vector3 fwd = Vector3.ProjectOnPlane(Quaternion.Euler(0, Heading, 0) * Vector3.forward, hit.normal).normalized;
            // Beim ersten Kontakt die Bewegung erhalten (nur der Fallanteil geht in den Boden), nicht hart auf die
            // Board-Achse zwingen: Grip, Lenken und Tempoverlust fahren danach ueber LandSettleTime hoch (StepRiding)
            Vector3 onGround = Vector3.ProjectOnPlane(_rb.linearVelocity, hit.normal);
            // Auf einer Schraege nach unten geht die Fallgeschwindigkeit in Tempo ueber (weich weiterrollen)
            float landSpeed = Mathf.Max(flatVel.magnitude, Vector3.Dot(_rb.linearVelocity, fwd));
            if (landSpeed > onGround.magnitude) onGround += fwd * (landSpeed - onGround.magnitude);
            _rb.linearVelocity = onGround;
            _landSettle = 0f;
            _landLossRate = landSpeed * (1f - Mathf.Clamp01(keep)) / LandSettleTime;
            _lastSlopeVy = _rb.linearVelocity.y;
            _fallVy = 0f;
        }

        static FlipKind FlipFromStick(Vector2 m)
        {
            if (m.magnitude < 0.4f) return FlipKind.Kickflip;
            if (Mathf.Abs(m.x) > Mathf.Abs(m.y)) return m.x < 0 ? FlipKind.Heelflip : FlipKind.Shuvit;
            return m.y > 0 ? FlipKind.Hardflip : FlipKind.Impossible;
        }

        static string FlipName(FlipKind k)
        {
            switch (k)
            {
                case FlipKind.Kickflip: return "KICKFLIP";
                case FlipKind.Heelflip: return "HEELFLIP";
                case FlipKind.Shuvit: return "POP SHOVE-IT";
                case FlipKind.Impossible: return "IMPOSSIBLE";
                case FlipKind.Hardflip: return "HARDFLIP";
                default: return "";
            }
        }

        static float FlipPoints(FlipKind k) => k == FlipKind.Hardflip || k == FlipKind.Impossible ? 350f : 250f;

        static string GrabFromStick(Vector2 m)
        {
            if (m.magnitude < 0.4f) return "INDY";
            if (Mathf.Abs(m.x) > Mathf.Abs(m.y)) return m.x < 0 ? "MELON" : "STALEFISH";
            return m.y > 0 ? "NOSEGRAB" : "TAILGRAB";
        }

        // ---------------------------------------------------------------- Wallplant

        /// <summary>
        /// In der Luft frontal gegen eine Wand: mit dem Fuss an der Wand abfangen (wie in Tony Hawk's). Danach mit Ollie
        /// abspringen; wer schon kurz vor der Wand Ollie drueckt, springt gleich wieder ab.
        /// </summary>
        bool TryWallPlant(Vector3 v)
        {
            // Tempo vor dem Aufprall: springt man direkt vor der Wand ab, bremst die Wand schon im ersten Physikschritt
            Vector3 flat = Vector3.ProjectOnPlane(v, Vector3.up);
            if (_prevAirFlat.sqrMagnitude > flat.sqrMagnitude) flat = _prevAirFlat;
            _prevAirFlat = Vector3.ProjectOnPlane(v, Vector3.up);
            if (_bailOutLanding || _plantCooldown > 0f || _airTime < 0.03f) return false;
            float speed = flat.magnitude;
            if (speed < 2.5f) return false;
            Vector3 dir = flat / speed;
            Vector3 chest = _rb.position + Vector3.up * 0.95f;
            float reach = 0.42f + speed * Time.fixedDeltaTime * 2f;
            if (!Physics.SphereCast(chest, 0.2f, dir, out RaycastHit hit, reach, _groundMask, QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(hit.normal.y) > 0.35f || hit.rigidbody != null) return false; // nur feste, senkrechte Waende
            Vector3 n = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
            if (Vector3.Dot(dir, -n) < 0.57f) return false; // nur frontal (bis 55 Grad schraeg)

            // Tricks bis zur Wand zaehlen; ein halber Flip an der Wand ist ein Sturz
            if (!BankAirTricks()) return true;
            _plantChain++;
            combo?.AddAction(_plantChain > 1 ? $"WALLPLANT x{_plantChain}" : "WALLPLANT", 250f + 150f * (_plantChain - 1));

            State = SkaterState.WallPlant;
            _doubleJumpUsed = false;
            _plantCharging = false;
            _plantCharge = 0f;
            _wallNormal = n;
            _plantSpeed = speed;
            _plantTime = 0f;
            _grabbing = false;
            _spinVel = _turnVel = 0f;
            _airSpin = 0f;
            // Zur Wand schauen (die Figur dreht sich beim Abspringen von selbst weg)
            Heading = Mathf.Atan2(-n.x, -n.z) * Mathf.Rad2Deg + (Fakie ? 180f : 0f);
            _rb.MoveRotation(Quaternion.Euler(0, Heading, 0));
            _rb.linearVelocity = Vector3.zero;
            CurrentTrick = "WALLPLANT";
            return true;
        }

        void StepWallPlant(float dt)
        {
            _plantTime += dt;
            combo?.Hold();
            // Erst kurz haengen, dann langsam an der Wand herunterrutschen (leicht an die Wand gedrueckt).
            // Beim Aufladen haelt man sich an der Wand.
            bool grounded = ProbeGround(out _, 0.02f);
            float slide = grounded || _plantCharging || _plantTime < 0.3f ? 0f : Mathf.Min(0.4f + (_plantTime - 0.3f) * 1.2f, 2f);
            _rb.linearVelocity = Vector3.down * slide - _wallNormal * 0.3f;

            // Ollie druecken startet das Aufladen (auch ein Druck kurz vor der Wand), loslassen springt ab
            bool press = _olliePressed || (_lastAirOllie >= 0f && _airTime - _lastAirOllie < 0.25f);
            if (!_plantCharging && press && _plantTime >= plantMinTime)
            {
                _lastAirOllie = -1f;
                if (!_ollieHeld) { JumpOffWall(0f); return; } // schon losgelassen: kurzer Absprung
                _plantCharging = true;
                _plantCharge = 0f;
            }
            if (!press && !_plantCharging) _lastAirOllie = -1f;
            if (_plantCharging)
            {
                _plantCharge = Mathf.Min(1f, _plantCharge + dt / plantChargeTime);
                OllieCharge = _plantCharge; // Anzeige wie beim Ollie
                // Loslassen springt ab; wer nach voller Ladung weiter haelt, springt nach einer kurzen Weile von selbst
                if (!_ollieHeld || _plantCharge >= 1f && _plantTime > plantChargeTime + plantMinTime + 0.6f) { JumpOffWall(_plantCharge); return; }
                return;
            }

            // Nicht abgesprungen (oder unten angekommen): von der Wand wegkippen und normal landen
            if (_plantTime > plantWindow || (_plantTime > 0.6f && grounded))
            {
                Heading = Mathf.Atan2(_wallNormal.x, _wallNormal.z) * Mathf.Rad2Deg;
                ExitPlant(_wallNormal * 1.8f + Vector3.down * 0.5f);
            }
        }

        /// <summary>Von der Wand abspringen: Schwung (Boost) weg von der Wand, mit Ladung weiter und hoeher; Stick lenkt seitlich.</summary>
        void JumpOffWall(float charge)
        {
            Heading = Mathf.Atan2(_wallNormal.x, _wallNormal.z) * Mathf.Rad2Deg;
            float away = Mathf.Max(plantBoost, _plantSpeed * plantPush) + charge * plantChargeBoost + plantChainBoost * Mathf.Min(_plantChain - 1, 4);
            float up = plantPop * (1f + 0.4f * charge) * board.pop * (Admin.Moon ? 1.3f : 1f);
            Vector3 side = Vector3.Cross(Vector3.up, _wallNormal) * _move.x * 2.5f;
            ExitPlant(_wallNormal * away + side + Vector3.up * up);
            _popTimer = 0.22f;
            if (charge > 0.95f) combo?.AddAction("BOOST", 150f);
        }

        void ExitPlant(Vector3 velocity)
        {
            _plantCharging = false;
            OllieCharge = 0f;
            _rb.MoveRotation(Quaternion.Euler(0, Heading, 0));
            EnterAir();
            _rb.linearVelocity = velocity;
            _plantCooldown = 0.35f;
            _lastAirOllie = -1f;
        }

        // ---------------------------------------------------------------- Doppelsprung

        void DoubleJump()
        {
            _doubleJumpUsed = true;
            Vector3 v = _rb.linearVelocity;
            v.y = doubleJumpUp * (Admin.Moon ? 1.3f : 1f);
            _rb.linearVelocity = v;
            _pending.Add(("DOPPELSPRUNG", 300f));
            _popTimer = 0.22f;
            DoubleJumped?.Invoke();
        }

        /// <summary>Steht in Flugrichtung gleich eine Wand (dann ist Ollie der Wallplant, kein Doppelsprung)?</summary>
        bool WallAhead(Vector3 v)
        {
            Vector3 flat = Vector3.ProjectOnPlane(v, Vector3.up);
            if (flat.sqrMagnitude < 1f) return false;
            return Physics.SphereCast(_rb.position + Vector3.up * 0.95f, 0.2f, flat.normalized, out RaycastHit h, 0.45f + flat.magnitude * 0.25f, _groundMask, QueryTriggerInteraction.Ignore)
                   && Mathf.Abs(h.normal.y) < 0.35f;
        }

        // ---------------------------------------------------------------- Wallride

        /// <summary>
        /// In der Luft mit gehaltenem Grind schraeg (bis 35 Grad) an eine Wand: an der Wand entlangfahren wie in Tony Hawk's.
        /// Frontal ist es ein Wallplant. Ollie springt ab (Wallie), Grind loslassen laesst einen abfallen.
        /// </summary>
        bool TryWallRide(Vector3 v)
        {
            if (_bailOutLanding || _rideCooldown > 0f || _airTime < 0.05f) return false;
            Vector3 flat = Vector3.ProjectOnPlane(v, Vector3.up);
            if (_prevAirFlat.sqrMagnitude > flat.sqrMagnitude) flat = _prevAirFlat;
            float speed = flat.magnitude;
            if (speed < rideMinSpeed) return false;
            Vector3 dir = flat / speed;
            // Nicht direkt am Boden ansetzen (Fuesse mindestens 25 cm hoch)
            if (Physics.Raycast(_rb.position + Vector3.up * 0.3f, Vector3.down, 0.55f, _groundMask, QueryTriggerInteraction.Ignore)) return false;

            Vector3 chest = _rb.position + Vector3.up * 0.9f;
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            RaycastHit best = default;
            bool found = false;
            foreach (float side in new[] { 1f, -1f })
            {
                // Seitlich und schraeg nach vorn suchen
                foreach (var probe in new[] { right * side, (right * side + dir).normalized })
                {
                    if (!Physics.SphereCast(chest, 0.2f, probe, out RaycastHit h, 1.1f, _groundMask, QueryTriggerInteraction.Ignore)) continue;
                    if (Mathf.Abs(h.normal.y) > 0.3f || h.rigidbody != null) continue;
                    if (!found || h.distance < best.distance) { best = h; found = true; }
                }
            }
            if (!found) return false;
            Vector3 n = Vector3.ProjectOnPlane(best.normal, Vector3.up).normalized;
            float into = Vector3.Dot(dir, -n);
            if (into < -0.15f || into > 0.57f) return false; // weg von der Wand oder zu frontal (dann Wallplant)
            // Die Wand muss ein Stueck weiter vorn noch da sein und bis zu den Fuessen reichen
            Vector3 along = Vector3.ProjectOnPlane(dir, n).normalized;
            if (!Physics.Raycast(chest + along * 1.2f + n * 0.3f, -n, 1.6f, _groundMask, QueryTriggerInteraction.Ignore)) return false;
            if (!Physics.Raycast(_rb.position + Vector3.up * 0.25f + n * 0.3f, -n, 1.6f, _groundMask, QueryTriggerInteraction.Ignore)) return false;

            if (!BankAirTricks()) return true;
            combo?.AddAction("WALLRIDE", 200f);
            State = SkaterState.WallRide;
            _rideNormal = _lastRideNormal = n;
            _rideDir = along;
            _rideSpeed = Mathf.Max(speed * 0.95f, rideMinSpeed + 1f);
            _rideVy = Mathf.Max(v.y * 0.5f, 0f) + rideLift;
            _rideTime = 0f;
            _grabbing = false;
            _spinVel = _turnVel = 0f;
            _airSpin = 0f;
            Heading = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
            _rb.MoveRotation(Quaternion.Euler(0, Heading, 0));
            CurrentTrick = "WALLRIDE";
            _grindBuffer = 0f;
            _doubleJumpUsed = false;
            RideDust(true);
            return true;
        }

        /// <summary>Tricks bis zur Wand gutschreiben; ein halber Flip ist ein Sturz (dann false).</summary>
        bool BankAirTricks()
        {
            if (_flip != FlipKind.None)
            {
                if (_flipT < 0.6f && !Admin.NoBail) { Bail("GEGEN DIE WAND"); return false; }
                _pending.Add((FlipName(_flip), FlipPoints(_flip)));
                _flip = FlipKind.None;
            }
            if (_grabbing) _pending.Add((_grabName, 100f + _grabTime * 450f));
            int halfSpins = Mathf.RoundToInt(Mathf.Abs(_airSpin) / 180f);
            if (halfSpins > 0) _pending.Add(((halfSpins * 180).ToString(), halfSpins * 160f));
            foreach (var p in _pending)
                if (p.name != "OLLIE") combo?.AddAction(p.name, p.points);
            _pending.Clear();
            return true;
        }

        void StepWallRide(float dt)
        {
            _rideTime += dt;
            combo?.AddPoints(120f * dt);
            combo?.Hold();

            // Weicher Bogen: erst leicht hoch, dann sanft runter; mit der Zeit zieht die Schwerkraft staerker
            float g = Mathf.Lerp(rideGravity, 0.85f, Mathf.Clamp01((_rideTime - 0.9f) / 1.2f));
            _rideVy -= Gravity * g * dt;
            _rideSpeed = Mathf.Max(rideMinSpeed * 0.8f, _rideSpeed - 0.5f * dt);

            // An der Wand bleiben (auch an leicht gebogenen Waenden): Wand neu suchen, Abstand halten
            Vector3 chest = _rb.position + Vector3.up * 0.9f;
            bool wall = Physics.SphereCast(chest + _rideNormal * 0.2f, 0.15f, -_rideNormal, out RaycastHit h, RideGap + 0.7f, _groundMask, QueryTriggerInteraction.Ignore)
                        && Mathf.Abs(h.normal.y) < 0.3f && h.rigidbody == null;
            Vector3 vel = _rideDir * _rideSpeed + Vector3.up * _rideVy;
            if (wall)
            {
                Vector3 n = Vector3.ProjectOnPlane(h.normal, Vector3.up).normalized;
                _rideNormal = Vector3.Slerp(_rideNormal, n, 1f - Mathf.Exp(-10f * dt)).normalized;
                _lastRideNormal = _rideNormal;
                _rideDir = Vector3.ProjectOnPlane(_rideDir, _rideNormal).normalized;
                float gap = Vector3.Dot(chest - h.point, _rideNormal);
                vel += _rideNormal * Mathf.Clamp((RideGap - gap) * 8f, -3f, 3f);
            }
            Heading = Mathf.Atan2(_rideDir.x, _rideDir.z) * Mathf.Rad2Deg;
            _rb.MoveRotation(Quaternion.Euler(0, Heading, 0));
            _rb.linearVelocity = vel;
            if (_rideDust != null) _rideDust.transform.position = _rb.position + Vector3.up * 0.35f - _rideNormal * (RideGap - 0.05f);

            if (_olliePressed)
            {
                // Wallie: weg von der Wand und hoch, Tempo bleibt
                combo?.AddAction("WALLIE", 150f);
                ExitRide(_rideDir * _rideSpeed * 1.05f + _rideNormal * rideJumpOut + Vector3.up * rideJumpUp * board.pop * (Admin.Moon ? 1.3f : 1f));
                _popTimer = 0.22f;
                return;
            }
            // Einmal Grind antippen reicht; nochmal antippen laesst los
            bool letGo = _grindPressed && _rideTime > 0.25f;
            bool grounded = _rideVy < 0f && ProbeGround(out _, 0.05f);
            if (!wall || letGo || grounded || _rideTime > rideMaxTime)
                ExitRide(_rideDir * _rideSpeed + _rideNormal * (wall ? 1.4f : 0.3f) + Vector3.up * Mathf.Min(_rideVy, 0.5f));
        }

        void ExitRide(Vector3 velocity)
        {
            EnterAir();
            _rb.linearVelocity = velocity;
            _rideCooldown = 0.35f;
            RideDust(false);
        }

        /// <summary>Feiner Staub, wo die Rollen an der Wand laufen.</summary>
        void RideDust(bool on)
        {
            if (_rideDust == null && on)
            {
                _rideDust = SkaterFx.MakeDust(transform);
            }
            if (_rideDust == null) return;
            var em = _rideDust.emission;
            em.rateOverTime = on ? 40f : 0f;
        }

        // ---------------------------------------------------------------- Grind

        bool TryStartGrind()
        {
            Vector3 pos = _rb.position;
            GrindRail best = null;
            float bestDist = float.MaxValue, bestS = 0f;
            Vector3 bestPoint = Vector3.zero;
            foreach (var rail in GrindRail.All)
            {
                if (!rail.Closest(pos, out Vector3 p, out float s)) continue;
                float dy = p.y - pos.y;
                if (dy < -0.6f || dy > 1.4f) continue;
                Vector3 flat = p - pos;
                flat.y = 0f;
                float d = flat.magnitude;
                if (d < 1.1f && d < bestDist) { best = rail; bestDist = d; bestS = s; bestPoint = p; }
            }
            if (best == null) return false;

            Vector3 dir = best.DirectionAt(bestS);
            Vector3 v = _rb.linearVelocity;
            _railDir = Vector3.Dot(v, dir) >= 0f ? 1f : -1f;
            _grindSpeed = Mathf.Max(Vector3.ProjectOnPlane(v, Vector3.up).magnitude, 4f);
            _rail = best;
            _railS = bestS;
            State = SkaterState.Grinding;
            _plantChain = 0;
            _doubleJumpUsed = false;
            _rb.isKinematic = true;
            Manualing = false;
            _grabbing = false;
            _flip = FlipKind.None;
            Balance = UnityEngine.Random.Range(-0.12f, 0.12f);
            _balanceVel = 0f;

            if (_pending.Count > 0)
            {
                foreach (var p in _pending)
                    if (p.name != "OLLIE") combo?.AddAction(p.name, p.points);
                _pending.Clear();
            }
            _grindName = _move.magnitude < 0.4f ? "50-50"
                : Mathf.Abs(_move.x) > Mathf.Abs(_move.y) ? "BOARDSLIDE"
                : _move.y > 0 ? "NOSEGRIND" : "5-0";
            CurrentTrick = _grindName;
            combo?.AddAction(_grindName, 100f);
            _rb.position = bestPoint + Vector3.up * 0.02f;
            return true;
        }

        void StepGrind(float dt)
        {
            Vector3 dir = _rail.DirectionAt(_railS) * _railDir;
            _grindSpeed += Gravity * -dir.y * dt;
            // Auf der Rail Tempo aufbauen (bis zur Obergrenze); bergab kommt die Schwerkraft dazu
            if (_grindSpeed < grindMaxSpeed) _grindSpeed = Mathf.Min(grindMaxSpeed, _grindSpeed + grindAccel * dt);
            _grindSpeed = Mathf.Max(2.5f, _grindSpeed);
            _railS += _grindSpeed * _railDir * dt;

            if (_railS < 0f || _railS > _rail.Length)
            {
                // Schliesst hier eine andere Stange an (z. B. oben waagerecht, dann die Treppe runter)? Dann weiterrutschen.
                bool atEnd = _railS > _rail.Length;
                float over = atEnd ? _railS - _rail.Length : -_railS;
                Vector3 end = _rail.PointAt(atEnd ? _rail.Length : 0f);
                if (GrindRail.FindConnected(_rail, end, dir, out var next, out float s, out float sign))
                {
                    _rail = next;
                    _railDir = sign;
                    _railS = Mathf.Clamp(s + sign * over, 0f, next.Length);
                    dir = _rail.DirectionAt(_railS) * _railDir;
                }
                else
                {
                    ExitGrind(dir * _grindSpeed + Vector3.up * 2f);
                    return;
                }
            }

            Vector3 pos = _rail.PointAt(_railS) + Vector3.up * 0.02f;
            _rb.MovePosition(pos);
            Heading = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            _rb.MoveRotation(Quaternion.Euler(0, Heading, 0));
            GroundNormal = Vector3.up;

            UpdateBalance(-_move.x, dt);
            if (Mathf.Abs(Balance) > 1f)
            {
                // Zur Seite kippen, zu der die Balance weggelaufen ist (nicht oben auf der festen Stange liegen bleiben)
                Vector3 side = Vector3.Cross(Vector3.up, dir) * Mathf.Sign(Balance);
                Bail("VOM RAIL GEFALLEN");
                _rb.linearVelocity = dir * _grindSpeed * 0.4f + side * 2.5f;
                return;
            }

            combo?.AddPoints(110f * dt);
            combo?.Hold();

            // Absprung vom Grind gibt zusaetzlich Schwung nach vorn
            if (_olliePressed) ExitGrind(dir * (_grindSpeed * grindJumpBoost + 1.5f) + Vector3.up * 5.2f * board.pop);
        }

        void ExitGrind(Vector3 velocity)
        {
            _rail = null;
            _rb.isKinematic = false;
            EnterAir();
            _rb.linearVelocity = velocity;
        }

        /// <summary>Balance-Spiel fuer Grind und Manual. counter = Stick gegen die Neigung.</summary>
        void UpdateBalance(float counter, float dt)
        {
            float drift = Mathf.Sign(Balance) * 1.5f / board.balance;
            _balanceVel += (drift + UnityEngine.Random.Range(-1.2f, 1.2f)) * Chill.BalanceCalm * dt;
            _balanceVel += counter * 4.2f * dt;
            _balanceVel *= 1f - 1.5f * dt;
            Balance += _balanceVel * dt;
            if (Admin.NoBail && Mathf.Abs(Balance) > 0.9f)
            {
                Balance = Mathf.Sign(Balance) * 0.9f;
                _balanceVel = 0f;
            }
        }

        // ---------------------------------------------------------------- Feste Gelaender

        void OnCollisionEnter(Collision c) => CheckRailTop(c);
        void OnCollisionStay(Collision c) => CheckRailTop(c);

        /// <summary>Gelaender sind fest. Liegt man in der Luft oder nach einem Sturz oben auf einer Stange, merken.</summary>
        void CheckRailTop(Collision c)
        {
            if (c.gameObject.layer != _railLayer || (State != SkaterState.Air && State != SkaterState.Bailed)) return;
            for (int i = 0; i < c.contactCount; i++)
            {
                var cp = c.GetContact(i);
                if (cp.normal.y > 0.3f)
                {
                    _onRailTop = true;
                    _railTopPoint = cp.point;
                    return;
                }
            }
        }

        /// <summary>
        /// Ohne Grind-Taste auf einer Stange gelandet (z. B. nach einem Ollie aus dem Grind): seitlich herunterrutschen,
        /// weg von der Stange bzw. zur Lenkseite, statt oben darauf zu balancieren.
        /// </summary>
        void SlideOffRail()
        {
            if (!_onRailTop) return;
            Vector3 away = _rb.position - _railTopPoint;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0025f)
                away = Quaternion.Euler(0, Heading, 0) * Vector3.right * (_move.x < -0.2f ? -1f : 1f);
            away.Normalize();
            float current = Vector3.Dot(_rb.linearVelocity, away);
            if (current < 2.5f) _rb.linearVelocity += away * (2.5f - current);
        }

        // ---------------------------------------------------------------- Skitchen

        /// <summary>
        /// Am Auto festhalten (seitlich an der Tuer oder hinten an der Stossstange). Rechts faehrt man Fakie, damit
        /// die Zehenseite (und die greifende Hand) zum Auto zeigt.
        /// </summary>
        public bool StartHitch(Hitchable target, int anchor)
        {
            if (target == null || State != SkaterState.Riding) return false;
            _hitch = target;
            _hitchAnchor = anchor;
            _hitchSway = 0f;
            _hitchTime = 0f;
            State = SkaterState.Hitched;
            Manualing = false;
            _grabbing = false;
            _flip = FlipKind.None;
            _pushPhase = -1f;
            _brake = 0f;
            _turnVel = 0f;
            OllieCharge = 0f;
            _ollieLock = _ollieHeld;
            Fakie = anchor == Hitchable.Right;
            Heading = target.transform.eulerAngles.y;
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.None;
            if (_col != null) _col.enabled = false;
            _grindSpeed = target.Velocity.magnitude;
            CurrentTrick = "SKITCH";
            combo?.AddAction("SKITCH", 150f);
            return true;
        }

        void LeaveHitchPhysics()
        {
            _hitch = null;
            if (_col != null) _col.enabled = true;
            _rb.isKinematic = !isLocal;
            _rb.interpolation = isLocal ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            CurrentTrick = null;
        }

        /// <summary>Loslassen: einfach weiterrollen (F / Y) oder mit Ollie abschleudern (SLINGSHOT, je laenger geladen, desto mehr).</summary>
        public void ReleaseHitch(bool slingshot)
        {
            if (State != SkaterState.Hitched) return;
            Hitchable h = _hitch;
            Vector3 v = h != null ? h.Velocity : Vector3.zero;
            v.y = 0f;
            if (v.magnitude > HitchMaxSpeed) v = v.normalized * HitchMaxSpeed;
            Vector3 carRight = h != null ? h.transform.right : transform.right;
            Vector3 away = _hitchAnchor == Hitchable.Left ? -carRight : _hitchAnchor == Hitchable.Right ? carRight : Vector3.zero;
            Vector3 fwd = h != null ? Vector3.ProjectOnPlane(h.transform.forward, Vector3.up).normalized : Quaternion.Euler(0, Heading, 0) * Vector3.forward;
            float charge = OllieCharge;
            Vector3 pos = transform.position;
            LeaveHitchPhysics();
            _rb.position = pos;
            OllieCharge = 0f;
            Heading = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            _rb.rotation = Quaternion.Euler(0, Heading, 0);
            if (slingshot)
            {
                _rb.linearVelocity = v + fwd * (2.5f + 3.5f * charge) + away * 1.5f + Vector3.up * Mathf.Lerp(4.6f, 6.6f, charge) * board.pop;
                _popTimer = 0.22f;
                EnterAir();
                _pending.Add(("SLINGSHOT", 250f + v.magnitude * 12f));
            }
            else
            {
                State = SkaterState.Riding;
                _rb.linearVelocity = v + away * 1.2f;
                _ollieLock = _ollieHeld;
                _lastSlopeVy = _fallVy = 0f;
            }
        }

        void StepHitched(float dt)
        {
            Hitchable h = _hitch;
            if (h == null || !h.Available) { ReleaseHitch(false); return; }
            if (h.transform.up.y < 0.6f) { Bail("AUTO UMGEKIPPT"); return; }
            _hitchTime += dt;

            // Lenken: seitlich etwas vom Auto weg oder ran, hinten hin und her
            float target = _hitchAnchor == Hitchable.Left ? -_move.x * 0.45f : _hitchAnchor == Hitchable.Right ? _move.x * 0.45f : _move.x * 0.6f;
            if (_hitchAnchor != Hitchable.Rear) target = Mathf.Max(target, -0.15f);
            _hitchSway = Mathf.MoveTowards(_hitchSway, target, dt * 1.6f);
            _lean = Mathf.MoveTowards(_lean, -_move.x * 0.5f, dt * 3f);

            Vector3 p = h.StandPoint(_hitchAnchor, _hitchSway);
            Heading = h.transform.eulerAngles.y;
            _rb.position = p;
            _rb.rotation = Quaternion.Euler(0, Heading, 0);
            float speed = h.Velocity.magnitude;
            _grindSpeed = speed;

            // Punkte nach Tempo, Combo bleibt offen
            if (combo != null)
            {
                combo.AddPoints(speed * 6f * dt);
                combo.Hold();
            }

            // Ollie halten = laden, loslassen = Slingshot (eine noch gehaltene Taste vom Anhaengen zaehlt nicht)
            if (_ollieLock && !_ollieHeld) _ollieLock = false;
            if (_ollieLock) { _ollieReleased = false; return; }
            if (_ollieHeld) OllieCharge = Mathf.Min(1f, OllieCharge + dt / 0.5f);
            if (_ollieReleased && OllieCharge > 0f && _hitchTime > 0.15f) ReleaseHitch(true);
        }

        /// <summary>
        /// Nach allen Updates (auch der Netzwerk-Interpolation fremder Autos): Skater genau an den Haltepunkt setzen
        /// und der Figur sagen, wo die Hand greift. Laeuft vor Kamera und Pose (HitchGlue, Ausfuehrungsreihenfolge -100).
        /// </summary>
        public void GlueHitch()
        {
            Hitchable h = isLocal ? HitchTarget : RemoteState == SkaterState.Hitched ? RemoteHitch : null;
            if (h == null)
            {
                if (rig != null) rig.hold = Mathf.MoveTowards(rig.hold, 0f, Time.deltaTime * 6f);
                return;
            }
            int anchor = isLocal ? _hitchAnchor : RemoteHitchAnchor;
            Vector3 p = h.StandPoint(anchor, isLocal ? _hitchSway : 0f);
            transform.SetPositionAndRotation(p, Quaternion.Euler(0, h.transform.eulerAngles.y, 0));
            if (rig != null)
            {
                rig.holdPoint = h.HoldPoint(anchor);
                rig.hold = Mathf.MoveTowards(rig.hold, 1f, Time.deltaTime * 6f);
            }
        }


        // ---------------------------------------------------------------- Sturz

        public void Bail(string reason)
        {
            if (State == SkaterState.WallRide) RideDust(false);
            if (State == SkaterState.Hitched) LeaveHitchPhysics();
            State = SkaterState.Bailed;
            _plantChain = 0;
            _plantCharging = false;
            _bailTimer = 1.6f;
            if (_rb.isKinematic && isLocal) _rb.isKinematic = false;
            _rb.linearVelocity *= 0.35f;
            _rail = null;
            Manualing = false;
            _grabbing = false;
            _flip = FlipKind.None;
            _bailOutLanding = false;
            _bailOutGrace = 0f;
            LandPromptActive = false;
            _pending.Clear();
            CurrentTrick = null;
            combo?.Fail(reason);
            BailedEvent?.Invoke(reason);
        }

        void StepBailed(float dt)
        {
            _bailTimer -= dt;
            Vector3 v = _rb.linearVelocity;
            if (ProbeGround(out RaycastHit hit, 0.1f))
            {
                v = Vector3.MoveTowards(Vector3.ProjectOnPlane(v, hit.normal), Vector3.zero, 12f * dt);
                v += Vector3.up * Mathf.Clamp(-(hit.distance - StandDistance) / dt * 0.6f, -6f, 6f);
            }
            else v += Vector3.down * Gravity * dt;
            _rb.linearVelocity = v;
            SlideOffRail();
            if (_bailTimer <= 0f)
            {
                State = SkaterState.Riding;
                _rb.linearVelocity = Vector3.zero;
                if (Fakie)
                {
                    // Im Stand: Blickrichtung behalten, aber wieder normal (nicht Fakie) losfahren
                    Heading += 180f;
                    Fakie = false;
                    _rb.MoveRotation(Quaternion.Euler(0, Heading, 0));
                }
            }
        }

        static float Smooth01(float t) => t * t * (3f - 2f * t);

        // ---------------------------------------------------------------- Optik (lokal und entfernt)

        float _crouch, _bailAnim, _walkAnim;
        Quaternion _alignWorld;
        bool _alignInit;

        /// <summary>Entfernter Spieler im Wallride: naechste Wand links oder rechts suchen (fuer die Neigung).</summary>
        Vector3 RemoteWallNormal()
        {
            Vector3 chest = transform.position + Vector3.up * 0.9f;
            Vector3 best = _lastRideNormal;
            float bestD = 1.3f;
            foreach (float side in new[] { 1f, -1f })
                if (Physics.Raycast(chest, transform.right * side, out RaycastHit h, bestD, _groundMask, QueryTriggerInteraction.Ignore) && Mathf.Abs(h.normal.y) < 0.3f)
                {
                    bestD = h.distance;
                    best = Vector3.ProjectOnPlane(h.normal, Vector3.up).normalized;
                }
            return best;
        }

        void Animate(float dt)
        {
            if (bodyPivot == null || boardPivot == null) return;
            SkaterState s = isLocal ? State : RemoteState;

            if (align != null)
            {
                // Wallride: Koerper kippt weich von der Wand weg (die Fuesse stehen an der Wand), beim Abspringen wieder zurueck
                bool riding = s == SkaterState.WallRide;
                if (riding && !isLocal) _lastRideNormal = RemoteWallNormal();
                _rideBlend = Mathf.MoveTowards(_rideBlend, riding ? 1f : 0f, dt * (riding ? 4f : 2.5f));
                Vector3 up = s == SkaterState.Riding ? GroundNormal : Vector3.up;
                if (_rideBlend > 0f) up = Vector3.Slerp(up, _lastRideNormal, 0.62f * Mathf.SmoothStep(0f, 1f, _rideBlend)).normalized;
                Quaternion tilt = Quaternion.FromToRotation(Vector3.up, up);
                // Lokal direkt aus Heading + Fakie (beides aendert sich im selben Physikschritt), nicht aus der
                // interpolierten Rigidbody-Drehung: sonst zuckt das Board bei der Fakie-Landung kurz um bis zu 180 Grad
                Quaternion facing = isLocal
                    ? Quaternion.Euler(0f, Heading + (Fakie ? 180f : 0f), 0f)
                    : transform.rotation * Quaternion.Euler(0f, RemoteFakie ? 180f : 0f, 0f);
                Quaternion target = tilt * facing;
                // Von der eigenen, gemerkten Welt-Ausrichtung aus nachfuehren: Align haengt an der Skater-Wurzel, und die
                // springt bei einer Fakie-Landung um 180 Grad (Heading + 180, Fakie). Ginge man von align.rotation aus,
                // wuerde das Board erst mitgerissen und dann zurueckgedreht = eine ungewollte 360-Drehung.
                if (!_alignInit) { _alignWorld = align.rotation; _alignInit = true; }
                _alignWorld = Quaternion.Slerp(_alignWorld, target, 1f - Mathf.Exp(-12f * dt));
                align.rotation = _alignWorld;
            }

            _landAbsorb = Mathf.MoveTowards(_landAbsorb, 0f, dt * 2.2f);
            _popTimer = Mathf.Max(0f, _popTimer - dt);
            bool plant = s == SkaterState.WallPlant;
            bool wallRide = s == SkaterState.WallRide;
            bool air = s == SkaterState.Air || plant || wallRide;
            bool hitched = s == SkaterState.Hitched;
            float crouchTarget = hitched ? 0.3f + OllieCharge * 0.6f : isLocal
                ? Mathf.Max(OllieCharge * 0.95f, _landAbsorb, Manualing ? 0.15f : 0f, s == SkaterState.Grinding ? 0.35f : 0f, plant ? 0.6f + 0.35f * Mathf.Max(0f, PlantCharge) : wallRide ? 0.42f : air ? 0.3f : 0.08f)
                : (s == SkaterState.Grinding ? 0.35f : plant ? 0.6f : wallRide ? 0.42f : air ? 0.3f : 0.08f);
            _crouch = Mathf.Lerp(_crouch, crouchTarget, 1f - Mathf.Exp(-16f * dt));
            _bailAnim = Mathf.MoveTowards(_bailAnim, s == SkaterState.Bailed ? 1f : 0f, dt * 3f);
            // Zu Fuss: Figur dreht sich von seitlich (Board-Stand) nach vorn in Laufrichtung
            _walkAnim = Mathf.MoveTowards(_walkAnim, s == SkaterState.Walking ? 1f : 0f, dt * 5f);

            bodyPivot.localPosition = rig != null ? new Vector3(0, 0.1f, 0) : new Vector3(0, 0.13f - _crouch * 0.28f - _bailAnim * 0.3f, 0);
            bodyPivot.localRotation = Quaternion.Euler(_bailAnim * 75f, Mathf.Lerp(80f, 0f, Smooth01(_walkAnim)), rig != null ? 0f : -_lean * 14f);

            if (rig != null)
            {
                rig.crouch = _crouch;
                rig.lean = isLocal ? _lean : 0f;
                rig.pushPhase = isLocal && s == SkaterState.Riding ? _pushPhase : -1f;
                rig.brake = isLocal ? _brake : 0f;
                rig.airborne = Mathf.MoveTowards(rig.airborne, wallRide ? 0.45f : air ? 1f : 0f, dt * 5f);
                rig.manual = Mathf.MoveTowards(rig.manual, isLocal && Manualing ? 1f : 0f, dt * 6f);
                rig.grind = Mathf.MoveTowards(rig.grind, s == SkaterState.Grinding ? 1f : 0f, dt * 6f);
                rig.bail = _bailAnim;
                rig.walk = Smooth01(_walkAnim);
                // Zu Fuss in der Luft (Sprung, Kante): Sprung-Pose. Entfernt: kein Boden unter den Fuessen
                bool walkAir = s == SkaterState.Walking && (isLocal ? _walkAirTime > 0.03f : !ProbeGround(out _, 0.25f));
                rig.walkJump = Mathf.MoveTowards(rig.walkJump, walkAir ? 1f : 0f, dt * (walkAir ? 9f : 7f));
                rig.grab = isLocal && _grabbing ? _grabName : null;
            }

            if (!isLocal) return; // Board-Rotation kommt bei entfernten Spielern per Netzwerk

            Quaternion boardRot = Quaternion.Euler(0, 0, s == SkaterState.Riding ? -_lean * 9f : 0f);
            Vector3 boardPos = new Vector3(0, 0.1f, 0);
            if (_flip != FlipKind.None)
            {
                float t = Mathf.Clamp01(_flipT);
                switch (_flip)
                {
                    case FlipKind.Kickflip: boardRot = Quaternion.Euler(0, 0, 360f * t); break;
                    case FlipKind.Heelflip: boardRot = Quaternion.Euler(0, 0, -360f * t); break;
                    case FlipKind.Shuvit: boardRot = Quaternion.Euler(0, 180f * t, 0); break;
                    case FlipKind.Impossible: boardRot = Quaternion.Euler(360f * t, 0, 0); break;
                    case FlipKind.Hardflip: boardRot = Quaternion.Euler(360f * t, 0, 360f * t); break;
                }
                boardPos.y += Mathf.Sin(t * Mathf.PI) * 0.25f;
            }
            else if (State == SkaterState.WallPlant) { boardRot = Quaternion.Euler(-62f, 0, 0); boardPos = new Vector3(0, 0.32f, 0.12f); } // Nose an der Wand
            else if (State == SkaterState.WallRide && align != null)
            {
                // Deck flach an der Wand: was das Kippen des Koerpers noch nicht geschafft hat, dreht das Board selbst
                Vector3 fwd = Vector3.ProjectOnPlane(align.forward, _rideNormal).normalized;
                boardRot = Quaternion.Inverse(align.rotation) * Quaternion.LookRotation(fwd, _rideNormal);
                boardPos = new Vector3(0, 0.1f, 0);
            }
            else if (_popTimer > 0f) boardRot = Quaternion.Euler(-28f * Mathf.Sin(_popTimer / 0.22f * Mathf.PI), 0, 0); // Tail schnappt
            else if (_grabbing) boardPos.y += 0.1f;
            else if (State == SkaterState.Grinding && _grindName == "BOARDSLIDE") { boardRot = Quaternion.Euler(0, 90f, 0); boardPos.y = slideBoardY; }
            else if (State == SkaterState.Grinding) boardPos.y = grindBoardY;
            else if (Manualing) boardRot = Quaternion.Euler(-12f, 0, 0);
            else if (State == SkaterState.Bailed) { boardRot = Quaternion.Euler(0, 40f, 170f); boardPos = new Vector3(0.6f, 0.08f, 0.5f); }
            else if (State == SkaterState.Walking) { boardRot = Quaternion.Euler(0f, 0f, 90f); boardPos = new Vector3(0.3f, 0.66f, 0.04f); } // unter dem rechten Arm

            boardPivot.localRotation = Quaternion.Slerp(boardPivot.localRotation, boardRot, _flip != FlipKind.None ? 1f : 1f - Mathf.Exp(-18f * dt));
            boardPivot.localPosition = Vector3.Lerp(boardPivot.localPosition, boardPos, 1f - Mathf.Exp(-20f * dt));
        }
    }
}
