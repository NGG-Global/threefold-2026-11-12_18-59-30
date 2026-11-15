using System.Collections.Generic;

namespace Threefold.EditorTools
{
    /// <summary>Which mesh the Mage wears. Both sit on the same skeleton and play the same animation clips.</summary>
    internal enum MageLook
    {
        /// <summary>The mesh inside Mage.fbx: the library mannequin with the first, hooded outfit.</summary>
        Library,

        /// <summary>MageWayfarer.fbx: hair, a drawn face, the layered outfit, painted from one atlas.</summary>
        Wayfarer,
    }

    /// <summary>
    /// The decisions of the Mage's look that need no Unity object, so they can be tested outside Unity: which
    /// look the generated visual should have, and whether the Wayfarer model is the same Humanoid as Mage.fbx.
    /// </summary>
    internal static class MageLookRules
    {
        /// <summary>Raised when the visual prefab's layout changes. 2: the look is recorded (Docs/Characters/Mage.md).</summary>
        internal const string StampVersion = "threefold-mage-visual-2";

        private const string Automatic = "auto";
        private const string ByHand = "chosen";

        internal readonly struct Decision
        {
            internal Decision(MageLook look, bool build, bool chosenByHand)
            {
                Look = look;
                Build = build;
                ChosenByHand = chosenByHand;
            }

            /// <summary>The look the visual prefab should have.</summary>
            internal MageLook Look { get; }

            /// <summary>True if the prefab has to be built, or built again with another model.</summary>
            internal bool Build { get; }

            /// <summary>True if the owner picked the look with a menu command; the setups then leave it alone.</summary>
            internal bool ChosenByHand { get; }
        }

        /// <summary>The text stored with the visual prefab, saying which look it holds and who chose it.</summary>
        internal static string Stamp(MageLook look, bool chosenByHand)
        {
            return $"{StampVersion}|{look}|{(chosenByHand ? ByHand : Automatic)}";
        }

        /// <summary>
        /// Reads a stamp. A prefab made before looks existed has none: it holds the library look, chosen by nobody.
        /// </summary>
        internal static void ReadStamp(string stamp, out MageLook look, out bool chosenByHand)
        {
            look = MageLook.Library;
            chosenByHand = false;
            if (string.IsNullOrEmpty(stamp))
            {
                return;
            }

            string[] parts = stamp.Split('|');
            if (parts.Length != 3 || parts[0] != StampVersion || !System.Enum.TryParse(parts[1], out MageLook stored))
            {
                return;
            }

            look = stored;
            chosenByHand = parts[2] == ByHand;
        }

        /// <summary>
        /// What a setup run should do. Without a choice by hand, the Wayfarer look is used whenever its model is
        /// usable, and the library look otherwise. A look chosen by hand is kept, except that a Wayfarer model
        /// that has stopped being usable always gives way to the library look, which cannot fail.
        /// </summary>
        internal static Decision Decide(bool prefabExists, string stamp, bool wayfarerUsable)
        {
            MageLook preferred = wayfarerUsable ? MageLook.Wayfarer : MageLook.Library;
            if (!prefabExists)
            {
                return new Decision(preferred, true, false);
            }

            ReadStamp(stamp, out MageLook current, out bool chosenByHand);
            if (chosenByHand)
            {
                bool lost = current == MageLook.Wayfarer && !wayfarerUsable;
                return lost ? new Decision(MageLook.Library, true, false) : new Decision(current, false, true);
            }

            return new Decision(preferred, preferred != current, false);
        }

        /// <summary>
        /// Compares how two models were mapped to the Humanoid: pairs of (human bone, bone in the model). Returns
        /// null if the second maps every human bone to the bone of the same name as the first and maps nothing
        /// more, which is what lets it play the first model's clips unchanged; otherwise the first difference.
        /// </summary>
        internal static string CompareHumanMappings(IReadOnlyList<KeyValuePair<string, string>> library,
            IReadOnlyList<KeyValuePair<string, string>> variant)
        {
            if (library == null || library.Count == 0)
            {
                return "the Mage model's Humanoid mapping could not be read";
            }

            if (variant == null || variant.Count == 0)
            {
                return "its Humanoid mapping could not be read";
            }

            var expected = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> pair in library)
            {
                expected[pair.Key] = pair.Value;
            }

            var seen = new HashSet<string>();
            foreach (KeyValuePair<string, string> pair in variant)
            {
                seen.Add(pair.Key);
                if (!expected.TryGetValue(pair.Key, out string bone))
                {
                    return $"it maps '{pair.Key}' to '{pair.Value}', which the Mage model does not map at all";
                }

                if (bone != pair.Value)
                {
                    return $"it maps '{pair.Key}' to '{pair.Value}' instead of '{bone}'";
                }
            }

            foreach (KeyValuePair<string, string> pair in library)
            {
                if (!seen.Contains(pair.Key))
                {
                    return $"it does not map '{pair.Key}' ('{pair.Value}' in the Mage model)";
                }
            }

            return null;
        }
    }
}
