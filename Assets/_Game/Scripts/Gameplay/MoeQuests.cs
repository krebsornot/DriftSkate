using System;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Moe sitzt hinten in der Seitengasse auf einem Bierkasten und vergibt Jobs: Pakete ausliefern (auf Zeit, mit Auto oder Board),
    /// Tags spruehen, gechillt eine Combo fahren. Belohnung: Geld, manchmal eine Tuete von Nix. Wer den ersten Job geschafft hat,
    /// kriegt bei Nix Freundschaftspreis. Nach allen Jobs geht es von vorn los, mit hoeheren Zielen und mehr Geld. Nur lokal.
    /// </summary>
    public class MoeQuests : QuestNpc
    {
        public static MoeQuests Instance { get; private set; }
        public static readonly Color NameColor = Palette.Hex("F07A3A");

        public enum Kind { Deliver, Tag, ChillCombo }
        public enum Phase { Open, Active, Done }

        class Job
        {
            public string title;
            public Kind kind;
            public float target, time;
            public long reward;
            public int baggies;
            public Func<Vector3> drop;
            public string dropName;
            public string[] intro, done;
            public string nag, arrive;
        }

        /// <summary>{T} = Ziel, {R} = Belohnung, {Z} = Zeit (alles mit der Runde hochgerechnet).</summary>
        static readonly Job[] Jobs =
        {
            new Job
            {
                title = "ERSTE LIEFERUNG", kind = Kind.Deliver, time = 120f, reward = 2000, baggies = 1,
                drop = () => new Vector3(CityBuilder.Half - 14f, 0f, 10f), dropName = "Hafen Ost",
                intro = new[]
                {
                    "Yo. Du bist neu hier, oder? Ich bin Moe.\nDie Gasse hier ist meine.",
                    "Ich hab 'n Paket, das muss zum Hafen. Ostseite.\nFragen stellst du keine.",
                    "Du hast {Z}. Auto, Board, egal.\n{R} und 'ne Tuete von Nix obendrauf."
                },
                nag = "Hafen Ost, Mann. Die Uhr laeuft.",
                arrive = "PAKET ABGELIEFERT!  Zurueck zu Moe",
                done = new[] { "Puenktlich und ohne Fragen. Gefaellt mir.", "{R}. Und Nix macht dir ab jetzt Freundschaftspreis." }
            },
            new Job
            {
                title = "GRUESSE AN JOJO", kind = Kind.Deliver, time = 90f, reward = 2500,
                drop = () => StonerNpc.HangoutPosition + StonerNpc.HangoutFacing * 3f, dropName = "Jojo",
                intro = new[]
                {
                    "Kennst du Jojo? Langer Typ, Dreads,\nhaengt mit Kalle auf dem Sofa ab.",
                    "Der wartet auf Nachschub. Bring ihm das.\n{Z}, sonst wird er nervoes. {R}."
                },
                nag = "Jojo wartet, Bro. Und Jojo wartet nicht gern.\nAlso... eigentlich schon. Aber trotzdem.",
                arrive = "JOJO HAT SEIN PAKET!  Zurueck zu Moe",
                done = new[] { "Jojo hat angerufen. Er sagt, du bist\n\"ein Engel, Mann\". Was auch immer.", "{R}. Weiter so." }
            },
            new Job
            {
                title = "MARKIER DAS REVIER", kind = Kind.Tag, target = 3, reward = 3000, baggies = 1,
                intro = new[]
                {
                    "Die Jungs vom Hafen denken,\ndas hier ist ihr Viertel.",
                    "Spruh {T} Tags, egal wo in der Stadt.\nDamit jeder weiss, wer hier wohnt. {R} plus 'ne Tuete."
                },
                nag = "Noch nicht genug Farbe an den Waenden, Mann.",
                done = new[] { "Hab's gesehen. Sieht gut aus. Richtig gut.", "{R}. Und die Tuete hast du dir verdient." }
            },
            new Job
            {
                title = "EXPRESS", kind = Kind.Deliver, time = 75f, reward = 3500,
                drop = () => new Vector3(CityBuilder.BlockCenter(1), 0f, CityBuilder.BlockCenter(3) - 12f), dropName = "Skatepark",
                intro = new[] { "Eilig. Richtig eilig.", "Skatepark Sued. {Z}.\nWenn du's schaffst: {R}." },
                nag = "Skatepark! Los, los!",
                arrive = "EXPRESS GELIEFERT!  Zurueck zu Moe",
                done = new[] { "Wie warst du so schnell da? Egal. Respekt.", "{R}." }
            },
            new Job
            {
                title = "GANZ ENTSPANNT", kind = Kind.ChillCombo, target = 5000, reward = 4000,
                intro = new[]
                {
                    "Weisst du, was die meisten falsch machen?\nDie sind viel zu verkrampft.",
                    "Rauch eine [G], dann fahr mir 'ne Combo\nueber {T}. Ganz locker. {R}."
                },
                nag = "Erst chillen [G], dann Combo. Reihenfolge, Bro.",
                done = new[] { "Siehst du? Locker bleiben, dann klappt das.", "{R}. Du gehoerst jetzt zur Gasse." }
            },
        };

        public override string Speaker => "MOE";
        protected override Color TagColor => NameColor;
        protected override string TrackerTag => "MOES JOB";

        public Phase State { get; private set; }
        public float Progress { get; private set; }
        public float TimeLeft => _timeLeft;
        public Vector3 DropPoint => _drop;

        float _timeLeft;
        Vector3 _drop;
        GameObject _beacon;
        ComboSystem _combo;

        int Index => SaveSystem.Profile.moeQuest;
        Job Current => Jobs[Index % Jobs.Length];
        int Round => Index / Jobs.Length;
        float Scale => 1f + 0.5f * Round;

        float Target => Current.kind == Kind.ChillCombo ? Mathf.Round(Current.target * Scale / 500f) * 500f : Current.target + Round;
        float TimeLimit => Mathf.Max(45f, Current.time - 10f * Round);
        long Reward => (long)(Mathf.Round(Current.reward * Scale / 100f) * 100f);

        string Clock(float t) => Mathf.FloorToInt(Mathf.Max(0f, t) / 60f) + ":" + (Mathf.CeilToInt(Mathf.Max(0f, t)) % 60).ToString("00");

        string Fill(string line) => line.Replace("{T}", Current.kind == Kind.ChillCombo ? UIFactory.Money((long)Target) : Target.ToString("0"))
                                        .Replace("{R}", UIFactory.Money(Reward)).Replace("{Z}", Clock(TimeLimit) + " Minuten");

        // ------------------------------------------------------------------ Ansprechen

        public override void Talk()
        {
            if (Talking) return;
            switch (State)
            {
                case Phase.Open:
                    OpenDialog(Array.ConvertAll(Current.intro, Fill), "[E] BIN DABEI", "[B] SPAETER", accepted =>
                    {
                        if (accepted) Accept();
                        else Bark("Wie du willst. Ich sitz hier eh.");
                    });
                    break;
                case Phase.Active:
                    OpenDialog(new[] { Fill(Current.nag) + "\n(" + Objective() + ")" }, "[E] BIN DRAN", "[B] HINSCHMEISSEN", keepGoing =>
                    {
                        if (keepGoing) return;
                        Abort();
                        Bark("Hm. Dann mach ich's halt selber. Spaeter.");
                    });
                    break;
                case Phase.Done:
                    OpenDialog(Array.ConvertAll(Current.done, Fill), null, null, _ => TurnIn());
                    break;
            }
        }

        /// <summary>Annehmen (auch fuer den Autotest).</summary>
        public void Accept()
        {
            State = Phase.Active;
            Progress = 0f;
            _timeLeft = TimeLimit;
            ClearBeacon();
            if (Current.kind == Kind.Deliver)
            {
                _drop = Ground(Current.drop());
                _beacon = BuildBeacon("DropBeacon", _drop, NameColor, true);
                Bark("Und nicht reingucken.");
            }
            else if (Current.kind == Kind.ChillCombo && SaveSystem.Profile.baggies == 0)
            {
                // ohne Tuete geht's nicht: Moe legt eine aus
                SaveSystem.Profile.baggies++;
                SaveSystem.Save();
                Bark("Hier, eine geht auf mich.");
                BurstPop.Show("+1 TUETE", NixDealer.NameColor, new Vector2(0f, 260f), 230f, 1.8f);
            }
            else Bark("Sauber. Ich seh dich.");
            HUD.Instance?.Toast("JOB: " + Current.title, 2.5f);
        }

        void Abort()
        {
            State = Phase.Open;
            ClearBeacon();
        }

        void Complete(string toast)
        {
            State = Phase.Done;
            ClearBeacon();
            BurstPop.Show("ERLEDIGT!", NameColor, new Vector2(0f, 260f), 230f, 1.8f);
            SpraySound.Play(0.5f, false);
            HUD.Instance?.Toast(toast ?? "JOB ERLEDIGT!  Zurueck zu Moe", 3.5f);
        }

        void Fail(string why)
        {
            State = Phase.Open;
            ClearBeacon();
            BurstPop.Show("ZU SPAET!", Palette.Red, new Vector2(0f, 260f), 230f, 1.8f);
            HUD.Instance?.Toast(why + "  Sprich Moe nochmal an.", 4f);
        }

        void TurnIn()
        {
            long reward = Reward;
            int bags = Current.baggies;
            SaveSystem.Profile.moeQuest++;
            SaveSystem.Profile.baggies = Mathf.Min(NixDealer.MaxBaggies, SaveSystem.Profile.baggies + bags);
            SaveSystem.AddMoney(reward); // speichert auch den Fortschritt
            State = Phase.Open;
            Progress = 0f;
            BurstPop.Show("+" + UIFactory.Money(reward), Palette.Lime, new Vector2(0f, 240f), 260f, 2.2f);
            if (bags > 0) BurstPop.Show("+1 TUETE", NixDealer.NameColor, new Vector2(300f, 170f), 200f, 2.2f);
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
            if (_combo != null) { _combo.Ended -= OnComboEnded; _combo.ActionAdded -= OnAction; }
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
            if (local != null && State == Phase.Active && Current.kind == Kind.Deliver)
            {
                _timeLeft -= Time.deltaTime;
                if (_timeLeft <= 0f) Fail(Current.dropName == "Jojo" ? "Jojo ist eingeschlafen." : "Paket zu spaet. Moe ist sauer.");
                else if ((PlayerPos(local) - _drop).sqrMagnitude < 6f * 6f) Deliver();
            }
            base.Update();
        }

        void Deliver()
        {
            Complete(Current.arrive);
            if (Current.dropName != "Jojo") return;
            var jojo = StonerNpc.All.Find(n => n.Id == "Jojo");
            if (jojo == null || jojo.Head == null) return;
            jojo.Play("Laugh");
            SpeechBubble.Show(jojo.Head, Vector3.up * 0.3f, "JOJO", JojoQuests.BeanieColor, "Ohhh, von Moe? Danke, Dude!", false, 2.8f);
        }

        void OnAction(string label, int points)
        {
            if (State != Phase.Active || Current.kind != Kind.Tag || label == null || !label.Contains("TAG")) return;
            Progress++;
            if (Progress >= Target) Complete(null);
        }

        void OnComboEnded(long amount, bool failed, string reason)
        {
            if (State != Phase.Active || Current.kind != Kind.ChillCombo || !Chill.Active) return;
            Progress = Mathf.Max(Progress, amount);
            if (amount >= Target) Complete(null);
        }

        string Objective()
        {
            if (State == Phase.Done) return "Zurueck zu Moe!";
            switch (Current.kind)
            {
                case Kind.Deliver: return "Paket nach " + Current.dropName + " bringen";
                case Kind.Tag: return "Tags spruehen  " + Progress.ToString("0") + " / " + Target.ToString("0");
                case Kind.ChillCombo:
                    return (Chill.Active ? "Gechillt: Combo ueber " : "Erst chillen [G], dann Combo ueber ") + UIFactory.Money((long)Target)
                           + (Progress > 0 ? "  (beste " + UIFactory.Money((long)Progress) + ")" : "");
            }
            return "";
        }

        /// <summary>Fuer den Autotest: Job sofort als geschafft markieren.</summary>
        public void TestComplete() => Complete(null);

        // ------------------------------------------------------------------ Anzeige

        protected override bool TrackerVisible => State != Phase.Open;
        protected override string TrackerTitle => Current.title;
        protected override string TrackerText => Objective() + (State == Phase.Active && Current.kind == Kind.Deliver ? "   " + Clock(_timeLeft) : "");

        protected override Color TrackerTextColor =>
            State == Phase.Active && Current.kind == Kind.Deliver && _timeLeft < 20f && Mathf.Repeat(Time.time, 0.8f) < 0.4f ? Palette.Red
            : State == Phase.Done ? Palette.Lime : Palette.White;

        protected override Vector3? MarkerGoal
        {
            get
            {
                var p = PlayerAvatar.Local;
                if (State == Phase.Done)
                {
                    Vector3 home = transform.position + Vector3.up * 2f;
                    return p != null && (PlayerPos(p) - home).sqrMagnitude < 8f * 8f ? (Vector3?)null : home;
                }
                if (State == Phase.Active && Current.kind == Kind.Deliver) return _drop + Vector3.up * 2.5f;
                return null;
            }
        }

        protected override string IndicatorText => State == Phase.Done ? "$" : State == Phase.Open ? "!" : null;
        protected override Color IndicatorColor => State == Phase.Done ? Palette.Lime : NameColor;
    }
}
