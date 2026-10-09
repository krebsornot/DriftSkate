using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DriftSkate
{
    /// <summary>
    /// Gemeinsame Basis fuer NPCs, die Auftraege vergeben (Jojo, Luna): Ansprechen mit E / X (oder F / Y),
    /// Dialog in Manga-Sprechblasen mit Kamera ueber der Schulter, Fortschritts-Kasten links oben,
    /// Zielmarkierung am Bildrand, "!"-Zacke ueber dem Kopf und Leuchtsaeulen als Ziel in der Welt.
    /// </summary>
    public abstract class QuestNpc : MonoBehaviour
    {
        public static readonly List<QuestNpc> All = new List<QuestNpc>();
        const float TalkRange = 3.4f;

        protected StonerNpc Npc { get; private set; }

        /// <summary>Name in der Sprechblase und im Prompt ("JOJO").</summary>
        public abstract string Speaker { get; }
        protected abstract Color TagColor { get; }
        /// <summary>Kleines Schild ueber dem Fortschritts-Kasten ("JOJOS AUFTRAG").</summary>
        protected abstract string TrackerTag { get; }

        public abstract void Talk();

        // ------------------------------------------------------------------ Was die Anzeige zeigen soll

        protected virtual bool TrackerVisible => false;
        protected virtual string TrackerTitle => "";
        protected virtual string TrackerText => "";
        protected virtual Color TrackerTextColor => Palette.White;
        /// <summary>Wohin die Markierung zeigt (null = keine).</summary>
        protected virtual Vector3? MarkerGoal => null;
        /// <summary>Zeichen in der Zacke ueber dem Kopf (null = keine).</summary>
        protected virtual string IndicatorText => null;
        protected virtual Color IndicatorColor => Palette.Yellow;

        // ------------------------------------------------------------------ Ansprechen

        public bool Talking => _bubble != null;
        public bool ChoiceOpen => _bubble != null && _bubble.HasChoices;
        public static bool AnyTalking => All.Exists(q => q != null && q.Talking);

        /// <summary>Naechster Auftraggeber in Reichweite (null, wenn keiner da ist oder gerade jemand redet).</summary>
        public static QuestNpc Near(Vector3 pos)
        {
            if (AnyTalking) return null;
            QuestNpc best = null;
            float bestD = TalkRange * TalkRange;
            foreach (var q in All)
            {
                if (q == null || !q.CanTalk) continue;
                float d = (pos - q.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = q; }
            }
            return best;
        }

        /// <summary>Gerade ansprechbar (z. B. nicht waehrend eines laufenden Spielmodus).</summary>
        protected virtual bool CanTalk => true;

        // ------------------------------------------------------------------ Dialog

        SpeechBubble _bubble;
        string[] _pages;
        int _page, _openedFrame, _unblockFrame = -1;
        string _accept, _decline;
        Action<bool> _onEnd;
        int _testPress; // 1 = ja, 2 = nein (nur fuer den Autotest)

        /// <summary>Seiten nacheinander zeigen; auf der letzten Seite ggf. zwei Antworten. onEnd(true) = angenommen.</summary>
        protected void OpenDialog(string[] pages, string accept, string decline, Action<bool> onEnd)
        {
            if (Npc == null || Npc.Head == null || pages.Length == 0) return;
            _pages = pages;
            _page = 0;
            _accept = accept;
            _decline = decline;
            _onEnd = onEnd;
            _openedFrame = Time.frameCount;
            _unblockFrame = -1;
            GameInput.Blocked = true;
            _bubble = SpeechBubble.Show(Npc.Head, Vector3.up * 0.32f, Speaker, TagColor, pages[0], true);
            if (pages.Length == 1) _bubble.SetChoices(accept, decline);
            SetCameraShot();
        }

        /// <summary>Taste fuer "weiter / ja" bzw. "nein" (B, am Pad auch der B-Knopf).</summary>
        protected static bool YesPressed => GameInput.Talk.WasPressedThisFrame() || GameInput.Interact.WasPressedThisFrame() || GameInput.Ollie.WasPressedThisFrame();
        protected static bool NoPressed => GameInput.Board.WasPressedThisFrame() || GameInput.Grind.WasPressedThisFrame();

        void UpdateDialog()
        {
            bool paused = HUD.Instance != null && HUD.Instance.Paused;
            if (paused || Time.frameCount == _openedFrame) return;
            GameInput.Blocked = true;
            bool f = YesPressed || _testPress == 1;
            bool b = NoPressed || _testPress == 2;
            _testPress = 0;
            if (!f && !b) return;

            bool last = _page >= _pages.Length - 1;
            if (_bubble.Typing && !b)
            {
                _bubble.SkipTyping();
                return;
            }
            if (!last && !b)
            {
                _page++;
                _bubble.SetText(_pages[_page]);
                if (_page == _pages.Length - 1) _bubble.SetChoices(_accept, _decline);
                return;
            }
            // Letzte Seite: ja / nein. B auf frueheren Seiten bricht ab.
            CloseDialog(f && !b);
        }

        void CloseDialog(bool yes)
        {
            _bubble.Close();
            _bubble = null;
            _unblockFrame = Time.frameCount + 1; // sonst loest derselbe Tastendruck noch "Einsteigen" o. ae. aus
            CameraRig.Instance?.ClearDialogShot();
            var done = _onEnd;
            _onEnd = null;
            done?.Invoke(yes);
        }

        /// <summary>Steuerung erst im naechsten Frame wieder freigeben (z. B. nach einer Kamerafahrt).</summary>
        protected void UnblockNextFrame() => _unblockFrame = Time.frameCount + 1;

        /// <summary>Schuss ueber die Schulter: Figur links im Bild, Blase rechts. Kamera auf die Seite, wo der Spieler nicht steht.</summary>
        void SetCameraShot()
        {
            var rig = CameraRig.Instance;
            if (rig == null || Npc.Head == null) return;
            Vector3 head = Npc.Head.position;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float side = 1f;
            var p = PlayerAvatar.Local;
            if (p != null && Vector3.Dot(p.skater.transform.position - head, right) > 0f) side = -1f;
            // Steht da was im Weg (Baum, Laterne), die andere Seite nehmen
            int mask = ~LayerMask.GetMask("Skater", "Car", "Ignore Raycast", "Rail");
            Vector3 cam = head + fwd * 2.3f + right * side * 1.1f + Vector3.up * 0.05f;
            if (Physics.SphereCast(head, 0.3f, cam - head, out _, Vector3.Distance(head, cam), mask, QueryTriggerInteraction.Ignore))
            {
                Vector3 other = head + fwd * 2.3f - right * side * 1.1f + Vector3.up * 0.05f;
                if (!Physics.SphereCast(head, 0.3f, other - head, out _, Vector3.Distance(head, other), mask, QueryTriggerInteraction.Ignore)) cam = other;
                else cam = head + fwd * 1.8f + Vector3.up * 0.1f;
            }
            Vector3 camFwd = Vector3.ProjectOnPlane(head - cam, Vector3.up).normalized;
            Vector3 camRight = Vector3.Cross(Vector3.up, camFwd);
            // Blickpunkt rechts neben und etwas ueber dem Kopf: Figur links unten im Bild, rechts oben Platz fuer die Blase
            rig.SetDialogShot(cam, head + camRight * 0.7f + Vector3.up * 0.18f);
        }

        /// <summary>Fuer den Autotest: ja (true) oder nein (false) im Dialog druecken.</summary>
        public void TestPress(bool accept) => _testPress = accept ? 1 : 2;

        /// <summary>Kurzer Kommentar in einer kleinen Blase.</summary>
        protected void Bark(string text)
        {
            if (Npc == null || Npc.Head == null) return;
            SpeechBubble.Show(Npc.Head, Vector3.up * 0.3f, Speaker, TagColor, text, false, 2.4f);
        }

        // ------------------------------------------------------------------ Ablauf

        protected virtual void Start()
        {
            Npc = GetComponent<StonerNpc>();
            All.Add(this);
            BuildUi();
        }

        protected virtual void OnDestroy()
        {
            All.Remove(this);
            if (Talking) GameInput.Blocked = false;
            if (_tracker != null) Destroy(_tracker.gameObject);
            if (_marker != null) Destroy(_marker.gameObject);
            if (_indicator != null) Destroy(_indicator.gameObject);
        }

        protected virtual void Update()
        {
            if (Talking) UpdateDialog();
            else if (_unblockFrame >= 0 && Time.frameCount >= _unblockFrame)
            {
                _unblockFrame = -1;
                if (HUD.Instance == null || !HUD.Instance.Paused) GameInput.Blocked = false;
            }
            UpdateUi(PlayerAvatar.Local);
        }

        // ------------------------------------------------------------------ Hilfen

        protected static Vector3 PlayerPos(PlayerAvatar p) => p.Mode == PlayerMode.Driving ? p.car.transform.position : p.skater.transform.position;

        protected static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 60f, ~LayerMask.GetMask("Rail", "Skater", "Car"), QueryTriggerInteraction.Ignore))
                p.y = hit.point.y;
            return p;
        }

        /// <summary>Leuchtsaeule als Ziel, von weitem zu sehen. Optional mit schwebender Chipstuete.</summary>
        protected static GameObject BuildBeacon(string name, Vector3 pos, Color color, bool bag)
        {
            var root = new GameObject(name);
            root.transform.position = pos;
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(beam.GetComponent<Collider>());
            beam.transform.SetParent(root.transform, false);
            beam.transform.localPosition = Vector3.up * 30f;
            beam.transform.localScale = new Vector3(2.4f, 30f, 2.4f);
            var baseMat = Resources.Load<Material>("TrailMat");
            if (baseMat != null)
            {
                var m = new Material(baseMat) { name = "Beacon" };
                m.SetColor("_BaseColor", new Color(color.r, color.g, color.b, 0.3f));
                m.SetFloat("_FogAmount", 0.3f);
                beam.GetComponent<Renderer>().sharedMaterial = m;
            }
            beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (bag)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(b.GetComponent<Collider>());
                b.name = "Bag";
                b.transform.SetParent(root.transform, false);
                b.transform.localPosition = Vector3.up * 1.4f;
                b.transform.localScale = new Vector3(0.55f, 0.75f, 0.22f);
                b.GetComponent<Renderer>().sharedMaterial = ToonMaterials.Get(Palette.Red, 0.2f, false, 0.6f);
                root.AddComponent<Spin>().target = b.transform;
            }
            return root;
        }

        class Spin : MonoBehaviour
        {
            public Transform target;
            void Update() { if (target != null) target.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World); }
        }

        // ------------------------------------------------------------------ Anzeige

        RectTransform _tracker, _marker, _indicator;
        Text _trackTitle, _trackText, _markerDist, _indicatorText;
        Image _indicatorImg;

        void BuildUi()
        {
            var layer = SpeechBubble.Layer;

            // Fortschritt links oben, unter Netz-Info und Song (mehrere Kaesten stapeln sich)
            var panel = UIFactory.Panel(layer, "Quest_" + Speaker, new Color(0.09f, 0.07f, 0.12f, 0.88f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                                        new Vector2(30f, -146f), new Vector2(470f, 118f), 1.2f);
            _tracker = panel.rectTransform;
            var tag = UIFactory.Panel(_tracker, "Tag", TagColor, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(-8f, 2f), new Vector2(220f, 36f), -4f);
            var tagText = UIFactory.Label(tag.transform, TrackerTag, 23, Palette.Ink, TextAnchor.MiddleCenter, false, false);
            tagText.font = UIFactory.GraffitiFont;
            _trackTitle = UIFactory.LabelAt(_tracker, "", 32, Palette.Yellow, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18f, -26f), new Vector2(440f, 40f));
            _trackTitle.font = UIFactory.GraffitiFont;
            _trackTitle.fontStyle = FontStyle.Normal;
            _trackText = UIFactory.LabelAt(_tracker, "", 23, Palette.White, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18f, -66f), new Vector2(440f, 44f));

            // Zielmarkierung am Bildschirm
            _marker = UIFactory.Rect(layer, "Marker_" + Speaker, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 56f));
            BurstImage(_marker, TagColor);
            var ex = UIFactory.Label(_marker, "!", 34, Palette.Ink, TextAnchor.MiddleCenter, false, false);
            ex.font = UIFactory.GraffitiFont;
            _markerDist = UIFactory.LabelAt(_marker, "", 22, Palette.White, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(140f, 28f), TextAnchor.UpperCenter);

            // Zacke ueber dem Kopf
            _indicator = UIFactory.Rect(layer, "Indicator_" + Speaker, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(84f, 74f));
            var sh = BurstImage(_indicator, Palette.Ink);
            sh.rectTransform.offsetMin = sh.rectTransform.offsetMax = new Vector2(5f, -5f);
            _indicatorImg = BurstImage(_indicator, Palette.Yellow);
            _indicatorText = UIFactory.Label(_indicator, "!", 46, Palette.Ink, TextAnchor.MiddleCenter, false, false);
            _indicatorText.font = UIFactory.GraffitiFont;
        }

        static Image BurstImage(RectTransform parent, Color color)
        {
            var go = new GameObject("Burst", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = SpeechBubble.BurstSprite;
            img.color = color;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return img;
        }

        void UpdateUi(PlayerAvatar p)
        {
            if (_tracker == null) return;
            bool hud = !Admin.HideHud && !HUD.Cinematic && p != null;

            bool tracking = hud && TrackerVisible;
            _tracker.gameObject.SetActive(tracking);
            if (tracking)
            {
                int slot = 0;
                foreach (var q in All)
                {
                    if (q == this) break;
                    if (q != null && q.TrackerVisible) slot++;
                }
                _tracker.anchoredPosition = new Vector2(30f, -146f - slot * 134f);
                _trackTitle.text = TrackerTitle;
                _trackText.text = TrackerText;
                _trackText.color = TrackerTextColor;
            }

            Vector3? goal = hud && !AnyTalking ? MarkerGoal : null;
            _marker.gameObject.SetActive(goal.HasValue);
            if (goal.HasValue) PlaceMarker(goal.Value, PlayerPos(p));

            string ind = hud && !Talking ? IndicatorText : null;
            bool showInd = ind != null && Npc != null && Npc.Head != null;
            if (showInd)
            {
                Vector3 w = Npc.Head.position + Vector3.up * 0.5f;
                float dist = Camera.main != null ? Vector3.Distance(Camera.main.transform.position, w) : 0f;
                showInd = dist < 60f && SpeechBubble.Pin(_indicator, w, new Vector2(0f, Mathf.Sin(Time.time * 2f + Speaker.Length) * 6f));
                _indicator.localScale = Vector3.one * Mathf.Clamp(9f / Mathf.Max(1f, dist), 0.45f, 1.05f);
            }
            _indicator.gameObject.SetActive(showInd);
            if (showInd)
            {
                _indicator.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 1.3f) * 4f);
                _indicatorImg.color = IndicatorColor;
                _indicatorText.text = ind;
            }
        }

        void PlaceMarker(Vector3 goal, Vector3 from)
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 v = cam.WorldToViewportPoint(goal);
            bool behind = v.z < 0f;
            Vector2 c = new Vector2(v.x - 0.5f, v.y - 0.5f);
            if (behind) c = -c;
            const float mx = 0.045f, my = 0.08f;
            bool off = behind || Mathf.Abs(c.x) > 0.5f - mx || Mathf.Abs(c.y) > 0.5f - my;
            if (off)
            {
                // An den Bildrand schieben, in Richtung des Ziels (hinter einem: nach unten)
                if (behind) c.y = Mathf.Min(c.y, -0.1f);
                if (c.sqrMagnitude < 1e-6f) c = Vector2.down;
                float k = Mathf.Min((0.5f - mx) / Mathf.Max(1e-4f, Mathf.Abs(c.x)), (0.5f - my) / Mathf.Max(1e-4f, Mathf.Abs(c.y)));
                c *= k;
            }
            _marker.anchorMin = _marker.anchorMax = c + new Vector2(0.5f, 0.5f);
            _marker.anchoredPosition = new Vector2(0f, off ? 0f : Mathf.Sin(Time.time * 2.4f) * 4f);
            _marker.localScale = Vector3.one * (off ? 0.85f : 1f);
            _markerDist.text = Mathf.RoundToInt(Vector3.Distance(from, goal)) + " m";
        }
    }
}
