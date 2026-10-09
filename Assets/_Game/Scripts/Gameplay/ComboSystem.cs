using System;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Eine gemeinsame Combo fuer Drifts und Skate-Tricks. Jede neue Aktion erhoeht den Multiplikator.
    /// Ohne Aktion laeuft ein Zeitfenster ab; dann wird ein Hundertstel der Punkte als Geld gutgeschrieben (MoneyPerPoint).
    /// Sturz, Dreher oder Wandkontakt beenden sie mit halben Punkten.
    /// </summary>
    public class ComboSystem : MonoBehaviour
    {
        public const float Window = 2.2f;
        public const int MaxMultiplier = 20;
        /// <summary>
        /// Geld pro Combo-Punkt. Die Punkte (Anzeige, Bestwert, Quest-Ziele) bleiben, ausgezahlt wird ein Hundertstel:
        /// eine richtig gute Combo (800.000 Punkte) bringt 8.000 $, das guenstigste Garagen-Auto kostet 120.000 $.
        /// </summary>
        public const float MoneyPerPoint = 0.01f;
        public static long ToMoney(long points) => (long)Math.Round(points * (double)MoneyPerPoint);

        public bool Active { get; private set; }
        public float Points { get; private set; }
        public int Multiplier { get; private set; } = 1;
        public float WindowLeft { get; private set; }
        public long Total => (long)(Points * Multiplier);

        /// <summary>Label, Punkte der Aktion.</summary>
        public event Action<string, int> ActionAdded;
        /// <summary>Gutgeschriebener Betrag, gescheitert?, Grund.</summary>
        public event Action<long, bool, string> Ended;

        bool _heldThisFrame;

        public void AddAction(string label, float basePoints)
        {
            if (!Active)
            {
                Active = true;
                Points = 0f;
                Multiplier = 1;
            }
            else
            {
                Multiplier = Mathf.Min(MaxMultiplier, Multiplier + 1);
            }
            Points += basePoints;
            WindowLeft = Window;
            ActionAdded?.Invoke(label, Mathf.RoundToInt(basePoints));
        }

        public void AddPoints(float points)
        {
            if (!Active) return;
            Points += points;
            WindowLeft = Window;
        }

        /// <summary>Haelt die Combo am Leben, solange eine Aktion laeuft (Drift, Grind, Manual, Sprung).</summary>
        public void Hold()
        {
            if (!Active) return;
            WindowLeft = Window;
            _heldThisFrame = true;
        }

        public void Fail(string reason)
        {
            if (!Active) return;
            if (Admin.NoComboFail && reason != "RESET") return;
            long banked = Total / 2;
            Finish(banked, true, reason);
        }

        public void BankNow()
        {
            if (Active) Finish(Total, false, null);
        }

        void Finish(long amount, bool failed, string reason)
        {
            Active = false;
            long money = ToMoney(amount);
            if (money > 0) SaveSystem.AddMoney(money);
            if (!failed && amount > SaveSystem.Profile.bestCombo)
            {
                SaveSystem.Profile.bestCombo = amount;
                SaveSystem.Save();
            }
            Ended?.Invoke(amount, failed, reason);
            Points = 0f;
            Multiplier = 1;
        }

        void Update()
        {
            if (!Active) return;
            if (!_heldThisFrame)
            {
                WindowLeft -= Time.deltaTime * Chill.ComboDrain; // gechillt laeuft das Zeitfenster langsamer ab
                if (WindowLeft <= 0f) Finish(Total, false, null);
            }
            _heldThisFrame = false;
        }
    }
}
