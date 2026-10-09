using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DriftSkate
{
    /// <summary>Baut UGUI-Elemente im Code, im Jet-Set-Radio-Stil (knallige Flaechen, dicke Outline-Schrift).</summary>
    public static class UIFactory
    {
        static Font _font, _graffiti, _marker, _comic;
        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        /// <summary>Kraeftige Schrift fuer Spray-Knoepfe und Logo (Impact, sonst Arial Black / Standard).</summary>
        public static Font GraffitiFont => _graffiti != null ? _graffiti : (_graffiti = OsFont("Impact", "Arial Black"));

        /// <summary>Filzstift-Schrift fuer Tags (Ink Free, sonst Segoe Print / Standard).</summary>
        public static Font MarkerFont => _marker != null ? _marker : (_marker = OsFont("Ink Free", "Segoe Print"));

        /// <summary>Comic-Schrift fuer Sprechblasen (Comic Sans MS, sonst Segoe Print / Standard).</summary>
        public static Font ComicFont => _comic != null ? _comic : (_comic = OsFont("Comic Sans MS", "Segoe Print"));

        static Font OsFont(params string[] names)
        {
            try
            {
                var installed = new System.Collections.Generic.HashSet<string>(UnityEngine.Font.GetOSInstalledFontNames());
                foreach (var n in names)
                    if (installed.Contains(n)) return UnityEngine.Font.CreateDynamicFontFromOSFont(n, 40);
            }
            catch (Exception) { }
            return Font;
        }

        public static Canvas Canvas(string name, int sortOrder)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            // Expand: das 1920x1080-Layout passt immer ganz ins Fenster (auch 21:9, 16:10 oder Fenster ohne Taskleiste).
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            EnsureEventSystem();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            UnityEngine.Object.DontDestroyOnLoad(es);
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                                         Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(Transform parent, string name, float inset = 0f)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-inset * 2, -inset * 2));
            return rt;
        }

        public static Image Panel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                                  Vector2 pos, Vector2 size, float tilt = 0f, bool border = true)
        {
            var rt = Rect(parent, name, anchorMin, anchorMax, pivot, pos, size);
            rt.localRotation = Quaternion.Euler(0, 0, tilt);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (border)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = Palette.Ink;
                o.effectDistance = new Vector2(4, -4);
            }
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft,
                                 bool bold = true, bool outline = true, float outlineSize = 2.5f)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = bold ? FontStyle.BoldAndItalic : FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            if (outline)
            {
                var o = go.AddComponent<Outline>();
                o.effectColor = Palette.Ink;
                o.effectDistance = new Vector2(outlineSize, -outlineSize);
            }
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return t;
        }

        public static Text LabelAt(Transform parent, string text, int size, Color color, Vector2 anchor, Vector2 pivot, Vector2 pos,
                                   Vector2 boxSize, TextAnchor align = TextAnchor.MiddleLeft, bool bold = true, float tilt = 0f)
        {
            var holder = Rect(parent, "Label", anchor, anchor, pivot, pos, boxSize);
            holder.localRotation = Quaternion.Euler(0, 0, tilt);
            return Label(holder, text, size, color, align, bold);
        }

        public static Button Button(Transform parent, string label, Color color, Action onClick, Vector2 size, int fontSize = 30,
                                    Color? textColor = null)
        {
            var rt = Rect(parent, "Button_" + label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            var o = rt.gameObject.AddComponent<Outline>();
            o.effectColor = Palette.Ink;
            o.effectDistance = new Vector2(4, -4);
            var btn = rt.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 0.75f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.selectedColor = new Color(1f, 1f, 0.85f);
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var t = Label(rt, label, fontSize, textColor ?? Palette.Ink, TextAnchor.MiddleCenter, true, false);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = size.x;
            le.preferredHeight = size.y;
            return btn;
        }

        /// <summary>
        /// Knopf im Spruehdosen-Look: unsichtbare Klickflaeche, dahinter ein Farbstrich mit Nasen.
        /// Gleiche Beschriftung ergibt immer denselben Strich (variant &lt; 0).
        /// </summary>
        public static Button SprayButton(Transform parent, string label, Color color, Action onClick, Vector2 size, int fontSize = 30,
                                         Color? textColor = null, int variant = -1)
        {
            var rt = Rect(parent, "Button_" + label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            var paint = SprayPaint(rt, color, variant >= 0 ? variant : Mathf.Abs(label.GetHashCode()), size.y);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = paint;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 0.8f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f);
            colors.selectedColor = Color.white;
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var t = Label(rt, label, fontSize, textColor ?? Palette.Ink, TextAnchor.MiddleCenter, false, false);
            t.font = GraffitiFont;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = size.x;
            le.preferredHeight = size.y;
            return btn;
        }

        /// <summary>Farbstrich hinter einem Element: etwas breiter, die Nasen laufen unten heraus.</summary>
        public static Image SprayPaint(RectTransform target, Color color, int variant, float height)
        {
            float total = height / (SprayArt.StrokeBottom - SprayArt.StrokeTop);
            var rt = Rect(target, "Paint", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.offsetMin = new Vector2(-height * 0.22f, -total * (1f - SprayArt.StrokeBottom));
            rt.offsetMax = new Vector2(height * 0.22f, total * SprayArt.StrokeTop);
            rt.SetAsFirstSibling();
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = SprayArt.Stroke(variant);
            img.color = color;
            img.raycastTarget = false;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
            img.fillAmount = 1f;
            return img;
        }

        /// <summary>Farbe eines Knopfs setzen (Spray-Knopf: der Farbstrich, sonst die Flaeche).</summary>
        public static void SetButtonColor(Button b, Color color)
        {
            var paint = b.transform.Find("Paint");
            (paint != null ? paint.GetComponent<Image>() : b.GetComponent<Image>()).color = color;
        }

        /// <summary>Der Farbstrich eines Spray-Knopfs (oder null).</summary>
        public static Image PaintOf(Component c)
        {
            var paint = c.transform.Find("Paint");
            return paint != null ? paint.GetComponent<Image>() : null;
        }

        public static void SetButtonLabel(Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        public static Slider Slider(Transform parent, float value, Action<float> onChange, Vector2 size, Color fill)
        {
            var rt = Rect(parent, "Slider", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = size.x;
            le.preferredHeight = size.y;

            var bg = Rect(rt, "Background", new Vector2(0, 0.3f), new Vector2(1, 0.7f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.color = Palette.Ink;

            var fillArea = Rect(rt, "Fill Area", new Vector2(0, 0.3f), new Vector2(1, 0.7f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-8, -6));
            var fillRt = Rect(fillArea, "Fill", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var fillImg = fillRt.gameObject.AddComponent<Image>();
            fillImg.color = fill;

            var handleArea = Rect(rt, "Handle Area", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-20, 0));
            var handle = Rect(handleArea, "Handle", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 0));
            var hImg = handle.gameObject.AddComponent<Image>();
            hImg.color = Palette.White;
            var ho = handle.gameObject.AddComponent<Outline>();
            ho.effectColor = Palette.Ink;
            ho.effectDistance = new Vector2(3, -3);

            var slider = rt.gameObject.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handle;
            slider.targetGraphic = hImg;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = value;
            if (onChange != null) slider.onValueChanged.AddListener(v => onChange(v));
            return slider;
        }

        public static InputField InputField(Transform parent, string text, Action<string> onChange, Vector2 size, int fontSize = 30, bool spray = false)
        {
            var rt = Rect(parent, "Input", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = size.x;
            le.preferredHeight = size.y;
            var img = rt.gameObject.AddComponent<Image>();
            Graphic target = img;
            if (spray)
            {
                // Weisser Farbstrich als Feld, Text wie mit Filzstift
                img.color = new Color(1f, 1f, 1f, 0f);
                target = SprayPaint(rt, Palette.White, Mathf.Abs((text ?? "").GetHashCode()) + 3, size.y);
            }
            else
            {
                img.color = Palette.White;
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = Palette.Ink;
                o.effectDistance = new Vector2(3, -3);
            }

            var textRt = Rect(rt, "Text", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-24, -8));
            var t = textRt.gameObject.AddComponent<Text>();
            t.font = spray ? MarkerFont : Font;
            t.fontSize = fontSize;
            t.fontStyle = spray ? FontStyle.Bold : FontStyle.Normal;
            t.color = Palette.Ink;
            t.alignment = TextAnchor.MiddleLeft;
            t.supportRichText = false;

            var field = rt.gameObject.AddComponent<InputField>();
            field.textComponent = t;
            field.targetGraphic = target;
            field.text = text;
            if (onChange != null) field.onValueChanged.AddListener(v => onChange(v));
            return field;
        }

        /// <summary>Senkrechte Liste mit Scrollbalken. Gibt den Inhalt (mit VerticalLayoutGroup) zurueck.</summary>
        public static RectTransform ScrollList(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, float spacing = 14f)
        {
            var view = Rect(parent, "Scroll", anchorMin, anchorMax, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            view.offsetMin = offsetMin;
            view.offsetMax = offsetMax;
            var viewImg = view.gameObject.AddComponent<Image>();
            viewImg.color = new Color(0, 0, 0, 0.001f);
            view.gameObject.AddComponent<RectMask2D>();

            var content = Rect(view, "Content", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(10, 24, 10, 10);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            return content;
        }

        /// <summary>Waagerechte Zeile innerhalb einer Liste.</summary>
        public static RectTransform Row(Transform parent, float height, float spacing = 12f)
        {
            var rt = Rect(parent, "Row", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0, height));
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            return rt;
        }

        /// <summary>Text-Element fuer Layout-Gruppen (mit bevorzugter Breite).</summary>
        public static Text FlowLabel(Transform parent, string text, int size, Color color, float width, TextAnchor align = TextAnchor.MiddleLeft, bool bold = true)
        {
            var rt = Rect(parent, "FlowLabel", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(width, size * 1.4f));
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleWidth = width <= 0 ? 1 : 0;
            le.preferredHeight = size * 1.4f;
            return Label(rt, text, size, color, align, bold);
        }

        public static Image Swatch(Transform parent, Color color, Action onClick, float size = 56f)
        {
            var btn = Button(parent, "", color, onClick, new Vector2(size, size));
            return btn.GetComponent<Image>();
        }

        public static string Money(long amount) => "$" + amount.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
    }
}
