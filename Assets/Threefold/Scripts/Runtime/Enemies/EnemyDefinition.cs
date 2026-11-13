using UnityEngine;

namespace Threefold.Enemies
{
    /// <summary>
    /// One kind of enemy, as an asset: its name and tuning. Every enemy of that kind in every
    /// scene reads the same asset, so a value changed here changes them all.
    /// </summary>
    [CreateAssetMenu(menuName = "Threefold/Enemies/Enemy Definition", fileName = "NewEnemy")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "Enemy";
        [SerializeField] private EnemyTuning tuning = new EnemyTuning();

        public string DisplayName => displayName;

        /// <summary>The tuning as authored. Enemies use a scaled copy; do not change it at runtime.</summary>
        public EnemyTuning Tuning => tuning;

        /// <summary>Sets the values. Called by the setup tooling.</summary>
        public void Configure(string enemyName, EnemyTuning enemyTuning)
        {
            displayName = enemyName;
            tuning = enemyTuning ?? new EnemyTuning();
        }
    }
}
