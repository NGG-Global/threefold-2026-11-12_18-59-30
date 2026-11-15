namespace Threefold.Characters
{
    /// <summary>
    /// Something that says which aerial move the character is making, for the animation: 0 none, 1 ascension,
    /// 2 glide propulsion, 3 dash. Read every frame, so the Animator follows the gameplay state and can never be
    /// left in an aerial pose.
    /// </summary>
    public interface IAerialMoveSource
    {
        int AerialMove { get; }
    }
}
