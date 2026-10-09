using System;
using System.Collections.Generic;
using UnityEngine;

namespace DriftSkate
{
    /// <summary>
    /// Liefert Toon-Materialien pro Farbe (gecacht). Im Editor-Builder haengt sich ein Hook ein,
    /// der die Materialien als Assets speichert, damit gebaute Szenen sie behalten.
    /// </summary>
    public static class ToonMaterials
    {
        public const string ShaderName = "DriftSkate/Toon";

        /// <summary>Editor-Hook: (Material, Schluessel) -> gespeichertes Material.</summary>
        public static Func<Material, string, Material> PersistHook;

        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        static Shader _shader;

        public static Shader Shader
        {
            get
            {
                if (_shader == null) _shader = Shader.Find(ShaderName);
                if (_shader == null)
                {
                    var reference = Resources.Load<Material>("ToonShaderRef");
                    if (reference != null) _shader = reference.shader;
                }
                return _shader;
            }
        }

        public static Material Get(Color color, float outline = 0.35f, bool boxOutline = true, float emission = 0f, Surface surface = Surface.None)
        {
            string key = ColorUtility.ToHtmlStringRGB(color) + "_o" + Mathf.RoundToInt(outline * 100) +
                         (boxOutline ? "b" : "n") + "_e" + Mathf.RoundToInt(emission * 10) +
                         (surface != Surface.None ? "_" + surface : "");
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var mat = new Material(Shader) { name = "Toon_" + key, enableInstancing = true };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_OutlineWidth", outline);
            mat.SetFloat("_OutlineMode", boxOutline ? 1f : 0f);
            mat.SetFloat("_Emission", emission);
            if (outline <= 0f) mat.SetShaderPassEnabled("SRPDefaultUnlit", false);
            if (surface != Surface.None)
            {
                var look = SurfaceTextures.For(surface);
                mat.SetTexture("_DetailMap", SurfaceTextures.Get(look.texture));
                mat.SetFloat("_DetailScale", look.scale);
                mat.SetFloat("_DetailStrength", look.strength);
                mat.SetVector("_DetailScroll", look.scroll);
            }

            if (PersistHook != null) mat = PersistHook(mat, key);
            Cache[key] = mat;
            return mat;
        }

        /// <summary>
        /// Material mit Farb-Textur ueber die Mesh-UVs (Fenster, Schaufenster, Tueren), ohne Outline.
        /// glow: Pixel mit Alpha unter 1 leuchten (erleuchtete Fenster am Abend).
        /// </summary>
        public static Material GetTextured(string textureKey, Color tint, float emission = 0f, float glow = 0f)
        {
            string key = "tex_" + textureKey + "_" + ColorUtility.ToHtmlStringRGB(tint) + "_e" + Mathf.RoundToInt(emission * 10) +
                         (glow > 0f ? "_g" + Mathf.RoundToInt(glow * 10) : "");
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var mat = new Material(Shader) { name = "Toon_" + key, enableInstancing = true };
            mat.SetTexture("_BaseMap", SurfaceTextures.Get(textureKey));
            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_OutlineWidth", 0f);
            mat.SetFloat("_Emission", emission);
            mat.SetFloat("_GlowMask", glow);
            mat.SetShaderPassEnabled("SRPDefaultUnlit", false);

            if (PersistHook != null) mat = PersistHook(mat, key);
            Cache[key] = mat;
            return mat;
        }

        /// <summary>Aufkleber mit Alpha-Cutout aus einer erzeugten Textur (z. B. Schild-Schrift), gecacht und speicherbar.</summary>
        public static Material GetDecal(string textureKey, float emission = 0f)
        {
            string key = "decal_" + textureKey + "_e" + Mathf.RoundToInt(emission * 10);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var mat = CreateDecal(SurfaceTextures.Get(textureKey));
            mat.name = "Toon_" + key;
            mat.SetFloat("_Emission", emission);

            if (PersistHook != null) mat = PersistHook(mat, key);
            Cache[key] = mat;
            return mat;
        }

        /// <summary>Material mit Textur und Alpha-Cutout, z. B. fuer Graffiti-Aufkleber. Nicht gecacht.</summary>
        public static Material CreateDecal(Texture texture)
        {
            var mat = new Material(Shader) { name = "Toon_Decal" };
            mat.SetTexture("_BaseMap", texture);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.SetFloat("_OutlineWidth", 0f);
            mat.SetFloat("_RimStrength", 0f);
            mat.SetShaderPassEnabled("SRPDefaultUnlit", false);
            mat.SetShaderPassEnabled("ShadowCaster", false);
            return mat;
        }

        public static void ClearCache() => Cache.Clear();
    }

    /// <summary>Speicher-Hook fuer prozedurale Meshes (Rampen, Quarterpipes) im Editor.</summary>
    public static class MeshStore
    {
        public static Func<Mesh, string, Mesh> PersistHook;

        public static Mesh Persist(Mesh mesh, string key)
        {
            return PersistHook != null ? PersistHook(mesh, key) : mesh;
        }
    }

    /// <summary>Speicher-Hook fuer prozedurale Texturen (Oberflaechen, Fenster) im Editor: (Textur, Schluessel, linear) -> Asset.</summary>
    public static class TextureStore
    {
        public static Func<Texture2D, string, bool, Texture2D> PersistHook;

        public static Texture2D Persist(Texture2D tex, string key, bool linear)
        {
            return PersistHook != null ? PersistHook(tex, key, linear) : tex;
        }
    }
}
