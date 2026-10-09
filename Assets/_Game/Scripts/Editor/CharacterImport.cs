using UnityEditor;

namespace DriftSkate.EditorTools
{
    /// <summary>Import-Einstellungen fuer Figuren aus Blender (Tools/Blender/build_skater.py).</summary>
    public class CharacterImport : AssetPostprocessor
    {
        // Hochzaehlen, wenn sich die Einstellungen aendern: dann importiert Unity die Figuren neu.
        public override uint GetVersion() => 2;

        void OnPreprocessModel()
        {
            if (!assetPath.Contains("/Characters/")) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            // Generic erzeugt SkinnedMeshRenderer; Animationen kommen per Code (SkaterRig), daher ohne Avatar.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.optimizeGameObjects = false;
            importer.importBlendShapes = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.importNormals = ModelImporterNormals.Import;
        }
    }
}
