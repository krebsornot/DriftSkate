using UnityEngine;

namespace DriftSkate
{
    /// <summary>Spielername ueber anderen Spielern, schaut immer zur Kamera.</summary>
    public class NameTag : MonoBehaviour
    {
        public Transform target;
        public float height = 2.4f;
        TextMesh _text;

        public static NameTag Create(Transform parent, string text)
        {
            var go = new GameObject("NameTag");
            go.transform.SetParent(parent, false);
            var tag = go.AddComponent<NameTag>();
            var tm = go.AddComponent<TextMesh>();
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
            tm.fontSize = 48;
            tm.characterSize = 0.05f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.color = Palette.Yellow;
            tag._text = tm;
            tag.SetText(text);
            return tag;
        }

        public void SetText(string text)
        {
            if (_text != null) _text.text = text;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (target != null) transform.position = target.position + Vector3.up * height;
            if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
    }
}
