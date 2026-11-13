using System.Collections.Generic;
using UnityEditor;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Import settings for character models under Assets/Threefold/Art/Characters. They are applied
    /// on every import, so a model only has to be added to the folder: no Inspector work is needed.
    ///
    /// Models are imported as Humanoid, so animations can be shared between characters. Clips are
    /// renamed to the part after the last '|' (Blender writes "Armature|Idle"), loops are marked,
    /// and root motion is baked into the pose, because the CharacterController moves the character.
    /// </summary>
    internal sealed class CharacterModelPostprocessor : AssetPostprocessor
    {
        internal const string CharacterFolder = "Assets/Threefold/Art/Characters/";
        internal const string MageModelPath = CharacterFolder + "Mage/Mage.fbx";

        private static readonly HashSet<string> LoopingClips = new HashSet<string>
        {
            "Idle", "Walk", "Jog", "Sprint", "JumpLoop", "Hang",
        };

        // Changing this number makes Unity re-import the characters with the new settings.
        public override uint GetVersion()
        {
            return 2;
        }

        internal static bool IsCharacterModel(string path)
        {
            return path.StartsWith(CharacterFolder) && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The clip name a take is given: the text after the last '|'.</summary>
        internal static string CleanClipName(string takeName)
        {
            if (string.IsNullOrEmpty(takeName))
            {
                return takeName;
            }

            int separator = takeName.LastIndexOf('|');
            return separator >= 0 ? takeName.Substring(separator + 1) : takeName;
        }

        internal static bool IsLooping(string clipName)
        {
            return LoopingClips.Contains(clipName);
        }

        private void OnPreprocessModel()
        {
            if (!IsCharacterModel(assetPath) || !(assetImporter is ModelImporter importer))
            {
                return;
            }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.autoGenerateAvatarMappingIfUnspecified = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsCharacterModel(assetPath) || !(assetImporter is ModelImporter importer))
            {
                return;
            }

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                ModelImporterClipAnimation clip = clips[i];
                clip.name = CleanClipName(clip.takeName);
                clip.loopTime = IsLooping(clip.name);

                // In-place animations: keep the body where the animation puts it.
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }

            importer.clipAnimations = clips;
        }
    }
}
