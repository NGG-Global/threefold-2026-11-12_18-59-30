using UnityEngine;

namespace Threefold.Enemies
{
    /// <summary>A difficulty level as an asset. The scene's director uses one at a time.</summary>
    [CreateAssetMenu(menuName = "Threefold/Enemies/Difficulty", fileName = "NewDifficulty")]
    public sealed class EnemyDifficulty : ScriptableObject
    {
        [SerializeField] private EnemyDifficultySettings settings = new EnemyDifficultySettings();

        public EnemyDifficultySettings Settings => settings;

        /// <summary>Sets the values. Called by the setup tooling.</summary>
        public void Configure(EnemyDifficultySettings values)
        {
            settings = values ?? new EnemyDifficultySettings();
        }
    }
}
