using UnityEditor;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Materialien fuer Wasser und Hydranten-Fontaene als Assets unter Resources (damit die Shader in den Build kommen
    /// und zur Laufzeit per Resources.Load da sind). Menue DriftSkate -> Wasser -> Materialien anlegen; der Projektbau
    /// ruft es ebenfalls auf. Batch: -executeMethod DriftSkate.EditorTools.WaterAssets.CreateMaterials
    /// </summary>
    public static class WaterAssets
    {
        const string WaterPath = "Assets/_Game/Resources/WaterMat.mat", GeyserPath = "Assets/_Game/Resources/GeyserMat.mat";

        [MenuItem("DriftSkate/Wasser/Materialien anlegen")]
        public static void CreateMaterials()
        {
            var water = Shader.Find("DriftSkate/Water");
            var geyser = Shader.Find("DriftSkate/Geyser");
            if (water == null || geyser == null) { Debug.LogError("[DriftSkate] Wasser-Shader nicht gefunden (Water.shader / Geyser.shader)"); return; }
            Save(WaterPath, "WaterMat", water, m =>
            {
                m.SetColor("_DeepColor", Palette.Hex("1A6E9C"));
                m.SetColor("_ShallowColor", Palette.Hex("2DB4D6"));
                m.SetColor("_CrestColor", Palette.Hex("8FE6F5"));
                m.SetColor("_FoamColor", Palette.White);
                m.SetColor("_ShadowTint", new Color(0.58f, 0.52f, 0.78f, 1f));
                m.SetFloat("_WaveAmp", 1f);
                m.SetFloat("_RippleOn", 1f);
                m.SetFloat("_Opacity", 0.84f);
                m.SetFloat("_FoamDepth", 1.4f);
            });
            Save(GeyserPath, "GeyserMat", geyser, m =>
            {
                m.SetColor("_Color", new Color(0.55f, 0.9f, 1f, 0.8f));
                m.SetColor("_StreakColor", Color.white);
                m.SetFloat("_Sway", 0.12f);
                m.SetFloat("_Brightness", 1.3f);
            });
            AssetDatabase.SaveAssets();
            Debug.Log("[DriftSkate] Wasser-Materialien angelegt: " + WaterPath + ", " + GeyserPath);
        }

        static void Save(string path, string name, Shader shader, System.Action<Material> setup)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                setup(mat);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
                setup(mat);
                EditorUtility.SetDirty(mat);
            }
        }
    }
}
