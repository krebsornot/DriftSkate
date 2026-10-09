using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>Wandflaeche, auf die man sein Graffiti spruehen kann (Taste T / Steuerkreuz hoch).</summary>
    public class TagSpot : MonoBehaviour
    {
        static readonly List<TagSpot> All = new List<TagSpot>();

        public int Index { get; private set; }
        public bool TaggedByMe { get; private set; }
        /// <summary>Welches Graffiti hier gerade zu sehen ist (eigenes oder das eines anderen Spielers; null = Standard-Tag).</summary>
        public Texture ShownGraffiti { get; private set; }

        public Renderer frame;     // Rahmen, der vor dem Spruehen leuchtet
        public Renderer surface;   // Flaeche, auf die das Graffiti kommt

        Vector3 _frameScale;

        void OnEnable()
        {
            All.Add(this);
            All.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            for (int i = 0; i < All.Count; i++) All[i].Index = i;
        }

        void OnDisable() => All.Remove(this);

        public static TagSpot FindNear(Vector3 pos, float radius)
        {
            TagSpot best = null;
            float bestD = radius * radius;
            foreach (var s in All)
            {
                float d = (s.transform.position - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        public static TagSpot ByIndex(int i) => i >= 0 && i < All.Count ? All[i] : null;

        public static int TaggedCount()
        {
            int n = 0;
            foreach (var s in All) if (s.TaggedByMe) n++;
            return n;
        }

        public static int Count => All.Count;

        public void ApplyLocal(Texture graffiti)
        {
            TaggedByMe = true;
            ShownGraffiti = graffiti;
            if (surface != null)
            {
                surface.enabled = true;
                surface.sharedMaterial = ToonMaterials.CreateDecal(graffiti);
            }
            if (frame != null) frame.gameObject.SetActive(false);
        }

        /// <summary>Tag eines anderen Spielers: sein Graffiti (custom), sonst Standard-Tag in seiner Crew-Farbe.</summary>
        public void ApplyRemote(Color crew, Material custom = null, Texture graffiti = null)
        {
            if (TaggedByMe) return;
            ShownGraffiti = custom != null ? graffiti : null;
            if (surface != null && custom != null)
            {
                surface.enabled = true;
                surface.sharedMaterial = custom;
            }
            else if (surface != null)
            {
                surface.enabled = true;
                var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                GraffitiPainter.DrawDefaultTag(tex);
                var mat = ToonMaterials.CreateDecal(tex);
                mat.SetColor("_BaseColor", Color.Lerp(Color.white, crew, 0.6f));
                surface.sharedMaterial = mat;
            }
        }

        void Update()
        {
            if (frame != null && frame.gameObject.activeSelf)
            {
                if (_frameScale == Vector3.zero) _frameScale = frame.transform.localScale;
                float pulse = 1f + (0.5f + 0.5f * Mathf.Sin(Time.time * 4f + Index)) * 0.04f;
                frame.transform.localScale = new Vector3(_frameScale.x * pulse, _frameScale.y * pulse, _frameScale.z);
            }
        }
    }
}
