using UnityEngine;

namespace Threefold.WorldMagic
{
    /// <summary>
    /// How one kind of object responds to magic: which properties it has (thermal, structural,
    /// kinetic, arcane-reactive, stability) and their values. Put a <see cref="MagicalObject"/> on
    /// an object, give it one of these, and it responds; no script is written per object. Several
    /// objects can share a definition.
    /// </summary>
    [CreateAssetMenu(menuName = "Threefold/Magical Object Definition", fileName = "NewMagicalObject")]
    public sealed class MagicalObjectDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "Magical Object";

        [TextArea(2, 4)]
        [Tooltip("What the object is meant to do, for whoever tunes it.")]
        [SerializeField] private string notes = string.Empty;

        [SerializeField] private MagicalPropertySettings properties = new MagicalPropertySettings();

        public string DisplayName => displayName;
        public string Notes => notes;
        public MagicalPropertySettings Properties => properties;

        /// <summary>Sets the values. Called by the setup tooling and by tests.</summary>
        public void Configure(string objectName, string description, MagicalPropertySettings settings)
        {
            displayName = objectName;
            notes = description ?? string.Empty;
            properties = settings ?? new MagicalPropertySettings();
        }
    }
}
