using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Bodo und Udo (Brueder) verkaufen Hotdogs an den Imbisswagen in den Parks. Ansprechen mit E / X, kaufen mit E,
    /// ablehnen mit B. Ein Hotdog kommt in den Rucksack (Spielstand), essen mit H / linken Stick druecken gibt die
    /// Hotdog-Power (HotdogPower.cs). Beim Verkauf reicht er ihn mit der linken Hand ueber die Theke. Nur lokal.
    /// </summary>
    public class HotdogVendor : QuestNpc
    {
        public static readonly Color NameColor = HotdogPower.MustardColor;
        public const long Price = 250;

        public override string Speaker => Npc != null ? Npc.Id.ToUpperInvariant() : "IMBISS";
        protected override Color TagColor => NameColor;
        protected override string TrackerTag => "IMBISS";

        bool IsBodo => Npc == null || Npc.Id == "Bodo";
        string Brother => IsBodo ? "Udo" : "Bodo";

        static readonly string[] BodoOpeners =
        {
            "Moin! Heiss und fettig, wie immer?",
            "Na, Hunger? Die Wurst ist grad perfekt.",
            "Ah, mein Lieblingskunde. Sag nichts. Einer?",
            "Frisch vom Grill. Riech mal.",
            "Fuenf Dollar? Das Schild ist von 1990, Kumpel.",
        };

        static readonly string[] UdoOpeners =
        {
            "Hallo. Wuerstchen ist gerade auf Temperatur.",
            "Na? Einen mit Senf? Hab nur Senf.",
            "Bei mir ist der Senf besser. Sag's nicht Bodo.",
            "Du schon wieder. Gute Wahl.",
            "Ja, auf dem Schild steht fuenf. Das Schild luegt.",
        };

        static readonly string[] NoThanks = { "Kein Ding. Der Grill laeuft noch.", "Dann eben spaeter.", "Mehr fuer mich." };

        Transform _hotdog;
        float _serveTime = -1f;

        // ------------------------------------------------------------------ Ansprechen

        public override void Talk()
        {
            if (Talking) return;
            var p = SaveSystem.Profile;
            if (p.hotdogs >= HotdogPower.MaxHotdogs)
            {
                OpenDialog(new[] { "Du hast doch noch " + p.hotdogs + " Hotdogs.\nErstmal essen! [H]" }, null, null, _ => { });
                return;
            }
            string offer = "Ein Hotdog: " + UIFactory.Money(Price) + ".\nGibt ordentlich Power, auf dem Brett und im Auto.";
            string[] pages;
            if (p.hotdogsBought == 0)
                pages = IsBodo
                    ? new[] { "Moin! Ich bin Bodo. Beste Wurst der Stadt.", "Mein Bruder Udo hat auch so 'nen Wagen, im anderen Park.\nSeine sind... naja.", offer }
                    : new[] { "Hallo. Udo. Ich mach hier die Hotdogs.", "Bodo im anderen Park ist mein Bruder.\nFrag ihn nicht nach seinem Senf.", offer };
            else
            {
                var openers = IsBodo ? BodoOpeners : UdoOpeners;
                pages = new[] { openers[Random.Range(0, openers.Length)] + "\n" + offer };
            }
            OpenDialog(pages, "[E] HOTDOG KAUFEN  " + UIFactory.Money(Price), "[B] NEE, DANKE", yes =>
            {
                if (yes) Buy();
                else Bark(NoThanks[Random.Range(0, NoThanks.Length)]);
            });
        }

        /// <summary>Kaufen (auch fuer den Autotest).</summary>
        public bool Buy()
        {
            var p = SaveSystem.Profile;
            if (p.hotdogs >= HotdogPower.MaxHotdogs) return false;
            if (!SaveSystem.TrySpend(Price))
            {
                Bark("Kein Geld, keine Wurst.\nFahr erstmal 'ne Combo.");
                return false;
            }
            p.hotdogs++;
            p.hotdogsBought++;
            SaveSystem.Save();
            Npc?.Play("Serve");
            _serveTime = 0f;
            Bark(IsBodo ? "Bitte schoen! Senf ist schon drauf." : "Hier. Gruss an " + Brother + ", falls du ihn siehst.");
            BurstPop.Show("+1 HOTDOG", NameColor, new Vector2(0f, 260f), 230f, 1.8f);
            HUD.Instance?.Toast("HOTDOG GEKAUFT  [H] / linken Stick druecken: essen", 4f);
            return true;
        }

        // ------------------------------------------------------------------ Ablauf

        protected override void Start()
        {
            base.Start();
            _hotdog = HotdogPower.BuildHotdog("ServedHotdog").transform;
            _hotdog.gameObject.SetActive(false);
        }

        protected override void OnDestroy()
        {
            if (_hotdog != null) Destroy(_hotdog.gameObject);
            base.OnDestroy();
        }

        protected override void Update()
        {
            if (_serveTime >= 0f)
            {
                _serveTime += Time.deltaTime;
                if (_serveTime > 4f) _serveTime = -1f;
            }
            base.Update();
        }

        /// <summary>Hotdog quer auf der linken Handflaeche, solange die Hand ueber der Theke ist (nach der Animation).</summary>
        void LateUpdate()
        {
            bool show = _serveTime > 0.7f && _serveTime < 2.7f && Npc != null && Npc.CurrentAction == "Serve";
            if (_hotdog.gameObject.activeSelf != show) _hotdog.gameObject.SetActive(show);
            if (!show) return;
            var anim = Npc.Animator;
            var hand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            var mid = anim.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            var index = anim.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            var pinky = anim.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
            if (hand == null || mid == null || index == null || pinky == null) return;
            Vector3 fingers = (mid.position - hand.position).normalized;
            Vector3 across = (index.position - pinky.position).normalized;
            Vector3 palm = Vector3.Cross(fingers, across).normalized;
            if (Vector3.Dot(palm, Vector3.up) < 0f) palm = -palm; // beim Ueberreichen zeigt die Handflaeche nach oben
            _hotdog.SetPositionAndRotation(Vector3.Lerp(hand.position, mid.position, 0.9f) + palm * 0.035f, Quaternion.LookRotation(across, palm));
        }

        // ------------------------------------------------------------------ Anzeige

        protected override string IndicatorText => SaveSystem.Profile.hotdogs < HotdogPower.MaxHotdogs ? "$" : null;
        protected override Color IndicatorColor => NameColor;
    }
}
