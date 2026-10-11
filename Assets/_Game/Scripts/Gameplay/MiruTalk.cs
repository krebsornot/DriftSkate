using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Miru auf der Parkbank: spricht man sie an (E / X), steht sie auf und redet. Danach bleibt sie stehen,
    /// bis man weggefahren ist, und setzt sich wieder; oder man sagt ihr, sie soll sitzen bleiben.
    /// Ab dem zweiten Gespraech hat sie einen Auftrag: den Patrouillen-Bot (PatrolBot) ueber dem Park dreimal mit dem Board
    /// anspringen, bis er abstuerzt. Einmalig, Fortschritt im Spielstand (miruQuest). Nur lokal.
    /// </summary>
    public class MiruTalk : QuestNpc
    {
        public static MiruTalk Instance { get; private set; }
        public static readonly Color NameColor = Palette.Hex("E0384B");

        /// <summary>So weit weg muss der Spieler sein, damit sie sich wieder hinsetzt (und so lange).</summary>
        const float LeaveDistance = 9f, LeaveTime = 2.5f;

        Vector3 _seat, _stand;
        bool _waitingToTalk;
        float _awayTime;
        int _talks;

        public enum Phase { Open, Active, Done, Finished }
        public Phase State { get; private set; }
        public PatrolBot Bot { get; private set; }
        const long Reward = 4000;
        /// <summary>Patrouillen-Runde des Bots: um die Mitte des Parks.</summary>
        const float PatrolRadius = 16f;

        static readonly string[] QuestIntro =
        {
            "Siehst du das Ding, das hier immer rumschwirrt? ENFORCER 01.\nSo 'n Patrouillen-Bot von der Stadt.",
            "Der filmt jeden, der laenger als fuenf Minuten auf 'ner Bank sitzt.\nAlso mich. Den ganzen Tag.",
            "Hol ihn runter. Spring ihn mit dem Board an, dreimal muesste reichen.\nIch geb dir " + UIFactory.Money(Reward) + ". Hab ich gespart."
        };

        static readonly string[][] Chats =
        {
            new[] { "Hm? Ach, du bist der mit dem Board, der hier dauernd vorbeirollt.", "Ich bin Miru. Ich sitz hier meistens und guck mir die Stadt an." },
            new[] { "Schon wieder du.", "Die Bank hier ist die beste im Park. Man sieht alles, und keiner sieht einen." },
            new[] { "Weisst du, was ich an der Stadt mag? Abends wird alles rosa.", "Dann sieht sogar das Parkhaus irgendwie schoen aus." },
            new[] { "Jojo und Kalle da drueben haben mir mal Chips angeboten.", "Ich hab nein gesagt. Kalle redet seitdem nicht mehr mit mir." },
            new[] { "Wenn du dich auf die Fresse legst, guck ich uebrigens nicht weg.", "Nur damit du's weisst." },
        };

        public override string Speaker => "MIRU";
        protected override Color TagColor => NameColor;
        protected override string TrackerTag => "MIRU";
        public bool Standing => Npc != null && !Npc.Sitting;
        protected override bool CanTalk => !_waitingToTalk;

        public void Setup(Vector3 seat, Vector3 stand)
        {
            _seat = seat;
            _stand = stand;
        }

        protected override void Start()
        {
            base.Start();
            Instance = this;
            if (SaveSystem.Profile.miruQuest > 0) State = Phase.Finished;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (Instance == this) Instance = null;
            if (Bot != null) Destroy(Bot.gameObject);
        }

        public override void Talk()
        {
            if (Talking || _waitingToTalk || Npc == null) return;
            _awayTime = 0f;
            if (Npc.SitAmount < 0.05f) OpenChat();
            else
            {
                // Erst aufstehen, dann reden
                Npc.SetSitting(false);
                _waitingToTalk = true;
            }
        }

        void OpenChat()
        {
            // Der Auftrag kommt ab dem zweiten Gespraech (beim ersten stellt sie sich vor)
            if (State == Phase.Done) { TurnInDialog(); return; }
            if (State == Phase.Active)
            {
                OpenDialog(new[] { "Der Bot ist noch oben. Hoer doch, wie der summt.\nSpring ihn an. Mit dem Board. (" + Objective() + ")" },
                           "[E] BIN DRAN", "[B] LASS MAL", keepGoing =>
                {
                    if (keepGoing) { Bark("Ich guck zu."); return; }
                    Abort();
                    Bark("Schade. Dann filmt er halt weiter.");
                });
                return;
            }
            if (State == Phase.Open && _talks >= 1)
            {
                _talks++;
                OpenDialog(QuestIntro, "[E] MACH ICH", "[B] LIEBER NICHT", accepted =>
                {
                    if (accepted) Accept();
                    else Bark("Hm. Dacht ich mir.");
                });
                return;
            }
            // Erst der Reihe nach, danach zufaellig (ohne die Vorstellung)
            var pages = Chats[_talks < Chats.Length ? _talks : Random.Range(1, Chats.Length)];
            _talks++;
            OpenDialog(pages, "[E] BIS DANN", "[B] SETZ DICH RUHIG", stay =>
            {
                if (stay) Bark("Mhm. Fahr nicht gegen 'ne Laterne.");
                else
                {
                    Bark("Danke. Die Bank ist eh gemuetlicher.");
                    Npc.SetSitting(true);
                }
            });
        }

        // ------------------------------------------------------------------ Auftrag

        /// <summary>Annehmen (auch fuer Tests): der Bot taucht ueber dem Park auf.</summary>
        public void Accept()
        {
            State = Phase.Active;
            if (Bot != null) Destroy(Bot.gameObject);
            Vector3 park = new Vector3(CityBuilder.BlockCenter(3), 0f, CityBuilder.BlockCenter(2));
            Bot = PatrolBot.Spawn(park, PatrolRadius);
            if (Bot != null)
            {
                Bot.Hit += hits => { if (hits < PatrolBot.HitsNeeded) HUD.Instance?.Toast("Treffer! Noch " + (PatrolBot.HitsNeeded - hits) + " Mal", 2f); };
                Bot.Downed += () => Complete();
            }
            Bark("Da kommt er. Siehst du?");
            HUD.Instance?.Toast("AUFTRAG: BOT RUNTERHOLEN", 2.5f);
        }

        void Abort()
        {
            State = Phase.Open;
            if (Bot != null) Destroy(Bot.gameObject);
            Bot = null;
        }

        void Complete()
        {
            State = Phase.Done;
            BurstPop.Show("ERLEDIGT!", NameColor, new Vector2(0f, 200f), 230f, 1.8f);
            HUD.Instance?.Toast("ENFORCER 01 IST UNTEN!  Zurueck zu Miru", 3.5f);
        }

        void TurnInDialog()
        {
            OpenDialog(new[] { "Hab's gesehen. Der liegt da wie 'ne tote Taube.", "Hier, " + UIFactory.Money(Reward) + ". Und endlich hab ich meine Ruhe. Danke." },
                       null, null, _ => TurnIn());
        }

        void TurnIn()
        {
            State = Phase.Finished;
            SaveSystem.Profile.miruQuest = 1;
            SaveSystem.AddMoney(Reward); // speichert auch den Fortschritt
            BurstPop.Show("+" + UIFactory.Money(Reward), Palette.Lime, new Vector2(0f, 240f), 260f, 2.2f);
            SpraySound.Play(0.8f, false);
            // Der Schrott bleibt noch kurz liegen
            if (Bot != null) Destroy(Bot.gameObject, 20f);
            Bot = null;
        }

        string Objective() => State == Phase.Done ? "Zurueck zu Miru!"
            : "Bot anspringen  " + (Bot != null ? Bot.Hits : 0) + " / " + PatrolBot.HitsNeeded + (Bot != null && Bot.Alerted ? "   (er hat dich gesehen)" : "");

        /// <summary>Fuer Tests: Auftrag sofort als erledigt markieren.</summary>
        public void TestComplete() => Complete();

        protected override bool TrackerVisible => State == Phase.Active || State == Phase.Done;
        protected override string TrackerTitle => "ENFORCER 01";
        protected override string TrackerText => Objective();
        protected override Color TrackerTextColor => State == Phase.Done ? Palette.Lime : Palette.White;

        protected override Vector3? MarkerGoal
        {
            get
            {
                var p = PlayerAvatar.Local;
                Vector3? goal = State == Phase.Done ? transform.position + Vector3.up * 2f
                              : State == Phase.Active && Bot != null && !Bot.IsDown ? Bot.Center : (Vector3?)null;
                if (goal == null || p == null) return goal;
                return (PlayerPos(p) - goal.Value).sqrMagnitude < 10f * 10f ? (Vector3?)null : goal;
            }
        }

        protected override string IndicatorText => State == Phase.Done ? "$" : State == Phase.Open && _talks >= 1 ? "!" : null;
        protected override Color IndicatorColor => State == Phase.Done ? Palette.Lime : NameColor;

        protected override void Update()
        {
            base.Update();
            if (Npc == null) return;
            // Beim Aufstehen und Hinsetzen zwischen Bank und Platz davor gleiten
            transform.position = Vector3.Lerp(_stand, _seat, Npc.SitAmount);

            if (_waitingToTalk && Npc.SitAmount < 0.05f)
            {
                _waitingToTalk = false;
                OpenChat();
            }

            // Steht sie noch und der Spieler ist weg, setzt sie sich wieder
            if (!Npc.Sitting && !Talking && !_waitingToTalk)
            {
                var p = PlayerAvatar.Local;
                bool away = p == null || (PlayerPos(p) - _stand).sqrMagnitude > LeaveDistance * LeaveDistance;
                _awayTime = away ? _awayTime + Time.deltaTime : 0f;
                if (_awayTime > LeaveTime) Npc.SetSitting(true);
            }
        }
    }
}
