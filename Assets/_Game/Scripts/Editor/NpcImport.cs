using UnityEditor;
using UnityEngine;

namespace DriftSkate.EditorTools
{
    /// <summary>
    /// Import der Stoner-NPCs und der Gassen-Crew aus Tools/Blender/build_stoners.py (Resources/Characters/NPC_*.fbx):
    /// Figuren als Humanoid (eigene und Mixamo-Animationen passen), Takes heissen wie die NLA-Spuren in Blender.
    /// Die Wirbelsaeule hat wie bei Mixamo drei Glieder (Spine, Spine1, Spine2), sonst ordnet Unity die Brust nicht zu.
    /// Idle und Vibe laufen in Schleife; die Pose bleibt, wie sie in Blender gebaut ist (Sitzhoehe, Blickrichtung).
    /// </summary>
    public class NpcImport : AssetPostprocessor
    {
        static bool IsNpc(string path)
        {
            path = path.Replace('\\', '/');
            return path.Contains("/Resources/Characters/NPC_") && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);
        }

        static bool IsProp(string path) => path.Contains("NPC_Couch") || path.Contains("NPC_Crate");

        void OnPreprocessModel()
        {
            if (!IsNpc(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            if (mi.importBlendShapes) mi.importBlendShapes = false;
            if (mi.importCameras) mi.importCameras = false;
            if (mi.importLights) mi.importLights = false;
            if (mi.importVisibility) mi.importVisibility = false;
            if (IsProp(assetPath))
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
                mi.isReadable = true; // StonerNpc liest die Punkte, um die Lehne zu finden
                return;
            }
            // Nur setzen, was noch anders ist (Neuimporte aendern so nichts an der gespeicherten Knochen-Zuordnung)
            if (mi.animationType != ModelImporterAnimationType.Human) mi.animationType = ModelImporterAnimationType.Human;
            if (mi.avatarSetup == ModelImporterAvatarSetup.NoAvatar) mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            if (!mi.importAnimation) mi.importAnimation = true;
            if (mi.optimizeGameObjects) mi.optimizeGameObjects = false;
        }

        void OnPreprocessAnimation()
        {
            if (!IsNpc(assetPath) || IsProp(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            // Schon eingerichtet (gleiche Takes): nichts aendern
            var current = mi.clipAnimations;
            if (current.Length == clips.Length && System.Array.TrueForAll(current, c => c.name == ClipName(c.takeName) && c.loopTime == ShouldLoop(c.name) &&
                System.Array.Exists(clips, d => d.takeName == c.takeName))) return;
            foreach (var c in clips)
            {
                c.name = ClipName(c.takeName);
                c.loopTime = ShouldLoop(c.name);
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
                c.heightFromFeet = false;
            }
            mi.clipAnimations = clips;
        }

        bool ShouldLoop(string name) => name == "Idle" || name == "Vibe" ||
            (assetPath.EndsWith("NPC_Miru.fbx", System.StringComparison.OrdinalIgnoreCase) && name == "Sitting");

        /// <summary>"Jojo_Rig|Idle" -> "Idle"</summary>
        public static string ClipName(string take)
        {
            int bar = take.LastIndexOf('|');
            return bar >= 0 ? take.Substring(bar + 1) : take;
        }
    }
}
