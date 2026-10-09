using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DriftSkate
{
    /// <summary>
    /// Haelt in einer ScrollRect-Liste den ausgewaehlten Knopf sichtbar (Gamepad/Tastatur-Navigation):
    /// liegt er ober- oder unterhalb des sichtbaren Bereichs, gleitet die Liste weich dorthin.
    /// </summary>
    [RequireComponent(typeof(ScrollRect))]
    public class ScrollToSelected : MonoBehaviour
    {
        ScrollRect _scroll;
        readonly Vector3[] _corners = new Vector3[4];

        void Awake() => _scroll = GetComponent<ScrollRect>();

        void LateUpdate()
        {
            var es = EventSystem.current;
            if (es == null || _scroll.content == null) return;
            var sel = es.currentSelectedGameObject;
            if (sel == null || !sel.transform.IsChildOf(_scroll.content)) return;
            var viewport = _scroll.viewport != null ? _scroll.viewport : (RectTransform)transform;

            // Ober- und Unterkante des Knopfs im Viewport-Raum
            ((RectTransform)sel.transform).GetWorldCorners(_corners);
            float top = viewport.InverseTransformPoint(_corners[1]).y, bottom = viewport.InverseTransformPoint(_corners[0]).y;
            Rect view = viewport.rect;
            const float margin = 12f;
            float shift = 0f;
            if (top > view.yMax - margin) shift = top - (view.yMax - margin);
            else if (bottom < view.yMin + margin) shift = bottom - (view.yMin + margin);
            if (Mathf.Abs(shift) < 0.5f) return;

            // Inhalt verschieben (positiv = Liste nach unten, damit Oberes sichtbar wird)
            var content = _scroll.content;
            float max = Mathf.Max(0f, content.rect.height - view.height);
            Vector2 p = content.anchoredPosition;
            float target = Mathf.Clamp(p.y - shift, 0f, max);
            p.y = Mathf.Lerp(p.y, target, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            content.anchoredPosition = p;
        }
    }
}
