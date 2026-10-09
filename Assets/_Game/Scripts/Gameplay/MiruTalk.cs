using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Miru auf der Parkbank: spricht man sie an (E / X), steht sie auf und redet. Danach bleibt sie stehen,
    /// bis man weggefahren ist, und setzt sich wieder; oder man sagt ihr, sie soll sitzen bleiben.
    /// Keine Auftraege, nur Gespraech.
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
        protected override bool CanTalk => !_waitingToTalk;

        public bool Standing => Npc != null && !Npc.Sitting;

        public void Setup(Vector3 seat, Vector3 stand)
        {
            _seat = seat;
            _stand = stand;
        }

        protected override void Start()
        {
            base.Start();
            Instance = this;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (Instance == this) Instance = null;
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
