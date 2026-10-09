using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DriftSkate
{
    public enum GraffitiTool { Brush, Spray, Eraser }

    /// <summary>Malflaeche fuer das eigene Graffiti. Malt direkt in die gemeinsame Graffiti-Textur.</summary>
    [RequireComponent(typeof(RawImage))]
    public class GraffitiEditor : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public GraffitiTool tool = GraffitiTool.Brush;
        public Color color = Palette.Pink;
        public float size = 8f;
        public bool Dirty { get; private set; }

        RawImage _image;
        Texture2D _tex;
        Vector2 _last;
        bool _painting;

        void Awake()
        {
            _image = GetComponent<RawImage>();
            _tex = SaveSystem.Graffiti;
            _image.texture = _tex;
        }

        bool ToPixel(PointerEventData e, out Vector2 pixel)
        {
            var rt = (RectTransform)transform;
            pixel = Vector2.zero;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out Vector2 local)) return false;
            Rect r = rt.rect;
            Vector2 uv = new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
            pixel = new Vector2(uv.x * _tex.width, uv.y * _tex.height);
            return true;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (!ToPixel(e, out Vector2 p)) return;
            _painting = true;
            _last = p;
            Paint(p, p);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_painting || !ToPixel(e, out Vector2 p)) return;
            Paint(_last, p);
            _last = p;
        }

        public void OnPointerUp(PointerEventData e) => _painting = false;

        void Paint(Vector2 a, Vector2 b)
        {
            switch (tool)
            {
                case GraffitiTool.Brush: GraffitiPainter.Line(_tex, a, b, size, color); break;
                case GraffitiTool.Eraser: GraffitiPainter.Line(_tex, a, b, size * 1.5f, color, true); break;
                case GraffitiTool.Spray:
                    float dist = Vector2.Distance(a, b);
                    int steps = Mathf.Max(1, Mathf.CeilToInt(dist / Mathf.Max(1f, size * 0.5f)));
                    for (int i = 0; i <= steps; i++) GraffitiPainter.Spray(_tex, Vector2.Lerp(a, b, i / (float)steps), size * 2f, color, 14);
                    break;
            }
            _tex.Apply();
            Dirty = true;
        }

        public void Clear()
        {
            GraffitiPainter.Clear(_tex);
            Dirty = true;
        }

        public void ResetToDefault()
        {
            GraffitiPainter.DrawDefaultTag(_tex);
            Dirty = true;
        }

        public void Save()
        {
            SaveSystem.SaveGraffiti();
            Dirty = false;
        }
    }
}
