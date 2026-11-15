using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Threefold.EditorTools
{
    /// <summary>
    /// Import settings for character models under Assets/Threefold/Art/Characters. They are applied
    /// on every import, so a model only has to be added to the folder: no Inspector work is needed.
    ///
    /// Models are imported as Humanoid, so animations can be shared between characters. Clips are
    /// renamed to the part after the last '|' (Blender writes "Armature|Idle"), loops are marked
    /// (the movement loops by name, and every clip whose name ends in "_Loop", as the animation
    /// library names its loops), and root motion is baked into the pose, because the
    /// CharacterController moves the character.
    ///
    /// After import, smoothed normals are stored in every mesh's tangents, for the contour line of
    /// the visual style (see <see cref="OutlineNormals"/>). Character materials use no normal maps,
    /// so nothing else reads the tangents.
    /// </summary>
    internal sealed class CharacterModelPostprocessor : AssetPostprocessor
    {
        internal const string CharacterFolder = "Assets/Threefold/Art/Characters/";
        internal const string MageModelPath = CharacterFolder + "Mage/Mage.fbx";

        /// <summary>
        /// The Mage's Wayfarer look: a mesh on Mage.fbx's skeleton, with a few extra bones and no clips of its
        /// own (Tools/blender/build_mage_wayfarer.py). MageCharacterSetup uses it when it imports as the same Humanoid.
        /// </summary>
        internal const string MageWayfarerModelPath = CharacterFolder + "Mage/MageWayfarer.fbx";

        /// <summary>The painted atlas of the Wayfarer look, written by the same script.</summary>
        internal const string MagePaintTexturePath = CharacterFolder + "Mage/MageWayfarer_Paint.png";

        /// <summary>Painted character textures are atlases of this size at most; the tiles' margins are sized for it.</summary>
        internal const int CharacterTextureSize = 1024;

        private static readonly HashSet<string> LoopingClips = new HashSet<string>
        {
            "Idle", "Walk", "Jog", "Sprint", "JumpLoop", "Hang",

            // A combat stance whose library name does not say it loops.
            "Sword_Idle",
        };

        private const string LoopSuffix = "_Loop";

        // Changing this number makes Unity re-import the characters with the new settings.
        // 4: the combat clips (Docs/CombatAnimationArchitecture.md).
        public override uint GetVersion()
        {
            return 4;
        }

        /// <summary>
        /// True if a model's tangents hold smoothed normals for outlines, which is the case for
        /// every character model imported with this version of the settings.
        /// </summary>
        internal static bool StoresOutlineNormals(string modelPath)
        {
            return !string.IsNullOrEmpty(modelPath) && IsCharacterModel(modelPath);
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
            return !string.IsNullOrEmpty(clipName) &&
                   (LoopingClips.Contains(clipName) || clipName.EndsWith(LoopSuffix, System.StringComparison.Ordinal));
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

        /// <summary>True for a painted texture of a character: an image under the character folder.</summary>
        internal static bool IsCharacterTexture(string path)
        {
            return !string.IsNullOrEmpty(path) && path.StartsWith(CharacterFolder) &&
                   path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Painted character atlases: colour, opaque, mip-mapped (distant characters must not shimmer), clamped so
        /// the atlas edge never wraps round, trilinear, and no larger than the atlas was painted for.
        /// </summary>
        private void OnPreprocessTexture()
        {
            if (!IsCharacterTexture(assetPath) || !(assetImporter is TextureImporter importer))
            {
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.maxTextureSize = CharacterTextureSize;
            importer.isReadable = false;
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!IsCharacterModel(assetPath))
            {
                return;
            }

            var done = new HashSet<Mesh>();
            foreach (SkinnedMeshRenderer skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                StoreOutlineNormals(skinned.sharedMesh, done);
            }

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                StoreOutlineNormals(filter.sharedMesh, done);
            }
        }

        private static void StoreOutlineNormals(Mesh mesh, HashSet<Mesh> done)
        {
            if (mesh == null || !done.Add(mesh))
            {
                return;
            }

            Vector3[] positions = mesh.vertices;
            Vector3[] normals = mesh.normals;
            if (positions.Length == 0 || normals.Length != positions.Length)
            {
                return;
            }

            Vector3[] smooth = OutlineNormals.Smooth(positions, normals);
            var tangents = new Vector4[smooth.Length];
            for (int i = 0; i < smooth.Length; i++)
            {
                tangents[i] = new Vector4(smooth[i].x, smooth[i].y, smooth[i].z, 1f);
            }

            mesh.tangents = tangents;
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
