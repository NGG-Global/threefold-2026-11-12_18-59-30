namespace Threefold.Combat
{
    /// <summary>
    /// Which layers of effect a landed melee strike shows on its target, and how the target answers the blow.
    /// Plain C#: no Unity calls. Presentation only: nothing here deals damage or changes a rule.
    /// </summary>
    public static class ImpactLayers
    {
        /// <summary>The heavy layer is shown on this many targets of one swing at most, so effects never hide the enemies.</summary>
        public const int MaxHeavyPerSwing = 1;

        /// <summary>True if this strike of the combo (0 is the first) shows the heavy impact layer as well.</summary>
        public static bool ShowsHeavyLayer(int stepIndex, int heavyFromStep)
        {
            return heavyFromStep >= 0 && stepIndex >= heavyFromStep;
        }

        /// <summary>
        /// How a struck body gives and comes back, from 0 at the blow to 1 when it is over: a number to squash
        /// the body by across and stretch it by upwards. It gives at once (the largest squash is a tenth of
        /// the way in), swings back past its own shape once, and is still again at the end. Between -1 and 1;
        /// positive is squashed.
        /// </summary>
        public static float Squash(float progress)
        {
            if (!(progress > 0f) || progress >= 1f)
            {
                return 0f;
            }

            // A damped swing: full give early, one overshoot the other way, then rest.
            float decay = (1f - progress) * (1f - progress);
            float swing = (float)System.Math.Cos(progress * System.Math.PI * 2.5 - System.Math.PI * 0.25);
            float attack = progress < 0.1f ? progress / 0.1f : 1f;
            return attack * decay * swing;
        }
    }
}
