namespace Threefold.Combat
{
    /// <summary>Which side something fights on.</summary>
    public enum CombatTeam
    {
        /// <summary>Belongs to nobody: training targets, destructible objects. Anyone can hit it.</summary>
        Neutral,

        Player,

        Enemy,
    }

    public static class CombatTeams
    {
        /// <summary>
        /// True when both are on the same side. Allies do not hit each other with projectiles or
        /// strikes. Neutral is nobody's ally, not even of another neutral object.
        /// </summary>
        public static bool AreAllied(CombatTeam a, CombatTeam b)
        {
            return a != CombatTeam.Neutral && a == b;
        }
    }

    /// <summary>
    /// Something the level can put back into its starting state, such as a training dummy or an
    /// enemy. The session calls it when the level restarts.
    /// </summary>
    public interface IRestorable
    {
        void Restore();
    }
}
