namespace Threefold.Magic
{
    /// <summary>
    /// The starting timing of each discipline's body effects. The setup command writes these into the
    /// discipline assets, which are the truth afterwards; a discipline asset whose timing has not been
    /// written yet uses these directly. Proposals until reviewed in Unity and on a device.
    ///
    /// - Arcane, precise and flowing: a thin trail from the effect moment through the follow-through, a
    ///   ribbon around the body at the same moment, energy gathering in the casting hand while charging.
    /// - Flame, aggressive: fire on the fist from the start of the wind-up, growing to full strength at
    ///   contact, embers left behind after a strike that lands, heat along both forearms while charging.
    /// - Aegis, compressed force: energy gathers at the palm or forearm through the wind-up and is spent at
    ///   contact; a short streak only in the fast part of the strike; a distinct burst on the heavy slam;
    ///   while charging, energy pressed inwards in front of the chest.
    /// </summary>
    public static class MagicVfxDefaults
    {
        public static BodyEffectTiming For(MagicDiscipline discipline)
        {
            switch (discipline)
            {
                case MagicDiscipline.Flame:
                    return new BodyEffectTiming
                    {
                        configured = true,
                        trailFrom = BodyEffectMoment.Windup,
                        trailUntilWindowCloses = true,
                        trailStartIntensity = 0.35f,
                        swingFrom = BodyEffectMoment.HitWindow,
                        gatherBeforeStrike = false,
                        lingerAfterStrike = true,
                        heavyHitTrauma = 0.25f,
                        chargePlacement = ChargePlacement.BothForearms,
                        chargeStartIntensity = 0.25f,
                        chargeRadiusAtStart = 0.22f,
                        chargeRadiusAtFull = 0.1f,
                        drainSeconds = 0.25f,
                        cancelDrainSeconds = 0.08f,
                    };

                case MagicDiscipline.Aegis:
                    return new BodyEffectTiming
                    {
                        configured = true,
                        trailFrom = BodyEffectMoment.HitWindow,
                        trailUntilWindowCloses = false,
                        trailStartIntensity = 1f,
                        swingFrom = BodyEffectMoment.None,
                        gatherBeforeStrike = true,
                        lingerAfterStrike = false,
                        heavyHitTrauma = 0.25f,
                        chargePlacement = ChargePlacement.Chest,
                        chargeStartIntensity = 0.3f,
                        chargeRadiusAtStart = 0.6f,
                        chargeRadiusAtFull = 0.14f,
                        drainSeconds = 0.18f,
                        cancelDrainSeconds = 0.08f,
                    };

                default:
                    return new BodyEffectTiming
                    {
                        configured = true,
                        trailFrom = BodyEffectMoment.Effect,
                        trailUntilWindowCloses = true,
                        trailStartIntensity = 1f,
                        swingFrom = BodyEffectMoment.Effect,
                        gatherBeforeStrike = false,
                        lingerAfterStrike = false,
                        heavyHitTrauma = 0.25f,
                        chargePlacement = ChargePlacement.Effector,
                        chargeStartIntensity = 0.3f,
                        chargeRadiusAtStart = 0.4f,
                        chargeRadiusAtFull = 0.1f,
                        drainSeconds = 0.22f,
                        cancelDrainSeconds = 0.08f,
                    };
            }
        }

        /// <summary>The timing to use: the asset's own once configured, otherwise the defaults.</summary>
        public static BodyEffectTiming Resolve(BodyEffectTiming fromAsset, MagicDiscipline discipline)
        {
            return fromAsset != null && fromAsset.configured ? fromAsset : For(discipline);
        }
    }
}
