using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DriftSkate
{
    /// <summary>
    /// Luna (links am Sofa) bietet Spielmodi an: Sprint zum Ziel, Drift-Lieferung (unterwegs Punkte sammeln),
    /// Skate-Line (mit dem Board). Nach dem Annehmen steht man startklar auf der Strasse, eine Drohnen-Kamera
    /// fliegt zum Ziel und kreist darum, dann 3-2-1-GO. Ankommen in der Zeit (mit genug Punkten) gibt Geld,
    /// fuer jede uebrige Sekunde etwas extra. Nach allen Modi geht es schwerer von vorn los.
    /// </summary>
    public class LunaChallenges : QuestNpc
    {
        public static LunaChallenges Instance { get; private set; }

        public enum Kind { Race, DriftRun, SkateRun }
        public enum Phase { Open, Intro, Running }

        class Challenge
        {
            public string title, rule;
            public Kind kind;
            public int goal;
            public float time, points;
            public long reward;
            public string[] intro;
        }

        /// <summary>{T} = Zeit, {P} = Punkte, {R} = Belohnung.</summary>
        static readonly Challenge[] Challenges =
        {
            new Challenge
            {
                title = "SPRINT", kind = Kind.Race, goal = 0, time = 28f, reward = 2500,
                rule = "Mit dem Auto zum Ziel. Keine Punkte noetig, nur Tempo.",
                intro = new[]
                {
                    "Hey du. Du hast so 'ne... Energie, weisst du?",
                    "Ich hab da 'n Spiel. Ich zeig dir einen Punkt, du faehrst hin. {T}, bevor der Beat vorbei ist.",
                    "Schaffst du's, gibt's {R}. Plus was fuer jede Sekunde, die uebrig bleibt."
                }
            },
            new Challenge
            {
                title = "DRIFT-LIEFERUNG", kind = Kind.DriftRun, goal = 1, time = 75f, points = 6000f, reward = 4000,
                rule = "Zum Ziel fahren und unterwegs Punkte mit Drifts sammeln.",
                intro = new[]
                {
                    "Okay, das war schnell. Aber schnell ist nicht alles.",
                    "Diesmal will ich Style sehen. Fahr zum Ziel und sammel unterwegs {P} mit Drifts.",
                    "{T} hast du. Und {R}, wenn's schoen aussieht."
                }
            },
            new Challenge
            {
                title = "SKATE-LINE", kind = Kind.SkateRun, goal = 2, time = 40f, points = 2500f, reward = 3000,
                rule = "Mit dem Board zum Ziel, unterwegs Punkte mit Tricks holen.",
                intro = new[]
                {
                    "Lass das Auto mal stehen.",
                    "Board only: zum Ziel rollen und unterwegs {P} mit Tricks holen. {T}.",
                    "Wie 'ne Tanz-Choreo, nur auf Rollen. {R} fuer dich."
                }
            },
            new Challenge
            {
                title = "NACHTEXPRESS", kind = Kind.Race, goal = 3, time = 30f, reward = 3000,
                rule = "Mit dem Auto zum Ziel, so schnell es geht.",
                intro = new[]
                {
                    "Die Stadt klingt nachts anders, findest du nicht?",
                    "Neues Ziel, andere Ecke. {T}. Hoer auf den Motor.",
                    "{R}, wenn du's packst."
                }
            },
            new Challenge
            {
                title = "DRIFT KING", kind = Kind.DriftRun, goal = 4, time = 90f, points = 15000f, reward = 6000,
                rule = "Weiter Weg, viele Punkte: drifte dich zum Ziel.",
                intro = new[]
                {
                    "Okay. Das Grosse jetzt.",
                    "Ganz ans andere Ende der Stadt. Unterwegs {P} mit Drifts. {T}.",
                    "Wenn du das schaffst, bist du offiziell Teil der Couch. Und kriegst {R}."
                }
            },
        };

        /// <summary>Ziele an Kreuzungen, gut von oben zu sehen.</summary>
        static Vector3 GoalPosition(int i)
        {
            switch (i)
            {
                case 0: return new Vector3(CityBuilder.RoadCenter(1), 0f, CityBuilder.RoadCenter(5));
                case 1: return new Vector3(CityBuilder.RoadCenter(4), 0f, CityBuilder.RoadCenter(0));
                case 2: return new Vector3(CityBuilder.RoadCenter(2), 0f, CityBuilder.RoadCenter(3));
                case 3: return new Vector3(CityBuilder.RoadCenter(3), 0f, CityBuilder.RoadCenter(1));
                default: return new Vector3(CityBuilder.RoadCenter(5), 0f, CityBuilder.RoadCenter(4));
            }
        }

        public static readonly Color LunaColor = Palette.Pink;

        public override string Speaker => "LUNA";
        protected override Color TagColor => LunaColor;
        protected override string TrackerTag => "LUNAS CHALLENGE";

        public Phase State { get; private set; }
        public float TimeLeft => _timeLeft;
        public float Points => _banked + (_combo != null && _combo.Active ? _combo.Total : 0f);
        public Vector3 Goal => _goal;

        float _timeLeft, _banked, _arrivedToast;
        Vector3 _goal;
        GameObject _beacon;
        ComboSystem _combo;
        RectTransform _cine;
        Image _barTop, _barBottom;
        Text _cardTitle, _cardRule, _cardInfo;
        bool _testSkip;
        Coroutine _slide;

        int Index => SaveSystem.Profile.lunaChallenge;
        Challenge Current => Challenges[Index % Challenges.Length];
        int Round => Index / Challenges.Length;
        float TimeLimit => Mathf.Round(Current.time * Mathf.Max(0.7f, 1f - 0.08f * Round));
        float PointsNeeded => Mathf.Round(Current.points * (1f + 0.4f * Round) / 500f) * 500f;
        long Reward => (long)(Mathf.Round(Current.reward * (1f + 0.5f * Round) / 100f) * 100f);
        bool Driving => Current.kind != Kind.SkateRun;
        float GoalRadius => Driving ? 9f : 6f;

        string Fill(string line) => line.Replace("{T}", TimeLimit.ToString("0") + " Sekunden").Replace("{P}", UIFactory.Money((long)PointsNeeded)).Replace("{R}", UIFactory.Money(Reward));

        protected override bool CanTalk => State == Phase.Open;

        // ------------------------------------------------------------------ Ansprechen

        public override void Talk()
        {
            if (Talking || State != Phase.Open) return;
            var pages = Array.ConvertAll(Current.intro, Fill);
            OpenDialog(pages, "[E] LOS GEHT'S", "[B] SPAETER", accepted =>
            {
                if (accepted) StartCoroutine(Intro());
                else Bark("Okay. Die Musik laeuft eh weiter.");
            });
        }

        /// <summary>Startplatz: Strasse westlich vom Platz (Auto) bzw. vor dem Sofa (Board), Blick Richtung Ziel.</summary>
        void StartPose(out Vector3 pos, out float heading)
        {
            if (Driving)
            {
                pos = Ground(new Vector3(CityBuilder.RoadCenter(1), 0f, StonerNpc.HangoutPosition.z));
                heading = _goal.z >= pos.z ? 0f : 180f; // die Strasse laeuft hier in Nord-Sued-Richtung
            }
            else
            {
                pos = Ground(StonerNpc.HangoutPosition + StonerNpc.HangoutFacing * 6f);
                Vector3 d = _goal - pos;
                heading = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }
        }

        IEnumerator Intro()
        {
            var p = PlayerAvatar.Local;
            if (p == null) yield break;
            State = Phase.Intro;
            GameInput.Blocked = true;
            _goal = Ground(GoalPosition(Current.goal));
            if (_beacon != null) Destroy(_beacon);
            _beacon = BuildBeacon("ChallengeGoal", _goal, LunaColor, false);
            StartPose(out Vector3 start, out float heading);
            p.PlaceForChallenge(Driving, start, heading);
            Bark("Viel Glueck!");
            yield return null;

            // Drohnenflug: hoch ueber den Spieler, rueber zum Ziel, eine langsame Runde drumherum
            ShowCinematic(true, start);
            HUD.Cinematic = true;
            var rig = CameraRig.Instance;
            Vector3 from = PlayerPos(p);
            Vector3 dir = Vector3.ProjectOnPlane(_goal - from, Vector3.up);
            dir = dir.sqrMagnitude > 1f ? dir.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 aPos = from + Vector3.up * 28f - dir * 14f + side * 6f, aLook = from + dir * 25f;
            Vector3 orbitStart = _goal - dir * 30f + Vector3.up * 20f;
            const float tA = 1.6f, tB = 4.2f, tEnd = 7.8f;
            float t = 0f;
            while (t < tEnd && !_testSkip)
            {
                t += Time.unscaledDeltaTime;
                if (t > 0.6f && YesPressed) break;
                Vector3 pos, look;
                if (t < tA)
                {
                    float e = Smooth(t / tA);
                    pos = from + Vector3.up * Mathf.Lerp(5f, 28f, e) - dir * Mathf.Lerp(8f, 14f, e) + side * Mathf.Lerp(0f, 6f, e);
                    look = from + Vector3.up + dir * Mathf.Lerp(2f, 25f, e);
                }
                else if (t < tB)
                {
                    float e = Smooth((t - tA) / (tB - tA));
                    pos = Vector3.Lerp(aPos, orbitStart, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 18f;
                    look = Vector3.Lerp(aLook, _goal + Vector3.up * 3f, Smooth(Mathf.Clamp01(e * 1.3f)));
                }
                else
                {
                    float u = (t - tB) / (tEnd - tB);
                    Vector3 arm = Quaternion.AngleAxis(Mathf.Lerp(0f, 75f, Smooth(u)), Vector3.up) * (-dir * 30f);
                    pos = _goal + arm + Vector3.up * Mathf.Lerp(20f, 13f, Smooth(u));
                    look = _goal + Vector3.up * 3f;
                }
                rig?.SetDialogShot(pos, look);
                yield return null;
            }
            _testSkip = false;
            rig?.ClearDialogShot();
            ShowCinematic(false, start);
            HUD.Cinematic = false;

            // 3 - 2 - 1 - GO (die Kamera gleitet derweil zurueck)
            for (int i = 3; i > 0; i--)
            {
                BurstPop.Show(i.ToString(), Palette.Yellow, new Vector2(0f, 140f), 190f, 0.8f);
                SpraySound.Play(0.35f, false);
                yield return new WaitForSecondsRealtime(0.8f);
            }
            BurstPop.Show("GO!", Palette.Lime, new Vector2(0f, 140f), 230f, 1.1f);
            SpraySound.Play(0.7f, false);
            p.combo.BankNow();
            _banked = 0f;
            _arrivedToast = 0f;
            _timeLeft = TimeLimit;
            State = Phase.Running;
            UnblockNextFrame();
        }

        static float Smooth(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));

        /// <summary>Fuer den Autotest: Drohnenflug ueberspringen.</summary>
        public void TestSkipIntro() => _testSkip = true;

        // ------------------------------------------------------------------ Laufender Modus

        protected override void Start()
        {
            Instance = this;
            base.Start();
            BuildCinematic();
        }

        protected override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_combo != null) _combo.Ended -= OnComboEnded;
            if (_beacon != null) Destroy(_beacon);
            if (_cine != null) Destroy(_cine.gameObject);
            if (State == Phase.Intro) { GameInput.Blocked = false; HUD.Cinematic = false; }
            base.OnDestroy();
        }

        protected override void Update()
        {
            var local = PlayerAvatar.Local;
            var combo = local != null ? local.combo : null;
            if (combo != _combo)
            {
                if (_combo != null) _combo.Ended -= OnComboEnded;
                _combo = combo;
                if (_combo != null) _combo.Ended += OnComboEnded;
            }
            if (local != null && State == Phase.Running) Run(local, Time.deltaTime);
            base.Update();
        }

        void OnComboEnded(long amount, bool failed, string reason)
        {
            if (State == Phase.Running) _banked += amount;
        }

        void Run(PlayerAvatar p, float dt)
        {
            _timeLeft -= dt;
            if (_timeLeft <= 0f)
            {
                End(false, "ZEIT UM!");
                return;
            }
            if ((PlayerPos(p) - _goal).sqrMagnitude > GoalRadius * GoalRadius) return;
            bool rightMode = Driving ? p.Mode == PlayerMode.Driving : p.Mode == PlayerMode.Skating;
            if (!rightMode) return;
            if (Current.kind == Kind.Race || Points >= PointsNeeded) End(true, null);
            else if (Time.time > _arrivedToast)
            {
                _arrivedToast = Time.time + 3f;
                HUD.Instance?.Toast("ZU WENIG STYLE!  Noch " + UIFactory.Money((long)(PointsNeeded - Points)) + " Punkte", 2.5f);
            }
        }

        void End(bool won, string why)
        {
            State = Phase.Open;
            if (_beacon != null) Destroy(_beacon);
            _beacon = null;
            if (won)
            {
                long bonus = Mathf.FloorToInt(_timeLeft) * 40L;
                long total = Reward + bonus;
                float used = TimeLimit - _timeLeft;
                SaveSystem.Profile.lunaChallenge++;
                SaveSystem.AddMoney(total);
                BurstPop.Show("GESCHAFFT!", Palette.Yellow, new Vector2(0f, 300f), 230f, 2f);
                BurstPop.Show("+" + UIFactory.Money(total), Palette.Lime, new Vector2(40f, 120f), 250f, 2.6f);
                HUD.Instance?.Toast($"{Current.title}: {used:0.0} s  (Zeitbonus {UIFactory.Money(bonus)})", 4f);
                SpraySound.Play(0.8f, false);
                Npc?.Play("Laugh");
            }
            else
            {
                BurstPop.Show(why, Palette.Red, new Vector2(0f, 260f), 230f, 2f);
                HUD.Instance?.Toast("Luna wartet am Sofa, wenn du's nochmal versuchen willst.", 4f);
            }
        }

        // ------------------------------------------------------------------ Anzeige

        protected override bool TrackerVisible => State == Phase.Running;
        protected override string TrackerTitle => Current.title;

        protected override string TrackerText
        {
            get
            {
                float t = Mathf.Max(0f, _timeLeft);
                string time = Mathf.FloorToInt(t / 60f) + ":" + (t % 60f).ToString("00.0");
                if (Current.kind == Kind.Race) return "Zum Ziel   " + time;
                return UIFactory.Money((long)Points) + " / " + UIFactory.Money((long)PointsNeeded) + "   " + time;
            }
        }

        protected override Color TrackerTextColor =>
            _timeLeft < 10f && Mathf.Repeat(Time.time, 0.6f) < 0.3f ? Palette.Red
            : Current.kind != Kind.Race && Points >= PointsNeeded ? Palette.Lime : Palette.White;

        protected override Vector3? MarkerGoal => State == Phase.Running ? _goal + Vector3.up * 3f : (Vector3?)null;
        protected override string IndicatorText => State == Phase.Open ? "!" : null;
        protected override Color IndicatorColor => Palette.Cyan;

        /// <summary>Kino-Balken und Karte waehrend des Drohnenflugs.</summary>
        void BuildCinematic()
        {
            var layer = SpeechBubble.Layer;
            _cine = UIFactory.Stretch(layer, "ChallengeIntro");
            _barTop = Bar(_cine, true);
            _barBottom = Bar(_cine, false);

            var card = UIFactory.Panel(_cine, "Card", new Color(0.09f, 0.07f, 0.12f, 0.92f), Vector2.zero, Vector2.zero, Vector2.zero,
                                       new Vector2(60f, 150f), new Vector2(720f, 190f), 1.5f);
            var tag = UIFactory.Panel(card.transform, "Tag", LunaColor, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(-10f, 2f), new Vector2(260f, 40f), -4f);
            var tagText = UIFactory.Label(tag.transform, "LUNAS CHALLENGE", 25, Palette.Ink, TextAnchor.MiddleCenter, false, false);
            tagText.font = UIFactory.GraffitiFont;
            _cardTitle = UIFactory.LabelAt(card.transform, "", 58, Palette.Yellow, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22f, -26f), new Vector2(680f, 66f));
            _cardTitle.font = UIFactory.GraffitiFont;
            _cardTitle.fontStyle = FontStyle.Normal;
            _cardRule = UIFactory.LabelAt(card.transform, "", 25, Palette.White, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22f, -96f), new Vector2(680f, 36f));
            _cardInfo = UIFactory.LabelAt(card.transform, "", 27, Palette.Cyan, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22f, -138f), new Vector2(680f, 38f));
            UIFactory.LabelAt(_cine, "[E / X]  UEBERSPRINGEN", 24, Palette.White, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40f, 40f), new Vector2(400f, 36f), TextAnchor.MiddleRight);
            _cine.gameObject.SetActive(false);
        }

        static Image Bar(RectTransform parent, bool top)
        {
            var rt = UIFactory.Rect(parent, top ? "BarTop" : "BarBottom", new Vector2(0f, top ? 1f : 0f), new Vector2(1f, top ? 1f : 0f), new Vector2(0.5f, top ? 1f : 0f), Vector2.zero, new Vector2(0f, 110f));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = Palette.Ink;
            img.raycastTarget = false;
            return img;
        }

        void ShowCinematic(bool on, Vector3 start)
        {
            if (_cine == null) return;
            if (on)
            {
                _cardTitle.text = Current.title;
                _cardRule.text = Current.rule;
                string pts = Current.kind == Kind.Race ? "" : "   PUNKTE " + UIFactory.Money((long)PointsNeeded);
                _cardInfo.text = "ZEIT " + TimeLimit.ToString("0") + " s   ZIEL " + Mathf.RoundToInt(Vector3.Distance(start, _goal)) + " m" + pts + "   " + UIFactory.Money(Reward);
            }
            if (_slide != null) StopCoroutine(_slide);
            _cine.gameObject.SetActive(true);
            _slide = StartCoroutine(SlideBars(on));
        }

        IEnumerator SlideBars(bool on)
        {
            float t = 0f;
            var card = _cardTitle.transform.parent.parent;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / 0.45f;
                float k = Smooth(on ? t : 1f - t);
                _barTop.rectTransform.anchoredPosition = new Vector2(0f, (1f - k) * 110f);
                _barBottom.rectTransform.anchoredPosition = new Vector2(0f, -(1f - k) * 110f);
                ((RectTransform)card).anchoredPosition = new Vector2(Mathf.Lerp(-800f, 60f, k), 150f);
                yield return null;
            }
            if (!on) _cine.gameObject.SetActive(false);
        }
    }
}
