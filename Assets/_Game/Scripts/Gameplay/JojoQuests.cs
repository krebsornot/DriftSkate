using System;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Jojo (der mit Joint und Muetze) vergibt kleine Auftraege. Ansprechen mit E / X (oder F / Y), Dialog in Manga-Sprechblasen,
    /// annehmen mit E, ablehnen mit B. Fortschritt steht links im Bild, eine Markierung zeigt das Ziel.
    /// Erledigt man alle, geht es von vorn los, mit hoeheren Zielen und mehr Geld. Nur lokal, ohne Netzwerk.
    /// </summary>
    public class JojoQuests : QuestNpc
    {
        public static JojoQuests Instance { get; private set; }

        public enum Kind { Combo, Fetch, Trick, Speed, Grind }
        public enum Phase { Open, Active, Done }

        class Quest
        {
            public string title;
            public Kind kind;
            public float target;
            public long reward;
            public string trick;
            public float time;
            public string[] intro, done;
            public string nag;
        }

        /// <summary>{T} = Ziel, {R} = Belohnung (beides mit der Runde hochgerechnet).</summary>
        static readonly Quest[] Quests =
        {
            new Quest
            {
                title = "ZEIG MAL WAS", kind = Kind.Combo, target = 3000, reward = 1500,
                intro = new[]
                {
                    "Yo, Dude... du bist doch der mit dem Board, oder?",
                    "Kalle meint, du kannst gar nix. Ich mein... ich glaub an dich, Mann.",
                    "Hau mal 'ne fette Combo raus. So {T} fett. Dann hat Kalle endlich Ruhe. Gibt auch {R}."
                },
                nag = "Combo, Dude. {T}. Das Universum wartet.",
                done = new[] { "DUUUDE! Hast du das gesehen, Kalle?!", "Hier, {R} fuer dich. Nicht alles fuer Chips ausgeben, Mann." }
            },
            new Quest
            {
                title = "FRESSFLASH", kind = Kind.Fetch, time = 150f, reward = 2000,
                intro = new[]
                {
                    "Bro. Notfall.",
                    "Kalle hat seit zehn Minuten nix gegessen. Er wird schon ganz... still.",
                    "Hol uns Snacks vom Spaeti, ich markier dir das. Aber beeil dich, Mann! {R} sind drin."
                },
                nag = "Snacks, Dude! Kalle guckt schon so komisch das Sofa an.",
                done = new[] { "CHIPS! Du bist ein Held, Mann.", "Kalle weint grad 'n bisschen. Vor Freude. Glaub ich. Hier, {R}." }
            },
            new Quest
            {
                title = "FLIP IT", kind = Kind.Trick, trick = "KICKFLIP", target = 3, reward = 2000,
                intro = new[]
                {
                    "Ey... ich hatte grad 'ne Vision.",
                    "{T} Kickflips. Wann und wo, egal. Das Universum will das einfach sehen, Mann."
                },
                nag = "Kickflips, Bro. Die Vision war echt deutlich.",
                done = new[] { "Das Universum ist zufrieden. Ich auch.", "Nimm die {R}. Karma und so." }
            },
            new Quest
            {
                title = "WIND IN DEN DREADS", kind = Kind.Speed, target = 70, reward = 2500,
                intro = new[]
                {
                    "Weisst du, was ich vermisse? Wind, Mann. Richtig Wind im Gesicht.",
                    "Bring das Board auf {T}. Und dann erzaehl mir, wie's war. {R} fuer die Story."
                },
                nag = "Schneller, Dude! Ich will's bis hier spueren.",
                done = new[] { "Ich hab's gespuert, Dude. Echt jetzt. Bis hierher.", "{R}, wie versprochen. Fahr vorsichtig. Oder halt nicht." }
            },
            new Quest
            {
                title = "RAIL-MEDITATION", kind = Kind.Grind, target = 8, reward = 3000,
                intro = new[]
                {
                    "Grinden ist wie Meditieren, Mann. Nur mit mehr Funken.",
                    "Grind insgesamt {T}. Atmen nicht vergessen. {R} fuer deine innere Mitte."
                },
                nag = "Rails, Bro. Eins werden mit dem Metall.",
                done = new[] { "Namaste, Bro.", "Hier, {R}. Du strahlst jetzt richtig, weisst du das?" }
            },
        };

        /// <summary>Wo der Spaeti ist (wechselt mit jeder Runde): Kreuzungen in der Stadt.</summary>
        static Vector3 ShopPosition(int round)
        {
            switch (round % 3)
            {
                case 0: return new Vector3(CityBuilder.RoadCenter(3), 0f, CityBuilder.RoadCenter(4));
                case 1: return new Vector3(CityBuilder.RoadCenter(4), 0f, CityBuilder.RoadCenter(1));
                default: return new Vector3(CityBuilder.RoadCenter(0), 0f, CityBuilder.RoadCenter(5));
            }
        }

        public static readonly Color BeanieColor = Palette.Hex("E8AE45");

        public override string Speaker => "JOJO";
        protected override Color TagColor => BeanieColor;
        protected override string TrackerTag => "JOJOS AUFTRAG";

        public Phase State { get; private set; }
        public float Progress { get; private set; }

        float _timeLeft;
        Vector3 _shop;
        GameObject _beacon;
        ComboSystem _combo;

        int Index => SaveSystem.Profile.jojoQuest;
        Quest Current => Quests[Index % Quests.Length];
        int Round => Index / Quests.Length;
        float Scale => 1f + 0.5f * Round;

        float Target
        {
            get
            {
                var q = Current;
                switch (q.kind)
                {
                    case Kind.Speed: return Mathf.Min(q.target + 6f * Round, 88f);
                    case Kind.Trick: return q.target + Round;
                    case Kind.Combo: return Mathf.Round(q.target * Scale / 500f) * 500f;
                    default: return Mathf.Round(q.target * Scale);
                }
            }
        }

        long Reward => (long)(Mathf.Round(Current.reward * Scale / 100f) * 100f);

        string TargetText
        {
            get
            {
                switch (Current.kind)
                {
                    case Kind.Combo: return UIFactory.Money((long)Target);
                    case Kind.Speed: return Target.ToString("0") + " Sachen";
                    case Kind.Grind: return Target.ToString("0") + " Sekunden";
                    default: return Target.ToString("0");
                }
            }
        }

        string Fill(string line) => line.Replace("{T}", TargetText).Replace("{R}", UIFactory.Money(Reward));

        // ------------------------------------------------------------------ Ansprechen

        public override void Talk()
        {
            if (Talking) return;
            switch (State)
            {
                case Phase.Open:
                    OpenDialog(Array.ConvertAll(Current.intro, Fill), "[E] KLAR, MACH ICH", "[B] GRAD NICHT", accepted =>
                    {
                        if (accepted) Accept();
                        else Bark("Kein Stress, Dude. Ich chill hier eh.");
                    });
                    break;
                case Phase.Active:
                    OpenDialog(new[] { Fill(Current.nag) + "\n(" + Objective() + ")" }, "[E] BIN DRAN", "[B] AUFGEBEN", keepGoing =>
                    {
                        if (keepGoing) return;
                        Abort();
                        Bark("Auch okay, Mann. Vielleicht spaeter.");
                    });
                    break;
                case Phase.Done:
                    OpenDialog(Array.ConvertAll(Current.done, Fill), null, null, _ => TurnIn());
                    break;
            }
        }

        void Accept()
        {
            State = Phase.Active;
            Progress = 0f;
            _timeLeft = Current.time;
            if (Current.kind == Kind.Fetch)
            {
                _shop = Ground(ShopPosition(Round));
                ClearBeacon();
                _beacon = BuildBeacon("SnackBeacon", _shop, new Color(1f, 0.6f, 0.2f), true);
            }
            Bark(Current.kind == Kind.Fetch ? "Los, los, LOS!" : "Nice, Mann. Ich warte hier.");
            HUD.Instance?.Toast("AUFTRAG: " + Current.title, 2.5f);
        }

        void Abort()
        {
            State = Phase.Open;
            ClearBeacon();
        }

        void Complete()
        {
            State = Phase.Done;
            ClearBeacon();
            BurstPop.Show("GESCHAFFT!", Palette.Yellow, new Vector2(0f, 260f), 230f, 1.8f);
            HUD.Instance?.Toast("AUFTRAG ERFUELLT!  Zurueck zu Jojo", 3.5f);
        }

        void Fail(string why)
        {
            State = Phase.Open;
            ClearBeacon();
            BurstPop.Show("ZU SPAET!", Palette.Red, new Vector2(0f, 260f), 230f, 1.8f);
            HUD.Instance?.Toast(why + "  Sprich Jojo nochmal an.", 4f);
        }

        void TurnIn()
        {
            long reward = Reward;
            SaveSystem.Profile.jojoQuest++;
            SaveSystem.AddMoney(reward); // speichert auch den Fortschritt
            State = Phase.Open;
            Progress = 0f;
            BurstPop.Show("+" + UIFactory.Money(reward), Palette.Lime, new Vector2(0f, 240f), 260f, 2.2f);
            SpraySound.Play(0.8f, false);
            Npc?.Play("Laugh");
        }

        void ClearBeacon()
        {
            if (_beacon != null) Destroy(_beacon);
            _beacon = null;
        }

        // ------------------------------------------------------------------ Ablauf

        protected override void Start()
        {
            Instance = this;
            base.Start();
        }

        protected override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_combo != null)
            {
                _combo.Ended -= OnComboEnded;
                _combo.ActionAdded -= OnAction;
            }
            ClearBeacon();
            base.OnDestroy();
        }

        protected override void Update()
        {
            var local = PlayerAvatar.Local;
            var combo = local != null ? local.combo : null;
            if (combo != _combo)
            {
                if (_combo != null) { _combo.Ended -= OnComboEnded; _combo.ActionAdded -= OnAction; }
                _combo = combo;
                if (_combo != null) { _combo.Ended += OnComboEnded; _combo.ActionAdded += OnAction; }
            }
            if (local != null && State != Phase.Open) Track(local, Time.deltaTime);
            base.Update();
        }

        void Track(PlayerAvatar p, float dt)
        {
            var q = Current;
            if (q.kind == Kind.Fetch)
            {
                _timeLeft -= dt;
                if (_timeLeft <= 0f) { Fail("Kalle ist vor Hunger eingeschlafen."); return; }
            }
            if (State != Phase.Active) return;
            var sk = p.skater;
            bool onBoard = p.Mode == PlayerMode.Skating && (sk.State == SkaterState.Riding || sk.State == SkaterState.Air || sk.State == SkaterState.Grinding || sk.State == SkaterState.WallPlant || sk.State == SkaterState.WallRide);
            switch (q.kind)
            {
                case Kind.Speed:
                    if (onBoard) Progress = Mathf.Max(Progress, sk.Speed * 3.6f);
                    if (Progress >= Target) Complete();
                    break;
                case Kind.Grind:
                    if (p.Mode == PlayerMode.Skating && sk.State == SkaterState.Grinding) Progress += dt;
                    if (Progress >= Target) Complete();
                    break;
                case Kind.Fetch:
                    if ((PlayerPos(p) - _shop).sqrMagnitude < 6f * 6f)
                    {
                        HUD.Instance?.Toast("SNACKS GESCHNAPPT!  Ab zu Jojo", 3f);
                        SpraySound.Play(0.5f, false);
                        State = Phase.Done;
                        ClearBeacon();
                    }
                    break;
            }
        }

        void OnComboEnded(long amount, bool failed, string reason)
        {
            if (State != Phase.Active || Current.kind != Kind.Combo) return;
            Progress = Mathf.Max(Progress, amount);
            if (amount >= Target) Complete();
        }

        void OnAction(string label, int points)
        {
            if (State != Phase.Active || Current.kind != Kind.Trick || label == null || !label.Contains(Current.trick)) return;
            Progress++;
            if (Progress >= Target) Complete();
        }

        string Objective()
        {
            var q = Current;
            if (State == Phase.Done) return q.kind == Kind.Fetch ? "Snacks zu Jojo bringen" : "Zurueck zu Jojo!";
            switch (q.kind)
            {
                case Kind.Combo: return "Combo ueber " + UIFactory.Money((long)Target) + (Progress > 0 ? "  (beste " + UIFactory.Money((long)Progress) + ")" : "");
                case Kind.Fetch: return "Snacks vom Spaeti holen";
                case Kind.Trick: return "Kickflips  " + Progress.ToString("0") + " / " + Target.ToString("0");
                case Kind.Speed: return "Board auf " + Target.ToString("0") + " km/h  (bisher " + Progress.ToString("0") + ")";
                case Kind.Grind: return "Grinden  " + Progress.ToString("0.0") + " / " + Target.ToString("0") + " s";
            }
            return "";
        }

        // ------------------------------------------------------------------ Anzeige

        protected override bool TrackerVisible => State != Phase.Open;
        protected override string TrackerTitle => Current.title;

        protected override string TrackerText
        {
            get
            {
                string timer = Current.kind == Kind.Fetch ? "   " + Mathf.FloorToInt(Mathf.Max(0f, _timeLeft) / 60f) + ":" + (Mathf.CeilToInt(Mathf.Max(0f, _timeLeft)) % 60).ToString("00") : "";
                return Objective() + timer;
            }
        }

        protected override Color TrackerTextColor =>
            Current.kind == Kind.Fetch && _timeLeft < 20f && Mathf.Repeat(Time.time, 0.8f) < 0.4f ? Palette.Red : State == Phase.Done ? Palette.Lime : Palette.White;

        protected override Vector3? MarkerGoal
        {
            get
            {
                var p = PlayerAvatar.Local;
                if (State == Phase.Done)
                {
                    Vector3 home = transform.position + Vector3.up * 2.2f;
                    return p != null && (PlayerPos(p) - home).sqrMagnitude < 8f * 8f ? (Vector3?)null : home;
                }
                if (State == Phase.Active && Current.kind == Kind.Fetch) return _shop + Vector3.up * 2.5f;
                return null;
            }
        }

        protected override string IndicatorText => State == Phase.Done ? "$" : State == Phase.Open ? "!" : null;
        protected override Color IndicatorColor => State == Phase.Done ? Palette.Lime : Palette.Yellow;
    }
}
