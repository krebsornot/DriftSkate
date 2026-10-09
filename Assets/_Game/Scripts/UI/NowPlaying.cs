using UnityEngine;
using UnityEngine.UI;

namespace DriftSkate
{
    /// <summary>
    /// Kleine "Now Playing"-Anzeige links oben: Equalizer, der zur Musik ausschlaegt, Interpret und Titel
    /// (lange Titel laufen langsam durch) und ein duenner Fortschrittsbalken. Bei neuem Song gleitet sie kurz neu herein.
    /// Dateiname "Interpret - Titel" wird aufgeteilt.
    /// </summary>
    public class NowPlaying : MonoBehaviour
    {
        const float Width = 430f, Height = 58f, Bars = 5, TextLeft = 66f, TextRight = 52f;

        RectTransform _rt, _titleRt, _fill;
        Text _artist, _title;
        Image _flash;
        readonly RectTransform[] _bars = new RectTransform[(int)Bars];
        readonly float[] _levels = new float[(int)Bars];
        readonly float[] _spectrum = new float[256];
        string _shown;
        float _age = 99f, _scroll, _scrollWait;
        int _scrollDir = 1;

        public static NowPlaying Create(Transform parent, Vector2 pos)
        {
            var rt = UIFactory.Rect(parent, "NowPlaying", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(Width, Height));
            var np = rt.gameObject.AddComponent<NowPlaying>();
            np.Build(rt);
            return np;
        }

        void Build(RectTransform rt)
        {
            _rt = rt;
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = new Color(0.07f, 0.06f, 0.1f, 0.8f);
            bg.raycastTarget = false;

            // Akzentstrich links
            Img(rt, Palette.Pink, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(5f, 0f));

            // Equalizer
            for (int i = 0; i < _bars.Length; i++)
            {
                var b = Img(rt, Color.Lerp(Palette.Cyan, Palette.Pink, i / (Bars - 1f) * 0.6f), Vector2.zero, Vector2.zero, new Vector2(0.5f, 0f),
                            new Vector2(20f + i * 8f, 13f), new Vector2(5f, 4f));
                _bars[i] = b.rectTransform;
            }

            // Interpret klein oben, Titel darunter (in einer Maske, damit er durchlaufen kann)
            _artist = UIFactory.LabelAt(rt, "", 15, Palette.Cyan, new Vector2(0, 1), new Vector2(0, 1), new Vector2(TextLeft, -5f), new Vector2(Width - TextLeft - TextRight, 20f), TextAnchor.MiddleLeft, true);
            _artist.fontStyle = FontStyle.Bold;
            _artist.GetComponent<Outline>().enabled = false;
            var mask = UIFactory.Rect(rt, "TitleMask", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(TextLeft, -23f), new Vector2(Width - TextLeft - TextRight, 28f));
            mask.gameObject.AddComponent<RectMask2D>();
            _titleRt = UIFactory.Rect(mask, "Title", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(600f, 0f));
            _title = UIFactory.Label(_titleRt, "", 23, Palette.White, TextAnchor.MiddleLeft, true, false);
            _title.fontStyle = FontStyle.Bold;
            _title.horizontalOverflow = HorizontalWrapMode.Overflow;

            var key = UIFactory.LabelAt(rt, "[M]", 15, new Color(1f, 1f, 1f, 0.45f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12f, 2f), new Vector2(40f, 20f), TextAnchor.MiddleRight, true);
            key.GetComponent<Outline>().enabled = false;

            // Fortschritt ganz unten
            var track = Img(rt, new Color(1f, 1f, 1f, 0.12f), new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(5f, 0f), new Vector2(-5f, 3f));
            _fill = Img(track.rectTransform, Palette.Pink, Vector2.zero, new Vector2(0, 1), Vector2.zero, Vector2.zero, Vector2.zero).rectTransform;

            // Kurzes Aufleuchten bei neuem Song
            _flash = Img(rt, Palette.Pink, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _flash.color = new Color(1f, 1f, 1f, 0f);
        }

        static Image Img(RectTransform parent, Color color, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var r = UIFactory.Rect(parent, "Img", aMin, aMax, pivot, pos, size);
            var img = r.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        void Update()
        {
            var mp = MusicPlayer.Instance;
            string song = mp != null ? mp.CurrentSong : null;
            bool visible = !string.IsNullOrEmpty(song);
            foreach (Transform c in transform) c.gameObject.SetActive(visible);
            var bg = GetComponent<Image>();
            bg.enabled = visible;
            if (!visible) return;

            float dt = Time.unscaledDeltaTime;
            if (song != _shown) SetSong(song);
            _age += dt;

            // Hereingleiten und sanftes Aufleuchten (ruhig, kein Blinken)
            float slide = 1f - Mathf.Pow(1f - Mathf.Clamp01(_age / 0.6f), 3f);
            _rt.anchoredPosition = new Vector2(Mathf.Lerp(-Width - 40f, 30f, slide), _rt.anchoredPosition.y);
            _flash.color = new Color(Palette.Pink.r, Palette.Pink.g, Palette.Pink.b, Mathf.Clamp01(1f - _age / 1.6f) * 0.35f);

            _fill.anchorMax = new Vector2(mp.Progress, 1f);
            UpdateBars(mp, dt);
            UpdateScroll(dt);
        }

        void SetSong(string song)
        {
            _shown = song;
            _age = 0f;
            _scroll = 0f;
            _scrollWait = 2.5f;
            _scrollDir = 1;
            int dash = song.IndexOf(" - ", System.StringComparison.Ordinal);
            string artist = dash > 0 ? song.Substring(0, dash).Trim() : "RADIO";
            string title = dash > 0 ? song.Substring(dash + 3).Trim() : song;
            _artist.text = "♪  " + artist.ToUpperInvariant();
            _title.text = title;
            _titleRt.sizeDelta = new Vector2(Mathf.Max(100f, _title.preferredWidth + 10f), 0f);
            _titleRt.anchoredPosition = Vector2.zero;
        }

        /// <summary>Equalizer aus dem echten Frequenzspektrum, weich geglaettet.</summary>
        void UpdateBars(MusicPlayer mp, float dt)
        {
            bool playing = mp.IsPlaying && mp.Source != null;
            if (playing) mp.Source.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);
            int[] edges = { 1, 4, 10, 24, 60, 150 };
            for (int i = 0; i < _bars.Length; i++)
            {
                float v = 0f;
                if (playing)
                {
                    for (int k = edges[i]; k < edges[i + 1]; k++) v = Mathf.Max(v, _spectrum[k]);
                    v = Mathf.Clamp01(Mathf.Sqrt(v * (1.5f + i * 1.8f)) * 1.4f);
                }
                float rate = v > _levels[i] ? 14f : 4f;
                _levels[i] = Mathf.Lerp(_levels[i], v, 1f - Mathf.Exp(-rate * dt));
                _bars[i].sizeDelta = new Vector2(5f, Mathf.Lerp(3f, 32f, _levels[i]));
            }
        }

        /// <summary>Zu lange Titel langsam hin und her laufen lassen, mit Pause an den Enden.</summary>
        void UpdateScroll(float dt)
        {
            float overflow = _titleRt.sizeDelta.x - (Width - TextLeft - TextRight);
            if (overflow <= 0f) { _titleRt.anchoredPosition = Vector2.zero; return; }
            if (_scrollWait > 0f) { _scrollWait -= dt; return; }
            _scroll += _scrollDir * 28f * dt;
            if (_scroll >= overflow || _scroll <= 0f)
            {
                _scroll = Mathf.Clamp(_scroll, 0f, overflow);
                _scrollDir = -_scrollDir;
                _scrollWait = 2f;
            }
            _titleRt.anchoredPosition = new Vector2(-_scroll, 0f);
        }
    }
}
