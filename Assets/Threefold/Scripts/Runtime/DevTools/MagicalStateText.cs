using System.Globalization;
using System.Text;
using Threefold.Combat;
using Threefold.WorldMagic;

namespace Threefold.DevTools
{
    /// <summary>
    /// Describes a magical object's properties and its present state in words and numbers, for the
    /// development diagnostics only. The player never sees numbers (Docs/Discovery.md); this is for
    /// whoever tests the level. It reads and never changes anything. Plain C#.
    /// </summary>
    public static class MagicalStateText
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>The properties an object has, such as "thermal; structural".</summary>
        public static string Properties(MagicalPropertySettings settings)
        {
            if (settings == null)
            {
                return "none";
            }

            var text = new StringBuilder();
            Append(text, settings.thermal, "thermal");
            Append(text, settings.structural, "structural");
            Append(text, settings.kinetic, "kinetic");
            Append(text, settings.arcane, "arcane-reactive");
            Append(text, settings.stability, "stability");
            return text.Length > 0 ? text.ToString() : "none";
        }

        /// <summary>
        /// Every property's state with its value, such as "heat 64/120 Heated; integrity 100/100 Intact".
        /// </summary>
        public static string State(MagicalPropertySet set)
        {
            if (set == null)
            {
                return "no properties";
            }

            var text = new StringBuilder();
            if (set.Thermal != null)
            {
                Separate(text).Append("heat ").Append(Number(set.Thermal.Heat)).Append('/').Append(Number(set.Thermal.Settings.capacity))
                    .Append(' ').Append(set.Thermal.State);
            }

            if (set.Structural != null)
            {
                Separate(text).Append("integrity ").Append(Number(set.Structural.Integrity)).Append('/')
                    .Append(Number(set.Structural.Settings.integrity)).Append(' ').Append(set.Structural.State);
            }

            if (set.Kinetic != null)
            {
                Separate(text).Append("travel ").Append(Number(set.Kinetic.Offset.magnitude)).Append(" m ").Append(set.Kinetic.State);
                if (set.IsKineticLocked)
                {
                    text.Append(" (locked)");
                }
            }

            if (set.Arcane != null)
            {
                Separate(text).Append("charge ").Append(Number(set.Arcane.Charge)).Append('/')
                    .Append(Number(set.Arcane.Settings.activationThreshold)).Append(' ').Append(set.Arcane.State);
            }

            if (set.Stability != null)
            {
                Separate(text).Append(set.Stability.State);
                if (set.Stability.Occupants > 0)
                {
                    text.Append(", ").Append(set.Stability.Occupants).Append(" on it");
                }
            }

            return text.Length > 0 ? text.ToString() : "no properties";
        }

        /// <summary>
        /// Only what is temporary and running at the moment: heat, fire, charge, a timed activation, a
        /// brace, stress, a glide, a collapse that will recover. Empty when nothing temporary runs.
        /// </summary>
        public static string Temporary(MagicalPropertySet set)
        {
            if (set == null || set.IsDestroyed)
            {
                return string.Empty;
            }

            var text = new StringBuilder();
            if (set.Thermal != null && set.Thermal.IsBurning)
            {
                Separate(text).Append("burning");
            }
            else if (set.Thermal != null && set.Thermal.Heat > 0f)
            {
                Separate(text).Append("heat ").Append(Number(set.Thermal.Heat));
                if (set.Thermal.State != ThermalState.Unheated)
                {
                    text.Append(" (").Append(set.Thermal.State).Append(')');
                }
            }

            if (set.Arcane != null && set.Arcane.State == ArcaneState.Activated &&
                set.Arcane.Settings.activation != ArcaneActivation.Latch)
            {
                Separate(text).Append("active (").Append(set.Arcane.Settings.activation).Append(')');
            }
            else if (set.Arcane != null && set.Arcane.Charge > 0f && set.Arcane.State != ArcaneState.Activated)
            {
                Separate(text).Append("charge ").Append(Number(set.Arcane.Charge));
            }

            if (set.Stability != null)
            {
                if (set.Stability.BraceRemaining > 0f)
                {
                    Separate(text).Append("braced ").Append(Number(set.Stability.BraceRemaining)).Append(" s");
                }

                if (set.Stability.Stress > 0f && set.Stability.State != StabilityState.Collapsed)
                {
                    Separate(text).Append("stress ").Append(Number(set.Stability.Stress)).Append('/')
                        .Append(Number(set.Stability.Settings.stressLimit));
                }

                if (set.Stability.IsRecovering)
                {
                    Separate(text).Append("down, stands again in ").Append(Number(set.Stability.RecoverRemaining)).Append(" s");
                }
            }

            if (set.Kinetic != null && set.Kinetic.State == KineticState.Moving)
            {
                Separate(text).Append("gliding");
            }

            return text.ToString();
        }

        private static string Number(float value)
        {
            return value.ToString(value >= 10f ? "0" : "0.0", Invariant);
        }

        private static void Append(StringBuilder text, bool has, string name)
        {
            if (has)
            {
                Separate(text).Append(name);
            }
        }

        private static StringBuilder Separate(StringBuilder text)
        {
            if (text.Length > 0)
            {
                text.Append("; ");
            }

            return text;
        }
    }
}
