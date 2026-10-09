using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace DriftSkate
{
    /// <summary>
    /// In-Game-Radio: spielt eigene Musik (MP3, OGG, WAV) aus dem Musikordner im Spielstand-Ordner
    /// und aus StreamingAssets/Music. Taste M / Steuerkreuz rechts = naechster Song.
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        public static MusicPlayer Instance { get; private set; }

        readonly List<string> _files = new List<string>();
        AudioSource _src;
        int _index = -1;
        bool _loading, _wantPlay;

        public string CurrentSong { get; private set; }
        public int SongCount => _files.Count;
        public bool IsPlaying => _src != null && _src.isPlaying;
        /// <summary>0..1 Fortschritt im aktuellen Song (0, wenn die Laenge unbekannt ist).</summary>
        public float Progress => _src != null && _src.clip != null && _src.clip.length > 0f ? Mathf.Clamp01(_src.time / _src.clip.length) : 0f;
        public AudioSource Source => _src;

        public static void Ensure()
        {
            if (Instance != null) return;
            var go = new GameObject("MusicPlayer");
            DontDestroyOnLoad(go);
            go.AddComponent<MusicPlayer>();
        }

        void Awake()
        {
            Instance = this;
            _src = gameObject.AddComponent<AudioSource>();
            _src.spatialBlend = 0f;
            _src.loop = false;
            Rescan();
            if (_files.Count > 0)
            {
                _index = UnityEngine.Random.Range(0, _files.Count) - 1;
                Next();
            }
        }

        public void Rescan()
        {
            _files.Clear();
            try
            {
                Directory.CreateDirectory(SaveSystem.MusicFolder);
                string readme = Path.Combine(SaveSystem.MusicFolder, "HIER_MUSIK_REINLEGEN.txt");
                if (!File.Exists(readme))
                    File.WriteAllText(readme, "Lege hier MP3-, OGG- oder WAV-Dateien ab. Sie laufen im Spiel als Radio.\nNaechster Song: Taste M oder Steuerkreuz rechts.\n");
            }
            catch (Exception e)
            {
                Debug.LogWarning("Musikordner nicht verfuegbar: " + e.Message);
            }
            Collect(SaveSystem.MusicFolder);
            Collect(Path.Combine(Application.streamingAssetsPath, "Music"));
            _files.Sort(StringComparer.OrdinalIgnoreCase);
        }

        void Collect(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".mp3" || ext == ".ogg" || ext == ".wav") _files.Add(f);
            }
        }

        public void Next()
        {
            if (_files.Count == 0)
            {
                Rescan();
                if (_files.Count == 0)
                {
                    HUD.Instance?.Toast("Keine Musik gefunden. Ordner: " + SaveSystem.MusicFolder, 5f);
                    return;
                }
            }
            if (_loading) return;
            _index = (_index + 1) % _files.Count;
            StartCoroutine(Load(_files[_index]));
        }

        IEnumerator Load(string path)
        {
            _loading = true;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            AudioType type = ext == ".mp3" ? AudioType.MPEG : ext == ".ogg" ? AudioType.OGGVORBIS : AudioType.WAV;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type))
            {
                ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = true;
                yield return req.SendWebRequest();
                _loading = false;
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("Song konnte nicht geladen werden: " + path + " (" + req.error + ")");
                    yield break;
                }
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (_src.clip != null) Destroy(_src.clip);
                _src.clip = clip;
                _src.Play();
                _wantPlay = true;
                CurrentSong = Path.GetFileNameWithoutExtension(path);
                HUD.Instance?.ShowSong(CurrentSong);
            }
        }

        void Update()
        {
            _src.volume = SaveSystem.Profile.musicVolume;
            if (_wantPlay && !_loading && !_src.isPlaying && Application.isFocused) Next();
        }
    }
}
