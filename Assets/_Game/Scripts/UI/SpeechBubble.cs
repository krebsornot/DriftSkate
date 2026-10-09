using UnityEngine;
using UnityEngine.UI;

namespace DriftSkate
{
    /// <summary>
    /// Manga-Sprechblase ueber einer Figur: weisse Blase mit dicker Tusche-Linie, versetzter Farbschatten,
    /// Schwanz zeigt auf den Kopf, schraeges Namensschild, Text tippt sich ein. Zwei Arten:
    /// grosse Dialog-Blase (mit Weiter-Pfeil und Auswahl) und kleine Kommentar-Blase, die von selbst verschwindet.
    /// </summary>
    public class SpeechBubble : MonoBehaviour
    {
        // ------------------------------------------------------------------ Gemeinsame Teile

        static Canvas _canvas;
        static Sprite _body, _tail, _burst;

        /// <summary>Eigene Ebene ueber dem HUD (wird mit der Szene weggeraeumt und bei Bedarf neu gebaut).</summary>
        public static RectTransform Layer
        {
            get
            {
                if (_canvas == null)
                {
                    _canvas = UIFactory.Canvas("Bubble Canvas", 11);
                    _canvas.GetComponent<GraphicRaycaster>().enabled = false;
                }
                return (RectTransform)_canvas.transform;
            }
        }

        public static Sprite BodySprite => _body != null ? _body : (_body = MakeBody());
        public static Sprite TailSprite => _tail != null ? _tail : (_tail = MakeTail());
        public static Sprite BurstSprite => _burst != null ? _burst : (_burst = MakeBurst());

        /// <summary>Weltpunkt als Bildschirm-Anteil (0..1); false, wenn er hinter der Kamera liegt.</summary>
        public static bool ToViewport(Vector3 world, out Vector2 vp)
        {
            vp = default;
            var cam = Camera.main;
            if (cam == null) return false;
            Vector3 v = cam.WorldToViewportPoint(world);
            vp = v;
            return v.z > 0.05f;
        }

        /// <summary>UI-Element an einen Weltpunkt heften (Anker = Bildschirm-Anteil, passt so bei jeder Aufloesung).</summary>
        public static bool Pin(RectTransform rt, Vector3 world, Vector2 offset)
        {
            if (!ToViewport(world, out Vector2 vp)) return false;
            rt.anchorMin = rt.anchorMax = vp;
            rt.anchoredPosition = offset;
            return true;
        }

        // ------------------------------------------------------------------ Eine Blase

        const float Pad = 30f, TailTipX = 16f, TailW = 64f, TailTipY = 2f, TailH = 96f;

        Transform _anchor;
        Vector3 _offset;
        bool _dialog;
        RectTransform _rt, _tailRt, _choices, _next;
        Text _text;
        CanvasGroup _group;
        string _full = "";
        int _lastChar;
        float _shown, _hold, _age, _life, _closeAt = -1f, _punch, _restTilt, _width, _height;
        bool _showNext;

        public bool Typing => _shown < _full.Length;
        public bool Closed => _closeAt >= 0f || this == null;
        public bool HasChoices => _choices != null && _choices.gameObject.activeSelf;

        /// <summary>Neue Blase. dialog = gross mit Weiter-Pfeil; life &gt; 0 = verschwindet nach so vielen Sekunden (nach dem Tippen).</summary>
        public static SpeechBubble Show(Transform anchor, Vector3 offset, string speaker, Color tagColor, string text, bool dialog, float life = 0f)
        {
            var go = new GameObject("Bubble_" + speaker, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(Layer, false);
            var b = go.AddComponent<SpeechBubble>();
            b.Build(speaker, tagColor, dialog);
            b._anchor = anchor;
            b._offset = offset;
            b._life = life;
            b.SetText(text);
            b._punch = 0f;
            b.Place();
            return b;
        }

        void Build(string speaker, Color tagColor, bool dialog)
        {
            _dialog = dialog;
            _rt = (RectTransform)transform;
            _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
            _rt.pivot = new Vector2(dialog ? 0.22f : 0.5f, 0f);
            _group = GetComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _restTilt = Random.Range(-1.6f, 1.6f);

            // Harter Schatten in Knallfarbe, leicht versetzt (wie aufgedruckt)
            var shadow = Img("Shadow", BodySprite, dialog ? Palette.Pink : new Color(0.55f, 0.36f, 1f, 0.9f), true);
            Fill(shadow.rectTransform, new Vector2(dialog ? 10f : 7f, dialog ? -10f : -7f));

            var body = Img("Body", BodySprite, Color.white, true);
            Fill(body.rectTransform, Vector2.zero);

            // Schwanz nach dem Koerper, damit er dessen Unterkante ueberdeckt
            var tail = Img("Tail", TailSprite, Color.white, false);
            _tailRt = tail.rectTransform;
            _tailRt.anchorMin = _tailRt.anchorMax = Vector2.zero;
            _tailRt.pivot = new Vector2(0.5f, 1f);

            var textHolder = UIFactory.Rect(transform, "Text", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _text = UIFactory.Label(textHolder, "", dialog ? 33 : 26, Palette.Ink, TextAnchor.UpperLeft, false, false);
            _text.font = UIFactory.ComicFont;
            _text.fontStyle = FontStyle.Bold;
            _text.supportRichText = true;
            _text.lineSpacing = 1.05f;

            // Namensschild schraeg oben links
            var tag = UIFactory.Panel(transform, "Name", tagColor, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0.5f),
                                      new Vector2(dialog ? 22f : 14f, 0f), new Vector2(dialog ? 150f : 104f, dialog ? 42f : 32f), -5f);
            var name = UIFactory.Label(tag.transform, speaker, dialog ? 28 : 21, Palette.Ink, TextAnchor.MiddleCenter, false, false);
            name.font = UIFactory.GraffitiFont;

            if (dialog)
            {
                _next = UIFactory.Rect(transform, "Next", Vector2.right, Vector2.right, new Vector2(1f, 0f), new Vector2(-24f, 14f), new Vector2(40f, 34f));
                var arrow = UIFactory.Label(_next, "▼", 28, Palette.Pink, TextAnchor.MiddleCenter, false, false);
                arrow.font = UIFactory.Font;
            }
        }

        Image Img(string name, Sprite sprite, Color color, bool sliced)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static void Fill(RectTransform rt, Vector2 shift)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = shift;
            rt.offsetMax = shift;
        }

        /// <summary>Neuer Text: tippt von vorn, Blase passt ihre Groesse an und federt kurz.</summary>
        public void SetText(string text)
        {
            _full = text ?? "";
            _shown = 0f;
            _lastChar = 0;
            _hold = 0.08f;
            _punch = 1f;
            _showNext = false;
            Layout();
        }

        /// <summary>Zwei Antworten unten in der Blase (z. B. "[F] KLAR" / "[B] NOE"). null = keine.</summary>
        public void SetChoices(string accept, string decline)
        {
            if (_choices != null) Destroy(_choices.gameObject);
            _choices = null;
            if (accept != null)
            {
                _choices = UIFactory.Rect(transform, "Choices", Vector2.zero, Vector2.right, new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(-Pad * 2f, 50f));
                Chip(_choices, accept, Palette.Lime, 0f, -2f);
                if (decline != null) Chip(_choices, decline, Palette.Cream, 1f, 2.5f);
            }
            Layout();
        }

        static void Chip(RectTransform parent, string label, Color color, float side, float tilt)
        {
            var p = UIFactory.Panel(parent, "Chip", color, new Vector2(side, 0f), new Vector2(side, 1f), new Vector2(side, 0.5f), Vector2.zero, new Vector2(260f, 0f), tilt);
            var t = UIFactory.Label(p.transform, label, 25, Palette.Ink, TextAnchor.MiddleCenter, false, false);
            t.font = UIFactory.GraffitiFont;
        }

        public void SkipTyping()
        {
            _shown = _full.Length;
            _text.text = _full;
        }

        public void Close()
        {
            if (_closeAt < 0f) _closeAt = _age;
        }

        void Layout()
        {
            if (_text == null) return;
            _text.text = _full;
            float extra = HasChoices ? 74f : _dialog ? 34f : 0f;
            if (_dialog) _width = 640f;
            else
            {
                _text.horizontalOverflow = HorizontalWrapMode.Overflow;
                _width = Mathf.Clamp(_text.preferredWidth + Pad * 2f, 180f, 430f);
                _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            _rt.sizeDelta = new Vector2(_width, 200f);
            var tr = (RectTransform)_text.transform.parent;
            float top = Pad + (_dialog ? 10f : 6f);
            tr.offsetMin = new Vector2(Pad, Pad * 0.7f + extra);
            tr.offsetMax = new Vector2(-Pad, -top);
            _height = Mathf.Max(_dialog ? 150f : 74f, _text.preferredHeight + top + Pad * 0.7f + extra);
            _rt.sizeDelta = new Vector2(_width, _height);
            _text.text = "";
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _age += dt;

            // Schreibmaschine, kurze Pause nach Satzzeichen
            if (Typing)
            {
                _hold -= dt;
                if (_hold <= 0f)
                {
                    _shown = Mathf.Min(_full.Length, _shown + dt * (_dialog ? 42f : 55f));
                    if ((int)_shown > _lastChar)
                    {
                        _lastChar = (int)_shown;
                        char c = _full[_lastChar - 1];
                        // Pause nur am Satzende, nicht beim Punkt in "$3.000"
                        bool gap = _lastChar >= _full.Length || char.IsWhiteSpace(_full[_lastChar]);
                        if (gap && (c == '.' || c == '!' || c == '?')) _hold = 0.16f;
                        else if (gap && c == ',') _hold = 0.07f;
                    }
                }
                int n = (int)_shown;
                _text.text = n >= _full.Length ? _full : _full.Substring(0, n) + "<color=#00000000>" + _full.Substring(n) + "</color>";
            }
            else if (_text.text.Length != _full.Length) _text.text = _full;
            if (!Typing && !_showNext) _showNext = true;

            if (_life > 0f && _closeAt < 0f && !Typing && _age > _life + _full.Length / 55f) Close();

            Place();
        }

        void Place()
        {
            if (_anchor == null) { Destroy(gameObject); return; }
            float pop = Mathf.Clamp01(_age / 0.34f);
            float scale = EaseOutBack(pop);
            float alpha = 1f;
            if (_closeAt >= 0f)
            {
                float k = Mathf.Clamp01((_age - _closeAt) / 0.2f);
                scale *= 1f - 0.3f * k;
                alpha = 1f - k;
                if (k >= 1f) { Destroy(gameObject); return; }
            }
            _punch = Mathf.MoveTowards(_punch, 0f, Time.unscaledDeltaTime * 4f);
            scale *= 1f + Mathf.Sin(_punch * Mathf.PI) * 0.035f;

            Vector3 world = _anchor.position + _offset;
            bool visible = ToViewport(world, out Vector2 vp);
            if (!_dialog && Camera.main != null && (world - Camera.main.transform.position).sqrMagnitude > 30f * 30f) visible = false;
            _group.alpha = visible ? alpha : 0f;
            if (!visible) return;

            // Blase ueber/rechts vom Kopf, aber immer ganz im Bild. Gerechnet in Ebenen-Einheiten um die Bildmitte,
            // gesetzt relativ zum Anker am Kopf (so stimmt es auch, wenn sich die Aufloesung aendert).
            Rect area = Layer.rect;
            Vector2 head = new Vector2((vp.x - 0.5f) * area.width, (vp.y - 0.5f) * area.height);
            _rt.anchorMin = _rt.anchorMax = vp;
            Vector2 size = new Vector2(_width, _height);
            Vector2 pos = head + (_dialog ? new Vector2(70f, 70f) : new Vector2(0f, 80f));
            pos.y += Mathf.Sin(_age * 2.2f) * 3f;
            const float m = 24f;
            pos.x = Mathf.Clamp(pos.x, area.xMin + m + _rt.pivot.x * size.x, area.xMax - m - (1f - _rt.pivot.x) * size.x);
            pos.y = Mathf.Clamp(pos.y, area.yMin + m, area.yMax - m - size.y - 30f);
            _rt.anchoredPosition = pos - head;
            _rt.localScale = Vector3.one * scale;
            _rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(_dialog ? -4f : 4f, _restTilt, EaseOut(pop)));

            // Schwanz: an der Unterkante Richtung Kopf
            Vector2 bottomLeft = pos - new Vector2(_rt.pivot.x * size.x, 0f);
            float bx = Mathf.Clamp(head.x - bottomLeft.x, 52f, size.x - 52f);
            float overlap = 10f;
            _tailRt.anchoredPosition = new Vector2(bx, overlap);
            Vector2 v = head + new Vector2(0f, 14f) - (bottomLeft + new Vector2(bx, overlap));
            float w = _dialog ? 58f : 44f;
            float len = Mathf.Clamp(v.magnitude, 30f, _dialog ? 150f : 100f);
            float flip = v.x > 0f ? -1f : 1f;
            _tailRt.sizeDelta = new Vector2(w, len);
            _tailRt.localScale = new Vector3(flip, 1f, 1f);
            Vector2 tipDir = new Vector2((TailTipX / TailW - 0.5f) * w * flip, -(1f - TailTipY / TailH) * len);
            _tailRt.localRotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(tipDir, v) - _rt.localEulerAngles.z);

            if (_next != null)
            {
                _next.gameObject.SetActive(_showNext && !HasChoices);
                _next.anchoredPosition = new Vector2(-24f, 14f + Mathf.Abs(Mathf.Sin(_age * 2.6f)) * 5f);
            }
        }

        static float EaseOutBack(float t)
        {
            const float c = 1.2f;
            t -= 1f;
            return 1f + (c + 1f) * t * t * t + c * t * t;
        }

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        // ------------------------------------------------------------------ Sprites (im Code gezeichnet)

        static Color32 Px(float fillAlpha, float ink)
        {
            Color c = Color.Lerp(Color.white, Palette.Ink, Mathf.Clamp01(ink));
            c.a = Mathf.Clamp01(fillAlpha);
            return c;
        }

        static Texture2D NewTex(int w, int h, string name) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

        /// <summary>Abgerundetes Rechteck mit 7 px Tusche-Rand, 9-teilig skalierbar.</summary>
        static Sprite MakeBody()
        {
            const int S = 128;
            const float R = 46f, line = 7f, half = S * 0.5f - 2f;
            var tex = NewTex(S, S, "BubbleBody");
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - S * 0.5f) - (half - R);
                    float qy = Mathf.Abs(y + 0.5f - S * 0.5f) - (half - R);
                    float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - R;
                    px[y * S + x] = Px(0.5f - d, d + line + 0.5f);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(52, 52, 52, 52));
        }

        /// <summary>Spitzer Schwanz: oben offen (deckt die Blasenkante ab), Spitze unten links.</summary>
        static Sprite MakeTail()
        {
            const int W = (int)TailW, H = (int)TailH;
            const float line = 6.5f;
            var tex = NewTex(W, H, "BubbleTail");
            var px = new Color32[W * H];
            Vector2 tip = new Vector2(TailTipX, TailTipY), l = new Vector2(8f, H + 4f), r = new Vector2(W - 8f, H + 4f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float sl = Side(l, tip, p), sr = Side(tip, r, p);
                    float s = Mathf.Min(sl, sr);
                    px[y * W + x] = Px(s + 0.5f, line + 0.5f - s);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 1f), 100f);
        }

        /// <summary>Vorzeichenbehafteter Abstand zur Kante a-&gt;b (positiv = innen).</summary>
        static float Side(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 d = (b - a).normalized;
            return d.x * (p.y - a.y) - d.y * (p.x - a.x);
        }

        /// <summary>Zacken-Explosion fuer Ausrufe ("!", Belohnung).</summary>
        static Sprite MakeBurst()
        {
            const int S = 128, spikes = 13;
            const float line = 6f;
            var tex = NewTex(S, S, "Burst");
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f - S * 0.5f, y + 0.5f - S * 0.5f);
                    float a = (Mathf.Atan2(p.y, p.x) / (Mathf.PI * 2f) + 1f) * spikes;
                    int i = Mathf.FloorToInt(a) % spikes;
                    float amp = 0.17f + 0.09f * Mathf.Abs(Mathf.Sin(i * 12.9898f));
                    float tri = Mathf.Abs(a - Mathf.Floor(a) - 0.5f) * 2f;
                    float rad = 61f * (1f - amp + amp * tri);
                    float d = (p.magnitude - rad) * 0.85f;
                    px[y * S + x] = Px(0.5f - d, d + line + 0.5f);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    /// <summary>Kurz aufploppende Zacken-Blase mitten im Bild (z. B. "+$1.500").</summary>
    public class BurstPop : MonoBehaviour
    {
        RectTransform _rt;
        CanvasGroup _group;
        float _age, _life, _tilt;

        public static void Show(string text, Color color, Vector2 pos, float size, float life = 2f)
        {
            var go = new GameObject("BurstPop", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(SpeechBubble.Layer, false);
            var b = go.AddComponent<BurstPop>();
            b._rt = (RectTransform)go.transform;
            b._rt.anchorMin = b._rt.anchorMax = new Vector2(0.5f, 0.5f);
            b._rt.anchoredPosition = pos;
            b._rt.sizeDelta = new Vector2(size * 1.25f, size);
            b._group = go.GetComponent<CanvasGroup>();
            b._group.blocksRaycasts = false;
            b._life = life;
            b._tilt = Random.Range(-7f, 7f);
            var shadow = MakeImage(go.transform, Palette.Ink);
            shadow.rectTransform.offsetMin = shadow.rectTransform.offsetMax = new Vector2(9f, -9f);
            MakeImage(go.transform, color);
            var t = UIFactory.Label(go.transform, text, Mathf.RoundToInt(size * 0.26f), Palette.Ink, TextAnchor.MiddleCenter, false, false);
            t.font = UIFactory.GraffitiFont;
            b.Update();
        }

        static Image MakeImage(Transform parent, Color color)
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

        void Update()
        {
            _age += Time.unscaledDeltaTime;
            float pop = Mathf.Clamp01(_age / 0.3f);
            float s = 1f + 1.6f * Mathf.Pow(pop - 1f, 3f) + 0.6f * Mathf.Pow(pop - 1f, 2f);
            float fade = Mathf.Clamp01((_life - _age) / 0.4f);
            _rt.localScale = Vector3.one * s * (0.85f + 0.15f * fade);
            _rt.localRotation = Quaternion.Euler(0f, 0f, _tilt + Mathf.Sin(_age * 1.8f) * 1.5f);
            _group.alpha = fade;
            if (_age >= _life) Destroy(gameObject);
        }
    }
}
