using NUnit.Framework;
using Threefold.EditorTools;

namespace Threefold.Tests
{
    /// <summary>The naming rules that connect the exported character file to Unity.</summary>
    public sealed class CharacterSetupTests
    {
        [TestCase("Armature|Idle", "Idle")]
        [TestCase("Armature|Armature|SpellShoot", "SpellShoot")]
        [TestCase("Jog", "Jog")]
        [TestCase("", "")]
        public void ClipNamesDropTheBlenderPrefix(string take, string expected)
        {
            Assert.AreEqual(expected, CharacterModelPostprocessor.CleanClipName(take));
        }

        [TestCase("Idle", true)]
        [TestCase("Walk", true)]
        [TestCase("Jog", true)]
        [TestCase("Sprint", true)]
        [TestCase("JumpLoop", true)]
        [TestCase("JumpStart", false)]
        [TestCase("JumpLand", false)]
        [TestCase("SpellShoot", false)]
        [TestCase("Spell_Simple_Idle_Loop", true)]
        [TestCase("Idle_Shield_Loop", true)]
        [TestCase("Push_Loop", true)]
        [TestCase("Sword_Idle", true)]
        [TestCase("Punch_Jab", false)]
        [TestCase("Sword_Attack", false)]
        [TestCase("Death01", false)]
        [TestCase("Loop", false)]
        [TestCase("", false)]
        public void OnlyContinuousMovementLoops(string clip, bool loops)
        {
            Assert.AreEqual(loops, CharacterModelPostprocessor.IsLooping(clip));
        }

        [TestCase("Assets/Threefold/Art/Characters/Mage/Mage.fbx", true)]
        [TestCase("Assets/Threefold/Art/Characters/Mage/Mage.FBX", true)]
        [TestCase("Assets/Threefold/Art/Props/Crate.fbx", false)]
        [TestCase("Assets/Threefold/Art/Characters/Mage/Notes.txt", false)]
        public void ImportRulesApplyOnlyToCharacterModels(string path, bool applies)
        {
            Assert.AreEqual(applies, CharacterModelPostprocessor.IsCharacterModel(path));
        }

        [TestCase("MAGE_Cloak", "Cloak")]
        [TestCase("MAGE_Crystal (Instance)", "Crystal")]
        [TestCase("Skin", "Skin")]
        public void ModelMaterialsMapToThePalette(string materialName, string key)
        {
            Assert.AreEqual(key, MageCharacterSetup.MaterialKey(materialName));
        }
    }
}
