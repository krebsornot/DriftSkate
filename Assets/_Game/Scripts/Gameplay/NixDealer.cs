using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Nix lehnt in der Seitengasse an der Wand und vertickt Tueten. Ansprechen mit E / X, kaufen mit E, ablehnen mit B.
    /// Eine Tuete kommt in den Rucksack (Spielstand), rauchen mit G / Steuerkreuz links gibt den Chill-Effekt (Chill.cs).
    /// Wer schon einen Job fuer Moe erledigt hat, kriegt Freundschaftspreis. Nur lokal, ohne Netzwerk.
    /// </summary>
    public class NixDealer : QuestNpc
    {
        public static NixDealer Instance { get; private set; }
        public static readonly Color NameColor = Palette.Hex("B6FF3B");
        public const int MaxBaggies = 5;
        public const long Price = 1000, FriendPrice = 600;

        public static long CurrentPrice => SaveSystem.Profile.moeQuest > 0 ? FriendPrice : Price;

        public override string Speaker => "NIX";
        protected override Color TagColor => NameColor;
        protected override string TrackerTag => "NIX";

        static readonly string[] Openers =
        {
            "Psst. Na? Gleiches wie immer?",
            "Ey. Guck nicht so auffaellig.",
            "Wieder da? Hab noch was.",
            "Mann, du schon wieder. Gut so.",
        };

        static readonly string[] NoThanks = { "Kein Ding. Du weisst, wo ich steh.", "Okay. Ich war nie hier.", "Auch gut. Mehr fuer mich." };

        Transform _bag;
        float _dealTime = -1f;

        // ------------------------------------------------------------------ Ansprechen

        public override void Talk()
        {
            if (Talking) return;
            var p = SaveSystem.Profile;
            if (p.baggies >= MaxBaggies)
            {
                OpenDialog(new[] { "Du hast doch noch " + p.baggies + " Tueten, Bro.\nErstmal die wegrauchen. [G]" }, null, null, _ => { });
                return;
            }
            string offer = "Eine Tuete: " + UIFactory.Money(CurrentPrice) + ".\nMacht dich ganz ruhig auf dem Board.";
            string[] pages;
            if (p.nixDeals == 0)
                pages = new[] { "Psst. Ja, du. Komm mal her.", "Ich bin Nix. Nicht \"nichts\". Nix.", offer };
            else if (CurrentPrice == FriendPrice && !p.nixFriend)
                pages = new[] { "Moe sagt, du bist korrekt.", "Heisst: Freundschaftspreis. " + offer };
            else
                pages = new[] { Openers[Random.Range(0, Openers.Length)] + "\n" + offer };
            OpenDialog(pages, "[E] KAUFEN  " + UIFactory.Money(CurrentPrice), "[B] NEE, LASS MAL", yes =>
            {
                if (CurrentPrice == FriendPrice) p.nixFriend = true;
                if (yes) Buy();
                else Bark(NoThanks[Random.Range(0, NoThanks.Length)]);
            });
        }

        /// <summary>Kaufen (auch fuer den Autotest).</summary>
        public bool Buy()
        {
            var p = SaveSystem.Profile;
            if (p.baggies >= MaxBaggies) return false;
            if (!SaveSystem.TrySpend(CurrentPrice))
            {
                Bark("Kein Geld, keine Tuete.\nFahr erstmal 'ne Combo.");
                return false;
            }
            p.baggies++;
            p.nixDeals++;
            SaveSystem.Save();
            Npc?.Play("Deal");
            _dealTime = 0f;
            Bark("Hier. Und du hast das\nnicht von mir.");
            BurstPop.Show("+1 TUETE", NameColor, new Vector2(0f, 260f), 230f, 1.8f);
            HUD.Instance?.Toast("TUETE GEKAUFT  [G] / Steuerkreuz links: chillen", 4f);
            return true;
        }

        // ------------------------------------------------------------------ Ablauf

        protected override void Start()
        {
            Instance = this;
            base.Start();
            _bag = BuildBag().transform;
            _bag.gameObject.SetActive(false);
        }

        protected override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_bag != null) Destroy(_bag.gameObject);
            base.OnDestroy();
        }

        protected override void Update()
        {
            if (_dealTime >= 0f)
            {
                _dealTime += Time.deltaTime;
                if (_dealTime > 3f) _dealTime = -1f;
            }
            base.Update();
        }

        /// <summary>Tuete in der rechten Hand, solange die Hand beim Deal vorn ist (nach der Animation, daher LateUpdate).</summary>
        void LateUpdate()
        {
            bool show = _dealTime > 0.75f && _dealTime < 2.45f && Npc != null && Npc.CurrentAction == "Deal";
            if (_bag.gameObject.activeSelf != show) _bag.gameObject.SetActive(show);
            if (!show) return;
            var anim = Npc.Animator;
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var mid = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            var index = anim.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            var pinky = anim.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            if (hand == null || mid == null || index == null || pinky == null) return;
            Vector3 fingers = (mid.position - hand.position).normalized;
            Vector3 palm = Vector3.Cross(fingers, (index.position - pinky.position).normalized).normalized;
            if (Vector3.Dot(palm, Vector3.up) < 0f) palm = -palm; // beim Deal zeigt die Handflaeche nach oben
            _bag.SetPositionAndRotation(Vector3.Lerp(hand.position, mid.position, 1.05f) + palm * 0.028f, Quaternion.LookRotation(fingers, palm));
        }

        /// <summary>Kleine Tuete: Plastik mit rotem Zip-Verschluss, gruener Inhalt.</summary>
        static GameObject BuildBag()
        {
            var root = new GameObject("Baggie");
            Part(root.transform, new Vector3(0f, 0f, 0f), new Vector3(0.05f, 0.014f, 0.068f), Palette.Hex("E9F2F0"));
            Part(root.transform, new Vector3(0f, 0.003f, -0.008f), new Vector3(0.044f, 0.012f, 0.045f), Palette.Hex("6BA83A"));
            Part(root.transform, new Vector3(0f, 0f, 0.03f), new Vector3(0.052f, 0.016f, 0.008f), Palette.Hex("E8483A"));
            return root;
        }

        static void Part(Transform parent, Vector3 pos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = ToonMaterials.Get(color, 0f, false, 0f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ Anzeige

        protected override string IndicatorText => SaveSystem.Profile.baggies < MaxBaggies ? "$" : null;
        protected override Color IndicatorColor => NameColor;
    }
}
