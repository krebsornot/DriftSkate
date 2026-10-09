using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using GLTFast.Schema;
using UnityEngine;
using Material = UnityEngine.Material;

namespace DriftSkate
{
    /// <summary>Ein eigener Charakter oder ein eigenes Board aus dem Mods-Ordner.</summary>
    public class ModInfo
    {
        public string id, name, author, file, folder;
        public bool isBoard;
        public float height = 1.8f;
        public float speed = 1f, pop = 1f, balance = 1f;
        public string[] bones;
        public bool loading, loaded;
        public string error;
        public GameObject template;
        internal GltfImport import;

        public string Status => error != null ? "Fehler: " + error : loaded ? "bereit" : loading ? "laedt ..." : "wartet";
    }

    /// <summary>Optionale Beschreibung in mod.json neben der GLB-Datei.</summary>
    [Serializable]
    public class ModJson
    {
        public string name;
        public string author;
        public string file;
        public float height;               // Koerpergroesse in Metern (Charakter), Standard 1.8
        public float speed = 1f, pop = 1f, balance = 1f; // Board-Werte
        public string[] bones;             // eigene Knochen-Zuordnung, z. B. "Hips=pelvis"
    }

    /// <summary>
    /// Laedt eigene Charaktere und Boards (GLB/glTF) zur Laufzeit aus
    /// &lt;Spielordner&gt;/Mods/Skaters, Mods/Boards und dem gleichen Pfad im Spielstand-Ordner.
    /// </summary>
    public static class ModLibrary
    {
        public static readonly List<ModInfo> Characters = new List<ModInfo>();
        public static readonly List<ModInfo> Boards = new List<ModInfo>();

        /// <summary>Wird ausgeloest, wenn ein Mod fertig geladen (oder fehlgeschlagen) ist.</summary>
        public static event Action Changed;

        static bool _scanned;

        /// <summary>Mods-Ordner neben der Exe (im Editor: Projektordner).</summary>
        public static string GameModsFolder => Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Mods");
        public static string UserModsFolder => Path.Combine(Application.persistentDataPath, "Mods");

        public static void EnsureScanned()
        {
            if (!_scanned) Rescan();
        }

        public static void Rescan()
        {
            _scanned = true;
            foreach (var m in Characters) Unload(m);
            foreach (var m in Boards) Unload(m);
            Characters.Clear();
            Boards.Clear();
            foreach (var root in new[] { GameModsFolder, UserModsFolder })
            {
                Collect(Path.Combine(root, "Skaters"), false);
                Collect(Path.Combine(root, "Boards"), true);
            }
            foreach (var m in Characters) _ = LoadAsync(m);
            foreach (var m in Boards) _ = LoadAsync(m);
            Changed?.Invoke();
        }

        static void Unload(ModInfo m)
        {
            if (m.template != null) UnityEngine.Object.Destroy(m.template);
            m.import?.Dispose();
        }

        static void Collect(string dir, bool boards)
        {
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception)
            {
                return;
            }
            var list = boards ? Boards : Characters;
            // Einzelne Dateien direkt im Ordner
            foreach (var f in Directory.GetFiles(dir))
                if (IsModel(f)) Add(list, f, Path.GetFileNameWithoutExtension(f), null, boards);
            // Unterordner mit Modell und optional mod.json
            foreach (var sub in Directory.GetDirectories(dir))
            {
                ModJson json = null;
                string jsonPath = Path.Combine(sub, "mod.json");
                if (File.Exists(jsonPath))
                {
                    try { json = JsonUtility.FromJson<ModJson>(File.ReadAllText(jsonPath)); }
                    catch (Exception e) { Debug.LogWarning("mod.json fehlerhaft: " + jsonPath + " (" + e.Message + ")"); }
                }
                string model = json != null && !string.IsNullOrEmpty(json.file) ? Path.Combine(sub, json.file) : null;
                if (model == null || !File.Exists(model))
                {
                    model = null;
                    foreach (var f in Directory.GetFiles(sub))
                        if (IsModel(f)) { model = f; break; }
                }
                if (model != null) Add(list, model, Path.GetFileName(sub), json, boards);
            }
        }

        static bool IsModel(string f)
        {
            string ext = Path.GetExtension(f).ToLowerInvariant();
            return ext == ".glb" || ext == ".gltf";
        }

        static void Add(List<ModInfo> list, string file, string folderName, ModJson json, bool board)
        {
            string id = "mod:" + (board ? "board/" : "skater/") + folderName;
            if (list.Exists(m => m.id == id)) return;
            var info = new ModInfo
            {
                id = id,
                name = json != null && !string.IsNullOrEmpty(json.name) ? json.name : folderName,
                author = json?.author,
                file = file,
                folder = Path.GetDirectoryName(file),
                isBoard = board,
                bones = json?.bones,
            };
            if (json != null)
            {
                if (json.height > 0.5f) info.height = json.height;
                info.speed = json.speed > 0f ? json.speed : 1f;
                info.pop = json.pop > 0f ? json.pop : 1f;
                info.balance = json.balance > 0f ? json.balance : 1f;
            }
            list.Add(info);
        }

        static async Task LoadAsync(ModInfo m)
        {
            m.loading = true;
            try
            {
                var import = new GltfImport(null, null, new ToonMaterialGenerator(), null);
                bool ok = await import.LoadFile(m.file, new Uri(m.file));
                if (!ok)
                {
                    m.error = "Datei konnte nicht gelesen werden";
                    import.Dispose();
                    return;
                }
                var holder = new GameObject("Mod_" + m.name);
                holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(holder);
                ok = await import.InstantiateMainSceneAsync(holder.transform);
                if (!ok)
                {
                    m.error = "Szene konnte nicht erzeugt werden";
                    UnityEngine.Object.Destroy(holder);
                    import.Dispose();
                    return;
                }
                if (!m.isBoard)
                {
                    SkeletonMap.Resolve(holder.transform, m.bones, out string missing);
                    if (missing != null) m.error = "Knochen nicht gefunden: " + missing;
                }
                if (holder.GetComponentsInChildren<Renderer>(true).Length == 0) m.error = "kein sichtbares Mesh";
                m.template = holder;
                m.import = import;
                m.loaded = m.error == null;
            }
            catch (Exception e)
            {
                m.error = e.Message;
                Debug.LogWarning("Mod konnte nicht geladen werden: " + m.file + "\n" + e);
            }
            finally
            {
                m.loading = false;
                Changed?.Invoke();
            }
        }

        public static ModInfo Character(string id) => string.IsNullOrEmpty(id) ? null : Characters.Find(m => m.id == id);
        public static ModInfo Board(string id) => string.IsNullOrEmpty(id) ? null : Boards.Find(m => m.id == id);

        /// <summary>Board-Werte fuer ein Mod-Board (immer gratis).</summary>
        public static BoardDef BoardDef(string id)
        {
            var m = Board(id);
            if (m == null) return null;
            return new BoardDef
            {
                id = m.id, name = m.name, blurb = "Eigenes Board" + (m.author != null ? " von " + m.author : ""),
                price = 0, deck = Palette.White, wheels = Palette.White,
                speed = m.speed, pop = m.pop, balance = m.balance, isMod = true
            };
        }

        public static bool IsMod(string id) => id != null && id.StartsWith("mod:");
    }

    /// <summary>Wandelt glTF-Materialien in Toon-Materialien um (Farbe, Textur, Alpha, doppelseitig).</summary>
    public class ToonMaterialGenerator : IMaterialGenerator
    {
        public Material GetDefaultMaterial(bool pointsSupport = false) => ToonMaterials.Get(Palette.Concrete, 0.25f, false);

        public Material GenerateMaterial(MaterialBase gltfMaterial, IGltfReadable gltf, bool pointsSupport = false)
        {
            var mat = new Material(ToonMaterials.Shader) { name = string.IsNullOrEmpty(gltfMaterial.name) ? "ModMaterial" : gltfMaterial.name };
            var pbr = gltfMaterial.PbrMetallicRoughness;
            Color baseColor = pbr != null ? pbr.BaseColor : Color.white;
            mat.SetColor("_BaseColor", baseColor.gamma);
            var texInfo = pbr?.BaseColorTexture;
            if (texInfo != null && texInfo.index >= 0)
            {
                var tex = gltf.GetTexture(texInfo.index);
                if (tex != null)
                {
                    mat.SetTexture("_BaseMap", tex);
                    if (gltf.IsTextureYFlipped(texInfo.index))
                    {
                        mat.SetTextureScale("_BaseMap", new Vector2(1f, -1f));
                        mat.SetTextureOffset("_BaseMap", new Vector2(0f, 1f));
                    }
                }
            }
            var alpha = gltfMaterial.GetAlphaMode();
            if (alpha == MaterialBase.AlphaMode.Mask) mat.SetFloat("_Cutoff", Mathf.Max(0.01f, gltfMaterial.alphaCutoff));
            else if (alpha == MaterialBase.AlphaMode.Blend) mat.SetFloat("_Cutoff", 0.5f);
            mat.SetFloat("_OutlineMode", 0f);
            mat.SetFloat("_OutlineWidth", 0.25f);
            if (gltfMaterial.doubleSided)
            {
                mat.SetFloat("_Cull", 0f);
                mat.SetShaderPassEnabled("SRPDefaultUnlit", false); // keine Outline an duennen Flaechen
            }
            return mat;
        }

        public void SetLogger(ICodeLogger logger) { }
    }
}
