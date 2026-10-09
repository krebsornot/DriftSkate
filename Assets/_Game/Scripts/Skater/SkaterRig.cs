using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Prozedurale Animation der Spielfigur auf dem Board:
    /// Fuesse per IK fest auf dem Deck (oder beim Pushen auf dem Boden), Huefte und Knie federn,
    /// Oberkoerper lehnt in Kurven, Arme balancieren, Haende greifen bei Grabs ans Board, Kopf schaut nach vorn.
    /// Eingaben (crouch, lean, pushPhase ...) setzt der SkaterController jeden Frame.
    /// </summary>
    public class SkaterRig : MonoBehaviour
    {
        /// <summary>Hoehe der Deck-Oberseite ueber dem BoardPivot (setzt SkaterBuilder je nach Board).</summary>
        public float deckTop = 0.035f;

        // Pro Modell gemessen (siehe Init): Knoechelhoehe ueber der Sohle und Hueftpositionen ueber dem Deck
        float AnkleHeight = 0.115f, StandHip = 0.87f, CrouchHip = 0.5f, TuckHip = 0.42f;

        [Header("Bezugspunkte")]
        public Transform board;   // BoardPivot (Deck)
        public Transform frame;   // BodyPivot: Blickrichtung der Figur (seitlich zum Board)
        public Transform travel;  // Skater-Wurzel: Fahrtrichtung

        [Header("Pose (vom Controller gesetzt)")]
        [Range(0, 1)] public float crouch = 0.1f;
        [Range(-1, 1)] public float lean;
        public float pushPhase = -1f;
        [Range(0, 1)] public float brake;
        [Range(0, 1)] public float airborne;
        [Range(0, 1)] public float manual;
        [Range(0, 1)] public float grind;
        [Range(0, 1)] public float bail;
        public string grab;
        /// <summary>Skitchen: eine Hand haelt sich am Auto fest (Gewicht 0..1, Griffpunkt in Weltkoordinaten).</summary>
        [Range(0, 1)] public float hold;
        public Vector3 holdPoint;
        public bool regularStance = true;
        /// <summary>Zu Fuss: 0 = auf dem Board, 1 = gehen/rennen mit dem Board unter dem rechten Arm.</summary>
        [Range(0, 1)] public float walk;
        /// <summary>Zu Fuss gesprungen: Knie angezogen, Arme hoch (0..1).</summary>
        [Range(0, 1)] public float walkJump;

        [Header("Animation Lab (0 = bisherige Animation)")]
        [Range(0, 1)] public float proceduralLife;
        float _lifeTime, _acceleration, _turnRate, _previousSpeed, _previousYaw;
        bool _motionReady;

        Transform _hips, _spine, _chest, _neck, _head;
        /// <summary>Brust- und Halsknochen (z. B. fuer Fluegel am Ruecken).</summary>
        public Transform Chest => _chest;
        public Transform Neck => _neck;
        readonly Transform[] _upperArm = new Transform[2], _lowerArm = new Transform[2], _hand = new Transform[2];
        readonly Transform[] _upperLeg = new Transform[2], _lowerLeg = new Transform[2], _foot = new Transform[2];
        Transform[] _bones;
        Quaternion[] _restRot;
        Vector3[] _restPos;
        readonly Vector3[] _toeLocal = new Vector3[2], _soleUpLocal = new Vector3[2];
        Vector3 _headFaceLocal;
        float _thigh, _shin, _upper, _fore;
        float _pushWeight, _grabWeight, _armSwing, _feetFree;
        // Gehen: Tempo und Schrittphase aus der tatsaechlichen Bewegung (so stimmt es auch bei anderen Spielern online)
        Vector3 _lastRoot;
        bool _hasLastRoot;
        float _walkSpeed, _walkPhase;
        bool _ok;

        public bool Ready => _ok;

        /// <summary>Warum das Modell nicht animiert werden kann (z. B. fehlende Knochen), sonst null.</summary>
        public string Error { get; private set; }

        /// <summary>
        /// Knochen suchen und Ruhepose merken. Das Modell muss bereits aufrecht und nach vorn (frame.forward)
        /// ausgerichtet und auf Spielgroesse skaliert sein.
        /// </summary>
        public void Init(Transform modelRoot, string[] boneOverrides = null)
        {
            var map = SkeletonMap.Resolve(modelRoot, boneOverrides, out string missing);
            if (missing != null)
            {
                _ok = false;
                Error = "Knochen nicht gefunden: " + missing;
                Debug.LogWarning("SkaterRig: " + Error);
                return;
            }
            _hips = map.hips; _spine = map.spine; _chest = map.chest; _neck = map.neck; _head = map.head;
            for (int i = 0; i < 2; i++)
            {
                _upperArm[i] = map.upperArm[i];
                _lowerArm[i] = map.lowerArm[i];
                _hand[i] = map.hand[i];
                _upperLeg[i] = map.upperLeg[i];
                _lowerLeg[i] = map.lowerLeg[i];
                _foot[i] = map.foot[i];
            }
            _ok = true;

            var list = new System.Collections.Generic.List<Transform> { _hips, _spine, _chest, _neck, _head };
            for (int i = 0; i < 2; i++)
                list.AddRange(new[] { _upperArm[i], _lowerArm[i], _hand[i], _upperLeg[i], _lowerLeg[i], _foot[i] });
            var unique = new System.Collections.Generic.List<Transform>();
            foreach (var t in list) if (!unique.Contains(t)) unique.Add(t);
            _bones = unique.ToArray();
            _restRot = new Quaternion[_bones.Length];
            _restPos = new Vector3[_bones.Length];
            for (int i = 0; i < _bones.Length; i++)
            {
                _restRot[i] = _bones[i].localRotation;
                _restPos[i] = _bones[i].localPosition;
            }

            // Blickrichtung aus der Hueftlinie, nicht aus modelRoot.forward: viele Modelle (auch die
            // Standardfigur aus Blender) schauen entlang ihrer -Z-Achse, dann waeren Fuesse und Kopf verdreht.
            Vector3 modelUp = modelRoot.parent != null ? modelRoot.parent.up : Vector3.up;
            Vector3 left = Vector3.ProjectOnPlane(_upperLeg[0].position - _upperLeg[1].position, modelUp);
            Vector3 facing = left.sqrMagnitude > 1e-6f ? Vector3.Cross(modelUp, left.normalized) : Vector3.ProjectOnPlane(modelRoot.forward, modelUp).normalized;
            for (int i = 0; i < 2; i++)
            {
                _toeLocal[i] = _foot[i].InverseTransformDirection(facing);
                _soleUpLocal[i] = _foot[i].InverseTransformDirection(modelUp);
            }
            _headFaceLocal = _head.InverseTransformDirection(facing);
            _thigh = Vector3.Distance(_upperLeg[0].position, _lowerLeg[0].position);
            _shin = Vector3.Distance(_lowerLeg[0].position, _foot[0].position);
            _upper = Vector3.Distance(_upperArm[0].position, _lowerArm[0].position);
            float legLength = _thigh + _shin;
            _fore = Vector3.Distance(_lowerArm[0].position, _hand[0].position) + legLength * 0.09f;

            // Knoechelhoehe = Fussknochen ueber dem tiefsten Punkt des Modells (Sohle)
            float sole = float.MaxValue;
            foreach (var r in modelRoot.GetComponentsInChildren<Renderer>())
                if (r.enabled && r.gameObject.activeInHierarchy) sole = Mathf.Min(sole, r.bounds.min.y);
            float footY = (_foot[0].position.y + _foot[1].position.y) * 0.5f;
            AnkleHeight = sole < float.MaxValue ? Mathf.Clamp(footY - sole, 0.03f, 0.25f) : 0.1f;
            float hipDrop = Mathf.Max(0f, _hips.position.y - (_upperLeg[0].position.y + _upperLeg[1].position.y) * 0.5f);
            StandHip = AnkleHeight + hipDrop + legLength * 0.94f;
            CrouchHip = AnkleHeight + hipDrop + legLength * 0.5f;
            TuckHip = AnkleHeight + hipDrop + legLength * 0.4f;
        }

        void LateUpdate() => ApplyPose(Time.deltaTime);

        /// <summary>Pose sofort anwenden (auch im Editor fuer Vorschau-Bilder).</summary>
        public void ApplyPose(float dt)
        {
            if (!_ok || board == null || frame == null) return;
            Pose(dt);
        }

        void Pose(float dt)
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                _bones[i].localRotation = _restRot[i];
                _bones[i].localPosition = _restPos[i];
            }

            Transform root = travel != null ? travel : frame;
            Transform stance = board.parent != null ? board.parent : frame;

            // Bei Flip-Tricks loesen sich die Fuesse vom Board und schweben ueber der Standposition
            float boardTilt = Vector3.Angle(board.up, stance.up);
            float boardYaw = Vector3.Angle(Vector3.ProjectOnPlane(board.forward, stance.up), stance.forward);
            bool free = boardTilt > 35f || (airborne > 0.5f && boardYaw > 25f);
            _feetFree = Mathf.MoveTowards(_feetFree, free && bail < 0.5f ? 1f : 0f, dt * 12f);

            Vector3 up = Vector3.Slerp(board.up, stance.up, _feetFree).normalized;
            up = Vector3.Slerp(up, Vector3.up, walk).normalized;
            // Sturz: das Board liegt umgedreht da, die Figur richtet sich trotzdem nach oben (sonst steckt sie im Boden)
            up = Vector3.Slerp(up, Vector3.up, bail).normalized;
            Vector3 noseDir = Vector3.Slerp(board.forward, stance.forward, _feetFree).normalized;
            Vector3 travelDir = Vector3.ProjectOnPlane(root.forward, up).normalized;
            Vector3 bodyFwd = Vector3.ProjectOnPlane(frame.forward, up).normalized; // Zehenseite
            Vector3 bodyRight = Vector3.Cross(up, bodyFwd);
            int front = regularStance ? 0 : 1, back = 1 - front;

            // Gehen: Tempo entlang der Blickrichtung messen, Schrittphase danach weiterzaehlen
            float measured = 0f;
            Vector3 delta = root.position - _lastRoot;
            delta.y = 0f;
            if (_hasLastRoot && dt > 1e-5f && delta.magnitude < 3f) measured = Vector3.Dot(delta, travelDir) / dt;
            _lastRoot = root.position;
            _hasLastRoot = true;
            if (dt > 1e-5f) _walkSpeed = Mathf.Lerp(_walkSpeed, measured, 1f - Mathf.Exp(-10f * dt));
            float moving = Mathf.Clamp01(Mathf.Abs(_walkSpeed) / 0.6f);
            float run = Mathf.InverseLerp(3f, 6f, Mathf.Abs(_walkSpeed));
            float stride = Mathf.Lerp(0.3f, 0.48f, run); // halbe Schrittlaenge
            if (walk > 0.001f) _walkPhase = Mathf.Repeat(_walkPhase + _walkSpeed * dt / (4f * stride), 1f);
            float gait = _walkPhase * Mathf.PI * 2f;
            // Filter actual motion, so collisions and stopping affect the pose as well as input.
            float life = proceduralLife * (1f - bail);
            float safeDt = Mathf.Clamp(dt, 0f, 0.05f);
            _lifeTime += safeDt;
            float yaw = root.eulerAngles.y;
            bool continuous = _motionReady && delta.sqrMagnitude < 9f && dt > 0.00001f;
            float response = 1f - Mathf.Exp(-7f * safeDt);
            _acceleration = Mathf.Lerp(_acceleration, continuous ? Mathf.Clamp((measured - _previousSpeed) / dt, -12f, 12f) : 0f, response);
            _turnRate = Mathf.Lerp(_turnRate, continuous ? Mathf.Clamp(Mathf.DeltaAngle(_previousYaw, yaw) / dt, -180f, 180f) : 0f, response);
            _previousSpeed = measured;
            _previousYaw = yaw;
            _motionReady = true;
            float breath = Mathf.Sin(_lifeTime * 2.2f);
            float balanceWave = Mathf.Sin(_lifeTime * 3.1f) + 0.4f * Mathf.Sin(_lifeTime * 5.3f);
            float rideLife = life * (1f - walk) * (1f - airborne) * (1f - grind);
            float stanceSign = regularStance ? 1f : -1f;
            // Fakie: die Nose zeigt gegen die Fahrtrichtung. Angeschoben und gebremst wird immer mit dem Fuss,
            // der in Fahrtrichtung hinten steht (normal der hintere, Fakie der vordere), der andere bleibt stehen.
            bool fakieRide = Vector3.Dot(noseDir, travelDir) < 0f;
            float rideSign = fakieRide ? -1f : 1f;
            int pushFoot = fakieRide ? front : back;

            _pushWeight = Mathf.MoveTowards(_pushWeight, pushPhase >= 0f ? 1f : 0f, dt * 6f);
            _grabWeight = Mathf.MoveTowards(_grabWeight, string.IsNullOrEmpty(grab) ? 0f : 1f, dt * 9f);

            // ---------------------------------------------------- Fusspunkte
            Vector3 deckFront = Vector3.Lerp(board.TransformPoint(new Vector3(0f, deckTop, 0.19f)),
                stance.TransformPoint(new Vector3(0f, 0.1f + deckTop + 0.24f, 0.19f)), _feetFree) - bodyFwd * 0.03f;
            Vector3 deckBack = Vector3.Lerp(board.TransformPoint(new Vector3(0f, deckTop, -0.21f)),
                stance.TransformPoint(new Vector3(0f, 0.1f + deckTop + 0.24f, -0.21f)), _feetFree) - bodyFwd * 0.03f;
            Vector3 pushDeck = fakieRide ? deckFront : deckBack;
            // Zehenseite genau quer zur Fahrtrichtung (die Figur steht 80 statt 90 Grad gedreht; sonst laege der
            // Anschub-Punkt bei Fakie weiter hinten als normal und der Fuss kaeme nicht auf den Boden)
            Vector3 toeSide = Vector3.ProjectOnPlane(bodyFwd, travelDir).normalized;
            Vector3 pushTarget = pushDeck;
            Vector3 backToe = Vector3.ProjectOnPlane(Quaternion.AngleAxis(-8f * stanceSign, up) * bodyFwd, up).normalized;
            Vector3 frontToe = Vector3.ProjectOnPlane(Quaternion.AngleAxis(18f * stanceSign, up) * bodyFwd, up).normalized;
            Vector3 pushToe = fakieRide ? frontToe : backToe, standToe = fakieRide ? backToe : frontToe;
            Vector3 groundUp = Vector3.up;

            if (pushPhase >= 0f)
            {
                // Der in Fahrtrichtung hintere Fuss stoesst sich auf der Zehenseite vom Boden ab
                Vector3 ground = root.position;
                Vector3 a = ground + toeSide * 0.26f + travelDir * 0.08f;
                Vector3 b = a - travelDir * 0.6f;
                float p = pushPhase;
                Vector3 target;
                if (p < 0.2f)
                {
                    float t = Smooth(p / 0.2f);
                    target = Vector3.Lerp(pushDeck, a, t) + groundUp * Mathf.Sin(t * Mathf.PI) * 0.12f;
                }
                else if (p < 0.65f) target = Vector3.Lerp(a, b, Smooth((p - 0.2f) / 0.45f));
                else
                {
                    float t = Smooth((p - 0.65f) / 0.35f);
                    target = Vector3.Lerp(b, pushDeck, t) + groundUp * Mathf.Sin(t * Mathf.PI) * 0.2f;
                }
                pushTarget = Vector3.Lerp(pushDeck, target, _pushWeight);
                pushToe = Vector3.Slerp(pushToe, travelDir, _pushWeight * 0.8f);
                standToe = Vector3.Slerp(standToe, travelDir, _pushWeight * 0.6f);
            }
            if (brake > 0.01f)
            {
                // Fussbremse: der hintere Fuss (in Fahrtrichtung) schleift hinter dem Board
                Vector3 drag = root.position + toeSide * 0.22f - travelDir * 0.38f;
                pushTarget = Vector3.Lerp(pushTarget, drag, brake);
                pushToe = Vector3.Slerp(pushToe, travelDir, brake * 0.7f);
            }

            // ---------------------------------------------------- Huefte
            float hipH = Mathf.Lerp(StandHip, CrouchHip, crouch);
            hipH = Mathf.Lerp(hipH, TuckHip, Mathf.Max(_grabWeight, airborne * 0.35f));
            hipH -= _pushWeight * 0.1f;
            hipH += rideLife * (breath * 0.006f - Mathf.Abs(balanceWave) * 0.012f);
            hipH = Mathf.Lerp(hipH, 0.24f, bail); // liegt flach: Becken knapp ueber dem Boden
            float leanDeg = -lean * 16f;
            Vector3 mid = (deckFront + deckBack) * 0.5f;
            Vector3 travelRight = Vector3.Cross(up, travelDir);
            Vector3 hipsPos = mid + up * hipH - bodyFwd * 0.02f
                              + noseDir * (-manual * 0.14f + _pushWeight * 0.12f * stanceSign * rideSign)
                              + travelRight * lean * 0.1f;
            hipsPos += rideLife * (travelRight * (balanceWave * 0.012f + _turnRate * 0.0002f)
                - travelDir * _acceleration * 0.003f);
            if (walk > 0.001f)
            {
                // Beim Gehen wippt die Huefte (tiefster Punkt, wenn beide Fuesse am Boden sind), beim Rennen tiefer und vorgebeugt
                float bob = (1f - Mathf.Abs(Mathf.Sin(gait))) * Mathf.Lerp(0.02f, 0.05f, run) * moving;
                Vector3 walkHips = root.position + up * (StandHip * Mathf.Lerp(0.985f, 0.955f, run) - bob) + travelDir * 0.07f * run;
                walkHips -= up * Mathf.Max(0f, crouch - 0.1f) * 0.3f * (1f - walkJump); // Landung federt ab
                walkHips += life * (1f - walkJump) * (bodyRight * Mathf.Sin(gait) * 0.025f * moving
                    + up * breath * 0.005f * (1f - moving));
                hipsPos = Vector3.Lerp(hipsPos, walkHips, walk);
            }
            if (hold > 0.001f)
            {
                // Zum Auto hin lehnen, damit die Hand rankommt
                Vector3 toHold = Vector3.ProjectOnPlane(holdPoint - hipsPos, up);
                if (toHold.sqrMagnitude > 1e-4f) hipsPos += toHold.normalized * 0.1f * hold;
            }
            _hips.position = hipsPos;
            _hips.rotation = Quaternion.AngleAxis(leanDeg * (1f - walk) + Mathf.Sin(gait) * 6f * moving * walk, walk > 0.5f ? up : travelDir) * _hips.rotation;

            // ---------------------------------------------------- Oberkoerper
            float bend = Mathf.Lerp(6f, 30f, crouch) + _pushWeight * 14f + _grabWeight * 34f + grind * 8f;
            bend = Mathf.Lerp(bend, 4f + 14f * run * moving + 12f * walkJump, walk);
            bend += life * (breath * 0.9f + _acceleration * 0.65f);
            _spine.rotation = Quaternion.AngleAxis(bend * 0.45f, bodyRight) * _spine.rotation;
            _chest.rotation = Quaternion.AngleAxis(bend * 0.55f, bodyRight) * _chest.rotation;
            float twist = -(18f + _pushWeight * 30f) * stanceSign * rideSign; // Oberkoerper oeffnet sich zur Fahrtrichtung
            twist = Mathf.Lerp(twist, -Mathf.Sin(gait) * 9f * moving, walk); // Schultern gegen die Huefte
            _spine.rotation = Quaternion.AngleAxis(twist * 0.4f, up) * _spine.rotation;
            _chest.rotation = Quaternion.AngleAxis(twist * 0.6f, up) * _chest.rotation;
            _chest.rotation = Quaternion.AngleAxis(-_turnRate * 0.035f * life, bodyFwd) * _chest.rotation;

            // ---------------------------------------------------- Beine
            if (bail < 0.5f)
            {
                for (int i = 0; i < 2; i++)
                {
                    bool isFront = i == front, pushes = i == pushFoot;
                    Vector3 sole = pushes ? pushTarget : (isFront ? deckFront : deckBack);
                    Vector3 soleUp = !pushes || _pushWeight < 0.5f ? up : Vector3.Slerp(up, groundUp, _pushWeight);
                    Vector3 kneeHint = _upperLeg[i].position + bodyFwd * 0.7f + noseDir * (isFront ? 0.2f : -0.2f) * stanceSign;
                    Vector3 toe = pushes ? pushToe : standToe;
                    if (walk > 0.001f)
                    {
                        // Schritt: Standphase schiebt den Fuss nach hinten, Schwungphase hebt ihn nach vorn
                        float ph = Mathf.Repeat(_walkPhase + (i == 0 ? 0f : 0.5f), 1f), A = stride * moving, x, lift = 0f;
                        if (ph < 0.5f) x = Mathf.Lerp(A, -A, ph / 0.5f);
                        else
                        {
                            float t = (ph - 0.5f) / 0.5f;
                            x = Mathf.Lerp(-A, A, Smooth(t));
                            lift = Mathf.Sin(t * Mathf.PI) * Mathf.Lerp(0.07f, 0.16f, run) * moving;
                        }
                        Vector3 walkSole = root.position + travelDir * x + bodyRight * (i == 0 ? -0.1f : 0.1f) + up * lift;
                        sole = Vector3.Lerp(sole, walkSole, walk);
                        soleUp = Vector3.Slerp(soleUp, up, walk).normalized;
                        // Heel strike / toe-off, fading out when stopped or airborne.
                        float roll = ph < 0.5f ? Mathf.Lerp(-12f, 20f, ph * 2f) : -Mathf.Sin((ph - 0.5f) * Mathf.PI * 2f) * 12f;
                        soleUp = Quaternion.AngleAxis(roll * life * moving * walk * (1f - walkJump), bodyRight) * soleUp;
                        kneeHint = Vector3.Lerp(kneeHint, _upperLeg[i].position + travelDir * 0.8f, walk);
                        toe = Vector3.Slerp(toe, travelDir, walk);
                        if (walkJump > 0.001f)
                        {
                            // Sprung: Knie angezogen, ein Bein vorn, das andere hinten
                            Vector3 jumpSole = root.position + travelDir * (i == 0 ? 0.2f : -0.14f) + bodyRight * (i == 0 ? -0.1f : 0.1f)
                                               + up * (i == 0 ? 0.34f : 0.22f);
                            sole = Vector3.Lerp(sole, jumpSole, walkJump * walk);
                            soleUp = Vector3.Slerp(soleUp, (up + travelDir * (i == 0 ? 0.3f : -0.4f)).normalized, walkJump * walk).normalized;
                        }
                    }
                    Vector3 ankle = sole + soleUp * AnkleHeight;
                    TwoBone(_upperLeg[i], _lowerLeg[i], _foot[i], ankle, kneeHint, _thigh, _shin);
                    OrientFoot(i, toe, soleUp);
                }
            }
            else
            {
                for (int i = 0; i < 2; i++)
                    _lowerLeg[i].rotation = Quaternion.AngleAxis(65f, bodyRight) * _lowerLeg[i].rotation; // liegt auf dem Bauch: Unterschenkel nach oben
            }

            // ---------------------------------------------------- Arme
            _armSwing = Mathf.Lerp(_armSwing, pushPhase >= 0f ? Mathf.Sin(pushPhase * Mathf.PI * 2f) : 0f, 1f - Mathf.Exp(-10f * dt));
            float raise = Mathf.Lerp(24f, 58f, Mathf.Max(airborne * 0.7f, grind, manual * 0.8f, bail));
            for (int i = 0; i < 2; i++)
            {
                Vector3 side = i == 0 ? -bodyRight : bodyRight;
                float leanRaise = (i == 0 ? 1f : -1f) * lean * 18f * stanceSign;
                leanRaise += rideLife * (Mathf.Sin(_lifeTime * 3.1f + i * 1.7f) * 5f + _turnRate * (i == 0 ? 0.045f : -0.045f));
                float a = (raise + leanRaise) * Mathf.Deg2Rad;
                float swing = (i == front ? 1f : -1f) * _armSwing * 0.6f * _pushWeight;
                Vector3 upperDir = (-up * Mathf.Cos(a) + side * Mathf.Sin(a) + bodyFwd * 0.15f + noseDir * swing * stanceSign).normalized;
                Aim(_upperArm[i], _lowerArm[i].position, upperDir);
                Vector3 foreDir = Vector3.Slerp(upperDir, (bodyFwd + up * 0.3f).normalized, 0.3f + bail * 0.4f).normalized;
                if (walk > 0.001f)
                {
                    // Arme schwingen gegengleich zu den Beinen (linker Arm vor, wenn das rechte Bein vorn ist)
                    float legX = Mathf.Cos(gait + (i == 0 ? 0f : Mathf.PI));
                    float armSwing = -legX * Mathf.Lerp(0.35f, 0.85f, run) * moving;
                    Vector3 wUpper = (-up + travelDir * armSwing + side * 0.12f).normalized;
                    Vector3 wFore = (wUpper + travelDir * (0.25f + 0.6f * run) + up * 0.1f * run).normalized;
                    // Sprung: Arme schwingen nach oben und zur Seite
                    Vector3 jUpper = (up * 0.25f + travelDir * 0.55f + side * 0.6f).normalized;
                    Vector3 jFore = (up * 0.7f + travelDir * 0.4f).normalized;
                    wUpper = Vector3.Slerp(wUpper, jUpper, walkJump).normalized;
                    wFore = Vector3.Slerp(wFore, jFore, walkJump).normalized;
                    Aim(_upperArm[i], _lowerArm[i].position, Vector3.Slerp(upperDir, wUpper, walk));
                    foreDir = Vector3.Slerp(foreDir, wFore, walk).normalized;
                }
                Aim(_lowerArm[i], _hand[i].position, foreDir);
            }

            // Zu Fuss: die rechte Hand haelt das Board an der oberen Kante
            if (walk > 0.01f)
            {
                const int h = 1;
                Vector3 grip = board.TransformPoint(new Vector3(0.11f, deckTop * 0.5f, 0f));
                Vector3 hint = _upperArm[h].position + bodyRight * 0.4f - travelDir * 0.2f;
                TwoBone(_upperArm[h], _lowerArm[h], _hand[h], Vector3.Lerp(_hand[h].position, grip, walk), hint, _upper, _fore);
            }

            // Grab: eine Hand greift an die Board-Kante
            if (_grabWeight > 0.01f && !string.IsNullOrEmpty(grab))
            {
                GrabTarget(grab, front, back, out int hand, out Vector3 local);
                Vector3 target = board.TransformPoint(local + Vector3.up * (deckTop - 0.035f)); // Griffpunkte relativ zur Deckhoehe
                Vector3 hint = _upperArm[hand].position + (hand == 0 ? -bodyRight : bodyRight) * 0.4f - up * 0.2f;
                Vector3 current = _hand[hand].position;
                TwoBone(_upperArm[hand], _lowerArm[hand], _hand[hand], Vector3.Lerp(current, target, _grabWeight), hint, _upper, _fore);
            }

            // Skitchen: die Hand naeher am Auto greift den Haltepunkt (Arm hoechstens gestreckt)
            if (hold > 0.01f)
            {
                int hand = (_upperArm[0].position - holdPoint).sqrMagnitude < (_upperArm[1].position - holdPoint).sqrMagnitude ? 0 : 1;
                Vector3 shoulder = _upperArm[hand].position;
                Vector3 reach = holdPoint - shoulder;
                float maxReach = (_upper + _fore) * 0.98f;
                if (reach.magnitude > maxReach) reach = reach.normalized * maxReach;
                Vector3 hint = shoulder + (hand == 0 ? -bodyRight : bodyRight) * 0.25f - up * 0.4f;
                TwoBone(_upperArm[hand], _lowerArm[hand], _hand[hand], Vector3.Lerp(_hand[hand].position, shoulder + reach, hold), hint, _upper, _fore);
            }

            // ---------------------------------------------------- Kopf
            Vector3 face = _head.TransformDirection(_headFaceLocal);
            Vector3 look = Vector3.Slerp(Vector3.ProjectOnPlane(face, up), travelDir, 0.8f);
            Quaternion turn = Quaternion.FromToRotation(Vector3.ProjectOnPlane(face, up), look);
            _neck.rotation = Quaternion.Slerp(Quaternion.identity, turn, 0.35f) * _neck.rotation;
            _head.rotation = Quaternion.Slerp(Quaternion.identity, turn, 0.65f) * _head.rotation;
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        void GrabTarget(string name, int front, int back, out int hand, out Vector3 local)
        {
            switch (name)
            {
                case "MELON": hand = front; local = new Vector3(-0.12f, 0.02f, 0.02f); break;
                case "STALEFISH": hand = back; local = new Vector3(-0.12f, 0.02f, -0.24f); break;
                case "NOSEGRAB": hand = front; local = new Vector3(0f, 0.04f, 0.36f); break;
                case "TAILGRAB": hand = back; local = new Vector3(0f, 0.04f, -0.36f); break;
                default: hand = back; local = new Vector3(0.12f, 0.02f, -0.04f); break; // INDY
            }
        }

        static void Aim(Transform bone, Vector3 childPos, Vector3 dir)
        {
            Vector3 cur = childPos - bone.position;
            if (cur.sqrMagnitude < 1e-8f || dir.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
        }

        void OrientFoot(int i, Vector3 toe, Vector3 soleUp)
        {
            var f = _foot[i];
            toe = Vector3.ProjectOnPlane(toe, soleUp);
            if (toe.sqrMagnitude < 1e-6f) return;
            Vector3 curToe = f.TransformDirection(_toeLocal[i]);
            Vector3 curUp = f.TransformDirection(_soleUpLocal[i]);
            Quaternion from = Quaternion.LookRotation(curToe, curUp);
            Quaternion to = Quaternion.LookRotation(toe.normalized, soleUp);
            f.rotation = to * Quaternion.Inverse(from) * f.rotation;
        }

        /// <summary>Analytische Zwei-Knochen-IK (Oberschenkel-Unterschenkel-Fuss bzw. Ober-/Unterarm-Hand).</summary>
        static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint, float lenA, float lenB)
        {
            Vector3 aPos = a.position;
            Vector3 toTarget = target - aPos;
            float dist = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(lenA - lenB) + 0.001f, lenA + lenB - 0.001f);
            Vector3 dir = toTarget.normalized;
            Vector3 hintDir = Vector3.ProjectOnPlane(hint - aPos, dir);
            if (hintDir.sqrMagnitude < 1e-6f) hintDir = Vector3.ProjectOnPlane(Vector3.forward, dir);
            hintDir.Normalize();
            float cosA = (lenA * lenA + dist * dist - lenB * lenB) / (2f * lenA * dist);
            float angA = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f));
            Vector3 bWanted = aPos + (dir * Mathf.Cos(angA) + hintDir * Mathf.Sin(angA)) * lenA;
            a.rotation = Quaternion.FromToRotation(b.position - aPos, bWanted - aPos) * a.rotation;
            Vector3 cWanted = aPos + dir * dist;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, cWanted - b.position) * b.rotation;
        }
    }
}
